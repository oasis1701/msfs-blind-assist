using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public class TakeoffAssistManager : IDisposable
{
    private readonly ScreenReaderAnnouncer announcer;
    private readonly bool muteCenterlineAnnouncements;
    private readonly bool steerTowardTone;
    private readonly int headingToneThreshold;
    private readonly bool legacyMode;
    private readonly bool enableCallouts;

    /// <summary>
    /// When true, the centerline tone hard-pans to ±1 instead of using the
    /// proportional steerError / 5° curve (steer error = heading error plus
    /// the centerline-intercept crab). Speaker
    /// users get unambiguous "left or right" — no in-between values that
    /// could be confused with centred. Read on every position update so a
    /// runtime toggle in Hand Fly Options applies without restarting
    /// takeoff assist.
    /// </summary>
    private bool hardPanTone;
    private bool isActive = false;

    // Runway reference data (set by teleport or manual activation)
    private double? referenceThresholdLat;
    private double? referenceThresholdLon;
    private double? referenceRunwayHeadingTrue;      // For cross-track geometry
    private double? referenceRunwayHeadingMagnetic;  // For pan comparison and legacy mode
    private string? referenceRunwayID;
    private string? referenceAirportICAO;
    private bool hasRunwayReference = false;

    // Audio tone generator for centerline guidance (modern mode only)
    private AudioToneGenerator? centerlineTone;
    private readonly HandFlyWaveType toneWaveType;
    private readonly double toneVolume;

    // Tracking state - modern mode (centerline)
    private double lastAnnouncedCrossTrackFeet = 0;
    private DateTime lastCenterlineAnnouncement = DateTime.MinValue;

    // Tracking state - legacy mode (heading)
    private double lastAnnouncedHeadingDeviation = 0;
    private DateTime lastHeadingAnnouncement = DateTime.MinValue;

    // Tracking state - shared
    private double lastAnnouncedPitch = 0;
    private DateTime lastPitchAnnouncement = DateTime.MinValue;

    // Diagnostic frame trace for the takeoff roll (one append per ~100 ms while
    // active). Lets a post-flight reader see the actual swing and how the
    // cross-track intercept behaved, so the CROSSTRACK_INTERCEPT_* constants can
    // be tuned from data. (There is NO yaw-rate lead in takeoff assist — that is
    // taxi guidance's TaxiTurnLeadSeconds; do not add one from a log hunch.)
    private static readonly string TakeoffLogPath = Utils.AppLogs.PathFor("takeoff_assist.log");
    private const long MAX_TAKEOFF_LOG_BYTES = 2 * 1024 * 1024;
    private DateTime lastTakeoffLogTime = DateTime.MinValue;

    private double smoothedSteerError;
    private bool steerErrorSmootherInitialized;
    private int hardPanSide;        // -1 left / 0 centred / +1 right (hard-pan mode)
    private bool toneUnmuted;       // threshold-gate state (threshold >= 1 only)

    // Speed callout state (for takeoff roll)
    private bool hasAnnounced80Knots = false;
    private bool hasAnnounced100Knots = false;
    private bool hasAnnouncedV1 = false;
    private bool hasAnnouncedRotate = false;

    // Fenix V-speeds (set when takeoff assist activates, if Fenix loaded)
    private double? fenixV1Speed = null;
    private double? fenixVRSpeed = null;
    private bool isFenixAircraft = false;

    // Configuration constants - shared
    private const int ANNOUNCEMENT_INTERVAL_MS = 500; // 500ms between announcements
    private const double PITCH_THRESHOLD = 1.0; // Announce if pitch changes by >1 degree

    // Configuration constants - modern mode
    private const double CENTERLINE_TOLERANCE_FEET = 25.0; // Within ±25 feet is "center"
    private const double CENTERLINE_CHANGE_THRESHOLD_FEET = 10.0; // Announce if deviation changes by >10 feet
    private const double PAN_FULL_RANGE_DEGREES = 5.0; // ±5° tracking error for full left/right pan

    // Smoothing/hysteresis for the DISCRETE audio switches (mute gate + hard-pan
    // side). The proportional pan stays on the RAW steer error (continuous — can't
    // flap); the on/off and left/right decisions use a lightly smoothed error with
    // hysteresis so GPS cross-track jitter (0.1°/ft couples ~1 ft of noise into
    // 0.1° of steer error) and the zero-crossings of normal convergence can't
    // chatter the tone. Same lesson as TaxiSteeringTone's 3°/6° + min-sustain.
    private const double STEER_SMOOTH_ALPHA = 0.3;          // EMA at ~30 Hz ≈ 100 ms
    private const double HARDPAN_SIDE_ON_DEG = 0.5;         // enter a side
    private const double HARDPAN_SIDE_OFF_DEG = 0.25;       // drop to centred
    private const double MUTE_HYSTERESIS_DEG = 0.5;         // re-mute at threshold − this
    private const double MUTE_OFF_FLOOR_DEG = 0.25;         // never re-mute below this gap

    // Cross-track centerline tracking. The pan tone tracks an INTERCEPT
    // heading that converges on the centerline, not the bare runway heading.
    // Same IDEA as the taxi-guidance runway-lineup intercept (deadband → ramp →
    // cap on cross-track), but a DELIBERATELY different curve: linear 0.1°/ft
    // capped at 10°, vs taxi's sqrt ramp to 30° at 100 ft — a 30° crab command
    // during a high-speed roll would be dangerous; gentle linear is right here.
    // The 8 ft deadband intentionally matches taxi's LINEUP_NOISE_DEADBAND_FEET.
    // A pure heading-only tone (the original design) left pilots unable to hold
    // centerline: a small steady heading error silently integrates into a
    // huge sideways drift (measured 800 ft off at EIDW 28R while the nose
    // never strayed >12° from runway heading), and the tone gave no cue of it.
    // Here the "desired" heading is biased back toward the centerline by an
    // intercept angle proportional to cross-track, so nulling the tone means
    // converging on centerline rather than merely pointing straight.
    private const double CROSSTRACK_INTERCEPT_DEADBAND_FEET = 8.0;   // on centerline → hold heading, no crab
    private const double CROSSTRACK_INTERCEPT_DEG_PER_FOOT = 0.1;    // crab angle commanded per foot off
    private const double CROSSTRACK_MAX_INTERCEPT_DEG = 10.0;        // cap the commanded crab (gentle on the roll)

    // Configuration constants - legacy mode
    private const double HEADING_TOLERANCE_DEGREES = 1.0; // Within ±1 degree is "center"
    private const double HEADING_CHANGE_THRESHOLD = 1.0; // Announce if heading deviation changes by >1 degree

    public bool IsActive => isActive;
    public bool HasRunwayReference => hasRunwayReference;

    public event EventHandler<bool>? TakeoffAssistActiveChanged;

    public TakeoffAssistManager(ScreenReaderAnnouncer screenReaderAnnouncer,
        HandFlyWaveType waveType = HandFlyWaveType.Sine, double volume = 0.05,
        bool muteCenterline = false, bool steerTowardTone = true,
        int useHeadingToneThreshold = 0, bool useLegacyMode = false,
        bool useEnableCallouts = true)
    {
        announcer = screenReaderAnnouncer;
        toneWaveType = waveType;
        toneVolume = volume;
        muteCenterlineAnnouncements = muteCenterline;
        enableCallouts = useEnableCallouts;
        this.steerTowardTone = steerTowardTone;
        headingToneThreshold = useHeadingToneThreshold;
        legacyMode = useLegacyMode;

        // Only create tone generator if not in legacy mode
        if (!legacyMode)
        {
            centerlineTone = new AudioToneGenerator();
        }
    }

    /// <summary>
    /// Sets the runway reference from teleport operation.
    /// This allows centerline tracking using actual runway geometry.
    /// </summary>
    /// <param name="thresholdLat">Runway threshold latitude</param>
    /// <param name="thresholdLon">Runway threshold longitude</param>
    /// <param name="runwayHeadingTrue">Runway true heading (for cross-track geometry)</param>
    /// <param name="runwayHeadingMagnetic">Runway magnetic heading (for pan comparison)</param>
    /// <param name="runwayID">Runway identifier (e.g., "04L")</param>
    /// <param name="airportICAO">Airport ICAO code (e.g., "KEWR")</param>
    public void SetRunwayReference(double thresholdLat, double thresholdLon,
        double runwayHeadingTrue, double runwayHeadingMagnetic,
        string runwayID, string airportICAO)
    {
        referenceThresholdLat = thresholdLat;
        referenceThresholdLon = thresholdLon;
        referenceRunwayHeadingTrue = runwayHeadingTrue;
        referenceRunwayHeadingMagnetic = runwayHeadingMagnetic;
        referenceRunwayID = runwayID;
        referenceAirportICAO = airportICAO;
        hasRunwayReference = true;

        System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Runway reference set: {runwayID} at {airportICAO}, Lat={thresholdLat:F6}, Lon={thresholdLon:F6}, HdgTrue={runwayHeadingTrue:F1}, HdgMag={runwayHeadingMagnetic:F1}");
    }

    /// <summary>
    /// Snapshot of the current teleport/taxi-lineup runway reference, so MainForm
    /// can carry it across the settings-dialog manager recreate (Reset() clears it;
    /// without this, teleport → Hand Fly Options OK → Ctrl+T lost the runway and
    /// silently fell back to "no runway selected").
    /// </summary>
    public bool TryGetRunwayReference(out double thresholdLat, out double thresholdLon,
        out double headingTrue, out double headingMagnetic,
        out string runwayId, out string airportIcao)
    {
        if (hasRunwayReference &&
            referenceThresholdLat.HasValue && referenceThresholdLon.HasValue &&
            referenceRunwayHeadingTrue.HasValue && referenceRunwayHeadingMagnetic.HasValue &&
            referenceRunwayID != null && referenceAirportICAO != null)
        {
            thresholdLat = referenceThresholdLat.Value;
            thresholdLon = referenceThresholdLon.Value;
            headingTrue = referenceRunwayHeadingTrue.Value;
            headingMagnetic = referenceRunwayHeadingMagnetic.Value;
            runwayId = referenceRunwayID;
            airportIcao = referenceAirportICAO;
            return true;
        }
        thresholdLat = thresholdLon = headingTrue = headingMagnetic = 0;
        runwayId = airportIcao = string.Empty;
        return false;
    }

    /// <summary>
    /// Clears the runway reference (e.g., when aircraft changes or disconnects)
    /// </summary>
    public void ClearRunwayReference()
    {
        referenceThresholdLat = null;
        referenceThresholdLon = null;
        referenceRunwayHeadingTrue = null;
        referenceRunwayHeadingMagnetic = null;
        referenceRunwayID = null;
        referenceAirportICAO = null;
        hasRunwayReference = false;

        System.Diagnostics.Debug.WriteLine("[TakeoffAssistManager] Runway reference cleared");
    }

    /// <summary>
    /// Toggles takeoff assist mode on/off.
    /// If no runway reference exists, uses current position/heading as reference.
    /// </summary>
    /// <param name="currentLat">Current aircraft latitude</param>
    /// <param name="currentLon">Current aircraft longitude</param>
    /// <param name="currentHeadingMagnetic">Current magnetic heading in degrees</param>
    /// <param name="magVar">Magnetic variation in degrees (East positive, West negative)</param>
    public void Toggle(double currentLat, double currentLon, double currentHeadingMagnetic, double magVar)
    {
        isActive = !isActive;

        if (isActive)
        {
            // Reset tracking state based on mode
            lastAnnouncedPitch = 0;
            lastPitchAnnouncement = DateTime.MinValue;

            // Reset speed callout state
            hasAnnounced80Knots = false;
            hasAnnounced100Knots = false;
            hasAnnouncedV1 = false;
            hasAnnouncedRotate = false;

            if (legacyMode)
            {
                // Legacy mode: reset heading tracking state
                lastAnnouncedHeadingDeviation = 0;
                lastHeadingAnnouncement = DateTime.MinValue;
            }
            else
            {
                // Modern mode: reset centerline tracking state and start tone
                lastAnnouncedCrossTrackFeet = 0;
                lastCenterlineAnnouncement = DateTime.MinValue;
                centerlineTone?.Start(toneWaveType, toneVolume, 600.0);

                smoothedSteerError = 0;
                steerErrorSmootherInitialized = false;
                hardPanSide = 0;
                toneUnmuted = false;
            }

            if (hasRunwayReference)
            {
                // Use runway reference from teleport
                string modeInfo = legacyMode ? "legacy mode" : "";
                announcer.AnnounceImmediate($"Takeoff assist active{(legacyMode ? " legacy mode" : "")}, runway {referenceRunwayID} at {referenceAirportICAO}");
                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Activated with runway reference (legacy={legacyMode}): {referenceRunwayID} at {referenceAirportICAO}, HdgMag={referenceRunwayHeadingMagnetic:F1}");

                // EXPLICIT heading sanity check at activation. The intercept tone
                // now DOES cue an off-heading pilot (nonzero steerError), but the
                // tone is pan-only — this spoken pre-roll heading-vs-runway
                // cross-check (FAA AIM standard practice; sighted pilots do it
                // visually) gives the blind pilot explicit NUMBERS before the
                // roll starts. Threshold ≈ 3° matches the taxi-lineup tolerance.
                if (referenceRunwayHeadingMagnetic.HasValue)
                {
                    double headingDiff = currentHeadingMagnetic - referenceRunwayHeadingMagnetic.Value;
                    while (headingDiff > 180)  headingDiff -= 360;
                    while (headingDiff < -180) headingDiff += 360;

                    const double ALIGNED_TOLERANCE_DEG = 3.0;
                    if (Math.Abs(headingDiff) > ALIGNED_TOLERANCE_DEG)
                    {
                        int rwyHdg = (int)Math.Round(referenceRunwayHeadingMagnetic.Value);
                        int curHdg = (int)Math.Round(currentHeadingMagnetic);
                        string turn = headingDiff > 0 ? "left" : "right";
                        announcer.AnnounceImmediate(
                            $"Warning: heading {curHdg}, runway heading {rwyHdg}. " +
                            $"Turn {turn} to align before rolling.");
                    }
                }
            }
            else
            {
                // Use current position as reference
                // Magnetic heading is captured directly, true heading computed from magvar
                referenceThresholdLat = currentLat;
                referenceThresholdLon = currentLon;
                referenceRunwayHeadingMagnetic = currentHeadingMagnetic;
                referenceRunwayHeadingTrue = currentHeadingMagnetic + magVar;  // True = Magnetic + MagVar

                if (legacyMode)
                {
                    announcer.AnnounceImmediate($"Takeoff assist active legacy mode, reference heading {Math.Round(currentHeadingMagnetic)}");
                }
                else
                {
                    announcer.AnnounceImmediate($"Takeoff assist active, no runway selected, extending centerline from current position, heading {Math.Round(currentHeadingMagnetic)}");
                }
                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Activated with current position reference (legacy={legacyMode}): HdgMag={currentHeadingMagnetic:F1}");
            }

            // Header AFTER the reference branch so a synthetic-centerline activation
            // logs the just-captured heading instead of "icao= rwy= rwyHdgMag=-1".
            if (!legacyMode) BeginTakeoffLog();
        }
        else
        {
            // Stop centerline guidance tone (only exists in modern mode)
            centerlineTone?.Stop();

            // Always clear the runway reference on deactivation. Across-session
            // preservation is unnecessary (process restart resets everything);
            // within-session preservation is UNSAFE because the next CTRL+T may
            // be for a different runway after a turnaround flight. Previously
            // the teleport-set reference was kept "for next time", but that
            // caused flight 2 of a turnaround to silently reuse flight 1's
            // runway threshold and heading — the MainForm seeding guard
            // (`!takeoffAssistManager.HasRunwayReference`) rejected the fresh
            // taxi-lineup reference because the stale one was still present.
            ClearRunwayReference();

            announcer.AnnounceImmediate("Takeoff assist off");
            System.Diagnostics.Debug.WriteLine("[TakeoffAssistManager] Deactivated");
        }

        TakeoffAssistActiveChanged?.Invoke(this, isActive);
    }

    /// <summary>
    /// Process position update during takeoff assist.
    /// In modern mode: tracks centerline deviation in feet with audio tone.
    /// In legacy mode: tracks heading deviation in degrees without tone.
    /// </summary>
    /// <param name="currentLat">Current aircraft latitude</param>
    /// <param name="currentLon">Current aircraft longitude</param>
    /// <param name="currentHeadingMagnetic">Current aircraft magnetic heading in degrees</param>
    public void ProcessPositionUpdate(double currentLat, double currentLon, double currentHeadingMagnetic)
    {
        if (!isActive) return;
        if (!referenceRunwayHeadingMagnetic.HasValue) return;

        // Calculate heading deviation using MAGNETIC headings (used in both modes)
        // Normalized to -180 to +180
        double headingDiff = currentHeadingMagnetic - referenceRunwayHeadingMagnetic.Value;
        while (headingDiff > 180.0) headingDiff -= 360.0;
        while (headingDiff < -180.0) headingDiff += 360.0;

        if (legacyMode)
        {
            // Legacy mode: announce heading deviation in degrees
            double deviationChange = Math.Abs(headingDiff - lastAnnouncedHeadingDeviation);
            TimeSpan timeSinceLastAnnouncement = DateTime.Now - lastHeadingAnnouncement;

            if (deviationChange >= HEADING_CHANGE_THRESHOLD &&
                timeSinceLastAnnouncement.TotalMilliseconds >= ANNOUNCEMENT_INTERVAL_MS)
            {
                string announcement = FormatHeadingDeviationAnnouncement(headingDiff);
                announcer.AnnounceImmediate(announcement);

                lastAnnouncedHeadingDeviation = headingDiff;
                lastHeadingAnnouncement = DateTime.Now;

                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Legacy mode: Heading={currentHeadingMagnetic:F1}°, Deviation={headingDiff:F1}° → {announcement}");
            }
        }
        else
        {
            // Modern mode: track centerline position and use audio tone
            if (!referenceThresholdLat.HasValue || !referenceThresholdLon.HasValue ||
                !referenceRunwayHeadingTrue.HasValue) return;

            // Unified centerline math — see RunwayCenterlineTracker sign convention.
            // We already have MAGNETIC heading here; pass TRUE via (mag + variation) isn't available so
            // use the mag heading as an approximation for the heading-error field (we don't use that
            // field here — pan is computed from steerError (magnetic headingDiff + crab) below.
            var track = RunwayCenterlineTracker.Compute(
                currentLat, currentLon,
                currentHeadingMagnetic, // heading error not used by this caller
                referenceThresholdLat.Value, referenceThresholdLon.Value,
                referenceRunwayHeadingTrue.Value);
            double crossTrackFeet = track.CrossTrackFeet; // + = left, - = right

            // Refresh hard-pan setting each frame so a Hand Fly Options
            // toggle takes effect immediately without recreating the
            // manager. Cheap — single setting lookup.
            hardPanTone = SettingsManager.Current.TakeoffAssistHardPanTone;

            // --- Cross-track intercept ----------------------------------------
            // Bias the "ideal" heading back toward the centerline by a crab angle
            // proportional to how far off-centerline we are (sign convention:
            // crossTrackFeet + = left of centerline → command a right crab; - =
            // right → left crab). The tone is silent on the heading that CONVERGES
            // on centerline, not merely on runway heading — so a pilot pointed
            // straight but drifting off-CL still gets a steer cue. Within the
            // deadband we command no crab so a dead-on-centerline roll stays steady.
            // Crab command: 0 inside the deadband (the negative pre-clamp value
            // falls out of the 0 lower bound), then linear per foot, capped.
            double desiredCrabDeg = Math.Clamp(
                (Math.Abs(crossTrackFeet) - CROSSTRACK_INTERCEPT_DEADBAND_FEET)
                    * CROSSTRACK_INTERCEPT_DEG_PER_FOOT,
                0.0, CROSSTRACK_MAX_INTERCEPT_DEG) * Math.Sign(crossTrackFeet);

            // steerError > 0 = steer RIGHT to reach the intercept heading; < 0 =
            // steer LEFT. Matches the taxi steering-tone convention: the tone pans
            // in the direction you should TURN and you steer TOWARD it to centre
            // (steerTowardTone=false flips to steer-away for users trained on the
            // pre-#111 heading-deviation tone).
            double steerError = desiredCrabDeg - headingDiff;
            // headingDiff is normalized to ±180 but adding the crab can push the sum
            // past it (e.g. -175° heading error + 10° crab = 185°); re-wrap so a
            // near-reciprocal activation still pans toward the SHORTER turn.
            while (steerError > 180.0) steerError -= 360.0;
            while (steerError < -180.0) steerError += 360.0;
            // ------------------------------------------------------------------

            smoothedSteerError = steerErrorSmootherInitialized
                ? smoothedSteerError + STEER_SMOOTH_ALPHA * (steerError - smoothedSteerError)
                : steerError;
            steerErrorSmootherInitialized = true;

            // Audio pan in the steer direction — silent/centred tone = on the
            // heading that converges on centerline. Positive steerError = steer
            // right → pan RIGHT (unless steer-away is configured).
            float pan;
            if (hardPanTone)
            {
                // Hard-pan holds its side with hysteresis: enter a side at ON,
                // drop to centred below OFF, hold in between — so the ±1 slam
                // can't alternate speakers on jitter or normal convergence
                // zero-crossings (worst with the legacy threshold=0 "Always").
                if (Math.Abs(smoothedSteerError) >= HARDPAN_SIDE_ON_DEG)
                    hardPanSide = Math.Sign(smoothedSteerError);
                else if (Math.Abs(smoothedSteerError) < HARDPAN_SIDE_OFF_DEG)
                    hardPanSide = 0;
                pan = hardPanSide;
            }
            else
            {
                pan = (float)Math.Clamp(steerError / PAN_FULL_RANGE_DEGREES, -1.0, 1.0);
            }
            if (!steerTowardTone) pan = -pan;
            centerlineTone?.SetPan(pan);

            // Threshold mute with hysteresis: unmute at ≥ threshold, re-mute only
            // once the smoothed error falls half a degree back inside (floored so
            // the band never collapses). Being off-centerline un-mutes via the
            // crab term even with the nose pointed straight down the runway.
            if (headingToneThreshold > 0)
            {
                double offThresh = Math.Max(MUTE_OFF_FLOOR_DEG, headingToneThreshold - MUTE_HYSTERESIS_DEG);
                if (!toneUnmuted && Math.Abs(smoothedSteerError) >= headingToneThreshold)
                    toneUnmuted = true;
                else if (toneUnmuted && Math.Abs(smoothedSteerError) <= offThresh)
                    toneUnmuted = false;
                centerlineTone?.UpdateVolume(toneUnmuted ? toneVolume : 0);
            }
            else
            {
                // "Always" mode: re-assert volume every sounding frame (the
                // TaxiSteeringTone lesson — never rely on caller-side volume state).
                centerlineTone?.UpdateVolume(toneVolume);
            }

            AppendTakeoffLog(currentLat, currentLon, currentHeadingMagnetic,
                headingDiff, desiredCrabDeg, steerError, pan, crossTrackFeet);

            // Check if we should announce centerline deviation
            double deviationChange = Math.Abs(crossTrackFeet - lastAnnouncedCrossTrackFeet);
            TimeSpan timeSinceLastAnnouncement = DateTime.Now - lastCenterlineAnnouncement;

            if (deviationChange >= CENTERLINE_CHANGE_THRESHOLD_FEET &&
                timeSinceLastAnnouncement.TotalMilliseconds >= ANNOUNCEMENT_INTERVAL_MS)
            {
                string announcement = FormatCenterlineAnnouncement(crossTrackFeet);

                // Only announce if not muted
                if (!muteCenterlineAnnouncements)
                {
                    announcer.AnnounceImmediate(announcement);
                }

                lastAnnouncedCrossTrackFeet = crossTrackFeet;
                lastCenterlineAnnouncement = DateTime.Now;

                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Position: Lat={currentLat:F6}, Lon={currentLon:F6}, CrossTrack={crossTrackFeet:F1}ft → {announcement}{(muteCenterlineAnnouncements ? " (muted)" : "")}");
            }
        }
    }

    /// <summary>
    /// Process pitch update during takeoff assist
    /// </summary>
    /// <param name="currentPitch">Current pitch in degrees</param>
    public void ProcessPitchUpdate(double currentPitch)
    {
        if (!isActive) return;

        double pitchChange = Math.Abs(currentPitch - lastAnnouncedPitch);
        TimeSpan timeSinceLastAnnouncement = DateTime.Now - lastPitchAnnouncement;

        // Check if we should announce
        if (pitchChange >= PITCH_THRESHOLD &&
            timeSinceLastAnnouncement.TotalMilliseconds >= ANNOUNCEMENT_INTERVAL_MS)
        {
            string announcement = FormatPitchAnnouncement(currentPitch);
            announcer.AnnounceImmediate(announcement);

            lastAnnouncedPitch = currentPitch;
            lastPitchAnnouncement = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Pitch: {currentPitch:F1}° → {announcement}");
        }
    }

    /// <summary>
    /// Process IAS update during takeoff assist for speed callouts.
    /// Announces "80 knots", "100 knots", "V1" (Fenix only), and "rotate" (Fenix only).
    /// </summary>
    /// <param name="currentIAS">Current indicated airspeed in knots</param>
    public void ProcessSpeedUpdate(double currentIAS)
    {
        if (!isActive) return;
        if (!enableCallouts) return;

        // 80 knots callout (all aircraft)
        // Use Announce() (queued, non-interrupting) so callouts don't cut each other off
        if (!hasAnnounced80Knots && currentIAS >= 80.0)
        {
            announcer.Announce("80 knots");
            hasAnnounced80Knots = true;
            System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Speed callout: 80 knots (IAS={currentIAS:F1})");
        }

        // 100 knots callout (all aircraft)
        if (!hasAnnounced100Knots && currentIAS >= 100.0)
        {
            announcer.Announce("100 knots");
            hasAnnounced100Knots = true;
            System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Speed callout: 100 knots (IAS={currentIAS:F1})");
        }

        // V1 callout (Fenix only, if V1 speed is configured)
        if (isFenixAircraft && fenixV1Speed.HasValue && fenixV1Speed.Value > 0)
        {
            if (!hasAnnouncedV1 && currentIAS >= fenixV1Speed.Value)
            {
                announcer.Announce("V1");
                hasAnnouncedV1 = true;
                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Speed callout: V1 at {fenixV1Speed.Value} kt (IAS={currentIAS:F1})");
            }
        }

        // Rotate callout (Fenix only, if VR speed is configured)
        if (isFenixAircraft && fenixVRSpeed.HasValue && fenixVRSpeed.Value > 0)
        {
            if (!hasAnnouncedRotate && currentIAS >= fenixVRSpeed.Value)
            {
                announcer.Announce("rotate");
                hasAnnouncedRotate = true;
                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Speed callout: Rotate at {fenixVRSpeed.Value} kt (IAS={currentIAS:F1})");
            }
        }
    }

    /// <summary>
    /// Sets the Fenix V-speeds for callouts. Call when takeoff assist activates on Fenix.
    /// </summary>
    /// <param name="v1">V1 speed in knots (0 or null = not configured)</param>
    /// <param name="vr">VR speed in knots (0 or null = not configured)</param>
    public void SetFenixVSpeeds(double? v1, double? vr)
    {
        fenixV1Speed = (v1.HasValue && v1.Value > 0) ? v1 : null;
        fenixVRSpeed = (vr.HasValue && vr.Value > 0) ? vr : null;
        isFenixAircraft = true;

        System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Fenix V-speeds set: V1={fenixV1Speed?.ToString() ?? "N/A"}, VR={fenixVRSpeed?.ToString() ?? "N/A"}");
    }

    /// <summary>
    /// Clears Fenix V-speeds (call when switching away from Fenix or deactivating)
    /// </summary>
    public void ClearFenixVSpeeds()
    {
        fenixV1Speed = null;
        fenixVRSpeed = null;
        isFenixAircraft = false;
    }

    /// <summary>
    /// Formats centerline deviation announcement as increments (each increment = 10 feet)
    /// Used in modern mode.
    /// </summary>
    private string FormatCenterlineAnnouncement(double crossTrackFeet)
    {
        // Check if on centerline
        if (Math.Abs(crossTrackFeet) <= CENTERLINE_TOLERANCE_FEET)
        {
            return "center";
        }

        // Determine direction and magnitude (each increment = 10 feet)
        int increment = (int)Math.Round(Math.Abs(crossTrackFeet) / 10.0);
        string direction = crossTrackFeet > 0 ? "left" : "right";

        return $"{increment} {direction}";
    }

    /// <summary>
    /// Formats heading deviation announcement as degrees.
    /// Used in legacy mode. Format: "1 right", "2 left", "center"
    /// </summary>
    private string FormatHeadingDeviationAnnouncement(double headingDeviation)
    {
        // Check if on centerline (within tolerance)
        if (Math.Abs(headingDeviation) <= HEADING_TOLERANCE_DEGREES)
        {
            return "center";
        }

        // Determine direction and magnitude
        // Positive deviation = aircraft heading right of runway heading
        int deviationDegrees = (int)Math.Round(Math.Abs(headingDeviation));
        string direction = headingDeviation > 0 ? "right" : "left";

        return $"{deviationDegrees} {direction}";
    }

    /// <summary>
    /// Formats pitch announcement
    /// </summary>
    private string FormatPitchAnnouncement(double pitch)
    {
        int pitchDegrees = (int)Math.Round(pitch);

        if (pitchDegrees > 0)
        {
            return $"+{pitchDegrees}";
        }
        else if (pitchDegrees < 0)
        {
            return $"{pitchDegrees}";
        }
        else
        {
            return "level";
        }
    }

    /// <summary>
    /// Writes a session-start header + CSV column row to the takeoff trace.
    /// Appends (like the taxi log) so history survives across rolls; truncates
    /// once past MAX_TAKEOFF_LOG_BYTES so it can't grow without bound.
    /// </summary>
    private void BeginTakeoffLog()
    {
        lastTakeoffLogTime = DateTime.MinValue;
        try
        {
            if (System.IO.File.Exists(TakeoffLogPath) &&
                new System.IO.FileInfo(TakeoffLogPath).Length > MAX_TAKEOFF_LOG_BYTES)
            {
                System.IO.File.WriteAllText(TakeoffLogPath, string.Empty);
            }
            int rwyHdg = referenceRunwayHeadingMagnetic.HasValue
                ? (int)Math.Round(referenceRunwayHeadingMagnetic.Value) : -1;
            System.IO.File.AppendAllText(TakeoffLogPath, string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "=== Takeoff {0:yyyy-MM-dd HH:mm:ss} icao={1} rwy={2} rwyHdgMag={3} ===",
                DateTime.Now, referenceAirportICAO, referenceRunwayID, rwyHdg)
                + Environment.NewLine
                + "time,lat,lon,hdgMag,headingDiff,desiredCrab,steerErr,pan,crossTrackFt"
                + Environment.NewLine);
        }
        catch { /* diagnostic only */ }
    }

    /// <summary>
    /// One frame of the takeoff trace, throttled to ~100 ms.
    /// </summary>
    private void AppendTakeoffLog(double lat, double lon, double hdgMag,
        double headingDiff, double desiredCrabDeg, double steerError, float pan, double crossTrackFeet)
    {
        // UtcNow for the rate limit (monotonic across DST/clock changes; the
        // taxi-guidance trace convention). Local time only in the display column.
        var nowUtc = DateTime.UtcNow;
        if ((nowUtc - lastTakeoffLogTime).TotalMilliseconds < 100) return;
        lastTakeoffLogTime = nowUtc;
        try
        {
            System.IO.File.AppendAllText(TakeoffLogPath, string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0:HH:mm:ss.fff},{1:F7},{2:F7},{3:F1},{4:F2},{5:F2},{6:F2},{7:F2},{8:F1}",
                DateTime.Now, lat, lon, hdgMag, headingDiff, desiredCrabDeg,
                steerError, pan, crossTrackFeet) + Environment.NewLine);
        }
        catch { /* diagnostic only */ }
    }

    /// <summary>
    /// Resets the takeoff assist manager state
    /// </summary>
    public void Reset()
    {
        if (isActive)
        {
            centerlineTone?.Stop();
            isActive = false;
            TakeoffAssistActiveChanged?.Invoke(this, false);
            System.Diagnostics.Debug.WriteLine("[TakeoffAssistManager] Reset");
        }

        smoothedSteerError = 0;
        steerErrorSmootherInitialized = false;
        hardPanSide = 0;
        toneUnmuted = false;

        // Clear all reference data on reset
        ClearRunwayReference();
    }

    /// <summary>
    /// Disposes audio resources
    /// </summary>
    public void Dispose()
    {
        centerlineTone?.Stop();
        centerlineTone?.Dispose();
        centerlineTone = null;
        GC.SuppressFinalize(this);
    }
}

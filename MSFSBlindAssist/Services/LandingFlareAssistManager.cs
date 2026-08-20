using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Manual-landing flare + rollout assist ("manual landing" checkbox in the
/// destination-runway dialog, unchecked by default).
///
/// Armed at destination-runway selection time; sleeps until the aircraft is on
/// approach (below the altitude gate — MainForm feeds 1 Hz INDICATED_ALTITUDE
/// samples), then runs off a dedicated SIM_FRAME data feed:
///
///  • FLARE (50 ft gear height → touchdown): TWO tones, one per axis.
///    VERTICAL is frequency-coded off a sink-rate flare law (targetVS =
///    −(gearAGL/τ·60 + bias), the classic exponential flare an autoland flies):
///    high tone = sinking too fast, pull up; low tone = floating, release back
///    pressure; SILENCE = on profile. Sink-rate (not pitch) is the reference
///    because it is what the flare controls and is airframe-independent.
///    LATERAL is pan-coded off the centerline PD law (position + cross-track
///    RATE): pan is the side to steer toward, so the pilot rudders into the tone
///    through the flare and touches down tracking the centerline. The two tones
///    carry CONTRASTING waveforms so they stay separable while both sound.
///  • ROLLOUT (touchdown → taxi speed): the vertical tone stops — there is no
///    pitch task left — and the lateral tone continues, switching to the
///    takeoff-assist intercept-crab math, converging on the centerline. The
///    timbre thins at touchdown, which marks the boundary audibly.
///  • HANDOFF: tone stops below taxi-ish speed (earlier when landing-exit
///    guidance is running, so its exit-steering tone never overlaps ours) or
///    when the pilot deliberately turns off the runway; landing-exit guidance /
///    taxi guidance then own the arrival exactly as today.
///
/// There is deliberately NO spoken approach phase. Rate-limited intercept
/// headings from 1000 ft were tried and rejected by the pilot as too much
/// talking on short final; the flare tones are the whole airborne instrument.
///
/// Go-around (climb back above 100 ft gear height) silences the tones with a
/// spoken cue — silence must never be mistakable for "on profile" — and re-arms
/// for the next approach. A bounce (airborne again below that height) drops
/// back into flare mode so the tones keep guiding the second touchdown.
/// The manager stays armed after a completed rollout, so circuit training gets
/// flare guidance on every touch-and-go without re-opening the dialog.
///
/// AGL note: SimConnect's PLANE ALT ABOVE GROUND is the aircraft DATUM height,
/// not gear height — on a 777 the datum sits ~40 ft above the wheels in flare
/// attitude. All height gates here subtract the per-aircraft
/// VisualGuidanceProfile.FlareAltitudeBiasFt (read via delegate so an aircraft
/// switch after arming is honored), matching visual guidance's flare math.
/// </summary>
public class LandingFlareAssistManager : IDisposable
{
    private enum Phase { Armed, Flare, Rollout }

    private readonly ScreenReaderAnnouncer announcer;
    // Per-aircraft datum→gear height correction (VisualGuidanceProfile.FlareAltitudeBiasFt).
    private readonly Func<double> getFlareAglBiasFt;
    // True while visual guidance's dual tones are running — if so, VG owns the approach
    // audio and the flare phase stays silent (VG has its own flare cue); the rollout
    // pan tone still runs because VG auto-deactivates at touchdown.
    private readonly Func<bool> isVisualGuidanceActive;
    // True while the landing-exit planner's rollout guidance is engaged — raises the
    // rollout handoff speed so our pan tone is gone before the exit-steering tone starts.
    private readonly Func<bool> isLandingExitGuidanceActive;
    // True once taxi guidance has HANDED OFF to the exit route and its own steering tone
    // is panning. That handoff (turnBegun at up to 90 kt, exitedLaterally with no speed
    // cap at all) can fire well above the raised threshold above, so speed alone cannot
    // keep the two tones apart — and they steer opposite ways, ours back to the runway
    // centreline while the taxi tone leads onto the exit.
    private readonly Func<bool> isLandingExitTaxiSteering;

    // LATERAL / pan tone — the user's chosen waveform. Runs from flare engage all the way to
    // the landing-exit handoff; only the law feeding it changes at touchdown.
    private readonly AudioToneGenerator tone = new();
    // VERTICAL sink-rate tone — flare only, stopped at touchdown. Takes a waveform that
    // CONTRASTS with the lateral one: the two sound together through the flare, and identical
    // waveforms at a matched state blur (visual guidance's dual-tone lesson).
    private readonly AudioToneGenerator verticalTone = new();

    // Monotonic clock for the cross-track rate differentiator.
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    // Armed reference (destination runway)
    private Runway? runway;
    // The PAINTED LANDING THRESHOLD — every distance and centerline measurement in this class
    // anchors here. NOT runway.StartLat/StartLon, which is the physical pavement EDGE: at a
    // displaced-threshold runway the two are hundreds of feet apart (LGKR 16: 1341 ft, KJFK 22R:
    // ~3400 ft), which shortens every along-track distance by the displacement.
    private double thresholdLat;
    private double thresholdLon;
    private double thresholdElevationFt;
    private string runwayLabel = "";
    private bool armed;

    private Phase phase = Phase.Armed;
    private bool monitoringRequested;
    private bool wasAboveFlareBand;      // must see gearAGL above the latch height before engaging
    private bool toneStarted;            // lateral / pan generator
    private bool verticalToneStarted;    // flare sink-rate generator
    private bool toneReArmSpent;         // this device outage's one re-arm has run
    private bool silentFlare;            // flare engaged while visual guidance owns approach audio
    private bool rolloutAnnounced;       // announce "Rollout guidance." once per approach (not per bounce)
    private double vsSmoothed;
    private bool vsSmootherInitialized;

    // Cross-track rate state (flare lateral law). Differentiated numerically from the
    // cross-track distance — never inferred from heading, because heading is not ground track
    // in a wind, which is exactly the case the rate term exists to handle.
    private double lastCrossTrackFeet;
    private double lastCrossTrackSec;
    private bool crossTrackRateInitialized;
    private double crossTrackRateFps;

    // --- Feed gate (1 Hz slow path) ------------------------------------------------
    // Start the SIM_FRAME feed only on approach: baro altitude within this margin of
    // the destination threshold elevation. Baro error (mis-set QNH) is dwarfed by the
    // margin. Hysteresis so the gate can't flap around the boundary.
    private const double FEED_START_ABOVE_THRESHOLD_FT = 2500.0;
    private const double FEED_STOP_ABOVE_THRESHOLD_FT = 3500.0;

    // --- Flare engagement ----------------------------------------------------------
    private const double FLARE_START_GEAR_AGL_FT = 50.0;
    private const double FLARE_LATCH_GEAR_AGL_FT = 80.0;   // must descend from above this
    private const double FLARE_ENGAGE_MAX_DIST_NM = 3.0;   // must be near the selected runway
    private const double FLARE_ENGAGE_MIN_SINK_FPM = 50.0; // must actually be descending

    // --- Flare law + tone mapping ----------------------------------------------------
    // targetVS = −(gearAGL / τ · 60 + bias). τ = 6 s, bias = 120 fpm gives:
    // 50 ft → −620 fpm (≈ approach descent), 20 ft → −320, 10 ft → −220, 0 ft → −120
    // (a firm-but-comfortable touchdown). The tone encodes vsError = actual − target.
    private const double FLARE_TAU_SEC = 6.0;
    private const double FLARE_TOUCHDOWN_BIAS_FPM = 120.0;
    private const double FLARE_DEADBAND_FPM = 60.0;        // silence — on profile
    private const double FLARE_FPM_PER_TONE_UNIT = 60.0;   // 360 fpm error = full-scale tone
    private const float FLARE_TONE_MIN_HZ = 250f;
    private const float FLARE_TONE_MAX_HZ = 850f;
    private const double TONE_UNIT_RANGE = 6.0;            // ±6 units → min/max frequency
    private const double VS_EMA_ALPHA = 0.3;               // ~100 ms smoothing at 30 Hz

    // --- Flare lateral law (pan tone) -------------------------------------------------
    // Position + cross-track RATE. The rate term is not just damping: a position-only law (or
    // the rollout's intercept-crab law, which drives heading error to zero when centered)
    // cannot represent "correctly crabbed and holding", so it nags at a perfectly-flown
    // approach and settles steadily downwind. Command saturates at the full-scale pan angle,
    // so the pan can never be asked for more than hard over.
    private const double LATERAL_DEG_PER_FOOT = 0.05;
    private const double LATERAL_DEG_PER_FPS = 0.4;
    private const double LATERAL_MAX_COMMAND_DEG = 5.0;
    // The airborne lateral silence band is its OWN constant, NOT the user's
    // TakeoffAssistHeadingToneThreshold: that setting is in heading-error degrees against the
    // rollout's crab law, and reusing it here would silence the flare cue at a cross-track
    // offset the pilot never chose (at 0.05°/ft a 3° setting is 60 ft off the centerline).
    private const double LATERAL_DEADBAND_DEG = 1.5;
    private const double CROSSTRACK_RATE_EMA_ALPHA = 0.2;  // ~150 ms at 30 Hz
    private const double CROSSTRACK_RATE_MAX_DT_SEC = 0.5;  // a longer gap = re-baseline, not a rate

    // --- Go-around / bounce ----------------------------------------------------------
    private const double GO_AROUND_GEAR_AGL_FT = 100.0;    // climbed away — re-arm
    private const double BOUNCE_AIRBORNE_GEAR_AGL_FT = 5.0; // back in the air below this = bounce

    // --- Rollout centerline steering (mirrors TakeoffAssistManager modern mode) ------
    private const double CROSSTRACK_INTERCEPT_DEADBAND_FEET = 8.0;
    private const double CROSSTRACK_INTERCEPT_DEG_PER_FOOT = 0.1;
    private const double CROSSTRACK_MAX_INTERCEPT_DEG = 10.0;
    private const double PAN_FULL_RANGE_DEGREES = 5.0;

    // --- Rollout handoff --------------------------------------------------------------
    private const double ROLLOUT_END_GS_KTS = 40.0;
    private const double ROLLOUT_END_GS_WITH_EXIT_GUIDANCE_KTS = 55.0;
    private const double ROLLOUT_TURNOFF_HDG_DEG = 20.0;   // pilot committed to a runway exit
    private const double ROLLOUT_TURNOFF_MAX_GS_KTS = 90.0; // above this a heading swing is touchdown yaw/crab, not an exit

    public bool IsArmed => armed;
    public bool IsEngaged => armed && phase != Phase.Armed;

    /// <summary>Raised when the manager wants the SIM_FRAME feed started (true) or stopped (false).</summary>
    public event EventHandler<bool>? MonitoringRequestChanged;
    /// <summary>Raised on flare-engage (true) and on rollout-complete / go-around / disarm (false).</summary>
    public event EventHandler<bool>? EngagedChanged;

    public LandingFlareAssistManager(ScreenReaderAnnouncer screenReaderAnnouncer,
        Func<double> flareAglBiasFtProvider,
        Func<bool> visualGuidanceActiveCheck,
        Func<bool> landingExitGuidanceActiveCheck,
        Func<bool> landingExitTaxiSteeringCheck)
    {
        announcer = screenReaderAnnouncer;
        getFlareAglBiasFt = flareAglBiasFtProvider;
        isVisualGuidanceActive = visualGuidanceActiveCheck;
        isLandingExitGuidanceActive = landingExitGuidanceActiveCheck;
        isLandingExitTaxiSteering = landingExitTaxiSteeringCheck;
    }

    /// <summary>
    /// Arms the assist for the given destination runway. Silent — the caller composes
    /// the "destination set" announcement. Re-arming (new destination) resets any
    /// in-progress state first.
    /// </summary>
    public void Arm(Runway destinationRunway, Airport destinationAirport)
    {
        // A destination change mid-approach must not leave a tone running against
        // the old runway's geometry.
        StopEngagement(raiseEvents: true);

        runway = destinationRunway;

        // Project the anchor from the pavement edge down the runway to the PAINTED threshold. A
        // zero offset (94 % of runway ends, including every EGLL and KJFK end) is an exact no-op,
        // so this costs nothing where it doesn't apply.
        (thresholdLat, thresholdLon) = NavigationCalculator.CalculateTouchdownAimPoint(
            destinationRunway.StartLat, destinationRunway.StartLon,
            destinationRunway.Heading, destinationRunway.ThresholdOffset);

        thresholdElevationFt = destinationRunway.ThresholdElevation != 0
            ? destinationRunway.ThresholdElevation
            : destinationAirport.Altitude;
        runwayLabel = destinationRunway.RunwayID;
        armed = true;
        phase = Phase.Armed;
        wasAboveFlareBand = false;

        Log.Debug("LandingFlareAssist",
            $"Armed: {destinationAirport.ICAO} rwy {runwayLabel}, " +
            $"thrElev={thresholdElevationFt:F0} ft, displaced={destinationRunway.ThresholdOffset:F0} ft");
    }

    /// <summary>Disarms completely (destination re-selected without the checkbox, or reset).</summary>
    public void Disarm(bool announce = false)
    {
        bool wasArmed = armed;
        StopEngagement(raiseEvents: true);
        armed = false;
        runway = null;
        SetMonitoringRequested(false);

        if (announce && wasArmed)
            announcer.AnnounceImmediate("Manual landing assist off");
        Log.Debug("LandingFlareAssist", "Disarmed");
    }

    /// <summary>
    /// Sim connection dropped: the SIM_FRAME request died with it. Clear the latched
    /// monitoring flag so the next slow sample after reconnect re-issues the request,
    /// and silence any in-progress guidance.
    /// </summary>
    public void OnConnectionLost()
    {
        StopEngagement(raiseEvents: true);
        monitoringRequested = false;   // request is gone with the connection; don't raise
    }

    /// <summary>
    /// 1 Hz gate fed from the always-on INDICATED_ALTITUDE continuous variable.
    /// Decides when the high-rate feed should run: while engaged, always; while merely
    /// armed, only airborne within the altitude window of the destination threshold.
    /// </summary>
    public void ProcessSlowSample(double indicatedAltitudeFeet, bool onGround)
    {
        if (!armed)
        {
            SetMonitoringRequested(false);
            return;
        }

        if (IsEngaged)
        {
            SetMonitoringRequested(true);
            // 1 Hz is the right cadence for this — the SIM_FRAME path runs 30-60× faster and a
            // device open there would be a per-frame WASAPI call on the audio hot path.
            ReArmTonesIfDeviceLost();
            return;
        }

        double aboveThreshold = indicatedAltitudeFeet - thresholdElevationFt;
        bool want = monitoringRequested
            ? !onGround && aboveThreshold < FEED_STOP_ABOVE_THRESHOLD_FT
            : !onGround && aboveThreshold < FEED_START_ABOVE_THRESHOLD_FT;
        SetMonitoringRequested(want);
    }

    /// <summary>SIM_FRAME path — drives engagement, the flare tones, and the rollout tone.</summary>
    public void ProcessFrame(MSFSBlindAssist.SimConnect.SimConnectManager.FlareAssistData d)
    {
        if (!armed || runway == null) return;

        double gearAgl = Math.Max(0.0, d.AGL - getFlareAglBiasFt());
        bool onGround = d.OnGround >= 0.5;

        // Smooth VS continuously so the flare tone has a stable input the moment it engages.
        if (!vsSmootherInitialized)
        {
            vsSmoothed = d.VerticalSpeedFPM;
            vsSmootherInitialized = true;
        }
        else
        {
            vsSmoothed += VS_EMA_ALPHA * (d.VerticalSpeedFPM - vsSmoothed);
        }

        switch (phase)
        {
            case Phase.Armed:
                if (onGround) return;
                if (gearAgl > FLARE_LATCH_GEAR_AGL_FT) wasAboveFlareBand = true;

                if (wasAboveFlareBand &&
                    gearAgl <= FLARE_START_GEAR_AGL_FT &&
                    vsSmoothed < -FLARE_ENGAGE_MIN_SINK_FPM &&
                    NavigationCalculator.CalculateDistance(
                        d.Latitude, d.Longitude,
                        thresholdLat, thresholdLon) <= FLARE_ENGAGE_MAX_DIST_NM)
                {
                    EnterFlare();
                }
                break;

            case Phase.Flare:
                if (onGround)
                {
                    EnterRollout();
                    UpdateRolloutTone(d);
                }
                else if (gearAgl > GO_AROUND_GEAR_AGL_FT)
                {
                    // Climbing away. The tones MUST NOT just fall silent — silence means
                    // "on profile" in flare mode — so speak the state change.
                    StopEngagement(raiseEvents: true);
                    announcer.AnnounceImmediate("Go around detected. Flare assist rearmed.");
                }
                else
                {
                    if (silentFlare)
                    {
                        // VG owns the approach audio while its tones run. If the pilot toggles
                        // VG off mid-flare we take both flare tones over.
                        if (isVisualGuidanceActive()) break;
                        silentFlare = false;
                        StartVerticalToneIfNeeded();
                        StartLateralToneIfNeeded();
                    }
                    UpdateFlareVerticalTone(gearAgl);
                    UpdateFlareLateralTone(d);
                }
                break;

            case Phase.Rollout:
                if (!onGround && gearAgl > BOUNCE_AIRBORNE_GEAR_AGL_FT)
                {
                    // Bounce — back into the air below go-around height. Resume flare
                    // guidance so the tones talk the pilot through the second touchdown.
                    phase = Phase.Flare;
                    if (!silentFlare)
                    {
                        StartVerticalToneIfNeeded();   // restarted — it was stopped at touchdown
                        UpdateFlareVerticalTone(gearAgl);
                        UpdateFlareLateralTone(d);
                    }
                    return;
                }
                UpdateRolloutTone(d);
                CheckRolloutHandoff(d);
                break;
        }
    }

    private void EnterFlare()
    {
        phase = Phase.Flare;
        rolloutAnnounced = false;
        crossTrackRateInitialized = false;   // fresh differentiator per engagement
        // Visual guidance active = it owns the approach audio (its own flare cue included).
        // Stay silent through the flare; the rollout pan tone still runs after touchdown
        // because VG auto-deactivates on the touchdown edge.
        silentFlare = isVisualGuidanceActive();

        if (!silentFlare)
        {
            StartVerticalToneIfNeeded();
            StartLateralToneIfNeeded();
            announcer.AnnounceImmediate("Flare guidance");
        }

        EngagedChanged?.Invoke(this, true);
        Log.Debug("LandingFlareAssist", $"Flare engaged (silent={silentFlare})");
    }

    /// <summary>
    /// Flare VERTICAL sink-rate tone (frequency-coded, contrasting waveform). Silence when the
    /// sink rate is within the deadband of the exponential-flare target.
    /// </summary>
    private void UpdateFlareVerticalTone(double gearAgl)
    {
        double targetVs = LandingGuidanceLaws.FlareTargetVsFpm(gearAgl, FLARE_TAU_SEC, FLARE_TOUCHDOWN_BIAS_FPM);
        double vsError = vsSmoothed - targetVs;   // < 0: sinking too fast → pull up (high tone)

        if (Math.Abs(vsError) <= FLARE_DEADBAND_FPM)
        {
            verticalTone.UpdateVolume(0);
        }
        else
        {
            // CommandToToneUnits negates once, centrally: excessive sink (negative error) → high
            // frequency = "come up"; floating (positive error) → low = "go down". Matches the brief.
            verticalTone.UpdatePitch(LandingGuidanceLaws.CommandToToneUnits(
                vsError, FLARE_FPM_PER_TONE_UNIT, TONE_UNIT_RANGE));
            verticalTone.UpdateVolume(SettingsManager.Current.TakeoffAssistToneVolume);
        }
    }

    /// <summary>
    /// Flare LATERAL pan tone. Pan is the side to steer TOWARD (same convention as takeoff
    /// assist and the taxi steering tone), so the pilot rudders into the tone through the flare.
    ///
    /// The command is the centerline PD law — position plus cross-track RATE — so a correctly
    /// crabbed approach that is holding the centerline reads as on-profile and stays silent,
    /// which a position-only law could not do.
    /// </summary>
    private void UpdateFlareLateralTone(MSFSBlindAssist.SimConnect.SimConnectManager.FlareAssistData d)
    {
        if (runway == null) return;

        double headingTrue = d.HeadingMagnetic + d.MagneticVariation;
        var track = RunwayCenterlineTracker.Compute(
            d.Latitude, d.Longitude, headingTrue,
            thresholdLat, thresholdLon, runway.Heading);

        double crossTrackFeet = track.CrossTrackFeet;   // + = LEFT of centerline
        UpdateCrossTrackRate(crossTrackFeet);

        // + = steer RIGHT (aircraft left of centerline, or drifting further left).
        double commandDeg = LandingGuidanceLaws.LateralCommandDeg(
            crossTrackFeet, crossTrackRateFps,
            LATERAL_DEG_PER_FOOT, LATERAL_DEG_PER_FPS, LATERAL_MAX_COMMAND_DEG);

        ApplyPan(commandDeg, LATERAL_DEADBAND_DEG);
    }

    /// <summary>
    /// Numerically differentiates cross-track distance. Never inferred from heading: heading is
    /// not ground track in a wind, and a crosswind flare is precisely the case the rate term is
    /// there for. A missing or implausibly long frame gap re-baselines instead of producing a
    /// spike — one bad rate sample would hard-pan the tone in the last seconds before touchdown.
    /// </summary>
    private void UpdateCrossTrackRate(double crossTrackFeet)
    {
        double now = clock.Elapsed.TotalSeconds;

        if (!crossTrackRateInitialized)
        {
            lastCrossTrackFeet = crossTrackFeet;
            lastCrossTrackSec = now;
            crossTrackRateFps = 0.0;
            crossTrackRateInitialized = true;
            return;
        }

        double dt = now - lastCrossTrackSec;
        if (dt <= 0.0 || dt > CROSSTRACK_RATE_MAX_DT_SEC)
        {
            lastCrossTrackFeet = crossTrackFeet;
            lastCrossTrackSec = now;
            return;
        }

        double raw = (crossTrackFeet - lastCrossTrackFeet) / dt;   // + = drifting further LEFT
        crossTrackRateFps += CROSSTRACK_RATE_EMA_ALPHA * (raw - crossTrackRateFps);
        lastCrossTrackFeet = crossTrackFeet;
        lastCrossTrackSec = now;
    }

    private void EnterRollout()
    {
        phase = Phase.Rollout;
        StopVerticalTone();            // flare sink-rate cue is done at touchdown
        // The lateral tone carries straight through from the flare — only the law feeding it
        // changes. (StartLateralToneIfNeeded is a no-op unless the flare was silent under VG.)
        StartLateralToneIfNeeded();
        tone.UpdatePitch(0);           // pan mode: park the frequency at centre, meaning is the pan

        if (!rolloutAnnounced)
        {
            rolloutAnnounced = true;
            announcer.AnnounceImmediate("Rollout guidance");
        }
        Log.Debug("LandingFlareAssist", "Rollout engaged");
    }

    private void UpdateRolloutTone(MSFSBlindAssist.SimConnect.SimConnectManager.FlareAssistData d)
    {
        if (runway == null) return;

        // Same math as TakeoffAssistManager's modern mode: pan tracks the intercept
        // heading that CONVERGES on the centerline, not the bare runway heading.
        double headingTrue = d.HeadingMagnetic + d.MagneticVariation;
        var track = RunwayCenterlineTracker.Compute(
            d.Latitude, d.Longitude, headingTrue,
            thresholdLat, thresholdLon, runway.Heading);

        double runwayHeadingMag = runway.Heading - d.MagneticVariation;
        double headingDiff = d.HeadingMagnetic - runwayHeadingMag;
        while (headingDiff > 180.0) headingDiff -= 360.0;
        while (headingDiff < -180.0) headingDiff += 360.0;

        double crossTrackFeet = track.CrossTrackFeet; // + = left of centerline
        double desiredCrabDeg = 0.0;
        double absCt = Math.Abs(crossTrackFeet);
        if (absCt > CROSSTRACK_INTERCEPT_DEADBAND_FEET)
        {
            double mag = Math.Min(
                (absCt - CROSSTRACK_INTERCEPT_DEADBAND_FEET) * CROSSTRACK_INTERCEPT_DEG_PER_FOOT,
                CROSSTRACK_MAX_INTERCEPT_DEG);
            desiredCrabDeg = mag * Math.Sign(crossTrackFeet);
        }

        // > 0 = steer right. Pan in the steer direction (steer toward the tone), matching
        // takeoff assist and the taxi steering tone; the same user settings apply.
        ApplyPan(desiredCrabDeg - headingDiff);
    }

    /// <summary>
    /// Drives the lateral generator from a steer command in degrees (+ = steer RIGHT), honoring
    /// the shared TakeoffAssist pan settings (waveform/volume/invert/hard-pan) so the flare, the
    /// rollout, takeoff assist and taxi guidance all behave identically for a given configuration.
    /// </summary>
    /// <param name="silenceBelowDeg">
    /// Silence band. Null = use the user's <c>TakeoffAssistHeadingToneThreshold</c>, which is the
    /// rollout's contract (its command IS a heading error, the quantity that setting describes).
    /// The flare passes its own band instead — see <see cref="LATERAL_DEADBAND_DEG"/>.
    /// </param>
    private void ApplyPan(double steerCommandDeg, double? silenceBelowDeg = null)
    {
        var settings = SettingsManager.Current;

        float pan = settings.TakeoffAssistHardPanTone
            ? Math.Sign(steerCommandDeg)
            : (float)Math.Clamp(steerCommandDeg / PAN_FULL_RANGE_DEGREES, -1.0, 1.0);
        if (settings.TakeoffAssistInvertPanning) pan = -pan;
        tone.SetPan(pan);

        double threshold = silenceBelowDeg ?? settings.TakeoffAssistHeadingToneThreshold;
        bool shouldPlay = threshold <= 0 || Math.Abs(steerCommandDeg) >= threshold;
        tone.UpdateVolume(shouldPlay ? settings.TakeoffAssistToneVolume : 0);
    }

    private void CheckRolloutHandoff(MSFSBlindAssist.SimConnect.SimConnectManager.FlareAssistData d)
    {
        if (runway == null) return;

        double gs = d.GroundSpeedKnots;

        double headingDiff = (d.HeadingMagnetic + d.MagneticVariation) - runway.Heading;
        while (headingDiff > 180.0) headingDiff -= 360.0;
        while (headingDiff < -180.0) headingDiff += 360.0;
        bool turnedOff = Math.Abs(headingDiff) > ROLLOUT_TURNOFF_HDG_DEG &&
                         gs < ROLLOUT_TURNOFF_MAX_GS_KTS;

        // Hand off earlier when the landing-exit planner's rollout guidance is running,
        // so its exit-steering tone (which activates below ~50 kt near the exit) never
        // plays on top of ours.
        double endGs = isLandingExitGuidanceActive()
            ? ROLLOUT_END_GS_WITH_EXIT_GUIDANCE_KTS
            : ROLLOUT_END_GS_KTS;

        // ...but a rapid exit can take the handoff ABOVE that speed, and the moment it
        // does, the taxi steering tone is already panning toward the exit. Speed is the
        // wrong question then: end here whatever the groundspeed, or two pan tones give
        // the pilot opposite steering (worst on a shallow exit, where the heading never
        // swings the 20 degrees `turnedOff` needs).
        if (gs < endGs || turnedOff || isLandingExitTaxiSteering())
        {
            StopEngagement(raiseEvents: true);
            announcer.AnnounceImmediate("Rollout guidance complete");
            // Stay ARMED: circuits / touch-and-go get flare guidance again on the next
            // approach without re-opening the destination dialog. The feed gate drops
            // the SIM_FRAME request within a second (on ground, not engaged).
        }
    }

    /// <summary>
    /// What to do about a tone that came back from a routing sweep with no device.
    /// Pure so the policy can be pinned by <c>LandingFlareToneReArmTests</c> — the manager
    /// itself owns two real WASAPI generators and cannot be driven from a test.
    /// </summary>
    /// <param name="reArmSpent">This outage's one re-arm has already run.</param>
    internal readonly record struct ToneReArmDecision(
        bool RestartLateral, bool RestartVertical, bool SpendReArm);

    /// <summary>
    /// The router rebinds a NeedsDevice generator on its next SWEEP, and a sweep is what a
    /// device ARRIVING triggers — so a rebind that moves one tone and fails the other leaves
    /// the failed one silent with nothing scheduled to retry it. That is why this exists.
    ///
    /// Restarts ONLY the tone that lost its device, unlike VisualGuidanceManager's re-arm,
    /// which rebuilds its pair whenever either half dies. VG's two are a reference and a
    /// follower that mean nothing apart; these two are independent axes, so tearing down a
    /// healthy one would punch an audible hole in a cue the pilot is actively flying.
    ///
    /// Gated on NeedsDevice, never IsPlaying: IsPlaying reads false for the whole duration of
    /// a HEALTHY in-flight rebind (RebindTo clears it before reopening the device), so a
    /// 1 Hz sample landing mid-rebind would tear down an about-to-succeed tone. NeedsDevice is
    /// set only at an open attempt's terminal outcomes, so it never means "attempt in flight".
    ///
    /// A tone that is not STARTED is never restarted — the vertical tone is flare-only and is
    /// legitimately stopped for the whole rollout.
    /// </summary>
    internal static ToneReArmDecision DecideToneReArm(
        bool lateralStarted, bool lateralNeedsDevice,
        bool verticalStarted, bool verticalNeedsDevice,
        bool reArmSpent)
    {
        bool lateralLost = lateralStarted && lateralNeedsDevice;
        bool verticalLost = verticalStarted && verticalNeedsDevice;

        // Healthy: clear the latch so a LATER outage on the same approach re-arms once more.
        if (!lateralLost && !verticalLost)
            return new ToneReArmDecision(false, false, false);

        // One re-arm per outage. The second and later retries belong to the router's
        // event-driven sweeps; without this the 1 Hz sampler would attempt a WASAPI open every
        // tick for the rest of the approach — the retry loop docs/audio.md forbids.
        if (reArmSpent)
            return new ToneReArmDecision(false, false, true);

        return new ToneReArmDecision(lateralLost, verticalLost, true);
    }

    /// <summary>
    /// 1 Hz while engaged. Restarts a tone whose device went away — see
    /// <see cref="DecideToneReArm"/> for why the manager cannot leave this to the router.
    /// </summary>
    private void ReArmTonesIfDeviceLost()
    {
        ToneReArmDecision d = DecideToneReArm(
            toneStarted, tone.NeedsDevice,
            verticalToneStarted, verticalTone.NeedsDevice,
            toneReArmSpent);

        toneReArmSpent = d.SpendReArm;

        if (d.RestartLateral)
        {
            Log.Debug("LandingFlareAssist",
                "Lateral tone needs a device (rebind failed or its endpoint was lost); restarting it");
            tone.Stop();
            toneStarted = false;
            StartLateralToneIfNeeded();
        }

        if (d.RestartVertical)
        {
            Log.Debug("LandingFlareAssist",
                "Vertical tone needs a device (rebind failed or its endpoint was lost); restarting it");
            verticalTone.Stop();
            verticalToneStarted = false;
            StartVerticalToneIfNeeded();
        }
    }

    private void StartLateralToneIfNeeded()
    {
        if (toneStarted) return;
        tone.Configure(FLARE_TONE_MIN_HZ, FLARE_TONE_MAX_HZ, TONE_UNIT_RANGE, TONE_UNIT_RANGE);
        var settings = SettingsManager.Current;
        tone.Start(settings.TakeoffAssistToneWaveform, settings.TakeoffAssistToneVolume);
        tone.UpdatePitch(0);   // pan tone: parked at the centre frequency, meaning is the pan
        tone.SetPan(0);
        // Engage silent: the first Update…Tone frame sets the real volume, so an on-profile
        // engage is heard as silence (the design's core promise).
        tone.UpdateVolume(0);
        toneStarted = true;
    }

    private void StartVerticalToneIfNeeded()
    {
        if (verticalToneStarted) return;
        verticalTone.Configure(FLARE_TONE_MIN_HZ, FLARE_TONE_MAX_HZ, TONE_UNIT_RANGE, TONE_UNIT_RANGE);
        var settings = SettingsManager.Current;
        verticalTone.Start(ContrastingWaveform(settings.TakeoffAssistToneWaveform),
            settings.TakeoffAssistToneVolume);
        verticalTone.UpdateVolume(0);   // engage silent
        verticalToneStarted = true;
    }

    private void StopVerticalTone()
    {
        if (!verticalToneStarted) return;
        verticalTone.Stop();
        verticalToneStarted = false;
    }

    /// <summary>
    /// The vertical sink-rate tone must differ in TIMBRE from the lateral pan tone so the two
    /// never blur while both sound in the flare (visual guidance's dual-tone lesson). The lateral
    /// keeps the user's chosen waveform; the vertical takes a distinct one — mirroring VG's proven
    /// sine/triangle pairing, and falling back to a smooth sine against any bright lateral choice.
    /// </summary>
    private static HandFlyWaveType ContrastingWaveform(HandFlyWaveType lateral) => lateral switch
    {
        HandFlyWaveType.Sine => HandFlyWaveType.Triangle,
        HandFlyWaveType.Triangle => HandFlyWaveType.Sine,
        _ => HandFlyWaveType.Sine,   // bright lateral (Sawtooth/Square) → smooth sine vertical
    };

    /// <summary>Stops any running tone and returns to Armed. Does NOT clear the armed reference.</summary>
    private void StopEngagement(bool raiseEvents)
    {
        bool wasEngaged = IsEngaged;
        if (toneStarted)
        {
            tone.Stop();
            toneStarted = false;
        }
        StopVerticalTone();
        phase = Phase.Armed;
        wasAboveFlareBand = false;
        // Both tones are down, so the next engagement starts its own outage accounting —
        // a spent latch carried into the next approach would cost it its one re-arm.
        toneReArmSpent = false;
        silentFlare = false;
        vsSmootherInitialized = false;
        crossTrackRateInitialized = false;
        crossTrackRateFps = 0.0;

        if (raiseEvents && wasEngaged)
            EngagedChanged?.Invoke(this, false);
    }

    private void SetMonitoringRequested(bool want)
    {
        if (want == monitoringRequested) return;
        monitoringRequested = want;
        MonitoringRequestChanged?.Invoke(this, want);
    }

    public void Dispose()
    {
        tone.Stop();
        tone.Dispose();
        verticalTone.Stop();
        verticalTone.Dispose();
        GC.SuppressFinalize(this);
    }
}

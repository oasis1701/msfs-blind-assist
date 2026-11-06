using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Manages visual landing guidance using dual audio tones.
/// The DESIRED tone encodes the PID-commanded pitch (frequency, 200–800 Hz over the profile's
/// TonePitchRangeDeg — ±6° by default, configured SYMMETRICALLY so the mapping stays one
/// straight line, unlike the generator's own asymmetric −10°/+20° defaults which belong to hand
/// fly) and commanded bank (stereo pan over ±5° by default). The CURRENT tone plays alongside it, mirroring
/// the same mapping against the aircraft's *actual* pitch and bank, so frequency match
/// (zero-beat) means correct pitch attitude / vertical speed and pan match means correct bank.
/// Aircraft-specific tunables (approach AoA, Vref, rate caps, tone frequency range) come from
/// <see cref="VisualGuidanceProfile"/>.
/// </summary>
public class VisualGuidanceManager : IDisposable
{
    private readonly ScreenReaderAnnouncer announcer;
    private bool isActive = false;

    // Guidance state
    private GuidancePhase currentPhase = GuidancePhase.NotStarted;
    private Runway? runway;
    private double thresholdElevationMSL = 0.0;

    // Joining final (visual guidance redesign, stage 4; VisualGuidanceJoin). Once the wings are nearly
    // level after Ctrl+V, BA decides once how the aircraft reaches final; pressing Ctrl+V off and on
    // decides again. Joining holds the heading being flown until the turn onto final is due (VG-23).
    private VisualGuidanceJoinPlan? joinPlan;
    // The leg being flown, an index into joinPlan.Legs: the downwind, then the base, then final for the
    // simple pattern; final at once for every other plan.
    private int activeLegIndex;
    private bool turnBaseLinePending;                 // the pattern's base turn has come and "Turn base" is still to say
    private bool turnOntoFinalDue;                    // the turn onto final has come (on final: no turn to make)
    private double frozenInterceptDeg = double.NaN;   // joining: the intercept being flown when the turn came
    private DateTime? establishedSince;               // inside the established criteria since, else null
    private double maxApproachBankDeg = 25.0;         // VisualGuidanceProfile.MaxApproachBankDeg
    private double minimumFinalNm = 6.0;              // VisualGuidanceProfile.MinimumFinalNm
    // What the join has said: the decision's update (a turn already due then is not announced), the
    // turn line, the countdown (armed only if the turn was further off than its mark when first known),
    // and "Heading does not cross final" (said after 3 s not crossing; again only after 3 s crossing).
    private bool decidedThisUpdate;
    private bool turnLineHandled;
    private double? firstSecondsToTurn;
    private bool countdown30Said, countdown15Said;
    private DateTime? crossingSince, notCrossingSince;
    private bool notCrossingArmed;

    // Audio generators — both always run while guidance is active.
    private AudioToneGenerator? desiredAttitudeTone;  // PID-commanded attitude
    private AudioToneGenerator? currentAttitudeTone;  // Aircraft's actual attitude (follower; matched by ear)
    private HandFlyWaveType guidanceWaveType = HandFlyWaveType.Triangle;
    private double guidanceVolume = 0.05; // Default 5%
    private HandFlyWaveType currentToneWaveType = HandFlyWaveType.Sine;
    private double currentToneVolume = 0.05;
    private bool hardPanTone = false;  // see VisualGuidanceHardPanTone setting
    // The aircraft's dual-tone frequency/pan mapping, set from VisualGuidanceProfile in
    // Initialize (populated the same way as the other "aircraft-specific tunables" fields
    // below). Cached here — rather than read once from Initialize's own `profile` parameter —
    // because ProcessUpdate's tone-pair re-arm (see there) has to replay the same Configure()
    // call on a freshly reconstructed generator pair, and a method parameter does not survive
    // past Initialize's return. Defaults are the A320 numbers, matching every other tunable
    // below when no profile has been applied yet.
    private VisualGuidanceProfile visualGuidanceProfile = new();
    // Defer audible Start() until the first ProcessUpdate computes real pitch/bank — otherwise
    // the user hears ~33 ms of fused 500 Hz center-pan tone that represents nothing. Both tones
    // are instantiated in Initialize so disposal/lifecycle stays simple; this flag controls
    // when the audio output actually fires up.
    private bool tonesNeedStart = false;

    // One re-arm (dispose + rebuild + restart of the tone pair) per device-loss EPISODE. Set
    // when the re-arm in ProcessUpdate fires; cleared when both tones are playing again (and
    // on Initialize/DisposeTones). Needed because a failed restart no longer nulls the tones
    // (they stay registered so the router's next sweep can retry them) — without this latch
    // the re-arm would see their NeedsDevice again on the very next update (every sim frame) and rebuild the
    // pair per tick, a per-tick WASAPI open on the UI thread, i.e. the self-sustaining retry
    // loop docs/audio.md's known-limitations section forbids.
    private bool toneReArmSpent = false;

    // Bank (degrees, standard convention) at which hard-pan mode snaps to full left / right.
    // Below this magnitude the tone stays centered, avoiding twitchy flips around 0°.
    private const double HARD_PAN_DEADBAND_DEG = 1.0;

    // Position tracking
    private double? cachedLatitude;
    private double? cachedLongitude;
    private double? cachedAGL;
    private double? cachedAltMSL;
    private double? cachedBank;
    private double? cachedHeading;
    private double? cachedGroundTrack;  // GPS ground track for drift detection

    // Performance data for dynamic pitch calculation
    private double currentGroundSpeedKnots = 0.0;
    private double currentVerticalSpeedFPM = 0.0;
    private double currentPitch = 0.0;
    private double? smoothedCurrentFPM = null;  // Smoothed vertical speed for FPM-based guidance
    private double lastCalculatedTargetFPM = 0.0;  // Last calculated target FPM for hotkey announcement
    private double lastCalculatedAltitudeError = 0.0;  // Last calculated altitude error for hotkey announcement (positive = high, negative = low)

    // Real time (VG-20). ProcessUpdate runs on every sim frame, so every rate limit, smoothing
    // filter and derivative below runs on the measured frame time through VisualGuidanceTiming,
    // with the numbers the guidance was designed with at one update a second.
    private DateTime? lastUpdateUtc;      // the clock at the last ProcessUpdate that had data
    private double frameSeconds;          // seconds since the update before it, capped at 1 s
    // False until the first update after Initialize has produced a command: that update takes
    // the guidance's command with no rate limit, since there is nothing yet to be continuous with.
    private bool hasPreviousCommand;

    // State tracking for derivative term and rate limiting (lateral)
    private double previousDesiredBank = 0.0;
    private double? smoothedCrossTrackRate = null;  // Smoothed rate to prevent oscillation during handoff
    private const double CROSS_TRACK_RATE_WINDOW_S = 4.0;  // the design's 5 samples at 1 Hz
    private readonly VisualGuidanceWindowedRate crossTrackRateWindow = new(CROSS_TRACK_RATE_WINDOW_S);

    // State tracking for derivative term and rate limiting (vertical)
    private const double FPM_ERROR_RATE_WINDOW_S = 1.0;    // the design's interval between updates
    private readonly VisualGuidanceWindowedRate fpmErrorRateWindow = new(FPM_ERROR_RATE_WINDOW_S);
    private double previousDesiredPitch = 0.0;

    // Flare state tracking
    private double lastFlarePitch = 3.0;           // For rate limiting during flare

    // Announcement tracking
    private DateTime lastPhaseAnnouncement = DateTime.MinValue;
    private DateTime lastDistanceAnnouncement = DateTime.MinValue;
    private readonly HashSet<double> armedMileMarks = new();   // the mile marks still to be said on the way in
    private bool distanceCalloutsPrimed;   // the first update after Ctrl+V arms the marks still ahead
    // Decides when a bank callout is spoken (VG-21); rebuilt in Initialize with the profile's tone bank range.
    private VisualGuidanceBankCalloutGate bankCalloutGate = new(new VisualGuidanceProfile().ToneBankRangeDeg);
    private const int ANNOUNCEMENT_INTERVAL_MS = 1000;

    // Manual-query grace window (VG-19). When the pilot fires a readout hotkey (F status,
    // P pitch, S speed, etc.) while VG is active, a bank callout (AnnounceImmediate) would
    // cut the readout off mid-sentence. NotifyManualQuery() sets this timestamp; the bank
    // callouts may not speak while ClockForHarness() is before it. Held-back callouts lose
    // nothing: the gate keeps tracking, so the first one after the window speaks the error
    // as it is then. One-shot announcements (phase changes, distance callouts) are NOT
    // suppressed — they're rare and important enough to let through. (The centreline
    // callouts this window also covered were removed in stage 3 of the redesign, 2026-11:
    // F speaks the sideways position in every phase instead.)
    // Visual guidance's own lines open the same window for their own speaking time (Say's
    // holdSeconds, VG-21): a bank callout interrupts, and interrupting drops queued speech.
    private DateTime routineAnnouncementsSuppressedUntil = DateTime.MinValue;
    private const double MANUAL_QUERY_GRACE_SECONDS = 3.0;

    // How long each of visual guidance's own lines holds the bank callouts back: the line's
    // speaking time through System.Speech at Rate = 0 (the only SAPI rate this app produces),
    // trailing silence trimmed, plus about a fifth, as VG-1 measures. Measured 2026-11-04; a new or
    // reworded line is measured the same way, never estimated (VG-1, VG-21).
    private const double HOLD_ACTIVATION_S = 4.0;          // "Visual guidance active, runway 27L" 3.31 s
    private const double HOLD_FLARE_S = 0.6;               // "Flare" 0.49 s
    private const double HOLD_TOUCHDOWN_S = 0.9;           // "Touchdown" 0.72 s
    // Joining final's lines, measured 2026-11-04 the same way.
    private const double HOLD_ON_FINAL_S = 0.9;            // "On final" 0.70 s
    private const double HOLD_JOINING_S = 1.1;             // "Joining final" 0.90 s
    private const double HOLD_VECTORING_S = 1.4;           // "Vectoring to final" 1.15 s
    private const double HOLD_COUNTDOWN_30_S = 2.6;        // "Turn onto final in 30 seconds" 2.13 s
    private const double HOLD_COUNTDOWN_15_S = 2.7;        // "Turn onto final in 15 seconds" 2.23 s
    private const double HOLD_TURN_ONTO_FINAL_S = 1.4;     // "Turn onto final" 1.11 s
    private const double HOLD_TURN_BASE_S = 1.0;           // "Turn base" 0.78 s (measured 2026-11-05)
    private const double HOLD_NOT_CROSSING_S = 2.1;        // "Heading does not cross final" 1.68 s
    private const double HOLD_ESTABLISHED_S = 1.7;         // "Established on final" 1.39 s
    private const double HOLD_5_MILES_S = 1.4;             // "5 miles out" 1.11 s
    private const double HOLD_3_MILES_S = 1.2;             // "3 miles out" 0.99 s
    private const double HOLD_1_MILE_S = 1.1;              // "1 mile out" 0.85 s

    // Where BA's own queued lines are expected to finish (decision 11 of the stage-4 plan): a queued
    // line is spoken after the one before it, so its hold starts where that one's ends. An interrupting
    // line clears the queue, so its own end replaces this.
    private DateTime queuedSpeechEndsUtc = DateTime.MinValue;

    // Guidance constants
    private const double TOUCHDOWN_ALTITUDE_FT = 5.0;    // Consider touchdown below 5 ft AGL

    // Flare rate limiting constant
    // Maximum pitch change rate during flare (deg/sec). 10, not the 1.5 the 1 Hz design wrote: once
    // limits ran on real time (VG-20), 1.5°/s took a jet's cue 2.7 s to reach the flare pitch, after
    // its wheels touched (30 ft is about 2.4 s up), and the harness's jets landed about 230 fpm
    // firmer. 10°/s reaches it in about 0.4 s, close to the per-frame jump pilots had flown, with
    // touchdowns within 20 fpm of it (docs/virtual-pilot.md, "Stage 2"). Stage 6 replaces the flare.
    private const double MAX_FLARE_PITCH_RATE = 10.0;

    // ILS glideslope angle — usually 3°, but published per-runway (LCY 5.5°, Aspen 6.59°,
    // Innsbruck 3.8°). Populated in Initialize() from runway.GlideslopeAngleDeg with a 3.0°
    // fallback if the runway has no ILS or the navdata doesn't carry the angle. Used by the
    // ideal-glidepath altitude, the natural-3°-descent-rate FPM target, the phase-capture
    // deviation test, and the nominal-pitch baseline.
    private const double DEFAULT_GLIDESLOPE_ANGLE_DEG = 3.0;
    private double glideslopeAngleDeg = DEFAULT_GLIDESLOPE_ANGLE_DEG;

    // New vertical guidance constants
    private const double GLIDESLOPE_LOCK_DISTANCE_NM = 1.0;  // Distance at which to lock to steady 3° angle
    private const double GLIDESLOPE_GAIN = 2.0;              // Proportional gain: fpm correction per foot of deviation
    private const double MAX_DESCENT_RATE_FPM = -1500.0;     // Maximum descent rate when high

    // Lateral guidance gains (tuned for blind pilot manual landing with audio guidance)
    private const double LATERAL_GAIN_TRACKING = 120.0;    // Cross-track error (NM) to bank for tracking (increased for faster convergence)
    private const double LATERAL_RATE_DAMPING = 12.0;    // Cross-track rate damping
    private const double LATERAL_HEADING_GAIN = 1.0;     // Track/heading error to bank for alignment (Boeing 747: 1.0-2.8)
    // Aircraft-specific tunables — populated from IAircraftDefinition.GetVisualGuidanceProfile() in Initialize().
    // Defaults below are the A320 numbers used historically (preserved when no profile is supplied).
    private double airspeedReferenceKnots = 140.0;       // Reference Vref for sqrt(GS/Vref) lateral gain scaling
    private double maxBankRateDegPerSec = 3.0;           // Cap on commanded bank change rate (deg/sec)

    // Within this of the centreline the precision track flies (unchanged from before the redesign);
    // outside it, the capture law onto final (VisualGuidanceJoin.DesiredLegTrackDeg), which replaced
    // the 60° and 45° intercepts and the fixed-gain arc in stage 4.
    private const double ARC_MODE_EXIT_NM = 0.05;

    // Vertical guidance — aircraft-specific (set from VisualGuidanceProfile in Initialize).
    // typicalApproachAoaDeg is now only a FALLBACK used when measured AoA is unavailable or
    // outside the sanity band. Live AoA from SimConnect (INCIDENCE ALPHA) is preferred — it
    // inherently encodes weight + flap + speed and makes the nominal-pitch baseline converge
    // on whatever the airplane is actually trimmed for.
    private double typicalApproachAoaDeg = 6.0;
    private double maxPitchRateDegPerSec = 2.5;          // Cap on commanded pitch change rate (deg/sec)

    // Live angle of attack (degrees, right-handed: positive = nose pitched above relative
    // wind). Updated each frame from VISUAL_GUIDANCE_AOA. Smoothed with an EMA to remove
    // sim/sensor jitter (gust-driven AoA wiggle would otherwise modulate the desired-tone
    // frequency around the true trimmed value).
    private double? cachedAoaDeg;
    private double? smoothedAoaDeg;
    private const double AOA_SMOOTHING_FACTOR_PER_SECOND = 0.7;  // Same characteristic as FPM smoothing
    // Sanity band on the measured AoA. Stabilized-approach AoA sits in ~3–8° band; flare
    // briefly spikes to ~8–10°. Outside [-5°, 20°] we treat the reading as a sim glitch and
    // fall back to typicalApproachAoaDeg. -5° lower bound covers nose-low cruise descent;
    // 20° upper bound is well past stall AoA for any transport airframe.
    private const double AOA_MIN_VALID_DEG = -5.0;
    private const double AOA_MAX_VALID_DEG = 20.0;
    // Glidepath/flare altitude calibration — see VisualGuidanceProfile for the full rationale.
    // glideslopeAltitudeBiasFt is ADDED to the ideal distance×tan(3°) glidepath so VG's
    // "on glideslope" matches the real ILS (which crosses the threshold at the ~50 ft TCH).
    // flareAltitudeBiasFt is SUBTRACTED from the measured datum altitude before the flare /
    // touchdown thresholds are tested so they fire at true main-gear height. The two are
    // applied in DIFFERENT code paths (glideslope-error vs phase-detection) and are NOT a
    // single shared constant — they were measured separately (on the beam vs near the
    // ground, different attitudes), so do not "reconcile" them into one value.
    private double glideslopeAltitudeBiasFt = 60.0;      // A320 estimate; 777 measured = 80
    private double flareAltitudeBiasFt = 12.0;           // A320 estimate; 777 measured = 30
    // True main-gear height (ft) at which the flare phase fires. Per-aircraft: A320 default
    // 30 (FCTM), PMDG 777 = 40 (matches the autopilot's autoland flare initiation).
    private double flareTriggerWheelHeightFt = 30.0;
    // Target pitch commanded during the Flare phase. A320 default 6° per FCTM, 777 lower
    // (~4.5° per Boeing FCTM, "2–3° pitch increase from approach attitude" — 777 approach
    // pitch is ~+1.5°). The 6.0 here is only the pre-profile default; the aircraft's
    // VisualGuidanceProfile.FlareTargetPitchDeg overwrites it and is authoritative.
    private double flareTargetPitchDeg = 6.0;
    private const double CROSS_TRACK_RATE_SMOOTHING_PER_SECOND = 0.85;  // Exponential smoothing for cross-track rate, per second (strong filtering to prevent noise spikes)

    // FPM-based vertical guidance constants
    private const double FPM_SMOOTHING_FACTOR_PER_SECOND = 0.7;  // Exponential smoothing for current FPM, per second (moderate filtering)
    private const double FPM_P_GAIN = 0.005;             // Pitch correction per FPM error (degrees per FPM)
    private const double FPM_D_GAIN = 0.002;             // Pitch damping per FPM error rate (degrees per FPM/sec)

    public bool IsActive => isActive;
    public event EventHandler<bool>? VisualGuidanceActiveChanged;

    /// <summary>
    /// VirtualPilot hook (VP-1): when true, <see cref="Initialize"/> builds no tones and
    /// <see cref="ProcessUpdate"/> runs the guidance with none, so a simulated approach opens no
    /// audio device. Set only by tools/VirtualPilot and tests; the app never sets it. An instance
    /// <c>init</c> flag rather than a static switch, so parallel tests need no shared collection.
    /// </summary>
    internal bool HeadlessForHarness { get; init; }

    /// <summary>
    /// VirtualPilot hook (VP-1): every clock read in visual guidance goes through this. The app
    /// never sets it, so it reads <see cref="SimClock.UtcNow"/> (which the harness overrides);
    /// unit tests give each manager its own fake clock, so they need no shared override.
    /// </summary>
    internal Func<DateTime> ClockForHarness { get; init; } = static () => SimClock.UtcNow;

    /// <summary>
    /// VirtualPilot hook (VP-1, VP-2): where the flight recording's lines go. The app never sets it,
    /// so they go to <c>visual_guidance_flight.log</c> (<see cref="VisualGuidanceFlightRecord"/>);
    /// the harness and unit tests give their own sink.
    /// </summary>
    internal Action<string> FlightRecordForHarness { get; init; } = static line => FlightRecordLog.Info(line);

    private static readonly LogChannel FlightRecordLog = Log.Channel(VisualGuidanceFlightRecord.LogChannelName);

    // The flight recording's clock zero: the Initialize that started this approach. Recording is
    // true from that Initialize to the Stop, so an activation that fails before Initialize (Stop
    // with no runway) writes no stop line without a start.
    private DateTime recordStartUtc;
    private bool recording;

    /// <summary>VirtualPilot hook (VP-1): the pitch (nose-up positive) commanded on the last <see cref="ProcessUpdate"/>, before tone saturation.</summary>
    internal double DesiredPitchForHarness { get; private set; }

    /// <summary>VirtualPilot hook (VP-1): the bank (right-positive) commanded on the last <see cref="ProcessUpdate"/>, before tone saturation.</summary>
    internal double DesiredBankForHarness { get; private set; }

    /// <summary>VirtualPilot hook (VP-1): the current guidance phase's name.</summary>
    internal string PhaseForHarness => currentPhase.ToString();

    /// <summary>
    /// VirtualPilot hook (VP-1): the capture gain, deg·kt/nm (<see cref="VisualGuidanceJoin"/>). The app
    /// never sets it; the harness sweeps it to pick the default.
    /// </summary>
    internal double CaptureGainForHarness { get; init; } = VisualGuidanceJoin.DefaultCaptureGain;

    /// <summary>VirtualPilot hook (VP-1): the decision's name (OnFinal, Joining, Vectoring, Pattern), "None" before it.</summary>
    internal string DecisionForHarness => joinPlan?.State.ToString() ?? "None";

    /// <summary>SimConnect's pitch (radians, nose-down positive, Euler convention) in degrees nose-up positive. MainForm and VirtualPilot both convert through this.</summary>
    internal static double PitchDegreesFromSimConnect(double radians) => -(radians * 180.0 / Math.PI);

    /// <summary>SimConnect's bank (radians) in degrees, still left-positive: <see cref="StandardBank"/> flips it at the consumer (VG-7).</summary>
    internal static double BankDegreesFromSimConnect(double radians) => radians * 180.0 / Math.PI;

    /// <summary>SimConnect's INCIDENCE ALPHA (radians) in degrees.</summary>
    internal static double AoaDegreesFromSimConnect(double radians) => radians * 180.0 / Math.PI;

    /// <summary>
    /// What F says (VG-22): the phase, the sideways position, then the vertical, for example "On
    /// final, 6.2 miles. On the centreline. 40 feet high, target 750 down."
    /// (<see cref="VisualGuidanceStatusText"/>). Before the first update has placed the aircraft,
    /// "Visual guidance starting." The altitude error and target descent rate are the last
    /// update's; the position is the latest SimConnect gave.
    /// </summary>
    public string GetStatusReadout()
    {
        var phase = currentPhase switch
        {
            GuidancePhase.Deciding => VisualGuidanceReadoutPhase.Deciding,
            GuidancePhase.Joining => VisualGuidanceReadoutPhase.Joining,
            GuidancePhase.Vectoring => VisualGuidanceReadoutPhase.Vectoring,
            GuidancePhase.Final => VisualGuidanceReadoutPhase.OnFinal,
            GuidancePhase.Flare => VisualGuidanceReadoutPhase.Flare,
            GuidancePhase.Touchdown => VisualGuidanceReadoutPhase.Touchdown,
            _ => VisualGuidanceReadoutPhase.Starting,
        };
        if (phase == VisualGuidanceReadoutPhase.Starting || runway == null
            || cachedLatitude is not double lat || cachedLongitude is not double lon || cachedAltMSL is not double altMSL)
            return VisualGuidanceStatusText.Compose(new VisualGuidanceStatus(VisualGuidanceReadoutPhase.Starting, 0, 0, 0, 0, 0));

        double cross = SignedCrossTrackNm(runway, lat, lon);
        var status = new VisualGuidanceStatus(
            phase,
            DistanceNm: NavigationCalculator.CalculateDistance(lat, lon, runway.StartLat, runway.StartLon),
            CrossTrackNm: cross,
            AltitudeErrorFt: lastCalculatedAltitudeError,
            TargetFpm: lastCalculatedTargetFPM,
            WheelHeightFt: altMSL - thresholdElevationMSL - flareAltitudeBiasFt);
        // Joining and vectoring: the join from here (the countdown speaks the same time), and the pattern's leg.
        if (phase is VisualGuidanceReadoutPhase.Joining or VisualGuidanceReadoutPhase.Vectoring
            && PictureOfTheJoin(lat, lon, cross) is JoinPicture join)
        {
            status = status with
            {
                Leg = ActiveLegKind,
                InterceptDeg = join.InterceptDeg, Turning = turnOntoFinalDue, CrossesFinal = join.CrossesFinal,
                JoinAlongNm = join.JoinAlongNm, SecondsToTurn = join.SecondsToTurn, FinalToTheRight = join.FinalToTheRight,
            };
        }
        return VisualGuidanceStatusText.Compose(status);
    }

    /// <summary>
    /// Guidance phases from Ctrl+V to touchdown.
    /// </summary>
    private enum GuidancePhase
    {
        NotStarted,         // before the first update with a position
        Deciding,           // banked 5° or more: both tones on the aircraft until the wings are nearly level
        Joining,            // on a heading that crosses final: held until the turn onto final ("Joining final")
        Vectoring,          // BA chose the route onto final, straight in or by the simple pattern ("Vectoring to final")
        Final,              // established on final (or pressed there)
        Flare,              // the main gear under the profile's flare height
        Touchdown           // the main gear under 5 ft
    }

    public VisualGuidanceManager(ScreenReaderAnnouncer screenReaderAnnouncer)
    {
        announcer = screenReaderAnnouncer;
    }

    /// <summary>
    /// Toggles visual guidance mode on/off. On activation, flips isActive=true and fires the
    /// event so MainForm's handler can validate state + call <see cref="Initialize"/>. On
    /// deactivation, delegates fully to <see cref="Stop"/> which owns the entire teardown
    /// sequence (tones, state, event, announcement) — keeping the active→inactive transition
    /// identical regardless of trigger (hotkey, on-ground auto-deactivate, HandFly disable).
    /// </summary>
    public void Toggle()
    {
        if (isActive)
        {
            Stop();  // performs the flip, event, announce in one place
        }
        else
        {
            isActive = true;
            VisualGuidanceActiveChanged?.Invoke(this, true);
        }
    }

    /// <summary>
    /// Called by MainForm when the pilot fires a manual readout hotkey (F status, P pitch,
    /// S airspeed, etc.) while VG is active. Opens a short grace window during which the bank
    /// callouts may not speak (VG-19), so the readout the pilot asked for plays to completion
    /// instead of being cut off. No-op when VG is inactive.
    /// </summary>
    public void NotifyManualQuery()
    {
        if (!isActive) return;
        routineAnnouncementsSuppressedUntil = ClockForHarness().AddSeconds(MANUAL_QUERY_GRACE_SECONDS);
    }

    /// <summary>True while a manual-query grace window is open (see <see cref="NotifyManualQuery"/>).</summary>
    private bool RoutineAnnouncementsSuppressed => ClockForHarness() < routineAnnouncementsSuppressedUntil;

    /// <summary>
    /// Initializes visual guidance with runway, audio preferences, and aircraft-specific tunables.
    /// Two tones always play: the desired tone encodes PID-commanded attitude (frequency = pitch
    /// command, pan = bank command); the current tone mirrors the same mapping against the
    /// aircraft's actual attitude. The pilot matches pans (lateral) and zero-beats frequencies
    /// (vertical) by ear.
    /// </summary>
    public void Initialize(Runway destinationRunway, Airport destinationAirport,
                          HandFlyWaveType guidanceToneWaveform, double toneVolume,
                          HandFlyWaveType currentToneWaveform, double currentToneVol,
                          bool hardPan,
                          VisualGuidanceProfile profile)
    {
        // Defensive: if Initialize is called twice without an intervening Stop
        // (Toggle's flow guarantees Stop runs first today, but a future caller
        // might not), dispose any existing tones so we don't leak audio handles.
        // Do NOT call Stop() — Stop announces "Visual guidance off", which would
        // be followed immediately by Initialize's "Visual guidance active"
        // announcement, and that confusing sequence is exactly what defensive
        // re-init should avoid. Stop also nulls isActive / runway / airport
        // which are about to be re-set below.
        DisposeTones();

        runway = destinationRunway;
        guidanceWaveType = guidanceToneWaveform;
        guidanceVolume = toneVolume;
        currentToneWaveType = currentToneWaveform;
        currentToneVolume = currentToneVol;
        hardPanTone = hardPan;
        // Prefer the runway end's own elevation (runway_end.altitude) over the airport's
        // published field elevation — matters at airports with sloped runways or different
        // threshold elevations between ends (KASE, LSZS, KSEA 16L/34R). Fall back to airport
        // altitude when the runway value is unknown (older DB builds, soft runways, etc.).
        thresholdElevationMSL = destinationRunway.ThresholdElevation > 0
            ? destinationRunway.ThresholdElevation
            : destinationAirport.Altitude;
        // Use the published per-runway glideslope angle (LCY 5.5°, Aspen 6.59°, Innsbruck
        // 3.8°, ...) when navdata has it. Fall back to 3° for runways without an ILS row
        // or DB builds that don't populate ils.gs_pitch. Sanity-clamp to [2°, 8°] to reject
        // corrupt navdata (a 0.1° or 30° value would yield nonsense guidance); real-world
        // ILS angles fit comfortably inside this band — Aspen at 6.59° is the steepest
        // certified Cat I in the world.
        double rawGsAngle = destinationRunway.GlideslopeAngleDeg;
        glideslopeAngleDeg = (rawGsAngle >= 2.0 && rawGsAngle <= 8.0)
            ? rawGsAngle
            : DEFAULT_GLIDESLOPE_ANGLE_DEG;

        // Apply aircraft-specific tunables (preserves A320 defaults if profile is the base instance).
        typicalApproachAoaDeg = profile.TypicalApproachAoaDeg;
        airspeedReferenceKnots = profile.ReferenceVrefKnots;
        maxPitchRateDegPerSec = profile.MaxPitchRateDegPerSec;
        maxBankRateDegPerSec = profile.MaxBankRateDegPerSec;
        glideslopeAltitudeBiasFt = profile.GlideslopeAltitudeBiasFt;
        flareAltitudeBiasFt = profile.FlareAltitudeBiasFt;
        flareTriggerWheelHeightFt = profile.FlareTriggerWheelHeightFt;
        flareTargetPitchDeg = profile.FlareTargetPitchDeg;
        maxApproachBankDeg = profile.MaxApproachBankDeg;
        minimumFinalNm = profile.MinimumFinalNm;
        // Cached for ProcessUpdate's tone-pair re-arm — see the field comment.
        visualGuidanceProfile = profile;

        // Reset state: every press of Ctrl+V decides afresh.
        currentPhase = GuidancePhase.NotStarted;
        joinPlan = null;
        activeLegIndex = 0;
        turnBaseLinePending = false;
        turnOntoFinalDue = false;
        frozenInterceptDeg = double.NaN;
        establishedSince = null;
        decidedThisUpdate = false;
        turnLineHandled = false;
        firstSecondsToTurn = null;
        countdown30Said = countdown15Said = false;
        crossingSince = notCrossingSince = null;
        notCrossingArmed = false;
        queuedSpeechEndsUtc = DateTime.MinValue;
        lastPhaseAnnouncement = DateTime.MinValue;
        lastDistanceAnnouncement = DateTime.MinValue;
        armedMileMarks.Clear();
        distanceCalloutsPrimed = false;
        // A fresh gate (nothing spoken, no holds) on this aircraft's tone bank range: past it both
        // tones are pinned and only the callouts say how far (VG-21).
        bankCalloutGate = new VisualGuidanceBankCalloutGate(profile.ToneBankRangeDeg);
        routineAnnouncementsSuppressedUntil = DateTime.MinValue;
        cachedLatitude = null;
        cachedLongitude = null;
        cachedAGL = null;
        cachedAltMSL = null;
        cachedBank = null;
        cachedHeading = null;
        cachedGroundTrack = null;
        cachedAoaDeg = null;
        smoothedAoaDeg = null;

        // Reset the frame clock: the first update after activation has no frame time and no
        // previous command, so it is not rate-limited (VG-20).
        lastUpdateUtc = null;
        frameSeconds = 0.0;
        hasPreviousCommand = false;

        // Reset derivative term tracking (lateral)
        previousDesiredBank = 0.0;
        smoothedCrossTrackRate = null;
        crossTrackRateWindow.Clear();

        // Reset derivative term tracking (vertical)
        fpmErrorRateWindow.Clear();
        previousDesiredPitch = 0.0;
        smoothedCurrentFPM = null;

        // Reset flare state
        lastFlarePitch = 3.0;

        // Instantiate both generators and apply the aircraft's pitch→frequency mapping. We do
        // NOT Start() them yet — the first ProcessUpdate call kicks off audio output once real
        // pitch/bank values exist, avoiding ~33 ms of meaningless fused-tone playback at the
        // default center frequency / center pan. Configure() must run before Start(), so we
        // call it here even though Start is deferred.
        // Headless (VirtualPilot, VP-1): no tones at all; DisposeTones above already left
        // tonesNeedStart false, so nothing later tries to start them.
        if (!HeadlessForHarness)
        {
            try
            {
                desiredAttitudeTone = new AudioToneGenerator();
                currentAttitudeTone = new AudioToneGenerator();
                desiredAttitudeTone.Configure(profile.ToneMinFrequencyHz, profile.ToneMaxFrequencyHz,
                                              profile.TonePitchRangeDeg, profile.ToneBankRangeDeg);
                currentAttitudeTone.Configure(profile.ToneMinFrequencyHz, profile.ToneMaxFrequencyHz,
                                              profile.TonePitchRangeDeg, profile.ToneBankRangeDeg);
                tonesNeedStart = true;
                Log.Debug("VisualGuidance", $"Tones instantiated ({profile.ToneMinFrequencyHz}–{profile.ToneMaxFrequencyHz} Hz over ±{profile.TonePitchRangeDeg}° pitch, pan ±{profile.ToneBankRangeDeg}° bank); deferring Start until first ProcessUpdate");
            }
            catch (Exception ex)
            {
                Log.Debug("VisualGuidance", $"Failed to instantiate tones: {ex.Message}");
                desiredAttitudeTone?.Dispose();
                desiredAttitudeTone = null;
                currentAttitudeTone?.Dispose();
                currentAttitudeTone = null;
                tonesNeedStart = false;
            }
        }

        // The flight recording (VP-2): one start line per approach, then a line per update.
        recordStartUtc = ClockForHarness();
        recording = true;
        Record(VisualGuidanceFlightRecord.Start(destinationAirport.ICAO, runway.RunwayID, runway.StartLat, runway.StartLon,
            runway.Heading, runway.HeadingMag, thresholdElevationMSL, glideslopeAngleDeg, glideslopeAltitudeBiasFt, flareAltitudeBiasFt));

        Say($"Visual guidance active, runway {runway.RunwayID}", immediate: true, HOLD_ACTIVATION_S);
        Log.Debug("VisualGuidance", $"Initialized for runway {runway.RunwayID}");
    }

    /// <summary>
    /// Stops visual guidance: tears down both tones, clears runway state, fires the
    /// <see cref="VisualGuidanceActiveChanged"/> event with <c>false</c>, and (by default)
    /// announces "off". Idempotent — calling when already inactive is a no-op (no spurious
    /// event, no double announce). Always fires the event when a real transition happens, so
    /// every caller — hotkey toggle, on-ground auto-deactivate, HandFly disable, validation
    /// failure — gets MainForm's monitoring cleanup automatically.
    ///
    /// Pass <paramref name="announce"/> = false for activation-failure paths (validation errors
    /// in the event handler). Toggle has already flipped isActive=true but no guidance ever
    /// actually ran, so the misleading "Visual guidance off" callout is suppressed; the event
    /// still fires so MainForm can clean up any monitoring it queued.
    ///
    /// Internal callers that need silent tone-only teardown (e.g. the defensive re-init path
    /// in <see cref="Initialize"/>) should use <see cref="DisposeTones"/> directly.
    /// </summary>
    public void Stop(bool announce = true)
    {
        if (!isActive)
            return;  // idempotent — no double-event, no double-announce

        DisposeTones();

        runway = null;
        isActive = false;

        VisualGuidanceActiveChanged?.Invoke(this, false);
        if (announce)
            Say("Visual guidance off", immediate: false, holdSeconds: 0);   // guidance has ended
        Record(VisualGuidanceFlightRecord.Stop(RecordSeconds()));
        recording = false;
        Log.Debug("VisualGuidance", $"Stopped (announce={announce})");
    }

    /// <summary>
    /// Every line visual guidance speaks goes through here, so the flight recording has it (VP-2).
    /// <paramref name="immediate"/>: interrupt (AnnounceImmediate), else Announce.
    /// <paramref name="holdSeconds"/>: how long this line holds the bank callouts back, its measured
    /// speaking time (the HOLD_ constants, VG-21), since a bank callout interrupts and would drop it;
    /// 0 for the bank callouts themselves. A queued line is spoken after the queued lines before it,
    /// so its hold starts where theirs ends; an interrupting line's starts at once. It only ever
    /// extends the window, never shortens it.
    /// </summary>
    private void Say(string text, bool immediate, double holdSeconds)
    {
        if (immediate)
            announcer.AnnounceImmediate(text);
        else
            announcer.Announce(text);
        DateTime now = ClockForHarness();
        DateTime start = immediate || queuedSpeechEndsUtc < now ? now : queuedSpeechEndsUtc;
        DateTime end = start.AddSeconds(Math.Max(0, holdSeconds));
        // An interrupting line clears the screen reader's queue: what follows it is spoken after it.
        if (immediate || end > queuedSpeechEndsUtc)
            queuedSpeechEndsUtc = end;
        if (holdSeconds > 0 && end > routineAnnouncementsSuppressedUntil)
            routineAnnouncementsSuppressedUntil = end;
        Record(VisualGuidanceFlightRecord.Said(RecordSeconds(), text, immediate));
    }

    private double RecordSeconds() => (ClockForHarness() - recordStartUtc).TotalSeconds;

    // The recording must never disturb guidance: a failing sink loses its line, nothing else.
    private void Record(string line)
    {
        if (!recording) return;
        try { FlightRecordForHarness(line); }
        catch (Exception ex) { Log.Debug("VisualGuidance", $"Flight record line lost: {ex.Message}"); }
    }

    /// <summary>Signed as the lateral law takes it: nautical miles, + right of the centreline facing the runway.</summary>
    private double SignedCrossTrackNm(Runway rwy, double lat, double lon)
        => Math.Sign(NavigationCalculator.CalculateCrossTrackError(lat, lon, rwy.StartLat, rwy.StartLon, rwy.Heading))
           * NavigationCalculator.CalculateDistanceToLocalizer(lat, lon, rwy.StartLat, rwy.StartLon, rwy.Heading);

    /// <summary>
    /// Silently dispose both tones and clear the deferred-Start flag. Used by <see cref="Stop"/>
    /// (which adds the public-facing announcement + lifecycle bits) and by the defensive path
    /// in <see cref="Initialize"/> (which should NOT announce because the next line announces
    /// "Visual guidance active").
    /// </summary>
    private void DisposeTones()
    {
        if (desiredAttitudeTone != null)
        {
            desiredAttitudeTone.Stop();
            desiredAttitudeTone.Dispose();
            desiredAttitudeTone = null;
        }
        if (currentAttitudeTone != null)
        {
            currentAttitudeTone.Stop();
            currentAttitudeTone.Dispose();
            currentAttitudeTone = null;
        }
        tonesNeedStart = false;  // re-armed by next Initialize
        toneReArmSpent = false;  // a fresh pair starts a fresh episode
    }

    /// <summary>
    /// First-call audible-Start for both tones. Honors the "follower starts only if reference
    /// started" rule from Initialize.
    ///
    /// A tone that FAILS to start is KEPT, never disposed or nulled: a failed
    /// <c>Start</c> leaves the generator registered in the router with <c>NeedsDevice</c> set,
    /// which is exactly the set the next routing sweep (a device arriving, a settings save)
    /// retries — it restarts the tone with the wave/volume recorded here. The old
    /// dispose-and-null gave up on the whole approach after ONE failed attempt: the disposed
    /// generators left the router's registry, ProcessUpdate's top guard returned on the null
    /// forever, and audio coming back seconds later found nothing to restore. The follower is
    /// still not STARTED until the reference plays (a lone follower is a constant drone);
    /// ProcessUpdate's pair-completion check starts it on the reference's own recovery edge.
    /// </summary>
    private void StartTonesIfNeeded()
    {
        if (!tonesNeedStart) return;
        tonesNeedStart = false;

        // AudioToneGenerator.Start catches its own exceptions and leaves the instance in a
        // no-op state (isPlaying=false, internal providers nulled by Cleanup). It does NOT
        // throw, so the outer try/catch here is defensive against future changes only —
        // the meaningful signal is the IsPlaying property after Start returns.
        try
        {
            desiredAttitudeTone?.Start(guidanceWaveType, guidanceVolume);
        }
        catch (Exception ex)
        {
            Log.Debug("VisualGuidance", $"Desired-attitude tone Start threw (unexpected): {ex.Message}");
        }

        // If the desired tone failed to actually start (audio device error, driver issue),
        // skip starting the follower — a current-tone-only setup is useless (nothing to match
        // against) and would just play a constant 500 Hz background drone. Both generators
        // stay alive and registered so the router can retry the reference; see the class doc.
        if (desiredAttitudeTone == null || !desiredAttitudeTone.IsPlaying)
        {
            Log.Debug("VisualGuidance", "Desired-attitude tone did not start; leaving the pair registered for the router's next sweep");
            return;
        }

        Log.Debug("VisualGuidance", "Desired-attitude tone started");

        if (currentAttitudeTone != null)
        {
            try
            {
                currentAttitudeTone.Start(currentToneWaveType, currentToneVolume);
            }
            catch (Exception ex)
            {
                Log.Debug("VisualGuidance", $"Current-attitude tone Start threw (unexpected): {ex.Message}");
            }

            if (currentAttitudeTone.IsPlaying)
            {
                Log.Debug("VisualGuidance", "Current-attitude tone started");
            }
            else
            {
                // Follower failed to start — the reference plays alone for the moment, but the
                // follower stays registered with NeedsDevice set, so the router's next sweep
                // retries it (the pilot has the PID commands meanwhile; just no
                // current-attitude reference).
                Log.Debug("VisualGuidance", "Current-attitude tone did not start; leaving it registered for the router's next sweep");
            }
        }
    }

    /// <summary>
    /// Converts a SimConnect bank reading (positive = left wing down) into the standard
    /// right-positive convention <see cref="AudioToneGenerator.UpdateBank"/> expects. Centralizes
    /// the sign flip so every consumer of <see cref="cachedBank"/> can use the same name and
    /// reviewers immediately see when the conversion is missing.
    /// </summary>
    private static double StandardBank(double simConnectBank) => -simConnectBank;

    /// <summary>
    /// Applies a bank command to a tone, honoring the hard-pan setting. In hard-pan mode the
    /// pan snaps to ±1.0 once bank magnitude exceeds <see cref="HARD_PAN_DEADBAND_DEG"/> — useful
    /// for stereo-speaker setups where partial pan is hard to distinguish from centred.
    /// </summary>
    private void ApplyBank(AudioToneGenerator tone, double bankDegreesStandard)
    {
        if (hardPanTone)
        {
            float pan = Math.Abs(bankDegreesStandard) < HARD_PAN_DEADBAND_DEG
                ? 0f
                : (bankDegreesStandard > 0 ? 1f : -1f);
            tone.SetPan(pan);
        }
        else
        {
            tone.UpdateBank(bankDegreesStandard);
        }
    }

    /// <summary>
    /// Updates cached position data from SimConnect
    /// </summary>
    public void UpdateLatitude(double latitude) => cachedLatitude = latitude;
    public void UpdateLongitude(double longitude) => cachedLongitude = longitude;
    public void UpdateAGL(double agl) => cachedAGL = agl;
    public void UpdateAltitudeMSL(double altMSL) => cachedAltMSL = altMSL;

    /// <summary>
    /// Updates cached attitude data from hand fly monitoring
    /// </summary>
    public void UpdatePitch(double pitchDegrees)
    {
        currentPitch = pitchDegrees;  // Also update for dynamic pitch calculation
    }
    public void UpdateBank(double bankDegrees) => cachedBank = bankDegrees;

    /// <summary>
    /// Updates the cached angle of attack (degrees) from SimConnect's INCIDENCE ALPHA. Used
    /// in <see cref="CalculateDesiredPitch"/> to bias the nominal pitch baseline. Smoothing
    /// and validity gating are applied at consumption time (not here) so the raw cache stays
    /// honest about whether a sample exists.
    /// </summary>
    public void UpdateAoA(double aoaDegrees) => cachedAoaDeg = aoaDegrees;

    /// <summary>
    /// Optional live override for the lateral airspeed-compensation Vref. Aircraft models
    /// that publish their pilot-entered landing Vref (e.g., PMDG 777's FMC_LandingVREF in
    /// the CDA broadcast) can push it here to replace the profile-default reference. Values
    /// ≤0 are ignored — the profile default is preserved when no FMC entry exists. Used by
    /// the lateral PID's sqrt(GS / Vref) scaler; the effect on bank command is second-order
    /// (a ±10 kt Vref error shifts speedFactor by ~3%), so this is a polish improvement
    /// rather than a correctness fix.
    /// </summary>
    public void UpdateReferenceVref(double knots)
    {
        if (knots > 0)
        {
            airspeedReferenceKnots = knots;
            Log.Debug("VisualGuidance", $"ReferenceVref updated to {knots:F0} kt (live override)");
        }
    }
    public void UpdateHeading(double headingDegrees) => cachedHeading = headingDegrees;
    public void UpdateGroundTrack(double groundTrackDegrees) => cachedGroundTrack = groundTrackDegrees;

    /// <summary>
    /// Updates performance data for dynamic pitch calculation
    /// </summary>
    public void UpdateGroundSpeed(double groundSpeedKnots) => currentGroundSpeedKnots = groundSpeedKnots;
    public void UpdateVerticalSpeed(double verticalSpeedFPM) => currentVerticalSpeedFPM = verticalSpeedFPM;

    /// <summary>
    /// Processes all cached data when complete position update is available. Called on every sim
    /// frame from MainForm (the VISUAL_GUIDANCE data arrives per SIM_FRAME), so every limit and
    /// filter runs on the measured frame time (VG-20).
    /// </summary>
    public void ProcessUpdate()
    {
        // Headless (VirtualPilot, VP-1) runs the guidance with no tones; every tone use below
        // is null-safe. In the app the flag is false and this guard is unchanged.
        if (!isActive || runway == null || (desiredAttitudeTone == null && !HeadlessForHarness))
        {
            return;
        }

        // The pair invariant StartTonesIfNeeded enforces at startup has to hold continuously:
        // a device change rebinds the two generators independently, so EITHER one can die
        // while the other survives, and a lone tone is useless in both directions. The
        // follower alone is a constant drone the pilot cannot match anything against; the
        // reference alone is a commanded attitude with nothing to compare it to, which is the
        // same silence dressed up as guidance. Re-arming here restores the invariant on the
        // next update instead of leaving it broken for the rest of the approach.
        //
        // WATCHES BOTH TONES, deliberately. Gating on the reference alone covered exactly one
        // of the two orderings: a sweep that rebound desiredAttitudeTone successfully and then
        // failed to reopen currentAttitudeTone left the follower registered with NeedsDevice
        // set and silent, the re-arm never fired, and the pilot flew the rest of the approach
        // hearing the commanded attitude with nothing to zero-beat it against.
        // currentAttitudeTone stays null-conditional defensively (Initialize's catch nulls
        // both tones on a construction failure, and the top guard only checks the reference).
        //
        // Gated on NeedsDevice, NOT IsPlaying -- IsPlaying is the wrong signal here and reads
        // false for the ENTIRE DURATION of a healthy, in-flight rebind, not just a failed one.
        // AudioToneGenerator.RebindTo sets isPlaying = false BEFORE it reopens the device, and
        // StartLocked does not set it back to true until the new WasapiOut has actually been
        // built and Play() has been called -- the WASAPI enumerate, GetDevice, construction and
        // Init/Play in between all run with isPlaying still false. That rebind happens on the
        // router's own worker thread with no synchronisation against this UI-thread tick, so a
        // ProcessUpdate landing mid-rebind would read a healthy, about-to-succeed tone as dead.
        // DisposeTones's first Stop() blocks on the very startStopLock the router thread is
        // holding; by the time it is released the router has already reopened a good session,
        // which this block would then immediately stop and dispose again -- tearing down the
        // follower too even though ITS rebind may have succeeded cleanly -- for an avoidable
        // extra teardown/reopen and audio gap during the exact device change the router's
        // "move it without stopping guidance" sweep exists to make seamless.
        // NeedsDevice does not have that transient-false window: StartLocked sets it ONLY at
        // the two genuinely terminal outcomes of an open attempt (opened == null, or the catch
        // block) and clears it only on that same attempt's full success -- never on an
        // in-progress one -- and OnPlaybackStopped sets it the instant a live fault is detected.
        // So NeedsDevice==true always means "as of the last attempt, this generator ended up
        // with no device," never "an attempt is currently in flight." Do not swap this back to
        // IsPlaying, and do not require IsPlaying == false in addition to NeedsDevice: the two
        // can legitimately both be true right after OnPlaybackStopped fires on a still-flagged-
        // playing session, and requiring IsPlaying too would just delay detection of that case
        // until some later rebind attempt touches the flag.
        //
        // !tonesNeedStart is what stops this from firing on the very first frame after
        // Initialize -- the tones are constructed there but not yet Start()ed, so NeedsDevice
        // is still false at that point too (it defaults false and nothing has attempted an open
        // yet). tonesNeedStart only latches back to false once StartTonesIfNeeded has actually
        // attempted a Start, so this block cannot see a "not started yet" tone as a "died" tone.
        // Once it does fire it sets tonesNeedStart back to true and returns -- it does not call
        // Start itself -- so the very next StartTonesIfNeeded (later in this same call, once the
        // cached-data guard below has passed) consumes that flag and is the only thing that can
        // flip it false again.
        //
        // ONE re-arm per device-loss episode (toneReArmSpent). A failed restart KEEPS the pair
        // -- StartTonesIfNeeded no longer disposes or nulls on failure, precisely so the
        // generators stay in the router's registry with NeedsDevice set and a device arriving
        // restarts them -- which means NeedsDevice can still read true on the next update.
        // Without the latch this block would rebuild the pair per tick (a per-tick WASAPI open
        // on the UI thread, the retry loop docs/audio.md forbids); with it, the second and
        // later retries belong to the router's event-driven sweeps. The latch clears once both
        // tones are playing again, so a LATER outage in the same approach re-arms once more.
        bool desiredNeedsDevice = desiredAttitudeTone != null && desiredAttitudeTone.NeedsDevice;
        bool currentNeedsDevice = currentAttitudeTone?.NeedsDevice == true;
        if (!tonesNeedStart && (desiredNeedsDevice || currentNeedsDevice))
        {
            if (!toneReArmSpent)
            {
                string which = desiredNeedsDevice && currentNeedsDevice ? "Both attitude tones need"
                    : desiredNeedsDevice ? "The desired-attitude tone needs"
                    : "The current-attitude tone needs";
                Log.Debug("VisualGuidance", $"{which} a device (rebind failed or its endpoint was lost); restarting both tones");
                DisposeTones();
                // AFTER DisposeTones, which resets the latch for its other callers
                // (Stop/Initialize) — set before it, the latch would clear itself and the
                // re-arm would fire again every tick.
                toneReArmSpent = true;
                desiredAttitudeTone = new AudioToneGenerator();
                currentAttitudeTone = new AudioToneGenerator();
                desiredAttitudeTone.Configure(visualGuidanceProfile.ToneMinFrequencyHz, visualGuidanceProfile.ToneMaxFrequencyHz,
                                              visualGuidanceProfile.TonePitchRangeDeg, visualGuidanceProfile.ToneBankRangeDeg);
                currentAttitudeTone.Configure(visualGuidanceProfile.ToneMinFrequencyHz, visualGuidanceProfile.ToneMaxFrequencyHz,
                                              visualGuidanceProfile.TonePitchRangeDeg, visualGuidanceProfile.ToneBankRangeDeg);
                tonesNeedStart = true;
            }
            // else: this episode's one re-arm already ran; the registered NeedsDevice
            // generators are the router's to retry on its next sweep.
        }
        else if (!tonesNeedStart
                 && desiredAttitudeTone?.IsPlaying == true
                 && currentAttitudeTone != null
                 && !currentAttitudeTone.IsPlaying
                 && !currentAttitudeTone.NeedsDevice)
        {
            // PAIR COMPLETION. The router healed the reference (it was registered with
            // NeedsDevice and a sweep restarted it), but the follower was never STARTED --
            // StartTonesIfNeeded starts it only after the reference plays, so a failed
            // reference start leaves the follower cold, and a cold tone (no NeedsDevice) is
            // one the router deliberately refuses to start on its own. Start it here, on the
            // reference's own recovery edge. Self-limiting: after this call the follower is
            // either playing (this branch stops matching) or flagged NeedsDevice (the router's
            // to retry), so it cannot fire per tick.
            try
            {
                currentAttitudeTone.Start(currentToneWaveType, currentToneVolume);
                Log.Debug("VisualGuidance", currentAttitudeTone.IsPlaying
                    ? "Current-attitude tone started on the reference's recovery"
                    : "Current-attitude tone still has no device; leaving it registered for the router's next sweep");
            }
            catch (Exception ex)
            {
                Log.Debug("VisualGuidance", $"Current-attitude tone Start threw (unexpected): {ex.Message}");
            }
        }

        // Both tones sounding again closes the device-loss episode: a later outage in this
        // same approach gets its own one-shot re-arm.
        if (toneReArmSpent
            && desiredAttitudeTone?.IsPlaying == true
            && currentAttitudeTone?.IsPlaying == true)
        {
            toneReArmSpent = false;
        }

        // Ensure we have all required data
        if (!cachedLatitude.HasValue || !cachedLongitude.HasValue ||
            !cachedAGL.HasValue || !cachedAltMSL.HasValue ||
            !cachedHeading.HasValue)
        {
            return;
        }

        double lat = cachedLatitude.Value;
        double lon = cachedLongitude.Value;
        double agl = cachedAGL.Value;
        double altMSL = cachedAltMSL.Value;
        double heading = cachedHeading.Value;

        // The frame time every limit and filter in this update runs on (VG-20): 0 on the first
        // update, on a second update at the same instant, or if the clock steps back; at most 1 s
        // after a pause or a stalled frame stream.
        DateTime now = ClockForHarness();
        frameSeconds = VisualGuidanceTiming.FrameSeconds(lastUpdateUtc, now);
        lastUpdateUtc = now;

        try
        {
            // Update phase based on position
            decidedThisUpdate = false;
            UpdatePhase(lat, lon, agl, altMSL);

            // Calculate guidance
            double desiredBank = CalculateDesiredBank(lat, lon, heading);
            double desiredPitch = CalculateDesiredPitch(lat, lon, agl, altMSL);

            // The join's own lines (the turn, the countdown, a heading that stops crossing final), before
            // the bank callouts, so their holds apply in this very update (VG-21).
            HandleJoinCallouts(lat, lon);
            hasPreviousCommand = true;
            DesiredBankForHarness = desiredBank;
            DesiredPitchForHarness = desiredPitch;
            double currentBankStandard = StandardBank(cachedBank ?? 0.0);

            // The flight recording (VP-2): the state this update read and the commands it produced,
            // before anything it speaks.
            Record(VisualGuidanceFlightRecord.Frame(new VisualGuidanceFrameRecord(
                T: (now - recordStartUtc).TotalSeconds, Phase: currentPhase.ToString(),
                Lat: lat, Lon: lon, AltMslFt: altMSL, AglFt: agl,
                GsKt: currentGroundSpeedKnots, VsFpm: currentVerticalSpeedFPM,
                TrackMagDeg: cachedGroundTrack ?? heading, HeadingMagDeg: heading,
                PitchDeg: currentPitch, BankDeg: cachedBank is double bank ? StandardBank(bank) : double.NaN,
                AoaDeg: cachedAoaDeg ?? double.NaN,
                DesiredPitchDeg: desiredPitch, DesiredBankDeg: desiredBank,
                TargetFpm: lastCalculatedTargetFPM, AltitudeErrorFt: lastCalculatedAltitudeError,
                CrossTrackNm: SignedCrossTrackNm(runway, lat, lon),
                DistanceNm: NavigationCalculator.CalculateDistance(lat, lon, runway.StartLat, runway.StartLon))));

            // First-frame deferred Start — by the time the 150 ms WasapiOut buffer fills, the
            // phase-continuous oscillator's portamento has reached the target frequency (well
            // under a millisecond, whatever rate the endpoint mixes at), so the very first
            // audible note already reflects the commanded / actual attitude. No fused-tone
            // glitch at session start.
            // NOTE: StartTonesIfNeeded can null out desiredAttitudeTone if no output opens or
            // Init throws (bad audio device, driver issue). Re-check both tones after this — the
            // early-out at the top of ProcessUpdate only proves they were non-null on entry.
            StartTonesIfNeeded();

            // Update desired (PID-commanded) attitude tone
            if (desiredAttitudeTone != null)
            {
                desiredAttitudeTone.UpdatePitch(desiredPitch);
                ApplyBank(desiredAttitudeTone, desiredBank);
            }

            // Update current (actual) attitude tone — same Hz / pan mappings, so frequency match
            // ⇒ correct pitch attitude (and thus correct VS for the glideslope), pan match ⇒
            // correct bank. Pilot zero-beats the two by ear.
            if (currentAttitudeTone != null)
            {
                currentAttitudeTone.UpdatePitch(currentPitch);
                ApplyBank(currentAttitudeTone, currentBankStandard);
            }

            // Announce commanded bank angle
            AnnounceBankGuidance(desiredBank);

            // Distance callouts
            HandleDistanceCallouts(lat, lon);
        }
        catch (Exception ex)
        {
            Log.Debug("VisualGuidance", $"Error processing update: {ex.Message}");
        }
    }

    /// <summary>
    /// Moves a command from its previous value toward <paramref name="raw"/> at no more than
    /// <paramref name="maxRatePerSecond"/> over this update's frame time (VG-20). The first update
    /// after <see cref="Initialize"/> takes <paramref name="raw"/> as it is, so the guidance tone
    /// starts where the guidance says instead of ramping up from level.
    /// </summary>
    private double RateLimited(double previous, double raw, double maxRatePerSecond)
        => hasPreviousCommand ? VisualGuidanceTiming.RateLimit(previous, raw, maxRatePerSecond, frameSeconds) : raw;

    /// <summary>
    /// Updates current guidance phase based on position
    /// </summary>
    private void UpdatePhase(double lat, double lon, double agl, double altMSL)
    {
        if (runway == null) return;

        // Calculate AGL from MSL for consistency with FPM-based vertical guidance
        // SimConnect AGL sensor can be stale/inaccurate when on ground
        double calculatedAGL = altMSL - thresholdElevationMSL;

        // calculatedAGL is the SimConnect reference-datum height; the main gear sits
        // flareAltitudeBiasFt below it in the flare attitude. The touchdown (5 ft) and
        // flare (30 ft) thresholds are true main-gear heights, so test them against the
        // corrected wheel height — otherwise the flare cue fires ~one bias too late and,
        // on a widebody, the datum never gets within 5 ft of the runway so the touchdown
        // phase would never trigger via altitude at all.
        double wheelHeightAGL = calculatedAGL - flareAltitudeBiasFt;

        // Diagnostic logging to help identify sensor issues
        Log.Debug("VisualGuidance", 
            $"AGL: SimConnect={agl:F1}ft, Calculated={calculatedAGL:F1}ft, " +
            $"WheelHeight={wheelHeightAGL:F1}ft, Diff={Math.Abs(agl - calculatedAGL):F1}ft");

        GuidancePhase previousPhase = currentPhase;

        // Check for touchdown first
        if (wheelHeightAGL <= TOUCHDOWN_ALTITUDE_FT)
        {
            currentPhase = GuidancePhase.Touchdown;
            if (previousPhase != currentPhase)
            {
                Say("Touchdown", immediate: false, HOLD_TOUCHDOWN_S);
                // Continue guidance for centerline tracking during rollout
            }
            return;
        }

        // Check for flare. Trigger height is per-aircraft (777 = 40 to match autopilot
        // autoland; A320 = 30 from FCTM) via VisualGuidanceProfile.FlareTriggerWheelHeightFt.
        if (wheelHeightAGL <= flareTriggerWheelHeightFt)
        {
            currentPhase = GuidancePhase.Flare;
            if (previousPhase != currentPhase)
            {
                // Capture entry state
                lastFlarePitch = previousDesiredPitch;  // Start from current guidance pitch
                AnnouncePhaseChange("Flare", HOLD_FLARE_S);
            }
            return;
        }

        // Back above the flare height (a balloon, or a go-around): final again, as before the flare.
        if (currentPhase is GuidancePhase.Flare or GuidancePhase.Touchdown)
        {
            currentPhase = GuidancePhase.Final;
            return;
        }

        // Ctrl+V pressed: decide once the wings are nearly level (in a turn the heading being flown is
        // not yet the one the pilot means to hold).
        if (currentPhase == GuidancePhase.NotStarted)
            currentPhase = GuidancePhase.Deciding;
        if (currentPhase == GuidancePhase.Deciding)
        {
            if (Math.Abs(StandardBank(cachedBank ?? 0.0)) >= VisualGuidanceJoin.DecideBankDeg)
                return;
            Decide(lat, lon);
        }

        if (currentPhase is GuidancePhase.Joining or GuidancePhase.Vectoring)
            UpdateEstablished(lat, lon);
    }

    /// <summary>
    /// The decision (<see cref="VisualGuidanceJoin.Decide"/>): from the position, the ground track
    /// (the heading if there is none) relative to the runway's magnetic course, and the ground speed.
    /// </summary>
    private void Decide(double lat, double lon)
    {
        if (runway == null) return;
        var position = VisualGuidanceJoin.Locate(lat, lon, runway);
        double relativeTrack = VisualGuidanceJoin.Normalize((cachedGroundTrack ?? cachedHeading ?? runway.HeadingMag) - runway.HeadingMag);
        joinPlan = VisualGuidanceJoin.Decide(position, relativeTrack, currentGroundSpeedKnots, CaptureGainForHarness, visualGuidanceProfile);
        activeLegIndex = 0;
        // The simple pattern is vectoring too: BA chooses the route, spoken "Vectoring to final" (decision 2).
        currentPhase = joinPlan.State switch
        {
            VisualGuidanceJoinState.OnFinal => GuidancePhase.Final,
            VisualGuidanceJoinState.Joining => GuidancePhase.Joining,
            _ => GuidancePhase.Vectoring,
        };
        // On final there is no turn to make. A turn already due at this update is not announced either
        // (decision 3 of the stage-4 plan): the state line and the tone say it.
        turnOntoFinalDue = joinPlan.State == VisualGuidanceJoinState.OnFinal;
        turnLineHandled = turnOntoFinalDue;
        decidedThisUpdate = true;
        // It crossed final at the decision, so "Heading does not cross final" is armed from now.
        notCrossingArmed = joinPlan.State == VisualGuidanceJoinState.Joining;
        Log.Debug("VisualGuidance", $"Decided {joinPlan.State} at along {position.AlongNm:F2} nm, cross {position.CrossNm:F2} nm, " +
            $"track {relativeTrack:F1}° from the course, {currentGroundSpeedKnots:F0} kt, clamp {joinPlan.ClampDeg:F0}°");
        var (line, hold) = joinPlan.State switch
        {
            VisualGuidanceJoinState.OnFinal => ("On final", HOLD_ON_FINAL_S),
            VisualGuidanceJoinState.Joining => ("Joining final", HOLD_JOINING_S),
            _ => ("Vectoring to final", HOLD_VECTORING_S),
        };
        Say(line, immediate: false, hold);
    }

    /// <summary>The kind of the leg being flown: final for every plan but the simple pattern's downwind and base.</summary>
    private VisualGuidanceLegKind ActiveLegKind
        => joinPlan is { } plan && activeLegIndex < plan.Legs.Count ? plan.Legs[activeLegIndex].Kind : VisualGuidanceLegKind.Final;

    /// <summary>True on the simple pattern's downwind or base, before the turn onto final.</summary>
    private bool OnPatternLeg => currentPhase == GuidancePhase.Vectoring && ActiveLegKind != VisualGuidanceLegKind.Final;

    /// <summary>
    /// The simple pattern: moves on to the next leg when its turn has come (<see cref="VisualGuidanceJoin.LegIsDue"/>),
    /// more than one if several came at once. The base asks for "Turn base"; final is the turn onto final,
    /// spoken and timed as vectoring's. If both came in one update, only the turn onto final is said.
    /// </summary>
    private void AdvancePatternLeg(double lat, double lon, double relativeTrack, double speedFactor)
    {
        if (runway == null || joinPlan == null) return;
        var position = VisualGuidanceJoin.Locate(lat, lon, runway);
        while (activeLegIndex < joinPlan.Legs.Count - 1)
        {
            var next = joinPlan.Legs[activeLegIndex + 1];
            if (!VisualGuidanceJoin.LegIsDue(next, position, relativeTrack, currentGroundSpeedKnots, CaptureGainForHarness,
                    LATERAL_HEADING_GAIN * speedFactor))
                break;
            activeLegIndex++;
            if (next.Kind == VisualGuidanceLegKind.Base)
                turnBaseLinePending = true;
            else if (next.Kind == VisualGuidanceLegKind.Final)
            {
                turnBaseLinePending = false;
                turnOntoFinalDue = true;
            }
            Log.Debug("VisualGuidance", $"Pattern: {next.Kind} leg at along {position.AlongNm:F2} nm, cross {position.CrossNm:F2} nm, " +
                $"track {relativeTrack:F1}° from the course");
        }
    }

    /// <summary>
    /// Established: on final within <see cref="VisualGuidanceJoin.EstablishedCrossNm"/> and
    /// <see cref="VisualGuidanceJoin.EstablishedTrackDeg"/> of the course (the track as the lateral law
    /// takes it), on the approach side, for <see cref="VisualGuidanceJoin.EstablishedHoldS"/>. Then the
    /// phase is final for the rest of the approach. Only once the final leg is being flown: never on the
    /// pattern's downwind or base.
    /// </summary>
    private void UpdateEstablished(double lat, double lon)
    {
        if (runway == null || cachedHeading is not double heading) return;
        if (ActiveLegKind != VisualGuidanceLegKind.Final)
        {
            establishedSince = null;
            return;
        }
        double cross = SignedCrossTrackNm(runway, lat, lon);
        double trackError = VisualGuidanceJoin.Normalize(LateralTrackDeg(heading) - runway.HeadingMag);
        bool inside = Math.Abs(cross) <= VisualGuidanceJoin.EstablishedCrossNm
            && Math.Abs(trackError) <= VisualGuidanceJoin.EstablishedTrackDeg
            && VisualGuidanceJoin.Locate(lat, lon, runway).AlongNm > 0;
        if (!inside)
        {
            establishedSince = null;
            return;
        }
        DateTime now = lastUpdateUtc ?? ClockForHarness();
        establishedSince ??= now;
        if ((now - establishedSince.Value).TotalSeconds >= VisualGuidanceJoin.EstablishedHoldS - 1e-6)
        {
            currentPhase = GuidancePhase.Final;
            Say("Established on final", immediate: false, HOLD_ESTABLISHED_S);
        }
    }

    /// <summary>
    /// The track the lateral law steers (magnetic): the ground track, so the aircraft holds the line in a
    /// crosswind with its crab, but the heading below 100 ft, to line the nose up with the runway.
    /// </summary>
    private double LateralTrackDeg(double heading)
        => cachedAGL.HasValue && cachedAGL.Value <= 100.0 ? heading : cachedGroundTrack ?? heading;

    /// <summary>
    /// The bank the guidance asks for, right positive. Deciding: the aircraft's own bank. Joining before
    /// the turn onto final: wings level, the heading held (VG-23). The simple pattern's downwind and base:
    /// the capture law onto that leg. Otherwise, onto and along final: the precision track within
    /// <see cref="ARC_MODE_EXIT_NM"/> of the centreline, the capture law outside it.
    /// </summary>
    private double CalculateDesiredBank(double lat, double lon, double heading)
    {
        if (runway == null) return 0.0;

        {
            // Signed as the lateral law takes it: + right of the centreline facing the runway.
            double signedCrossTrackNM = SignedCrossTrackNm(runway, lat, lon);

            // Cross-track rate (derivative term) for damping, over a time window (oldest to newest
            // sample in the last CROSS_TRACK_RATE_WINDOW_S seconds). This filters out single-frame
            // noise that caused 0.000 ↔ 0.020 oscillation. No rate yet (one sample, or under
            // 0.01 s between them): 0 this update, and the smoothed rate is left as it was.
            crossTrackRateWindow.Add(lastUpdateUtc ?? ClockForHarness(), signedCrossTrackNM);
            double crossTrackRate = 0.0;
            if (crossTrackRateWindow.Rate is double rawCrossTrackRate)
            {
                // Exponential smoothing per second to reduce noise (prevents oscillation during phase handoff)
                crossTrackRate = VisualGuidanceTiming.Smooth(smoothedCrossTrackRate, rawCrossTrackRate,
                    CROSS_TRACK_RATE_SMOOTHING_PER_SECOND, frameSeconds);
                smoothedCrossTrackRate = crossTrackRate;
            }

            // The ground track holds the centreline in a crosswind (with the crab); below 100 ft the
            // heading lines the nose up with the runway (the decrab).
            double actualTrackAngle = LateralTrackDeg(heading);
            double trackAngleError = NormalizeHeading(runway.HeadingMag - actualTrackAngle);
            double relativeTrack = VisualGuidanceJoin.Normalize(actualTrackAngle - runway.HeadingMag);

            // Airspeed compensation scaling
            // Higher speeds need stronger corrections (compensates for larger turn radius)
            double speedFactor = Math.Sqrt(Math.Max(currentGroundSpeedKnots, 80.0) / airspeedReferenceKnots);

            double absXTE = Math.Abs(signedCrossTrackNM);
            double desiredBank;
            string guidancePhase;

            // Vectoring: the pattern's legs move on as their turns come; once on the final leg, note when the
            // turn onto final comes (the countdown and its line). On the downwind and the base the final
            // leg's cue is not checked: a far-side start can begin closer to final than the cue.
            if (currentPhase == GuidancePhase.Vectoring)
            {
                if (OnPatternLeg)
                    AdvancePatternLeg(lat, lon, relativeTrack, speedFactor);
                if (!OnPatternLeg)
                    TurnOntoFinalIsDue(lat, lon, signedCrossTrackNM, relativeTrack, speedFactor);
            }

            if (currentPhase == GuidancePhase.Deciding)
            {
                // Pressed in a turn: both tones sit on the aircraft's own bank, with no rate limit, until
                // the wings are nearly level and BA decides.
                guidancePhase = "DECIDING";
                desiredBank = StandardBank(cachedBank ?? 0.0);
            }
            else if (OnPatternLeg && joinPlan != null)
            {
                // ========== THE SIMPLE PATTERN: DOWNWIND OR BASE ==========
                // The same capture law onto the leg, with the pattern's 90° limit; past 170° off the leg the
                // turn is away from the runway (VisualGuidanceJoin.LegHeadingErrorDeg). The pattern mainly
                // serves IFR handovers today ("cleared visual approach" from the far side of the field);
                // full VFR circuits come with the general aviation work (the redesign's piece 7).
                var leg = joinPlan.Legs[activeLegIndex];
                guidancePhase = leg.Kind == VisualGuidanceLegKind.Downwind ? "PATTERN_DOWNWIND" : "PATTERN_BASE";
                double headingError = VisualGuidanceJoin.LegHeadingErrorDeg(leg, VisualGuidanceJoin.Locate(lat, lon, runway),
                    relativeTrack, currentGroundSpeedKnots, CaptureGainForHarness);
                double rawDesiredBank = RateLimited(previousDesiredBank, headingError * LATERAL_HEADING_GAIN * speedFactor,
                    maxBankRateDegPerSec);
                desiredBank = Math.Clamp(rawDesiredBank, -maxApproachBankDeg, maxApproachBankDeg);
                Log.Debug("VisualGuidance", $"{guidancePhase}: XTE={signedCrossTrackNM:F3}NM, RelTrk={relativeTrack:F1}°, " +
                    $"HdgErr={headingError:F1}°, Bank={desiredBank:F1}°, GS={currentGroundSpeedKnots:F0}kt");
            }
            else if (currentPhase == GuidancePhase.Joining
                     && !TurnOntoFinalIsDue(lat, lon, signedCrossTrackNM, relativeTrack, speedFactor))
            {
                // Wings level, the heading held: the heading ATC (or the pilot) chose, as FlyByWire's
                // autopilot holds its heading until LOC* engages; the capture law runs only to find the turn
                // onto final (VG-23). Flying its small early bank would let a pilot who follows it shrink
                // their own intercept and keep putting the turn off. Level at once, not at the rate limit:
                // after a press in a turn the command was the aircraft's own bank, and following it down
                // would ask a pilot rolling out for more bank than they have.
                guidancePhase = "HOLD_HEADING";
                desiredBank = 0.0;
            }
            else if (absXTE > ARC_MODE_EXIT_NM)  // 0.05 NM
            {
                // ========== CAPTURE ==========
                // The capture law onto final (VisualGuidanceJoin): a desired track of gain × offset ÷
                // ground speed toward the line, damped by the closing rate, clamped to the intercept
                // (on final 30°; vectoring the chosen angle; joining the one flown at the turn, at least
                // 30°), so the intercept shrinks to nothing as the aircraft reaches the line.
                guidancePhase = "CAPTURE";
                double clamp = FinalClampDeg(signedCrossTrackNM, relativeTrack);
                double desiredTrack = VisualGuidanceJoin.DesiredLegTrackDeg(signedCrossTrackNM, crossTrackRate,
                    currentGroundSpeedKnots, clamp, CaptureGainForHarness);

                // Simple heading controller onto the desired track
                double headingError = VisualGuidanceJoin.Normalize(desiredTrack - relativeTrack);
                double rawDesiredBank = headingError * LATERAL_HEADING_GAIN * speedFactor;

                // Apply bank rate limiting (per second of frame time, VG-20)
                rawDesiredBank = RateLimited(previousDesiredBank, rawDesiredBank, maxBankRateDegPerSec);

                // The profile's largest approach bank
                desiredBank = Math.Clamp(rawDesiredBank, -maxApproachBankDeg, maxApproachBankDeg);

                Log.Debug("VisualGuidance",
                    $"{guidancePhase}: XTE={signedCrossTrackNM:F3}NM, Rate={crossTrackRate:F3}NM/s, Clamp={clamp:F1}°, " +
                    $"DesTrk={desiredTrack:F1}°, RelTrk={relativeTrack:F1}°, HdgErr={headingError:F1}°, " +
                    $"Bank={desiredBank:F1}°, GS={currentGroundSpeedKnots:F0}kt");
            }
            else
            {
                // ========== PRECISION TRACK MODE (< 0.05 NM) ==========
                // Simplified tracking for final centerline hold
                // Arc mode should deliver aircraft very close to centerline on heading
                // This mode just holds position with minimal corrections

                guidancePhase = "PRECISION_TRACK";

                // PDH controller for final precision
                // Arc mode delivers aircraft at 0.05 NM with ~1.5° heading error
                // Both XTE and heading errors are tiny, so P and H cooperate (no fighting!)
                double proportionalTerm = -signedCrossTrackNM * LATERAL_GAIN_TRACKING * speedFactor;
                double derivativeTerm = -crossTrackRate * LATERAL_RATE_DAMPING * speedFactor;

                // Heading term at FULL strength - prevents wrong-direction turns
                double headingTerm = trackAngleError * LATERAL_HEADING_GAIN * speedFactor;

                double rawDesiredBank = proportionalTerm + derivativeTerm + headingTerm;

                // Apply bank rate limiting (per second of frame time, VG-20)
                rawDesiredBank = RateLimited(previousDesiredBank, rawDesiredBank, maxBankRateDegPerSec);

                // Tight bank limit for precision
                desiredBank = Math.Clamp(rawDesiredBank, -10.0, 10.0);

                // Calculate distance to threshold for debugging
                double distanceNM = NavigationCalculator.CalculateDistance(
                    lat, lon, runway.StartLat, runway.StartLon);

                // Debug output for precision mode
                Log.Debug("VisualGuidance", 
                    $"{guidancePhase}: XTE={signedCrossTrackNM:F3}NM, Rate={crossTrackRate:F3}NM/s, " +
                    $"TrkErr={trackAngleError:F1}°, P={proportionalTerm:F2}° D={derivativeTerm:F2}° H={headingTerm:F2}° " +
                    $"→ Bank={desiredBank:F1}°, Dist={distanceNM:F1}NM, GS={currentGroundSpeedKnots:F0}kt");
            }

            // Update for next iteration
            previousDesiredBank = desiredBank;

            // Magnetic reference debugging
            double impliedMagVar = cachedGroundTrack.HasValue ?
                NormalizeHeading(cachedGroundTrack.Value - heading) : 0.0;

            Log.Debug("VisualGuidance", 
                $"MAGNETIC DEBUG: RunwayMag={runway.HeadingMag:F1}° AircraftHdg={heading:F1}° " +
                $"GroundTrack={cachedGroundTrack?.ToString("F1") ?? "N/A"}° ActualTrack={actualTrackAngle:F1}° " +
                $"TrackErr={trackAngleError:F1}° ImpliedMagVar={impliedMagVar:F1}°");

            if (guidancePhase is "DECIDING" or "HOLD_HEADING")
                Log.Debug("VisualGuidance", $"{guidancePhase}: XTE={signedCrossTrackNM:F3}NM, RelTrk={relativeTrack:F1}° → Bank={desiredBank:F1}°");

            return desiredBank;
        }
    }

    /// <summary>
    /// Whether the turn onto final has come: the capture law, clamped to the intercept (joining: the one
    /// being flown, while the track crosses final; vectoring: the chosen angle), first asks
    /// <see cref="VisualGuidanceJoin.TurnCueBankDeg"/> of bank toward final
    /// (<see cref="VisualGuidanceJoin.TurnCueCrossNm"/>); or the aircraft is within
    /// <see cref="ARC_MODE_EXIT_NM"/> of the line. Once due it stays due, and joining freezes the
    /// intercept it was flying, which the capture law then keeps as its limit (at least 30°).
    /// </summary>
    private bool TurnOntoFinalIsDue(double lat, double lon, double cross, double relativeTrack, double speedFactor)
    {
        if (turnOntoFinalDue) return true;
        if (runway == null || joinPlan == null) return false;
        bool joining = joinPlan.State == VisualGuidanceJoinState.Joining;
        double liveIntercept = VisualGuidanceJoin.InterceptDeg(cross, relativeTrack);
        double clamp = joinPlan.ClampDeg;
        if (joining)
        {
            // The intercept being flown, as long as the track still meets final ahead of the threshold,
            // even inside the minimum final (that only decides "Heading does not cross final"); parallel
            // or diverging, no cue until the aircraft reaches the line.
            var position = new VisualGuidanceApproachPoint(VisualGuidanceJoin.Locate(lat, lon, runway).AlongNm, cross);
            clamp = VisualGuidanceJoin.MeetsFinalAhead(position, relativeTrack) ? liveIntercept : 0.0;
        }
        bool due = (clamp > 0 && Math.Abs(cross) <= VisualGuidanceJoin.TurnCueCrossNm(clamp, currentGroundSpeedKnots,
                       CaptureGainForHarness, LATERAL_HEADING_GAIN * speedFactor))
                   || Math.Abs(cross) <= ARC_MODE_EXIT_NM;
        if (!due) return false;
        turnOntoFinalDue = true;
        if (joining) frozenInterceptDeg = Math.Max(0.0, liveIntercept);
        Log.Debug("VisualGuidance", $"Turn onto final due: XTE={cross:F3}NM, clamp={clamp:F1}°, intercept={liveIntercept:F1}°");
        return true;
    }

    /// <summary>What F and the countdown know about the join, from the latest position and track.</summary>
    private readonly record struct JoinPicture(double InterceptDeg, bool CrossesFinal, double? SecondsToTurn,
        double JoinAlongNm, bool FinalToTheRight);

    private double SpeedFactor() => Math.Sqrt(Math.Max(currentGroundSpeedKnots, 80.0) / airspeedReferenceKnots);

    /// <summary>
    /// The join from here, for the heading and speed flown now: the intercept toward final; whether the
    /// track crosses final (joining; vectoring always "does"); the time to the turn onto final
    /// (<see cref="VisualGuidanceJoin.SecondsToTurn"/>, with the clamp the turn cue uses: the intercept
    /// flown when joining, the chosen angle when vectoring; none on the pattern's downwind); where the
    /// aircraft will be lined up on final (the live intercept when joining, the chosen angle when
    /// vectoring, the sum that placed the base for the pattern); and the side of the track the nearest
    /// point of final lies on (the threshold, when past it). Null before a decision.
    /// </summary>
    private JoinPicture? PictureOfTheJoin(double lat, double lon, double cross, bool withJoinAlong = true)
    {
        if (runway == null || joinPlan == null || cachedHeading is not double heading) return null;
        double along = VisualGuidanceJoin.Locate(lat, lon, runway).AlongNm;
        double relativeTrack = VisualGuidanceJoin.Normalize(LateralTrackDeg(heading) - runway.HeadingMag);
        double intercept = VisualGuidanceJoin.InterceptDeg(cross, relativeTrack);
        bool joining = joinPlan.State == VisualGuidanceJoinState.Joining;
        var position = new VisualGuidanceApproachPoint(along, cross);
        bool crosses = !joining || VisualGuidanceJoin.CrossesFinal(position, relativeTrack, minimumFinalNm, out _);
        // The turn (and so its countdown) comes whenever the track meets final ahead of the threshold,
        // even when that is inside the minimum final and "crosses" is false for the spoken line. On the
        // pattern's downwind the base comes first, so there is no time to the turn onto final yet.
        bool pattern = joinPlan.State == VisualGuidanceJoinState.Pattern;
        bool turnWillCome = joining ? VisualGuidanceJoin.MeetsFinalAhead(position, relativeTrack)
            : ActiveLegKind != VisualGuidanceLegKind.Downwind;
        double clamp = joining ? intercept : joinPlan.ClampDeg;
        // Vectoring: once the aircraft converges at all, the time is on the angle BA chose, which the
        // guidance is turning it onto; on the few degrees it has so far the time would read far too long
        // (and a countdown started on it would come late). Heading away or parallel: no time yet.
        double timedIntercept = joining ? intercept
            : intercept >= VisualGuidanceJoin.MinTimedInterceptDeg ? joinPlan.ClampDeg : 0.0;
        double? seconds = turnWillCome && !turnOntoFinalDue
            ? VisualGuidanceJoin.SecondsToTurn(cross, timedIntercept, clamp, currentGroundSpeedKnots, CaptureGainForHarness,
                LATERAL_HEADING_GAIN * SpeedFactor())
            : null;
        // Where it lines up, counting the turn onto the cut when vectoring, as the decision did (joining
        // flies its own cut, so there is no turn to count), and for the pattern the sum that placed its
        // base. Only F needs it; the countdown skips it.
        double joinAlong = !withJoinAlong ? double.NaN
            : pattern ? VisualGuidanceJoin.PatternJoinAlongNm(joinPlan, currentGroundSpeedKnots, CaptureGainForHarness, visualGuidanceProfile)
            : VisualGuidanceJoin.JoinAlongNm(position, relativeTrack,
                joining ? intercept : joinPlan.ClampDeg, currentGroundSpeedKnots, CaptureGainForHarness, visualGuidanceProfile);
        // Toward the nearest point of final, in (inbound, right) components, against the track's right normal.
        double toX = along > 0 ? 0 : along, toY = -cross;
        double track = relativeTrack * Math.PI / 180;
        bool toTheRight = toX * -Math.Sin(track) + toY * Math.Cos(track) > 0;
        return new JoinPicture(intercept, crosses, seconds, joinAlong, toTheRight);
    }

    /// <summary>
    /// The join's own lines, queued, each holding the bank callouts for its measured time (VG-21): the
    /// pattern's "Turn base" when its base turn comes; "Turn onto final" when the turn comes (neither when
    /// it was already due at the decision); the countdown at 30 and 15 s before it (on the pattern, while on
    /// the base), each once and only if the turn was further off than that when its time was first known;
    /// and, while joining before the turn, "Heading does not cross final" once the track has not crossed
    /// final for <see cref="VisualGuidanceJoin.NotCrossingHoldS"/>, again only after it has crossed for as
    /// long and stopped again. Joining never becomes vectoring by itself.
    /// </summary>
    private void HandleJoinCallouts(double lat, double lon)
    {
        if (runway == null || joinPlan == null) return;
        if (currentPhase is not (GuidancePhase.Joining or GuidancePhase.Vectoring)) return;

        if (turnBaseLinePending)
        {
            turnBaseLinePending = false;
            if (!decidedThisUpdate)
                Say("Turn base", immediate: false, HOLD_TURN_BASE_S);
        }

        if (turnOntoFinalDue)
        {
            if (!turnLineHandled)
            {
                turnLineHandled = true;
                if (!decidedThisUpdate)
                    Say("Turn onto final", immediate: false, HOLD_TURN_ONTO_FINAL_S);
            }
            return;
        }

        double cross = SignedCrossTrackNm(runway, lat, lon);
        if (PictureOfTheJoin(lat, lon, cross, withJoinAlong: false) is not JoinPicture picture) return;
        DateTime now = lastUpdateUtc ?? ClockForHarness();

        if (picture.SecondsToTurn is double seconds)
        {
            firstSecondsToTurn ??= seconds;
            if (!countdown15Said && firstSecondsToTurn > 15 && seconds <= 15)
            {
                countdown15Said = countdown30Said = true;
                Say("Turn onto final in 15 seconds", immediate: false, HOLD_COUNTDOWN_15_S);
            }
            else if (!countdown30Said && firstSecondsToTurn > 30 && seconds <= 30)
            {
                countdown30Said = true;
                Say("Turn onto final in 30 seconds", immediate: false, HOLD_COUNTDOWN_30_S);
            }
        }

        if (joinPlan.State != VisualGuidanceJoinState.Joining) return;
        if (picture.CrossesFinal)
        {
            notCrossingSince = null;
            crossingSince ??= now;
            if ((now - crossingSince.Value).TotalSeconds >= VisualGuidanceJoin.NotCrossingHoldS - 1e-6)
                notCrossingArmed = true;
        }
        else
        {
            crossingSince = null;
            notCrossingSince ??= now;
            if (notCrossingArmed && (now - notCrossingSince.Value).TotalSeconds >= VisualGuidanceJoin.NotCrossingHoldS - 1e-6)
            {
                notCrossingArmed = false;
                Say("Heading does not cross final", immediate: false, HOLD_NOT_CROSSING_S);
            }
        }
    }

    /// <summary>
    /// The capture law's intercept limit onto final: on final 30°, vectoring the chosen angle, joining the
    /// intercept flown at the turn (at least 30°, so a shallow join can still correct). Before a decision
    /// (pressed in the flare) 30°.
    /// </summary>
    private double FinalClampDeg(double cross, double relativeTrack)
    {
        if (joinPlan == null) return VisualGuidanceJoin.OnFinalClampDeg;
        if (joinPlan.State != VisualGuidanceJoinState.Joining) return joinPlan.ClampDeg;
        double intercept = turnOntoFinalDue ? frozenInterceptDeg : VisualGuidanceJoin.InterceptDeg(cross, relativeTrack);
        return Math.Max(intercept, VisualGuidanceJoin.PostTurnMinClampDeg);
    }

    /// <summary>
    /// Calculates desired pitch angle for vertical guidance
    /// </summary>
    private double CalculateDesiredPitch(double lat, double lon, double agl, double altMSL)
    {
        Log.Debug("VisualGuidance", $"CalculateDesiredPitch called: Phase={currentPhase}, AGL={agl:F0}ft, Runway={(runway != null ? "OK" : "NULL")}, GS={currentGroundSpeedKnots:F1}kt, VS={currentVerticalSpeedFPM:F0}fpm, CurPitch={currentPitch:F2}°");

        if (runway == null)
        {
            Log.Debug("VisualGuidance", "Runway is NULL - returning 0.0");
            return 0.0;
        }

        if (currentPhase == GuidancePhase.Flare)
        {
            // Simplified flare: Command constant +6° pitch
            double targetPitch = flareTargetPitchDeg;

            // Apply rate limiting for smooth transition from approach to flare (per second of frame time, VG-20)
            targetPitch = RateLimited(lastFlarePitch, targetPitch, MAX_FLARE_PITCH_RATE);

            lastFlarePitch = targetPitch;

            Log.Debug("VisualGuidance", 
                $"Flare: AGL={agl:F0}ft, Commanding pitch: {targetPitch:F1}°");

            return targetPitch;
        }
        else if (currentPhase != GuidancePhase.NotStarted && currentPhase != GuidancePhase.Touchdown)
        {
            // New glideslope-based vertical guidance - enforces 3° glideslope to threshold

            // Calculate direct distance to threshold
            double distanceToThresholdNM = NavigationCalculator.CalculateDistance(
                lat, lon, runway.StartLat, runway.StartLon);

            // Get current AGL relative to threshold
            double currentAGL = altMSL - thresholdElevationMSL;

            // Calculate ideal altitude on 3° glideslope.
            // A bare distance×tan(3°) path crosses the threshold at 0 ft — but a real ILS
            // glideslope crosses it at the ~50 ft reference datum height (TCH), and VG's
            // altitude is the SimConnect reference datum, which sits above the GS antenna.
            // glideslopeAltitudeBiasFt (per-aircraft, see VisualGuidanceProfile) folds both
            // terms in, so VG's "on glideslope" coincides with the real ILS path the
            // autopilot flies — instead of a path one TCH too low, which previously biased
            // the commanded vertical speed into commanding excess descent.
            double distanceFt = distanceToThresholdNM * 6076.12;
            double glideslopeAngleRad = glideslopeAngleDeg * Math.PI / 180.0;
            double idealAltitudeAGL = distanceFt * Math.Tan(glideslopeAngleRad) + glideslopeAltitudeBiasFt;

            // Calculate altitude error (positive = too high, negative = too low)
            double altitudeError = currentAGL - idealAltitudeAGL;

            // Apply exponential smoothing (per second, VG-20) to current FPM to reduce oscillation
            double smoothedFPM = VisualGuidanceTiming.Smooth(smoothedCurrentFPM, currentVerticalSpeedFPM,
                FPM_SMOOTHING_FACTOR_PER_SECOND, frameSeconds);
            smoothedCurrentFPM = smoothedFPM;

            // Calculate natural 3° descent rate based on groundspeed
            // FPM = groundspeed (knots) × tan(angle) × 101.269 (conversion factor)
            double natural3DegDescentRateFPM = -currentGroundSpeedKnots * Math.Tan(glideslopeAngleRad) * 101.269;

            double targetFPM;

            // Check if we're within 0.5 NM of threshold - lock to steady 3° angle
            if (distanceToThresholdNM <= GLIDESLOPE_LOCK_DISTANCE_NM)
            {
                // LOCK MODE: Maintain steady 3° descent rate, no altitude corrections
                targetFPM = natural3DegDescentRateFPM;

                Log.Debug("VisualGuidance", 
                    $"LOCK MODE: Dist={distanceToThresholdNM:F2}nm, " +
                    $"Maintaining steady 3° descent: {targetFPM:F0}fpm");
            }
            else
            {
                // CORRECTION MODE: Proportional control on glideslope deviation
                // Correction is proportional to altitude error, not distance/time dependent
                // This provides smooth, natural convergence to the glideslope
                double correctionRateFPM = -altitudeError * GLIDESLOPE_GAIN;
                targetFPM = natural3DegDescentRateFPM + correctionRateFPM;

                // Apply safety limits (never command climb, limit max descent). The clamp is
                // dynamic: -1500 fpm is the typical-3°-approach safety ceiling, but at steep
                // approaches (Aspen 6.59°, London City 5.5°) the natural rate can approach or
                // exceed it. Allow up to 1.3× the natural rate (whichever is more negative)
                // so the controller has headroom for catch-up corrections without clipping
                // the legitimate per-runway descent profile.
                double effectiveMaxDescent = Math.Min(MAX_DESCENT_RATE_FPM, natural3DegDescentRateFPM * 1.3);
                targetFPM = Math.Clamp(targetFPM, effectiveMaxDescent, 0.0);

                Log.Debug("VisualGuidance", 
                    $"CORRECTION MODE: AltErr={altitudeError:F0}ft, " +
                    $"Natural={natural3DegDescentRateFPM:F0}fpm, Correction={correctionRateFPM:F0}fpm, " +
                    $"Target={targetFPM:F0}fpm, Dist={distanceToThresholdNM:F2}nm");
            }

            // Store for hotkey announcement
            lastCalculatedTargetFPM = targetFPM;
            lastCalculatedAltitudeError = altitudeError;

            // Calculate FPM error (negative = need to descend more, positive = need to descend less)
            double fpmError = targetFPM - smoothedFPM;

            // FPM error rate for the derivative term, over FPM_ERROR_RATE_WINDOW_S (the design's
            // interval between updates), not frame to frame: a per-frame difference divided by
            // ~0.03 s magnified jitter about thirty times (VG-20). 0 until the window has a rate.
            fpmErrorRateWindow.Add(lastUpdateUtc ?? ClockForHarness(), fpmError);
            double fpmErrorRate = fpmErrorRateWindow.Rate ?? 0.0;

            // Calculate nominal pitch for descent.
            // Prefer LIVE angle of attack (SimConnect INCIDENCE ALPHA) over the static profile
            // estimate: measured AoA inherently encodes weight + flap + speed, so the nominal
            // converges on whatever the airplane is actually trimmed for. Fall back to the
            // profile value when (a) we haven't received an AoA sample yet, or (b) the reading
            // is outside the sanity band (sim glitches, on-ground oddities).
            // Smooth the AoA with an EMA to suppress gust-driven jitter that would otherwise
            // modulate the desired-tone frequency around the true trimmed value.
            double effectiveAoaDeg;
            if (cachedAoaDeg.HasValue &&
                cachedAoaDeg.Value >= AOA_MIN_VALID_DEG &&
                cachedAoaDeg.Value <= AOA_MAX_VALID_DEG)
            {
                smoothedAoaDeg = VisualGuidanceTiming.Smooth(smoothedAoaDeg, cachedAoaDeg.Value,
                    AOA_SMOOTHING_FACTOR_PER_SECOND, frameSeconds);
                effectiveAoaDeg = smoothedAoaDeg.Value;
            }
            else
            {
                // No valid measurement — use the per-aircraft estimate. Do NOT update
                // smoothedAoaDeg; we want it to resume from the last good value when
                // measurements come back.
                effectiveAoaDeg = typicalApproachAoaDeg;
            }
            double nominalPitch = -glideslopeAngleDeg + effectiveAoaDeg;  // A320 default ≈ +3°; 777 ≈ +1.5°; LCY steep ≈ +0.5°

            // PD controller on FPM error.
            // Sign convention (verified): fpmError = targetFPM − actualFPM. Negative fpmError
            // means actualFPM is more positive than target — the airplane is descending less
            // than the target rate — so we need to descend MORE, which (with autothrust holding
            // airspeed) means LESS pitch, i.e. a NEGATIVE correction. So proportionalTerm has
            // the SAME sign as fpmError: positive coefficient on fpmError. Same logic for the
            // derivative term: if fpmError is becoming more negative (we're falling further
            // behind on descent), the rate is negative, and we want the correction to push
            // pitch further negative — positive coefficient on the rate.
            //
            // The previous implementation had `-fpmError * gain` and `-fpmErrorRate * gain`,
            // which produced wrong-direction guidance: when the airplane was HIGH on glideslope
            // VG commanded MORE pitch (less descent — staying high). With autopilot active
            // fpmError stays near zero and the wrong-direction correction was negligible, so
            // the bug never manifested in autopilot tests; in manual flying it would steer the
            // pilot away from the glideslope.
            double proportionalTerm = fpmError * FPM_P_GAIN;
            double derivativeTerm = fpmErrorRate * FPM_D_GAIN;

            double rawDesiredPitch = nominalPitch + proportionalTerm + derivativeTerm;

            // Pitch rate limiting - prevent sudden tone frequency jumps (per second of frame time, VG-20)
            rawDesiredPitch = RateLimited(previousDesiredPitch, rawDesiredPitch, maxPitchRateDegPerSec);

            // Clamp final command
            double desiredPitch = Math.Clamp(rawDesiredPitch, -12.0, 12.0);

            // Deciding, joining and the pattern's downwind and base: the altitude is the pilot's or ATC's, so
            // the pitch tone sits on the aircraft's own pitch (the redesign's piece 4, Waiting), taken without
            // a rate limit; on the downwind and base the law would aim at a 3° path far below and ask for a
            // dive, until stage 5 plans the pattern's descent. The law above still runs, so its filters stay
            // warm and F's numbers current, and it starts from the aircraft's pitch when final begins.
            if (currentPhase is GuidancePhase.Deciding or GuidancePhase.Joining || OnPatternLeg)
                desiredPitch = currentPitch;

            // Update for next iteration
            previousDesiredPitch = desiredPitch;

            Log.Debug("VisualGuidance", 
                $"Vertical: Target={targetFPM:F0}fpm, Current={smoothedFPM:F0}fpm, Error={fpmError:F0}fpm, " +
                $"AGL={currentAGL:F0}ft, IdealAGL={idealAltitudeAGL:F0}ft, AltErr={altitudeError:F0}ft, " +
                $"Dist={distanceToThresholdNM:F2}nm, GS={currentGroundSpeedKnots:F0}kt, " +
                $"P={proportionalTerm:F2}° D={derivativeTerm:F2}° → Pitch={desiredPitch:F1}°");

            return desiredPitch;
        }
        else
        {
            // Level flight or slight descent
            return 0.0;
        }
    }

    /// <summary>
    /// Normalizes heading difference to -180 to +180 range
    /// </summary>
    private double NormalizeHeading(double heading)
    {
        while (heading > 180.0) heading -= 360.0;
        while (heading < -180.0) heading += 360.0;
        return heading;
    }

    /// <summary>
    /// Announces phase changes; <paramref name="holdSeconds"/> is the line's measured speaking time (VG-21).
    /// </summary>
    private void AnnouncePhaseChange(string message, double holdSeconds)
    {
        var now = ClockForHarness();
        if ((now - lastPhaseAnnouncement).TotalMilliseconds >= ANNOUNCEMENT_INTERVAL_MS)
        {
            Say(message, immediate: false, holdSeconds);
            lastPhaseAnnouncement = now;
        }
    }

    /// <summary>
    /// Handles distance callouts (5, 3, 1 miles)
    /// </summary>
    private void HandleDistanceCallouts(double lat, double lon)
    {
        if (runway == null) return;

        var now = ClockForHarness();
        double distanceNM = NavigationCalculator.CalculateDistance(
            lat, lon, runway.StartLat, runway.StartLon);

        // A mark is said when the distance comes down through it, once, and armed again only after the
        // distance has gone back above it by MILE_CALL_REARM_NM (a pattern flown out past it and back in).
        // Pressed inside a mark, it is not armed: saying "5 miles out" at 0.3 nm, then "3 miles out" and
        // "1 mile out" a second apart, was wrong, and the queued lines would hold the bank callouts for
        // their whole length.
        if (!distanceCalloutsPrimed)
        {
            distanceCalloutsPrimed = true;
            foreach (var (mark, _, _) in MileCallouts)
                if (distanceNM > mark) armedMileMarks.Add(mark);
            return;
        }
        foreach (var (mark, _, _) in MileCallouts)
            if (distanceNM > mark + MILE_CALL_REARM_NM) armedMileMarks.Add(mark);

        // On the pattern's downwind and base the distance is not a distance out on final (a far-side
        // downwind passes 3 nm from the threshold flying away from it): a mark passed there is not said,
        // and is said on final only once the pattern has taken the aircraft back out past it.
        if (OnPatternLeg)
        {
            armedMileMarks.RemoveWhere(m => distanceNM <= m);
            return;
        }

        if ((now - lastDistanceAnnouncement).TotalMilliseconds < ANNOUNCEMENT_INTERVAL_MS)
            return;

        // The nearest mark passed, if several came at once (a slew): the others are passed too.
        foreach (var (mark, line, hold) in Enumerable.Reverse(MileCallouts))
        {
            if (distanceNM <= mark && armedMileMarks.Contains(mark))
            {
                Say(line, immediate: false, hold);
                armedMileMarks.RemoveWhere(m => m >= mark && distanceNM <= m);
                lastDistanceAnnouncement = now;
                break;
            }
        }
    }

    // Announce at 5, 3, 1 miles: "5 miles out" (stage 5 of the redesign adds the path once established).
    private static readonly (double Nm, string Line, double Hold)[] MileCallouts =
    {
        (5.0, "5 miles out", HOLD_5_MILES_S), (3.0, "3 miles out", HOLD_3_MILES_S), (1.0, "1 mile out", HOLD_1_MILE_S),
    };
    private const double MILE_CALL_REARM_NM = 0.3;

    /// <summary>
    /// Speaks the bank error ("3 right", "matched") when <see cref="VisualGuidanceBankCalloutGate"/>
    /// says one is due (VG-21). The gate sees every update, including those inside a manual-query
    /// grace window, where it may not speak (VG-19): when the window ends, a callout that came due
    /// during it speaks the error as it is then, at once, not timed again from zero.
    /// </summary>
    private void AnnounceBankGuidance(double desiredBankDegrees)
    {
        // No actual bank yet: there is no error to speak.
        if (!cachedBank.HasValue)
            return;

        // Both banks right-positive: the command is, SimConnect's bank goes through StandardBank (VG-7).
        // Deciding: the command is the aircraft's own bank, so there is nothing to call. Joining before the
        // turn onto final, banked 5° or more: the pilot is turning on purpose (a new heading from ATC), and
        // BA holds whatever heading they fly, so it does not tell them to roll back (stage-4 checkpoint
        // review). The tones still show the bank, and the gate keeps tracking.
        double bankNow = StandardBank(cachedBank.Value);
        bool deliberateTurn = currentPhase == GuidancePhase.Joining && !turnOntoFinalDue
            && Math.Abs(bankNow) >= VisualGuidanceJoin.DecideBankDeg;
        string? callout = bankCalloutGate.Next(ClockForHarness(), desiredBankDegrees, bankNow,
            maySpeak: !RoutineAnnouncementsSuppressed && currentPhase != GuidancePhase.Deciding && !deliberateTurn);
        if (callout != null)
            Say(callout, immediate: true, holdSeconds: 0);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

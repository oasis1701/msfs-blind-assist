using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Manual-landing flare + rollout assist ("manual landing" checkbox in the
/// destination-runway dialog, unchecked by default), and the rollout centerline cue
/// on its own for every other landing with a destination runway or landing exit set
/// (ROLLOUT ONLY below).
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
///  • ROLLOUT ONLY: armed with no flare phase (Arm's includeFlare false) when a
///    destination runway or landing exit is set without the checkbox
///    (Navigation.RolloutCueArming), so an AUTOLAND gets the same rollout cue: no
///    tones on the approach, and the rollout engages at touchdown after flying above
///    80 ft, on a runway of the armed airport at 50 kt or more (ShouldEngageRolloutOnly, decided while the
///    aircraft is still at that speed, not on one frame). KSEA 16R 2026-10-15: an A300 autoland
///    held the centerline to 96 kt, then yawed off the runway with no cue at all,
///    because the cue existed only behind the checkbox.
///  • SHARING: while taxi guidance's landing rollout steers at an exit, the two
///    take turns on the tone by OWNERSHIP, never by a speed: this cue owns the
///    runway (exit guidance's Centerline mode, which pauses its own tone) and stays
///    engaged at volume 0 while exit guidance owns the tone for the exit itself
///    (ExitGuidanceOwnsRolloutTone), so the two never sound together and the cue
///    comes back when exit guidance retargets downfield. Visual guidance, still on
///    for up to a second after touchdown, holds the tone and the "Rollout guidance"
///    sentence the same way until it switches itself off (DecideRolloutToneOwner).
///    With no exit to steer at (no landing rollout, or the runway-end countdown) a
///    turn off the runway at exit speed mutes the cue (IsTurningOffRunway), so it
///    never pans against a deliberate turn-off or turnaround.
///  • HANDOFF: during a landing rollout in taxi guidance the tone stops only at a
///    stop (silently) or by the takeover below; with none running it stops below
///    taxi-ish speed or once laterally clear of the runway — "Rollout guidance
///    complete", QUEUED when taxi guidance ran during the rollout so it never
///    cuts taxi guidance off. (2026-10-15 retired the 55 kt end, which left 55-50 kt
///    with no tone, and the 20° heading turn-off, which ended the cue mid-veer.)
///    On the frame taxi guidance TAKES OVER from a
///    landing rollout (exit handover, backtrack, runway vacated, closure) the
///    tone stops SILENTLY instead: taxi guidance's own sentence is the one
///    utterance and its tone the only one (StepTaxiHandover). Taxi guidance
///    then owns the arrival.
///
/// Runway check: at flare engage and again at touchdown the assist identifies the runway the
/// aircraft is actually over (Navigation.LandingRunwayMatch). When that is not the armed runway —
/// typically a runway change after arming — the tones steer at the actual runway for that
/// engagement and the switch is spoken once ("Flare guidance, runway 12R, not 12L."). The armed
/// runway stays the pilot's choice and is restored when the engagement ends.
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
    // audio and the flare phase stays silent (VG has its own flare cue). VG switches itself
    // off on the touchdown edge of the 1 Hz SIM_ON_GROUND sample, up to a second after the
    // rollout's first ground frame, so the rollout tone and its sentence wait for that too
    // (DecideRolloutToneOwner, DecideRolloutGuidanceSpeech).
    private readonly Func<bool> isVisualGuidanceActive;
    // Taxi guidance as the rollout cue needs it on a frame, read ONCE per frame under one lock
    // (TaxiGuidanceManager.RolloutCueSnapshot):
    //  • InLandingRollout: an exit or the runway-end countdown runs. The rollout then has no speed end
    //    (DecideRolloutEnd): it shares the tone with exit guidance by ownership instead.
    //  • ExitGuidanceOwnsRolloutTone: exit guidance's exit-bearing or turn-window tone, or a deliberate
    //    turn-window silence. The cue then stays engaged at volume 0, so the two pan tones never sound
    //    together, and it sounds again when exit guidance yields.
    //  • LandingExitTaxiSteering: taxi guidance has HANDED OFF to the exit route and its own steering tone
    //    is panning. That handoff (turnBegun at up to 90 kt, exitedLaterally with no speed cap at all) can
    //    fire at any speed, so no speed rule could keep the two tones apart — and they steer opposite ways,
    //    ours back to the runway centreline while the taxi tone leads onto the exit. The takeover itself is
    //    caught on the state change (StepTaxiHandover); this is the backstop for a rollout entered while
    //    taxi guidance already steers (a bounce).
    private readonly Func<TaxiGuidanceManager.RolloutCueState> getTaxiGuidanceState;

    // True while the landing-exit planner holds a plan (used or not, for any airport).
    private readonly Func<bool> hasLandingExitPlan;
    // True when that plan will start exit guidance at the next touchdown at the given airport
    // (LandingExitPlanner.WillStartGuidanceAt), so the planner will lead its own touchdown sentence with the
    // runway correction. Both features notice a runway change and both say so at touchdown, the planner up to
    // about a second later (it hears the touchdown from the 1 Hz SIM_ON_GROUND sample, then reads a fresh
    // position) and interrupting, so the flare mode's immediate sentence leaves the telling to it
    // (LandingAssistRunwaySwitch.AtTouchdown) and only re-points its tones. A plan for another airport starts
    // nothing here (it says "Runway not identified" once per plan) and one already used says nothing, so this
    // assist speaks for itself then, after deferring until the planner has had the touchdown
    // (DecideRolloutGuidanceSpeech). The rollout-only cue does not predict: it always defers with a plan.
    private readonly Func<string, bool> landingExitPlanStartsAt;
    // True while the planner still has to decide the coming touchdown (LandingExitPlanner.IsAwaitingTouchdown):
    // a deferred "Rollout guidance" sentence waits for that decision instead of guessing its latency
    // (ResolveDeferredRolloutGuidance).
    private readonly Func<bool> isPlannerAwaitingTouchdown;

    // LATERAL / pan tone — the user's chosen waveform. Runs from flare engage all the way to
    // the landing-exit handoff; only the law feeding it changes at touchdown.
    private readonly AudioToneGenerator tone = new();
    // VERTICAL sink-rate tone — flare only, stopped at touchdown. Takes a waveform that
    // CONTRASTS with the lateral one: the two sound together through the flare, and identical
    // waveforms at a matched state blur (visual guidance's dual-tone lesson).
    private readonly AudioToneGenerator verticalTone = new();

    // Monotonic clock for the cross-track rate differentiator.
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    // The runway the pilot armed the assist for — their choice, never changed by a runway switch.
    private Runway? armedRunway;
    private Airport? armedAirport;
    // Every runway end at the armed airport, loaded at arming time, for the runway check.
    private IReadOnlyList<Runway> airportRunways = Array.Empty<Runway>();
    // The ACTIVE runway every measurement below uses: the armed runway, or for one engagement the
    // runway the aircraft is actually landing on. StopEngagement restores the armed runway.
    private Runway? runway;
    // The "…runway 12R, not 12L." correction has been spoken this engagement.
    private bool runwayCorrectionSpoken;
    // The PAINTED LANDING THRESHOLD — every distance and centerline measurement in this class
    // anchors here. NOT runway.StartLat/StartLon, which is the physical pavement EDGE: at a
    // displaced-threshold runway the two are hundreds of feet apart (LGKR 16: 1341 ft, KJFK 22R:
    // ~3400 ft), which shortens every along-track distance by the displacement.
    private double thresholdLat;
    private double thresholdLon;
    private double thresholdElevationFt;
    private string runwayLabel = "";
    private bool armed;
    // The arming includes the flare phase (the destination dialog's checkbox). False: the rollout-only cue
    // (RolloutCueArming) — no approach tones, the rollout engages at touchdown (ProcessRolloutOnlyArmed).
    private bool flareEnabled = true;
    // Rollout-only: flying above ROLLOUT_ONLY_ARM_GEAR_AGL_FT (or a bounce after engagement) has armed the
    // touchdown decision, which the first ground frame makes.
    private bool rolloutOnlyWasAirborne;

    // landing_assist.log: one header per rollout engagement, then a throttled per-frame trace of the cue
    // (FormatTraceLine), so a rollout like KSEA 16R's can be read back frame by frame.
    private static readonly LogChannel _traceLog = Log.Channel("landing_assist");
    private DateTime lastTraceUtc = DateTime.MinValue;
    private bool traceLineWritten;
    private bool lastTraceSounding;
    private RolloutToneOwner lastTraceOwner;
    // Ground speed of the last rollout frame, for the takeover's trace event (which has no frame of its own).
    private double lastRolloutGsKts;
    // A "Rollout guidance" sentence held at touchdown (RolloutGuidanceSpeech.Deferred), with whether it carries
    // the runway correction and when it was held (clock seconds). Kept across a bounce; dropped by a new arming
    // and by the end of the rollout.
    private string? deferredRolloutGuidance;
    private bool deferredRolloutGuidanceHasCorrection;
    private double deferredRolloutGuidanceSinceSec;

    private Phase phase = Phase.Armed;
    private bool monitoringRequested;
    private bool wasAboveFlareBand;      // must see gearAGL above the latch height before engaging
    private bool toneStarted;            // lateral / pan generator
    private bool verticalToneStarted;    // flare sink-rate generator
    private bool lateralReArmSpent;      // this outage's one lateral re-arm has run
    private bool verticalReArmSpent;     // this outage's one vertical re-arm has run
    private bool verticalCueLostAnnounced; // "sink rate cue unavailable" spoken this engagement
    private bool silentFlare;            // flare engaged while visual guidance owns approach audio
    private bool rolloutAnnounced;       // announce "Rollout guidance." once per approach (not per bounce)
    // Taxi guidance as MainForm forwards it from TaxiGuidanceManager.StateChanged (StepTaxiHandover).
    private bool taxiInLandingRollout;        // taxi guidance is in, or momentarily reloading, a landing rollout
    private bool taxiGuidanceActive;          // its last observed state is not Inactive
    private bool taxiGuidanceSeenThisRollout; // it ran during this rollout: the end callout is queued
    private bool taxiHandoverPending;         // it took over this rollout: yield on the next hook or frame
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

    // --- Rollout centerline steering (TakeoffAssistManager modern mode's own law and constants) ------
    private const double PAN_FULL_RANGE_DEGREES = 5.0;

    // --- Rollout handoff --------------------------------------------------------------
    // With no landing rollout running in taxi guidance. During one there is no speed end (DecideRolloutEnd).
    private const double ROLLOUT_END_GS_KTS = 40.0;
    // landing_assist.log trace throttle: one line per 100 ms while moving faster than a walk.
    private const double TRACE_MIN_INTERVAL_MS = 100.0;
    private const double TRACE_MOVING_MIN_GS_KTS = 3.0;
    // How far above ROLLOUT_END_GS_KTS a touchdown must be to engage the rollout-only cue: slower, it would be
    // "Rollout guidance" and an interrupting "Rollout guidance complete" a second apart (a light aircraft at 44 kt).
    private const double ROLLOUT_ONLY_ENGAGE_MARGIN_KTS = 10.0;
    // The rollout-only cue's touchdown floor: never below the landing-exit planner's own real-landing floor
    // (LandingExitPlanner.LANDING_MIN_GS_KNOTS: below it a teleport or a taxi onto the ground), so the cue never
    // engages on a touchdown the planner refuses as too slow, and ROLLOUT_ONLY_ENGAGE_MARGIN_KTS above the end.
    private static readonly double RolloutOnlyMinTouchdownGsKts =
        Math.Max(LandingExitPlanner.LANDING_MIN_GS_KNOTS, ROLLOUT_END_GS_KTS + ROLLOUT_ONLY_ENGAGE_MARGIN_KTS);
    // The rollout-only cue's touchdown decision is armed only by flying above this gear height
    // (ArmsRolloutOnlyTouchdown): the flare band's own latch, which every approach passes and a settle-back
    // after liftoff does not.
    private const double ROLLOUT_ONLY_ARM_GEAR_AGL_FT = FLARE_LATCH_GEAR_AGL_FT;
    // The longest a deferred "Rollout guidance" sentence waits for the planner to decide the touchdown
    // (ResolveDeferredRolloutGuidance). Only a backstop: the planner settles a touchdown it takes within about a
    // second (the 1 Hz SIM_ON_GROUND sample, a fresh position, the route), and this covers one it never takes.
    private const double ROLLOUT_GUIDANCE_DEFER_MAX_SEC = 5.0;
    // A turn off the runway mutes the cue at or below this speed when no exit guidance steers at an exit
    // (IsTurningOffRunway): the fastest any exit can be taken (RolloutExitGate.MaxTurnSpeedKts of a shallow
    // exit). Above it a heading swing is a veer, never a turn-off, and the cue keeps sounding.
    private const double TURN_OFF_MUTE_MAX_GS_KTS =
        RolloutExitGate.ShallowExitTurnOffSpeedKts + RolloutExitGate.TooFastMarginKts;

    public bool IsArmed => armed;
    public bool IsEngaged => armed && phase != Phase.Armed;

    /// <summary>
    /// True while the rollout centerline cue runs (flare or rollout-only arming). Exit guidance reads it every
    /// rollout frame (TaxiGuidanceManager.CenterlineCueEngagedProvider) and pauses its own tone on the runway.
    /// </summary>
    public bool IsRolloutEngaged => armed && phase == Phase.Rollout;

    /// <summary>Raised when the manager wants the SIM_FRAME feed started (true) or stopped (false).</summary>
    public event EventHandler<bool>? MonitoringRequestChanged;
    /// <summary>Raised on flare-engage (true) and on rollout-complete / go-around / disarm (false).</summary>
    public event EventHandler<bool>? EngagedChanged;

    public LandingFlareAssistManager(ScreenReaderAnnouncer screenReaderAnnouncer,
        Func<double> flareAglBiasFtProvider,
        Func<bool> visualGuidanceActiveCheck,
        Func<TaxiGuidanceManager.RolloutCueState> taxiGuidanceStateProvider,
        Func<bool> landingExitPlanPendingCheck,
        Func<string, bool> landingExitPlanStartsAtCheck,
        Func<bool> plannerAwaitingTouchdownCheck)
    {
        announcer = screenReaderAnnouncer;
        getFlareAglBiasFt = flareAglBiasFtProvider;
        isVisualGuidanceActive = visualGuidanceActiveCheck;
        getTaxiGuidanceState = taxiGuidanceStateProvider;
        hasLandingExitPlan = landingExitPlanPendingCheck;
        landingExitPlanStartsAt = landingExitPlanStartsAtCheck;
        isPlannerAwaitingTouchdown = plannerAwaitingTouchdownCheck;
    }

    /// <summary>
    /// Arms the assist for the given destination runway. Silent — the caller composes
    /// the "destination set" announcement. Re-arming (new destination) resets any
    /// in-progress state first. <paramref name="airportRunways"/> is every runway end at the
    /// destination, used to tell which runway the aircraft is actually landing on.
    /// <paramref name="includeFlare"/> false arms the rollout centerline cue alone (a destination
    /// or landing exit set without the manual-landing checkbox, MainForm's RolloutCueArming): no
    /// tones on the approach, the rollout engaging at touchdown.
    /// </summary>
    public void Arm(Runway destinationRunway, Airport destinationAirport, IReadOnlyList<Runway> airportRunways,
        bool includeFlare = true)
    {
        // A destination change mid-approach must not leave a tone running against
        // the old runway's geometry.
        StopEngagement(raiseEvents: true);

        armedRunway = destinationRunway;
        armedAirport = destinationAirport;
        this.airportRunways = airportRunways ?? Array.Empty<Runway>();
        ApplyActiveRunway(destinationRunway);
        armed = true;
        flareEnabled = includeFlare;
        phase = Phase.Armed;
        wasAboveFlareBand = false;
        rolloutOnlyWasAirborne = false;
        rolloutAnnounced = false;
        deferredRolloutGuidance = null;

        Log.Debug("LandingFlareAssist",
            $"Armed: {destinationAirport.ICAO} rwy {runwayLabel}, flare={includeFlare}, " +
            $"thrElev={thresholdElevationFt:F0} ft, displaced={destinationRunway.ThresholdOffset:F0} ft, " +
            $"runways={this.airportRunways.Count}");
    }

    /// <summary>
    /// Points every measurement at <paramref name="r"/>: the aim point projected from the pavement
    /// edge down the runway to the PAINTED threshold (a zero offset — 94 % of runway ends,
    /// including every EGLL and KJFK end — is an exact no-op), the threshold elevation, and the
    /// spoken label.
    /// </summary>
    private void ApplyActiveRunway(Runway r)
    {
        runway = r;
        (thresholdLat, thresholdLon) = NavigationCalculator.CalculateTouchdownAimPoint(
            r.StartLat, r.StartLon, r.Heading, r.ThresholdOffset);
        thresholdElevationFt = r.ThresholdElevation != 0
            ? r.ThresholdElevation
            : armedAirport?.Altitude ?? 0.0;
        runwayLabel = r.RunwayID;
        // A new centerline makes the previous cross-track sample meaningless to the rate
        // differentiator — without this, a touchdown switch followed by a bounce back into
        // Flare (Phase.Rollout's inline path, which does not call EnterFlare) computes a rate
        // across the jump and spikes the lateral tone to full pan for many frames, not one —
        // the rate is EMA-smoothed (alpha 0.2), so a ~49 m switch holds the spike roughly
        // 8-25 frames at 30 Hz.
        crossTrackRateInitialized = false;
    }

    /// <summary>
    /// Disarms completely (no destination or exit plan arms it any more, or reset). With
    /// <paramref name="announce"/> (a database switch) it says what it switched off
    /// (<see cref="DisarmAnnouncement"/>): "Manual landing assist off" only for the checkbox arming, so a
    /// pilot who never ticked the box is never told an assist they never chose was switched off.
    /// </summary>
    public void Disarm(bool announce = false)
    {
        string? announcement = DisarmAnnouncement(armed, flareEnabled);
        StopEngagement(raiseEvents: true);
        armed = false;
        flareEnabled = true;
        rolloutOnlyWasAirborne = false;
        deferredRolloutGuidance = null;
        runway = null;
        armedRunway = null;
        armedAirport = null;
        airportRunways = Array.Empty<Runway>();
        SetMonitoringRequested(false);

        if (announce && announcement != null)
            announcer.AnnounceImmediate(announcement);
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

        // Rollout-only arming is not engaged until its touchdown decision, which the first SIM_FRAME ground
        // frame makes. This 1 Hz ground sample can arrive first, and dropping the feed here would lose the
        // decision for that landing, so the feed stays until it is made.
        if (!flareEnabled && rolloutOnlyWasAirborne && onGround)
        {
            SetMonitoringRequested(true);
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
                if (!flareEnabled)
                {
                    ProcessRolloutOnlyArmed(d, gearAgl, onGround);
                    break;
                }
                if (onGround) return;
                if (gearAgl > FLARE_LATCH_GEAR_AGL_FT) wasAboveFlareBand = true;

                if (wasAboveFlareBand &&
                    gearAgl <= FLARE_START_GEAR_AGL_FT &&
                    vsSmoothed < -FLARE_ENGAGE_MIN_SINK_FPM &&
                    NavigationCalculator.CalculateDistance(
                        d.Latitude, d.Longitude,
                        thresholdLat, thresholdLon) <= FLARE_ENGAGE_MAX_DIST_NM)
                {
                    EnterFlare(d);
                }
                break;

            case Phase.Flare:
                if (onGround)
                {
                    var taxiAtTouchdown = getTaxiGuidanceState();
                    EnterRollout(d, taxiAtTouchdown);
                    UpdateRolloutTone(d, taxiAtTouchdown);
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
                // MainForm yields on the taxi position frame; this catches a takeover it did not see first.
                YieldIfTaxiGuidanceTookOver();
                if (phase != Phase.Rollout) return;
                if (!onGround && gearAgl > BOUNCE_AIRBORNE_GEAR_AGL_FT)
                {
                    if (!flareEnabled)
                    {
                        // Rollout-only has no flare guidance to resume: stop the tone silently and let the
                        // next touchdown re-engage it through the same gate (ProcessRolloutOnlyArmed).
                        TraceRolloutEvent("Rollout end: airborne again", d.GroundSpeedKnots);
                        StopEngagement(raiseEvents: true);
                        rolloutOnlyWasAirborne = true;
                        return;
                    }
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
                var taxi = getTaxiGuidanceState();
                var track = UpdateRolloutTone(d, taxi);
                if (track.HasValue) CheckRolloutHandoff(d, track.Value, taxi);
                break;
        }
    }

    /// <summary>
    /// Rollout-only arming waiting for its touchdown. An airborne frame above the arming height
    /// (<see cref="ArmsRolloutOnlyTouchdown"/>) arms the decision (and, above go-around height, re-arms the
    /// "Rollout guidance" sentence for the next approach); below the flare height the tone's device is opened,
    /// silent, so the touchdown frame pays no WASAPI open. On the ground the decision
    /// (<see cref="ShouldEngageRolloutOnly"/>) stays open while the aircraft is still at touchdown speed: one
    /// sample at the instant of touchdown (a ground-bit flicker short of the pavement, the first frame on its
    /// edge) must not refuse the whole rollout. Below that speed it is settled, and a refusal logged once.
    /// </summary>
    private void ProcessRolloutOnlyArmed(MSFSBlindAssist.SimConnect.SimConnectManager.FlareAssistData d,
        double gearAgl, bool onGround)
    {
        if (!onGround)
        {
            if (ArmsRolloutOnlyTouchdown(onGround, gearAgl)) rolloutOnlyWasAirborne = true;
            if (gearAgl > GO_AROUND_GEAR_AGL_FT)
            {
                // Climbed away: a new approach, whose touchdown gets its own sentence, and no touchdown near.
                rolloutAnnounced = false;
                deferredRolloutGuidance = null;
                StopLateralTone();
            }
            else if (rolloutOnlyWasAirborne && gearAgl <= FLARE_START_GEAR_AGL_FT)
            {
                // Where the flare mode opens it: EnterRollout then finds the tone started, at volume 0.
                StartLateralToneIfNeeded();
            }
            return;
        }

        if (!rolloutOnlyWasAirborne || armedRunway == null) return;

        var verdict = LandingRunwayMatch.Evaluate(
            d.Latitude, d.Longitude, d.HeadingMagnetic + d.MagneticVariation, armedRunway, airportRunways);
        if (ShouldEngageRolloutOnly(rolloutOnlyWasAirborne, d.GroundSpeedKnots, verdict.Verdict))
        {
            rolloutOnlyWasAirborne = false;
            var taxi = getTaxiGuidanceState();
            EnterRollout(d, taxi);
            EngagedChanged?.Invoke(this, true);
            UpdateRolloutTone(d, taxi);
            return;
        }

        if (d.GroundSpeedKnots >= RolloutOnlyMinTouchdownGsKts) return;   // still at touchdown speed: keep deciding
        rolloutOnlyWasAirborne = false;
        StopLateralTone();
        TraceRolloutEvent($"Touchdown not engaged: runway check {verdict.Verdict} against armed " +
            $"{armedAirport?.ICAO ?? "-"} {armedRunway.RunwayID}", d.GroundSpeedKnots);
    }

    /// <summary>
    /// Forwarded by MainForm from <see cref="TaxiGuidanceManager.StateChanged"/>. Runs INSIDE
    /// TaxiGuidanceManager.SetState, usually under its state lock, so it only RECORDS: no tone, speech,
    /// logging or events. <see cref="YieldIfTaxiGuidanceTookOver"/> acts on what it records.
    /// </summary>
    public void ObserveTaxiGuidanceState(TaxiGuidanceState newState)
    {
        var step = StepTaxiHandover(taxiInLandingRollout, newState);
        taxiInLandingRollout = step.InLandingRollout;
        taxiGuidanceActive = newState != TaxiGuidanceState.Inactive;
        if (phase != Phase.Rollout) return;
        if (taxiGuidanceActive) taxiGuidanceSeenThisRollout = true;
        if (step.TookOver) taxiHandoverPending = true;
    }

    /// <summary>
    /// Ends the rollout SILENTLY once taxi guidance has taken over from a landing rollout
    /// (<see cref="StepTaxiHandover"/>): taxi guidance's own sentence is the one utterance, and its tone
    /// first sounds on the next position frame, so two pan tones never play together. MainForm calls this
    /// after the taxi position update, on the frame of the transition; the rollout frame calls it too.
    /// </summary>
    public void YieldIfTaxiGuidanceTookOver()
    {
        if (!taxiHandoverPending) return;
        taxiHandoverPending = false;
        // A real takeover always leaves the landing rollout. Taxi guidance already back in one means the
        // pending flag was a misread: a new landing's planner activation passes through Taxiing on its way in.
        if (taxiInLandingRollout) return;
        if (phase != Phase.Rollout) return;
        TraceRolloutEvent("Rollout end: taxi guidance took over", lastRolloutGsKts);
        deferredRolloutGuidance = null;   // exit guidance ran, so its touchdown sentence covered it
        StopEngagement(raiseEvents: true);
        Log.Debug("LandingFlareAssist", "Rollout handed over to taxi guidance");
    }

    private void EnterFlare(MSFSBlindAssist.SimConnect.SimConnectManager.FlareAssistData d)
    {
        phase = Phase.Flare;
        rolloutAnnounced = false;
        crossTrackRateInitialized = false;   // fresh differentiator per engagement
        // Visual guidance active = it owns the approach audio (its own flare cue included).
        // Stay silent through the flare; the rollout pan tone takes over after touchdown, once
        // VG has switched itself off on the touchdown edge (DecideRolloutToneOwner).
        silentFlare = isVisualGuidanceActive();

        // Lined up with a different runway from the armed one (a runway change after arming)? The
        // tones must steer at the runway actually being landed on, or they pan hard toward the armed
        // one in the last 50 ft. Approach mode: the aircraft can still be short of the pavement.
        bool speakCorrection = false;
        if (armedRunway != null)
        {
            var verdict = LandingRunwayMatch.Evaluate(
                d.Latitude, d.Longitude, d.HeadingMagnetic + d.MagneticVariation,
                armedRunway, airportRunways, LandingRunwayMatch.ApproachBeforeThresholdMarginM);
            var decision = LandingAssistRunwaySwitch.AtFlareEngage(verdict, silentFlare);
            if (decision.SwitchTo != null)
            {
                ApplyActiveRunway(decision.SwitchTo);
                Log.Debug("LandingFlareAssist",
                    $"Flare engage: armed rwy {armedRunway.RunwayID}, {verdict.Verdict} {decision.SwitchTo.RunwayID} — switched");
            }
            speakCorrection = decision.SpeakCorrection;
        }

        if (!silentFlare)
        {
            StartVerticalToneIfNeeded();
            StartLateralToneIfNeeded();
            announcer.AnnounceImmediate(LandingAssistRunwaySwitch.FlareGuidancePhrase(
                runwayLabel, armedRunway?.RunwayID ?? runwayLabel, speakCorrection));
            if (speakCorrection) runwayCorrectionSpoken = true;
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

    private void EnterRollout(MSFSBlindAssist.SimConnect.SimConnectManager.FlareAssistData d,
        TaxiGuidanceManager.RolloutCueState taxi)
    {
        phase = Phase.Rollout;
        // A takeover belongs to THIS rollout; taxi guidance already running counts as seen.
        taxiHandoverPending = false;
        taxiGuidanceSeenThisRollout = taxiGuidanceActive;
        StopVerticalTone();            // flare sink-rate cue is done at touchdown
        // The lateral tone carries straight through from the flare — only the law feeding it
        // changes. (StartLateralToneIfNeeded is a no-op unless the flare was silent under VG.)
        StartLateralToneIfNeeded();
        tone.UpdatePitch(0);           // pan mode: park the frequency at centre, meaning is the pan

        // Will exit guidance start at THIS touchdown and speak its own sentence, runway correction included?
        // Already running (the planner got there first), or predicted for a fresh plan for this airport. The
        // flare mode's immediate sentence leaves the correction to the planner then. A held sentence
        // (RolloutGuidanceSpeech.Deferred: a plan that will not start guidance here, the rollout-only cue with any
        // plan, or visual guidance still on) waits until the planner has had the touchdown and visual guidance is
        // off, and is spoken only if exit guidance did NOT start, so that sentence always carries the correction.
        bool exitGuidanceStartsHere = taxi.InLandingRollout
            || (armedAirport != null && landingExitPlanStartsAt(armedAirport.ICAO));
        var speech = DecideRolloutGuidanceSpeech(flareEnabled, hasLandingExitPlan(), exitGuidanceStartsHere,
            isVisualGuidanceActive());

        // Confirm the runway on the ground: switch if the aircraft touched down on a different runway
        // from the active one, and say so once when that is not the armed runway.
        bool speakCorrection = false;
        if (armedRunway != null && runway != null)
        {
            var verdict = LandingRunwayMatch.Evaluate(
                d.Latitude, d.Longitude, d.HeadingMagnetic + d.MagneticVariation, runway, airportRunways);
            var decision = LandingAssistRunwaySwitch.AtTouchdown(verdict, runway, armedRunway, runwayCorrectionSpoken,
                exitPlanWillSayIt: speech == RolloutGuidanceSpeech.Immediate && exitGuidanceStartsHere);
            if (decision.SwitchTo != null)
            {
                ApplyActiveRunway(decision.SwitchTo);
                Log.Debug("LandingFlareAssist",
                    $"Touchdown: {verdict.Verdict} {decision.SwitchTo.RunwayID} — switched (armed {armedRunway.RunwayID})");
            }
            speakCorrection = decision.SpeakCorrection;
        }

        if (!rolloutAnnounced)
        {
            rolloutAnnounced = true;
            deferredRolloutGuidance = null;
            string phrase = LandingAssistRunwaySwitch.RolloutGuidancePhrase(
                runwayLabel, armedRunway?.RunwayID ?? runwayLabel, speakCorrection);
            if (speech == RolloutGuidanceSpeech.Immediate)
            {
                announcer.AnnounceImmediate(phrase);
                if (speakCorrection) runwayCorrectionSpoken = true;
            }
            else
            {
                deferredRolloutGuidance = phrase;
                deferredRolloutGuidanceHasCorrection = speakCorrection;
                deferredRolloutGuidanceSinceSec = clock.Elapsed.TotalSeconds;
            }
        }
        BeginRolloutTrace(exitGuidanceStartsHere);
        Log.Debug("LandingFlareAssist", $"Rollout engaged (flare={flareEnabled})");
    }

    /// <summary>The landing_assist.log header for this rollout, and a fresh trace throttle.</summary>
    private void BeginRolloutTrace(bool exitGuidanceStartsHere)
    {
        traceLineWritten = false;
        lastTraceUtc = DateTime.MinValue;
        try
        {
            _traceLog.Info($"=== Rollout icao={armedAirport?.ICAO ?? "-"} rwy={runwayLabel} " +
                $"armed={armedRunway?.RunwayID ?? "-"} flare={flareEnabled} exitPlan={hasLandingExitPlan()} " +
                $"exitGuidanceHere={exitGuidanceStartsHere} ===");
            _traceLog.Info("lat,lon,gsKt,hdgOffRwyDeg,crossTrackFt,crabDeg,steerDeg,pan,sounding,owner");
        }
        catch { /* diagnostic only */ }
    }

    /// <summary>
    /// One trace line for this rollout frame: at most every TRACE_MIN_INTERVAL_MS while moving, and always
    /// when the cue starts or stops sounding or the tone changes owner, so a stopped aircraft cannot flood it.
    /// </summary>
    private void TraceRolloutFrame(MSFSBlindAssist.SimConnect.SimConnectManager.FlareAssistData d,
        double headingOffRunwayDeg, double crossTrackFeet, double crabDeg, double steerDeg,
        bool sounding, RolloutToneOwner owner)
    {
        DateTime nowUtc = DateTime.UtcNow;
        bool changed = !traceLineWritten || sounding != lastTraceSounding || owner != lastTraceOwner;
        bool due = d.GroundSpeedKnots > TRACE_MOVING_MIN_GS_KTS
                   && (nowUtc - lastTraceUtc).TotalMilliseconds >= TRACE_MIN_INTERVAL_MS;
        if (!changed && !due) return;

        traceLineWritten = true;
        lastTraceUtc = nowUtc;
        lastTraceSounding = sounding;
        lastTraceOwner = owner;
        try
        {
            _traceLog.Info(FormatTraceLine(d.Latitude, d.Longitude, d.GroundSpeedKnots, headingOffRunwayDeg,
                crossTrackFeet, crabDeg, steerDeg, PanFor(steerDeg, SettingsManager.Current), sounding, owner));
        }
        catch { /* diagnostic only */ }
    }

    /// <returns>This frame's centerline track, for the handoff check; null with no active runway.</returns>
    private RunwayCenterlineTracker.Result? UpdateRolloutTone(
        MSFSBlindAssist.SimConnect.SimConnectManager.FlareAssistData d, TaxiGuidanceManager.RolloutCueState taxi)
    {
        if (runway == null) return null;

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
        double desiredCrabDeg = LandingGuidanceLaws.CenterlineInterceptCrabDeg(crossTrackFeet,
            TakeoffAssistManager.CROSSTRACK_INTERCEPT_DEADBAND_FEET, TakeoffAssistManager.CROSSTRACK_INTERCEPT_DEG_PER_FOOT,
            TakeoffAssistManager.CROSSTRACK_MAX_INTERCEPT_DEG);

        // > 0 = steer right. Pan in the steer direction (steer toward the tone), matching
        // takeoff assist and the taxi steering tone; the same user settings apply. While visual guidance is
        // still on after touchdown, exit guidance owns the tone (the exit itself), or the aircraft turns off the
        // runway with no exit guidance steering at an exit, this one stays engaged at volume 0 and comes back
        // when the other lets go or the aircraft lines up again.
        double steerDeg = desiredCrabDeg - headingDiff;
        lastRolloutGsKts = d.GroundSpeedKnots;
        var owner = DecideRolloutToneOwner(isVisualGuidanceActive(), taxi.ExitGuidanceOwnsRolloutTone,
            IsTurningOffRunway(taxi.InExitRollout, d.GroundSpeedKnots, headingDiff));
        bool sounding = ApplyPan(steerDeg, muted: owner != RolloutToneOwner.Cue);
        TraceRolloutFrame(d, headingDiff, crossTrackFeet, desiredCrabDeg, steerDeg, sounding, owner);
        ServiceDeferredRolloutGuidance(taxi);
        return track;
    }

    /// <summary>
    /// Speaks or drops a held "Rollout guidance" sentence once the planner has had the touchdown and visual
    /// guidance is off (<see cref="ResolveDeferredRolloutGuidance"/>). Spoken without interrupting, so a
    /// sentence still being read ("Runway not identified…", "Visual guidance off") finishes first.
    /// </summary>
    private void ServiceDeferredRolloutGuidance(TaxiGuidanceManager.RolloutCueState taxi)
    {
        if (deferredRolloutGuidance == null) return;
        var outcome = ResolveDeferredRolloutGuidance(clock.Elapsed.TotalSeconds - deferredRolloutGuidanceSinceSec,
            plannerAwaitingTouchdown: isPlannerAwaitingTouchdown(),
            exitGuidanceRunning: taxi.InLandingRollout || taxi.LandingExitTaxiSteering,
            visualGuidanceActive: isVisualGuidanceActive());
        if (outcome == DeferredSpeech.Wait) return;

        if (outcome == DeferredSpeech.Speak)
        {
            announcer.Announce(deferredRolloutGuidance);
            if (deferredRolloutGuidanceHasCorrection) runwayCorrectionSpoken = true;
        }
        TraceRolloutEvent(outcome == DeferredSpeech.Speak
            ? "Rollout guidance spoken" : "Rollout guidance left to exit guidance", lastRolloutGsKts);
        deferredRolloutGuidance = null;
    }

    /// <summary>
    /// Drives the lateral generator from a steer command in degrees (+ = steer RIGHT), honoring
    /// the shared TakeoffAssist pan settings (waveform/volume/steer-toward/hard-pan) so the flare, the
    /// rollout, takeoff assist and taxi guidance all behave identically for a given configuration.
    /// </summary>
    /// <param name="silenceBelowDeg">
    /// Silence band. Null = use the user's <c>TakeoffAssistHeadingToneThreshold</c>, which is the
    /// rollout's contract (its command IS a heading error, the quantity that setting describes).
    /// The flare passes its own band instead — see <see cref="LATERAL_DEADBAND_DEG"/>.
    /// </param>
    /// <param name="muted">Visual guidance or exit guidance holds the tone this frame (<see cref="DecideRolloutToneOwner"/>):
    /// volume 0 whatever the command.</param>
    /// <returns>Whether the tone sounds this frame.</returns>
    private bool ApplyPan(double steerCommandDeg, double? silenceBelowDeg = null, bool muted = false)
    {
        var settings = SettingsManager.Current;

        tone.SetPan(PanFor(steerCommandDeg, settings));

        double threshold = silenceBelowDeg ?? settings.TakeoffAssistHeadingToneThreshold;
        bool shouldPlay = !muted && (threshold <= 0 || Math.Abs(steerCommandDeg) >= threshold);
        tone.UpdateVolume(shouldPlay ? settings.TakeoffAssistToneVolume : 0);
        return shouldPlay;
    }

    /// <summary>
    /// Pan for a steer command (+ = steer RIGHT) under the shared takeoff-assist tone settings: hard
    /// pan or proportional over PAN_FULL_RANGE_DEGREES, on the steer side when the pilot steers toward
    /// the tone. Reads TakeoffAssistSteerTowardTone, the setting takeoff assist reads — never the
    /// retired TakeoffAssistInvertPanning, whose value after the July tone migration means the
    /// opposite of how this assist once used it. Pure — <c>LandingFlareAssistPanTests</c>.
    /// </summary>
    internal static float PanFor(double steerCommandDeg, UserSettings settings)
    {
        float pan = settings.TakeoffAssistHardPanTone
            ? Math.Sign(steerCommandDeg)
            : (float)Math.Clamp(steerCommandDeg / PAN_FULL_RANGE_DEGREES, -1.0, 1.0);
        return settings.TakeoffAssistSteerTowardTone ? pan : -pan;
    }

    /// <param name="track">This frame's centerline track, as <see cref="UpdateRolloutTone"/> computed it.</param>
    private void CheckRolloutHandoff(MSFSBlindAssist.SimConnect.SimConnectManager.FlareAssistData d,
        RunwayCenterlineTracker.Result track, TaxiGuidanceManager.RolloutCueState taxi)
    {
        if (runway == null) return;

        // Laterally clear of the runway the cue steers at: off its pavement, whether by a turn-off or an
        // excursion (Navigation.RolloutExitGate.IsLaterallyClearOfRunway, the app's one "off the runway").
        bool clearOfRunway = RolloutExitGate.IsLaterallyClearOfRunway(track.AbsCrossTrackFeet * 0.3048, runway.Width);
        bool inRollout = taxi.InLandingRollout;
        bool taxiSteering = taxi.LandingExitTaxiSteering;

        var end = DecideRolloutEnd(d.GroundSpeedKnots, clearOfRunway,
            inLandingRollout: inRollout,
            taxiGuidanceSteering: taxiSteering,
            taxiGuidanceSeen: taxiGuidanceSeenThisRollout);
        if (end == RolloutEnd.Continue) return;

        string reason = taxiSteering ? "taxi guidance steering"
            : inRollout ? "stopped"
            : clearOfRunway ? "clear of the runway"
            : "below 40 kt";
        TraceRolloutEvent($"Rollout end: {reason}", d.GroundSpeedKnots);
        deferredRolloutGuidance = null;   // never "Rollout guidance" after the rollout has ended
        StopEngagement(raiseEvents: true);
        if (end == RolloutEnd.Interrupting)
            announcer.AnnounceImmediate("Rollout guidance complete");
        else if (end == RolloutEnd.Queued)
            announcer.Announce("Rollout guidance complete");
        else
            Log.Debug("LandingFlareAssist", $"Rollout ended silently: {reason}");
        // Stay ARMED: circuits / touch-and-go get flare guidance again on the next
        // approach without re-opening the destination dialog. The feed gate drops
        // the SIM_FRAME request within a second (on ground, not engaged).
    }

    /// <summary>What <see cref="CheckRolloutHandoff"/> does on a rollout frame.</summary>
    internal enum RolloutEnd
    {
        /// <summary>Keep guiding.</summary>
        Continue,
        /// <summary>Stop with no speech: taxi guidance already steers, and its words and tone are the handover;
        /// or the aircraft has stopped during a landing rollout, whose own speech covers the stop.</summary>
        Silent,
        /// <summary>Stop and interrupt with "Rollout guidance complete": no taxi guidance ran this rollout.</summary>
        Interrupting,
        /// <summary>Stop and QUEUE "Rollout guidance complete", so it cannot cut off taxi guidance's speech.</summary>
        Queued,
    }

    /// <summary>
    /// When the rollout ends and what it says. Pure — <c>LandingFlareRolloutEndTests</c>.
    ///
    /// During a landing rollout in taxi guidance (<paramref name="inLandingRollout"/>: an exit or the runway-end
    /// countdown) it ends only at a stop, silently — the rollout's own speech covers the stop — because exit
    /// guidance's handoffs (turnBegun, exitedLaterally) end it at a real exit through the takeover, and it
    /// takes turns on the tone with exit guidance by ownership (RolloutExitGate.ExitGuidanceOwnsTone), never
    /// by a speed. With no landing rollout running it ends below ROLLOUT_END_GS_KTS, or once the aircraft is
    /// laterally clear of the runway (<paramref name="laterallyClearOfRunway"/>), turn-off or excursion alike.
    ///
    /// KSEA 16R, 2026-10-15, retired two older ends. A 55 kt end whenever exit guidance ran left 55 to 50 kt
    /// with no tone and only a heading cue below. A 20° heading turn-off below 90 kt ended the cue — and said
    /// "Rollout guidance complete" — in the middle of the very veer it exists for: that A300 yawed 2-3°/s, past
    /// 20° seconds after leaving the centerline. A heading swing cannot tell a veer from a turn-off; position
    /// can. Taxi guidance ALREADY steering ends it at any speed, silently: a rapid exit can take the handoff
    /// far above any speed limit, and two pan tones would steer the pilot opposite ways. When taxi guidance
    /// ran during the rollout the callout is QUEUED, never interrupting, so it cannot cut off a countdown
    /// milestone, "Runway vacated…" or "Exit reached…".
    /// </summary>
    internal static RolloutEnd DecideRolloutEnd(double groundSpeedKts, bool laterallyClearOfRunway,
        bool inLandingRollout, bool taxiGuidanceSteering, bool taxiGuidanceSeen)
    {
        if (taxiGuidanceSteering) return RolloutEnd.Silent;

        if (inLandingRollout)
            return groundSpeedKts < RolloutExitGate.NoExitStoppedGroundSpeedKts ? RolloutEnd.Silent : RolloutEnd.Continue;

        if (groundSpeedKts >= ROLLOUT_END_GS_KTS && !laterallyClearOfRunway) return RolloutEnd.Continue;

        return taxiGuidanceSeen ? RolloutEnd.Queued : RolloutEnd.Interrupting;
    }

    /// <summary>
    /// One <c>landing_assist.log</c> event line — why the cue ended, or why a touchdown did not engage it — in
    /// the invariant culture: <c>=== {description} gs={gs:F1} ===</c>. These go beside the trace, not only to
    /// debug.log, which rotates within minutes. Pure — <c>LandingFlareRolloutOnlyTests</c>.
    /// </summary>
    internal static string FormatTraceEvent(string description, double groundSpeedKts)
        => InvariantLogLine.Format($"=== {description} gs={groundSpeedKts:F1} ===");

    private void TraceRolloutEvent(string description, double groundSpeedKts)
    {
        try { _traceLog.Info(FormatTraceEvent(description, groundSpeedKts)); }
        catch { /* diagnostic only */ }
    }

    /// <summary>
    /// Does this touchdown engage the rollout-only cue (armed with no flare phase)? Only after flying
    /// (<paramref name="wasAirborne"/>: a fast taxi or a rejected takeoff never left the ground), at 50 kt or
    /// more (RolloutOnlyMinTouchdownGsKts: the landing-exit planner's 40 kt real-landing floor, and far enough
    /// above the 40 kt end that a slow touchdown is not "Rollout guidance" and "Rollout guidance complete" a
    /// second apart), and on a runway of the armed airport: <see cref="LandingRunwayVerdict.Unknown"/> means no
    /// runway there contains the aircraft, a stale destination at another airport, and the cue must not pan
    /// toward a centerline that is not under the wheels. Pure — <c>LandingFlareRolloutOnlyTests</c>.
    /// </summary>
    internal static bool ShouldEngageRolloutOnly(bool wasAirborne, double groundSpeedKts, LandingRunwayVerdict verdict)
        => wasAirborne
           && groundSpeedKts >= RolloutOnlyMinTouchdownGsKts
           && verdict != LandingRunwayVerdict.Unknown;

    /// <summary>
    /// Does this frame arm the rollout-only cue's touchdown decision? Only airborne above
    /// ROLLOUT_ONLY_ARM_GEAR_AGL_FT, the flare band's own latch height, which every approach passes:
    /// settling back onto the runway right after liftoff never climbed through it, so it never starts the
    /// cue on the takeoff roll. A bounce after engagement re-arms on its own path. Pure —
    /// <c>LandingFlareRolloutOnlyTests</c>.
    /// </summary>
    internal static bool ArmsRolloutOnlyTouchdown(bool onGround, double gearAglFt)
        => !onGround && gearAglFt > ROLLOUT_ONLY_ARM_GEAR_AGL_FT;

    /// <summary>Who holds the rollout's pan tone on a frame (<see cref="DecideRolloutToneOwner"/>).</summary>
    internal enum RolloutToneOwner
    {
        /// <summary>This cue: it sounds.</summary>
        Cue,
        /// <summary>Exit guidance, for the exit itself ([ROL-32]): the cue is muted.</summary>
        ExitGuidance,
        /// <summary>Visual guidance, still on after touchdown: the cue is muted.</summary>
        VisualGuidance,
        /// <summary>Nobody: the aircraft is turning off the runway (<see cref="IsTurningOffRunway"/>), and the cue
        /// is muted rather than panning against the turn.</summary>
        TurnOff,
    }

    /// <summary>
    /// Who holds the rollout's pan tone on this frame. Visual guidance first, while it is still on: it switches
    /// itself off only on the touchdown edge of the 1 Hz SIM_ON_GROUND sample, up to a second after this cue's
    /// first ground frame, and until then its two tones keep tracking the centerline. Then exit guidance while
    /// it owns the tone for the exit (RolloutExitGate.ExitGuidanceOwnsTone), then nobody while the aircraft turns
    /// off the runway (<paramref name="turningOffRunway"/>), and this cue everywhere else. The cue sounds only
    /// while it holds the tone, so no two guidance tones play together. Pure — <c>LandingFlareRolloutOnlyTests</c>.
    /// </summary>
    internal static RolloutToneOwner DecideRolloutToneOwner(bool visualGuidanceActive, bool exitGuidanceOwnsTone,
        bool turningOffRunway)
        => visualGuidanceActive ? RolloutToneOwner.VisualGuidance
           : exitGuidanceOwnsTone ? RolloutToneOwner.ExitGuidance
           : turningOffRunway ? RolloutToneOwner.TurnOff
           : RolloutToneOwner.Cue;

    /// <summary>
    /// Is the aircraft turning off the runway, so the cue must go quiet instead of panning against the turn?
    /// Only with no exit guidance steering at an exit (<paramref name="inExitRollout"/> false: no landing rollout,
    /// or the runway-end countdown, where nothing else owns the turn): heading at least
    /// RolloutExitGate.TurnBegunHeadingDeg off the runway at or below TURN_OFF_MUTE_MAX_GS_KTS, the fastest any
    /// exit can be taken. The cue stays engaged and sounds again when the aircraft lines up; its end is still a
    /// stop, a takeover, or being clear of the runway ([ROL-32]). Above that speed a heading swing is a veer
    /// (KSEA 16R left the pavement at 70 kt) and the cue keeps steering. During an exit rollout exit guidance
    /// owns the turn window itself. Pure — <c>LandingFlareRolloutOnlyTests</c>.
    /// </summary>
    internal static bool IsTurningOffRunway(bool inExitRollout, double groundSpeedKts, double headingOffRunwayDeg)
        => !inExitRollout
           && groundSpeedKts <= TURN_OFF_MUTE_MAX_GS_KTS
           && Math.Abs(headingOffRunwayDeg) >= RolloutExitGate.TurnBegunHeadingDeg;

    /// <summary>How the "Rollout guidance" sentence is spoken at touchdown.</summary>
    internal enum RolloutGuidanceSpeech
    {
        /// <summary>At once, interrupting.</summary>
        Immediate,
        /// <summary>Held until the planner has had the touchdown and visual guidance is off
        /// (<see cref="ResolveDeferredRolloutGuidance"/>).</summary>
        Deferred,
    }

    /// <summary>
    /// How the "Rollout guidance" sentence is spoken at touchdown. With visual guidance off: with no exit plan,
    /// at once, as the manual landing assist always did, and so in flare mode when the plan will start exit
    /// guidance here (<paramref name="exitGuidanceStartsHere"/>: the sentence then leaves the runway correction to
    /// the planner). Deferred for the rollout-only cue with any plan, and in flare mode for a plan that will not
    /// start guidance here, because the planner hears the touchdown up to about a second later (the 1 Hz
    /// SIM_ON_GROUND sample plus a position read) and may then speak — its touchdown sentence, which says
    /// guidance has started, or "Runway not identified" for a plan made for another airport — and an immediate
    /// sentence of ours would be cut off by it. With visual guidance still on, deferred in every mode: it would
    /// cut off visual guidance's "Touchdown", and it belongs after "Visual guidance off". Pure —
    /// <c>LandingFlareRolloutOnlyTests</c>.
    /// </summary>
    internal static RolloutGuidanceSpeech DecideRolloutGuidanceSpeech(bool flareEnabled, bool exitPlanExists,
        bool exitGuidanceStartsHere, bool visualGuidanceActive)
        => visualGuidanceActive || (exitPlanExists && (!flareEnabled || !exitGuidanceStartsHere))
            ? RolloutGuidanceSpeech.Deferred
            : RolloutGuidanceSpeech.Immediate;

    /// <summary>What happens to a deferred "Rollout guidance" sentence on this rollout frame.</summary>
    internal enum DeferredSpeech
    {
        /// <summary>Keep holding it.</summary>
        Wait,
        /// <summary>Speak it now, without interrupting.</summary>
        Speak,
        /// <summary>Drop it: exit guidance started, and its touchdown sentence said so.</summary>
        Drop,
    }

    /// <summary>
    /// A deferred "Rollout guidance" sentence waits while the landing-exit planner still has to decide this
    /// touchdown (<paramref name="plannerAwaitingTouchdown"/>: LandingExitPlanner.IsAwaitingTouchdown, false once
    /// it has started guidance or said "Runway not identified"), never a guess at how long that takes, and for as
    /// long as visual guidance is still on. ROLLOUT_GUIDANCE_DEFER_MAX_SEC is only the backstop for a touchdown the
    /// planner never takes. Then it is dropped when exit guidance is running (the planner's touchdown sentence
    /// said guidance had started), and spoken otherwise: no plan, a plan made for another airport, a plan already
    /// used this session, or a touchdown the planner did not take. Pure — <c>LandingFlareRolloutOnlyTests</c>.
    /// </summary>
    internal static DeferredSpeech ResolveDeferredRolloutGuidance(double secondsSinceTouchdown,
        bool plannerAwaitingTouchdown, bool exitGuidanceRunning, bool visualGuidanceActive)
        => visualGuidanceActive
           || (plannerAwaitingTouchdown && secondsSinceTouchdown < ROLLOUT_GUIDANCE_DEFER_MAX_SEC) ? DeferredSpeech.Wait
           : exitGuidanceRunning ? DeferredSpeech.Drop
           : DeferredSpeech.Speak;

    /// <summary>
    /// What a database switch's disarm says: "Manual landing assist off" for the checkbox arming, as always,
    /// and "Rollout centerline tone off" for the rollout-only cue, so its silence at the next touchdown is never
    /// mistaken for an on-track cue; nothing when nothing was armed. Pure — <c>LandingFlareRolloutOnlyTests</c>.
    /// </summary>
    internal static string? DisarmAnnouncement(bool wasArmed, bool flareEnabled)
        => !wasArmed ? null
           : flareEnabled ? "Manual landing assist off"
           : "Rollout centerline tone off";

    /// <summary>
    /// One <c>landing_assist.log</c> trace line, in the invariant culture whatever the pilot's Windows
    /// culture: <c>lat,lon,gsKt,hdgOffRwyDeg,crossTrackFt,crabDeg,steerDeg,pan,sounding,owner</c>. Owner is
    /// <c>vg</c> while visual guidance is still on, <c>exit</c> while exit guidance owns the tone and <c>turn</c>
    /// while the aircraft turns off the runway (the cue is muted for all three), <c>cue</c> otherwise. Pure —
    /// <c>LandingFlareRolloutOnlyTests</c>.
    /// </summary>
    internal static string FormatTraceLine(double lat, double lon, double groundSpeedKts, double headingOffRunwayDeg,
        double crossTrackFeet, double crabDeg, double steerDeg, float pan, bool sounding, RolloutToneOwner owner)
        => InvariantLogLine.Format(
            $"{lat:F6},{lon:F6},{groundSpeedKts:F1},{headingOffRunwayDeg:F2},{crossTrackFeet:F1},{crabDeg:F2}," +
            $"{steerDeg:F2},{pan:F2},{(sounding ? 1 : 0)},{TraceOwnerLabel(owner)}");

    private static string TraceOwnerLabel(RolloutToneOwner owner) => owner switch
    {
        RolloutToneOwner.VisualGuidance => "vg",
        RolloutToneOwner.ExitGuidance => "exit",
        RolloutToneOwner.TurnOff => "turn",
        _ => "cue",
    };

    /// <summary>
    /// One taxi-guidance state change, classified for the manual landing assist. Pure —
    /// <c>LandingFlareTaxiHandoverTests</c>.
    ///
    /// Taxi guidance TAKES OVER when it leaves a landing rollout for anything but a momentary route reload
    /// (retargets and declines pass through RouteLoaded) or a full stop (a Taxi Stop must not end the
    /// independent assist). That covers the exit handover, backtracking, the countdown's "Runway vacated"
    /// and every landing-exit closure without a call at any of their sites. Touchdown passes through
    /// Taxiing before the rollout starts and a departure never enters one, so neither counts. Counting
    /// backtracking in IsLandingExitTaxiSteering instead let the assist's interrupting "Rollout guidance
    /// complete" cut off "End of runway … Turn around" within a frame.
    /// </summary>
    internal static (bool InLandingRollout, bool TookOver) StepTaxiHandover(bool inLandingRollout, TaxiGuidanceState newState) =>
        newState switch
        {
            TaxiGuidanceState.LandingRollout => (true, false),
            TaxiGuidanceState.RouteLoaded => (inLandingRollout, false),
            TaxiGuidanceState.Inactive => (false, false),
            _ => (false, inLandingRollout),
        };

    /// <summary>
    /// What to do about a tone that came back from a routing sweep with no device.
    /// Pure so the policy can be pinned by <c>LandingFlareToneReArmTests</c> — the manager
    /// itself owns two real WASAPI generators and cannot be driven from a test.
    /// AnnounceVerticalLost: the vertical cue could not be revived and, left alone, would now
    /// impersonate "on profile" — the caller speaks it (with its own phase gate + once latch).
    /// </summary>
    internal readonly record struct ToneReArmDecision(
        bool RestartLateral, bool RestartVertical,
        bool SpendLateralReArm, bool SpendVerticalReArm,
        bool AnnounceVerticalLost);

    /// <summary>
    /// The router rebinds a NeedsDevice generator on its next SWEEP, and a sweep is what a
    /// device ARRIVING triggers — so a rebind that moves one tone and fails the other leaves
    /// the failed one silent with nothing scheduled to retry it. That is why this exists.
    ///
    /// Restarts ONLY the tone that lost its device, unlike VisualGuidanceManager's re-arm,
    /// which rebuilds its pair whenever either half dies. VG's two are a reference and a
    /// follower that mean nothing apart; these two are independent axes, so tearing down a
    /// healthy one would punch an audible hole in a cue the pilot is actively flying. The
    /// one-per-outage latches are per-tone for the same reason: a vertical tone that failed
    /// permanently must not spend the lateral tone's re-arm — the lateral carries the whole
    /// rollout and a later, independent outage on it still deserves its restart.
    ///
    /// Gated on NeedsDevice, never IsPlaying: IsPlaying reads false for the whole duration of
    /// a HEALTHY in-flight rebind (RebindTo clears it before reopening the device), so a
    /// 1 Hz sample landing mid-rebind would tear down an about-to-succeed tone. NeedsDevice
    /// never means "attempt in flight" on a tone that was healthy — it is set only at an open
    /// attempt's terminal outcomes. Residual, shared with VG's re-arm: on a tone that ALREADY
    /// had NeedsDevice, RebindTo leaves the flag up for the whole retry, so a sample landing
    /// exactly there tears down and reopens a session the router just built — one redundant
    /// (and inaudible: the tone was silent) teardown, bounded by the latch.
    ///
    /// A tone that is not STARTED is never restarted — the vertical tone is flare-only and is
    /// legitimately stopped for the whole rollout — and never announced: nothing was lost.
    ///
    /// A vertical tone still lost AFTER its retry is ANNOUNCED (AnnounceVerticalLost): its
    /// healthy state is silence, so unlike the lateral tone (continuously audible when
    /// off-centre, so its death is heard as an absence) it can lie to the pilot — a dead
    /// sink-rate cue reads as a perfectly flown flare. Same rule as go-around: silence must
    /// never be mistakable for "on profile". Announced on the tick AFTER the restart attempt,
    /// not with it — speaking on the same tick would announce an outage the restart may be
    /// about to cure.
    /// </summary>
    internal static ToneReArmDecision DecideToneReArm(
        bool lateralStarted, bool lateralNeedsDevice, bool lateralReArmSpent,
        bool verticalStarted, bool verticalNeedsDevice, bool verticalReArmSpent)
    {
        bool lateralLost = lateralStarted && lateralNeedsDevice;
        bool verticalLost = verticalStarted && verticalNeedsDevice;

        // A healthy tone clears ITS latch so a later outage on the same approach re-arms
        // again; a still-lost tone keeps its latch — its retries belong to the router's
        // event-driven sweeps now, or the 1 Hz sampler would attempt a WASAPI open every tick
        // for the rest of the approach (the retry loop docs/audio.md forbids; a FLAPPING
        // endpoint — healthy one tick, faulted the next — still re-enters at 1 Hz, the same
        // bounded residual VG's latch has).
        bool restartLateral = lateralLost && !lateralReArmSpent;
        bool restartVertical = verticalLost && !verticalReArmSpent;

        return new ToneReArmDecision(
            restartLateral, restartVertical,
            SpendLateralReArm: lateralLost,
            SpendVerticalReArm: verticalLost,
            AnnounceVerticalLost: verticalLost && verticalReArmSpent);
    }

    /// <summary>
    /// ~1 Hz while engaged (driven by the INDICATED_ALTITUDE feed, which is change-gated — a
    /// perfectly stationary aircraft stops ticking, harmless here since engagement implies
    /// motion). Restarts a tone whose device went away — see <see cref="DecideToneReArm"/>
    /// for why the manager cannot leave this to the router.
    /// </summary>
    private void ReArmTonesIfDeviceLost()
    {
        ToneReArmDecision d = DecideToneReArm(
            toneStarted, tone.NeedsDevice, lateralReArmSpent,
            verticalToneStarted, verticalTone.NeedsDevice, verticalReArmSpent);

        lateralReArmSpent = d.SpendLateralReArm;
        verticalReArmSpent = d.SpendVerticalReArm;

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

        // Flare only: in the rollout the vertical tone is legitimately down, and under a
        // silent flare visual guidance owns the approach audio (and this tone never started).
        // Once per engagement — the condition holds every tick of a dead-endpoint outage.
        if (d.AnnounceVerticalLost && phase == Phase.Flare && !silentFlare
            && !verticalCueLostAnnounced)
        {
            verticalCueLostAnnounced = true;
            Log.Debug("LandingFlareAssist",
                "Vertical tone still has no device after its restart attempt — speaking the loss");
            announcer.AnnounceImmediate("Flare sink rate cue unavailable");
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

    private void StopLateralTone()
    {
        if (!toneStarted) return;
        tone.Stop();
        toneStarted = false;
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
        StopLateralTone();
        StopVerticalTone();
        phase = Phase.Armed;
        wasAboveFlareBand = false;
        // Both tones are down, so the next engagement starts its own outage accounting —
        // spent latches carried into the next approach would cost it its re-arms, and a stale
        // announce latch would silence the next approach's warning.
        lateralReArmSpent = false;
        verticalReArmSpent = false;
        verticalCueLostAnnounced = false;
        silentFlare = false;
        vsSmootherInitialized = false;
        crossTrackRateInitialized = false;
        crossTrackRateFps = 0.0;
        taxiHandoverPending = false;

        // A runway switch lasts one engagement: circuits keep the pilot's armed choice.
        runwayCorrectionSpoken = false;
        if (armedRunway != null && !ReferenceEquals(runway, armedRunway))
            ApplyActiveRunway(armedRunway);

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

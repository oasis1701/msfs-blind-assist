using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Manages visual landing guidance using dual audio tones.
/// The DESIRED tone encodes the PID-commanded pitch (frequency 200–800 Hz over ±10°) and
/// commanded bank (stereo pan over ±10°). The CURRENT tone (optional, on by default) mirrors
/// the same mapping against the aircraft's *actual* pitch and bank, so frequency match
/// (zero-beat) means correct pitch attitude / vertical speed and pan match means correct bank.
/// Aircraft-specific tunables (approach AoA, Vref, rate caps) come from <see cref="VisualGuidanceProfile"/>.
/// </summary>
public class VisualGuidanceManager : IDisposable
{
    private readonly ScreenReaderAnnouncer announcer;
    private bool isActive = false;

    // Guidance state
    private GuidancePhase currentPhase = GuidancePhase.Intercepting;
    private Runway? runway;
    private Airport? airport;
    private double magneticVariation = 0.0;
    private double thresholdElevationMSL = 0.0;

    // Audio generators — both always run while guidance is active.
    private AudioToneGenerator? desiredAttitudeTone;  // PID-commanded attitude
    private AudioToneGenerator? currentAttitudeTone;  // Aircraft's actual attitude (follower; matched by ear)
    private HandFlyWaveType guidanceWaveType = HandFlyWaveType.Triangle;
    private double guidanceVolume = 0.05; // Default 5%
    private HandFlyWaveType currentToneWaveType = HandFlyWaveType.Sine;
    private double currentToneVolume = 0.05;
    private bool hardPanTone = false;  // see VisualGuidanceHardPanTone setting
    // Defer audible Start() until the first ProcessUpdate computes real pitch/bank — otherwise
    // the user hears ~33 ms of fused 500 Hz center-pan tone that represents nothing. Both tones
    // are instantiated in Initialize so disposal/lifecycle stays simple; this flag controls
    // when WaveOut actually fires up.
    private bool tonesNeedStart = false;

    // Bank (degrees, standard convention) at which hard-pan mode snaps to full left / right.
    // Below this magnitude the tone stays centered, avoiding twitchy flips around 0°.
    private const double HARD_PAN_DEADBAND_DEG = 1.0;

    // Position tracking
    private double? cachedLatitude;
    private double? cachedLongitude;
    private double? cachedAGL;
    private double? cachedAltMSL;
    private double? cachedPitch;
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

    // State tracking for derivative term and rate limiting (lateral)
    private double? previousCrossTrackError = null;  // Used for integral anti-windup sign change detection
    private double previousDesiredBank = 0.0;
    private double? smoothedCrossTrackRate = null;  // Smoothed rate to prevent oscillation during handoff
    private Queue<(double xte, DateTime timestamp)> crossTrackHistory = new Queue<(double, DateTime)>();
    private const int CROSS_TRACK_HISTORY_SIZE = 5;  // 5 samples at 1Hz = 4 second window

    // State tracking for derivative term and rate limiting (vertical)
    private double? previousGlideslopeDeviation = null;  // Reused for FPM error history
    private DateTime? previousGlideslopeTimestamp = null;
    private double previousDesiredPitch = 0.0;

    // Flare state tracking
    private double lastFlarePitch = 3.0;           // For rate limiting during flare

    // Announcement tracking
    private DateTime lastPhaseAnnouncement = DateTime.MinValue;
    private DateTime lastDistanceAnnouncement = DateTime.MinValue;
    private double lastAnnouncedDistance = double.MaxValue;
    private DateTime lastExtendingProgressAnnouncement = DateTime.MinValue;
    private int? lastAnnouncedBankError = null;  // Track last announced bank error for error-based announcements
    private string? lastAnnouncedCenterlineDeviation = null;  // Track last announced centerline deviation
    private const int ANNOUNCEMENT_INTERVAL_MS = 1000;
    private const int EXTENDING_PROGRESS_INTERVAL_MS = 10000;  // 10 seconds

    // Debouncing for behind threshold detection
    private DateTime lastBehindStateChange = DateTime.MinValue;
    private bool wasBehindLastCheck = false;
    private const int STATE_CHANGE_DELAY_MS = 5000; // 5 seconds

    // Guidance constants
    private const double CENTERLINE_TOLERANCE_NM = 0.3;  // On centerline if within 0.3 NM
    private const double GLIDESLOPE_CAPTURE_FT = 100.0;  // Capture glideslope when within 100 ft
    private const double FLARE_ALTITUDE_FT = 30.0;       // Start flare at 30 ft AGL
    private const double TOUCHDOWN_ALTITUDE_FT = 5.0;    // Consider touchdown below 5 ft AGL

    // Flare rate limiting constant
    private const double MAX_FLARE_PITCH_RATE = 1.5;        // Maximum pitch change rate during flare (deg/sec)

    // Stabilization gate: Target 3000 ft AGL at 9 NM for terrain clearance
    private const double STABILIZATION_GATE_NM = 9.0;     // Distance from threshold for stabilization gate
    private const double STABILIZATION_GATE_AGL_FT = 3000.0;  // Target altitude at stabilization gate
    // Standard ILS glideslope angle
    private const double GLIDESLOPE_ANGLE_DEG = 3.0;   // Standard 3° glideslope angle

    // New vertical guidance constants
    private const double GLIDESLOPE_LOCK_DISTANCE_NM = 1.0;  // Distance at which to lock to steady 3° angle
    private const double GLIDESLOPE_GAIN = 2.0;              // Proportional gain: fpm correction per foot of deviation
    private const double MAX_DESCENT_RATE_FPM = -1500.0;     // Maximum descent rate when high
    private const double FLARE_TARGET_PITCH_DEG = 6.0;       // Target pitch in flare

    // Lateral guidance gains (tuned for blind pilot manual landing with audio guidance)
    private const double LATERAL_GAIN_INTERCEPT = 0.5;   // Heading error to bank for intercept
    private const double LATERAL_GAIN_TRACKING = 120.0;    // Cross-track error (NM) to bank for tracking (increased for faster convergence)
    private const double LATERAL_RATE_DAMPING = 12.0;    // Cross-track rate damping
    private const double LATERAL_HEADING_GAIN = 1.0;     // Track/heading error to bank for alignment (Boeing 747: 1.0-2.8)
    // Aircraft-specific tunables — populated from IAircraftDefinition.GetVisualGuidanceProfile() in Initialize().
    // Defaults below are the A320 numbers used historically (preserved when no profile is supplied).
    private double airspeedReferenceKnots = 140.0;       // Reference Vref for sqrt(GS/Vref) lateral gain scaling
    private double maxBankRateDegPerSec = 3.0;           // Cap on commanded bank change rate (deg/sec)

    // Arc mode guidance constants (replaces problematic P/H balance in CAPTURE mode)
    private const double ARC_MODE_ENTRY_NM = 1.5;           // Start arc capture at 1.5 NM (matches INTERCEPT_45 angle)
    private const double ARC_MODE_EXIT_NM = 0.05;           // End arc at 300 feet, switch to precision
    private const double ARC_INTERCEPT_GAIN = 30.0;         // Degrees of intercept angle per NM (tunable: 25-40)
    private const double ARC_MAX_INTERCEPT_ANGLE = 45.0;    // Maximum intercept angle (matches INTERCEPT_45 handoff)
    private const double ARC_RATE_DAMPING = 12.0;           // Damping to prevent overshoot if pilot overbanks
    private const double ARC_BANK_LIMIT = 15.0;             // Gentle bank limit for comfort during arc

    // Vertical guidance — aircraft-specific (set from VisualGuidanceProfile in Initialize).
    private double typicalApproachAoaDeg = 6.0;          // A320 baseline; biases nominal pitch = -3° + AoA
    private double maxPitchRateDegPerSec = 2.5;          // Cap on commanded pitch change rate (deg/sec)
    private const double CROSS_TRACK_RATE_SMOOTHING_FACTOR = 0.85;  // Exponential smoothing for cross-track rate (0.85 = strong filtering to prevent noise spikes)

    // FPM-based vertical guidance constants
    private const double FPM_SMOOTHING_FACTOR = 0.7;     // Exponential smoothing for current FPM (0.7 = moderate filtering)
    private const double FPM_P_GAIN = 0.005;             // Pitch correction per FPM error (degrees per FPM)
    private const double FPM_D_GAIN = 0.002;             // Pitch damping per FPM error rate (degrees per FPM/sec)

    public bool IsActive => isActive;
    public event EventHandler<bool>? VisualGuidanceActiveChanged;

    /// <summary>
    /// Gets the current target FPM for the approach
    /// </summary>
    public double GetTargetFPM() => lastCalculatedTargetFPM;

    /// <summary>
    /// Gets the current altitude deviation from the calculated glideslope profile in feet
    /// (positive = above profile/high, negative = below profile/low)
    /// </summary>
    public double GetAltitudeDeviation() => lastCalculatedAltitudeError;

    /// <summary>
    /// Guidance phases from approach to touchdown
    /// </summary>
    private enum GuidancePhase
    {
        NotStarted,         // Initial state before first position update
        Extending,          // Behind runway, need to extend out
        Intercepting,       // Off centerline, intercepting
        Final,              // On centerline, before glideslope capture
        GlideslopeTracking, // On centerline and glideslope
        Flare,              // Below 30 ft AGL, flaring
        Touchdown           // Below 5 ft AGL, landed
    }

    public VisualGuidanceManager(ScreenReaderAnnouncer screenReaderAnnouncer)
    {
        announcer = screenReaderAnnouncer;
    }

    /// <summary>
    /// Toggles visual guidance mode on/off
    /// </summary>
    public void Toggle()
    {
        isActive = !isActive;
        VisualGuidanceActiveChanged?.Invoke(this, isActive);

        if (!isActive)
        {
            Stop();
        }
    }

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
        // might not), tear down any existing tones so we don't leak audio
        // resources. Safe no-op when both tones are already null.
        if (desiredAttitudeTone != null || currentAttitudeTone != null)
        {
            Stop();
            isActive = true;  // Stop() flips isActive false; restore for the new session
        }

        runway = destinationRunway;
        airport = destinationAirport;
        guidanceWaveType = guidanceToneWaveform;
        guidanceVolume = toneVolume;
        currentToneWaveType = currentToneWaveform;
        currentToneVolume = currentToneVol;
        hardPanTone = hardPan;
        magneticVariation = destinationAirport.MagVar;
        thresholdElevationMSL = destinationAirport.Altitude;

        // Apply aircraft-specific tunables (preserves A320 defaults if profile is the base instance).
        typicalApproachAoaDeg = profile.TypicalApproachAoaDeg;
        airspeedReferenceKnots = profile.ReferenceVrefKnots;
        maxPitchRateDegPerSec = profile.MaxPitchRateDegPerSec;
        maxBankRateDegPerSec = profile.MaxBankRateDegPerSec;

        // Reset state
        currentPhase = GuidancePhase.NotStarted;
        lastPhaseAnnouncement = DateTime.MinValue;
        lastDistanceAnnouncement = DateTime.MinValue;
        lastAnnouncedDistance = double.MaxValue;
        lastExtendingProgressAnnouncement = DateTime.MinValue;
        lastAnnouncedBankError = null;
        cachedLatitude = null;
        cachedLongitude = null;
        cachedAGL = null;
        cachedAltMSL = null;
        cachedPitch = null;
        cachedBank = null;
        cachedHeading = null;
        cachedGroundTrack = null;

        // Reset derivative term tracking (lateral)
        previousCrossTrackError = null;
        previousDesiredBank = 0.0;
        smoothedCrossTrackRate = null;
        crossTrackHistory.Clear();  // Clear multi-sample history buffer

        // Reset derivative term tracking (vertical)
        previousGlideslopeDeviation = null;
        previousGlideslopeTimestamp = null;
        previousDesiredPitch = 0.0;
        smoothedCurrentFPM = null;

        // Reset flare state
        lastFlarePitch = 3.0;

        // Instantiate both generators and apply the aircraft's pitch→frequency mapping. We do
        // NOT Start() them yet — the first ProcessUpdate call kicks off audio output once real
        // pitch/bank values exist, avoiding ~33 ms of meaningless fused-tone playback at the
        // default center frequency / center pan. Configure() must run before Start(), so we
        // call it here even though Start is deferred.
        try
        {
            desiredAttitudeTone = new AudioToneGenerator();
            currentAttitudeTone = new AudioToneGenerator();
            desiredAttitudeTone.Configure(profile.ToneMinFrequencyHz, profile.ToneMaxFrequencyHz, profile.TonePitchRangeDeg);
            currentAttitudeTone.Configure(profile.ToneMinFrequencyHz, profile.ToneMaxFrequencyHz, profile.TonePitchRangeDeg);
            tonesNeedStart = true;
            System.Diagnostics.Debug.WriteLine($"[VisualGuidanceManager] Tones instantiated ({profile.ToneMinFrequencyHz}–{profile.ToneMaxFrequencyHz} Hz over ±{profile.TonePitchRangeDeg}°); deferring Start until first ProcessUpdate");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VisualGuidanceManager] Failed to instantiate tones: {ex.Message}");
            desiredAttitudeTone?.Dispose();
            desiredAttitudeTone = null;
            currentAttitudeTone?.Dispose();
            currentAttitudeTone = null;
            tonesNeedStart = false;
        }

        announcer.AnnounceImmediate($"Visual guidance active, runway {runway.RunwayID}");
        System.Diagnostics.Debug.WriteLine($"[VisualGuidanceManager] Initialized for runway {runway.RunwayID}");
    }

    /// <summary>
    /// Stops visual guidance
    /// </summary>
    public void Stop()
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

        runway = null;
        airport = null;
        isActive = false;
        tonesNeedStart = false;  // next Initialize will rearm it

        announcer.Announce("Visual guidance off");
        System.Diagnostics.Debug.WriteLine("[VisualGuidanceManager] Stopped");
    }

    /// <summary>
    /// First-call audible-Start for both tones. Honors the "follower starts only if reference
    /// started" rule from Initialize. After this returns, both tones are either playing (and
    /// ready to be modulated by UpdatePitch/UpdateBank in the same frame) or null (init failure).
    /// </summary>
    private void StartTonesIfNeeded()
    {
        if (!tonesNeedStart) return;
        tonesNeedStart = false;

        try
        {
            desiredAttitudeTone?.Start(guidanceWaveType, guidanceVolume);
            System.Diagnostics.Debug.WriteLine("[VisualGuidanceManager] Desired-attitude tone started");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VisualGuidanceManager] Failed to start desired-attitude tone: {ex.Message}");
            desiredAttitudeTone?.Dispose();
            desiredAttitudeTone = null;
        }

        if (desiredAttitudeTone != null && currentAttitudeTone != null)
        {
            try
            {
                currentAttitudeTone.Start(currentToneWaveType, currentToneVolume);
                System.Diagnostics.Debug.WriteLine("[VisualGuidanceManager] Current-attitude tone started");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VisualGuidanceManager] Failed to start current-attitude tone: {ex.Message}");
                currentAttitudeTone.Dispose();
                currentAttitudeTone = null;
            }
        }
        else
        {
            // Reference failed → the follower would play a useless constant tone; tear it down.
            currentAttitudeTone?.Dispose();
            currentAttitudeTone = null;
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
        cachedPitch = pitchDegrees;
        currentPitch = pitchDegrees;  // Also update for dynamic pitch calculation
    }
    public void UpdateBank(double bankDegrees) => cachedBank = bankDegrees;
    public void UpdateHeading(double headingDegrees) => cachedHeading = headingDegrees;
    public void UpdateGroundTrack(double groundTrackDegrees) => cachedGroundTrack = groundTrackDegrees;

    /// <summary>
    /// Updates performance data for dynamic pitch calculation
    /// </summary>
    public void UpdateGroundSpeed(double groundSpeedKnots) => currentGroundSpeedKnots = groundSpeedKnots;
    public void UpdateVerticalSpeed(double verticalSpeedFPM) => currentVerticalSpeedFPM = verticalSpeedFPM;

    /// <summary>
    /// Processes all cached data when complete position update is available
    /// Called every second (1 Hz) from MainForm
    /// </summary>
    public void ProcessUpdate()
    {
        if (!isActive || runway == null || desiredAttitudeTone == null)
        {
            return;
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

        try
        {
            // Update phase based on position
            UpdatePhase(lat, lon, agl, altMSL);

            // Calculate guidance
            double desiredBank = CalculateDesiredBank(lat, lon, heading);
            double desiredPitch = CalculateDesiredPitch(lat, lon, agl, altMSL);
            double currentBankStandard = StandardBank(cachedBank ?? 0.0);

            // First-frame deferred Start — by the time WaveOut's 150 ms buffer fills, the
            // phase-continuous oscillator's portamento has reached the target frequency
            // (~0.23 ms at 44.1 kHz), so the very first audible note already reflects the
            // commanded / actual attitude. No fused-tone glitch at session start.
            StartTonesIfNeeded();

            // Update desired (PID-commanded) attitude tone
            desiredAttitudeTone.UpdatePitch(desiredPitch);
            ApplyBank(desiredAttitudeTone, desiredBank);

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

            // Centerline deviation announcements (for testing)
            AnnounceCenterlineDeviation(lat, lon);

            // Distance callouts
            HandleDistanceCallouts(lat, lon);

            // Extending phase progress updates
            HandleExtendingProgress(lat, lon);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VisualGuidanceManager] Error processing update: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates current guidance phase based on position
    /// </summary>
    private void UpdatePhase(double lat, double lon, double agl, double altMSL)
    {
        if (runway == null) return;

        // Calculate AGL from MSL for consistency with FPM-based vertical guidance
        // SimConnect AGL sensor can be stale/inaccurate when on ground
        double calculatedAGL = altMSL - thresholdElevationMSL;

        // Diagnostic logging to help identify sensor issues
        System.Diagnostics.Debug.WriteLine(
            $"[VisualGuidance] AGL: SimConnect={agl:F1}ft, Calculated={calculatedAGL:F1}ft, Diff={Math.Abs(agl - calculatedAGL):F1}ft");

        GuidancePhase previousPhase = currentPhase;

        // Check for touchdown first
        if (calculatedAGL <= TOUCHDOWN_ALTITUDE_FT)
        {
            currentPhase = GuidancePhase.Touchdown;
            if (previousPhase != currentPhase)
            {
                announcer.Announce("Touchdown");
                // Continue guidance for centerline tracking during rollout
            }
            return;
        }

        // Check for flare
        if (calculatedAGL <= FLARE_ALTITUDE_FT)
        {
            currentPhase = GuidancePhase.Flare;
            if (previousPhase != currentPhase)
            {
                // Capture entry state
                lastFlarePitch = previousDesiredPitch;  // Start from current guidance pitch
                AnnouncePhaseChange("Flare");
            }
            return;
        }

        // Calculate position relative to runway
        double distance = NavigationCalculator.CalculateDistance(
            lat, lon, runway.StartLat, runway.StartLon);

        double crossTrackError = NavigationCalculator.CalculateDistanceToLocalizer(
            lat, lon, runway.StartLat, runway.StartLon, runway.Heading);

        // Check if behind runway (need to extend)
        // Exclude if below 500 ft AGL - aircraft is landing, continue guidance through touchdown
        if (calculatedAGL > 500.0 && IsBehindRunway(lat, lon))
        {
            currentPhase = GuidancePhase.Extending;
            if (previousPhase != currentPhase)
            {
                AnnouncePhaseChange("Extending, turn to heading " +
                    CalculateExtensionHeading().ToString("F0"));
            }
            return;
        }

        // Check if significantly off centerline (only enter Intercepting from initial states)
        // Once on Final/Glideslope, stay there - tones will guide back to centerline
        if (crossTrackError > CENTERLINE_TOLERANCE_NM &&
            (currentPhase == GuidancePhase.NotStarted || currentPhase == GuidancePhase.Intercepting))
        {
            currentPhase = GuidancePhase.Intercepting;
            if (previousPhase != currentPhase)
            {
                // Format distance to final (1 decimal place)
                string distanceText = crossTrackError < 1.0
                    ? $"{crossTrackError:F1} mile"
                    : $"{crossTrackError:F1} miles";

                AnnouncePhaseChange($"Intercepting final, {distanceText} to final");
            }
            return;
        }

        // On centerline - check glideslope
        double glideslopeDeviation = NavigationCalculator.CalculateGlideslopeDeviation(
            altMSL,                                      // Aircraft altitude MSL
            distance,                                     // Distance from threshold in NM
            GLIDESLOPE_ANGLE_DEG,                        // 3-degree glideslope
            thresholdElevationMSL);                      // Threshold elevation MSL

        if (Math.Abs(glideslopeDeviation) < GLIDESLOPE_CAPTURE_FT)
        {
            currentPhase = GuidancePhase.GlideslopeTracking;
            if (previousPhase != currentPhase)
            {
                AnnouncePhaseChange("On vertical profile");
            }
        }
        else
        {
            currentPhase = GuidancePhase.Final;
            if (previousPhase != currentPhase)
            {
                AnnouncePhaseChange("Established on centerline");
            }
        }
    }

    /// <summary>
    /// Calculates desired bank angle for lateral guidance
    /// </summary>
    private double CalculateDesiredBank(double lat, double lon, double heading)
    {
        if (runway == null) return 0.0;

        double crossTrackError = NavigationCalculator.CalculateDistanceToLocalizer(
            lat, lon, runway.StartLat, runway.StartLon, runway.Heading);

        if (currentPhase == GuidancePhase.Extending)
        {
            // Guide to extension heading
            double extensionHeading = CalculateExtensionHeading();
            double headingError = NormalizeHeading(extensionHeading - heading);
            double desiredBank = Math.Clamp(headingError * LATERAL_GAIN_INTERCEPT, -20.0, 20.0);

            return desiredBank;
        }
        else
        {
            // Enhanced PD controller with airspeed compensation for centerline tracking
            // Get signed cross-track error for direction (positive = right, negative = left)
            double signedCrossTrack = NavigationCalculator.CalculateCrossTrackError(
                lat, lon, runway.StartLat, runway.StartLon, runway.Heading);
            // Apply sign to distance (crossTrackError is unsigned distance in NM)
            double signedCrossTrackNM = Math.Sign(signedCrossTrack) * crossTrackError;

            // Calculate cross-track rate (derivative term) for damping using multi-sample averaging
            // Add current sample to history buffer
            crossTrackHistory.Enqueue((signedCrossTrackNM, DateTime.Now));

            // Maintain history size limit (rolling window)
            while (crossTrackHistory.Count > CROSS_TRACK_HISTORY_SIZE)
            {
                crossTrackHistory.Dequeue();
            }

            // Calculate rate over entire history window (oldest to newest)
            // This filters out single-frame noise that caused 0.000 ↔ 0.020 oscillation
            double crossTrackRate = 0.0;
            if (crossTrackHistory.Count >= 2)
            {
                var oldest = crossTrackHistory.First();
                var newest = crossTrackHistory.Last();
                double deltaTime = (newest.timestamp - oldest.timestamp).TotalSeconds;

                if (deltaTime > 0.01)  // Avoid division by zero
                {
                    double rawCrossTrackRate = (newest.xte - oldest.xte) / deltaTime;

                    // Apply exponential smoothing to reduce noise (prevents oscillation during phase handoff)
                    if (smoothedCrossTrackRate.HasValue)
                    {
                        crossTrackRate = CROSS_TRACK_RATE_SMOOTHING_FACTOR * smoothedCrossTrackRate.Value +
                                        (1.0 - CROSS_TRACK_RATE_SMOOTHING_FACTOR) * rawCrossTrackRate;
                    }
                    else
                    {
                        crossTrackRate = rawCrossTrackRate;
                    }

                    smoothedCrossTrackRate = crossTrackRate;
                }
            }

            // Update tracking state for integral anti-windup logic (sign change detection)
            previousCrossTrackError = signedCrossTrackNM;

            // Ground track vs heading selection based on altitude
            // High altitude (>100 ft AGL): Use ground track to maintain centerline (allows crab in wind)
            // Low altitude (≤100 ft AGL): Use heading to align nose with runway (decrab for landing)
            double actualTrackAngle;
            if (cachedAGL.HasValue && cachedAGL.Value <= 100.0)
            {
                actualTrackAngle = heading;  // Force heading alignment for decrab below 100 ft
            }
            else
            {
                actualTrackAngle = cachedGroundTrack ?? heading;  // Normal tracking above 100 ft
            }
            double trackAngleError = NormalizeHeading(runway.HeadingMag - actualTrackAngle);

            // Airspeed compensation scaling
            // Higher speeds need stronger corrections (compensates for larger turn radius)
            double speedFactor = Math.Sqrt(Math.Max(currentGroundSpeedKnots, 80.0) / airspeedReferenceKnots);

            // **TARGET HEADING INTERCEPT LOGIC**
            // Phase-based approach matching real autopilot behavior
            // INTERCEPT phases: Calculate and fly to target intercept heading
            // ARC MODE: Smooth arc capture from 1.5 to 0.05 NM
            // PRECISION TRACK: Final PDH control below 0.05 NM

            double absXTE = Math.Abs(signedCrossTrackNM);
            double desiredBank;
            string guidancePhase;

            // Phase determination and guidance calculation
            if (absXTE > 2.0)
            {
                // INTERCEPT PHASE 1: Far from centerline - 60° intercept
                double interceptAngle = 60.0;
                double targetHeading = signedCrossTrackNM < 0
                    ? runway.HeadingMag + interceptAngle
                    : runway.HeadingMag - interceptAngle;
                targetHeading = NormalizeHeading(targetHeading);

                desiredBank = CalculateHeadingInterceptBank(targetHeading, actualTrackAngle);
                guidancePhase = "INTERCEPT_60";
            }
            else if (absXTE > ARC_MODE_ENTRY_NM)  // 1.0 NM
            {
                // INTERCEPT PHASE 2: Medium distance - 45° intercept
                double interceptAngle = 45.0;
                double targetHeading = signedCrossTrackNM < 0
                    ? runway.HeadingMag + interceptAngle
                    : runway.HeadingMag - interceptAngle;
                targetHeading = NormalizeHeading(targetHeading);

                desiredBank = CalculateHeadingInterceptBank(targetHeading, actualTrackAngle);
                guidancePhase = "INTERCEPT_45";
            }
            else if (absXTE > ARC_MODE_EXIT_NM)  // 0.05 NM
            {
                // ========== ARC CAPTURE MODE ==========
                // Calculates target intercept heading that creates smooth arc to centerline
                // Intercept angle reduces proportionally as aircraft approaches line
                // At 1.5 NM: 45° intercept (matches INTERCEPT_45) → At 0.05 NM: 1.5° intercept
                // This naturally aligns heading while approaching centerline

                guidancePhase = "ARC_CAPTURE";

                // Calculate intercept angle based on distance from centerline
                // Negative sign: left of centerline (negative XTE) → positive intercept angle
                double interceptAngle = -ARC_INTERCEPT_GAIN * signedCrossTrackNM;

                // Add damping to prevent overshoot if approaching too fast
                // (crossTrackRate already calculated above at line 494)
                double dampedInterceptAngle = interceptAngle - (ARC_RATE_DAMPING * crossTrackRate);

                // Limit to reasonable range
                dampedInterceptAngle = Math.Clamp(dampedInterceptAngle, -ARC_MAX_INTERCEPT_ANGLE, ARC_MAX_INTERCEPT_ANGLE);

                // Calculate target heading for the arc
                double targetHeading = runway.HeadingMag + dampedInterceptAngle;
                targetHeading = NormalizeHeading(targetHeading);

                // Simple heading controller to fly the arc
                double headingError = NormalizeHeading(targetHeading - actualTrackAngle);
                double rawDesiredBank = headingError * LATERAL_HEADING_GAIN * speedFactor;

                // Apply bank rate limiting
                double maxBankChange = maxBankRateDegPerSec * 1.0;
                double bankChange = rawDesiredBank - previousDesiredBank;
                if (Math.Abs(bankChange) > maxBankChange)
                {
                    rawDesiredBank = previousDesiredBank + Math.Sign(bankChange) * maxBankChange;
                }

                // Apply bank limit for comfort
                desiredBank = Math.Clamp(rawDesiredBank, -ARC_BANK_LIMIT, ARC_BANK_LIMIT);

                // Calculate distance to threshold for debugging
                double distanceNM = NavigationCalculator.CalculateDistance(
                    lat, lon, runway.StartLat, runway.StartLon);

                // Debug output for arc mode
                System.Diagnostics.Debug.WriteLine(
                    $"[VisualGuidance] {guidancePhase}: XTE={signedCrossTrackNM:F3}NM, Rate={crossTrackRate:F3}NM/s, " +
                    $"IntAngle={interceptAngle:F1}°, Damped={dampedInterceptAngle:F1}°, " +
                    $"TgtHdg={targetHeading:F1}°, ActTrk={actualTrackAngle:F1}°, HdgErr={headingError:F1}°, " +
                    $"Bank={desiredBank:F1}°, Dist={distanceNM:F1}NM, GS={currentGroundSpeedKnots:F0}kt");
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

                // Apply bank rate limiting
                double maxBankChange = maxBankRateDegPerSec * 1.0;
                double bankChange = rawDesiredBank - previousDesiredBank;
                if (Math.Abs(bankChange) > maxBankChange)
                {
                    rawDesiredBank = previousDesiredBank + Math.Sign(bankChange) * maxBankChange;
                }

                // Tight bank limit for precision
                desiredBank = Math.Clamp(rawDesiredBank, -10.0, 10.0);

                // Calculate distance to threshold for debugging
                double distanceNM = NavigationCalculator.CalculateDistance(
                    lat, lon, runway.StartLat, runway.StartLon);

                // Debug output for precision mode
                System.Diagnostics.Debug.WriteLine(
                    $"[VisualGuidance] {guidancePhase}: XTE={signedCrossTrackNM:F3}NM, Rate={crossTrackRate:F3}NM/s, " +
                    $"TrkErr={trackAngleError:F1}°, P={proportionalTerm:F2}° D={derivativeTerm:F2}° H={headingTerm:F2}° " +
                    $"→ Bank={desiredBank:F1}°, Dist={distanceNM:F1}NM, GS={currentGroundSpeedKnots:F0}kt");
            }

            // Update for next iteration
            previousDesiredBank = desiredBank;

            // Magnetic reference debugging
            double impliedMagVar = cachedGroundTrack.HasValue ?
                NormalizeHeading(cachedGroundTrack.Value - heading) : 0.0;

            System.Diagnostics.Debug.WriteLine(
                $"[VisualGuidance] MAGNETIC DEBUG: RunwayMag={runway.HeadingMag:F1}° AircraftHdg={heading:F1}° " +
                $"GroundTrack={cachedGroundTrack?.ToString("F1") ?? "N/A"}° ActualTrack={actualTrackAngle:F1}° " +
                $"TrackErr={trackAngleError:F1}° ImpliedMagVar={impliedMagVar:F1}°");

            // Phase-specific debug output (INTERCEPT phases only - CAPTURE_TRACK logs inside its block)
            if (guidancePhase.StartsWith("INTERCEPT"))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[VisualGuidance] {guidancePhase}: XTE={signedCrossTrackNM:F3}NM, " +
                    $"ActualTrk={actualTrackAngle:F1}°, TrkErr={trackAngleError:F1}°, " +
                    $"GS={currentGroundSpeedKnots:F0}kt → Bank={desiredBank:F1}°");
            }

            return desiredBank;
        }
    }

    /// <summary>
    /// Calculates bank angle needed to intercept and hold a target heading
    /// Used during INTERCEPT phases (>0.5 NM from centerline)
    /// </summary>
    private double CalculateHeadingInterceptBank(double targetHeading, double actualTrack)
    {
        // Calculate heading error (how far we are from target heading)
        double headingError = NormalizeHeading(targetHeading - actualTrack);

        // Proportional controller: 0.5° of bank per degree of heading error
        // This provides smooth turn to reach target heading
        const double HEADING_GAIN = 0.5;
        double baseBank = headingError * HEADING_GAIN;

        // Clamp to safe bank limits for intercept (±25°)
        double desiredBank = Math.Clamp(baseBank, -25.0, 25.0);

        // Apply bank rate limiting to prevent sudden changes
        double maxBankChange = maxBankRateDegPerSec * 1.0;  // 1 second update rate
        double bankChange = desiredBank - previousDesiredBank;
        if (Math.Abs(bankChange) > maxBankChange)
        {
            desiredBank = previousDesiredBank + Math.Sign(bankChange) * maxBankChange;
        }

        return desiredBank;
    }

    /// <summary>
    /// Calculates desired pitch angle for vertical guidance
    /// </summary>
    private double CalculateDesiredPitch(double lat, double lon, double agl, double altMSL)
    {
        System.Diagnostics.Debug.WriteLine($"[VisualGuidance] CalculateDesiredPitch called: Phase={currentPhase}, AGL={agl:F0}ft, Runway={(runway != null ? "OK" : "NULL")}, GS={currentGroundSpeedKnots:F1}kt, VS={currentVerticalSpeedFPM:F0}fpm, CurPitch={currentPitch:F2}°");

        if (runway == null)
        {
            System.Diagnostics.Debug.WriteLine("[VisualGuidance] Runway is NULL - returning 0.0");
            return 0.0;
        }

        if (currentPhase == GuidancePhase.Flare)
        {
            // Simplified flare: Command constant +6° pitch
            double targetPitch = FLARE_TARGET_PITCH_DEG;

            // Apply rate limiting for smooth transition from approach to flare
            double maxPitchChange = MAX_FLARE_PITCH_RATE * 1.0;  // 1 second update rate
            double pitchChange = targetPitch - lastFlarePitch;
            if (Math.Abs(pitchChange) > maxPitchChange)
            {
                targetPitch = lastFlarePitch + Math.Sign(pitchChange) * maxPitchChange;
            }

            lastFlarePitch = targetPitch;

            System.Diagnostics.Debug.WriteLine(
                $"[VisualGuidance] Flare: AGL={agl:F0}ft, Commanding pitch: {targetPitch:F1}°");

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

            // Calculate ideal altitude on 3° glideslope from threshold
            // idealAltitude = distanceToThreshold × tan(3°) + thresholdElevation
            double distanceFt = distanceToThresholdNM * 6076.12;
            double glideslopeAngleRad = GLIDESLOPE_ANGLE_DEG * Math.PI / 180.0;
            double idealAltitudeAGL = distanceFt * Math.Tan(glideslopeAngleRad);

            // Calculate altitude error (positive = too high, negative = too low)
            double altitudeError = currentAGL - idealAltitudeAGL;

            // Apply exponential smoothing to current FPM to reduce oscillation
            double smoothedFPM;
            if (smoothedCurrentFPM.HasValue)
            {
                smoothedFPM = FPM_SMOOTHING_FACTOR * smoothedCurrentFPM.Value +
                             (1.0 - FPM_SMOOTHING_FACTOR) * currentVerticalSpeedFPM;
            }
            else
            {
                smoothedFPM = currentVerticalSpeedFPM;
            }
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

                System.Diagnostics.Debug.WriteLine(
                    $"[VisualGuidance] LOCK MODE: Dist={distanceToThresholdNM:F2}nm, " +
                    $"Maintaining steady 3° descent: {targetFPM:F0}fpm");
            }
            else
            {
                // CORRECTION MODE: Proportional control on glideslope deviation
                // Correction is proportional to altitude error, not distance/time dependent
                // This provides smooth, natural convergence to the glideslope
                double correctionRateFPM = -altitudeError * GLIDESLOPE_GAIN;
                targetFPM = natural3DegDescentRateFPM + correctionRateFPM;

                // Apply safety limits (never command climb, limit max descent)
                targetFPM = Math.Clamp(targetFPM, MAX_DESCENT_RATE_FPM, 0.0);

                System.Diagnostics.Debug.WriteLine(
                    $"[VisualGuidance] CORRECTION MODE: AltErr={altitudeError:F0}ft, " +
                    $"Natural={natural3DegDescentRateFPM:F0}fpm, Correction={correctionRateFPM:F0}fpm, " +
                    $"Target={targetFPM:F0}fpm, Dist={distanceToThresholdNM:F2}nm");
            }

            // Store for hotkey announcement
            lastCalculatedTargetFPM = targetFPM;
            lastCalculatedAltitudeError = altitudeError;

            // Calculate FPM error (negative = need to descend more, positive = need to descend less)
            double fpmError = targetFPM - smoothedFPM;

            // Calculate FPM error rate for derivative term
            double fpmErrorRate = 0.0;
            if (previousGlideslopeDeviation.HasValue && previousGlideslopeTimestamp.HasValue)
            {
                double deltaTime = (DateTime.Now - previousGlideslopeTimestamp.Value).TotalSeconds;
                if (deltaTime > 0.01)
                {
                    // Using previousGlideslopeDeviation to store previous FPM error
                    fpmErrorRate = (fpmError - previousGlideslopeDeviation.Value) / deltaTime;
                }
            }

            // Update tracking state for next iteration
            previousGlideslopeDeviation = fpmError;  // Reusing field for FPM error history
            previousGlideslopeTimestamp = DateTime.Now;

            // Calculate nominal pitch for descent
            double nominalPitch = -GLIDESLOPE_ANGLE_DEG + typicalApproachAoaDeg;  // A320 default ≈ +3°; 777 ≈ +1.5°

            // PD controller on FPM error:
            // Proportional: Correct based on FPM error
            // Derivative: Dampen based on rate of FPM error change
            double proportionalTerm = -fpmError * FPM_P_GAIN;
            double derivativeTerm = -fpmErrorRate * FPM_D_GAIN;

            double rawDesiredPitch = nominalPitch + proportionalTerm + derivativeTerm;

            // Pitch rate limiting - prevent sudden tone frequency jumps
            double maxPitchChange = maxPitchRateDegPerSec * 1.0;  // 1 second update rate
            double pitchChange = rawDesiredPitch - previousDesiredPitch;
            if (Math.Abs(pitchChange) > maxPitchChange)
            {
                rawDesiredPitch = previousDesiredPitch + Math.Sign(pitchChange) * maxPitchChange;
            }

            // Clamp final command
            double desiredPitch = Math.Clamp(rawDesiredPitch, -12.0, 12.0);

            // Update for next iteration
            previousDesiredPitch = desiredPitch;

            System.Diagnostics.Debug.WriteLine(
                $"[VisualGuidance] Vertical: Target={targetFPM:F0}fpm, Current={smoothedFPM:F0}fpm, Error={fpmError:F0}fpm, " +
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
    /// Checks if aircraft is behind the runway threshold
    /// </summary>
    private bool IsBehindRunway(double lat, double lon)
    {
        if (runway == null) return false;

        // Safety check: Don't make determination if too close (bearing becomes unstable)
        const double MINIMUM_DISTANCE_NM = 0.1; // ~600 feet
        double distanceToThreshold = NavigationCalculator.CalculateDistance(
            lat, lon, runway.StartLat, runway.StartLon);

        if (distanceToThreshold < MINIMUM_DISTANCE_NM)
        {
            return false; // Too close to make reliable bearing determination
        }

        // Calculate bearing from threshold to aircraft (returns true bearing)
        double bearingToAircraft = NavigationCalculator.CalculateBearing(
            runway.StartLat, runway.StartLon, lat, lon);

        // Aircraft is behind if bearing matches runway direction (±90°)
        // Runway heading points in the direction aircraft goes after takeoff/landing
        double runwayHeading = runway.Heading;

        // If bearing to aircraft is within ±90° of runway heading, aircraft is behind threshold
        double bearingDiff = Math.Abs(NormalizeHeading(bearingToAircraft - runwayHeading));
        bool currentlyBehind = bearingDiff < 90.0;

        // Debouncing: Prevent rapid state changes
        if (currentlyBehind != wasBehindLastCheck)
        {
            // State is trying to change - check if enough time has passed
            if ((DateTime.Now - lastBehindStateChange).TotalMilliseconds < STATE_CHANGE_DELAY_MS)
            {
                // Not enough time passed, return previous state (ignore this change)
                return wasBehindLastCheck;
            }
            // Enough time passed, allow state change
            lastBehindStateChange = DateTime.Now;
        }

        // Update tracking and return current state
        wasBehindLastCheck = currentlyBehind;
        return currentlyBehind;
    }

    /// <summary>
    /// Calculates heading to extend away from runway when behind threshold.
    /// Returns reciprocal (opposite) heading to fly away and create proper spacing.
    /// </summary>
    private double CalculateExtensionHeading()
    {
        if (runway == null) return 0.0;

        // Fly reciprocal (opposite direction) to extend away from runway
        // This creates proper spacing before turning for approach
        double runwayTrueHeading = runway.Heading;
        double reciprocalTrueHeading = (runwayTrueHeading + 180.0) % 360.0;
        double reciprocalMagneticHeading = reciprocalTrueHeading - magneticVariation;

        // Normalize to 0-360
        return (reciprocalMagneticHeading + 360.0) % 360.0;
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
    /// Announces phase changes
    /// </summary>
    private void AnnouncePhaseChange(string message)
    {
        var now = DateTime.Now;
        if ((now - lastPhaseAnnouncement).TotalMilliseconds >= ANNOUNCEMENT_INTERVAL_MS)
        {
            announcer.Announce(message);
            lastPhaseAnnouncement = now;
        }
    }

    /// <summary>
    /// Handles distance callouts (5, 3, 1 miles)
    /// </summary>
    private void HandleDistanceCallouts(double lat, double lon)
    {
        if (runway == null) return;

        var now = DateTime.Now;
        if ((now - lastDistanceAnnouncement).TotalMilliseconds < ANNOUNCEMENT_INTERVAL_MS)
            return;

        double distanceNM = NavigationCalculator.CalculateDistance(
            lat, lon, runway.StartLat, runway.StartLon);

        // Announce at 5, 3, 1 miles
        double[] calloutDistances = { 5.0, 3.0, 1.0 };

        foreach (double calloutDist in calloutDistances)
        {
            if (distanceNM <= calloutDist && lastAnnouncedDistance > calloutDist)
            {
                announcer.Announce($"{calloutDist} mile{(calloutDist > 1 ? "s" : "")}");
                lastAnnouncedDistance = calloutDist;
                lastDistanceAnnouncement = now;
                break;
            }
        }
    }

    /// <summary>
    /// Handles periodic progress announcements during Extending phase
    /// </summary>
    private void HandleExtendingProgress(double lat, double lon)
    {
        if (runway == null || currentPhase != GuidancePhase.Extending) return;

        var now = DateTime.Now;
        if ((now - lastExtendingProgressAnnouncement).TotalMilliseconds < EXTENDING_PROGRESS_INTERVAL_MS)
            return;

        // Calculate distance to threshold
        double distanceNM = NavigationCalculator.CalculateDistance(
            lat, lon, runway.StartLat, runway.StartLon);

        // Round to nearest 0.5 NM for cleaner announcements
        double roundedDistance = Math.Round(distanceNM * 2.0) / 2.0;

        if (roundedDistance >= 0.5)
        {
            string distanceText = roundedDistance == 1.0 ? "1 mile" : $"{roundedDistance} miles";
            announcer.Announce($"{distanceText} behind threshold");
            lastExtendingProgressAnnouncement = now;
        }
        else if (roundedDistance < 0.5 && roundedDistance >= 0.1)
        {
            announcer.Announce("Approaching threshold");
            lastExtendingProgressAnnouncement = now;
        }
    }

    /// <summary>
    /// Announces bank error for verbal guidance feedback (error-based announcements)
    /// </summary>
    private void AnnounceBankGuidance(double desiredBankDegrees)
    {
        string announcement;
        int roundedError;

        // If actual bank angle is available, use error-based announcements
        if (cachedBank.HasValue)
        {
            // Bank error: positive ⇒ need to roll right, negative ⇒ need to roll left.
            // Both operands now in the same (right-positive standard) convention via StandardBank().
            double bankError = desiredBankDegrees - StandardBank(cachedBank.Value);
            roundedError = (int)Math.Round(bankError);

            // Round commanded bank to check if on correct path
            int roundedCommandedBank = (int)Math.Round(desiredBankDegrees);

            // Check if we're on the correct path (commanded bank ≈ 0°)
            bool onCorrectPath = Math.Abs(desiredBankDegrees) < 0.5;  // Within 0.5° of 0 is "on path"

            // Check if actual bank matches commanded bank (within 0.4° tolerance)
            bool matched = Math.Abs(bankError) <= 0.4;

            if (matched)
            {
                // Special announcement if on correct path and wings level
                if (onCorrectPath)
                {
                    announcement = "On commanded path";
                }
                else
                {
                    announcement = "matched";
                }
                roundedError = 0;  // Use 0 for tracking since we're matched
            }
            else
            {
                // Announce the bank error (how much correction is needed)
                if (roundedError < 0)
                {
                    announcement = $"{Math.Abs(roundedError)} left";
                }
                else if (roundedError > 0)
                {
                    announcement = $"{roundedError} right";
                }
                else
                {
                    // Rounded to 0 but not within strict 0.3° tolerance
                    announcement = "matched";
                }
            }
        }
        else
        {
            // No actual bank data available - cannot provide error-based guidance
            return;
        }

        // Only announce if error changed from last announcement
        if (lastAnnouncedBankError.HasValue && roundedError == lastAnnouncedBankError.Value)
            return;

        // Announce immediately
        announcer.AnnounceImmediate(announcement);
        lastAnnouncedBankError = roundedError;
    }

    /// <summary>
    /// Announces centerline deviation for testing and situational awareness
    /// </summary>
    private void AnnounceCenterlineDeviation(double lat, double lon)
    {
        if (runway == null) return;

        // Calculate cross-track error (distance from centerline)
        double crossTrackErrorNM = NavigationCalculator.CalculateDistanceToLocalizer(
            lat, lon, runway.StartLat, runway.StartLon, runway.Heading);

        // Get signed cross-track error to determine direction
        double signedCrossTrack = NavigationCalculator.CalculateCrossTrackError(
            lat, lon, runway.StartLat, runway.StartLon, runway.Heading);

        // Determine direction and format announcement
        string announcement;
        const double ON_CENTER_THRESHOLD = 0.05; // Within 0.05 NM considered "on center"

        if (crossTrackErrorNM <= ON_CENTER_THRESHOLD)
        {
            announcement = "on runway center";
        }
        else
        {
            // Round to 0.1 NM precision
            double roundedDistance = Math.Round(crossTrackErrorNM, 1);

            // Determine direction (positive = right, negative = left)
            string direction = signedCrossTrack > 0 ? "right" : "left";

            announcement = $"{roundedDistance:F1} nm {direction} of center";
        }

        // Only announce if changed from last announcement
        if (lastAnnouncedCenterlineDeviation == announcement)
            return;

        // Announce immediately for testing feedback
        announcer.AnnounceImmediate(announcement);
        lastAnnouncedCenterlineDeviation = announcement;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Manages visual landing guidance using dual audio tones.
/// Provides real-time guidance from intercept through touchdown using current and desired attitude tones.
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
    private double touchdownAimPointLat = 0.0;  // Calculated aim point for glideslope
    private double touchdownAimPointLon = 0.0;

    // Audio generators
    private AudioToneGenerator? desiredAttitudeTone;  // Guidance tone
    private HandFlyWaveType guidanceWaveType = HandFlyWaveType.Triangle;
    private double guidanceVolume = 0.05; // Default 5%

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

    // State tracking for derivative term and rate limiting (lateral)
    private double? previousCrossTrackError = null;
    private DateTime? previousCrossTrackTimestamp = null;
    private double previousDesiredBank = 0.0;

    // Integral term accumulation (lateral PID)
    private double crossTrackIntegral = 0.0;

    // State tracking for derivative term and rate limiting (vertical)
    private double? previousGlideslopeDeviation = null;
    private DateTime? previousGlideslopeTimestamp = null;
    private double previousDesiredPitch = 0.0;

    // Integral term accumulation (vertical PID)
    private double glideslopeIntegral = 0.0;

    // Announcement tracking
    private DateTime lastPhaseAnnouncement = DateTime.MinValue;
    private DateTime lastDistanceAnnouncement = DateTime.MinValue;
    private double lastAnnouncedDistance = double.MaxValue;
    private DateTime lastExtendingProgressAnnouncement = DateTime.MinValue;
    private int? lastAnnouncedBankDegrees = null;  // Track last announced bank angle
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
    private const double FLARE_ALTITUDE_FT = 50.0;       // Start flare at 50 ft AGL
    private const double TOUCHDOWN_ALTITUDE_FT = 5.0;    // Consider touchdown below 5 ft AGL

    // Stabilization gate: Target 3000 ft AGL at 9 NM for terrain clearance
    private const double STABILIZATION_GATE_NM = 9.0;     // Distance from threshold for stabilization gate
    private const double STABILIZATION_GATE_AGL_FT = 3000.0;  // Target altitude at stabilization gate
    // Calculate glideslope angle to pass through stabilization gate: atan(3000 ft / (9 NM × 6076 ft/NM)) ≈ 3.14°
    private const double GLIDESLOPE_ANGLE_DEG = 3.143;   // Modified glideslope for 3000 ft at 9 NM

    private const double TOUCHDOWN_AIM_POINT_FT = 1500.0;  // Aim point distance from threshold
    private const double TOUCHDOWN_AIM_POINT_RATIO = 0.2;  // Use 20% of runway length for short runways

    // Lateral guidance gains (tuned for A320 - Phase 1 moderate settings)
    private const double LATERAL_GAIN_INTERCEPT = 0.5;   // Heading error to bank for intercept
    private const double LATERAL_GAIN_TRACKING = 5.0;    // Cross-track error (NM) to bank for tracking
    private const double LATERAL_RATE_DAMPING = 15.0;    // Cross-track rate (NM/sec) to bank damping
    private const double LATERAL_INTEGRAL_GAIN = 0.5;    // Integral gain for accumulated cross-track error
    private const double INTEGRAL_LIMIT_LATERAL = 10.0;  // Anti-windup limit (bank degrees)
    private const double HEADING_ALIGNMENT_GAIN = 0.3;   // Track angle error to bank for heading alignment
    private const double AIRSPEED_REFERENCE_KNOTS = 140.0;  // Reference speed for gain scaling
    private const double MAX_BANK_RATE_DEG_PER_SEC = 5.0;   // Maximum bank command change rate

    // Vertical guidance gains (tuned for A320 - Phase 1 moderate settings)
    private const double VERTICAL_GAIN = 0.5;            // Glideslope deviation (per 200 ft) to pitch correction
    private const double VERTICAL_RATE_DAMPING = 0.15;   // Glideslope rate (ft/sec) to pitch damping
    private const double VERTICAL_INTEGRAL_GAIN = 0.3;   // Integral gain for accumulated glideslope error
    private const double INTEGRAL_LIMIT_VERTICAL = 5.0;  // Anti-windup limit (pitch degrees)
    private const double TYPICAL_APPROACH_AOA = 6.0;     // Typical angle of attack for A320 approach configuration
    private const double MAX_PITCH_RATE_DEG_PER_SEC = 2.5;  // Maximum pitch command change rate
    private const double FPM_PER_DEGREE_PITCH = 175.0;   // Typical vertical speed change per degree of pitch at approach speeds (legacy, may remove)

    public bool IsActive => isActive;
    public event EventHandler<bool>? VisualGuidanceActiveChanged;

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
        Flare,              // Below 50 ft AGL, flaring
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
    /// Initializes visual guidance with runway and preferences
    /// </summary>
    public void Initialize(Runway destinationRunway, Airport destinationAirport,
                          HandFlyWaveType guidanceToneWaveform, double toneVolume)
    {
        runway = destinationRunway;
        airport = destinationAirport;
        guidanceWaveType = guidanceToneWaveform;
        guidanceVolume = toneVolume;
        magneticVariation = destinationAirport.MagVar;
        thresholdElevationMSL = destinationAirport.Altitude;

        // Calculate touchdown aim point (offset from threshold for safe touchdown zone)
        double runwayLengthFeet = destinationRunway.Length * 3.28084;  // Convert meters to feet
        double touchdownDistanceFeet = Math.Min(TOUCHDOWN_AIM_POINT_FT, runwayLengthFeet * TOUCHDOWN_AIM_POINT_RATIO);
        (touchdownAimPointLat, touchdownAimPointLon) = NavigationCalculator.CalculateTouchdownAimPoint(
            destinationRunway.StartLat,
            destinationRunway.StartLon,
            destinationRunway.Heading,
            touchdownDistanceFeet);

        // Reset state
        currentPhase = GuidancePhase.NotStarted;
        lastPhaseAnnouncement = DateTime.MinValue;
        lastDistanceAnnouncement = DateTime.MinValue;
        lastAnnouncedDistance = double.MaxValue;
        lastExtendingProgressAnnouncement = DateTime.MinValue;
        lastAnnouncedBankDegrees = null;
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
        previousCrossTrackTimestamp = null;
        previousDesiredBank = 0.0;

        // Reset integral terms
        crossTrackIntegral = 0.0;
        glideslopeIntegral = 0.0;

        // Reset derivative term tracking (vertical)
        previousGlideslopeDeviation = null;
        previousGlideslopeTimestamp = null;
        previousDesiredPitch = 0.0;

        // Start desired attitude tone
        try
        {
            desiredAttitudeTone = new AudioToneGenerator();
            desiredAttitudeTone.Start(guidanceWaveType, guidanceVolume);
            System.Diagnostics.Debug.WriteLine("[VisualGuidanceManager] Guidance tone started");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VisualGuidanceManager] Failed to start guidance tone: {ex.Message}");
            desiredAttitudeTone?.Dispose();
            desiredAttitudeTone = null;
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

        runway = null;
        airport = null;
        isActive = false;

        announcer.Announce("Visual guidance off");
        System.Diagnostics.Debug.WriteLine("[VisualGuidanceManager] Stopped");
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
        System.Diagnostics.Debug.WriteLine("[VisualGuidanceManager] ProcessUpdate() called");

        if (!isActive || runway == null || desiredAttitudeTone == null)
        {
            System.Diagnostics.Debug.WriteLine($"[VisualGuidanceManager] Early return: isActive={isActive}, runway={(runway != null ? "set" : "null")}, tone={(desiredAttitudeTone != null ? "set" : "null")}");
            return;
        }

        // Ensure we have all required data
        if (!cachedLatitude.HasValue || !cachedLongitude.HasValue ||
            !cachedAGL.HasValue || !cachedAltMSL.HasValue ||
            !cachedHeading.HasValue)
        {
            System.Diagnostics.Debug.WriteLine($"[VisualGuidanceManager] Waiting for data: Lat={cachedLatitude.HasValue}, Lon={cachedLongitude.HasValue}, AGL={cachedAGL.HasValue}, MSL={cachedAltMSL.HasValue}, HDG={cachedHeading.HasValue}");
            return;
        }

        System.Diagnostics.Debug.WriteLine("[VisualGuidanceManager] All data present, executing calculations");

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

            // Update guidance tone
            desiredAttitudeTone.UpdatePitch(desiredPitch);
            desiredAttitudeTone.UpdateBank(desiredBank);

            // Announce commanded bank angle
            AnnounceBankGuidance(desiredBank);

            // Centerline deviation announcements (for testing)
            AnnounceCenterlineDeviation(lat, lon);

            // Distance callouts
            HandleDistanceCallouts(lat, lon);

            // Extending phase progress updates
            HandleExtendingProgress(lat, lon);

            System.Diagnostics.Debug.WriteLine(
                $"[VisualGuidanceManager] Phase: {currentPhase}, " +
                $"Desired: P={desiredPitch:F1}° B={desiredBank:F1}° AGL={agl:F0}ft");
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

        GuidancePhase previousPhase = currentPhase;

        // Check for touchdown first
        if (agl <= TOUCHDOWN_ALTITUDE_FT)
        {
            currentPhase = GuidancePhase.Touchdown;
            if (previousPhase != currentPhase)
            {
                announcer.Announce("Touchdown");
                Stop();  // Auto-stop on touchdown
            }
            return;
        }

        // Check for flare
        if (agl <= FLARE_ALTITUDE_FT)
        {
            currentPhase = GuidancePhase.Flare;
            if (previousPhase != currentPhase)
            {
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
        if (agl > 500.0 && IsBehindRunway(lat, lon))
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
                AnnouncePhaseChange("Glideslope captured");
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

            // Calculate cross-track rate (derivative term) for damping
            double crossTrackRate = 0.0;
            if (previousCrossTrackError.HasValue && previousCrossTrackTimestamp.HasValue)
            {
                double deltaTime = (DateTime.Now - previousCrossTrackTimestamp.Value).TotalSeconds;
                if (deltaTime > 0.01)  // Avoid division by zero
                {
                    crossTrackRate = (signedCrossTrackNM - previousCrossTrackError.Value) / deltaTime;
                }
            }

            // Update tracking state for next iteration
            previousCrossTrackError = signedCrossTrackNM;
            previousCrossTrackTimestamp = DateTime.Now;

            // Calculate track angle error (heading alignment)
            // Use ground track if available for better drift detection, otherwise fall back to heading
            double actualTrackAngle = cachedGroundTrack ?? heading;
            double trackAngleError = NormalizeHeading(runway.HeadingMag - actualTrackAngle);

            // Airspeed compensation scaling
            // Higher speeds need stronger corrections (compensates for larger turn radius)
            double speedFactor = Math.Sqrt(Math.Max(currentGroundSpeedKnots, 80.0) / AIRSPEED_REFERENCE_KNOTS);

            // Integral term with anti-windup (1 Hz update rate)
            crossTrackIntegral += signedCrossTrackNM * 1.0;

            // Anti-windup: Reset integral if error is large or changing sign (crossing centerline)
            if (Math.Abs(signedCrossTrackNM) > 1.0 ||  // Far from centerline
                (previousCrossTrackError.HasValue &&
                 Math.Sign(signedCrossTrackNM) != Math.Sign(previousCrossTrackError.Value)))
            {
                crossTrackIntegral = 0.0;  // Reset integral
            }

            // Clamp integral to prevent windup
            crossTrackIntegral = Math.Clamp(crossTrackIntegral, -INTEGRAL_LIMIT_LATERAL, INTEGRAL_LIMIT_LATERAL);

            // PID controller with heading alignment:
            // Proportional: -K1 × cross_track_error (main correction)
            // Integral: -Ki × ∫cross_track_error dt (eliminate steady-state error)
            // Derivative: -K2 × cross_track_rate (damping to prevent overshoot)
            // Heading: K3 × track_angle_error (align with runway heading)
            double proportionalTerm = -signedCrossTrackNM * LATERAL_GAIN_TRACKING * speedFactor;
            double integralTerm = -crossTrackIntegral * LATERAL_INTEGRAL_GAIN * speedFactor;
            double derivativeTerm = -crossTrackRate * LATERAL_RATE_DAMPING * speedFactor;
            double headingTerm = trackAngleError * HEADING_ALIGNMENT_GAIN;

            double rawDesiredBank = proportionalTerm + integralTerm + derivativeTerm + headingTerm;

            // Bank rate limiting - prevent sudden audio panning flips
            double maxBankChange = MAX_BANK_RATE_DEG_PER_SEC * 1.0;  // 1 second update rate
            double bankChange = rawDesiredBank - previousDesiredBank;
            if (Math.Abs(bankChange) > maxBankChange)
            {
                rawDesiredBank = previousDesiredBank + Math.Sign(bankChange) * maxBankChange;
            }

            // Adaptive bank limits based on cross-track error
            // Far from centerline: allow more aggressive banks for faster intercept
            // Close to centerline: use gentle banks for smooth tracking
            double bankLimit;
            double absXTE = Math.Abs(signedCrossTrackNM);
            if (absXTE > 1.0)
            {
                // Far off centerline (>1 NM): aggressive intercept
                bankLimit = 25.0;
            }
            else if (absXTE > 0.3)
            {
                // Medium distance (0.3-1 NM): moderate intercept
                bankLimit = 20.0;
            }
            else
            {
                // On or near centerline (<0.3 NM): gentle tracking
                bankLimit = 15.0;
            }

            // Clamp final command with adaptive limit
            double desiredBank = Math.Clamp(rawDesiredBank, -bankLimit, bankLimit);

            // Update for next iteration
            previousDesiredBank = desiredBank;

            System.Diagnostics.Debug.WriteLine(
                $"[VisualGuidance] Lateral PID: XTE={signedCrossTrackNM:F3}NM, Rate={crossTrackRate:F3}NM/s, Integral={crossTrackIntegral:F3}, " +
                $"GndTrk={cachedGroundTrack?.ToString("F0") ?? "N/A"}°, TrkErr={trackAngleError:F1}°, GS={currentGroundSpeedKnots:F0}kt, SpeedFactor={speedFactor:F2}, " +
                $"P={proportionalTerm:F2}° I={integralTerm:F2}° D={derivativeTerm:F2}° H={headingTerm:F2}° → Bank={desiredBank:F1}° (limit=±{bankLimit:F0}°)");

            return desiredBank;
        }
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
            // Exponential flare: smoothly increase pitch from approach to touchdown
            // At 50 ft: ~3° (continue approach pitch to maintain glideslope)
            // At 5 ft: ~8° (arrest descent for gentle touchdown)

            // Calculate altitude ratio (1.0 at 50ft, 0.0 at 5ft)
            double altitudeRatio = (agl - TOUCHDOWN_ALTITUDE_FT) / (FLARE_ALTITUDE_FT - TOUCHDOWN_ALTITUDE_FT);
            altitudeRatio = Math.Clamp(altitudeRatio, 0.0, 1.0);

            // Exponential curve: more aggressive pitch increase closer to ground
            // Using power of 1.5 creates smooth transition with accelerating rate near touchdown
            double flareProgress = 1.0 - Math.Pow(altitudeRatio, 1.5);

            // Pitch range: 3° (approach) to 8° (touchdown)
            const double APPROACH_PITCH = 3.0;
            const double TOUCHDOWN_PITCH = 8.0;
            double desiredPitch = APPROACH_PITCH + (flareProgress * (TOUCHDOWN_PITCH - APPROACH_PITCH));

            return Math.Clamp(desiredPitch, 2.0, 8.5);
        }
        else if (currentPhase != GuidancePhase.NotStarted && currentPhase != GuidancePhase.Touchdown)
        {
            // Enhanced PD controller with absolute pitch commands (no feedback coupling)

            // Calculate distance from threshold
            double distanceFromThreshold = NavigationCalculator.CalculateDistance(
                lat, lon, runway.StartLat, runway.StartLon);

            // Calculate glideslope deviation (positive = above glideslope)
            double glideslopeDeviation = NavigationCalculator.CalculateGlideslopeDeviation(
                altMSL,                                      // Aircraft altitude MSL
                distanceFromThreshold,                       // Distance from threshold in NM
                GLIDESLOPE_ANGLE_DEG,                        // 3-degree glideslope
                thresholdElevationMSL);                      // Threshold elevation MSL

            // Calculate glideslope deviation rate (derivative term) for damping
            double glideslopeRate = 0.0;
            if (previousGlideslopeDeviation.HasValue && previousGlideslopeTimestamp.HasValue)
            {
                double deltaTime = (DateTime.Now - previousGlideslopeTimestamp.Value).TotalSeconds;
                if (deltaTime > 0.01)  // Avoid division by zero
                {
                    glideslopeRate = (glideslopeDeviation - previousGlideslopeDeviation.Value) / deltaTime;
                }
            }

            // Update tracking state for next iteration
            previousGlideslopeDeviation = glideslopeDeviation;
            previousGlideslopeTimestamp = DateTime.Now;

            // Calculate nominal pitch for glideslope (INDEPENDENT of current pitch)
            // Flight path angle = -3.14°, typical A320 approach AOA = 6°
            // Therefore: pitch = flight_path_angle + AOA
            double nominalPitch = -GLIDESLOPE_ANGLE_DEG + TYPICAL_APPROACH_AOA;  // ≈ +3°

            // Integral term with anti-windup (1 Hz update rate)
            double normalizedGSError = glideslopeDeviation / 200.0;  // Normalize to similar scale as lateral
            glideslopeIntegral += normalizedGSError * 1.0;

            // Anti-windup: Reset integral if error is large or changing sign
            if (Math.Abs(glideslopeDeviation) > 400.0 ||  // Far from glideslope
                (previousGlideslopeDeviation.HasValue &&
                 Math.Sign(glideslopeDeviation) != Math.Sign(previousGlideslopeDeviation.Value)))
            {
                glideslopeIntegral = 0.0;  // Reset integral
            }

            // Clamp integral to prevent windup
            glideslopeIntegral = Math.Clamp(glideslopeIntegral, -INTEGRAL_LIMIT_VERTICAL, INTEGRAL_LIMIT_VERTICAL);

            // PID controller for glideslope tracking:
            // Proportional: Correct based on altitude error
            // Integral: Eliminate steady-state altitude error
            // Derivative: Dampen based on rate of altitude error change
            double proportionalTerm = -normalizedGSError * VERTICAL_GAIN;
            double integralTerm = -glideslopeIntegral * VERTICAL_INTEGRAL_GAIN;
            double derivativeTerm = -glideslopeRate * VERTICAL_RATE_DAMPING;

            double rawDesiredPitch = nominalPitch + proportionalTerm + integralTerm + derivativeTerm;

            // Pitch rate limiting - prevent sudden tone frequency jumps
            double maxPitchChange = MAX_PITCH_RATE_DEG_PER_SEC * 1.0;  // 1 second update rate
            double pitchChange = rawDesiredPitch - previousDesiredPitch;
            if (Math.Abs(pitchChange) > maxPitchChange)
            {
                rawDesiredPitch = previousDesiredPitch + Math.Sign(pitchChange) * maxPitchChange;
            }

            // Clamp final command
            double desiredPitch = Math.Clamp(rawDesiredPitch, -6.0, 3.0);

            // Update for next iteration
            previousDesiredPitch = desiredPitch;

            System.Diagnostics.Debug.WriteLine(
                $"[VisualGuidance] Vertical PID: GSDev={glideslopeDeviation:F0}ft, Rate={glideslopeRate:F1}ft/s, Integral={glideslopeIntegral:F3}, " +
                $"Nominal={nominalPitch:F2}°, P={proportionalTerm:F2}° I={integralTerm:F2}° D={derivativeTerm:F2}° → Pitch={desiredPitch:F1}°");

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
    /// Announces commanded bank angle for verbal guidance feedback
    /// </summary>
    private void AnnounceBankGuidance(double desiredBankDegrees)
    {
        // Round to nearest degree
        int roundedBank = (int)Math.Round(desiredBankDegrees);

        // Only announce if changed from last announcement
        if (lastAnnouncedBankDegrees.HasValue && roundedBank == lastAnnouncedBankDegrees.Value)
            return;

        // Format announcement
        string announcement;
        if (roundedBank == 0)
        {
            announcement = "Tone centered";
        }
        else if (roundedBank < 0)
        {
            announcement = $"{Math.Abs(roundedBank)} left";
        }
        else
        {
            announcement = $"{roundedBank} right";
        }

        // Announce immediately
        announcer.AnnounceImmediate(announcement);
        lastAnnouncedBankDegrees = roundedBank;
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

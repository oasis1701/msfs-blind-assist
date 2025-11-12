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
    private InterceptAngle interceptAngle = InterceptAngle.Medium45;
    private double magneticVariation = 0.0;
    private double thresholdElevationMSL = 0.0;

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

    // Announcement tracking
    private DateTime lastPhaseAnnouncement = DateTime.MinValue;
    private DateTime lastDistanceAnnouncement = DateTime.MinValue;
    private double lastAnnouncedDistance = double.MaxValue;
    private DateTime lastExtendingProgressAnnouncement = DateTime.MinValue;
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
    private const double GLIDESLOPE_ANGLE_DEG = 3.0;     // Standard 3-degree glideslope

    // Lateral guidance gains
    private const double LATERAL_GAIN_INTERCEPT = 1.0;   // Heading error to bank for intercept
    private const double LATERAL_GAIN_TRACKING = 30.0;   // Cross-track error (NM) to bank for tracking

    // Vertical guidance gains
    private const double VERTICAL_GAIN = 2.0;            // Glideslope deviation (per 200 ft) to pitch correction

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
                          InterceptAngle configuredInterceptAngle, HandFlyWaveType guidanceToneWaveform,
                          double toneVolume)
    {
        runway = destinationRunway;
        airport = destinationAirport;
        interceptAngle = configuredInterceptAngle;
        guidanceWaveType = guidanceToneWaveform;
        guidanceVolume = toneVolume;
        magneticVariation = destinationAirport.MagVar;
        thresholdElevationMSL = destinationAirport.Altitude;

        // Reset state
        currentPhase = GuidancePhase.NotStarted;
        lastPhaseAnnouncement = DateTime.MinValue;
        lastDistanceAnnouncement = DateTime.MinValue;
        lastAnnouncedDistance = double.MaxValue;
        lastExtendingProgressAnnouncement = DateTime.MinValue;
        cachedLatitude = null;
        cachedLongitude = null;
        cachedAGL = null;
        cachedAltMSL = null;
        cachedPitch = null;
        cachedBank = null;
        cachedHeading = null;

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
    public void UpdatePitch(double pitchDegrees) => cachedPitch = pitchDegrees;
    public void UpdateBank(double bankDegrees) => cachedBank = bankDegrees;
    public void UpdateHeading(double headingDegrees) => cachedHeading = headingDegrees;

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
        // Provide extension guidance regardless of distance - always correct to turn around when on wrong side
        if (IsBehindRunway(lat, lon))
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
                // Calculate intercept heading based on configured intercept angle
                double interceptAngleDeg = interceptAngle switch
                {
                    InterceptAngle.Shallow30 => 30.0,
                    InterceptAngle.Medium45 => 45.0,
                    InterceptAngle.Steep60 => 60.0,
                    _ => 45.0
                };

                double interceptHeading = NavigationCalculator.CalculateAngledInterceptHeading(
                    lat, lon, runway.StartLat, runway.StartLon, runway.Heading,
                    interceptAngleDeg, magneticVariation);

                // Format distance to final (1 decimal place)
                string distanceText = crossTrackError < 1.0
                    ? $"{crossTrackError:F1} mile"
                    : $"{crossTrackError:F1} miles";

                AnnouncePhaseChange($"Joining final on heading {interceptHeading:F0}, {distanceText} to final");
            }
            return;
        }

        // On centerline - check glideslope
        double glideslopeDeviation = NavigationCalculator.CalculateGlideslopeDeviation(
            altMSL,                                      // Aircraft altitude MSL
            distance,                                     // Distance from threshold in NM
            GLIDESLOPE_ANGLE_DEG,                        // 3-degree glideslope
            thresholdElevationMSL,                       // Threshold elevation MSL
            runway.StartLat,                              // Glideslope antenna lat (use threshold)
            runway.StartLon,                              // Glideslope antenna lon (use threshold)
            (int?)0,                                      // Glideslope antenna altitude (sea level)
            lat,                                          // Aircraft lat
            lon);                                         // Aircraft lon

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

        if (currentPhase == GuidancePhase.Intercepting && crossTrackError > CENTERLINE_TOLERANCE_NM)
        {
            // Use configured intercept angle
            double interceptAngleDeg = interceptAngle switch
            {
                InterceptAngle.Shallow30 => 30.0,
                InterceptAngle.Medium45 => 45.0,
                InterceptAngle.Steep60 => 60.0,
                _ => 45.0
            };

            double interceptHeading = NavigationCalculator.CalculateAngledInterceptHeading(
                lat, lon, runway.StartLat, runway.StartLon, runway.Heading,
                interceptAngleDeg, magneticVariation);

            double headingError = NormalizeHeading(interceptHeading - heading);
            double desiredBank = Math.Clamp(headingError * LATERAL_GAIN_INTERCEPT, -20.0, 20.0);

            return desiredBank;
        }
        else if (currentPhase == GuidancePhase.Extending)
        {
            // Guide to extension heading
            double extensionHeading = CalculateExtensionHeading();
            double headingError = NormalizeHeading(extensionHeading - heading);
            double desiredBank = Math.Clamp(headingError * LATERAL_GAIN_INTERCEPT, -20.0, 20.0);

            return desiredBank;
        }
        else
        {
            // Proportional correction for centerline tracking
            // Get signed cross-track error for direction (positive = right, negative = left)
            double signedCrossTrack = NavigationCalculator.CalculateCrossTrackError(
                lat, lon, runway.StartLat, runway.StartLon, runway.Heading);
            // Apply sign to distance (crossTrackError is unsigned distance in NM)
            double signedCrossTrackNM = Math.Sign(signedCrossTrack) * crossTrackError;
            // Invert to create negative feedback: right of centerline → left bank, left of centerline → right bank
            double desiredBank = Math.Clamp(-signedCrossTrackNM * LATERAL_GAIN_TRACKING, -15.0, 15.0);
            return desiredBank;
        }
    }

    /// <summary>
    /// Calculates desired pitch angle for vertical guidance
    /// </summary>
    private double CalculateDesiredPitch(double lat, double lon, double agl, double altMSL)
    {
        if (runway == null) return 0.0;

        if (currentPhase == GuidancePhase.Flare)
        {
            // Smooth transition from -3° at 50ft to +2° at 5ft
            double flareProgress = Math.Clamp((FLARE_ALTITUDE_FT - agl) / (FLARE_ALTITUDE_FT - TOUCHDOWN_ALTITUDE_FT), 0.0, 1.0);
            double desiredPitch = -3.0 + (flareProgress * 5.0);  // -3° to +2°
            return Math.Clamp(desiredPitch, -3.0, 2.5);
        }
        else if (currentPhase == GuidancePhase.GlideslopeTracking)
        {
            // Calculate distance from threshold
            double distanceFromThreshold = NavigationCalculator.CalculateDistance(
                lat, lon, runway.StartLat, runway.StartLon);

            // Calculate glideslope deviation
            double glideslopeDeviation = NavigationCalculator.CalculateGlideslopeDeviation(
                altMSL,                                      // Aircraft altitude MSL
                distanceFromThreshold,                       // Distance from threshold in NM
                GLIDESLOPE_ANGLE_DEG,                        // 3-degree glideslope
                thresholdElevationMSL,                       // Threshold elevation MSL
                runway.StartLat,                              // Glideslope antenna lat (use threshold)
                runway.StartLon,                              // Glideslope antenna lon (use threshold)
                (int?)0,                                      // Glideslope antenna altitude (sea level)
                lat,                                          // Aircraft lat
                lon);                                         // Aircraft lon

            // Positive deviation = above glideslope → need more descent (more negative pitch)
            double pitchCorrection = (glideslopeDeviation / 200.0) * VERTICAL_GAIN;
            double desiredPitch = -3.0 - pitchCorrection;

            return Math.Clamp(desiredPitch, -6.0, 0.0);
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

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Intercept angle options for visual guidance
/// </summary>
public enum InterceptAngle
{
    Shallow30,
    Medium45,
    Steep60
}

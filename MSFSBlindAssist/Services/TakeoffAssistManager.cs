using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public class TakeoffAssistManager : IDisposable
{
    private readonly ScreenReaderAnnouncer announcer;
    private readonly bool muteCenterlineAnnouncements;
    private readonly bool invertPanning;
    private readonly bool legacyMode;
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

    // Configuration constants - shared
    private const int ANNOUNCEMENT_INTERVAL_MS = 500; // 500ms between announcements
    private const double PITCH_THRESHOLD = 1.0; // Announce if pitch changes by >1 degree

    // Configuration constants - modern mode
    private const double CENTERLINE_TOLERANCE_FEET = 25.0; // Within ±25 feet is "center"
    private const double CENTERLINE_CHANGE_THRESHOLD_FEET = 10.0; // Announce if deviation changes by >10 feet
    private const double NM_TO_FEET = 6076.12;
    private const double PAN_FULL_RANGE_DEGREES = 5.0; // ±5° heading deviation for full left/right pan

    // Configuration constants - legacy mode
    private const double HEADING_TOLERANCE_DEGREES = 1.0; // Within ±1 degree is "center"
    private const double HEADING_CHANGE_THRESHOLD = 1.0; // Announce if heading deviation changes by >1 degree

    public bool IsActive => isActive;
    public bool HasRunwayReference => hasRunwayReference;

    public event EventHandler<bool>? TakeoffAssistActiveChanged;

    public TakeoffAssistManager(ScreenReaderAnnouncer screenReaderAnnouncer,
        HandFlyWaveType waveType = HandFlyWaveType.Sine, double volume = 0.05,
        bool muteCenterline = false, bool useInvertPanning = false, bool useLegacyMode = false)
    {
        announcer = screenReaderAnnouncer;
        toneWaveType = waveType;
        toneVolume = volume;
        muteCenterlineAnnouncements = muteCenterline;
        invertPanning = useInvertPanning;
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
            }

            if (hasRunwayReference)
            {
                // Use runway reference from teleport
                string modeInfo = legacyMode ? "legacy mode" : "";
                announcer.AnnounceImmediate($"Takeoff assist active{(legacyMode ? " legacy mode" : "")}, runway {referenceRunwayID} at {referenceAirportICAO}");
                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Activated with runway reference (legacy={legacyMode}): {referenceRunwayID} at {referenceAirportICAO}, HdgMag={referenceRunwayHeadingMagnetic:F1}");
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
        }
        else
        {
            // Stop centerline guidance tone (only exists in modern mode)
            centerlineTone?.Stop();

            // Deactivating - clear non-runway reference (keep teleport reference for next time)
            if (!hasRunwayReference)
            {
                referenceThresholdLat = null;
                referenceThresholdLon = null;
                referenceRunwayHeadingTrue = null;
                referenceRunwayHeadingMagnetic = null;
            }

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

            // Calculate perpendicular distance to centerline using TRUE heading (geographic accuracy)
            double crossTrackNM = NavigationCalculator.CalculateDistanceToLocalizer(
                currentLat, currentLon,
                referenceThresholdLat.Value, referenceThresholdLon.Value,
                referenceRunwayHeadingTrue.Value);

            // Get signed cross-track error to determine direction (left/right) using TRUE heading
            double signedError = NavigationCalculator.CalculateCrossTrackError(
                currentLat, currentLon,
                referenceThresholdLat.Value, referenceThresholdLon.Value,
                referenceRunwayHeadingTrue.Value);

            // Convert to feet and apply sign
            double crossTrackFeet = crossTrackNM * NM_TO_FEET;
            if (signedError > 0) crossTrackFeet = -crossTrackFeet; // Positive signedError = left of centerline

            // Audio pan based on heading deviation - centered tone = nose pointed down runway
            // Positive headingDiff = pointed right of runway, pan right (unless inverted)
            float pan = (float)Math.Clamp(headingDiff / PAN_FULL_RANGE_DEGREES, -1.0, 1.0);
            if (invertPanning) pan = -pan;
            centerlineTone?.SetPan(pan);

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

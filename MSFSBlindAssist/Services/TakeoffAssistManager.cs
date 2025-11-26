using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public class TakeoffAssistManager : IDisposable
{
    private readonly ScreenReaderAnnouncer announcer;
    private bool isActive = false;

    // Runway reference data (set by teleport or manual activation)
    private double? referenceThresholdLat;
    private double? referenceThresholdLon;
    private double? referenceRunwayHeadingTrue;      // For cross-track geometry
    private double? referenceRunwayHeadingMagnetic;  // For pan comparison
    private string? referenceRunwayID;
    private string? referenceAirportICAO;
    private bool hasRunwayReference = false;

    // Audio tone generator for centerline guidance
    private AudioToneGenerator? centerlineTone;
    private readonly HandFlyWaveType toneWaveType;
    private readonly double toneVolume;

    // Tracking state
    private double lastAnnouncedCrossTrackFeet = 0;
    private double lastAnnouncedPitch = 0;
    private DateTime lastCenterlineAnnouncement = DateTime.MinValue;
    private DateTime lastPitchAnnouncement = DateTime.MinValue;

    // Configuration constants
    private const int ANNOUNCEMENT_INTERVAL_MS = 500; // 500ms between announcements
    private const double CENTERLINE_TOLERANCE_FEET = 25.0; // Within ±25 feet is "on centerline"
    private const double CENTERLINE_CHANGE_THRESHOLD_FEET = 10.0; // Announce if deviation changes by >10 feet
    private const double PITCH_THRESHOLD = 1.0; // Announce if pitch changes by >1 degree
    private const double NM_TO_FEET = 6076.12;
    private const double PAN_FULL_RANGE_DEGREES = 5.0; // ±5° heading deviation for full left/right pan

    public bool IsActive => isActive;
    public bool HasRunwayReference => hasRunwayReference;

    public event EventHandler<bool>? TakeoffAssistActiveChanged;

    public TakeoffAssistManager(ScreenReaderAnnouncer screenReaderAnnouncer,
        HandFlyWaveType waveType = HandFlyWaveType.Sine, double volume = 0.05)
    {
        announcer = screenReaderAnnouncer;
        toneWaveType = waveType;
        toneVolume = volume;
        centerlineTone = new AudioToneGenerator();
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
            // Reset tracking state
            lastAnnouncedCrossTrackFeet = 0;
            lastAnnouncedPitch = 0;
            lastCenterlineAnnouncement = DateTime.MinValue;
            lastPitchAnnouncement = DateTime.MinValue;

            // Start centerline guidance tone at 600 Hz
            centerlineTone?.Start(toneWaveType, toneVolume, 600.0);

            if (hasRunwayReference)
            {
                // Use runway reference from teleport
                announcer.AnnounceImmediate($"Takeoff assist active, runway {referenceRunwayID} at {referenceAirportICAO}");
                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Activated with runway reference: {referenceRunwayID} at {referenceAirportICAO}, Lat={referenceThresholdLat:F6}, Lon={referenceThresholdLon:F6}, HdgTrue={referenceRunwayHeadingTrue:F1}, HdgMag={referenceRunwayHeadingMagnetic:F1}");
            }
            else
            {
                // Use current position as reference
                // Magnetic heading is captured directly, true heading computed from magvar
                referenceThresholdLat = currentLat;
                referenceThresholdLon = currentLon;
                referenceRunwayHeadingMagnetic = currentHeadingMagnetic;
                referenceRunwayHeadingTrue = currentHeadingMagnetic + magVar;  // True = Magnetic + MagVar

                announcer.AnnounceImmediate($"Takeoff assist active, no runway selected, extending centerline from current position, heading {Math.Round(currentHeadingMagnetic)}");
                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Activated with current position reference: Lat={currentLat:F6}, Lon={currentLon:F6}, HdgMag={currentHeadingMagnetic:F1}, HdgTrue={referenceRunwayHeadingTrue:F1}");
            }
        }
        else
        {
            // Stop centerline guidance tone
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
    /// Process position update during takeoff assist for centerline tracking
    /// </summary>
    /// <param name="currentLat">Current aircraft latitude</param>
    /// <param name="currentLon">Current aircraft longitude</param>
    /// <param name="currentHeadingMagnetic">Current aircraft magnetic heading in degrees</param>
    public void ProcessPositionUpdate(double currentLat, double currentLon, double currentHeadingMagnetic)
    {
        if (!isActive) return;
        if (!referenceThresholdLat.HasValue || !referenceThresholdLon.HasValue ||
            !referenceRunwayHeadingTrue.HasValue || !referenceRunwayHeadingMagnetic.HasValue) return;

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

        // Calculate heading deviation using MAGNETIC headings (pilot intuition)
        // Normalized to -180 to +180
        double headingDiff = currentHeadingMagnetic - referenceRunwayHeadingMagnetic.Value;
        while (headingDiff > 180.0) headingDiff -= 360.0;
        while (headingDiff < -180.0) headingDiff += 360.0;

        // Audio pan based on heading deviation - centered tone = nose pointed down runway
        // Positive headingDiff = pointed right of runway, pan right
        float pan = (float)Math.Clamp(headingDiff / PAN_FULL_RANGE_DEGREES, -1.0, 1.0);
        centerlineTone?.SetPan(pan);

        // Check if we should announce
        double deviationChange = Math.Abs(crossTrackFeet - lastAnnouncedCrossTrackFeet);
        TimeSpan timeSinceLastAnnouncement = DateTime.Now - lastCenterlineAnnouncement;

        if (deviationChange >= CENTERLINE_CHANGE_THRESHOLD_FEET &&
            timeSinceLastAnnouncement.TotalMilliseconds >= ANNOUNCEMENT_INTERVAL_MS)
        {
            string announcement = FormatCenterlineAnnouncement(crossTrackFeet);
            announcer.AnnounceImmediate(announcement);

            lastAnnouncedCrossTrackFeet = crossTrackFeet;
            lastCenterlineAnnouncement = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Position: Lat={currentLat:F6}, Lon={currentLon:F6}, CrossTrack={crossTrackFeet:F1}ft → {announcement}");
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

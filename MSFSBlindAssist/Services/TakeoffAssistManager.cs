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
    private double? referenceRunwayHeading;
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
    private const double PAN_FULL_RANGE_FEET = 50.0; // ±50 feet for full left/right pan

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
    /// <param name="runwayHeading">Runway true heading</param>
    /// <param name="runwayID">Runway identifier (e.g., "04L")</param>
    /// <param name="airportICAO">Airport ICAO code (e.g., "KEWR")</param>
    public void SetRunwayReference(double thresholdLat, double thresholdLon, double runwayHeading,
        string runwayID, string airportICAO)
    {
        referenceThresholdLat = thresholdLat;
        referenceThresholdLon = thresholdLon;
        referenceRunwayHeading = runwayHeading;
        referenceRunwayID = runwayID;
        referenceAirportICAO = airportICAO;
        hasRunwayReference = true;

        System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Runway reference set: {runwayID} at {airportICAO}, Lat={thresholdLat:F6}, Lon={thresholdLon:F6}, Hdg={runwayHeading:F1}");
    }

    /// <summary>
    /// Clears the runway reference (e.g., when aircraft changes or disconnects)
    /// </summary>
    public void ClearRunwayReference()
    {
        referenceThresholdLat = null;
        referenceThresholdLon = null;
        referenceRunwayHeading = null;
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
    /// <param name="currentHeading">Current magnetic heading in degrees</param>
    public void Toggle(double currentLat, double currentLon, double currentHeading)
    {
        isActive = !isActive;

        if (isActive)
        {
            // Reset tracking state
            lastAnnouncedCrossTrackFeet = 0;
            lastAnnouncedPitch = 0;
            lastCenterlineAnnouncement = DateTime.MinValue;
            lastPitchAnnouncement = DateTime.MinValue;

            // Start centerline guidance tone
            centerlineTone?.Start(toneWaveType, toneVolume);

            if (hasRunwayReference)
            {
                // Use runway reference from teleport
                announcer.AnnounceImmediate($"Takeoff assist active, runway {referenceRunwayID} at {referenceAirportICAO}");
                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Activated with runway reference: {referenceRunwayID} at {referenceAirportICAO}, Lat={referenceThresholdLat:F6}, Lon={referenceThresholdLon:F6}, Hdg={referenceRunwayHeading:F1}");
            }
            else
            {
                // Use current position as reference
                referenceThresholdLat = currentLat;
                referenceThresholdLon = currentLon;
                referenceRunwayHeading = currentHeading;

                announcer.AnnounceImmediate($"Takeoff assist active, no runway selected, extending centerline from current position, heading {Math.Round(currentHeading)}");
                System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Activated with current position reference: Lat={currentLat:F6}, Lon={currentLon:F6}, Hdg={currentHeading:F1}");
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
                referenceRunwayHeading = null;
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
    public void ProcessPositionUpdate(double currentLat, double currentLon)
    {
        if (!isActive) return;
        if (!referenceThresholdLat.HasValue || !referenceThresholdLon.HasValue || !referenceRunwayHeading.HasValue) return;

        // Calculate perpendicular distance to centerline using NavigationCalculator
        double crossTrackNM = NavigationCalculator.CalculateDistanceToLocalizer(
            currentLat, currentLon,
            referenceThresholdLat.Value, referenceThresholdLon.Value,
            referenceRunwayHeading.Value);

        // Get signed cross-track error to determine direction (left/right)
        double signedError = NavigationCalculator.CalculateCrossTrackError(
            currentLat, currentLon,
            referenceThresholdLat.Value, referenceThresholdLon.Value,
            referenceRunwayHeading.Value);

        // Convert to feet and apply sign
        double crossTrackFeet = crossTrackNM * NM_TO_FEET;
        if (signedError > 0) crossTrackFeet = -crossTrackFeet; // Positive signedError = left of centerline

        // Update audio pan - negate so sound goes toward centerline (follow the sound)
        // If aircraft is RIGHT of centerline (+crossTrackFeet), sound pans LEFT (negative pan)
        float pan = (float)Math.Clamp(-crossTrackFeet / PAN_FULL_RANGE_FEET, -1.0, 1.0);
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
    /// Formats centerline deviation announcement in feet
    /// </summary>
    private string FormatCenterlineAnnouncement(double crossTrackFeet)
    {
        // Check if on centerline
        if (Math.Abs(crossTrackFeet) <= CENTERLINE_TOLERANCE_FEET)
        {
            return "on centerline";
        }

        // Determine direction and magnitude
        int deviationFeet = (int)Math.Round(Math.Abs(crossTrackFeet));
        string direction = crossTrackFeet > 0 ? "right" : "left";

        return $"{deviationFeet} feet {direction}";
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

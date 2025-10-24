using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Services;
public class TakeoffAssistManager
{
    private readonly ScreenReaderAnnouncer announcer;
    private bool isActive = false;
    private double referenceHeading = 0;
    private double lastAnnouncedHeading = 0;
    private double lastAnnouncedPitch = 0;
    private DateTime lastHeadingAnnouncement = DateTime.MinValue;
    private DateTime lastPitchAnnouncement = DateTime.MinValue;

    // Configuration constants
    private const int ANNOUNCEMENT_INTERVAL_MS = 500; // 500ms between announcements
    private const double HEADING_THRESHOLD = 1.0; // Announce if heading changes by >1 degree
    private const double PITCH_THRESHOLD = 1.0; // Announce if pitch changes by >1 degree
    private const double CENTERLINE_TOLERANCE = 1.0; // Within ±1 degree is "on centerline"

    public bool IsActive => isActive;

    public event EventHandler<bool>? TakeoffAssistActiveChanged;

    public TakeoffAssistManager(ScreenReaderAnnouncer screenReaderAnnouncer)
    {
        announcer = screenReaderAnnouncer;
    }

    /// <summary>
    /// Toggles takeoff assist mode on/off
    /// </summary>
    /// <param name="currentHeading">Current magnetic heading in degrees</param>
    public void Toggle(double currentHeading)
    {
        isActive = !isActive;

        if (isActive)
        {
            // Activating - record reference heading
            referenceHeading = currentHeading;
            lastAnnouncedHeading = currentHeading;
            lastAnnouncedPitch = 0;
            lastHeadingAnnouncement = DateTime.MinValue;
            lastPitchAnnouncement = DateTime.MinValue;

            announcer.AnnounceImmediate($"Takeoff assist active, reference heading {Math.Round(currentHeading)}");
            System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Activated with reference heading: {referenceHeading:F1}");
        }
        else
        {
            // Deactivating
            announcer.AnnounceImmediate("Takeoff assist off");
            System.Diagnostics.Debug.WriteLine("[TakeoffAssistManager] Deactivated");
        }

        TakeoffAssistActiveChanged?.Invoke(this, isActive);
    }

    /// <summary>
    /// Process heading update during takeoff assist
    /// </summary>
    /// <param name="currentHeading">Current magnetic heading in degrees</param>
    public void ProcessHeadingUpdate(double currentHeading)
    {
        if (!isActive) return;

        // Calculate deviation from reference heading
        double deviation = NormalizeHeadingDifference(currentHeading - referenceHeading);
        double headingChange = Math.Abs(currentHeading - lastAnnouncedHeading);
        TimeSpan timeSinceLastAnnouncement = DateTime.Now - lastHeadingAnnouncement;

        // Check if we should announce
        if (headingChange >= HEADING_THRESHOLD &&
            timeSinceLastAnnouncement.TotalMilliseconds >= ANNOUNCEMENT_INTERVAL_MS)
        {
            string announcement = FormatHeadingAnnouncement(deviation);
            announcer.AnnounceImmediate(announcement);

            lastAnnouncedHeading = currentHeading;
            lastHeadingAnnouncement = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[TakeoffAssistManager] Heading: {currentHeading:F1}°, Deviation: {deviation:F1}° → {announcement}");
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
    /// Formats heading deviation announcement
    /// </summary>
    private string FormatHeadingAnnouncement(double deviation)
    {
        // Check if on centerline
        if (Math.Abs(deviation) <= CENTERLINE_TOLERANCE)
        {
            return "on centerline";
        }

        // Determine direction and magnitude
        int deviationDegrees = (int)Math.Round(Math.Abs(deviation));
        string direction = deviation > 0 ? "right" : "left";

        return $"{direction} {deviationDegrees}";
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
    /// Normalizes heading difference to -180 to +180 range
    /// </summary>
    private double NormalizeHeadingDifference(double diff)
    {
        while (diff > 180) diff -= 360;
        while (diff < -180) diff += 360;
        return diff;
    }

    /// <summary>
    /// Resets the takeoff assist manager state
    /// </summary>
    public void Reset()
    {
        if (isActive)
        {
            isActive = false;
            TakeoffAssistActiveChanged?.Invoke(this, false);
            System.Diagnostics.Debug.WriteLine("[TakeoffAssistManager] Reset");
        }
    }
}

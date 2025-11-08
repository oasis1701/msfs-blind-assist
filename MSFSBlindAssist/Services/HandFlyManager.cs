using MSFSBlindAssist.Accessibility;

namespace MSFSBlindAssist.Services;

public class HandFlyManager
{
    private readonly ScreenReaderAnnouncer announcer;
    private bool isActive = false;
    private double lastAnnouncedPitch = 0;
    private double lastAnnouncedBank = 0;
    private DateTime lastPitchAnnouncement = DateTime.MinValue;
    private DateTime lastBankAnnouncement = DateTime.MinValue;

    // Configuration constants
    private const int ANNOUNCEMENT_INTERVAL_MS = 500; // 500ms between announcements
    private const double PITCH_THRESHOLD = 0.1; // Announce if pitch changes by >0.1 degree
    private const double BANK_THRESHOLD = 1.0; // Announce if bank changes by >1 degree

    public bool IsActive => isActive;

    public event EventHandler<bool>? HandFlyModeActiveChanged;

    public HandFlyManager(ScreenReaderAnnouncer screenReaderAnnouncer)
    {
        announcer = screenReaderAnnouncer;
    }

    /// <summary>
    /// Toggles hand fly mode on/off
    /// </summary>
    public void Toggle()
    {
        isActive = !isActive;

        if (isActive)
        {
            // Activating - reset tracking
            lastAnnouncedPitch = 0;
            lastAnnouncedBank = 0;
            lastPitchAnnouncement = DateTime.MinValue;
            lastBankAnnouncement = DateTime.MinValue;

            announcer.AnnounceImmediate("Hand fly mode active");
            System.Diagnostics.Debug.WriteLine("[HandFlyManager] Activated");
        }
        else
        {
            // Deactivating
            announcer.AnnounceImmediate("Hand fly mode off");
            System.Diagnostics.Debug.WriteLine("[HandFlyManager] Deactivated");
        }

        HandFlyModeActiveChanged?.Invoke(this, isActive);
    }

    /// <summary>
    /// Process pitch update during hand fly mode
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

            System.Diagnostics.Debug.WriteLine($"[HandFlyManager] Pitch: {currentPitch:F1}° → {announcement}");
        }
    }

    /// <summary>
    /// Process bank angle update during hand fly mode
    /// </summary>
    /// <param name="currentBank">Current bank angle in degrees (positive = right, negative = left)</param>
    public void ProcessBankUpdate(double currentBank)
    {
        if (!isActive) return;

        double bankChange = Math.Abs(currentBank - lastAnnouncedBank);
        TimeSpan timeSinceLastAnnouncement = DateTime.Now - lastBankAnnouncement;

        // Check if we should announce
        if (bankChange >= BANK_THRESHOLD &&
            timeSinceLastAnnouncement.TotalMilliseconds >= ANNOUNCEMENT_INTERVAL_MS)
        {
            string announcement = FormatBankAnnouncement(currentBank);
            announcer.AnnounceImmediate(announcement);

            lastAnnouncedBank = currentBank;
            lastBankAnnouncement = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[HandFlyManager] Bank: {currentBank:F1}° → {announcement}");
        }
    }

    /// <summary>
    /// Formats pitch announcement with decimal precision - always shows value
    /// </summary>
    private string FormatPitchAnnouncement(double pitch)
    {
        if (pitch >= 0)
        {
            return $"+{pitch:F1}";
        }
        else
        {
            return $"{pitch:F1}";
        }
    }

    /// <summary>
    /// Formats bank angle announcement - always shows direction and value
    /// </summary>
    private string FormatBankAnnouncement(double bank)
    {
        // Determine direction and magnitude (show decimal precision)
        double bankDegrees = Math.Abs(bank);
        string direction = bank >= 0 ? "right" : "left";

        return $"{direction} {bankDegrees:F1}";
    }

    /// <summary>
    /// Resets the hand fly manager state
    /// </summary>
    public void Reset()
    {
        if (isActive)
        {
            isActive = false;
            HandFlyModeActiveChanged?.Invoke(this, false);
            System.Diagnostics.Debug.WriteLine("[HandFlyManager] Reset");
        }
    }
}

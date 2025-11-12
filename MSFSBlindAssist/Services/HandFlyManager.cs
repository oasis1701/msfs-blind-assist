using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public class HandFlyManager : IDisposable
{
    private readonly ScreenReaderAnnouncer announcer;
    private bool isActive = false;
    private double lastAnnouncedPitch = 0;
    private double lastAnnouncedBank = 0;
    private DateTime lastPitchAnnouncement = DateTime.MinValue;
    private DateTime lastBankAnnouncement = DateTime.MinValue;

    // Heading and VS tracking
    private double? lastAnnouncedHeading = null;
    private double? lastAnnouncedVS = null;
    private DateTime lastHeadingAnnouncement = DateTime.MinValue;
    private DateTime lastVSAnnouncement = DateTime.MinValue;

    // Audio tone generator
    private AudioToneGenerator? audioGenerator;
    private HandFlyFeedbackMode feedbackMode;
    private HandFlyWaveType waveType;
    private double volume;

    // Heading and VS monitoring settings
    private bool monitorHeading;
    private bool monitorVerticalSpeed;
    private int announcementIntervalMs;

    // Configuration constants
    private const int ANNOUNCEMENT_INTERVAL_MS = 500; // 500ms between announcements
    private const double PITCH_THRESHOLD = 0.1; // Announce if pitch changes by >0.1 degree
    private const double BANK_THRESHOLD = 1.0; // Announce if bank changes by >1 degree
    private const double HEADING_THRESHOLD = 1.0; // Announce if heading changes by >1 degree

    public bool IsActive => isActive;
    public bool MonitorHeading => monitorHeading;
    public bool MonitorVerticalSpeed => monitorVerticalSpeed;

    public event EventHandler<bool>? HandFlyModeActiveChanged;

    public HandFlyManager(ScreenReaderAnnouncer screenReaderAnnouncer)
    {
        announcer = screenReaderAnnouncer;

        // Load settings from SettingsManager
        var settings = SettingsManager.Current;
        feedbackMode = settings.HandFlyFeedbackMode;
        waveType = settings.HandFlyWaveType;
        volume = settings.HandFlyToneVolume;
        monitorHeading = settings.HandFlyMonitorHeading;
        monitorVerticalSpeed = settings.HandFlyMonitorVerticalSpeed;
        announcementIntervalMs = settings.HandFlyAnnouncementIntervalMs;
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
            lastAnnouncedHeading = null;
            lastAnnouncedVS = null;
            lastHeadingAnnouncement = DateTime.MinValue;
            lastVSAnnouncement = DateTime.MinValue;

            // Start audio if tones are enabled
            if (feedbackMode == HandFlyFeedbackMode.TonesOnly ||
                feedbackMode == HandFlyFeedbackMode.Both)
            {
                try
                {
                    audioGenerator = new AudioToneGenerator();
                    audioGenerator.Start(waveType, volume);
                    System.Diagnostics.Debug.WriteLine("[HandFlyManager] Audio tone started");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[HandFlyManager] Failed to start audio: {ex.Message}");
                    audioGenerator?.Dispose();
                    audioGenerator = null;
                }
            }

            announcer.AnnounceImmediate("Hand fly mode active");
            System.Diagnostics.Debug.WriteLine("[HandFlyManager] Activated");
        }
        else
        {
            // Deactivating - stop audio
            StopAudio();

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

        // Update audio tone frequency in real-time
        audioGenerator?.UpdatePitch(currentPitch);

        // Handle announcements based on feedback mode
        bool shouldAnnounce = feedbackMode == HandFlyFeedbackMode.AnnouncementsOnly ||
                             feedbackMode == HandFlyFeedbackMode.Both;

        if (shouldAnnounce)
        {
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
    }

    /// <summary>
    /// Process bank angle update during hand fly mode
    /// </summary>
    /// <param name="currentBank">Current bank angle in degrees (SimConnect convention: positive = left, negative = right)</param>
    public void ProcessBankUpdate(double currentBank)
    {
        if (!isActive) return;

        // Update audio stereo panning in real-time
        // Negate to convert SimConnect convention (positive=left, negative=right) to standard convention (positive=right, negative=left)
        audioGenerator?.UpdateBank(-currentBank);

        // Handle announcements based on feedback mode
        bool shouldAnnounce = feedbackMode == HandFlyFeedbackMode.AnnouncementsOnly ||
                             feedbackMode == HandFlyFeedbackMode.Both;

        if (shouldAnnounce)
        {
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
    }

    /// <summary>
    /// Process heading update during hand fly mode
    /// </summary>
    /// <param name="currentHeading">Current magnetic heading in degrees (0-360)</param>
    public void ProcessHeadingUpdate(double currentHeading)
    {
        if (!isActive || !monitorHeading) return;

        // Check if we should announce
        bool shouldAnnounce = false;
        TimeSpan timeSinceLastAnnouncement = DateTime.Now - lastHeadingAnnouncement;

        if (lastAnnouncedHeading == null)
        {
            // First announcement - always announce
            shouldAnnounce = true;
        }
        else if (timeSinceLastAnnouncement.TotalMilliseconds >= announcementIntervalMs)
        {
            // Check if heading changed by threshold
            double headingChange = Math.Abs(currentHeading - lastAnnouncedHeading.Value);

            // Handle wraparound (359° to 1° should be 2° change, not 358°)
            if (headingChange > 180)
            {
                headingChange = 360 - headingChange;
            }

            if (headingChange >= HEADING_THRESHOLD)
            {
                shouldAnnounce = true;
            }
        }

        if (shouldAnnounce)
        {
            string announcement = FormatHeadingAnnouncement(currentHeading);
            announcer.AnnounceImmediate(announcement);

            lastAnnouncedHeading = currentHeading;
            lastHeadingAnnouncement = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[HandFlyManager] Heading: {currentHeading:F0}° → {announcement}");
        }
    }

    /// <summary>
    /// Process vertical speed update during hand fly mode
    /// </summary>
    /// <param name="currentVS">Current vertical speed in feet per minute</param>
    public void ProcessVerticalSpeedUpdate(double currentVS)
    {
        if (!isActive || !monitorVerticalSpeed) return;

        // Check if we should announce
        bool shouldAnnounce = false;
        TimeSpan timeSinceLastAnnouncement = DateTime.Now - lastVSAnnouncement;

        if (lastAnnouncedVS == null)
        {
            // First announcement - always announce
            shouldAnnounce = true;
        }
        else if (timeSinceLastAnnouncement.TotalMilliseconds >= announcementIntervalMs)
        {
            // Announce if VS changed (any change threshold)
            if (Math.Abs(currentVS - lastAnnouncedVS.Value) > 0.1) // Small threshold to avoid floating point noise
            {
                shouldAnnounce = true;
            }
        }

        if (shouldAnnounce)
        {
            string announcement = FormatVSAnnouncement(currentVS);
            announcer.AnnounceImmediate(announcement);

            lastAnnouncedVS = currentVS;
            lastVSAnnouncement = DateTime.Now;

            System.Diagnostics.Debug.WriteLine($"[HandFlyManager] VS: {currentVS:F0} fpm → {announcement}");
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
        // Note: SimConnect convention is positive = left, negative = right
        double bankDegrees = Math.Abs(bank);
        string direction = bank >= 0 ? "left" : "right";

        return $"{direction} {bankDegrees:F1}";
    }

    /// <summary>
    /// Formats heading announcement
    /// </summary>
    private string FormatHeadingAnnouncement(double heading)
    {
        // Round to nearest whole degree
        int roundedHeading = (int)Math.Round(heading);

        // Ensure heading is in 0-360 range
        if (roundedHeading < 0) roundedHeading += 360;
        if (roundedHeading >= 360) roundedHeading -= 360;

        return $"{roundedHeading}";
    }

    /// <summary>
    /// Formats vertical speed announcement
    /// </summary>
    private string FormatVSAnnouncement(double vs)
    {
        // Round to nearest whole number for exact VS reporting
        int vsValue = (int)Math.Round(vs);

        // Format as +/- number (e.g., "+2347", "-487", "+0")
        if (vsValue >= 0)
        {
            return $"+{vsValue}";
        }
        else
        {
            return $"{vsValue}";
        }
    }

    /// <summary>
    /// Resets the hand fly manager state
    /// </summary>
    public void Reset()
    {
        if (isActive)
        {
            isActive = false;
            StopAudio();
            HandFlyModeActiveChanged?.Invoke(this, false);
            System.Diagnostics.Debug.WriteLine("[HandFlyManager] Reset");
        }
    }

    /// <summary>
    /// Updates hand fly settings while active (can be called from settings form)
    /// </summary>
    public void UpdateSettings(HandFlyFeedbackMode newFeedbackMode, HandFlyWaveType newWaveType, double newVolume,
        bool newMonitorHeading, bool newMonitorVerticalSpeed)
    {
        feedbackMode = newFeedbackMode;
        waveType = newWaveType;
        volume = newVolume;
        monitorHeading = newMonitorHeading;
        monitorVerticalSpeed = newMonitorVerticalSpeed;

        // If active, update or restart audio based on new settings
        if (isActive)
        {
            bool needsAudio = feedbackMode == HandFlyFeedbackMode.TonesOnly ||
                            feedbackMode == HandFlyFeedbackMode.Both;

            if (needsAudio)
            {
                // Update existing audio or start if not running
                if (audioGenerator != null)
                {
                    audioGenerator.UpdateWaveType(waveType);
                    audioGenerator.UpdateVolume(volume);
                }
                else
                {
                    // Start audio if it wasn't running before
                    try
                    {
                        audioGenerator = new AudioToneGenerator();
                        audioGenerator.Start(waveType, volume);
                        System.Diagnostics.Debug.WriteLine("[HandFlyManager] Audio tone started via settings update");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[HandFlyManager] Failed to start audio: {ex.Message}");
                    }
                }
            }
            else
            {
                // Stop audio if no longer needed
                StopAudio();
            }
        }

        System.Diagnostics.Debug.WriteLine($"[HandFlyManager] Settings updated: Mode={feedbackMode}, Wave={waveType}, Volume={volume:F2}");
    }

    /// <summary>
    /// Stops and disposes audio generator
    /// </summary>
    private void StopAudio()
    {
        if (audioGenerator != null)
        {
            audioGenerator.Stop();
            audioGenerator.Dispose();
            audioGenerator = null;
            System.Diagnostics.Debug.WriteLine("[HandFlyManager] Audio tone stopped");
        }
    }

    /// <summary>
    /// Disposes resources
    /// </summary>
    public void Dispose()
    {
        StopAudio();
        GC.SuppressFinalize(this);
    }
}

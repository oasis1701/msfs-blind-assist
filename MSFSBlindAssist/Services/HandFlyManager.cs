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

    // Audio tone generator
    private AudioToneGenerator? audioGenerator;
    private HandFlyFeedbackMode feedbackMode;
    private HandFlyWaveType waveType;
    private double volume;

    // Configuration constants
    private const int ANNOUNCEMENT_INTERVAL_MS = 500; // 500ms between announcements
    private const double PITCH_THRESHOLD = 0.1; // Announce if pitch changes by >0.1 degree
    private const double BANK_THRESHOLD = 1.0; // Announce if bank changes by >1 degree

    public bool IsActive => isActive;

    public event EventHandler<bool>? HandFlyModeActiveChanged;

    public HandFlyManager(ScreenReaderAnnouncer screenReaderAnnouncer)
    {
        announcer = screenReaderAnnouncer;

        // Load settings from SettingsManager
        var settings = SettingsManager.Current;
        feedbackMode = settings.HandFlyFeedbackMode;
        waveType = settings.HandFlyWaveType;
        volume = settings.HandFlyToneVolume;
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
    /// <param name="currentBank">Current bank angle in degrees (positive = right, negative = left)</param>
    public void ProcessBankUpdate(double currentBank)
    {
        if (!isActive) return;

        // Update audio stereo panning in real-time
        audioGenerator?.UpdateBank(currentBank);

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
    public void UpdateSettings(HandFlyFeedbackMode newFeedbackMode, HandFlyWaveType newWaveType, double newVolume)
    {
        feedbackMode = newFeedbackMode;
        waveType = newWaveType;
        volume = newVolume;

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

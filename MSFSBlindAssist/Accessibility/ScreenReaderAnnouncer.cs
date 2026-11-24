using System.Speech.Synthesis;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Accessibility;

public class ScreenReaderAnnouncer : IDisposable
{
    // System.Speech for fallback TTS (modern .NET alternative to COM SAPI)
    private SpeechSynthesizer? speechSynthesizer;
    private bool disposed = false;

    /// <summary>
    /// When true, the AUTOMATIC announce paths (Announce / AnnounceQueued /
    /// AnnounceWithQueue) are silently dropped. User-initiated AnnounceImmediate is
    /// NOT affected, so hotkeys still talk. Used to keep the app silent during the
    /// initial grace period after an aircraft is detected, so cockpit values that
    /// happen to differ from their sentinel cache (e.g. altimeter setting) don't
    /// announce on first detect. Lifted when the grace-period timer fires.
    /// </summary>
    public bool Suppressed { get; set; } = false;

    // Every screen reader (NVDA, JAWS, ZDSR and the rest) through Prism; SAPI is speechSynthesizer above.
    private readonly ScreenReaderOutput screenReaders;
    private AnnouncementMode currentMode;

    // Speech queue for sequential announcements
    private Queue<string> announcementQueue;
    private System.Windows.Forms.Timer queueTimer;
    private const int QUEUE_INTERVAL_MS = 900; // 900ms delay between queued messages (increased from 600ms to prevent NVDA buffer overflow)

        // Native methods for screen reader detection
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public ScreenReaderAnnouncer(IntPtr handle)
        {
            // Initialize speech queue and timer
            announcementQueue = new Queue<string>();
            queueTimer = new System.Windows.Forms.Timer();
            queueTimer.Interval = QUEUE_INTERVAL_MS;
            queueTimer.Tick += QueueTimer_Tick;

            // Load announcement mode from settings
            LoadAnnouncementMode();
            // Log.Debug("Accessibility", $"[ScreenReaderAnnouncer] Loaded mode from settings: {currentMode}");

            // Prism itself starts on the first announcement, not here: the test doubles that subclass this
            // announcer never speak through it, so they never load prism.dll.
            screenReaders = ScreenReaderOutput.Shared;

            try
            {
                // Initialize System.Speech as fallback method (modern .NET alternative)
                speechSynthesizer = new SpeechSynthesizer();
                speechSynthesizer.Volume = 100;  // 0-100
                speechSynthesizer.Rate = 0;      // -10 to 10, 0 is normal
                // Debug.WriteLine("[ScreenReaderAnnouncer] System.Speech initialized successfully");
            }
            catch (Exception ex)
            {
                // Fallback to SAPI only
                Log.Debug("Accessibility", $"Initialization error: {ex.Message}, falling back to SAPI");
                currentMode = AnnouncementMode.SAPI;
            }
        }

        private void LoadAnnouncementMode()
        {
            try
            {
                string modeString = SettingsManager.Current.AnnouncementMode;
                if (Enum.TryParse(modeString, out AnnouncementMode mode))
                {
                    currentMode = mode;
                }
                else
                {
                    currentMode = AnnouncementMode.ScreenReader; // Default
                }
            }
            catch
            {
                currentMode = AnnouncementMode.ScreenReader; // Default
            }
        }

        public void SetAnnouncementMode(AnnouncementMode mode)
        {
            currentMode = mode;
            try
            {
                SettingsManager.Current.AnnouncementMode = mode.ToString();
                SettingsManager.Save();
            }
            catch
            {
                // Settings save failed, continue with runtime change
            }
        }

        public AnnouncementMode GetAnnouncementMode()
        {
            return currentMode;
        }

        /// <summary>Whether announcements go to a screen reader in <paramref name="mode"/>: only in Screen Reader mode.
        /// SAPI mode keeps the Windows voice whatever reader is running, as the guide promises for a reader that is
        /// not working with BA (before Prism, a running NVDA or JAWS overrode it).</summary>
        internal static bool UsesScreenReader(AnnouncementMode mode) => mode == AnnouncementMode.ScreenReader;

        /// <summary>The two lines of the Settings status box: the reader <see cref="ScreenReaderOutput"/> found
        /// (<paramref name="reader"/>, null for none), or why there is none.</summary>
        internal static string DetectionStatusText(string? reader, bool prismStarted)
        {
            string nl = Environment.NewLine;
            if (reader != null) return $"Screen reader detected: {reader}{nl}Choose 'Screen Reader' for best experience.";
            if (prismStarted) return $"No screen reader detected.{nl}SAPI mode recommended for speech feedback.";
            return $"Unable to start screen reader detection.{nl}SAPI mode will be used as fallback.";
        }

        public void TestScreenReaderConnection()
        {
            Log.Debug("Accessibility", "=== Screen Reader Diagnostic Test ===");
            Log.Debug("Accessibility", $"Current announcement mode: {currentMode}");
            Log.Debug("Accessibility", $"Screen reader (Prism): {screenReaders.DetectNow() ?? "none"}");
            Log.Debug("Accessibility", $"SAPI available: {speechSynthesizer != null}");
            Log.Debug("Accessibility", "=== End Diagnostic Test ===");
        }

        // The four announce entry points are virtual so headless tests (e.g.
        // GroundTrafficMonitorHeadlessTests) can subclass with a text-capturing
        // announcer and check what the app WOULD have spoken.
        public virtual void Announce(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            // Initial-detect grace: drop automatic announcements (not user hotkeys).
            if (Suppressed)
                return;

            // Log.Debug("Accessibility", $"[ScreenReaderAnnouncer] Announce called - Mode: {currentMode}, Message: {message}");

            try
            {
                // The screen reader (queued: spoken when it is idle), and brailled; else SAPI below.
                bool success = UsesScreenReader(currentMode) && screenReaders.TrySpeak(message, interrupt: false);

                // If screen reader failed or SAPI mode selected, use System.Speech
                if (!success && speechSynthesizer != null)
                {
                    // Debug.WriteLine("[ScreenReaderAnnouncer] Using System.Speech TTS");
                    speechSynthesizer.SpeakAsync(message);
                    success = true;
                }

                if (!success)
                {
                    Log.Debug("Accessibility", "Warning: All speech methods failed");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Accessibility", $"Error announcing: {ex.Message}");
            }
        }

        public virtual void AnnounceImmediate(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            // Log.Debug("Accessibility", $"[ScreenReaderAnnouncer] AnnounceImmediate called - Mode: {currentMode}, Message: {message}");

            try
            {
                // The screen reader, cutting off what it is saying, and brailled; else SAPI below.
                bool success = UsesScreenReader(currentMode) && screenReaders.TrySpeak(message, interrupt: true);

                // If screen reader failed or SAPI mode selected, use System.Speech with interrupt
                if (!success && speechSynthesizer != null)
                {
                    // Debug.WriteLine("[ScreenReaderAnnouncer] Using immediate System.Speech with interrupt");
                    speechSynthesizer.SpeakAsyncCancelAll(); // Cancel current speech
                    speechSynthesizer.SpeakAsync(message);     // Speak new message
                    success = true;
                }

                if (!success)
                {
                    Log.Debug("Accessibility", "Warning: All immediate speech methods failed");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Accessibility", $"Error in immediate announce: {ex.Message}");
            }
        }

        public virtual void AnnounceQueued(string message)
        {
            Announce(message);
        }

        /// <summary>
        /// Adds a message to the announcement queue for sequential delivery with delays.
        /// Ideal for ECAM messages where multiple messages may appear simultaneously.
        /// </summary>
        public virtual void AnnounceWithQueue(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            if (Suppressed)
                return;

            lock (announcementQueue)
            {
                announcementQueue.Enqueue(message);
                // Log.Debug("Accessibility", $"[ScreenReaderAnnouncer] Queued message: '{message}' (Queue size: {announcementQueue.Count})");

                // Start timer if it's not already running
                if (!queueTimer.Enabled)
                {
                    // Log.Debug("Accessibility", "[ScreenReaderAnnouncer] Starting queue timer");
                    queueTimer.Start();
                }
            }
        }

        /// <summary>
        /// Number of announcements currently waiting in the queue. Lets a caller that
        /// shares this queue with something more urgent (VATSIM chatter sharing it with
        /// ECAM messages, see VatsimAnnouncementService) decide to drop a message
        /// instead of queuing it behind an existing backlog. Read under the same lock
        /// AnnounceWithQueue/QueueTimer_Tick already use.
        /// </summary>
        public int QueuedAnnouncementCount
        {
            get { lock (announcementQueue) { return announcementQueue.Count; } }
        }

        /// <summary>
        /// Timer tick handler that processes one message from the queue at a time.
        /// </summary>
        private void QueueTimer_Tick(object? sender, EventArgs e)
        {
            string? messageToSpeak = null;

            lock (announcementQueue)
            {
                if (announcementQueue.Count > 0)
                {
                    messageToSpeak = announcementQueue.Dequeue();
                    // Log.Debug("Accessibility", $"[ScreenReaderAnnouncer] Processing queued message: '{messageToSpeak}' (Remaining: {announcementQueue.Count})");
                }
                else
                {
                    // Queue is empty, stop the timer
                    // Log.Debug("Accessibility", "[ScreenReaderAnnouncer] Queue empty, stopping timer");
                    queueTimer.Stop();
                }
            }

            // Announce outside the lock to avoid holding it during speech
            if (messageToSpeak != null)
            {
                Announce(messageToSpeak);
            }
        }

        public void Cleanup()
        {
            Dispose();
        }
        
        public void Dispose()
        {
            if (!disposed)
            {
                // Stop and dispose queue timer
                if (queueTimer != null)
                {
                    try
                    {
                        queueTimer.Stop();
                        queueTimer.Dispose();
                    }
                    catch { }
                    queueTimer = null!;
                }

                // Clear any pending announcements
                if (announcementQueue != null)
                {
                    lock (announcementQueue)
                    {
                        announcementQueue.Clear();
                    }
                }

                if (speechSynthesizer != null)
                {
                    try
                    {
                        speechSynthesizer.Dispose();
                    }
                    catch { }
                    speechSynthesizer = null;
                }

                // Nothing to release for the screen readers: ScreenReaderOutput.Shared lives as long as the process.

                disposed = true;
            }
        }
    }

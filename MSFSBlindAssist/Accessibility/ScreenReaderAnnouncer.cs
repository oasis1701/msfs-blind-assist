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
                // Windows speech is unavailable: the screen reader is then used whatever the mode (TriesScreenReader),
                // so a broken SAPI never means silence. The mode itself is left as the pilot set it.
                Log.Warn("Accessibility", $"Windows speech (SAPI) could not start, so only a screen reader speaks: {ex.Message}");
                try { speechSynthesizer?.Dispose(); } catch { }
                speechSynthesizer = null;
                WindowsSpeechFailedToStart = true;
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

        /// <summary>Whether a message is offered to the screen reader first: in Screen Reader mode, and in SAPI mode
        /// too when Windows speech is unavailable (<paramref name="sapiAvailable"/> false), so that a broken SAPI never
        /// silences a pilot whose screen reader works.</summary>
        internal static bool TriesScreenReader(AnnouncementMode mode, bool sapiAvailable) => UsesScreenReader(mode) || !sapiAvailable;

        /// <summary>The two lines of the Settings status box: the reader <see cref="ScreenReaderOutput"/> found
        /// (<paramref name="reader"/>, null for none), or why there is none. With SAPI chosen
        /// (<paramref name="sapiChosen"/>) a found reader is named with why the Windows voice still speaks: before Prism a
        /// running reader overrode a saved SAPI choice, so a pilot updating with SAPI saved learns it here.
        /// It says what the announcer does: with Windows speech unavailable (<paramref name="sapiAvailable"/> false) the
        /// reader speaks whatever is chosen (<see cref="TriesScreenReader"/>), and with no reader either nothing does.</summary>
        internal static string DetectionStatusText(string? reader, bool prismStarted, bool sapiChosen, bool sapiAvailable)
        {
            string nl = Environment.NewLine;
            if (reader != null && sapiChosen && sapiAvailable)
                return $"Screen reader detected: {reader}{nl}SAPI is chosen, so BA speaks with Windows speech. Choose 'Screen Reader' to use {reader}.";
            if (reader != null && sapiChosen)
                return $"Screen reader detected: {reader}{nl}SAPI is chosen, but Windows speech could not start, so BA speaks through {reader}.";
            if (reader != null) return $"Screen reader detected: {reader}{nl}BA speaks through {reader}.";
            if (prismStarted && sapiAvailable) return $"No screen reader detected.{nl}SAPI mode recommended for speech feedback.";
            if (prismStarted) return $"No screen reader detected, and Windows speech could not start.{nl}BA cannot speak until a screen reader is running.";
            if (sapiAvailable) return $"Unable to start screen reader detection.{nl}SAPI mode will be used as fallback.";
            return $"Unable to start screen reader detection, and Windows speech could not start.{nl}BA cannot speak.";
        }

        /// <summary>Whether a detected reader is the voice the pilot hears: Screen Reader is chosen, or SAPI is but
        /// Windows speech could not start. The Settings status box is green then.</summary>
        internal static bool SpeaksThroughReader(bool sapiChosen, bool sapiAvailable) => !sapiChosen || !sapiAvailable;

        /// <summary>True once Windows speech (System.Speech) failed to start in this process: the announcer then
        /// speaks through the screen reader whatever the mode, and the Settings status box says so.</summary>
        internal static bool WindowsSpeechFailedToStart { get; private set; }

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

            // Queued: the reader speaks it when idle.
            Deliver(message, interrupt: false);
        }

        // One announcement to this announcer's reader and Windows voice (Route). Never throws.
        private void Deliver(string message, bool interrupt)
        {
            var synth = speechSynthesizer;
            Route(message, interrupt, currentMode, screenReaders.TrySpeak,
                synth == null ? null : text => synth.SpeakAsync(text),
                synth == null ? null : synth.SpeakAsyncCancelAll);
        }

        /// <summary>
        /// Where one announcement goes. The screen reader (spoken and brailled, <paramref name="readerTrySpeak"/>)
        /// when the mode, or a missing Windows speech (<paramref name="sapiSpeak"/> null), says so; else Windows
        /// speech; and the screen reader after all when Windows speech throws. Never throws.
        ///
        /// <para>An interrupting announcement silences both voices. When the reader takes it, Windows speech is
        /// cancelled too (<paramref name="sapiCancelAll"/>): while the reader was away (closed, restarting) its
        /// messages queued up in Windows speech, and without this that backlog talks over the reader with nothing
        /// to stop it. A queued announcement cancels nothing: what Windows speech still holds was never heard.</para>
        /// </summary>
        internal static SpokenBy Route(string message, bool interrupt, AnnouncementMode mode,
            Func<string, bool, bool> readerTrySpeak, Action<string>? sapiSpeak, Action? sapiCancelAll)
        {
            bool readerFirst = TriesScreenReader(mode, sapiAvailable: sapiSpeak != null);
            if (readerFirst && ReaderTook(readerTrySpeak, message, interrupt))
            {
                if (interrupt && sapiCancelAll != null)
                {
                    try { sapiCancelAll(); }
                    catch (Exception ex) { Log.Debug("Accessibility", $"Windows speech could not be silenced: {ex.Message}"); }
                }
                return SpokenBy.ScreenReader;
            }

            if (sapiSpeak != null)
            {
                try
                {
                    if (interrupt) sapiCancelAll?.Invoke();
                    sapiSpeak(message);
                    return SpokenBy.WindowsSpeech;
                }
                catch (Exception ex)
                {
                    Log.Debug("Accessibility", $"Windows speech failed ({ex.Message}); trying the screen reader");
                }
            }

            // Asked once: a reader that already refused this message is not asked again.
            if (!readerFirst && ReaderTook(readerTrySpeak, message, interrupt)) return SpokenBy.ScreenReader;
            Log.Debug("Accessibility", "Warning: All speech methods failed");
            return SpokenBy.Nobody;
        }

        private static bool ReaderTook(Func<string, bool, bool> readerTrySpeak, string message, bool interrupt)
        {
            try
            {
                return readerTrySpeak(message, interrupt);
            }
            catch (Exception ex)
            {
                Log.Debug("Accessibility", $"Error announcing through the screen reader: {ex.Message}");
                return false;
            }
        }

        public virtual void AnnounceImmediate(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            // Log.Debug("Accessibility", $"[ScreenReaderAnnouncer] AnnounceImmediate called - Mode: {currentMode}, Message: {message}");

            // Cutting off what is being said, on the reader and on Windows speech alike.
            Deliver(message, interrupt: true);
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

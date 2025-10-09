using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FBWBA.Accessibility
{
    public class ScreenReaderAnnouncer : IDisposable
    {
        // SAPI for fallback TTS
        private dynamic speechSynthesizer;
        private bool disposed = false;

        // Screen reader integration components
        private TolkWrapper tolkWrapper;
        private NvdaControllerWrapper nvdaWrapper;
        private AnnouncementMode currentMode;

        // Native methods for screen reader detection
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_USER = 0x0400;

        public ScreenReaderAnnouncer(IntPtr handle)
        {
            // Load announcement mode from settings
            LoadAnnouncementMode();
            System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Loaded mode from settings: {currentMode}");

            try
            {
                // Initialize NVDA Controller Client first (most reliable for NVDA)
                nvdaWrapper = new NvdaControllerWrapper();
                bool nvdaRunning = nvdaWrapper.IsRunning;
                System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] NVDA Controller initialized, NVDA running: {nvdaRunning}");

                // Initialize Tolk for other screen readers (JAWS, Window-Eyes, etc.)
                tolkWrapper = new TolkWrapper();
                bool tolkInitialized = tolkWrapper.Initialize();
                System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Tolk initialized: {tolkInitialized}");

                if (tolkInitialized)
                {
                    string detectedSR = tolkWrapper.DetectedScreenReader;
                    bool hasScreenReader = tolkWrapper.IsScreenReaderRunning();
                    bool hasSpeech = tolkWrapper.HasSpeech();
                    System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Tolk detected screen reader: {detectedSR}, Has SR: {hasScreenReader}, Has Speech: {hasSpeech}");
                }

                // Report overall status
                if (nvdaRunning)
                {
                    System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] NVDA detected and will be used directly");
                }
                else if (tolkInitialized && tolkWrapper.IsScreenReaderRunning())
                {
                    System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Other screen reader detected via Tolk: {tolkWrapper.DetectedScreenReader}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] No screen readers detected - will use SAPI fallback");
                }

                // Suggest ScreenReader mode if any screen reader is detected but mode is not set correctly
                if ((nvdaRunning || (tolkInitialized && tolkWrapper.IsScreenReaderRunning())) && currentMode != AnnouncementMode.ScreenReader)
                {
                    System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Screen reader detected but mode is {currentMode} - consider switching to ScreenReader mode");
                }

                // Initialize SAPI as fallback method
                Type spVoiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                if (spVoiceType != null)
                {
                    speechSynthesizer = Activator.CreateInstance(spVoiceType);
                    // Set to async mode
                    speechSynthesizer.Volume = 100;
                    speechSynthesizer.Rate = 0;
                    System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] SAPI initialized successfully");
                }
            }
            catch (Exception ex)
            {
                // Fallback to SAPI only
                System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Initialization error: {ex.Message}, falling back to SAPI");
                currentMode = AnnouncementMode.SAPI;
            }
        }

        private void LoadAnnouncementMode()
        {
            try
            {
                string modeString = Properties.Settings.Default.AnnouncementMode;
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
                Properties.Settings.Default.AnnouncementMode = mode.ToString();
                Properties.Settings.Default.Save();
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

        private bool IsAnyScreenReaderRunning()
        {
            bool nvdaRunning = nvdaWrapper?.IsRunning == true;
            bool tolkRunning = tolkWrapper?.IsScreenReaderRunning() == true;
            return nvdaRunning || tolkRunning;
        }

        public void TestScreenReaderConnection()
        {
            System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] === Screen Reader Diagnostic Test ===");
            System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Current announcement mode: {currentMode}");

            // Test NVDA Controller Client
            if (nvdaWrapper != null)
            {
                bool nvdaRunning = nvdaWrapper.IsRunning;
                System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] NVDA Controller - Running: {nvdaRunning}");

                if (nvdaRunning)
                {
                    uint? pid = nvdaWrapper.ProcessId;
                    System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] NVDA Process ID: {pid}");

                    // Test speech (commented out to prevent test announcements)
                    System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] NVDA Controller speech capability detected (test speech disabled)");
                    // bool speechResult = nvdaWrapper.Speak("NVDA test from FBWBA", false);
                    // System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] NVDA Controller speech test result: {speechResult}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] NVDA Controller wrapper is null");
            }

            // Test Tolk
            if (tolkWrapper != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Tolk wrapper loaded: {tolkWrapper.IsLoaded}");

                if (tolkWrapper.IsLoaded)
                {
                    string detected = tolkWrapper.DetectedScreenReader;
                    bool isRunning = tolkWrapper.IsScreenReaderRunning();
                    bool hasSpeech = tolkWrapper.HasSpeech();
                    bool hasBraille = tolkWrapper.HasBraille();

                    System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Tolk detected screen reader: {detected}");
                    System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Tolk screen reader running: {isRunning}");
                    System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Tolk has speech: {hasSpeech}");
                    System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Tolk has braille: {hasBraille}");

                    // Test speech (commented out to prevent test announcements)
                    if (hasSpeech)
                    {
                        System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Tolk speech capability detected (test speech disabled)");
                        // bool speechResult = tolkWrapper.Speak("Tolk test from FBWBA", false);
                        // System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Tolk speech test result: {speechResult}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Tolk wrapper not loaded");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Tolk wrapper is null");
            }

            System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] SAPI available: {speechSynthesizer != null}");
            System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Any screen reader running: {IsAnyScreenReaderRunning()}");
            System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] === End Diagnostic Test ===");
        }

        public void Announce(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            // System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Announce called - Mode: {currentMode}, Message: {message}");

            try
            {
                bool success = false;

                // Try screen readers if that's the selected mode or if any screen reader is detected
                if (currentMode == AnnouncementMode.ScreenReader || IsAnyScreenReaderRunning())
                {
                    // First priority: Try NVDA Controller Client if NVDA is running
                    if (nvdaWrapper?.IsRunning == true)
                    {
                        // System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Attempting NVDA Controller speech...");
                        success = nvdaWrapper.Speak(message, false);
                        // System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] NVDA Controller speech result: {success}");
                    }

                    // Second priority: Try Tolk for other screen readers if NVDA failed or not running
                    if (!success && tolkWrapper?.IsScreenReaderRunning() == true)
                    {
                        // System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Attempting Tolk speech...");
                        success = tolkWrapper.Speak(message, false);
                        // System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Tolk speech result: {success}");

                        if (!success)
                        {
                            System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Tolk speech failed - checking screen reader status");
                            string detectedSR = tolkWrapper.DetectedScreenReader;
                            bool hasSpeech = tolkWrapper.HasSpeech();
                            System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Current status - Detected: {detectedSR}, HasSpeech: {hasSpeech}");
                        }
                    }
                }

                // If screen reader failed or SAPI mode selected, use SAPI
                if (!success && speechSynthesizer != null)
                {
                    // System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Using SAPI speech");
                    // Flag 1 = async, 2 = purge before speak (for queued speech)
                    speechSynthesizer.Speak(message, 1);
                    success = true; // Mark as successful since SAPI doesn't return status
                }

                // Always output to console as fallback - some screen readers pick this up
                Console.WriteLine(message);

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Warning: All speech methods failed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Error announcing: {ex.Message}");
                // Final fallback: console only
                Console.WriteLine(message);
            }
        }

        public void AnnounceImmediate(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            // System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] AnnounceImmediate called - Mode: {currentMode}, Message: {message}");

            try
            {
                bool success = false;

                // Try screen readers if that's the selected mode or if any screen reader is detected (with interrupt)
                if (currentMode == AnnouncementMode.ScreenReader || IsAnyScreenReaderRunning())
                {
                    // First priority: Try NVDA Controller Client if NVDA is running (with interrupt)
                    if (nvdaWrapper?.IsRunning == true)
                    {
                        // System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Attempting immediate NVDA Controller speech with interrupt...");
                        success = nvdaWrapper.Speak(message, true); // true = interrupt
                        // System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Immediate NVDA Controller speech result: {success}");
                    }

                    // Second priority: Try Tolk for other screen readers if NVDA failed or not running
                    if (!success && tolkWrapper?.IsScreenReaderRunning() == true)
                    {
                        // System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Attempting immediate Tolk speech with interrupt...");
                        success = tolkWrapper.Speak(message, true); // true = interrupt
                        // System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Immediate Tolk speech result: {success}");
                    }
                }

                // If screen reader failed or SAPI mode selected, use SAPI with interrupt
                if (!success && speechSynthesizer != null)
                {
                    // System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Using immediate SAPI speech with interrupt");
                    // 2 = purge before speak (interrupt), 1 = async
                    speechSynthesizer.Speak(message, 3);
                    success = true;
                }

                // Always output to console as fallback with priority marker
                Console.WriteLine($"! {message}");

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine("[ScreenReaderAnnouncer] Warning: All immediate speech methods failed");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenReaderAnnouncer] Error in immediate announce: {ex.Message}");
                // Final fallback: console only
                Console.WriteLine($"! {message}");
            }
        }

        public void AnnounceQueued(string message)
        {
            Announce(message);
        }
        
        public void Cleanup()
        {
            Dispose();
        }
        
        public void Dispose()
        {
            if (!disposed)
            {
                if (speechSynthesizer != null)
                {
                    try
                    {
                        Marshal.ReleaseComObject(speechSynthesizer);
                    }
                    catch { }
                    speechSynthesizer = null;
                }

                if (tolkWrapper != null)
                {
                    try
                    {
                        tolkWrapper.Dispose();
                    }
                    catch { }
                    tolkWrapper = null;
                }

                if (nvdaWrapper != null)
                {
                    try
                    {
                        nvdaWrapper.Dispose();
                    }
                    catch { }
                    nvdaWrapper = null;
                }

                disposed = true;
            }
        }
    }
}

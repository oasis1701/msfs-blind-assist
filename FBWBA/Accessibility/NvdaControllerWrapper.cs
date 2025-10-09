using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FBWBA.Accessibility
{
    public class NvdaControllerWrapper : IDisposable
    {
        private bool _disposed = false;

        // P/Invoke declarations for nvdaControllerClient.dll
        [DllImport("nvdaControllerClient.dll", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_brailleMessage(string message);

        [DllImport("nvdaControllerClient.dll")]
        private static extern int nvdaController_cancelSpeech();

        [DllImport("nvdaControllerClient.dll", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_speakText(string text);

        [DllImport("nvdaControllerClient.dll")]
        private static extern int nvdaController_testIfRunning();

        [DllImport("nvdaControllerClient.dll", CharSet = CharSet.Unicode)]
        private static extern int nvdaController_getProcessId(out uint processId);

        public bool IsRunning
        {
            get
            {
                try
                {
                    int result = nvdaController_testIfRunning();
                    bool isRunning = result == 0;
                    return isRunning;
                }
                catch (Exception ex)
                {
                    return false;
                }
            }
        }

        public uint? ProcessId
        {
            get
            {
                try
                {
                    uint pid;
                    int result = nvdaController_getProcessId(out pid);
                    if (result == 0)
                    {
                        return pid;
                    }
                    else
                    {
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    return null;
                }
            }
        }

        public bool Speak(string text, bool interrupt = false)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            try
            {
                if (interrupt)
                {
                    int cancelResult = nvdaController_cancelSpeech();
                    if (cancelResult != 0)
                    {
                    }
                }

                int result = nvdaController_speakText(text);
                bool success = result == 0;

                if (success)
                {
                }
                else
                {
                }

                return success;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public bool Braille(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            try
            {
                int result = nvdaController_brailleMessage(message);
                bool success = result == 0;

                if (success)
                {
                }
                else
                {
                }

                return success;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public bool CancelSpeech()
        {
            try
            {
                int result = nvdaController_cancelSpeech();
                bool success = result == 0;

                if (success)
                {
                }
                else
                {
                }

                return success;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
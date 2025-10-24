
namespace MSFSBlindAssist.Accessibility;
public enum AnnouncementMode
{
    ScreenReader,
    SAPI
}

public class TolkWrapper : IDisposable
{
    private bool _isLoaded = false;
    private bool _disposed = false;

    // P/Invoke declarations for Tolk.dll (uses wide character functions)
    [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Tolk_Load();

    [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Tolk_Unload();

    [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool Tolk_IsLoaded();

    [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Tolk_TrySAPI([MarshalAs(UnmanagedType.Bool)] bool trySAPI);

    [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Tolk_Output([MarshalAs(UnmanagedType.LPWStr)] string text, [MarshalAs(UnmanagedType.Bool)] bool interrupt);

    [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr Tolk_DetectScreenReader();

    [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Tolk_HasSpeech();

    [DllImport("Tolk.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Tolk_HasBraille();

    public bool IsLoaded => _isLoaded;

    public string DetectedScreenReader
    {
        get
        {
            if (!_isLoaded) return "None";
            try
            {
                IntPtr ptr = Tolk_DetectScreenReader();
                if (ptr == IntPtr.Zero) return "None";

                // Tolk returns wide character strings
                string result = Marshal.PtrToStringUni(ptr) ?? "None";
                // System.Diagnostics.Debug.WriteLine($"[TolkWrapper] DetectScreenReader returned: '{result}'");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TolkWrapper] Error in DetectScreenReader: {ex.Message}");
                return "Error";
            }
        }
    }

    public bool Initialize()
    {
        try
        {
            if (_isLoaded) return true;

            // System.Diagnostics.Debug.WriteLine("[TolkWrapper] Initializing Tolk...");

            // First load Tolk
            Tolk_Load();
            // System.Diagnostics.Debug.WriteLine("[TolkWrapper] Tolk_Load() called");

            _isLoaded = Tolk_IsLoaded();
            // System.Diagnostics.Debug.WriteLine($"[TolkWrapper] Tolk_IsLoaded() returned: {_isLoaded}");

            // Only configure SAPI after successful load
            if (_isLoaded)
            {
                Tolk_TrySAPI(true); // Enable SAPI fallback after load
                // System.Diagnostics.Debug.WriteLine("[TolkWrapper] TrySAPI(true) called after load");

                // Check what we have available
                string detected = DetectedScreenReader;
                bool hasSpeech = HasSpeech();
                // System.Diagnostics.Debug.WriteLine($"[TolkWrapper] Detected: {detected}, HasSpeech: {hasSpeech}");
            }

            return _isLoaded;
        }
        catch (Exception)
        {
            // System.Diagnostics.Debug.WriteLine($"[TolkWrapper] Failed to initialize Tolk");
            return false;
        }
    }

    public bool Speak(string text, bool interrupt = false)
    {
        if (!_isLoaded || string.IsNullOrEmpty(text))
        {
            // System.Diagnostics.Debug.WriteLine($"[TolkWrapper] Speak failed - IsLoaded: {_isLoaded}, Text empty: {string.IsNullOrEmpty(text)}");
            return false;
        }

        try
        {
            // System.Diagnostics.Debug.WriteLine($"[TolkWrapper] Calling Tolk_Output with: '{text}', interrupt: {interrupt}");
            bool result = Tolk_Output(text, interrupt);
            // System.Diagnostics.Debug.WriteLine($"[TolkWrapper] Tolk_Output returned: {result}");
            return result;
        }
        catch (Exception)
        {
            // System.Diagnostics.Debug.WriteLine($"[TolkWrapper] Failed to speak via Tolk");
            return false;
        }
    }

    public bool IsScreenReaderRunning()
    {
        if (!_isLoaded) return false;

        try
        {
            string detected = DetectedScreenReader;
            bool isRunning = !string.IsNullOrEmpty(detected) && detected != "None" && detected != "SAPI" && detected != "Error";
            // System.Diagnostics.Debug.WriteLine($"[TolkWrapper] IsScreenReaderRunning: {isRunning} (detected: {detected})");
            return isRunning;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TolkWrapper] Error checking screen reader: {ex.Message}");
            return false;
        }
    }

    public bool HasSpeech()
    {
        if (!_isLoaded) return false;
        try
        {
            return Tolk_HasSpeech();
        }
        catch
        {
            return false;
        }
    }

    public bool HasBraille()
    {
        if (!_isLoaded) return false;
        try
        {
            return Tolk_HasBraille();
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_isLoaded)
            {
                try
                {
                    Tolk_Unload();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error unloading Tolk: {ex.Message}");
                }
                _isLoaded = false;
            }
            _disposed = true;
        }
    }
}
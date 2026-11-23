using MSFSBlindAssist.Utils.Logging;

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

    // Tolk's load state belongs to the process, not to a wrapper: every TolkWrapper shares this count.
    private static readonly SharedLoadCount SharedLoad = new();

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
                // Log.Debug("Accessibility", $"[TolkWrapper] DetectScreenReader returned: '{result}'");
                return result;
            }
            catch (Exception ex)
            {
                Log.Debug("Accessibility", $"Error in DetectScreenReader: {ex.Message}");
                return "Error";
            }
        }
    }

    public bool Initialize()
    {
        try
        {
            if (_isLoaded) return true;

            // Log.Debug("Accessibility", "[TolkWrapper] Initializing Tolk...");

            // Load Tolk for the first wrapper only; a later one joins the session that is already up
            // (SharedLoadCount).
            _isLoaded = SharedLoad.Acquire(Tolk_Load, Tolk_IsLoaded);
            // Log.Debug("Accessibility", $"[TolkWrapper] Tolk loaded: {_isLoaded}");

            // Only configure SAPI after successful load
            if (_isLoaded)
            {
                Tolk_TrySAPI(true); // Enable SAPI fallback after load
                // Log.Debug("Accessibility", "[TolkWrapper] TrySAPI(true) called after load");

                // Check what we have available
                string detected = DetectedScreenReader;
                bool hasSpeech = HasSpeech();
                // Log.Debug("Accessibility", $"[TolkWrapper] Detected: {detected}, HasSpeech: {hasSpeech}");
            }

            return _isLoaded;
        }
        catch (Exception)
        {
            // Log.Debug("Accessibility", $"[TolkWrapper] Failed to initialize Tolk");
            return false;
        }
    }

    public bool Speak(string text, bool interrupt = false)
    {
        if (!_isLoaded || string.IsNullOrEmpty(text))
        {
            // Log.Debug("Accessibility", $"[TolkWrapper] Speak failed - IsLoaded: {_isLoaded}, Text empty: {string.IsNullOrEmpty(text)}");
            return false;
        }

        try
        {
            // Log.Debug("Accessibility", $"[TolkWrapper] Calling Tolk_Output with: '{text}', interrupt: {interrupt}");
            bool result = Tolk_Output(text, interrupt);
            // Log.Debug("Accessibility", $"[TolkWrapper] Tolk_Output returned: {result}");
            return result;
        }
        catch (Exception)
        {
            // Log.Debug("Accessibility", $"[TolkWrapper] Failed to speak via Tolk");
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
            // Log.Debug("Accessibility", $"[TolkWrapper] IsScreenReaderRunning: {isRunning} (detected: {detected})");
            return isRunning;
        }
        catch (Exception ex)
        {
            Log.Debug("Accessibility", $"Error checking screen reader: {ex.Message}");
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
                    // Unloads only when this was the last wrapper: the Settings status check disposes its own
                    // wrapper while the announcer's is still speaking through Tolk.
                    SharedLoad.Release(Tolk_Unload);
                }
                catch (Exception ex)
                {
                    Log.Debug("Accessibility", $"Error unloading Tolk: {ex.Message}");
                }
                _isLoaded = false;
            }
            _disposed = true;
        }
    }
}

/// <summary>Counts the users of a library whose load state is process-wide and not reference counted, so that it
/// is loaded for the first user and unloaded only when the last one leaves. Tolk is one: measured 2026-11-23,
/// Tolk_Load twice then Tolk_Unload once leaves Tolk_IsLoaded false, so a second TolkWrapper that was disposed
/// (the Settings status check) took the announcer's session down with it, and JAWS users fell back to SAPI.</summary>
internal sealed class SharedLoadCount
{
    private readonly object _gate = new();
    private int _users;

    /// <summary>Runs <paramref name="load"/> for the first user only, then counts this user when
    /// <paramref name="isLoaded"/> says the library is up. False, and nothing counted, when it is not.</summary>
    public bool Acquire(Action load, Func<bool> isLoaded)
    {
        lock (_gate)
        {
            if (_users == 0) load();
            if (!isLoaded()) return false;
            _users++;
            return true;
        }
    }

    /// <summary>Runs <paramref name="unload"/> when the last counted user leaves; does nothing for a release
    /// nobody acquired.</summary>
    public void Release(Action unload)
    {
        lock (_gate)
        {
            if (_users == 0) return;
            _users--;
            if (_users == 0) unload();
        }
    }
}
namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Simple file logger for PMDG 777 debugging.
/// Writes to pmdg777_debug.log in the app directory.
/// </summary>
public static class PMDG777Debug
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "pmdg777_debug.log");
    private static readonly object Lock = new();

    public static void Log(string message)
    {
        try
        {
            lock (Lock)
            {
                File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
        }
        catch { /* swallow logging errors */ }
    }

    public static void Clear()
    {
        try { File.Delete(LogPath); } catch { }
    }
}

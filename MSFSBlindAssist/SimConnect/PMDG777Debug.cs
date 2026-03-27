namespace MSFSBlindAssist.SimConnect;

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
        catch { }
    }

    public static void Clear()
    {
        try { File.Delete(LogPath); } catch { }
    }
}

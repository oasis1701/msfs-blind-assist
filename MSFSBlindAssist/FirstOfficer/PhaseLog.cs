using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Tiny append-only diagnostic log for the FO flight-phase monitors
/// (<c>%APPDATA%\MSFSBlindAssist\logs\fo-phase.log</c>). Low volume by design —
/// threshold sets, latch transitions and crossing fires only, never per-tick.
/// Exists to make "altimeters never went to STD" reports diagnosable after the
/// fact: the log shows whether thresholds were loaded, whether the latch armed,
/// and whether/when the crossing fired.
/// </summary>
internal static class PhaseLog
{
    private static readonly object _lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (_lock)
                File.AppendAllText(AppLogs.PathFor("fo-phase.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.f} {message}\r\n");
        }
        catch { /* diagnostics must never break the monitor */ }
    }
}

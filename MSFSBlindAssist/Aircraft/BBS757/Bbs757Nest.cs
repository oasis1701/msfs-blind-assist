using System.Diagnostics;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// BBS Nest (<c>C:\BBS Nest\bbs_nest.exe</c>) is BlueBird's companion app, its own SimConnect
/// client. Without it the 757 takes every switch write into its cockpit model and no system
/// reacts: measured for a whole session with the battery bus dead and the APU never turning
/// (2026-10-25; docs/b757.md, "BBS Nest"). The aircraft's own warning is on a display,
/// so the app checks for the process itself.
/// </summary>
public static class Bbs757Nest
{
    /// <summary>The process name Windows reports for <c>bbs_nest.exe</c>.</summary>
    public const string ProcessName = "bbs_nest";

    /// <summary>
    /// True when BBS Nest is running. <paramref name="processExists"/> answers "is a process with
    /// this name running?" (tests pass their own); the default asks Windows. When Windows cannot
    /// be asked the answer is true: a false "not running" would send the pilot after a problem
    /// that is not there.
    /// </summary>
    public static bool IsRunning(Func<string, bool>? processExists = null)
        => (processExists ?? AnyProcessNamed)(ProcessName);

    private static bool AnyProcessNamed(string name)
    {
        try
        {
            var found = Process.GetProcessesByName(name);
            foreach (var p in found) p.Dispose();
            return found.Length > 0;
        }
        catch (Exception ex)
        {
            Log.Warn("BBS757", $"Could not list processes to look for {name}: {ex.Message}");
            return true;
        }
    }
}

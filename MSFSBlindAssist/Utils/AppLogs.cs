using System;
using System.IO;

namespace MSFSBlindAssist.Utils;

/// <summary>
/// THE single home for every MSFSBA diagnostic log:
/// <c>%APPDATA%\MSFSBlindAssist\logs</c> (Roaming).
/// <para>
/// Historically logs were scattered — taxi/rollout logs in the ROAMING root
/// (<c>%APPDATA%\MSFSBlindAssist\</c>), the startup log in <c>%TEMP%</c>, and the
/// GSX/docking logs in <c>%LOCALAPPDATA%\MSFSBlindAssist\logs</c> — which made
/// "send me your logs" support impossible to explain. They were first unified
/// under Local, then moved here so EVERYTHING the app owns (settings, databases,
/// logs) lives in ONE Roaming tree: a tester opens
/// <c>%APPDATA%\MSFSBlindAssist</c> and finds all of it. Every log writer MUST
/// resolve its path through <see cref="PathFor"/> — do not hand-build log paths.
/// <see cref="MigrateLegacyLogs"/> sweeps both legacy locations (the Roaming
/// ROOT <c>*.log</c> files and the entire Local logs folder) into this folder
/// once at startup.
/// </para>
/// </summary>
public static class AppLogs
{
    /// <summary>The one logs directory: <c>%APPDATA%\MSFSBlindAssist\logs</c>.</summary>
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MSFSBlindAssist", "logs");

    /// <summary>
    /// Full path for a log file inside the canonical logs folder. Ensures the folder
    /// exists (best-effort — never throws), so callers can append without their own
    /// CreateDirectory dance.
    /// </summary>
    public static string PathFor(string fileName)
    {
        try { Directory.CreateDirectory(Dir); } catch { /* caller's append will no-op into its own catch */ }
        return Path.Combine(Dir, fileName);
    }

    /// <summary>
    /// One-time, best-effort consolidation of legacy log files into <see cref="Dir"/>:
    /// (a) the old Roaming-ROOT location (<c>%APPDATA%\MSFSBlindAssist\*.log</c>) used
    /// before the first unification, and (b) the entire former canonical folder
    /// (<c>%LOCALAPPDATA%\MSFSBlindAssist\logs</c>, all files — it held only
    /// diagnostics, including non-<c>.log</c> ones like <c>input_events.txt</c>).
    /// A tester's machine ends up with exactly ONE logs folder regardless of which
    /// builds they ran. Never throws; a file that can't move (locked, unreadable)
    /// is simply left behind — harmless, just stale.
    /// </summary>
    public static void MigrateLegacyLogs()
    {
        string roamingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MSFSBlindAssist");
        string localLogs = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MSFSBlindAssist", "logs");

        MigrateFrom(roamingRoot, "*.log");
        MigrateFrom(localLogs, "*");

        // Drop the legacy Local logs folder once empty so testers can't find a
        // stale second location. Best-effort: fails silently if anything remains.
        try { Directory.Delete(localLogs); } catch { /* non-empty or locked — fine */ }
    }

    private static void MigrateFrom(string sourceDir, string pattern)
    {
        try
        {
            if (!Directory.Exists(sourceDir)) return;
            Directory.CreateDirectory(Dir);
            foreach (string src in Directory.GetFiles(sourceDir, pattern))
            {
                try
                {
                    string dest = Path.Combine(Dir, Path.GetFileName(src));
                    if (string.Equals(src, dest, StringComparison.OrdinalIgnoreCase))
                        continue; // already canonical (defensive: source IS the target dir)
                    if (File.Exists(dest))
                    {
                        // Same-named log already exists in the new home (e.g. created
                        // since the update) — preserve the legacy content by appending
                        // it under a marker, then drop the legacy file.
                        File.AppendAllText(dest,
                            Environment.NewLine + $"# --- migrated legacy content from {src} ---" + Environment.NewLine
                            + File.ReadAllText(src));
                        File.Delete(src);
                    }
                    else
                    {
                        File.Move(src, dest);
                    }
                }
                catch { /* locked or unreadable — leave it; never block startup */ }
            }
        }
        catch { /* migration is convenience only */ }
    }
}

using System;
using System.IO;

namespace MSFSBlindAssist.Utils;

/// <summary>
/// THE single home for every MSFSBA diagnostic log:
/// <c>%LOCALAPPDATA%\MSFSBlindAssist\logs</c>.
/// <para>
/// Historically logs were scattered — taxi/rollout logs in the ROAMING root
/// (<c>%APPDATA%\MSFSBlindAssist\</c>), the startup log in <c>%TEMP%</c>, and the
/// GSX/docking logs here — which made "send me your logs" support impossible to
/// explain (testers opened the Roaming folder, found settings + databases, and no
/// logs). Every log writer now resolves its path through <see cref="PathFor"/>,
/// and <see cref="MigrateLegacyLogs"/> moves any pre-existing Roaming-root log
/// files into this folder once at startup. New log files MUST use
/// <see cref="PathFor"/> — do not hand-build log paths again.
/// </para>
/// <para>
/// Local (not Roaming) is deliberate: diagnostics are machine-specific and should
/// not follow a roaming Windows profile across machines. Settings + databases stay
/// in Roaming — only logs live here.
/// </para>
/// </summary>
public static class AppLogs
{
    /// <summary>The one logs directory: <c>%LOCALAPPDATA%\MSFSBlindAssist\logs</c>.</summary>
    public static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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
    /// One-time, best-effort move of legacy log files from the old Roaming-root
    /// location (<c>%APPDATA%\MSFSBlindAssist\*.log</c>) into <see cref="Dir"/>, so
    /// a tester's machine ends up with exactly ONE logs folder even if they ran
    /// older builds. Never throws; a file that can't move (locked, name collision)
    /// is simply left behind — harmless, just stale.
    /// </summary>
    public static void MigrateLegacyLogs()
    {
        try
        {
            string legacyDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MSFSBlindAssist");
            if (!Directory.Exists(legacyDir)) return;
            Directory.CreateDirectory(Dir);
            foreach (string src in Directory.GetFiles(legacyDir, "*.log"))
            {
                try
                {
                    string dest = Path.Combine(Dir, Path.GetFileName(src));
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

using System.Runtime.InteropServices;
using Microsoft.Win32;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.VPilot;

public enum VPilotInstallStatus
{
    /// <summary>The DLL was written (first install or an update).</summary>
    Installed,
    /// <summary>The installed DLL already matches the one we ship.</summary>
    AlreadyCurrent,
    /// <summary>No vPilot folder could be found by any of the three routes.</summary>
    VPilotNotFound,
    /// <summary>An OLDER DLL is installed and vPilot currently holds it open.</summary>
    Locked,
    /// <summary>Anything else — permissions, a missing shipped file, a disk error.</summary>
    Failed,
}

public sealed record VPilotInstallResult(
    VPilotInstallStatus Status,
    string Detail,
    bool LegacyRemoved,
    string? PluginsFolder);

/// <summary>
/// Puts the vPilot plugin where vPilot will find it, and takes the old standalone
/// vPilot-to-TTS plugin away.
///
/// Every method here writes outside our own tree, so every method is best-effort and
/// NEVER throws — a failed install must degrade to a status the settings dialog can
/// explain, not an exception in the middle of pressing OK.
/// </summary>
public static class VPilotPluginInstaller
{
    public const string PluginFileName = "MSFSBlindAssist.VPilotPlugin.dll";

    /// <summary>The standalone vPilot-to-TTS project's plugin. Removed on install: left
    /// in place it announces the same events a second time.</summary>
    public const string LegacyPluginFileName = "vPilot-to-TTS.dll";

    private const string ShippedSubfolder = "vPilotPlugin";

    /// <summary>
    /// Pure resolution, filesystem probe injected. Order: user override, then vPilot's
    /// own registry key, then the default install location. A candidate may name either
    /// the vPilot install folder or the Plugins folder itself — Browse accepts both, and
    /// the user should not have to know which one we wanted.
    /// </summary>
    public static string? ResolvePluginsFolder(
        string? overridePath, string? registryInstallDir, string? localAppData,
        Func<string, bool> directoryExists)
    {
        string? defaultInstall = string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, "vPilot");

        foreach (string? candidate in new[] { overridePath, registryInstallDir, defaultInstall })
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            string trimmed = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (trimmed.Length == 0)
                continue;

            // Already the Plugins folder (the user browsed straight to it).
            if (string.Equals(Path.GetFileName(trimmed), "Plugins", StringComparison.OrdinalIgnoreCase)
                && directoryExists(trimmed))
                return trimmed;

            string plugins = Path.Combine(trimmed, "Plugins");
            if (directoryExists(plugins))
                return plugins;

            // vPilot is here but has never loaded a plugin. Install() creates the folder.
            if (directoryExists(trimmed))
                return plugins;
        }

        return null;
    }

    public static string? FindPluginsFolder() =>
        FindPluginsFolder(SettingsManager.Current.VPilotPluginsFolderOverride);

    /// <summary>
    /// Resolve exactly as <see cref="FindPluginsFolder()"/> does, but against a
    /// caller-supplied override rather than the saved one — so the settings panel can
    /// preview an unsaved Browse result without its candidate list diverging from the
    /// path the install will actually take. A preview that consults fewer candidates
    /// than the installer can report "vPilot was not found" about a vPilot the
    /// installer finds immediately.
    /// </summary>
    public static string? FindPluginsFolder(string? overridePath)
    {
        try
        {
            return ResolvePluginsFolder(
                overridePath,
                ReadRegistryInstallDir(),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Directory.Exists);
        }
        catch (Exception ex)
        {
            Log.Debug("VPilot", $"Plugins folder resolution failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Path of the DLL we ship, inside our own output folder.</summary>
    public static string ShippedPluginPath =>
        Path.Combine(AppContext.BaseDirectory, ShippedSubfolder, PluginFileName);

    /// <summary>True when the installed DLL matches the shipped one (same length and
    /// last-write time — File.Copy preserves both, so this is an exact match, not a heuristic).</summary>
    public static bool IsPluginCurrent(string pluginsFolder)
    {
        try
        {
            var source = new FileInfo(ShippedPluginPath);
            var dest = new FileInfo(Path.Combine(pluginsFolder, PluginFileName));
            return source.Exists && dest.Exists
                && source.Length == dest.Length
                && source.LastWriteTimeUtc == dest.LastWriteTimeUtc;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsPluginInstalled(string pluginsFolder)
    {
        try { return File.Exists(Path.Combine(pluginsFolder, PluginFileName)); }
        catch { return false; }
    }

    public static VPilotInstallResult Install()
    {
        string? pluginsFolder = FindPluginsFolder();
        if (pluginsFolder == null)
            return new VPilotInstallResult(VPilotInstallStatus.VPilotNotFound,
                "vPilot was not found.", false, null);

        string source = ShippedPluginPath;
        if (!File.Exists(source))
        {
            Log.Warn("VPilot", $"Shipped plugin missing at {source}");
            return new VPilotInstallResult(VPilotInstallStatus.Failed,
                "The plugin file is missing from this installation.", false, pluginsFolder);
        }

        bool legacyRemoved = false;
        try
        {
            Directory.CreateDirectory(pluginsFolder);
            legacyRemoved = RemoveLegacyPlugin(pluginsFolder);

            string dest = Path.Combine(pluginsFolder, PluginFileName);
            bool destExisted = File.Exists(dest);

            if (destExisted && IsPluginCurrent(pluginsFolder))
                return new VPilotInstallResult(VPilotInstallStatus.AlreadyCurrent,
                    "The plugin is installed and up to date.", legacyRemoved, pluginsFolder);

            try
            {
                File.Copy(source, dest, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only an EXISTING file can be locked. A first install succeeds even
                // with vPilot running (nothing holds a file that isn't there yet) —
                // vPilot simply won't load it until it restarts. Do not merge these.
                if (destExisted)
                {
                    Log.Debug("VPilot", $"Plugin update blocked, vPilot holds the file: {ex.Message}");
                    return new VPilotInstallResult(VPilotInstallStatus.Locked,
                        "vPilot is running with an older plugin.", legacyRemoved, pluginsFolder);
                }
                throw;
            }

            // Strip Mark of the Web. If the user extracted the release zip without
            // unblocking it, the copy inherits the zone stream and .NET refuses to load
            // the assembly into vPilot.
            DeleteFile(dest + ":Zone.Identifier");

            Log.Info("VPilot", $"Plugin installed to {dest}");
            return new VPilotInstallResult(VPilotInstallStatus.Installed,
                "The plugin was installed.", legacyRemoved, pluginsFolder);
        }
        catch (Exception ex)
        {
            Log.Warn("VPilot", $"Plugin install failed: {ex.Message}");
            return new VPilotInstallResult(VPilotInstallStatus.Failed,
                "The plugin could not be installed.", legacyRemoved, pluginsFolder);
        }
    }

    private static bool RemoveLegacyPlugin(string pluginsFolder)
    {
        try
        {
            string legacy = Path.Combine(pluginsFolder, LegacyPluginFileName);
            if (!File.Exists(legacy)) return false;
            File.Delete(legacy);
            Log.Info("VPilot", $"Removed the legacy vPilot-to-TTS plugin from {pluginsFolder}");
            return true;
        }
        catch (Exception ex)
        {
            // Locked by a running vPilot. Harmless — it talks to a pipe name we no
            // longer use — and the next startup will try again.
            Log.Debug("VPilot", $"Legacy plugin removal skipped: {ex.Message}");
            return false;
        }
    }

    private static string? ReadRegistryInstallDir()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\vPilot");
            return key?.GetValue("Install_Dir") as string;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFile(string lpFileName);
}

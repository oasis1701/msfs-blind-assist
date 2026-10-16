using System.ComponentModel;
using System.Security;
using System.Threading;
using Microsoft.Win32;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Volanta;

public enum VolantaAccessState
{
    /// <summary>Volanta.exe was not found: not where its installer puts it, not registered as installed in
    /// Windows' uninstall keys, and no running copy says otherwise.</summary>
    NotInstalled,
    NotRunning,
    /// <summary>Volanta runs but was not started with live map access: its own autostart, the sim, an update
    /// or the pilot started it. It must be restarted to gain it.</summary>
    RunningWithoutAccess,
    RunningWithAccess,
}

/// <summary>
/// Starts Volanta with live map access: a local-only Chromium debugging port (<see cref="VolantaLink.DebugPort"/>).
/// Volanta runs one copy at a time, so a running Volanta must be gone first; the switch on a second launch would
/// only bring the first one forward. A restart is two steps: <see cref="ForceCloseAsync"/>, then
/// <see cref="StartWithAccess"/>. Asking Volanta to quit was not reliable: with its "show in tray" setting on, its
/// close button (and Alt+F4) only hide it in the notification area (measured 2026-10-15), so the owner chose on
/// 2026-10-15 to force-close it straight away. Volanta kept tracking the same flight across every restart. The
/// window always asks the pilot first, and says Volanta will be force-closed.
/// </summary>
public static class VolantaLauncher
{
    private const string ProcessName = "Volanta";
    /// <summary>Volanta's sim plugins: Volanta's own quit stops them first, so a force-close ends them too, or a
    /// stale one stays connected to the sim beside the plugin the new Volanta starts.</summary>
    private static readonly string[] PluginProcessNames = { "VolantaPlugin_x64", "VolantaPlugin_x86" };
    private const string ExeName = "Volanta.exe";
    /// <summary>Windows' uninstall keys, where Volanta's installer registers itself: under the current user for
    /// the usual per-user install, under the machine for an all-users one (either view on a 64-bit Windows).</summary>
    private static readonly string[] UninstallKeys =
    {
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    };
    private static readonly TimeSpan RegistryLookupKeeps = TimeSpan.FromSeconds(30);
    private static readonly object RegistryGate = new();
    private static (DateTime At, string? Path)? _registryLookup;

    /// <summary>What the pilot hears when no Volanta can be found to start. A running Volanta's own exe is read
    /// from its process, so starting Volanta by hand and pressing the button again always works.</summary>
    public const string NotFoundMessage = "Volanta was not found: it is not in its usual folder for this Windows user, "
        + "and Windows lists no Volanta install. If it is installed, start Volanta yourself, then press the Connection "
        + "tab's button to restart it with live map access.";

    /// <summary>The switches that give live map access. The address keeps the port to this PC.</summary>
    public static string LaunchArguments =>
        $"--remote-debugging-port={VolantaLink.DebugPort} --remote-debugging-address=127.0.0.1";

    /// <summary>Where Volanta's installer puts it, for this Windows user.</summary>
    public static string DefaultExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Volanta", ExeName);

    public static bool IsRunning() => CountProcesses() > 0;

    private static int CountProcesses()
    {
        var all = Process.GetProcessesByName(ProcessName);
        foreach (var p in all) p.Dispose();
        return all.Length;
    }

    /// <summary>
    /// Volanta's exe: the running Volanta's own, if one runs and Windows lets us read it; else the default
    /// path; else the install Windows' uninstall keys register (an all-users install, or a moved one). Read it
    /// from the running copy BEFORE a force-close: afterwards only the other two remain.
    /// </summary>
    public static string? FindExePath()
    {
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            using (p)
            {
                try
                {
                    string? path = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { /* exited, or not ours to read */ }
            }
        }
        if (File.Exists(DefaultExePath)) return DefaultExePath;
        return RegisteredExePath();
    }

    /// <summary>The exe of the Volanta install registered in Windows' uninstall keys, or null. Volanta's
    /// installer (measured 1.19.2) writes no InstallLocation, only a DisplayIcon of "&lt;exe&gt;,0"; both are
    /// read. The scan is kept for a while: the Connection tab asks every few seconds while Volanta is not
    /// running.</summary>
    private static string? RegisteredExePath()
    {
        lock (RegistryGate)
        {
            if (_registryLookup is { } kept && DateTime.UtcNow - kept.At < RegistryLookupKeeps
                && (kept.Path == null || File.Exists(kept.Path)))
                return kept.Path;
            string? found = null;
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (string keyPath in UninstallKeys)
                {
                    found ??= RegisteredExePath(root, keyPath);
                }
            }
            _registryLookup = (DateTime.UtcNow, found);
            return found;
        }
    }

    private static string? RegisteredExePath(RegistryKey root, string keyPath)
    {
        try
        {
            using var uninstall = root.OpenSubKey(keyPath);
            if (uninstall == null) return null;
            foreach (string name in uninstall.GetSubKeyNames())
            {
                using var entry = uninstall.OpenSubKey(name);
                if (entry?.GetValue("DisplayName") is not string display
                    || !display.StartsWith(ProcessName, StringComparison.OrdinalIgnoreCase)) continue;
                string? exe = ExeFromDisplayIcon(entry.GetValue("DisplayIcon") as string)
                    ?? ExeFromInstallLocation(entry.GetValue("InstallLocation") as string);
                if (exe != null && File.Exists(exe)) return exe;
            }
        }
        catch (Exception ex) when (ex is SecurityException or IOException or UnauthorizedAccessException)
        {
            Log.Debug("Volanta", $"Could not read {root.Name}\\{keyPath}: {ex.Message}");
        }
        return null;
    }

    /// <summary>The exe named by an uninstall entry's DisplayIcon ("C:\...\Volanta.exe,0", with or without the
    /// icon index or quotes), when it is Volanta's exe; null otherwise.</summary>
    internal static string? ExeFromDisplayIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        string path = displayIcon.Trim();
        int comma = path.LastIndexOf(',');
        if (comma > 0 && path[(comma + 1)..].Trim().All(c => char.IsAsciiDigit(c) || c == '-')) path = path[..comma];
        path = path.Trim().Trim('"');
        return Path.GetFileName(path).Equals(ExeName, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    /// <summary>Volanta's exe inside an uninstall entry's InstallLocation, when there is one.</summary>
    internal static string? ExeFromInstallLocation(string? installLocation)
    {
        if (string.IsNullOrWhiteSpace(installLocation)) return null;
        return Path.Combine(installLocation.Trim().Trim('"'), ExeName);
    }

    /// <summary>Whether Volanta answers on the live map port: the port is up and the browser behind it says it
    /// is Volanta, so another program's debugging port on the same number is never mistaken for it.</summary>
    public static async Task<bool> HasLiveMapAccessAsync(CancellationToken ct = default)
        => await VolantaLink.IsVolantaOnPortAsync(ct).ConfigureAwait(false) == true;

    public static async Task<VolantaAccessState> DetectAsync(CancellationToken ct = default)
    {
        if (!IsRunning()) return FindExePath() == null ? VolantaAccessState.NotInstalled : VolantaAccessState.NotRunning;
        return await HasLiveMapAccessAsync(ct).ConfigureAwait(false)
            ? VolantaAccessState.RunningWithAccess
            : VolantaAccessState.RunningWithoutAccess;
    }

    /// <summary>Starts Volanta with live map access. Volanta must not be running. <paramref name="exe"/> is
    /// Volanta's exe, read with <see cref="FindExePath"/> before a restart's force-close; null looks it up now.
    /// Returns an error to show, or null on success (the link connects on its own once Volanta's page is up).</summary>
    public static string? StartWithAccess(string? exe = null)
    {
        exe ??= FindExePath();
        if (exe == null) return NotFoundMessage;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, LaunchArguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            });
            Log.Info("Volanta", $"Started Volanta with live map access: {exe}");
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Warn("Volanta", $"Could not start Volanta: {ex.Message}");
            return $"Volanta could not be started: {ex.Message}";
        }
    }

    /// <summary>
    /// The first step of a restart: ends Volanta's processes and its sim plugins, and waits for them to go.
    /// False when Volanta is still running after 10 s: Windows would not end it, and the pilot must quit it from
    /// its notification-area menu.
    /// </summary>
    public static async Task<bool> ForceCloseAsync(CancellationToken ct = default)
    {
        Log.Info("Volanta", "Force-closing Volanta for a restart with live map access");
        ForceClose();
        if (await WaitForExitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false)) return true;
        Log.Warn("Volanta", "Volanta is still running after the force-close");
        return false;
    }

    private static async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning()) return true;
            await Task.Delay(250, ct).ConfigureAwait(false);
        }
        return !IsRunning();
    }

    /// <summary>Ends every Volanta process and its sim plugins. Best-effort: one already gone, or one Windows
    /// will not let us end, is skipped.</summary>
    private static void ForceClose()
    {
        foreach (string name in PluginProcessNames.Prepend(ProcessName))
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    try { p.Kill(entireProcessTree: true); }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException or AggregateException)
                    {
                        Log.Debug("Volanta", $"Could not end {name} ({p.Id}): {ex.Message}");
                    }
                }
            }
        }
    }
}

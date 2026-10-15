using System.ComponentModel;
using System.Net.Http;
using System.Threading;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Volanta;

public enum VolantaAccessState
{
    /// <summary>Volanta.exe is not where its installer puts it, and no running copy says otherwise.</summary>
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
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>The switches that give live map access. The address keeps the port to this PC.</summary>
    public static string LaunchArguments =>
        $"--remote-debugging-port={VolantaLink.DebugPort} --remote-debugging-address=127.0.0.1";

    /// <summary>Where Volanta's installer puts it, for this Windows user.</summary>
    public static string DefaultExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Volanta", "Volanta.exe");

    public static bool IsRunning() => CountProcesses() > 0;

    private static int CountProcesses()
    {
        var all = Process.GetProcessesByName(ProcessName);
        foreach (var p in all) p.Dispose();
        return all.Length;
    }

    /// <summary>The running Volanta's own exe if one runs and Windows lets us read it, else the default path.</summary>
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
        return File.Exists(DefaultExePath) ? DefaultExePath : null;
    }

    /// <summary>Whether Volanta answers on the live map port: the port is up and the browser behind it says it
    /// is Volanta, so another program's debugging port on the same number is never mistaken for it.</summary>
    public static async Task<bool> HasLiveMapAccessAsync(CancellationToken ct = default)
    {
        try
        {
            string version = await Http.GetStringAsync($"http://127.0.0.1:{VolantaLink.DebugPort}/json/version", ct)
                .ConfigureAwait(false);
            return version.Contains("Volanta/", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    public static async Task<VolantaAccessState> DetectAsync(CancellationToken ct = default)
    {
        if (!IsRunning()) return FindExePath() == null ? VolantaAccessState.NotInstalled : VolantaAccessState.NotRunning;
        return await HasLiveMapAccessAsync(ct).ConfigureAwait(false)
            ? VolantaAccessState.RunningWithAccess
            : VolantaAccessState.RunningWithoutAccess;
    }

    /// <summary>Starts Volanta with live map access. Volanta must not be running. Returns an error to show, or
    /// null on success (the link connects on its own once Volanta's page is up).</summary>
    public static string? StartWithAccess()
    {
        string? exe = FindExePath();
        if (exe == null) return "Volanta is not installed for this Windows user.";
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

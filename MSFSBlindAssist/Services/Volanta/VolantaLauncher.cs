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

public enum VolantaQuitResult
{
    /// <summary>Volanta quit on its own: its own quit over the debugging port, or its window closing.</summary>
    Quit,
    /// <summary>Volanta did not quit when asked, so its processes and sim plugins were ended.</summary>
    ForceClosed,
    /// <summary>Volanta is still running: Windows refused to end it.</summary>
    StillRunning,
}

/// <summary>
/// Starts Volanta with live map access: a local-only Chromium debugging port (<see cref="VolantaLink.DebugPort"/>).
/// Volanta runs one copy at a time, so a running Volanta must be quit first; the switch on a second launch
/// would only bring the first one forward. With Volanta's "show in tray" setting on, its close button (and
/// Alt+F4) only hide it in the notification area (measured 2026-10-15). <see cref="QuitAsync"/> therefore
/// escalates: Volanta's own quit over the debugging port when it has access (the same as Quit in its
/// notification-area menu), else its window asked to close; and when Volanta is still running after that, its
/// processes and sim plugins are ended (the owner's choice, 2026-10-15: asking Volanta was not reliable). The
/// window always asks the pilot first, and says the force-close may happen.
/// </summary>
public static class VolantaLauncher
{
    private const string ProcessName = "Volanta";
    /// <summary>Volanta's sim plugins: its own quit stops them first, so a force-close ends them too, or a stale
    /// one stays connected to the sim beside the plugin the new Volanta starts.</summary>
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
    /// Quits Volanta and waits for it to exit. First the gentle way: with live map access, Volanta's own quit
    /// (the same as Quit in its notification-area menu); without, its window asked to close, which quits only a
    /// Volanta whose "show in tray" setting is off. When Volanta is still running after that (8 s after its own
    /// quit, 4 s after a window close), its processes and its sim plugins are ended, and the wait goes on.
    /// </summary>
    public static async Task<VolantaQuitResult> QuitAsync(CancellationToken ct = default)
    {
        bool asked = await VolantaLink.RunVolantaQuitAsync(ct).ConfigureAwait(false);
        if (!asked)
        {
            foreach (var p in Process.GetProcessesByName(ProcessName))
            {
                using (p)
                {
                    try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); }
                    catch (InvalidOperationException) { /* already exited */ }
                }
            }
        }
        if (await WaitForExitAsync(TimeSpan.FromSeconds(asked ? 8 : 4), ct).ConfigureAwait(false))
            return VolantaQuitResult.Quit;

        Log.Warn("Volanta", $"Volanta did not quit when asked ({(asked ? "its own quit" : "window close")}); force-closing it");
        ForceClose();
        if (await WaitForExitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false))
            return VolantaQuitResult.ForceClosed;
        Log.Warn("Volanta", "Volanta is still running after the force-close");
        return VolantaQuitResult.StillRunning;
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

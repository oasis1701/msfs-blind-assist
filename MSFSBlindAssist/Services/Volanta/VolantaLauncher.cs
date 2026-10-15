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
/// Volanta runs one copy at a time, so a running Volanta must be closed first; the switch on a second launch
/// would only bring the first one forward. Closing asks Volanta's window to close, as its close button does,
/// and never force-quits it: the pilot is told to close it themselves when it does not go.
/// </summary>
public static class VolantaLauncher
{
    private const string ProcessName = "Volanta";
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

    /// <summary>Asks every Volanta window to close and waits for Volanta to exit. False when it is still running
    /// at the deadline: it may be hidden in the notification area, where it has no window to close.</summary>
    public static async Task<bool> CloseAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        foreach (var p in Process.GetProcessesByName(ProcessName))
        {
            using (p)
            {
                try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); }
                catch (InvalidOperationException) { /* already exited */ }
            }
        }
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (!IsRunning()) return true;
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        bool closed = !IsRunning();
        if (!closed) Log.Warn("Volanta", "Volanta did not close when asked");
        return closed;
    }
}

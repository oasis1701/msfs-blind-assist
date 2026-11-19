using MSFSBlindAssist.Database;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils;
using MSFSBlindAssist.Utils.Logging;
using static System.FormattableString;
using AppLog = MSFSBlindAssist.Utils.Logging.Log;

namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>
/// The app's side of <see cref="ModuleSession"/> for one WASM module: the simulator version, the module's installed
/// files (such as PMDG's 737NG3.wasm), the read-only simulator handle, the module's log ("&lt;module id&gt;-module",
/// such as pmdg737-module) and the background threads. Everything stays read-only: the files are opened shared for
/// read, write and delete, and the simulator only through <see cref="SimProcess"/>. The log is safe from any thread.
/// </summary>
public sealed class ModuleHost : IModuleHost
{
    private readonly SimConnectManager _sim;
    private readonly ModuleDescription _module;
    private readonly LogChannel _channel;

    public ModuleHost(SimConnectManager sim, ModuleDescription module)
    {
        _sim = sim ?? throw new ArgumentNullException(nameof(sim));
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _channel = AppLog.Channel($"{module.Id}-module");
    }

    public long NowMs => Environment.TickCount64;

    public string SimulatorVersion => _sim.DetectedSimulatorVersion;

    public IReadOnlyList<string> FindInstalledFiles(string simulatorVersion, Action<string> log)
    {
        string? root = MsfsPackagesLocator.TryGetInstalledPackagesPath(simulatorVersion);
        if (root is null)
        {
            log($"file analysis: no packages folder found for {simulatorVersion}");
            return Array.Empty<string>();
        }
        var files = ModuleFiles.FindInstalled(root, _module);
        log(Invariant($"file analysis: {files.Count} {_module.WasmFileName} file(s) under {root}"));
        return files;
    }

    /// <summary>Reads a whole file without ever standing in the way of another program's write or delete.
    /// A file cut short while it is read throws EndOfStreamException, an IOException: "not recognised".</summary>
    public byte[] ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    public IProcessMemorySource? OpenSimulator(string simulatorVersion, out string? failure)
    {
        failure = null;
        string? processName = SimulatorDetector.GetProcessName(simulatorVersion);
        if (processName is null)
        {
            Log($"memory search: no simulator process name for {simulatorVersion}");
            return null;
        }
        var process = SimProcess.TryOpen(processName, out failure);
        if (process is null) Log($"memory search: could not open {processName}: {failure}");
        return process;
    }

    public void Log(string line) => _channel.Info(line);

    public Task<T> RunInBackground<T>(string name, Func<T> work) => OnBackgroundThread(name, work);

    /// <summary>
    /// Runs <paramref name="work"/> on a dedicated low-priority background thread. The task always
    /// completes: with the result, or faulted with whatever the work (or starting the thread) threw,
    /// which a monitor treats as an internal error.
    /// </summary>
    internal static Task<T> OnBackgroundThread<T>(string name, Func<T> work)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.TrySetResult(work());
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = name,
        };
        try
        {
            thread.Start();
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
        }
        return done.Task;
    }
}

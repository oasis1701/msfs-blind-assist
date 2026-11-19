using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.PMDG;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>The distinct builds of the installed files; <see cref="Failure"/> is one of <see cref="FmaReasons"/> when
/// <see cref="Builds"/> is empty.</summary>
public sealed record ModuleAnalysis(IReadOnlyList<ModuleBuild> Builds, string? Failure);

/// <summary>
/// A build whose anchor matched exactly one allocation of the simulator's memory. Every reader reads
/// through <see cref="Memory"/> while it holds a lease on the one simulator handle all located builds
/// share; the handle closes when the session and every reader have released theirs.
/// </summary>
public sealed class LocatedModule
{
    private readonly SharedHandle _handle;

    internal LocatedModule(ModuleBuild build, ModuleAnchor anchor, ModuleMemory memory, SharedHandle handle)
    {
        Build = build;
        Anchor = anchor;
        Memory = memory;
        _handle = handle;
    }

    public ModuleBuild Build { get; }

    /// <summary>The anchor that located the build.</summary>
    public ModuleAnchor Anchor { get; }

    public ModuleMemory Memory { get; }

    /// <summary>True while the memory still holds the anchor where it was found.</summary>
    internal bool IsStillThere() => Memory.Matches(Anchor.Address, Anchor.Bytes);

    /// <summary>A lease that keeps the simulator handle open until disposed (disposing twice counts once).
    /// Throws <see cref="ObjectDisposedException"/> once the handle has closed.</summary>
    public IDisposable AcquireLease() => _handle.Acquire();
}

/// <summary>
/// The result of one memory search: a non-empty <see cref="Found"/> means found; <see cref="FmaReasons.NoAccess"/>
/// is final; an empty list with a null failure means not found yet. The session holds one lease on the
/// handle while the location is current.
/// </summary>
public sealed record ModuleLocation(IReadOnlyList<LocatedModule> Found, string? Failure)
{
    internal IDisposable? SessionLease { get; init; }

    /// <summary>The session lets go of the handle; readers' leases keep it open until they are disposed.</summary>
    internal void ReleaseSessionLease() => SessionLease?.Dispose();
}

/// <summary>The app's side of the session, so it runs against fakes: the clock, the simulator version,
/// the installed files, the read-only simulator handle, the log and the background threads.</summary>
public interface IModuleHost
{
    long NowMs { get; }

    /// <summary>Read on the thread that asks the session for work (the UI thread).</summary>
    string SimulatorVersion { get; }

    IReadOnlyList<string> FindInstalledFiles(string simulatorVersion, Action<string> log);
    byte[] ReadFile(string path);

    /// <summary>Null when the simulator cannot be opened; <paramref name="failure"/> is SimProcess's
    /// wording, or null when there is no process to open yet.</summary>
    IProcessMemorySource? OpenSimulator(string simulatorVersion, out string? failure);

    void Log(string line);

    /// <summary>Runs <paramref name="work"/> off the calling thread; the task always completes.</summary>
    Task<T> RunInBackground<T>(string name, Func<T> work);
}

/// <summary>
/// The work every reader of a WASM module's memory shares: each installed file's layouts are got once from the layout
/// source (WASM-11), and the module's memory is searched once, by any reader's anchor, on one read-only simulator
/// handle. Readers ask for both and turn the results into their own samplers; one reader without a layout never stops
/// another reader. <see cref="AnalyseAsync"/> starts once and returns the same task afterwards. <see cref="LocateAsync"/>
/// joins a search in flight, returns a search again that started less than <see cref="ReuseMs"/> ago and
/// found memory that still holds its anchors, and otherwise searches again: a reader retrying every 5 s
/// from its own attempt (the FMA) always gets a fresh search. <see cref="Invalidate"/> (a flight reload
/// or reconnect) forgets the location and a search in flight, and an analysis that failed.
/// Safe from any thread: all state is under one lock.
/// </summary>
public sealed class ModuleSession : IDisposable
{
    internal const long ReuseMs = 5_000;

    /// <summary><see cref="SimProcess.TryOpen"/>'s own wording when the process exists but cannot be opened.</summary>
    private const string SimProcessNoAccess = "no access to the simulator";

    private readonly IModuleHost _host;
    private readonly ILayoutSource _layouts;
    private readonly string _jobName; // the background threads' name prefix, such as "PMDG737"
    private readonly object _gate = new();
    private Task<ModuleAnalysis>? _analysis;
    private string? _analysedSimulator; // the simulator version _analysis found its files for
    private Task<ModuleLocation>? _search; // the current search, in flight or completed; its location holds the session's lease
    private long _searchStartMs;
    private bool _disposed;

    public ModuleSession(ModuleDescription module, IModuleHost host, ILayoutSource layouts)
    {
        ArgumentNullException.ThrowIfNull(module);
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _layouts = layouts ?? throw new ArgumentNullException(nameof(layouts));
        _jobName = module.Id.ToUpperInvariant();
    }

    /// <summary>The installed files' builds. Faulted when an unexpected exception escaped (an internal error).</summary>
    public Task<ModuleAnalysis> AnalyseAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return AnalysisLocked();
        }
    }

    /// <summary>The located builds. Waits for the analysis first (on the background thread), and faults
    /// when it faulted.</summary>
    public Task<ModuleLocation> LocateAsync()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_search is { IsCompleted: false }) return _search;
            long now = _host.NowMs;
            // An empty location has nothing to share, and memory that lost its anchor has moved.
            if (_search is { IsCompletedSuccessfully: true } done && now - _searchStartMs < ReuseMs
                && done.Result.Found.Count > 0 && done.Result.Found.All(m => m.IsStillThere()))
                return done;

            DropLocationLocked();
            var analysis = AnalysisLocked();
            string simulator = _host.SimulatorVersion; // read here, on the caller's thread
            var host = _host;
            _searchStartMs = now;
            var search = host.RunInBackground($"{_jobName} memory search", () => Search(host, analysis, simulator));
            _search = search;
            search.ContinueWith(OnSearchDone, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return search;
        }
    }

    /// <summary>A flight reload or reconnect: the next <see cref="LocateAsync"/> searches afresh (a search in
    /// flight is left to finish and close its handle), and an analysis that failed runs again. Loaded
    /// builds are kept.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            if (_disposed) return;
            DropLocationLocked();
            if (_analysis is { IsCompleted: true } done && (!done.IsCompletedSuccessfully || done.Result.Builds.Count == 0))
                _analysis = null;
        }
    }

    /// <summary>True when the installed files were analysed for a simulator other than <paramref name="simulator"/> (MSFS
    /// 2020 and 2024 each install their own copy of the module, which can be another build): the readers keep layouts
    /// derived from the old files, so they start again on a new session. An empty or unknown version never counts.</summary>
    public bool AnalysedForOtherSimulator(string? simulator)
    {
        if (string.IsNullOrEmpty(simulator) || simulator == "Unknown") return false;
        lock (_gate)
        {
            return !_disposed && _analysis is not null && _analysedSimulator != simulator;
        }
    }

    /// <summary>Releases the session's lease; readers' leases keep the handle open until they are disposed.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            DropLocationLocked();
            _analysis = null;
        }
    }

    private Task<ModuleAnalysis> AnalysisLocked()
    {
        if (_analysis is not null) return _analysis;
        string simulator = _host.SimulatorVersion; // read here, on the caller's thread
        _analysedSimulator = simulator;
        var host = _host;
        var layouts = _layouts;
        return _analysis = host.RunInBackground($"{_jobName} file analysis",
            () => Analyse(host.FindInstalledFiles(simulator, host.Log), layouts, host.Log));
    }

    /// <summary>Forgets the current search, releasing its location's session lease if it has completed (a
    /// search still in flight releases its own in <see cref="OnSearchDone"/>). Releasing twice counts once.
    /// The last lease closes the read-only handle here, under the lock: CloseHandle is quick and never throws.</summary>
    private void DropLocationLocked()
    {
        var old = _search;
        _search = null;
        if (old is { IsCompletedSuccessfully: true }) old.Result.ReleaseSessionLease();
    }

    /// <summary>A search that is no longer current when it completes (invalidated, replaced or disposed
    /// meanwhile) releases its own session lease, since nothing else will.</summary>
    private void OnSearchDone(Task<ModuleLocation> search)
    {
        if (!search.IsCompletedSuccessfully) return;
        lock (_gate)
        {
            if (ReferenceEquals(search, _search) && !_disposed) return;
        }
        search.Result.ReleaseSessionLease();
    }

    private static ModuleLocation Search(IModuleHost host, Task<ModuleAnalysis> analysisTask, string simulator)
    {
        var analysis = analysisTask.GetAwaiter().GetResult(); // a failed analysis faults the search too
        if (analysis.Builds.Count == 0) return new ModuleLocation(Array.Empty<LocatedModule>(), analysis.Failure);
        var source = host.OpenSimulator(simulator, out string? failure);
        if (source is null) return NotOpened(failure);
        return Locate(source, analysis.Builds, host.Log);
    }

    /// <summary>The result when the simulator could not be opened: no access is final, anything else
    /// (not running yet) is "not found yet".</summary>
    internal static ModuleLocation NotOpened(string? failure) =>
        new(Array.Empty<LocatedModule>(), failure == SimProcessNoAccess ? FmaReasons.NoAccess : null);

    /// <summary>
    /// Gets each file's build from <paramref name="layouts"/>; builds are kept once each (the same build installed
    /// twice, one layout file, gives one). Every file is logged: its layouts in full with each part that was not
    /// recognised, "same layout as" a file already logged, or the reason each reader, or the whole file, was not
    /// recognised. A reader without a layout costs only its own reader (WASM-11).
    /// </summary>
    internal static ModuleAnalysis Analyse(IReadOnlyList<string> files, ILayoutSource layouts, Action<string> log)
    {
        if (files.Count == 0)
        {
            log($"file analysis: {FmaReasons.FilesNotFound}");
            return new ModuleAnalysis(Array.Empty<ModuleBuild>(), FmaReasons.FilesNotFound);
        }
        var builds = new List<ModuleBuild>();
        foreach (string file in files)
        {
            var lookup = layouts.Find(file);
            if (lookup.Build is not { } build)
            {
                log($"file analysis: {file}: not recognised: {lookup.Failure}");
                continue;
            }
            if (build.Anchors.Count == 0)
            {
                foreach (var reader in build.Module.Readers)
                    log($"file analysis: {file}: {reader.LogName} not recognised: {build.Failure(reader.Id)}");
                continue;
            }
            var same = builds.Find(b => b.SameLayoutsAs(build));
            if (same is not null)
            {
                log($"file analysis: {file}: same layout as {same.SourceFile}");
                continue;
            }
            builds.Add(build);
            foreach (var reader in build.Module.Readers)
            {
                if (build.Layout<object>(reader.Id) is { } layout)
                {
                    foreach (string line in reader.Describe(layout)) log(line);
                    foreach (var (part, reason) in reader.PartFailures(layout))
                        log($"file analysis: {file}: {reader.LogName} {part} not recognised: {reason}");
                }
                else
                {
                    log($"file analysis: {file}: {reader.LogName} not recognised: {build.Failure(reader.Id)}");
                }
            }
        }
        if (builds.Count == 0) return new ModuleAnalysis(Array.Empty<ModuleBuild>(), FmaReasons.FileNotRecognised);
        return new ModuleAnalysis(builds, null);
    }

    /// <summary>
    /// Searches <paramref name="source"/> once per distinct anchor, each build's anchors in order; every
    /// build whose anchor matched exactly one allocation is found. Takes ownership of
    /// <paramref name="source"/>: the found builds share it through leases, the first of them the
    /// session's (<see cref="ModuleLocation.ReleaseSessionLease"/>); with none found it is disposed
    /// before returning.
    /// </summary>
    internal static ModuleLocation Locate(IProcessMemorySource source, IReadOnlyList<ModuleBuild> builds, Action<string> log)
    {
        bool handedOver = false;
        try
        {
            var bases = new Dictionary<ModuleAnchor, ulong?>();
            var found = new List<(ModuleBuild Build, ModuleAnchor Anchor, ulong Base)>();
            foreach (var build in builds)
            {
                foreach (var anchor in build.Anchors)
                {
                    if (!bases.TryGetValue(anchor, out ulong? linearBase))
                    {
                        linearBase = WasmMemoryLocator.FindLinearBase(source, anchor.Address, anchor.Bytes);
                        bases[anchor] = linearBase;
                        log(linearBase is { } b
                            ? Invariant($"memory search: the anchor at 0x{anchor.Address:x} found in the allocation at 0x{b:x}")
                            : Invariant($"memory search: the anchor at 0x{anchor.Address:x} not found in exactly one allocation"));
                    }
                    if (linearBase is { } at)
                    {
                        found.Add((build, anchor, at));
                        break;
                    }
                }
            }
            if (found.Count == 0) return new ModuleLocation(Array.Empty<LocatedModule>(), null);

            var handle = SharedHandle.Open(source, out IDisposable sessionLease);
            var modules = found.Select(f => new LocatedModule(f.Build, f.Anchor, new ModuleMemory(source, f.Base), handle)).ToArray();
            handedOver = true;
            log(Invariant($"memory search: {modules.Length} build(s) located"));
            return new ModuleLocation(modules, null) { SessionLease = sessionLease };
        }
        finally
        {
            if (!handedOver) source.Dispose();
        }
    }
}

/// <summary>
/// One simulator handle shared by the session and every reader: each holds its own lease, and the source
/// is disposed exactly once, when the last lease is (from any thread: a monitor disposes an abandoned
/// search's samplers on a pool thread). Disposing a lease twice counts once; no lease can be taken once
/// the count reached zero.
/// </summary>
internal sealed class SharedHandle
{
    private readonly IDisposable _source;
    private int _count;

    private SharedHandle(IDisposable source)
    {
        _source = source;
        _count = 1;
    }

    /// <summary>A handle whose first lease, <paramref name="firstLease"/>, is already counted.</summary>
    public static SharedHandle Open(IDisposable source, out IDisposable firstLease)
    {
        var handle = new SharedHandle(source);
        firstLease = new Lease(handle);
        return handle;
    }

    public IDisposable Acquire()
    {
        while (true)
        {
            int count = Volatile.Read(ref _count);
            if (count == 0) throw new ObjectDisposedException(nameof(LocatedModule), "The simulator handle is closed.");
            if (Interlocked.CompareExchange(ref _count, count + 1, count) == count) return new Lease(this);
        }
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _count) == 0) _source.Dispose();
    }

    private sealed class Lease : IDisposable
    {
        private readonly SharedHandle _owner;
        private int _released;

        public Lease(SharedHandle owner) => _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) _owner.Release();
        }
    }
}

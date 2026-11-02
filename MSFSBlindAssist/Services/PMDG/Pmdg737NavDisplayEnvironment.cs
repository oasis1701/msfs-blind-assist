using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The app's side of <see cref="Pmdg737NavDisplayMonitor"/>: the PMDG SDK's Captain EFIS mode and range, the
/// one Ctrl+M row, speech and the pmdg737-nd log. The file analysis and the memory search belong to the shared
/// <see cref="ModuleSession"/> (WASM-11); this class turns its results into the ND reader's layouts and
/// samplers. The monitor calls every member on the UI thread except <see cref="Log"/>, which an abandoned
/// job's clean-up may call from a pool thread (the log is safe from any thread).
/// </summary>
public sealed class Pmdg737NavDisplayEnvironment : INavDisplayEnvironment
{
    private const string LogChannelName = "pmdg737-nd";

    // Qualified: inside this class, Log is the INavDisplayEnvironment method.
    private static readonly LogChannel Channel = global::MSFSBlindAssist.Utils.Logging.Log.Channel(LogChannelName);

    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _aircraftCode;
    private readonly ModuleSession _session;

    public Pmdg737NavDisplayEnvironment(SimConnectManager sim, ScreenReaderAnnouncer announcer, string aircraftCode, ModuleSession session,
        Pmdg737DisplayFilesNotice filesNotice)
    {
        _sim = sim ?? throw new ArgumentNullException(nameof(sim));
        _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));
        _aircraftCode = aircraftCode ?? throw new ArgumentNullException(nameof(aircraftCode));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        FilesNotice = filesNotice ?? throw new ArgumentNullException(nameof(filesNotice));
    }

    /// <summary>The session's notice, shared with the other three readers' environments (WASM-29).</summary>
    public Pmdg737DisplayFilesNotice FilesNotice { get; }

    public long NowMs => Environment.TickCount64;

    /// <summary>The data manager is replaced on every connect and aircraft switch, so it is read afresh here
    /// every time, and the connection is checked first (WASM-7): a disconnect leaves the old manager ready
    /// with its last snapshot.</summary>
    public NavDisplaySdkSnapshot? ReadSdk()
    {
        bool connected = _sim.IsConnected;
        var dm = connected ? _sim.PMDGDataManager as PMDGNG3DataManager : null;
        return Snapshot(connected, dm is { IsReady: true }, name => dm!.GetFieldValue(name));
    }

    /// <summary>The SDK's Captain EFIS mode and range selector positions, or null while disconnected or not ready.</summary>
    internal static NavDisplaySdkSnapshot? Snapshot(bool connected, bool ready, Func<string, double> field)
    {
        if (!connected || !ready) return null;
        return new NavDisplaySdkSnapshot(ModeSel: (int)field("EFIS_ModeSel_0"), RangeSel: (int)field("EFIS_RangeSel_0"));
    }

    public bool IsMuted() => DefAnnounceMuteSets.IsMuted(_aircraftCode, Pmdg737NavDisplayMonitor.MuteKey, SettingsManager.Current);

    /// <summary>The non-interrupting announce: nothing the reader says by itself interrupts.</summary>
    public void Speak(string text) => _announcer.Announce(text);

    public void Log(string line) => Channel.Info(line);

    /// <summary>The session's shared analysis, as the ND layouts. A fault passes through unwrapped.</summary>
    public Task<NavDisplayAnalysis> AnalyseAsync() =>
        _session.AnalyseAsync().ContinueWith(t => ToNavAnalysis(t.GetAwaiter().GetResult()), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    /// <summary>The session's shared memory search, as one sampler per requested layout found, each on its own
    /// lease of the shared simulator handle. A fault passes through unwrapped.</summary>
    public Task<NavDisplayLocateResult> LocateAsync(IReadOnlyList<Pmdg737NavDisplayLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        var snapshot = layouts.ToArray();
        return _session.LocateAsync().ContinueWith(t => ToNavCandidates(t.GetAwaiter().GetResult(), snapshot), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>The distinct ND layouts of the analysed builds, in file order. A build whose ND layout is a failure
    /// is skipped (the FMA and engine displays may still use it); none left is "file not recognised", and the
    /// analysis's own failure (files not found) passes through. <see cref="NavDisplayAnalysis.LayoutFileLoaded"/>
    /// says whether the module has a build at all (WASM-29).</summary>
    internal static NavDisplayAnalysis ToNavAnalysis(ModuleAnalysis analysis)
    {
        bool loaded = analysis.Builds.Count > 0;
        var layouts = new List<Pmdg737NavDisplayLayout>();
        foreach (var build in analysis.Builds)
            if (build.Layout<Pmdg737NavDisplayLayout>(Pmdg737Module.NavDisplay) is { } layout && !layouts.Contains(layout)) layouts.Add(layout);
        if (layouts.Count > 0) return new NavDisplayAnalysis(layouts, null) { LayoutFileLoaded = loaded };
        string failure = !loaded && analysis.Failure is not null ? analysis.Failure : FmaReasons.FileNotRecognised;
        return new NavDisplayAnalysis(Array.Empty<Pmdg737NavDisplayLayout>(), failure) { LayoutFileLoaded = loaded };
    }

    /// <summary>One sampler for each requested layout whose build was located, each holding its own lease on the
    /// shared handle. A handle closed meanwhile (a reload or switch racing the search) is "not found yet", and
    /// the location's failure (no access) passes through.</summary>
    internal static NavDisplayLocateResult ToNavCandidates(ModuleLocation location, IReadOnlyList<Pmdg737NavDisplayLayout> layouts)
    {
        var samplers = new List<INavDisplaySampler>();
        foreach (var layout in layouts)
        {
            var module = location.Found.FirstOrDefault(m => layout.Equals(m.Build.Layout<Pmdg737NavDisplayLayout>(Pmdg737Module.NavDisplay)));
            if (module is null) continue;
            IDisposable lease;
            try
            {
                lease = module.AcquireLease();
            }
            catch (ObjectDisposedException)
            {
                continue;
            }
            samplers.Add(new ModuleNavDisplaySampler(module.Memory, lease, layout));
        }
        return new NavDisplayLocateResult(samplers, samplers.Count == 0 ? location.Failure : null);
    }
}

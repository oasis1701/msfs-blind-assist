using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The app's side of <see cref="Pmdg737EngineDisplayMonitor"/>: the PMDG SDK's tanks, the three Ctrl+M rows,
/// speech and the pmdg737-engine-displays log. The file analysis and the memory search belong to the shared
/// <see cref="ModuleSession"/> (WASM-11); this class turns its results into the engine display
/// reader's layouts and samplers. The monitor calls every member on the UI thread except <see cref="Log"/>,
/// which an abandoned job's clean-up may call from a pool thread (the log is safe from any thread).
/// </summary>
public sealed class Pmdg737EngineDisplayEnvironment : IEngineDisplayEnvironment
{
    private const string LogChannelName = "pmdg737-engine-displays";

    // Qualified: inside this class, Log is the IEngineDisplayEnvironment method.
    private static readonly LogChannel Channel = global::MSFSBlindAssist.Utils.Logging.Log.Channel(LogChannelName);

    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _aircraftCode;
    private readonly ModuleSession _session;

    public Pmdg737EngineDisplayEnvironment(SimConnectManager sim, ScreenReaderAnnouncer announcer, string aircraftCode, ModuleSession session,
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
    public EngineDisplaySdkSnapshot? ReadSdk()
    {
        bool connected = _sim.IsConnected;
        var dm = connected ? _sim.PMDGDataManager as PMDGNG3DataManager : null;
        return Snapshot(connected, dm is { IsReady: true }, name => dm!.GetFieldValue(name));
    }

    /// <summary>The SDK's tanks (pounds; left is tank 1, right tank 2), or null while disconnected or not ready.</summary>
    internal static EngineDisplaySdkSnapshot? Snapshot(bool connected, bool ready, Func<string, double> field)
    {
        if (!connected || !ready) return null;
        return new EngineDisplaySdkSnapshot(
            FuelLeftLb: field("FUEL_QtyLeft"),
            FuelCenterLb: field("FUEL_QtyCenter"),
            FuelRightLb: field("FUEL_QtyRight"));
    }

    public bool IsMuted(EngineDisplayMuteRow row) => DefAnnounceMuteSets.IsMuted(_aircraftCode, row switch
    {
        EngineDisplayMuteRow.Colors => Pmdg737EngineDisplayMonitor.MuteKeyColors,
        EngineDisplayMuteRow.ThrustMode => Pmdg737EngineDisplayMonitor.MuteKeyThrustMode,
        _ => Pmdg737EngineDisplayMonitor.MuteKeyAlerts,
    }, SettingsManager.Current);

    /// <summary>The non-interrupting announce: nothing the reader says by itself interrupts (WASM-9).</summary>
    public void Speak(string text) => _announcer.Announce(text);

    public void Log(string line) => Channel.Info(line);

    /// <summary>The session's shared analysis, as the engine display layouts. A fault passes through unwrapped.</summary>
    public Task<EngineDisplayAnalysis> AnalyseAsync() =>
        _session.AnalyseAsync().ContinueWith(t => ToEngineAnalysis(t.GetAwaiter().GetResult()), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    /// <summary>The session's shared memory search, as one sampler per requested layout found, each on its own
    /// lease of the shared simulator handle. A fault passes through unwrapped.</summary>
    public Task<EngineDisplayLocateResult> LocateAsync(IReadOnlyList<Pmdg737EngineDisplayLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        var snapshot = layouts.ToArray();
        return _session.LocateAsync().ContinueWith(t => ToEngineCandidates(t.GetAwaiter().GetResult(), snapshot), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>The distinct engine display layouts of the analysed builds, in file order. A build whose engine
    /// display layout is a failure is skipped (the FMA may still use it); none left is "file not recognised", and the
    /// analysis's own failure (files not found) passes through. <see cref="EngineDisplayAnalysis.LayoutFileLoaded"/>
    /// says whether the module has a build at all (WASM-29).</summary>
    internal static EngineDisplayAnalysis ToEngineAnalysis(ModuleAnalysis analysis)
    {
        bool loaded = analysis.Builds.Count > 0;
        var layouts = new List<Pmdg737EngineDisplayLayout>();
        foreach (var build in analysis.Builds)
            if (build.Layout<Pmdg737EngineDisplayLayout>(Pmdg737Module.EngineDisplays) is { } layout && !layouts.Contains(layout)) layouts.Add(layout);
        if (layouts.Count > 0) return new EngineDisplayAnalysis(layouts, null) { LayoutFileLoaded = loaded };
        string failure = !loaded && analysis.Failure is not null ? analysis.Failure : FmaReasons.FileNotRecognised;
        return new EngineDisplayAnalysis(Array.Empty<Pmdg737EngineDisplayLayout>(), failure) { LayoutFileLoaded = loaded };
    }

    /// <summary>One sampler for each requested layout whose build was located, each holding its own lease on the
    /// shared handle. A handle closed meanwhile (a reload or switch racing the search) is "not found yet", and
    /// the location's failure (no access) passes through.</summary>
    internal static EngineDisplayLocateResult ToEngineCandidates(ModuleLocation location, IReadOnlyList<Pmdg737EngineDisplayLayout> layouts)
    {
        var samplers = new List<IEngineDisplaySampler>();
        foreach (var layout in layouts)
        {
            var module = location.Found.FirstOrDefault(m => layout.Equals(m.Build.Layout<Pmdg737EngineDisplayLayout>(Pmdg737Module.EngineDisplays)));
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
            samplers.Add(new ModuleEngineDisplaySampler(module.Memory, lease, layout));
        }
        return new EngineDisplayLocateResult(samplers, samplers.Count == 0 ? location.Failure : null);
    }
}

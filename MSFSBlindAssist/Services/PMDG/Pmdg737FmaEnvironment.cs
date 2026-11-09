using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The app's side of <see cref="Pmdg737FmaMonitor"/>: the PMDG SDK, the Ctrl+M row, speech and the
/// pmdg737-fma log. The two slow jobs (loading the layouts of PMDG's installed 737NG3.wasm files and
/// searching the simulator's memory) belong to the shared <see cref="ModuleSession"/>, which every reader of
/// PMDG's module memory uses; this class turns its results into the FMA's layouts and samplers.
/// The monitor calls every member on the UI thread except <see cref="Log"/>, which an abandoned job's
/// clean-up may call from a pool thread (the log is safe from any thread).
/// </summary>
public sealed class Pmdg737FmaEnvironment : IFmaEnvironment
{
    private const string LogChannelName = "pmdg737-fma";

    // Qualified: inside this class, Log is the IFmaEnvironment method.
    private static readonly LogChannel Channel = global::MSFSBlindAssist.Utils.Logging.Log.Channel(LogChannelName);

    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _aircraftCode;
    private readonly ModuleSession _session;

    public Pmdg737FmaEnvironment(SimConnectManager sim, ScreenReaderAnnouncer announcer, string aircraftCode, ModuleSession session,
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

    /// <summary>
    /// The data manager is replaced on every connect and aircraft switch, so it is read afresh here every
    /// time. The connection is checked first: a disconnect leaves the old manager in place, still ready,
    /// with its last snapshot, so without the check a closed sim would send the monitor searching for two
    /// minutes and then say "unavailable". Disconnected, the monitor waits silently for the next connection.
    /// </summary>
    public FmaSdkSnapshot? ReadSdk()
    {
        if (!_sim.IsConnected) return null;
        if (_sim.PMDGDataManager is not PMDGNG3DataManager { IsReady: true } dm) return null;
        return new FmaSdkSnapshot(
            FdCaptain: dm.GetFieldValue("MCP_FDSw_0") > 0.5,
            FdFirstOfficer: dm.GetFieldValue("MCP_FDSw_1") > 0.5,
            McpAltitude: (int)Math.Round(dm.GetFieldValue("MCP_Altitude")),
            McpHeading: (int)Math.Round(dm.GetFieldValue("MCP_Heading")));
    }

    public bool IsMuted => DefAnnounceMuteSets.IsMuted(_aircraftCode, Pmdg737FmaMonitor.MuteKey, SettingsManager.Current);

    public void Speak(string text) => _announcer.Announce(text);

    public void Log(string line) => Channel.Info(line);

    /// <summary>The session's shared analysis, as the FMA's layouts. A fault passes through unwrapped.</summary>
    public Task<FmaAnalysis> AnalyseAsync() =>
        _session.AnalyseAsync().ContinueWith(t => ToFmaAnalysis(t.GetAwaiter().GetResult()), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    /// <summary>The session's shared memory search, as one sampler per requested layout found, each on its
    /// own lease of the shared simulator handle. A fault passes through unwrapped.</summary>
    public Task<FmaLocateResult> LocateAsync(IReadOnlyList<Pmdg737FmaLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        var snapshot = layouts.ToArray();
        return _session.LocateAsync().ContinueWith(t => ToFmaCandidates(t.GetAwaiter().GetResult(), snapshot), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>The distinct FMA layouts of the analysed builds, in file order. A build whose FMA layout
    /// is a failure is skipped (another reader's layout found it); none left is "file not recognised", and the
    /// analysis's own failure (files not found) passes through. <see cref="FmaAnalysis.LayoutFileLoaded"/>
    /// says whether the module has a build at all (WASM-29).</summary>
    internal static FmaAnalysis ToFmaAnalysis(ModuleAnalysis analysis)
    {
        bool loaded = analysis.Builds.Count > 0;
        var layouts = new List<Pmdg737FmaLayout>();
        foreach (var build in analysis.Builds)
            if (build.Layout<Pmdg737FmaLayout>(Pmdg737Module.Fma) is { } layout && !layouts.Contains(layout)) layouts.Add(layout);
        if (layouts.Count > 0) return new FmaAnalysis(layouts, null) { LayoutFileLoaded = loaded };
        string failure = !loaded && analysis.Failure is not null ? analysis.Failure : FmaReasons.FileNotRecognised;
        return new FmaAnalysis(Array.Empty<Pmdg737FmaLayout>(), failure) { LayoutFileLoaded = loaded };
    }

    /// <summary>
    /// One sampler for each requested layout whose build was located, each holding its own lease on the
    /// shared handle. A handle closed meanwhile (a reload or switch racing the search) is "not found yet",
    /// and the location's failure (no access) passes through.
    /// </summary>
    internal static FmaLocateResult ToFmaCandidates(ModuleLocation location, IReadOnlyList<Pmdg737FmaLayout> layouts)
    {
        var samplers = new List<IFmaSampler>();
        foreach (var layout in layouts)
        {
            var module = location.Found.FirstOrDefault(m => layout.Equals(m.Build.Layout<Pmdg737FmaLayout>(Pmdg737Module.Fma)));
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
            samplers.Add(new ModuleFmaSampler(module.Memory, lease, layout));
        }
        return new FmaLocateResult(samplers, samplers.Count == 0 ? location.Failure : null);
    }
}

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The app's side of <see cref="Pmdg737PfdMonitor"/>: the simulator's indicated airspeed and altitude for the check
/// (from the app's cache: the 737's <see cref="PMDG737Definition.PfdCheckIasKey"/> and the base's INDICATED_ALTITUDE),
/// the one Ctrl+M row, speech and the pmdg737-pfd log. The file analysis and the memory search belong to the shared
/// <see cref="ModuleSession"/> (WASM-11); this class turns its results into the PFD reader's layouts and
/// samplers. The monitor calls every member on the UI thread except <see cref="Log"/>, which an abandoned job's
/// clean-up may call from a pool thread (the log is safe from any thread).
/// </summary>
public sealed class Pmdg737PfdEnvironment : IPfdEnvironment
{
    private const string LogChannelName = "pmdg737-pfd";

    // Qualified: inside this class, Log is the IPfdEnvironment method.
    private static readonly LogChannel Channel = global::MSFSBlindAssist.Utils.Logging.Log.Channel(LogChannelName);

    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly string _aircraftCode;
    private readonly ModuleSession _session;

    public Pmdg737PfdEnvironment(SimConnectManager sim, ScreenReaderAnnouncer announcer, string aircraftCode, ModuleSession session,
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

    /// <summary>The connection is checked first (WASM-7): the cache keeps the last values after a disconnect.</summary>
    public PfdSimSnapshot? ReadSimulator() => Snapshot(_sim.IsConnected, _sim.GetCachedVariableValue);

    /// <summary>The simulator's airspeed and altitude from the cache, each null until delivered (never 0, [DA40S-1]),
    /// or null while disconnected.</summary>
    internal static PfdSimSnapshot? Snapshot(bool connected, Func<string, double?> cached) =>
        connected ? new PfdSimSnapshot(cached(PMDG737Definition.PfdCheckIasKey), cached("INDICATED_ALTITUDE")) : null;

    public bool IsMuted() => DefAnnounceMuteSets.IsMuted(_aircraftCode, Pmdg737PfdMonitor.MuteKey, SettingsManager.Current);

    /// <summary>The non-interrupting announce: nothing the reader says by itself interrupts.</summary>
    public void Speak(string text) => _announcer.Announce(text);

    public void Log(string line) => Channel.Info(line);

    /// <summary>The session's shared analysis, as the PFD layouts. A fault passes through unwrapped.</summary>
    public Task<PfdAnalysis> AnalyseAsync() =>
        _session.AnalyseAsync().ContinueWith(t => ToPfdAnalysis(t.GetAwaiter().GetResult()), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    /// <summary>The session's shared memory search, as one sampler per requested layout found, each on its own lease of
    /// the shared simulator handle. A fault passes through unwrapped.</summary>
    public Task<PfdLocateResult> LocateAsync(IReadOnlyList<Pmdg737PfdLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        var snapshot = layouts.ToArray();
        return _session.LocateAsync().ContinueWith(t => ToPfdCandidates(t.GetAwaiter().GetResult(), snapshot), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>The distinct PFD layouts of the analysed builds, in file order. A build whose PFD layout is a failure is
    /// skipped (the other readers may still use it); none left is "file not recognised", and the analysis's own failure
    /// (files not found) passes through. <see cref="PfdAnalysis.LayoutFileLoaded"/> says whether the module has a build
    /// at all (WASM-29).</summary>
    internal static PfdAnalysis ToPfdAnalysis(ModuleAnalysis analysis)
    {
        bool loaded = analysis.Builds.Count > 0;
        var layouts = new List<Pmdg737PfdLayout>();
        foreach (var build in analysis.Builds)
            if (build.Layout<Pmdg737PfdLayout>(Pmdg737Module.Pfd) is { } layout && !layouts.Contains(layout)) layouts.Add(layout);
        if (layouts.Count > 0) return new PfdAnalysis(layouts, null) { LayoutFileLoaded = loaded };
        string failure = !loaded && analysis.Failure is not null ? analysis.Failure : FmaReasons.FileNotRecognised;
        return new PfdAnalysis(Array.Empty<Pmdg737PfdLayout>(), failure) { LayoutFileLoaded = loaded };
    }

    /// <summary>One sampler for each requested layout whose build was located, each holding its own lease on the shared
    /// handle. A handle closed meanwhile (a reload or switch racing the search) is "not found yet", and the location's
    /// failure (no access) passes through.</summary>
    internal static PfdLocateResult ToPfdCandidates(ModuleLocation location, IReadOnlyList<Pmdg737PfdLayout> layouts)
    {
        var samplers = new List<IPfdSampler>();
        foreach (var layout in layouts)
        {
            var module = location.Found.FirstOrDefault(m => layout.Equals(m.Build.Layout<Pmdg737PfdLayout>(Pmdg737Module.Pfd)));
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
            samplers.Add(new ModulePfdSampler(module.Memory, lease, layout));
        }
        return new PfdLocateResult(samplers, samplers.Count == 0 ? location.Failure : null);
    }
}

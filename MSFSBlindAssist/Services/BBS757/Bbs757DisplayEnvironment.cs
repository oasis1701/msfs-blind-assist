using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Services.WasmMemory;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using AppLog = MSFSBlindAssist.Utils.Logging.Log;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The app's side of every 757 display monitor (<see cref="Bbs757DisplayMonitor{TLayout, TReading}"/>), one per module
/// session, shared by every reader on it: the readers' simulator inputs from SimConnect's batch cache, the 757's Ctrl+M rows,
/// the non-interrupting announce (S5), the readers' named logs, and the session's one file analysis and memory search
/// (WASM-11) with its one "display files could not be loaded" notice (S14). The monitor calls every member on the UI
/// thread except <see cref="Log"/>, which an abandoned job's clean-up may call from a pool thread (the log is safe from
/// any thread).
/// </summary>
public sealed class Bbs757DisplayEnvironment : IBbs757DisplayEnvironment
{
    /// <summary>In every BlueBird 757 title (all ten of the package's, plan 23 Task 1 Step 6), matched in any case.</summary>
    public const string TitleMark = "757";

    private readonly Func<bool> _connected;
    private readonly Func<bool> _flightRunning;
    private readonly Func<string?> _title;
    private readonly Func<string, double?> _cached;
    private readonly IReadOnlyDictionary<string, string> _cacheKeys;
    private readonly Func<UserSettings> _settings;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly ModuleSession _session;

    /// <param name="cacheKeys">Each reader input's <see cref="Bbs757SimInput.Key"/>, to the key of the variable whose
    /// cached value it is (<c>BBS757Definition.DisplayCacheKeys</c>).</param>
    public Bbs757DisplayEnvironment(SimConnectManager sim, ScreenReaderAnnouncer announcer, ModuleSession session,
        Bbs757DisplayFilesNotice filesNotice, IReadOnlyDictionary<string, string> cacheKeys)
        : this(ConnectedOf(sim), () => sim.FlightRunning, () => sim.CurrentAircraftTitle, key => sim.GetCachedVariableValue(key),
            cacheKeys, () => SettingsManager.Current, announcer, session, filesNotice)
    {
    }

    /// <summary>For tests: the simulator's connection, flight, title and cache, and the settings, as delegates.</summary>
    internal Bbs757DisplayEnvironment(Func<bool> connected, Func<bool> flightRunning, Func<string?> title,
        Func<string, double?> cached, IReadOnlyDictionary<string, string> cacheKeys, Func<UserSettings> settings,
        ScreenReaderAnnouncer announcer, ModuleSession session, Bbs757DisplayFilesNotice filesNotice)
    {
        _connected = connected ?? throw new ArgumentNullException(nameof(connected));
        _flightRunning = flightRunning ?? throw new ArgumentNullException(nameof(flightRunning));
        _title = title ?? throw new ArgumentNullException(nameof(title));
        _cached = cached ?? throw new ArgumentNullException(nameof(cached));
        _cacheKeys = cacheKeys ?? throw new ArgumentNullException(nameof(cacheKeys));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _announcer = announcer ?? throw new ArgumentNullException(nameof(announcer));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        FilesNotice = filesNotice ?? throw new ArgumentNullException(nameof(filesNotice));
    }

    private static Func<bool> ConnectedOf(SimConnectManager sim)
    {
        ArgumentNullException.ThrowIfNull(sim);
        return () => sim.IsConnected;
    }

    /// <summary>The session's notice, shared with every other reader's monitor (S14).</summary>
    public Bbs757DisplayFilesNotice FilesNotice { get; }

    public long NowMs => Environment.TickCount64;

    /// <summary>Whether <paramref name="title"/> is a BlueBird 757's: it contains <see cref="TitleMark"/>.</summary>
    public static bool IsBbs757Title(string? title) =>
        title is not null && title.Contains(TitleMark, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The cached values of the reader inputs whose <see cref="Bbs757SimInput.Key"/>s are <paramref name="keys"/>, each
    /// under its key (an L:var's or a stock variable's alike); null, so the monitor waits silently, while the simulator is
    /// disconnected, while no flight runs (the "Sim" system event: the menus, a flight's load), while the loaded title is
    /// no 757's (another aircraft under the 757 profile), or while a key has no value yet. With no keys (a reader that
    /// takes no input: the FMA, the lower EICAS) it is the same wait, and otherwise an empty set of values. The connection
    /// is checked first, before anything else is read ([WASM-7]'s order): a closed simulator leaves the last title and
    /// values in place, and without the check the monitor would search memory that is gone and say "unavailable: aircraft
    /// memory not found" two minutes later. The flight comes next for the same reason: in the menus the title stays the last
    /// flight's, so the title alone would send the monitor searching there.
    /// </summary>
    public Bbs757SimValues? ReadSimulator(IReadOnlyList<string> keys)
    {
        if (!_connected()) return null;
        if (!_flightRunning()) return null;
        if (!IsBbs757Title(_title())) return null;
        var values = new Dictionary<string, double>(keys.Count, StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (!_cacheKeys.TryGetValue(key, out string? varKey) || _cached(varKey) is not { } value) return null;
            values[key] = value;
        }
        return new Bbs757SimValues(values);
    }

    /// <summary>Whether the 757 Monitor Manager (Ctrl+M) has <paramref name="muteKey"/>'s row muted.</summary>
    public bool IsMuted(string muteKey) => _settings().BBS757DisabledMonitorVariablesSet.Contains(muteKey);

    /// <summary>The non-interrupting announce (S5): a reader never cuts off another line.</summary>
    public void Speak(string text) => _announcer.Announce(text);

    public void Log(string channel, string line) => AppLog.Channel(channel).Info(line);

    public Task<ModuleAnalysis> AnalyseAsync() => _session.AnalyseAsync();

    public Task<ModuleLocation> LocateAsync() => _session.LocateAsync();
}

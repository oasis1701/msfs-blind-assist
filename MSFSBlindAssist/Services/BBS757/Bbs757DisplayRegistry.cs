using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// What a 757 display reader's key handler is handed: the definition's hotkey arguments, and <see cref="AiRead"/>, which
/// runs the base's AI read of the same key (S15: the fallback when the reader cannot run).
/// </summary>
internal readonly record struct Bbs757KeyContext(SimConnectManager Sim, ScreenReaderAnnouncer Announcer, Form Parent,
    HotkeyManager Hotkeys, Func<bool> AiRead);

/// <summary>
/// The 757's display readers, as <c>BBS757Definition</c> registers them (the FMA itself, then one seam per later reader,
/// S16): their monitors, which the definition starts on its one module session, restarts on a flight reload and stops on a
/// switch away; their Ctrl+M rows, listed last in the 757's Monitor Manager; and their keys, which the definition offers
/// a reader before anything else it does with a key. Filled once, on the UI thread, and read there.
/// </summary>
internal sealed class Bbs757DisplayRegistry
{
    private readonly List<IBbs757DisplayMonitor> _monitors = new();
    private readonly List<MonitorRow> _rows = new();
    private readonly List<Bbs757SimInput> _inputs = new();
    private readonly Dictionary<HotkeyAction, Func<Bbs757KeyContext, bool>> _keys = new();
    private readonly List<Form> _windows = new();

    /// <summary>Every reader's monitor, in registration order (the FMA first).</summary>
    public IReadOnlyList<IBbs757DisplayMonitor> Monitors => _monitors;

    /// <summary>Every reader's Ctrl+M rows, in registration order.</summary>
    public IReadOnlyList<MonitorRow> Rows => _rows;

    /// <summary>Every reader's simulator inputs (its monitor's <see cref="IBbs757DisplayMonitor.SimInputs"/>), each key
    /// once, in registration order: the definition puts each on the continuous batch and reads it from there.</summary>
    public IReadOnlyList<Bbs757SimInput> SimInputs => _inputs;

    /// <summary>
    /// A reader's monitor, with the simulator inputs its reader declares (taken from the monitor, so they can never differ
    /// from the reader's). A key two readers share must name the same variable; an input must be an L:var or a stock
    /// variable with its units. Either mistake is a wiring error, so it throws, and registers nothing of the reader.
    /// </summary>
    public void Add(IBbs757DisplayMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var inputs = monitor.SimInputs ?? throw new ArgumentException("the monitor has no simulator inputs list", nameof(monitor));
        var added = new List<Bbs757SimInput>();
        foreach (var input in inputs)
        {
            Check(input);
            var known = _inputs.Concat(added).Where(i => string.Equals(i.Key, input.Key, StringComparison.Ordinal)).ToList();
            if (known.Count == 0) added.Add(input);
            else if (!known[0].SameVariable(input))
                throw new InvalidOperationException(
                    $"757 display input key {input.Key} is already {Describe(known[0])}, not {Describe(input)}.");
        }
        _inputs.AddRange(added);
        _monitors.Add(monitor);
    }

    private static void Check(Bbs757SimInput input)
    {
        if (string.IsNullOrEmpty(input.Key) || string.IsNullOrEmpty(input.Name))
            throw new ArgumentException("a 757 display reader input needs a key and a name");
        if (input.Type is not (SimVarType.LVar or SimVarType.SimVar))
            throw new ArgumentException($"757 display reader input {input.Key} is a {input.Type}: only an L:var or a stock variable");
        if (input.Type == SimVarType.SimVar && string.IsNullOrEmpty(input.Units))
            throw new ArgumentException($"757 display reader input {input.Key} is a stock variable with no units");
    }

    private static string Describe(Bbs757SimInput input) =>
        input.Type == SimVarType.LVar ? $"L:{input.Name}" : $"{input.Name} in {input.Units}";

    /// <summary>The handler of <paramref name="action"/>: true when it took the key. One per key: two readers on one key
    /// is a wiring error, so a second throws.</summary>
    public void OnKey(HotkeyAction action, Func<Bbs757KeyContext, bool> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!_keys.TryAdd(action, handler))
            throw new InvalidOperationException($"A 757 display reader already handles {action}.");
    }

    public void AddMonitorRow(MonitorRow row) => _rows.Add(row);

    /// <summary>Every window a reader keeps for reuse, in the order owned.</summary>
    public IReadOnlyList<Form> Windows => _windows;

    /// <summary>A window a reader made once and shows again (its <c>BBS757DisplayForm</c>): the definition's switch away
    /// closes it for good and disposes it, so it never stays up showing a stopped monitor. Each window once; on the UI
    /// thread.</summary>
    public void OwnWindow(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!_windows.Contains(form)) _windows.Add(form);
    }

    /// <summary>Runs the key's handler: true when there is one and it took the key; false leaves the key to the
    /// definition.</summary>
    public bool TryHandle(HotkeyAction action, Bbs757KeyContext context) =>
        _keys.TryGetValue(action, out var handler) && handler(context);
}

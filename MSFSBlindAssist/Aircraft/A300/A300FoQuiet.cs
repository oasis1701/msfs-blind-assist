namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The A300 First Officer's quiet, per control (the MD-11's model, design decision 2026-10-10; [FOA-14]). A First
/// Officer write quiets only the row it moved (its lever call-out, its "did not move" read-back) and that row's own lights
/// for <see cref="RowQuietMs"/>; the flow step already says what happened. Every other light, and the master warning
/// and caution, still speak. Only a system test's run quiets every light (<see cref="QuietAll"/>), since a test lights
/// many. A later, shorter call never shortens a window. Pure; the caller passes the time.
/// </summary>
public sealed class A300FoQuiet
{
    public const int RowQuietMs = 5000;

    private readonly Dictionary<string, long> _rowUntil = new(StringComparer.Ordinal);
    private long _allUntil = long.MinValue;

    // Declared before OwnedLamps, which reads it in its static initializer.
    private static readonly IReadOnlyDictionary<string, string> IrsPanels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["A300_NAVATTLEFT"] = "IRS_1_",
        ["A300_NAVATTRIGHT"] = "IRS_3_",
        ["A300_NAVATTCTR"] = "IRS_2_",
    };

    /// <summary>
    /// A row's own lights: the legends on its own button, which the cockpit names after it (row <c>A300_X</c>,
    /// light node <c>X_…</c>); the light that labels a button (<see cref="A300PanelLamps.ByButton"/>); an autobrake
    /// button's armed light (its decel light is the braking, not the press); and each IRS mode selector's panel
    /// lights, whose nodes iniBuilds numbers differently (IRS 2's panel is node IRS_3, IRS 3's is IRS_2).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> OwnedLamps = BuildOwnership();

    /// <summary>Every light some row owns.</summary>
    public static IEnumerable<string> AllOwnedLamps => OwnedLamps.Values.SelectMany(l => l).Distinct();

    /// <summary>The lights <paramref name="rowKey"/> owns (empty for a row with none).</summary>
    public static IReadOnlySet<string> LampsOf(string rowKey) =>
        OwnedLamps.TryGetValue(rowKey, out var lamps) ? lamps : new HashSet<string>();

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildOwnership()
    {
        var owned = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Add(string row, string lamp)
        {
            if (!owned.TryGetValue(row, out var set))
                owned[row] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(lamp);
        }
        var lamps = A300PanelLamps.Resolved.Select(r => r.Lamp).ToList();   // the lights the board reads
        foreach (var control in A300ControlMap.Load().Controls.Where(c => c.Key.StartsWith("A300_", StringComparison.Ordinal)))
        {
            string node = control.Key["A300_".Length..] + "_";
            foreach (var lamp in lamps.Where(l => l.Node.StartsWith(node, StringComparison.Ordinal)))
                Add(control.Key, lamp.Key);
        }
        foreach (var (row, lamp) in A300PanelLamps.ByButton)
            Add(row, lamp);
        foreach (var (row, button) in A300Autobrake.ByButton)
            Add(row, button.ArmedLampId);
        foreach (var (row, prefix) in IrsPanels)
            foreach (var lamp in lamps.Where(l => l.Node.StartsWith(prefix, StringComparison.Ordinal)))
                Add(row, lamp.Key);
        return owned.ToDictionary(p => p.Key, p => (IReadOnlySet<string>)p.Value, StringComparer.Ordinal);
    }

    /// <summary>A First Officer write to <paramref name="rowKey"/>: quiets it and its own lights.</summary>
    public void NoteRow(string rowKey, long nowMs, int windowMs = RowQuietMs)
    {
        long until = nowMs + windowMs;
        if (!_rowUntil.TryGetValue(rowKey, out var current) || until > current)
            _rowUntil[rowKey] = until;
    }

    /// <summary>A system test's run: every light and the master lights stay quiet.</summary>
    public void QuietAll(long nowMs, int windowMs)
    {
        long until = nowMs + windowMs;
        if (until > _allUntil)
            _allUntil = until;
    }

    public bool IsRowQuiet(string rowKey, long nowMs) =>
        _rowUntil.TryGetValue(rowKey, out var until) && nowMs <= until;

    /// <summary>The master warning and caution: quiet only during a test.</summary>
    public bool IsMasterQuiet(long nowMs) => nowMs <= _allUntil;

    public bool IsLampQuiet(string lampId, long nowMs) =>
        nowMs <= _allUntil || _rowUntil.Any(r => nowMs <= r.Value && LampsOf(r.Key).Contains(lampId));

    public void Clear()
    {
        _rowUntil.Clear();
        _allUntil = long.MinValue;
    }
}

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The 757's cockpit-wide announcements (plan 13): turns the watched L:vars' changes, as the tablet
/// agent reports them, into speech. A light says its phrase as it comes on or goes out; a switch moved
/// by anything but the pilot's own pick in the app (a preset, BlueBird's automation, a hardware panel,
/// the mouse) says "{label} {position}" from the control map. The pilot's own write is its echo and
/// is never said: within <see cref="OwnWriteEchoMs"/> of <see cref="OwnWrite"/> for the control, or
/// while its pick is pending. A burst (more than <see cref="BurstSize"/> switch moves in one read or
/// within <see cref="BurstWindowMs"/>) is said as one "Panel state changed: N switches" line, and a
/// flood of lights (a lights test, power on or off) as one "N lights on, M off" line. Nothing is said until the flight has settled (<see cref="Settle"/>, then
/// <see cref="SettleQuietMs"/> of quiet), so a flight load's flood of changes stays silent.
///
/// Each light and switch has a Ctrl+M row (<see cref="MonitorRows"/>), and group rows mute all lights,
/// all switches (the burst line included) or one panel's switches (<see cref="IsMuted"/>).
/// </summary>
public sealed class Bbs757CockpitAnnouncements
{
    /// <summary>How long after the app's own write a change of that control is its echo.</summary>
    public const long OwnWriteEchoMs = 5000;

    /// <summary>More switch moves than this, in one read or within <see cref="BurstWindowMs"/>, are one summary line.</summary>
    public const int BurstSize = 5;

    /// <summary>The window in which switch moves count toward one burst.</summary>
    public const long BurstWindowMs = 1000;

    /// <summary>The quiet after <see cref="Settle"/> before anything is said: the end of a flight load's flood.</summary>
    public const long SettleQuietMs = 5000;

    public const string KeyPrefix = "BBS757W_";
    public const string AllLightsKey = KeyPrefix + "ALL_LIGHTS";
    public const string AllSwitchesKey = KeyPrefix + "ALL_SWITCHES";

    /// <summary>The Ctrl+M key of one light or switch row, by its L:var.</summary>
    public static string RowKey(string lvar) => KeyPrefix + lvar;

    /// <summary>The Ctrl+M key that mutes one panel's switches.</summary>
    public static string PanelKey(string panel) => KeyPrefix + "PANEL_" + panel;

    private readonly Dictionary<string, Bbs757Light> _lights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Bbs757Control> _switches = new(StringComparer.Ordinal);
    private readonly Func<long> _nowMs;
    private readonly Dictionary<string, long> _ownWrites = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _lightOn = new(StringComparer.Ordinal);
    private readonly Queue<(long At, int Count)> _recentSwitches = new();
    private readonly Queue<(long At, int Count)> _recentLights = new();
    private long? _settledAt;

    public Bbs757CockpitAnnouncements(IReadOnlyList<Bbs757Light> lights, IReadOnlyList<Bbs757Control> controls, Func<long> nowMs)
    {
        _nowMs = nowMs;
        foreach (var l in lights) _lights.TryAdd(l.LVar, l);
        foreach (var c in controls)
            if (!string.IsNullOrWhiteSpace(c.StateVar) && !_lights.ContainsKey(c.StateVar!))
                _switches.TryAdd(c.StateVar!, c);
    }

    /// <summary>The app wrote this control (a panel press or pick): its change in the next few seconds is the echo.</summary>
    public void OwnWrite(string node) => _ownWrites[node] = _nowMs();

    /// <summary>The flight has settled (the connection's first batch): speech starts after <see cref="SettleQuietMs"/>.</summary>
    public void Settle() => _settledAt ??= _nowMs();

    /// <summary>A flight load or reconnect: silent again until the next <see cref="Settle"/>.</summary>
    public void Reset()
    {
        _settledAt = null;
        _ownWrites.Clear();
        _lightOn.Clear();
        _recentSwitches.Clear();
        _recentLights.Clear();
    }

    /// <summary>
    /// What to say for one read's changes, lights and switches by L:var: switch lines first (or
    /// their summary), then the lights. <paramref name="isPickPending"/> takes a control's node;
    /// <paramref name="inMuteSet"/> a Ctrl+M key.
    /// </summary>
    public IReadOnlyList<string> Changes(IReadOnlyDictionary<string, double> changes, Func<string, bool> isPickPending, Func<string, bool> inMuteSet)
    {
        long now = _nowMs();
        bool speaking = _settledAt is long s && now - s >= SettleQuietMs;
        var switchLines = new List<string>();
        var lightLines = new List<(string Phrase, bool On)>();

        foreach (var (name, value) in changes)
        {
            if (_lights.TryGetValue(name, out var light))
            {
                bool on = value > 0;
                bool known = _lightOn.TryGetValue(name, out bool wasOn);
                _lightOn[name] = on;
                if (!speaking || (known && wasOn == on)) continue;
                string? phrase = on ? light.OnPhrase : light.OffPhrase;
                if (phrase != null && !IsMuted(name, true, null, inMuteSet)) lightLines.Add((phrase, on));
            }
            else if (_switches.TryGetValue(name, out var control))
            {
                if (!speaking) continue;
                if (_ownWrites.TryGetValue(control.Node, out long at) && now - at < OwnWriteEchoMs) continue;
                if (isPickPending(control.Node)) continue;
                if (IsMuted(name, false, control.Panel, inMuteSet)) continue;
                switchLines.Add(control.Label + " " + PositionName(control, value));
            }
        }

        var said = new List<string>();
        if (switchLines.Count > 0)
        {
            if (IsBurst(_recentSwitches, switchLines.Count, now))
            {
                if (!inMuteSet(AllSwitchesKey))
                    said.Add($"Panel state changed: {switchLines.Count.ToString(CultureInfo.InvariantCulture)} switches");
            }
            else said.AddRange(switchLines);
        }
        if (lightLines.Count > 0)
        {
            // A lights test, or power coming on or going off, changes dozens at once: one line.
            if (IsBurst(_recentLights, lightLines.Count, now)) said.Add(LightSummary(lightLines.Count(l => l.On), lightLines.Count(l => !l.On)));
            else said.AddRange(lightLines.Select(l => l.Phrase));
        }
        return said;
    }

    /// <summary>
    /// Whether <paramref name="count"/> changes now, with those of the last <see cref="BurstWindowMs"/>,
    /// make a burst (more than <see cref="BurstSize"/>); records them.
    /// </summary>
    private static bool IsBurst(Queue<(long At, int Count)> recentChanges, int count, long now)
    {
        while (recentChanges.Count > 0 && now - recentChanges.Peek().At >= BurstWindowMs) recentChanges.Dequeue();
        int recent = recentChanges.Sum(r => r.Count);
        recentChanges.Enqueue((now, count));
        return count > BurstSize || recent + count > BurstSize;
    }

    /// <summary>"9 lights on", "3 lights off", "1 light on, 9 off".</summary>
    private static string LightSummary(int on, int off)
    {
        static string N(int n) => n.ToString(CultureInfo.InvariantCulture);
        string lights(int n) => n == 1 ? "light" : "lights";
        if (off == 0) return $"{N(on)} {lights(on)} on";
        if (on == 0) return $"{N(off)} {lights(off)} off";
        return $"{N(on)} {lights(on)} on, {N(off)} off";
    }

    /// <summary>A position's own name, or the value itself (invariant) when the map names none.</summary>
    private static string PositionName(Bbs757Control control, double value)
    {
        foreach (var p in control.Positions)
            if (Math.Abs(p.Value - value) < 0.001) return p.Label;
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether a light or switch row is muted: its own key, its group's (all lights or all switches)
    /// or, for a switch, its panel's key is in the Ctrl+M set. Pure.
    /// </summary>
    public static bool IsMuted(string lvar, bool isLight, string? panel, Func<string, bool> inMuteSet)
    {
        if (inMuteSet(RowKey(lvar))) return true;
        if (isLight) return inMuteSet(AllLightsKey);
        if (inMuteSet(AllSwitchesKey)) return true;
        return !string.IsNullOrEmpty(panel) && inMuteSet(PanelKey(panel));
    }

    /// <summary>
    /// The Ctrl+M rows, in order: all lights, all switches, one row per panel's switches (by panel
    /// name), then one row per light and one per switch. The shared dialog's search finds "Light:",
    /// "Switch:" or a panel's name. Pure.
    /// </summary>
    public static IReadOnlyList<MonitorRow> MonitorRows(IReadOnlyList<Bbs757Light> lights, IReadOnlyList<Bbs757Control> watchedSwitches)
    {
        var rows = new List<MonitorRow>
        {
            new(AllLightsKey, "All Annunciator Lights"),
            new(AllSwitchesKey, "All Switches From Any Source"),
        };
        var keys = new HashSet<string>(rows.Select(r => r.Key), StringComparer.Ordinal);
        void Add(string key, string label) { if (keys.Add(key)) rows.Add(new MonitorRow(key, label)); }

        foreach (var panel in watchedSwitches.Select(c => c.Panel).Where(p => !string.IsNullOrEmpty(p))
                     .Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            Add(PanelKey(panel), "Switches: " + panel);
        foreach (var l in lights) Add(RowKey(l.LVar), "Light: " + l.Label);
        foreach (var c in watchedSwitches)
            if (!string.IsNullOrWhiteSpace(c.StateVar)) Add(RowKey(c.StateVar!), $"Switch: {c.Panel}, {c.Label}");
        return rows;
    }
}

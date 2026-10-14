namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// One VHF radio's call-out for a frequency change MSFSBA did not make ([A300-29]): an ATC program setting the next
/// frequency into the standby window, a swap, a hardware transfer button. It reads what the cockpit shows, the
/// aircraft's OWN two windows and its transfer switch (<see cref="A300Radio.Active"/> and
/// <see cref="A300Radio.Standby"/>, [A300-26]), never the stock COM frequencies: the A300 mirrors the stock standby into
/// its window and shows its own windows, so they are right whoever moved them. Pure: the caller passes the readings, the
/// clock and whether a flight load is still settling.
///
/// The pairs are compared as formatted text, never as raw doubles. A reading speaks ONE sentence for what changed,
/// "VHF 1 standby 121.500", "VHF 1 active 124.805", or both after a swap, "VHF 1 active 124.805, standby 121.500", and
/// becomes the baseline. A change speaks only once its (active, standby) pair has read the same at two consecutive
/// flushes. The stock swap event EXCHANGES the two windows' contents and leaves the transfer switch alone (measured
/// 2026-10-14), and each window streams on its own once-a-second subscription, so the two new values can reach the
/// definition either side of a batch end: spoken at the first flush that saw a changed pair, one swap was two
/// sentences, the first of them half of it. The caller flushes once per batch period, which outlasts the gap between
/// the two windows' deliveries (each is read once a second, so the two arrive less than a second apart), and a pair
/// that moves again is held again.
///
/// MSFSBA's own change is quiet: from <see cref="MarkOwn"/>, for <see cref="A300CommandedState.HoldMs"/>, every
/// flush only re-baselines, because the typed confirmation or the transfer read-back has already said it ([CORE-7]).
/// The window is time alone, never a value compare: a rounding or an encoding difference could not make an own change
/// speak twice. A radio has no baseline until all three inputs have been read, and none while a flight load is
/// still settling or after <see cref="Reset"/>: the first complete reading is a baseline, never a change.
/// </summary>
public sealed class A300FrequencyWatch
{
    private readonly record struct Pair(string Active, string Standby);

    private Pair? _baseline;
    private Pair? _seen;
    private long _ownUntilMs = long.MinValue;

    public A300FrequencyWatch(A300Radio radio) => Radio = radio;

    public A300Radio Radio { get; }

    /// <summary>MSFSBA is changing this radio now (the typed standby box, a transfer press, a tune of its own).</summary>
    public void MarkOwn(long nowMs) => _ownUntilMs = nowMs + A300CommandedState.HoldMs;

    /// <summary>A context reset: the next complete reading is a baseline, and a pending mark is gone.</summary>
    public void Reset()
    {
        _baseline = null;
        _seen = null;
        _ownUntilMs = long.MinValue;
    }

    /// <summary>
    /// A flush with the inputs' current readings (null for one never read). The sentence to speak, or null: no
    /// baseline yet, an own change, a settling load, a pair not yet held for a flush, or nothing changed.
    /// </summary>
    public string? Flush(double? window1, double? window2, double? transfer, long nowMs, bool settling)
    {
        if (window1 is not double w1 || window2 is not double w2 || transfer is not double t)
        {
            _baseline = null;
            _seen = null;
            return null;
        }
        var now = new Pair(Radio.Active(w1, w2, t), Radio.Standby(w1, w2, t));
        if (_baseline is not { } was || settling || nowMs <= _ownUntilMs)
        {
            _baseline = now;
            _seen = now;
            return null;
        }
        bool held = _seen == now;
        _seen = now;
        if (now == was || !held)
            return null;
        _baseline = now;
        return Sentence(was, now);
    }

    private string Sentence(Pair was, Pair now) =>
        now.Active != was.Active && now.Standby != was.Standby ? $"{Radio.Name} active {now.Active}, standby {now.Standby}"
        : now.Active != was.Active ? $"{Radio.Name} active {now.Active}"
        : $"{Radio.Name} standby {now.Standby}";
}

/// <summary>
/// The squawk's call-out for a code MSFSBA did not set ([A300-29]): "Squawk 5473", the digits as one number, as a typed
/// code's confirmation and a code knob's read-back say it. A single value, so there is nothing to hold: it speaks at the
/// first flush that sees a different code, compared as the formatted digits. The same baseline, own-change and settling
/// rules as <see cref="A300FrequencyWatch"/>. Pure.
/// </summary>
public sealed class A300SquawkWatch
{
    private string? _baseline;
    private long _ownUntilMs = long.MinValue;

    /// <summary>MSFSBA is changing the code now (the typed box, a code knob step).</summary>
    public void MarkOwn(long nowMs) => _ownUntilMs = nowMs + A300CommandedState.HoldMs;

    public void Reset()
    {
        _baseline = null;
        _ownUntilMs = long.MinValue;
    }

    /// <summary>A flush with the code's current reading (BCO16, null if never read): the sentence to speak, or null.</summary>
    public string? Flush(double? code, long nowMs, bool settling)
    {
        if (code is not double bcd)
        {
            _baseline = null;
            return null;
        }
        string digits = A300Readouts.Squawk(bcd);
        if (_baseline is null || settling || nowMs <= _ownUntilMs)
        {
            _baseline = digits;
            return null;
        }
        if (digits == _baseline)
            return null;
        _baseline = digits;
        return A300Readouts.SquawkPhrase(bcd);
    }
}

/// <summary>
/// What the outside-change call-outs read and which of MSFSBA's own actions quiet them ([A300-29]): the VHF radios'
/// windows and transfer switches, the squawk code, the keys that carry the three Ctrl+M rows ("VHF 1 frequencies",
/// "VHF 2 frequencies", "Squawk"), and the typed boxes, transfer presses and code knobs that are MSFSBA's own changes.
/// The ADF radios are not watched.
/// </summary>
public static class A300RadioWatches
{
    /// <summary>The squawk's own subscription to the transponder code, which carries its Ctrl+M row. Not the readout
    /// (<see cref="A300Readouts.SquawkCodeKey"/>), which is read on request.</summary>
    public const string SquawkKey = "A300_RADIO_SQUAWK";
    public const string SquawkVar = "TRANSPONDER CODE:1";
    public const string SquawkUnits = "BCO16";
    public const string SquawkRowName = "Squawk";

    /// <summary>The transponder code knobs' control keys: a step of either is MSFSBA's own change of the squawk, and is
    /// read back (<see cref="A300Readouts.KnobReadBacks"/>).</summary>
    public static readonly IReadOnlySet<string> CodeKnobKeys =
        new HashSet<string>(StringComparer.Ordinal) { "A300_TCAS_BIG", "A300_TCAS_SMALL" };

    public static bool Watches(A300Radio radio) => A300Radios.Vhf.Contains(radio);

    /// <summary>The key that carries a VHF radio's Ctrl+M row: its transfer switch's.</summary>
    public static string RowKey(A300Radio radio) => radio.TransferKey;

    public static string RowName(A300Radio radio) => $"{radio.Name} frequencies";

    /// <summary>The keys that carry a Ctrl+M row: each VHF radio's transfer switch and the squawk.</summary>
    public static readonly IReadOnlySet<string> RowKeys =
        A300Radios.Vhf.Select(RowKey).Append(SquawkKey).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every key a watch reads: each VHF radio's two windows and transfer switch, and the squawk.</summary>
    public static readonly IReadOnlySet<string> InputKeys =
        A300Radios.Vhf.SelectMany(r => new[] { r.Window1Key, r.Window2Key, r.TransferKey }).Append(SquawkKey)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>MSFSBA's own changes of a VHF radio: its typed standby box and its transfer button (by control key).</summary>
    private static readonly IReadOnlyDictionary<string, A300Radio> VhfByOwnKey = BuildVhfByOwnKey();

    /// <summary>The VHF radio a typed standby box (a typed key) or a transfer button (its control key) changes, or null.</summary>
    public static A300Radio? VhfRadioFor(string key) => VhfByOwnKey.GetValueOrDefault(key);

    /// <summary>Whether the typed squawk box (its typed key) or a code knob (its control key) changes the squawk.</summary>
    public static bool IsSquawkOwnChange(string key) => key == A300TypedValues.SquawkKey || CodeKnobKeys.Contains(key);

    private static Dictionary<string, A300Radio> BuildVhfByOwnKey()
    {
        var own = new Dictionary<string, A300Radio>(StringComparer.Ordinal)
        {
            [A300TypedValues.Com1StandbyKey] = A300Radios.Vhf[0],
            [A300TypedValues.Com2StandbyKey] = A300Radios.Vhf[1],
        };
        foreach (var (button, radio) in A300Radios.ByTransfer)
            if (Watches(radio))
                own[button] = radio;
        return own;
    }
}

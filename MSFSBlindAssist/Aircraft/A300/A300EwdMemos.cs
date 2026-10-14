using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>A variable a memo's condition reads: its own subscription's key, and its unit when stock.</summary>
public sealed record A300MemoInput(string Key, string Var, bool IsStock, string Units);

/// <summary>One E/WD memo: the aircraft's text, the words read out, and its condition (null while an input is
/// unread).</summary>
public sealed record A300Memo(int Number, string Text, string Words, Func<Func<string, double?>, bool?> Condition)
{
    public bool? IsActive(Func<string, double?> read) => Condition(read);
}

/// <summary>What one sample changed: the memos shown now, in the E/WD's order, and those shown for the first time.</summary>
public sealed record A300MemoUpdate(IReadOnlyList<A300Memo> Displayed, IReadOnlyList<A300Memo> Shown);

/// <summary>
/// The E/WD memos, transcribed from the A300's own EWD code (inibuilds-A300.wasm, v1.0.11, read 2026-10-10): the
/// twenty <c>Message_MemoN::is_active</c> conditions over the aircraft's variables (three are stock:
/// <c>AircraftVariables</c> 17, 85 and 279), in <c>EWD::prepareMemoList</c>'s order, which is the order shown.
/// Nothing reads the simulator's memory or a picture ([A300-8]).
/// </summary>
public static class A300EwdMemos
{
    public const string Panel = "ECAM Memos";
    public const string LineKey = "A300_RO_EWD_MEMOS";

    /// <summary>A memo is shown once active for longer than this (<c>EWD::UpdateMemo</c>).</summary>
    public const long ShowAfterMs = 2500;

    private static readonly List<A300MemoInput> InputList = new();

    public static IReadOnlyList<A300MemoInput> Inputs => InputList;

    public static readonly IReadOnlyList<A300Memo> All = new[]
    {
        Memo(1, "CAB PRESS MAN CTL", "Cabin pressure manual control", Eq("INI_CABIN_MAN_PRESS_ARROW", 1)),
        Memo(2, "TAT IN ICING RANGE", "TAT in icing range", Between(Stock("TOTAL AIR TEMPERATURE", "Celsius"), -15, 5)),
        Memo(3, "APU RUNNING", "APU running", Eq("INI_apu_available", 1)),
        Memo(4, "CONTINUOUS RELIGHT ON", "Continuous relight on", Eq("INI_eng_ignition_switch", 4)),
        Memo(5, "FUEL X FEED", "Fuel crossfeed", Eq("INI_xfeed_transfer_on", 1)),
        Memo(6, "EXT PWR CONNECTED", "External power connected", Eq(Stock("EXTERNAL POWER ON:1", "Bool"), 1)),
        Memo(7, "PARKING BRAKE ON", "Parking brake on", Eq("INI_PARKING_BRAKE_STATUS", 1)),
        Memo(8, "SEAT BELTS ON", "Seat belts on", Eq("INI_SEATBELTS_SWITCH", 1)),
        Memo(9, "NO SMOKING ON", "No smoking on", Eq("INI_NO_SMOKING_ON", 1)),
        Memo(10, "FUEL FEED MAN CTL", "Fuel feed manual control", Eq("INI_FUEL_FEED_MAN_CTL", 1)),
        Memo(11, "SPD BRAKES EXTENDED", "Speed brakes extended", Above(Stock("SPOILERS HANDLE POSITION", "Percent"), 0)),
        Memo(12, "LDG LIGHT EXTENDED", "Landing light extended",
            Or(AtMost("INI_LANDING_LIGHT_L_SWITCH", 1), AtMost("INI_LANDING_LIGHT_R_SWITCH", 1))),
        Memo(13, "ECON FLOW SELECTED", "Economy flow selected", Eq("INI_econ_flow_selected", 1)),
        Memo(14, "MAX COOL ON", "Max cool on", Eq("INI_max_cool", 1)),
        Memo(15, "EMER CANCEL ON", "Emergency cancel on", Eq("INI_EMER_CANCEL", 1)),
        Memo(16, "ENG ANTI ICE ON", "Engine anti ice on", Or(Eq("INI_ENG1_ANTI_ICE", 1), Eq("INI_ENG2_ANTI_ICE", 1))),
        Memo(17, "WING ANTI ICE ON", "Wing anti ice on", Eq("INI_WING_ANTI_ICE", 1)),
        Memo(18, "CTR TANK FEEDING", "Center tank feeding", Eq("INI_CENTER_TANK_IS_FEEDING", 1)),
        Memo(19, "IRS IN ALIGN", "IRS in align", Or(Aligning(1), Aligning(2), Aligning(3))),
        Memo(20, "TCAS STBY", "TCAS standby", AtMost("INI_tcas_mode_pedestal", 1)),
    };

    /// <summary>The status line: the memos shown, or "none". Memos are never spoken (design decision, 2026-10-11).</summary>
    public static string Line(IReadOnlyCollection<string> words) => words.Count == 0 ? "none" : string.Join(", ", words);

    private static A300Memo Memo(int number, string text, string words, Func<Func<string, double?>, bool?> condition) =>
        new(number, text, words, condition);

    private static string Input(string var, bool isStock = false, string units = "number")
    {
        string key = "A300_MEMO_IN_" + Regex.Replace(var.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
        if (!InputList.Any(i => i.Key == key))
            InputList.Add(new A300MemoInput(key, var, isStock, units));
        return key;
    }

    private static string Stock(string var, string units) => Input(var, isStock: true, units);

    /// <summary>A variable by its key; a bare name is an L:var.</summary>
    private static string KeyOf(string varOrKey) => varOrKey.StartsWith("A300_MEMO_IN_", StringComparison.Ordinal) ? varOrKey : Input(varOrKey);

    // The aircraft reads these as integers (Variables::GetInt), so a value is rounded before it is compared.
    private static Func<Func<string, double?>, bool?> Eq(string var, int value)
    {
        string key = KeyOf(var);
        return read => read(key) is double v ? Math.Round(v) == value : null;
    }

    private static Func<Func<string, double?>, bool?> AtMost(string var, int value)
    {
        string key = KeyOf(var);
        return read => read(key) is double v ? Math.Round(v) <= value : null;
    }

    private static Func<Func<string, double?>, bool?> Above(string key, double value) =>
        read => read(key) is double v ? v > value : null;

    private static Func<Func<string, double?>, bool?> Between(string key, double low, double high) =>
        read => read(key) is double v ? v >= low && v <= high : null;

    private static Func<Func<string, double?>, bool?> Aligning(int irs)
    {
        string aligning = Input($"INI_IRS{irs}_IS_ALIGNING");
        string remain = Input($"INI_IRS{irs}_TIME_REMAIN");
        return read => read(aligning) is double a && read(remain) is double t ? Math.Round(a) == 1 && t != 0 : null;
    }

    /// <summary>True when any side is; unknown when none is and a side is unread.</summary>
    private static Func<Func<string, double?>, bool?> Or(params Func<Func<string, double?>, bool?>[] sides) => read =>
    {
        var values = sides.Select(s => s(read)).ToArray();
        return values.Any(v => v == true) ? true : values.Any(v => v == null) ? null : false;
    };
}

/// <summary>
/// Which memos the E/WD shows, as <c>EWD::UpdateMemo</c> decides it: a memo is shown once it has been active for
/// longer than <see cref="A300EwdMemos.ShowAfterMs"/>, and drops out, its wait starting again, as soon as it is not.
/// The first reading with every input known is the baseline: what it shows was not a change, so nothing is new.
/// </summary>
public sealed class A300MemoTracker
{
    private readonly bool[] _shown = new bool[A300EwdMemos.All.Count];
    private readonly long?[] _since = new long?[A300EwdMemos.All.Count];
    private bool _baselinePending = true;

    /// <summary>Whether a complete reading has set the baseline, so what is shown is known.</summary>
    public bool HasBaseline => !_baselinePending;

    public void Reset()
    {
        Array.Clear(_shown);
        Array.Clear(_since);
        _baselinePending = true;
    }

    public A300MemoUpdate Update(Func<string, double?> read, long nowMs)
    {
        var memos = A300EwdMemos.All;
        var active = memos.Select(m => m.IsActive(read)).ToArray();
        if (_baselinePending)
        {
            if (active.Any(a => a == null))
                return new A300MemoUpdate(Array.Empty<A300Memo>(), Array.Empty<A300Memo>());
            for (int i = 0; i < memos.Count; i++)
            {
                _shown[i] = active[i] == true;
                _since[i] = null;
            }
            _baselinePending = false;
            return new A300MemoUpdate(Displayed(), Array.Empty<A300Memo>());
        }

        var shownNow = new List<A300Memo>();
        for (int i = 0; i < memos.Count; i++)
        {
            switch (active[i])
            {
                case true when !_shown[i]:
                    if (_since[i] is not long since)
                        _since[i] = nowMs;
                    else if (nowMs - since > A300EwdMemos.ShowAfterMs)
                    {
                        _shown[i] = true;
                        shownNow.Add(memos[i]);
                    }
                    break;
                case false:
                    _shown[i] = false;
                    _since[i] = null;
                    break;
                // An input that went unread leaves the memo as it was.
            }
        }
        return new A300MemoUpdate(Displayed(), shownNow);
    }

    private IReadOnlyList<A300Memo> Displayed() =>
        A300EwdMemos.All.Where((_, i) => _shown[i]).ToArray();
}

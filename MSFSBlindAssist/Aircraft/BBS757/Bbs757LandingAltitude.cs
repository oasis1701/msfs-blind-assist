using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Aircraft.BBS757;

/// <summary>
/// The 757's pressurization landing altitude, typed in the Pressurization panel: the text the pilot
/// types, the calculator write that sets it and the phrases that say it. The cockpit's knob moves it
/// in 10 ft steps, from -1070 to 14100 ft. <c>L:B757_OVHD_LDG_ELEV</c> holds the feet and
/// <c>L:B757_OVHD_LDG_ELEV_CHG</c> is the knob's step input: writing 1 adds 10 ft (measured
/// 2026-10-26). Pure.
/// </summary>
public static class Bbs757LandingAltitude
{
    /// <summary>The landing altitude in feet, the panel's read-back.</summary>
    public const string ValueVar = "B757_OVHD_LDG_ELEV";

    /// <summary>The knob's step input: a 1 written to it adds 10 ft to <see cref="ValueVar"/> and redraws the panel's digits.</summary>
    public const string StepVar = "B757_OVHD_LDG_ELEV_CHG";

    /// <summary>The panel's text field: a key containing "_SET" is what MainForm renders as a box that Enter applies.</summary>
    public const string SetKey = "BBS757_LDG_ALT_SET";

    /// <summary>The read-back row, <see cref="ValueVar"/> in feet, shown in the panel's status display.</summary>
    public const string DisplayKey = "BBS757_LDG_ALT";

    /// <summary>The lowest landing altitude the knob reaches, in feet.</summary>
    public const int MinFeet = -1070;

    /// <summary>The highest landing altitude the knob reaches, in feet.</summary>
    public const int MaxFeet = 14100;

    /// <summary>What one step of the knob changes, in feet.</summary>
    public const int StepFeet = 10;

    public const string EmptyMessage = "Type the landing altitude in feet first.";
    public const string InvalidMessage = "Landing altitude must be from -1070 to 14100 feet.";

    private const string UnavailablePhrase = "Landing altitude unavailable";
    private const string UnavailableDisplay = "unavailable";

    /// <summary>
    /// The landing altitude the pilot typed, in feet, on any Windows culture (<see cref="NumberParsing.TryParseTyped"/>,
    /// CORE-17): "1030" and "1030,0" read 1030. Rounded to the nearest 10 ft, a half away from zero
    /// (645 is 650, -65 is -70), and only then checked against <see cref="MinFeet"/> and
    /// <see cref="MaxFeet"/>: 14104 is 14100 and is taken, 14105 is 14110 and is not. An empty box says to
    /// type a value first; anything else that is refused gets the range sentence. "1.030" and "1,030"
    /// could be a thousands group, so they are refused rather than guessed. On a refusal
    /// <paramref name="feet"/> is 0.
    /// </summary>
    public static bool TryParseText(string? text, out int feet, out string error)
    {
        feet = 0;
        error = "";
        if (string.IsNullOrWhiteSpace(text)) { error = EmptyMessage; return false; }
        if (!NumberParsing.TryParseTyped(text, out double typed)) { error = InvalidMessage; return false; }

        double rounded = Math.Round(typed / StepFeet, MidpointRounding.AwayFromZero) * StepFeet;
        if (rounded < MinFeet || rounded > MaxFeet) { error = InvalidMessage; return false; }

        feet = (int)rounded;
        return true;
    }

    /// <summary>
    /// The calculator string that sets the landing altitude to <paramref name="feet"/>: the value
    /// ten feet short, then one step of the knob, as <c>"1020 (&gt;L:B757_OVHD_LDG_ELEV) 1
    /// (&gt;L:B757_OVHD_LDG_ELEV_CHG)"</c> for 1030 (measured: it reads 1030; <c>-70</c> reads -60). The
    /// value written alone holds, but leaves the panel's digits stale until the next knob step, so the
    /// step is what makes the panel show it. Invariant and fixed point (<see cref="Bbs757Rpn.SetLVar"/>):
    /// sv-SE's minus sign is U+2212, which the RPN parser rejects. Takes a value from
    /// <see cref="TryParseText"/>.
    /// </summary>
    public static string Write(int feet)
        => $"{Bbs757Rpn.SetLVar(ValueVar, feet - StepFeet)} {Bbs757Rpn.SetLVar(StepVar, 1)}";

    /// <summary>
    /// The landing altitude as it is spoken: "Landing altitude 1030 feet", "Landing altitude minus 60
    /// feet", "Landing altitude unavailable" when nothing finite was read. Whole feet, no thousands
    /// separator, whatever the PC's culture.
    /// </summary>
    public static string Phrase(double? feet)
    {
        if (feet is not double v || !double.IsFinite(v)) return UnavailablePhrase;
        double whole = Math.Round(v, MidpointRounding.AwayFromZero);
        return whole < 0
            ? $"Landing altitude minus {Whole(-whole)} feet"
            : $"Landing altitude {Whole(whole)} feet";
    }

    /// <summary>
    /// The landing altitude for the panel's status row: "1030 feet", "-60 feet" (an ASCII hyphen-minus),
    /// "unavailable" when the reading is not finite. Whole feet, no thousands separator, whatever the
    /// PC's culture.
    /// </summary>
    public static string Display(double feet)
    {
        if (!double.IsFinite(feet)) return UnavailableDisplay;
        double whole = Math.Round(feet, MidpointRounding.AwayFromZero);
        return $"{(whole < 0 ? "-" : "")}{Whole(Math.Abs(whole))} feet";
    }

    /// <summary>A non-negative whole number of feet, invariant, with no group separator; a rounded -0 is "0".</summary>
    private static string Whole(double feet)
        => (feet == 0 ? 0 : feet).ToString("0", CultureInfo.InvariantCulture);
}

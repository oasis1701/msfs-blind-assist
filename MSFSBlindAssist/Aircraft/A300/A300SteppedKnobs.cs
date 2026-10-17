using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// A knob a typed box turns ([A300-30]): the box's key, the map control whose B: event is one click
/// (<see cref="A300WritePlan.Set"/>), the readout that says where the knob is, how far one click moves it, how many
/// clicks one calculator string may carry, how many clicks its whole travel is (the most one round turns it, whatever the
/// readout says: <see cref="A300SteppedKnobs.ClicksFor"/>), and the words for the value reached or the value it stopped at.
/// </summary>
public sealed record A300SteppedKnob(string TypedKey, string ControlKey, string ReadoutKey, double Step, int ClicksPerSend,
    int MaxClicks, Func<double, string> Phrase, Func<double, string> StoppedPhrase);

/// <summary>
/// The two knobs a typed value turns instead of writing it ([A300-30]), and their clicks. Measured in cruise on 2026-10-14
/// (package 1.0.11):
/// <list type="bullet">
/// <item>Landing elevation (<c>L:INI_landing_elevation</c>): 50 feet a click; five clicks in one calculator string all
/// registered (450 to 700 and back to 450).</item>
/// <item>Flex temperature (<c>L:INI_FLEX_TEMPERATURE</c>): one degree a click, 0 to 72 ([A300-21]); four clicks in one
/// string moved it ONE degree, so each click is a send of its own; an identical string sent twice in a row was dropped,
/// which the unique send prevents ([VAR-4]). A direct write is undone: the FMS copies its own value there every update.</item>
/// </list>
/// Pure: the definition sends the strings and reads the knob back (IniA300Definition.KnobEntry). Every number is
/// formatted with the invariant culture.
/// </summary>
public static class A300SteppedKnobs
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The flex temperature knob, on the Thrust Rating Panel: 0 to 72 degrees, a degree a click, so 72 clicks end to
    /// end. Confirmed in the TRP's words (<see cref="A300Trp.FlexPhrase"/>, "Flex temperature 45 degrees").</summary>
    public static readonly A300SteppedKnob Flex = new(A300TypedValues.FlexTemperatureKey, A300Trp.FlexKnobKey,
        A300Readouts.FlexTemperatureKey, Step: 1, ClicksPerSend: 1, MaxClicks: 72,
        A300Trp.FlexPhrase, degrees => $"Flex temperature stopped at {Whole(degrees)} degrees");

    /// <summary>The landing elevation knob, on its own panel: -2000 to 14000 feet, 50 feet a click, so 320 clicks end to
    /// end (the range is a judgment, <see cref="A300TypedValues.KnobTarget"/>). Confirmed as its status line reads it, with no
    /// thousands separator ("Landing elevation 5450 feet").</summary>
    public static readonly A300SteppedKnob LandingElevation = new(A300TypedValues.LandingElevationKey, "A300_LANDING_ELEV_SET",
        "A300_RO_LANDING_ELEV", Step: 50, ClicksPerSend: 5, MaxClicks: 320,
        feet => $"Landing elevation {Whole(feet)} feet", feet => $"Landing elevation stopped at {Whole(feet)} feet");

    /// <summary>The knob a typed key turns, or null for a typed value that is written
    /// (<see cref="A300TypedValues.IsKnobTyped"/>).</summary>
    public static A300SteppedKnob? For(string typedKey) => typedKey switch
    {
        A300TypedValues.FlexTemperatureKey => Flex,
        A300TypedValues.LandingElevationKey => LandingElevation,
        _ => null,
    };

    /// <summary>
    /// The clicks from <paramref name="current"/> to the click nearest <paramref name="target"/>: positive turns it up, a
    /// half click away from zero, and never more than <paramref name="maxClicks"/> either way (the knob's whole travel,
    /// <see cref="A300SteppedKnob.MaxClicks"/>). A reading that is absurd would otherwise build a list of sends as long as the
    /// reading is, on the UI thread, and past about 1e10 clicks the integer cast saturates and <see cref="Sends"/> throws on
    /// <c>Math.Abs(int.MinValue)</c>: so the bound comes before the cast. The next round puts right what a bound left short.
    /// </summary>
    public static int ClicksFor(double current, double target, double step, int maxClicks) =>
        (int)Math.Clamp(Math.Round((target - current) / step, MidpointRounding.AwayFromZero), -maxClicks, maxClicks);

    /// <summary>The calculator strings that send <paramref name="clicks"/> clicks of the control's own event, each holding
    /// at most <paramref name="clicksPerSend"/> of them, space-separated (<c>1 (&gt;B:EVENT)</c> up, <c>-1</c> down).</summary>
    public static IReadOnlyList<string> Sends(A300Control control, int clicks, int clicksPerSend)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(clicksPerSend, 1);
        string click = A300WritePlan.Set(control, clicks > 0 ? 1 : -1).Rpn;
        var sends = new List<string>();
        for (int left = Math.Abs(clicks); left > 0; left -= clicksPerSend)
            sends.Add(string.Join(" ", Enumerable.Repeat(click, Math.Min(left, clicksPerSend))));
        return sends;
    }

    private static string Whole(double value)
    {
        string text = Math.Round(value).ToString("0", Inv);
        return text == "-0" ? "0" : text;
    }
}

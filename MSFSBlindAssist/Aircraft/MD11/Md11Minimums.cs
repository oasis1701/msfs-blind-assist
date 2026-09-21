using System.Globalization;

namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>One side's minimums: the panel field, the export read back, the inbox written, the mode switch and its silent mirror, where the field sits.</summary>
public sealed record Md11MinimumsSide(string Name, string SetKey, string ReadKey, string WriteVar, string ModeSwitch, string ModeKey, string AnchorKey, string PanelName);

/// <summary>
/// Typed minimums (DH/MDA) for the captain and the first officer.
///
/// MEASURED LIVE (2026-09-06), because TFDi document the inbox names and nothing else:
///   • <c>MD11_EXTCTL_CAP_MIN</c> / <c>FO_MIN</c> are one-shot command inboxes like the FCP's,
///     but idle at <b>-9999</b>, not -1.
///   • A write is consumed in either minimums mode and sets the <b>BARO</b> minimums only. With
///     the captain's mode on Radio, 500 was consumed and <c>MD11_CAP_MINIMUMS</c> stayed at the
///     200 ft radio value; switching the mode to Baro made it read 500. F/O identical (600).
///   • So the export shows whichever mode the switch selects, and the radio value cannot be set
///     through this inbox at all.
/// The read-back sentence therefore has to say which of those two things the pilot is hearing.
///
/// The field is MainForm's "_SET" text box plus Set button (the COM-standby convention), pre-
/// filled from the export so tabbing in reads the current value; the aircraft's own minimums
/// knob and mode switch stay on the panel beside it.
///
/// The mode word the rows speak ("200 feet, radio") comes from <see cref="Md11MinimumsSide.ModeKey"/>
/// — a silent, batch-covered MIRROR of the switch's own L:var (<see cref="Md11MinimumsSide.ModeSwitch"/>),
/// registered separately in <c>TFDiMD11Definition.Panels.BuildExportVariables</c>. The switch itself
/// stays <c>OnRequest</c>, because it is also an operable combo: batch-covering it directly (once
/// tried, reverted) leaves it with no individual data definition, and <c>Md11SelectorWalker</c> reads
/// <c>SimConnectManager.SupportsFreshReads</c> to choose its read protocol — false for a batch-covered
/// var with no individual def, which downgrades the walk to the legacy cache-poll protocol that can
/// call a real movement "did not move". Same shape as <c>MD11_CAP_MINIMUMS</c> sharing its Name with
/// the minimums cap's read-only row (<see cref="Md11ExportBacked"/>): two keys, one underlying
/// L:var, only one of them batched.
/// </summary>
public static class Md11Minimums
{
    public const int MinFeet = 1;
    public const int MaxFeet = 15000;   // a typo guard, not the aircraft's limit

    /// <summary>The inbox's idle value — how "no command pending" reads on these two vars.</summary>
    public const double IdleSentinel = -9999;

    /// <summary>
    /// Names the range rather than saying "first": MainForm hands the definition 0 for BOTH an
    /// empty box and a typed "0" — the box pre-fills from the export, which reads 0 when the
    /// aircraft has no minimums set — so "type it first" was wrong half the time, telling a pilot
    /// who had just typed something that they had typed nothing. The range is true either way.
    /// </summary>
    public const string EmptyMessage = "Type the minimums in feet, 1 to 15000.";
    public const string RangeMessage = "Minimums must be between 1 and 15000 feet.";
    public const string WholeFeetMessage = "Minimums must be whole feet.";

    public static readonly Md11MinimumsSide Captain = new(
        Name: "Captain", SetKey: "MD11_CAP_MINIMUMS_SET", ReadKey: "MD11_CAP_MINIMUMS",
        WriteVar: "MD11_EXTCTL_CAP_MIN", ModeSwitch: "MD11_LECP_MINIMUMS_KB",
        ModeKey: "MD11_CAP_MINIMUMS_MODE",
        AnchorKey: "MD11_LECP_MINIMUMS_CAP", PanelName: "EFIS Captain");

    public static readonly Md11MinimumsSide FirstOfficer = new(
        Name: "First Officer", SetKey: "MD11_FO_MINIMUMS_SET", ReadKey: "MD11_FO_MINIMUMS",
        WriteVar: "MD11_EXTCTL_FO_MIN", ModeSwitch: "MD11_RECP_MINIMUMS_KB",
        ModeKey: "MD11_FO_MINIMUMS_MODE",
        AnchorKey: "MD11_RECP_MINIMUMS_CAP", PanelName: "EFIS First Officer");

    public static readonly Md11MinimumsSide[] Sides = { Captain, FirstOfficer };

    public static bool TryGetSide(string key, out Md11MinimumsSide side)
    {
        foreach (var s in Sides)
            if (string.Equals(s.SetKey, key, StringComparison.Ordinal)) { side = s; return true; }
        side = Captain;
        return false;
    }

    /// <summary>The mirror key that carries what a side's export shows: radio (0) or baro (1).</summary>
    public static string ModeKeyFor(string readKey)
        => string.Equals(readKey, FirstOfficer.ReadKey, StringComparison.Ordinal) ? FirstOfficer.ModeKey : Captain.ModeKey;

    /// <summary>
    /// What the pilot typed, as MainForm hands it over (a double; an empty or unparseable box is 0).
    /// Whole feet, 1–15000; anything else is refused with a spoken reason and nothing is sent.
    /// </summary>
    public static bool TryParse(double typed, out int feet, out string error)
    {
        feet = 0; error = "";
        if (double.IsNaN(typed) || typed <= 0) { error = EmptyMessage; return false; }
        if (Math.Abs(typed - Math.Round(typed)) > 1e-9) { error = WholeFeetMessage; return false; }
        if (typed < MinFeet || typed > MaxFeet) { error = RangeMessage; return false; }
        feet = (int)Math.Round(typed);
        return true;
    }

    /// <summary>
    /// The read-back sentence. A matching export means the pilot hears the value they typed;
    /// with the mode on Radio the export cannot show the baro value at all, so say so rather than
    /// report a failure that did not happen.
    /// </summary>
    public static string Confirmation(Md11MinimumsSide side, int requested, double? readBack, bool? modeIsBaro)
    {
        string req = Feet(requested);
        if (readBack == null)
            return $"{side.Name} baro minimums set to {req} feet, the display did not report back.";
        bool matches = Math.Abs(readBack.Value - requested) < 0.5;
        if (matches)
            return $"{side.Name} minimums {req} feet.";
        string shown = Feet(readBack.Value);
        return modeIsBaro switch
        {
            // Not "did not change, still N feet": the read-back cannot say the display is
            // unchanged — the aircraft may have taken a DIFFERENT value than the one typed. It
            // reports what the display shows, in the squawk entry's words ("…did not take, the
            // transponder reads …"), so the two typed-entry read-backs read alike.
            true => $"{side.Name} minimums did not take, the display shows {shown} feet.",
            false => $"{side.Name} baro minimums set to {req} feet. Minimums mode is Radio, showing {shown} feet.",
            null => $"{side.Name} baro minimums set to {req} feet. The minimums display shows {shown} feet.",
        };
    }

    /// <summary>A status row's text: the feet, plus the mode the display is in when the switch has been read.</summary>
    public static string DescribeReading(double feet, double? mode)
    {
        string text = $"{Feet(feet)} feet";
        if (mode == null) return text;
        return mode > 0.5 ? $"{text}, baro" : $"{text}, radio";
    }

    private static string Feet(double v) => v.ToString("0", CultureInfo.InvariantCulture);
}

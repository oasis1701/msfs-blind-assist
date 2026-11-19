using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The rows of the Fenix E/WD window: one snapshot of the display channel turned into what the upper ECAM draws of its
/// own, worded for a screen reader. <see cref="Compose"/> is the only place that decides the order of the rows.
/// </summary>
/// <remarks>
/// <para>The rows run: the engine block, the thrust limit, the FOB line, the flap and slat text, then the upper ECAM's
/// left lines and its right lines, each top to bottom. A text with nothing to say gives no row. The engine block is one
/// row for each gauge, "&lt;label&gt;: &lt;engine 1&gt;, &lt;engine 2&gt;", in the order the screen shows the gauges, and it
/// follows the engine family: the CFM56 and the IAE V2500 draw different sets of gauges (measured), and the LEAP and the
/// GTF get the CFM56's unmeasured. The family is not in the channel, so the caller passes the one the config L:vars gave,
/// and an unread one gives a single status row in place of the block rather than a guessed order. The flap row is
/// labeled "Flaps: 1+F, green", a label the app adds since a bare number says little by ear.</para>
/// <para>The labels, units, decimals and the order of the blocks are what the E/WD drew on the installed build
/// 3.0.0.7613. N1, N2 and EPR are truncated to the decimals the display draws and EGT and the fuel flow are rounded, as
/// the display does; the fuel flow is the channel's number in the unit field 154 names. A value the display cannot draw
/// reads "XX", the crosses it draws in its place. Numbers are written with the invariant culture and an ASCII minus
/// whatever the PC's culture is, and a value that rounds to 0 never reads with a minus.</para>
/// <para>A text the channel sends is spoken through <see cref="FenixChannelSpeech"/>, with its colors. The gate already
/// untrusts the E/WD for a letter the app does not know in the FOB line or the upper ECAM, and the decoder checks the
/// thrust limit and the flap text; this checks them as well, so a window never shows a row with the letter dropped.</para>
/// </remarks>
internal static class FenixChannelEwdRows
{
    /// <summary>What the window is called, and what <see cref="FenixChannelMonitor.ReportFeatureProblem"/> logs it under.</summary>
    public const string DisplayName = "E/WD";

    // What the display draws for a number it cannot: its crosses.
    private const string Cross = "XX";

    // A value of this size or more is no engine value, and Number and Truncated read it as one the display cannot draw, so
    // a garbage 1e300 reads "XX" and not 301 digits. The bound is 1e15 because (decimal)double keeps only 15 significant
    // digits, so a larger value no longer holds the decimals asked for. It also leaves a wide margin for Truncated's
    // multiply by the scale: decimal holds about 7.9e28, against 1e15 times the 10 or 1000 the callers scale by.
    private const double DrawableBelow = 1e15;

    private const string EnginesUnread = "Engines: not available, engine type not read yet";

    private enum Gauge
    {
        N1,
        N2,
        FuelFlow,
        Epr,
        Egt
    }

    // The gauges in the order the screen shows them, top to bottom and left to right. The CFM56 draws N1 first (measured
    // on 3.0.0.7613); the LEAP and the GTF get the same block unmeasured, since no NEO exists on that build. The IAE V2500
    // draws EPR first (measured), with the fuel flow block left of the EGT gauge at that level and the N2 block left of
    // the N1 gauge at the next.
    private static readonly Gauge[] N1FirstBlock = [Gauge.N1, Gauge.Egt, Gauge.N2, Gauge.FuelFlow];

    private static readonly Gauge[] EprFirstBlock = [Gauge.Epr, Gauge.FuelFlow, Gauge.Egt, Gauge.N2, Gauge.N1];

    // ---- The window ---------------------------------------------------------------------------

    /// <summary>
    /// The window's rows for <paramref name="s"/>, or the one status row when the E/WD cannot be read: the problem,
    /// which also goes to <paramref name="reportProblem"/>. No message or no frame is "no data"; a message the decoder
    /// cannot read gives the decoder's problem; a letter the app does not know in the FOB line or the upper ECAM gives
    /// "unknown code 'x' in the FOB line" or "... in the upper ECAM".
    /// </summary>
    public static FenixChannelRows Compose(
        FenixChannelSnapshot s, Aircraft.FenixEngineFamily family, Action<string>? reportProblem = null)
    {
        if (s.Push is not { } push || s.Frame is not { } frame) return Stop("no data");

        FenixChannelSection<FenixEwdData> section = FenixChannelEwdData.Decode(push);
        if (!section.IsOk) return Stop(section.Problem ?? "no data");

        if (UnknownCodeProblem(frame) is { } unknown) return Stop(unknown);

        FenixEwdData ewd = section.Value!;
        var rows = new List<string>(EngineRows(ewd, family));

        Add(rows, FenixChannelSpeech.Line(ewd.ThrustLimit));
        Add(rows, FenixChannelSpeech.Line(frame.FobText));
        Add(rows, Labeled("Flaps", FenixChannelSpeech.Line(ewd.FlapText)));

        foreach (string line in frame.UpperEcamLeft)
        {
            Add(rows, FenixChannelSpeech.Line(line));
        }

        foreach (string line in frame.UpperEcamRight)
        {
            Add(rows, FenixChannelSpeech.Line(line));
        }

        return FenixChannelRows.Of(rows);

        FenixChannelRows Stop(string problem)
        {
            reportProblem?.Invoke(problem);
            return FenixChannelRows.NotAvailable(problem);
        }
    }

    // ---- The engines --------------------------------------------------------------------------

    private static IReadOnlyList<string> EngineRows(FenixEwdData ewd, Aircraft.FenixEngineFamily family)
    {
        Gauge[]? block = family switch
        {
            Aircraft.FenixEngineFamily.Cfm56 or Aircraft.FenixEngineFamily.CfmLeap or Aircraft.FenixEngineFamily.PwGtf
                => N1FirstBlock,
            Aircraft.FenixEngineFamily.IaeV2500 => EprFirstBlock,
            _ => null
        };

        return block is null ? [EnginesUnread] : block.Select(gauge => GaugeRow(gauge, ewd)).ToList();
    }

    private static string GaugeRow(Gauge gauge, FenixEwdData ewd) => gauge switch
    {
        Gauge.N1 => Pair("N1 %", ewd, engine => Truncated(engine.N1, 1)),
        Gauge.N2 => Pair("N2 %", ewd, engine => Truncated(engine.N2, 1)),
        Gauge.Epr => Pair("EPR", ewd, engine => Truncated(engine.Epr, 3)),
        Gauge.Egt => Pair("EGT °C", ewd, engine => Number(engine.Egt, 0)),
        Gauge.FuelFlow => Pair(ewd.IsKg ? "FF KG/H" : "FF LBS/H", ewd, engine => Number(engine.FuelFlow, 0)),
        _ => throw new ArgumentOutOfRangeException(nameof(gauge), gauge, "no such gauge")
    };

    // Engine 1 is the left engine (129.1) and engine 2 the right (129.2).
    private static string Pair(string label, FenixEwdData ewd, Func<FenixEwdEngine, string> shown)
        => $"{label}: {shown(ewd.Left)}, {shown(ewd.Right)}";

    // ---- Numbers ------------------------------------------------------------------------------

    /// <summary>
    /// A number rounded away from zero to <paramref name="decimals"/> places (0 to 15), written with the invariant
    /// culture and never as "-0": "377", "-49". "XX" for NaN, infinity and a value no engine has, which the display
    /// cannot draw.
    /// </summary>
    internal static string Number(double value, int decimals)
    {
        if (!double.IsFinite(value) || Math.Abs(value) >= DrawableBelow) return Cross;

        double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0.0 : rounded).ToString(FixedPoint(decimals), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A number truncated toward zero to <paramref name="decimals"/> places, written like <see cref="Number"/>: "1.407",
    /// "81.2", never "-0.0". The display truncates N1, N2 and EPR rather than rounding them (measured on 3.0.0.7613: the
    /// channel's 21.567 is drawn 21.5 and its EPR 1.00978 is drawn 1.009). "XX" for NaN, infinity and a value no
    /// engine has.
    /// </summary>
    internal static string Truncated(double value, int decimals)
    {
        if (!double.IsFinite(value) || Math.Abs(value) >= DrawableBelow) return Cross;

        // In decimal, never in double: a double cannot hold 1.407 exactly, so scaling it gives 1406.99999..., and the
        // display must read "1.407" for it, never "1.406". The conversion to decimal keeps the 15 digits a double has.
        decimal scale = 1m;
        for (int place = 0; place < decimals; place++)
        {
            scale *= 10m;
        }

        decimal truncated = Math.Truncate((decimal)value * scale) / scale;
        return (truncated == 0 ? 0m : truncated).ToString(FixedPoint(decimals), CultureInfo.InvariantCulture);
    }

    private static string FixedPoint(int decimals) => "F" + decimals.ToString(CultureInfo.InvariantCulture);

    // ---- Helpers ------------------------------------------------------------------------------

    private static void Add(List<string> rows, string? row)
    {
        if (row is not null) rows.Add(row);
    }

    // A row with the label the display does not draw: "Flaps: 1+F, green". Null stays null, so a blank text gives no row.
    private static string? Labeled(string label, string? row) => row is null ? null : $"{label}: {row}";

    // The problem of the first letter the app does not know in the FOB line and then in the upper ECAM, the left lines
    // and then the right ones; null when it knows them all. The thrust limit and the flap text are the decoder's.
    private static string? UnknownCodeProblem(FenixChannelFrame frame)
    {
        if (UnknownIn(frame.FobText) is char fobLetter)
        {
            return FenixChannelSection<FenixEwdData>.UnknownCode(fobLetter, "FOB line").Problem;
        }

        foreach (string line in frame.UpperEcamLeft.Concat(frame.UpperEcamRight))
        {
            if (UnknownIn(line) is char ecamLetter)
            {
                return FenixChannelSection<FenixEwdData>.UnknownCode(ecamLetter, "upper ECAM").Problem;
            }
        }

        return null;
    }

    private static char? UnknownIn(string text)
    {
        FenixTextLine line = FenixChannelText.Parse(text);
        return line.UnknownCodes.Count > 0 ? line.UnknownCodes[0] : null;
    }
}

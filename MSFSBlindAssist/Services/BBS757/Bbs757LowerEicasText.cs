using System.Globalization;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The lower EICAS's rows as BlueBird draws them (R17, R18): its label words and number formats live here, never in the
/// layout. Source: BlueBird's draw code, read from the module on 2026-11-09 (docs/b757.md, "Lower EICAS (Alt+S)"). Numbers are always culture-invariant; "whole" is
/// BlueBird's <c>to_wstring(int)</c> of a value truncated toward zero, never rounded; an invalid engine's cell reads
/// "blank"; runs of spaces in BlueBird's words collapse to one; the left engine is first.
/// </summary>
public static class Bbs757LowerEicasText
{
    private const string Blank = "blank";
    private const double KgPerLb = 2.205;

    /// <summary>The page's word by kind: "engine", "STATUS", "engine off", "blank" (any other value).</summary>
    public static string PageWord(Bbs757LowerEicasLayout layout, int value) =>
        value == layout.EnginePage ? "engine"
        : value == layout.StatusPage ? "STATUS"
        : value == layout.EngineOff ? "engine off"
        : "blank";

    /// <summary>The rows of the page shown, in the draw's order; none for a blank page. A page whose layout part is
    /// missing is the one row "&lt;word&gt; page: not read: &lt;reason&gt;".</summary>
    public static IReadOnlyList<string> PageRows(Bbs757LowerEicasLayout layout, Bbs757LowerEicasRaw raw)
    {
        int v = raw.PageValue;
        if (v == layout.EnginePage || v == layout.EngineOff)
        {
            if (layout.Engine is null || raw.Engines is null || raw.Engines.Count < 2)
                return new[] { "engine page: not read: " + (layout.EngineMissing ?? "no engine page in this layout") };
            return v == layout.EnginePage ? EnginePageRows(raw) : EngineOffRows(raw);
        }
        if (v == layout.StatusPage)
        {
            if (layout.Status is null || raw.Status is null)
                return new[] { "STATUS page: not read: " + (layout.StatusMissing ?? "no STATUS page in this layout") };
            return StatusPageRows(raw);
        }
        return Array.Empty<string>();
    }

    // ---- spoken lines, window rows (plan 25, Task 5) -----------------------------------------------------------

    /// <summary>The prefix of a page change: "Lower EICAS: engine page".</summary>
    public const string ChangePrefix = "Lower EICAS: ";

    private const string StatusWord = "STATUS";

    private static string KindOf(Bbs757LowerEicasReading r) => r.Page ?? Blank;

    /// <summary>The page in words (R9): "engine page", "STATUS page", "engine page off", "blank".</summary>
    public static string PagePhrase(string? pageWord) => pageWord switch
    {
        "engine" => "engine page",
        "STATUS" => "STATUS page",
        "engine off" => "engine page off",
        _ => Blank,
    };

    /// <summary>What changed from <paramref name="heard"/> to <paramref name="now"/>, in speaking order: the page change
    /// (S11: with <see cref="ChangePrefix"/> unless <paramref name="withPrefix"/> is false), then "Status: A, B" for the
    /// messages new, then "Status gone: A, B" for those gone, in list order, compared by text, ordinal, and only when both
    /// readings show the STATUS page: the messages present when STATUS opens are its content, and a message that comes or
    /// goes under another page is never spoken.</summary>
    public static IReadOnlyList<string> Changes(Bbs757LowerEicasReading heard, Bbs757LowerEicasReading now, bool withPrefix)
    {
        var lines = new List<string>();
        if (!string.Equals(KindOf(heard), KindOf(now), StringComparison.Ordinal))
            lines.Add((withPrefix ? ChangePrefix : "") + PagePhrase(now.Page));
        if (KindOf(heard) == StatusWord && KindOf(now) == StatusWord)
        {
            var added = now.StatusMessages.Where(m => !heard.StatusMessages.Contains(m, StringComparer.Ordinal)).ToList();
            var gone = heard.StatusMessages.Where(m => !now.StatusMessages.Contains(m, StringComparer.Ordinal)).ToList();
            if (added.Count > 0) lines.Add("Status: " + string.Join(", ", added));
            if (gone.Count > 0) lines.Add("Status gone: " + string.Join(", ", gone));
        }
        return lines;
    }

    /// <summary>The window's rows: "Page: STATUS" and the page's rows, or the one row "Lower EICAS: blank".</summary>
    public static IReadOnlyList<string> Rows(Bbs757LowerEicasReading reading)
    {
        if (KindOf(reading) == Blank) return new[] { ChangePrefix + Blank };
        var rows = new List<string> { "Page: " + reading.Page };
        rows.AddRange(reading.PageRows);
        return rows;
    }

    // ---- engine page -----------------------------------------------------------------------------------------

    private static List<string> EnginePageRows(Bbs757LowerEicasRaw raw)
    {
        var e = raw.Engines!;
        var rows = new List<string>();
        AddN(rows, raw, "N2", x => x.N2, x => x.MaxN2);
        if (raw.Rr) AddN(rows, raw, "N3", x => x.N3, x => x.MaxN3);
        rows.Add("FF: " + Pair(e, x => FuelFlow(raw, x, status: false)));
        rows.Add("OIL PRESS: " + Pair(e, x => x.Valid ? Whole(x.OilPressure) : Blank));
        if (raw.Messages.Count > 0) rows.Add("STATUS");
        rows.Add("OIL TEMP: " + Pair(e, x => x.Valid && x.N2 >= 5 ? OilTemperature(x.OilTemperature) : Blank));
        rows.Add("OIL QTY: " + Pair(e, x => x.Valid ? Whole(Math.Clamp(x.OilQuantity * 21, 0, 21)) : Blank));
        rows.Add("VIB: " + Pair(e, x => x.Valid ? Math.Abs(x.Vibration).ToString("F1", CultureInfo.InvariantCulture) : Blank));
        return rows;
    }

    /// <summary>The engine page off: the FF dials always, and the N2 (and RR N3) dials with their max boxes only while the
    /// exceedance latch is set.</summary>
    private static List<string> EngineOffRows(Bbs757LowerEicasRaw raw)
    {
        var rows = new List<string>();
        if (raw.LatchSet)
        {
            AddN(rows, raw, "N2", x => x.N2, x => x.MaxN2);
            if (raw.Rr) AddN(rows, raw, "N3", x => x.N3, x => x.MaxN3);
        }
        rows.Add("FF: " + Pair(raw.Engines!, x => FuelFlow(raw, x, status: false)));
        return rows;
    }

    private static void AddN(List<string> rows, Bbs757LowerEicasRaw raw, string label,
        Func<Bbs757LowerEicasEngineRaw, double> value, Func<Bbs757LowerEicasEngineRaw, double> max)
    {
        var e = raw.Engines!;
        rows.Add($"{label}: " + Pair(e, x => x.Valid ? WholeF32(value(x)) : Blank));
        // The max box is drawn only while it is not 0: the row exists while either is.
        if (e.Any(x => max(x) != 0))
            rows.Add($"{label} max: " + Pair(e, x => x.Valid && max(x) != 0 ? WholeF32(max(x)) : Blank));
    }

    /// <summary>23017: at 0 and above whole; between -10 and 0 "- " then the absolute whole value; at -10 and below
    /// the signed whole value.</summary>
    internal static string OilTemperature(double t) =>
        t > -10 && t < 0 ? "- " + Whole(Math.Abs(t)) : Whole(t);

    /// <summary>Fuel flow in thousands, one decimal; pounds while the settings' flag is set, else kilograms (÷ 2.205).
    /// While starting (R16): the engine page holds 600 lb/h, ramping to 1200 with the HP spool between 25 and 60 %; the
    /// STATUS page shows 0 once past the hold and 600 during it. Blank for an invalid engine.</summary>
    internal static string FuelFlow(Bbs757LowerEicasRaw raw, Bbs757LowerEicasEngineRaw e, bool status)
    {
        if (!e.Valid) return Blank;
        double lbPerHour;
        if (status)
            lbPerHour = e.Hold ? 600 : e.Starting ? 0 : e.FuelFlow;
        else if (e.Starting)
            lbPerHour = e.Hold ? 600 : 600 + 600 * Math.Clamp((e.HpSpool - 25) / 35, 0, 1);
        else
            lbPerHour = e.FuelFlow;
        double shown = raw.Pounds ? lbPerHour : lbPerHour / KgPerLb;
        return (shown / 1000).ToString("F1", CultureInfo.InvariantCulture);
    }

    // ---- STATUS page -----------------------------------------------------------------------------------------

    private static List<string> StatusPageRows(Bbs757LowerEicasRaw raw)
    {
        var s = raw.Status!;
        var rows = new List<string>();
        string[] names = { "L", "C", "R" };
        rows.Add("HYD QTY: " + string.Join(", ", Enumerable.Range(0, 3).Select(i => $"{names[i]} {HydraulicQuantity(s.HydraulicQuantity[i], s.HydraulicCapacity[i])}")));
        rows.Add("HYD PRESS: " + string.Join(", ", Enumerable.Range(0, 3).Select(i => $"{names[i]} {Whole(s.HydraulicPressure[i])}")));
        rows.Add("APU EGT: " + Whole(s.ApuEgt));
        rows.Add("RPM: " + Whole(s.ApuRpm));
        rows.Add("OIL Q: " + ApuOil(s.ApuOilQuantity));
        if (s.OxygenShown) rows.Add("OXY PRESS: " + Whole(OxygenPressure(s)));
        rows.Add("RUD: " + Rudder(s.Rudder));
        rows.Add("AIL left: " + Vertical(s.AileronLeft));
        // The update stores the four values already as drawn (-elevator with its gain, -right aileron): the draw uses them as stored.
        rows.Add("ELEV: " + Vertical(s.Elevator));
        rows.Add("AIL right: " + Vertical(s.AileronRight));
        rows.Add("FF: " + (raw.Engines is { Count: >= 2 } e ? Pair(e, x => FuelFlow(raw, x, status: true)) : "not read"));
        foreach (string m in raw.Messages) rows.Add(m);
        rows.Add("BRAKE TEMP: " + string.Join(", ", s.BrakeTemperatures.Select((d, i) => $"{i + 1}: {d.ToString(CultureInfo.InvariantCulture)}")));
        return rows;
    }

    /// <summary>23019/23020: the percentage × 0.01 with two decimals, then " RF" below 75 % and " OF" above 122 %.</summary>
    internal static string HydraulicQuantity(double quantity, double capacity)
    {
        if (!(capacity > 0)) return Blank;
        double pct = quantity * 100 / capacity;
        string number = (pct * 0.01).ToString("F2", CultureInfo.InvariantCulture);
        return pct > 122 ? number + " OF" : pct < 75 ? number + " RF" : number;
    }

    /// <summary>APU oil quantity rounded to quarters, clamped 0.25..1: "FULL" at 0.99 and above, else two decimals.</summary>
    internal static string ApuOil(double q)
    {
        double quarters = Math.Clamp(Math.Round(q * 4, MidpointRounding.AwayFromZero) / 4, 0.25, 1);
        return quarters >= 0.99 ? "FULL" : quarters.ToString("F2", CultureInfo.InvariantCulture);
    }

    /// <summary>Getter 35596: 0 while the active byte is clear; the capacity while the full flag is 1 or more; else the
    /// level clamped to 0..capacity and rounded to tens.</summary>
    internal static double OxygenPressure(Bbs757LowerEicasStatusRaw s)
    {
        if (!s.OxygenActive) return 0;
        if (s.OxygenFull >= 1) return s.OxygenCapacity;
        double clamped = Math.Clamp(s.OxygenLevel, 0, Math.Max(0, s.OxygenCapacity));
        return Math.Round(clamped / 10, MidpointRounding.AwayFromZero) * 10;
    }

    private static int Percent(double v) =>
        (int)Math.Round(Math.Abs(Math.Clamp(v, -1, 1)) * 100, MidpointRounding.AwayFromZero);

    /// <summary>R15: a positive drawn value is right on the rudder scale.</summary>
    internal static string Rudder(double v)
    {
        int p = Percent(v);
        return p == 0 ? "centered" : $"{p} % {(v > 0 ? "right" : "left")}";
    }

    /// <summary>R15: a positive drawn value is down on a vertical scale.</summary>
    internal static string Vertical(double v)
    {
        int p = Percent(v);
        return p == 0 ? "centered" : $"{p} % {(v > 0 ? "down" : "up")}";
    }

    // ---- shared ----------------------------------------------------------------------------------------------

    private static string Pair(IReadOnlyList<Bbs757LowerEicasEngineRaw> engines, Func<Bbs757LowerEicasEngineRaw, string> cell) =>
        cell(engines[0]) + ", " + cell(engines[1]);

    /// <summary>The N2/N3 digits and their max boxes: BlueBird demotes to f32 (<c>f32.demote_f64</c>) before truncating, so a
    /// value within about 4e-6 below a whole number reads that number. No other row demotes.</summary>
    internal static string WholeF32(double v) => Whole((float)v);

    /// <summary>BlueBird's whole number: truncated toward zero, invariant.</summary>
    internal static string Whole(double v) => ((long)Math.Truncate(v)).ToString(CultureInfo.InvariantCulture);
}

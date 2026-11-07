using System.Globalization;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>
/// The Engine displays window's lists: a display unit's page named first, then that page's rows in PMDG's
/// draw order (<see cref="Pmdg737EngineDisplayLayout.PageItems"/>) with PMDG's labels and printed format:
/// whole numbers rounded as PMDG prints them (x + 0.5, floored), N1, N2 and vibration to one decimal, fuel
/// flow and fuel in thousands (tanks rounded to 10 first, as PMDG's tank gauge does), kilograms when PMDG's
/// option says so. Both engines share a row, engine 1 first; a colour that is not normal and a gauge
/// message follow their engine's value. A page the reader does not read is only named. Before PMDG's
/// aircraft initialises the displays are dark (the objects hold constructor values): the list is "off".
/// Invariant culture throughout (VAR-5). Pure.
/// </summary>
public static class Pmdg737EngineDisplayPages
{
    private const double PoundsToKilograms = 0.453592;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The page a unit's page value names; <see cref="DisplayPage.Unknown"/> for a value outside the derived map.</summary>
    public static DisplayPage PageOf(int value, Pmdg737EngineDisplayLayout l) =>
        l.Pages.Pages.TryGetValue(value, out var page) ? page : DisplayPage.Unknown;

    /// <summary>
    /// Whether the displays show anything at all: PMDG's aircraft has initialised and the upper or lower
    /// unit shows a page other than off or blank. PMDG's logic runs with the displays dark (live, cold and
    /// dark: every unit on page 0, A/T LIM set because the FMC is unpowered, the empty tanks LOW), so
    /// nothing is said while they are dark, and their lighting up is never news.
    /// </summary>
    public static bool DisplaysLit(EngineDisplaySample s, Pmdg737EngineDisplayLayout l) =>
        s.Initialised && (Lit(PageOf(s.UpperPage, l)) || Lit(PageOf(s.LowerPage, l)));

    private static bool Lit(DisplayPage page) => page is not (DisplayPage.Off or DisplayPage.Blank);

    /// <summary>
    /// The thrust mode as PMDG prints it: nothing for a mode at or above the limit, "MAN" when N1 SET is not
    /// AUTO, "---" for no mode, else the word PMDG chooses for the mode by its derate option and the assumed
    /// temperature; nothing for any other value (PMDG would read outside its table).
    /// </summary>
    public static string ThrustModeText(EngineDisplaySample s, Pmdg737EngineDisplayLayout l)
    {
        var rules = l.Gauges.ThrustRules;
        int mode = s.ThrustMode;
        if (mode >= rules.ModeLimit) return "";
        if (s.N1SetSelector != rules.N1SetAuto) return "MAN";
        if (mode == rules.NoMode) return "---";
        if (mode < 0) return "";
        int word = rules.Word(mode, s.DerateOption, s.AssumedTempShown);
        return word < l.Gauges.ThrustWords.Count ? l.Gauges.ThrustWords[word] : "";
    }

    /// <summary>The list for one display unit: "<paramref name="unitName"/>: page name", then the page's rows.</summary>
    public static IReadOnlyList<string> Lines(string unitName, int pageValue, EngineDisplaySample s, Pmdg737EngineDisplayLayout l)
    {
        if (!s.Initialised) return new[] { unitName + ": off" };
        var page = PageOf(pageValue, l);
        var lines = new List<string> { $"{unitName}: {PageName(page, pageValue)}" };
        if (l.PageItems.TryGetValue(page, out var items))
        {
            foreach (var item in items)
                lines.AddRange(Rows(item, s, l));
        }
        return lines;
    }

    private static string PageName(DisplayPage page, int value) => page switch
    {
        DisplayPage.Off => "off",
        DisplayPage.Blank => "blank",
        DisplayPage.Pfd => "PFD",
        DisplayPage.Nd => "navigation display",
        DisplayPage.Map => "map",
        DisplayPage.EnginePrimary => "primary engine page",
        DisplayPage.EngineSecondary => "secondary engine page",
        DisplayPage.EngineCompact => "compact engine page",
        DisplayPage.EngineSideBySide => "side by side engine page",
        DisplayPage.Sys => "SYS page",
        _ => "page " + value.ToString(Inv),
    };

    private static IEnumerable<string> Rows(PageItem item, EngineDisplaySample s, Pmdg737EngineDisplayLayout l)
    {
        switch (item)
        {
            case PageItem.Tat:
                yield return $"TAT {Signed(Whole(s.Tat))}C";
                break;
            case PageItem.ThrustMode:
                if (ThrustRow(s, l) is { } thrust) yield return thrust;
                break;
            case PageItem.N1:
                yield return "N1: " + Engines(s, (e, i) => OneDecimal(e.N1) + Colour(Gauge.N1, s, i)
                    + (e.ReverserShown ? (e.ReverserSecond ? " REV green" : " REV amber") : "")
                    + (e.TaiOn || e.TaiAmber ? (e.TaiAmber ? " TAI amber" : " TAI") : ""))
                    + (s.N1SetSelector != l.Gauges.ThrustRules.N1SetAuto ? ". Reference " + Engines(s, (e, _) => OneDecimal(e.N1Reference)) : "");
                break;
            case PageItem.Egt:
                yield return "EGT: " + Engines(s, (e, i) => Whole(e.Egt).ToString(Inv) + Colour(Gauge.Egt, s, i) + (e.EngineFail ? " ENG FAIL" : ""));
                break;
            case PageItem.N2:
                yield return "N2: " + Engines(s, (e, i) => OneDecimal(e.N2) + Colour(Gauge.N2, s, i) + (e.CrossBleedStart ? " X-BLD START" : ""));
                break;
            case PageItem.FuelFlow:
                yield return s.ShowFuelUsed
                    ? "FUEL USED: " + Engines(s, (e, _) => Thousands(e.FuelUsed, s))
                    : "FF: " + Engines(s, (e, _) => Thousands(e.FuelFlow, s));
                break;
            case PageItem.OilPressure:
                yield return "OIL PRESS: " + Engines(s, (e, i) => Whole(e.OilPressure).ToString(Inv) + Colour(Gauge.OilPressure, s, i));
                break;
            case PageItem.OilTemperature:
                yield return "OIL TEMP: " + Engines(s, (e, i) => Whole(e.OilTemperature).ToString(Inv) + Colour(Gauge.OilTemperature, s, i));
                break;
            case PageItem.OilQuantity:
                yield return (s.OilUnit == OilQuantityUnit.Percent ? "OIL QTY %: " : "OIL QTY: ")
                    + Engines(s, (e, _) => Whole(e.OilQuantity * s.OilMultiplier).ToString(Inv)
                        + (s.OilLowIndication && e.OilQuantity < s.OilQuantityLow ? " LO" : ""));
                break;
            case PageItem.Vibration:
                yield return "VIB: " + Engines(s, (e, i) => e.Vibration > l.Gauges.VibrationShownAbove
                    ? OneDecimal(e.Vibration) + Colour(Gauge.Vibration, s, i) : Blank);
                break;
            case PageItem.Alerts:
                var alerts = Alerts(s, l);
                yield return "Alerts: " + (alerts.Count == 0 ? "none" : string.Join(", ", alerts));
                break;
            case PageItem.Fuel:
                yield return FuelRow(s, l);
                break;
            case PageItem.HydraulicPressure:
                yield return $"HYD PRESS: {Pressure(s.Sys.HydPressureA, l)}, {Pressure(s.Sys.HydPressureB, l)}";
                break;
            case PageItem.HydraulicQuantity:
                yield return $"HYD QTY %: {Whole(s.Sys.HydQuantityA).ToString(Inv)}, {Whole(s.Sys.HydQuantityB).ToString(Inv)}";
                break;
            case PageItem.SysHydraulics:
                yield return "HYDRAULIC: A, B";
                yield return $"QTY %: {Whole(s.Sys.HydQuantityA).ToString(Inv)}{(s.Sys.RefillA ? " RF" : "")}, "
                    + $"{Whole(s.Sys.HydQuantityB).ToString(Inv)}{(s.Sys.RefillB ? " RF" : "")}";
                yield return $"PRESS: {Pressure(s.Sys.HydPressureA, l)}, {Pressure(s.Sys.HydPressureB, l)}";
                break;
            case PageItem.SysFlightControls:
                var sys = s.Sys;
                yield return string.Create(Inv, $"FLIGHT CONTROLS: AIL {Whole(sys.AileronLeft)}, FLT SPLR {Whole(sys.SpoilerLeft)}, ELEV {Whole(sys.Elevator)}, ")
                    + string.Create(Inv, $"FLT SPLR {Whole(sys.SpoilerRight)}, AIL {Whole(sys.AileronRight)}, RUDDER {Whole(sys.Rudder)}");
                break;
            case PageItem.SysBrakeTemperature:
                yield return "BRAKE TEMP: " + string.Join(", ", s.Sys.BrakeTemperatures.Select((t, k) =>
                    t.ToString("0.0", Inv) + (k < s.Sys.BrakeStatus.Count && s.Sys.BrakeStatus[k] != 0 ? " hot" : "")));
                break;
        }
    }

    /// <summary>The thrust word, the assumed temperature beside it when PMDG draws one, and A/T LIM; null when PMDG draws none of them.</summary>
    private static string? ThrustRow(EngineDisplaySample s, Pmdg737EngineDisplayLayout l)
    {
        var parts = new List<string>();
        if (ThrustModeText(s, l) is { Length: > 0 } word) parts.Add(word);
        if (s.AssumedTempShown) parts.Add(Signed(s.AssumedTemperature) + "C");
        string row = string.Join(" ", parts);
        if (s.AutothrottleLimit) row = row.Length > 0 ? row + ", A/T LIM" : "A/T LIM";
        return row.Length > 0 ? row : null;
    }

    /// <summary>
    /// The alert column, as the display draws it: each engine's crew alerts (shown while their state is not
    /// off), then the autoland advisory, A/T LIM, and the ATC and COMM memos.
    /// </summary>
    public static IReadOnlyList<string> Alerts(EngineDisplaySample s, Pmdg737EngineDisplayLayout l)
    {
        var a = l.Alerts;
        var alerts = new List<string>();
        for (int i = 0; i < s.Engines.Count; i++)
        {
            var e = s.Engines[i];
            string engine = "engine " + (i + 1).ToString(Inv) + " ";
            if (e.StartValve != 0) alerts.Add(engine + "START VALVE OPEN");
            if (e.OilFilterBypass != 0) alerts.Add(engine + "OIL FILTER BYPASS");
            if (e.LowOilPressure != 0) alerts.Add(engine + "LOW OIL PRESSURE");
        }
        if (s.AutolandAdvisory == a.NoLand3Value) alerts.Add("NO LAND 3");
        else if (s.AutolandAdvisory == a.NoAutolandValue) alerts.Add("NO AUTOLAND");
        if (s.AutothrottleLimit) alerts.Add("A/T LIM");
        if (s.AtcMemo) alerts.Add("ATC");
        if (s.CommMemo) alerts.Add("COMM");
        return alerts;
    }

    private static string FuelRow(EngineDisplaySample s, Pmdg737EngineDisplayLayout l)
    {
        string Tank(int k) => Tens(s.FuelQuantity[k], s) + Warning(s.FuelWarning[k], l.Alerts);
        double total = s.FuelQuantity.Sum() * (s.FuelInKg ? PoundsToKilograms : 1) / 1000;
        return $"FUEL {(s.FuelInKg ? "KG" : "LB")}: 1: {Tank(1)}, CTR: {Tank(0)}, 2: {Tank(2)}, TOTAL: {total.ToString("0.0", Inv)}";
    }

    private static string Warning(int value, AlertFields a) =>
        value == a.FuelLowValue ? " LOW" : value == a.FuelImbalanceValue ? " IMBAL" : value == a.FuelConfigValue ? " CONFIG" : "";

    /// <summary>A tank in thousands: rounded to 10 (pounds or kilograms) first, as PMDG's tank gauge prints it.</summary>
    private static string Tens(double pounds, EngineDisplaySample s)
    {
        double quantity = pounds * (s.FuelInKg ? PoundsToKilograms : 1);
        return (Math.Floor(quantity / 10 + 0.5) * 10 / 1000).ToString("0.00", Inv);
    }

    /// <summary>A flow or a used quantity in thousands, to two decimals ("%3.2f" of the float PMDG prints).</summary>
    private static string Thousands(double pounds, EngineDisplaySample s) =>
        ((double)(float)(pounds * (s.FuelInKg ? PoundsToKilograms : 1) / 1000)).ToString("0.00", Inv);

    /// <summary>What the list says where PMDG prints nothing: a readout holding PMDG's no-value marker.</summary>
    private const string Blank = "blank";

    /// <summary>A hydraulic pressure as PMDG's SYS page prints it: its rounded value, only above PMDG's no-value marker.</summary>
    private static string Pressure(double psi, Pmdg737EngineDisplayLayout l) =>
        Whole(psi) > l.Sys.HydPressureShownAbove ? Whole(psi).ToString(Inv) : Blank;

    private static string Engines(EngineDisplaySample s, Func<EngineValues, int, string> one) =>
        string.Join(", ", s.Engines.Select((e, i) => one(e, i)));

    private static string Colour(Gauge gauge, EngineDisplaySample s, int engine) =>
        Pmdg737EngineDisplayColors.ColorOf(gauge, s, engine) is var c && c != GaugeColor.Normal ? " " + Pmdg737EngineDisplayColors.Word(c) : "";

    /// <summary>"%d" of PMDG's floor(x + 0.5).</summary>
    private static long Whole(double value) => (long)Math.Floor(value + 0.5);

    /// <summary>"%1.1f" of the float PMDG prints.</summary>
    private static string OneDecimal(double value) => ((double)(float)value).ToString("0.0", Inv);

    /// <summary>"%+3d", trimmed.</summary>
    private static string Signed(long value) => value.ToString("+0;-0;+0", Inv);
}

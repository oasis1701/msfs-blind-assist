// The Engine displays window's lists: each display unit's page named first, then the page's rows in
// PMDG's draw order with its labels and printed format (fuel and fuel flow in thousands, PMDG's own
// rounding), both engines on one row, colours and gauge messages after their engine. Before PMDG's
// aircraft initialises the displays are dark and the objects hold constructor values: both lists say off.

using MSFSBlindAssist.Services.PMDG;
using Xunit;
using static MSFSBlindAssist.Services.PMDG.Pmdg737EngineDisplayPages;

namespace MSFSBlindAssist.Tests;

public class Pmdg737EngineDisplayPagesTests
{
    private static readonly Pmdg737EngineDisplayLayout Layout = GoldenLayouts.EngineDisplays;

    private static EngineDisplaySample Idle => EngineDisplaySampleFactory.Idle();

    private static EngineValues Engine(int n) => EngineDisplaySampleFactory.IdleEngine(n);

    private static EngineDisplaySample WithEngines(EngineDisplaySample s, EngineValues e1, EngineValues e2) => s with { Engines = new[] { e1, e2 } };

    private static IReadOnlyList<string> Upper(EngineDisplaySample s) => Lines("Upper display", s.UpperPage, s, Layout);

    private static IReadOnlyList<string> Lower(EngineDisplaySample s) => Lines("Lower display", s.LowerPage, s, Layout);

    private static string Row(IReadOnlyList<string> lines, string label) => Assert.Single(lines, l => l.StartsWith(label, StringComparison.Ordinal));

    private static readonly string[] UpperIdle =
    {
        "Upper display: primary engine page",
        "TAT +19C",
        "TO",
        "N1: 21.0, 21.0",
        "EGT: 466, 466",
        "FF: 0.67, 0.67",
        "Alerts: none",
        "FUEL LB: 1: 8.63, CTR: 7.78, 2: 8.63, TOTAL: 25.0",
    };

    private static readonly string[] LowerIdle =
    {
        "Lower display: secondary engine page",
        "N2: 60.6, 60.6",
        "FF: 0.67, 0.67",
        "OIL PRESS: 31, 32",
        "OIL TEMP: 76, 76",
        "OIL QTY %: 80, 80",
        "VIB: 0.9, 0.5",
    };

    [Fact]
    public void Primary_page_rows_from_the_live_idle_reading() => Assert.Equal(UpperIdle, Upper(Idle));

    [Fact]
    public void Secondary_page_rows_from_the_live_idle_reading() => Assert.Equal(LowerIdle, Lower(Idle));

    [Fact]
    public void Compact_page_has_every_item_in_order()
    {
        var lines = Lines("Upper display", 7, Idle, Layout);
        Assert.Equal(new[]
        {
            "Upper display: compact engine page", "TAT +19C", "TO", "N1: 21.0, 21.0", "EGT: 466, 466", "N2: 60.6, 60.6", "FF: 0.67, 0.67",
            "OIL PRESS: 31, 32", "OIL TEMP: 76, 76", "OIL QTY %: 80, 80", "VIB: 0.9, 0.5", "Alerts: none",
            "FUEL LB: 1: 8.63, CTR: 7.78, 2: 8.63, TOTAL: 25.0",
        }, lines);
    }

    [Fact]
    public void Side_by_side_page_follows_its_own_order()
    {
        var lines = Lines("Upper display", 4, Idle, Layout);
        Assert.Equal("Upper display: side by side engine page", lines[0]);
        Assert.Equal(new[] { "TO", "TAT +19C", "N1: 21.0, 21.0" }, lines.Skip(1).Take(3));
        Assert.Equal(new[] { "HYD PRESS: 3000, 3000", "HYD QTY %: 92, 92" }, lines.TakeLast(2));
    }

    [Fact]
    public void Sys_page_rows()
    {
        var s = Idle with
        {
            Sys = Idle.Sys with { RefillA = true, Rudder = -1.4, BrakeTemperatures = new[] { 0.0, 1.26, 0.0, 0.0 }, BrakeStatus = new[] { 0, 1, 0, 0 } },
        };
        Assert.Equal(new[]
        {
            "Lower display: SYS page",
            "HYDRAULIC: A, B",
            "QTY %: 92 RF, 92",
            "PRESS: 3000, 3000",
            "FLIGHT CONTROLS: AIL 0, FLT SPLR 0, ELEV 0, FLT SPLR 0, AIL 0, RUDDER -1",
            "BRAKE TEMP: 0.0, 1.3 hot, 0.0, 0.0",
        }, Lines("Lower display", 13, s, Layout));
    }

    [Fact]
    public void Unknown_page_is_named_by_number() =>
        Assert.Equal(new[] { "Upper display: page 14" }, Lines("Upper display", 14, Idle, Layout));

    [Fact]
    public void Navigation_display_is_named_only() =>
        Assert.Equal(new[] { "Lower display: navigation display" }, Lines("Lower display", 3, Idle, Layout));

    [Theory]
    [InlineData(0, "off")]
    [InlineData(1, "blank")]
    [InlineData(2, "PFD")]
    [InlineData(9, "map")]
    public void Pages_the_reader_does_not_read_are_named_only(int page, string name) =>
        Assert.Equal(new[] { "Upper display: " + name }, Lines("Upper display", page, Idle, Layout));

    [Fact]
    public void Before_the_aircraft_initialises_the_displays_are_off()
    {
        var junk = Idle with { Initialised = false, UpperPage = 4 };
        Assert.Equal(new[] { "Upper display: off" }, Upper(junk));
        Assert.Equal(new[] { "Lower display: off" }, Lower(junk));
    }

    [Fact]
    public void Kilograms_label_and_convert_every_fuel_number()
    {
        var lines = Upper(Idle with { FuelInKg = true });
        Assert.Equal("FUEL KG: 1: 3.91, CTR: 3.53, 2: 3.91, TOTAL: 11.4", Row(lines, "FUEL"));
        Assert.Equal("FF: 0.31, 0.31", Row(lines, "FF"));
    }

    [Fact]
    public void Oil_quantity_follows_the_selected_unit()
    {
        var engines = WithEngines(Idle, Engine(1) with { OilQuantity = 0.9 }, Engine(2) with { OilQuantity = 0.9 });
        Assert.Equal("OIL QTY %: 90, 90", Row(Lower(engines), "OIL QTY"));
        Assert.Equal("OIL QTY: 17, 17", Row(Lower(engines with { OilUnit = OilQuantityUnit.Quarts, OilMultiplier = 19.25 }), "OIL QTY"));
        Assert.Equal("OIL QTY: 16, 16", Row(Lower(engines with { OilUnit = OilQuantityUnit.Litres, OilMultiplier = 18.2173 }), "OIL QTY"));
    }

    [Fact]
    public void Oil_quantity_LO_needs_the_option()
    {
        var low = WithEngines(Idle, Engine(1) with { OilQuantity = 0.2 }, Engine(2));
        Assert.Equal("OIL QTY %: 20 LO, 80", Row(Lower(low), "OIL QTY"));
        Assert.Equal("OIL QTY %: 20, 80", Row(Lower(low with { OilLowIndication = false }), "OIL QTY"));
    }

    [Fact]
    public void Fuel_used_replaces_fuel_flow()
    {
        var s = WithEngines(Idle with { ShowFuelUsed = true }, Engine(1) with { FuelUsed = 1230 }, Engine(2) with { FuelUsed = 1200 });
        Assert.Equal("FUEL USED: 1.23, 1.20", Row(Upper(s), "FUEL USED"));
    }

    [Fact]
    public void Colors_and_messages_sit_after_their_engine()
    {
        var s = WithEngines(Idle,
            Engine(1) with { Egt = 960, ReverserShown = true, N2 = 25.3, CrossBleedStart = true, TaiOn = true, OilTemperature = 150, Vibration = 4.2 },
            Engine(2) with { EngineFail = true, ReverserShown = true, ReverserSecond = true, TaiAmber = true, N2 = 0 });
        Assert.Equal("EGT: 960 red, 466 ENG FAIL", Row(Upper(s), "EGT"));
        Assert.Equal("N1: 21.0 REV amber TAI, 21.0 REV green TAI amber", Row(Upper(s), "N1"));
        Assert.Equal("N2: 25.3 X-BLD START, 0.0", Row(Lower(s), "N2"));
        Assert.Equal("OIL TEMP: 150 amber, 76", Row(Lower(s), "OIL TEMP"));
        Assert.Equal("VIB: 4.2 high, 0.5", Row(Lower(s), "VIB"));
    }

    [Fact]
    public void A_value_with_no_reading_is_blank_as_pmdg_shows_it()
    {
        // PMDG stores -1000000 for "no value" (live 2026-10-24: hydraulic pressure with the pumps off,
        // vibration unpowered) and prints the readout only above -999999.
        var s = WithEngines(Idle, Engine(1) with { Vibration = -1000000 }, Engine(2))
            with { Sys = Idle.Sys with { HydPressureA = -1000000 } };
        Assert.Equal("VIB: blank, 0.5", Row(Lower(s), "VIB"));
        Assert.Equal("PRESS: blank, 3000", Row(Lines("Lower display", 13, s, Layout), "PRESS"));
        Assert.Equal("HYD PRESS: blank, 3000", Row(Lines("Upper display", 4, s, Layout), "HYD PRESS"));
    }

    [Fact]
    public void Tank_warnings_follow_their_tank() =>
        Assert.Equal("FUEL LB: 1: 8.63 LOW, CTR: 7.78 CONFIG, 2: 8.63 IMBAL, TOTAL: 25.0",
            Row(Upper(Idle with { FuelWarning = new[] { 2, 1, 3 } }), "FUEL"));

    [Fact]
    public void Alerts_row_holds_the_alert_column_in_order()
    {
        var s = WithEngines(Idle with { AutolandAdvisory = 0, AutothrottleLimit = true, AtcMemo = true, CommMemo = true },
            Engine(1) with { StartValve = 1, LowOilPressure = 2 }, Engine(2) with { OilFilterBypass = 2 });
        Assert.Equal("Alerts: engine 1 START VALVE OPEN, engine 1 LOW OIL PRESSURE, engine 2 OIL FILTER BYPASS, NO AUTOLAND, A/T LIM, ATC, COMM",
            Row(Upper(s), "Alerts"));
        Assert.Equal("Alerts: NO LAND 3", Row(Upper(Idle with { AutolandAdvisory = 1 }), "Alerts"));
    }

    [Fact]
    public void Thrust_mode_is_TO_on_the_ground() => Assert.Equal("TO", ThrustModeText(Idle, Layout));

    [Fact]
    public void Thrust_mode_MAN_when_n1_set_is_manual()
    {
        var manual = Idle with { N1SetSelector = 1 };
        Assert.Equal("MAN", ThrustModeText(manual, Layout));
        Assert.Equal("N1: 21.0, 21.0. Reference 95.5, 95.5", Row(Upper(manual), "N1"));
    }

    [Fact]
    public void Thrust_mode_dashes_when_none() => Assert.Equal("---", ThrustModeText(Idle with { ThrustMode = -1 }, Layout));

    [Fact]
    public void Thrust_mode_words_follow_pmdgs_choice()
    {
        Assert.Equal("CLB", ThrustModeText(Idle with { ThrustMode = 4 }, Layout));
        Assert.Equal("R-CLB", ThrustModeText(Idle with { ThrustMode = 5 }, Layout));
        Assert.Equal("R-TO", ThrustModeText(Idle with { AssumedTempShown = true }, Layout));
        Assert.Equal("D-TO 1", ThrustModeText(Idle with { ThrustMode = 1, AssumedTempShown = true, DerateOption = true }, Layout));
        Assert.Equal("TO 1", ThrustModeText(Idle with { ThrustMode = 1, DerateOption = true }, Layout));
    }

    [Fact]
    public void Nothing_drawn_at_or_above_the_mode_limit()
    {
        Assert.Equal("", ThrustModeText(Idle with { ThrustMode = 10 }, Layout));
        Assert.Equal("", ThrustModeText(Idle with { ThrustMode = 10, N1SetSelector = 1 }, Layout));
        Assert.DoesNotContain(Upper(Idle with { ThrustMode = 10 }), l => l == "" || l == "TO");
        Assert.Equal(UpperIdle.Length - 1, Upper(Idle with { ThrustMode = 10 }).Count);
    }

    [Fact]
    public void Thrust_row_adds_the_assumed_temperature_and_the_limit() =>
        Assert.Equal("R-TO +35C, A/T LIM",
            Upper(Idle with { AssumedTempShown = true, AssumedTemperature = 35, AutothrottleLimit = true })[2]);

    [Fact]
    public void Tat_below_zero_rounds_as_pmdg_prints_it() =>
        Assert.Equal("TAT -4C", Upper(Idle with { Tat = -3.7 })[1]);

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Rows_are_invariant_under_a_comma_decimal_culture(string culture)
    {
        Assert.Equal(UpperIdle, TestCulture.Run(culture, () => Upper(Idle)));
        Assert.Equal(LowerIdle, TestCulture.Run(culture, () => Lower(Idle)));
        Assert.Equal("FUEL KG: 1: 3.91, CTR: 3.53, 2: 3.91, TOTAL: 11.4",
            TestCulture.Run(culture, () => Row(Upper(Idle with { FuelInKg = true }), "FUEL")));
    }
}

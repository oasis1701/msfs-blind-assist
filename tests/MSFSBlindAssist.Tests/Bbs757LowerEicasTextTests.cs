using MSFSBlindAssist.Services.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The lower EICAS's rows as drawn (plan 25, Task 4): truncated whole numbers, one-decimal fuel flow in pounds or
/// kilograms, the start formulas, the oil temperature boundaries, the HYD QTY suffixes, the pointer words and the
/// blank cells of an invalid engine, all culture-invariant (de-DE and sv-SE).</summary>
public class Bbs757LowerEicasTextTests
{
    private static readonly Bbs757LowerEicasLayout Layout = Bbs757LowerEicasLayoutTests.Sample();

    private static Bbs757LowerEicasEngineRaw Eng(bool valid = true, double n2 = 81.99, double n3 = 77.4, double ff = 3600,
        double oilP = 91.9, double oilT = 114.7, double oilQ = 0.58, double vib = -1.84, double maxN2 = 0, double maxN3 = 0,
        bool starting = false, bool hold = false, double hp = 77.4) =>
        new(valid, n2, n3, ff, oilP, oilT, oilQ, vib, maxN2, maxN3, starting, hold, hp);

    private static Bbs757LowerEicasStatusRaw Status(double[]? qty = null, double[]? cap = null, double egt = -55,
        double rpm = 0, double oilQ = 0.75, bool oxyShown = true, bool oxyActive = true, int oxyCap = 1850,
        double oxyFull = 0, double oxyLevel = 1760, double rud = 0, double ailL = 0, double elev = 0, double ailR = 0,
        uint[]? brakes = null) =>
        new(cap ?? new double[] { 1, 1, 1 }, qty ?? new[] { 0.98, 0.97, 1.0 }, new double[] { 3000.9, 2880, 2950 },
            egt, rpm, oilQ, oxyShown, oxyActive, oxyCap, oxyFull, oxyLevel, rud, ailL, elev, ailR,
            brakes ?? new uint[8]);

    private static Bbs757LowerEicasRaw Raw(int page, Bbs757LowerEicasEngineRaw? left = null, Bbs757LowerEicasEngineRaw? right = null,
        bool rr = true, bool latch = false, bool pounds = true, Bbs757LowerEicasStatusRaw? status = null,
        string[]? messages = null, bool noEngines = false) =>
        new(page, rr, latch, pounds, noEngines ? null : new[] { left ?? Eng(), right ?? Eng(n2: 82.09) },
            status ?? Status(), messages ?? Array.Empty<string>());

    private static IReadOnlyList<string> Rows(Bbs757LowerEicasRaw raw) => Bbs757LowerEicasText.PageRows(Layout, raw);

    private static readonly string[] Cultures = { "en-US", "de-DE", "sv-SE" };

    private static void InEveryCulture(Action check)
    {
        foreach (string c in Cultures)
            TestCulture.Run(c, () => { check(); return 0; });
    }

    [Fact]
    public void The_page_words_are_by_kind()
    {
        Assert.Equal("engine", Bbs757LowerEicasText.PageWord(Layout, 1));
        Assert.Equal("STATUS", Bbs757LowerEicasText.PageWord(Layout, 2));
        Assert.Equal("engine off", Bbs757LowerEicasText.PageWord(Layout, 3));
        Assert.Equal("blank", Bbs757LowerEicasText.PageWord(Layout, 0));
        Assert.Equal("blank", Bbs757LowerEicasText.PageWord(Layout, 77));
        Assert.Empty(Rows(Raw(0)));
    }

    [Fact]
    public void The_engine_page_truncates_whole_numbers_and_prints_one_decimal()
    {
        InEveryCulture(() => Assert.Equal(new[]
        {
            "N2: 81, 82",
            "N3: 77, 77",
            "FF: 3.6, 3.6",
            "OIL PRESS: 91, 91",
            "OIL TEMP: 114, 114",
            "OIL QTY: 12, 12",
            "VIB: 1.8, 1.8",
        }, Rows(Raw(1))));
    }

    [Fact]
    public void Oil_quantity_clamps_to_0_to_21_quarts_before_truncating()
    {
        // Evidence: 23014 clamps the drawn quantity to 0..21.
        var rows = Rows(Raw(1, left: Eng(oilQ: 1.2), right: Eng(n2: 82.09, oilQ: -0.1)));
        Assert.Contains("OIL QTY: 21, 0", rows);
    }

    [Fact]
    public void A_pratt_and_whitney_engine_page_has_no_n3_rows()
    {
        Assert.DoesNotContain(Rows(Raw(1, rr: false)), r => r.StartsWith("N3"));
    }

    [Fact]
    public void The_max_rows_exist_while_either_box_is_not_zero_and_a_zero_box_reads_blank()
    {
        InEveryCulture(() =>
        {
            var rows = Rows(Raw(1, left: Eng(maxN2: 100.9), right: Eng(n2: 82.09, maxN3: 99.5)));
            Assert.Equal("N2 max: 100, blank", rows[1]);
            Assert.Equal("N3 max: blank, 99", rows[3]);
            Assert.Equal("N3: 77, 77", rows[2]);
        });
    }

    [Fact]
    public void A_blanked_value_reads_as_drawn()
    {
        InEveryCulture(() =>
        {
            var rows = Rows(Raw(1, left: Eng(valid: false, n2: 4.2, maxN2: 101)));
            Assert.Equal("N2: blank, 82", rows[0]);
            Assert.Equal("N2 max: blank, blank", rows[1]);
            Assert.Contains("N3: blank, 77", rows);
            Assert.Contains("FF: blank, 3.6", rows);
            Assert.Contains("OIL PRESS: blank, 91", rows);
            Assert.Contains("OIL TEMP: blank, 114", rows);
            Assert.Contains("OIL QTY: blank, 12", rows);
            Assert.Contains("VIB: blank, 1.8", rows);
        });
    }

    [Theory]
    [InlineData(114.7, "114")]
    [InlineData(0, "0")]
    [InlineData(0.9, "0")]
    [InlineData(-0.4, "- 0")]
    [InlineData(-5.7, "- 5")]
    [InlineData(-9.9, "- 9")]
    [InlineData(-10, "-10")]
    [InlineData(-12.8, "-12")]
    public void The_oil_temperature_boundaries(double value, string expected)
    {
        InEveryCulture(() => Assert.Equal(expected, Bbs757LowerEicasText.OilTemperature(value)));
    }

    [Fact]
    public void The_oil_temperature_is_blank_below_five_percent_n2_of_that_engine()
    {
        var rows = Rows(Raw(1, left: Eng(n2: 4.9), right: Eng(n2: 5)));
        Assert.Contains("OIL TEMP: blank, 114", rows);
    }

    [Fact]
    public void Fuel_flow_is_in_kilograms_unless_the_settings_say_pounds()
    {
        InEveryCulture(() =>
        {
            Assert.Contains("FF: 3.6, 3.6", Rows(Raw(1, pounds: true)));
            // 3600 / 2.205 = 1632.7 kg
            Assert.Contains("FF: 1.6, 1.6", Rows(Raw(1, pounds: false)));
        });
    }

    [Fact]
    public void Fuel_flow_while_starting_follows_the_draw_per_page()
    {
        InEveryCulture(() =>
        {
            // Engine page: hold 600; start past hold 600..1200 by the HP spool (25 -> 600, 42.5 -> 900, 60 -> 1200).
            Assert.Contains("FF: 0.6, 0.6", Rows(Raw(1, left: Eng(starting: true, hold: true, hp: 70), right: Eng(starting: true, hold: true, hp: 10))));
            Assert.Contains("FF: 0.6, 0.9", Rows(Raw(1, left: Eng(starting: true, hp: 25), right: Eng(starting: true, hp: 42.5))));
            Assert.Contains("FF: 1.2, 0.6", Rows(Raw(1, left: Eng(starting: true, hp: 99), right: Eng(starting: true, hp: -5))));
            // Hold without a start flag shows data+88 on the engine page.
            Assert.Contains("FF: 3.6, 3.6", Rows(Raw(1, left: Eng(hold: true), right: Eng(hold: true))));
            // STATUS page: start past hold 0; hold 600 (start or not); else data+88.
            var status = Rows(Raw(2, left: Eng(starting: true), right: Eng(hold: true)));
            Assert.Contains("FF: 0.0, 0.6", status);
            Assert.Contains("FF: 3.6, 3.6", Rows(Raw(2)));
        });
    }

    [Fact]
    public void The_status_cue_shows_on_the_engine_page_only_while_the_list_is_not_empty()
    {
        Assert.DoesNotContain("STATUS", Rows(Raw(1)));
        var rows = Rows(Raw(1, messages: new[] { "WARN ELEX" }));
        Assert.Equal("STATUS", rows[rows.ToList().FindIndex(r => r.StartsWith("OIL PRESS")) + 1]);
    }

    [Fact]
    public void The_engine_off_page_shows_only_fuel_flow_unless_the_latch_is_set()
    {
        InEveryCulture(() =>
        {
            Assert.Equal(new[] { "FF: 3.6, 3.6" }, Rows(Raw(3)));
            Assert.Equal(new[] { "N2: 81, 82", "N2 max: 100, 101", "N3: 77, 77", "FF: 3.6, 3.6" },
                Rows(Raw(3, latch: true, left: Eng(maxN2: 100.3), right: Eng(n2: 82.09, maxN2: 101.2))));
            Assert.Equal(new[] { "N2: 81, 82", "FF: 3.6, 3.6" }, Rows(Raw(3, latch: true, rr: false)));
        });
    }

    [Theory]
    [InlineData(0.98, "0.98")]
    [InlineData(0.70, "0.70 RF")]
    [InlineData(0.75, "0.75")]
    [InlineData(0.7499, "0.75 RF")]
    [InlineData(1.22, "1.22")]
    [InlineData(1.25, "1.25 OF")]
    [InlineData(1.0, "1.00")]
    public void The_hydraulic_quantity_and_its_suffix(double quantity, string expected)
    {
        InEveryCulture(() => Assert.Equal(expected, Bbs757LowerEicasText.HydraulicQuantity(quantity, 1)));
    }

    [Fact]
    public void The_hydraulic_quantity_is_a_percentage_of_the_capacity()
    {
        // 7.0 of 10.0 is 70 %: "0.70 RF".
        Assert.Equal("0.70 RF", Bbs757LowerEicasText.HydraulicQuantity(7, 10));
        Assert.Equal("blank", Bbs757LowerEicasText.HydraulicQuantity(7, 0));
    }

    [Fact]
    public void The_status_page_rows_as_drawn()
    {
        InEveryCulture(() => Assert.Equal(new[]
        {
            "HYD QTY: L 0.70 RF, C 0.97, R 1.25 OF",
            "HYD PRESS: L 3000, C 2880, R 2950",
            "APU EGT: -55",
            "RPM: 0",
            "OIL Q: 0.75",
            "OXY PRESS: 1760",
            "RUD: centered",
            "AIL left: centered",
            "ELEV: centered",
            "AIL right: centered",
            "FF: 3.6, 3.6",
            "WARN ELEX",
            "L EICAS CMPTR",
            "BRAKE TEMP: 1: 0, 2: 1, 3: 2, 4: 3, 5: 4, 6: 5, 7: 8, 8: 9",
        }, Rows(Raw(2, status: Status(qty: new[] { 0.70, 0.97, 1.25 }, brakes: new uint[] { 0, 1, 2, 3, 4, 5, 8, 9 }),
            messages: new[] { "WARN ELEX", "L EICAS CMPTR" }))));
    }

    [Theory]
    [InlineData(0.0, "centered")]
    [InlineData(0.004, "centered")]
    [InlineData(0.25, "25 % right")]
    [InlineData(-0.25, "25 % left")]
    [InlineData(1.7, "100 % right")]
    [InlineData(-1.7, "100 % left")]
    public void The_rudder_pointer_words(double v, string expected)
    {
        InEveryCulture(() => Assert.Equal(expected, Bbs757LowerEicasText.Rudder(v)));
    }

    [Theory]
    [InlineData(0.30, "30 % down")]
    [InlineData(-0.30, "30 % up")]
    [InlineData(0.0, "centered")]
    public void The_vertical_pointer_words(double v, string expected)
    {
        InEveryCulture(() => Assert.Equal(expected, Bbs757LowerEicasText.Vertical(v)));
    }

    [Fact]
    public void The_flight_control_rows_draw_the_stored_values_as_they_are()
    {
        InEveryCulture(() =>
        {
            // The update stores the values as drawn: positive is down on the vertical scales, right on the rudder.
            var rows = Rows(Raw(2, status: Status(rud: 0.25, ailL: 0.3, elev: -0.1, ailR: 0.3)));
            Assert.Contains("RUD: 25 % right", rows);
            Assert.Contains("AIL left: 30 % down", rows);
            Assert.Contains("ELEV: 10 % up", rows);
            Assert.Contains("AIL right: 30 % down", rows);
            Assert.Contains("AIL right: 30 % up", Rows(Raw(2, status: Status(ailR: -0.3))));
            // No gain and no negation after the load: a stored elevator of 0.2 is 20 %.
            Assert.Contains("ELEV: 20 % down", Rows(Raw(2, status: Status(elev: 0.2))));
            Assert.Contains("ELEV: 100 % down", Rows(Raw(2, status: Status(elev: 1.4))));
        });
    }

    [Fact]
    public void The_n2_and_n3_digits_and_max_boxes_truncate_after_an_f32_demotion()
    {
        InEveryCulture(() =>
        {
            // 81.99999999 demotes to exactly 82f in f32, so it reads 82 (an f64 truncation would give 81).
            double nearly = 81.99999999;
            Assert.Equal(81, (int)nearly);
            var rows = Rows(Raw(1, left: Eng(n2: nearly, n3: 76.999999999, maxN2: 100.99999999), right: Eng(n2: 81.9999)));
            Assert.Equal("N2: 82, 81", rows[0]);
            Assert.Equal("N2 max: 101, blank", rows[1]);
            Assert.Equal("N3: 77, 77", rows[2]);
            // The other whole numbers stay f64: OIL PRESS 91.99999999 is 91.
            Assert.Contains("OIL PRESS: 91, 91", Rows(Raw(1, left: Eng(oilP: 91.99999999))));
        });
    }

    [Theory]
    [InlineData(0.75, "0.75")]
    [InlineData(0.99, "FULL")]
    [InlineData(1.0, "FULL")]
    [InlineData(0.1, "0.25")]
    [InlineData(0.4, "0.50")]
    [InlineData(0.55, "0.50")]
    [InlineData(0.9, "FULL")]
    public void The_apu_oil_is_rounded_to_quarters(double q, string expected)
    {
        InEveryCulture(() => Assert.Equal(expected, Bbs757LowerEicasText.ApuOil(q)));
    }

    [Fact]
    public void The_oxygen_pressure_follows_its_getter()
    {
        Assert.Equal(0, Bbs757LowerEicasText.OxygenPressure(Status(oxyActive: false)));
        Assert.Equal(1850, Bbs757LowerEicasText.OxygenPressure(Status(oxyFull: 1)));
        Assert.Equal(1760, Bbs757LowerEicasText.OxygenPressure(Status(oxyLevel: 1757)));
        Assert.Equal(1850, Bbs757LowerEicasText.OxygenPressure(Status(oxyLevel: 5000)));
        Assert.Equal(0, Bbs757LowerEicasText.OxygenPressure(Status(oxyLevel: -20)));
        Assert.DoesNotContain(Rows(Raw(2, status: Status(oxyShown: false))), r => r.StartsWith("OXY"));
    }

    [Fact]
    public void The_status_page_ff_row_needs_the_engine_part()
    {
        Assert.Contains("FF: not read", Rows(Raw(2, noEngines: true)));
    }

    [Fact]
    public void A_missing_part_is_one_row()
    {
        var noEngine = Layout with { Engine = null, EngineMissing = "none here" };
        Assert.Equal(new[] { "engine page: not read: none here" }, Bbs757LowerEicasText.PageRows(noEngine, Raw(1)));
        Assert.Equal(new[] { "engine page: not read: none here" }, Bbs757LowerEicasText.PageRows(noEngine, Raw(3)));
        var noStatus = Layout with { Status = null, StatusMissing = "no list" };
        Assert.Equal(new[] { "STATUS page: not read: no list" }, Bbs757LowerEicasText.PageRows(noStatus, Raw(2)));
    }
}

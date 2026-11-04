// Characterization tests for the Fenix SD's ENG page (FenixChannelSdPages.EngRows, seam ComposeEng): the rows of both
// engines as the SD draws them, in the SD's order, and what stops the page. The pushes are built with SdPush
// (FenixChannelSdWindowTests.cs), page 8, and every field number is written out here, never read from
// FenixChannelFields, so that a wrong number in the app fails a test.
//
// The contract: each row is a pair, engine 1 then engine 2 ("OIL QT: 13.9, 14.1"). F.USED (134.13.1, 134.13.2) follows the
// weight unit (154) and is whole; OIL QT (129.n.23) is to one decimal; OIL PSI (.24) and OIL °C (.25) are whole; VIB N1
// (.11) and VIB N2 (.12) are to one decimal. While 129.7 is set the page adds NAC °C (.7, whole); while 129.6 is set it
// adds IGN ("A" for .17, "B" for .18, "A B", or "off"), the start valve (.8, in the valve words, which read "open" for a
// field left out) and PSI (.9, whole). No row speaks a color. A part of the push the page reads that is left out, or a
// field in another wire shape, stops this page only: one row "ENG: not available, <problem>", while the page's name and
// the bottom line stay and the window reports the problem once under the page's name.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdEngTests
{
    // The bottom line of SdPush's default push, as the window reads it.
    private static readonly string[] BottomRows =
        ["TAT °C: +15, green", "SAT °C: +15, green", "UTC: 15:06", "GW KG: --, cyan"];

    // The rows of a push that composes, and nothing reported.
    private static IReadOnlyList<string> RowsOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    // One engine's SingleEngineState (129.1 or 129.2), the fields the page reads, the defaults of a plausible engine
    // on the ground. A null leaves the field out, as the Fenix leaves out a default.
    private sealed record EngineValues(
        double? OilQuantity = 13.94, double? OilPressure = 266.4, double? OilTemperature = 101.2,
        double? VibrationN1 = 0.2, double? VibrationN2 = 0.1, double? NacelleTemp = 44.4, double? StarterPsi = 32.2,
        int? StartValve = null, bool Igniter1 = false, bool Igniter2 = false, bool OilQtyPulse = false,
        bool OilPressPulse = false)
    {
        public FenixChannelProtoWriter Write()
        {
            var engine = new FenixChannelProtoWriter();
            if (NacelleTemp is { } nacelle) engine.Double(7, nacelle);
            if (StartValve is { } valve) engine.Varint(8, (ulong)valve);
            if (StarterPsi is { } psi) engine.Double(9, psi);
            if (VibrationN1 is { } vibrationN1) engine.Double(11, vibrationN1);
            if (VibrationN2 is { } vibrationN2) engine.Double(12, vibrationN2);
            if (Igniter1) engine.Bool(17, true);
            if (Igniter2) engine.Bool(18, true);
            if (OilQuantity is { } quantity) engine.Double(23, quantity);
            if (OilPressure is { } pressure) engine.Double(24, pressure);
            if (OilTemperature is { } temperature) engine.Double(25, temperature);
            if (OilQtyPulse) engine.Bool(26, true);
            if (OilPressPulse) engine.Bool(29, true);
            return engine;
        }
    }

    // The right engine of a push whose two engines differ a little, so that a row that reads the wrong engine shows.
    private static EngineValues RightEngine => new(
        OilQuantity: 14.06, OilPressure: 260.6, OilTemperature: 101.6, VibrationN1: 0.24, VibrationN2: 0.14,
        NacelleTemp: 43.6, StarterPsi: null);

    // An ENG page push (page 8): the engine message (129) with the engines given, the flags that show the nacelle and
    // the starting rows, and the fuel message (134) with 134.13 the fuel used, 1250.4 and 1259.6 by default, and its
    // total. A null engine is left out of the push, and fuel false leaves out all of 134.
    private static SdPush EngPush(
        FenixChannelProtoWriter? left, FenixChannelProtoWriter? right, bool showStarting = false,
        bool showNacelle = false, bool fuel = true, bool kg = true)
    {
        return new SdPush { Page = 8, Kg = kg }.With(remote =>
        {
            var engines = new FenixChannelProtoWriter();
            if (left is not null) engines.Message(1, left);
            if (right is not null) engines.Message(2, right);
            if (showStarting) engines.Bool(6, true);
            if (showNacelle) engines.Bool(7, true);
            remote.Message(129, engines);

            if (fuel)
            {
                var used = new FenixChannelProtoWriter().Double(1, 1250.4).Double(2, 1259.6).Double(3, 2510);
                remote.Message(134, new FenixChannelProtoWriter().Message(13, used));
            }
        });
    }

    private static SdPush TwoEngines(bool showStarting = false, bool showNacelle = false, bool kg = true)
        => EngPush(new EngineValues().Write(), RightEngine.Write(), showStarting, showNacelle, kg: kg);

    // ---- The rows -------------------------------------------------------------------------------

    [Fact]
    public void The_eng_page_reads_both_engines_in_the_sds_order()
    {
        // Neither flag is set, so the nacelle and the starting rows are not drawn: the page's name, the six pairs, then
        // the bottom line. Fuel used is whole (1250.4, 1259.6), the oil quantity and the vibrations to one decimal.
        Assert.Equal(
            new[]
            {
                "ENG", "F.USED KG: 1250, 1260", "OIL QT: 13.9, 14.1", "OIL PSI: 266, 261", "OIL °C: 101, 102",
                "VIB N1: 0.2, 0.2", "VIB N2: 0.1, 0.1"
            }.Concat(BottomRows),
            RowsOf(TwoEngines()));
    }

    [Theory]
    [InlineData(true, false, false, false, "OIL QT: 13.9 pulsing, 14.1", "OIL PSI: 266, 261")]
    [InlineData(false, false, true, false, "OIL QT: 13.9, 14.1 pulsing", "OIL PSI: 266, 261")]
    [InlineData(true, false, true, false, "OIL QT: 13.9 pulsing, 14.1 pulsing", "OIL PSI: 266, 261")]
    [InlineData(false, true, false, false, "OIL QT: 13.9, 14.1", "OIL PSI: 266 pulsing, 261")]
    [InlineData(false, false, false, true, "OIL QT: 13.9, 14.1", "OIL PSI: 266, 261 pulsing")]
    [InlineData(false, true, false, true, "OIL QT: 13.9, 14.1", "OIL PSI: 266 pulsing, 261 pulsing")]
    public void An_oil_value_the_sd_pulses_says_pulsing_after_it(
        bool qty1, bool press1, bool qty2, bool press2, string qtyRow, string pressRow)
    {
        // 129.n.26 pulses the oil quantity and 129.n.29 the oil pressure, each engine on its own.
        SdPush push = EngPush(
            new EngineValues(OilQtyPulse: qty1, OilPressPulse: press1).Write(),
            (RightEngine with { OilQuantity = 14.1, OilPressure = 261, OilQtyPulse = qty2, OilPressPulse = press2 }).Write());

        IReadOnlyList<string> rows = RowsOf(push);

        Assert.Equal(qtyRow, rows[2]);
        Assert.Equal(pressRow, rows[3]);
    }

    [Fact]
    public void Nacelle_and_start_rows_show_only_while_drawn()
    {
        // The nacelle temperature while 129.7 is set; the igniters, the start valve and the starter pressure while 129.6
        // is. Engine 1 has both igniters and its start valve left out, which is open; engine 2 has neither igniter, its
        // valve closed and its pressure left out, which is 0.
        SdPush nacelle = EngPush(new EngineValues().Write(), RightEngine.Write(), showNacelle: true);
        SdPush starting = EngPush(
            new EngineValues(Igniter1: true, Igniter2: true).Write(),
            (RightEngine with { StartValve = 1 }).Write(), showStarting: true);
        SdPush both = EngPush(
            new EngineValues(Igniter1: true, Igniter2: true).Write(),
            (RightEngine with { StartValve = 1 }).Write(), showStarting: true, showNacelle: true);

        IReadOnlyList<string> withNacelle = RowsOf(nacelle);
        IReadOnlyList<string> withStarting = RowsOf(starting);
        IReadOnlyList<string> withBoth = RowsOf(both);

        Assert.Equal("NAC °C: 44, 44", withNacelle[7]);
        Assert.DoesNotContain(withNacelle, row => row.StartsWith("IGN", StringComparison.Ordinal));
        Assert.Equal(new[] { "IGN: A B, off", "start valve: open, closed", "PSI: 32, 0" }, withStarting.Skip(7).Take(3));
        Assert.DoesNotContain(withStarting, row => row.StartsWith("NAC", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "NAC °C: 44, 44", "IGN: A B, off", "start valve: open, closed", "PSI: 32, 0" },
            withBoth.Skip(7).Take(4));
        Assert.Equal(BottomRows, withBoth.TakeLast(4));
    }

    [Fact]
    public void The_igniters_read_their_letters()
    {
        // Igniter A is 17 and B is 18: one of them is its letter alone.
        SdPush push = EngPush(
            new EngineValues(Igniter1: true).Write(), new EngineValues(Igniter2: true).Write(), showStarting: true);

        Assert.Equal("IGN: A, B", RowsOf(push)[7]);
    }

    [Fact]
    public void Fuel_used_follows_the_weight_unit()
    {
        // Field 154 set is kilograms and left out is pounds (the Fenix leaves false out): the same figures are 2.2 times
        // off if the label is wrong, so the row's unit follows the field and the figures stay as sent.
        Assert.Equal("F.USED KG: 1250, 1260", RowsOf(TwoEngines(kg: true))[1]);
        Assert.Equal("F.USED LBS: 1250, 1260", RowsOf(TwoEngines(kg: false))[1]);
    }

    [Fact]
    public void A_value_that_is_not_a_number_reads_xx()
    {
        // The SD draws crosses for a number it has none for, and the channel sends NaN.
        SdPush push = EngPush(
            new EngineValues(OilQuantity: double.NaN).Write(), RightEngine.Write());

        Assert.Equal("OIL QT: XX, 14.1", RowsOf(push)[2]);
    }

    [Fact]
    public void A_start_valve_that_is_no_state_reads_not_available_and_is_reported()
    {
        // Value 9 is no state of the valve enum: the row says so, the page stays, and the problem is reported (at page
        // level, as the page's context gives it; the window adds the page's name).
        SdPush push = EngPush(
            new EngineValues(StartValve: 9).Write(), (RightEngine with { StartValve = 1 }).Write(), showStarting: true);
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelSdPages.EngRows(push.Context(reported.Add));

        Assert.Null(rows.Problem);
        Assert.Contains("start valve: not available, closed", rows.Rows!);
        Assert.Equal(new[] { "field 129.1.8 has an unknown value 9" }, reported);
    }

    // ---- What stops the page --------------------------------------------------------------------

    [Fact]
    public void A_missing_engine_is_not_available()
    {
        // 129.2 is left out: the page never reads the second engine as its defaults. The page returns the problem and
        // does not report it; the window reports it once, under the page's name.
        SdPush push = EngPush(new EngineValues().Write(), null);
        var returned = new List<string>();
        var reported = new List<string>();

        FenixChannelRows page = FenixChannelSdPages.EngRows(push.Context(returned.Add));
        FenixChannelRows window = push.Rows(reported.Add);

        Assert.Equal("no field 129.2", page.Problem);
        Assert.Null(page.Rows);
        Assert.Empty(returned);
        Assert.Null(window.Problem);
        Assert.Equal(new[] { "ENG", "ENG: not available, no field 129.2" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { "ENG: no field 129.2" }, reported);
    }

    [Fact]
    public void A_missing_fuel_message_is_not_available_never_zero()
    {
        // Fuel used left out of the push is not 0 kilograms.
        var reported = new List<string>();

        FenixChannelRows window = EngPush(new EngineValues().Write(), RightEngine.Write(), fuel: false).Rows(reported.Add);

        Assert.Equal(new[] { "ENG", "ENG: not available, no field 134" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { "ENG: no field 134" }, reported);
    }

    [Fact]
    public void A_wrong_shape_stops_the_page_only()
    {
        // The left engine's oil quantity (129.1.23) arrives as a varint: the page is one row naming the field, its name
        // and the bottom line stay, and the problem is reported once, under the page's name.
        SdPush push = EngPush(
            (new EngineValues() with { OilQuantity = null }).Write().Varint(23, 7), RightEngine.Write());
        var reported = new List<string>();

        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Equal(
            new[] { "ENG", "ENG: not available, field 129.1.23 has the wrong shape" }.Concat(BottomRows),
            rows.Rows);
        Assert.Equal(new[] { "ENG: field 129.1.23 has the wrong shape" }, reported);
    }

    // ---- Culture --------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Eng_rows_are_the_same_in_every_culture(string culture)
    {
        // A decimal (13.94) and negatives (-2.5 and -0.3, -3.4): the PC's decimal comma and sv-SE's true minus sign never
        // reach the rows, which are the invariant culture's, and a negative rounds away from zero.
        SdPush push = EngPush(
            new EngineValues(VibrationN1: -0.3, NacelleTemp: -2.5, Igniter1: true, Igniter2: true).Write(),
            (RightEngine with { StartValve = 1, NacelleTemp = -3.4 }).Write(), showStarting: true, showNacelle: true);

        Assert.Equal(
            new[]
            {
                "ENG", "F.USED KG: 1250, 1260", "OIL QT: 13.9, 14.1", "OIL PSI: 266, 261", "OIL °C: 101, 102",
                "VIB N1: -0.3, 0.2", "VIB N2: 0.1, 0.1", "NAC °C: -3, -3", "IGN: A B, off",
                "start valve: open, closed", "PSI: 32, 0"
            }.Concat(BottomRows),
            TestCulture.Run(culture, () => RowsOf(push)));
    }
}

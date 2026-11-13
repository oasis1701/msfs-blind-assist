// Characterization tests for the Fenix SD's FUEL page (FenixChannelSdPages.FuelRows, seam ComposeFuel): the tanks, the
// pumps, the valves, the fuel used and on board, and the tank temperatures, in the layout of the loaded airframe, and what
// stops the page. The pushes are built with SdPush (FenixChannelSdWindowTests.cs), page 10, and every field number is
// written out here, never read from FenixChannelFields, so that a wrong number in the app fails a test.
//
// The contract: page 10 reads 154 (the weight unit) and 134 (the fuel state: 134.2 to 134.7 the pumps, 134.11 the valves,
// 134.12 the quantities, 134.13 the fuel used, 134.14 the temperatures; on the A321 also 134.1). The airframe is the
// variant's Type, never a channel field. An A319, an A320 and (inferred) an A318 have one layout, the rows: F.USED 1+2
// (134.13.3, in the weight unit), APU valve (134.11.3), LP valves (134.11.1, .2), X FEED valve (134.11.4), the five tanks
// (134.12.1 to .5: left outer, left inner, center, right inner, right outer; whole, no unit), left, center and right pumps
// (134.2/.3, .4/.5, .6/.7: "off" without the switch .1, "LO" with the switch and low pressure .3, else "running"), left and
// right transfer valves (134.11.5/.6, .7/.8), FOB (134.12.6, in the weight unit), the outer tanks' temperatures (134.14.1,
// .4) and the inner tanks' (134.14.2, .3). An A321 has its own: no outer tanks, transfer valves, center pumps or outer
// temperatures, its wing tanks "left tank" and "right tank", and after "center tank" the center transfer valves (134.1.3,
// .4, "open" while set, else "closed"), ACT 1 (134.12.7) while the variant's ActCount is 1 or more and ACT 2 (134.12.8)
// while it is 2 or more, then "tanks °C" (134.14.2, .3). An airframe not known is "airframe not known", before a field is
// read. A valve reads its state word, "open" when the field is left out; one with a value that is no state reads "not
// available" and is reported without stopping the page. A message the rows read that the push leaves out stops this page
// only ("FUEL: not available, no field 134.11"), never read as its defaults. A part the airframe lacks is never read: a
// value the push carries for it, or a message it leaves out, cannot change or stop the page.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdFuelTests
{
    // The bottom line of SdPush's default push, as the window reads it.
    private static readonly string[] BottomRows =
        ["TAT °C: +15, green", "SAT °C: +15, green", "UTC: 15:06", "GW KG: --, cyan"];

    // An A321 with two extra tanks, the airframe the A321 layout is tested with.
    private static readonly FenixVariant A321 = FenixVariant.Unknown with
    {
        Type = FenixAircraftType.A321,
        Engine = FenixEngineFamily.IaeV2500,
        ActCount = 2
    };

    // The page's own rows (after its name, before the bottom line) of FuelPush's default push on an A320, in KG: the left
    // LP valve open and the right closed, the APU valve in transit, the crossfeed failed open, the left transfer valves
    // failed closed and closed and the right XX and open; the pumps running or LO or off in pairs of their own; and each
    // tank, temperature and total with a value of its own, rounded where it has a fraction (3456.5 to 3457, 13.5 to 14).
    private static readonly string[] A320Page =
    [
        "F.USED 1+2: 7220 KG", "APU valve: in transit", "LP valves: open, closed", "X FEED valve: failed open",
        "left outer tank: 1101", "left inner tank: 3457", "center tank: 6543", "right inner tank: 3790",
        "right outer tank: 1202", "left pumps: running, LO", "center pumps: off, LO", "right pumps: off, running",
        "left transfer valves: failed closed, closed", "right transfer valves: XX, open", "FOB: 8150 KG",
        "outer tanks °C: 12, 17", "inner tanks °C: 14, 16"
    ];

    // The same push on an A321 with <acts> ACT rows shown (ActCount of 0, 1 or 2). Every part of the push is the same: the
    // rows are what the layout reads of it.
    private static string[] A321Page(int acts)
    {
        var rows = new List<string>
        {
            "F.USED 1+2: 7220 KG", "APU valve: in transit", "LP valves: open, closed", "X FEED valve: failed open",
            "left tank: 3457", "center tank: 6543", "center transfer valves: open, closed"
        };
        if (acts >= 1) rows.Add("ACT 1: 503");
        if (acts >= 2) rows.Add("ACT 2: 404");
        rows.AddRange(
        [
            "right tank: 3790", "left pumps: running, LO", "right pumps: off, running", "FOB: 8150 KG", "tanks °C: 14, 16"
        ]);
        return [.. rows];
    }

    // The rows of a push that composes, and nothing reported.
    private static IReadOnlyList<string> RowsOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    // The page's own rows of a push that composes: what is between the name "FUEL" and the bottom line, whose gross weight
    // is in the unit given (KG while 154 is set, LBS while it is left out).
    private static IReadOnlyList<string> PageOf(SdPush push, string unit = "KG")
    {
        IReadOnlyList<string> rows = RowsOf(push);

        Assert.Equal("FUEL", rows[0]);
        Assert.Equal(
            BottomRows.Select(row => row.Replace("GW KG", $"GW {unit}", StringComparison.Ordinal)),
            rows.TakeLast(BottomRows.Length));
        return rows.Skip(1).SkipLast(BottomRows.Length).ToList();
    }

    // The rows with each one that starts with a prefix given replaced by the row given for it.
    private static IEnumerable<string> Replaced(IEnumerable<string> page, params (string Prefix, string Row)[] replacements)
        => page.Select(row =>
        {
            foreach ((string prefix, string replacement) in replacements)
            {
                if (row.StartsWith(prefix, StringComparison.Ordinal)) return replacement;
            }

            return row;
        });

    // ---- Building the fuel state ----------------------------------------------------------------

    // A fuel pump (134.2 to 134.7): the switch (1) and the low pressure (3), each written only while set, as the Fenix
    // leaves out a false.
    // The pump drawn amber is field 2.
    private static FenixChannelProtoWriter PumpMessage(bool switchOn, bool lowPressure, bool amber = false)
    {
        var pump = new FenixChannelProtoWriter();
        if (switchOn) pump.Bool(1, true);
        if (amber) pump.Bool(2, true);
        if (lowPressure) pump.Bool(3, true);
        return pump;
    }

    // The A321's center transfer valves (134.1.3 and 134.1.4), each written only while set; 134.1.5 and .6 are the valves
    // drawn amber, 134.1.7 and .8 their flow drawn amber.
    private static FenixChannelProtoWriter JetPumpMessage(
        bool left, bool right, bool leftValveAmber = false, bool rightValveAmber = false, bool leftFlowAmber = false,
        bool rightFlowAmber = false, bool leftFlow = false, bool rightFlow = false)
    {
        var jetPumps = new FenixChannelProtoWriter();
        if (leftFlow) jetPumps.Bool(1, true);
        if (rightFlow) jetPumps.Bool(2, true);
        if (left) jetPumps.Bool(3, true);
        if (right) jetPumps.Bool(4, true);
        if (leftValveAmber) jetPumps.Bool(5, true);
        if (rightValveAmber) jetPumps.Bool(6, true);
        if (leftFlowAmber) jetPumps.Bool(7, true);
        if (rightFlowAmber) jetPumps.Bool(8, true);
        return jetPumps;
    }

    // The valves (134.11) in the order of their fields (1 to 8: engine 1 and 2 LP, APU, crossfeed, left transfer 1 and 2,
    // right transfer 1 and 2), a state of 0 (open) left out as the Fenix leaves it; fewer than eight leave the rest out.
    private static FenixChannelProtoWriter ValveMessage(params int[] states)
    {
        var valves = new FenixChannelProtoWriter();
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i] != 0) valves.Varint(i + 1, unchecked((ulong)(long)states[i]));
        }

        return valves;
    }

    // The quantities (134.12), fields 1 to 8: the five tanks, the total, ACT 1 and ACT 2.
    private static FenixChannelProtoWriter QuantityMessage(
        double leftOuter, double leftInner, double center, double rightInner, double rightOuter, double total,
        double act1, double act2)
        => new FenixChannelProtoWriter()
            .Double(1, leftOuter).Double(2, leftInner).Double(3, center).Double(4, rightInner).Double(5, rightOuter)
            .Double(6, total).Double(7, act1).Double(8, act2);

    // The fuel used (134.13): engine 1, engine 2 and the total.
    private static FenixChannelProtoWriter UsedMessage(double engine1, double engine2, double total)
        => new FenixChannelProtoWriter().Double(1, engine1).Double(2, engine2).Double(3, total);

    // The temperatures (134.14), fields 1 to 4: left outer, left inner, right inner and right outer.
    private static FenixChannelProtoWriter TemperatureMessage(
        double leftOuter, double leftInner, double rightInner, double rightOuter)
        => new FenixChannelProtoWriter().Double(1, leftOuter).Double(2, leftInner).Double(3, rightInner).Double(4, rightOuter);

    // The fuel state of a FUEL push, every part carrying values of its own so that a row that reads another part's field
    // fails; a null part is left out of the push. The defaults are the pages above: every field of both layouts is
    // carried, whatever airframe the variant is.
    private sealed record FuelBoxes
    {
        public FenixVariant Variant { get; init; } = SdPush.A320;
        public bool Kg { get; init; } = true;

        // Whether 134 is in the push at all.
        public bool HasFuelState { get; init; } = true;

        public FenixChannelProtoWriter? JetPumps { get; init; } = JetPumpMessage(left: true, right: false);
        public FenixChannelProtoWriter? LeftWing1 { get; init; } = PumpMessage(true, false);
        public FenixChannelProtoWriter? LeftWing2 { get; init; } = PumpMessage(true, true);
        public FenixChannelProtoWriter? Center1 { get; init; } = PumpMessage(false, true);
        public FenixChannelProtoWriter? Center2 { get; init; } = PumpMessage(true, true);
        public FenixChannelProtoWriter? RightWing1 { get; init; } = PumpMessage(false, false);
        public FenixChannelProtoWriter? RightWing2 { get; init; } = PumpMessage(true, false);

        // The LP valves drawn amber (134.9, 134.10) and the ACT pump (134.15, its amber is field 3; the Fenix sends the
        // message empty while nothing is amber).
        public bool ValveAmber1 { get; init; }
        public bool ValveAmber2 { get; init; }
        public FenixChannelProtoWriter? ActPump { get; init; } = new FenixChannelProtoWriter();

        // Open, closed, in transit, failed open, failed closed, closed, DataInvalid (XX), open.
        public FenixChannelProtoWriter? Valves { get; init; } = ValveMessage(0, 1, 2, 3, 4, 1, 5, 0);

        public FenixChannelProtoWriter? Quantities { get; init; } =
            QuantityMessage(1101.4, 3456.5, 6543.2, 3789.6, 1202.4, 8149.5, 502.6, 404.4);

        public FenixChannelProtoWriter? Used { get; init; } = UsedMessage(3000.1, 4000.2, 7219.6);
        public FenixChannelProtoWriter? Temperatures { get; init; } = TemperatureMessage(12.4, 13.5, 15.6, 17.2);

        // The same boxes with the message at <path> ("134", "134.1", "134.2" to "134.7", "134.11" to "134.14") left out.
        public FuelBoxes Without(string path) => path switch
        {
            "134" => this with { HasFuelState = false },
            "134.1" => this with { JetPumps = null },
            "134.15" => this with { ActPump = null },
            "134.11" => this with { Valves = null },
            "134.12" => this with { Quantities = null },
            "134.13" => this with { Used = null },
            "134.14" => this with { Temperatures = null },
            _ when path.StartsWith("134.", StringComparison.Ordinal)
                   && int.TryParse(path.AsSpan(4), out int pump) && pump is >= 2 and <= 7 => WithPump(pump, null),
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, "no such message")
        };

        // The same boxes with the pump at field <field> (2 to 7 of 134) set to <pump>.
        public FuelBoxes WithPump(int field, FenixChannelProtoWriter? pump) => field switch
        {
            2 => this with { LeftWing1 = pump },
            3 => this with { LeftWing2 = pump },
            4 => this with { Center1 = pump },
            5 => this with { Center2 = pump },
            6 => this with { RightWing1 = pump },
            7 => this with { RightWing2 = pump },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "no such pump")
        };

        // The same boxes with all six pumps set to <pump>.
        public FuelBoxes WithAllPumps(Func<FenixChannelProtoWriter> pump) => this with
        {
            LeftWing1 = pump(), LeftWing2 = pump(), Center1 = pump(), Center2 = pump(), RightWing1 = pump(),
            RightWing2 = pump()
        };
    }

    // A FUEL page push (page 10): the weight unit, then the fuel state with its fields in the order of their numbers, on
    // the variant the boxes name.
    private static SdPush FuelPush(FuelBoxes? boxes = null)
    {
        FuelBoxes b = boxes ?? new FuelBoxes();
        return new SdPush { Page = 10, Variant = b.Variant, Kg = b.Kg }.With(remote =>
        {
            if (!b.HasFuelState) return;

            var fuel = new FenixChannelProtoWriter();
            Write(fuel, 1, b.JetPumps);
            Write(fuel, 2, b.LeftWing1);
            Write(fuel, 3, b.LeftWing2);
            Write(fuel, 4, b.Center1);
            Write(fuel, 5, b.Center2);
            Write(fuel, 6, b.RightWing1);
            Write(fuel, 7, b.RightWing2);
            if (b.ValveAmber1) fuel.Bool(9, true);
            if (b.ValveAmber2) fuel.Bool(10, true);
            Write(fuel, 11, b.Valves);
            Write(fuel, 12, b.Quantities);
            Write(fuel, 13, b.Used);
            Write(fuel, 14, b.Temperatures);
            Write(fuel, 15, b.ActPump);
            remote.Message(134, fuel);
        });
    }

    private static void Write(FenixChannelProtoWriter message, int field, FenixChannelProtoWriter? part)
    {
        if (part is not null) message.Message(field, part);
    }

    // ---- The rows -------------------------------------------------------------------------------

    [Fact]
    public void The_a320_fuel_page_reads_tanks_pumps_and_valves()
    {
        // The page's name, the fuel used, the APU, LP and crossfeed valves, the five tanks, the three pump pairs, the two
        // transfer valve pairs, the fuel on board and the two temperature pairs, then the bottom line. Every tank, pump,
        // valve and temperature has values of its own, and the figures round at a half.
        Assert.Equal(new[] { "FUEL" }.Concat(A320Page).Concat(BottomRows), RowsOf(FuelPush()));
    }

    [Theory]
    [InlineData(FenixAircraftType.A318)]    // inferred: the A318 follows the A320's layout, as no A318 has been measured
    [InlineData(FenixAircraftType.A319)]
    [InlineData(FenixAircraftType.A320)]
    public void The_a319_and_the_a320_share_a_layout_and_so_does_the_a318(FenixAircraftType type)
    {
        FenixVariant variant = SdPush.A320 with { Type = type };

        Assert.Equal(A320Page, PageOf(FuelPush(new FuelBoxes { Variant = variant })));
    }

    [Fact]
    public void The_a321_fuel_page_has_no_outer_tanks_or_center_pumps()
    {
        // The same push, which carries every field of both layouts (the outer tanks, the transfer valves, the center pumps,
        // the outer temperatures: the channel sends values for all of them), on an A321: its own layout, whole.
        IReadOnlyList<string> rows = RowsOf(FuelPush(new FuelBoxes { Variant = A321 }));

        Assert.Equal(new[] { "FUEL" }.Concat(A321Page(2)).Concat(BottomRows), rows);
        Assert.DoesNotContain(rows, row => row.Contains("outer", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row => row.Contains("center pumps", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row => row.Contains("left transfer", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row => row.Contains("right transfer", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null, 0)]       // an unread count shows no ACT row
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]          // the page draws two
    public void Act_rows_follow_the_variants_extra_tanks(int? actCount, int shown)
    {
        // The airframe's config decides, never the data: the push carries both ACT quantities every time.
        FenixVariant variant = A321 with { ActCount = actCount };

        Assert.Equal(A321Page(shown), PageOf(FuelPush(new FuelBoxes { Variant = variant })));
    }

    [Fact]
    public void An_a320_has_no_act_rows_whatever_its_config_says()
    {
        FenixVariant variant = SdPush.A320 with { ActCount = 2 };

        Assert.Equal(A320Page, PageOf(FuelPush(new FuelBoxes { Variant = variant })));
    }

    [Fact]
    public void The_center_transfer_valves_follow_their_own_flags()
    {
        // 134.1.3 is the left valve and 134.1.4 the right one, "open" while set, else "closed": all four combinations.
        (bool Left, bool Right, string Row)[] combinations =
        [
            (false, false, "center transfer valves: closed, closed"),
            (true, false, "center transfer valves: open, closed"),
            (false, true, "center transfer valves: closed, open"),
            (true, true, "center transfer valves: open, open")
        ];

        foreach ((bool left, bool right, string row) in combinations)
        {
            IReadOnlyList<string> rows = PageOf(FuelPush(new FuelBoxes
            {
                Variant = A321,
                JetPumps = JetPumpMessage(left, right)
            }));

            Assert.Equal(Replaced(A321Page(2), ("center transfer valves:", row)), rows);
        }
    }

    // ---- Valves ---------------------------------------------------------------------------------

    [Fact]
    public void An_absent_valve_reads_open()
    {
        // The Fenix leaves Open (0) out, so 134.11 without field 1 reads the left LP valve open, and without field 2 too,
        // both: "open, open". A 134.11 with nothing in it reads every valve open.
        IReadOnlyList<string> oneLeftOut = PageOf(FuelPush(new FuelBoxes { Valves = ValveMessage(0, 1, 2, 3, 4, 1, 5, 0) }));
        IReadOnlyList<string> bothLeftOut = PageOf(FuelPush(new FuelBoxes { Valves = ValveMessage(0, 0, 2, 3, 4, 1, 5, 0) }));
        IReadOnlyList<string> noneSent = PageOf(FuelPush(new FuelBoxes { Valves = ValveMessage() }));

        Assert.Equal(A320Page, oneLeftOut);
        Assert.Equal(Replaced(A320Page, ("LP valves:", "LP valves: open, open")), bothLeftOut);
        Assert.Equal(
            Replaced(
                A320Page,
                ("APU valve:", "APU valve: open"), ("LP valves:", "LP valves: open, open"),
                ("X FEED valve:", "X FEED valve: open"), ("left transfer valves:", "left transfer valves: open, open"),
                ("right transfer valves:", "right transfer valves: open, open")),
            noneSent);
    }

    [Theory]
    [InlineData(1, "LP valves: open, open", "LP valves: failed open, open")]
    [InlineData(2, "LP valves: open, open", "LP valves: open, failed open")]
    [InlineData(3, "APU valve: open", "APU valve: failed open")]
    [InlineData(4, "X FEED valve: open", "X FEED valve: failed open")]
    [InlineData(5, "left transfer valves: open, open", "left transfer valves: failed open, open")]
    [InlineData(6, "left transfer valves: open, open", "left transfer valves: open, failed open")]
    [InlineData(7, "right transfer valves: open, open", "right transfer valves: failed open, open")]
    [InlineData(8, "right transfer valves: open, open", "right transfer valves: open, failed open")]
    public void Each_valve_row_reads_its_own_field(int field, string open, string failed)
    {
        // Every valve open but the one given, which is failed open: only the row that reads that field changes.
        var states = new int[8];
        states[field - 1] = 3;
        (string, string)[] allOpen =
        [
            ("APU valve:", "APU valve: open"), ("LP valves:", "LP valves: open, open"),
            ("X FEED valve:", "X FEED valve: open"), ("left transfer valves:", "left transfer valves: open, open"),
            ("right transfer valves:", "right transfer valves: open, open")
        ];

        IReadOnlyList<string> rows = PageOf(FuelPush(new FuelBoxes { Valves = ValveMessage(states) }));

        string[] expected = Replaced(A320Page, allOpen).Select(row => row == open ? failed : row).ToArray();
        Assert.Equal(expected, rows);
    }

    [Fact]
    public void A_missing_valve_message_is_not_available_never_open()
    {
        // 134.11 is left out: every valve would read open if the page took the Fenix's defaults for it. The page returns
        // the problem and does not report it; the window reports it once, under the page's name.
        SdPush push = FuelPush(new FuelBoxes { Valves = null });
        var returned = new List<string>();
        var reported = new List<string>();

        FenixChannelRows page = FenixChannelSdPages.FuelRows(push.Context(returned.Add));
        FenixChannelRows window = push.Rows(reported.Add);

        Assert.Equal("no field 134.11", page.Problem);
        Assert.Null(page.Rows);
        Assert.Empty(returned);
        Assert.Null(window.Problem);
        Assert.Equal(new[] { "FUEL", "FUEL: not available, no field 134.11" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { "FUEL: no field 134.11" }, reported);
    }

    [Fact]
    public void An_unknown_valve_value_reads_not_available_and_is_reported()
    {
        // 134.11.4 (the crossfeed) arrives as 9, which is no valve state: its row says so, the rest of the page is as
        // usual, and the problem is reported without stopping the page (at page level as the page's context gives it; the
        // window adds the page's name).
        SdPush push = FuelPush(new FuelBoxes { Valves = ValveMessage(0, 1, 2, 9, 4, 1, 5, 0) });
        var reported = new List<string>();
        var windowReported = new List<string>();

        FenixChannelRows page = FenixChannelSdPages.FuelRows(push.Context(reported.Add));
        FenixChannelRows window = push.Rows(windowReported.Add);

        string[] expected = Replaced(A320Page, ("X FEED valve:", "X FEED valve: not available")).ToArray();
        Assert.Null(page.Problem);
        Assert.Equal(expected, page.Rows);
        Assert.Equal(new[] { "field 134.11.4 has an unknown value 9" }, reported);
        Assert.Null(window.Problem);
        Assert.Equal(new[] { "FUEL" }.Concat(expected).Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { "FUEL: field 134.11.4 has an unknown value 9" }, windowReported);
    }

    // ---- Pumps ----------------------------------------------------------------------------------

    [Fact]
    public void Pump_words_follow_the_switch_and_the_low_pressure()
    {
        // "off" without the switch (whatever the pressure), "LO" with the switch and low pressure, else "running". Each pump
        // reads its own two fields: the four combinations are given in turn, each pump a different one at a time.
        (bool Switch, bool Low, string Word)[] combinations =
            [(false, false, "off"), (false, true, "off"), (true, true, "LO"), (true, false, "running")];

        for (int rotation = 0; rotation < combinations.Length; rotation++)
        {
            var pumps = Enumerable.Range(0, 6).Select(i => combinations[(rotation + i) % 4]).ToArray();

            IReadOnlyList<string> rows = PageOf(FuelPush(new FuelBoxes
            {
                LeftWing1 = PumpMessage(pumps[0].Switch, pumps[0].Low),
                LeftWing2 = PumpMessage(pumps[1].Switch, pumps[1].Low),
                Center1 = PumpMessage(pumps[2].Switch, pumps[2].Low),
                Center2 = PumpMessage(pumps[3].Switch, pumps[3].Low),
                RightWing1 = PumpMessage(pumps[4].Switch, pumps[4].Low),
                RightWing2 = PumpMessage(pumps[5].Switch, pumps[5].Low)
            }));

            Assert.Contains($"left pumps: {pumps[0].Word}, {pumps[1].Word}", rows);
            Assert.Contains($"center pumps: {pumps[2].Word}, {pumps[3].Word}", rows);
            Assert.Contains($"right pumps: {pumps[4].Word}, {pumps[5].Word}", rows);
        }
    }

    [Theory]
    [InlineData(2, "left pumps: running, running", "left pumps: LO, running")]
    [InlineData(3, "left pumps: running, running", "left pumps: running, LO")]
    [InlineData(4, "center pumps: running, running", "center pumps: LO, running")]
    [InlineData(5, "center pumps: running, running", "center pumps: running, LO")]
    [InlineData(6, "right pumps: running, running", "right pumps: LO, running")]
    [InlineData(7, "right pumps: running, running", "right pumps: running, LO")]
    public void Each_pump_row_reads_its_own_pumps(int field, string running, string low)
    {
        // Every pump running but the one given (134.<field>), which has low pressure: only its own row changes.
        FuelBoxes boxes = new FuelBoxes().WithAllPumps(() => PumpMessage(true, false)).WithPump(field, PumpMessage(true, true));

        IReadOnlyList<string> rows = PageOf(FuelPush(boxes));

        string[] expected = Replaced(
                A320Page,
                ("left pumps:", "left pumps: running, running"), ("center pumps:", "center pumps: running, running"),
                ("right pumps:", "right pumps: running, running"))
            .Select(row => row == running ? low : row)
            .ToArray();
        Assert.Equal(expected, rows);
    }

    // ---- Quantities, weights and temperatures ---------------------------------------------------

    [Fact]
    public void Weights_follow_the_weight_unit()
    {
        // The Fenix leaves 154 out for pounds: the fuel used and the fuel on board are then LBS, never KG, or every weight
        // would be 2.2 times off. The tanks and the temperatures have no unit either way.
        IReadOnlyList<string> pounds = PageOf(FuelPush(new FuelBoxes { Kg = false }), "LBS");
        IReadOnlyList<string> kilograms = PageOf(FuelPush(new FuelBoxes { Kg = true }));
        IReadOnlyList<string> a321Pounds = PageOf(FuelPush(new FuelBoxes { Kg = false, Variant = A321 }), "LBS");

        Assert.Equal("F.USED 1+2: 7220 LBS", pounds[0]);
        Assert.Contains("FOB: 8150 LBS", pounds);
        Assert.Equal(
            Replaced(A320Page, ("F.USED", "F.USED 1+2: 7220 LBS"), ("FOB:", "FOB: 8150 LBS")),
            pounds);
        Assert.Equal(A320Page, kilograms);
        Assert.Equal(
            Replaced(A321Page(2), ("F.USED", "F.USED 1+2: 7220 LBS"), ("FOB:", "FOB: 8150 LBS")),
            a321Pounds);
    }

    [Fact]
    public void A_value_that_is_not_a_number_reads_xx()
    {
        // The SD draws crosses for a number it has none for, and the channel sends NaN.
        IReadOnlyList<string> rows = PageOf(FuelPush(new FuelBoxes
        {
            Quantities = QuantityMessage(1101.4, double.NaN, 6543.2, 3789.6, 1202.4, double.PositiveInfinity, 502.6, 404.4),
            Used = UsedMessage(3000.1, 4000.2, double.NaN),
            Temperatures = TemperatureMessage(12.4, 13.5, double.NegativeInfinity, 17.2)
        }));

        Assert.Equal(
            Replaced(
                A320Page,
                ("left inner tank:", "left inner tank: XX"), ("FOB:", "FOB: XX KG"), ("F.USED", "F.USED 1+2: XX KG"),
                ("inner tanks", "inner tanks °C: 14, XX")),
            rows);
    }

    // ---- The airframe ---------------------------------------------------------------------------

    [Theory]
    [InlineData(FenixAircraftType.Unknown)]
    [InlineData((FenixAircraftType)9)]      // a value no airframe has
    public void An_unknown_airframe_is_not_available(FenixAircraftType type)
    {
        // Decided before any field is read: the push carries no fuel message at all, so a page that read a field first
        // would say "no field 134". The page returns the problem and does not report it; the window reports it once.
        FenixVariant variant = FenixVariant.Unknown with { Type = type };
        SdPush push = FuelPush(new FuelBoxes { Variant = variant }.Without("134"));
        SdPush complete = FuelPush(new FuelBoxes { Variant = variant });
        var returned = new List<string>();
        var reported = new List<string>();

        FenixChannelRows page = FenixChannelSdPages.FuelRows(push.Context(returned.Add));
        FenixChannelRows window = push.Rows(reported.Add);
        FenixChannelRows completeWindow = complete.Rows(_ => { });

        Assert.Equal("airframe not known", page.Problem);
        Assert.Null(page.Rows);
        Assert.Empty(returned);
        Assert.Null(window.Problem);
        Assert.Equal(new[] { "FUEL", "FUEL: not available, airframe not known" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { "FUEL: airframe not known" }, reported);
        Assert.Equal(window.Rows, completeWindow.Rows);
    }

    [Fact]
    public void The_window_passes_the_variant_to_the_page()
    {
        // The same RemoteData on the two airframes: the layout follows the variant the window is given, never the push.
        SdPush push = FuelPush(new FuelBoxes { Variant = A321 });
        var reported = new List<string>();

        FenixChannelRows onA321 = FenixChannelSd.Compose(push.Snapshot(), A321, reported.Add);
        FenixChannelRows onA320 = FenixChannelSd.Compose(push.Snapshot(), SdPush.A320, reported.Add);

        Assert.Null(onA321.Problem);
        Assert.Equal(new[] { "FUEL" }.Concat(A321Page(2)).Concat(BottomRows), onA321.Rows);
        Assert.Null(onA320.Problem);
        Assert.Equal(new[] { "FUEL" }.Concat(A320Page).Concat(BottomRows), onA320.Rows);
        Assert.Empty(reported);
    }

    [Fact]
    public void The_a320_page_never_reads_the_a321_only_parts()
    {
        // The center transfer valves (134.1) and the ACT quantities (134.12.7, .8) are the A321's. On an A320 with two
        // extra tanks in its config, a push without 134.1 and with the ACT quantities in a shape the page would refuse
        // composes as usual, and nothing is reported.
        FenixChannelProtoWriter quantities = new FenixChannelProtoWriter()
            .Double(1, 1101.4).Double(2, 3456.5).Double(3, 6543.2).Double(4, 3789.6).Double(5, 1202.4).Double(6, 8149.5)
            .Varint(7, 7).Varint(8, 7);
        FuelBoxes boxes = new()
        {
            Variant = SdPush.A320 with { ActCount = 2 },
            JetPumps = null,
            Quantities = quantities
        };

        Assert.Equal(A320Page, PageOf(FuelPush(boxes)));
    }

    [Fact]
    public void The_a321_page_never_reads_what_its_airframe_lacks()
    {
        // The outer tanks, the transfer valves, the center pumps, the outer temperatures and an ACT tank its config does
        // not have are not the A321's, so nothing the push holds for them can change or stop its page: the center pumps are
        // left out, the transfer valves read as no state, and the rest is in a shape the page would refuse.
        FenixChannelProtoWriter quantities = new FenixChannelProtoWriter()
            .Varint(1, 7).Double(2, 3456.5).Double(3, 6543.2).Double(4, 3789.6).Varint(5, 7).Double(6, 8149.5)
            .Double(7, 502.6).Varint(8, 7);
        FenixChannelProtoWriter temperatures = new FenixChannelProtoWriter()
            .Varint(1, 7).Double(2, 13.5).Double(3, 15.6).Varint(4, 7);
        FuelBoxes boxes = new()
        {
            Variant = A321 with { ActCount = 1 },
            Center1 = null,
            Center2 = null,
            Valves = ValveMessage(0, 1, 2, 3, 9, 9, 9, 9),
            Quantities = quantities,
            Temperatures = temperatures
        };
        SdPush push = FuelPush(boxes);
        var reported = new List<string>();

        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Equal(new[] { "FUEL" }.Concat(A321Page(1)).Concat(BottomRows), rows.Rows);
        Assert.Empty(reported);
    }

    // ---- What stops the page --------------------------------------------------------------------

    [Theory]
    [InlineData("134")]
    [InlineData("134.2")]
    [InlineData("134.3")]
    [InlineData("134.4")]
    [InlineData("134.5")]
    [InlineData("134.6")]
    [InlineData("134.7")]
    [InlineData("134.11")]
    [InlineData("134.12")]
    [InlineData("134.13")]
    [InlineData("134.14")]
    public void Every_message_the_a320_page_reads_is_needed(string path)
    {
        // A pump, the valves, the quantities, the fuel used or the temperatures left out is never read as its defaults.
        var reported = new List<string>();

        FenixChannelRows window = FuelPush(new FuelBoxes().Without(path)).Rows(reported.Add);

        Assert.Null(window.Problem);
        Assert.Equal(new[] { "FUEL", $"FUEL: not available, no field {path}" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { $"FUEL: no field {path}" }, reported);
    }

    [Theory]
    [InlineData("134")]
    [InlineData("134.1")]
    [InlineData("134.2")]
    [InlineData("134.3")]
    [InlineData("134.6")]
    [InlineData("134.7")]
    [InlineData("134.11")]
    [InlineData("134.12")]
    [InlineData("134.13")]
    [InlineData("134.14")]
    public void Every_message_the_a321_page_reads_is_needed(string path)
    {
        // The A321 also reads its center transfer valves (134.1), and not its center pumps (134.4, 134.5).
        var reported = new List<string>();

        FenixChannelRows window = FuelPush(new FuelBoxes { Variant = A321 }.Without(path)).Rows(reported.Add);

        Assert.Null(window.Problem);
        Assert.Equal(new[] { "FUEL", $"FUEL: not available, no field {path}" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { $"FUEL: no field {path}" }, reported);
    }

    [Fact]
    public void A_wrong_shape_stops_the_page_only()
    {
        // 134.12.1 (the left outer tank) arrives as a varint: the page is one row naming the field, its name and the
        // bottom line stay, and the problem is reported once, under the page's name.
        SdPush push = FuelPush(new FuelBoxes { Quantities = new FenixChannelProtoWriter().Varint(1, 1100) });
        var reported = new List<string>();

        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Equal(
            new[] { "FUEL", "FUEL: not available, field 134.12.1 has the wrong shape" }.Concat(BottomRows), rows.Rows);
        Assert.Equal(new[] { "FUEL: field 134.12.1 has the wrong shape" }, reported);
    }

    // ---- Culture --------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Fuel_rows_are_the_same_in_every_culture(string culture)
    {
        // Figures that round at a half and are thousands (1234.5, 12345.5), negatives (-52.5, -12.5) and one that rounds to
        // nothing (-0.4), and a crossfeed state with a minus sign (-7) whose report must stay as it is written: the PC's
        // thousands separator, decimal comma and sv-SE's true minus sign never reach the rows or the report, and "-0" is
        // never written.
        FenixChannelProtoWriter quantities =
            QuantityMessage(1234.5, -0.4, 12345.5, -52.5, 1202.5, 98765.5, 502.5, -404.5);
        FenixChannelProtoWriter temperatures = TemperatureMessage(-12.5, -0.4, 15.5, -52.5);
        FuelBoxes a320 = new()
        {
            Valves = ValveMessage(0, 1, 2, -7, 4, 1, 5, 0),
            Quantities = quantities,
            Used = UsedMessage(3000.1, 4000.2, 7219.5),
            Temperatures = temperatures
        };
        FuelBoxes a321 = a320 with { Variant = A321 };

        (IReadOnlyList<string> A320, IReadOnlyList<string> A321, IReadOnlyList<string> Reported) spoken =
            TestCulture.Run(culture, () =>
            {
                var reported = new List<string>();
                FenixChannelRows onA320 = FuelPush(a320).Rows(reported.Add);
                FenixChannelRows onA321 = FuelPush(a321).Rows(reported.Add);

                Assert.Null(onA320.Problem);
                Assert.Null(onA321.Problem);
                return (onA320.Rows!, onA321.Rows!, (IReadOnlyList<string>)reported);
            });

        Assert.Equal(
            new[]
            {
                "FUEL", "F.USED 1+2: 7220 KG", "APU valve: in transit", "LP valves: open, closed",
                "X FEED valve: not available", "left outer tank: 1235", "left inner tank: 0", "center tank: 12346",
                "right inner tank: -53", "right outer tank: 1203", "left pumps: running, LO", "center pumps: off, LO",
                "right pumps: off, running", "left transfer valves: failed closed, closed", "right transfer valves: XX, open",
                "FOB: 98766 KG", "outer tanks °C: -13, -53", "inner tanks °C: 0, 16"
            }.Concat(BottomRows),
            spoken.A320);
        Assert.Equal(
            new[]
            {
                "FUEL", "F.USED 1+2: 7220 KG", "APU valve: in transit", "LP valves: open, closed",
                "X FEED valve: not available", "left tank: 0", "center tank: 12346", "center transfer valves: open, closed",
                "ACT 1: 503", "ACT 2: -405", "right tank: -53", "left pumps: running, LO", "right pumps: off, running",
                "FOB: 98766 KG", "tanks °C: 0, 16"
            }.Concat(BottomRows),
            spoken.A321);
        Assert.Equal(
            new[] { "FUEL: field 134.11.4 has an unknown value -7", "FUEL: field 134.11.4 has an unknown value -7" },
            spoken.Reported);
    }

    // ---- Colors ---------------------------------------------------------------------------------

    private static FenixChannelProtoWriter ActPumpMessage(bool amber)
        => amber ? new FenixChannelProtoWriter().Bool(3, true) : new FenixChannelProtoWriter();

    // The pump of 134.<field> as the default boxes have it, drawn amber.
    private static FenixChannelProtoWriter AmberPump(int field) => field switch
    {
        2 => PumpMessage(true, false, amber: true),
        3 => PumpMessage(true, true, amber: true),
        4 => PumpMessage(false, true, amber: true),
        5 => PumpMessage(true, true, amber: true),
        6 => PumpMessage(false, false, amber: true),
        _ => PumpMessage(true, false, amber: true)
    };

    [Theory]
    [InlineData(2, "left pumps: running amber, LO")]
    [InlineData(3, "left pumps: running, LO amber")]
    [InlineData(4, "center pumps: off amber, LO")]
    [InlineData(5, "center pumps: off, LO amber")]
    [InlineData(6, "right pumps: off amber, running")]
    [InlineData(7, "right pumps: off, running amber")]
    public void A_pump_drawn_amber_says_amber_after_its_word_on_the_a320(int field, string row)
    {
        // Each pump on its own (134.n.2); the pump's own state (switch, low pressure) is the word before it.
        string prefix = row[..(row.IndexOf(':') + 1)];

        IReadOnlyList<string> page = PageOf(FuelPush(new FuelBoxes().WithPump(field, AmberPump(field))));

        Assert.Equal(Replaced(A320Page, (prefix, row)), page);
    }

    [Theory]
    [InlineData(2, "left pumps: running amber, LO")]
    [InlineData(3, "left pumps: running, LO amber")]
    [InlineData(6, "right pumps: off amber, running")]
    [InlineData(7, "right pumps: off, running amber")]
    public void A_pump_drawn_amber_says_amber_after_its_word_on_the_a321(int field, string row)
    {
        string prefix = row[..(row.IndexOf(':') + 1)];

        IReadOnlyList<string> page = PageOf(FuelPush(new FuelBoxes { Variant = A321 }.WithPump(field, AmberPump(field))));

        Assert.Equal(Replaced(A321Page(2), (prefix, row)), page);
    }

    [Fact]
    public void The_center_pumps_stay_unread_on_the_a321_even_when_amber()
    {
        IReadOnlyList<string> page = PageOf(FuelPush(new FuelBoxes { Variant = A321 }.WithPump(4, AmberPump(4))));

        Assert.Equal(A321Page(2), page);
    }

    [Theory]
    [InlineData(true, false, "LP valves: open amber, closed")]
    [InlineData(false, true, "LP valves: open, closed amber")]
    [InlineData(true, true, "LP valves: open amber, closed amber")]
    public void An_lp_valve_drawn_amber_says_amber_after_its_word(bool one, bool two, string row)
    {
        var boxes = new FuelBoxes { ValveAmber1 = one, ValveAmber2 = two };

        Assert.Equal(Replaced(A320Page, ("LP valves:", row)), PageOf(FuelPush(boxes)));
        Assert.Equal(
            Replaced(A321Page(2), ("LP valves:", row)),
            PageOf(FuelPush(boxes with { Variant = A321 })));
    }

    [Theory]
    // valve amber alone adds it
    [InlineData(true, false, false, false, false, false, "center transfer valves: open amber, closed")]
    [InlineData(false, true, false, false, false, false, "center transfer valves: open, closed amber")]
    // flow amber alone (the cruise push's shape) does not
    [InlineData(false, false, true, true, false, false, "center transfer valves: open, closed")]
    // flow and flow amber add it, per side
    [InlineData(false, false, true, false, true, false, "center transfer valves: open amber, closed")]
    [InlineData(false, false, false, true, false, true, "center transfer valves: open, closed amber")]
    // a flow without its color does not
    [InlineData(false, false, false, false, true, true, "center transfer valves: open, closed")]
    // valve amber and flow amber together say it once
    [InlineData(true, false, true, false, true, false, "center transfer valves: open amber, closed")]
    [InlineData(true, true, true, true, true, true, "center transfer valves: open amber, closed amber")]
    public void A_center_transfer_valve_or_its_flowing_flow_drawn_amber_says_amber_once(
        bool leftValve, bool rightValve, bool leftFlowAmber, bool rightFlowAmber, bool leftFlow, bool rightFlow, string row)
    {
        var boxes = new FuelBoxes
        {
            Variant = A321,
            JetPumps = JetPumpMessage(true, false, leftValve, rightValve, leftFlowAmber, rightFlowAmber, leftFlow, rightFlow)
        };

        Assert.Equal(Replaced(A321Page(2), ("center transfer valves:", row)), PageOf(FuelPush(boxes)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void The_act_pump_drawn_amber_gets_its_own_row_after_the_act_rows(int acts)
    {
        FenixVariant variant = A321 with { ActCount = acts };
        List<string> expected = [.. A321Page(acts)];
        expected.Insert(expected.IndexOf("right tank: 3790"), "ACT pump, amber");

        Assert.Equal(expected, PageOf(FuelPush(new FuelBoxes { Variant = variant, ActPump = ActPumpMessage(true) })));
        Assert.Equal(A321Page(acts), PageOf(FuelPush(new FuelBoxes { Variant = variant, ActPump = ActPumpMessage(false) })));
    }

    [Fact]
    public void The_act_pump_is_not_read_without_act_tanks_or_on_an_a320()
    {
        // No ACT tank: 134.15 is not read, so even a push without it composes, and its amber is never spoken.
        FenixVariant none = A321 with { ActCount = 0 };
        Assert.Equal(A321Page(0), PageOf(FuelPush(new FuelBoxes { Variant = none, ActPump = ActPumpMessage(true) })));
        Assert.Equal(A321Page(0), PageOf(FuelPush(new FuelBoxes { Variant = none }.Without("134.15"))));
        Assert.Equal(A320Page, PageOf(FuelPush(new FuelBoxes { ActPump = ActPumpMessage(true) })));
        Assert.Equal(A320Page, PageOf(FuelPush(new FuelBoxes().Without("134.15"))));
    }

    [Fact]
    public void A_missing_act_pump_message_stops_an_a321_with_act_tanks()
    {
        SdPush push = FuelPush(new FuelBoxes { Variant = A321 }.Without("134.15"));

        Assert.Equal("no field 134.15", FenixChannelSdPages.FuelRows(push.Context(_ => { })).Problem);
    }
}

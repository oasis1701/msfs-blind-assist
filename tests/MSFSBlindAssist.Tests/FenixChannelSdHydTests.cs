// Characterization tests for the Fenix SD's HYD page (FenixChannelSdPages.HydRows, seam ComposeHyd): the three hydraulic
// systems, the PTU, the pumps, the RAT, the shutoff (fire) valves and the reservoirs, in the SD's order, and what stops
// the page. The pushes are built with SdPush (FenixChannelSdWindowTests.cs), page 11, and every field number is written
// out here, never read from FenixChannelFields, so that a wrong number in the app fails a test.
//
// The contract: page 11 reads 132 (the PTU state) and 133 (the hydraulic state). The rows, every number whole: GREEN,
// BLUE and YELLOW pressure (133.1.1, 133.2.1, 133.3.1, "GREEN: 2988 PSI"); PTU (132, the four states measured in the sim:
// -1 "off", 0 "idle", 1 "yellow to green", 2 "green to yellow"; any other value reads "PTU: not available" and is
// reported as "PTU state <n> not known" without stopping the page); GREEN ENG 1 pump (133.4 switch, 133.5 output), BLUE
// ELEC pump (133.8.1 switch, 133.8.2 output, then ", OVHT" while 133.8.3), YELLOW ENG 2 pump (133.6, 133.7) and YELLOW
// ELEC pump (133.9.1, 133.9.2), each "off" without the switch, "LO" with the switch and no output, else "running"; RAT
// (133.12, "stowed", "deployed" while set); GREEN and YELLOW fire valve (133.10, 133.11, "open" while set, else "closed",
// inferred); and each reservoir (133.1.2, 133.2.2, 133.3.2: a whole number with no unit, then ", LO AIR PRESS" while
// 133.n.6 and ", OVHT" while 133.n.7). A message the rows read that the push leaves out (133, 133.1 to 133.3, 133.8,
// 133.9) stops this page only ("HYD: not available, no field 133.2"), never read as its defaults; one present but empty
// (the Fenix sends an empty yellow electric pump while it is off) is its defaults.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdHydTests
{
    // The bottom line of SdPush's default push, as the window reads it.
    private static readonly string[] BottomRows =
        ["TAT °C: +15, green", "SAT °C: +15, green", "UTC: 15:06", "GW KG: --, cyan"];

    // The page's own rows (after its name, before the bottom line) of HydPush's default push: the PTU left out, the green
    // and blue pumps running, the yellow engine pump low, the yellow electric pump off, the green valve open and the
    // yellow closed, the RAT stowed and no reservoir flag set.
    private static readonly string[] PlainPage =
    [
        "GREEN: 2988 PSI", "BLUE: 3012 PSI", "YELLOW: 2955 PSI", "PTU: idle", "GREEN ENG 1 pump: running",
        "BLUE ELEC pump: running", "YELLOW ENG 2 pump: LO", "YELLOW ELEC pump: off", "RAT: stowed",
        "GREEN fire valve: open", "YELLOW fire valve: closed", "GREEN reservoir: 14", "BLUE reservoir: 7",
        "YELLOW reservoir: 12"
    ];

    // The rows of a push that composes, and nothing reported.
    private static IReadOnlyList<string> RowsOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    // The page's own rows of a push that composes: what is between the name "HYD" and the bottom line.
    private static IReadOnlyList<string> PageOf(SdPush push)
    {
        IReadOnlyList<string> rows = RowsOf(push);

        Assert.Equal("HYD", rows[0]);
        Assert.Equal(BottomRows, rows.TakeLast(BottomRows.Length));
        return rows.Skip(1).SkipLast(BottomRows.Length).ToList();
    }

    // ---- Building the hydraulic state -----------------------------------------------------------

    // One hydraulic system (133.1 to 133.3): pressure (1), quantity (2), and the two reservoir flags (6, 7) while set.
    private static FenixChannelProtoWriter HydSystem(
        double pressure, double quantity, bool lowAirPressure = false, bool overheat = false)
    {
        var system = new FenixChannelProtoWriter().Double(1, pressure).Double(2, quantity);
        if (lowAirPressure) system.Bool(6, true);
        if (overheat) system.Bool(7, true);
        return system;
    }

    // An electric pump (133.8, 133.9): the switch (1), the output (2) and the overheat (3), each written only while set,
    // as the Fenix leaves out a false. All three clear is an empty message, which the Fenix sends while the pump is off.
    private static FenixChannelProtoWriter ElecPump(bool switchOn = false, bool output = false, bool overheat = false)
    {
        var pump = new FenixChannelProtoWriter();
        if (switchOn) pump.Bool(1, true);
        if (output) pump.Bool(2, true);
        if (overheat) pump.Bool(3, true);
        return pump;
    }

    // An engine pump's switch and output (133.4 and 133.5, or 133.6 and 133.7).
    private readonly record struct EnginePump(bool Switch, bool Output);

    // The hydraulic state of a HYD push, each part distinct from the others so that a row that reads another part's
    // field fails; a null message is left out of the push. The defaults are the plain page above. Values carry a fraction
    // that rounds (away from zero, at .5).
    private sealed record HydBoxes
    {
        public FenixChannelProtoWriter? Green { get; init; } = HydSystem(2987.6, 14.4);
        public FenixChannelProtoWriter? Blue { get; init; } = HydSystem(3012.4, 6.5);
        public FenixChannelProtoWriter? Yellow { get; init; } = HydSystem(2954.5, 11.6);
        public EnginePump GreenEnginePump { get; init; } = new(true, true);
        public EnginePump YellowEnginePump { get; init; } = new(true, false);
        public FenixChannelProtoWriter? BlueElecPump { get; init; } = ElecPump(switchOn: true, output: true);
        public FenixChannelProtoWriter? YellowElecPump { get; init; } = ElecPump();
        public bool GreenShutoff { get; init; } = true;
        public bool YellowShutoff { get; init; }
        public bool Rat { get; init; }

        // Whether 133 is in the push at all.
        public bool HasHydraulicState { get; init; } = true;

        // The PTU state (132); null leaves the field out, as the Fenix does for the idle value. A negative state is a
        // negative varint (sign-extended to 64 bits), as protobuf writes an int32.
        public int? Ptu { get; init; }

        // The same boxes with the message at <path> ("133", "133.1" to "133.3", "133.8", "133.9") left out of the push.
        public HydBoxes Without(string path) => path switch
        {
            "133" => this with { HasHydraulicState = false },
            "133.1" => this with { Green = null },
            "133.2" => this with { Blue = null },
            "133.3" => this with { Yellow = null },
            "133.8" => this with { BlueElecPump = null },
            "133.9" => this with { YellowElecPump = null },
            _ => throw new ArgumentOutOfRangeException(nameof(path), path, "no such message")
        };
    }

    // A HYD page push (page 11): the PTU state while it is not null, then the hydraulic state, its fields in the order of
    // their numbers.
    private static SdPush HydPush(HydBoxes? boxes = null)
    {
        HydBoxes b = boxes ?? new HydBoxes();
        return new SdPush { Page = 11 }.With(remote =>
        {
            if (b.Ptu is { } ptu) remote.Varint(132, unchecked((ulong)(long)ptu));
            if (!b.HasHydraulicState) return;

            var hydraulic = new FenixChannelProtoWriter();
            Write(hydraulic, 1, b.Green);
            Write(hydraulic, 2, b.Blue);
            Write(hydraulic, 3, b.Yellow);
            if (b.GreenEnginePump.Switch) hydraulic.Bool(4, true);
            if (b.GreenEnginePump.Output) hydraulic.Bool(5, true);
            if (b.YellowEnginePump.Switch) hydraulic.Bool(6, true);
            if (b.YellowEnginePump.Output) hydraulic.Bool(7, true);
            Write(hydraulic, 8, b.BlueElecPump);
            Write(hydraulic, 9, b.YellowElecPump);
            if (b.GreenShutoff) hydraulic.Bool(10, true);
            if (b.YellowShutoff) hydraulic.Bool(11, true);
            if (b.Rat) hydraulic.Bool(12, true);
            remote.Message(133, hydraulic);
        });
    }

    private static void Write(FenixChannelProtoWriter message, int field, FenixChannelProtoWriter? part)
    {
        if (part is not null) message.Message(field, part);
    }

    // ---- The rows -------------------------------------------------------------------------------

    [Fact]
    public void The_hyd_page_reads_the_three_systems()
    {
        // The page's name, the three pressures, the PTU (field 132 left out, which is its default, idle), the four pumps,
        // the RAT, the two fire valves, the three reservoirs, then the bottom line. Each system, pump and valve has
        // values of its own, and the figures round at a half.
        Assert.Equal(new[] { "HYD" }.Concat(PlainPage).Concat(BottomRows), RowsOf(HydPush()));
    }

    [Fact]
    public void Pump_words_follow_switch_and_output()
    {
        // The word of a pump row is its switch and its output only: "off" without the switch (whatever the output), "LO"
        // with the switch and no output, else "running". Each pump reads its own two fields: they are given all four
        // combinations in turn, each pump a different one at a time.
        (bool Switch, bool Output, string Word)[] combinations =
            [(false, false, "off"), (false, true, "off"), (true, false, "LO"), (true, true, "running")];

        for (int rotation = 0; rotation < combinations.Length; rotation++)
        {
            var green = combinations[rotation % 4];
            var blue = combinations[(rotation + 1) % 4];
            var yellowEngine = combinations[(rotation + 2) % 4];
            var yellowElec = combinations[(rotation + 3) % 4];

            IReadOnlyList<string> rows = PageOf(HydPush(new HydBoxes
            {
                GreenEnginePump = new EnginePump(green.Switch, green.Output),
                BlueElecPump = ElecPump(blue.Switch, blue.Output),
                YellowEnginePump = new EnginePump(yellowEngine.Switch, yellowEngine.Output),
                YellowElecPump = ElecPump(yellowElec.Switch, yellowElec.Output)
            }));

            Assert.Contains($"GREEN ENG 1 pump: {green.Word}", rows);
            Assert.Contains($"BLUE ELEC pump: {blue.Word}", rows);
            Assert.Contains($"YELLOW ENG 2 pump: {yellowEngine.Word}", rows);
            Assert.Contains($"YELLOW ELEC pump: {yellowElec.Word}", rows);
        }
    }

    [Fact]
    public void The_blue_electric_pump_says_ovht_by_its_own_flag()
    {
        // 133.8.3 adds ", OVHT" to the blue electric pump's row, after its word, whatever the word is; no other row
        // changes.
        IReadOnlyList<string> running = PageOf(HydPush(new HydBoxes
        {
            BlueElecPump = ElecPump(switchOn: true, output: true, overheat: true)
        }));
        IReadOnlyList<string> off = PageOf(HydPush(new HydBoxes { BlueElecPump = ElecPump(overheat: true) }));

        Assert.Equal(PlainPage.Select(row => row == "BLUE ELEC pump: running" ? "BLUE ELEC pump: running, OVHT" : row), running);
        Assert.Equal(PlainPage.Select(row => row == "BLUE ELEC pump: running" ? "BLUE ELEC pump: off, OVHT" : row), off);
    }

    [Fact]
    public void The_rat_and_the_fire_valves_follow_their_own_fields()
    {
        // 133.12 is the RAT, 133.10 the green valve and 133.11 the yellow one, open while set: the opposite of the plain
        // page's states, then both set.
        IReadOnlyList<string> opposite = PageOf(HydPush(new HydBoxes { Rat = true, GreenShutoff = false, YellowShutoff = true }));
        IReadOnlyList<string> both = PageOf(HydPush(new HydBoxes { GreenShutoff = true, YellowShutoff = true }));

        Assert.Equal(
            PlainPage.Select(row => row switch
            {
                "RAT: stowed" => "RAT: deployed",
                "GREEN fire valve: open" => "GREEN fire valve: closed",
                "YELLOW fire valve: closed" => "YELLOW fire valve: open",
                _ => row
            }),
            opposite);
        Assert.Equal(PlainPage.Select(row => row == "YELLOW fire valve: closed" ? "YELLOW fire valve: open" : row), both);
    }

    [Fact]
    public void The_reservoir_words_follow_their_own_system()
    {
        // Green low air pressure (133.1.6), blue overheat (133.2.7), yellow both: the words come after the quantity, low
        // air pressure first, and a system's flag never reaches another system's row.
        IReadOnlyList<string> rows = PageOf(HydPush(new HydBoxes
        {
            Green = HydSystem(2987.6, 14.4, lowAirPressure: true),
            Blue = HydSystem(3012.4, 6.5, overheat: true),
            Yellow = HydSystem(2954.5, 11.6, lowAirPressure: true, overheat: true)
        }));

        Assert.Equal(
            PlainPage.Select(row => row switch
            {
                "GREEN reservoir: 14" => "GREEN reservoir: 14, LO AIR PRESS",
                "BLUE reservoir: 7" => "BLUE reservoir: 7, OVHT",
                "YELLOW reservoir: 12" => "YELLOW reservoir: 12, LO AIR PRESS, OVHT",
                _ => row
            }),
            rows);
    }

    [Fact]
    public void An_empty_electric_pump_message_is_the_pump_off()
    {
        // The Fenix sends an empty message for an electric pump that is off: present, so its defaults, and not a missing
        // part of the page.
        IReadOnlyList<string> rows = PageOf(HydPush(new HydBoxes
        {
            BlueElecPump = ElecPump(),
            YellowElecPump = ElecPump()
        }));

        Assert.Contains("BLUE ELEC pump: off", rows);
        Assert.Contains("YELLOW ELEC pump: off", rows);
    }

    [Fact]
    public void A_value_that_is_not_a_number_reads_xx()
    {
        // The SD draws crosses for a number it has none for, and the channel sends NaN.
        IReadOnlyList<string> rows = PageOf(HydPush(new HydBoxes
        {
            Green = HydSystem(double.NaN, 14.4),
            Blue = HydSystem(3012.4, double.PositiveInfinity)
        }));

        Assert.Contains("GREEN: XX PSI", rows);
        Assert.Contains("BLUE reservoir: XX", rows);
        Assert.Contains("GREEN reservoir: 14", rows);
    }

    // ---- The PTU --------------------------------------------------------------------------------

    [Theory]
    [InlineData(-1, "PTU: off")]                    // PTU pushbutton off with both engines running; AUTO with the parking brake set and one engine master on (the inhibit)
    [InlineData(0, "PTU: idle")]                    // normal, both engines running, nothing to transfer (the Fenix leaves the field out)
    [InlineData(1, "PTU: yellow to green")]         // both engines off with the yellow electric pump on, and engine 1 starting with engine 2 running
    [InlineData(2, "PTU: green to yellow")]         // about 50 s after engine 1's master went off with engine 2 already off
    public void Each_measured_ptu_state_reads_its_word(int state, string row)
    {
        // The field is written out here, as a value the Fenix sends (the page test above leaves it out); -1 goes out as a
        // negative varint, the way protobuf writes an int32.
        IReadOnlyList<string> rows = PageOf(HydPush(new HydBoxes { Ptu = state }));

        Assert.Equal(PlainPage.Select(plain => plain.StartsWith("PTU:", StringComparison.Ordinal) ? row : plain), rows);
    }

    [Theory]
    [InlineData(3, "PTU state 3 not known")]
    [InlineData(1234, "PTU state 1234 not known")]
    [InlineData(-2, "PTU state -2 not known")]
    [InlineData(-7, "PTU state -7 not known")]
    public void An_unmeasured_ptu_state_reads_not_available_and_is_reported(int state, string report)
    {
        // Any value but the four measured ones is a state the app does not know: the row says so, the rest of the page
        // stays, and the problem is reported without stopping the page (at page level, as the page's context gives it; the
        // window adds the page's name).
        SdPush push = HydPush(new HydBoxes { Ptu = state });
        var reported = new List<string>();
        var windowReported = new List<string>();

        FenixChannelRows page = FenixChannelSdPages.HydRows(push.Context(reported.Add));
        FenixChannelRows window = push.Rows(windowReported.Add);

        Assert.Null(page.Problem);
        Assert.Equal(PlainPage.Select(plain => plain.StartsWith("PTU:", StringComparison.Ordinal) ? "PTU: not available" : plain), page.Rows);
        Assert.Equal(new[] { report }, reported);
        Assert.Null(window.Problem);
        Assert.Equal(
            new[] { "HYD" }
                .Concat(PlainPage.Select(plain => plain.StartsWith("PTU:", StringComparison.Ordinal) ? "PTU: not available" : plain))
                .Concat(BottomRows),
            window.Rows);
        Assert.Equal(new[] { $"HYD: {report}" }, windowReported);
    }

    // ---- What stops the page --------------------------------------------------------------------

    [Fact]
    public void A_missing_system_is_not_available()
    {
        // 133.2 (the blue system) is left out: the page never reads blue at 0 PSI. The page returns the problem and does
        // not report it; the window reports it once, under the page's name.
        SdPush push = HydPush(new HydBoxes { Blue = null });
        var returned = new List<string>();
        var reported = new List<string>();

        FenixChannelRows page = FenixChannelSdPages.HydRows(push.Context(returned.Add));
        FenixChannelRows window = push.Rows(reported.Add);

        Assert.Equal("no field 133.2", page.Problem);
        Assert.Null(page.Rows);
        Assert.Empty(returned);
        Assert.Null(window.Problem);
        Assert.Equal(new[] { "HYD", "HYD: not available, no field 133.2" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { "HYD: no field 133.2" }, reported);
    }

    [Theory]
    [InlineData("133")]
    [InlineData("133.1")]
    [InlineData("133.3")]
    [InlineData("133.8")]
    [InlineData("133.9")]
    public void Every_other_message_the_page_reads_is_needed_too(string path)
    {
        var reported = new List<string>();

        FenixChannelRows window = HydPush(new HydBoxes().Without(path)).Rows(reported.Add);

        Assert.Equal(new[] { "HYD", $"HYD: not available, no field {path}" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { $"HYD: no field {path}" }, reported);
    }

    [Fact]
    public void A_stopped_page_reports_no_ptu_state()
    {
        // The PTU state is reported only for a page that is shown: with the blue system left out the page is one row, and
        // the log gets that problem alone.
        var reported = new List<string>();

        FenixChannelRows window = HydPush(new HydBoxes { Blue = null, Ptu = 5 }).Rows(reported.Add);

        Assert.Equal(new[] { "HYD", "HYD: not available, no field 133.2" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { "HYD: no field 133.2" }, reported);
    }

    [Fact]
    public void A_wrong_shape_stops_the_page_only()
    {
        // 133.1.1 (the green pressure) arrives as a varint: the page is one row naming the field, its name and the bottom
        // line stay, and the problem is reported once, under the page's name.
        SdPush push = HydPush(new HydBoxes { Green = new FenixChannelProtoWriter().Varint(1, 3000) });
        var reported = new List<string>();

        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Equal(
            new[] { "HYD", "HYD: not available, field 133.1.1 has the wrong shape" }.Concat(BottomRows), rows.Rows);
        Assert.Equal(new[] { "HYD: field 133.1.1 has the wrong shape" }, reported);
    }

    // ---- Culture --------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Hyd_rows_are_the_same_in_every_culture(string culture)
    {
        // Figures that round at a half (2987.5, -2.5, 13.5) and a negative that rounds to nothing (-0.4), the reservoir
        // words, and a PTU state with a minus sign (-7) whose report must stay as it is written: the PC's decimal comma
        // and sv-SE's true minus sign never reach the rows or the report, and "-0" is never written.
        SdPush push = HydPush(new HydBoxes
        {
            Green = HydSystem(2987.5, 13.5, lowAirPressure: true),
            Blue = HydSystem(-12.5, -2.5, overheat: true),
            Yellow = HydSystem(-0.4, 11.6),
            Ptu = -7
        });

        (IReadOnlyList<string> Rows, IReadOnlyList<string> Reported) spoken = TestCulture.Run(culture, () =>
        {
            var reported = new List<string>();
            FenixChannelRows rows = push.Rows(reported.Add);

            Assert.Null(rows.Problem);
            return (rows.Rows!, (IReadOnlyList<string>)reported);
        });

        Assert.Equal(
            new[]
            {
                "HYD", "GREEN: 2988 PSI", "BLUE: -13 PSI", "YELLOW: 0 PSI", "PTU: not available",
                "GREEN ENG 1 pump: running", "BLUE ELEC pump: running", "YELLOW ENG 2 pump: LO", "YELLOW ELEC pump: off",
                "RAT: stowed", "GREEN fire valve: open", "YELLOW fire valve: closed",
                "GREEN reservoir: 14, LO AIR PRESS", "BLUE reservoir: -3, OVHT", "YELLOW reservoir: 12"
            }.Concat(BottomRows),
            spoken.Rows);
        Assert.Equal(new[] { "HYD: PTU state -7 not known" }, spoken.Reported);
    }
}

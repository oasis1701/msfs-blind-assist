// Characterization tests for the Fenix SD's ELEC page (FenixChannelSdPages.ElecRows, seam ComposeElec): the boxes of the
// electrical system as the SD draws them, in the SD's order, and what stops the page. The pushes are built with SdPush
// (FenixChannelSdWindowTests.cs), page 7, and every field number is written out here, never read from
// FenixChannelFields, so that a wrong number in the app fails a test.
//
// The contract: every number is whole. Page 7 reads 16 (the flags) to 26 (battery 2): BAT 1 (25: volts 1, amps 2, then
// ", charging" while 16.2), DC BAT (17.6), BAT 2 (26, 16.3), DC 1 (17.1), DC ESS (17.3), DC 2 (17.2), AC 1 (17.4), AC ESS
// (17.7), AC 2 (17.5), TR 1 (20), ESS TR (22), TR 2 (21), GEN 1 (18: load, volts, frequency, "OFF" while 18.4 is clear), APU
// GEN (24: while 24.12 the same three, or "OFF" while 24.14 is set, else the legend alone), EXT PWR (23: volts,
// frequency, only while 17.8), GEN 2 (19), IDG 1 (18.5, then ", LO PR" while 16.6 and ", DISC" while 16.4), GALLEY SHED
// (only while 16.1), IDG 2 (19.5, 16.7, 16.5). A bus reads "powered" or "not powered". EMER GEN and STAT INV have no
// field, so no row. A box the push leaves out stops this page only ("ELEC: not available, no field 17"), never read as
// its defaults; a box present but empty (the Fenix sends an empty elecFlags while every flag is false) is its defaults.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdElecTests
{
    // The bottom line of SdPush's default push, as the window reads it.
    private static readonly string[] BottomRows =
        ["TAT °C: +15, green", "SAT °C: +15, green", "UTC: 15:06", "GW KG: --, cyan"];

    // The page's own rows (after its name, before the bottom line) of ElecPush's default push: no external power, no
    // shed, no flag set.
    private static readonly string[] PlainPage =
    [
        "BAT 1: 27 V, 0 A", "DC BAT: powered", "BAT 2: 26 V, 5 A", "DC 1: powered", "DC ESS: powered",
        "DC 2: not powered", "AC 1: powered", "AC ESS: not powered", "AC 2: not powered", "TR 1: 28 V, 12 A",
        "ESS TR: 30 V, 4 A", "TR 2: 29 V, 9 A", "GEN 1: 25 %, 115 V, 400 HZ", "APU GEN: 13 %, 116 V, 399 HZ",
        "GEN 2: 32 %, 114 V, 402 HZ", "IDG 1: 99 °C", "IDG 2: 88 °C"
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

    // The page's own rows of a push that composes: what is between the name "ELEC" and the bottom line.
    private static IReadOnlyList<string> PageOf(SdPush push)
    {
        IReadOnlyList<string> rows = RowsOf(push);

        Assert.Equal("ELEC", rows[0]);
        Assert.Equal(BottomRows, rows.TakeLast(BottomRows.Length));
        return rows.Skip(1).SkipLast(BottomRows.Length).ToList();
    }

    // ---- Building the boxes ---------------------------------------------------------------------

    // The flags (16), each written only while set, as the Fenix leaves out a false.
    private static FenixChannelProtoWriter Flags(
        bool galleyShed = false, bool bat1Charging = false, bool bat2Charging = false, bool gen1Disconnected = false,
        bool gen2Disconnected = false, bool gen1LowOilPressure = false, bool gen2LowOilPressure = false)
    {
        var flags = new FenixChannelProtoWriter();
        if (galleyShed) flags.Bool(1, true);
        if (bat1Charging) flags.Bool(2, true);
        if (bat2Charging) flags.Bool(3, true);
        if (gen1Disconnected) flags.Bool(4, true);
        if (gen2Disconnected) flags.Bool(5, true);
        if (gen1LowOilPressure) flags.Bool(6, true);
        if (gen2LowOilPressure) flags.Bool(7, true);
        return flags;
    }

    // The bus status (17) with the buses named by field number set: 1 DC 1, 2 DC 2, 3 DC ESS, 4 AC 1, 5 AC 2, 6 DC BAT,
    // 7 AC ESS, 8 external power connected. The others are left out, which is false.
    private static FenixChannelProtoWriter Buses(params int[] powered)
    {
        var buses = new FenixChannelProtoWriter();
        foreach (int field in powered)
        {
            buses.Bool(field, true);
        }

        return buses;
    }

    // One engine generator (18, 19): load, volts and frequency, the selector (4) while on, the IDG temperature (5).
    private static FenixChannelProtoWriter Generator(double load, double volts, double frequency, bool on, double temperature)
    {
        var generator = new FenixChannelProtoWriter().Double(1, load).Double(2, volts).Double(3, frequency);
        if (on) generator.Bool(4, true);
        return generator.Double(5, temperature);
    }

    // A transformer-rectifier or a battery (20, 21, 22, 25, 26).
    private static FenixChannelProtoWriter VoltAmps(double volts, double amps)
        => new FenixChannelProtoWriter().Double(1, volts).Double(2, amps);

    // The external power (23): volts and frequency.
    private static FenixChannelProtoWriter ExtPwr(double volts, double frequency)
        => new FenixChannelProtoWriter().Double(1, volts).Double(2, frequency);

    // The APU (24): its generator's load, volts and frequency, an N1 the page does not read, the master (12) while on and
    // the APU GEN pushbutton's off (14) while set.
    private static FenixChannelProtoWriter Apu(
        bool master, double load, double volts, double frequency, bool off = false)
    {
        var apu = new FenixChannelProtoWriter().Double(1, load).Double(2, volts).Double(3, frequency).Double(4, 100);
        if (master) apu.Bool(12, true);
        if (off) apu.Bool(14, true);
        return apu;
    }

    // The nine boxes of an ELEC push, each distinct from the others so that a row that reads another box's field fails; a
    // null box is left out of the push. The defaults are a plain ELEC page: every flag false (an empty elecFlags, as the
    // Fenix sends it), external power present but not connected, every generator on, the APU master on. Values carry a
    // fraction that rounds (away from zero, at .5), so that a row that does not round fails too.
    private sealed record ElecBoxes
    {
        public FenixChannelProtoWriter? Flags { get; init; } = FenixChannelSdElecTests.Flags();
        public FenixChannelProtoWriter? Buses { get; init; } = FenixChannelSdElecTests.Buses(1, 3, 4, 6);
        public FenixChannelProtoWriter? Gen1 { get; init; } = Generator(25.4, 115.4, 400.4, true, 98.6);
        public FenixChannelProtoWriter? Gen2 { get; init; } = Generator(31.5, 113.6, 401.5, true, 87.5);
        public FenixChannelProtoWriter? Tr1 { get; init; } = VoltAmps(28.4, 11.6);
        public FenixChannelProtoWriter? Tr2 { get; init; } = VoltAmps(28.6, 9.4);
        public FenixChannelProtoWriter? EssTr { get; init; } = VoltAmps(30.4, 3.7);
        public FenixChannelProtoWriter? ExtPwr { get; init; } = FenixChannelSdElecTests.ExtPwr(112.4, 405.2);
        public FenixChannelProtoWriter? Apu { get; init; } = FenixChannelSdElecTests.Apu(true, 12.5, 116.4, 398.5);
        public FenixChannelProtoWriter? Bat1 { get; init; } = VoltAmps(27.4, 0.3);
        public FenixChannelProtoWriter? Bat2 { get; init; } = VoltAmps(25.6, 4.5);

        // The same boxes with the one at <field> (16 to 26) left out of the push.
        public ElecBoxes Without(int field) => field switch
        {
            16 => this with { Flags = null },
            17 => this with { Buses = null },
            18 => this with { Gen1 = null },
            19 => this with { Gen2 = null },
            20 => this with { Tr1 = null },
            21 => this with { Tr2 = null },
            22 => this with { EssTr = null },
            23 => this with { ExtPwr = null },
            24 => this with { Apu = null },
            25 => this with { Bat1 = null },
            26 => this with { Bat2 = null },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    // An ELEC page push (page 7): each box that is not null, in the order of its field number.
    private static SdPush ElecPush(ElecBoxes? boxes = null)
    {
        ElecBoxes b = boxes ?? new ElecBoxes();
        return new SdPush { Page = 7 }.With(remote =>
        {
            Write(remote, 16, b.Flags);
            Write(remote, 17, b.Buses);
            Write(remote, 18, b.Gen1);
            Write(remote, 19, b.Gen2);
            Write(remote, 20, b.Tr1);
            Write(remote, 21, b.Tr2);
            Write(remote, 22, b.EssTr);
            Write(remote, 23, b.ExtPwr);
            Write(remote, 24, b.Apu);
            Write(remote, 25, b.Bat1);
            Write(remote, 26, b.Bat2);
        });
    }

    private static void Write(FenixChannelProtoWriter remote, int field, FenixChannelProtoWriter? box)
    {
        if (box is not null) remote.Message(field, box);
    }

    // ---- The rows -------------------------------------------------------------------------------

    [Fact]
    public void The_elec_page_reads_every_box_in_the_sds_order()
    {
        // No external power and no shed: the page's name, the seventeen boxes in the SD's order, then the bottom line. The
        // flags are an empty message (every flag false), the buses are a mix, and every box has values of its own.
        Assert.Equal(new[] { "ELEC" }.Concat(PlainPage).Concat(BottomRows), RowsOf(ElecPush()));
    }

    [Theory]
    [InlineData(1, "DC 1")]
    [InlineData(2, "DC 2")]
    [InlineData(3, "DC ESS")]
    [InlineData(4, "AC 1")]
    [InlineData(5, "AC 2")]
    [InlineData(6, "DC BAT")]
    [InlineData(7, "AC ESS")]
    public void Each_bus_reads_its_own_flag(int field, string label)
    {
        // Only the bus whose field is set is powered: no row reads another bus's flag.
        IReadOnlyList<string> rows = PageOf(ElecPush(new ElecBoxes { Buses = Buses(field) }));

        Assert.Equal(new[] { $"{label}: powered" }, rows.Where(row => row.EndsWith(": powered", StringComparison.Ordinal)));
        Assert.Equal(6, rows.Count(row => row.EndsWith(": not powered", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_battery_says_charging_by_its_own_flag()
    {
        // 16.2 is battery 1 and 16.3 is battery 2: the word follows the figures, after the amps.
        IReadOnlyList<string> first = PageOf(ElecPush(new ElecBoxes { Flags = Flags(bat1Charging: true) }));
        IReadOnlyList<string> second = PageOf(ElecPush(new ElecBoxes { Flags = Flags(bat2Charging: true) }));

        Assert.Contains("BAT 1: 27 V, 0 A, charging", first);
        Assert.Contains("BAT 2: 26 V, 5 A", first);
        Assert.Contains("BAT 1: 27 V, 0 A", second);
        Assert.Contains("BAT 2: 26 V, 5 A, charging", second);
    }

    [Fact]
    public void A_generator_switched_off_reads_off()
    {
        // The selector (18.4, 19.4) clear: the row is "OFF", whatever values the push carries, and the other generator
        // and both IDGs are unaffected.
        IReadOnlyList<string> first = PageOf(ElecPush(new ElecBoxes { Gen1 = Generator(25.4, 115.4, 400.4, false, 98.6) }));
        IReadOnlyList<string> second = PageOf(ElecPush(new ElecBoxes { Gen2 = Generator(31.5, 113.6, 401.5, false, 87.5) }));

        Assert.Equal(
            PlainPage.Select(row => row.StartsWith("GEN 1:", StringComparison.Ordinal) ? "GEN 1: OFF" : row), first);
        Assert.Equal(
            PlainPage.Select(row => row.StartsWith("GEN 2:", StringComparison.Ordinal) ? "GEN 2: OFF" : row), second);
    }

    [Fact]
    public void Ext_pwr_and_galley_shed_show_only_while_drawn()
    {
        // The push always carries the external power's values (23), yet the row shows only while 17.8 says it is
        // connected, between APU GEN and GEN 2; GALLEY SHED shows only while 16.1 is set, between the IDGs.
        var withExt = PlainPage.ToList();
        withExt.Insert(withExt.IndexOf("GEN 2: 32 %, 114 V, 402 HZ"), "EXT PWR: 112 V, 405 HZ");
        var withShed = PlainPage.ToList();
        withShed.Insert(withShed.IndexOf("IDG 2: 88 °C"), "GALLEY SHED");
        var withBoth = withExt.ToList();
        withBoth.Insert(withBoth.IndexOf("IDG 2: 88 °C"), "GALLEY SHED");

        Assert.Equal(withExt, PageOf(ElecPush(new ElecBoxes { Buses = Buses(1, 3, 4, 6, 8) })));
        Assert.Equal(withShed, PageOf(ElecPush(new ElecBoxes { Flags = Flags(galleyShed: true) })));
        Assert.Equal(
            withBoth, PageOf(ElecPush(new ElecBoxes { Buses = Buses(1, 3, 4, 6, 8), Flags = Flags(galleyShed: true) })));
        Assert.Equal(PlainPage, PageOf(ElecPush()));
    }

    [Fact]
    public void The_external_power_box_is_needed_only_while_it_is_drawn()
    {
        // Left out while 17.8 is clear it is nothing to show; left out while 17.8 is set, it is a box the page needs: not
        // available, never external power at 0 V.
        Assert.Equal(PlainPage, PageOf(ElecPush(new ElecBoxes { ExtPwr = null })));

        var reported = new List<string>();
        FenixChannelRows window = ElecPush(new ElecBoxes { Buses = Buses(1, 3, 4, 6, 8), ExtPwr = null }).Rows(reported.Add);

        Assert.Equal(new[] { "ELEC", "ELEC: not available, no field 23" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { "ELEC: no field 23" }, reported);
    }

    [Fact]
    public void Apu_gen_shows_values_only_with_the_apu_master_on()
    {
        // 24.12 set: the generator's load, volts and frequency. Clear (left out, as the Fenix sends a false), the legend
        // alone, though the push carries the figures; an empty APU message is the master off too.
        IReadOnlyList<string> on = PageOf(ElecPush());
        IReadOnlyList<string> off = PageOf(ElecPush(new ElecBoxes { Apu = Apu(false, 12.5, 116.4, 398.5) }));
        IReadOnlyList<string> empty = PageOf(ElecPush(new ElecBoxes { Apu = new FenixChannelProtoWriter() }));

        Assert.Contains("APU GEN: 13 %, 116 V, 399 HZ", on);
        Assert.Equal(PlainPage.Select(row => row.StartsWith("APU GEN", StringComparison.Ordinal) ? "APU GEN" : row), off);
        Assert.Equal(off, empty);
    }

    [Fact]
    public void Apu_gen_reads_off_while_its_pushbutton_is_off()
    {
        // The APU running with the APU GEN pushbutton off: the Fenix sets 24.14 and the SD shows the generator OFF, yet the
        // push still carries the figures (measured in the sim: 0 %, 115 V, 399 HZ). The row is "APU GEN: OFF", as "GEN 1:
        // OFF" is, and no other row changes; a row that ignores 24.14 reads the figures.
        IReadOnlyList<string> rows = PageOf(ElecPush(new ElecBoxes { Apu = Apu(true, 12.5, 116.4, 398.5, off: true) }));

        Assert.Equal(
            PlainPage.Select(row => row.StartsWith("APU GEN", StringComparison.Ordinal) ? "APU GEN: OFF" : row), rows);
    }

    [Fact]
    public void Apu_gen_off_without_the_master_leaves_the_legend()
    {
        // 24.14 is read only while the master (24.12) is on: with the master clear the row is the bare legend, the SD's
        // own, whether 24.14 is set or even in the wrong shape (a double): a field the page does not read can neither
        // stop the page nor be reported.
        IReadOnlyList<string> off = PageOf(ElecPush(new ElecBoxes { Apu = Apu(false, 12.5, 116.4, 398.5, off: true) }));
        var wrongShape = new FenixChannelProtoWriter().Double(1, 12.5).Double(14, 1);
        IReadOnlyList<string> unread = PageOf(ElecPush(new ElecBoxes { Apu = wrongShape }));

        IEnumerable<string> legend =
            PlainPage.Select(row => row.StartsWith("APU GEN", StringComparison.Ordinal) ? "APU GEN" : row);
        Assert.Equal(legend, off);
        Assert.Equal(legend, unread);
    }

    [Fact]
    public void A_missing_apu_message_is_not_available_never_the_legend()
    {
        // The APU row is on every ELEC page, so the page needs field 24: left out, it is not the master off.
        var reported = new List<string>();

        FenixChannelRows window = ElecPush(new ElecBoxes { Apu = null }).Rows(reported.Add);

        Assert.Equal(new[] { "ELEC", "ELEC: not available, no field 24" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { "ELEC: no field 24" }, reported);
    }

    [Fact]
    public void Idg_words_follow_the_temperature()
    {
        // The IDG's row is its temperature (18.5, 19.5), then ", LO PR" for the generator's low oil pressure (16.6, 16.7)
        // and ", DISC" for its disconnection (16.4, 16.5), in that order, and each generator's own words.
        IReadOnlyList<string> both = PageOf(ElecPush(new ElecBoxes
        {
            Flags = Flags(gen1LowOilPressure: true, gen1Disconnected: true)
        }));
        IReadOnlyList<string> lowPressure = PageOf(ElecPush(new ElecBoxes { Flags = Flags(gen1LowOilPressure: true) }));
        IReadOnlyList<string> disconnected = PageOf(ElecPush(new ElecBoxes { Flags = Flags(gen1Disconnected: true) }));
        IReadOnlyList<string> second = PageOf(ElecPush(new ElecBoxes { Flags = Flags(gen2LowOilPressure: true, gen2Disconnected: true) }));

        Assert.Contains("IDG 1: 99 °C, LO PR, DISC", both);
        Assert.Contains("IDG 2: 88 °C", both);
        Assert.Contains("IDG 1: 99 °C, LO PR", lowPressure);
        Assert.Contains("IDG 1: 99 °C, DISC", disconnected);
        Assert.Contains("IDG 1: 99 °C", second);
        Assert.Contains("IDG 2: 88 °C, LO PR, DISC", second);
    }

    [Fact]
    public void A_value_that_is_not_a_number_reads_xx()
    {
        // The SD draws crosses for a number it has none for, and the channel sends NaN.
        IReadOnlyList<string> rows = PageOf(ElecPush(new ElecBoxes { Bat1 = VoltAmps(double.NaN, 0.3) }));

        Assert.Contains("BAT 1: XX V, 0 A", rows);
    }

    // ---- What stops the page --------------------------------------------------------------------

    [Fact]
    public void A_missing_box_is_not_available_never_zero()
    {
        // 17 (the buses) is left out: the page never reads every bus as not powered. The page returns the problem and does
        // not report it; the window reports it once, under the page's name.
        SdPush push = ElecPush(new ElecBoxes { Buses = null });
        var returned = new List<string>();
        var reported = new List<string>();

        FenixChannelRows page = FenixChannelSdPages.ElecRows(push.Context(returned.Add));
        FenixChannelRows window = push.Rows(reported.Add);

        Assert.Equal("no field 17", page.Problem);
        Assert.Null(page.Rows);
        Assert.Empty(returned);
        Assert.Null(window.Problem);
        Assert.Equal(new[] { "ELEC", "ELEC: not available, no field 17" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { "ELEC: no field 17" }, reported);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(25)]
    [InlineData(26)]
    public void Every_other_box_the_page_reads_is_needed_too(int field)
    {
        var reported = new List<string>();

        FenixChannelRows window = ElecPush(new ElecBoxes().Without(field)).Rows(reported.Add);

        Assert.Equal(new[] { "ELEC", $"ELEC: not available, no field {field}" }.Concat(BottomRows), window.Rows);
        Assert.Equal(new[] { $"ELEC: no field {field}" }, reported);
    }

    [Fact]
    public void A_wrong_shape_stops_the_page_only()
    {
        // 17.6 (DC BAT) arrives as a double: the page is one row naming the field, its name and the bottom line stay, and
        // the problem is reported once, under the page's name.
        SdPush push = ElecPush(new ElecBoxes { Buses = new FenixChannelProtoWriter().Double(6, 1) });
        var reported = new List<string>();

        FenixChannelRows rows = push.Rows(reported.Add);

        Assert.Null(rows.Problem);
        Assert.Equal(
            new[] { "ELEC", "ELEC: not available, field 17.6 has the wrong shape" }.Concat(BottomRows), rows.Rows);
        Assert.Equal(new[] { "ELEC: field 17.6 has the wrong shape" }, reported);
    }

    // ---- Culture --------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Elec_rows_are_the_same_in_every_culture(string culture)
    {
        // Every kind of row at once (external power, the shed, a charging battery, the IDG words), with figures that round
        // at a half (27.5, -12.5, -2.5) and a negative that rounds to nothing (-0.4): the PC's decimal comma and sv-SE's
        // true minus sign never reach the rows, which are the invariant culture's, and "-0" is never written.
        SdPush push = ElecPush(new ElecBoxes
        {
            Flags = Flags(galleyShed: true, bat1Charging: true, gen1LowOilPressure: true, gen2Disconnected: true),
            Buses = Buses(1, 3, 4, 6, 8),
            Bat1 = VoltAmps(27.5, -12.5),
            Tr1 = VoltAmps(28.4, -0.4),
            Gen1 = Generator(25.5, 115.4, 400.4, true, -2.5)
        });

        Assert.Equal(
            new[]
            {
                "ELEC", "BAT 1: 28 V, -13 A, charging", "DC BAT: powered", "BAT 2: 26 V, 5 A", "DC 1: powered",
                "DC ESS: powered", "DC 2: not powered", "AC 1: powered", "AC ESS: not powered", "AC 2: not powered",
                "TR 1: 28 V, 0 A", "ESS TR: 30 V, 4 A", "TR 2: 29 V, 9 A", "GEN 1: 26 %, 115 V, 400 HZ",
                "APU GEN: 13 %, 116 V, 399 HZ", "EXT PWR: 112 V, 405 HZ", "GEN 2: 32 %, 114 V, 402 HZ",
                "IDG 1: -3 °C, LO PR", "GALLEY SHED", "IDG 2: 88 °C, DISC"
            }.Concat(BottomRows),
            TestCulture.Run(culture, () => RowsOf(push)));
    }
}

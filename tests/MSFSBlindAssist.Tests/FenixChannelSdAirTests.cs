// Characterization tests for the Fenix SD's air pages: BLEED (FenixChannelSdPages.BleedRows, seam ComposeBleed), and later
// PRESS (Task 5) and COND (Task 6), each in its own section below. The pushes are built with SdPush
// (FenixChannelSdWindowTests.cs) and the composers are called directly, so every field number is written out here, never
// read from FenixChannelFields: a wrong number in the app fails a test.
//
// BLEED: the page reads 151.1 (valves), 151.2 (bleed PSI), 151.3 (bleed degrees), 151.4 (flags), 151.5 and 151.6 (the packs),
// all whole numbers. RAM AIR (151.1.11); PACK outlet (151.5.4, 151.6.4); PACK compressor (151.5.3, 151.6.3); PACK flow
// valves (151.1.9, .10); ANTI ICE valves (151.1.7, .8) only while 151.4.1; X BLEED valve (151.1.6); APU valve (151.1.5);
// bleed PSI (151.2.1, .2); bleed degrees (151.3.1, .2); bleed valves (151.1.1, .3); HP valves (151.1.2, .4). A message left
// out stops the page ("no field 151.1"), never its defaults; Open is left out on the wire and reads open.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSdAirTests
{
    // ================= BLEED =================

    // The valves (151.1): each field is written only when not Open (Closed is 1). Field numbers: Eng1Bleed 1, Eng1Hp 2,
    // Eng2Bleed 3, Eng2Hp 4, ApuBleed 5, Crossbleed 6, WaiLeft 7, WaiRight 8, Pack1Flow 9, Pack2Flow 10, RamAir 11.
    private static FenixChannelProtoWriter BleedValves(params (int Field, ulong Value)[] states)
    {
        var valves = new FenixChannelProtoWriter();
        foreach ((int field, ulong value) in states) valves.Varint(field, value);
        return valves;
    }

    // The cruise push of 2026-11-03: HP valves 2 and 4, APU 5, crossbleed 6, WAI 7 and 8, ram air 11 closed.
    private static FenixChannelProtoWriter CruiseValves() => BleedValves((2, 1), (4, 1), (5, 1), (6, 1), (7, 1), (8, 1), (11, 1));

    private static FenixChannelProtoWriter Pair(double first, double second) => new FenixChannelProtoWriter().Double(1, first).Double(2, second);

    private static FenixChannelProtoWriter Pack(double compressor, double outlet) => new FenixChannelProtoWriter().Double(3, compressor).Double(4, outlet);

    private static FenixChannelProtoWriter Flags(bool waiSwitchOn) => waiSwitchOn ? new FenixChannelProtoWriter().Bool(1, true) : new FenixChannelProtoWriter();

    // The pneumatic state (151); a null part is left out of the push unless defaults is on (the cruise push's values).
    private static SdPush BleedPush(
        FenixChannelProtoWriter? valves, FenixChannelProtoWriter? pressures = null, FenixChannelProtoWriter? temperatures = null,
        FenixChannelProtoWriter? flags = null, FenixChannelProtoWriter? pack1 = null, FenixChannelProtoWriter? pack2 = null,
        bool defaults = true)
    {
        if (defaults)
        {
            pressures ??= Pair(43.15, 43.15);
            temperatures ??= Pair(200, 200);
            flags ??= Flags(false);
            pack1 ??= Pack(175.27, 22.79);
            pack2 ??= Pack(175.27, 22.79);
        }

        var pneumatic = new FenixChannelProtoWriter();
        if (valves is not null) pneumatic.Message(1, valves);
        if (pressures is not null) pneumatic.Message(2, pressures);
        if (temperatures is not null) pneumatic.Message(3, temperatures);
        if (flags is not null) pneumatic.Message(4, flags);
        if (pack1 is not null) pneumatic.Message(5, pack1);
        if (pack2 is not null) pneumatic.Message(6, pack2);
        return new SdPush { Page = 2 }.With(remote => remote.Message(151, pneumatic));
    }

    private static IReadOnlyList<string> BleedRowsOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelSdPages.BleedRows(push.Context(reported.Add));

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    [Fact]
    public void The_bleed_page_reads_packs_valves_and_engines()
    {
        Assert.Equal(
            new[]
            {
                "RAM AIR: closed", "PACK outlet °C: 23, 23", "PACK compressor °C: 175, 175", "PACK flow valves: open, open",
                "X BLEED valve: closed", "APU valve: closed", "bleed PSI: 43, 43", "bleed °C: 200, 200",
                "bleed valves: open, open", "HP valves: closed, closed"
            },
            BleedRowsOf(BleedPush(CruiseValves())));
    }

    [Fact]
    public void Anti_ice_shows_only_while_selected()
    {
        // 151.4.1 clear: no row, though the valves carry states; set: the row sits after the pack rows, before X BLEED.
        Assert.DoesNotContain(BleedRowsOf(BleedPush(CruiseValves())), row => row.StartsWith("ANTI ICE"));

        IReadOnlyList<string> rows = BleedRowsOf(BleedPush(CruiseValves(), flags: Flags(true)));

        Assert.Equal("ANTI ICE valves: closed, closed", rows[4]);
        Assert.Equal("X BLEED valve: closed", rows[5]);
        Assert.Equal(11, rows.Count);
    }

    [Fact]
    public void An_absent_valve_reads_open()
    {
        // 151.1 without fields 1 and 3: the engine bleed valves read open, as does every valve the push leaves out.
        IReadOnlyList<string> rows = BleedRowsOf(BleedPush(BleedValves((2, 1), (4, 1))));

        Assert.Contains("bleed valves: open, open", rows);
        Assert.Contains("RAM AIR: open", rows);
        Assert.Contains("HP valves: closed, closed", rows);
        Assert.Contains("X BLEED valve: open", rows);
    }

    [Fact]
    public void A_missing_valve_message_is_not_available_never_open()
    {
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelSdPages.BleedRows(BleedPush(null).Context(reported.Add));

        Assert.Equal("no field 151.1", rows.Problem);
        Assert.Null(rows.Rows);
        Assert.Empty(reported);
    }

    [Theory]
    [InlineData(2, "no field 151.2")]
    [InlineData(3, "no field 151.3")]
    [InlineData(4, "no field 151.4")]
    [InlineData(5, "no field 151.5")]
    [InlineData(6, "no field 151.6")]
    public void A_missing_part_is_not_available_with_its_path(int part, string problem)
    {
        SdPush push = BleedPush(
            CruiseValves(),
            pressures: part == 2 ? null : Pair(43, 43), temperatures: part == 3 ? null : Pair(200, 200),
            flags: part == 4 ? null : Flags(false), pack1: part == 5 ? null : Pack(175, 23), pack2: part == 6 ? null : Pack(175, 23),
            defaults: false);

        FenixChannelRows rows = FenixChannelSdPages.BleedRows(push.Context(_ => { }));

        Assert.Equal(problem, rows.Problem);
    }

    [Fact]
    public void An_unknown_valve_value_reads_not_available_and_is_reported()
    {
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelSdPages.BleedRows(
            BleedPush(BleedValves((2, 1), (4, 1), (5, 1), (6, 9), (11, 1))).Context(reported.Add));

        Assert.Null(rows.Problem);
        Assert.Contains("X BLEED valve: not available", rows.Rows!);
        Assert.Contains("APU valve: closed", rows.Rows!);
        Assert.Contains("bleed PSI: 43, 43", rows.Rows!);
        Assert.Equal(new[] { "field 151.1.6 has an unknown value 9" }, reported);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Bleed_rows_are_the_same_in_every_culture(string culture)
    {
        // -0.4 never reads "-0"; -52.5 rounds away from zero to -53 with an ASCII minus, whatever the PC's culture.
        SdPush push = BleedPush(CruiseValves(), pressures: Pair(-0.4, 43.5), temperatures: Pair(-52.5, 200));

        IReadOnlyList<string> rows = TestCulture.Run(culture, () => BleedRowsOf(push));

        Assert.Contains("bleed PSI: 0, 44", rows);
        Assert.Contains("bleed °C: -53, 200", rows);
        Assert.DoesNotContain(rows, row => row.Contains('−') || row.Contains("-0"));
    }

    // ================= PRESS (Task 5) and COND (Task 6): their sections go here =================
}

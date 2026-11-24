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

    [Fact]
    public void The_apu_valve_is_in_transit_for_its_three_seconds_of_failed_open()
    {
        // The APU page's rows have the rule's cases; this page words the same valve through the same hold.
        SdPush push = BleedPush(BleedValves((5, 3)));
        var hold = new FenixChannelApuBleedHold();

        FenixChannelRows At(long atMs) => FenixChannelSdPages.BleedRows(push.Context(_ => { }) with { ApuBleedHold = hold, AtMs = atMs });

        Assert.Contains("APU valve: in transit", At(10_000).Rows!);
        At(11_000);
        At(12_000);
        At(13_000);
        Assert.Contains("APU valve: failed open", At(14_000).Rows!);
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

    // The flags (151.4) from field numbers: 1 wing anti-ice switch, 2 and 3 the anti-ice valves amber (left, right), 9 and 10
    // start air amber, 11 and 12 the bleed temperature amber, 13 and 14 the bleed pressure amber.
    private static FenixChannelProtoWriter BleedFlags(params int[] set)
    {
        var flags = new FenixChannelProtoWriter();
        foreach (int field in set.OrderBy(f => f)) flags.Bool(field, true);
        return flags;
    }

    [Fact]
    public void Bleed_values_speak_the_side_drawn_amber()
    {
        IReadOnlyList<string> left = BleedRowsOf(BleedPush(CruiseValves(), flags: BleedFlags(11, 13)));
        Assert.Contains("bleed PSI: 43 amber, 43", left);
        Assert.Contains("bleed °C: 200 amber, 200", left);

        IReadOnlyList<string> right = BleedRowsOf(BleedPush(CruiseValves(), flags: BleedFlags(12, 14)));
        Assert.Contains("bleed PSI: 43, 43 amber", right);
        Assert.Contains("bleed °C: 200, 200 amber", right);

        IReadOnlyList<string> both = BleedRowsOf(BleedPush(CruiseValves(), pressures: Pair(12, 33), temperatures: Pair(270, 160),
            flags: BleedFlags(11, 12, 13, 14)));
        Assert.Contains("bleed PSI: 12 amber, 33 amber", both);
        Assert.Contains("bleed °C: 270 amber, 160 amber", both);

        IReadOnlyList<string> one = BleedRowsOf(BleedPush(CruiseValves(), pressures: Pair(12, 33), temperatures: Pair(270, 160),
            flags: BleedFlags(11, 14)));
        Assert.Contains("bleed PSI: 12, 33 amber", one);
        Assert.Contains("bleed °C: 270 amber, 160", one);
    }

    [Fact]
    public void Anti_ice_valves_speak_the_side_drawn_amber()
    {
        Assert.Contains("ANTI ICE valves: closed amber, closed", BleedRowsOf(BleedPush(CruiseValves(), flags: BleedFlags(1, 2))));
        Assert.Contains("ANTI ICE valves: closed, closed amber", BleedRowsOf(BleedPush(CruiseValves(), flags: BleedFlags(1, 3))));
        Assert.Contains("ANTI ICE valves: closed amber, closed amber", BleedRowsOf(BleedPush(CruiseValves(), flags: BleedFlags(1, 2, 3))));

        // The row still shows only while the switch is on.
        Assert.DoesNotContain(BleedRowsOf(BleedPush(CruiseValves(), flags: BleedFlags(2, 3))), row => row.StartsWith("ANTI ICE"));
    }

    [Fact]
    public void Start_air_rows_follow_the_hp_valves()
    {
        List<string> rows = BleedRowsOf(BleedPush(CruiseValves(), flags: BleedFlags(9, 10))).ToList();
        Assert.Equal(new[] { "HP valves: closed, closed", "start air 1, amber", "start air 2, amber" }, rows.GetRange(rows.Count - 3, 3));

        Assert.Equal("start air 1, amber", BleedRowsOf(BleedPush(CruiseValves(), flags: BleedFlags(9)))[^1]);
        Assert.Equal("start air 2, amber", BleedRowsOf(BleedPush(CruiseValves(), flags: BleedFlags(10)))[^1]);
        Assert.DoesNotContain(BleedRowsOf(BleedPush(CruiseValves())), row => row.StartsWith("start air"));
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

    // ================= PRESS =================
    //
    // PRESS (FenixChannelSdPages.PressRows, seam ComposePress): the page reads 151.1 (valves), 151.9 (flags) and 151.10
    // (pressurization data). LDG ELEV AUTO or MAN (151.9.16): 151.10.7 FT; ΔP 151.10.1 (one decimal) PSI; V/S 151.10.2 FT/MIN;
    // CAB ALT 151.10.3 FT; SYS 1 (151.9.14), SYS 2 (151.9.15) and MAN (151.9.13) only while set; VENT INLET 151.10.4; VENT
    // EXTRACT 151.10.5; OUTFLOW 151.10.6 x 100 %; SAFETY 151.10.9; PACK 1 and PACK 2 by the flow valves 151.1.9 and .10.

    // The pressurization flags (151.9): 13 manual, 14 system 1, 15 system 2, 16 manual landing elevation.
    // Any other flag (the colors, pulses and faults: 1 to 11, 17 to 20, 22) goes in "also".
    private static FenixChannelProtoWriter PressFlags(bool manual = false, bool sys1 = false, bool sys2 = false, bool manualElev = false,
        params int[] also)
    {
        var set = new SortedSet<int>(also);
        if (manual) set.Add(13);
        if (sys1) set.Add(14);
        if (sys2) set.Add(15);
        if (manualElev) set.Add(16);

        var flags = new FenixChannelProtoWriter();
        foreach (int field in set) flags.Bool(field, true);
        return flags;
    }

    private static IReadOnlyList<string> PressWith(params int[] flags)
        => PressRowsOf(PressPush(new FenixChannelProtoWriter(), PressFlags(also: flags), PressData()));

    // The pressurization data (151.10), the cruise push of 2026-11-03 by default: the inlet, extract and safety valves
    // (4, 5, 9) closed, which is 1 on the wire.
    private static FenixChannelProtoWriter PressData(
        double deltaP = 7.983, double vs = -0.1665, double altitude = 6399.74, double outflow = 0.0842, double landing = 550)
        => new FenixChannelProtoWriter().Double(1, deltaP).Double(2, vs).Double(3, altitude).Varint(4, 1).Varint(5, 1)
            .Double(6, outflow).Double(7, landing).Varint(9, 1);

    // The pneumatic state (151) with the PRESS page's parts; a null part is left out of the push.
    private static SdPush PressPush(
        FenixChannelProtoWriter? valves, FenixChannelProtoWriter? flags, FenixChannelProtoWriter? data)
    {
        var pneumatic = new FenixChannelProtoWriter();
        if (valves is not null) pneumatic.Message(1, valves);
        if (flags is not null) pneumatic.Message(9, flags);
        if (data is not null) pneumatic.Message(10, data);
        return new SdPush { Page = 3 }.With(remote => remote.Message(151, pneumatic));
    }

    private static IReadOnlyList<string> PressRowsOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelSdPages.PressRows(push.Context(reported.Add));

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    [Fact]
    public void The_press_page_reads_ldg_elev_and_the_cabin()
    {
        // The cruise push: both packs' flow valves are left out (open), system 2 active.
        Assert.Equal(
            new[]
            {
                "LDG ELEV AUTO: 550 FT", "ΔP: 8.0 PSI", "V/S: 0 FT/MIN", "CAB ALT: 6400 FT", "SYS 2", "VENT INLET: closed",
                "VENT EXTRACT: closed", "OUTFLOW: 8 %", "SAFETY: closed", "PACK 1: open", "PACK 2: open"
            },
            PressRowsOf(PressPush(new FenixChannelProtoWriter(), PressFlags(sys2: true), PressData())));
    }

    [Fact]
    public void Cabin_values_speak_their_colors_and_pulses()
    {
        // 151.9: 6 cabin altitude red, 7 its pulse, 8 delta P amber, 9 its pulse, 10 V/S amber, 11 its pulse.
        Assert.Contains("ΔP: 8.0 PSI, amber", PressWith(8));
        Assert.Contains("ΔP: 8.0 PSI, pulsing", PressWith(9));
        Assert.Contains("ΔP: 8.0 PSI, amber, pulsing", PressWith(8, 9));
        Assert.Contains("V/S: 0 FT/MIN, amber", PressWith(10));
        Assert.Contains("V/S: 0 FT/MIN, pulsing", PressWith(11));
        Assert.Contains("V/S: 0 FT/MIN, amber, pulsing", PressWith(10, 11));
        Assert.Contains("CAB ALT: 6400 FT, red", PressWith(6));
        Assert.Contains("CAB ALT: 6400 FT, pulsing", PressWith(7));
        Assert.Contains("CAB ALT: 6400 FT, red, pulsing", PressWith(6, 7));

        // Nothing set: the bare rows.
        Assert.Contains("ΔP: 8.0 PSI", PressWith());
    }

    [Fact]
    public void Systems_speak_amber_even_when_not_active()
    {
        IReadOnlyList<string> amber = PressWith(19, 20);
        Assert.Contains("SYS 1, amber", amber);
        Assert.Contains("SYS 2, amber", amber);

        IReadOnlyList<string> active = PressRowsOf(PressPush(new FenixChannelProtoWriter(), PressFlags(sys1: true, also: 19), PressData()));
        Assert.Contains("SYS 1, amber", active);
        Assert.DoesNotContain("SYS 2", active);
        Assert.DoesNotContain("SYS 2, amber", PressWith(19));
    }

    [Fact]
    public void Vent_valves_blower_and_extract_speak_amber_and_faults()
    {
        List<string> rows = PressWith(3, 4, 5, 17, 18).ToList();

        Assert.Equal(
            new[]
            {
                "VENT, amber", "VENT INLET: closed, amber", "VENT EXTRACT: closed, amber", "BLOWER: fault", "EXTRACT: fault",
                "OUTFLOW: 8 %"
            },
            rows.GetRange(rows.IndexOf("VENT, amber"), 6));

        Assert.Contains("VENT, amber", PressWith(3));
        Assert.DoesNotContain("VENT INLET: closed, amber", PressWith(3));
        Assert.Contains("VENT INLET: closed, amber", PressWith(4));
        Assert.Contains("VENT EXTRACT: closed, amber", PressWith(5));
        Assert.Contains("BLOWER: fault", PressWith(17));
        Assert.DoesNotContain("EXTRACT: fault", PressWith(17));
        Assert.Contains("EXTRACT: fault", PressWith(18));
        Assert.DoesNotContain(PressWith(), row => row.StartsWith("VENT,") || row.StartsWith("BLOWER") || row.StartsWith("EXTRACT"));
    }

    [Fact]
    public void Outflow_and_packs_speak_amber()
    {
        Assert.Contains("OUTFLOW: 8 %, amber", PressWith(22));
        Assert.Contains("PACK 1: open, amber", PressWith(1));
        Assert.DoesNotContain("PACK 2: open, amber", PressWith(1));
        Assert.Contains("PACK 2: open, amber", PressWith(2));
        Assert.Contains("OUTFLOW: 8 %", PressWith());
    }

    [Fact]
    public void Ldg_elev_reads_man_while_manual()
    {
        IReadOnlyList<string> rows = PressRowsOf(PressPush(new FenixChannelProtoWriter(), PressFlags(manualElev: true), PressData(landing: 1250)));

        Assert.Equal("LDG ELEV MAN: 1250 FT", rows[0]);
    }

    [Fact]
    public void Systems_and_man_show_only_while_drawn()
    {
        IReadOnlyList<string> none = PressRowsOf(PressPush(new FenixChannelProtoWriter(), PressFlags(), PressData()));
        IReadOnlyList<string> all = PressRowsOf(PressPush(
            new FenixChannelProtoWriter().Varint(9, 1).Varint(10, 2), PressFlags(manual: true, sys1: true, sys2: true), PressData()));

        Assert.DoesNotContain("SYS 1", none);
        Assert.DoesNotContain("SYS 2", none);
        Assert.DoesNotContain("MAN", none);
        Assert.Equal(new[] { "SYS 1", "SYS 2", "MAN" }, all.Skip(4).Take(3));
        Assert.Contains("PACK 1: closed", all);
        Assert.Contains("PACK 2: in transit", all);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Cabin_values_are_the_same_in_every_culture(string culture)
    {
        // V/S -0.4 never reads "-0"; ΔP 7.86 reads 7.9 with a point, whatever the PC's culture.
        SdPush push = PressPush(new FenixChannelProtoWriter(), PressFlags(), PressData(deltaP: 7.86, vs: -0.4));

        IReadOnlyList<string> rows = TestCulture.Run(culture, () => PressRowsOf(push));

        Assert.Contains("V/S: 0 FT/MIN", rows);
        Assert.Contains("ΔP: 7.9 PSI", rows);
        Assert.DoesNotContain(rows, row => row.Contains('−') || row.Contains("-0"));
    }

    [Theory]
    [InlineData(1, "no field 151.1")]
    [InlineData(9, "no field 151.9")]
    [InlineData(10, "no field 151.10")]
    public void A_missing_press_part_is_not_available_with_its_path(int part, string problem)
    {
        SdPush push = PressPush(
            part == 1 ? null : new FenixChannelProtoWriter(), part == 9 ? null : PressFlags(), part == 10 ? null : PressData());

        FenixChannelRows rows = FenixChannelSdPages.PressRows(push.Context(_ => { }));

        Assert.Equal(problem, rows.Problem);
        Assert.Null(rows.Rows);
    }

    // ================= COND =================
    //
    // COND (FenixChannelSdPages.CondRows, seam ComposeCond): the page reads 151.7 (zones) and 151.8 (conditioning states), all
    // whole numbers. CKPT: 151.7.1 °C, duct 151.7.4 °C, trim valve 151.7.7 x 100 % hot (how far open toward hot, on the SD's C to H gauge); FWD (.2, .5, .8); AFT (.3, .6, .9);
    // FWD CARGO (.11, .13, .15) only while 151.8.5; AFT CARGO (.12, .14, .16) only while 151.8.6; FAN 1 off (151.8.1) and
    // FAN 2 off (151.8.7), ALTN MODE (151.8.2) and PACK REG (151.8.3) only while set. HOT AIR (151.7.10, a double: left out
    // 0 open, 1 closed, 2 in transit, measured 2026-11-03) is a row right after AFT.

    // The zone temperatures (151.7): the cruise push of 2026-11-03 by default (cargo values present, as the Fenix sends them).
    private static FenixChannelProtoWriter CondZone(double fwdCargoTrim = 0.5, double aftCargoTrim = 0.25)
        => new FenixChannelProtoWriter()
            .Double(1, 23.9909).Double(2, 23.9866).Double(3, 23.9869).Double(4, 37.5915).Double(5, 33.9229).Double(6, 25.1471)
            .Double(7, 0.3363).Double(8, 0.4057).Double(9, 0.1143).Double(11, 6.3123).Double(12, 5.8325)
            .Double(13, 14.4).Double(14, 15.5).Double(15, fwdCargoTrim).Double(16, aftCargoTrim);

    // The conditioning states (151.8): 1 fan 1 off, 2 alternate, 3 pack reg, 5 forward cargo heating, 6 aft cargo heating,
    // 7 fan 2 off; each written only while set.
    private static FenixChannelProtoWriter CondStates(
        bool fan1Off = false, bool alternate = false, bool packReg = false, bool cargoFwd = false, bool cargoAft = false,
        bool fan2Off = false)
    {
        var states = new FenixChannelProtoWriter();
        if (fan1Off) states.Bool(1, true);
        if (alternate) states.Bool(2, true);
        if (packReg) states.Bool(3, true);
        if (cargoFwd) states.Bool(5, true);
        if (cargoAft) states.Bool(6, true);
        if (fan2Off) states.Bool(7, true);
        return states;
    }

    // The pneumatic state (151) with the COND page's parts; a null part is left out of the push.
    private static SdPush CondPush(FenixChannelProtoWriter? zone, FenixChannelProtoWriter? states)
    {
        var pneumatic = new FenixChannelProtoWriter();
        if (zone is not null) pneumatic.Message(7, zone);
        if (states is not null) pneumatic.Message(8, states);
        return new SdPush { Page = 4 }.With(remote => remote.Message(151, pneumatic));
    }

    private static IReadOnlyList<string> CondRowsOf(SdPush push)
    {
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelSdPages.CondRows(push.Context(reported.Add));

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    [Fact]
    public void The_cond_page_reads_the_three_zones()
    {
        // The cruise push: 151.8 present and empty, so no cargo rows although 151.7.11 and .12 carry values.
        Assert.Equal(
            new[]
            {
                "CKPT: 24 °C, duct 38 °C, trim valve 34 % hot", "FWD: 24 °C, duct 34 °C, trim valve 41 % hot",
                "AFT: 24 °C, duct 25 °C, trim valve 11 % hot", "HOT AIR: open"
            },
            CondRowsOf(CondPush(CondZone(), CondStates())));
    }

    [Fact]
    public void Cargo_rows_show_only_with_cargo_heating()
    {
        IReadOnlyList<string> fwd = CondRowsOf(CondPush(CondZone(), CondStates(cargoFwd: true)));
        IReadOnlyList<string> aft = CondRowsOf(CondPush(CondZone(), CondStates(cargoAft: true)));
        IReadOnlyList<string> both = CondRowsOf(CondPush(CondZone(), CondStates(cargoFwd: true, cargoAft: true)));

        Assert.Equal("FWD CARGO: 6 °C, duct 14 °C, trim valve 50 % hot", Assert.Single(fwd, row => row.Contains("CARGO")));
        Assert.Equal("AFT CARGO: 6 °C, duct 16 °C, trim valve 25 % hot", Assert.Single(aft, row => row.Contains("CARGO")));
        Assert.Equal(new[] { "FWD CARGO", "AFT CARGO" }, both.Skip(4).Select(row => row.Split(':')[0]));
    }

    [Fact]
    public void Fan_and_mode_rows_show_only_while_drawn()
    {
        IReadOnlyList<string> none = CondRowsOf(CondPush(CondZone(), CondStates()));
        IReadOnlyList<string> all = CondRowsOf(CondPush(
            CondZone(), CondStates(fan1Off: true, alternate: true, packReg: true, cargoFwd: true, cargoAft: true, fan2Off: true)));

        Assert.Equal(4, none.Count);
        Assert.Equal(new[] { "FAN 1: off", "FAN 2: off", "ALTN MODE", "PACK REG" }, all.Skip(6));
        Assert.Equal(new[] { "FAN 2: off" }, CondRowsOf(CondPush(CondZone(), CondStates(fan2Off: true))).Skip(4));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Cond_rows_are_the_same_in_every_culture(string culture)
    {
        // A temperature of -0.4 never reads "-0"; -52.5 rounds away from zero to -53 with an ASCII minus.
        var zone = new FenixChannelProtoWriter().Double(1, -0.4).Double(2, -52.5).Double(3, 21.5).Double(4, 38.4).Double(7, 0.345);
        SdPush push = CondPush(zone, CondStates());

        IReadOnlyList<string> rows = TestCulture.Run(culture, () => CondRowsOf(push));

        Assert.Equal("CKPT: 0 °C, duct 38 °C, trim valve 35 % hot", rows[0]);
        Assert.StartsWith("FWD: -53 °C", rows[1]);
        Assert.StartsWith("AFT: 22 °C", rows[2]);
        Assert.DoesNotContain(rows, row => row.Contains('−') || row.Contains("-0"));
        Assert.Equal("HOT AIR: open", rows[3]);
    }

    [Theory]
    [InlineData(null, "HOT AIR: open", null)]
    [InlineData(1.0, "HOT AIR: closed", null)]
    [InlineData(2.0, "HOT AIR: in transit", null)]
    [InlineData(3.0, "HOT AIR: not available", "field 151.7.10 has an unknown value 3")]
    [InlineData(1.5, "HOT AIR: not available", "field 151.7.10 has an unknown value 1.5")]
    public void Each_measured_hot_air_value_reads_its_word(double? value, string row, string? reportedProblem)
    {
        FenixChannelProtoWriter zone = CondZone();
        if (value is { } v) zone.Double(10, v);
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelSdPages.CondRows(CondPush(zone, CondStates()).Context(reported.Add));

        Assert.Null(rows.Problem);
        Assert.Equal(row, rows.Rows![3]);
        Assert.Equal(reportedProblem is null ? new string[0] : new[] { reportedProblem }, reported);
    }

    [Theory]
    [InlineData(7, "no field 151.7")]
    [InlineData(8, "no field 151.8")]
    public void A_missing_cond_part_is_not_available_with_its_path(int part, string problem)
    {
        SdPush push = CondPush(part == 7 ? null : CondZone(), part == 8 ? null : CondStates());

        FenixChannelRows rows = FenixChannelSdPages.CondRows(push.Context(_ => { }));

        Assert.Equal(problem, rows.Problem);
        Assert.Null(rows.Rows);
    }

    [Fact]
    public void A_missing_pneumatic_state_is_not_available()
    {
        FenixChannelRows rows = FenixChannelSdPages.CondRows(new SdPush { Page = 4 }.Context(_ => { }));

        Assert.Equal("no field 151", rows.Problem);
    }
}

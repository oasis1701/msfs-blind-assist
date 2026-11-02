// The PFD window's rows: the approved example (owner, 2026-10-29) and the rows decided at the Task 5 checkpoint, from a
// reading of the test memory whose fields are set as PMDG keeps them (each found by offset through the layout's own
// fields, the test layout copying the real file's). Part names come from one table; items PMDG draws without a label are
// named only where the layout itself singles them out, never by their place in a list; a value is read only while PMDG
// draws it; numbers never follow the PC's culture.

using System.Globalization;
using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.Pmdg737PfdTestMemory;

namespace MSFSBlindAssist.Tests;

public class Pmdg737PfdTextTests
{
    private static readonly Pmdg737PfdLayout L = M.Layout;

    private static readonly string[] Columns = { "A/T: MCP SPD", "Roll: VOR/LOC", "Pitch: G/S", "AFDS: CMD" };

    /// <summary>The test memory with PMDG's fields set by object and offset (the kind and stride from the layout's own
    /// field of that offset).</summary>
    private sealed class Pfd
    {
        private static readonly IReadOnlyList<PfdField> Fields =
            PfdLayoutWalk.Expressions(L).SelectMany(x => PfdEval.Fields(x.Expression)).Distinct().ToList();

        public readonly M Memory = new();

        public Pfd G(int offset, double v, long index = 0) => Set(L.Objects.Glass, offset, v, index);

        public Pfd O(int offset, double v) => Set(L.Objects.Options, offset, v, 0);

        public Pfd Cafs(int offset, double v) => Set(PfdLayoutWalk.Owners(L).Single(o => o.Global == 0xed2e4), offset, v, 0);

        private Pfd Set(GlassObject owner, int offset, double v, long index)
        {
            // An element past the first needs the field with a stride (the flap speeds, not the UP bug's own read).
            var f = Fields.FirstOrDefault(x => x.Owner == owner && x.Field.Offset == offset && (index == 0 || x.Field.Stride != 0))
                ?? throw new InvalidOperationException($"no field at 0x{owner.Global:x} + {offset}");
            Memory.Set(f with { Index = null }, v, index);
            return this;
        }

        public Pfd Ident(string text)
        {
            Memory.Text(Memory.Address(L.Objects.Glass) + 4560, text);
            return this;
        }

        /// <summary>The IRU's condition, which the ATT flag reads through the IRS object's pointer (0 while the IRS is off).</summary>
        public Pfd Iru(double condition)
        {
            Memory.Put(Memory.Iru + (uint)M.IruCondition.Field.Offset, M.IruCondition.Field.Kind, condition);
            return this;
        }

        public Pfd Panel(bool fpv = false, bool std = false, bool powered = true)
        {
            Memory.Panel(0, fpv: fpv, std: std, powered: powered);
            return this;
        }

        public Pfd Page(int page)
        {
            var u = L.Units;
            Memory.I(Memory.EfisAt(Memory.PfdUnit.Unit * u.UnitStride + u.PageOffset), page);
            return this;
        }

        public PfdSample Sample() => Memory.Sample(full: true);

        public IReadOnlyList<string> Rows(IReadOnlyList<string>? columns = null, string readout = "FMA reader starting", Pmdg737PfdLayout? layout = null) =>
            Pmdg737PfdText.Rows(Sample(), layout ?? L, columns ?? Columns, readout);
    }

    /// <summary>The approved example's state: final approach on the ILS to 18C at Cincinnati, autopilot on, flaps 15,
    /// gear down, about 1,500 ft above the ground, 4.8 NM out; nothing else PMDG would draw.</summary>
    private static Pfd Approach()
    {
        var p = new Pfd().Panel();
        // The speed tape: 160 knots, the trend arrow to 154, selected 150, bands at 200 and 115, amber from 128.
        p.G(1560, 160).G(2056, 1).G(2120, -0.6).G(2068, 1).G(1656, 150).G(1688, 0).G(2024, 1).G(1516, 0)
            .G(2080, 1).G(1704, 200).G(1736, 200).G(2092, 1).G(1800, 115).G(1768, 128)
            .G(1868, 1).G(1976, 142)
            .G(1528, 0.25).O(6, 1).G(3664, 175).G(2152, 152);
        // The flap bugs: flaps 15 selected toward 25 on the approach (the case drawing 25 then 15), UP well above.
        p.G(2184, 1).G(2232, 140).G(9228, 3).Cafs(3468, 0).G(2040, 0).G(2248, 6).G(2296, 40)
            .G(2200, 230, 0).G(2200, 157, 5).G(2200, 147, 6);
        // The attitude: pitch 2.5 up, wings level; the PLI 11 above; the flight director; the radio altitude.
        p.G(2312, 0).G(2336, 1).G(2832, 2.5).G(2800, 0).G(2912, 0)
            .G(2408, 0).G(2864, 1).G(2880, 13.5)
            .O(10, 1).G(2432, 0).G(2480, 1).G(2504, 1).G(2528, 3.0).G(2560, 0)
            .G(2384, 0).G(7272, 1480);
        // No TCAS advisory.
        p.G(8192, 9999).G(8160, -9999);
        // The ILS: ICVG on course 177, DME 4.8, the outer marker; the localizer 0.2 dots left, the glideslope 0.1 below.
        p.Ident("ICVG").G(4472, 1).G(4504, 1).G(6104, 177).G(4024, 4.8).G(4368, 0).G(4088, 1)
            .G(4536, 1).G(6512, 0).G(6304, 1).G(4320, 0).G(6280, 1).G(6216, -0.1)
            .G(6536, 0).G(6464, 1).G(4344, 0).G(6440, 1).G(6376, 0.05);
        // The altitude tape: 2385 ft (the box 2380), selected 3000, BARO minimums 1100, the landing altitude far below,
        // 30.01 inches.
        p.G(7400, 0).G(7336, 2385).G(7424, 3000).G(7496, 0)
            .G(7752, 1).G(7784, 1).G(7832, 1100)
            .G(7992, 1).G(8008, 650)
            .G(7696, 0).G(7672, 0).G(7544, 30.01);
        // 750 fpm down; the heading 177 MAG, selected 180, the track 175.
        p.G(8056, 0).G(8080, -750).G(3584, 0)
            .G(3744, 0).G(2360, 1).G(3768, 1).G(3600, 177).G(3728, 180);
        return p;
    }

    private static readonly string[] Example =
    {
        // The FMA's columns name themselves; no "FMA: " before them (owner, in flight, 2026-10-30).
        "A/T: MCP SPD",
        "Roll: VOR/LOC",
        "Pitch: G/S",
        "AFDS: CMD",
        "Above the speed tape: selected speed 150",
        "Speed tape: red and black band from 200 up, 40 above",
        "Speed tape: 160 knots, trend arrow to 154",
        "Speed tape: 15 bug, 157, 3 below",
        "Speed tape: selected speed bug, 150, 10 below",
        "Speed tape: 25 bug, 147, 13 below",
        "Speed tape: REF bug, 142, 18 below",
        "Speed tape: amber bar from 128 down, 32 below",
        "Speed tape: red and black band from 115 down, 45 below",
        "Below the speed tape: GS 152",
        "Attitude: approach info, ICVG/177, DME 4.8",
        // Not in the approved example, which came before the approach mode line's rows: PMDG prints "ILS" under the
        // ident on an ILS (seen live at ground session 1), a row decided at the Task 5 checkpoint.
        "Attitude: approach mode, ILS",
        "Attitude: marker beacon, OM",
        "Attitude: pitch 2.5° up, wings level",
        "Attitude: slip indicator, centred",
        "Attitude: pitch limit indicator, 11° above",
        "Attitude: flight director pitch bar, 0.5° above",
        "Attitude: flight director roll bar, centred",
        "Attitude: glideslope pointer, 0.1 dots below",
        "Attitude: radio altitude 1480",
        "Attitude: minimums, BARO 1100",
        "Attitude: localizer pointer, 0.2 dots left",
        "Above the altitude tape: selected altitude 3000",
        "Altitude tape: selected altitude bug, at the top edge",
        "Altitude tape: 2380 feet",
        "Below the altitude tape: 30.01 IN.",
        "Vertical speed: 750 down",
        "Heading: 177 MAG",
        "Heading: selected heading 180, 3° right",
        "Heading: track 175, 2° left",
    };

    [Fact]
    public void Part_names_are_the_one_table()
    {
        Assert.Equal("FMA: ", PfdParts.Of(PfdPlace.Fma));
        Assert.Equal("Above the speed tape: ", PfdParts.Of(PfdPlace.AboveSpeedTape));
        Assert.Equal("Speed tape: ", PfdParts.Of(PfdPlace.SpeedTape));
        Assert.Equal("Below the speed tape: ", PfdParts.Of(PfdPlace.BelowSpeedTape));
        Assert.Equal("Attitude: ", PfdParts.Of(PfdPlace.Attitude));
        Assert.Equal("Above the altitude tape: ", PfdParts.Of(PfdPlace.AboveAltitudeTape));
        Assert.Equal("Altitude tape: ", PfdParts.Of(PfdPlace.AltitudeTape));
        Assert.Equal("Below the altitude tape: ", PfdParts.Of(PfdPlace.BelowAltitudeTape));
        Assert.Equal("Vertical speed: ", PfdParts.Of(PfdPlace.VerticalSpeed));
        Assert.Equal("Heading: ", PfdParts.Of(PfdPlace.Heading));
        Assert.Equal("Message: ", PfdParts.Of(PfdPlace.Message));
    }

    [Fact]
    public void Approved_example_rows_exactly() => Assert.Equal(Example, Approach().Rows());

    [Fact]
    public void No_vspd_is_a_message_row()
    {
        // On the ground before the takeoff speeds are set.
        var rows = Approach().G(1516, 1).G(1832, 0).G(1892, 1).Rows();
        Assert.Equal("Message: NO VSPD", rows[^1]);
        Assert.Single(rows, r => r.Contains("NO VSPD"));
    }

    [Fact]
    public void Speed_flag_replaces_the_tape_rows()
    {
        var rows = Approach().G(2056, 0).Rows();
        Assert.Equal(new[] { "Speed tape: SPD flag" }, rows.Where(r => r.StartsWith("Speed tape: ", StringComparison.Ordinal)));
        Assert.Equal("Speed tape: SPD flag", rows[5]);   // in the tape's place, after the selected speed PMDG still prints
        Assert.Equal("Above the speed tape: selected speed 150", rows[4]);
        Assert.DoesNotContain(rows, r => r.StartsWith("Below the speed tape: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Selected_speed_shows_above_and_on_the_strip()
    {
        var rows = Approach().Rows();
        Assert.Contains("Above the speed tape: selected speed 150", rows);
        Assert.Contains("Speed tape: selected speed bug, 150, 10 below", rows);
        // Out of the strip PMDG parks the bug at the edge; the readout still prints the number.
        rows = Approach().G(1656, 250).Rows();
        Assert.Contains("Above the speed tape: selected speed 250", rows);
        Assert.Contains("Speed tape: selected speed bug, at the top edge", rows);
        Assert.Equal("Speed tape: selected speed bug, at the top edge", rows[5]);
    }

    [Fact]
    public void White_bugs_are_named_from_the_layout()
    {
        // VREF + 20 (PMDG's option byte 7), drawn as REF's value plus 20.
        Assert.Contains("Speed tape: VREF plus 20 bug, 162, 2 above", Approach().O(7, 1).Rows());
        // The 80 knot bug (100 with PMDG's option, its option byte 8) while no REF is set.
        Assert.Contains("Speed tape: 100 knot bug, 100, 15 above", Approach().G(1868, 0).G(1560, 85).O(8, 1).Rows());
        Assert.Contains("Speed tape: 80 knot bug, 80, 5 below", Approach().G(1868, 0).G(1560, 85).Rows());
        // V2 + 15: the bug whose gate the flap bugs' choice reads.
        Assert.Contains("Speed tape: V2 plus 15 bug, 170, 10 above", Approach().G(2040, 170).Rows());
        // The speed reference bug (the SPD REF selector's BUG position).
        Assert.Contains("Speed tape: speed reference bug, 165, 5 above", Approach().G(2024, 0).G(1880, 1).G(1992, 165).Rows());
    }

    [Fact]
    public void Flap_bugs_with_no_case_say_not_read()
    {
        var uncovered = L with { Speed = L.Speed with { FlapBugs = L.Speed.FlapBugs with { Cases = Array.Empty<PfdFlapCase>() } } };
        var rows = Approach().Rows(layout: uncovered);
        Assert.Contains("Speed tape: flap bugs, not read", rows);
        Assert.DoesNotContain(rows, r => r.Contains(" bug, 157"));
    }

    [Fact]
    public void Minimums_shown_reads_the_minimums_row()
    {
        // The window's MINS RST read-back says the minimums as the rows show them.
        Assert.Equal("BARO 1100", Pmdg737PfdText.MinimumsShown(Approach().Rows()));
        Assert.Equal("RADIO 200", Pmdg737PfdText.MinimumsShown(Approach().G(7752, 0).G(7808, 1).G(7928, 200).Rows()));
        Assert.Null(Pmdg737PfdText.MinimumsShown(Approach().G(7784, 0).Rows()));
        Assert.Null(Pmdg737PfdText.MinimumsShown(new[] { Pmdg737PfdText.Off }));
    }

    [Fact]
    public void Minimums_for_the_setter_are_the_mode_s_readout()
    {
        // The minimums setter turns from PMDG's own value for the selector's mode, shown or not.
        Assert.Equal(new PfdMinimums(Baro: true, Value: 1100, Shown: true), Pmdg737PfdMinimums.Read(Approach().Sample(), L, baro: true));
        Assert.Equal(new PfdMinimums(true, 1100, false), Pmdg737PfdMinimums.Read(Approach().G(7784, 0).Sample(), L, baro: true));
        Assert.Equal(new PfdMinimums(false, 200, true),
            Pmdg737PfdMinimums.Read(Approach().G(7752, 0).G(7808, 1).G(7928, 200).Sample(), L, baro: false));
        // In BARO mode the RADIO value is there, not shown.
        Assert.Equal(new PfdMinimums(false, 200, false), Pmdg737PfdMinimums.Read(Approach().G(7928, 200).Sample(), L, baro: false));
        // A light reading holds no values.
        Assert.Null(Pmdg737PfdMinimums.Read(Approach().Memory.Sample(full: false), L, baro: true));
    }

    [Fact]
    public void Lnav_armed_takes_the_pfds_text_in_the_roll_row()
    {
        // The FMA reader reads the autopilot's armed roll, which never holds the LNAV PMDG shows armed beside another mode or
        // alone; the window's roll row takes PMDG's drawn text then (owner's checkpoint, the spec's FMA row).
        string[] heading = { "A/T: MCP SPD", "Roll: HDG SEL", "Pitch: V/S", "AFDS: CMD" };
        Assert.Equal("Roll: HDG SEL, armed LNAV", Approach().G(2960, 11).Rows(heading)[1]);
        string[] beside = { "A/T: MCP SPD", "Roll: HDG SEL, armed VOR/LOC", "Pitch: V/S", "AFDS: CMD" };
        Assert.Equal("Roll: HDG SEL, armed LNAV VOR/LOC", Approach().G(2960, 10).Rows(beside)[1]);
        // Nothing armed drawn: the FMA reader's column as it is.
        Assert.Equal("Roll: VOR/LOC", Approach().G(2960, 0).Rows()[1]);
    }

    [Fact]
    public void Single_cue_flight_director_is_one_row_in_the_bars_place()
    {
        // Without PMDG's split-axis option the flight director is a single cue: the window says it is there, unread.
        var rows = Approach().O(10, 0).Rows();
        int pli = rows.ToList().IndexOf("Attitude: pitch limit indicator, 11° above");
        Assert.Equal("Attitude: flight director, single cue, not read", rows[pli + 1]);
        Assert.DoesNotContain(rows, r => r.Contains("flight director pitch bar") || r.Contains("flight director roll bar"));
        // The flight director off: no row.
        Assert.DoesNotContain(Approach().O(10, 0).G(2480, 0).G(2504, 0).Rows(), r => r.Contains("flight director"));
    }

    [Fact]
    public void A_value_printf_cannot_print_makes_its_part_not_read()
    {
        // A number "%d" cannot print (a garbage double past a long's range; PfdEval's trunc keeps it) is that part's
        // "not read" row, never a throw out of Rows: the window would keep its old rows.
        var rows = Approach().G(1656, 1e300).Rows();
        Assert.Contains("Above the speed tape: not read", rows);
        Assert.Contains("Altitude tape: 2380 feet", rows);
    }

    [Theory]
    [InlineData(0.02, "Attitude: slip indicator, centred")]          // under a pixel
    [InlineData(0.06, "Attitude: slip indicator, slightly right")]   // live in the climb: 2.4 px, hollow
    [InlineData(-0.5, "Attitude: slip indicator, slightly left")]
    [InlineData(1.2, "Attitude: slip indicator, right")]             // PMDG fills it from 0.9
    public void Slip_reads_where_pmdg_draws_it(double slip, string row) =>
        Assert.Contains(row, Approach().G(2912, slip).Rows());

    [Fact]
    public void Fpv_row_with_fpv_on()
    {
        var rows = Approach().Panel(fpv: true).G(2736, 0).G(2768, -1).Rows();
        Assert.Contains("Attitude: flight path vector, 2.5° below, 1° left", rows);
        Assert.DoesNotContain(Approach().Rows(), r => r.Contains("flight path vector"));
    }

    [Fact]
    public void Fd_rows_only_while_the_bars_show()
    {
        Assert.DoesNotContain(Approach().G(2480, 0).G(2504, 0).Rows(), r => r.Contains("flight director"));
        var rows = Approach().G(2528, -6.0).G(2800, 10).G(2560, 2.5).Rows();
        Assert.Contains("Attitude: flight director pitch bar, 8.5° below", rows);
        Assert.Contains("Attitude: pitch 2.5° up, bank 10° left", rows);
        Assert.Contains("Attitude: flight director roll bar, 8° right", rows);
        // PMDG holds the roll bar at 8 steps (20° of bank difference).
        Assert.Contains("Attitude: flight director roll bar, at the right edge", Approach().G(2560, -30).Rows());
        Assert.Contains("Attitude: flight director roll bar, at the left edge", Approach().G(2560, 30).Rows());
    }

    [Theory]
    [InlineData(200, "Vertical speed: needle, 200 up")]
    [InlineData(-230, "Vertical speed: needle, 250 down")]
    [InlineData(10, "Vertical speed: needle level")]
    [InlineData(1210, "Vertical speed: 1200 up")]
    public void Vertical_speed_needle_below_the_threshold(double rate, string row) =>
        Assert.Contains(row, Approach().G(8080, rate).Rows());

    [Fact]
    public void Selected_vs_bug_in_vs_mode() =>
        Assert.Contains("Vertical speed: selected bug, 1000 down", Approach().G(3584, 1).G(8144, -1000).Rows());

    [Fact]
    public void Selected_altitude_bug_at_the_top_edge()
    {
        Assert.Contains("Altitude tape: selected altitude bug, at the top edge", Approach().Rows());
        var rows = Approach().G(7424, 2500).Rows();
        Assert.Contains("Altitude tape: selected altitude bug, 2500, 115 above", rows);
        Assert.True(rows.ToList().IndexOf("Altitude tape: selected altitude bug, 2500, 115 above") < rows.ToList().IndexOf("Altitude tape: 2380 feet"));
    }

    [Fact]
    public void Metres_join_their_rows()
    {
        var rows = Approach().G(7496, 1).Rows();
        Assert.Contains("Above the altitude tape: selected altitude 3000, 914 metres", rows);
        Assert.Contains("Altitude tape: 2380 feet, 727 metres", rows);
        Assert.DoesNotContain(rows, r => r.EndsWith(" M", StringComparison.Ordinal));
    }

    [Fact]
    public void Landing_altitude_bands()
    {
        // 1000 ft with the landing altitude at 650: the strip shows 600 to 1400; the bars with PMDG's option (byte 14).
        var rows = Approach().G(7336, 1000).G(7424, 5000).O(14, 1).Rows();
        Assert.Contains("Altitude tape: upper landing bar from 1150 to 1650", rows);
        Assert.Contains("Altitude tape: lower landing bar from 650 to 1150", rows);
        Assert.Contains("Altitude tape: hatched band from 650 down, 350 below", rows);
        // At 1530 ft the hatched band lies below the strip: no row, the bars still on it.
        rows = Approach().G(7336, 1530).G(7424, 5000).O(14, 1).Rows();
        Assert.DoesNotContain(rows, r => r.Contains("hatched band"));
        Assert.Contains("Altitude tape: lower landing bar from 650 to 1150", rows);
    }

    [Fact]
    public void Altimeter_in_hpa_and_std()
    {
        Assert.Contains("Below the altitude tape: 1016 HPA", Approach().G(7696, 1).G(7608, 1016.4).Rows());
        Assert.Contains("Below the altitude tape: STD", Approach().G(7672, 1).Rows());
        Assert.Contains("Below the altitude tape: STD, preselect 30.01 IN.", Approach().G(7672, 1).G(7728, 1).Rows());
    }

    [Fact]
    public void Off_is_one_row() =>
        Assert.Equal(new[] { "Captain PFD: off" }, Approach().Panel(powered: false).Rows());

    [Fact]
    public void Not_on_any_display_is_one_row() =>
        Assert.Equal(new[] { "Captain PFD: not on any display" }, Approach().Page(3).Rows());

    [Fact]
    public void Classic_page_is_one_row() =>
        Assert.Equal(new[] { "Captain PFD: classic EFIS display, not read" }, Approach().Page(L.Units.ClassicPages[0]).Rows());

    [Fact]
    public void Optional_part_failed_says_not_read()
    {
        var broken = L with
        {
            Optional = L.Optional with
            {
                Approach = null,
                Failures = new[] { new PfdPartFailure(Pmdg737PfdParts.Approach, "approach: no ident drawn") },
            },
        };
        var rows = Approach().Rows(layout: broken);
        Assert.Equal("Attitude: approach info, not read: approach: no ident drawn", rows[14]);
        Assert.DoesNotContain(rows, r => r.Contains("pointer, "));
    }

    [Fact]
    public void Performance_scales_read_vertical_in_feet()
    {
        // The vertical RNP and ANP are feet (RNP 400, ANP 148 in the descent, seen live 2026-10-30), read whole; the
        // lateral ones nautical miles, as PMDG's PROGRESS page prints them.
        var rows = Approach().G(6536, 1).G(6800, 400).G(6864, 148.4).G(6512, 1).G(6560, 2).G(6624, 0.07).Rows();
        Assert.Contains("Attitude: vertical performance scale, RNP 400 feet, ANP 148 feet", rows);
        Assert.Contains("Attitude: lateral performance scale, RNP 2.00, ANP 0.07", rows);
    }

    [Fact]
    public void Fma_rows_from_the_columns() =>
        Assert.Equal(Example.Take(4), Approach().Rows().Take(4));

    [Theory]
    [InlineData("FMA reader starting", "FMA reader starting")]
    [InlineData("FMA announcements stopped: values do not match the SDK. Last known: A/T: N1.", "FMA announcements stopped: values do not match the SDK. Last known: A/T: N1.")]
    [InlineData("FMA announcements unavailable: aircraft memory not found", "FMA announcements unavailable: aircraft memory not found")]
    [InlineData("reader stopped", "FMA: reader stopped")]
    public void Fma_not_running_is_one_row(string readout, string row)
    {
        var rows = Pmdg737PfdText.Rows(Approach().Sample(), L, null, readout);
        Assert.Equal(row, rows[0]);
        Assert.Equal("Above the speed tape: selected speed 150", rows[1]);
    }

    [Fact]
    public void Fma_strip_extra_follows_the_columns()
    {
        var rows = Approach().G(3568, 1).Rows();
        Assert.Equal("AUTOPILOT", rows[4]);
        Assert.Equal(Example.Skip(4), rows.Skip(5));
    }

    [Fact]
    public void Flags_stand_in_the_instruments_place()
    {
        var rows = Approach().G(7400, 1).G(8056, 1).G(3744, 1).Rows();
        Assert.Contains("Altitude tape: ALT flag", rows);
        Assert.Contains("Vertical speed: VERT flag", rows);
        Assert.Contains("Heading: HDG flag", rows);
        Assert.DoesNotContain(rows, r => r.StartsWith("Heading: 177", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, r => r.StartsWith("Vertical speed: 750", StringComparison.Ordinal));
        Assert.True(rows.ToList().IndexOf("Altitude tape: ALT flag") < rows.ToList().IndexOf("Vertical speed: VERT flag"));
    }

    [Fact]
    public void Attitude_with_no_drawing_and_no_flag_says_not_shown()
    {
        // The IRU aligning: PMDG draws no attitude and no flag (live, ground session 1).
        var rows = Approach().G(2336, 0).Rows();
        Assert.Contains("Attitude: not shown", rows);
        Assert.DoesNotContain(rows, r => r.StartsWith("Attitude: pitch", StringComparison.Ordinal));
        Assert.Contains("Attitude: ATT flag", Approach().G(2312, 1).Iru(0).Rows());
    }

    [Fact]
    public void Light_sample_gives_the_fma_and_message_rows()
    {
        var light = new Pfd().Panel().G(1516, 1).G(1832, 0).G(1892, 1).G(2056, 1).G(2068, 1).G(2080, 1).G(7992, 1).Memory.Sample(full: false);
        Assert.Null(light.Reading);
        Assert.Equal(new[] { "A/T: MCP SPD", "Roll: VOR/LOC", "Pitch: G/S", "AFDS: CMD", "Message: NO VSPD" },
            Pmdg737PfdText.Rows(light, L, Columns, ""));
    }

    [Fact]
    public void Spoken_messages_in_layout_order()
    {
        var s = Approach().G(2056, 0).G(7412, 1).G(9244, 1).Sample();
        Assert.Equal(new[] { "SPD", "ALT DISAGREE" }, Pmdg737PfdText.SpokenMessages(s, L));
    }

    [Fact]
    public void Rows_are_invariant_under_de_DE()
    {
        var p = Approach().G(7696, 0).G(1688, 1).G(1656, 0.78);
        var expected = p.Rows();
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var rows = p.Rows();
            Assert.Equal(expected, rows);
            Assert.Contains("Attitude: pitch 2.5° up, wings level", rows);
            Assert.Contains("Below the altitude tape: 30.01 IN.", rows);
            Assert.Contains("Attitude: glideslope pointer, 0.1 dots below", rows);
            Assert.Contains("Attitude: approach info, ICVG/177, DME 4.8", rows);
            Assert.Contains("Above the speed tape: selected speed .78", rows);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

// Characterization tests for FenixChannelPfdRows, which turns one snapshot of the Fenix display channel into the rows of
// the PFD window. Pure logic: every snapshot is built with PfdPushes (FenixChannelPfdDecoderTests.cs), whose starting
// point is a cruise at 34,000 feet and Mach .775 at standard pressure (CruiseRows below), with no FMA text and no flight
// director.
//
// The contract: the rows run FMA, airspeed and speed target, the tape's speeds, Mach, altitude, vertical speed,
// attitude, flight director, heading, radio altitude and DH, then the LS rows; a row whose condition fails or whose value
// is not a number is left out, except a primary value of an instrument the PFD draws, which reads "not available"; an
// instrument the PFD does not draw (its flag) is one row in its place, in the PFD's words and with no color; numbers are
// written with the invariant culture, a minus is ASCII and a whole number rounds away from zero; a problem of the
// snapshot is one status row and is reported.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelPfdRowsTests
{
    // U+2800, the blank braille cell the display uses as a spacer. Named because it cannot be seen in source.
    private const char Spacer = (char)0x2800;

    // The cruise of PfdPushes with an empty FMA and no flight director.
    private static readonly string[] CruiseRows =
    [
        "Airspeed 268 knots", "Speed target 268 knots, managed", "VMAX 284 knots", "Green dot 237 knots", "VLS 232 knots",
        "Alpha prot 197 knots", "Mach .775, green", "Altitude 34011 feet, STD", "Vertical speed 0 feet per minute",
        "Pitch 3.1 degrees up, wings level", "Heading 313, track 318"
    ];

    private static IReadOnlyList<FenixFmaSlot> Fma(params (int Slot, string Text)[] texts)
    {
        FenixFmaSlot[] slots = Enumerable.Repeat(new FenixFmaSlot("", false), 16).ToArray();
        foreach ((int slot, string text) in texts)
        {
            slots[slot] = new FenixFmaSlot(text, false);
        }

        return slots;
    }

    private static IReadOnlyList<string> RowsOf(PfdPushes.Push push, IReadOnlyList<FenixFmaSlot>? fma = null,
        FenixFdState? fd = null)
    {
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelPfdRows.Compose(push.Snapshot(fma, fd), reported.Add);

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    private static string RowStartingWith(PfdPushes.Push push, string start, FenixFdState? fd = null)
        => Assert.Single(RowsOf(push, fd: fd), row => row.StartsWith(start, StringComparison.Ordinal));

    private static FenixChannelPfd PfdOf(PfdPushes.Push push) => FenixChannelPfd.From(push.Snapshot()).Value!;

    // The cruise rows with the ones from first for count replaced by these.
    private static string[] CruiseWith(int first, int count, params string[] replacement)
    {
        List<string> rows = CruiseRows.ToList();
        rows.RemoveRange(first, count);
        rows.InsertRange(first, replacement);
        return rows.ToArray();
    }

    private static void InCulture(string name, Action action)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- The cruise ---------------------------------------------------------------------------

    [Fact]
    public void A_cruise_pfd_reads_top_to_bottom()
    {
        Assert.Equal(CruiseRows, RowsOf(new PfdPushes.Push()));
    }

    [Fact]
    public void The_measured_constants_are_the_measured_ones()
    {
        Assert.Equal(
            (30.0, 2.0, 1.0, true, true, true, 10.0, "degrees", true, true, true),
            (FenixChannelPfdRows.TapeFloorKnots, FenixChannelPfdRows.TrendShownKnots,
                FenixChannelPfdRows.VerticalSpeedToFeetPerMinute, FenixChannelPfdRows.BankPositiveIsRight,
                FenixChannelPfdRows.LocDotsPositiveIsRight, FenixChannelPfdRows.GsDotsPositiveIsAbove,
                FenixChannelPfdRows.MetricStepMeters, FenixChannelPfdRows.FdUnitWord,
                FenixChannelPfdRows.FdPitchPositiveIsUp, FenixChannelPfdRows.FdRollPositiveIsRight,
                FenixChannelPfdRows.FdYawPositiveIsRight));
        Assert.Equal(
            ("PFD", "SPD flag", "ALT flag", "ATT flag", "HDG flag", "V/S flag"),
            (FenixChannelPfdRows.Feature, FenixChannelPfdRows.SpeedFlag, FenixChannelPfdRows.AltitudeFlag,
                FenixChannelPfdRows.AttitudeFlag, FenixChannelPfdRows.HeadingFlag,
                FenixChannelPfdRows.VerticalSpeedFlag));
    }

    // ---- The FMA ------------------------------------------------------------------------------

    [Fact]
    public void The_fma_reads_by_column()
    {
        IReadOnlyList<FenixFmaSlot> slots = Fma((0, "gMACH"), (1, "gALT!CRZ"), (2, "gNAV"), (4, "wAP1"),
            (10, "wfLVR CLB"), (14, "cA/THR"));
        string[] expected =
        [
            "Thrust: MACH, green", "Vertical: ALT CRZ, green", "Lateral: NAV, green",
            "Engagement: AP1, white; A/THR, cyan", "FMA message: LVR CLB, white"
        ];

        Assert.Equal(expected, FenixChannelPfdRows.FmaRows(slots));
        Assert.Equal(expected.Concat(CruiseRows), RowsOf(new PfdPushes.Push(), slots));
    }

    [Fact]
    public void The_approach_column_keeps_its_third_row_and_the_messages_run_by_column_then_slot()
    {
        // Slots 3, 8 and 13 are the lines the Fenix sent on the ILS to EDDF 07R, with the spacer cells it draws; the
        // message in slot 11 is the one it sent with no color letter. Slots 10 and 12 are here for the order.
        IReadOnlyList<FenixFmaSlot> slots = Fma((3, "CAT3"), (8, "DUAL"), (13, "RADIOdc" + Spacer + Spacer + " 200"),
            (12, "aTOO LOW"), (10, "wfLVR CLB"), (11, "CHECK SPEED MODE"));

        Assert.Equal(
            new[]
            {
                "Approach: CAT3 DUAL, RADIO white, 200 cyan", "FMA message: LVR CLB, white",
                "FMA message: CHECK SPEED MODE", "FMA message: TOO LOW, amber"
            },
            FenixChannelPfdRows.FmaRows(slots));
    }

    [Fact]
    public void The_altitude_says_amber_while_the_pfd_draws_it_amber()
    {
        // 122.8, measured 2026-11-03: the readout turns amber from a BARO minimum down, the only sign the PFD gives there, and
        // at a RADIO minimum. The word sits on the altitude, as the baro beside it stays its own color.
        Assert.Equal("Altitude 700 feet, amber, STD",
            RowStartingWith(new PfdPushes.Push { Altitude = 700, AltitudeAmber = true }, "Altitude"));
        Assert.Equal("Altitude 700 feet, STD", RowStartingWith(new PfdPushes.Push { Altitude = 700 }, "Altitude"));
        Assert.Equal("Altitude not available, STD",
            RowStartingWith(new PfdPushes.Push { Altitude = double.NaN, AltitudeAmber = true }, "Altitude"));
    }

    [Theory]
    [InlineData("gLAND", "LAND")]
    [InlineData("gFLARE", "FLARE")]
    [InlineData("gROLL OUT", "ROLL OUT")]
    public void The_lateral_row_is_left_out_while_the_pfd_draws_land_flare_or_roll_out(string coded, string mode)
    {
        // The PFD draws these across the Vertical and Lateral columns, and the Fenix keeps sending a lateral mode underneath
        // that it does not draw (measured in flight, 2026-11-04). The window lists what the glass shows, as the announcer does.
        Assert.Equal(new[] { $"Vertical: {mode}, green" }, FenixChannelPfdRows.FmaRows(Fma((1, coded), (2, "gLOC"))));
    }

    [Fact]
    public void The_lateral_row_stays_under_any_other_vertical_mode()
    {
        Assert.Equal(new[] { "Vertical: G/S, green", "Lateral: LOC, green" },
            FenixChannelPfdRows.FmaRows(Fma((1, "gG/S"), (2, "gLOC"))));
    }

    [Fact]
    public void The_fma_lines_measured_in_flight_read_as_the_pfd_draws_them()
    {
        // Each line as the channel sent it on 2026-11-03 (an A321, EDDF 07C to an ILS autoland on 07R), alone in its
        // column so the row shows exactly what that line says.
        Assert.Equal(new[] { "Approach: CAT3 DUAL, RADIO white, 200 cyan" },
            FenixChannelPfdRows.FmaRows(Fma((3, "CAT3"), (8, "DUAL"), (13, "RADIOdc" + Spacer + Spacer + " 200"))));
        Assert.Equal(new[] { "Approach: CAT3 SINGLE, BARO white, 700 cyan" },
            FenixChannelPfdRows.FmaRows(Fma((3, "CAT3"), (8, "SINGLE"), (13, "BAROdc" + Spacer + "  700"))));

        // The V/S reversion's line, with the letter p the flight director's flash came with, and the glide slope line
        // that fills slot 6 when slot 1 is empty.
        Assert.Equal(new[] { "Vertical: V/S green, +1800 cyan" },
            FenixChannelPfdRows.FmaRows(Fma((1, "bgpV/Scp+1800"))));
        Assert.Equal(new[] { "Vertical: G/S, cyan" }, FenixChannelPfdRows.FmaRows(Fma((1, ""), (6, " cG/S"))));

        // A message with no color letter is spoken as it is, and a slot with only its change box has nothing to say.
        Assert.Equal(new[] { "FMA message: CHECK SPEED MODE" },
            FenixChannelPfdRows.FmaRows(Fma((11, "CHECK SPEED MODE"))));

        FenixFmaSlot[] boxedAndEmpty = Fma().ToArray();
        boxedAndEmpty[0] = new FenixFmaSlot("", true);
        Assert.Empty(FenixChannelPfdRows.FmaRows(boxedAndEmpty));
        Assert.DoesNotContain(RowsOf(new PfdPushes.Push(), boxedAndEmpty), row => row.StartsWith("Thrust"));
    }

    [Fact]
    public void An_empty_fma_has_no_rows_and_a_slot_with_no_place_is_never_read()
    {
        Assert.Empty(FenixChannelPfdRows.FmaRows(Fma()));
        Assert.Empty(FenixChannelPfdRows.FmaRows([]));
        Assert.Empty(FenixChannelPfdRows.FmaRows(Fma((15, "gSTRAY"))));

        // A list shorter than the layout is not an error.
        Assert.Equal(new[] { "Thrust: MACH, green" },
            FenixChannelPfdRows.FmaRows([new FenixFmaSlot("gMACH", false)]));
    }

    [Fact]
    public void A_slot_with_an_unknown_letter_leaves_its_row_out_and_does_not_throw()
    {
        Assert.Equal(new[] { "Lateral: NAV, green" },
            FenixChannelPfdRows.FmaRows(Fma((0, "kMACH"), (2, "gNAV"), (10, "kLVR CLB"))));
    }

    [Fact]
    public void The_fd_row_reads_as_the_pfd_draws_it()
    {
        Assert.Equal((2, FenixFmaColumn.Engagement), FenixChannelFmaLayout.Of(FenixChannelPfdRows.FdSlot));

        Assert.Equal(new[] { "Engagement: AP1, white; 1 FD 2, white; A/THR, cyan" },
            FenixChannelPfdRows.FmaRows(Fma((4, "wAP1"), (9, "w1   2"), (14, "cA/THR"))));
        Assert.Equal(new[] { "Engagement: 1 FD -, white" }, FenixChannelPfdRows.FmaRows(Fma((9, "w1   -"))));
        Assert.Equal(new[] { "Engagement: - FD 2, white" }, FenixChannelPfdRows.FmaRows(Fma((9, "w-   2"))));

        // Another shape in the slot passes through as it came.
        Assert.Equal(new[] { "Engagement: FD OFF, white" }, FenixChannelPfdRows.FmaRows(Fma((9, "wFD OFF"))));
        Assert.Equal(new[] { "Engagement: 12, white" }, FenixChannelPfdRows.FmaRows(Fma((9, "w12"))));
        Assert.Equal(new[] { "Engagement: 3 4, white" }, FenixChannelPfdRows.FmaRows(Fma((9, "w3   4"))));

        // Both FDs off: the slot is blank and the column says nothing of them.
        Assert.Equal(new[] { "Engagement: AP1, white" }, FenixChannelPfdRows.FmaRows(Fma((4, "wAP1"), (9, ""))));
    }

    [Fact]
    public void The_fd_row_is_reworded_on_a_copy_and_the_slots_given_are_left_as_they_were()
    {
        IReadOnlyList<FenixFmaSlot> slots = Fma((9, "w1   2"));

        FenixChannelPfdRows.FmaRows(slots);

        Assert.Equal("w1   2", slots[9].Text);
    }

    // ---- Airspeed, the target and the tape ----------------------------------------------------

    [Fact]
    public void A_lost_instrument_shows_its_flag_in_its_place()
    {
        // The speed flag takes the airspeed, the target, the tape and the Mach rows, seven in the cruise.
        Assert.Equal(CruiseWith(0, 7, "SPD flag"), RowsOf(new PfdPushes.Push { SpeedShown = false }));
        Assert.Equal(CruiseWith(7, 1, "ALT flag"), RowsOf(new PfdPushes.Push { AltitudeShown = false }));
        Assert.Equal(CruiseWith(8, 1, "V/S flag"), RowsOf(new PfdPushes.Push { VerticalSpeedShown = false }));
        Assert.Equal(CruiseWith(9, 1, "ATT flag"), RowsOf(new PfdPushes.Push { AttitudeShown = false }));
        Assert.Equal(CruiseWith(10, 1, "HDG flag"), RowsOf(new PfdPushes.Push { HeadingShown = false }));
    }

    [Fact]
    public void A_lost_speed_shows_no_speed_whatever_the_push_holds()
    {
        var push = new PfdPushes.Push
        {
            SpeedShown = false, SpeedTrend = 9, V1 = 141, Vr = 143, V2 = 147, F = 190, S = 215, VfeNext = 235,
            MachText = "g.775"
        };

        Assert.Equal(CruiseWith(0, 7, "SPD flag"), RowsOf(push));
        Assert.Empty(FenixChannelPfdRows.TapeSpeeds(PfdOf(push)));
    }

    [Fact]
    public void A_lost_instrument_value_is_not_read_even_when_it_is_not_a_number()
    {
        var push = new PfdPushes.Push
        {
            SpeedShown = false, AltitudeShown = false, VerticalSpeedShown = false, AttitudeShown = false,
            HeadingShown = false, Airspeed = double.NaN, Altitude = double.NaN, VerticalSpeed = double.NaN,
            Pitch = double.NaN, Bank = double.NaN, MagHeading = double.NaN
        };

        Assert.Equal(
            new[] { "SPD flag", "ALT flag", "V/S flag", "ATT flag", "HDG flag" },
            RowsOf(push));
    }

    [Fact]
    public void A_shown_value_that_is_not_a_number_is_not_available()
    {
        // The airspeed, with a trend that would have been shown.
        Assert.Equal(CruiseWith(0, 1, "Airspeed not available"),
            RowsOf(new PfdPushes.Push { Airspeed = double.NaN, SpeedTrend = 6 }));

        // The heading; the track stays.
        Assert.Equal(CruiseWith(10, 1, "Heading not available, track 318"),
            RowsOf(new PfdPushes.Push { MagHeading = double.NaN }));
        Assert.Equal(CruiseWith(10, 1, "Heading 313"), RowsOf(new PfdPushes.Push { MagneticTrack = double.NaN }));

        // The attitude: each half on its own, and both.
        Assert.Equal(CruiseWith(9, 1, "Pitch not available, wings level"),
            RowsOf(new PfdPushes.Push { Pitch = double.NaN }));
        Assert.Equal(CruiseWith(9, 1, "Pitch 3.1 degrees up, bank not available"),
            RowsOf(new PfdPushes.Push { Bank = double.NaN }));
        Assert.Equal(CruiseWith(9, 1, "Pitch not available, bank not available"),
            RowsOf(new PfdPushes.Push { Pitch = double.NaN, Bank = double.NaN }));

        // The altitude keeps its baro reference, which is the EFIS's; the vertical speed.
        Assert.Equal(CruiseWith(7, 1, "Altitude not available, STD"),
            RowsOf(new PfdPushes.Push { Altitude = double.NaN, MetricAltitude = true }));
        Assert.Equal(CruiseWith(8, 1, "Vertical speed not available"),
            RowsOf(new PfdPushes.Push { VerticalSpeed = double.NaN }));

        // Rows that are not a primary value are left out.
        Assert.Equal(CruiseWith(4, 1), RowsOf(new PfdPushes.Push { Vls = double.NaN }));
        Assert.Equal(CruiseWith(1, 1), RowsOf(new PfdPushes.Push { TargetIas = double.NaN }));
    }

    [Fact]
    public void The_airspeed_reads_below_the_tape_floor_and_with_the_trend_arrow_where_it_is_drawn()
    {
        Assert.Equal("Airspeed below 30 knots", RowStartingWith(new PfdPushes.Push { Airspeed = 7 }, "Airspeed"));
        Assert.Equal("Airspeed below 30 knots", RowStartingWith(new PfdPushes.Push { Airspeed = 29.9 }, "Airspeed"));
        Assert.Equal("Airspeed 30 knots", RowStartingWith(new PfdPushes.Push { Airspeed = 30 }, "Airspeed"));

        // The arrow ends at the airspeed plus the trend; from a size of 2 knots on.
        Assert.Equal("Airspeed 268 knots, trend arrow to 275",
            RowStartingWith(new PfdPushes.Push { SpeedTrend = 6.8 }, "Airspeed"));
        Assert.Equal("Airspeed 268 knots, trend arrow to 266",
            RowStartingWith(new PfdPushes.Push { SpeedTrend = -2 }, "Airspeed"));
        Assert.Equal("Airspeed 268 knots, trend arrow to 270",
            RowStartingWith(new PfdPushes.Push { SpeedTrend = 2 }, "Airspeed"));
        Assert.Equal("Airspeed 268 knots", RowStartingWith(new PfdPushes.Push { SpeedTrend = 1.9 }, "Airspeed"));
        Assert.Equal("Airspeed 268 knots", RowStartingWith(new PfdPushes.Push { SpeedTrend = -1.9 }, "Airspeed"));
        Assert.Equal("Airspeed 268 knots", RowStartingWith(new PfdPushes.Push { SpeedTrend = double.NaN }, "Airspeed"));
    }

    [Fact]
    public void The_trend_arrow_stays_on_the_tape()
    {
        // The tape ends at 30 knots, so the PFD cannot draw an arrow from an airspeed under it or to a tip under it.
        // The first case has both under, the second only the tip (35 less 40 is under it), the third only the airspeed
        // (the tip at 85 is on the tape).
        Assert.Equal("Airspeed below 30 knots",
            RowStartingWith(new PfdPushes.Push { Airspeed = 25, SpeedTrend = -30 }, "Airspeed"));
        Assert.Equal("Airspeed 35 knots",
            RowStartingWith(new PfdPushes.Push { Airspeed = 35, SpeedTrend = -40 }, "Airspeed"));
        Assert.Equal("Airspeed below 30 knots",
            RowStartingWith(new PfdPushes.Push { Airspeed = 25, SpeedTrend = 60 }, "Airspeed"));

        // A tip on the tape is read, down to 30 knots itself.
        Assert.Equal("Airspeed 100 knots, trend arrow to 70",
            RowStartingWith(new PfdPushes.Push { Airspeed = 100, SpeedTrend = -30 }, "Airspeed"));
        Assert.Equal("Airspeed 70 knots, trend arrow to 30",
            RowStartingWith(new PfdPushes.Push { Airspeed = 70, SpeedTrend = -40 }, "Airspeed"));
        Assert.Equal("Airspeed 30 knots, trend arrow to 40",
            RowStartingWith(new PfdPushes.Push { Airspeed = 30, SpeedTrend = 10 }, "Airspeed"));
    }

    [Fact]
    public void The_speed_target_reads_managed_or_selected_while_the_bugs_are_visible()
    {
        Assert.Equal("Speed target 268 knots, managed", RowStartingWith(new PfdPushes.Push(), "Speed target"));
        Assert.Equal("Speed target 250 knots, selected",
            RowStartingWith(new PfdPushes.Push { SpeedManaged = false, TargetIas = 249.5 }, "Speed target"));

        Assert.DoesNotContain(RowsOf(new PfdPushes.Push { BugsVisible = false }),
            row => row.StartsWith("Speed target", StringComparison.Ordinal));
    }

    [Fact]
    public void The_speed_tape_runs_top_to_bottom()
    {
        // Every mark and limit but VMAX, green dot and VSW is a number: V1, VR and V2, the flap speeds, VFE next, VLS and alpha
        // prot. A made-up state that puts each of them in a different place on the tape.
        var push = new PfdPushes.Push
        {
            VMax = double.NaN, GreenDot = double.NaN, Vls = 128, AlphaProt = 105.6, V1 = 141, Vr = 143, V2 = 147, F = 190,
            S = 215, VfeNext = 235
        };

        Assert.Equal(
            new (string, double)[]
            {
                ("VFE next", 235), ("S speed", 215), ("F speed", 190), ("V2", 147), ("VR", 143), ("V1", 141),
                ("VLS", 128), ("Alpha prot", 105.6)
            },
            FenixChannelPfdRows.TapeSpeeds(PfdOf(push)));
        Assert.Equal(
            new[]
            {
                "Airspeed 268 knots", "Speed target 268 knots, managed", "VFE next 235 knots", "S speed 215 knots",
                "F speed 190 knots", "V2 147 knots", "VR 143 knots", "V1 141 knots", "VLS 128 knots",
                "Alpha prot 106 knots", "Mach .775, green"
            },
            RowsOf(push).Take(11));
    }

    [Fact]
    public void A_vsw_in_alternate_law_replaces_alpha_prot_and_a_speed_not_drawn_is_left_out()
    {
        var alternate = new PfdPushes.Push { Vsw = 120, AlphaProt = 125 };
        Assert.Equal(
            new (string, double)[] { ("VMAX", 283.7129), ("Green dot", 237.4975), ("VLS", 232.4935), ("VSW", 120) },
            FenixChannelPfdRows.TapeSpeeds(PfdOf(alternate)));

        var nothing = new PfdPushes.Push
        {
            VMax = double.NaN, Vls = double.NaN, AlphaProt = double.NaN, GreenDot = double.NaN
        };
        Assert.Empty(FenixChannelPfdRows.TapeSpeeds(PfdOf(nothing)));
    }

    [Fact]
    public void Equal_speeds_keep_the_order_of_the_tape_rows()
    {
        var push = new PfdPushes.Push
        {
            VMax = 200, Vls = 200, AlphaProt = 150, GreenDot = 200, F = 200, S = 200, VfeNext = 200, V1 = 150
        };

        Assert.Equal(
            new[] { "VMAX", "VFE next", "F speed", "S speed", "Green dot", "VLS", "Alpha prot", "V1" },
            FenixChannelPfdRows.TapeSpeeds(PfdOf(push)).Select(speed => speed.Name));
    }

    [Fact]
    public void A_speed_reads_whole_knots_rounded_away_from_zero()
    {
        Assert.Equal("VLS 232 knots", FenixChannelPfdRows.SpeedWords("VLS", 232.4935));
        Assert.Equal("Green dot 238 knots", FenixChannelPfdRows.SpeedWords("Green dot", 237.5));
        Assert.Equal("V1 141 knots", FenixChannelPfdRows.SpeedWords("V1", 140.5));
    }

    [Fact]
    public void The_mach_line_reads_its_text_with_its_color_and_a_blank_one_is_left_out()
    {
        Assert.Equal("Mach .775, green", RowStartingWith(new PfdPushes.Push { MachText = "g.775" }, "Mach"));
        Assert.DoesNotContain(RowsOf(new PfdPushes.Push { MachText = "" }),
            row => row.StartsWith("Mach", StringComparison.Ordinal));
        Assert.DoesNotContain(RowsOf(new PfdPushes.Push { MachText = "g   " }),
            row => row.StartsWith("Mach", StringComparison.Ordinal));
    }

    // ---- Altitude and the baro ----------------------------------------------------------------

    [Fact]
    public void The_baro_reference_follows_the_captains_efis()
    {
        // STD, as loaded in the cruise.
        Assert.Equal("Altitude 34011 feet, STD", RowStartingWith(new PfdPushes.Push(), "Altitude"));

        // QNH in hectopascals, and an absent field 1.14, which is 1013.
        Assert.Equal("Altitude 34011 feet, QNH 1029",
            RowStartingWith(new PfdPushes.Push { Efis = { QnhStd = false, Qnh = 1029f } }, "Altitude"));
        Assert.Equal("Altitude 34011 feet, QNH 1013",
            RowStartingWith(new PfdPushes.Push { Efis = { QnhStd = false, Qnh = 1013f } }, "Altitude"));

        // QNH in inches, the figure the Fenix sends, two places; without it the reference is not read.
        Assert.Equal("Altitude 34011 feet, QNH 29.92",
            RowStartingWith(
                new PfdPushes.Push { Efis = { QnhStd = false, QnhInches = true, QnhInch = 29.92f } }, "Altitude"));
        Assert.Equal("Altitude 34011 feet, baro not read",
            RowStartingWith(
                new PfdPushes.Push { Efis = { QnhStd = false, QnhInches = true, QnhInch = null } }, "Altitude"));

        // QFE is not a reference the window reads.
        Assert.Equal("Altitude 34011 feet, baro not read",
            RowStartingWith(new PfdPushes.Push { Efis = { QnhStd = false, Qfe = true } }, "Altitude"));
    }

    [Fact]
    public void The_baro_reference_is_worded_one_way_whatever_the_row()
    {
        Assert.Equal("STD", FenixChannelPfdRows.BaroReference(new FenixPfdBaro(true, false, false, 1013, double.NaN)));
        Assert.Equal("QNH 1029", FenixChannelPfdRows.BaroReference(new FenixPfdBaro(false, false, false, 1029, 30.39)));
        Assert.Equal("QNH 1014", FenixChannelPfdRows.BaroReference(new FenixPfdBaro(false, false, false, 1013.5, 29.9)));
        Assert.Equal("QNH 29.90", FenixChannelPfdRows.BaroReference(new FenixPfdBaro(false, true, false, 1013, 29.9)));
        Assert.Equal("QNH 29.92",
            FenixChannelPfdRows.BaroReference(new FenixPfdBaro(false, true, false, 1013, 29.920000076293945)));
        Assert.Null(FenixChannelPfdRows.BaroReference(new FenixPfdBaro(false, false, true, 1013, 29.92)));
        Assert.Null(FenixChannelPfdRows.BaroReference(new FenixPfdBaro(false, true, false, 1013, double.NaN)));
        Assert.Null(FenixChannelPfdRows.BaroReference(new FenixPfdBaro(false, false, false, double.NaN, 29.92)));
    }

    [Fact]
    public void A_negative_altitude_reads_with_a_minus()
    {
        var push = new PfdPushes.Push { Altitude = -120.8, Efis = { QnhStd = false, QnhInches = true, QnhInch = 29.92f } };

        Assert.Equal("Altitude -121 feet, QNH 29.92", RowStartingWith(push, "Altitude"));
        Assert.Equal("Altitude 0 feet, STD", RowStartingWith(new PfdPushes.Push { Altitude = -0.3 }, "Altitude"));

        InCulture("sv-SE", () => Assert.Equal("Altitude -121 feet, QNH 29.92", RowStartingWith(push, "Altitude")));
    }

    [Fact]
    public void The_metric_altitude_reads_while_its_flag_is_set()
    {
        Assert.Equal("Altitude -115 feet, STD, -40 meters",
            RowStartingWith(new PfdPushes.Push { Altitude = -115.1, MetricAltitude = true }, "Altitude"));
        Assert.Equal("Altitude 341 feet, STD, 100 meters",
            RowStartingWith(new PfdPushes.Push { Altitude = 341, MetricAltitude = true }, "Altitude"));
        Assert.Equal("Altitude 34011 feet, STD, 10370 meters",
            RowStartingWith(new PfdPushes.Push { MetricAltitude = true }, "Altitude"));
        Assert.Equal("Altitude -2 feet, STD, 0 meters",
            RowStartingWith(new PfdPushes.Push { Altitude = -2, MetricAltitude = true }, "Altitude"));

        // The part follows the reference, and is gone once the flag clears.
        Assert.Equal("Altitude 341 feet, baro not read, 100 meters",
            RowStartingWith(
                new PfdPushes.Push { Altitude = 341, MetricAltitude = true, Efis = { QnhStd = false, Qfe = true } },
                "Altitude"));
        Assert.Equal("Altitude 341 feet, STD", RowStartingWith(new PfdPushes.Push { Altitude = 341 }, "Altitude"));
    }

    [Fact]
    public void The_efis_meters_setting_is_not_what_decides_the_metric_altitude()
    {
        var push = new PfdPushes.Push { Altitude = 341 };
        push.Efis.Meters = true;
        push.OfficerEfis.Meters = true;

        Assert.Equal("Altitude 341 feet, STD", RowStartingWith(push, "Altitude"));
    }

    // ---- Vertical speed and attitude ----------------------------------------------------------

    [Theory]
    [InlineData(1786.8, "Vertical speed 1800 feet per minute up")]
    [InlineData(-999.9, "Vertical speed 1000 feet per minute down")]
    [InlineData(-1786.8, "Vertical speed 1800 feet per minute down")]
    [InlineData(50.0, "Vertical speed 100 feet per minute up")]
    [InlineData(-50.0, "Vertical speed 100 feet per minute down")]
    [InlineData(49.9, "Vertical speed 0 feet per minute")]
    [InlineData(-49.9, "Vertical speed 0 feet per minute")]
    [InlineData(0.0, "Vertical speed 0 feet per minute")]
    public void The_vertical_speed_reads_to_the_nearest_hundred_with_its_side(double rate, string expected)
    {
        Assert.Equal(expected, RowStartingWith(new PfdPushes.Push { VerticalSpeed = rate }, "Vertical speed"));
    }

    [Theory]
    [InlineData(3.1028, -0.0465, "Pitch 3.1 degrees up, wings level")]
    [InlineData(-2.46, 25.4, "Pitch 2.5 degrees down, bank 25 degrees right")]
    [InlineData(1.04, -1.0, "Pitch 1 degree up, bank 1 degree left")]
    [InlineData(10.0, 0.5, "Pitch 10 degrees up, bank 1 degree right")]
    [InlineData(0.25, -0.5, "Pitch 0.3 degrees up, bank 1 degree left")]
    [InlineData(-0.25, 0.4, "Pitch 0.3 degrees down, wings level")]
    [InlineData(0.04, 0.4, "Pitch 0 degrees, wings level")]
    [InlineData(-0.04, -0.4, "Pitch 0 degrees, wings level")]
    [InlineData(-31.96, -67.2, "Pitch 32 degrees down, bank 67 degrees left")]
    public void The_attitude_reads_pitch_to_one_place_and_the_bank_in_whole_degrees(double pitch, double bank,
        string expected)
    {
        Assert.Equal(expected, RowStartingWith(new PfdPushes.Push { Pitch = pitch, Bank = bank }, "Pitch"));
    }

    // ---- The flight director ------------------------------------------------------------------

    private static readonly FenixFdState BarsOn = FenixFdState.Default with { FdOnCapt = true };

    private static string[] CruiseWithFlightDirector(string row)
        => CruiseRows.Take(10).Append(row).Concat(CruiseRows.Skip(10)).ToArray();

    [Fact]
    public void The_flight_director_bars_read_where_they_are_drawn()
    {
        // The fields hold the commanded attitude; a bar sits at the commanded value minus the attitude.
        var push = new PfdPushes.Push { Pitch = 3.1, Bank = 0 };

        Assert.Equal(
            CruiseWithFlightDirector("Flight director: pitch bar 2.5 degrees up, roll bar 4 degrees left"),
            RowsOf(push, fd: BarsOn with { Pitch = 5.56, Roll = -4.04 }));
        Assert.Equal(
            CruiseWithFlightDirector("Flight director: pitch bar centered, roll bar centered"),
            RowsOf(push, fd: BarsOn with { Pitch = 3.1, Roll = 0 }));
        Assert.Equal(
            CruiseWithFlightDirector("Flight director: pitch bar 1 degree up, roll bar 2.1 degrees right"),
            RowsOf(push, fd: BarsOn with { Pitch = 4.1, Roll = 2.1 }));

        // A roll of 25 at a bank of 16.8, and a pitch commanded below the attitude.
        var turning = new PfdPushes.Push { Pitch = 3.1, Bank = 16.8 };
        Assert.Equal("Flight director: pitch bar 2.1 degrees down, roll bar 8.2 degrees right",
            RowStartingWith(turning, "Flight director", BarsOn with { Pitch = 1.0, Roll = 25 }));
    }

    [Fact]
    public void A_difference_that_rounds_to_nothing_is_a_centered_bar_and_a_difference_that_is_not_a_number_is_left_out()
    {
        Assert.Equal("Flight director: pitch bar centered, roll bar centered",
            FenixChannelPfdRows.FlightDirectorRow(BarsOn with { Pitch = 3.14, Roll = -0.04 }, true, 3.1, 0, 0));

        Assert.Equal("Flight director: roll bar 3 degrees right",
            FenixChannelPfdRows.FlightDirectorRow(BarsOn with { Pitch = double.NaN, Roll = 3 }, true, 3.1, 0, 0));
        Assert.Equal("Flight director: roll bar 3 degrees right",
            FenixChannelPfdRows.FlightDirectorRow(BarsOn with { Pitch = 3, Roll = 3 }, true, double.NaN, 0, 0));
        Assert.Equal("Flight director: pitch bar 1 degree down",
            FenixChannelPfdRows.FlightDirectorRow(BarsOn with { Pitch = 2, Roll = 3 }, true, 3, double.NaN, 0));
    }

    [Fact]
    public void The_yaw_bar_reads_only_while_roll_guidance_is_set()
    {
        FenixFdState guided = BarsOn with { Pitch = 3.1, Roll = 0, Yawbar = 67.39, RollGuidance = true };

        // 67.39 at heading 64.91 is right of center, after the other parts.
        Assert.Equal("Flight director: pitch bar centered, roll bar centered, yaw bar 2.5 degrees right",
            FenixChannelPfdRows.FlightDirectorRow(guided, true, 3.1, 0, 64.91));
        Assert.Equal("Flight director: pitch bar centered, roll bar centered, yaw bar 2.9 degrees left",
            FenixChannelPfdRows.FlightDirectorRow(guided with { Yawbar = 62 }, true, 3.1, 0, 64.91));

        // The same value without roll guidance is a number in cruise with an ILS tuned, and no bar.
        Assert.Equal("Flight director: pitch bar centered, roll bar centered",
            FenixChannelPfdRows.FlightDirectorRow(guided with { RollGuidance = false }, true, 3.1, 0, 64.91));

        // The difference is taken round the compass.
        Assert.Equal("Flight director: pitch bar centered, roll bar centered, yaw bar 2 degrees right",
            FenixChannelPfdRows.FlightDirectorRow(guided with { Yawbar = 1 }, true, 3.1, 0, 359));
        Assert.Equal("Flight director: pitch bar centered, roll bar centered, yaw bar 2 degrees left",
            FenixChannelPfdRows.FlightDirectorRow(guided with { Yawbar = 359 }, true, 3.1, 0, 1));

        // A yaw bar that is not a number, or a heading that is not, is left out.
        Assert.Equal("Flight director: pitch bar centered, roll bar centered",
            FenixChannelPfdRows.FlightDirectorRow(guided with { Yawbar = double.NaN }, true, 3.1, 0, 64.91));
        Assert.Equal("Flight director: pitch bar centered, roll bar centered",
            FenixChannelPfdRows.FlightDirectorRow(guided, true, 3.1, 0, double.NaN));

        // The inhibits set at touchdown take the other two bars; the yaw bar stays.
        Assert.Equal("Flight director: yaw bar 2.5 degrees right",
            FenixChannelPfdRows.FlightDirectorRow(guided with { InhibitPitch = true, InhibitRoll = true }, true, 3.1, 0,
                64.91));

        // Through the window, with the frame's heading.
        var push = new PfdPushes.Push { Pitch = 3.1, Bank = 0, MagHeading = 64.91 };
        Assert.Equal("Flight director: pitch bar centered, roll bar centered, yaw bar 2.5 degrees right",
            RowStartingWith(push, "Flight director", guided));
    }

    [Fact]
    public void The_yaw_bar_is_left_out_while_the_heading_is_flagged()
    {
        // With the IR's heading lost (46.1.2 false, the HDG flag) the frame's heading can be a stale number: the yaw bar
        // sits at its commanded heading minus a heading the PFD does not draw, so it has no place to be read.
        FenixFdState guided = BarsOn with { Pitch = 3.1, Roll = 0, Yawbar = 67.39, RollGuidance = true };
        var shown = new PfdPushes.Push { Pitch = 3.1, Bank = 0, MagHeading = 64.91 };
        var flagged = new PfdPushes.Push { Pitch = 3.1, Bank = 0, MagHeading = 64.91, HeadingShown = false };

        Assert.Equal("Flight director: pitch bar centered, roll bar centered, yaw bar 2.5 degrees right",
            RowStartingWith(shown, "Flight director", guided));

        // The pitch and roll bars are read as they were, and the heading row is the flag.
        Assert.Equal("Flight director: pitch bar centered, roll bar centered",
            RowStartingWith(flagged, "Flight director", guided));
        Assert.Contains("HDG flag", RowsOf(flagged, fd: guided));

        // With the yaw bar the only one drawn, nothing is left to read: no row.
        FenixFdState yawOnly = guided with { InhibitPitch = true, InhibitRoll = true };
        Assert.Equal("Flight director: yaw bar 2.5 degrees right", RowStartingWith(shown, "Flight director", yawOnly));
        Assert.DoesNotContain(RowsOf(flagged, fd: yawOnly),
            row => row.StartsWith("Flight director", StringComparison.Ordinal));
    }

    [Fact]
    public void Bars_that_are_not_drawn_leave_no_row()
    {
        FenixFdState numbers = BarsOn with { Pitch = 5.56, Roll = -4.04, Yawbar = 67.39, RollGuidance = true };
        FenixFdState nan = BarsOn with
        {
            FdOnFo = true, Pitch = double.NaN, Roll = double.NaN, Yawbar = double.NaN, RollGuidance = true
        };

        Assert.Null(FenixChannelPfdRows.FlightDirectorRow(nan, true, 3.1, 0, 64.91));
        Assert.Null(FenixChannelPfdRows.FlightDirectorRow(numbers with { FdOnCapt = false, FdOnFo = true }, true, 3.1, 0, 64.91));
        Assert.Null(FenixChannelPfdRows.FlightDirectorRow(FenixFdState.Default, true, 3.1, 0, 64.91));
        Assert.Null(FenixChannelPfdRows.FlightDirectorRow(numbers, false, 3.1, 0, 64.91));
        Assert.Null(FenixChannelPfdRows.FlightDirectorRow(numbers with { FdInhibit = true }, true, 3.1, 0, 64.91));

        // The flight path director is drawn instead, while the values stay numbers.
        Assert.Null(FenixChannelPfdRows.FlightDirectorRow(numbers with { TrkFpa = true }, true, 3.1, 0, 64.91));

        // One inhibit takes one bar.
        FenixFdState pitchAndRoll = numbers with { RollGuidance = false };
        Assert.Equal("Flight director: roll bar 4 degrees left",
            FenixChannelPfdRows.FlightDirectorRow(pitchAndRoll with { InhibitPitch = true }, true, 3.1, 0, 64.91));
        Assert.Equal("Flight director: pitch bar 2.5 degrees up",
            FenixChannelPfdRows.FlightDirectorRow(pitchAndRoll with { InhibitRoll = true }, true, 3.1, 0, 64.91));
        Assert.Null(FenixChannelPfdRows.FlightDirectorRow(
            pitchAndRoll with { InhibitPitch = true, InhibitRoll = true }, true, 3.1, 0, 64.91));
    }

    [Fact]
    public void Bars_that_are_not_drawn_leave_no_row_in_the_window_and_the_attitude_flag_stays()
    {
        var push = new PfdPushes.Push { Pitch = 3.1, Bank = 0 };
        FenixFdState numbers = BarsOn with { Pitch = 5.56, Roll = -4.04 };

        Assert.Equal(CruiseRows, RowsOf(push, fd: FenixFdState.Default));
        Assert.Equal(CruiseRows, RowsOf(push, fd: numbers with { FdOnCapt = false, FdOnFo = true }));
        Assert.Equal(CruiseRows, RowsOf(push, fd: numbers with { FdInhibit = true }));

        var lost = new PfdPushes.Push { AttitudeShown = false };
        Assert.Equal(CruiseWith(9, 1, "ATT flag"), RowsOf(lost, fd: numbers));
    }

    [Fact]
    public void The_flight_director_flash_reads_flashing()
    {
        FenixFdState bars = BarsOn with { Pitch = 5.56, Roll = -4.04 };

        Assert.Equal("Flight director: pitch bar 2.5 degrees up, roll bar 4 degrees left, flashing",
            FenixChannelPfdRows.FlightDirectorRow(bars with { FdFlash = true }, true, 3.1, 0, 0));
        Assert.Equal("Flight director: pitch bar 2.5 degrees up, roll bar 4 degrees left",
            FenixChannelPfdRows.FlightDirectorRow(bars with { FdFlash = false }, true, 3.1, 0, 0));

        // With no bar to speak there is no row, flashing or not.
        Assert.Null(FenixChannelPfdRows.FlightDirectorRow(
            FenixFdState.Default with { FdFlash = true }, true, 3.1, 0, 0));
        Assert.Null(FenixChannelPfdRows.FlightDirectorRow(
            BarsOn with { FdFlash = true, Pitch = double.NaN, Roll = double.NaN }, true, 3.1, 0, 0));
        Assert.Null(FenixChannelPfdRows.FlightDirectorRow(bars with { FdFlash = true, TrkFpa = true }, true, 3.1, 0, 0));
    }

    // ---- Heading ------------------------------------------------------------------------------

    [Theory]
    [InlineData(313.2, "313")]
    [InlineData(5.2, "005")]
    [InlineData(-1.44, "359")]
    [InlineData(359.6, "360")]
    [InlineData(359.4, "359")]
    [InlineData(-0.4, "360")]
    [InlineData(0.0, "360")]
    [InlineData(360.0, "360")]
    [InlineData(361.0, "001")]
    [InlineData(720.2, "360")]
    [InlineData(-90.0, "270")]
    [InlineData(180.0, "180")]
    public void A_compass_value_is_three_digits_from_001_to_360(double degrees, string expected)
    {
        Assert.Equal(expected, FenixChannelPfdRows.Compass(degrees));
    }

    [Fact]
    public void A_compass_value_that_is_not_a_number_is_null()
    {
        Assert.Null(FenixChannelPfdRows.Compass(double.NaN));
        Assert.Null(FenixChannelPfdRows.Compass(double.PositiveInfinity));
        Assert.Null(FenixChannelPfdRows.Compass(double.NegativeInfinity));
    }

    [Fact]
    public void A_negative_heading_near_north_reads_as_a_compass_heading()
    {
        Assert.Equal("Heading 359, track 318", RowStartingWith(new PfdPushes.Push { MagHeading = -1.44 }, "Heading"));
        Assert.Equal("Heading 360, track 318", RowStartingWith(new PfdPushes.Push { MagHeading = 359.6 }, "Heading"));
        Assert.Equal("Heading 313, track 360", RowStartingWith(new PfdPushes.Push { MagneticTrack = -0.4 }, "Heading"));
        Assert.Equal("Heading 005, track 318", RowStartingWith(new PfdPushes.Push { MagHeading = 5.2 }, "Heading"));
    }

    // ---- Radio altitude, DH and the LS rows -------------------------------------------------------

    [Fact]
    public void The_radio_altitude_reads_its_text_with_its_color_while_it_is_not_blank()
    {
        Assert.Equal(CruiseRows.Append("Radio altitude 190, amber"),
            RowsOf(new PfdPushes.Push { RadioAltitudeText = "a190" }));
        Assert.Equal(CruiseRows, RowsOf(new PfdPushes.Push { RadioAltitudeText = "a   " }));
    }

    [Fact]
    public void The_dh_message_reads_after_the_radio_altitude()
    {
        Assert.Equal(CruiseRows.Append("Radio altitude 190, amber").Append("DH"),
            RowsOf(new PfdPushes.Push { RadioAltitudeText = "a190", DhMessage = true }));
        Assert.Equal(CruiseRows.Append("Radio altitude 190, amber"),
            RowsOf(new PfdPushes.Push { RadioAltitudeText = "a190", DhMessage = false }));

        // The message is its own row: it does not wait for a radio altitude text.
        Assert.Equal(CruiseRows.Append("DH"), RowsOf(new PfdPushes.Push { DhMessage = true }));
    }

    private static PfdPushes.Push IlsPush(bool ls) => new()
    {
        Efis = { Ls = ls },
        LsIdent = "mIGWQ", LsFrequency = "m111.70", LsDme = "c4.2",
        LocDots = 0.4, GsDots = 0.5,
        ApproachCenter = "wILS", ApproachLoc = "aLOC", ApproachGs = "rG/S",
        MarkerText = "mOM", LsFlagText = "rLS"
    };

    [Fact]
    public void The_ls_rows_read_with_ls_on()
    {
        Assert.Equal(
            CruiseRows.Concat(new[]
            {
                "LS information: IGWQ, magenta; 111.70, magenta; 4.2, cyan",
                "Localizer diamond 0.4 dots right",
                "Glideslope diamond 0.5 dots above",
                "Approach labels: ILS, white; LOC, amber; G/S, red",
                "Marker beacon: OM, magenta",
                "LS flag: LS, red"
            }),
            RowsOf(IlsPush(true)));
    }

    [Fact]
    public void Ils_lines_show_only_with_ls_on()
    {
        // The LS information and the two scales wait for the LS button; the labels, the marker and the LS flag are shown
        // whenever their text is set.
        Assert.Equal(
            CruiseRows.Concat(new[]
            {
                "Approach labels: ILS, white; LOC, amber; G/S, red",
                "Marker beacon: OM, magenta",
                "LS flag: LS, red"
            }),
            RowsOf(IlsPush(false)));

        // With those texts blank too, LS off leaves nothing.
        PfdPushes.Push bare = IlsPush(false);
        bare.ApproachCenter = bare.ApproachLoc = bare.ApproachGs = bare.MarkerText = bare.LsFlagText = "";
        Assert.Equal(CruiseRows, RowsOf(bare));
    }

    [Fact]
    public void The_amber_ils_reminder_reads_as_the_pfd_draws_it()
    {
        // Measured: with the LS button off on an ILS approach the PFD draws an amber ILS, and 117.3 holds "ax1ILS".
        PfdPushes.Push push = IlsPush(false);
        push.ApproachCenter = push.ApproachLoc = push.ApproachGs = push.MarkerText = "";
        push.LsFlagText = "ax1ILS";

        Assert.Equal(CruiseRows.Append("LS flag: ILS, amber"), RowsOf(push));
    }

    [Fact]
    public void The_deviations_read_the_side_the_diamond_is_drawn_on()
    {
        PfdPushes.Push Deviations(double loc, double gs) => new() { Efis = { Ls = true }, LocDots = loc, GsDots = gs };

        // The rows name the diamond the PFD draws, so a pilot cannot read them as the aircraft's side.
        Assert.Equal(new[] { "Localizer diamond 0.4 dots right", "Glideslope diamond 0.5 dots above" },
            RowsOf(Deviations(0.4, 0.5)).Skip(CruiseRows.Length));
        Assert.Equal(new[] { "Localizer diamond 1.9 dots left", "Glideslope diamond 1.3 dots below" },
            RowsOf(Deviations(-1.94, -1.3)).Skip(CruiseRows.Length));

        // A figure that rounds to 0 is centered, and so is a real 0.0, which the message leaves out of its part.
        Assert.Equal(new[] { "Localizer centered", "Glideslope centered" },
            RowsOf(Deviations(0.04, -0.04)).Skip(CruiseRows.Length));
        Assert.Equal(new[] { "Localizer centered", "Glideslope centered" },
            RowsOf(Deviations(0.0, 0.0)).Skip(CruiseRows.Length));
        Assert.Equal(new[] { "Localizer diamond 1 dot right", "Glideslope diamond 1 dot below" },
            RowsOf(Deviations(1.02, -1)).Skip(CruiseRows.Length));

        // A scale the PFD does not draw is NaN on the wire, and has no row.
        Assert.Equal(new[] { "Glideslope diamond 0.5 dots above" },
            RowsOf(Deviations(double.NaN, 0.5)).Skip(CruiseRows.Length));
        Assert.Equal(CruiseRows, RowsOf(Deviations(double.NaN, double.NaN)));
    }

    [Fact]
    public void A_deviation_past_the_scale_reads_at_full_scale()
    {
        // The scales hold two dots either side, and the diamond sits at the scale's end from there on (2.35 and 4.0 drew
        // the half diamond at the end): the row says so and gives no figure the scale cannot show.
        Assert.Equal(2, FenixChannelPfdRows.FullScaleDots);

        PfdPushes.Push Deviations(double loc, double gs) => new() { Efis = { Ls = true }, LocDots = loc, GsDots = gs };

        Assert.Equal(new[] { "Localizer diamond at full scale, right", "Glideslope diamond at full scale, above" },
            RowsOf(Deviations(2.0, 2.0)).Skip(CruiseRows.Length));
        Assert.Equal(new[] { "Localizer diamond at full scale, right", "Glideslope diamond at full scale, above" },
            RowsOf(Deviations(2.35, 2.87)).Skip(CruiseRows.Length));
        Assert.Equal(new[] { "Localizer diamond at full scale, right", "Glideslope diamond at full scale, above" },
            RowsOf(Deviations(4.0, 4.0)).Skip(CruiseRows.Length));
        Assert.Equal(new[] { "Localizer diamond at full scale, left", "Glideslope diamond at full scale, below" },
            RowsOf(Deviations(-2.35, -2.35)).Skip(CruiseRows.Length));
        Assert.Equal(new[] { "Localizer diamond at full scale, left", "Glideslope diamond at full scale, below" },
            RowsOf(Deviations(-2.0, -4.0)).Skip(CruiseRows.Length));

        // Each scale on its own, and just inside the end the figure is still read.
        Assert.Equal(new[] { "Localizer diamond at full scale, left", "Glideslope diamond 1.9 dots above" },
            RowsOf(Deviations(-2.35, 1.94)).Skip(CruiseRows.Length));
        Assert.Equal(new[] { "Glideslope diamond at full scale, below" },
            RowsOf(Deviations(double.NaN, -2.35)).Skip(CruiseRows.Length));
    }

    [Fact]
    public void A_deviation_the_part_leaves_out_reads_centered_and_a_scale_not_drawn_has_no_row()
    {
        // The channel sends NaN for a scale it does not draw; 82.1 left out of a part that is sent is a real 0.0.
        var localizerLeftOut = new PfdPushes.Push { Efis = { Ls = true }, LocDots = 0, GsDots = double.NaN };
        Assert.Equal(new[] { "Localizer centered" }, RowsOf(localizerLeftOut).Skip(CruiseRows.Length));

        var glideslopeLeftOut = new PfdPushes.Push { Efis = { Ls = true }, LocDots = double.NaN, GsDots = 0 };
        Assert.Equal(new[] { "Glideslope centered" }, RowsOf(glideslopeLeftOut).Skip(CruiseRows.Length));

        // The same with the fields left out by their path, and with the whole part left out, which draws no scale.
        var bothLeftOut = new PfdPushes.Push { Efis = { Ls = true } }.Omit("82.1", "82.2");
        Assert.Equal(new[] { "Localizer centered", "Glideslope centered" }, RowsOf(bothLeftOut).Skip(CruiseRows.Length));
        var noPart = new PfdPushes.Push { Efis = { Ls = true }, LocDots = 0.5, GsDots = 0.5 }.Omit("82");
        Assert.Equal(CruiseRows, RowsOf(noPart));

        // Off the LS button there is no scale row either way.
        Assert.Equal(CruiseRows, RowsOf(new PfdPushes.Push { LocDots = 0, GsDots = 0 }));
    }

    // ---- A snapshot that cannot be read ---------------------------------------------------------

    [Fact]
    public void A_text_with_an_unknown_letter_is_one_status_row_and_is_reported()
    {
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelPfdRows.Compose(
            new PfdPushes.Push { MachText = "k.775" }.Snapshot(), reported.Add);

        Assert.Null(rows.Rows);
        Assert.Equal("unknown code 'k' in the Mach line", rows.Problem);
        Assert.Equal(new[] { "unknown code 'k' in the Mach line" }, reported);
    }

    [Fact]
    public void A_wrong_shape_is_one_status_row_and_is_reported()
    {
        var reported = new List<string>();

        FenixChannelRows rows = FenixChannelPfdRows.Compose(
            new PfdPushes.Push().AsVarint("28.5").Snapshot(), reported.Add);

        Assert.Null(rows.Rows);
        Assert.Equal("field 28.5 has the wrong shape", rows.Problem);
        Assert.Equal(new[] { "field 28.5 has the wrong shape" }, reported);
    }

    [Fact]
    public void A_snapshot_with_no_message_is_one_status_row_and_is_reported()
    {
        var reported = new List<string>();
        FenixChannelSnapshot snapshot = new PfdPushes.Push().Snapshot() with { Push = null };

        FenixChannelRows rows = FenixChannelPfdRows.Compose(snapshot, reported.Add);

        Assert.Null(rows.Rows);
        Assert.Equal("no data", rows.Problem);
        Assert.Equal(new[] { "no data" }, reported);
    }

    // ---- Cultures -----------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Rows_are_the_same_in_every_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var push = new PfdPushes.Push
            {
                Pitch = 3.1, Bank = 0, Efis = { QnhStd = false, QnhInches = true, QnhInch = 29.92f, Ls = true },
                LocDots = 0.4, GsDots = -1.3
            };
            IReadOnlyList<string> rows = RowsOf(push, fd: BarsOn with { Pitch = 5.56, Roll = -4.04 });

            Assert.Contains("Pitch 3.1 degrees up, wings level", rows);
            Assert.Contains("Altitude 34011 feet, QNH 29.92", rows);
            Assert.Contains("Localizer diamond 0.4 dots right", rows);
            Assert.Contains("Glideslope diamond 1.3 dots below", rows);
            Assert.Contains("Flight director: pitch bar 2.5 degrees up, roll bar 4 degrees left", rows);
            Assert.Contains("Mach .775, green", rows);

            // A minus is ASCII, and so is the wrapped difference of the yaw bar.
            var negative = new PfdPushes.Push { Altitude = -120.8, MagHeading = -1.44, MetricAltitude = true };
            IReadOnlyList<string> belowSea = RowsOf(negative);
            Assert.Contains("Altitude -121 feet, STD, -40 meters", belowSea);
            Assert.Contains("Heading 359, track 318", belowSea);
            Assert.Equal("Flight director: yaw bar 2 degrees right",
                FenixChannelPfdRows.FlightDirectorRow(
                    BarsOn with { Pitch = 3.1, Roll = 0, Yawbar = 1, RollGuidance = true, InhibitPitch = true,
                        InhibitRoll = true }, true, 3.1, 0, 359));
            Assert.Equal("Vertical speed 1800 feet per minute down",
                RowStartingWith(new PfdPushes.Push { VerticalSpeed = -1786.8 }, "Vertical speed"));
        });
    }
}

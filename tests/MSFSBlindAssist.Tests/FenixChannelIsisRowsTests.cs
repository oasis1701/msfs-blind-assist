// Characterization tests for FenixChannelIsisRows, the rows of the Fenix ISIS window: a decoded IsisState worded for a
// screen reader, in the ISIS's own layout (speed column, attitude, altitude column) and its own words where it draws
// words, with plan 02's PFD words for what the PFD also shows. Pure logic: every state is built here.
//
// What the rows follow, measured on 3.0.0.7613 (2026-11-04, evidence07-isis.md): the speed tape's
// floor is 30 kt; the Mach is on the wire only while the ISIS draws it (from 0.50 climbing until 0.45 descending), two
// decimals with no leading zero; pitch is positive up and bank positive right; the deviations are positive right and above,
// two dots full scale; the QNH is always hPa and the ISIS converts it to inches (two places) while 182.6 is clear; STD
// keeps the QNH. While initializing the ISIS draws only flags, "INIT 90s" and the baro; a quick realignment draws "ATT 10s"
// in place of the attitude; the bugs page replaces the whole display. The figures of 182.16 and 182.17 are the lengths
// of their phases, never a countdown. The slip has no row until its side and threshold are measured.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelIsisRowsTests
{
    private static readonly IReadOnlyList<FenixIsisBug> DefaultBugs =
    [
        new(false, 30), new(false, 30), new(false, 30), new(false, 30), new(false, 6000), new(false, 10000),
    ];

    // A cruise ISIS: hPa, STD, no LS scales, the default bugs, all off.
    private static FenixIsisState Cruise() => new(
        Initializing: false, Standard: true, LsShown: false, InBugsPage: false, HectoPascal: true, LocFailed: false,
        GsFailed: false, NewStyle: false, Pitch: 2.6, Bank: -1.2, Airspeed: 251.4, Altitude: 31012.2, Mach: 0.746,
        AlignSeconds: null, RealignSeconds: null, Qnh: 1013, LocDots: double.NaN, GsDots: double.NaN, SelectedBug: 0,
        Bugs: DefaultBugs, Slip: 0);

    private static IReadOnlyList<string> Rows(FenixIsisState state) => FenixChannelIsisRows.Rows(state);

    // ---- The main display --------------------------------------------------------------------

    [Fact]
    public void Cruise_rows_go_by_column()
    {
        Assert.Equal(
            new[]
            {
                "Airspeed 251 knots", "Mach .75", "Pitch 2.6 degrees up, bank 1 degree left", "Altitude 31012 feet, STD",
            },
            Rows(Cruise()).ToArray());
    }

    [Fact]
    public void Enabled_bugs_read_in_their_columns()
    {
        FenixIsisState state = Cruise() with
        {
            Bugs = [new(true, 140), new(false, 30), new(true, 160), new(false, 30), new(false, 6000), new(true, 10100)],
        };

        Assert.Equal(
            new[]
            {
                "Airspeed 251 knots", "Speed bug 140", "Speed bug 160", "Mach .75",
                "Pitch 2.6 degrees up, bank 1 degree left", "Altitude 31012 feet, STD", "Altitude bug 10100",
            },
            Rows(state).ToArray());
    }

    public static TheoryData<bool, bool, double, string> Baros() => new()
    {
        { true, true, 1013, "Altitude 31012 feet, STD" },           // STD keeps the QNH, which is not read
        { false, true, 1013.4, "Altitude 31012 feet, QNH 1013" },
        { false, true, 1016, "Altitude 31012 feet, QNH 1016" },
        { false, false, 1016, "Altitude 31012 feet, QNH 30.00" },   // measured: 1016 hPa drawn 30.00
        { false, false, 1013.2, "Altitude 31012 feet, QNH 29.92" },
        { true, false, 1016, "Altitude 31012 feet, STD" },
    };

    [Theory]
    [MemberData(nameof(Baros))]
    public void Baro_reads_std_or_qnh_in_its_unit(bool standard, bool hectoPascal, double qnh, string expected)
    {
        IReadOnlyList<string> rows = Rows(Cruise() with { Standard = standard, HectoPascal = hectoPascal, Qnh = qnh });

        Assert.Contains(expected, rows);
    }

    [Fact]
    public void A_qnh_that_is_no_number_reads_baro_not_read()
    {
        Assert.Contains("Altitude 31012 feet, baro not read", Rows(Cruise() with { Standard = false, Qnh = double.NaN }));
    }

    [Fact]
    public void Attitude_words_follow_the_measured_signs()
    {
        Assert.Contains("Pitch 9.4 degrees up, bank 27 degrees right", Rows(Cruise() with { Pitch = 9.38, Bank = 27.0 }));
        Assert.Contains("Pitch 1.5 degrees down, bank 11 degrees left", Rows(Cruise() with { Pitch = -1.5, Bank = -11.2 }));
        Assert.Contains("Pitch 0 degrees, wings level", Rows(Cruise() with { Pitch = 0.04, Bank = 0.4 }));
        Assert.Contains("Pitch 1 degree up, bank 1 degree right", Rows(Cruise() with { Pitch = 1.0, Bank = 1.4 }));
    }

    [Fact]
    public void A_speed_under_the_tape_floor_reads_below()
    {
        Assert.Contains("Airspeed below 30 knots", Rows(Cruise() with { Airspeed = 0 }));
        Assert.Contains("Airspeed below 30 knots", Rows(Cruise() with { Airspeed = 29.6 }));
        Assert.Contains("Airspeed 30 knots", Rows(Cruise() with { Airspeed = 30 }));
        Assert.Equal(30, FenixChannelIsisRows.TapeFloorKnots);
    }

    [Fact]
    public void Mach_shows_while_the_wire_sends_it()
    {
        Assert.Contains("Mach .50", Rows(Cruise() with { Mach = 0.5000536 }));
        Assert.Contains("Mach .47", Rows(Cruise() with { Mach = 0.467 }));
        Assert.Contains("Mach .55", Rows(Cruise() with { Mach = 0.5494 }));
        Assert.DoesNotContain(Rows(Cruise() with { Mach = double.NaN }), row => row.StartsWith("Mach"));

        // A Mach left out of the message decodes as 0, and the ISIS draws no Mach below .45.
        Assert.DoesNotContain(Rows(Cruise() with { Mach = 0 }), row => row.StartsWith("Mach"));
    }

    [Fact]
    public void A_nan_value_is_never_spoken_as_a_number()
    {
        FenixIsisState state = Cruise() with
        {
            Airspeed = double.NaN, Pitch = double.NaN, Bank = double.PositiveInfinity, Altitude = double.NaN,
            Mach = double.NaN, LsShown = true, LocDots = double.NaN, GsDots = double.NegativeInfinity,
        };

        IReadOnlyList<string> rows = Rows(state);

        Assert.Contains("Airspeed not available", rows);
        Assert.Contains("Pitch not available, bank not available", rows);
        Assert.Contains("Altitude not available, STD", rows);
        Assert.DoesNotContain(rows, row => row.Contains("NaN") || row.Contains("∞") || row.Contains("Infinity"));
    }

    // ---- The LS scales -----------------------------------------------------------------------

    [Fact]
    public void Ls_rows_follow_the_scales()
    {
        // Scales off: no row, though the wire carries the deviations (measured lined up at CYYZ).
        Assert.DoesNotContain(Rows(Cruise() with { LocDots = 0.23, GsDots = 4 }),
            row => row.StartsWith("Localizer") || row.StartsWith("Glideslope"));

        FenixIsisState on = Cruise() with { LsShown = true };

        Assert.Contains("Localizer centered", Rows(on with { LocDots = 0.04 }));
        Assert.Contains("Localizer diamond 0.4 dots right", Rows(on with { LocDots = 0.44 }));
        Assert.Contains("Localizer diamond 1.5 dots left", Rows(on with { LocDots = -1.54 }));
        Assert.Contains("Localizer diamond at full scale, left", Rows(on with { LocDots = -4 }));
        Assert.Contains("Localizer diamond 1 dot right", Rows(on with { LocDots = 1.0 }));
        Assert.Contains("Glideslope diamond 1.3 dots above", Rows(on with { GsDots = 1.25 }));
        Assert.Contains("Glideslope diamond at full scale, above", Rows(on with { GsDots = 4 }));
        Assert.Contains("Glideslope diamond 0.6 dots below", Rows(on with { GsDots = -0.6 }));
        Assert.Contains("Localizer scale, no diamond", Rows(on with { LocDots = double.NaN }));
        Assert.Contains("Glideslope scale, no diamond", Rows(on with { GsDots = double.NaN }));
    }

    [Fact]
    public void A_failed_scale_reads_its_flag()
    {
        IReadOnlyList<string> rows = Rows(Cruise() with
        {
            LsShown = true, LocFailed = true, GsFailed = true, LocDots = 0.4, GsDots = 1.0,
        });

        Assert.Contains("LOC flag", rows);
        Assert.Contains("G/S flag", rows);
        Assert.DoesNotContain(rows, row => row.StartsWith("Localizer") || row.StartsWith("Glideslope"));
    }

    [Fact]
    public void Ls_rows_come_after_the_attitude_and_before_the_altitude()
    {
        IReadOnlyList<string> rows = Rows(Cruise() with { LsShown = true, LocDots = 0.44, GsDots = 1.25 });

        Assert.Equal(
            new[]
            {
                "Airspeed 251 knots", "Mach .75", "Pitch 2.6 degrees up, bank 1 degree left",
                "Localizer diamond 0.4 dots right", "Glideslope diamond 1.3 dots above", "Altitude 31012 feet, STD",
            },
            rows.ToArray());
    }

    // ---- Initializing, realigning, the bugs page ---------------------------------------------

    [Fact]
    public void Initializing_draws_flags_and_its_length()
    {
        // As measured at the KLAX gate: no tapes, no attitude; ATT, SPD, ALT and "INIT 90s"; the baro kept; LOC and G/S
        // flags while they are set with the scales shown. 182.17 = 10 at the same time is not drawn.
        FenixIsisState state = Cruise() with
        {
            Initializing = true, AlignSeconds = 90, RealignSeconds = 10, Standard = false, Qnh = 1015, LsShown = true,
            LocFailed = true, GsFailed = true, Airspeed = 0.06, Altitude = 367.7, Pitch = -0.15, Mach = double.NaN,
        };

        Assert.Equal(
            new[] { "SPD flag", "ATT flag", "INIT 90 seconds", "LOC flag", "G/S flag", "ALT flag, QNH 1015" },
            Rows(state).ToArray());
    }

    [Fact]
    public void Initializing_with_no_length_says_init()
    {
        IReadOnlyList<string> rows = Rows(Cruise() with { Initializing = true, AlignSeconds = null });

        Assert.Contains("INIT", rows);
        Assert.DoesNotContain(rows, row => row.StartsWith("INIT "));
    }

    [Fact]
    public void Realignment_replaces_the_attitude()
    {
        IReadOnlyList<string> rows = Rows(Cruise() with { RealignSeconds = 10 });

        Assert.Equal(new[] { "Airspeed 251 knots", "Mach .75", "ATT 10 seconds", "Altitude 31012 feet, STD" }, rows.ToArray());
        Assert.DoesNotContain(rows, row => row.StartsWith("Pitch") || row.Contains("bank"));
    }

    [Fact]
    public void The_bugs_page_lists_every_bug()
    {
        FenixIsisState state = Cruise() with
        {
            InBugsPage = true, SelectedBug = 5,
            Bugs = [new(false, 30), new(true, 140), new(false, 30), new(false, 30), new(false, 6000), new(true, 10100)],
        };

        Assert.Equal(
            new[]
            {
                "Bugs page",
                "Speed bug 1: 30, off", "Speed bug 2: 140, on", "Speed bug 3: 30, off", "Speed bug 4: 30, off",
                "Altitude bug 1: 6000, off", "Altitude bug 2: 10100, on, selected",
            },
            Rows(state).ToArray());
    }

    [Fact]
    public void The_bugs_page_marks_the_first_bug_when_none_is_sent()
    {
        IReadOnlyList<string> rows = Rows(Cruise() with { InBugsPage = true, SelectedBug = 0 });

        Assert.Equal("Speed bug 1: 30, off, selected", rows[1]);
        Assert.Single(rows, row => row.EndsWith(", selected"));
    }

    [Fact]
    public void A_bug_value_that_is_no_number_reads_not_available()
    {
        IReadOnlyList<string> page = Rows(Cruise() with
        {
            InBugsPage = true, Bugs = [new(true, double.NaN)],
        });
        IReadOnlyList<string> main = Rows(Cruise() with { Bugs = [new(true, double.NaN)] });

        Assert.Contains("Speed bug 1: not available, on, selected", page);
        Assert.DoesNotContain(main, row => row.StartsWith("Speed bug"));
    }

    // ---- Culture and Compose -----------------------------------------------------------------

    [Fact]
    public void Rows_are_the_same_in_every_culture()
    {
        FenixIsisState state = Cruise() with { Standard = false, HectoPascal = false, Qnh = 1013.2, LsShown = true, LocDots = 0.44 };
        IReadOnlyList<string> invariant = TestCulture.Run("", () => Rows(state));

        foreach (string name in new[] { "de-DE", "sv-SE" })
        {
            Assert.Equal(invariant.ToArray(), TestCulture.Run(name, () => Rows(state)).ToArray());
        }

        Assert.Contains("Altitude 31012 feet, QNH 29.92", invariant);
        Assert.Contains("Pitch 2.6 degrees up, bank 1 degree left", invariant);
    }

    private static FenixChannelSnapshot Snapshot(FenixChannelProtoWriter? isis)
    {
        FenixChannelPush? push = isis is null
            ? FenixChannelPush.From(new FenixChannelProtoWriter().Double(197, 1000).ToArray())
            : FenixChannelPush.From(new FenixChannelProtoWriter().Message(182, isis).ToArray());
        return new(new FenixDisplayVerdict(true, "trusted"), null, push, null, 0, 1);
    }

    private static FenixChannelProtoWriter GoodIsis() => new FenixChannelProtoWriter()
        .Bool(1, true).Bool(6, true).Double(12, 251.4).Double(13, 31012.2).Double(18, 1016);

    [Fact]
    public void A_round_gauge_fenix_shows_no_isis()
    {
        var problems = new List<string>();
        FenixChannelRows rows = FenixChannelIsisRows.Compose(
            Snapshot(GoodIsis()), FenixVariant.Unknown with { HasIsis = false }, problems.Add);

        Assert.Null(rows.Rows);
        Assert.Equal("this Fenix has no ISIS", rows.Problem);
        Assert.Empty(problems);
    }

    [Fact]
    public void A_good_isis_composes_its_rows()
    {
        foreach (FenixVariant variant in new[] { FenixVariant.Unknown, FenixVariant.Unknown with { HasIsis = true } })
        {
            FenixChannelRows rows = FenixChannelIsisRows.Compose(Snapshot(GoodIsis()), variant, _ => { });

            Assert.Null(rows.Problem);
            Assert.Contains("Altitude 31012 feet, QNH 1016", rows.Rows!);
        }
    }

    [Fact]
    public void A_decoding_problem_is_reported_and_shown()
    {
        var problems = new List<string>();
        FenixChannelRows rows = FenixChannelIsisRows.Compose(
            Snapshot(GoodIsis().Varint(10, 2)), FenixVariant.Unknown, problems.Add);

        Assert.Equal("field 182.10 has the wrong shape", rows.Problem);
        Assert.Equal(new[] { "field 182.10 has the wrong shape" }, problems.ToArray());
    }

    [Fact]
    public void No_isis_in_the_push_is_no_isis_data()
    {
        var problems = new List<string>();
        FenixChannelRows rows = FenixChannelIsisRows.Compose(Snapshot(null), FenixVariant.Unknown, problems.Add);

        Assert.Equal("no ISIS data", rows.Problem);
        Assert.Equal(new[] { "no ISIS data" }, problems.ToArray());
    }

    [Fact]
    public void A_snapshot_with_no_push_is_no_isis_data()
    {
        var snapshot = new FenixChannelSnapshot(new FenixDisplayVerdict(true, "trusted"), null, null, null, 0, 1);

        Assert.Equal("no ISIS data", FenixChannelIsisRows.Compose(snapshot, FenixVariant.Unknown, _ => { }).Problem);
    }
}

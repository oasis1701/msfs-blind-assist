using MSFSBlindAssist.Services.BBS757;
using Xunit;
using static MSFSBlindAssist.Tests.Bbs757AdiReaderTests;

namespace MSFSBlindAssist.Tests;

/// <summary>The ADI's rows and words (plan 27, Task 5): the rows of docs/b757/evidence/displays-adi.md, "Step 4", in its
/// order and wording, each starting with where it sits; a flag in its instrument's place; and the five flags as the
/// only thing spoken. The readings are decoded by the sampler's own draw rules from
/// <see cref="Bbs757AdiReaderTests.Approach"/>, so a row is checked against what memory held.</summary>
public class Bbs757AdiTextTests
{
    private static readonly Bbs757AdiLayout Layout = Bbs757AdiLayoutTests.Sample();

    /// <summary>The approach of the sampler's tests, changed, as the sampler decodes it.</summary>
    internal static Bbs757AdiReading Reading(Func<Bbs757AdiRaw, Bbs757AdiRaw>? change = null, Bbs757AdiSim? sim = null,
        bool machShown = true, Bbs757AdiLayout? layout = null)
    {
        var raw = Approach();
        return Bbs757AdiReader.Decode(layout ?? Layout, change is null ? raw : change(raw), sim ?? In(), machShown);
    }

    private static Bbs757AdiRaw Speed(Bbs757AdiRaw r, Func<Bbs757AdiSpeedRaw, Bbs757AdiSpeedRaw> change) => r with { Speed = change(r.Speed!) };
    private static Bbs757AdiRaw Ils(Bbs757AdiRaw r, Func<Bbs757AdiIlsRaw, Bbs757AdiIlsRaw> change) => r with { Ils = change(r.Ils!) };
    private static Bbs757AdiRaw Ra(Bbs757AdiRaw r, Func<Bbs757AdiRadioAltitudeRaw, Bbs757AdiRadioAltitudeRaw> change) => r with { RadioAltitude = change(r.RadioAltitude!) };
    private static Bbs757AdiRaw Attitude(Bbs757AdiRaw r, Func<Bbs757AdiAttitudeRaw, Bbs757AdiAttitudeRaw> change) => r with { Attitude = change(r.Attitude!) };

    private static IReadOnlyList<string> Rows(Bbs757AdiReading reading) => Bbs757AdiText.Rows(reading);

    private static IReadOnlyList<string> Changes(Bbs757AdiReading heard, Bbs757AdiReading now, bool withPrefix = true) =>
        Bbs757AdiText.Changes(heard, now, withPrefix);

    private static readonly string[] Places = { "Left: ", "Center: ", "Right: ", "Bottom: " };

    /// <summary>Everything the ADI can draw at once: the roll's V1 and VR, the F bug, Mach, a TCAS advisory both ways,
    /// the rising runway, ALT.</summary>
    private static Bbs757AdiReading Everything() => Reading(
        r => Speed(r, s => s with { TimerKept = 60 }) with
        {
            FlightDirector = new Bbs757AdiFlightDirectorRaw(true, 9.9, 7),
            Tcas = new Bbs757AdiTcasRaw(1, 1, 5, 8),
            Ils = r.Ils! with { GsPointer = 0.5 },
        },
        In(mach: 0.78));

    private static readonly string[] EverythingRows =
    {
        "Left: airspeed 250",
        "Left: target speed 180",
        "Left: V1 135",
        "Left: VR 140",
        "Left: flap maneuvering speed 161",
        "Left: Mach .780",
        "Left: ground speed 240",
        "Center: pitch 2.5 up, bank 10 left",
        "Center: flight director, pitch bar 7.4 above, roll bar 3 right",
        "Center: TCAS, keep the pitch above 5.0 up",
        "Center: TCAS, keep the pitch below 8.0 up",
        "Center: rising runway",
        "Right: glideslope, pointer 0.5 dots up",
        "Right: ALT",
        "Right: DH 200",
        "Right: radio altitude 1500",
        "Bottom: localizer, pointer 0.3 dots right, expanded scale",
    };

    [Fact]
    public void Rows_are_in_screen_order_each_starting_with_its_place()
    {
        var rows = Rows(Everything());
        Assert.Equal(EverythingRows, rows);
        Assert.All(rows, row => Assert.Contains(Places, place => row.StartsWith(place, StringComparison.Ordinal)));
        // Left, then center, then right, then bottom: never out of order.
        var order = rows.Select(row => Array.FindIndex(Places, place => row.StartsWith(place, StringComparison.Ordinal))).ToList();
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void The_gate_reads_as_the_capture_showed()
    {
        // The evidence's live reading: the tape at 30 with the MCP's 200 above it, V1 INOP, GS 0, the horizon on the
        // symbol, the pitch bar under the 10° line, both pointers at their scales' ends, DH200 and -6.
        Assert.Equal(new[]
        {
            "Left: airspeed 30",
            "Left: target speed 200",
            "Left: V1 INOP",
            "Left: ground speed 0",
            "Center: pitch 0.5 down, wings level",
            "Center: flight director, pitch bar 8.5 above, roll bar centered",
            "Right: glideslope, pointer at full scale, up",
            "Right: DH 200",
            "Right: radio altitude -6",
            "Bottom: localizer, pointer at full scale, right",
        }, Rows(GateReading()));
    }

    [Fact]
    public void A_flag_replaces_its_instruments_value()
    {
        // ATT: the flag in the horizon's place, no pitch or bank beside it, though the simulator still gives both.
        var att = Reading(r => Attitude(r, a => a with { IrsAttitude = false }));
        Assert.Contains("Center: ATT flag", Rows(att));
        Assert.DoesNotContain(Rows(att), row => row.Contains("pitch") || row.Contains("bank") || row.Contains("level"));

        // SPD: the flag, and no other row of the tape, though the adc still holds an airspeed.
        var spd = Reading(r => Speed(r, s => s with { AdcFailed = true }));
        Assert.Equal(new[] { "Left: SPD flag" }, Rows(spd).Where(row => row.StartsWith("Left: ")));

        // G/S and LOC: the flag in the scale's place, though the block still holds a valid pointer.
        var gs = Reading(r => Ils(r, i => i with { GsFlag = true }));
        Assert.Equal(new[] { "Right: G/S flag" }, Rows(gs).Where(row => row.Contains("G/S") || row.Contains("glideslope")));
        var loc = Reading(r => Ils(r, i => i with { LocFlag = true }));
        Assert.Equal(new[] { "Bottom: LOC flag" }, Rows(loc).Where(row => row.StartsWith("Bottom: ")));

        // RA: the flag in the number's place, though the ra still holds an altitude.
        var ra = Reading(r => Ra(r, x => x with { RaFailed = true }));
        Assert.Equal(new[] { "Right: RA flag" }, Rows(ra).Where(row => row.Contains("RA ") || row.Contains("radio altitude")));

        // Each is listed in its instrument's place: the other rows stay where they were.
        var all = Reading(r => Ra(Ils(Speed(Attitude(r, a => a with { IrsAttitude = false }), s => s with { AdcFailed = true }),
            i => i with { GsFlag = true, LocFlag = true }), x => x with { RaFailed = true }));
        Assert.Equal(new[] { "Left: SPD flag", "Center: ATT flag", "Right: G/S flag", "Right: ALT", "Right: RA flag", "Bottom: LOC flag" }, Rows(all));

        // And spoken once: as it appears, then nothing while it stays.
        var clean = Reading();
        Assert.Equal(new[] { "ADI flag: ATT" }, Changes(clean, att));
        Assert.Empty(Changes(att, att));
        Assert.Empty(Changes(att, Reading(r => Attitude(r, a => a with { IrsAttitude = false, IrsMode = 4 }), In(pitch: 3))));
        Assert.Equal(new[] { "ADI flag gone: ATT" }, Changes(att, clean));
    }

    [Fact]
    public void A_pinned_pointer_reads_full_scale()
    {
        string Bottom(double pointer, bool expanded = false) =>
            Rows(Reading(r => Ils(r, i => i with { LocPointer = pointer, Expanded = expanded }))).Single(row => row.StartsWith("Bottom: "));
        string Right(double dots) =>
            Rows(Reading(r => Ils(r, i => i with { GsPointer = dots }))).Single(row => row.Contains("glideslope"));

        Assert.Equal("Bottom: localizer, pointer at full scale, left", Bottom(-1));
        Assert.Equal("Bottom: localizer, pointer at full scale, right", Bottom(1));
        Assert.Equal("Bottom: localizer, pointer 1.9 dots left", Bottom(-0.95));
        // The expanded scale's end is half a dot: a number, never the normal scale's "full scale".
        Assert.Equal("Bottom: localizer, pointer 0.5 dots left, expanded scale", Bottom(-1, expanded: true));

        Assert.Equal("Right: glideslope, pointer at full scale, up", Right(2.2));
        Assert.Equal("Right: glideslope, pointer at full scale, down", Right(-2.2));
        Assert.Equal("Right: glideslope, pointer 2.1 dots down", Right(-2.1));

        // Never a number beyond the scale, whatever the field holds.
        Assert.Equal("Bottom: localizer, pointer at full scale, right", Bottom(1.7));
        Assert.Equal("Right: glideslope, pointer at full scale, up", Right(3.5));
        var beyond = Rows(Reading(r => Ils(r, i => i with { LocPointer = -1.7, GsPointer = -3.5, Expanded = false })));
        Assert.Contains("Right: glideslope, pointer at full scale, down", beyond);
        Assert.Contains("Bottom: localizer, pointer at full scale, left", beyond);
        Assert.DoesNotContain(beyond, row => row.Contains("dots"));
    }

    [Fact]
    public void Radio_altitude_above_its_range_is_blank()
    {
        static bool IsRadioAltitude(string row) => row.Contains("radio altitude") || row.Contains("RA ");

        Assert.Contains("Right: radio altitude 2500", Rows(Reading(r => Ra(r, x => x with { RaAltitude = 2516.9 }))));
        foreach (double stored in new[] { 2517.5, 3000, 12000, 41000 })
        {
            var rows = Rows(Reading(r => Ra(r, x => x with { RaAltitude = stored })));
            Assert.DoesNotContain(rows, IsRadioAltitude);   // no number, and no flag: the ra works
            Assert.Contains("Right: DH 200", rows);         // the decision height is drawn all the same
        }
        // Coming into range and leaving it is never spoken.
        Assert.Empty(Changes(Reading(r => Ra(r, x => x with { RaAltitude = 3000 })), Reading()));
        Assert.Empty(Changes(Reading(), Reading(r => Ra(r, x => x with { RaAltitude = 3000 }))));
    }

    [Fact]
    public void No_line_repeats_a_bluebird_callout()
    {
        // BlueBird plays its own 80 knots, V1 and rotate on the roll, its radio altitude and minimums callouts on the
        // approach, and its TCAS, windshear and terrain aurals. Every state those pass through says nothing here.
        var steps = new List<Bbs757AdiReading>();
        foreach (double knots in new double[] { 30, 60, 79, 80, 81, 100, 134, 135, 136, 139, 140, 141, 160 })
            steps.Add(Reading(r => Speed(r, s => s with { Airspeed = knots, TimerKept = 20, FlapSpeed = 201 }), In(groundSpeed: knots)));
        steps.Add(Reading(r => Speed(r, s => s with { Airspeed = 180, TimerKept = 121, FlapSpeed = 201 })));
        foreach (double feet in new double[] { 3000, 2501, 2500, 2400, 1000, 501, 500, 499, 400, 201, 200, 199, 100, 50, 40, 30, 20, 10, 0 })
            steps.Add(Reading(r => Ra(r, x => x with { RaAltitude = feet + 17 })));
        steps.Add(Reading(r => Ra(r, x => x with { DhShown = false })));
        steps.Add(Reading(r => r with { Tcas = new Bbs757AdiTcasRaw(1, 0, 5, 0) }));
        steps.Add(Reading(r => r with { Tcas = new Bbs757AdiTcasRaw(0, 1, 0, 2) }));
        steps.Add(Reading(r => Ils(r, i => i with { RollMode = 2 })));
        steps.Add(Reading(r => Ils(r, i => i with { GsPointer = 2.2, LocPointer = 1, Expanded = false })));
        steps.Add(Reading(r => r with { FlightDirector = new Bbs757AdiFlightDirectorRaw(false, 0, 0) }));
        steps.Add(Reading(sim: In(pitch: -15, bank: -30, mach: 0.8)));

        foreach (var heard in steps)
            foreach (var now in steps)
                Assert.Empty(Changes(heard, now));

        // Whatever is spoken is a flag coming or going, in its own words, and nothing else.
        var flagged = new[]
        {
            Reading(r => Attitude(r, a => a with { IrsState = 0 })),
            Reading(r => Speed(r, s => s with { AdcFailed = true })),
            Reading(r => Ils(r, i => i with { GsFlag = true })),
            Reading(r => Ils(r, i => i with { LocFlag = true })),
            Reading(r => Ra(r, x => x with { RaFailed = true })),
        };
        var spoken = new List<string>();
        foreach (var flag in flagged)
            foreach (var step in steps)
            {
                spoken.AddRange(Changes(step, flag));
                spoken.AddRange(Changes(flag, step));
            }
        Assert.NotEmpty(spoken);
        Assert.All(spoken, line => Assert.Matches("^ADI flag( gone)?: (ATT|SPD|LOC|G/S|RA)$", line));
        string[] callouts = { "knots", "V1", "rotate", "minimums", "hundred", "thousand", "traffic", "climb", "descend", "windshear", "terrain", "pull up" };
        Assert.All(spoken, line => Assert.DoesNotContain(callouts, word => line.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Numbers_format_as_drawn_under_de_DE_and_sv_SE(string culture)
    {
        Assert.Equal(EverythingRows, TestCulture.Run(culture, () => Rows(Everything())));
        Assert.Equal(new[] { "ADI flag: SPD, RA" }, TestCulture.Run(culture, () => Changes(Reading(),
            Reading(r => Ra(Speed(r, s => s with { AdcFailed = true }), x => x with { RaFailed = true })))));
        // A fraction in every number that has one: a point, never a comma.
        var rows = TestCulture.Run(culture, () => Rows(Reading(
            r => Ils(r, i => i with { GsPointer = -1.25, LocPointer = -0.35, Expanded = false }) with
            {
                FlightDirector = new Bbs757AdiFlightDirectorRaw(true, 0.25, 12.6),
                Tcas = new Bbs757AdiTcasRaw(1, 0, -1.5, 0),
            },
            In(pitch: 1.26, bank: -3.6, groundSpeed: 249.9, mach: 0.4056))));
        Assert.Contains("Left: Mach .406", rows);
        Assert.Contains("Left: ground speed 249", rows);   // a whole number, truncated
        Assert.Contains("Center: pitch 1.3 down, bank 4 right", rows);
        Assert.Contains("Center: flight director, pitch bar 1.5 above, roll bar 16 left", rows);
        Assert.Contains("Center: TCAS, keep the pitch above 1.5 down", rows);
        Assert.Contains("Right: glideslope, pointer 1.3 dots down", rows);
        Assert.Contains("Bottom: localizer, pointer 0.7 dots left", rows);
        Assert.DoesNotContain(rows, row => System.Text.RegularExpressions.Regex.IsMatch(row, @"\d,\d"));
    }

    [Fact]
    public void Level_wings_level_and_centered_have_their_words()
    {
        var rows = Rows(Reading(
            r => Ils(r, i => i with { GsPointer = 0.04, LocPointer = -0.02, Expanded = false }) with
            {
                FlightDirector = new Bbs757AdiFlightDirectorRaw(true, 0, 0.3),
                Tcas = new Bbs757AdiTcasRaw(1, 1, 0.04, -0.04),
            },
            In(pitch: 0.04, bank: 0.4)));
        Assert.Contains("Center: level, wings level", rows);
        Assert.Contains("Center: flight director, pitch bar centered, roll bar centered", rows);
        Assert.Contains("Center: TCAS, keep the pitch above level", rows);
        Assert.Contains("Center: TCAS, keep the pitch below level", rows);
        Assert.Contains("Right: glideslope, pointer centered", rows);
        Assert.Contains("Bottom: localizer, pointer centered", rows);
        // On the expanded scale too, with its name.
        Assert.Contains("Bottom: localizer, pointer centered, expanded scale",
            Rows(Reading(r => Ils(r, i => i with { LocPointer = 0.05, Expanded = true }))));

        // Just past the thresholds: 0.05 of a degree or a dot, half a degree of bank.
        var past = Rows(Reading(
            r => Ils(r, i => i with { GsPointer = -0.05, LocPointer = 0.03, Expanded = false }) with
            {
                FlightDirector = new Bbs757AdiFlightDirectorRaw(true, 0.06, -0.6),
            },
            In(pitch: 0, bank: 0)));
        Assert.Contains("Center: flight director, pitch bar 0.1 above, roll bar 1 right", past);
        Assert.Contains("Right: glideslope, pointer 0.1 dots down", past);
        Assert.Contains("Bottom: localizer, pointer 0.1 dots right", past);
    }

    [Fact]
    public void The_attitude_row_says_what_the_center_shows()
    {
        Assert.Contains("Center: no attitude", Rows(Reading(r => Attitude(r, a => a with { IrsState = 3, IrsMode = 2, IrsAttitude = false }))));
        Assert.Contains("Center: pitch 10.0 down, bank 25 right", Rows(Reading(sim: In(pitch: 10, bank: -25))));
        Assert.Contains("Center: pitch 18.0 up, wings level", Rows(Reading(sim: In(pitch: -22, bank: 0))));   // the horizon's stop
        // One simulator value not given: its item is left out; neither: no row.
        Assert.Contains("Center: pitch 2.5 up", Rows(Reading(sim: In(bank: null))));
        Assert.Contains("Center: bank 10 left", Rows(Reading(sim: In(pitch: null))));
        Assert.DoesNotContain(Rows(Reading(sim: In(pitch: null, bank: null))),
            row => row.StartsWith("Center: pitch") || row.StartsWith("Center: bank") || row.StartsWith("Center: level") || row.Contains("attitude"));
    }

    [Fact]
    public void A_scale_without_its_pointer_and_a_scale_not_drawn()
    {
        var alone = Rows(Reading(r => Ils(r, i => i with { GsValid = false, LocValid = false })));
        Assert.Contains("Right: glideslope scale, no pointer", alone);
        Assert.Contains("Bottom: localizer scale, no pointer", alone);
        Assert.DoesNotContain("Center: rising runway", alone);

        var none = Rows(Reading(r => Ils(r, i => i with { GsScale = false, LocScale = false })));
        Assert.DoesNotContain(none, row => row.Contains("glideslope") || row.Contains("localizer"));
        Assert.DoesNotContain(none, row => row.StartsWith("Bottom: "));
    }

    [Theory]
    [InlineData(0.78, ".780")]
    [InlineData(0.4, ".400")]
    [InlineData(0.3849, ".385")]
    [InlineData(0.8451, ".845")]
    [InlineData(1.02, "1.020")]
    public void Mach_has_three_decimals_and_no_leading_zero(double mach, string shown)
    {
        Assert.Contains("Left: Mach " + shown, Rows(Reading(sim: In(mach: mach))));
    }

    [Fact]
    public void Mach_is_absent_while_the_tape_shows_dashes()
    {
        Assert.DoesNotContain(Rows(Reading(machShown: false)), row => row.Contains("Mach"));
    }

    [Fact]
    public void A_whole_number_is_rounded_or_cut_as_its_instrument_shows_it()
    {
        var rows = Rows(Reading(r => Speed(r, s => s with { Airspeed = 249.6, McpSpeed = 179.999, FlapSpeed = 160.5 }), In(groundSpeed: 0.9)));
        Assert.Contains("Left: airspeed 250", rows);
        Assert.Contains("Left: target speed 180", rows);
        Assert.Contains("Left: flap maneuvering speed 161", rows);
        Assert.Contains("Left: ground speed 0", rows);
    }

    [Fact]
    public void A_part_not_read_has_its_one_row_in_its_place()
    {
        var layout = Layout with
        {
            Speed = null, SpeedMissing = "speed: no stopwatch in the afds",
            Tcas = null, TcasMissing = null,
        };
        var rows = Rows(Reading(layout: layout));
        Assert.Equal("Speed tape: not read: speed: no stopwatch in the afds", rows[0]);
        Assert.Equal(new[]
        {
            "Speed tape: not read: speed: no stopwatch in the afds",
            "Center: pitch 2.5 up, bank 10 left",
            "Center: flight director, pitch bar centered, roll bar 5 right",
            "TCAS: not read: not in this layout file",
            "Center: rising runway",
            "Right: glideslope, pointer 0.5 dots down",
            "Right: ALT",
            "Right: DH 200",
            "Right: radio altitude 1500",
            "Bottom: localizer, pointer 0.3 dots right, expanded scale",
        }, rows);

        var none = Layout with
        {
            Attitude = null, AttitudeMissing = "a", Speed = null, SpeedMissing = "s", Ils = null, IlsMissing = "i",
            RadioAltitude = null, RadioAltitudeMissing = "r", FlightDirector = null, FlightDirectorMissing = "f",
            Tcas = null, TcasMissing = "t", ReferenceSpeeds = null, ReferenceSpeedsMissing = "v",
        };
        Assert.Equal(new[]
        {
            "Speed tape: not read: s",
            "Attitude: not read: a",
            "Flight director: not read: f",
            "TCAS: not read: t",
            "Localizer and glideslope: not read: i",
            "Radio altitude and decision height: not read: r",
        }, Rows(Bbs757AdiReader.Decode(none, new Bbs757AdiRaw(true, null, null, null, null, null, null, null), In(), false)));
        // The reference speeds are never a row, read or not.
        Assert.DoesNotContain(Rows(Reading()), row => row.Contains("VREF") || row.Contains("121") || row.Contains("123"));
    }

    [Fact]
    public void A_dark_or_corrupt_reading_has_no_rows_and_says_nothing()
    {
        var dark = Reading(r => r with { Lit = false });
        var corrupt = Reading(r => Speed(r, s => s with { Airspeed = double.NaN }));
        Assert.True(corrupt.Corrupt);
        Assert.Empty(Rows(dark));
        Assert.Empty(Rows(corrupt));

        // A flag is compared between two lit readings only: a display going dark took no flag away, and one that lights
        // with a flag showing is the monitor's silent baseline.
        var flagged = Reading(r => Attitude(r, a => a with { IrsState = 0 }));
        Assert.Empty(Changes(flagged, dark));
        Assert.Empty(Changes(dark, flagged));
        Assert.Empty(Changes(dark, dark));
    }

    [Fact]
    public void Each_flag_is_spoken_as_it_appears_and_as_it_goes()
    {
        var clean = Reading();
        foreach (var (word, flagged) in new[]
                 {
                     ("ATT", Reading(r => Attitude(r, a => a with { IrsState = 0 }))),
                     ("SPD", Reading(r => Speed(r, s => s with { AdcFailed = true }))),
                     ("LOC", Reading(r => Ils(r, i => i with { LocFlag = true }))),
                     ("G/S", Reading(r => Ils(r, i => i with { GsFlag = true }))),
                     ("RA", Reading(r => Ra(r, x => x with { RaFailed = true }))),
                 })
        {
            Assert.Equal(new[] { "ADI flag: " + word }, Changes(clean, flagged));
            Assert.Equal(new[] { "ADI flag gone: " + word }, Changes(flagged, clean));
            Assert.Empty(Changes(flagged, flagged));
            // After "ADI announcements resumed" the display is already named.
            Assert.Equal(new[] { "flag: " + word }, Changes(clean, flagged, withPrefix: false));
            Assert.Equal(new[] { "flag gone: " + word }, Changes(flagged, clean, withPrefix: false));
            Assert.Equal(new[] { word }, Bbs757AdiText.Flags(flagged));
        }
        Assert.Empty(Bbs757AdiText.Flags(clean));
    }

    [Fact]
    public void Flags_of_one_settle_share_a_line_in_screen_order()
    {
        var two = Reading(r => Ra(Speed(r, s => s with { AdcFailed = true }), x => x with { RaFailed = true }));
        var others = Reading(r => Ils(Attitude(r, a => a with { IrsState = 0 }), i => i with { GsFlag = true, LocFlag = true }));
        Assert.Equal(new[] { "ADI flag: SPD, RA" }, Changes(Reading(), two));
        Assert.Equal(new[] { "ADI flag: ATT, G/S, LOC", "ADI flag gone: SPD, RA" }, Changes(two, others));
        Assert.Equal(new[] { "SPD", "ATT", "G/S", "RA", "LOC" },
            Bbs757AdiText.Flags(Reading(r => Ra(Ils(Speed(Attitude(r, a => a with { IrsState = 0 }), s => s with { AdcFailed = true }),
                i => i with { GsFlag = true, LocFlag = true }), x => x with { RaFailed = true }))));
    }

    [Fact]
    public void Only_the_five_flags_are_spoken()
    {
        // V1 INOP, ALT, the blank center while the IRS aligns, a scale losing its pointer: listed, never spoken.
        var clean = Reading();
        Assert.Empty(Changes(clean, Reading(r => Speed(r, s => s with { V1 = 0, V1Inop = true }))));
        Assert.Empty(Changes(clean, Reading(r => Attitude(r, a => a with { IrsState = 3, IrsAttitude = false }))));
        Assert.Empty(Changes(clean, Reading(r => Ils(r, i => i with { GsValid = false, LocValid = false }))));
        Assert.Empty(Changes(clean, Reading(r => Ra(r, x => x with { VerticalSpeed = 0 }))));
        // Aligning to the flag (the alignment lost) is the flag appearing.
        Assert.Equal(new[] { "ADI flag: ATT" }, Changes(Reading(r => Attitude(r, a => a with { IrsState = 3, IrsAttitude = false })),
            Reading(r => Attitude(r, a => a with { IrsState = 0, IrsAttitude = false }))));
    }
}

using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.BBS757;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Output N on the BlueBird 757 (plan 04, task 1): the VOR tunings come from the NAV RADIO page of the
/// first officer's CDU (<see cref="Bbs757NavRad"/>), the courses from BlueBird's own L:vars, and the
/// stock NAV radios, which BlueBird does not use, are never read. The screens are session 8's: row 2 of
/// the page holds the tunings ("116.90AGSU" auto, "113.90R" tuned from the CDU, "------" not tuned).
/// </summary>
public class Bbs757NavRadTests
{
    private const int Fo = Bbs757CduPages.FirstOfficer;

    // ---- Screens ------------------------------------------------------------------------------

    /// <summary>A left text and a right text on one 24-column row, the right one flush to the edge.</summary>
    private static string Row(string left, string right = "")
        => left.PadRight(Bbs757CduFrame.Columns - right.Length) + right;

    private static Bbs757CduScreen Screen(string title, string tunings)
    {
        var lines = Enumerable.Repeat(new string(' ', Bbs757CduFrame.Columns), Bbs757CduFrame.Rows).ToArray();
        lines[0] = title.PadRight(Bbs757CduFrame.Columns);
        lines[1] = Row(" VOR L", "VOR R ");
        lines[2] = tunings;
        lines[3] = Row(" CRS", "CRS ");
        lines[4] = Row(" 095", "000 ");
        return new Bbs757CduScreen(lines);
    }

    /// <summary>The NAV RADIO page with the given VOR L and VOR R texts on its tuning row.</summary>
    private static Bbs757CduScreen NavRadio(string left, string right = "------")
        => Screen("        NAV RADIO       ", Row(left, right));

    private static Bbs757CduScreen Legs() => Screen("  ACT RTE 1 LEGS     1/7", Row("", ""));

    private static readonly Bbs757VorTuning NotTuned = new(null, null, null);

    /// <summary>The ILS control panel as the app counts it, on a channel.</summary>
    private static Bbs757IlsDial IlsAt(double mhz)
    {
        Assert.True(Bbs757IlsPanel.TryDialFor(mhz, out var dial));
        return dial;
    }

    private static readonly Bbs757IlsDial IlsParked = Bbs757IlsDial.LoadState;

    /// <summary>
    /// A flight load (MainForm's AircraftLoaded), then the first COM row delivered: the flight runs, and the
    /// ILS panel is counted from PARK, .10.
    /// </summary>
    private static void LoadFlight(BBS757Definition def)
    {
        def.OnSimContextReset();
        def.ProcessSimVarUpdate(Bbs757Radios.ActiveKey(1), 121.9, new SpeechCapture());
    }

    private static void InCulture(string name, Action body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static async Task InCultureAsync(string name, Func<Task> body)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            await body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- The page -------------------------------------------------------------------------------

    [Fact]
    public void Parses_auto_and_remote()
    {
        // 116.90AGSU: auto (the FMC tuned it), ident GSU. 113.90R: tuned from the CDU, no ident yet.
        // VOR R reads ------ when it is not tuned.
        var auto = Bbs757NavRad.Parse(NavRadio("116.90AGSU"));
        Assert.Equal(new Bbs757VorTuning(116.90, 'A', "GSU"), auto!.Left);
        Assert.Equal(NotTuned, auto.Right);

        var remote = Bbs757NavRad.Parse(NavRadio("113.90R"));
        Assert.Equal(new Bbs757VorTuning(113.90, 'R', null), remote!.Left);
        Assert.Equal(NotTuned, remote.Right);
    }

    [Theory]
    [InlineData("114.20MABC", 114.20, 'M', "ABC")]   // any other letter is a manual tuning
    [InlineData("108.00", 108.00, null, null)]        // no letter, no ident
    public void Parses_other_modes(string text, double mhz, char? mode, string? ident)
    {
        var page = Bbs757NavRad.Parse(NavRadio(text));

        Assert.Equal(new Bbs757VorTuning(mhz, mode, ident), page!.Left);
    }

    [Fact]
    public void Parses_the_exact_rows_session_8_read()
    {
        // 24 columns each, as the page service hands them over.
        Assert.Equal(new Bbs757VorTuning(116.90, 'A', "GSU"),
            Bbs757NavRad.Parse(Screen("        NAV RADIO       ", "116.90AGSU        ------"))!.Left);
        Assert.Equal(new Bbs757VorTuning(113.90, 'R', null),
            Bbs757NavRad.Parse(Screen("        NAV RADIO       ", "113.90R           ------"))!.Left);
    }

    [Theory]
    // The right side is mirrored, [ident][mode]freq, flush right (measured 2026-10-26: a tuned VOR R
    // read "GSUR116.90"); the left side is freq[mode][ident].
    [InlineData("GSUR116.90", 116.90, 'R', "GSU")]    // tuned from the CDU, station received
    [InlineData("GSUA116.90", 116.90, 'A', "GSU")]    // auto
    [InlineData("R113.90", 113.90, 'R', null)]        // tuned from the CDU, no ident yet
    [InlineData("M117.40", 117.40, 'M', null)]        // manual on the VOR/DME panel
    [InlineData("114.20", 114.20, null, null)]        // no letter, no ident
    public void Parses_vor_r_mirrored(string text, double mhz, char? mode, string? ident)
    {
        var page = Bbs757NavRad.Parse(NavRadio("------", text));

        Assert.Equal(new Bbs757VorTuning(mhz, mode, ident), page!.Right);
        Assert.Equal(NotTuned, page.Left);
    }

    /// <summary>The whole NAV RADIO screen as CDU 2 showed it (measured 2026-10-26, rows 0-13).</summary>
    private static Bbs757CduScreen MeasuredNavRadio(string tunings, string courses = "")
    {
        var lines = new[]
        {
            "        NAV RADIO       ",
            "VOR L              VOR R",
            tunings,
            "CRS      RADIAL      CRS",
            courses.PadRight(Bbs757CduFrame.Columns),
            new string(' ', Bbs757CduFrame.Columns),
            new string(' ', Bbs757CduFrame.Columns),
            new string(' ', Bbs757CduFrame.Columns),
            new string(' ', Bbs757CduFrame.Columns),
            new string(' ', Bbs757CduFrame.Columns),
            new string(' ', Bbs757CduFrame.Columns),
            "        PRESELECT       ",
            "------            ------",
            new string(' ', Bbs757CduFrame.Columns),
        };
        return new Bbs757CduScreen(lines);
    }

    [Fact]
    public void Parses_the_measured_nav_radio_screens()
    {
        // As found: both VORs auto-tuned or not tuned.
        var auto = Bbs757NavRad.Parse(MeasuredNavRadio("116.90AGSU        ------"))!;
        Assert.Equal(new Bbs757VorTuning(116.90, 'A', "GSU"), auto.Left);
        Assert.Equal(NotTuned, auto.Right);

        // Both tuned from the CDU (11:35:10 and 11:36:08): VOR L freq-first, VOR R ident-first.
        var both = Bbs757NavRad.Parse(MeasuredNavRadio("116.90RGSU        GSUR116.90", "095                  095"))!;
        Assert.Equal(new Bbs757VorTuning(116.90, 'R', "GSU"), both.Left);
        Assert.Equal(new Bbs757VorTuning(116.90, 'R', "GSU"), both.Right);

        // VOR L manual on the VOR/DME panel (117.40M, no ident), VOR R still not tuned.
        var manual = Bbs757NavRad.Parse(MeasuredNavRadio("117.40M           ------"))!;
        Assert.Equal(new Bbs757VorTuning(117.40, 'M', null), manual.Left);
        Assert.Equal(NotTuned, manual.Right);
    }

    [Fact]
    public void Not_the_page_is_null()
    {
        Assert.True(Bbs757NavRad.IsNavRadio(NavRadio("116.90AGSU")));
        Assert.False(Bbs757NavRad.IsNavRadio(Legs()));
        Assert.Null(Bbs757NavRad.Parse(Legs()));
        Assert.Null(Bbs757NavRad.Parse(Bbs757CduFrame.Blank));
    }

    [Fact]
    public void A_side_it_cannot_read_is_unavailable_and_the_other_still_reads()
    {
        // "Frequency unavailable" is honest; "VOR L not tuned" for a text not read would not be. The
        // page is null only when the screen is not NAV RADIO.
        var garbageLeft = Bbs757NavRad.Parse(NavRadio("GARBAGE", "GSUR116.90"))!;
        Assert.Null(garbageLeft.Left);
        Assert.Equal(new Bbs757VorTuning(116.90, 'R', "GSU"), garbageLeft.Right);

        var garbageRight = Bbs757NavRad.Parse(NavRadio("116.90AGSU", "GARBAGE"))!;
        Assert.Equal(new Bbs757VorTuning(116.90, 'A', "GSU"), garbageRight.Left);
        Assert.Null(garbageRight.Right);

        // A row that has not been drawn: both sides unreadable, still a page.
        var undrawn = Bbs757NavRad.Parse(Screen("        NAV RADIO       ", new string(' ', Bbs757CduFrame.Columns)))!;
        Assert.Null(undrawn.Left);
        Assert.Null(undrawn.Right);
    }

    // ---- The sentences ------------------------------------------------------------------------

    [Theory]
    [InlineData(116.90, 'A', "GSU", "VOR L 116.90 auto, GSU")]
    [InlineData(113.90, 'R', null, "VOR L 113.90 remote")]
    [InlineData(114.20, 'M', "ABC", "VOR L 114.20 manual, ABC")]
    [InlineData(108.00, null, null, "VOR L 108.00")]
    public void Tuning_phrases(double mhz, char? mode, string? ident, string said)
    {
        Assert.Equal(said, Bbs757NavRad.TuningPhrase("VOR L", new Bbs757VorTuning(mhz, mode, ident)));
    }

    [Fact]
    public void An_untuned_side_says_so()
    {
        Assert.Equal("VOR R not tuned", Bbs757NavRad.TuningPhrase("VOR R", NotTuned));
    }

    [Fact]
    public void An_unreadable_side_says_its_frequency_is_unavailable()
    {
        Assert.Equal("VOR R frequency unavailable", Bbs757NavRad.TuningPhrase("VOR R", null));
    }

    [Fact]
    public void Readout_sentences()
    {
        var page = new Bbs757NavRadPage(new Bbs757VorTuning(116.90, 'A', "GSU"), NotTuned);

        // With the page: each VOR's tuning, then the ILS course, then the ILS panel as the app counts it,
        // said as a count (no display read here). An auto-tuned or untuned VOR has no course to say
        // (measured: the CDU blanks its CRS field and ignores an entry).
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS 110.50 by count.",
            Bbs757NavRad.Readout(page, 95, 0, 95, IlsAt(110.50)));
        // Without it (the first officer's CDU busy or unreachable): the courses, the counted ILS, and why
        // there is no more.
        Assert.Equal("VOR L course 095, VOR R course 000, ILS course 095. ILS 110.50 by count. VOR frequencies unavailable.",
            Bbs757NavRad.Readout(null, 95, 0, 95, IlsAt(110.50)));
        // An ILS course that cannot be read says so, in both forms.
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course unavailable. ILS parked by count.",
            Bbs757NavRad.Readout(page, null, 0, null, IlsParked));
        Assert.Equal("VOR L course 095, VOR R course unavailable, ILS course unavailable. ILS parked by count. VOR frequencies unavailable.",
            Bbs757NavRad.Readout(null, 95, null, null, IlsParked));
        // A panel the app has not counted says so, in both forms.
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS tuning unknown.",
            Bbs757NavRad.Readout(page, 95, 0, 95, null));
        Assert.Equal("VOR L course unavailable, VOR R course unavailable, ILS course unavailable. ILS tuning unknown. VOR frequencies unavailable.",
            Bbs757NavRad.Readout(null, null, null, null, null));
    }

    [Fact]
    public void A_vor_course_is_said_only_while_that_vor_is_tuned_by_hand()
    {
        var remote = new Bbs757VorTuning(116.90, 'R', "GSU");
        var manual = new Bbs757VorTuning(117.40, 'M', null);
        var auto = new Bbs757VorTuning(116.90, 'A', "GSU");

        // Hand-tuned from the CDU (R) or on the VOR/DME panel (M): the course applies, whichever side.
        Assert.Equal("VOR L 116.90 remote, GSU, course 095. VOR R 117.40 manual, course 270. ILS course 095. ILS parked by count.",
            Bbs757NavRad.Readout(new Bbs757NavRadPage(remote, manual), 95, 270, 95, IlsParked));
        // One hand-tuned, the other auto or untuned: only the hand-tuned one has a course.
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R 117.40 manual, course 270. ILS course 095. ILS parked by count.",
            Bbs757NavRad.Readout(new Bbs757NavRadPage(auto, manual), 95, 270, 95, IlsParked));
        Assert.Equal("VOR L 116.90 remote, GSU, course 095. VOR R not tuned. ILS course 095. ILS parked by count.",
            Bbs757NavRad.Readout(new Bbs757NavRadPage(remote, NotTuned), 95, 270, 95, IlsParked));
        // A hand-tuned VOR whose course cannot be read says so.
        Assert.Equal("VOR L 116.90 remote, GSU, course unavailable. VOR R not tuned. ILS course 095. ILS parked by count.",
            Bbs757NavRad.Readout(new Bbs757NavRadPage(remote, NotTuned), null, 270, 95, IlsParked));
        // A side the page could not be read for: nothing is known of its course either.
        Assert.Equal("VOR L frequency unavailable. VOR R 117.40 manual, course 270. ILS course 095. ILS parked by count.",
            Bbs757NavRad.Readout(new Bbs757NavRadPage(null, manual), 95, 270, 95, IlsParked));
    }

    [Fact]
    public void A_read_and_a_count_of_the_same_value_sound_different()
    {
        // The count is the app's guess from the clicks it sent; the read is what the panel's display shows. N
        // never says the count as if it were a reading: the count carries "by count", the read does not.
        var page = new Bbs757NavRadPage(new Bbs757VorTuning(116.90, 'A', "GSU"), NotTuned);
        var read11050 = new Bbs757IlsReading(false, 110.50, 95);
        var readParked = new Bbs757IlsReading(true, null, 95);

        string read = Bbs757NavRad.Readout(page, 95, 0, 95, IlsAt(110.50), read11050);
        string counted = Bbs757NavRad.Readout(page, 95, 0, 95, IlsAt(110.50));
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS 110.50.", read);
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS 110.50 by count.", counted);
        Assert.NotEqual(read, counted);

        // PARK, and the form without the page, the same way.
        Assert.NotEqual(Bbs757NavRad.Readout(page, 95, 0, 95, IlsParked, readParked), Bbs757NavRad.Readout(page, 95, 0, 95, IlsParked));
        Assert.EndsWith("ILS parked.", Bbs757NavRad.Readout(page, 95, 0, 95, IlsParked, readParked));
        Assert.Equal("VOR L course 095, VOR R course 000, ILS course 095. ILS 110.50. VOR frequencies unavailable.",
            Bbs757NavRad.Readout(null, 95, 0, 95, IlsAt(110.50), read11050));
        Assert.Equal("VOR L course 095, VOR R course 000, ILS course 095. ILS 110.50 by count. VOR frequencies unavailable.",
            Bbs757NavRad.Readout(null, 95, 0, 95, IlsAt(110.50)));

        // No count and no read: neither, as before.
        Assert.EndsWith("ILS tuning unknown.", Bbs757NavRad.Readout(page, 95, 0, 95, null));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_page_and_the_sentences_do_not_depend_on_the_pcs_culture(string culture)
    {
        // CI runs under en-US: under a comma-decimal culture "116.90" must still be 116.9 (not 11690)
        // and be spoken with a dot, and so must the counted ILS.
        InCulture(culture, () =>
        {
            var page = Bbs757NavRad.Parse(NavRadio("116.90AGSU", "GSUR117.40"));

            Assert.Equal(116.90, page!.Left!.Mhz);
            Assert.Equal(117.40, page.Right!.Mhz);
            Assert.Equal("VOR L 116.90 auto, GSU. VOR R 117.40 remote, GSU, course 270. ILS course 095. ILS 110.55 by count.",
                Bbs757NavRad.Readout(page, 95, 270, 95, IlsAt(110.55)));
            Assert.Equal("VOR L course 095, VOR R course 270, ILS course 095. ILS 108.10 by count. VOR frequencies unavailable.",
                Bbs757NavRad.Readout(null, 95, 270, 95, IlsAt(108.10)));
            Assert.Equal("VOR L course 095, VOR R course 270, ILS course 095. ILS 109.35. VOR frequencies unavailable.",
                Bbs757NavRad.Readout(null, 95, 270, 95, IlsAt(108.10), new Bbs757IlsReading(false, 109.35, 95)));
        });
    }

    [Theory]
    [InlineData(95.0, 95)]
    [InlineData(0.0, 0)]
    [InlineData(359.4, 359)]
    [InlineData(94.6, 95)]
    public void A_course_is_whole_degrees(double raw, int degrees)
    {
        Assert.Equal(degrees, Bbs757NavRad.Course(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    [InlineData(361.0)]
    public void A_course_that_is_not_one_is_unavailable(double? raw)
    {
        Assert.Null(Bbs757NavRad.Course(raw));
    }

    [Fact]
    public void Courses_are_three_digits()
    {
        // ...and an ILS frequency two decimals ("108.10", never "108.1").
        Assert.Equal("VOR L course 005, VOR R course 000, ILS course 360. ILS 108.10 by count. VOR frequencies unavailable.",
            Bbs757NavRad.Readout(null, 5, 0, 360, IlsAt(108.10)));
    }

    // ---- The ILS course typed in ----------------------------------------------------------------

    [Theory]
    [InlineData("095", 95)]
    [InlineData("95", 95)]
    [InlineData("0", 0)]
    [InlineData("000", 0)]
    [InlineData("270", 270)]
    [InlineData("359", 359)]
    [InlineData("360", 0)]       // 360 is north: 0
    [InlineData(" 180 ", 180)]   // surrounding spaces are not part of it
    [InlineData("95.0", 95)]     // a whole number typed with a decimal mark
    public void Course_parsing(string typed, int degrees)
    {
        Assert.True(Bbs757NavRad.TryParseCourse(typed, out int parsed, out string error));
        Assert.Equal(degrees, parsed);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(null, "Type a course first.")]
    [InlineData("", "Type a course first.")]
    [InlineData("   ", "Type a course first.")]
    [InlineData("361", "Course must be 0 to 360.")]
    [InlineData("1000", "Course must be 0 to 360.")]
    [InlineData("-5", "Course must be 0 to 360.")]
    [InlineData("95.5", "Course must be 0 to 360.")]   // not a whole number
    [InlineData("1,000", "Course must be 0 to 360.")]  // a thousands group is refused, never guessed
    [InlineData("abc", "Course must be 0 to 360.")]
    [InlineData("NaN", "Course must be 0 to 360.")]
    public void Course_parsing_refuses_what_is_not_a_course(string? typed, string said)
    {
        Assert.False(Bbs757NavRad.TryParseCourse(typed, out int degrees, out string error));
        Assert.Equal(said, error);
        Assert.Equal(0, degrees);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_typed_course_is_read_in_any_culture(string culture)
    {
        InCulture(culture, () =>
        {
            Assert.True(Bbs757NavRad.TryParseCourse("095", out int plain, out _));
            Assert.Equal(95, plain);
            Assert.True(Bbs757NavRad.TryParseCourse("95,0", out int comma, out _));   // a comma-decimal PC's own mark
            Assert.Equal(95, comma);
            Assert.True(Bbs757NavRad.TryParseCourse("95.0", out int dot, out _));
            Assert.Equal(95, dot);
        });
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Course_code_is_invariant(string culture)
    {
        // A whole number of degrees, written with no culture in it (VAR-5).
        InCulture(culture, () =>
        {
            Assert.Equal("95 (>L:BBS_ILS_COURSE)", Bbs757NavRad.CourseCode("BBS_ILS_COURSE", 95));
            Assert.Equal("0 (>L:BBS_ILS_COURSE)", Bbs757NavRad.CourseCode("BBS_ILS_COURSE", 0));
            Assert.Equal("270 (>L:BBS_ILS_COURSE)", Bbs757NavRad.CourseCode("BBS_ILS_COURSE", 270));
        });
    }

    // ---- The FMC's runway ILS (APPROACH REF) --------------------------------------------------------

    /// <summary>A CDU screen from 14 rows, each exactly 24 columns (the page service's shape).</summary>
    private static Bbs757CduScreen Cdu(params string[] rows)
    {
        Assert.Equal(Bbs757CduFrame.Rows, rows.Length);
        Assert.All(rows, r => Assert.Equal(Bbs757CduFrame.Columns, r.Length));
        return new Bbs757CduScreen(rows);
    }

    private static readonly string Blank24 = new(' ', Bbs757CduFrame.Columns);

    /// <summary>
    /// APPROACH REF as CDU 2 showed it with KATL-KMIA programmed and an arrival selected, on the ground
    /// (measured 2026-10-26, section E2). The ILS is two rows: a label row and the row under it. The
    /// CDU's entry box reads as '_' once scraped.
    /// </summary>
    private static Bbs757CduScreen MeasuredApproachRef(string? runwayRow = null, string? ilsLabel = null, string? ilsValue = null)
        => Cdu(
            "      APPROACH REF      ",
            " GROSS WT               ",
            "___._                   ",
            Blank24, Blank24, Blank24, Blank24,
            runwayRow ?? " KATL09L      FLAP/SPEED",
            "12390FT3776M      --/---",
            ilsLabel ?? " ILS 09L       FRONT CRS",
            ilsValue ?? "110.50 IHZK         095°",
            "------------------------",
            "<INDEX                  ",
            Blank24);

    /// <summary>APPROACH REF with no arrival runway (measured, no route): no ILS rows.</summary>
    private static Bbs757CduScreen ApproachRefWithoutIls()
        => Cdu(
            "      APPROACH REF      ",
            " GROSS WT               ",
            "???.?                   ",
            Blank24, Blank24, Blank24, Blank24,
            "              FLAP/SPEED",
            "                  --/---",
            Blank24, Blank24,
            "------------------------",
            "<INDEX                  ",
            Blank24);

    [Fact]
    public void Is_approach_ref_by_its_title()
    {
        Assert.True(Bbs757NavRad.IsApproachRef(MeasuredApproachRef()));
        Assert.True(Bbs757NavRad.IsApproachRef(ApproachRefWithoutIls()));
        Assert.False(Bbs757NavRad.IsApproachRef(NavRadio("116.90AGSU")));
        Assert.False(Bbs757NavRad.IsApproachRef(Legs()));
        Assert.False(Bbs757NavRad.IsApproachRef(Bbs757CduFrame.Blank));
    }

    [Fact]
    public void Approach_ref_ils_parsed()
    {
        var ils = Bbs757NavRad.ParseApproachRefIls(MeasuredApproachRef());

        Assert.Equal(new Bbs757RunwayIls("09L", 110.50, "IHZK", 95, "KATL"), ils);
    }

    [Fact]
    public void A_page_with_no_ils_rows_gives_no_ils()
    {
        Assert.Null(Bbs757NavRad.ParseApproachRefIls(ApproachRefWithoutIls()));
        Assert.Null(Bbs757NavRad.ParseApproachRefIls(NavRadio("116.90AGSU")));
        Assert.Null(Bbs757NavRad.ParseApproachRefIls(Bbs757CduFrame.Blank));
        // The label without a frequency under it is no ILS to set either.
        Assert.Null(Bbs757NavRad.ParseApproachRefIls(MeasuredApproachRef(ilsValue: "------                  ")));
        Assert.Null(Bbs757NavRad.ParseApproachRefIls(MeasuredApproachRef(ilsValue: "                        ")));
    }

    [Fact]
    public void An_ils_without_a_front_course_is_still_read()
    {
        var ils = Bbs757NavRad.ParseApproachRefIls(MeasuredApproachRef(ilsValue: "110.50 IHZK".PadRight(Bbs757CduFrame.Columns)));

        Assert.Equal(new Bbs757RunwayIls("09L", 110.50, "IHZK", null, "KATL"), ils);
    }

    [Fact]
    public void The_airport_is_named_only_when_its_runway_is_the_ils_runway()
    {
        // Row 7 names the airport and the runway the page is about; another runway there is not this ILS's.
        Assert.Null(Bbs757NavRad.ParseApproachRefIls(MeasuredApproachRef(runwayRow: " KMIA09R      FLAP/SPEED"))!.Airport);
        Assert.Null(Bbs757NavRad.ParseApproachRefIls(MeasuredApproachRef(runwayRow: "              FLAP/SPEED"))!.Airport);
        // Another runway with a letter, and a runway without one.
        var other = Bbs757NavRad.ParseApproachRefIls(MeasuredApproachRef(
            runwayRow: " KMIA08R      FLAP/SPEED", ilsLabel: " ILS 08R       FRONT CRS", ilsValue: "109.90 IATL         090°"));
        Assert.Equal(new Bbs757RunwayIls("08R", 109.90, "IATL", 90, "KMIA"), other);
        var plain = Bbs757NavRad.ParseApproachRefIls(MeasuredApproachRef(
            runwayRow: " EDDF25       FLAP/SPEED", ilsLabel: " ILS 25        FRONT CRS", ilsValue: "109.90 IFFC         249°"));
        Assert.Equal(new Bbs757RunwayIls("25", 109.90, "IFFC", 249, "EDDF"), plain);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_ils_is_read_and_said_whatever_the_pcs_culture(string culture)
    {
        InCulture(culture, () =>
        {
            var ils = Bbs757NavRad.ParseApproachRefIls(MeasuredApproachRef());

            Assert.Equal(110.50, ils!.Mhz);   // not 11050
            Assert.Equal("FMC runway ILS KATL 09L, 110.50, IHZK, front course 095", Bbs757NavRad.RunwayIlsPhrase(ils));
        });
    }

    [Fact]
    public void Runway_ils_phrases()
    {
        Assert.Equal("FMC runway ILS KATL 09L, 110.50, IHZK, front course 095",
            Bbs757NavRad.RunwayIlsPhrase(new Bbs757RunwayIls("09L", 110.50, "IHZK", 95, "KATL")));
        // No airport read: the runway alone.
        Assert.Equal("FMC runway ILS 09L, 110.50, IHZK, front course 095",
            Bbs757NavRad.RunwayIlsPhrase(new Bbs757RunwayIls("09L", 110.50, "IHZK", 95)));
        // No front course on the page: it is left out, never invented.
        Assert.Equal("FMC runway ILS KATL 09L, 110.50, IHZK",
            Bbs757NavRad.RunwayIlsPhrase(new Bbs757RunwayIls("09L", 110.50, "IHZK", null, "KATL")));
        // The frequency has two decimals ("108.10", not "108.1") and the course three digits.
        Assert.Equal("FMC runway ILS 26L, 108.10, IBRU, front course 005",
            Bbs757NavRad.RunwayIlsPhrase(new Bbs757RunwayIls("26L", 108.1, "IBRU", 5)));
        Assert.Equal("FMC runway ILS unavailable", Bbs757NavRad.RunwayIlsPhrase(null));
    }

    // ---- The variables ------------------------------------------------------------------------

    [Fact]
    public void The_course_variables_are_read_on_request_and_have_no_Ctrl_M_row()
    {
        var vars = new BBS757Definition().GetVariables();
        var expected = new[]
        {
            (Bbs757NavRad.VorLCourseKey, "BBS_VOR_L_COURSE", "VOR L Course"),
            (Bbs757NavRad.VorRCourseKey, "BBS_VOR_R_COURSE", "VOR R Course"),
            (Bbs757NavRad.IlsCourseKey, "BBS_ILS_COURSE", "ILS Course"),
        };

        foreach (var (key, name, label) in expected)
        {
            var v = vars[key];
            Assert.Equal(name, v.Name);
            Assert.Equal(label, v.DisplayName);
            Assert.Equal(SimVarType.LVar, v.Type);
            Assert.Equal(UpdateFrequency.OnRequest, v.UpdateFrequency);
            Assert.True(v.ExcludeFromMonitorManager);   // read on request, never announced: a Ctrl+M row would mute nothing
        }
    }

    [Fact]
    public void The_nav_variables_are_read_on_request_and_have_no_Ctrl_M_row()
    {
        var vars = new BBS757Definition().GetVariables();
        var expected = new[]
        {
            (Bbs757NavRad.IlsFrequencyKey, "BBS_757_ILS_FREQ", "ILS Frequency"),
            (Bbs757NavRad.VorLFrequencyKey, "BBS_757_VOR_L_FREQ", "VOR L Frequency"),
            (Bbs757NavRad.VorRFrequencyKey, "BBS_757_VOR_R_FREQ", "VOR R Frequency"),
            (Bbs757NavRad.VorLAutoKey, "B757_MCP_VOR1_MODE_AUTO", "VOR L Auto Tuning"),
            (Bbs757NavRad.VorRAutoKey, "B757_MCP_VOR2_MODE_AUTO", "VOR R Auto Tuning"),
        };

        Assert.Equal(expected, Bbs757NavRad.NavVariables);
        foreach (var (key, name, label) in expected)
        {
            var v = vars[key];
            Assert.Equal(name, v.Name);
            Assert.Equal(label, v.DisplayName);
            Assert.Equal(SimVarType.LVar, v.Type);
            Assert.Equal(UpdateFrequency.OnRequest, v.UpdateFrequency);
            Assert.True(v.ExcludeFromMonitorManager);
        }
    }

    [Fact]
    public async Task N_says_the_variable_in_any_culture_and_the_count_when_it_is_absent()
    {
        var cdu = new FakeCdu { Screen = NavRadio("116.90AGSU") };
        var display = new FakeIlsDisplay { Answer = () => Bbs757IlsPanel.ReadingFrom(110.50, 95) };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95), IlsDisplayFor = _ => display };
        var speech = new SpeechCapture();
        var simConnect = new SimConnectManager(IntPtr.Zero);

        async Task<string> N()
        {
            speech.Interrupts.Clear();
            await def.ReadNavRadiosAsync(simConnect, speech, Pages(cdu));
            return Assert.Single(speech.Interrupts);
        }

        await InCultureAsync("sv-SE", async () => Assert.EndsWith("ILS 110.50.", await N()));

        display.Answer = () => Bbs757IlsPanel.ReadingFrom(null, 95);
        def.OnSimContextReset();   // a fresh load: the count is PARK
        def.ProcessSimVarUpdate(Bbs757Radios.ActiveKey(1), 121.9, new SpeechCapture());
        Assert.EndsWith("ILS parked by count.", await N());
    }

    // ---- Output N -------------------------------------------------------------------------------

    private static McpIo Courses(double? vorL, double? vorR, double? ils,
        double? freqL = null, double? freqR = null, double? autoL = null, double? autoR = null, List<string>? reads = null) => new()
    {
        Read = (key, _) =>
        {
            reads?.Add(key);
            return Task.FromResult(key switch
            {
                Bbs757NavRad.VorLCourseKey => vorL,
                Bbs757NavRad.VorRCourseKey => vorR,
                Bbs757NavRad.IlsCourseKey => ils,
                Bbs757NavRad.VorLFrequencyKey => freqL,
                Bbs757NavRad.VorRFrequencyKey => freqR,
                Bbs757NavRad.VorLAutoKey => autoL,
                Bbs757NavRad.VorRAutoKey => autoR,
                _ => throw new InvalidOperationException($"N read {key}, which is not a BlueBird nav course"),
            });
        },
        Write = _ => throw new InvalidOperationException("N writes nothing"),
        Delay = (_, _) => Task.CompletedTask,
    };

    /// <summary>A definition whose courses read as given and whose tablet client is gone: the CDU service answers null.</summary>
    private static BBS757Definition DefWithNoCdu(McpIo io) => new()
    {
        NestRunning = () => true,
        ReadoutIo = _ => io,
        TabletClientFactory = () =>
        {
            var client = new CoherentBbs757TabletClient(() => "");
            client.Dispose();
            return client;
        },
    };

    private static async Task<bool> WaitUntil(Func<bool> done, int timeoutMs = 5000)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            if (clock.ElapsedMilliseconds > timeoutMs) return false;
            await Task.Delay(10);
        }
        return true;
    }

    [Fact]
    public async Task N_is_handled_by_the_757()
    {
        // Until now N fell through to MainForm's readout of the stock NAV radios, which BlueBird does
        // not use. With the CDU service answering null, the action is still the 757's, and it still
        // speaks the courses. (A new definition has not seen a flight load: its ILS count is unknown.)
        var def = DefWithNoCdu(Courses(95, 0, 95));
        var speech = new SpeechCapture();

        bool handled = def.HandleHotkeyAction(HotkeyAction.ReadNavRadioInfo, new SimConnectManager(IntPtr.Zero), speech, null!, null!);

        Assert.True(handled);
        Assert.True(await WaitUntil(() => speech.Interrupts.Count > 0), "N said nothing");
        Assert.Equal(new[] { "VOR L course 095, VOR R course 000, ILS course 095. ILS tuning unknown. VOR frequencies unavailable." },
            speech.Interrupts.ToArray());
    }

    [Fact]
    public async Task N_reads_the_page_the_first_officers_cdu_shows()
    {
        var cdu = new FakeCdu { Screen = NavRadio("116.90AGSU") };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS tuning unknown." }, speech.Interrupts);
        Assert.Empty(cdu.Presses);   // already on the page: no key
    }

    [Fact]
    public async Task N_brings_up_the_page_on_the_first_officers_cdu_only()
    {
        var cdu = new FakeCdu { Screen = Legs(), ScreenAfterNavRadKey = NavRadio("113.90R") };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L 113.90 remote, course 095. VOR R not tuned. ILS course 095. ILS tuning unknown." }, speech.Interrupts);
        Assert.Equal(new[] { (Fo, Bbs757NavRad.PageKey, 250) }, cdu.Presses);   // R52: the captain's CDU is never touched
    }

    [Fact]
    public async Task N_with_the_cdu_busy_still_reads_the_courses()
    {
        // The pilot is typing in the CDU window: keys are queued, so the page service presses nothing.
        var cdu = new FakeCdu { Screen = Legs(), ScreenAfterNavRadKey = NavRadio("116.90AGSU"), PendingKeyCount = 3 };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L course 095, VOR R course 000, ILS course 095. ILS tuning unknown. VOR frequencies unavailable." },
            speech.Interrupts);
        Assert.Empty(cdu.Presses);
    }

    [Fact]
    public async Task N_with_the_tablet_view_unreachable_still_reads_the_courses()
    {
        var cdu = new FakeCdu { Screen = null, IsReadable = false };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L course 095, VOR R course 000, ILS course 095. ILS tuning unknown. VOR frequencies unavailable." },
            speech.Interrupts);
    }

    [Fact]
    public void Without_the_page_the_vors_come_from_the_variables()
    {
        var l = Bbs757NavRad.TuningFromVariables(116.05, 1);
        var r = Bbs757NavRad.TuningFromVariables(113.90, 0);
        const string said = "VOR L 116.05 auto. VOR R 113.90 manual, course 095. ILS course 095. ILS 110.50.";
        var read = new Bbs757IlsReading(false, 110.50, 95);
        Assert.Equal(said, Bbs757NavRad.Readout(null, 0, 95, 95, IlsAt(110.50), read, l, r));
        InCulture("sv-SE", () => Assert.Equal(said, Bbs757NavRad.Readout(null, 0, 95, 95, IlsAt(110.50), read, l, r)));
    }

    [Fact]
    public void Variable_tunings_are_built_from_the_raw_values()
    {
        Assert.Null(Bbs757NavRad.TuningFromVariables(null, 1));
        Assert.Equal(new Bbs757VorTuning(116.05, 'A', null), Bbs757NavRad.TuningFromVariables(116.05, 1));
        Assert.Equal(new Bbs757VorTuning(116.05, 'M', null), Bbs757NavRad.TuningFromVariables(116.05, 0));
        Assert.Equal(new Bbs757VorTuning(116.05, null, null), Bbs757NavRad.TuningFromVariables(116.05, null));
    }

    [Theory]
    [InlineData(118.0)]
    [InlineData(107.9)]
    public void Variables_out_of_the_vor_band_are_not_tuned(double mhz)
    {
        var t = Bbs757NavRad.TuningFromVariables(mhz, 1);
        Assert.Equal(new Bbs757VorTuning(null, 'A', null), t);
        Assert.StartsWith("VOR L not tuned.", Bbs757NavRad.Readout(null, 95, 95, 95, IlsParked, null, t, t));
    }

    [Theory]
    [InlineData(107.999999, 108.00)]
    [InlineData(117.950001, 117.95)]
    public void Variables_at_the_band_edge_are_tuned_by_the_rounding(double mhz, double tuned)
    {
        Assert.Equal(new Bbs757VorTuning(tuned, 'A', null), Bbs757NavRad.TuningFromVariables(mhz, 1));
    }

    [Fact]
    public void A_vor_variable_reading_zero_or_nothing_is_unread()
    {
        Assert.Null(Bbs757NavRad.TuningFromVariables(0, 1));
        Assert.Null(Bbs757NavRad.TuningFromVariables(double.NaN, 1));
        var zero = Bbs757NavRad.TuningFromVariables(0, 0);
        Assert.StartsWith("VOR L course 095, VOR R course 000, ILS course 095.",
            Bbs757NavRad.Readout(null, 95, 0, 95, IlsParked, null, zero, zero));
        Assert.EndsWith("VOR frequencies unavailable.", Bbs757NavRad.Readout(null, 95, 0, 95, IlsParked, null, zero, zero));
    }

    [Fact]
    public void Without_the_page_or_the_variables_the_words_are_as_before()
    {
        Assert.Equal("VOR L course 095, VOR R course 000, ILS course 095. ILS 110.50. VOR frequencies unavailable.",
            Bbs757NavRad.Readout(null, 95, 0, 95, IlsAt(110.50), new Bbs757IlsReading(false, 110.50, 95), null, null));
    }

    [Fact]
    public void A_side_whose_variable_is_unread_says_its_frequency_is_unavailable()
    {
        var l = Bbs757NavRad.TuningFromVariables(116.05, 1);
        Assert.StartsWith("VOR L 116.05 auto. VOR R frequency unavailable.", Bbs757NavRad.Readout(null, 95, 0, 95, IlsParked, null, l, null));
    }

    [Fact]
    public void With_the_page_the_variables_are_not_used()
    {
        var page = Bbs757NavRad.Parse(NavRadio("116.90AGSU"))!;
        var v = Bbs757NavRad.TuningFromVariables(113.90, 0);
        Assert.StartsWith("VOR L 116.90 auto, GSU. VOR R not tuned.", Bbs757NavRad.Readout(page, 95, 0, 95, IlsParked, null, v, v));
    }

    [Fact]
    public async Task N_without_the_page_reads_the_four_variables_and_speaks_them()
    {
        var reads = new List<string>();
        var cdu = new FakeCdu { Screen = Legs(), ScreenAfterNavRadKey = NavRadio("116.90AGSU"), PendingKeyCount = 3 };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95, 116.05, 113.90, 1, 0, reads) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L 116.05 auto. VOR R 113.90 manual, course 000. ILS course 095. ILS tuning unknown." }, speech.Interrupts);
        Assert.Contains(Bbs757NavRad.VorLFrequencyKey, reads);
        Assert.Contains(Bbs757NavRad.VorRAutoKey, reads);
    }

    [Fact]
    public async Task N_with_the_page_does_not_read_the_variables()
    {
        var reads = new List<string>();
        var cdu = new FakeCdu { Screen = NavRadio("116.90AGSU") };
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95, 116.05, 113.90, 1, 0, reads) };

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), new SpeechCapture(), Pages(cdu));

        Assert.DoesNotContain(Bbs757NavRad.VorLFrequencyKey, reads);
        Assert.DoesNotContain(Bbs757NavRad.VorRAutoKey, reads);
    }

    [Fact]
    public async Task N_with_a_course_that_cannot_be_read_says_so()
    {
        var cdu = new FakeCdu { Screen = NavRadio("113.90R") };   // tuned by hand: its course applies
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(null, 0, double.NaN) };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(cdu));

        Assert.Equal(new[] { "VOR L 113.90 remote, course unavailable. VOR R not tuned. ILS course unavailable. ILS tuning unknown." },
            speech.Interrupts);
    }

    [Fact]
    public async Task N_that_fails_still_says_something()
    {
        var def = new BBS757Definition
        {
            NestRunning = () => true,
            ReadoutIo = _ => new McpIo
            {
                Read = (_, _) => throw new InvalidOperationException("no link"),
                Write = _ => { },
                Delay = (_, _) => Task.CompletedTask,
            },
        };
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(new FakeCdu()));

        Assert.Equal(new[] { "VOR L course unavailable, VOR R course unavailable, ILS course unavailable. ILS tuning unknown. VOR frequencies unavailable." },
            speech.Interrupts);
    }

    [Fact]
    public async Task N_that_fails_still_says_the_counted_ils()
    {
        // The count is the app's own: a readout whose sim reads fail still has it, said as a count.
        var def = new BBS757Definition
        {
            NestRunning = () => true,
            ReadoutIo = _ => new McpIo
            {
                Read = (_, _) => throw new InvalidOperationException("no link"),
                Write = _ => { },
                Delay = (_, _) => Task.CompletedTask,
            },
        };
        LoadFlight(def);   // a flight load: the panel loads parked
        var speech = new SpeechCapture();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(new FakeCdu()));

        Assert.Equal(new[] { "VOR L course unavailable, VOR R course unavailable, ILS course unavailable. ILS parked by count. VOR frequencies unavailable." },
            speech.Interrupts);
    }

    [Fact]
    public async Task N_says_the_counted_ils()
    {
        // No variable holds the ILS frequency: with the display unreadable, N says what the app counted, after
        // the courses, and says it is a count.
        var cdu = new FakeCdu { Screen = NavRadio("116.90AGSU") };
        var sim = new IlsSim();
        sim.Fresh[Bbs757NavRad.IlsCourseKey] = 95;   // each click follows a fresh read
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95), WriteIo = _ => sim.Io };
        var speech = new SpeechCapture();
        var simConnect = new SimConnectManager(IntPtr.Zero);

        async Task<string> N()
        {
            speech.Interrupts.Clear();
            await def.ReadNavRadiosAsync(simConnect, speech, Pages(cdu));
            return Assert.Single(speech.Interrupts);
        }

        // MSFSBA started, or the 757 picked, after the flight loaded: the panel was never counted.
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS tuning unknown.", await N());

        LoadFlight(def);   // a flight load: the panel loads parked
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS parked by count.", await N());

        await def.SetIlsAsync(110.50, simConnect, speech);
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS 110.50 by count.", await N());

        // The first officer's CDU busy on another page: the courses and the count, then why there is no more.
        cdu.Screen = Legs();
        cdu.PendingKeyCount = 3;
        Assert.Equal("VOR L course 095, VOR R course 000, ILS course 095. ILS 110.50 by count. VOR frequencies unavailable.", await N());
        cdu.Screen = NavRadio("116.90AGSU");
        cdu.PendingKeyCount = 0;

        // A comma-decimal PC still hears a dot.
        await InCultureAsync("de-DE", async () =>
            Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS 110.50 by count.", await N()));

        def.OnSimContextReset();   // a dropped connection: MainForm.OnConnectionLost calls both
        def.OnVariableCacheCleared();
        def.ProcessSimVarUpdate(Bbs757Radios.ActiveKey(1), 121.9, new SpeechCapture());   // the reconnect's re-fire
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS tuning unknown.", await N());
    }

    [Fact]
    public async Task N_says_the_read()
    {
        // N reads the ILS panel's display once, after the CDU, and says what it shows; the read is the count from then on.
        var cdu = new FakeCdu { Screen = NavRadio("116.90AGSU") };
        var display = new FakeIlsDisplay();
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95), IlsDisplayFor = _ => display };
        var speech = new SpeechCapture();
        var simConnect = new SimConnectManager(IntPtr.Zero);

        async Task<string> N()
        {
            speech.Interrupts.Clear();
            await def.ReadNavRadiosAsync(simConnect, speech, Pages(cdu));
            return Assert.Single(speech.Interrupts);
        }

        const string Vors = "VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095.";

        // MSFSBA started over a running flight: no count, but the display reads.
        display.Answer = () => new Bbs757IlsReading(false, 110.50, 95);
        Assert.Equal($"{Vors} ILS 110.50.", await N());
        Assert.Equal(1, display.Reads);   // one read
        Assert.Equal(IlsAt(110.50), def.IlsDial);

        // The panel turned by hand: the read wins over the count.
        display.Answer = () => new Bbs757IlsReading(false, 109.30, 95);
        Assert.Equal($"{Vors} ILS 109.30.", await N());
        Assert.Equal(IlsAt(109.30), def.IlsDial);

        // PARK: said from the read, and the count keeps the fraction it had (.30).
        display.Answer = () => new Bbs757IlsReading(true, null, 95);
        Assert.Equal($"{Vors} ILS parked.", await N());
        Assert.Equal(new Bbs757IlsDial(Bbs757IlsDial.Park, 2), def.IlsDial);

        // A comma-decimal PC still hears a dot.
        display.Answer = () => new Bbs757IlsReading(false, 109.35, 95);
        await InCultureAsync("de-DE", async () => Assert.Equal($"{Vors} ILS 109.35.", await N()));

        // A flight loading: no count. An unreadable display gives the count's words...
        def.OnSimContextReset();
        display.Answer = () => null;
        Assert.Equal($"{Vors} ILS tuning unknown.", await N());
        // ...and a read ends the pending load: the flight runs, its panel PARK with the .10 it loads with.
        display.Answer = () => new Bbs757IlsReading(true, null, 95);
        Assert.Equal($"{Vors} ILS parked.", await N());
        Assert.Equal(Bbs757IlsDial.LoadState, def.IlsDial);
        def.ProcessSimVarUpdate(Bbs757Radios.ActiveKey(1), 121.9, new SpeechCapture());   // the first COM row: no second load
        Assert.Equal(Bbs757IlsDial.LoadState, def.IlsDial);

        // The window's status says the read too.
        var rig = new WindowRig(new FakeCdu { Screen = NavRadio("116.90AGSU") }, display: display);
        display.Answer = () => new Bbs757IlsReading(false, 111.95, 95);
        var form = rig.Open();
        await rig.Def.NavRadiosFill!;
        Assert.Equal($"{Vors} ILS 111.95. FMC runway ILS unavailable.", form.StatusText);
    }

    [Fact]
    public async Task N_after_switching_away_says_nothing()
    {
        var def = new BBS757Definition { NestRunning = () => true, ReadoutIo = _ => Courses(95, 0, 95) };
        var speech = new SpeechCapture();
        def.OnSwitchedAway();

        await def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), speech, Pages(new FakeCdu { Screen = NavRadio("116.90AGSU") }));

        Assert.Empty(speech.Interrupts);
    }

    // ---- The Ctrl+N window ------------------------------------------------------------------------

    private static Bbs757CduScreen InitPage(string title, string? row12 = null)
    {
        var rows = Enumerable.Repeat(Blank24, Bbs757CduFrame.Rows).ToArray();
        rows[0] = title;
        if (row12 != null) rows[12] = row12;
        return Cdu(rows);
    }

    /// <summary>INIT on the ground gave POS INIT (measured): 6L is the way to the index.</summary>
    private static Bbs757CduScreen PosInit() => InitPage("      POS INIT       1/4", "<INDEX            ROUTE>");

    /// <summary>With a route programmed INIT gave PERF INIT (measured): its 6L is the index too.</summary>
    private static Bbs757CduScreen PerfInit() => InitPage("        PERF INIT       ", "<INDEX          TAKEOFF>");

    private static Bbs757CduScreen InitRefIndex() => InitPage("     INIT/REF INDEX     ", "                  MAINT>");

    private const string Reading = "Reading the nav radios…";

    /// <summary>N's sentence for the FO CDU on NAV RADIO with VOR L auto-tuned, from a definition that has seen no flight load.</summary>
    private const string TunedAuto = "VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS tuning unknown.";

    /// <summary>A simulated aircraft behind the ILS course write: what a fresh read answers, the writes and waits made.</summary>
    private sealed class IlsSim
    {
        public bool CanWrite = true;
        public readonly Dictionary<string, double?> Fresh = new(StringComparer.Ordinal);
        public readonly List<string> Writes = new();
        public readonly List<int> Delays = new();

        public Bbs757SimIo Io => new()
        {
            CanWrite = () => CanWrite,
            ReadFresh = (key, _) => Task.FromResult(Fresh.TryGetValue(key, out double? v) ? v : null),
            Cached = _ => null,
            Write = rpn => Writes.Add(rpn),
            RequestRead = _ => { },
            Delay = (ms, ct) =>
            {
                Delays.Add(ms);
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
        };
    }

    /// <summary>
    /// A definition wired to a fake first officer's CDU, a simulated aircraft, an ILS display (unreadable unless
    /// a test gives one that reads) and a window nothing shows.
    /// </summary>
    private sealed class WindowRig
    {
        public readonly SpeechCapture Speech = new();
        public readonly FakeCdu Cdu;
        public readonly IlsSim Sim = new();
        public readonly FakeIlsDisplay Display;
        public readonly BBS757Definition Def;
        public int Shown;

        /// <param name="gate">When set, the courses read waits for it (the window is then still opening).</param>
        /// <summary>Every key the window's reads asked for.</summary>
        public readonly List<string> Reads = new();

        public WindowRig(FakeCdu cdu, Task? gate = null, double? vorL = 95, double? vorR = 0, double? ils = 95, FakeIlsDisplay? display = null,
            double? freqL = null, double? freqR = null, double? autoL = null, double? autoR = null)
        {
            Cdu = cdu;
            Display = display ?? new FakeIlsDisplay();
            Def = new BBS757Definition
            {
                NestRunning = () => true,
                WriteIo = _ => Sim.Io,
                IlsDisplayFor = _ => Display,
                ShowRadioForm = (_, _) => Shown++,
                ReadoutIo = _ => new McpIo
                {
                    Read = async (key, _) =>
                    {
                        if (gate != null) await gate;
                        Reads.Add(key);
                        return key switch
                        {
                            Bbs757NavRad.VorLCourseKey => vorL,
                            Bbs757NavRad.VorRCourseKey => vorR,
                            Bbs757NavRad.IlsCourseKey => ils,
                            // The VOR variables N reads when the page is unavailable.
                            Bbs757NavRad.VorLFrequencyKey => freqL,
                            Bbs757NavRad.VorRFrequencyKey => freqR,
                            Bbs757NavRad.VorLAutoKey => autoL,
                            Bbs757NavRad.VorRAutoKey => autoR,
                            _ => throw new InvalidOperationException($"the window read {key}"),
                        };
                    },
                    Write = _ => throw new InvalidOperationException("the readout writes nothing"),
                    Delay = (_, _) => Task.CompletedTask,
                },
            };
        }

        public Forms.BBS757.BBS757NavRadiosForm Open()
            => Def.ShowNavRadiosWindow(new SimConnectManager(IntPtr.Zero), Speech, null, Pages(Cdu));
    }

    /// <summary>The FO CDU on NAV RADIO, INIT leading to the approach page the measured way (or the page given).</summary>
    private static FakeCdu CduToApproachRef(Bbs757CduScreen initGives, Bbs757CduScreen? indexL5Gives = null)
    {
        var cdu = new FakeCdu { Screen = NavRadio("116.90AGSU"), ScreenAfterNavRadKey = NavRadio("116.90AGSU") };
        cdu.Transitions[("NAV RADIO", Bbs757CduKeys.Init)] = initGives;
        cdu.Transitions[("POS INIT", "L6")] = InitRefIndex();
        cdu.Transitions[("PERF INIT", "L6")] = InitRefIndex();
        cdu.Transitions[("INIT/REF INDEX", "L5")] = indexL5Gives ?? MeasuredApproachRef();
        return cdu;
    }

    [Fact]
    public async Task Window_opens_without_approach_ref()
    {
        // The FO CDU is on NAV RADIO and INIT changes nothing: no APPROACH REF can be had. The window
        // opens anyway, and its status says so.
        var rig = new WindowRig(new FakeCdu { Screen = NavRadio("116.90AGSU") });

        var form = rig.Open();
        await rig.Def.NavRadiosFill!;

        const string said = TunedAuto + " FMC runway ILS unavailable.";
        Assert.Equal(said, form.StatusText);
        Assert.Equal(new[] { said }, rig.Speech.Interrupts);   // spoken once: the screen reader cannot know it changed
        Assert.Equal(1, rig.Shown);
        Assert.Equal(new[] { (Fo, Bbs757CduKeys.Init, 250) }, rig.Cdu.Presses);   // INIT tried, nothing else pressed
    }

    [Fact]
    public async Task The_window_opens_on_a_reading_message_and_speaks_nothing_until_it_is_filled()
    {
        var gate = new TaskCompletionSource();
        var rig = new WindowRig(new FakeCdu { Screen = NavRadio("116.90AGSU") }, gate.Task);

        var form = rig.Open();

        Assert.Equal(Reading, form.StatusText);
        Assert.Empty(rig.Speech.All);

        gate.SetResult();
        await rig.Def.NavRadiosFill!;

        Assert.StartsWith(TunedAuto, form.StatusText);
        Assert.Single(rig.Speech.All);
    }

    [Theory]
    [InlineData(false)]   // INIT gave POS INIT (no route)
    [InlineData(true)]    // INIT gave PERF INIT (route programmed)
    public async Task The_fmc_runway_ils_is_read_through_init_the_index_and_approach(bool perfInit)
    {
        var rig = new WindowRig(CduToApproachRef(perfInit ? PerfInit() : PosInit()));

        var form = rig.Open();
        await rig.Def.NavRadiosFill!;

        const string said = TunedAuto + " FMC runway ILS KATL 09L, 110.50, IHZK, front course 095.";
        Assert.Equal(said, form.StatusText);
        Assert.Equal(new[] { said }, rig.Speech.Interrupts);
        // The keys measured: INIT, 6L (INDEX), 5L (APPROACH), then the FO CDU is put back on NAV RADIO.
        Assert.Equal(new[]
        {
            (Fo, Bbs757CduKeys.Init, 250), (Fo, "L6", 250), (Fo, "L5", 250), (Fo, Bbs757CduKeys.NavRad, 250),
        }, rig.Cdu.Presses);
        Assert.True(Bbs757NavRad.IsNavRadio(rig.Cdu.Screen!));
    }

    [Fact]
    public async Task In_flight_init_may_open_approach_ref_at_once()
    {
        // Boeing's INIT REF is phase dependent: if INIT shows APPROACH REF there are no index keys to press.
        var rig = new WindowRig(CduToApproachRef(MeasuredApproachRef()));

        var form = rig.Open();
        await rig.Def.NavRadiosFill!;

        Assert.EndsWith("FMC runway ILS KATL 09L, 110.50, IHZK, front course 095.", form.StatusText);
        Assert.Equal(new[] { (Fo, Bbs757CduKeys.Init, 250), (Fo, Bbs757CduKeys.NavRad, 250) }, rig.Cdu.Presses);
    }

    [Fact]
    public async Task When_init_shows_the_index_itself_6L_is_not_pressed()
    {
        var rig = new WindowRig(CduToApproachRef(InitRefIndex()));

        var form = rig.Open();
        await rig.Def.NavRadiosFill!;

        Assert.EndsWith("FMC runway ILS KATL 09L, 110.50, IHZK, front course 095.", form.StatusText);
        Assert.Equal(new[] { (Fo, Bbs757CduKeys.Init, 250), (Fo, "L5", 250), (Fo, Bbs757CduKeys.NavRad, 250) }, rig.Cdu.Presses);
    }

    [Theory]
    [InlineData("wrong page after 6L")]
    [InlineData("6L did nothing")]
    [InlineData("5L did nothing")]
    public async Task A_page_that_is_not_the_expected_one_on_the_way_gives_no_ils_and_the_cdu_is_put_back(string fault)
    {
        var cdu = CduToApproachRef(PosInit());
        switch (fault)
        {
            case "wrong page after 6L": cdu.Transitions[("POS INIT", "L6")] = Legs(); break;
            case "6L did nothing": cdu.Transitions.Remove(("POS INIT", "L6")); break;
            default: cdu.Transitions.Remove(("INIT/REF INDEX", "L5")); break;
        }
        var rig = new WindowRig(cdu);

        var form = rig.Open();
        await rig.Def.NavRadiosFill!;

        Assert.Equal(TunedAuto + " FMC runway ILS unavailable.", form.StatusText);
        Assert.True(Bbs757NavRad.IsNavRadio(cdu.Screen!));   // success or not, the FO CDU ends on NAV RADIO
        Assert.Equal((Fo, Bbs757CduKeys.NavRad, 250), cdu.Presses[^1]);
    }

    [Fact]
    public async Task An_approach_page_with_no_ils_says_so_and_the_cdu_is_put_back()
    {
        var rig = new WindowRig(CduToApproachRef(ApproachRefWithoutIls()));

        var form = rig.Open();
        await rig.Def.NavRadiosFill!;

        Assert.Equal(TunedAuto + " FMC runway ILS unavailable.", form.StatusText);
        Assert.Equal((Fo, Bbs757CduKeys.NavRad, 250), rig.Cdu.Presses[^1]);
    }

    [Fact]
    public async Task With_the_cdu_busy_the_window_still_opens_with_the_courses_and_no_key_is_pressed()
    {
        // The pilot is typing in the CDU window: the page service presses nothing.
        var rig = new WindowRig(new FakeCdu { Screen = Legs(), PendingKeyCount = 3 });

        var form = rig.Open();
        await rig.Def.NavRadiosFill!;

        Assert.Equal("VOR L course 095, VOR R course 000, ILS course 095. ILS tuning unknown. VOR frequencies unavailable. FMC runway ILS unavailable.",
            form.StatusText);
        Assert.Empty(rig.Cdu.Presses);
    }

    [Fact]
    public async Task With_the_page_unavailable_the_window_shows_and_says_the_vors_from_the_variables()
    {
        // The window shows N's own sentence, so the fallback reaches it too.
        var rig = new WindowRig(new FakeCdu { Screen = Legs(), PendingKeyCount = 3 },
            freqL: 116.05, freqR: 113.90, autoL: 1, autoR: 0);

        var form = rig.Open();
        await rig.Def.NavRadiosFill!;

        const string said = "VOR L 116.05 auto. VOR R 113.90 manual, course 000. ILS course 095. ILS tuning unknown. FMC runway ILS unavailable.";
        Assert.Equal(said, form.StatusText);
        Assert.Equal(new[] { said }, rig.Speech.Interrupts);
        Assert.Contains(Bbs757NavRad.VorLFrequencyKey, rig.Reads);
        Assert.Contains(Bbs757NavRad.VorRAutoKey, rig.Reads);
    }

    [Fact]
    public async Task With_the_page_available_the_window_reads_none_of_the_vor_variables()
    {
        var rig = new WindowRig(new FakeCdu { Screen = NavRadio("116.90AGSU") },
            freqL: 116.05, freqR: 113.90, autoL: 1, autoR: 0);

        var form = rig.Open();
        await rig.Def.NavRadiosFill!;

        Assert.StartsWith(TunedAuto, form.StatusText);
        Assert.DoesNotContain(Bbs757NavRad.VorLFrequencyKey, rig.Reads);
        Assert.DoesNotContain(Bbs757NavRad.VorRFrequencyKey, rig.Reads);
        Assert.DoesNotContain(Bbs757NavRad.VorLAutoKey, rig.Reads);
        Assert.DoesNotContain(Bbs757NavRad.VorRAutoKey, rig.Reads);
    }

    [Fact]
    public async Task A_window_closed_before_it_is_filled_says_nothing()
    {
        var gate = new TaskCompletionSource();
        var rig = new WindowRig(CduToApproachRef(PosInit()), gate.Task);
        var form = rig.Open();

        form.Dispose();
        gate.SetResult();
        await rig.Def.NavRadiosFill!;

        Assert.Empty(rig.Speech.All);
        Assert.Empty(rig.Cdu.Presses);   // closed before its walk: no key at all, and nothing to put back
    }

    // ---- The fill, coordinated (the final review's Important 2) -------------------------------------

    private static readonly System.Reflection.MethodInfo OnFormClosed =
        typeof(Form).GetMethod("OnFormClosed", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    [Theory]
    [InlineData("escape")]        // Escape (a window never shown, as here, is disposed at once)
    [InlineData("form closed")]   // a shown window's FormClosed
    public async Task Closing_the_window_mid_fill_stops_its_keys(string how)
    {
        var cdu = CduToApproachRef(PosInit());
        var rig = new WindowRig(cdu);
        cdu.OnPress = key =>
        {
            if (key != Bbs757CduKeys.Init) return;   // the walk has left NAV RADIO: the pilot closes the window now
            var window = rig.Def.NavRadiosWindow!;
            if (how == "escape") OnKeyDown.Invoke(window, new object[] { new KeyEventArgs(Keys.Escape) });
            else OnFormClosed.Invoke(window, new object[] { new FormClosedEventArgs(CloseReason.UserClosing) });
        };

        rig.Open();
        await rig.Def.NavRadiosFill!;

        // INIT went out before the close; no L6 or L5 after it, and the FO CDU is put back on NAV RADIO, which
        // the fill had left (R52).
        Assert.Equal(new[] { (Fo, Bbs757CduKeys.Init, 250), (Fo, Bbs757CduKeys.NavRad, 250) }, cdu.Presses);
        Assert.True(Bbs757NavRad.IsNavRadio(cdu.Screen!));
        Assert.Empty(rig.Speech.All);   // nothing said for a window that is gone
    }

    [Fact]
    public async Task N_during_the_fills_walk_waits_for_it()
    {
        // N pressed while the window's fill walks INIT, L6, L5 on the first officer's CDU: N's NAV RAD key would
        // pull the CDU off the walk. It waits for the walk to end (the CDU back on NAV RADIO), then reads.
        var cdu = CduToApproachRef(PosInit());
        var rig = new WindowRig(cdu);
        var nSpeech = new SpeechCapture();
        Task? n = null;
        cdu.OnPress = key =>
        {
            if (key == Bbs757CduKeys.Init) n ??= rig.Def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), nSpeech, Pages(cdu));
        };

        var form = rig.Open();
        await rig.Def.NavRadiosFill!;
        await n!;

        Assert.Equal(new[]
        {
            (Fo, Bbs757CduKeys.Init, 250), (Fo, "L6", 250), (Fo, "L5", 250), (Fo, Bbs757CduKeys.NavRad, 250),
        }, cdu.Presses);   // the walk whole, and no key of N's in it (the CDU was back on NAV RADIO for N)
        Assert.EndsWith("FMC runway ILS KATL 09L, 110.50, IHZK, front course 095.", form.StatusText);
        Assert.Equal(new[] { TunedAuto }, nSpeech.Interrupts);
    }

    [Fact]
    public async Task The_fill_during_Ns_cdu_step_waits_for_it()
    {
        // Ctrl+N while N brings NAV RADIO up: the fill's walk does not start until N's step has checked its page.
        var cdu = CduToApproachRef(PosInit());
        cdu.Screen = Legs();   // N has a key to press
        var rig = new WindowRig(cdu);
        var nSpeech = new SpeechCapture();
        cdu.OnPress = key =>
        {
            if (key == Bbs757CduKeys.NavRad && rig.Def.NavRadiosWindow == null) rig.Open();
        };
        var nPages = new Bbs757CduPages(cdu, (_, _) =>
        {
            cdu.Events.Add("N checks its page");
            return Task.CompletedTask;
        });

        await rig.Def.ReadNavRadiosAsync(new SimConnectManager(IntPtr.Zero), nSpeech, nPages);
        await rig.Def.NavRadiosFill!;

        Assert.Equal(new[]
        {
            "press NAVRAD", "N checks its page",                       // N's step, whole
            "press INIT", "press L6", "press L5", "press NAVRAD",      // then the fill's walk
        }, cdu.Events);
        Assert.Equal(new[] { TunedAuto }, nSpeech.Interrupts);
        Assert.Equal(TunedAuto + " FMC runway ILS KATL 09L, 110.50, IHZK, front course 095.", rig.Def.NavRadiosWindow!.StatusText);
    }

    [Theory]
    [InlineData("frequency")]
    [InlineData("park")]
    [InlineData("course")]
    public async Task A_set_made_during_the_fill_is_not_overwritten_or_contradicted_by_its_end(string what)
    {
        // The fill read its readout before the set: at its end it neither replaces the set's words nor says
        // the older readout; it only adds the FMC's runway ILS to the status, silently.
        var gate = new TaskCompletionSource();
        var rig = new WindowRig(CduToApproachRef(PosInit()), gate.Task);
        LoadFlight(rig.Def);
        rig.Sim.Fresh[Bbs757NavRad.IlsCourseKey] = 95;   // each click follows a fresh read; the course reads back 095
        var form = rig.Open();   // the fill waits in its courses read

        string said;
        switch (what)
        {
            case "frequency":
                form.ApplyIlsFrequency("110.50");
                said = "ILS 110.50 set by count: the ILS display could not be read.";
                break;
            case "park":
                form.ParkIls();
                said = "ILS already parked by count: the ILS display could not be read.";
                break;
            default:
                form.ApplyIlsCourse("095");
                said = "ILS course 095";
                break;
        }
        Assert.True(await WaitUntil(() => rig.Speech.Interrupts.Count > 0));
        gate.SetResult();
        await rig.Def.NavRadiosFill!;

        Assert.Equal(new[] { said }, rig.Speech.Interrupts);   // the fill's end said nothing
        // The set's words stand, closed as a sentence, and the FMC's runway ILS follows them.
        string sentence = said.EndsWith('.') ? said : said + ".";
        Assert.Equal($"{sentence} FMC runway ILS KATL 09L, 110.50, IHZK, front course 095.", form.StatusText);
    }

    [Fact]
    public async Task Switching_away_stops_the_read_and_says_nothing()
    {
        var gate = new TaskCompletionSource();
        var rig = new WindowRig(new FakeCdu { Screen = NavRadio("116.90AGSU") }, gate.Task);
        var form = rig.Open();

        rig.Def.OnSwitchedAway();
        gate.SetResult();
        await rig.Def.NavRadiosFill!;

        Assert.True(form.IsDisposed);
        Assert.Empty(rig.Speech.All);
        Assert.Empty(rig.Cdu.Presses);
    }

    [Fact]
    public async Task Ctrl_N_shows_one_window_and_brings_it_forward_again()
    {
        var shown = 0;
        var def = new BBS757Definition
        {
            NestRunning = () => true,
            ReadoutIo = _ => Courses(95, 0, 95),
            ShowRadioForm = (_, _) => shown++,
            TabletClientFactory = () =>
            {
                var client = new CoherentBbs757TabletClient(() => "");
                client.Dispose();
                return client;
            },
        };
        var speech = new SpeechCapture();
        var sim = new SimConnectManager(IntPtr.Zero);

        bool first = def.HandleHotkeyAction(HotkeyAction.SetNavRadios, sim, speech, null!, new HotkeyManager());
        var window = def.NavRadiosWindow;
        await def.NavRadiosFill!;
        bool second = def.HandleHotkeyAction(HotkeyAction.SetNavRadios, sim, speech, null!, new HotkeyManager());

        Assert.True(first);
        Assert.True(second);
        Assert.NotNull(window);
        Assert.Same(window, def.NavRadiosWindow);   // the same window, shown again
        Assert.Equal(1, shown);
        Assert.Equal("757 Nav Radios", window!.Text);

        def.OnSwitchedAway();
        Assert.True(window.IsDisposed);
        Assert.Null(def.NavRadiosWindow);
    }

    /// <summary>A window on its own, whose fields hand over to nothing.</summary>
    private static Forms.BBS757.BBS757NavRadiosForm NewForm() => new(new SpeechCapture(), _ => null, _ => null, () => { });

    private static readonly System.Reflection.MethodInfo OnKeyDown =
        typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    private static readonly System.Reflection.MethodInfo OnClick =
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

    private static TextBox Box(Form form, string name) => form.Controls.OfType<TextBox>().Single(t => t.AccessibleName == name);

    [Fact]
    public void The_window_has_a_read_only_status_box_first_and_the_ils_course_box_with_its_label()
    {
        using var form = NewForm();

        var boxes = form.Controls.OfType<TextBox>().OrderBy(t => t.TabIndex).ToList();
        Assert.Equal(3, boxes.Count);   // the status, the ILS course and the ILS frequency: no VOR boxes, the CDU sets a VOR's course
        Assert.True(boxes[0].ReadOnly);   // the status, first ([VAT-13])
        Assert.False(boxes[1].ReadOnly);
        Assert.Equal("757 Nav Radios", form.Text);

        var label = form.Controls.OfType<Label>().Single(l => l.Text.Replace("&", "") == "ILS course");
        Assert.InRange(label.TabIndex, boxes[0].TabIndex + 1, boxes[1].TabIndex - 1);   // right before its box
        Assert.Equal("ILS course", boxes[1].AccessibleName);
    }

    [Fact]
    public void The_ils_frequency_box_and_the_ils_park_button_follow_the_ils_course_box()
    {
        using var form = NewForm();

        // Tab order: status, ILS course, ILS frequency, ILS park, then Close.
        var tabOrder = form.Controls.Cast<Control>()
            .Where(c => c.TabStop && c is TextBox or Button)
            .OrderBy(c => c.TabIndex)
            .Select(c => c.AccessibleName ?? c.Text)
            .ToList();
        Assert.Equal(new[] { "Nav radios status", "ILS course", "ILS frequency", "ILS park", "Close" }, tabOrder);

        var frequency = Box(form, "ILS frequency");
        Assert.False(frequency.ReadOnly);
        var label = form.Controls.OfType<Label>().Single(l => l.Text.Replace("&", "") == "ILS frequency");
        Assert.InRange(label.TabIndex, Box(form, "ILS course").TabIndex + 1, frequency.TabIndex - 1);   // its label, right before it
    }

    [Fact]
    public void Escape_closes_the_window()
    {
        var form = NewForm();

        OnKeyDown.Invoke(form, new object[] { new KeyEventArgs(Keys.Escape) });

        Assert.True(form.IsDisposed);
    }

    // ---- The ILS course ---------------------------------------------------------------------------

    private static (BBS757Definition Def, IlsSim Sim, SpeechCapture Speech) IlsRig()
    {
        var sim = new IlsSim();
        return (new BBS757Definition { NestRunning = () => true, WriteIo = _ => sim.Io }, sim, new SpeechCapture());
    }

    private static Task SetIls(BBS757Definition def, int degrees, SpeechCapture speech, Action<string>? onSaid = null)
        => def.SetIlsCourseAsync(degrees, new SimConnectManager(IntPtr.Zero), speech, onSaid);

    [Fact]
    public async Task An_ils_course_is_written_read_back_and_said_from_what_came_back()
    {
        var (def, sim, speech) = IlsRig();
        sim.Fresh[Bbs757NavRad.IlsCourseKey] = 95;
        var said = new List<string>();

        await SetIls(def, 95, speech, said.Add);

        Assert.Equal(new[] { "95 (>L:BBS_ILS_COURSE)" }, sim.Writes);
        Assert.Equal(new[] { 1500 }, sim.Delays);   // ReadBackDelayMs, no more
        Assert.Equal(new[] { "ILS course 095" }, speech.Interrupts);
        Assert.Equal(new[] { "ILS course 095" }, said);   // every result also goes into the status box
    }

    [Fact]
    public async Task What_the_aircraft_holds_is_said_not_what_was_asked()
    {
        var (def, sim, speech) = IlsRig();
        sim.Fresh[Bbs757NavRad.IlsCourseKey] = 90;   // the aircraft did not take 95

        await SetIls(def, 95, speech);

        Assert.Equal(new[] { "ILS course 090" }, speech.Interrupts);
    }

    [Fact]
    public async Task An_ils_course_that_cannot_be_read_back_is_not_claimed()
    {
        var (def, sim, speech) = IlsRig();
        sim.Fresh[Bbs757NavRad.IlsCourseKey] = null;
        var said = new List<string>();

        await SetIls(def, 5, speech, said.Add);

        Assert.Equal(new[] { "ILS course 005 sent, not read back" }, speech.Interrupts);
        Assert.Equal(speech.Interrupts, said);
    }

    [Fact]
    public async Task An_ils_course_is_refused_aloud_when_no_write_can_land()
    {
        var (def, sim, speech) = IlsRig();
        sim.CanWrite = false;
        var said = new List<string>();

        await SetIls(def, 95, speech, said.Add);

        Assert.Empty(sim.Writes);   // nothing written
        Assert.Empty(sim.Delays);
        Assert.Equal(new[] { "ILS course unavailable" }, speech.Interrupts);
        Assert.Equal(new[] { "ILS course unavailable" }, said);
    }

    [Fact]
    public async Task An_ils_course_after_switching_away_writes_and_says_nothing()
    {
        var (def, sim, speech) = IlsRig();
        def.OnSwitchedAway();

        await SetIls(def, 95, speech);

        Assert.Empty(sim.Writes);
        Assert.Empty(speech.All);
    }

    [Fact]
    public async Task Enter_in_the_ils_box_sets_that_course_only()
    {
        var rig = new WindowRig(new FakeCdu { Screen = NavRadio("116.90AGSU") });
        rig.Sim.Fresh[Bbs757NavRad.IlsCourseKey] = 270;
        var form = rig.Open();
        await rig.Def.NavRadiosFill!;
        rig.Speech.Interrupts.Clear();
        var presses = rig.Cdu.Presses.Count;

        form.ApplyIlsCourse(" 270 ");

        Assert.True(await WaitUntil(() => rig.Speech.Interrupts.Count > 0));
        Assert.Equal(new[] { "ILS course 270" }, rig.Speech.Interrupts);
        Assert.Equal("ILS course 270", form.StatusText);
        Assert.Equal(new[] { "270 (>L:BBS_ILS_COURSE)" }, rig.Sim.Writes);   // one write: the ILS course
        Assert.Equal(presses, rig.Cdu.Presses.Count);                        // no CDU key for it
    }

    [Theory]
    [InlineData("", "Type a course first.")]
    [InlineData("400", "Course must be 0 to 360.")]
    [InlineData("north", "Course must be 0 to 360.")]
    public async Task A_course_that_is_not_one_is_refused_aloud_and_nothing_is_written(string typed, string said)
    {
        var rig = new WindowRig(new FakeCdu { Screen = NavRadio("116.90AGSU") });
        var form = rig.Open();
        await rig.Def.NavRadiosFill!;
        rig.Speech.Interrupts.Clear();

        form.ApplyIlsCourse(typed);

        Assert.Equal(new[] { said }, rig.Speech.Interrupts);   // a validation error always speaks
        Assert.Equal(said, form.StatusText);
        Assert.Empty(rig.Sim.Writes);
    }

    // ---- The ILS frequency and park (counted knob clicks) -------------------------------------------

    /// <summary>The clicks from the panel as a flight loads it (PARK, .10) to 110.50: WHOLE -1 x2, FRAC +1 x4.</summary>
    private static readonly string[] LoadStateTo11050 =
    {
        "-1 (>L:B757_ILS_FREQ_WHOLE_INC)", "-1 (>L:B757_ILS_FREQ_WHOLE_INC)",
        "1 (>L:B757_ILS_FREQ_FRAC_INC)", "1 (>L:B757_ILS_FREQ_FRAC_INC)", "1 (>L:B757_ILS_FREQ_FRAC_INC)", "1 (>L:B757_ILS_FREQ_FRAC_INC)",
    };

    /// <summary>A window opened after a flight load (the ILS counted from PARK, .10) and filled.</summary>
    private static async Task<(WindowRig Rig, Forms.BBS757.BBS757NavRadiosForm Form)> OpenAfterAFlightLoad()
    {
        var rig = new WindowRig(new FakeCdu { Screen = NavRadio("116.90AGSU") });
        LoadFlight(rig.Def);
        rig.Sim.Fresh[Bbs757NavRad.IlsCourseKey] = 95;   // each click follows a fresh read
        var form = rig.Open();
        await rig.Def.NavRadiosFill!;
        rig.Speech.Interrupts.Clear();
        return (rig, form);
    }

    [Fact]
    public async Task The_window_status_says_the_counted_ils()
    {
        var (_, form) = await OpenAfterAFlightLoad();

        // The rig's display is unreadable: the count, said as one.
        Assert.Equal("VOR L 116.90 auto, GSU. VOR R not tuned. ILS course 095. ILS parked by count. FMC runway ILS unavailable.", form.StatusText);
    }

    [Fact]
    public async Task Enter_in_the_ils_frequency_box_sets_that_frequency_only()
    {
        var (rig, form) = await OpenAfterAFlightLoad();
        var presses = rig.Cdu.Presses.Count;
        Box(form, "ILS course").Text = "270";   // typed, not applied
        var frequency = Box(form, "ILS frequency");
        frequency.Text = " 110.50 ";

        OnKeyDown.Invoke(frequency, new object[] { new KeyEventArgs(Keys.Enter) });

        Assert.True(await WaitUntil(() => rig.Speech.Interrupts.Count > 0));
        const string byCount = "ILS 110.50 set by count: the ILS display could not be read.";   // the rig's display is unreadable
        Assert.Equal(new[] { byCount }, rig.Speech.Interrupts);
        Assert.Equal(byCount, form.StatusText);
        Assert.Equal(LoadStateTo11050, rig.Sim.Writes);                         // the knob clicks only: no course write (R55)
        Assert.Equal(Enumerable.Repeat(250, 5), rig.Sim.Delays);                // 250 ms between clicks
        Assert.Equal(presses, rig.Cdu.Presses.Count);                           // no CDU key for it
    }

    [Theory]
    [InlineData("", "Type a frequency first.")]
    [InlineData("110.52", "ILS frequency must be 108.10 to 111.95, on an ILS channel.")]
    [InlineData("118.00", "ILS frequency must be 108.10 to 111.95, on an ILS channel.")]
    public async Task A_frequency_that_is_no_ils_channel_is_refused_aloud_and_nothing_is_written(string typed, string said)
    {
        var (rig, form) = await OpenAfterAFlightLoad();

        form.ApplyIlsFrequency(typed);

        Assert.Equal(new[] { said }, rig.Speech.Interrupts);   // a validation error always speaks
        Assert.Equal(said, form.StatusText);
        Assert.Empty(rig.Sim.Writes);
    }

    [Fact]
    public async Task The_ils_park_button_parks_it()
    {
        var (rig, form) = await OpenAfterAFlightLoad();
        form.ApplyIlsFrequency("110.50");
        Assert.True(await WaitUntil(() => rig.Speech.Interrupts.Count > 0));
        rig.Speech.Interrupts.Clear();
        rig.Sim.Writes.Clear();
        var park = form.Controls.OfType<Button>().Single(b => b.AccessibleName == "ILS park");

        OnClick.Invoke(park, new object[] { EventArgs.Empty });

        Assert.True(await WaitUntil(() => rig.Speech.Interrupts.Count > 0));
        const string byCount = "ILS parked by count: the ILS display could not be read.";
        Assert.Equal(new[] { byCount }, rig.Speech.Interrupts);
        Assert.Equal(byCount, form.StatusText);
        Assert.Equal(new[] { "1 (>L:B757_ILS_FREQ_WHOLE_INC)", "1 (>L:B757_ILS_FREQ_WHOLE_INC)" }, rig.Sim.Writes);   // 110, 111, PARK
    }

    [Fact]
    public async Task Before_any_flight_load_the_window_refuses_the_ils_frequency_and_park_aloud()
    {
        var rig = new WindowRig(new FakeCdu { Screen = NavRadio("116.90AGSU") });
        var form = rig.Open();
        await rig.Def.NavRadiosFill!;
        rig.Speech.Interrupts.Clear();

        form.ApplyIlsFrequency("110.50");
        Assert.True(await WaitUntil(() => rig.Speech.Interrupts.Count == 1));   // the display is tried first
        form.ParkIls();
        Assert.True(await WaitUntil(() => rig.Speech.Interrupts.Count == 2));

        const string refusal = "ILS tuning unknown: the ILS display could not be read.";
        Assert.Equal(new[] { refusal, refusal }, rig.Speech.Interrupts);
        Assert.Equal(refusal, form.StatusText);
        Assert.Empty(rig.Sim.Writes);
    }

    // ---- A fake first officer's CDU ---------------------------------------------------------------

    private static Bbs757CduPages Pages(FakeCdu cdu) => new(cdu, (_, _) => Task.CompletedTask);

    private sealed class FakeCdu : IBbs757CduLink
    {
        public bool IsReadable { get; set; } = true;
        public int PendingKeyCount { get; set; }

        /// <summary>What the CDU shows now; null: no frame.</summary>
        public Bbs757CduScreen? Screen { get; set; }

        /// <summary>What it shows once the NAV RAD key was pressed; null: the key changes nothing.</summary>
        public Bbs757CduScreen? ScreenAfterNavRadKey { get; set; }

        /// <summary>What a key shows on a page: (the page's identity, the key) to the screen it then shows; a key not listed changes nothing.</summary>
        public Dictionary<(string Page, string Key), Bbs757CduScreen> Transitions { get; } = new();

        public List<(int Cdu, string Key, int HoldMs)> Presses { get; } = new();

        /// <summary>Every key pressed ("press INIT"), in order with whatever a test adds.</summary>
        public List<string> Events { get; } = new();

        /// <summary>Runs after each key, once it took effect, with the key: a test's hook into a walk.</summary>
        public Action<string>? OnPress { get; set; }

        public void PressKey(int cdu, string key, int holdMs)
        {
            Presses.Add((cdu, key, holdMs));
            Events.Add($"press {key}");
            if (key == Bbs757CduKeys.NavRad && ScreenAfterNavRadKey != null) Screen = ScreenAfterNavRadKey;
            else if (Screen != null && Transitions.TryGetValue((Bbs757CduTitle.Identity(Screen.Lines[0]), key), out var next)) Screen = next;
            OnPress?.Invoke(key);
        }

        public Task<Bbs757CduScreen?> ScrapeAsync(int cdu, CancellationToken ct) => Task.FromResult(Screen);
    }
}

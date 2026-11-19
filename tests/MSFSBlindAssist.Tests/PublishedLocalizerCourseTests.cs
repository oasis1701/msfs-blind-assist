// Characterization tests for PublishedLocalizerCourse: a localizer's course is the approach
// chart's, read from the airport's procedures, and worked out from the localizer's heading only
// when no procedure flies it. In an MSFS 2024 build the worked-out course matched the chart for
// only 43% of localizers (KJFK 04L 43 against 44).

using MSFSBlindAssist.Database;
using MSFSBlindAssist.Forms;
using Leg = MSFSBlindAssist.Database.PublishedLocalizerCourse.Leg;

namespace MSFSBlindAssist.Tests;

public class PublishedLocalizerCourseTests
{
    private const string Icao = "KTST";

    // ---- The selection rule -------------------------------------------------------------

    [Fact]
    public void An_ILS_procedure_beats_an_NDB_procedure_that_names_the_same_localizer()
    {
        // TTCP 11: the NDB/DME approach flies ITAB on its last leg at the NDB's 115; the ILS
        // procedures say 109. The NDB approach has the lower id, so the type decides.
        var legs = new[]
        {
            new Leg("11", "ITAB", "NDBDME", "RW11", 55200, 1, 115.0),
            new Leg("11", "ITAB", "ILS", "NOTEL", 55204, 2, 109.0),
            new Leg("11", "ITAB", "ILS", "RW11", 55204, 3, 109.0),
        };

        Assert.Equal(109, PublishedLocalizerCourse.Select(legs, "11", "ITAB"));
    }

    [Fact]
    public void An_LDA_whose_final_leg_stops_short_of_the_runway_takes_its_last_leg()
    {
        // KDCA 19: the LDA's legs end at a missed approach point, not at RW19.
        var legs = new[]
        {
            new Leg("19", "IVWH", "LDA", "DARIC", 34588, 180052, 150.0),
            new Leg("19", "IVWH", "LDA", "HATAL", 34588, 180053, 150.0),
            new Leg("19", "IVWH", "LDA", "ZAXEB", 34588, 180056, 147.0),
        };

        Assert.Equal(147, PublishedLocalizerCourse.Select(legs, "19", "IVWH"));
    }

    [Fact]
    public void A_procedure_that_reaches_the_runway_beats_one_that_does_not()
        => Assert.Equal(109, PublishedLocalizerCourse.Select(new[]
        {
            new Leg("11", "ITAB", "ILS", "FAF11", 10, 1, 120.0),
            new Leg("11", "ITAB", "ILS", "RW11", 11, 2, 109.0),
        }, "11", "ITAB"));

    [Fact]
    public void Two_procedures_a_degree_apart_always_give_the_lower_approach_id()
    {
        // CYYR 08: two ILS procedures, 75 and 76.
        var legs = new[]
        {
            new Leg("08", "IYR", "ILS", "RW08", 21, 2, 76.0),
            new Leg("08", "IYR", "ILS", "RW08", 20, 1, 75.0),
        };

        Assert.Equal(75, PublishedLocalizerCourse.Select(legs, "08", "IYR"));
        Assert.Equal(75, PublishedLocalizerCourse.Select(legs.Reverse(), "08", "IYR"));
    }

    [Fact]
    public void Runway_and_ident_match_whatever_their_case()
        => Assert.Equal(171, PublishedLocalizerCourse.Select(new[]
        {
            new Leg("18", "IHF", "ILS", "RW18", 1, 1, 171.0),
        }, "18", "ihf"));

    [Fact]
    public void Another_runway_or_another_localizer_never_answers()
    {
        var legs = new[]
        {
            new Leg("27", "IAB", "LOCB", "RW27", 1, 1, 270.0),   // a back course to the other end
            new Leg("09", "IXY", "ILS", "RW09", 2, 2, 91.0),     // another localizer for this runway
        };

        Assert.Null(PublishedLocalizerCourse.Select(legs, "09", "IAB"));
        Assert.Null(PublishedLocalizerCourse.Select(legs, "09", ""));
        Assert.Null(PublishedLocalizerCourse.Select(Array.Empty<Leg>(), "09", "IAB"));
    }

    [Theory]
    [InlineData(0.4, 360)]
    [InlineData(359.6, 360)]
    [InlineData(360.0, 360)]
    [InlineData(4.5, 5)]
    public void The_course_is_a_whole_degree_from_1_to_360(double course, int expected)
        => Assert.Equal(expected, PublishedLocalizerCourse.Select(new[]
        {
            new Leg("36", "INO", "ILS", "RW36", 1, 1, course),
        }, "36", "INO"));

    // ---- Through the runway list and the EFB page -----------------------------------------

    /// <summary>ESST 16's localizer (true 157.7037, own variation 5.7037: worked out as 152),
    /// with whatever procedure legs the test adds.</summary>
    private static RunwayFixtureDb BuildAirport(Action<RunwayFixtureDb> addProcedures)
    {
        var db = new RunwayFixtureDb();
        db.InsertAirport(1, Icao, Icao, magVar: 5.0);
        db.InsertRunwayEnd(1, "16", ilsIdent: "NST", heading: 157.7);
        db.InsertRunwayEnd(2, "34", ilsIdent: null, heading: 337.7);
        db.InsertRunway(1, 1, 1, 2, heading: 157.7);
        db.InsertLocalizerOnly("NST", Icao, "16", locHeading: 157.7037, frequency: 108700, magVar: 5.7037);
        addProcedures(db);
        db.Seal();
        return db;
    }

    private static string ListText(RunwayFixtureDb db, string runway)
        => new LittleNavMapProvider(db.DbPath, "FS2024").GetRunways(Icao).Single(r => r.RunwayID == runway).ToString();

    private static IEnumerable<string> PageLines(RunwayFixtureDb db, string runway)
        => ElectronicFlightBagForm.GetRunwayDetailedInfoCore(db.DbPath, "FS2024", Icao, runway)
            .Split('\n').Select(l => l.Trim());

    [Fact]
    public void The_runway_list_and_the_EFB_page_say_the_charted_course()
    {
        using var db = BuildAirport(d =>
        {
            d.InsertApproach(1, 1, Icao, "16", type: "LOC");
            d.InsertApproachLeg(1, 1, "FN16", "NST", 153.0);
            d.InsertApproachLeg(2, 1, "RW16", "NST", 153.0);
        });

        Assert.EndsWith(" - Localizer 108.70, course 153", ListText(db, "16"));
        Assert.Contains("Localizer 108.70, course 153", PageLines(db, "16"));
    }

    [Fact]
    public void Without_a_procedure_the_course_is_worked_out_from_the_heading()
    {
        using var db = BuildAirport(_ => { });

        Assert.EndsWith(" - Localizer 108.70, course 152", ListText(db, "16"));
        Assert.Contains("Localizer 108.70, course 152", PageLines(db, "16"));
    }

    [Fact]
    public void A_true_course_leg_or_a_missed_approach_leg_is_never_the_course()
    {
        using var db = BuildAirport(d =>
        {
            d.InsertApproach(1, 1, Icao, "16", type: "LOC");
            d.InsertApproachLeg(1, 1, "RW16", "NST", 158.0, isTrueCourse: true);
            d.InsertApproachLeg(2, 1, "MA16", "NST", 200.0, isMissed: true);
            d.InsertApproachLeg(3, 1, "RW16", "NST", 160.0, legType: "TF");
        });

        Assert.EndsWith(" - Localizer 108.70, course 152", ListText(db, "16"));
    }

    [Fact]
    public void A_recovered_orphan_localizer_takes_its_charted_course()
    {
        // fs2024 orphan (join columns NULL) recovered by OrphanIlsLookup: its ident still names
        // the procedure's navaid, so the chart's 89 wins over the worked-out 88.
        using var db = new RunwayFixtureDb();
        db.InsertAirport(1, Icao, Icao, lonx: 0.0, laty: 0.0);
        db.InsertRunwayEnd(101, "09", ilsIdent: "", heading: 90.0, lonx: 0.0, laty: 0.0);
        db.InsertRunwayEnd(102, "27", ilsIdent: "", heading: 270.0, lonx: 0.0323, laty: 0.0);
        db.InsertRunway(1, 1, 101, 102, heading: 90.0);
        db.InsertLocalizerOnly("ILOC", null, null, locHeading: 90.0, frequency: 109100, lonx: 0.0323, laty: 0.0,
            magVar: 2.0);
        db.InsertApproach(1, 1, Icao, "09", type: "LOC");
        db.InsertApproachLeg(1, 1, "RW09", "ILOC", 89.0);
        db.Seal();

        Assert.EndsWith(" - Localizer 109.10, course 89", ListText(db, "09"));
        Assert.Contains("Localizer 109.10, course 89", PageLines(db, "09"));
    }
}

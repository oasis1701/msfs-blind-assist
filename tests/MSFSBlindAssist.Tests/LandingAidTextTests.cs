// Characterization tests for LandingAidText, the one rule that names a runway end's radio
// landing aid, and for the two places that speak it: the EFB runway page
// (ElectronicFlightBagForm.GetRunwayDetailedInfoCore) and the runway description
// (Runway.ToString, through LittleNavMapProvider.GetRunways).
//
// The reported defect: ESST 16 and 34 have a localizer and no glideslope (navdatareader type
// "0", gs_range and gs_pitch NULL), and the EFB headed them "ILS INFORMATION" with "ILS
// Ident" and "ILS Frequency" rows, so a pilot planned an ILS that does not exist.

using System.Globalization;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class LandingAidTextTests
{
    private const string Icao = "KTST";

    private static void UnderCulture(string cultureName, Action assertions)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            assertions();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ---- The rule itself ----------------------------------------------------------------

    [Fact]
    public void A_localizer_with_no_glideslope_is_never_called_an_ILS()
    {
        string text = LandingAidText.Describe(hasGlideslope: false, ilsType: "0",
            frequencyMhz: 108.70, magneticCourse: 157, glideslopePitch: 0);

        // "Localizer" already means no glideslope, so it is not said again.
        Assert.Equal("Localizer 108.70, course 157", text);
        Assert.DoesNotContain("ILS", text);
        Assert.DoesNotContain("glideslope", text);
    }

    [Fact]
    public void A_CAT_I_ILS_does_not_say_its_category_or_a_standard_glideslope()
        => Assert.Equal("ILS 110.10, course 171",
            LandingAidText.Describe(true, "1", 110.10, 171, 3.0));

    [Theory]
    [InlineData("2", "ILS CAT II 110.30, course 270")]
    [InlineData("3", "ILS CAT III 110.30, course 270")]
    public void CAT_II_and_III_are_said(string type, string expected)
        => Assert.Equal(expected, LandingAidText.Describe(true, type, 110.30, 270, 3.0));

    [Theory]
    [InlineData(5.5, "ILS 110.10, course 90, glideslope 5.5 degrees")]      // London City
    [InlineData(6.65, "ILS 110.10, course 90, glideslope 6.7 degrees")]     // Lugano, halves away from zero
    [InlineData(3.2, "ILS 110.10, course 90, glideslope 3.2 degrees")]
    [InlineData(3.04, "ILS 110.10, course 90")]                             // rounds to 3.0: not said
    [InlineData(0.0, "ILS 110.10, course 90")]                              // unknown angle: not said
    public void Only_a_glideslope_that_is_not_3_degrees_is_said(double pitch, string expected)
        => Assert.Equal(expected, LandingAidText.Describe(true, "1", 110.10, 90, pitch));

    [Fact]
    public void A_CAT_III_with_a_steep_glideslope_says_both()
        => Assert.Equal("ILS CAT III 110.30, course 270, glideslope 3.5 degrees",
            LandingAidText.Describe(true, "3", 110.30, 270, 3.5));

    [Theory]
    [InlineData("U")]   // MSFS 2020 navdatareader's uncategorised ILS/IGS/LDA-with-glideslope
    [InlineData("ILS")]
    [InlineData("")]
    [InlineData(null)]
    public void An_uncategorised_ILS_with_a_glideslope_is_a_plain_ILS(string? type)
        => Assert.Equal("ILS 109.50, course 45, glideslope 3.5 degrees",
            LandingAidText.Describe(true, type, 109.50, 45, 3.5));

    // An offset approach's course is not the runway's, so it is named for its facility and
    // never called an ILS or a localizer. Rows from an MSFS 2024 navdatareader 1.2.4 build with
    // a Navigraph update.
    [Theory]
    [InlineData("A", false, 111.35, 147, 0.0, "LDA 111.35, course 147")]                          // KDCA 19, runway 186
    [InlineData("L", true, 109.35, 239, 4.2, "LDA 109.35, course 239, glideslope 4.2 degrees")]   // KDLS 25, runway 254
    [InlineData("L", true, 109.35, 239, 3.0, "LDA 109.35, course 239, with glideslope")]
    [InlineData("I", true, 111.90, 89, 3.1, "IGS 111.90, course 89, glideslope 3.1 degrees")]     // VHHX 13, runway 137
    [InlineData("I", true, 111.90, 89, 3.0, "IGS 111.90, course 89")]
    [InlineData("I", false, 111.90, 89, 0.0, "IGS 111.90, course 89, no glideslope")]
    [InlineData("A", true, 111.35, 147, 3.0, "LDA 111.35, course 147, with glideslope")]          // the data decides, not the code
    public void An_offset_approach_is_named_for_its_facility(string type, bool hasGlideslope,
        double frequencyMhz, int course, double pitch, string expected)
    {
        string text = LandingAidText.Describe(hasGlideslope, type, frequencyMhz, course, pitch);

        Assert.Equal(expected, text);
        Assert.DoesNotContain("ILS", text);
        Assert.DoesNotContain("Localizer", text);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "LDA|IGS"));
    }

    [Fact]
    public void A_type_0_row_that_is_named_ILS_is_still_a_localizer_by_its_glideslope_data()
    {
        // 16 of the 321 type "0" rows in an MSFS 2020 build are NAMED "ILS RW…"; the data
        // decides, never the name.
        Assert.False(LandingAidText.HasGlideslope(null, null));
        Assert.False(LandingAidText.HasGlideslope(0, 0));
        Assert.True(LandingAidText.HasGlideslope(10, null));
        Assert.True(LandingAidText.HasGlideslope(null, 3.0));
    }

    [Theory]
    [InlineData(176.68, 5.7, 171)]
    [InlineData(2.0, 5.0, 357)]     // wraps below north
    [InlineData(358.0, -5.0, 3)]    // wraps past north, west variation
    [InlineData(5.4, 5.4, 360)]     // due north is 360, never 0
    [InlineData(90.5, 0.0, 91)]     // halves round away from zero
    public void The_course_is_magnetic_from_1_to_360(double trueHeading, double magVar, int expected)
        => Assert.Equal(expected, LandingAidText.MagneticCourse(trueHeading, magVar));

    [Fact]
    public void Every_number_reads_with_a_dot_under_a_comma_culture()
        => UnderCulture("sv-SE", () =>
        {
            Assert.Equal("ILS 110.10, course 171, glideslope 5.5 degrees",
                LandingAidText.Describe(true, "1", 110.1, 171, 5.5));
            Assert.Equal("Localizer 108.70, course 157",
                LandingAidText.Describe(false, "0", 108.7, 157, 0));
        });

    // ---- The EFB runway page ------------------------------------------------------------

    private static RunwayFixtureDb BuildTwoRunwayAirport()
    {
        // 16 has a localizer only (ESST's shape); 34 has a CAT III ILS; 09/27 has neither.
        //
        // The course is read with the LOCALIZER's own variation: NST carries ESST's real pair
        // (true 157.7037, ils.mag_var 5.7037, published course 152), which the airport's 5.0
        // would round to 153. IST has no variation of its own, so it falls back to the
        // airport's: 337.7 - 5.0 = 333.
        var db = new RunwayFixtureDb();
        db.InsertAirport(1, Icao, Icao, magVar: 5.0);
        db.InsertRunwayEnd(1, "16", ilsIdent: "NST", heading: 157.7);
        db.InsertRunwayEnd(2, "34", ilsIdent: "IST", heading: 337.7);
        db.InsertRunway(1, 1, 1, 2, heading: 157.7);
        db.InsertRunwayEnd(3, "09", ilsIdent: null, heading: 90.0, lonx: 1.0, laty: 1.0);
        db.InsertRunwayEnd(4, "27", ilsIdent: null, heading: 270.0, lonx: 1.01, laty: 1.0);
        db.InsertRunway(2, 1, 3, 4, heading: 90.0, lonx: 1.0, laty: 1.0);
        db.InsertLocalizerOnly("NST", Icao, "16", locHeading: 157.7037, frequency: 108700, name: "ILS RW16",
            magVar: 5.7037);
        db.InsertIls("IST", Icao, "34", locHeading: 337.7, frequency: 110900,
            gsPitch: 3.0, gsRange: 10, type: "3");
        db.Seal();
        return db;
    }

    [Fact]
    public void The_EFB_page_calls_a_localizer_only_runway_a_localizer()
    {
        using var db = BuildTwoRunwayAirport();
        UnderCulture("de-DE", () =>
        {
            string page = ElectronicFlightBagForm.GetRunwayDetailedInfoCore(db.DbPath, "FS2020", Icao, "16");

            Assert.Contains("Localizer 108.70, course 152", page.Split('\n').Select(l => l.Trim()));
            Assert.DoesNotContain("ILS Ident", page);
            Assert.DoesNotContain("ILS Frequency", page);
            Assert.DoesNotContain("ILS Type", page);
            Assert.DoesNotContain("ILS INFORMATION", page);
        });
    }

    [Fact]
    public void The_EFB_page_names_a_CAT_III_ILS()
    {
        using var db = BuildTwoRunwayAirport();
        string page = ElectronicFlightBagForm.GetRunwayDetailedInfoCore(db.DbPath, "FS2020", Icao, "34");

        Assert.Contains("ILS CAT III 110.90, course 333", page.Split('\n').Select(l => l.Trim()));
    }

    [Fact]
    public void The_EFB_page_says_no_localizer()
    {
        using var db = BuildTwoRunwayAirport();
        string page = ElectronicFlightBagForm.GetRunwayDetailedInfoCore(db.DbPath, "FS2020", Icao, "09");

        Assert.Contains("  No localizer", page);
        Assert.DoesNotContain("No ILS available", page);
    }

    // ---- The runway description ---------------------------------------------------------

    [Fact]
    public void The_runway_description_uses_the_same_words()
    {
        using var db = BuildTwoRunwayAirport();
        UnderCulture("sv-SE", () =>
        {
            var runways = new LittleNavMapProvider(db.DbPath, "FS2020").GetRunways(Icao)
                .ToDictionary(r => r.RunwayID);

            Assert.EndsWith(" - Localizer 108.70, course 152", runways["16"].ToString());
            Assert.EndsWith(" - ILS CAT III 110.90, course 333", runways["34"].ToString());
            Assert.DoesNotContain("ILS", runways["09"].ToString());
            Assert.DoesNotContain("Localizer", runways["09"].ToString());
        });
    }

    [Fact]
    public void The_runway_list_and_the_EFB_page_name_an_LDA()
    {
        // KDCA 19's shape: an LDA with no glideslope (type "A"), true 136.0 with a 10.6 degree
        // west variation, so course 147 on a runway that is 186 magnetic.
        using var db = new RunwayFixtureDb();
        db.InsertAirport(1, Icao, Icao, magVar: -10.6);
        db.InsertRunwayEnd(1, "01", ilsIdent: null, heading: 355.5);
        db.InsertRunwayEnd(2, "19", ilsIdent: "IVWH", heading: 175.5);
        db.InsertRunway(1, 1, 1, 2, heading: 355.5);
        db.InsertIls("IVWH", Icao, "19", locHeading: 136.0, frequency: 111350, type: "A",
            name: "LDA RW19", magVar: -10.6);
        db.Seal();

        UnderCulture("de-DE", () =>
        {
            var rwy19 = new LittleNavMapProvider(db.DbPath, "FS2024").GetRunways(Icao).Single(r => r.RunwayID == "19");
            Assert.EndsWith(" - LDA 111.35, course 147", rwy19.ToString());

            string page = ElectronicFlightBagForm.GetRunwayDetailedInfoCore(db.DbPath, "FS2024", Icao, "19");
            Assert.Contains("LDA 111.35, course 147", page.Split('\n').Select(l => l.Trim()));
        });
    }

    [Fact]
    public void A_recovered_localizer_only_orphan_keeps_no_glideslope_angle()
    {
        // fs2024 orphan (join columns NULL) recovered by OrphanIlsLookup: ReadILSFromReader
        // defaults its NULL gs_pitch to 3.0 for the ILS guidance, which must not make the
        // runway an ILS or give it a glideslope angle.
        using var db = new RunwayFixtureDb();
        db.InsertAirport(1, Icao, Icao, lonx: 0.0, laty: 0.0);
        db.InsertRunwayEnd(101, "09", ilsIdent: "", heading: 90.0, lonx: 0.0, laty: 0.0);
        db.InsertRunwayEnd(102, "27", ilsIdent: "", heading: 270.0, lonx: 0.0323, laty: 0.0);
        db.InsertRunway(1, 1, 101, 102, heading: 90.0);
        // Its own variation (2.0) wins over the airport's (0): course 88, not 90.
        db.InsertLocalizerOnly("ILOC", null, null, locHeading: 90.0, frequency: 109100, lonx: 0.0323, laty: 0.0,
            magVar: 2.0);
        db.Seal();

        var rwy09 = new LittleNavMapProvider(db.DbPath, "FS2024").GetRunways(Icao).Single(r => r.RunwayID == "09");

        Assert.Equal(109.10, rwy09.ILSFreq, 3);
        Assert.False(rwy09.ILSHasGlideslope);
        Assert.Equal(0.0, rwy09.GlideslopeAngleDeg);
        Assert.EndsWith(" - Localizer 109.10, course 88", rwy09.ToString());
    }
}

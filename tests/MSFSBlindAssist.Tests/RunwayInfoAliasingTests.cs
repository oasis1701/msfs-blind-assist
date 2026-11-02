// Fixture test for the runway_end column-aliasing invariant in
// MSFSBlindAssist.Forms.ElectronicFlightBagForm.GetRunwayDetailedInfoCore (Forms/ElectronicFlightBagForm.cs).
//
// This is the SQL-level companion to ElectronicFlightBagFormRunwayInfoTests.cs. That file's
// FormatRunwayHeadingsAndCoordinates/FormatRunwayPatternAltitude tests only pin correct
// RENDERING of already-resolved values — they pass hard-coded doubles straight to the
// formatter and never touch a database, so they cannot catch a dropped `AS end_heading` /
// `AS end_altitude` / `AS end_lonx` / `AS end_laty` SQL alias or a C# revert from
// reader["end_heading"] back to the ambiguous bare reader["heading"]. This file drives the
// REAL query — `SELECT r.*, re.*, re.heading AS end_heading, ... FROM runway r JOIN
// runway_end re ON ... JOIN airport a ...` — against a synthetic SQLite fixture built with
// BOTH runway and runway_end columns literally named heading/altitude/lonx/laty (mirroring
// the real navdata schema's ambiguity), so it reproduces the exact column-resolution
// mechanics the production database has.
//
// GetRunwayDetailedInfoCore(dbPath, simulatorVersion, icao, runwayId) is the extracted,
// behavior-neutral core of the private GetRunwayDetailedInfo(icao, runwayId): the public
// method now just resolves dbPath/simulatorVersion via DatabasePathResolver/SettingsManager
// (unchanged) and delegates here — zero SQL or formatting change from the pre-extraction
// method body.

using System.Globalization;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class RunwayInfoAliasingTests
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

    // Primary end 06R: true heading 64.0°, own coordinates/altitude.
    // The runway row's OWN heading/altitude/lonx/laty columns are set to these same primary
    // values — replicating the real navdata schema, where the ambiguous `runway` table columns
    // resolve to the primary end when no alias is used. This is exactly the "showed the primary
    // end's heading (off by 180°)" bug [EFB-6] guards against.
    private const double PrimaryHeading = 64.0;
    private const double PrimaryAltitude = 10.0;
    private const double PrimaryLonx = -122.375;
    private const double PrimaryLaty = 37.618;

    // Secondary end 24L: true heading 244.0° (the reciprocal of 64.0), distinct coordinates
    // and altitude — the SELECTED end whose OWN values must appear in the output.
    private const double SecondaryHeading = 244.0;
    private const double SecondaryAltitude = 25.0;
    private const double SecondaryLonx = -122.360;
    private const double SecondaryLaty = 37.601;

    private static RunwayFixtureDb BuildFixture()
    {
        var fixture = new RunwayFixtureDb();
        fixture.InsertAirport(airportId: 1, icao: Icao, ident: Icao, magVar: -13.5);

        fixture.InsertRunwayEnd(runwayEndId: 1, name: "06R", ilsIdent: "I06R",
            heading: PrimaryHeading, altitude: PrimaryAltitude, lonx: PrimaryLonx, laty: PrimaryLaty);
        fixture.InsertRunwayEnd(runwayEndId: 2, name: "24L", ilsIdent: "I24L",
            heading: SecondaryHeading, altitude: SecondaryAltitude, lonx: SecondaryLonx, laty: SecondaryLaty);

        // The runway row's own heading/altitude/lonx/laty mirror the PRIMARY end — the real
        // schema's ambiguous "runway center" columns land on the primary end's values.
        fixture.InsertRunway(runwayId: 1, airportId: 1, primaryEndId: 1, secondaryEndId: 2,
            heading: PrimaryHeading, altitude: PrimaryAltitude, lonx: PrimaryLonx, laty: PrimaryLaty,
            patternAltitude: 1000);

        // A matching, airport-scoped ILS row for EACH end so the primary ILS resolution path
        // (runway_end.ils_ident + a same-airport `ils` row) always succeeds and
        // LittleNavMapProvider's spatial fallback — a separate, unrelated code path this test
        // is not exercising — is never reached.
        fixture.InsertIls(ident: "I06R", locAirportIdent: Icao, locRunwayName: "06R", locHeading: PrimaryHeading);
        fixture.InsertIls(ident: "I24L", locAirportIdent: Icao, locRunwayName: "24L", locHeading: SecondaryHeading);

        fixture.Seal();
        return fixture;
    }

    [Fact]
    public void Secondary_end_query_returns_its_own_heading_not_the_primary_ends_reciprocal_heading()
    {
        using var fixture = BuildFixture();

        string result = ElectronicFlightBagForm.GetRunwayDetailedInfoCore(fixture.DbPath, "FS2020", Icao, "24L");

        Assert.Contains("True Heading:         244.0°", result);
        Assert.DoesNotContain("True Heading:         64.0°", result);
    }

    [Fact]
    public void Secondary_end_query_returns_its_own_coordinates_not_the_primary_ends()
    {
        using var fixture = BuildFixture();

        string result = ElectronicFlightBagForm.GetRunwayDetailedInfoCore(fixture.DbPath, "FS2020", Icao, "24L");

        // -122.360 / 37.601 (secondary) versus -122.375 / 37.618 (primary), as rendered.
        Assert.Contains("Longitude:            122.3600 W", result);
        Assert.Contains("Latitude:             37.6010 N", result);
        Assert.DoesNotContain("Longitude:            122.3750 W", result);
        Assert.DoesNotContain("Latitude:             37.6180 N", result);
    }

    [Fact]
    public void Secondary_end_query_returns_its_own_altitude_msl_not_the_primary_ends()
    {
        using var fixture = BuildFixture();

        string result = ElectronicFlightBagForm.GetRunwayDetailedInfoCore(fixture.DbPath, "FS2020", Icao, "24L");

        Assert.Contains("Altitude (MSL):       25 ft", result);
        Assert.DoesNotContain("Altitude (MSL):       10 ft", result);
    }

    [Fact]
    public void Primary_end_query_returns_its_own_heading_not_the_secondary_ends()
    {
        using var fixture = BuildFixture();

        string result = ElectronicFlightBagForm.GetRunwayDetailedInfoCore(fixture.DbPath, "FS2020", Icao, "06R");

        Assert.Contains("True Heading:         64.0°", result);
        Assert.DoesNotContain("True Heading:         244.0°", result);
    }

    [Fact]
    public void The_whole_runway_page_reads_every_number_with_a_dot_under_a_comma_culture()
    {
        // 32-bit-float values as navdata stores them (fs2024 ESGG 21). Every number on the page,
        // the ILS block included, must read with a dot and without float noise on a comma-decimal
        // Windows; the fixture's names hold no comma, so any comma in the output is a number.
        using var fixture = new RunwayFixtureDb();
        fixture.InsertAirport(airportId: 1, icao: Icao, ident: Icao, magVar: 5.44113683700562);
        fixture.InsertRunwayEnd(runwayEndId: 1, name: "03", ilsIdent: "SGG",
            heading: 25.9843444824219, altitude: 510.045959472656, lonx: 12.2942123413086, laty: 57.678596496582);
        fixture.InsertRunwayEnd(runwayEndId: 2, name: "21", ilsIdent: "NGG",
            heading: 206.004821777344, altitude: 510.045959472656, lonx: 12.2654523849487, laty: 57.6470985412598);
        fixture.InsertRunway(runwayId: 1, airportId: 1, primaryEndId: 1, secondaryEndId: 2,
            heading: 25.9843444824219, altitude: 510.045959472656, length: 10802.5, width: 155.0);
        // NGG with a DME and a glideslope, so those blocks show and are checked too.
        fixture.InsertIls(ident: "NGG", locAirportIdent: Icao, locRunwayName: "21",
            locHeading: 206.004821777344, frequency: 108500, locWidth: 3.40000009536743,
            lonx: 12.2654523849487, laty: 57.6470985412598, altitude: 505.997406005859,
            gsPitch: 3.0, dmeRange: 18, gsRange: 10);
        fixture.InsertIls(ident: "SGG", locAirportIdent: Icao, locRunwayName: "03",
            locHeading: 25.9843444824219, frequency: 110300, locWidth: 3.40000009536743);
        fixture.Seal();

        UnderCulture("sv-SE", () =>
        {
            string result = ElectronicFlightBagForm.GetRunwayDetailedInfoCore(fixture.DbPath, "FS2020", Icao, "21");

            Assert.Contains("Frequency:            108.50 MHz", result);
            // 206.0 true less 5.4 east variation: course 201 magnetic.
            // A 3.0 degree glideslope is not said: the whole line is just this.
            Assert.Contains("ILS 108.50, course 201", result.Split('\n').Select(l => l.Trim()));
            Assert.Contains("Loc Heading:          206.0° true", result);
            Assert.Contains("Loc Width:            3.4°", result);
            Assert.Contains("DME Altitude:         506 ft", result);
            Assert.Contains("DME Longitude:        12.2655 E", result);
            Assert.Contains("GS Pitch:             3.0°", result);
            Assert.Contains("GS Latitude:          57.6471 N", result);
            Assert.Contains("Localizer Altitude:   506 ft", result);
            Assert.Contains("Magnetic Heading:     200.6°", result);
            Assert.Contains("Length:               10803 ft", result);
            Assert.Contains("Altitude (MSL):       510 ft", result);
            // The localizer summary line is comma-separated text, so look for a decimal comma.
            Assert.DoesNotMatch(@"\d,\d", result);
        });
    }

    [Fact]
    public void A_recovered_ILS_reads_with_a_dot_and_says_its_heading_is_true()
    {
        // 09L's runway_end has no ils_ident and its localizer is an fs2024 orphan, so the page
        // takes the GetILSForRunway path (OrphanIlsRunwayLinkTests' geometry). That path
        // formatted with "F2"/"F1" in the current culture: "110,30 MHz" under sv-SE.
        using var fixture = new RunwayFixtureDb();
        fixture.InsertAirport(1, Icao, Icao, lonx: 0.0, laty: 0.0);
        fixture.InsertRunwayEnd(101, "09L", ilsIdent: "", heading: 90.0, lonx: 0.0, laty: 0.00135);
        fixture.InsertRunwayEnd(102, "27R", ilsIdent: "", heading: 270.0, lonx: 0.0323, laty: 0.00135);
        fixture.InsertRunway(1, 1, 101, 102, heading: 90.0);
        fixture.InsertOrphanIls("ITST", 0.0323, 0.00135, 90.0048217773438, 110300, "ILS/GS CAT I RW09L");
        fixture.Seal();

        UnderCulture("sv-SE", () =>
        {
            string result = ElectronicFlightBagForm.GetRunwayDetailedInfoCore(fixture.DbPath, "FS2024", Icao, "09L");

            Assert.Contains("ILS 110.30, course 90", result.Split('\n').Select(l => l.Trim()));
            Assert.Contains("Ident:                ITST", result);
            Assert.Contains("Frequency:            110.30 MHz", result);
            Assert.Contains("Loc Heading:          90.0° true", result);
            Assert.Contains("Loc Width:            3.0°", result);
            Assert.DoesNotMatch(@"\d,\d", result);
        });
    }
}

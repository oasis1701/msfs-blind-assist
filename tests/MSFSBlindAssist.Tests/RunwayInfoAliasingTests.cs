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

using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class RunwayInfoAliasingTests
{
    private const string Icao = "KTST";

    // Primary end 06R: true heading 64.0°, own coordinates/altitude.
    // The runway row's OWN heading/altitude/lonx/laty columns are set to these same primary
    // values — replicating the real navdata schema, where the ambiguous `runway` table columns
    // resolve to the primary end when no alias is used. This is exactly the "showed the primary
    // end's heading (off by 180°)" bug the CLAUDE.md invariant describes.
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

        Assert.Contains($"Longitude:            {SecondaryLonx}", result);
        Assert.Contains($"Latitude:             {SecondaryLaty}", result);
        Assert.DoesNotContain($"Longitude:            {PrimaryLonx}", result);
        Assert.DoesNotContain($"Latitude:             {PrimaryLaty}", result);
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
}

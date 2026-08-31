// End-to-end test of the ORPHAN-ILS re-link, through LittleNavMapProvider.GetRunways —
// the call that fills the Shift+D "Select Destination Runway" list.
//
// This exercises the real SQL: the airport-scoped ils_ident LEFT JOIN (which finds
// nothing here, because Orbx KATL leaves every runway_end.ils_ident blank), the fall
// through in CreateRunwayFromReader, and the geometric recovery behind OrphanIlsLookup.
// OrphanIlsMatcherTests covers the selection rule in isolation; this one proves it is
// actually wired into the path the pilot sees.
//
// The fixture is ALL TEN KATL runway ends and all ten orphaned ils rows, shared with the
// matcher suite via KatlOrphanIlsFixture. It covers every end rather than the two runways
// that reproduce the headline pair, because three of the five ends this fix actually
// changes — 08R, 09L and 27R — are otherwise never carried through the provider at all,
// and it is the provider that owns the SQL, the ilsFreq <= 0 fall-through and the
// candidate hand-off.

using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Tests;

public class OrphanIlsRunwayLinkTests
{
    private const string Icao = KatlOrphanIlsFixture.Icao;

    private static Dictionary<string, double> FrequenciesByRunway(RunwayFixtureDb db, string icao = Icao)
        => new LittleNavMapProvider(db.DbPath, "FS2024")
            .GetRunways(icao)
            .ToDictionary(r => r.RunwayID, r => r.ILSFreq);

    // The reported defect, stated exactly as the pilot saw it.
    [Fact]
    public void Parallel_runways_do_not_share_one_ILS_frequency()
    {
        using var db = KatlOrphanIlsFixture.BuildDb();
        var freq = FrequenciesByRunway(db);

        Assert.NotEqual(freq["08L"], freq["09R"]);
        Assert.Equal(109.30, freq["08L"], 3);
        Assert.Equal(108.90, freq["09R"], 3);
    }

    // The same near-tie mis-assigned the west-facing pair; the pilot only compared the
    // east-facing one.
    [Fact]
    public void Reciprocal_parallel_runways_do_not_share_one_ILS_frequency()
    {
        using var db = KatlOrphanIlsFixture.BuildDb();
        var freq = FrequenciesByRunway(db);

        Assert.NotEqual(freq["26R"], freq["27L"]);
        Assert.Equal(110.10, freq["26R"], 3);
        Assert.Equal(108.50, freq["27L"], 3);
    }

    // Every one of the ten ends, through the provider. Five of these changed with the fix
    // — 08L, 08R, 09L, 27L and 27R — and only two of the five reached the provider before.
    [Theory]
    [InlineData("08L", 109.30)]
    [InlineData("08R", 109.90)]
    [InlineData("09L", 110.50)]
    [InlineData("09R", 108.90)]
    [InlineData("10", 111.55)]
    [InlineData("26L", 108.70)]
    [InlineData("26R", 110.10)]
    [InlineData("27L", 108.50)]
    [InlineData("27R", 111.30)]
    [InlineData("28", 111.75)]
    public void Every_KATL_runway_reads_its_own_ILS_frequency(string runway, double expectedMhz)
    {
        using var db = KatlOrphanIlsFixture.BuildDb();
        Assert.Equal(expectedMhz, FrequenciesByRunway(db)[runway], 3);
    }

    // The symptom itself, through the provider: no two of the ten may report one frequency.
    [Fact]
    public void No_two_KATL_runways_report_the_same_ILS_frequency()
    {
        using var db = KatlOrphanIlsFixture.BuildDb();
        var freqs = FrequenciesByRunway(db).Values.ToList();

        Assert.DoesNotContain(0.0, freqs);
        Assert.Equal(freqs.Count, freqs.Distinct().Count());
    }

    // The localizer course must follow the frequency — the dialog reads both, and showing
    // 08L "090" from 09R's antenna is the same defect in the other field.
    [Fact]
    public void Localizer_course_comes_from_the_runways_own_localizer()
    {
        using var db = KatlOrphanIlsFixture.BuildDb();
        var headings = new LittleNavMapProvider(db.DbPath, "FS2024")
            .GetRunways(Icao)
            .ToDictionary(r => r.RunwayID, r => r.ILSHeading);

        Assert.Equal(89.97322082519531, headings["08L"], 4);   // IHFW, not IFUN
        Assert.Equal(89.97322845458984, headings["09R"], 4);
    }

    // A runway with no localizer of its own must report none rather than borrow the
    // parallel's — a wrong ILS frequency read out to a blind pilot is worse than none.
    [Fact]
    public void Runway_without_its_own_localizer_reports_no_ILS()
    {
        using var db = new RunwayFixtureDb();
        db.InsertAirport(1, "KTST", "KTST", lonx: 0.0, laty: 0.0);

        // Two parallels 150 m apart on 090; only 09L has a localizer.
        db.InsertRunwayEnd(101, "09L", ilsIdent: "", heading: 90.0, lonx: 0.0, laty: 0.00135);
        db.InsertRunwayEnd(102, "27R", ilsIdent: "", heading: 270.0, lonx: 0.0323, laty: 0.00135);
        db.InsertRunway(1, 1, 101, 102, heading: 90.0);

        db.InsertRunwayEnd(103, "09R", ilsIdent: "", heading: 90.0, lonx: 0.0, laty: 0.0);
        db.InsertRunwayEnd(104, "27L", ilsIdent: "", heading: 270.0, lonx: 0.0323, laty: 0.0);
        db.InsertRunway(2, 1, 103, 104, heading: 90.0);

        db.InsertOrphanIls("ITST", 0.0323, 0.00135, 90.0, 110300, "ILS/GS CAT I RW09L");
        db.Seal();

        var freq = FrequenciesByRunway(db, "KTST");
        Assert.Equal(110.30, freq["09L"], 3);
        Assert.Equal(0.0, freq["09R"]);
    }
}

// End-to-end test of the ORPHAN-ILS re-link, through LittleNavMapProvider.GetRunways —
// the call that fills the Shift+D "Select Destination Runway" list.
//
// This exercises the real SQL: the airport-scoped ils_ident LEFT JOIN (which finds
// nothing here, because Orbx KATL leaves every runway_end.ils_ident blank), the fall
// through in CreateRunwayFromReader, and the geometric recovery in
// GetILSForRunwayFallback. OrphanIlsMatcherTests covers the selection rule in isolation;
// this one proves it is actually wired into the path the pilot sees.
//
// The fixture is REAL KATL geometry at full stored precision, narrowed to the two runways
// that reproduce the reported defect: 08L/26R and 09R/27L, with the four orphaned ils rows
// that serve them. Under the predecessor rule (nearest antenna to the threshold) 08L took
// runway 09R's IFUN — the antenna is 3,011 m from 08L's threshold against 3,027 m for
// 08L's own — so the dialog showed 08L and 09R the same 108.90 MHz and the same 090
// course. 27L took 26R's IGXZ the same way.

using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Tests;

public class OrphanIlsRunwayLinkTests
{
    private const string Icao = "KATL";

    private static RunwayFixtureDb BuildKatl()
    {
        var db = new RunwayFixtureDb();
        db.InsertAirport(1, Icao, Icao, magVar: -5.541150093078613,
            lonx: -84.43181610107422, laty: 33.63862228393555);

        // Runway 08L/26R. ils_ident deliberately blank on both ends — that is what Orbx
        // KATL stores, and it is what forces the geometric fallback.
        db.InsertRunwayEnd(101, "08L", ilsIdent: "", heading: 89.98899841308594,
            lonx: -84.43907165527344, laty: 33.649532318115234);
        db.InsertRunwayEnd(102, "26R", ilsIdent: "", heading: 269.989013671875,
            lonx: -84.40937805175781, laty: 33.6495361328125);
        db.InsertRunway(1, 1, 101, 102, heading: 89.98899841308594, length: 9000, width: 150);

        // Runway 09R/27L.
        db.InsertRunwayEnd(103, "09R", ilsIdent: "", heading: 89.97699737548828,
            lonx: -84.44805145263672, laty: 33.631839752197266);
        db.InsertRunwayEnd(104, "27L", ilsIdent: "", heading: 269.97698974609375,
            lonx: -84.4183578491211, laty: 33.63185119628906);
        db.InsertRunway(2, 1, 103, 104, heading: 89.97699737548828, length: 9000, width: 150);

        // The four orphaned localizers, as stored.
        db.InsertOrphanIls("IHFW", -84.40640258789062, 33.64954376220703, 89.97322082519531, 109300, "ILS/GS CAT III RW08L");
        db.InsertOrphanIls("IGXZ", -84.44181060791016, 33.649532318115234, 269.9896240234375, 110100, "ILS/GS CAT II RW26R");
        db.InsertOrphanIls("IFUN", -84.41452026367188, 33.63182067871094, 89.97322845458984, 108900, "ILS/GS CAT III RW09R");
        db.InsertOrphanIls("IFSQ", -84.45093536376953, 33.631813049316406, 269.9896240234375, 108500, "ILS/GS CAT II RW27L");

        db.Seal();
        return db;
    }

    private static Dictionary<string, double> FrequenciesByRunway(RunwayFixtureDb db)
        => new LittleNavMapProvider(db.DbPath, "FS2024")
            .GetRunways(Icao)
            .ToDictionary(r => r.RunwayID, r => r.ILSFreq);

    // The reported defect, stated exactly as the pilot saw it.
    [Fact]
    public void Parallel_runways_do_not_share_one_ILS_frequency()
    {
        using var db = BuildKatl();
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
        using var db = BuildKatl();
        var freq = FrequenciesByRunway(db);

        Assert.NotEqual(freq["26R"], freq["27L"]);
        Assert.Equal(110.10, freq["26R"], 3);
        Assert.Equal(108.50, freq["27L"], 3);
    }

    // The localizer course must follow the frequency — the dialog reads both, and showing
    // 08L "090" from 09R's antenna is the same defect in the other field.
    [Fact]
    public void Localizer_course_comes_from_the_runways_own_localizer()
    {
        using var db = BuildKatl();
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

        var freq = FrequenciesByRunway2(db);
        Assert.Equal(110.30, freq["09L"], 3);
        Assert.Equal(0.0, freq["09R"]);
    }

    private static Dictionary<string, double> FrequenciesByRunway2(RunwayFixtureDb db)
        => new LittleNavMapProvider(db.DbPath, "FS2024")
            .GetRunways("KTST")
            .ToDictionary(r => r.RunwayID, r => r.ILSFreq);
}

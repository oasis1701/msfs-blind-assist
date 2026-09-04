// Characterization tests for MSFSBlindAssist.Database.OrphanIlsMatcher — the geometric
// rule that re-links an ORPHANED ils row (one whose loc_airport_ident / loc_runway_name
// join columns navdatareader left NULL) to the runway end it actually serves.
//
// The KATL geometry lives in KatlOrphanIlsFixture, which OrphanIlsRunwayLinkTests shares —
// see that file for its provenance and for why it is stated once rather than transcribed
// into each suite.

using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Tests;

public class OrphanIlsMatcherTests
{
    private static OrphanIlsMatcher.RunwayEnd End(string name, double lat, double lon, double hdg,
        double lengthM = 0.0)
        => new(name, lat, lon, hdg, lengthM);

    private static ILSData Cand(string ident, double lat, double lon, double locHdg)
        => new() { Ident = ident, AntennaLatitude = lat, AntennaLongitude = lon, LocalizerHeading = locHdg };

    private static readonly OrphanIlsMatcher.RunwayEnd[] KatlEnds = KatlOrphanIlsFixture.MatcherEnds();
    private static readonly ILSData[] KatlCandidates = KatlOrphanIlsFixture.MatcherCandidates();

    private static string? SelectAtKatl(string runwayName)
        => OrphanIlsMatcher.SelectBest(
            KatlEnds, KatlOrphanIlsFixture.IndexOfEnd(runwayName), KatlCandidates)?.Ident;

    // --- The reported defect ------------------------------------------------

    // Orbx KATL leaves runway_end.ils_ident blank on all ten ends, so every runway falls
    // through to this matcher. Under the predecessor rule — nearest antenna to the
    // threshold — 08L took IFUN, runway 09R's CAT III localizer: 09R's antenna is 3011 m
    // from 08L's threshold and 08L's OWN is 3027 m, so a SIXTEEN-METRE margin out of three
    // kilometres decided it, and the Shift+D dialog showed 08L and 09R the same 108.90 /
    // 090. A localizer sits a full runway length along track from the threshold it serves,
    // so straight-line distance is dominated by the along-track term and barely sees the
    // ~1.9 km lateral offset that actually distinguishes one parallel runway from the next.
    [Fact]
    public void KATL_08L_takes_its_own_localizer_not_the_parallel_runways()
    {
        Assert.Equal("IHFW", SelectAtKatl("08L"));   // 109.30, not IFUN/108.90
    }

    // The same near-tie mis-picked two more east-facing ends (08R and 09L also took IFUN)
    // and both of 27L/27R took IBRU — five of the ten in all, and the pilot only noticed
    // the pair they compared.
    [Theory]
    [InlineData("08L", "IHFW")]
    [InlineData("08R", "IATL")]
    [InlineData("09L", "IHZK")]
    [InlineData("09R", "IFUN")]
    [InlineData("10", "IOMO")]
    [InlineData("26L", "IBRU")]
    [InlineData("26R", "IGXZ")]
    [InlineData("27L", "IFSQ")]
    [InlineData("27R", "IAFA")]
    [InlineData("28", "IPKU")]
    public void KATL_every_runway_end_takes_its_own_localizer(string runway, string expectedIdent)
    {
        Assert.Equal(expectedIdent, SelectAtKatl(runway));
    }

    // No two ends may share a localizer — that IS the reported symptom, stated directly.
    [Fact]
    public void KATL_no_two_runway_ends_share_a_localizer()
    {
        var picks = KatlEnds.Select(e => SelectAtKatl(e.Name)).ToList();
        Assert.DoesNotContain(null, picks);
        Assert.Equal(picks.Count, picks.Distinct().Count());
    }

    // --- The rules the selection rests on -----------------------------------

    // Cross-track is the discriminator, not straight-line range: at KATL the correct
    // localizer is 0.1-3.4 m off its runway's centerline while the nearest wrong one is
    // 305 m off — a ~90x separation, against the 16 m that separated them by range.
    [Fact]
    public void Cross_track_separates_parallels_far_more_sharply_than_range()
    {
        var end08L = KatlEnds.Single(e => e.Name == "08L");
        var own = KatlCandidates.Single(c => c.Ident == "IHFW");
        var parallel = KatlCandidates.Single(c => c.Ident == "IFUN");

        double ownCross = OrphanIlsMatcher.CrossTrackMetres(end08L, own.AntennaLatitude, own.AntennaLongitude);
        double parallelCross = OrphanIlsMatcher.CrossTrackMetres(end08L, parallel.AntennaLatitude, parallel.AntennaLongitude);

        Assert.True(ownCross < 5.0, $"own localizer should be on the centerline, was {ownCross:F1} m");
        Assert.True(parallelCross > 1000.0, $"parallel runway's localizer should be far off, was {parallelCross:F1} m");
    }

    // A localizer serving this runway lies BEYOND the far end, i.e. ahead of the threshold
    // along the runway heading. Anything behind the threshold belongs to something else.
    [Fact]
    public void Candidate_behind_the_threshold_is_rejected()
    {
        var end = End("09", 0.0, 0.0, 90.0);
        // 3 km due WEST of the threshold, on centerline, correct heading — but behind.
        var behind = Cand("XXX", 0.0, -0.0323, 90.0);
        Assert.Null(OrphanIlsMatcher.SelectBest([end], 0, [behind]));
    }

    // ...and not so far beyond the far end that it must be serving something else. The
    // centerline is extended forward without limit, so without this ceiling a second
    // airport on the same bearing is admitted on cross-track alone — the cross-track
    // backstop cannot catch it, because the two runways are collinear.
    [Fact]
    public void Candidate_far_beyond_the_far_end_is_rejected()
    {
        var end = End("09", 0.0, 0.0, 90.0, lengthM: 3000.0);
        // On centerline and ahead, but ~11 km out: 3 km of runway plus 8 km of nothing,
        // well past the 3,000 m localizer setback allowance.
        var tooFar = Cand("XXX", 0.0, 0.0988, 90.0);
        Assert.Null(OrphanIlsMatcher.SelectBest([end], 0, [tooFar]));
    }

    // The ceiling is per-runway, so it must not fire on a legitimately long runway. KEDW's
    // lakebed runway is 10.7 km; its localizer would sit beyond that.
    [Fact]
    public void Candidate_beyond_a_long_runway_is_still_accepted()
    {
        var end = End("09", 0.0, 0.0, 90.0, lengthM: 10668.0);
        var farButLegitimate = Cand("XXX", 0.0, 0.0988, 90.0);   // ~11 km, inside 10668 + 3000
        Assert.Equal("XXX", OrphanIlsMatcher.SelectBest([end], 0, [farButLegitimate])?.Ident);
    }

    // An end whose runway length is unknown (0) skips the ceiling rather than risk
    // dropping a real ILS.
    [Fact]
    public void Unknown_runway_length_disables_the_along_track_ceiling()
    {
        var end = End("09", 0.0, 0.0, 90.0);   // lengthM defaults to 0
        var far = Cand("XXX", 0.0, 0.0988, 90.0);
        Assert.Equal("XXX", OrphanIlsMatcher.SelectBest([end], 0, [far])?.Ident);
    }

    // Heading gates the reciprocal end's localizer out.
    [Fact]
    public void Candidate_on_the_reciprocal_heading_is_rejected()
    {
        var end = End("09", 0.0, 0.0, 90.0);
        var reciprocal = Cand("XXX", 0.0, 0.0323, 270.0);
        Assert.Null(OrphanIlsMatcher.SelectBest([end], 0, [reciprocal]));
    }

    // The mutual-best rule: a localizer goes to the runway whose centerline it is closest
    // to, so a closely-spaced parallel that has no localizer of its own gets NOTHING rather
    // than borrowing its neighbour's. Measured live at KPHX 25L/25R (246 m apart), LGAD,
    // ZKWS, UTTT and UZTT. Showing a blind pilot the wrong ILS frequency is worse than
    // showing none — they would tune and fly a localizer for the runway beside them.
    [Fact]
    public void A_parallel_without_its_own_localizer_gets_none_rather_than_its_neighbours()
    {
        // Two parallels 150 m apart on 090; only the north one has a localizer. The
        // spacing is deliberately well inside MaxCrossTrackMetres so that the mutual-best
        // rule is what rejects it for the south runway, not the absolute cap.
        var north = End("09L", 0.00135, 0.0, 90.0);
        var south = End("09R", 0.0, 0.0, 90.0);
        var northLoc = Cand("INOR", 0.00135, 0.0323, 90.0);
        OrphanIlsMatcher.RunwayEnd[] ends = [north, south];

        Assert.Equal("INOR", OrphanIlsMatcher.SelectBest(ends, 0, [northLoc])?.Ident);
        Assert.Null(OrphanIlsMatcher.SelectBest(ends, 1, [northLoc]));
    }

    // The target is identified by POSITION, not by name. An airport carrying two ends with
    // the same name — add-on scenery layered over stock runways, which is the situation
    // that produced this bug in the first place — must still have the second one compete.
    // Under a name compare it was skipped as "the target itself", the mutual-best test
    // passed vacuously, and the parallel-borrowing failure came straight back.
    [Fact]
    public void A_same_named_parallel_still_competes()
    {
        var north = End("09", 0.00135, 0.0, 90.0);
        var south = End("09", 0.0, 0.0, 90.0);       // same name, different pavement
        var northLoc = Cand("INOR", 0.00135, 0.0323, 90.0);
        OrphanIlsMatcher.RunwayEnd[] ends = [north, south];

        Assert.Equal("INOR", OrphanIlsMatcher.SelectBest(ends, 0, [northLoc])?.Ident);
        Assert.Null(OrphanIlsMatcher.SelectBest(ends, 1, [northLoc]));
    }

    // A runway with no candidate at all returns "no match", which the caller renders as
    // "no ILS" — the honest degradation.
    [Fact]
    public void No_candidates_returns_no_match()
    {
        var end = End("09", 0.0, 0.0, 90.0);
        Assert.Null(OrphanIlsMatcher.SelectBest([end], 0, []));
    }

    // A runway name the airport does not have resolves to index -1, which must return no
    // match rather than throw — the provider hands the FindIndex result straight through.
    [Fact]
    public void Target_index_out_of_range_returns_no_match()
    {
        var end = End("09", 0.0, 0.0, 90.0);
        var loc = Cand("XXX", 0.0, 0.0323, 90.0);
        Assert.Null(OrphanIlsMatcher.SelectBest([end], -1, [loc]));
        Assert.Null(OrphanIlsMatcher.SelectBest([end], 5, [loc]));
    }

    // An absurdly far off-centerline antenna is refused outright even when it is the only
    // candidate and nothing competes for it (VVLO's was 9,971 m off the runway it would
    // otherwise have been given).
    [Fact]
    public void Candidate_far_off_the_centerline_is_rejected_even_when_uncontested()
    {
        var end = End("09", 0.0, 0.0, 90.0);
        var wayOff = Cand("XXX", 0.09, 0.0323, 90.0);   // ~10 km north of the centerline
        Assert.Null(OrphanIlsMatcher.SelectBest([end], 0, [wayOff]));
    }

    // Longitude degrees shrink with latitude; the cross-track metres conversion must
    // account for it or every high-latitude airport is mis-scaled. ENSB (Svalbard, 78°N)
    // is the extreme case in this database.
    [Fact]
    public void Cross_track_accounts_for_longitude_convergence()
    {
        // 0.01° of longitude at 78°N is ~231 m, not the ~1113 m it spans at the equator.
        var atPole = End("18", 78.0, 0.0, 180.0);
        double m = OrphanIlsMatcher.CrossTrackMetres(atPole, 78.0, 0.01);
        Assert.InRange(m, 200.0, 260.0);
    }
}

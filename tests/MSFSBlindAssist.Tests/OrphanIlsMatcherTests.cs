// Characterization tests for MSFSBlindAssist.Database.OrphanIlsMatcher — the geometric
// rule that re-links an ORPHANED ils row (one whose loc_airport_ident / loc_runway_name
// join columns navdatareader left NULL) to the runway end it actually serves.
//
// The fixture is REAL DATA, read out of a live fs2024.sqlite built 2026-08-30: all ten
// KATL runway ends and all ten orphaned KATL ils rows, at full stored precision. KATL is
// the motivating case because it is five PARALLEL runways all on ~090/270, which is
// exactly the geometry the predecessor rule (nearest antenna to the threshold) cannot
// resolve — see KATL_08L_takes_its_own_localizer_not_the_parallel_runways.
//
// Ground truth for each ils row is its `name` column ("ILS/GS CAT III RW08L"), which the
// matcher itself never reads — it is a label, not a join key, and several airports carry
// a stale one (ENSB's runway ends are name-swapped relative to their headings; FNLF/VAFA
// were renamed for magnetic drift). It is trustworthy at KATL, where all ten agree with
// the published plates.

using MSFSBlindAssist.Database;

namespace MSFSBlindAssist.Tests;

public class OrphanIlsMatcherTests
{
    private static OrphanIlsMatcher.RunwayEnd End(string name, double lat, double lon, double hdg)
        => new(name, lat, lon, hdg);

    private static OrphanIlsMatcher.IlsCandidate Cand(string ident, double lat, double lon, double locHdg)
        => new(ident, lat, lon, locHdg);

    // Every KATL runway end, exactly as stored (runway_end.name / laty / lonx / heading).
    private static readonly OrphanIlsMatcher.RunwayEnd[] KatlEnds =
    [
        End("08L", 33.649532318115234, -84.43907165527344, 89.98899841308594),
        End("08R", 33.646785736083984, -84.43846130371094, 89.9749984741211),
        End("09L", 33.63473129272461, -84.44800567626953, 90.00199890136719),
        End("09R", 33.631839752197266, -84.44805145263672, 89.97699737548828),
        End("10", 33.620277404785156, -84.4479751586914, 89.97500610351562),
        End("26L", 33.64679718017578, -84.40548706054688, 269.9750061035156),
        End("26R", 33.6495361328125, -84.40937805175781, 269.989013671875),
        End("27L", 33.63185119628906, -84.4183578491211, 269.97698974609375),
        End("27R", 33.634727478027344, -84.40718841552734, 270.00201416015625),
        End("28", 33.62028884887695, -84.41829681396484, 269.9750061035156),
    ];

    // Every orphaned ils row inside the KATL bounding box, exactly as stored.
    private static readonly OrphanIlsMatcher.IlsCandidate[] KatlCandidates =
    [
        Cand("IAFA", 33.63470458984375, -84.45140075683594, 270.01483154296875),   // RW27R 111.30
        Cand("IATL", 33.646793365478516, -84.40199279785156, 89.97297668457031),   // RW08R 109.90
        Cand("IBRU", 33.646785736083984, -84.44181823730469, 269.9911804199219),   // RW26L 108.70
        Cand("IFSQ", 33.631813049316406, -84.45093536376953, 269.9896240234375),   // RW27L 108.50
        Cand("IFUN", 33.63182067871094, -84.41452026367188, 89.97322845458984),    // RW09R 108.90
        Cand("IGXZ", 33.649532318115234, -84.44181060791016, 269.9896240234375),   // RW26R 110.10
        Cand("IHFW", 33.64954376220703, -84.40640258789062, 89.97322082519531),    // RW08L 109.30
        Cand("IHZK", 33.634700775146484, -84.40518188476562, 89.99320220947266),   // RW09L 110.50
        Cand("IOMO", 33.62028884887695, -84.4148941040039, 89.9599609375),         // RW10  111.55
        Cand("IPKU", 33.620269775390625, -84.45156860351562, 269.976318359375),    // RW28  111.75
    ];

    private static string? SelectAtKatl(string runwayName)
    {
        var target = KatlEnds.Single(e => e.Name == runwayName);
        int i = OrphanIlsMatcher.SelectBest(target, KatlCandidates, KatlEnds);
        return i < 0 ? null : KatlCandidates[i].Ident;
    }

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

    // The same near-tie mis-picked three more east-facing ends (08R and 09L also took
    // IFUN) and both of 27L/27R took IBRU — the pilot only noticed the pair they compared.
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
        double own = OrphanIlsMatcher.CrossTrackMetres(end08L, KatlCandidates.Single(c => c.Ident == "IHFW"));
        double parallel = OrphanIlsMatcher.CrossTrackMetres(end08L, KatlCandidates.Single(c => c.Ident == "IFUN"));

        Assert.True(own < 5.0, $"own localizer should be on the centerline, was {own:F1} m");
        Assert.True(parallel > 1000.0, $"parallel runway's localizer should be far off, was {parallel:F1} m");
    }

    // A localizer serving this runway lies BEYOND the far end, i.e. ahead of the threshold
    // along the runway heading. Anything behind the threshold belongs to something else.
    [Fact]
    public void Candidate_behind_the_threshold_is_rejected()
    {
        var end = End("09", 0.0, 0.0, 90.0);
        // 3 km due WEST of the threshold, on centerline, correct heading — but behind.
        var behind = Cand("XXX", 0.0, -0.0323, 90.0);
        Assert.Equal(-1, OrphanIlsMatcher.SelectBest(end, [behind], [end]));
    }

    // Heading gates the reciprocal end's localizer out.
    [Fact]
    public void Candidate_on_the_reciprocal_heading_is_rejected()
    {
        var end = End("09", 0.0, 0.0, 90.0);
        var reciprocal = Cand("XXX", 0.0, 0.0323, 270.0);
        Assert.Equal(-1, OrphanIlsMatcher.SelectBest(end, [reciprocal], [end]));
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

        Assert.Equal(0, OrphanIlsMatcher.SelectBest(north, [northLoc], [north, south]));
        Assert.Equal(-1, OrphanIlsMatcher.SelectBest(south, [northLoc], [north, south]));
    }

    // A runway with no candidate at all returns "no match", which the caller renders as
    // "no ILS" — the honest degradation.
    [Fact]
    public void No_candidates_returns_no_match()
    {
        var end = End("09", 0.0, 0.0, 90.0);
        Assert.Equal(-1, OrphanIlsMatcher.SelectBest(end, [], [end]));
    }

    // An absurdly far off-centerline antenna is refused outright even when it is the only
    // candidate and nothing competes for it (VVLO's was 9,971 m off the runway it would
    // otherwise have been given).
    [Fact]
    public void Candidate_far_off_the_centerline_is_rejected_even_when_uncontested()
    {
        var end = End("09", 0.0, 0.0, 90.0);
        var wayOff = Cand("XXX", 0.09, 0.0323, 90.0);   // ~10 km north of the centerline
        Assert.Equal(-1, OrphanIlsMatcher.SelectBest(end, [wayOff], [end]));
    }

    // Longitude degrees shrink with latitude; the cross-track metres conversion must
    // account for it or every high-latitude airport is mis-scaled. ENSB (Svalbard, 78°N)
    // is the extreme case in this database.
    [Fact]
    public void Cross_track_accounts_for_longitude_convergence()
    {
        // 0.01° of longitude at 78°N is ~231 m, not the ~1113 m it spans at the equator.
        var atPole = End("18", 78.0, 0.0, 180.0);
        double m = OrphanIlsMatcher.CrossTrackMetres(atPole, Cand("X", 78.0, 0.01, 180.0));
        Assert.InRange(m, 200.0, 260.0);
    }
}

// Characterization tests for RouteRunwayCrossings.EdgeCrossesRunway — the ONE owner of
// "does this taxi edge cross this runway?".
//
// Regression pinned: OMDB, 2026-09 (the crossing half of the 12R/12L reports).
//   Three safety tests asked that question against `RunwayCenterline.Lat1..Lon2` — the
//   navdata `start` rows — while the runway's actual pavement is `Pavement1..2` (the runway
//   table). `SnapStartToRunwayCenterline` repairs a start row only LATERALLY, so at a
//   displaced threshold it sits far inside the pavement and the band between them is REAL
//   RUNWAY that no crossing test could see:
//
//     OMDB 12R/30L : start-row line 2,779 m vs pavement 3,724 m
//                    -> 761 m invisible at the 12R end, 184 m at the 30L end
//                    -> taxiways K5, K6, K7, M8, K16 cross in the blind band
//     OMDB 12L/30R : start-row line 3,450 m vs pavement 4,050 m
//                    -> 496 m at the 12L end, 103 m at the 30R end
//                    -> taxiways M1B, N1, N1A, N1C, N9 cross in the blind band
//
//   Consequences, all three from the one frame:
//     * the auto runway-crossing hold-short pass logged `crosses=(none)` for routes that
//       cross there, so no hold-short was placed and nothing was announced — the thing
//       CLAUDE.md says must never be disabled, silently disabled by geometry;
//     * an explicit "hold short of runway 12R at K6" pick was refused with "route does not
//       cross it after taxiway K6" — while TaxiAssistForm's own picker OFFERED K6, because
//       `GetTaxiwaysCrossingRunway` builds its list from `RunwayFrame.For(runway, …)`, i.e.
//       the RUNWAY TABLE. The picker and the validator disagreed about where the runway is;
//     * the landing-exit handoff guard could not see a re-crossing there either.
//
// The fix points all three at the pavement frame, which is what the picker, the reach walk
// and the entrance picker already use. Strictly wider: the pavement line CONTAINS the
// start-row line, so nothing that was detected stops being detected.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayCrossingFrameTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    /// <summary>
    /// Runway 09/27 on the equator. Pavement runs 0 → 3,724 m east (OMDB 12R's length);
    /// the start rows sit 761 m in at the 09 end and 184 m in at the 27 end — OMDB 12R/30L's
    /// own measured offsets.
    /// </summary>
    private static TaxiGraph.RunwayCenterline Omdb12RShape() => new()
    {
        Name1 = "09", Name2 = "27",
        Lat1 = 0.0, Lon1 = 761.0 * DEG_PER_M,
        Lat2 = 0.0, Lon2 = (3724.0 - 184.0) * DEG_PER_M,
        HalfWidthMeters = 22.9,
        PavementLat1 = 0.0, PavementLon1 = 0.0,
        PavementLat2 = 0.0, PavementLon2 = 3724.0 * DEG_PER_M,
        PavementHalfWidthMeters = 30.0,
    };

    /// <summary>A north-south edge crossing the runway at <paramref name="metresAlong"/>.</summary>
    private static bool Crosses(TaxiGraph.RunwayCenterline rwy, double metresAlong) =>
        RouteRunwayCrossings.EdgeCrossesRunway(
            -40.0 * DEG_PER_M, metresAlong * DEG_PER_M,
            +40.0 * DEG_PER_M, metresAlong * DEG_PER_M,
            rwy);

    [Fact]
    public void A_taxiway_crossing_inside_the_displaced_threshold_band_is_a_crossing()
    {
        // OMDB's K6/K7/M8 shape: real runway pavement, 300 m in from the paint, but 461 m
        // short of where the start row claims the runway begins.
        Assert.True(Crosses(Omdb12RShape(), 300.0));
    }

    [Fact]
    public void The_far_end_band_counts_too()
    {
        // 184 m of pavement sat beyond the 27-end start row — the 30L end at OMDB.
        Assert.True(Crosses(Omdb12RShape(), 3724.0 - 100.0));
    }

    [Fact]
    public void A_crossing_the_old_frame_already_saw_is_unchanged()
    {
        // The pavement line CONTAINS the start-row line, so this can only ever add
        // detections. Mid-runway is inside both.
        Assert.True(Crosses(Omdb12RShape(), 1800.0));
    }

    [Fact]
    public void Pavement_beyond_the_threshold_is_still_not_a_crossing()
    {
        // Off the end of the runway entirely: a taxiway crossing the centreline EXTENDED is
        // not crossing the runway, and must not gain a hold-short.
        Assert.False(Crosses(Omdb12RShape(), -150.0));
        Assert.False(Crosses(Omdb12RShape(), 3724.0 + 150.0));
    }

    [Fact]
    public void An_edge_that_only_touches_one_side_is_not_a_crossing()
    {
        // Both endpoints north of the runway — a taxiway running alongside it.
        Assert.False(RouteRunwayCrossings.EdgeCrossesRunway(
            40.0 * DEG_PER_M, 1000.0 * DEG_PER_M,
            60.0 * DEG_PER_M, 1400.0 * DEG_PER_M,
            Omdb12RShape()));
    }

    [Fact]
    public void A_centerline_with_no_runway_table_behaves_exactly_as_before()
    {
        // Build falls back to the start-row values for Pavement* when it was given no
        // runway table (tests, probes), so those callers must be byte-identical.
        var rwy = new TaxiGraph.RunwayCenterline
        {
            Name1 = "09", Name2 = "27",
            Lat1 = 0.0, Lon1 = 0.0,
            Lat2 = 0.0, Lon2 = 3000.0 * DEG_PER_M,
            HalfWidthMeters = 22.9,
            PavementLat1 = 0.0, PavementLon1 = 0.0,
            PavementLat2 = 0.0, PavementLon2 = 3000.0 * DEG_PER_M,
            PavementHalfWidthMeters = 22.9,
        };

        Assert.True(Crosses(rwy, 1500.0));
        Assert.False(Crosses(rwy, -200.0));
    }

    [Fact]
    public void A_centerline_with_UNSET_pavement_falls_back_to_the_start_rows()
    {
        // Build always fills Pavement*, but a RunwayCenterline constructed directly (tests,
        // tools/ probes) leaves them at (0,0) — a degenerate line at null island that crosses
        // nothing. Silently answering "no crossing" is the UNSAFE direction here: no crossing
        // found means no hold-short placed. RolloutRunwayReCrossingTests builds exactly this
        // shape from real KATL geometry.
        var startRowsOnly = new TaxiGraph.RunwayCenterline
        {
            Lat1 = 0.0, Lon1 = 0.0, Name1 = "09",
            Lat2 = 0.0, Lon2 = 3000.0 * DEG_PER_M, Name2 = "27",
            HalfWidthMeters = 22.9,
            // Pavement* deliberately left at their defaults.
        };

        Assert.True(Crosses(startRowsOnly, 1500.0));
        Assert.False(Crosses(startRowsOnly, -200.0));
    }

    [Fact]
    public void A_null_runway_is_not_a_crossing()
    {
        Assert.False(RouteRunwayCrossings.EdgeCrossesRunway(
            -40.0 * DEG_PER_M, 0.0, 40.0 * DEG_PER_M, 0.0, null!));
    }
}

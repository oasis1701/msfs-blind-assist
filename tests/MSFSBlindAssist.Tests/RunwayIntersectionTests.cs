// Characterization tests for TaxiGraph.GetRunwayIntersections (and, through it,
// the private ProjectOntoCenterline helper) — the intersection-departure
// enumeration added for the Taxi Assist "Intersection departure" picker.
//
// The fixture is a synthetic east-west runway on the equator (lat 0), where
// the code's shared equirectangular constant (111132 m per degree, cos(0) = 1
// for longitude) makes expected along/remaining values exactly computable:
// metres = degrees-of-longitude x 111132. Each taxiway is one or two TaxiPath
// rows ending at (or near) the centerline, mirroring how navdata models
// runway entrances; TaxiGraph.Build indexes their endpoint nodes by name.
//
// Pinned behaviors (see GetRunwayIntersections' doc comment):
//   - only nodes within halfWidth+5 m of the centerline qualify (parallels out)
//   - along < 15 m (threshold connector) and remaining < 45 m (far-end nub)
//     are rejected PER NODE, before the per-taxiway best-node pick, so an
//     invalid connector node can't shadow a genuine mid-field entrance
//   - along-track is unclamped, so nodes beyond either threshold are rejected
//   - one entry per MEETING POINT: qualifying nodes on the same taxiway are
//     clustered by along-track gap (>100 m starts a new cluster); each
//     cluster contributes its node closest to the centerline
//   - the reported point is the node projected ONTO the centerline
//   - results sort threshold-first; direction follows the thr->far arguments
//
// This is characterization, not spec verification: values are derived by
// reasoning about the source and confirmed by running the tests; if a literal
// ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayIntersectionTests
{
    // Same constant as TaxiGraph.FastDistanceMeters / ProjectOntoCenterline.
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    // Runway 09/27: thresholds at (0, 0) and (0, 0.027) — 3000.564 m long.
    private const double FarLon = 0.027;
    private const double RunwayLenM = FarLon * M_PER_DEG;

    private const double HalfWidthM = 30.0; // maxPerp becomes 35 m (+5 slop)

    // Builds the fixture airport. Off-runway stub ends sit at lat 0.0006
    // (~66.7 m north — outside the 35 m band) so only the deliberate
    // on/near-centerline endpoint of each taxiway can qualify.
    //   M — mid-field crossing, node exactly on the centerline at 1000.2 m
    //   N — entrance whose node sits 10 m OFF the centerline at 2000.4 m
    //   Q — near-threshold connector (5 m along, invalid) PLUS a genuine
    //       mid-field entrance (2500.5 m) under the same name
    //   T — threshold connector only (10 m along) — never qualifies
    //   E — far-end stub, 20 m of runway remaining (invalid for 09,
    //       but a valid 20 m-along entrance when departing 27)
    //   Z — node beyond the far threshold (unclamped along > length)
    //   P — full-length parallel taxiway 200 m north
    private static TaxiGraph BuildFixtureGraph()
    {
        const double stubLat = 0.0006;            // ~66.7 m perp: outside the band
        const double nOffLat = 10.0 * DEG_PER_M;  // N's near node: 10 m perp
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = stubLat, StartLon = 0.009,   EndLat = 0,       EndLon = 0.009,   Name = "M" },
            new TaxiPath { StartLat = stubLat, StartLon = 0.018,   EndLat = nOffLat, EndLon = 0.018,   Name = "N" },
            new TaxiPath { StartLat = stubLat, StartLon = 0.000045, EndLat = 0,      EndLon = 0.000045, Name = "Q" },
            new TaxiPath { StartLat = stubLat, StartLon = 0.0225,  EndLat = 0,       EndLon = 0.0225,  Name = "Q" },
            new TaxiPath { StartLat = stubLat, StartLon = 0.00009, EndLat = 0,       EndLon = 0.00009, Name = "T" },
            new TaxiPath { StartLat = stubLat, StartLon = 0.02682, EndLat = 0,       EndLon = 0.02682, Name = "E" },
            new TaxiPath { StartLat = stubLat, StartLon = 0.0272,  EndLat = 0,       EndLon = 0.0272,  Name = "Z" },
            new TaxiPath { StartLat = 0.0018,  StartLon = 0.0,     EndLat = 0.0018,  EndLon = 0.027,   Name = "P" },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static List<TaxiGraph.RunwayIntersection> Departing09(TaxiGraph g) =>
        g.GetRunwayIntersections(0, 0, 0, FarLon, HalfWidthM);

    private static List<TaxiGraph.RunwayIntersection> Departing27(TaxiGraph g) =>
        g.GetRunwayIntersections(0, FarLon, 0, 0, HalfWidthM);

    // --- Inclusion/exclusion + ordering -----------------------------------------

    [Fact]
    public void Departing09_offers_exactly_the_valid_entrances_threshold_first()
    {
        var result = Departing09(BuildFixtureGraph());

        // T (connector only), E (20 m remaining), Z (beyond far end) and
        // P (parallel) are all excluded; M, N, Q survive, sorted by along.
        Assert.Equal(new[] { "M", "N", "Q" }, result.Select(ix => ix.TaxiwayName).ToArray());
    }

    [Fact]
    public void Along_and_remaining_are_measured_from_the_departure_threshold()
    {
        var result = Departing09(BuildFixtureGraph());

        var m = result.Single(ix => ix.TaxiwayName == "M");
        Assert.Equal(0.009 * M_PER_DEG, m.AlongMetersFromThreshold, 1);
        Assert.Equal(RunwayLenM - 0.009 * M_PER_DEG, m.RemainingMeters, 1);

        var q = result.Single(ix => ix.TaxiwayName == "Q");
        Assert.Equal(0.0225 * M_PER_DEG, q.AlongMetersFromThreshold, 1);
        Assert.Equal(RunwayLenM - 0.0225 * M_PER_DEG, q.RemainingMeters, 1);
    }

    [Fact]
    public void Departing_the_reciprocal_runway_reverses_the_frame()
    {
        var result = Departing27(BuildFixtureGraph());

        // From the 27 threshold: E's stub (20 m along) is now a valid entrance,
        // Q's connector (5 m of runway left) and T (10 m left) are far-end nubs.
        Assert.Equal(new[] { "E", "Q", "N", "M" }, result.Select(ix => ix.TaxiwayName).ToArray());

        var e = result.Single(ix => ix.TaxiwayName == "E");
        Assert.Equal(RunwayLenM - 0.02682 * M_PER_DEG, e.AlongMetersFromThreshold, 1);

        // Q must report the MID-FIELD entrance, not the (rejected) connector.
        var q = result.Single(ix => ix.TaxiwayName == "Q");
        Assert.Equal(RunwayLenM - 0.0225 * M_PER_DEG, q.AlongMetersFromThreshold, 1);
    }

    [Fact]
    public void An_invalid_connector_node_does_not_shadow_a_valid_midfield_entrance()
    {
        // Q's connector node (5 m along) sits exactly ON the centerline — if the
        // per-node gates ran AFTER the closest-node pick, it would win the pick,
        // then be rejected, dropping taxiway Q entirely. Q surviving with the
        // mid-field along value pins the gate-before-pick ordering.
        var result = Departing09(BuildFixtureGraph());

        var q = result.Single(ix => ix.TaxiwayName == "Q");
        Assert.True(q.AlongMetersFromThreshold > 100.0);
    }

    // --- Geometry of the reported point ------------------------------------------

    [Fact]
    public void Reported_point_is_the_node_projected_onto_the_centerline()
    {
        var result = Departing09(BuildFixtureGraph());

        // N's qualifying node sits 10 m north of the centerline; the intersection
        // point must be its foot-of-perpendicular ON the centerline (lat 0 here),
        // not the raw node position.
        var n = result.Single(ix => ix.TaxiwayName == "N");
        Assert.Equal(0.0, n.Latitude, 9);
        Assert.Equal(0.018, n.Longitude, 9);

        // NodeId still identifies the real graph node (for routing).
        var g = BuildFixtureGraph();
        var node = g.Nodes[Departing09(g).Single(ix => ix.TaxiwayName == "N").NodeId];
        Assert.Contains("N", node.TaxiwayNames);
        Assert.NotEqual(0.0, node.Latitude);
    }

    [Fact]
    public void Closest_node_to_the_centerline_wins_within_a_meeting_point()
    {
        // Taxiway R has two qualifying nodes 66.7 m apart along the runway —
        // one dense polyline entrance, i.e. ONE meeting point (cluster): a node
        // 20 m off the centerline at 1200 m along, and a node 2 m off at
        // 1266.7 m along. One entry, and it is the 2 m node (minimum
        // perpendicular distance), not the first-along one.
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0006, StartLon = 0.0108, EndLat = 20.0 * DEG_PER_M, EndLon = 0.0108, Name = "R" },
            new TaxiPath { StartLat = 0.0006, StartLon = 0.0114, EndLat = 2.0 * DEG_PER_M,  EndLon = 0.0114, Name = "R" },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var result = Departing09(g);

        var r = Assert.Single(result);
        Assert.Equal("R", r.TaxiwayName);
        Assert.Equal(0.0114 * M_PER_DEG, r.AlongMetersFromThreshold, 1);
    }

    [Fact]
    public void A_taxiway_meeting_the_runway_twice_yields_one_entry_per_meeting_point()
    {
        // Taxiway V reaches the pavement at 1000 m and again at 1300 m — the
        // paired high-speed-exit shape sharing one name (real navdata: KORD Y4
        // meets 04R/22L at 1361 m AND 1684 m). The 300 m along-track gap
        // (> CLUSTER_GAP_M 100) makes these two distinct meeting points, each
        // offered as its own entry, threshold-first.
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0006, StartLon = 0.009,  EndLat = 0, EndLon = 0.009,  Name = "V" },
            new TaxiPath { StartLat = 0.0006, StartLon = 0.0117, EndLat = 0, EndLon = 0.0117, Name = "V" },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var result = Departing09(g);

        Assert.Equal(2, result.Count);
        Assert.All(result, ix => Assert.Equal("V", ix.TaxiwayName));
        Assert.Equal(0.009 * M_PER_DEG,  result[0].AlongMetersFromThreshold, 1);
        Assert.Equal(0.0117 * M_PER_DEG, result[1].AlongMetersFromThreshold, 1);
    }

    [Fact]
    public void Half_width_band_includes_the_5m_navdata_slop()
    {
        // One node 33 m off the centerline (inside halfWidth 30 + 5 slop), one
        // 40 m off (outside). Only the first taxiway is offered.
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0006, StartLon = 0.009, EndLat = 33.0 * DEG_PER_M, EndLon = 0.009, Name = "IN" },
            new TaxiPath { StartLat = 0.0006, StartLon = 0.018, EndLat = 40.0 * DEG_PER_M, EndLon = 0.018, Name = "OUT" },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var result = Departing09(g);

        var only = Assert.Single(result);
        Assert.Equal("IN", only.TaxiwayName);
    }

    // --- Full-length entrance filter (displaced thresholds) -----------------------

    // Shared fixture for the lineup-point filter: FL serves the full-length
    // lineup point at 300 m down the physical pavement (displaced-threshold
    // shape, e.g. KJFK 22R's ~1 km displacement), NEAR sits 33 m past it
    // (inside the 50 m margin), MID is a genuine mid-field shortcut.
    private static TaxiGraph BuildDisplacedGraph()
    {
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0.0006, StartLon = 0.0027, EndLat = 0, EndLon = 0.0027, Name = "FL" },
            new TaxiPath { StartLat = 0.0006, StartLon = 0.003,  EndLat = 0, EndLon = 0.003,  Name = "NEAR" },
            new TaxiPath { StartLat = 0.0006, StartLon = 0.009,  EndLat = 0, EndLon = 0.009,  Name = "MID" },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    [Fact]
    public void Lineup_point_filters_the_full_length_entrance_and_its_margin()
    {
        // Lineup point at 300 m along: FL (300 m) and NEAR (333 m ≤ 300+50)
        // are the normal full-length entrance, not shortcuts; MID survives.
        var result = BuildDisplacedGraph().GetRunwayIntersections(
            0, 0, 0, FarLon, HalfWidthM, lineupLat: 0.0, lineupLon: 0.0027);

        var only = Assert.Single(result);
        Assert.Equal("MID", only.TaxiwayName);
    }

    [Fact]
    public void Omitted_lineup_point_keeps_all_entries()
    {
        var result = Departing09(BuildDisplacedGraph());

        Assert.Equal(new[] { "FL", "NEAR", "MID" },
            result.Select(ix => ix.TaxiwayName).ToArray());
    }

    [Fact]
    public void Lineup_point_beyond_mid_runway_is_treated_as_corrupt_and_ignored()
    {
        // A start row projecting past the runway midpoint (2000 m of 3000 m)
        // can't be a real full-length lineup point — the filter must switch
        // off rather than empty the list.
        var result = BuildDisplacedGraph().GetRunwayIntersections(
            0, 0, 0, FarLon, HalfWidthM, lineupLat: 0.0, lineupLon: 0.018);

        Assert.Equal(3, result.Count);
    }

    // --- Degenerate input ---------------------------------------------------------

    [Fact]
    public void Zero_length_runway_returns_empty()
    {
        var g = BuildFixtureGraph();
        Assert.Empty(g.GetRunwayIntersections(0, 0.009, 0, 0.009, HalfWidthM));
    }
}

// Characterization tests for TaxiGraph.FindBacktrackEntryNode — the geometric,
// NAME-INDEPENDENT entrance finder used by the Taxi Assist "Full-length departure
// (requires backtrack)" option. It picks the taxiway->runway entrance the pilot
// enters to backtrack toward the departure threshold, at airports with no
// full-length parallel taxiway (e.g. iniBuilds EGNM, whose taxi_path segments are
// unnamed — which is exactly why this cannot rely on GetRunwayIntersections).
//
// Fixture idiom (shared with RunwayIntersectionTests): a synthetic east-west
// runway on the equator (thresholds at (0,0) and (0,0.027)), where the code's
// equirectangular constant (111132 m/deg, cos(0)=1) makes along-track distances
// exactly metres = degrees-of-longitude x 111132.
//
// Pinned behaviors (see FindBacktrackEntryNode's doc comment):
//   - among valid entrances, the one CLOSEST to the departure threshold wins
//     (least backtrack) — and "closest" is measured from the thr argument, so the
//     reciprocal runway direction picks the entrance near the other end
//   - only nodes within halfWidth+5 m of the centerline, past the 40 m threshold
//     connector, and short of the far-end nub qualify
//   - a node must have a graph neighbour OFF the runway (a real junction), so a
//     bare on-runway chain yields no entrance
//   - only entrances reachable from the aircraft (same connected component) are
//     eligible — an isolated island entrance is skipped even if it is closer
//
// Characterization, not spec: if a literal ever disagrees with real output, fix
// the test to match the output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class BacktrackEntryTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    private const double FarLon = 0.027;
    private const double HalfWidthM = 30.0;   // maxPerp = 35 m
    private const double OffLat = 0.0006;     // ~66.7 m north — off the runway

    // A backtrack airport: an off-runway spine (north, one connected component)
    // links three runway entrances at 667 m / 1334 m / 2000 m from the 09
    // threshold. Plus an ISOLATED entrance at 333 m (its stub connects to nothing)
    // in its own component — closer to the 09 threshold but unreachable.
    private static TaxiGraph BuildBacktrackGraph()
    {
        var paths = new List<TaxiPath>
        {
            // Spine segments (share the junction nodes at 0.006 / 0.012 / 0.018).
            new TaxiPath { StartLat = OffLat, StartLon = 0.006, EndLat = OffLat, EndLon = 0.012 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.012, EndLat = OffLat, EndLon = 0.018 },
            // Connectors from the spine down onto the runway centerline.
            new TaxiPath { StartLat = OffLat, StartLon = 0.006, EndLat = 0, EndLon = 0.006 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.012, EndLat = 0, EndLon = 0.012 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.018, EndLat = 0, EndLon = 0.018 },
            // Isolated entrance at 333 m — its own component (connects to nothing).
            new TaxiPath { StartLat = OffLat, StartLon = 0.003, EndLat = 0, EndLon = 0.003 },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    // Aircraft parked on the spine, near its 0.006 end.
    private const double AcLat = OffLat;
    private const double AcLon = 0.006;

    [Fact]
    public void Picks_the_reachable_entrance_closest_to_the_departure_threshold()
    {
        var g = BuildBacktrackGraph();

        // Departing 09 (thr = (0,0)): the closest reachable entrance is 0.006
        // (667 m). The 333 m entrance is closer but in another component (the
        // apron can't reach it), so it must be skipped.
        var entry = g.FindBacktrackEntryNode(
            thrLat: 0, thrLon: 0, farLat: 0, farLon: FarLon,
            halfWidthMeters: HalfWidthM, aircraftLat: AcLat, aircraftLon: AcLon);

        Assert.NotNull(entry);
        Assert.Equal(0.006, entry!.Longitude, 6);
        Assert.Equal(0.0, entry.Latitude, 6);
    }

    [Fact]
    public void Reciprocal_direction_picks_the_entrance_near_the_other_threshold()
    {
        var g = BuildBacktrackGraph();

        // Departing 27 (thr = (0,0.027)): "closest to the departure threshold" is
        // now the 0.018 entrance (1000 m from the 27 end), not 0.006.
        var entry = g.FindBacktrackEntryNode(
            thrLat: 0, thrLon: FarLon, farLat: 0, farLon: 0,
            halfWidthMeters: HalfWidthM, aircraftLat: AcLat, aircraftLon: AcLon);

        Assert.NotNull(entry);
        Assert.Equal(0.018, entry!.Longitude, 6);
    }

    [Fact]
    public void A_bare_on_runway_chain_with_no_off_runway_junction_yields_no_entrance()
    {
        // Runway pavement modelled as on-centerline segments, with NO taxiway
        // joining it. Every node's neighbours are on the runway, so none is a real
        // entrance — the finder must return null rather than steering onto a
        // mid-runway node no taxiway connects to.
        var paths = new List<TaxiPath>
        {
            new TaxiPath { StartLat = 0, StartLon = 0.006, EndLat = 0, EndLon = 0.012 },
            new TaxiPath { StartLat = 0, StartLon = 0.012, EndLat = 0, EndLon = 0.018 },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var entry = g.FindBacktrackEntryNode(
            thrLat: 0, thrLon: 0, farLat: 0, farLon: FarLon,
            halfWidthMeters: HalfWidthM, aircraftLat: 0, aircraftLon: 0.006);

        Assert.Null(entry);
    }

    [Fact]
    public void Threshold_connector_and_far_end_nub_do_not_qualify()
    {
        // TC: a junction only 20 m past the departure threshold (< 40 m) — too
        // close to require a backtrack. NUB: a junction 20 m short of the far end.
        // All three hang off one spine (so all are REACHABLE — this isolates the
        // along filter, not the component filter); only the genuine mid-field
        // entrance (MID) qualifies.
        var paths = new List<TaxiPath>
        {
            // Spine linking all three junctions into one component.
            new TaxiPath { StartLat = OffLat, StartLon = 0.00018, EndLat = OffLat, EndLon = 0.009 },
            new TaxiPath { StartLat = OffLat, StartLon = 0.009,   EndLat = OffLat, EndLon = 0.02682 },
            // Connectors down to the runway.
            new TaxiPath { StartLat = OffLat, StartLon = 0.00018, EndLat = 0, EndLon = 0.00018 }, // ~20 m along
            new TaxiPath { StartLat = OffLat, StartLon = 0.009,   EndLat = 0, EndLon = 0.009 },   // ~1000 m along
            new TaxiPath { StartLat = OffLat, StartLon = 0.02682, EndLat = 0, EndLon = 0.02682 }, // ~20 m remaining
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var entry = g.FindBacktrackEntryNode(
            thrLat: 0, thrLon: 0, farLat: 0, farLon: FarLon,
            halfWidthMeters: HalfWidthM, aircraftLat: OffLat, aircraftLon: 0.009);

        Assert.NotNull(entry);
        Assert.Equal(0.009, entry!.Longitude, 6);
    }

    [Fact]
    public void Zero_length_runway_returns_null()
    {
        var g = BuildBacktrackGraph();
        Assert.Null(g.FindBacktrackEntryNode(
            0, 0.006, 0, 0.006, HalfWidthM, AcLat, AcLon));
    }
}

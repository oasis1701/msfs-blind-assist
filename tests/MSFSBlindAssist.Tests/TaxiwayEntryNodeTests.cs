// Characterization tests for the FIRST-CLEARED-TAXIWAY ENTRY choice — the node the
// route is anchored on when the pilot names a taxiway. Two call sites share one rule
// (TaxiRouter.FindNearestNodesOnTaxiway): the router's step-1 candidate ranking, and
// TaxiGuidanceManager.LoadRoute's pre-snap via TaxiRouter.FindBestEntryNodeOnTaxiway.
//
// The rule is TOTAL ROUTE COST through the candidate — (start -> entry) + (entry ->
// destination) — not distance to the entry alone. Every node on the direct path ties
// at the minimum (the sum is just the through-route length), so the tie-break picks
// the first of them the aircraft reaches; a candidate that would force a there-and-back
// detour is ranked behind by exactly the detour it adds.
//
// Motivating defect (LOWS, 2026-08-16, progressive taxi "L" from the runway 15 vacate
// point): L's nearest node by BOTH Euclidean (64 m) and graph distance (80 m) was a
// 15 m dead-end stub off the south junction, while the north junction was 107 m. The
// route opened with a 15 m leg the wrong way and a 170 deg hairpin, and the pilot spent
// 55 s turning round in it.
//
// Fixture idiom (shared with BacktrackEntryTests): synthetic geometry on the equator,
// where the code's equirectangular constant (111132 m/deg, cos(0)=1) makes degrees of
// latitude AND longitude both exactly 111132 m.
//
// Characterization, not spec: if a literal ever disagrees with real output, fix the
// test to match the output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiwayEntryNodeTests
{
    private const double M_PER_DEG = 111132.0;
    private static double M(double metres) => metres / M_PER_DEG;

    private static TaxiPath Path(string name,
        double startNorthM, double startEastM, double endNorthM, double endEastM) =>
        new()
        {
            Type = "T",
            Name = name,
            Width = 75.0,
            StartLat = M(startNorthM), StartLon = M(startEastM),
            EndLat = M(endNorthM), EndLon = M(endEastM),
        };

    // The LOWS shape. Taxiway L runs north along east = 0:
    //     A (0 m)  --91 m--  C (91 m)  --909 m--  D (1000 m, the destination)
    // A carries a 15 m dead-end STUB east to S, and the stub is named L too (at LOWS
    // the whole junction area carried the name). The aircraft sits at X, off L to the
    // east, linked to BOTH ends by an unnamed connector:
    //     X -> S is 79.8 m,  X -> C is 107.8 m
    // so the stub is nearer, but entering there costs 79.8 + 15 + 91 = 185.8 m to reach
    // C against 107.8 m direct — a 78 m out-and-back.
    private const double XNorthM = 30.0;
    private const double XEastM = 88.9;

    private static TaxiGraph BuildStubJunctionGraph()
    {
        var paths = new List<TaxiPath>
        {
            // Taxiway L, south to north.
            Path("L", 0, 0, 91, 0),
            Path("L", 91, 0, 1000, 0),
            // The 15 m dead-end stub off L's south junction.
            Path("L", 0, 0, 0, 15),
            // Unnamed connectors from the aircraft's position to each end.
            Path("", XNorthM, XEastM, 0, 15),
            Path("", XNorthM, XEastM, 91, 0),
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());
    }

    private static int NodeAt(TaxiGraph g, double northM, double eastM)
    {
        foreach (var n in g.Nodes.Values)
        {
            if (Math.Abs(n.Latitude - M(northM)) < 1e-9 &&
                Math.Abs(n.Longitude - M(eastM)) < 1e-9)
                return n.NodeId;
        }
        throw new Xunit.Sdk.XunitException($"no node at ({northM} m N, {eastM} m E)");
    }

    [Fact]
    public void Entry_prefers_the_junction_over_a_nearer_stub_that_forces_a_reversal()
    {
        var g = BuildStubJunctionGraph();
        int aircraft = NodeAt(g, XNorthM, XEastM);
        int stubTip = NodeAt(g, 0, 15);
        int northJunction = NodeAt(g, 91, 0);
        int destination = NodeAt(g, 1000, 0);

        int entry = new TaxiRouter(g).FindBestEntryNodeOnTaxiway(aircraft, "L", destination);

        Assert.Equal(northJunction, entry);
        Assert.NotEqual(stubTip, entry);
    }

    [Fact]
    public void Constrained_route_onto_the_taxiway_does_not_enter_via_the_stub()
    {
        var g = BuildStubJunctionGraph();
        int aircraft = NodeAt(g, XNorthM, XEastM);
        int stubTip = NodeAt(g, 0, 15);
        int southJunction = NodeAt(g, 0, 0);
        int destination = NodeAt(g, 1000, 0);

        var route = new TaxiRouter(g)
            .FindConstrainedPath(aircraft, destination, new List<string> { "L" });

        Assert.NotNull(route);
        var visited = new HashSet<int>();
        foreach (var seg in route!.Segments)
        {
            visited.Add(seg.FromNode.NodeId);
            visited.Add(seg.ToNode.NodeId);
        }
        // Neither the stub tip nor the south junction is on the way: the route joins L
        // at the north junction and runs straight up it.
        Assert.DoesNotContain(stubTip, visited);
        Assert.DoesNotContain(southJunction, visited);
    }

    // A straight taxiway with the aircraft linked to its middle. Every node on the
    // through-path ties on total cost, so the tie-break must pick the one the aircraft
    // reaches FIRST — not the one nearest the destination (which ties) and not an
    // arbitrary one (which is what an exact floating-point comparison would give).
    [Fact]
    public void Entry_on_a_through_path_is_the_first_node_the_aircraft_reaches()
    {
        var paths = new List<TaxiPath>
        {
            Path("L", 0, 0, 100, 0),
            Path("L", 100, 0, 200, 0),
            Path("L", 200, 0, 300, 0),
            Path("", 100, 60, 100, 0),     // connector onto L's middle node
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        int aircraft = NodeAt(g, 100, 60);
        int joinNode = NodeAt(g, 100, 0);
        int destination = NodeAt(g, 300, 0);

        int entry = new TaxiRouter(g).FindBestEntryNodeOnTaxiway(aircraft, "L", destination);

        Assert.Equal(joinNode, entry);
    }

    // The EVRA rule this replaces must still hold: a dead-end that is marginally nearer
    // in a straight line loses to the junction the aircraft genuinely reaches first.
    // Here the west dead-end is 1 m closer to the aircraft as the crow flies but 200 m
    // further by road, AND costs a 200 m out-and-back on top.
    [Fact]
    public void Entry_still_rejects_a_euclidean_near_dead_end_reached_the_long_way()
    {
        var paths = new List<TaxiPath>
        {
            // Taxiway C runs east-west with its junction onto the connector in the middle.
            Path("C", 0, -200, 0, 0),
            Path("C", 0, 0, 0, 200),
            // Connector from the aircraft down onto the middle of C, then on to the gate.
            Path("", 300, 0, 0, 0),
            Path("", 0, 200, -100, 200),
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        int aircraft = NodeAt(g, 300, 0);
        int westDeadEnd = NodeAt(g, 0, -200);
        int junction = NodeAt(g, 0, 0);
        int destination = NodeAt(g, -100, 200);

        int entry = new TaxiRouter(g).FindBestEntryNodeOnTaxiway(aircraft, "C", destination);

        Assert.Equal(junction, entry);
        Assert.NotEqual(westDeadEnd, entry);
    }

    [Fact]
    public void Missing_nodes_or_unknown_taxiway_report_no_entry_rather_than_throwing()
    {
        var g = BuildStubJunctionGraph();
        int aircraft = NodeAt(g, XNorthM, XEastM);
        int destination = NodeAt(g, 1000, 0);
        var router = new TaxiRouter(g);

        Assert.Equal(-1, router.FindBestEntryNodeOnTaxiway(aircraft, "ZZ", destination));
        Assert.Equal(-1, router.FindBestEntryNodeOnTaxiway(aircraft, "", destination));
        Assert.Equal(-1, router.FindBestEntryNodeOnTaxiway(999999, "L", destination));
        Assert.Equal(-1, router.FindBestEntryNodeOnTaxiway(aircraft, "L", 999999));
    }
}

// Characterization tests for TaxiGraph.Build's orphan-parking-island bridge.
//
// Regression pinned: OMDB gate B 18R, 2026-09-04 (issue #228).
//   The fs2024 navdata models B 18R's stand as a two-node stub — the stand node and
//   its apron connector — whose open end stops 12 m short of the nearest taxiway-U
//   node instead of meeting it. Build's node merge is 1.5 m, so the stub stayed its
//   own connected component (2 nodes) while the rest of OMDB was one 3,632-node
//   component.
//
//   Every start-node selector in LoadRoute is filtered to the DESTINATION's component
//   (the GCLP S5 island defence). With B 18R as the destination that component held
//   exactly two nodes, both at the far side of the airport from an aircraft that had
//   just vacated 30L, so FindNearestNodeOnTaxiway / FindNearestNode /
//   FindNearestNodeInDirection all returned null and LoadRoute answered "Could not
//   find a nearby taxiway node." — with no route logged at all. The pilot could not
//   taxi to their assigned gate and had to teleport to it.
//
// The fix joins an island that contains a PARKING node to the main component with a
// single bridge edge when the two are within MAX_ORPHAN_PARKING_BRIDGE_M (50 m — the
// same "this is a data seam, not a real gap" distance TaxiRouter.AStarSearchStrict
// already relaxes bridge edges over). Islands without parking are left alone: that is
// the GCLP S5 shape the component filter exists to reject.
//
// Fixture geometry is on the equator (lat 0) so one degree of longitude and one of
// latitude are both 111,132 m — TaxiGraph's own equirectangular constant.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class OrphanParkingIslandBridgeTests
{
    private const double M_PER_DEG = 111132.0;
    private const double DEG_PER_M = 1.0 / M_PER_DEG;

    // A short main taxiway "U" running east along the equator: five nodes, 60 m apart.
    private static List<TaxiPath> MainTaxiway() =>
        Enumerable.Range(0, 4).Select(i => new TaxiPath
        {
            Name = "U",
            Type = "PT",
            Width = 75.0,
            StartLat = 0.0, StartLon = (i * 60.0) * DEG_PER_M,
            EndLat = 0.0,   EndLon = ((i + 1) * 60.0) * DEG_PER_M,
        }).ToList();

    /// <summary>
    /// A parking stand stub: connector node <paramref name="gapMeters"/> NORTH of the
    /// taxiway node at 120 m east, and the stand itself 39 m further north. Mirrors the
    /// OMDB B 18R shape (stand → connector → nothing).
    /// </summary>
    private static (List<TaxiPath> paths, List<ParkingSpot> parking) ParkingStub(double gapMeters)
    {
        double connectorLat = gapMeters * DEG_PER_M;
        double standLat = (gapMeters + 39.0) * DEG_PER_M;
        double lon = 120.0 * DEG_PER_M;

        var paths = new List<TaxiPath>
        {
            new TaxiPath
            {
                Name = "", Type = "P", Width = 60.0,
                StartLat = connectorLat, StartLon = lon,
                EndLat = standLat,       EndLon = lon,
            }
        };
        var parking = new List<ParkingSpot>
        {
            new ParkingSpot
            {
                Name = "B", Number = 18, Suffix = "R",
                Latitude = standLat, Longitude = lon,
            }
        };
        return (paths, parking);
    }

    private static TaxiGraph BuildWithStub(double gapMeters)
    {
        var (stub, parking) = ParkingStub(gapMeters);
        var paths = MainTaxiway();
        paths.AddRange(stub);
        return TaxiGraph.Build(paths, parking, new List<StartPosition>());
    }

    private static TaxiNode StandNode(TaxiGraph g) =>
        g.Nodes.Values.Single(n => n.Type == TaxiNodeType.Parking);

    private static TaxiNode TaxiwayNode(TaxiGraph g, double metresEast) =>
        g.Nodes.Values
            .Where(n => n.TaxiwayNames.Contains("U"))
            .OrderBy(n => Math.Abs(n.Longitude - metresEast * DEG_PER_M))
            .First();

    [Fact]
    public void Stand_stranded_12m_from_the_taxiway_is_reattached()
    {
        // The measured OMDB B 18R gap.
        var g = BuildWithStub(12.0);

        var stand = StandNode(g);
        var taxiway = TaxiwayNode(g, 120.0);

        Assert.Equal(taxiway.ComponentId, stand.ComponentId);
    }

    [Fact]
    public void The_bridge_lands_on_the_stubs_open_end_not_on_the_stand()
    {
        var g = BuildWithStub(12.0);

        var taxiway = TaxiwayNode(g, 120.0);
        var bridges = g.Adjacency[taxiway.NodeId]
                       .Where(e => g.Nodes[e.ToNodeId].ComponentId == taxiway.ComponentId
                                   && !g.Nodes[e.ToNodeId].TaxiwayNames.Contains("U"))
                       .ToList();

        var bridge = Assert.Single(bridges);
        var other = g.Nodes[bridge.ToNodeId];

        // The connector, not the stand — the stand is 39 m further out.
        Assert.NotEqual(TaxiNodeType.Parking, other.Type);
        Assert.InRange(bridge.DistanceMeters, 11.0, 13.0);

        // Bidirectional, like every other edge Build adds.
        Assert.Contains(g.Adjacency[other.NodeId], e => e.ToNodeId == taxiway.NodeId);
    }

    [Fact]
    public void The_bridged_island_is_a_dead_end_spur_not_a_shortcut()
    {
        var g = BuildWithStub(12.0);

        // Exactly one edge crosses from the former island into the taxiway network, so
        // no main-component route can ever be re-routed THROUGH the stand.
        var islandNodes = g.Nodes.Values.Where(n => !n.TaxiwayNames.Contains("U")).ToList();
        int crossings = islandNodes
            .SelectMany(n => g.Adjacency[n.NodeId])
            .Count(e => g.Nodes[e.ToNodeId].TaxiwayNames.Contains("U"));

        Assert.Equal(1, crossings);
    }

    [Fact]
    public void A_stand_genuinely_far_from_the_network_is_left_disconnected()
    {
        // 200 m of unmodelled ground is not a data seam — bridging it would steer the
        // pilot across whatever is actually there.
        var g = BuildWithStub(200.0);

        Assert.NotEqual(TaxiwayNode(g, 120.0).ComponentId, StandNode(g).ComponentId);
    }

    [Fact]
    public void A_taxiway_island_with_no_parking_is_left_disconnected()
    {
        // The GCLP S5 shape: a named taxiway modelled with no connection at either
        // terminus. The component filter exists to REJECT it, so the bridge must not
        // quietly undo that.
        var paths = MainTaxiway();
        paths.Add(new TaxiPath
        {
            Name = "S5", Type = "PT", Width = 75.0,
            StartLat = 12.0 * DEG_PER_M, StartLon = 120.0 * DEG_PER_M,
            EndLat = 60.0 * DEG_PER_M,   EndLon = 120.0 * DEG_PER_M,
        });

        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), new List<StartPosition>());

        var s5 = g.Nodes.Values.First(n => n.TaxiwayNames.Contains("S5"));
        Assert.NotEqual(TaxiwayNode(g, 120.0).ComponentId, s5.ComponentId);
    }

    // ENSB (Svalbard) latitude. One degree of longitude is ~23,000 m there, not 111,132,
    // so a search grid keyed on a uniform degree cell covers only 50 x cos(78 deg) = 10 m
    // east-west and never sees a candidate 30 m away. The stub below is offset EAST, which
    // is the direction that exposes it; the equator fixtures above are offset north and
    // pass either way.
    private const double ArcticLat = 78.246;

    [Fact]
    public void An_east_west_gap_is_measured_in_metres_at_arctic_latitudes()
    {
        double lonPerM = 1.0 / (M_PER_DEG * Math.Cos(ArcticLat * Math.PI / 180.0));

        var paths = Enumerable.Range(0, 4).Select(i => new TaxiPath
        {
            Name = "U", Type = "PT", Width = 75.0,
            StartLat = ArcticLat, StartLon = (i * 60.0) * lonPerM,
            EndLat = ArcticLat,   EndLon = ((i + 1) * 60.0) * lonPerM,
        }).ToList();

        // Stand stub 30 m EAST of the taxiway's last node, running further east.
        double connectorLon = (240.0 + 30.0) * lonPerM;
        double standLon = (240.0 + 69.0) * lonPerM;
        paths.Add(new TaxiPath
        {
            Name = "", Type = "P", Width = 60.0,
            StartLat = ArcticLat, StartLon = connectorLon,
            EndLat = ArcticLat,   EndLon = standLon,
        });
        var parking = new List<ParkingSpot>
        {
            new ParkingSpot { Name = "B", Number = 18, Suffix = "R",
                              Latitude = ArcticLat, Longitude = standLon }
        };

        var g = TaxiGraph.Build(paths, parking, new List<StartPosition>());

        var stand = StandNode(g);
        var taxiway = g.Nodes.Values.First(n => n.TaxiwayNames.Contains("U"));
        Assert.Equal(taxiway.ComponentId, stand.ComponentId);
    }

    [Fact]
    public void A_stand_already_on_the_network_gains_no_extra_edge()
    {
        // Zero-gap control: the stub meets the taxiway, so Build's 1.5 m node merge
        // already joins them and the bridge pass must be a no-op.
        var g = BuildWithStub(0.0);

        var taxiway = TaxiwayNode(g, 120.0);
        Assert.Equal(taxiway.ComponentId, StandNode(g).ComponentId);

        // taxiway node has 2 "U" neighbours plus the merged connector = 3, never 4.
        Assert.Equal(3, g.Adjacency[taxiway.NodeId].Count);
    }
}

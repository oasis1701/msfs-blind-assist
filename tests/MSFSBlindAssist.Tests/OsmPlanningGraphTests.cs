using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation.Briefing;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

// The OpenStreetMap PLANNING-ONLY graph: named ways become "T" paths, holding positions within 3 m of
// a way vertex become hold-short nodes, parking positions become typeless stands. Never fed to guidance.
public class OsmPlanningGraphTests
{
    private static double Lat(double n) => RunwayFixture.Lat(n);
    private static double Lon(double e) => RunwayFixture.Lon(e);

    private static AirportTaxiData Osm(params (string name, double e1, double n1, double e2, double n2)[] segs)
    {
        var d = new AirportTaxiData { Source = "osm" };
        foreach (var s in segs)
            d.Taxiways.Add(new NamedTaxiSegment { Name = s.name, Lat1 = Lat(s.n1), Lon1 = Lon(s.e1), Lat2 = Lat(s.n2), Lon2 = Lon(s.e2) });
        return d;
    }

    [Fact]
    public void Consecutive_way_segments_connect_into_one_taxiway()
    {
        var osm = Osm(("A", 0, 0, 100, 0), ("A", 100, 0, 200, 0));
        var bundle = OsmPlanningGraph.Build(new[] { osm }, new List<Runway>(), new List<StartPosition>(), null)!;

        Assert.Equal(BriefingTier.OpenStreetMap, bundle.Tier);
        Assert.Equal(3, bundle.Graph.Nodes.Count);
        var middle = bundle.Graph.Nodes.Values.Single(n => Math.Abs(n.Longitude - Lon(100)) < 1e-9);
        Assert.Equal(2, bundle.Graph.Adjacency[middle.NodeId].Count);
        Assert.Equal(OsmPlanningGraph.Note, bundle.Note);
    }

    [Fact]
    public void A_holding_position_on_a_vertex_becomes_a_hold_short_node()
    {
        var osm = Osm(("A", 0, 0, 100, 0), ("A", 100, 0, 200, 0));
        osm.HoldingPoints.Add(("A1", Lat(0), Lon(101.0), "runway"));   // 1 m from the vertex at 100 E
        osm.HoldingPoints.Add(("A2", Lat(0), Lon(200.0), "ILS"));
        var g = OsmPlanningGraph.Build(new[] { osm }, new List<Runway>(), new List<StartPosition>(), null)!.Graph;

        Assert.Equal(TaxiNodeType.HoldShort, g.Nodes.Values.Single(n => Math.Abs(n.Longitude - Lon(100)) < 1e-9).Type);
        Assert.Equal(TaxiNodeType.ILSHoldShort, g.Nodes.Values.Single(n => Math.Abs(n.Longitude - Lon(200)) < 1e-9).Type);
        Assert.Equal(TaxiNodeType.Normal, g.Nodes.Values.Single(n => Math.Abs(n.Longitude - Lon(0)) < 1e-9).Type);
    }

    [Fact]
    public void A_holding_position_further_than_3m_from_any_vertex_is_ignored()
    {
        Assert.Equal("N", OsmPlanningGraph.HoldTypeAt(Lat(0), Lon(0), new List<(string, double, double, string)> { ("X", Lat(0), Lon(5), "runway") }));
        Assert.Equal("HS", OsmPlanningGraph.HoldTypeAt(Lat(0), Lon(0), new List<(string, double, double, string)> { ("X", Lat(0), Lon(2), "") }));
        Assert.Equal("IHS", OsmPlanningGraph.HoldTypeAt(Lat(0), Lon(0), new List<(string, double, double, string)> { ("X", Lat(0), Lon(2), "ils") }));
    }

    [Fact]
    public void Parking_positions_become_typeless_stands_with_parsed_identity()
    {
        var b25 = OsmPlanningGraph.ToSpot(("B25", 1.0, 2.0));
        Assert.Equal("B", b25.Name);
        Assert.Equal(25, b25.Number);
        Assert.Equal(0, b25.Type);
        Assert.Equal(0.0, b25.Radius);
        Assert.Equal(1.0, b25.Latitude);

        var word = OsmPlanningGraph.ToSpot(("HAWKER", 1.0, 2.0));
        Assert.Equal("HAWKER", word.Name);
        Assert.Equal(0, word.Number);
    }

    [Fact]
    public void Unnamed_segments_are_skipped_and_no_taxiways_means_no_graph()
    {
        var empty = new AirportTaxiData { Source = "osm" };
        empty.Taxiways.Add(new NamedTaxiSegment { Name = "", Lat1 = Lat(0), Lon1 = Lon(0), Lat2 = Lat(0), Lon2 = Lon(100) });
        Assert.Null(OsmPlanningGraph.Build(new[] { empty }, new List<Runway>(), new List<StartPosition>(), null));
        Assert.Null(OsmPlanningGraph.Build(null, new List<Runway>(), new List<StartPosition>(), null));
    }

    [Fact]
    public void The_source_with_the_most_taxiways_wins_and_osm_breaks_a_tie()
    {
        var osm = Osm(("A", 0, 0, 100, 0));
        var apt = Osm(("A", 0, 0, 100, 0));
        apt.GetType().GetProperty("Source")!.SetValue(apt, "aptdat");   // init-only: set through reflection for the fixture
        Assert.Same(osm, OsmPlanningGraph.PickSource(new[] { apt, osm }));

        var bigger = Osm(("A", 0, 0, 100, 0), ("B", 0, 0, 0, 100));
        bigger.GetType().GetProperty("Source")!.SetValue(bigger, "aptdat");
        Assert.Same(bigger, OsmPlanningGraph.PickSource(new[] { osm, bigger }));
    }
}

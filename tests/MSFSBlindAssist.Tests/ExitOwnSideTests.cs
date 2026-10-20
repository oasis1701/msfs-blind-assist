// The landing-exit route-start anchor keeps to the exit's own side of the runway just landed on
// (Navigation.ExitOwnSide, used by TaxiGuidanceManager.LoadRoute's start-node pick and its
// backwards-start retry). A taxiway name can run on both banks; the nearest node by that name must
// never start the route across the runway from the exit's vacate point.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using static MSFSBlindAssist.Tests.RunwayFixture;

namespace MSFSBlindAssist.Tests;

public class ExitOwnSideTests
{
    // East-west runway, pavement half-width 30 m, centreline at north = 0.
    private static RunwayShape Runway() => RunwayShape.For(EastWest("09", "27", lengthM: 3000.0, halfWidthM: 30.0));

    [Fact]
    public void Destination_north_admits_the_north_bank_and_the_pavement_but_not_the_south_bank()
    {
        var filter = ExitOwnSide.Filter(Runway(), Lat(90), Lon(1500));

        Assert.NotNull(filter);
        Assert.True(filter!(Node(1, 1500, 60)));     // north bank
        Assert.True(filter(Node(2, 1500, 0)));       // centreline
        Assert.True(filter(Node(3, 1500, -20)));     // pavement, south half
        Assert.False(filter(Node(4, 1500, -60)));    // south bank — across the runway
    }

    [Fact]
    public void Destination_south_mirrors_it()
    {
        var filter = ExitOwnSide.Filter(Runway(), Lat(-90), Lon(1500));

        Assert.NotNull(filter);
        Assert.True(filter!(Node(1, 1500, -60)));
        Assert.False(filter(Node(2, 1500, 60)));
    }

    [Fact]
    public void A_destination_on_the_pavement_names_no_side()
        => Assert.Null(ExitOwnSide.Filter(Runway(), Lat(10), Lon(1500)));

    [Fact]
    public void The_named_anchor_picks_the_own_side_node_even_when_the_far_bank_is_nearer()
    {
        // Taxiway A crosses the runway at east 1500: a node 40 m SOUTH (nearer the aircraft) and one
        // 60 m NORTH. The exit's vacate point is north, so the route must start on the north node.
        var graph = new TaxiGraph();
        var south = Node(10, 1500, -40);
        var north = Node(11, 1500, 60);
        south.TaxiwayNames.Add("A");
        north.TaxiwayNames.Add("A");
        graph.Nodes[south.NodeId] = south;
        graph.Nodes[north.NodeId] = north;

        double acLat = Lat(0), acLon = Lon(1450);
        var unfiltered = graph.FindNearestNodeOnTaxiway(acLat, acLon, "A");
        var ownSide = graph.FindNearestNodeOnTaxiway(acLat, acLon, "A",
            accept: ExitOwnSide.Filter(Runway(), Lat(90), Lon(1500)));

        Assert.Equal(south.NodeId, unfiltered!.NodeId);   // the defect's shape: nearest is across the runway
        Assert.Equal(north.NodeId, ownSide!.NodeId);
    }

    [Fact]
    public void With_no_own_side_node_the_filtered_search_finds_nothing_and_the_caller_keeps_its_old_pick()
    {
        var graph = new TaxiGraph();
        var south = Node(10, 1500, -40);
        south.TaxiwayNames.Add("A");
        graph.Nodes[south.NodeId] = south;

        Assert.Null(graph.FindNearestNodeOnTaxiway(Lat(0), Lon(1450), "A",
            accept: ExitOwnSide.Filter(Runway(), Lat(90), Lon(1500))));
    }
}

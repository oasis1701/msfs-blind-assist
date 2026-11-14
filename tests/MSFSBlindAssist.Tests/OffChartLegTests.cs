using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class OffChartLegTests
{
    private static int Node(TaxiGraph g, (double Lat, double Lon) p) => KbosHcJunctionFixture.Node(g, p);

    [Fact]
    public void From_the_end_of_H_left_the_leg_straight_ahead_wins()
    {
        var g = KbosHcJunctionFixture.Build();
        var pav = PavementMap.Build(g);
        var list = OffChartLeg.Find(g, pav, Node(g, KbosHcJunctionFixture.X), 180.0, "C", TurnSide.Left);

        Assert.NotEmpty(list);
        Assert.Equal(Node(g, KbosHcJunctionFixture.Y), list[0].JoinNodeId);
        Assert.Equal(Node(g, KbosHcJunctionFixture.C4), list[0].NextNodeId);
        Assert.InRange(list[0].LengthMeters, 95, 98);
        Assert.InRange(list[0].FirstTurnDeg, -4.5, -1.5);
        Assert.InRange(list[0].TurnAtTaxiwayDeg, 72, 76);

        int zIndex = list.FindIndex(c => c.JoinNodeId == Node(g, KbosHcJunctionFixture.Z));
        Assert.True(zIndex > 0, "the X-to-Z leg should also be listed, after the best one");
    }

    [Fact]
    public void The_diagonal_across_the_corner_is_refused_for_pavement()
    {
        var g = KbosHcJunctionFixture.Build();
        var pav = PavementMap.Build(g);
        int c4 = Node(g, KbosHcJunctionFixture.C4);
        int east = g.Adjacency[c4]
            .Select(e => g.Nodes[e.ToNodeId])
            .OrderByDescending(n => n.Longitude).First().NodeId;

        Assert.Equal(OffChartLeg.Refusal.OffPavement,
            OffChartLeg.Judge(g, pav, Node(g, KbosHcJunctionFixture.X), 180.0, c4, east, TurnSide.Left));
    }

    [Fact]
    public void From_the_connector_one_leg_passes_and_it_stays_off_the_runway()
    {
        var g = KbosHcJunctionFixture.Build();
        var pav = PavementMap.Build(g);
        int n261 = Node(g, KbosHcJunctionFixture.N261);
        var list = OffChartLeg.Find(g, pav, n261, 234.5, "C", TurnSide.Left);

        var only = Assert.Single(list);
        Assert.Equal(Node(g, KbosHcJunctionFixture.C1), only.JoinNodeId);
        Assert.Equal(Node(g, KbosHcJunctionFixture.Z), only.NextNodeId);
        Assert.InRange(only.LengthMeters, 37, 40);
        Assert.InRange(only.FirstTurnDeg, 40, 44);
        Assert.InRange(only.TurnAtTaxiwayDeg, 80, 85);

        Assert.Equal(OffChartLeg.Refusal.EntersRunway,
            OffChartLeg.Judge(g, pav, n261, 234.5, Node(g, KbosHcJunctionFixture.Z),
                Node(g, KbosHcJunctionFixture.Y), TurnSide.Left));
    }

    [Fact]
    public void A_leg_that_starts_on_the_runway_may_run_along_it()
    {
        var g = KbosHcJunctionFixture.Build();
        var pav = PavementMap.Build(g);
        Assert.Equal(OffChartLeg.Refusal.None,
            OffChartLeg.Judge(g, pav, Node(g, KbosHcJunctionFixture.X), 180.0,
                Node(g, KbosHcJunctionFixture.Z), Node(g, KbosHcJunctionFixture.Y), TurnSide.Left));
    }

    [Fact]
    public void A_leg_from_one_runway_onto_another_is_refused()
    {
        // Runways 09 (east-west) and 18 (north-south) cross at (37.0, -122.0). Taxiway A ends on the
        // 09 centerline 60 m west of the crossing; taxiway B crosses the 18 centerline 60 m south of it.
        const double lat0 = 37.0, lon0 = -122.0;
        double mLat = 1.0 / 111132.0;
        double mLon = 1.0 / (111132.0 * Math.Cos(lat0 * Math.PI / 180.0));
        double aLon = lon0 - 60 * mLon, bLat = lat0 - 60 * mLat;

        TaxiPath P(string name, double la1, double lo1, double la2, double lo2) => new TaxiPath
        {
            Name = name, Type = "PT", StartType = "N", EndType = "N", Width = 98.0,
            StartLat = la1, StartLon = lo1, EndLat = la2, EndLon = lo2,
        };
        var paths = new List<TaxiPath>
        {
            P("A", lat0 + 66 * mLat, aLon, lat0, aLon),
            P("B", bLat, lon0 - 66 * mLon, bLat, lon0),
            P("B", bLat, lon0, bLat, lon0 + 66 * mLon),
        };
        var runways = new List<Runway>
        {
            new Runway { RunwayID = "09", StartLat = lat0, StartLon = lon0 - 180 * mLon,
                EndLat = lat0, EndLon = lon0 + 180 * mLon, Heading = 90, Width = 150, Length = 360 / 0.3048 },
            new Runway { RunwayID = "18", StartLat = lat0 + 180 * mLat, StartLon = lon0,
                EndLat = lat0 - 180 * mLat, EndLon = lon0, Heading = 180, Width = 150, Length = 360 / 0.3048 },
        };
        var starts = new List<StartPosition>
        {
            new StartPosition { RunwayName = "09", Latitude = lat0, Longitude = lon0 - 180 * mLon, Heading = 90 },
            new StartPosition { RunwayName = "27", Latitude = lat0, Longitude = lon0 + 180 * mLon, Heading = 270 },
            new StartPosition { RunwayName = "18", Latitude = lat0 + 180 * mLat, Longitude = lon0, Heading = 180 },
            new StartPosition { RunwayName = "36", Latitude = lat0 - 180 * mLat, Longitude = lon0, Heading = 0 },
        };
        var g = TaxiGraph.Build(paths, new List<ParkingSpot>(), starts, runways);
        var pav = PavementMap.Build(g);

        int aEnd = Node(g, (lat0, aLon));
        int bJoin = Node(g, (bLat, lon0));
        int bNext = Node(g, (bLat, lon0 + 66 * mLon));

        Assert.Equal(OffChartLeg.Refusal.EntersRunway,
            OffChartLeg.Judge(g, pav, aEnd, 180.0, bJoin, bNext, TurnSide.Left));
    }

    [Fact]
    public void The_hairpin_at_the_connector_junction_is_refused_for_shape()
    {
        var g = KbosHcJunctionFixture.Build();
        var pav = PavementMap.Build(g);
        Assert.Equal(OffChartLeg.Refusal.Shape,
            OffChartLeg.Judge(g, pav, Node(g, KbosHcJunctionFixture.X), 180.0,
                Node(g, KbosHcJunctionFixture.K), Node(g, KbosHcJunctionFixture.C1), TurnSide.Left));
    }

    [Fact]
    public void Excluded_nodes_are_never_join_nodes()
    {
        var g = KbosHcJunctionFixture.Build();
        var pav = PavementMap.Build(g);
        var list = OffChartLeg.Find(g, pav, Node(g, KbosHcJunctionFixture.X), 180.0, "C", TurnSide.Left,
            new HashSet<int> { Node(g, KbosHcJunctionFixture.Y) });
        Assert.NotEmpty(list);
        Assert.Equal(Node(g, KbosHcJunctionFixture.Z), list[0].JoinNodeId);
        Assert.DoesNotContain(list, c => c.JoinNodeId == Node(g, KbosHcJunctionFixture.Y));
    }

    [Fact]
    public void Nothing_passes_when_the_taxiway_is_out_of_reach()
    {
        var g = KbosHcJunctionFixture.Build();
        var pav = PavementMap.Build(g);
        Assert.Empty(OffChartLeg.Find(g, pav, Node(g, KbosHcJunctionFixture.HNorth), 170.0, "C", TurnSide.Left));
    }
}

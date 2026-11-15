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

    // ---- synthetic local-metre graphs (x east, y north of 37.0, -122.0) ----

    private const double SynLat = 37.0, SynLon = -122.0;
    private static double SynLatOf(double yM) => SynLat + yM / 111132.0;
    private static double SynLonOf(double xM) => SynLon + xM / (111132.0 * Math.Cos(SynLat * Math.PI / 180.0));
    private static (double Lat, double Lon) Syn(double x, double y) => (SynLatOf(y), SynLonOf(x));
    private static (double X, double Y) Polar(double fromX, double fromY, double bearingDeg, double meters)
        => (fromX + meters * Math.Sin(bearingDeg * Math.PI / 180.0),
            fromY + meters * Math.Cos(bearingDeg * Math.PI / 180.0));

    private static TaxiPath SynPath(string name, (double X, double Y) a, (double X, double Y) b, double widthFt = 98.0)
    {
        var (la, lo) = Syn(a.X, a.Y);
        var (lb, lob) = Syn(b.X, b.Y);
        return new TaxiPath
        {
            Name = name, Type = "PT", StartType = "N", EndType = "N", Width = widthFt,
            StartLat = la, StartLon = lo, EndLat = lb, EndLon = lob,
        };
    }

    private static TaxiGraph SynBuild(List<TaxiPath> paths, List<Runway>? runways = null, List<StartPosition>? starts = null)
        => TaxiGraph.Build(paths, new List<ParkingSpot>(), starts ?? new List<StartPosition>(), runways ?? new List<Runway>());

    /// One leg from F (the origin, arriving heading south, 180) to a join node of "T", judged for a
    /// Left pick. first and atTaxiway are the signed turns toward the picked side. An apron edge F-J
    /// keeps the leg on mapped pavement; no runway is near.
    private static OffChartLeg.Refusal JudgeSyntheticLeg(double length, double first, double atTaxiway)
    {
        double legBearing = 180.0 - first;
        double taxiwayBearing = legBearing - atTaxiway;
        var f = (X: 0.0, Y: 0.0);
        var j = Polar(0, 0, legBearing, length);
        var n = Polar(j.X, j.Y, taxiwayBearing, 60.0);
        var g = SynBuild(new List<TaxiPath>
        {
            SynPath("P", f, j, widthFt: 300.0),
            SynPath("T", j, n),
        });
        var pav = PavementMap.Build(g);
        var mid = Syn((f.X + j.X) / 2, (f.Y + j.Y) / 2);
        Assert.True(pav.IsOnMappedPavement(mid.Lat, mid.Lon), "arrange: the leg must lie on mapped pavement");
        return OffChartLeg.Judge(g, pav, Node(g, Syn(f.X, f.Y)), 180.0,
            Node(g, Syn(j.X, j.Y)), Node(g, Syn(n.X, n.Y)), TurnSide.Left);
    }

    // The expected refusal is the Refusal member's name: the enum is internal, a public test cannot take it.
    [Theory]
    [InlineData(149.0, "None")]
    [InlineData(151.0, "TooLongOrShort")]
    [InlineData(5.1, "None")]
    [InlineData(4.9, "TooLongOrShort")]
    public void The_length_limits_are_150_and_5_metres(double length, string expected)
        => Assert.Equal(expected, JudgeSyntheticLeg(length, 0.0, 60.0).ToString());

    [Theory]
    [InlineData(-24.5, "None")]
    [InlineData(-25.5, "Shape")]
    public void The_leg_may_leave_at_most_25_degrees_to_the_other_side(double first, string expected)
        => Assert.Equal(expected, JudgeSyntheticLeg(60.0, first, 60.0).ToString());

    [Theory]
    [InlineData(99.5, "None")]
    [InlineData(100.5, "Shape")]
    public void The_leg_may_leave_at_most_100_degrees_to_the_picked_side(double first, string expected)
        => Assert.Equal(expected, JudgeSyntheticLeg(60.0, first, 30.0).ToString());

    [Theory]
    [InlineData(89.5, "None")]    // total 169.5
    [InlineData(90.5, "Shape")]   // total 170.5
    public void The_two_turns_together_stay_under_170_degrees(double atTaxiway, string expected)
        => Assert.Equal(expected, JudgeSyntheticLeg(60.0, 80.0, atTaxiway).ToString());

    [Fact]
    public void Candidates_within_five_degrees_of_each_other_are_ordered_by_length()
    {
        // Two legs from F: one straight ahead and long (first turn 0), one short and 4 degrees to the left.
        var f = (X: 0.0, Y: 0.0);
        var jLong = Polar(0, 0, 180.0, 100.0);
        var jShort = Polar(0, 0, 176.0, 40.0);
        var nLong = Polar(jLong.X, jLong.Y, 120.0, 60.0);
        var nShort = Polar(jShort.X, jShort.Y, 116.0, 60.0);
        var g = SynBuild(new List<TaxiPath>
        {
            SynPath("P", f, jLong, 300.0),
            SynPath("P", f, jShort, 300.0),
            SynPath("T", jLong, nLong),
            SynPath("T", jShort, nShort),
        });
        var pav = PavementMap.Build(g);
        var list = OffChartLeg.Find(g, pav, Node(g, Syn(f.X, f.Y)), 180.0, "T", TurnSide.Left);

        Assert.True(list.Count >= 2);
        Assert.Equal(Node(g, Syn(jShort.X, jShort.Y)), list[0].JoinNodeId);
        Assert.Equal(Node(g, Syn(jLong.X, jLong.Y)), list[1].JoinNodeId);
        Assert.True(Math.Abs(list[1].FirstTurnDeg) < Math.Abs(list[0].FirstTurnDeg));
    }

    [Fact]
    public void A_leg_along_one_runway_through_its_crossing_with_another_is_refused()
    {
        // Runway A (east-west) is listed first, runway B (north-south) crosses it at the origin. The leg
        // starts on A 60 m west of the crossing and runs along A, through B's pavement, to a node of T.
        Runway Rwy(string id, (double X, double Y) a, (double X, double Y) b, double heading)
        {
            var (la, lo) = Syn(a.X, a.Y);
            var (lb, lob) = Syn(b.X, b.Y);
            return new Runway { RunwayID = id, StartLat = la, StartLon = lo, EndLat = lb, EndLon = lob,
                Heading = heading, Width = 150, Length = 360 / 0.3048 };
        }
        StartPosition Start(string id, (double X, double Y) p, double heading)
        {
            var (la, lo) = Syn(p.X, p.Y);
            return new StartPosition { RunwayName = id, Latitude = la, Longitude = lo, Heading = heading };
        }
        var runways = new List<Runway>
        {
            Rwy("09", (-180, 0), (180, 0), 90),
            Rwy("18", (0, 180), (0, -180), 180),
        };
        var starts = new List<StartPosition>
        {
            Start("09", (-180, 0), 90), Start("27", (180, 0), 270),
            Start("18", (0, 180), 180), Start("36", (0, -180), 0),
        };
        var f = (X: -60.0, Y: 0.0);
        var j = (X: 60.0, Y: 0.0);
        var n = Polar(j.X, j.Y, 45.0, 60.0);
        var g = SynBuild(new List<TaxiPath>
        {
            SynPath("I", (f.X, 40.0), f),
            SynPath("T", j, n),
        }, runways, starts);
        var pav = PavementMap.Build(g);

        Assert.Equal(OffChartLeg.Refusal.EntersRunway,
            OffChartLeg.Judge(g, pav, Node(g, Syn(f.X, f.Y)), 90.0,
                Node(g, Syn(j.X, j.Y)), Node(g, Syn(n.X, n.Y)), TurnSide.Left));
    }

    [Fact]
    public void Nothing_passes_when_the_taxiway_is_out_of_reach()
    {
        var g = KbosHcJunctionFixture.Build();
        var pav = PavementMap.Build(g);
        Assert.Empty(OffChartLeg.Find(g, pav, Node(g, KbosHcJunctionFixture.HNorth), 170.0, "C", TurnSide.Left));
    }
}

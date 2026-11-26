// X-Plane red-sign holding points (OMDB "KK"): the apt.dat sign parser, the clustering that ties a
// sign to its taxiway, and NamedHoldingPointResolver.ResolveSigns' placement + refusals. Signs are
// a FALLBACK — every refusal here is the "leave it out rather than guess" direction.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class SignHoldingPointTests
{
    private const double REF_LAT = 37.0, BASE_LON = -122.0, M_PER_DEG = 111320.0;
    private static double LatN(double m) => REF_LAT + m / M_PER_DEG;
    private static double LonE(double m) => BASE_LON + m / (M_PER_DEG * Math.Cos(REF_LAT * Math.PI / 180.0));

    // --- Sign markup -------------------------------------------------------------------------

    [Theory]
    [InlineData("{@R}KK{@@}KK", new[] { "KK" })]                                   // OMDB KK, both faces
    [InlineData("{@R}KP{@L}K{@Y}K8{^rd,@@,@R}KP", new[] { "KP" })]                 // red name beside location/direction
    [InlineData("{@Y}{^l}K{@L}K1{@Y}K{^r}", new string[0])]                        // no red at all
    [InlineData("{@R}A1{@@}{@R}27R-09L", new string[0])]                           // runway holding position: dropped whole
    [InlineData("{@R}NESSI|{@R}09L", new string[0])]
    [InlineData("{@R}ILS{@@}{@R}CAT_II", new string[0])]                           // describes the hold, names nothing
    [InlineData("{@R}RWY11", new string[0])]
    [InlineData("{@R}O5L", new string[0])]                                         // letter-O typo for 05L
    [InlineData("{@R}NO_ENTRY", new string[0])]
    [InlineData("{@R}N8033F", new string[0])]                                      // KGBG: an aircraft registration on a sign
    [InlineData("{@R}HP12", new[] { "HP12" })]                                     // ZSAM: two digits is a real name
    public void RedSignNames_keeps_only_holding_point_designators(string markup, string[] expected)
    {
        Assert.Equal(expected, SignHoldingPointExtractor.RedSignNames(markup));
    }

    // --- Extraction --------------------------------------------------------------------------

    private static NamedTaxiSegment Seg(string name, double n1, double e1, double n2, double e2) =>
        new() { Name = name, Lat1 = LatN(n1), Lon1 = LonE(e1), Lat2 = LatN(n2), Lon2 = LonE(e2) };

    [Fact]
    public void Extract_ties_a_sign_to_the_taxiway_it_stands_beside()
    {
        var taxiways = new[] { Seg("K", 0, 0, 0, 1000), Seg("K2", 0, 500, 300, 500) };
        // KK signs on both sides of K at 200 m east, 30 m off its centreline.
        var signs = new[] { (LatN(30), LonE(200), "{@R}KK"), (LatN(-30), LonE(205), "{@R}KK") };

        var p = Assert.Single(SignHoldingPointExtractor.Extract(signs, taxiways));
        Assert.Equal("KK", p.Name);
        Assert.Equal("K", p.Taxiway);
        Assert.Equal(2, p.SignCount);
        Assert.Equal(LatN(0), p.Lat, 6);   // centroid of the two sides = the centreline
    }

    [Fact]
    public void Extract_drops_a_name_used_at_two_places()
    {
        var taxiways = new[] { Seg("K", 0, 0, 0, 2000) };
        var signs = new[] { (LatN(30), LonE(100), "{@R}KK"), (LatN(30), LonE(1500), "{@R}KK") };
        Assert.Empty(SignHoldingPointExtractor.Extract(signs, taxiways));
    }

    [Fact]
    public void Extract_drops_a_sign_with_no_taxiway_nearby()
    {
        var taxiways = new[] { Seg("K", 0, 0, 0, 1000) };
        var signs = new[] { (LatN(200), LonE(100), "{@R}KK") };
        Assert.Empty(SignHoldingPointExtractor.Extract(signs, taxiways));
    }

    [Fact]
    public void AptDatParser_reads_sign_rows()
    {
        string apt = string.Join("\n",
            $"1201 {LatN(0):F8} {LonE(0):F8} both 1",
            $"1201 {LatN(0):F8} {LonE(1000):F8} both 2",
            "1202 1 2 twoway taxiway_E K",
            $"20 {LatN(30):F8} {LonE(200):F8} 90.0 0 2 {{@R}}KK{{@@}}KK");
        var data = AptDatParser.Parse(apt);
        var p = Assert.Single(data.SignHoldingPoints);
        Assert.Equal(("KK", "K"), (p.Name, p.Taxiway));
    }

    // --- Placement on the scenery graph ------------------------------------------------------

    // Runway 09/27 along north = 0 from 0 to 3000 m east. Taxiway K parallel at 250 m north;
    // connector B from the runway up to K at 1500 m east.
    private static TaxiGraph Graph()
    {
        var paths = new List<TaxiPath>
        {
            new() { Name = "K", StartLat = LatN(250), StartLon = LonE(0), EndLat = LatN(250), EndLon = LonE(1500), Width = 75 },
            new() { Name = "K", StartLat = LatN(250), StartLon = LonE(1500), EndLat = LatN(250), EndLon = LonE(3000), Width = 75 },
            new() { Name = "B", StartLat = LatN(250), StartLon = LonE(1500), EndLat = LatN(0), EndLon = LonE(1500), Width = 75 },
        };
        var starts = new List<StartPosition>
        {
            new() { RunwayName = "09", Latitude = LatN(0), Longitude = LonE(0), Heading = 90 },
            new() { RunwayName = "27", Latitude = LatN(0), Longitude = LonE(3000), Heading = 270 },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), starts);
    }

    private static SignHoldingPoint Sign(string name, double n, double e, string taxiway) =>
        new(name, LatN(n), LonE(e), taxiway, 1);

    [Fact]
    public void ResolveSigns_places_the_point_on_its_named_taxiway()
    {
        var g = Graph();
        int before = g.Nodes.Count;
        var p = Assert.Single(NamedHoldingPointResolver.ResolveSigns(
            g, new[] { Sign("KK", 285, 700, "K") }, new HashSet<string>()));

        Assert.Equal("KK (from sign, approximate)", p.DisplayLabel);
        Assert.Equal(before + 1, g.Nodes.Count);            // split onto K, not snapped elsewhere
        Assert.Equal(LatN(250), p.Latitude, 6);
        Assert.Equal(LonE(700), p.Longitude, 5);
        Assert.True(p.InsertedOnEdge);
        Assert.Contains("K", g.Nodes[p.NodeId].TaxiwayNames);
    }

    // Where Am I says "near holding point KK on taxiway K" from another taxiway, so the point
    // carries the taxiway its sign names.
    [Fact]
    public void ResolveSigns_records_the_taxiway_the_sign_names()
    {
        var p = Assert.Single(NamedHoldingPointResolver.ResolveSigns(
            Graph(), new[] { Sign("KK", 285, 700, "K") }, new HashSet<string>()));

        Assert.Equal(new[] { "K" }, p.Taxiways);
    }

    // X-Plane may spell the taxiway "k" where navdata says "K". The point keeps the graph's
    // spelling, the one every other answer speaks ([RTE-25]).
    [Fact]
    public void ResolveSigns_records_the_taxiway_in_the_graphs_spelling()
    {
        var p = Assert.Single(NamedHoldingPointResolver.ResolveSigns(
            Graph(), new[] { Sign("KK", 285, 700, "k") }, new HashSet<string>()));

        Assert.Equal(new[] { "K" }, p.Taxiways);
    }

    [Fact]
    public void ResolveSigns_never_overrides_a_name_a_painted_line_carries()
    {
        var g = Graph();
        var result = NamedHoldingPointResolver.ResolveSigns(
            g, new[] { Sign("KK", 285, 700, "K") }, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "kk" });
        Assert.Empty(result);
    }

    [Fact]
    public void ResolveSigns_refuses_near_a_runway_and_leaves_the_graph_alone()
    {
        var g = Graph();
        int before = g.Nodes.Count;
        // On connector B, 100 m from the runway centreline: a runway holding position.
        var result = NamedHoldingPointResolver.ResolveSigns(
            g, new[] { Sign("BA", 100, 1530, "B") }, new HashSet<string>());
        Assert.Empty(result);
        Assert.Equal(before, g.Nodes.Count);
    }

    // The same graph with taxiway C passing behind the runway's 27 end, 60 m beyond it.
    private static TaxiGraph GraphWithTaxiwayBehindTheRunwayEnd()
    {
        var paths = new List<TaxiPath>
        {
            new() { Name = "K", StartLat = LatN(250), StartLon = LonE(0), EndLat = LatN(250), EndLon = LonE(3060), Width = 75 },
            new() { Name = "C", StartLat = LatN(250), StartLon = LonE(3060), EndLat = LatN(-250), EndLon = LonE(3060), Width = 75 },
        };
        var starts = new List<StartPosition>
        {
            new() { RunwayName = "09", Latitude = LatN(0), Longitude = LonE(0), Heading = 90 },
            new() { RunwayName = "27", Latitude = LatN(0), Longitude = LonE(3000), Heading = 270 },
        };
        return TaxiGraph.Build(paths, new List<ParkingSpot>(), starts);
    }

    // The clearance is measured to the nearest point of the centreline, its ENDS included. Measured
    // only beside the runway, a point just past the end was kept: the 2026-11-25 audit found five
    // (EGTB A2 67 m beyond the end of 24L/06R and 15 m off its line, EGTB A3, LFLH A1 and A2,
    // LIRF DL1), all runway holding positions.
    [Fact]
    public void ResolveSigns_refuses_a_point_just_beyond_a_runway_end()
    {
        var g = GraphWithTaxiwayBehindTheRunwayEnd();
        int before = g.Nodes.Count;
        // On C, 60 m beyond the 27 end and 15 m off the extended centreline: 62 m from the end.
        var result = NamedHoldingPointResolver.ResolveSigns(
            g, new[] { Sign("CE", 15, 3090, "C") }, new HashSet<string>());
        Assert.Empty(result);
        Assert.Equal(before, g.Nodes.Count);
    }

    // 200 m to the side and 60 m beyond the end is 209 m from the end: clear of the runway.
    [Fact]
    public void ResolveSigns_keeps_a_point_beyond_a_runway_end_that_is_clear_of_it()
    {
        var p = Assert.Single(NamedHoldingPointResolver.ResolveSigns(
            GraphWithTaxiwayBehindTheRunwayEnd(), new[] { Sign("CF", 200, 3090, "C") }, new HashSet<string>()));
        Assert.Equal(new[] { "C" }, p.Taxiways);
    }

    [Fact]
    public void ResolveSigns_refuses_a_name_that_is_also_a_taxiway()
    {
        var g = Graph();
        Assert.Empty(NamedHoldingPointResolver.ResolveSigns(
            g, new[] { Sign("B", 285, 700, "K") }, new HashSet<string>()));
    }

    [Fact]
    public void ResolveSigns_refuses_when_the_named_taxiway_is_too_far()
    {
        var g = Graph();
        Assert.Empty(NamedHoldingPointResolver.ResolveSigns(
            g, new[] { Sign("KK", 400, 700, "K") }, new HashSet<string>()));
    }
}

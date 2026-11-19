// Characterization tests for RunwayCrossingResolver — "cross runway 23R at P1" in Progressive Taxi,
// added 2026-09-14.
//
// The real case (EGCC 23R, measured against the user's navdata + live OSM): the crossing is ONE
// stretch of pavement that changes name on the centreline — P1 (hold, 137 m north) → a node 0.4 m
// north of the axis → DZ1 (hold, 137 m south). No far-side node carries the name P, and the P1 →
// centreline edge never spans the axis, so the old picker offered DZ and never P, and could not
// offer the holding point at all.
//
// Fixture idiom: a synthetic east-west runway on the equator (heading 090, 3000 m, 150 ft wide),
// where RunwayFrame's 111320 m/deg makes cross-track metres = degrees of latitude x 111320
// (positive = north = LEFT of the heading).
//
// Characterization, not spec: if a literal disagrees with real output, fix the test.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayCrossingResolverTests
{
    private const double M = 111320.0;
    private const double CrossLon = 0.009;   // ~1000 m down the runway

    private static double Lat(double metresNorth) => metresNorth / M;

    private static Runway Rwy() => new()
    {
        AirportICAO = "TEST", RunwayID = "09", Heading = 90, HeadingMag = 90,
        StartLat = 0, StartLon = 0, EndLat = 0, EndLon = 0.027,
        Length = 9843, Width = 150,
    };

    private static TaxiPath Seg(string name, double lat1, double lon1, double lat2, double lon2,
        string startType = "N", string endType = "N") => new()
    {
        Name = name, Type = "T", StartLat = lat1, StartLon = lon1, EndLat = lat2, EndLon = lon2,
        StartType = startType, EndType = endType,
    };

    private static TaxiGraph Build(params TaxiPath[] paths) =>
        TaxiGraph.Build(paths.ToList(), new List<ParkingSpot>(), new List<StartPosition>());

    private static TaxiNode At(TaxiGraph g, double metresNorth, double lon) =>
        g.Nodes.Values.Single(n => Math.Abs(n.Latitude - Lat(metresNorth)) < 1e-7 && Math.Abs(n.Longitude - lon) < 1e-7);

    /// <summary>The EGCC P/DZ shape: named P north of the axis, DZ from the centreline south.</summary>
    private static TaxiGraph EgccShape() => Build(
        Seg("P", Lat(300), CrossLon, Lat(137), CrossLon, "N", "HSND"),
        Seg("P", Lat(137), CrossLon, Lat(0.4), CrossLon, "HSND", "N"),
        Seg("DZ", Lat(0.4), CrossLon, Lat(-100), CrossLon),
        Seg("DZ", Lat(-100), CrossLon, Lat(-137), CrossLon, "N", "HSND"),
        Seg("DZ", Lat(-137), CrossLon, Lat(-145), CrossLon, "HSND", "N"),
        Seg("DZ", Lat(-145), CrossLon, Lat(-300), CrossLon));

    [Fact]
    public void A_crossing_that_changes_name_on_the_centreline_ends_past_the_far_hold_line()
    {
        var g = EgccShape();
        var p1 = At(g, 137, CrossLon);

        var crossing = RunwayCrossingResolver.FindAcross(g, Rwy(), p1.NodeId);

        Assert.NotNull(crossing);
        // First far node at holding distance is 100 m; DZ1's own line is just ahead at 137 m,
        // so it settles one hop past it — the tail clear of the line, not straddling it.
        Assert.Equal(At(g, -145, CrossLon).NodeId, crossing!.Value.FarNodeId);
        Assert.InRange(crossing.Value.CrossingAlongM, 990, 1010);
        Assert.False(RunwayCrossingResolver.PassesNearSideHold(g, crossing.Value));
    }

    [Fact]
    public void Both_halves_of_a_renamed_crossing_are_offered_as_crossing_taxiways()
    {
        // The P1 -> centreline edge lies wholly north of the axis (0.4 m), so the edge-spanning
        // rule alone saw only DZ. The pavement-junction rule must add P.
        var names = RunwayCrossingResolver.TaxiwaysCrossing(EgccShape(), Rwy());

        Assert.Contains("P", names);
        Assert.Contains("DZ", names);
    }

    [Fact]
    public void A_picked_taxiway_resolves_its_crossing_from_where_it_meets_the_runway()
    {
        var g = EgccShape();
        var starts = RunwayCrossingResolver.TaxiwayCrossingStarts(g, Rwy(), "P", side: +1);

        Assert.Equal(new[] { At(g, 137, CrossLon).NodeId }, starts);
        Assert.Empty(RunwayCrossingResolver.TaxiwayCrossingStarts(g, Rwy(), "P", side: -1));
    }

    [Fact]
    public void A_runway_entry_with_nothing_on_the_far_side_is_not_a_crossing()
    {
        var g = Build(
            Seg("E", Lat(137), 0.0005, Lat(0.4), 0.0005, "HSND", "N"),
            Seg("E", Lat(300), 0.0005, Lat(137), 0.0005, "N", "HSND"));

        Assert.Null(RunwayCrossingResolver.FindAcross(g, Rwy(), At(g, 137, 0.0005).NodeId));
    }

    [Fact]
    public void Taxiing_down_the_runway_to_another_connector_is_not_a_crossing()
    {
        // Stub A meets the runway at 1000 m, stub B leaves the far side 400 m further on, joined
        // only by pavement modelled along the centreline. Crossing "at A" would mean a 400 m
        // backtrack on the runway — never offered.
        const double BLon = 0.0126;
        var g = Build(
            Seg("A", Lat(137), CrossLon, Lat(0.4), CrossLon, "HSND", "N"),
            Seg("RW", Lat(0.4), CrossLon, Lat(0.4), BLon),
            Seg("B", Lat(0.4), BLon, Lat(-137), BLon, "N", "HSND"),
            Seg("B", Lat(-137), BLon, Lat(-300), BLon, "HSND", "N"));

        Assert.Null(RunwayCrossingResolver.FindAcross(g, Rwy(), At(g, 137, CrossLon).NodeId));
    }

    [Fact]
    public void Crossing_the_extended_centreline_beyond_the_threshold_is_not_crossing_the_runway()
    {
        const double Before = -0.002;   // ~220 m before the runway start
        var g = Build(
            Seg("X", Lat(137), Before, Lat(0.4), Before, "HSND", "N"),
            Seg("X", Lat(0.4), Before, Lat(-137), Before, "N", "HSND"),
            Seg("X", Lat(-137), Before, Lat(-300), Before, "HSND", "N"));

        Assert.Null(RunwayCrossingResolver.FindAcross(g, Rwy(), At(g, 137, Before).NodeId));
    }

    [Fact]
    public void A_hold_further_back_crosses_only_through_the_nearer_line()
    {
        // EGCC B2 (229 m) behind B1 (153 m): both reach the far side, but only B1 is the crossing
        // point ATC names — the caller drops B2 on PassesNearSideHold.
        var g = Build(
            Seg("B", Lat(229), CrossLon, Lat(153), CrossLon, "HSND", "HSND"),
            Seg("B", Lat(153), CrossLon, Lat(0.4), CrossLon, "HSND", "N"),
            Seg("BZ", Lat(0.4), CrossLon, Lat(-153), CrossLon, "N", "HSND"),
            Seg("BZ", Lat(-153), CrossLon, Lat(-160), CrossLon, "HSND", "N"));
        var rwy = Rwy();

        var fromB2 = RunwayCrossingResolver.FindAcross(g, rwy, At(g, 229, CrossLon).NodeId);
        var fromB1 = RunwayCrossingResolver.FindAcross(g, rwy, At(g, 153, CrossLon).NodeId);

        Assert.NotNull(fromB2);
        Assert.True(RunwayCrossingResolver.PassesNearSideHold(g, fromB2!.Value));
        Assert.NotNull(fromB1);
        Assert.False(RunwayCrossingResolver.PassesNearSideHold(g, fromB1!.Value));
    }

    [Fact]
    public void Another_runways_hold_line_on_the_far_side_is_stopped_short_of_never_passed()
    {
        var g = Build(
            Seg("C", Lat(137), CrossLon, Lat(0.4), CrossLon, "HSND", "N"),
            Seg("C", Lat(0.4), CrossLon, Lat(-100), CrossLon),
            Seg("C", Lat(-100), CrossLon, Lat(-137), CrossLon, "N", "HSND"),
            Seg("C", Lat(-137), CrossLon, Lat(-145), CrossLon, "HSND", "N"));
        At(g, -137, CrossLon).HoldShortName = "Runway 18";

        var crossing = RunwayCrossingResolver.FindAcross(g, Rwy(), At(g, 137, CrossLon).NodeId);

        Assert.NotNull(crossing);
        Assert.Equal(At(g, -100, CrossLon).NodeId, crossing!.Value.FarNodeId);
    }

    [Fact]
    public void A_far_hold_with_no_node_close_behind_it_still_settles_past_the_line()
    {
        // EDDF 25C L16 -> M28: the next node is ~60 m beyond the paint. Ending ON the line would
        // leave the tail over it.
        var g = Build(
            Seg("L", Lat(137), CrossLon, Lat(0.4), CrossLon, "HSND", "N"),
            Seg("M", Lat(0.4), CrossLon, Lat(-92), CrossLon, "N", "HSND"),
            Seg("M", Lat(-92), CrossLon, Lat(-152), CrossLon, "HSND", "N"));

        var crossing = RunwayCrossingResolver.FindAcross(g, Rwy(), At(g, 137, CrossLon).NodeId);

        Assert.NotNull(crossing);
        Assert.Equal(At(g, -152, CrossLon).NodeId, crossing!.Value.FarNodeId);
    }
}

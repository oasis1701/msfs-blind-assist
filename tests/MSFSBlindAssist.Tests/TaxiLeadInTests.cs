// Characterization tests for MSFSBlindAssist.Navigation.TaxiLeadIn.
//
// This is characterization, not spec verification: values are derived by
// reasoning about the source and confirmed by running the tests; if a literal
// ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TaxiLeadInTests
{
    private static TaxiRouteSegment Seg(string taxiway, double distM) => new TaxiRouteSegment
    {
        FromNode = new TaxiNode(),
        ToNode = new TaxiNode(),
        TaxiwayName = taxiway,
        DistanceMeters = distM,
    };

    // --- Extract ---------------------------------------------------------------

    [Fact]
    public void Extract_reports_no_lead_in_when_the_route_starts_on_the_cleared_taxiway()
    {
        var route = new TaxiRoute { Segments = { Seg("A", 100), Seg("A", 50) } };

        var info = TaxiLeadIn.Extract(route, "A");

        Assert.False(info.HasLeadIn);
        Assert.Equal(0, info.DistanceMeters);
        Assert.Empty(info.Taxiways);
    }

    [Fact]
    public void Extract_collects_the_leading_run_of_segments_before_the_cleared_taxiway()
    {
        var route = new TaxiRoute { Segments = { Seg("", 20), Seg("B", 30), Seg("A", 100) } };

        var info = TaxiLeadIn.Extract(route, "A");

        Assert.True(info.HasLeadIn);
        Assert.Equal(50, info.DistanceMeters);
        Assert.Equal(new[] { "B" }, info.Taxiways);
    }

    [Fact]
    public void Extract_dedups_consecutive_same_named_segments_but_keeps_repeats_after_a_break()
    {
        var route = new TaxiRoute
        {
            Segments =
            {
                Seg("B", 10), Seg("B", 10), Seg("C", 10), Seg("B", 10), Seg("A", 5),
            },
        };

        var info = TaxiLeadIn.Extract(route, "A");

        Assert.Equal(new[] { "B", "C", "B" }, info.Taxiways);
    }

    [Fact]
    public void Extract_treats_the_whole_route_as_lead_in_when_the_cleared_taxiway_never_appears()
    {
        var route = new TaxiRoute { Segments = { Seg("B", 10), Seg("C", 20) } };

        var info = TaxiLeadIn.Extract(route, "A");

        Assert.True(info.HasLeadIn);
        Assert.Equal(30, info.DistanceMeters);
        Assert.Equal(new[] { "B", "C" }, info.Taxiways);
    }

    private static TaxiNode NodeOn(params string[] taxiways)
    {
        var node = new TaxiNode();
        foreach (var t in taxiways) node.TaxiwayNames.Add(t);
        return node;
    }

    private static TaxiRouteSegment Seg(string taxiway, double distM, TaxiNode from, TaxiNode to) => new TaxiRouteSegment
    {
        FromNode = from,
        ToNode = to,
        TaxiwayName = taxiway,
        DistanceMeters = distM,
    };

    // VTBS 02R, 2026-11-18: cleared E7, E8, D6 but vacated at E10. The router met E7 only at its
    // junction with E8, so no segment carried the name and the whole 2.2 km route was measured.
    [Fact]
    public void Extract_ends_the_lead_in_at_the_cleared_taxiways_junction_when_no_segment_carries_its_name()
    {
        var onE = NodeOn("E", "E9");
        var eAndE8 = NodeOn("E", "E8", "D6");
        var e7Junction = NodeOn("E7", "E8");
        var route = new TaxiRoute
        {
            Segments =
            {
                Seg("E", 550, onE, eAndE8),
                Seg("E8", 90, eAndE8, e7Junction),
                Seg("E8", 90, e7Junction, eAndE8),
                Seg("D6", 1500, eAndE8, NodeOn("D6")),
            },
        };

        var info = TaxiLeadIn.Extract(route, "E7");

        Assert.True(info.HasLeadIn);
        Assert.Equal(640, info.DistanceMeters);
        Assert.Equal(new[] { "E", "E8" }, info.Taxiways);
    }

    [Fact]
    public void Extract_reports_no_lead_in_when_the_route_starts_at_the_cleared_taxiways_junction()
    {
        var c8Junction = NodeOn("C8", "C");
        var route = new TaxiRoute { Segments = { Seg("C", 400, c8Junction, NodeOn("C")) } };

        var info = TaxiLeadIn.Extract(route, "C8");

        Assert.False(info.HasLeadIn);
        Assert.Equal(0, info.DistanceMeters);
    }

    // --- DoublesBackAtUndrivenStart ----------------------------------------------

    private static TaxiRouteSegment Leg(string taxiway, double bearing, TaxiNode to) => new TaxiRouteSegment
    {
        FromNode = new TaxiNode(),
        ToNode = to,
        TaxiwayName = taxiway,
        DistanceMeters = 100,
        BearingDegrees = bearing,
    };

    // VTBS 02R: up the parallel E, out along E8 to the E7 junction, and straight back along E8.
    [Fact]
    public void DoublesBackAtUndrivenStart_is_true_when_the_route_reverses_where_it_meets_the_cleared_taxiway()
    {
        var route = new TaxiRoute
        {
            Segments =
            {
                Leg("E", 14, NodeOn("E", "E8")), Leg("E8", 284, NodeOn("E7", "E8")),
                Leg("E8", 104, NodeOn("E8", "D6")), Leg("D6", 104, NodeOn("D6")),
            },
        };

        Assert.True(TaxiLeadIn.DoublesBackAtUndrivenStart(route, "E7"));
    }

    [Fact]
    public void DoublesBackAtUndrivenStart_is_false_when_the_route_carries_on_through_the_junction()
    {
        var route = new TaxiRoute { Segments = { Leg("E", 14, NodeOn("E", "E8", "D6")), Leg("D6", 104, NodeOn("D6")) } };

        Assert.False(TaxiLeadIn.DoublesBackAtUndrivenStart(route, "E8"));
    }

    // VHHH 07L C8: the 122 degree turn off a high-speed exit is a real turn, not a reversal.
    [Fact]
    public void DoublesBackAtUndrivenStart_is_false_for_a_sharp_turn_short_of_a_reversal()
    {
        var route = new TaxiRoute { Segments = { Leg("", 193, NodeOn("C8", "C")), Leg("C", 71, NodeOn("C")) } };

        Assert.False(TaxiLeadIn.DoublesBackAtUndrivenStart(route, "C8"));
    }

    [Fact]
    public void DoublesBackAtUndrivenStart_is_false_when_the_route_drives_the_cleared_taxiway()
    {
        var route = new TaxiRoute { Segments = { Leg("B", 90, NodeOn("A", "B")), Leg("A", 270, NodeOn("A")) } };

        Assert.False(TaxiLeadIn.DoublesBackAtUndrivenStart(route, "A"));
    }

    [Fact]
    public void DoublesBackAtUndrivenStart_is_false_when_the_route_never_meets_the_cleared_taxiway()
    {
        var route = new TaxiRoute { Segments = { Leg("B", 90, NodeOn("B")), Leg("B", 270, NodeOn("B")) } };

        Assert.False(TaxiLeadIn.DoublesBackAtUndrivenStart(route, "A"));
    }

    // Equator fixture: metres = degrees x 111132.
    private static TaxiNode At(double eastM, double northM, params string[] taxiways)
    {
        var node = NodeOn(taxiways);
        node.Latitude = northM / 111132.0;
        node.Longitude = eastM / 111132.0;
        return node;
    }

    private static TaxiRouteSegment Edge(string taxiway, TaxiNode from, TaxiNode to)
    {
        double north = (to.Latitude - from.Latitude) * 111132.0, east = (to.Longitude - from.Longitude) * 111132.0;
        return new TaxiRouteSegment
        {
            FromNode = from, ToNode = to, TaxiwayName = taxiway,
            DistanceMeters = Math.Sqrt(north * north + east * east),
            BearingDegrees = (Math.Atan2(east, north) * 180.0 / Math.PI + 360.0) % 360.0,
        };
    }

    // KDEN 07, cleared B4, vacated at B3 (VirtualPilot, fs2024): the route reaches B4 only by a 3.3 m
    // spur from WA out to the B4 node and straight back, then carries on east along WA. The spur is the
    // reversal, short as it is; read over 30 m of route either side it smoothed into a 119 degree turn
    // and B4 stayed, sending the pilot 450 m in a straight line to it.
    [Fact]
    public void DoublesBackAtUndrivenStart_catches_a_short_spur_out_to_the_junction_and_back()
    {
        var approach = At(0, -20, "");
        var wa = At(0, 0, "WA");
        var b4 = At(-3.3, 0, "B4", "WA");
        var on = At(47.6, 0, "WA", "AA");
        var route = new TaxiRoute
        {
            Segments = { Edge("", approach, wa), Edge("WA", wa, b4), Edge("WA", b4, wa), Edge("WA", wa, on) },
        };

        Assert.True(TaxiLeadIn.DoublesBackAtUndrivenStart(route, "B4"));
    }

    // A route that STARTS on the cleared taxiway has met it there (as Extract reads it: no lead-in), so a
    // later node that carries the same name is not where it meets it. Navdata names repeat on separate
    // pieces and reach junction nodes through merged path ends.
    [Fact]
    public void DoublesBackAtUndrivenStart_is_false_when_the_route_starts_on_the_cleared_taxiway()
    {
        var start = At(0, 0, "A", "B");
        var later = At(200, 0, "B", "A");
        var back = At(100, 0, "B");
        var route = new TaxiRoute { Segments = { Edge("B", start, later), Edge("B", later, back) } };

        Assert.False(TaxiLeadIn.DoublesBackAtUndrivenStart(route, "A"));
    }

    // --- IsAcceptable ------------------------------------------------------------

    [Fact]
    public void IsAcceptable_rejects_when_a_fallback_reason_is_present()
    {
        Assert.False(TaxiLeadIn.IsAcceptable(50, 100, "no nodes found on taxiway"));
    }

    [Fact]
    public void IsAcceptable_allows_up_to_the_ratio_plus_pad_budget()
    {
        // gap=10 -> allowed = 10*2.5 + 300 = 325
        Assert.True(TaxiLeadIn.IsAcceptable(325.0, 10, null));
        Assert.False(TaxiLeadIn.IsAcceptable(325.1, 10, null));
    }

    // --- Clause --------------------------------------------------------------------

    [Fact]
    public void Clause_is_empty_when_there_is_no_lead_in()
    {
        var info = new TaxiLeadIn.LeadInInfo { HasLeadIn = false };
        Assert.Equal("", TaxiLeadIn.Clause(info, "A"));
    }

    [Fact]
    public void Clause_names_the_taxiway_directly_when_no_intermediate_taxiways_are_named()
    {
        var info = new TaxiLeadIn.LeadInInfo { HasLeadIn = true, Taxiways = Array.Empty<string>() };
        Assert.Equal(" First taxi onto A.", TaxiLeadIn.Clause(info, "A"));
    }

    [Fact]
    public void Clause_names_a_single_intermediate_taxiway()
    {
        var info = new TaxiLeadIn.LeadInInfo { HasLeadIn = true, Taxiways = new[] { "B" } };
        Assert.Equal(" First taxi via B to reach A.", TaxiLeadIn.Clause(info, "A"));
    }

    [Fact]
    public void Clause_lists_multiple_intermediate_taxiways_with_an_oxford_and()
    {
        var info = new TaxiLeadIn.LeadInInfo { HasLeadIn = true, Taxiways = new[] { "B", "C" } };
        Assert.Equal(" First taxi via B and C to reach A.", TaxiLeadIn.Clause(info, "A"));
    }
}

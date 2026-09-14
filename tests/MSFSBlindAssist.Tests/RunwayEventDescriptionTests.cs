// How recorded runway entries and crossings are described: the route summary and "Route changed"
// clause (every entry and crossing, held or not), the one hold sentence, shared-stop labels, the
// non-runway hold count and the diagnostic log line.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayEventDescriptionTests
{
    private static TaxiRouteRunwayEvent Cross(string d, bool held = true) => new() { Kind = RunwayEventKind.Crossing, Designator = d, Held = held };
    private static TaxiRouteRunwayEvent Enter(string d, bool held = true) => new() { Kind = RunwayEventKind.Entry, Designator = d, Held = held };

    private static TaxiRouteSegment Seg(bool hold, string? label) => new()
    {
        FromNode = new TaxiNode(), ToNode = new TaxiNode(), IsHoldShortPoint = hold, HoldShortRunway = label,
    };

    // --- ExtractRunwayDesignators / LabelNamesOnlyRunway --------------------------------

    [Fact]
    public void Every_runway_a_label_names_is_extracted_in_order()
    {
        Assert.Equal(new[] { "06", "29" }, RouteRunwayCrossings.ExtractRunwayDesignators("runway 06 and runway 29"));
        Assert.Equal(new[] { "15R" }, RouteRunwayCrossings.ExtractRunwayDesignators("runway 15R at N"));
        Assert.Equal(new[] { "22R" }, RouteRunwayCrossings.ExtractRunwayDesignators("D5, Runway 22R"));
        Assert.Equal(new[] { "09" }, RouteRunwayCrossings.ExtractRunwayDesignators("runway 9 at Q"));
        Assert.Empty(RouteRunwayCrossings.ExtractRunwayDesignators("end of taxiway B"));
        Assert.Empty(RouteRunwayCrossings.ExtractRunwayDesignators(null));
    }

    [Fact]
    public void A_label_names_only_a_runway_when_every_runway_it_names_is_that_pavement()
    {
        Assert.True(RouteRunwayCrossings.LabelNamesOnlyRunway("runway 06 at D5", "24"));
        Assert.True(RouteRunwayCrossings.LabelNamesOnlyRunway("runway 06", "6"));
        Assert.False(RouteRunwayCrossings.LabelNamesOnlyRunway("runway 06 and runway 29", "06"));
        Assert.False(RouteRunwayCrossings.LabelNamesOnlyRunway("A5", "06"));
        Assert.False(RouteRunwayCrossings.LabelNamesOnlyRunway(null, "06"));
    }

    // --- ComposeSharedLabel ---------------------------------------------------------

    [Fact]
    public void A_stop_shared_by_two_runways_names_both_in_route_order()
    {
        Assert.Equal("runway 06 at D5 and runway 29", RouteRunwayCrossings.ComposeSharedLabel("runway 06 at D5", "29"));
        Assert.Equal("runway 29", RouteRunwayCrossings.ComposeSharedLabel(null, "29"));
        Assert.Equal("runway 29 at A5", RouteRunwayCrossings.ComposeSharedLabel("A5", "29"));
    }

    [Fact]
    public void A_shared_stop_already_naming_this_pavement_or_a_user_terminator_is_kept()
    {
        Assert.Null(RouteRunwayCrossings.ComposeSharedLabel("runway 06", "24"));
        Assert.Null(RouteRunwayCrossings.ComposeSharedLabel("end of taxiway B", "29"));
    }

    // --- ComposeHoldShortInstruction ------------------------------------------------

    [Fact]
    public void The_hold_sentence_is_the_one_wording()
    {
        Assert.Equal("Stop. Hold short of runway 12R. Press continue when cleared.",
            RouteRunwayCrossings.ComposeHoldShortInstruction("runway 12R"));
        Assert.Equal("Stop. Hold short. Press continue when cleared.",
            RouteRunwayCrossings.ComposeHoldShortInstruction(null));
    }

    // --- DescribeRunwayEvents -------------------------------------------------------

    [Fact]
    public void The_same_runway_crossed_twice_reads_twice()
        => Assert.Equal("crossing runway 10L twice",
            RouteRunwayCrossings.DescribeRunwayEvents(new[] { Cross("10L"), Cross("10L") }));

    [Fact]
    public void Three_times_is_counted()
        => Assert.Equal("crossing runway 10L 3 times",
            RouteRunwayCrossings.DescribeRunwayEvents(new[] { Cross("10L"), Cross("10L"), Cross("10L") }));

    [Fact]
    public void Distinct_runways_keep_taxi_order()
        => Assert.Equal("crossing runways 04L, 04R and 27",
            RouteRunwayCrossings.DescribeRunwayEvents(new[] { Cross("04L"), Cross("04R"), Cross("27") }));

    [Fact]
    public void Reciprocal_designators_merge_as_one_pavement_speaking_both_names()
    {
        Assert.Equal("crossing runway 10L/28R twice",
            RouteRunwayCrossings.DescribeRunwayEvents(new[] { Cross("10L"), Cross("28R") }));
        Assert.Equal("crossing runway 09/27 twice",
            RouteRunwayCrossings.DescribeRunwayEvents(new[] { Cross("9"), Cross("27") }));
    }

    [Fact]
    public void Entries_are_named_after_crossings()
    {
        Assert.Equal("entering runway 01", RouteRunwayCrossings.DescribeRunwayEvents(new[] { Enter("01") }));
        Assert.Equal("crossing runway 26R, entering runway 04L",
            RouteRunwayCrossings.DescribeRunwayEvents(new[] { Enter("04L"), Cross("26R") }));
    }

    [Fact]
    public void A_crossing_that_could_not_be_held_is_still_named()
        => Assert.Equal("crossing runway 26R",
            RouteRunwayCrossings.DescribeRunwayEvents(new[] { Cross("26R", held: false) }));

    [Fact]
    public void No_events_is_an_empty_clause()
    {
        Assert.Equal("", RouteRunwayCrossings.DescribeRunwayEvents(Array.Empty<TaxiRouteRunwayEvent>()));
        Assert.Equal("", RouteRunwayCrossings.DescribeRunwayEvents(null));
    }

    // --- CountNonRunwayHoldShorts ----------------------------------------------------

    [Fact]
    public void Only_holds_naming_no_runway_are_counted()
    {
        var segs = new List<TaxiRouteSegment>
        {
            Seg(true, "runway 15R at N"), Seg(true, "D5, Runway 22R"), Seg(true, "end of taxiway B"), Seg(true, "A5"), Seg(false, "A6"),
        };
        Assert.Equal(2, RouteRunwayCrossings.CountNonRunwayHoldShorts(segs, excludeLastSegment: false));
    }

    [Fact]
    public void A_runway_destinations_final_countdown_rail_is_not_counted()
    {
        var segs = new List<TaxiRouteSegment> { Seg(true, "end of taxiway B"), Seg(false, null), Seg(true, "A5") };
        Assert.Equal(1, RouteRunwayCrossings.CountNonRunwayHoldShorts(segs, excludeLastSegment: true));
        Assert.Equal(0, RouteRunwayCrossings.CountNonRunwayHoldShorts(new List<TaxiRouteSegment>(), excludeLastSegment: true));
    }

    // --- DescribeForLog -------------------------------------------------------------

    [Fact]
    public void The_log_line_lists_crossings_entries_unheld_and_the_start_hold()
    {
        var route = new TaxiRoute
        {
            Segments = { Seg(false, null), Seg(false, null), Seg(false, null) },
            RunwayEvents = { Cross("26R"), Enter("04L", held: false) },
            StartHoldRunway = "runway 26R",
        };

        Assert.Equal(
            "Route crossings: phase=load dest=\"Runway 04R\" segments=3 crosses=26R enters=04L unheld=04L startHold=\"runway 26R\"",
            RouteRunwayCrossings.DescribeForLog("load", "Runway 04R", route));
    }

    [Fact]
    public void The_log_line_says_none_for_a_route_that_meets_no_runway()
        => Assert.Equal(
            "Route crossings: phase=recalc dest=\"Gate A 5\" segments=0 crosses=(none) enters=(none) unheld=(none) startHold=(none)",
            RouteRunwayCrossings.DescribeForLog("recalc", "Gate A 5", new TaxiRoute()));
}

// Tests for MSFSBlindAssist.Navigation.HoldingPointHoldShort: a pilot's "hold short of
// holding point L2" on a taxiway row of an ordinary (runway or gate) route.
//
// LOWW, 2026-11-14: "taxi via L, hold short of L2" on the way to Runway 16. The route passed
// L2's own node 801 m along L, and the planner had no way to ask for a stop there.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class HoldingPointHoldShortTests
{
    /// <summary>A route through the given node ids, every segment on taxiway L.</summary>
    private static List<TaxiRouteSegment> Through(params int[] nodeIds)
    {
        var list = new List<TaxiRouteSegment>();
        for (int i = 1; i < nodeIds.Length; i++)
            list.Add(new TaxiRouteSegment
            {
                FromNode = new TaxiNode { NodeId = nodeIds[i - 1] },
                ToNode = new TaxiNode { NodeId = nodeIds[i] },
                TaxiwayName = "L",
            });
        return list;
    }

    private static readonly HoldingPointHoldShort L2 = new("L2", 1845);

    [Fact]
    public void Place_StopsOnTheSegmentThatEndsAtThePoint()
    {
        var route = Through(332, 400, 1845, 1582, 1109);

        Assert.True(L2.Place(route));

        Assert.Equal(new[] { false, true, false, false }, route.Select(s => s.IsHoldShortPoint).ToArray());
        Assert.Equal("holding point L2", route[1].HoldShortRunway);
    }

    [Fact]
    public void Place_LeavesARouteThatDoesNotPassThePointAlone()
    {
        var route = Through(332, 400, 1582);

        Assert.False(L2.Place(route));

        Assert.DoesNotContain(route, s => s.IsHoldShortPoint);
    }

    // The route STARTS on the point: there is nothing to taxi up to and stop at.
    [Fact]
    public void Place_DoesNotCountThePointTheRouteStartsOn()
    {
        Assert.False(L2.Place(Through(1845, 1582, 1109)));
    }

    // "A, C, A" ([RTE-33]) can pass one point twice; the row the pick sits on says which
    // pass is meant, so the search starts where that row's taxiway run starts.
    [Fact]
    public void Place_StartsLookingAtTheRowsOwnRun()
    {
        var route = Through(1, 1845, 2, 3, 1845, 4);

        Assert.True(L2.Place(route, fromSegment: 2));

        Assert.False(route[0].IsHoldShortPoint);
        Assert.True(route[3].IsHoldShortPoint);
    }

    // Another of the pilot's picks already stops there: one stop, and its label stays.
    [Fact]
    public void Place_KeepsTheLabelOfAStopAlreadyThere()
    {
        var route = Through(332, 1845, 1582);
        route[0].IsHoldShortPoint = true;
        route[0].HoldShortRunway = "end of taxiway L";

        Assert.True(L2.Place(route));

        Assert.Equal("end of taxiway L", route[0].HoldShortRunway);
    }

    // The label names no runway, so the stop is a plain hold-short point everywhere a
    // label is read: counted in the summary, never taken for a runway hold...
    [Fact]
    public void Label_NamesNoRunway()
    {
        Assert.Null(RouteRunwayCrossings.ExtractRunwayDesignator(L2.Label));
    }

    // ...and when the automatic runway pass later needs that same stop for a runway, the
    // runway is named and the point is kept ("Hold short of runway 16 at holding point L2").
    [Fact]
    public void Label_IsKeptWhenARunwayHoldLandsOnTheSameStop()
    {
        Assert.Equal(
            "runway 16 at holding point L2",
            RouteRunwayCrossings.ComposeCrossingLabel(L2.Label, "16"));
    }

    // A recalculated route carries a pick over only while its stop is still AHEAD. One the
    // aircraft has passed must not come back if the new route happens to run through the
    // point again, and one that was never on the route was reported when it was loaded.
    [Theory]
    [InlineData(0, true)]    // still taxiing toward it
    [InlineData(1, true)]    // on the segment that ends at it
    [InlineData(2, false)]   // past it
    public void IsStillAhead_ComparesTheStopWithWhereTheAircraftIs(int currentSegment, bool expected)
    {
        var route = Through(332, 400, 1845, 1582, 1109);
        L2.Place(route);

        Assert.Equal(expected, L2.IsStillAhead(route, currentSegment));
    }

    [Fact]
    public void IsStillAhead_IsFalseForAPointTheRouteNeverHeld()
    {
        Assert.False(L2.IsStillAhead(Through(332, 400, 1582), 0));
        // Passing the node is not enough: the stop itself has to be there.
        Assert.False(L2.IsStillAhead(Through(332, 1845, 1582), 0));
        Assert.False(L2.IsStillAhead(null, 0));
    }

    // A route starts at a node near the aircraft (a recalculation: the nearest one within
    // 50 m), and that can be the point itself while the aircraft is still short of it. Place
    // has no segment to stop on then, and "the route does not pass it" would tell the pilot
    // the point is somewhere else, when it is the next node ahead.
    [Fact]
    public void StartsRoute_IsTrueOnlyWhenTheRoutesFirstNodeIsThePoint()
    {
        Assert.True(L2.StartsRoute(Through(1845, 1582, 1109)));
        Assert.False(L2.StartsRoute(Through(332, 1845, 1582)));
        Assert.False(L2.StartsRoute(Through(332, 400, 1582)));
        Assert.False(L2.StartsRoute(new List<TaxiRouteSegment>()));
        Assert.False(L2.StartsRoute(null));
    }

    [Fact]
    public void StartsRouteNote_SaysWhereThePointIsAndThatNoStopIsCalled()
    {
        Assert.Equal(
            "Note: this route starts at holding point L2. No stop will be called there.",
            L2.StartsRouteNote);
    }

    // The pick landed on the runway destination's own hold line (a runway holding point snaps
    // onto the scenery's hold node). That stop is counted down by its label, so the label has
    // to keep the runway: "Hold short Runway 16 at holding point A1 in 100 metres.", the same
    // shape the "depart from named holding point" stop already speaks.
    [Fact]
    public void AtDestinationHold_PutsTheRunwayInFrontOfAPickLabel()
    {
        Assert.Equal(
            "Runway 16 at holding point A1",
            HoldingPointHoldShort.AtDestinationHold(new HoldingPointHoldShort("A1", 7).Label, "Runway 16"));
        Assert.Equal("16", RouteRunwayCrossings.ExtractRunwayDesignator(
            HoldingPointHoldShort.AtDestinationHold("holding point A1", "Runway 16")));
    }

    // Every other label is left exactly as it is: the scenery's own, the pilot's other picks
    // ([HLD-2]), an empty one, and one this already composed (a second pass adds nothing).
    [Theory]
    [InlineData("runway 16 at A1")]
    [InlineData("end of taxiway L")]
    [InlineData("taxiway W")]
    [InlineData("Runway 16 at holding point A1")]
    [InlineData("")]
    [InlineData(null)]
    public void AtDestinationHold_LeavesEveryOtherLabelAlone(string? label)
    {
        Assert.Equal(label, HoldingPointHoldShort.AtDestinationHold(label, "Runway 16"));
    }

    // A pick placed past the destination's hold line is cut off with the route's tail
    // (TruncateToHoldShort). The pilot is told, in a sentence of its own.
    [Fact]
    public void BeyondDestinationHoldNote_NamesThePointAndTheRunway()
    {
        Assert.Equal(
            "Note: holding point L2 lies beyond the Runway 16 hold short point, so no stop is called there.",
            L2.BeyondDestinationHoldNote("Runway 16"));
    }
}

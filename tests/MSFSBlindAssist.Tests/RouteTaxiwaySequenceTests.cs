// Characterization tests for MSFSBlindAssist.Navigation.RouteTaxiwaySequence.
//
// The rule these pin used to live as three hand-copied loops (LoadRoute's spoken summary,
// the "Route changed" via-list, and the remaining-sequence half of the recalculation's
// no-op guard). Two of the three SPEAK their result, so a drift between them is a drift in
// what the pilot is told the route is.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RouteTaxiwaySequenceTests
{
    private static List<TaxiRouteSegment> Route(params string[] names)
    {
        var list = new List<TaxiRouteSegment>();
        foreach (var name in names)
            list.Add(new TaxiRouteSegment { TaxiwayName = name });
        return list;
    }

    [Fact]
    public void CollapsesARunOfSegmentsSharingAName()
    {
        Assert.Equal(
            new[] { "A", "B" },
            RouteTaxiwaySequence.DistinctConsecutive(Route("A", "A", "A", "B", "B")));
    }

    // Only ADJACENT repeats collapse. A route that leaves a taxiway and comes back names it
    // twice — that is what the pilot taxis and what ATC cleared (the KBOS pattern, where a
    // clearance legitimately reuses a taxiway across a runway crossing).
    [Fact]
    public void KeepsATaxiwayThatTheRouteReturnsToLater()
    {
        Assert.Equal(
            new[] { "N", "E", "N" },
            RouteTaxiwaySequence.DistinctConsecutive(Route("N", "N", "E", "N")));
    }

    [Fact]
    public void DropsUnnamedSegmentsWithoutBreakingARun()
    {
        // The blank is the route's own snap stub. It must not split "A" into two legs.
        Assert.Equal(
            new[] { "A" },
            RouteTaxiwaySequence.DistinctConsecutive(Route("A", "", "A")));
    }

    [Fact]
    public void ComparesNamesIgnoringCase()
    {
        Assert.Equal(
            new[] { "Link 5" },
            RouteTaxiwaySequence.DistinctConsecutive(Route("Link 5", "LINK 5", "link 5")));
    }

    // The recalculation's no-op guard passes its segment cursor straight in.
    [Fact]
    public void StartsAtTheRequestedCursor()
    {
        Assert.Equal(
            new[] { "C", "D" },
            RouteTaxiwaySequence.DistinctConsecutive(Route("A", "B", "C", "D"), start: 2));
    }

    // A cursor of -1 is the manager's "not positioned yet" value; it must read the whole route
    // rather than throw.
    [Fact]
    public void TreatsANegativeCursorAsTheStartOfTheRoute()
    {
        Assert.Equal(
            new[] { "A", "B" },
            RouteTaxiwaySequence.DistinctConsecutive(Route("A", "B"), start: -1));
    }

    [Fact]
    public void ReturnsEmptyForACursorPastTheEnd()
    {
        Assert.Empty(RouteTaxiwaySequence.DistinctConsecutive(Route("A", "B"), start: 9));
    }

    // The recalculation reads `_route?.Segments`, which is null before any route is loaded.
    [Fact]
    public void ReturnsEmptyForANullRoute()
    {
        Assert.Empty(RouteTaxiwaySequence.DistinctConsecutive(null));
    }
}

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

    // ── What the route uses BEYOND the taxiways the pilot entered ──────────────────────────
    //
    // LOWW, 2026-11-14: the pilot entered "L" for Runway 16 and the summary said "via L" for
    // a route that ran L, W, EX23, E, EX33, D: 888 of its 2,001 m were on taxiways nobody
    // named. The router fills in everything after the last entered taxiway on its own, and
    // the summary has to say so.

    [Fact]
    public void ViaClause_NamesTheTaxiwaysTheRouteAddsAfterTheEnteredOnes()
    {
        Assert.Equal(
            "L, then W, EX23, E, EX33, D",
            RouteTaxiwaySequence.ViaClause(
                new[] { "L" },
                Route("L", "L", "W", "EX23", "EX23", "E", "EX33", "D")));
    }

    [Fact]
    public void ViaClause_IsTheEnteredListAloneWhenTheRouteEndsOnIt()
    {
        Assert.Equal(
            "A, B, K",
            RouteTaxiwaySequence.ViaClause(new[] { "A", "B", "K" }, Route("A", "B", "B", "K")));
    }

    // An unnamed stand lead-in or runway stub after the last entered taxiway adds no name.
    [Fact]
    public void ViaClause_IgnoresUnnamedPavementAfterTheEnteredOnes()
    {
        Assert.Equal(
            "A, B",
            RouteTaxiwaySequence.ViaClause(new[] { "A", "B" }, Route("A", "B", "", "")));
    }

    // The entered part stays exactly as the pilot gave it: a connector the route crosses
    // BETWEEN two entered taxiways is not announced (the pilot read back "A, K").
    [Fact]
    public void ViaClause_KeepsTheEnteredPartAsEnteredAndAddsOnlyWhatFollowsIt()
    {
        Assert.Equal(
            "A, K, then K5",
            RouteTaxiwaySequence.ViaClause(new[] { "A", "K" }, Route("A", "A3", "K", "K5")));
    }

    // "A, C, A" ([RTE-33]): the walk follows the entered order, so the second A is the one
    // the tail starts after, not the first.
    [Fact]
    public void ViaClause_WalksARepeatedTaxiwayInOrder()
    {
        Assert.Equal(
            "A, C, A, then D",
            RouteTaxiwaySequence.ViaClause(new[] { "A", "C", "A" }, Route("A", "C", "A", "D")));
    }

    // A route that leaves the last entered taxiway and comes back to it says so: the return
    // is part of what the router added.
    [Fact]
    public void ViaClause_NamesAReturnToTheLastEnteredTaxiway()
    {
        Assert.Equal(
            "L, then W, L",
            RouteTaxiwaySequence.ViaClause(new[] { "L" }, Route("L", "W", "L")));
    }

    private static List<TaxiRouteSegment> Measured(params (string Name, double Metres)[] parts)
    {
        var list = new List<TaxiRouteSegment>();
        foreach (var (name, metres) in parts)
            list.Add(new TaxiRouteSegment { TaxiwayName = name, DistanceMeters = metres });
        return list;
    }

    // OMDB, 2026-11-24: K4 entered for Runway 30R and driven straight over Z and K, whose
    // junction pieces carry the crossing taxiways' names. The summary read "via K4, then Z, K,
    // K4, M7B, M, M13": the rest of K4 itself, announced as something the router had added.
    [Fact]
    public void ViaClause_DoesNotNameTheJunctionsTheLastEnteredTaxiwayCrosses()
    {
        Assert.Equal(
            "K4, then M7B, M, M13",
            RouteTaxiwaySequence.ViaClause(new[] { "K4" }, Measured(
                ("Z", 150), ("K4", 12), ("Z", 9), ("K", 28), ("K4", 190), ("M7B", 120), ("M", 1300), ("M13", 80))));
    }

    // More than one junction on the way, and unnamed pavement inside one, are crossed the same.
    [Fact]
    public void ViaClause_CrossesEveryJunctionOnTheLastEnteredTaxiway()
    {
        Assert.Equal(
            "L, then W",
            RouteTaxiwaySequence.ViaClause(new[] { "L" }, Measured(
                ("L", 400), ("EX2", 45), ("L", 300), ("", 10), ("EX3", 40), ("", 10), ("L", 250), ("W", 600))));
    }

    // A way round by another taxiway and back is not a junction: it is longer than one is wide.
    [Theory]
    [InlineData(120.0, "L")]
    [InlineData(121.0, "L, then W, L")]
    public void ViaClause_StillNamesAWayRoundAndBack(double metresOnW, string expected)
    {
        Assert.Equal(expected, RouteTaxiwaySequence.ViaClause(
            new[] { "L" }, Measured(("L", 400), ("W", metresOnW), ("L", 300))));
    }

    // An entered taxiway the route only touches at a junction has no segment of its own.
    // It is skipped in the walk rather than ending it, so the tail still starts after the
    // last entered taxiway the route did run along.
    [Fact]
    public void ViaClause_SkipsAnEnteredTaxiwayTheRouteNeverRunsAlong()
    {
        Assert.Equal(
            "A, B, C, then D",
            RouteTaxiwaySequence.ViaClause(new[] { "A", "B", "C" }, Route("A", "C", "D")));
    }

    [Fact]
    public void ViaClause_ComparesNamesIgnoringCase()
    {
        Assert.Equal(
            "Link 5, then B",
            RouteTaxiwaySequence.ViaClause(new[] { "Link 5" }, Route("LINK 5", "B")));
    }

    // The summary is built inside TaxiGuidanceManager, which needs a graph and a provider to
    // run. This reads its source instead: the via-list of a route that honoured a clearance
    // must come from ViaClause. Joining route.TaxiwaySequence there again is exactly the LOWW
    // summary, "via L" for a route that went on over five more taxiways.
    [Fact]
    public void TheRouteSummaryBuildsItsViaListWithViaClause()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "MSFSBlindAssist.sln")))
            root = Path.GetDirectoryName(root)
                ?? throw new InvalidOperationException("MSFSBlindAssist.sln was not found");
        string source = File.ReadAllText(Path.Combine(
            root, "MSFSBlindAssist", "Services", "TaxiGuidanceManager.Announcements.cs"));
        string body = source[source.IndexOf("private string BuildRouteSummary(", StringComparison.Ordinal)..];

        Assert.Contains("RouteTaxiwaySequence.ViaClause(route.TaxiwaySequence, route.Segments)", body);
        Assert.DoesNotContain("string.Join(\", \", route.TaxiwaySequence)", body);
    }

    // NONE of the entered taxiways has a segment on the route (a single entered taxiway that
    // the route only touches at a junction). There is then no "last entered taxiway" to
    // start after: returning the whole route put the lead-in taxiway behind "then" as well,
    // a second time beside the lead-in clause. The summary keeps to what was entered.
    [Fact]
    public void ViaClause_AddsNothingWhenNoEnteredTaxiwayIsOnTheRoute()
    {
        Assert.Equal("B", RouteTaxiwaySequence.ViaClause(new[] { "B" }, Route("A", "C", "D")));
        Assert.Empty(RouteTaxiwaySequence.BeyondEntered(new[] { "B", "E" }, Route("A", "C", "D")));
    }

    [Fact]
    public void BeyondEntered_IsEmptyForANullRouteOrNoEnteredTaxiways()
    {
        Assert.Empty(RouteTaxiwaySequence.BeyondEntered(new[] { "A" }, null));
        Assert.Empty(RouteTaxiwaySequence.BeyondEntered(Array.Empty<string>(), Route("A", "B")));
    }
}

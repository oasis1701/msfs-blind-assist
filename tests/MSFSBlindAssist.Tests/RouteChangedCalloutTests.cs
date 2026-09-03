// Unit tests for MSFSBlindAssist.Navigation.RouteChangedCallout — the spoken callout for a
// route the off-route detector RECALCULATED mid-taxi.
//
// Motivating gap (found in the final review of the recalc hold-short fix, 2026-09-03):
// LoadRoute's route summary NAMES the runways a route crosses ("crossing runways 04L, 04R
// and 27") — added after KSFO 2026-07-01, where a pilot heard two unexplained "hold short of
// runway 10L" callouts, perceived a giant loop, and doubted correct guidance. A recalculated
// route can change which runways are crossed, yet its callout named only the new taxiways, so
// the pilot got the hold-short callouts with no warning of the route's shape — the exact trust
// failure the LoadRoute clause exists to prevent, on the path where the route just changed
// under them.
//
// The composition is pure so the excludeLastSegment rule (the subtle half) is pinned here
// rather than only in the sim.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RouteChangedCalloutTests
{
    private static TaxiRouteSegment Seg(string? holdShortRunway = null) => new()
    {
        FromNode = new TaxiNode { NodeId = 1 },
        ToNode = new TaxiNode { NodeId = 2 },
        IsHoldShortPoint = holdShortRunway != null,
        HoldShortRunway = holdShortRunway,
    };

    private static List<TaxiRouteSegment> Plain(int count)
    {
        var list = new List<TaxiRouteSegment>();
        for (int i = 0; i < count; i++) list.Add(Seg());
        return list;
    }

    // --- the existing wording must not drift ------------------------------------------

    [Fact]
    public void NoCrossings_KeepsTheExistingWordingExactly()
    {
        string callout = RouteChangedCallout.Compose(
            new[] { "Z", "D" }, "380 metres", "Runway 04R", Plain(4), isRunwayDestination: true);

        Assert.Equal("Route changed. Now via Z, D. 380 metres to Runway 04R.", callout);
    }

    [Fact]
    public void NoTaxiwayNames_KeepsTheExistingShortForm()
    {
        string callout = RouteChangedCallout.Compose(
            Array.Empty<string>(), "120 metres", "Gate A 29", Plain(3), isRunwayDestination: false);

        Assert.Equal("Route changed. 120 metres to Gate A 29.", callout);
    }

    // --- the gap this closes -----------------------------------------------------------

    // The crossing clause rides with the TAXIWAY list, ahead of the distance — deliberately
    // NOT appended after the destination. Two reasons, both raised in review:
    //  - A destination name can itself contain commas: ParkingSpot.Describe() appends the
    //    terminal and online aliases ("A 24A - Gate Medium, also A24 (online)"). Tacked on
    //    after that, ", crossing runway 09L" reads as one more item in the gate's name.
    //  - AnnounceInstruction is AnnounceImmediate, and every announce latch has just been
    //    reset one line above the call site, so the next position frame's turn/approach
    //    callout can truncate this sentence. Truncation takes the END, so the runway-safety
    //    clause must not be the last thing said. Losing the distance instead is survivable.
    [Fact]
    public void Crossings_AreNamedWithTheTaxiwaysAheadOfTheDistance()
    {
        var segs = Plain(6);
        segs[1].IsHoldShortPoint = true; segs[1].HoldShortRunway = "runway 26R";
        segs[3].IsHoldShortPoint = true; segs[3].HoldShortRunway = "runway 04L";

        string callout = RouteChangedCallout.Compose(
            new[] { "D" }, "2.6 kilometres", "Runway 04R", segs, isRunwayDestination: true);

        Assert.Equal(
            "Route changed. Now via D, crossing runways 26R and 04L. 2.6 kilometres to Runway 04R.",
            callout);
    }

    // With no taxiway list the clause has to start its own sentence, so it is capitalised.
    [Fact]
    public void Crossings_StandAloneAndAreCapitalised_WhenThereAreNoTaxiwayNames()
    {
        var segs = Plain(4);
        segs[1].IsHoldShortPoint = true; segs[1].HoldShortRunway = "runway 26R";

        string callout = RouteChangedCallout.Compose(
            Array.Empty<string>(), "900 metres", "Runway 08L", segs, isRunwayDestination: true);

        Assert.Equal("Route changed. Crossing runway 26R. 900 metres to Runway 08L.", callout);
    }

    // The case that drove the ordering: a real GSX gate label carries commas of its own.
    [Fact]
    public void CommaBearingDestinationName_CannotSwallowTheCrossingClause()
    {
        var segs = Plain(4);
        segs[1].IsHoldShortPoint = true; segs[1].HoldShortRunway = "runway 09L";

        string callout = RouteChangedCallout.Compose(
            new[] { "B" }, "400 metres", "A 24A - Gate Medium, also A24 (online)", segs,
            isRunwayDestination: false);

        Assert.Equal(
            "Route changed. Now via B, crossing runway 09L. 400 metres to " +
            "A 24A - Gate Medium, also A24 (online).",
            callout);
        // The safety clause must finish before the gate name begins.
        Assert.True(callout.IndexOf("crossing runway 09L", StringComparison.Ordinal)
                    < callout.IndexOf("A 24A", StringComparison.Ordinal));
    }

    // --- the subtle half: the destination's own hold is NOT a crossing -----------------

    // TruncateToHoldShort tags the FINAL segment of a runway route purely as the countdown
    // rail for the destination's own hold-short. Announcing it as a crossing would tell the
    // pilot they cross the runway they are taxiing to. LoadRoute's summary excludes it; so
    // must this, by the same rule.
    [Fact]
    public void RunwayDestination_DoesNotAnnounceItsOwnFinalHoldAsACrossing()
    {
        var segs = Plain(4);
        segs[^1].IsHoldShortPoint = true; segs[^1].HoldShortRunway = "Runway 04R";

        string callout = RouteChangedCallout.Compose(
            new[] { "D" }, "800 metres", "Runway 04R", segs, isRunwayDestination: true);

        Assert.Equal("Route changed. Now via D. 800 metres to Runway 04R.", callout);
    }

    [Fact]
    public void RunwayDestination_StillAnnouncesEarlierCrossingsOfTheDestinationStrip()
    {
        var segs = Plain(5);
        segs[1].IsHoldShortPoint = true; segs[1].HoldShortRunway = "runway 04R at D5";
        segs[^1].IsHoldShortPoint = true; segs[^1].HoldShortRunway = "Runway 04R";

        string callout = RouteChangedCallout.Compose(
            new[] { "D" }, "1.2 kilometres", "Runway 04R", segs, isRunwayDestination: true);

        Assert.Equal(
            "Route changed. Now via D, crossing runway 04R. 1.2 kilometres to Runway 04R.",
            callout);
    }

    // A gate route has no TruncateToHoldShort pass, so its final segment carries no rail to
    // exclude — a hold-short tagged there is a real crossing and must be named.
    [Fact]
    public void GateDestination_DoesNotExcludeItsFinalSegment()
    {
        var segs = Plain(3);
        segs[^1].IsHoldShortPoint = true; segs[^1].HoldShortRunway = "runway 09L";

        string callout = RouteChangedCallout.Compose(
            new[] { "B" }, "400 metres", "Gate B 12", segs, isRunwayDestination: false);

        Assert.Equal(
            "Route changed. Now via B, crossing runway 09L. 400 metres to Gate B 12.", callout);
    }

    // --- flows through Describe's own rules --------------------------------------------

    [Fact]
    public void OneRunwayCrossedTwice_ReadsAsTwiceNotAsTwoRunways()
    {
        var segs = Plain(6);
        segs[1].IsHoldShortPoint = true; segs[1].HoldShortRunway = "runway 28R";
        segs[3].IsHoldShortPoint = true; segs[3].HoldShortRunway = "runway 10L";  // reciprocal

        string callout = RouteChangedCallout.Compose(
            new[] { "Q" }, "1.1 kilometres", "Runway 10R", segs, isRunwayDestination: true);

        Assert.Contains("twice", callout);
        Assert.DoesNotContain("crossing runways", callout);
    }

    // A hold-short whose label names no runway (a user checkbox hold, "end of taxiway B") is
    // NOT a crossing and must not be counted as one. The recalc deliberately says nothing
    // about non-runway holds — it does not re-apply the pilot's per-row picks at all.
    [Fact]
    public void NonRunwayHoldShorts_AreNotAnnouncedAsCrossings()
    {
        var segs = Plain(4);
        segs[1].IsHoldShortPoint = true; segs[1].HoldShortRunway = "end of taxiway B";

        string callout = RouteChangedCallout.Compose(
            new[] { "B" }, "300 metres", "Gate C 4", segs, isRunwayDestination: false);

        Assert.Equal("Route changed. Now via B. 300 metres to Gate C 4.", callout);
    }

    [Fact]
    public void EmptyRoute_IsHandledWithoutThrowing()
    {
        string callout = RouteChangedCallout.Compose(
            new[] { "A" }, "0 metres", "Runway 09", Array.Empty<TaxiRouteSegment>(),
            isRunwayDestination: true);

        Assert.Equal("Route changed. Now via A. 0 metres to Runway 09.", callout);
    }
}

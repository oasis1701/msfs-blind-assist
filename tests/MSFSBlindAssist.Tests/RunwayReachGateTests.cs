// Characterization tests for MSFSBlindAssist.Navigation.RunwayReachGate — the pure
// decision core behind "does this taxi route actually reach the runway?".
//
// Safety-critical: the verdict drives BOTH the spoken route-load warning and
// TaxiGuidanceManager's during-lineup bailout ("This route does not reach Runway X"),
// so a false Reaches leaves a blind pilot steering at an unreachable runway with the
// tone panning forever (PHNL 04L, ~4 minutes), and a false failure barks at a perfectly
// good departure (LPPT 02 legitimately starts its lineup 458 ft off the centerline).
//
// It exists because the same rule was written out TWICE in TaxiGuidanceManager — as an
// if/else-if chain in LoadRoute and as an inverted &&/|| expression in
// TryRecalculateRoute — and the two had already drifted apart on the no-route-end case.
// One core, one matrix, both call sites.
//
// The walk probe is a Func because it is a bounded Dijkstra: the order of the guards is
// itself load-bearing, and these tests pin that the probe is NOT run when a cheaper
// guard already settled the answer.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayReachGateTests
{
    private const double MaxCross = 120.0;   // TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M
    private const double MaxWalk = 400.0;    // TaxiGuidanceManager.RUNWAY_REACH_MAX_WALK_M

    private static Func<double> Walk(double metres, Action? onCall = null) =>
        () => { onCall?.Invoke(); return metres; };

    private static Func<double> NeverCalled() =>
        () => throw new Xunit.Sdk.XunitException("the walk probe must not run on this path");

    // ---- A gate destination is never judged against the runway rules ----

    [Fact]
    public void AGateDestinationAlwaysReaches()
    {
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: false,
            destinationCrossMeters: 9999.0, maxCrossMeters: MaxCross,
            routeContainedDestination: false, endIsRunwayHold: false, hasRouteEnd: true,
            walkProbeMeters: NeverCalled(), maxWalkMeters: MaxWalk);

        Assert.Equal(RunwayReachVerdict.Reaches, v.Verdict);
    }

    // ---- Guard 1: the destination node sits off to the side ----

    [Fact]
    public void ADestinationNodeWellOffTheCentrelineEndsAsideOfTheRunway()
    {
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 456.0, maxCrossMeters: MaxCross,
            routeContainedDestination: true, endIsRunwayHold: false, hasRouteEnd: true,
            walkProbeMeters: NeverCalled(), maxWalkMeters: MaxWalk);

        Assert.Equal(RunwayReachVerdict.EndsAsideOfRunway, v.Verdict);
        Assert.Equal(456.0, v.CrossMeters);
    }

    // ---- Guard 2: the route did reach the destination node ----

    [Fact]
    public void ARouteThatContainedTheDestinationNodeReaches()
    {
        // The normal departure. Truncation later moves the end back to a hold line, which
        // is why this is captured BEFORE TruncateToHoldShort runs.
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 6.8, maxCrossMeters: MaxCross,
            routeContainedDestination: true, endIsRunwayHold: false, hasRouteEnd: true,
            walkProbeMeters: NeverCalled(), maxWalkMeters: MaxWalk);

        Assert.Equal(RunwayReachVerdict.Reaches, v.Verdict);
    }

    // ---- Guard 3: the end is a hold named for this runway ----

    [Fact]
    public void AnEndOnThisRunwaysOwnHoldReaches()
    {
        // Covers a set-back CAT II/III hold (EGKK A3, 162 m off) and the sparse GA fields
        // where a legitimate hold is a long taxi from the pavement.
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 10.0, maxCrossMeters: MaxCross,
            routeContainedDestination: false, endIsRunwayHold: true, hasRouteEnd: true,
            walkProbeMeters: NeverCalled(), maxWalkMeters: MaxWalk);

        Assert.Equal(RunwayReachVerdict.Reaches, v.Verdict);
    }

    // ---- Guard 4: the graph walk ----

    [Fact]
    public void AnEndAShortTaxiFromThePavementReaches()
    {
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 10.0, maxCrossMeters: MaxCross,
            routeContainedDestination: false, endIsRunwayHold: false, hasRouteEnd: true,
            walkProbeMeters: Walk(54.0), maxWalkMeters: MaxWalk);

        Assert.Equal(RunwayReachVerdict.Reaches, v.Verdict);
        Assert.Equal(54.0, v.WalkMeters);
    }

    [Fact]
    public void AnEndOnAParallelTaxiwayStopsShortOfTheRunway()
    {
        // The real PHNL 04L shape: the clearance ended on a taxiway that only parallels
        // the runway, so the route stops hundreds of metres of taxiing away.
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 3.2, maxCrossMeters: MaxCross,
            routeContainedDestination: false, endIsRunwayHold: false, hasRouteEnd: true,
            walkProbeMeters: Walk(655.0), maxWalkMeters: MaxWalk);

        Assert.Equal(RunwayReachVerdict.StopsShortOfRunway, v.Verdict);
        Assert.Equal(655.0, v.WalkMeters);
    }

    [Fact]
    public void TheWalkThresholdIsInclusive()
    {
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 3.2, maxCrossMeters: MaxCross,
            routeContainedDestination: false, endIsRunwayHold: false, hasRouteEnd: true,
            walkProbeMeters: Walk(MaxWalk), maxWalkMeters: MaxWalk);

        Assert.Equal(RunwayReachVerdict.Reaches, v.Verdict);
    }

    // ---- The no-route-end case: the two call sites used to disagree here ----

    [Fact]
    public void ARouteWithNoUsableEndReachesRatherThanWarning()
    {
        // LoadRoute required a non-null end before it would warn; TryRecalculateRoute
        // treated a null end as "reaches". Same outcome, now stated once — and the walk
        // probe has nothing to measure from, so it must not run.
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 10.0, maxCrossMeters: MaxCross,
            routeContainedDestination: false, endIsRunwayHold: false, hasRouteEnd: false,
            walkProbeMeters: NeverCalled(), maxWalkMeters: MaxWalk);

        Assert.Equal(RunwayReachVerdict.Reaches, v.Verdict);
    }

    // ---- Probe laziness: the Dijkstra runs at most once, and only when needed ----

    [Fact]
    public void TheWalkProbeRunsOnceWhenItIsTheDecidingGuard()
    {
        int calls = 0;
        RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 3.2, maxCrossMeters: MaxCross,
            routeContainedDestination: false, endIsRunwayHold: false, hasRouteEnd: true,
            walkProbeMeters: Walk(655.0, () => calls++), maxWalkMeters: MaxWalk);

        Assert.Equal(1, calls);
    }

    // ---- DescribeFailure: what the pilot actually hears -------------------------------
    //
    // A blind pilot has no display to cross-check these against, so a number spoken here
    // has to be a number that was measured. "Unreachable" must not be rendered as the
    // search bound dressed up as a distance.

    private static string Metres(double m) => $"{m:F0} metres";

    [Fact]
    public void AReachingRouteHasNothingToSay()
    {
        Assert.Null(RunwayReachGate.DescribeFailure(
            new RunwayReachResult(RunwayReachVerdict.Reaches, 6.8, 0.0), "Runway 04L", Metres));
    }

    [Fact]
    public void AsideOfTheRunwayNamesTheSidewaysDistance()
    {
        string? s = RunwayReachGate.DescribeFailure(
            new RunwayReachResult(RunwayReachVerdict.EndsAsideOfRunway, 456.0, 0.0),
            "Runway 04L", Metres);

        Assert.NotNull(s);
        Assert.Contains("456 metres to the side of Runway 04L", s);
        Assert.Contains("reprogram", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMeasuredShortfallNamesTheTaxiingDistance()
    {
        string? s = RunwayReachGate.DescribeFailure(
            new RunwayReachResult(RunwayReachVerdict.StopsShortOfRunway, 3.2, 655.0),
            "Runway 04L", Metres);

        Assert.NotNull(s);
        Assert.Contains("655 metres of taxiing away", s);
    }

    [Fact]
    public void AnUnreachableRunwaySpeaksNoDistanceAtAll()
    {
        // The walk probe returns infinity when no path exists within its search bound.
        // Reporting the bound instead would tell the pilot "about 1500 metres of taxiing
        // away" — a specific, confident, fabricated number for a route with NO path.
        string? s = RunwayReachGate.DescribeFailure(
            new RunwayReachResult(RunwayReachVerdict.StopsShortOfRunway, 3.2, double.PositiveInfinity),
            "Runway 04L", Metres);

        Assert.NotNull(s);
        Assert.DoesNotContain("metres", s);
        Assert.DoesNotContain("1500", s);
        Assert.DoesNotContain("∞", s);
        Assert.Contains("no path", s, StringComparison.OrdinalIgnoreCase);
    }
}

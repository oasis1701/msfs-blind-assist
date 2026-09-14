// Characterization tests for RunwayReachGate's starter-extension verdict — a route that
// ends ON the runway's own centreline, BEHIND the threshold the navdata records.
//
// Regression pinned: OMDB runway 12R via taxiway K1, 2026-09-05 (issue #229).
//   SayIntentions cleared "taxi to holding point runway 12R via U, W, Z, Z9, K, K1". The
//   router honoured it in full (73 nodes, no fallback) and ended on K1 — and the reach gate
//   then said "this route stops short of Runway 12R, and does not reach the runway. The last
//   taxiway you entered does not connect to it. Check your taxiway entry and reprogram."
//
//   Measured against the reporter's own fs2024.sqlite:
//     * the route's end (node 571, the far end of K1) sits 20.1 m off runway 12R's
//       centreline and 699 m BEHIND its threshold;
//     * graph walk from there to the modelled runway pavement: 1,018 m, over the
//       RUNWAY_REACH_MAX_WALK_M 400 m threshold — hence the warning;
//     * X-Plane's apt.dat puts 12R's PHYSICAL pavement start 31 m from that node, with a
//       713 m DISPLACED THRESHOLD. MSFS models the runway from the displaced threshold
//       onward, so the whole full-length departure end of 12R is pavement the navdata does
//       not call runway at all.
//
//   So the clearance was correct, the advice ("reprogram") was unactionable — there is no
//   other taxiway to enter — and the premise ("does not connect to it") was false.
//
// The verdict this pins is deliberately NARROW and is MUTUALLY EXCLUSIVE with the failure
// the gate exists to catch. StopsShortOfRunway's motivating case is PHNL 04L: a clearance
// that ended on a taxiway PARALLELING the runway, with no connector. A parallel taxiway is
// laterally OFFSET, so it can never be inside the runway's own lateral corridor — which is
// exactly what this verdict requires. Nothing that warns today stops warning because of it.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RunwayReachStarterExtensionTests
{
    private const double MaxCross = 120.0;   // TaxiGuidanceManager.RUNWAY_REACH_MAX_CROSS_M
    private const double MaxWalk = 400.0;    // TaxiGuidanceManager.RUNWAY_REACH_MAX_WALK_M

    // The measured OMDB 12R figures.
    private const double OmdbBehindM = 699.0;
    private const double OmdbWalkM = 1018.0;

    private static Func<double> Walk(double metres) => () => metres;

    private static Func<double> NeverCalled() =>
        () => throw new Xunit.Sdk.XunitException("the walk probe must not run on this path");

    [Fact]
    public void A_route_ending_on_the_centreline_behind_the_threshold_reaches()
    {
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 0.0,
            maxCrossMeters: MaxCross,
            routeContainedDestination: false,
            endIsRunwayHold: false,
            hasRouteEnd: true,
            walkProbeMeters: Walk(OmdbWalkM),
            maxWalkMeters: MaxWalk,
            routeEndBehindThresholdMeters: OmdbBehindM);

        Assert.Equal(RunwayReachVerdict.ReachesBehindThreshold, v.Verdict);
        Assert.Equal(OmdbBehindM, v.BehindThresholdMeters);
    }

    [Fact]
    public void It_settles_the_answer_without_paying_for_the_walk()
    {
        // The walk is a bounded Dijkstra and the guard order is load-bearing, exactly as the
        // existing guards are. Being on the runway's own axis is a cheaper and more specific
        // answer than "how far would you taxi", so it must come first.
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 0.0,
            maxCrossMeters: MaxCross,
            routeContainedDestination: false,
            endIsRunwayHold: false,
            hasRouteEnd: true,
            walkProbeMeters: NeverCalled(),
            maxWalkMeters: MaxWalk,
            routeEndBehindThresholdMeters: OmdbBehindM);

        Assert.Equal(RunwayReachVerdict.ReachesBehindThreshold, v.Verdict);
    }

    [Fact]
    public void Without_the_measurement_the_old_verdict_is_unchanged()
    {
        // Every caller that cannot measure it (no centreline for the runway, or an end that
        // is not on the axis) passes null, and must get byte-identical behaviour to before.
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 0.0,
            maxCrossMeters: MaxCross,
            routeContainedDestination: false,
            endIsRunwayHold: false,
            hasRouteEnd: true,
            walkProbeMeters: Walk(OmdbWalkM),
            maxWalkMeters: MaxWalk,
            routeEndBehindThresholdMeters: null);

        Assert.Equal(RunwayReachVerdict.StopsShortOfRunway, v.Verdict);
    }

    [Fact]
    public void A_destination_node_off_to_the_side_still_outranks_it()
    {
        // EndsAsideOfRunway describes the DESTINATION node, not the route end, and it means
        // the entered clearance ended on a taxiway that only parallels the runway. That is a
        // different and worse fault, and it must keep its place at the top of the chain.
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: MaxCross + 1.0,
            maxCrossMeters: MaxCross,
            routeContainedDestination: false,
            endIsRunwayHold: false,
            hasRouteEnd: true,
            walkProbeMeters: NeverCalled(),
            maxWalkMeters: MaxWalk,
            routeEndBehindThresholdMeters: OmdbBehindM);

        Assert.Equal(RunwayReachVerdict.EndsAsideOfRunway, v.Verdict);
    }

    [Fact]
    public void A_gate_destination_is_still_never_judged()
    {
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: false,
            destinationCrossMeters: 9999.0,
            maxCrossMeters: MaxCross,
            routeContainedDestination: false,
            endIsRunwayHold: false,
            hasRouteEnd: true,
            walkProbeMeters: NeverCalled(),
            maxWalkMeters: MaxWalk,
            routeEndBehindThresholdMeters: OmdbBehindM);

        Assert.Equal(RunwayReachVerdict.Reaches, v.Verdict);
    }

    [Fact]
    public void The_advisory_names_the_distance_and_never_says_reprogram()
    {
        // The whole point. "Reprogram" is unactionable here — the clearance is correct and
        // there is no other taxiway to enter — and telling a pilot to re-enter a correct
        // clearance teaches them to distrust every reach warning, including the real ones.
        var v = new RunwayReachResult(
            RunwayReachVerdict.ReachesBehindThreshold, 0.0, 0.0, OmdbBehindM);

        string? note = RunwayReachGate.Describe(v, "Runway 12R", m => $"{m:F0} metres");

        Assert.NotNull(note);
        Assert.Contains("Runway 12R", note);
        Assert.Contains("699 metres", note);
        Assert.DoesNotContain("reprogram", note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("does not reach", note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stops short", note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_reaching_route_still_says_nothing_at_all()
    {
        Assert.Null(RunwayReachGate.Describe(
            new RunwayReachResult(RunwayReachVerdict.Reaches, 0.0, 0.0),
            "Runway 12R", m => $"{m:F0} metres"));
    }

    [Fact]
    public void The_parallel_taxiway_warning_is_untouched()
    {
        // PHNL 04L: the clearance ended on a taxiway paralleling the runway, 655 m of taxiing
        // from the pavement. A parallel taxiway is laterally offset, so it is never inside the
        // runway's corridor and never reports a behind-threshold distance — this is why the
        // new verdict cannot disarm the protection the gate was built for.
        var v = RunwayReachGate.Evaluate(
            isRunwayDestination: true,
            destinationCrossMeters: 3.2,
            maxCrossMeters: MaxCross,
            routeContainedDestination: false,
            endIsRunwayHold: false,
            hasRouteEnd: true,
            walkProbeMeters: Walk(655.0),
            maxWalkMeters: MaxWalk,
            routeEndBehindThresholdMeters: null);

        Assert.Equal(RunwayReachVerdict.StopsShortOfRunway, v.Verdict);
        Assert.Contains("reprogram",
            RunwayReachGate.Describe(v, "Runway 04L", m => $"{m:F0} metres")!,
            StringComparison.OrdinalIgnoreCase);
    }
}

namespace MSFSBlindAssist.Navigation;

/// <summary>How a runway-destination route relates to the runway it was built for.</summary>
public enum RunwayReachVerdict
{
    /// <summary>The route gets the aircraft to the runway.</summary>
    Reaches,
    /// <summary>The destination node itself sits well off to the side of the centerline.</summary>
    EndsAsideOfRunway,
    /// <summary>The route ends on pavement that does not connect onward to the runway.</summary>
    StopsShortOfRunway,
}

/// <summary>The verdict plus the measurement that produced it, for the spoken warning.</summary>
public readonly record struct RunwayReachResult(
    RunwayReachVerdict Verdict, double CrossMeters, double WalkMeters);

public static class RunwayReachGate
{
    /// <summary>
    /// Pure decision core for "does this route reach the runway?", used by BOTH
    /// TaxiGuidanceManager.LoadRoute (which speaks a warning) and TryRecalculateRoute
    /// (which arms the during-lineup bailout). It exists because the rule was previously
    /// written out twice in two different shapes — an if/else-if chain and an inverted
    /// &amp;&amp;/|| expression — which had already drifted apart on the no-route-end case.
    /// </summary>
    /// <param name="routeContainedDestination">Whether the route passed through the
    /// destination node, captured BEFORE TruncateToHoldShort — truncation legitimately
    /// moves a reaching route's end back to a hold line, so judging the post-truncation
    /// end reads every normal departure as ended-short.</param>
    /// <param name="hasRouteEnd">Whether the route has a usable end node at all. Without
    /// one there is nothing to measure, so the honest answer is Reaches, not a warning.</param>
    /// <param name="walkProbeMeters">How much further the aircraft would have to TAXI to
    /// be on the pavement. A bounded Dijkstra, so it is deferred: the guard order above it
    /// is load-bearing and this must not run when a cheaper guard already decided.</param>
    public static RunwayReachResult Evaluate(
        bool isRunwayDestination,
        double destinationCrossMeters,
        double maxCrossMeters,
        bool routeContainedDestination,
        bool endIsRunwayHold,
        bool hasRouteEnd,
        Func<double> walkProbeMeters,
        double maxWalkMeters)
    {
        // Gate destinations leave the runway-only safety net disarmed.
        if (!isRunwayDestination)
            return new RunwayReachResult(RunwayReachVerdict.Reaches, 0.0, 0.0);

        // The destination node is off to the side: the entered clearance ended on a
        // taxiway that only parallels the runway, with no connector to it.
        if (destinationCrossMeters > maxCrossMeters)
            return new RunwayReachResult(
                RunwayReachVerdict.EndsAsideOfRunway, destinationCrossMeters, 0.0);

        // Two independent "the route is fine" signals, either of which settles it without
        // paying for the walk: the route actually got to the destination node, or it
        // stopped exactly where a departure is supposed to stop.
        if (routeContainedDestination || !hasRouteEnd || endIsRunwayHold)
            return new RunwayReachResult(
                RunwayReachVerdict.Reaches, destinationCrossMeters, 0.0);

        double walk = walkProbeMeters();
        return new RunwayReachResult(
            walk > maxWalkMeters ? RunwayReachVerdict.StopsShortOfRunway : RunwayReachVerdict.Reaches,
            destinationCrossMeters, walk);
    }

    /// <summary>
    /// The spoken warning for a failing verdict, or null when the route reaches. Distance
    /// rendering is the caller's (it owns the pilot's metres/feet setting).
    /// </summary>
    public static string? DescribeFailure(
        RunwayReachResult result, string destinationName, Func<double, string> formatDistance)
    {
        switch (result.Verdict)
        {
            case RunwayReachVerdict.EndsAsideOfRunway:
                return $"Warning: this route ends about {formatDistance(result.CrossMeters)} to the " +
                       $"side of {destinationName} and does not reach the runway. You may be missing " +
                       $"the taxiway that connects to the runway. Check your taxiway entry and reprogram.";

            case RunwayReachVerdict.StopsShortOfRunway:
                // No path at all within the probe's search bound. Speaking the bound here
                // would render "unreachable" as a confident measured distance the pilot
                // cannot check, so say what is actually known instead.
                if (double.IsPositiveInfinity(result.WalkMeters))
                    return $"Warning: this route stops short of {destinationName} and never reaches " +
                           $"the runway. The last taxiway you entered has no path to it. Check your " +
                           $"taxiway entry and reprogram.";

                return $"Warning: this route stops short of {destinationName}, about " +
                       $"{formatDistance(result.WalkMeters)} of taxiing away, and does not reach the " +
                       $"runway. The last taxiway you entered does not connect to it. Check your " +
                       $"taxiway entry and reprogram.";

            default:
                return null;
        }
    }

    /// <summary>
    /// How far aside of the runway centreline a route's destination node must sit before the end
    /// of the route stops being treated as that runway's hold. Up to here a route that fails the
    /// reach check keeps its hold, its Continue and the lineup tone: the runway may be one unmapped
    /// connector away, and the conservative stop before it is worth having. Beyond it the runway is
    /// not there to hold short of or to enter: at WSSS, 2026-11-24, the scenery's taxiways stop
    /// 1,500 m from runway 02R/20L, and the route's end was spoken as "Runway 20L ahead",
    /// "Hold short of Runway 20L" and "Entering Runway 20L, heading 203. Turn right."
    /// </summary>
    public const double FarFromRunwayCrossMeters = 400.0;

    /// <summary>Whether the route ends too far aside of its runway to be spoken as that runway's
    /// hold (<see cref="FarFromRunwayCrossMeters"/>). Only the aside verdict carries a measured
    /// distance to judge; a route that stops short keeps the hold it has.</summary>
    public static bool EndsFarFromRunway(RunwayReachResult result)
        => result.Verdict == RunwayReachVerdict.EndsAsideOfRunway
           && result.CrossMeters > FarFromRunwayCrossMeters;

    /// <summary>What is said where such a route ends, in place of the runway hold.</summary>
    public static string DescribeRouteEnd(string destinationName)
        => $"Stop. End of this route. It does not reach {destinationName}. Reprogram the taxi " +
           "route, including the taxiway that connects to the runway.";
}

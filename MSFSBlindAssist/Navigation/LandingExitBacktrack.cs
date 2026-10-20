using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Which landing exits can only be left by turning round and backtracking up the runway,
/// and which earlier exit the backtrack should head for. ONE rule shared by the Landing
/// Exit form's "(backtrack required)" label and the rollout's guided backtrack, so the
/// list can never promise something the guidance does not do.
///
/// Motivating case — LROP 08R (2026-09-13): the last "D" is a turn-pad loop at the runway
/// end that never leaves the pavement; the way off is D at 7,793 ft. Picking it used to
/// end with "continue ahead until clear of the runway", which a turn pad cannot satisfy.
///
/// The first version of the label (End + not vacating + a usable exit behind) was checked
/// against every exit in the fs2020 DB and was wrong for 124 of 308: exits whose resolved
/// stop merely lands on a CROSSING runway (KDTW 09L/22L/27R), and ones the vacate walk
/// simply failed to find the way off. It is therefore also required that the graph has
/// no way off the runway without travelling more than <see cref="BacktrackAllowanceMetres"/>
/// back up the pavement — a sharp turn-back taxiway (off the pavement within that
/// allowance) is a turn, not a backtrack.
/// </summary>
public static class LandingExitBacktrack
{
    /// <summary>
    /// How far back up the runway pavement the search may go before a way off counts as a
    /// backtrack. Comfortably more than a turn-back taxiway's run on the pavement, far
    /// less than the distance between two exits.
    /// </summary>
    public const double BacktrackAllowanceMetres = 100.0;

    /// <summary>Graph distance budget for the way-off search.</summary>
    private const double SearchLimitMetres = 1500.0;

    private const double MetersPerDegLat = 111132.0;   // TaxiGraph's shared constant

    /// <summary>
    /// The exit a backtrack from <paramref name="exit"/> should head for, or null when
    /// no backtrack is needed (or none is possible). Requires, in order: an End exit;
    /// that does not vacate the runway itself (<paramref name="vacates"/>); from which
    /// the graph offers no way off without backtracking; and an EARLIER exit on the same
    /// runway that does vacate — the nearest such one.
    /// </summary>
    public static LandingExit? FindVia(TaxiGraph? graph, Runway? runway, LandingExit exit,
                                       IReadOnlyList<LandingExit> exits, Func<LandingExit, bool> vacates)
    {
        if (graph == null || runway == null || exit == null) return null;
        if (exit.ExitType != "End" || vacates(exit)) return null;
        if (CanLeaveWithoutBacktrack(graph, runway, exit.NodeId)) return null;

        LandingExit? via = null;
        foreach (var e in exits)
        {
            if (e.DistanceFromThresholdFeet >= exit.DistanceFromThresholdFeet) continue;
            if (via != null && e.DistanceFromThresholdFeet <= via.DistanceFromThresholdFeet) continue;
            if (!vacates(e)) continue;
            via = e;
        }
        return via;
    }

    /// <summary>
    /// Sets <see cref="LandingExit.BacktrackVia"/> on every exit, using the
    /// <see cref="LandingExit.VacatesRunway"/> verdicts already on the list.
    /// </summary>
    public static void Mark(TaxiGraph? graph, Runway? runway, IReadOnlyList<LandingExit> exits)
    {
        foreach (var e in exits)
            e.BacktrackVia = FindVia(graph, runway, e, exits, x => x.VacatesRunway);
    }

    /// <summary>
    /// The same vacate verdict the form and the rollout handoff use: the resolved stop
    /// point is off the landing runway's pavement AND clear of every other runway.
    /// </summary>
    public static bool Vacates(TaxiGraph? graph, LandingExit exit, IReadOnlyList<LandingExit> exits, Runway runway)
    {
        int dest = LandingExitDestination.Resolve(graph, exit, exits, runway, runway.Heading,
            out _, out double endLateralM, out _);
        return RunwayVacateResolver.IsOffPavement(endLateralM, runway)
            && RunwayVacateResolver.IsClearOfOtherRunways(graph, dest, runway, runway.Heading);
    }

    /// <summary>
    /// True when some node off the landing runway's pavement is reachable from
    /// <paramref name="junctionNodeId"/> without ever standing on
    /// the pavement more than <see cref="BacktrackAllowanceMetres"/> behind the junction.
    /// Nodes already off the pavement may lie anywhere (a turn-back taxiway is allowed).
    /// </summary>
    public static bool CanLeaveWithoutBacktrack(TaxiGraph graph, Runway runway, int junctionNodeId)
    {
        if (!graph.Nodes.TryGetValue(junctionNodeId, out var start)) return false;
        double h = runway.Heading * Math.PI / 180.0, cosH = Math.Cos(h), sinH = Math.Sin(h);
        (double along, double lateral) Project(TaxiNode n)
        {
            double latR = (runway.StartLat + n.Latitude) * 0.5 * Math.PI / 180.0;
            double dN = (n.Latitude - runway.StartLat) * MetersPerDegLat;
            double dE = (n.Longitude - runway.StartLon) * MetersPerDegLat * Math.Cos(latR);
            return (dE * sinH + dN * cosH, Math.Abs(dE * cosH - dN * sinH));
        }

        double startAlong = Project(start).along;
        var dist = new Dictionary<int, double> { [junctionNodeId] = 0.0 };
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(junctionNodeId, 0.0);
        while (queue.TryDequeue(out int id, out double d))
        {
            if (d > dist[id] || d > SearchLimitMetres) continue;
            if (!graph.Nodes.TryGetValue(id, out var node)) continue;
            // Off THIS runway's pavement is enough. Deliberately not "clear of every
            // runway": where the way ahead runs onto a crossing runway (24 small
            // crossing-runway fields in the DB — CYYB, EGPT, YKSC ...) the honest word is
            // the existing "no taxiway mapped clear of the runway" warning, not backtrack.
            var (_, lateral) = Project(node);
            if (RunwayVacateResolver.IsOffPavement(lateral, runway))
                return true;
            if (!graph.Adjacency.TryGetValue(id, out var edges)) continue;
            foreach (var e in edges)
            {
                if (!graph.Nodes.TryGetValue(e.ToNodeId, out var to)) continue;
                var (toAlong, toLateral) = Project(to);
                bool onPavement = !RunwayVacateResolver.IsOffPavement(toLateral, runway);
                if (onPavement && toAlong < startAlong - BacktrackAllowanceMetres) continue;
                double nd = d + e.DistanceMeters;
                if (dist.TryGetValue(e.ToNodeId, out double old) && nd >= old) continue;
                dist[e.ToNodeId] = nd;
                queue.Enqueue(e.ToNodeId, nd);
            }
        }
        return false;
    }
}

using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Picks the taxi-route destination node for a landing exit, and resolves it to a
/// point that is genuinely clear of the runway.
///
/// <para>Shared deliberately by TWO callers that must never disagree:
/// <c>TaxiGuidanceManager.ResolveExitHandoffDestination</c> (which uses it at the
/// LandingRollout → Taxiing handoff) and <c>LandingExitForm</c> (which uses it BEFORE
/// the flight, to warn that an exit has no mapped path off the runway). If the form
/// predicted with different logic than the handoff runs, it would clear an exit that
/// later strands the aircraft — or warn about one that works.</para>
/// </summary>
public static class LandingExitDestination
{
    /// <summary>
    /// The destination the route should terminate at, BEFORE the vacate walk.
    ///
    /// Priority:
    ///   (a) ApronNodeId — corridor-exit node computed by GetLandingExits' BFS.
    ///   (b) Furthest same-named non-End exit — multi-segment RETs (LEMD L5).
    ///   (c) The first adjacent node in the exit direction.
    ///   (d) NodeId — the junction itself (dead-end).
    ///
    /// None of these guarantees the aircraft ends up off the runway; that is
    /// <see cref="RunwayVacateResolver.ExtendClearOfRunway"/>'s job, applied after.
    /// </summary>
    public static int Pick(TaxiGraph? graph, LandingExit exit,
                           IReadOnlyList<LandingExit> allExits, out string source)
    {
        source = "none";
        if (exit == null) return 0;

        if (exit.ApronNodeId > 0 && exit.ApronNodeId != exit.NodeId)
        {
            source = "apron";
            return exit.ApronNodeId;
        }

        // Multi-segment RETs: the exit continues under the same taxiway name further
        // down the runway, and the furthest such node is the one clear of it.
        if (!string.IsNullOrEmpty(exit.TaxiwayName))
        {
            LandingExit? furthest = null;
            foreach (var e in allExits)
            {
                if (!string.Equals(e.TaxiwayName, exit.TaxiwayName, StringComparison.OrdinalIgnoreCase)) continue;
                if (e.NodeId == exit.NodeId) continue;
                if (e.DistanceFromThresholdFeet <= exit.DistanceFromThresholdFeet) continue;
                if (e.ExitType == "End") continue;
                if (furthest == null || e.DistanceFromThresholdFeet > furthest.DistanceFromThresholdFeet)
                    furthest = e;
            }
            if (furthest != null)
            {
                source = "sameNamedRet";
                return furthest.NodeId;
            }
        }

        int ext = FindExitExtensionNode(graph, exit.NodeId, exit.ExitBearingTrue);
        source = ext > 0 ? "ext" : "junction-only";
        return ext > 0 ? ext : exit.NodeId;
    }

    /// <summary>
    /// <see cref="Pick"/> followed by the vacate walk — the complete answer to "where
    /// does this exit put the aircraft?".
    /// </summary>
    /// <param name="endLateralM">Out: how far from the runway axis the stop point ends up.</param>
    public static int Resolve(TaxiGraph? graph, LandingExit exit,
                              IReadOnlyList<LandingExit> allExits,
                              Runway? runway, double runwayHeadingTrue,
                              out double startLateralM, out double endLateralM,
                              out string source)
    {
        int dest = Pick(graph, exit, allExits, out source);
        int vacated = RunwayVacateResolver.ExtendClearOfRunway(
            graph, dest, exit.NodeId, runway, runwayHeadingTrue,
            out startLateralM, out endLateralM);
        if (vacated != dest) source = $"{source}+vacate";
        return vacated;
    }

    /// <summary>
    /// Finds the first graph node adjacent to <paramref name="junctionNodeId"/> in
    /// approximately the exit direction. Used to extend landing-exit routes by one
    /// segment past the junction so the look-ahead walk (GuidanceGeometry.WalkTarget)
    /// can continue around the corner and start panning the tone before the junction.
    /// Returns -1 if no suitable node is found.
    /// </summary>
    private static int FindExitExtensionNode(TaxiGraph? graph, int junctionNodeId, double exitBearingTrue)
    {
        if (graph == null) return -1;
        if (!graph.Adjacency.TryGetValue(junctionNodeId, out var edges)) return -1;
        int best = -1;
        double bestDiff = 60.0; // must be within 60° of exit bearing
        foreach (var e in edges)
        {
            if (e.PathType == "R") continue; // skip runway edges
            double diff = Math.Abs(NormalizeAngle(e.BearingDegrees - exitBearingTrue));
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = e.ToNodeId;
            }
        }
        return best;
    }

    private static double NormalizeAngle(double deg)
    {
        while (deg > 180.0) deg -= 360.0;
        while (deg < -180.0) deg += 360.0;
        return deg;
    }
}

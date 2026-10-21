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
    /// Largest along-track gap between consecutive same-named exit junctions that
    /// still reads as one continuous rapid-exit arc. Real multi-segment RET nodes
    /// sit tens-to-hundreds of feet apart; the known same-named-but-separate pairs
    /// are 946 ft (EGLL 09R S5W U-link) and ≥ 1,400 ft (KBNA gap-fill re-admissions).
    /// </summary>
    private const double RET_CONTINUATION_MAX_STEP_FT = 500.0;

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
        //
        // CHAINED continuations only. A genuine multi-segment RET is several
        // same-named junction nodes in close succession along one arc, so each
        // step to the next is short. But `allExits` also legitimately carries
        // same-named exits that are PHYSICALLY SEPARATE turnoffs: the strict name
        // dedup is scoped to the fallback path (EGLL 09R keeps both S5W junctions,
        // 946 ft apart — a U-link, not one arc) and the KBNA coverage gap fill
        // re-admits same-named exits ≥ 1400 ft apart on single-letter-named
        // sceneries. An uncapped "furthest same-named" then resolved the vacate
        // destination to a SIBLING junction thousands of feet downfield (KBNA 20L:
        // chose G at ~3,700 ft, destination became the other G at ~6,155 ft),
        // steering the pilot off their exit and far along the runway-parallel
        // taxiway — and the form's VacatesRunway pre-check was evaluated at that
        // far sibling too. Walking the chain link by link, each within
        // RET_CONTINUATION_MAX_STEP_FT, keeps every real arc (consecutive nodes of
        // one RET are tens-to-hundreds of feet apart) and stops dead at the first
        // gap that means "different turnoff" (EGLL's 946 ft, KBNA's ≥ 1400 ft).
        if (!string.IsNullOrEmpty(exit.TaxiwayName))
        {
            var sameNamed = new List<LandingExit>();
            foreach (var e in allExits)
            {
                if (!string.Equals(e.TaxiwayName, exit.TaxiwayName, StringComparison.OrdinalIgnoreCase)) continue;
                if (e.NodeId == exit.NodeId) continue;
                if (e.DistanceFromThresholdFeet <= exit.DistanceFromThresholdFeet) continue;
                if (e.ExitType == "End") continue;
                sameNamed.Add(e);
            }
            sameNamed.Sort((a, b) => a.DistanceFromThresholdFeet.CompareTo(b.DistanceFromThresholdFeet));

            LandingExit? furthest = null;
            double prevDistFt = exit.DistanceFromThresholdFeet;
            foreach (var e in sameNamed)
            {
                if (e.DistanceFromThresholdFeet - prevDistFt > RET_CONTINUATION_MAX_STEP_FT)
                    break;
                furthest = e;
                prevDistFt = e.DistanceFromThresholdFeet;
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
    /// Runtime correction for a vacate destination whose route from the exit JUNCTION starts by
    /// running BACK down the runway (first leg ≥ 3 m more than 120° off the landing heading).
    /// KDTW 03R P3: the chosen stop is on W3, reached by going back 150 m along W2 — a taxiway
    /// drawn down the centreline — so the handoff opened with "Make a U-turn to the right" on
    /// the runway at 29 kt, while P3's own pavement leaves at the junction. VirtualPilot
    /// (2026-09-18, 100 busiest airports): ~250 landings heard a U-turn on the runway.
    /// <para>Walks the exit's OWN named taxiway strictly away from the runway axis (≤ 600 m,
    /// until 60 m clear of the pavement edge), then applies the same
    /// <see cref="RunwayVacateResolver.ExtendClearOfRunway"/> every destination gets. The result
    /// is used only if it is clear of the pavement and of every other runway AND its own route
    /// does not start backwards; otherwise <paramref name="dest"/> is returned unchanged. A
    /// destination whose route does not start backwards is never touched, so only already
    /// broken handoffs can change. Deliberately NOT folded into <see cref="Resolve"/>: that feeds
    /// the exit sweeps and every invariant measured on them.</para>
    /// </summary>
    public static int CorrectBackwardsStart(TaxiGraph? graph, LandingExit exit, Runway? runway,
                                            double runwayHeadingTrue, int dest)
    {
        if (graph == null || runway == null || dest <= 0 || string.IsNullOrEmpty(exit.TaxiwayName)) return dest;
        if (!graph.Nodes.ContainsKey(exit.NodeId) || !PathStartsBackwards(graph, exit.NodeId, dest, runwayHeadingTrue)) return dest;

        double Lateral(TaxiNode n)
        {
            const double MPD = 111132.0;
            double h = runwayHeadingTrue * Math.PI / 180.0;
            double latMid = (n.Latitude + runway.StartLat) * 0.5 * Math.PI / 180.0;
            double dN = (n.Latitude - runway.StartLat) * MPD, dE = (n.Longitude - runway.StartLon) * MPD * Math.Cos(latMid);
            return Math.Abs(dE * Math.Cos(h) - dN * Math.Sin(h));
        }
        double halfWidthM = (runway.Width > 0 ? runway.Width : 150.0) * 0.3048 / 2.0;

        int cur = exit.NodeId, prev = -1;
        double walked = 0;
        while (walked < 600.0 && Lateral(graph.Nodes[cur]) < halfWidthM + 60.0)
        {
            if (!graph.Adjacency.TryGetValue(cur, out var edges)) break;
            double here = Lateral(graph.Nodes[cur]);
            TaxiEdge? best = null; double bestGain = 0.5;
            foreach (var e in edges)
            {
                if (e.ToNodeId == prev || !string.Equals(e.TaxiwayName, exit.TaxiwayName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!graph.Nodes.TryGetValue(e.ToNodeId, out var to)) continue;
                double gain = Lateral(to) - here;
                if (gain > bestGain) { bestGain = gain; best = e; }
            }
            if (best == null) break;
            walked += best.DistanceMeters; prev = cur; cur = best.ToNodeId;
        }
        if (prev < 0 || !RunwayVacateResolver.IsOffPavement(Lateral(graph.Nodes[cur]), runway)) return dest;

        int alt = RunwayVacateResolver.ExtendClearOfRunway(graph, cur, prev, runway, runwayHeadingTrue, out _, out double endLateralM);
        if (alt <= 0 || !RunwayVacateResolver.IsOffPavement(endLateralM, runway)
            || !RunwayVacateResolver.IsClearOfOtherRunways(graph, alt, runway, runwayHeadingTrue)
            || PathStartsBackwards(graph, exit.NodeId, alt, runwayHeadingTrue))
            return dest;
        return alt;
    }

    /// <summary>
    /// True when even after <see cref="CorrectBackwardsStart"/> the route from the exit junction
    /// to where the handoff would stop starts back down the runway — a reverse exit.
    /// </summary>
    public static bool RequiresTurnBack(TaxiGraph? graph, LandingExit exit, IReadOnlyList<LandingExit> allExits,
                                        Runway? runway, double runwayHeadingTrue)
    {
        if (graph == null || runway == null || !graph.Nodes.ContainsKey(exit.NodeId)) return false;
        int dest = Resolve(graph, exit, allExits, runway, runwayHeadingTrue, out _, out _, out _);
        return RequiresTurnBack(graph, exit, runway, runwayHeadingTrue, dest);
    }

    /// <summary>
    /// <see cref="RequiresTurnBack(TaxiGraph?, LandingExit, IReadOnlyList{LandingExit}, Runway?, double)"/>
    /// for a caller that has ALREADY run <see cref="Resolve"/> for this exit (the vacate screen), so the
    /// destination is not resolved twice.
    /// </summary>
    public static bool RequiresTurnBack(TaxiGraph? graph, LandingExit exit, Runway? runway,
                                        double runwayHeadingTrue, int resolvedDestination)
    {
        if (graph == null || runway == null || !graph.Nodes.ContainsKey(exit.NodeId)) return false;
        int dest = CorrectBackwardsStart(graph, exit, runway, runwayHeadingTrue, resolvedDestination);
        return dest > 0 && dest != exit.NodeId && PathTurnsBack(graph, exit.NodeId, dest, runwayHeadingTrue);
    }

    /// <summary>How far along the exit path <see cref="PathTurnsBack"/> looks.</summary>
    public const double TurnBackWindowMetres = 150.0;

    /// <summary>
    /// True when, within its first <see cref="TurnBackWindowMetres"/>, the shortest route from
    /// <paramref name="from"/> to <paramref name="to"/> has a leg (≥ 3 m) more than 120° off the
    /// landing heading, or turns more than 120° between two consecutive such legs. The first-leg
    /// test alone missed exits that HOOK back (KDTW 09L V2: 40°, 333°, 285° on an 89° runway;
    /// KMCI 01L E: a 4 m forward stub, then 193°) and exits that hairpin partway (YBBN 01R A7:
    /// 317° then 118°). VirtualPilot 2026-09-18: all spoke "Make a U-turn" unflagged.
    /// </summary>
    public static bool PathTurnsBack(TaxiGraph graph, int from, int to, double runwayHeadingTrue)
    {
        var r = new TaxiRouter(graph).FindShortestPath(from, to);
        if (r == null) return false;
        double run = 0;
        double? prev = null;
        foreach (var seg in r.Segments)
        {
            if (run > TurnBackWindowMetres) break;
            run += seg.DistanceMeters;
            if (seg.DistanceMeters < 3.0) continue;
            if (Off(seg.BearingDegrees, runwayHeadingTrue) > 120.0) return true;
            if (prev is { } p && Off(seg.BearingDegrees, p) > 120.0) return true;
            prev = seg.BearingDegrees;
        }
        return false;

        static double Off(double a, double b) => Math.Abs(((a - b) % 360 + 540) % 360 - 180);
    }

    /// <summary>True when the shortest route from <paramref name="from"/> to <paramref name="to"/> starts with a leg (≥ 3 m) more than 120° off the landing heading.</summary>
    public static bool PathStartsBackwards(TaxiGraph graph, int from, int to, double runwayHeadingTrue)
    {
        var r = new TaxiRouter(graph).FindShortestPath(from, to);
        if (r == null) return false;
        foreach (var seg in r.Segments)
        {
            if (seg.DistanceMeters < 3.0) continue;
            double d = Math.Abs(((seg.BearingDegrees - runwayHeadingTrue) % 360 + 540) % 360 - 180);
            return d > 120.0;
        }
        return false;
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
        // The search past the landed runway's own hold line (RunwayVacateResolver.
        // ExtendPastOwnHoldAhead) stands aside where the exit's path TURNS BACK: those exits
        // own their stop through CorrectBackwardsStart and the backwards-start retry, and
        // moving it made two of them worse (RJAA 34L A8, CYYC 11 A; VirtualPilot 2026-09-25).
        if (graph != null && runway != null)
        {
            int plain = RunwayVacateResolver.ExtendClearOfRunway(
                graph, dest, exit.NodeId, runway, runwayHeadingTrue,
                out double plainStartM, out double plainEndM, ownHoldSearch: false);
            if (plain != vacated && PathTurnsBack(graph, exit.NodeId, vacated, runwayHeadingTrue))
            {
                vacated = plain;
                startLateralM = plainStartM;
                endLateralM = plainEndM;
            }
        }
        if (vacated != dest) source = $"{source}+vacate";

        // The destination pick COMMITS the vacate walk to one branch of the
        // junction (the walk cannot step back through cameFrom), so where that
        // branch stalls inside the pavement, a walk anchored on the JUNCTION —
        // free to take any branch — can do better. Measured 2026-08-26 when the
        // unnamed-edge bearing fallback made "ext" picks possible at unnamed
        // junctions: 2,876 exits whose junction-anchored walk used to clear the
        // pavement stalled on the ext branch. Originally scoped to ext picks;
        // widened 2026-08-28 to EVERY pick that fails the off-pavement test —
        // the fallback-site parity work gave apron picks to exits that never had
        // one, and a handful (EGBG 33, SC41 15) resolve to an apron whose own
        // branch stalls on the pavement while the junction-anchored walk clears
        // it. Still gated on the primary answer having FAILED, so every pick
        // that resolves off-pavement — the long-measured population — is
        // byte-for-byte unaffected; taking whichever ends further out is a
        // strict floor.
        if (dest != exit.NodeId
            && !RunwayVacateResolver.IsOffPavement(endLateralM, runway))
        {
            int alt = RunwayVacateResolver.ExtendClearOfRunway(
                graph, exit.NodeId, exit.NodeId, runway, runwayHeadingTrue,
                out double altStartM, out double altEndM);
            if (altEndM > endLateralM + 0.5)
            {
                vacated = alt;
                startLateralM = altStartM;
                endLateralM = altEndM;
                source = $"{source}+junctionWalk";
            }
        }

        // Off-pavement arbitration for the chained-continuation cap. The cap is
        // what stops a KBNA-class far same-named SIBLING becoming the destination
        // — but at small fields the far same-named node is sometimes the ONLY
        // point the graph gets off the runway from (a parallel taxiway whose only
        // clear junction is at the far end): capped, the walk from the nearer
        // node ended ON the pavement (3V5 30: end lateral 0.0 m) where legacy's
        // far pick ended clear. Same pattern as the junctionWalk floor above:
        // gated on the primary answer having FAILED the off-pavement test, so
        // every exit the cap resolves correctly — including the KBNA shape it
        // exists for, whose capped answer IS off-pavement — is byte-for-byte
        // unaffected; and the fallback is accepted only when it genuinely gets
        // the aircraft off the runway.
        if (!RunwayVacateResolver.IsOffPavement(endLateralM, runway)
            && !string.IsNullOrEmpty(exit.TaxiwayName))
        {
            LandingExit? far = null;
            foreach (var e in allExits)
            {
                if (!string.Equals(e.TaxiwayName, exit.TaxiwayName, StringComparison.OrdinalIgnoreCase)) continue;
                if (e.NodeId == exit.NodeId || e.NodeId == dest) continue;
                if (e.DistanceFromThresholdFeet <= exit.DistanceFromThresholdFeet) continue;
                if (e.ExitType == "End") continue;
                if (far == null || e.DistanceFromThresholdFeet > far.DistanceFromThresholdFeet)
                    far = e;
            }
            if (far != null)
            {
                int farVacated = RunwayVacateResolver.ExtendClearOfRunway(
                    graph, far.NodeId, exit.NodeId, runway, runwayHeadingTrue,
                    out double farStartM, out double farEndM);
                if (RunwayVacateResolver.IsOffPavement(farEndM, runway))
                {
                    vacated = farVacated;
                    startLateralM = farStartM;
                    endLateralM = farEndM;
                    source = "sameNamedFar+vacate";
                }
            }
        }
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
            if (TaxiGraph.IsStandBridge(e)) continue; // skip fabricated stand bridges
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

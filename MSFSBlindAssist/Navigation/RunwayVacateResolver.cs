using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Pushes a landing-exit route destination far enough down the exit taxiway that
/// the aircraft ends up GENUINELY VACATED — past the runway-holding position, not
/// merely past the pavement edge.
///
/// <para>Motivating defect (EVRA runway 18 → taxiway B, 2026-08-07). The exit
/// junction node sits ON the runway centreline; the next node down B is 33 m
/// laterally (the runway half-width is 22.6 m) and the node after that 89 m. The
/// scenery's own HSND hold-short node — the painted line — is at 106 m. The
/// LandingRollout → Taxiing handoff routed to the FIRST adjacent node
/// (<c>FindExitExtensionNode</c>) and announced "hold position" with the aircraft
/// stopped 33 m from the centreline: ~10 m past the pavement edge, 73 m short of
/// the hold line, tail still in the runway strip. Tower could not clear anyone
/// else to line up and had to ask the pilot to continue past the hold line.</para>
///
/// <para>The existing <c>ApronNodeId</c> corridor BFS in
/// <see cref="TaxiGraph.GetLandingExits"/> can't be the whole answer here: its
/// tolerance is half-width + 15 m (37.6 m), which only means "off the pavement",
/// and it does not run for every exit-classification branch. This resolver is the
/// backstop applied at handoff time, so the stop point is correct regardless of
/// which branch produced the exit and whether <c>ApronNodeId</c> was computed.</para>
///
/// <para>Pure (graph + geometry in, node id out) so the placement rule is
/// unit-testable — no SimConnect, no live state.</para>
/// </summary>
public static class RunwayVacateResolver
{
    /// <summary>
    /// Lateral distance from the runway centreline at which an aircraft counts as
    /// clear. ICAO Annex 14 Table 3-2 puts the runway-holding position 90 m from
    /// the centreline for a code E precision-approach runway (75 m non-instrument,
    /// 107.5 m code F). 90 m is what a controller means by "vacated", and is well
    /// beyond every runway half-width — a pure pavement-edge test (the 37.6 m
    /// corridor tolerance) is NOT sufficient and must not be substituted here.
    /// </summary>
    public const double VacatedClearanceMetres = 90.0;

    /// <summary>
    /// Margin beyond the pavement edge below which the aircraft cannot be called off
    /// the runway at all. This is the WEAK test — "not physically on the concrete" —
    /// not <see cref="VacatedClearanceMetres"/>, which is what a controller means by
    /// vacated. It exists so the app can tell the pilot the difference between
    /// "stopped short of the hold line" (annoying) and "stopped ON the runway"
    /// (dangerous), and matches the corridor tolerance
    /// <c>TaxiGraph.GetLandingExits</c> already uses for "off the pavement".
    /// </summary>
    private const double OffPavementMarginMetres = 15.0;

    /// <summary>Half-width used when a runway carries no width in the navdata — TaxiGraph's own default.</summary>
    private const double DefaultHalfWidthMetres = 75.0 * 0.3048;

    /// <summary>
    /// True when a stop point <paramref name="lateralM"/> from the runway axis is at
    /// least off the pavement. Deliberately separate from the 90 m holding-position
    /// target: an exit can fail THAT and still be perfectly safe, but an exit that
    /// fails THIS leaves the aircraft on an active runway.
    /// </summary>
    public static bool IsOffPavement(double lateralM, Runway? runway)
    {
        double halfWidth = (runway != null && runway.Width > 0.0)
            ? runway.Width * 0.3048 / 2.0
            : DefaultHalfWidthMetres;
        return lateralM >= halfWidth + OffPavementMarginMetres;
    }

    /// <summary>
    /// Extra distance the walk may continue past a hold-short node so the whole
    /// airframe — not just the datum — ends up beyond the painted line. Applied
    /// only when the next node is within this distance, so a hold node followed by
    /// a long leg to the next junction stops AT the hold node rather than being
    /// dragged hundreds of metres downfield.
    /// </summary>
    private const double PastHoldMarginMetres = 60.0;

    /// <summary>Total walk budget from the starting destination node.</summary>
    private const double MaxWalkMetres = 400.0;

    /// <summary>Hop budget — a belt-and-braces cap for pathological graphs.</summary>
    private const int MaxHops = 8;

    private const double MetersPerDegLat = 111132.0;   // TaxiGraph's shared constant

    /// <summary>
    /// How far a centreline endpoint may sit from the landing runway's axis and still
    /// be considered the SAME runway. Comfortably above the few metres of scatter left
    /// after <c>TaxiGraph.SnapStartToRunwayCenterline</c>, and far below any parallel
    /// runway separation (EGKK's ~200 m pair is the tightest measured).
    /// </summary>
    private const double SameRunwayLateralM = 30.0;

    /// <summary>Heading agreement (or reciprocal) required for the same-runway match.</summary>
    private const double SameRunwayHeadingDeg = 20.0;

    /// <summary>
    /// Returns a destination node at least <see cref="VacatedClearanceMetres"/>
    /// from the landing runway's axis and, where the scenery models one, past the
    /// exit path's own hold-short node.
    /// </summary>
    /// <param name="graph">The taxi graph the route is being built on.</param>
    /// <param name="destNodeId">
    /// The destination the handoff would otherwise use (ApronNodeId / furthest
    /// same-named RET node / extension node / the junction itself).
    /// </param>
    /// <param name="cameFromNodeId">
    /// The node the route reaches <paramref name="destNodeId"/> FROM (the exit
    /// junction). Keeps the first step from doubling straight back onto the runway.
    /// Pass 0 or a negative value when unknown.
    /// </param>
    /// <param name="runway">The runway just landed on.</param>
    /// <param name="runwayHeadingTrue">Landing direction, degrees true.</param>
    /// <returns>
    /// The extended node id, or <paramref name="destNodeId"/> unchanged when it is
    /// already clear, when the graph is missing, or when no further node qualifies.
    /// Never returns a node CLOSER to the runway than the one passed in — a walk
    /// that cannot reach the clearance target degrades to the furthest node it did
    /// reach, which is still no worse than today's behaviour.
    /// </returns>
    public static int ExtendClearOfRunway(
        TaxiGraph? graph,
        int destNodeId,
        int cameFromNodeId,
        Runway? runway,
        double runwayHeadingTrue)
        => ExtendClearOfRunway(graph, destNodeId, cameFromNodeId, runway,
                               runwayHeadingTrue, out _, out _);

    /// <inheritdoc cref="ExtendClearOfRunway(TaxiGraph,int,int,Runway,double)"/>
    /// <param name="startLateralM">Out: lateral offset of the node passed in.</param>
    /// <param name="endLateralM">Out: lateral offset of the node returned.</param>
    public static int ExtendClearOfRunway(
        TaxiGraph? graph,
        int destNodeId,
        int cameFromNodeId,
        Runway? runway,
        double runwayHeadingTrue,
        out double startLateralM,
        out double endLateralM,
        bool ownHoldSearch = true)
    {
        // First pass WITH the another-runway hold-line gates: the walk never
        // steps onto or past a hold line that genuinely guards a different
        // runway (close parallels, crossing runways) — stopping before it is
        // the correct hold-short semantics. But at some fields the only path
        // off the landing runway's pavement leads through such a line, and a
        // stop ON the landed runway is strictly worse than one inside another
        // runway's protected area — so when the gated walk fails even the
        // off-pavement test, retry unrestricted (the legacy walk) and take
        // whichever result gets further from the runway. Every exit whose
        // gated walk clears the pavement keeps the hold-short stop; nothing
        // ends closer to the runway than the legacy walk achieved (whole-DB
        // sweep requirement: zero width-relative off-pavement regressions).
        int gated = ExtendClearOfRunwayCore(graph, destNodeId, cameFromNodeId,
            runway, runwayHeadingTrue, enforceHoldGates: true,
            out startLateralM, out endLateralM);
        if (graph == null || runway == null) return gated;
        if (IsOffPavement(endLateralM, runway))
            return ownHoldSearch
                ? ExtendPastOwnHoldAhead(graph, gated, runway, runwayHeadingTrue, ref endLateralM)
                : gated;

        int legacy = ExtendClearOfRunwayCore(graph, destNodeId, cameFromNodeId,
            runway, runwayHeadingTrue, enforceHoldGates: false,
            out double legacyStartM, out double legacyEndM);
        if (legacyEndM > endLateralM + 0.5)
        {
            startLateralM = legacyStartM;
            endLateralM = legacyEndM;
            return legacy;
        }
        return gated;
    }

    private static int ExtendClearOfRunwayCore(
        TaxiGraph? graph,
        int destNodeId,
        int cameFromNodeId,
        Runway? runway,
        double runwayHeadingTrue,
        bool enforceHoldGates,
        out double startLateralM,
        out double endLateralM)
    {
        startLateralM = 0.0;
        endLateralM = 0.0;

        if (graph == null || runway == null || destNodeId <= 0) return destNodeId;
        if (!graph.Nodes.TryGetValue(destNodeId, out var startNode)) return destNodeId;

        double hdgRad = runwayHeadingTrue * Math.PI / 180.0;
        double cosH = Math.Cos(hdgRad);
        double sinH = Math.Sin(hdgRad);

        // SIGNED offset from the runway axis: positive on one side, negative on the
        // other. The sign matters — see the `side` latch below.
        double SignedLateral(double lat, double lon)
        {
            double latR = (runway.StartLat + lat) * 0.5 * Math.PI / 180.0;
            double mPerLon = MetersPerDegLat * Math.Cos(latR);
            double dN = (lat - runway.StartLat) * MetersPerDegLat;
            double dE = (lon - runway.StartLon) * mPerLon;
            return dE * cosH - dN * sinH;
        }
        double Signed(TaxiNode n) => SignedLateral(n.Latitude, n.Longitude);
        double Lateral(TaxiNode n) => Math.Abs(Signed(n));

        // Which side of the runway the aircraft is leaving on. The walk may only
        // move FURTHER OUT on that side; without it, a taxiway that continues
        // straight ACROSS the runway would look like progress (its |offset| grows)
        // and the walk would route the pilot over the runway to the far side.
        //
        // The DESTINATION's own side wins whenever it is off the axis at all — that is
        // where the aircraft already is, so it is the side being vacated to. Only when
        // the destination is still sitting on the centreline is the junction →
        // destination step consulted, and if that is degenerate too the side stays 0
        // and the first accepted hop sets it. Reading the step FIRST is wrong and was
        // measured to be: where the junction node lies further off-axis than the
        // destination (EGKK, whose start rows are the known laterally-bogus ones), the
        // step points back toward the axis, and the walk then rated a node ON the
        // centreline as better than the 26 m one it started from.
        double sStart = Signed(startNode);
        double sFrom = (cameFromNodeId > 0 && graph.Nodes.TryGetValue(cameFromNodeId, out var fromNode))
            ? Signed(fromNode) : 0.0;
        int side = 0;
        if (Math.Abs(sStart) >= 0.5)              side = Math.Sign(sStart);
        else if (Math.Abs(sStart - sFrom) >= 0.5) side = Math.Sign(sStart - sFrom);

        // Distance made good away from the runway on the chosen side. Before the side
        // is known this is |offset|, which is the same thing for a node already off
        // to one side.
        double ProgressOn(TaxiNode n, int s) => s == 0 ? Math.Abs(Signed(n)) : s * Signed(n);
        double Progress(TaxiNode n) => ProgressOn(n, side);

        startLateralM = Lateral(startNode);
        endLateralM = startLateralM;

        // A destination sitting on a CROSSING runway can be arbitrarily far from the
        // LANDING runway's axis and still be the worst possible stop point. At a
        // runway intersection (KDTW 04R × 09L: exits Y/Y4/V resolve their ApronNodeId
        // to a node dead-centre on 09L, ~95 m from 04R's axis) the lateral-only test
        // below reads "already vacated" and the walk never runs — so guidance routed
        // the pilot onto 09L and announced "Off the runway. Stop and hold position."
        // Measured DB-wide 2026-08-26: 2,213 exits at 792 airports resolved onto
        // another runway's pavement. When the start node is on another runway the walk
        // MUST run (to escape it), and the never-closer contract floor is dropped to
        // zero — a node clear of every runway beats one parked on a crossing runway
        // regardless of its offset from the landing runway.
        bool startOnOtherRunway = IsOnDifferentRunway(graph, startNode, runway, runwayHeadingTrue);
        double contractFloorM = startOnOtherRunway ? 0.0 : startLateralM;

        // Already vacated — leave the destination alone. A destination that IS the
        // hold line still enters the walk, so it gets the same single tail-clearance
        // hop past the line that a destination reached BY walking does; otherwise an
        // ApronNodeId that happens to land on the hold node would stop with the
        // airframe straddling it while an identical geometry reached one hop earlier
        // would not.
        if (startLateralM >= VacatedClearanceMetres && !IsHoldNode(startNode)
            && !startOnOtherRunway)
            return destNodeId;

        int cur = destNodeId;
        int prev = cameFromNodeId > 0 ? cameFromNodeId : 0;
        double curLateral = Progress(startNode);
        double walked = 0.0;
        var visited = new HashSet<int> { destNodeId };
        if (prev > 0) visited.Add(prev);

        int best = destNodeId;
        double bestLateral = curLateral;

        for (int hop = 0; hop < MaxHops; hop++)
        {
            if (!graph.Adjacency.TryGetValue(cur, out var edges)) break;

            // Step to the neighbour that moves FURTHEST away from the runway axis
            // while continuing broadly in the direction of travel. Requiring a
            // strict lateral increase is what keeps the walk from turning back onto
            // the runway, or sideways onto a parallel taxiway that runs alongside
            // it — either of which would leave the pilot no better off.
            TaxiEdge? pick = null;
            double pickLateral = curLateral;
            foreach (var e in edges)
            {
                if (e.ToNodeId == prev) continue;
                if (visited.Contains(e.ToNodeId)) continue;
                if (string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase)) continue;
                if (TaxiGraph.IsStandBridge(e)) continue;
                if (!graph.Nodes.TryGetValue(e.ToNodeId, out var cand)) continue;

                // Never step onto ANOTHER runway's pavement — an exit that feeds
                // straight into a crossing runway must stop short of it, not taxi
                // across. The runway just landed on is explicitly EXCLUDED from that
                // block: sceneries routinely model the first node or two of the exit
                // taxiway still inside the runway width, and refusing to transit them
                // dead-ends the walk with the aircraft still on the runway — which is
                // the very failure this class exists to prevent (measured over 60
                // airports: 35 exits, e.g. KJFK 04L/J, VABB, EFHK, stopped 0-10 m from
                // the centreline because the only way out was one on-pavement node).
                // Crossing to the far side is prevented by the `side` latch, not by
                // this test.
                // EXCEPTION: when the walk STARTS on another runway (a crossing-
                // intersection exit — KDTW 04R × 09L, where the whole Y/Y4/V hub
                // sits inside 09L's corridor), transit through that pavement is the
                // only physical way off it, so on-runway candidates are traversable.
                // What must never happen is SETTLING there — the best-node update
                // below requires the settled node to be clear of every other runway.
                if (!startOnOtherRunway
                    && IsOnDifferentRunway(graph, cand, runway, runwayHeadingTrue)) continue;

                // Never step ONTO (and therefore never past) a hold-short line
                // that NAMES a different runway — the walk stops at the node
                // before it, which is exactly where an aircraft holds short. The
                // pavement test above cannot catch this: the protected area
                // between another runway's hold line and its pavement edge is
                // off-pavement by that test, yet parking there is the incursion
                // the line exists to prevent (close parallels, crossing runways).
                if (enforceHoldGates && IsHoldNode(cand)
                    && HoldGuardsAnotherRunway(graph, cand, runway, runwayHeadingTrue)) continue;

                double lat = Progress(cand);
                if (lat <= curLateral + 0.5) continue;   // must genuinely move away
                if (lat > pickLateral) { pick = e; pickLateral = lat; }
            }

            if (pick == null) break;

            // First hop off the centreline fixes which side we are vacating to, so
            // every later hop is measured against it and the walk cannot cross over.
            if (side == 0 && graph.Nodes.TryGetValue(pick.ToNodeId, out var firstCand))
            {
                double s = Signed(firstCand);
                if (Math.Abs(s) >= 0.5)
                {
                    side = Math.Sign(s);
                    curLateral = Progress(startNode);
                    pickLateral = Progress(firstCand);
                }
            }
            if (walked + pick.DistanceMeters > MaxWalkMetres) break;

            // Passing a hold-short node with the datum only just beyond it still
            // leaves the tail on the wrong side of the line. Take one more short
            // hop when the previous node was a hold line and this step is short
            // enough to be a continuation rather than a trek to the next junction.
            bool prevWasHold = graph.Nodes.TryGetValue(cur, out var curNode)
                               && IsHoldNode(curNode);
            if (prevWasHold && curLateral >= VacatedClearanceMetres
                && (pick.DistanceMeters > PastHoldMarginMetres
                    // The tail-clearance hop past a hold line is for the LANDING
                    // runway's own line only. The pick filter above keeps the walk
                    // from stepping ONTO another runway's named hold, so `cur` can
                    // normally only be one when the DESTINATION itself was placed
                    // on it — never step deeper into that runway's protected area.
                    || (enforceHoldGates
                        && HoldGuardsAnotherRunway(graph, curNode!, runway, runwayHeadingTrue))))
                break;

            walked += pick.DistanceMeters;
            prev = cur;
            cur = pick.ToNodeId;
            curLateral = pickLateral;
            visited.Add(cur);

            // Contract guard: the returned node is never CLOSER to the runway than the
            // one passed in. Side-relative progress alone does not enforce that — a
            // start node on the far side of the axis has negative progress, so a node
            // barely off the centreline would out-rank it and the walk would hand back
            // a WORSE stop point than it was given. Absolute offset is the thing the
            // caller and the pilot care about, so it gates the update.
            if (curLateral > bestLateral
                && graph.Nodes.TryGetValue(cur, out var curBestNode)
                && Lateral(curBestNode) >= contractFloorM
                // Never SETTLE on another runway — traversable (startOnOtherRunway
                // exception above) is not stoppable. For a start-clear walk this is
                // a no-op: on-runway candidates never became hops at all.
                && !(startOnOtherRunway
                     && IsOnDifferentRunway(graph, curBestNode, runway, runwayHeadingTrue)))
            {
                best = cur;
                bestLateral = curLateral;
            }

            if (curLateral >= VacatedClearanceMetres)
            {
                // Clear. Stop here unless this node is the hold line itself (the
                // loop then takes exactly one more short hop past it), or the walk
                // is still transiting another runway's pavement (keep escaping),
                // or the LANDING runway's own painted hold line is just one short
                // hop ahead — a scenery whose hold sits beyond the 90 m target
                // (CAT II/III set-backs run to 160 m; EVRA's own line is at 106 m)
                // with an ordinary node in the 90-105 m band used to stop BETWEEN
                // the clearance target and the paint, leaving the tail in the
                // strip the hold protects. Continuing that one hop lands on the
                // hold node, and the existing one-more-hop rule then clears it.
                bool haveN = graph.Nodes.TryGetValue(cur, out var n);
                bool stillOnOther = startOnOtherRunway && haveN
                    && IsOnDifferentRunway(graph, n!, runway, runwayHeadingTrue);
                bool ownHoldJustAhead = false;
                if (!(haveN && IsHoldNode(n!)) && !stillOnOther
                    && graph.Adjacency.TryGetValue(cur, out var aheadEdges))
                {
                    foreach (var ae in aheadEdges)
                    {
                        if (ae.ToNodeId == prev || visited.Contains(ae.ToNodeId)) continue;
                        if (ae.DistanceMeters > PastHoldMarginMetres) continue;
                        if (string.Equals(ae.PathType, "R", StringComparison.OrdinalIgnoreCase)) continue;
                        if (TaxiGraph.IsStandBridge(ae)) continue;
                        if (!graph.Nodes.TryGetValue(ae.ToNodeId, out var aheadN)) continue;
                        if (!IsHoldNode(aheadN) || HoldGuardsAnotherRunway(graph, aheadN, runway, runwayHeadingTrue)) continue;
                        if (Progress(aheadN) > curLateral + 0.5) { ownHoldJustAhead = true; break; }
                    }
                }
                if (!(haveN && IsHoldNode(n!)) && !stillOnOther && !ownHoldJustAhead)
                    break;
            }
        }

        // ---- FALLBACK: only when the greedy walk finished short of the target ----
        //
        // The walk above demands a strictly increasing offset at EVERY hop. That is
        // what keeps it from wandering, but it cannot cross an exit taxiway whose
        // first stretch runs PARALLEL to the runway before turning off — the next node
        // sits at the same offset or a hair inside it, so the walk stops dead with the
        // aircraft still on the pavement (VABB 09/Q: 1.3 m, next node also 1.3 m;
        // 27/E1: 2.0 m, next node 1.9 m; measured 2026-08-08 across 60 airports).
        //
        // This search allows such a stretch to be TRAVERSED while still requiring the
        // node it settles on to be genuinely further out. It is deliberately gated on
        // the greedy walk having failed, so every exit that already reaches the
        // holding position is byte-for-byte unaffected by it.
        // Also runs when the walk failed to move OFF a crossing runway: the greedy
        // walk's step filter refuses candidates on another runway, so from a start
        // node ON one it can dead-end with best == the on-runway start even though
        // its lateral clears the 90 m target (see startOnOtherRunway above).
        bool bestStillOnOtherRunway = startOnOtherRunway && best == destNodeId;
        if (bestLateral < VacatedClearanceMetres || bestStillOnOtherRunway)
        {
            int found = 0;
            double foundProgress = bestStillOnOtherRunway ? 0.0 : bestLateral;
            // With no side established (destination still on the centreline and the
            // junction step degenerate) each side is searched separately, so a path is
            // never assembled from nodes on BOTH sides — which would cross the runway.
            foreach (int trySide in side != 0 ? new[] { side } : new[] { 1, -1 })
            {
                int cand = SearchForClearNode(graph, destNodeId, cameFromNodeId, runway,
                                              runwayHeadingTrue, trySide, ProgressOn,
                                              startOnOtherRunway, enforceHoldGates);
                if (cand <= 0 || !graph.Nodes.TryGetValue(cand, out var candNode)) continue;
                double p = ProgressOn(candNode, trySide);
                if (p > foundProgress + 0.5 && Lateral(candNode) >= contractFloorM)
                {
                    found = cand;
                    foundProgress = p;
                }
            }
            if (found > 0) best = found;
        }

        // Report the ABSOLUTE offset of the node we settled on. `bestLateral` is
        // side-relative progress, which is negative for a start node sitting on the
        // far side of the axis — a diagnostic distance must never read negative.
        endLateralM = graph.Nodes.TryGetValue(best, out var bestNode)
            ? Lateral(bestNode) : startLateralM;
        return best;
    }

    /// <summary>How far past the walk's stop the search for the landed runway's own hold line reaches.</summary>
    private const double OwnHoldSearchMetres = 80.0;
    /// <summary>A line further out than this is not a runway-holding position (ICAO: 107.5 m code F; CAT II/III set-backs to ~160 m, EGKK A3 162 m).</summary>
    private const double OwnHoldMaxLateralMetres = 180.0;

    /// <summary>
    /// The greedy walk only takes steps that move strictly further from the runway, so on an exit
    /// taxiway that CURVES it stops where the offset briefly stops growing, which can be short of
    /// the landed runway's own painted hold line. EGLL 09L to A1 (VirtualPilot 2026-09-25): the walk
    /// stopped at 120 m (A1 runs 120, 123, 120 m out), 22 m short of the "A1, Runway 27R" line at
    /// 141 m, so the pilot was told "Off the runway" inside the protected area. The walk's one-hop
    /// lookahead cannot see a line two hops away.
    ///
    /// <para>Searches up to <see cref="OwnHoldSearchMetres"/> of taxiing further, never more than
    /// 10 m back toward the runway, never onto any runway or past another runway's hold, for a hold
    /// node that NAMES the runway just landed on (either end; an unnamed line never counts) further
    /// out than the stop. Found, the stop moves past it by the walk's own tail-clearance hop;
    /// otherwise it is returned unchanged, so this only ever moves a stop OUTWARD past its own
    /// line.</para>
    /// </summary>
    private static int ExtendPastOwnHoldAhead(
        TaxiGraph graph, int stopNodeId, Runway runway, double runwayHeadingTrue, ref double endLateralM)
    {
        if (!graph.Nodes.TryGetValue(stopNodeId, out var stop)) return stopNodeId;
        if (IsHoldNode(stop) && NamesLandingRunway(stop, runway)) return stopNodeId;

        double hdgRad = runwayHeadingTrue * Math.PI / 180.0;
        double cosH = Math.Cos(hdgRad), sinH = Math.Sin(hdgRad);
        double Lateral(TaxiNode n)
        {
            double latR = (runway.StartLat + n.Latitude) * 0.5 * Math.PI / 180.0;
            double dN = (n.Latitude - runway.StartLat) * MetersPerDegLat;
            double dE = (n.Longitude - runway.StartLon) * MetersPerDegLat * Math.Cos(latR);
            return Math.Abs(dE * cosH - dN * sinH);
        }
        double stopLat = Lateral(stop);
        double floor = stopLat - 10.0;

        var dist = new Dictionary<int, double> { [stopNodeId] = 0.0 };
        var parent = new Dictionary<int, int>();
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(stopNodeId, 0.0);
        TaxiNode? hold = null;
        while (queue.TryDequeue(out int cur, out double d))
        {
            if (d > dist[cur]) continue;
            if (cur != stopNodeId && graph.Nodes.TryGetValue(cur, out var cn) && IsHoldNode(cn)
                && NamesLandingRunway(cn, runway) && Lateral(cn) > stopLat + 2.0
                && Lateral(cn) <= OwnHoldMaxLateralMetres)
            { hold = cn; break; }
            if (!graph.Adjacency.TryGetValue(cur, out var edges)) continue;
            foreach (var e in edges)
            {
                double nd = d + e.DistanceMeters;
                if (nd > OwnHoldSearchMetres) continue;
                if (string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase)) continue;
                if (TaxiGraph.IsStandBridge(e)) continue;
                if (!graph.Nodes.TryGetValue(e.ToNodeId, out var nn)) continue;
                if (Lateral(nn) < floor) continue;
                if (IsOnDifferentRunway(graph, nn, runway, runwayHeadingTrue)) continue;
                if (IsHoldNode(nn) && HoldGuardsAnotherRunway(graph, nn, runway, runwayHeadingTrue)) continue;
                if (dist.TryGetValue(e.ToNodeId, out double old) && old <= nd) continue;
                dist[e.ToNodeId] = nd;
                parent[e.ToNodeId] = cur;
                queue.Enqueue(e.ToNodeId, nd);
            }
        }
        if (hold == null) return stopNodeId;

        int result = hold.NodeId;
        double holdLat = Lateral(hold);
        int cameFrom = parent.TryGetValue(hold.NodeId, out int pNode) ? pNode : -1;
        if (graph.Adjacency.TryGetValue(hold.NodeId, out var past))
        {
            double bestLat = holdLat + 0.5;
            foreach (var e in past)
            {
                if (e.ToNodeId == cameFrom || e.DistanceMeters > PastHoldMarginMetres) continue;
                if (string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase)) continue;
                if (TaxiGraph.IsStandBridge(e)) continue;
                if (!graph.Nodes.TryGetValue(e.ToNodeId, out var nn)) continue;
                if (IsOnDifferentRunway(graph, nn, runway, runwayHeadingTrue)) continue;
                if (IsHoldNode(nn) && HoldGuardsAnotherRunway(graph, nn, runway, runwayHeadingTrue)) continue;
                double l = Lateral(nn);
                if (l > bestLat) { bestLat = l; result = nn.NodeId; }
            }
        }
        // No short hop past the line: stopping ON it leaves the aircraft on a node whose only way
        // on is a long leg (KCVG 09 C: 298 m), and the taxi planner then starts the route from a
        // node elsewhere ("the route begins 250 metres from your position"). Keep the old stop.
        if (result == hold.NodeId) return stopNodeId;
        endLateralM = graph.Nodes.TryGetValue(result, out var rn) ? Lateral(rn) : endLateralM;
        return result;
    }

    /// <summary>True when a hold node's label names the runway just landed on (either end).</summary>
    private static bool NamesLandingRunway(TaxiNode n, Runway runway)
    {
        string? des = string.IsNullOrEmpty(n.HoldShortName) ? null
            : RouteRunwayCrossings.ExtractRunwayDesignator(n.HoldShortName);
        if (des == null) return false;
        string mine = RouteRunwayCrossings.NormalizeDesignator((runway.RunwayID ?? "").Trim());
        return mine.Length > 0 && (des.Equals(mine, StringComparison.OrdinalIgnoreCase)
            || RouteRunwayCrossings.Reciprocal(des).Equals(mine, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHoldNode(TaxiNode n)
        => n.Type == TaxiNodeType.HoldShort || n.Type == TaxiNodeType.ILSHoldShort;

    /// <summary>
    /// True when a hold-short node genuinely guards a runway OTHER than the one
    /// just landed on. The walk treats every hold node it meets as the exit path's
    /// own line and steps PAST it; at close parallels (KSEA-class, ~230 m
    /// separation) or an exit feeding a crossing runway, the hold within reach can
    /// be the NEXT runway's — and stepping past it parks the stop point inside
    /// that runway's protected area, the exact incursion the line exists to
    /// prevent.
    ///
    /// <para>TWO independent signals must agree before the verdict flips, because
    /// each alone is unreliable exactly where it matters:</para>
    /// <para>1. The NAME must attribute the hold to a different (non-reciprocal)
    /// designator. But hold names mis-bind between close parallels — TaxiGraph
    /// names a hold after the nearest centerline within 150 m, so at paired strips
    /// under that separation (12TS: 18L/36R beside 18R/36L) the landing runway's
    /// OWN holds routinely carry the parallel's name. Trusting the name alone
    /// stalled 2,400+ vacate walks just off the pavement in the whole-DB sweep.</para>
    /// <para>2. The GEOMETRY must corroborate: the node must lie closer to the
    /// named runway's centerline than to the landing runway's axis. A mis-bound
    /// own-hold sits close to the landing runway and fails this; a genuine
    /// next-runway hold sits in that runway's approach band and passes.</para>
    /// <para>An UNNAMED hold (or a name with no runway designator, or no matching
    /// centerline) returns false — walking away from the landing runway, the
    /// holds encountered are overwhelmingly its own, so only affirmative
    /// name+geometry agreement changes behaviour.</para>
    /// </summary>
    private static bool HoldGuardsAnotherRunway(
        TaxiGraph graph, TaxiNode n, Runway runway, double runwayHeadingTrue)
    {
        string? label = n.HoldShortName;
        if (string.IsNullOrEmpty(label)) return false;
        string? des = RouteRunwayCrossings.ExtractRunwayDesignator(label);
        if (des == null) return false;
        string mine = RouteRunwayCrossings.NormalizeDesignator((runway.RunwayID ?? "").Trim());
        if (mine.Length == 0) return false;
        if (des.Equals(mine, StringComparison.OrdinalIgnoreCase) ||
            RouteRunwayCrossings.Reciprocal(des).Equals(mine, StringComparison.OrdinalIgnoreCase))
            return false;   // named for the strip just landed on

        // Signal 2: geometric corroboration against the NAMED runway's centerline.
        double hdgRad = runwayHeadingTrue * Math.PI / 180.0;
        double cosH = Math.Cos(hdgRad);
        double sinH = Math.Sin(hdgRad);
        double latR = (runway.StartLat + n.Latitude) * 0.5 * Math.PI / 180.0;
        double mPerLon = MetersPerDegLat * Math.Cos(latR);
        double dN = (n.Latitude - runway.StartLat) * MetersPerDegLat;
        double dE = (n.Longitude - runway.StartLon) * mPerLon;
        double landingLateral = Math.Abs(dE * cosH - dN * sinH);

        foreach (var cl in graph.RunwayCenterlines)
        {
            bool named =
                des.Equals(RouteRunwayCrossings.NormalizeDesignator(cl.Name1),
                           StringComparison.OrdinalIgnoreCase) ||
                des.Equals(RouteRunwayCrossings.NormalizeDesignator(cl.Name2),
                           StringComparison.OrdinalIgnoreCase);
            if (!named) continue;
            if (IsLandingRunway(cl, runway, runwayHeadingTrue)) continue;
            double perp = TaxiGraph.PerpendicularDistanceMetersStatic(
                n.Latitude, n.Longitude, cl.Lat1, cl.Lon1, cl.Lat2, cl.Lon2);
            if (perp < landingLateral) return true;
        }
        return false;
    }

    /// <summary>
    /// True when the node is clear of every OTHER runway's pavement (the landing
    /// runway is judged by <see cref="IsOffPavement"/> instead). Pairs with it at
    /// both verdict sites — the handoff's <c>_landingExitOffPavement</c> and the
    /// form's <c>VacatesRunway</c> — so a stop point parked on a CROSSING runway is
    /// never announced as "off the runway, stop and hold position" (KDTW 04R × 09L).
    /// Unknown graph/node degrades to true — the caller then falls back to the
    /// lateral-only verdict, which is today's behaviour.
    /// </summary>
    public static bool IsClearOfOtherRunways(
        TaxiGraph? graph, int nodeId, Runway? runway, double runwayHeadingTrue)
    {
        if (graph == null || runway == null || nodeId <= 0) return true;
        if (!graph.Nodes.TryGetValue(nodeId, out var node)) return true;
        return !IsOnDifferentRunway(graph, node, runway, runwayHeadingTrue);
    }

    /// <summary>
    /// How far a traversed node may sit on the FAR side of the runway axis. A node on
    /// the runway you are crossing legitimately reads slightly negative (the datum is
    /// still between the two edges); anything beyond the far pavement edge would mean
    /// the path has crossed the runway, which is never allowed.
    /// </summary>
    private const double FarSideFloorMetres = -25.0;

    /// <summary>
    /// Shortest-walk search for a node at least <see cref="VacatedClearanceMetres"/>
    /// out on <paramref name="side"/>, allowed to TRAVERSE nodes that do not themselves
    /// move further out (the parallel-stretch case the greedy walk cannot cross).
    /// <para>Called ONLY as a fallback after the greedy walk has finished short of the
    /// target, so it can never alter an exit that already resolves correctly.</para>
    /// <para>Prefers the NEAREST qualifying node by walked distance rather than the
    /// furthest-out one, so it stops at the first genuinely clear point instead of
    /// dragging the pilot to the end of the taxiway. When nothing reaches the target it
    /// returns the furthest-out node it saw, which the caller accepts only if it beats
    /// what the greedy walk already had.</para>
    /// </summary>
    private static int SearchForClearNode(
        TaxiGraph graph, int startNodeId, int cameFromNodeId, Runway runway,
        double runwayHeadingTrue, int side, Func<TaxiNode, int, double> progressOn,
        bool allowOtherRunwayTransit = false,
        bool enforceHoldGates = true)
    {
        var dist = new Dictionary<int, double> { [startNodeId] = 0.0 };
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(startNodeId, 0.0);

        int bestFar = 0;
        double bestFarProgress = double.NegativeInfinity;

        while (queue.TryDequeue(out int cur, out double curDist))
        {
            if (curDist > dist.GetValueOrDefault(cur, double.MaxValue)) continue;
            if (!graph.Adjacency.TryGetValue(cur, out var edges)) continue;

            foreach (var e in edges)
            {
                if (e.ToNodeId == cameFromNodeId) continue;
                if (string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase)) continue;
                if (TaxiGraph.IsStandBridge(e)) continue;
                if (!graph.Nodes.TryGetValue(e.ToNodeId, out var cand)) continue;
                // Same transit exception as the greedy walk: a search that STARTS on
                // a crossing runway may pass through its pavement (the only way off
                // it at an intersection hub), but may never SETTLE there — both
                // accept sites below re-check.
                bool candOnOther = IsOnDifferentRunway(graph, cand, runway, runwayHeadingTrue);
                if (candOnOther && !allowOtherRunwayTransit) continue;

                // Same rule as the greedy walk: never path onto or past a hold
                // line that NAMES a different runway — the node before it is the
                // correct stop, and a path assembled through it would settle
                // inside that runway's protected area.
                if (enforceHoldGates && IsHoldNode(cand)
                    && HoldGuardsAnotherRunway(graph, cand, runway, runwayHeadingTrue)) continue;

                double p = progressOn(cand, side);
                if (p < FarSideFloorMetres) continue;      // would cross the runway

                double nd = curDist + e.DistanceMeters;
                if (nd > MaxWalkMetres) continue;
                if (nd >= dist.GetValueOrDefault(e.ToNodeId, double.MaxValue)) continue;

                dist[e.ToNodeId] = nd;

                // First node to clear the holding position wins outright — the queue is
                // ordered by walked distance, so this IS the nearest qualifying node.
                // A node still on another runway's pavement is transit, not an answer.
                if (p >= VacatedClearanceMetres && !candOnOther)
                    return ExtendPastHoldLine(graph, e.ToNodeId, cur, side, progressOn,
                                              runway, runwayHeadingTrue, enforceHoldGates);

                if (p > bestFarProgress && !candOnOther) { bestFarProgress = p; bestFar = e.ToNodeId; }
                queue.Enqueue(e.ToNodeId, nd);
            }
        }

        return bestFar;
    }

    /// <summary>
    /// Mirrors the greedy walk's tail-clearance rule: when the node that first cleared
    /// the holding position IS the painted line, take one more short hop so the whole
    /// airframe ends up beyond it.
    /// </summary>
    private static int ExtendPastHoldLine(
        TaxiGraph graph, int node, int cameFrom, int side, Func<TaxiNode, int, double> progressOn,
        Runway runway, double runwayHeadingTrue, bool enforceHoldGates = true)
    {
        if (!graph.Nodes.TryGetValue(node, out var n) || !IsHoldNode(n)) return node;
        // Only the LANDING runway's own line gets the tail-clearance hop past it —
        // another runway's line is a boundary to hold at, never to step beyond.
        if (enforceHoldGates && HoldGuardsAnotherRunway(graph, n, runway, runwayHeadingTrue)) return node;
        if (!graph.Adjacency.TryGetValue(node, out var edges)) return node;

        double curProgress = progressOn(n, side);
        int pick = 0;
        double pickProgress = curProgress;
        foreach (var e in edges)
        {
            if (e.ToNodeId == cameFrom) continue;
            if (e.DistanceMeters > PastHoldMarginMetres) continue;
            if (string.Equals(e.PathType, "R", StringComparison.OrdinalIgnoreCase)) continue;
            if (TaxiGraph.IsStandBridge(e)) continue;
            if (!graph.Nodes.TryGetValue(e.ToNodeId, out var cand)) continue;
            double p = progressOn(cand, side);
            if (p > pickProgress) { pick = e.ToNodeId; pickProgress = p; }
        }
        return pick > 0 ? pick : node;
    }

    /// <summary>
    /// True when the node sits on the pavement of a runway OTHER than the one just
    /// landed on. Uses the same strict runway-shape test as
    /// <see cref="TaxiGraph.TryGetRunwayAtPosition"/> — no tolerance fudge, so a node
    /// on an exit immediately abeam a runway isn't mis-attributed to it.
    /// </summary>
    private static bool IsOnDifferentRunway(
        TaxiGraph graph, TaxiNode node, Runway landingRunway, double runwayHeadingTrue)
    {
        foreach (var rwy in graph.RunwayCenterlines)
        {
            if (!RunwayShape.For(rwy).Contains(node.Latitude, node.Longitude, 0.0)) continue;
            if (IsLandingRunway(rwy, landingRunway, runwayHeadingTrue)) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// True when this centreline IS the runway just landed on.
    /// <para>Matched by COLLINEARITY, not by name or endpoint coincidence: both
    /// centreline endpoints must lie within <see cref="SameRunwayLateralM"/> of the
    /// infinite axis through the landing runway, and the two must run parallel (or
    /// reciprocal). ALONG-track disagreement is expected and deliberately ignored —
    /// centrelines are built from the <c>start</c> table's lineup points, which sit
    /// hundreds of metres from the <c>runway_end</c> threshold at displaced-threshold
    /// runways (EGKK 26L: 406 m behind), so an endpoint-distance match would fail on
    /// exactly the airports that need it. A parallel runway is separated laterally by
    /// far more than the tolerance (EGKK's pair, the tightest in the sweep, is ~200 m
    /// apart), so it can never be mistaken for the landing runway.</para>
    /// </summary>
    private static bool IsLandingRunway(
        TaxiGraph.RunwayCenterline line, Runway landingRunway, double runwayHeadingTrue)
    {
        double hdgRad = runwayHeadingTrue * Math.PI / 180.0;
        double cosH = Math.Cos(hdgRad);
        double sinH = Math.Sin(hdgRad);

        double LateralFromAxis(double lat, double lon)
        {
            double latR = (landingRunway.StartLat + lat) * 0.5 * Math.PI / 180.0;
            double mPerLon = MetersPerDegLat * Math.Cos(latR);
            double dN = (lat - landingRunway.StartLat) * MetersPerDegLat;
            double dE = (lon - landingRunway.StartLon) * mPerLon;
            return Math.Abs(dE * cosH - dN * sinH);
        }

        if (LateralFromAxis(line.Lat1, line.Lon1) > SameRunwayLateralM) return false;
        if (LateralFromAxis(line.Lat2, line.Lon2) > SameRunwayLateralM) return false;

        // Parallel or reciprocal — a taxiway-width sliver of a crossing runway could
        // otherwise pass the lateral test near the intersection.
        double delta = Math.Abs(NormalizeAngle(line.HeadingDeg1 - runwayHeadingTrue));
        return delta <= SameRunwayHeadingDeg || delta >= 180.0 - SameRunwayHeadingDeg;
    }

    private static double NormalizeAngle(double deg)
    {
        while (deg > 180.0) deg -= 360.0;
        while (deg < -180.0) deg += 360.0;
        return deg;
    }
}

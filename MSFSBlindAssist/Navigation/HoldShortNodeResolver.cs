using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Resolves the graph node where a Progressive Taxi "Hold short of runway"
/// terminator should end the route: the hold point on the aircraft's own (near)
/// side of the runway.
///
/// The scenery's own hold-short markers (navdata HS/HSND/IHS/IHSND →
/// TaxiNodeType.HoldShort / ILSHoldShort) are authoritative for where the hold
/// line actually is. Motivating incident (KSFO 2026-07-01, "Q, hold short
/// 10R"): taxiway Q carries two HSND nodes on the 28L approach, ~97 m and
/// ~157 m from the centerline; the purely geometric scan picked the 157 m node
/// and the pilot held ~60 m before the real hold line.
///
/// Candidates are ranked in TIERS so a designated node can never hijack the
/// route away from the cleared taxiway (the 2026-07 review fix — a blanket
/// "designated first" let an HSND kilometres down the runway on a DIFFERENT
/// taxiway beat a plain node on the cleared one):
///
///   1. Designated node ON the cleared last taxiway (closest to the runway —
///      the same convention TruncateToHoldShort uses for runway destinations).
///   2. Designated node at the cleared taxiway's runway junction (within
///      HS_JUNCTION_ALONG_M along-track of the taxiway's own closest plain
///      candidate) — covers the common scenery shape where the final approach
///      stub from taxiway to hold line is an unnamed connector, so the HSND
///      doesn't carry the taxiway name.
///   3. Plain node on the cleared taxiway — the legacy geometric pick, with
///      its deliberate setback: Runway.Width is in FEET, and dividing by 2 and
///      reading the result as METRES gives ~1.64 × the physical half-width
///      (a 200 ft runway → ~97 m), approximating real hold-line placement
///      (FAA hold lines sit 125–280 ft from the centerline). Do NOT "fix" the
///      units — see <see cref="LegacySetbackMetres"/>.
///   4. Designated node on any taxiway (no cleared-taxiway candidate exists).
///   5. Plain node on any taxiway (legacy last resort).
///
/// Designated candidates are additionally gated by the node's own
/// <see cref="TaxiNode.HoldShortName"/>: when it names a runway (reciprocal-
/// aware), it must be THIS runway — TaxiGraph names hold nodes after their
/// runway precisely to disambiguate closely spaced parallels, whose
/// between-the-pair hold nodes can sit inside the geometric window.
///
/// Pure static (graph + runway + aircraft state in, node out) so
/// tools/ProgressiveTaxiProbe can assert the tiers on a synthetic graph.
/// </summary>
public static class HoldShortNodeResolver
{
    private const double MAX_LATERAL_M = 600.0;        // max lateral distance from runway centerline
    private const double MAX_ALONG_PAST_END_M = 500.0; // buffer past each runway end

    // A designated hold-short node is trusted as belonging to THIS runway only
    // within the same tolerance TaxiGraph uses to NAME hold nodes after a runway
    // (HOLDSHORT_RUNWAY_MATCH_M). Beyond that, an HS node inside the geometric
    // window could belong to a parallel runway.
    private const double HS_NODE_MATCH_M = 150.0;

    // A designated node closer to the centerline than the true pavement
    // half-width PLUS this buffer is treated as a misplaced scenery marker,
    // not a hold line — real hold lines never hug the pavement edge (FAA
    // minimum hold-line offset is 125 ft ≈ 38 m from the centerline; the
    // buffer keeps a heavy jet's nose out of the runway safety area when a
    // third-party scenery drops an HSND a metre off the pavement).
    private const double HS_MIN_SETBACK_BUFFER_M = 15.0;

    // Tier-2 window: how far along the runway a designated node may sit from
    // the cleared taxiway's own closest plain candidate and still be treated
    // as "this taxiway's hold line on an unnamed stub". Kept below typical
    // parallel-taxiway junction spacing (~100 m+) so an adjacent taxiway's
    // hold node doesn't hijack the route.
    private const double HS_JUNCTION_ALONG_M = 75.0;

    /// <summary>
    /// The legacy geometric hold setback. Runway.Width is in FEET; dividing by
    /// 2 and reading the result as METRES is a DELIBERATE conservative setback
    /// (~1.64 × the physical half-width — a 200 ft runway → ~97 m),
    /// approximating real hold-line placement so a geometric hold target
    /// clears the runway safety area. Do NOT "fix" the units to ft→m — that
    /// would put the target at the pavement edge with the tail still over the
    /// runway. Shared with TaxiAssistForm.FindFarSideRunwayNode so the
    /// near-side and far-side finders can never diverge on the setback.
    /// </summary>
    public static double LegacySetbackMetres(Runway runway) =>
        Math.Max(runway.Width > 0 ? runway.Width / 2.0 : 30.0, 15.0);

    /// <summary>
    /// TRUE physical half-width in metres (Runway.Width is FEET) — the
    /// pavement edge, used for on-runway detection and as the base of the
    /// designated-node sanity floor.
    /// </summary>
    public static double TrueHalfWidthMetres(Runway runway) =>
        runway.Width > 0 ? runway.Width * 0.3048 / 2.0 : 23.0;

    /// <summary>
    /// Finds the hold-short node on the aircraft's side of <paramref name="runway"/>,
    /// preferring the scenery's designated hold-short nodes per the tier order
    /// in the class remarks, falling back to the legacy geometric
    /// closest-to-centerline scan.
    /// </summary>
    public static TaxiNode? ResolveNearSide(
        TaxiGraph graph, Runway runway,
        double aircraftLat, double aircraftLon, double aircraftHeadingMag,
        string lastTaxiway)
    {
        var frame = RunwayFrame.For(runway, aircraftLat);

        double legacyMinLateralM = LegacySetbackMetres(runway);
        double halfWidthTrueM = TrueHalfWidthMetres(runway);
        double hsFloorM = halfWidthTrueM + HS_MIN_SETBACK_BUFFER_M;

        double acSignedCT = frame.SignedCrossTrack(aircraftLat, aircraftLon);

        // Near side = the aircraft's own side. The threshold is the TRUE
        // pavement edge, not the legacy setback: designated hold lines sit
        // INSIDE the legacy floor (KSFO: line at ~90 m, floor 97 m), so a
        // pilot legitimately stopped AT the hold line is off the pavement and
        // physically on a side — use it. Only an aircraft actually ON the
        // pavement needs the heading heuristic (which picks the side it is
        // coming FROM, opposite the exit side the far-side finder would pick,
        // and hard-codes a side for runway-parallel headings).
        int nearSign;
        if (Math.Abs(acSignedCT) >= halfWidthTrueM)
        {
            nearSign = Math.Sign(acSignedCT);
        }
        else
        {
            double perpComp = Math.Sin((runway.HeadingMag - aircraftHeadingMag) * Math.PI / 180.0);
            nearSign = perpComp >= 0 ? -1 : 1;
        }

        // Restrict candidates to the aircraft's connected component so the
        // resolved node is actually reachable (mirrors FindFarSideRunwayNode).
        int? aircraftComponentId = graph.FindNearestNode(aircraftLat, aircraftLon)?.ComponentId;

        bool InWindow(TaxiNode node, out double lateralAbs, out double along)
        {
            lateralAbs = 0;
            along = 0;
            if (aircraftComponentId.HasValue && node.ComponentId != aircraftComponentId.Value)
                return false;

            double nodeSignedCT = frame.SignedCrossTrack(node.Latitude, node.Longitude);
            if (Math.Sign(nodeSignedCT) != nearSign) return false;
            lateralAbs = Math.Abs(nodeSignedCT);
            if (lateralAbs > MAX_LATERAL_M) return false;

            along = frame.Along(node.Latitude, node.Longitude);
            if (along < -MAX_ALONG_PAST_END_M) return false;
            if (along > frame.LengthM + MAX_ALONG_PAST_END_M) return false;
            return true;
        }

        bool OnLastTaxiway(TaxiNode node) => node.TaxiwayNames.Contains(lastTaxiway);

        // The target runway's two designators, for the HoldShortName gate.
        string targetId = runway.RunwayID ?? "";
        string targetRecip = RouteRunwayCrossings.Reciprocal(targetId);

        // ---- Single scan: collect designated candidates + track plain bests ----
        var designated = new List<(TaxiNode node, double lateral, double along)>();

        TaxiNode? bestNode = null;   double bestLateral = double.MaxValue;     // plain, any taxiway
        TaxiNode? bestOnTw = null;   double bestLateralTw = double.MaxValue;   // plain, on lastTaxiway
        double bestOnTwAlong = 0;

        foreach (var node in graph.Nodes.Values)
        {
            if (!InWindow(node, out double lateralAbs, out double along)) continue;

            if ((node.Type == TaxiNodeType.HoldShort || node.Type == TaxiNodeType.ILSHoldShort) &&
                lateralAbs >= hsFloorM && lateralAbs <= HS_NODE_MATCH_M)
            {
                // Runway gate: when the node's own name says which runway it
                // guards, it must be this one (or its reciprocal — same
                // pavement). A bare/unparseable name stays geometric-only.
                string? named = RouteRunwayCrossings.ExtractRunwayDesignator(node.HoldShortName);
                if (named == null ||
                    named.Equals(targetId, StringComparison.OrdinalIgnoreCase) ||
                    named.Equals(targetRecip, StringComparison.OrdinalIgnoreCase))
                {
                    designated.Add((node, lateralAbs, along));
                }
            }

            // Legacy geometric tracking (a designated node past the legacy
            // floor also counts here, exactly as the old single-pass scan did).
            if (lateralAbs < legacyMinLateralM) continue;
            if (lateralAbs < bestLateral) { bestLateral = lateralAbs; bestNode = node; }
            if (lateralAbs < bestLateralTw && OnLastTaxiway(node))
            {
                bestLateralTw = lateralAbs;
                bestOnTw = node;
                bestOnTwAlong = along;
            }
        }

        static TaxiNode? ClosestToRunway(IEnumerable<(TaxiNode node, double lateral, double along)> c)
        {
            TaxiNode? best = null; double bestLat = double.MaxValue;
            foreach (var (node, lateral, _) in c)
                if (lateral < bestLat) { bestLat = lateral; best = node; }
            return best;
        }

        // Tier 1: designated node on the cleared taxiway.
        var tier1 = ClosestToRunway(designated.Where(d => OnLastTaxiway(d.node)));
        if (tier1 != null) return tier1;

        if (bestOnTw != null)
        {
            // Tier 2: designated node at the cleared taxiway's runway junction
            // (unnamed-stub shape).
            var tier2 = ClosestToRunway(designated.Where(d =>
                Math.Abs(d.along - bestOnTwAlong) <= HS_JUNCTION_ALONG_M));
            if (tier2 != null) return tier2;

            // Tier 3: plain node on the cleared taxiway (legacy behaviour).
            return bestOnTw;
        }

        // Tiers 4/5: no cleared-taxiway candidate at all — best designated
        // node anywhere, else the legacy any-taxiway geometric pick.
        return ClosestToRunway(designated) ?? bestNode;
    }
}

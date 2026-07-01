using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Resolves the graph node where a Progressive Taxi "Hold short of runway"
/// terminator should end the route: the hold point on the aircraft's own (near)
/// side of the runway.
///
/// Two-pass design:
///
/// PASS 1 — designated hold-short nodes. The scenery's own hold-short markers
/// (navdata HS/HSND/IHS/IHSND → TaxiNodeType.HoldShort / ILSHoldShort) are
/// authoritative for where the hold line actually is. Motivating incident
/// (KSFO 2026-07-01, "Q, hold short 10R"): taxiway Q carries two HSND nodes on
/// the 28L approach, ~97 m and ~157 m from the centerline; the purely geometric
/// scan (pass 2) picked the 157 m node and the pilot held ~60 m before the real
/// hold line. Pass 1 picks the designated node CLOSEST to the runway — the same
/// convention TruncateToHoldShort uses for runway destinations ("the latest,
/// closest-to-runway hold of either type").
///
/// PASS 2 — geometric fallback (sparse navdata with no hold-short markers).
/// Behaviour is intentionally IDENTICAL to the pre-2026-07 scan, including its
/// setback: Runway.Width is in FEET, and dividing by 2 and reading the result
/// as METRES gives ~1.64 × the physical half-width (a 200 ft runway → ~97 m
/// setback), which approximates real-world hold-line placement (FAA hold lines
/// sit 125–280 ft from the centerline). Do NOT "fix" the units to ft→m here —
/// that would place the fallback hold at the pavement edge, INSIDE any real
/// hold line. Designated nodes (pass 1) override this heuristic entirely.
///
/// Pure static (graph + runway + aircraft state in, node out) so
/// tools/ProgressiveTaxiProbe can assert both passes on a synthetic graph.
/// </summary>
public static class HoldShortNodeResolver
{
    private const double DEG_TO_M_LAT = 111320.0;
    private const double MAX_LATERAL_M = 600.0;        // max lateral distance from runway centerline
    private const double MAX_ALONG_PAST_END_M = 500.0; // buffer past each runway end

    // A designated hold-short node is trusted as belonging to THIS runway only
    // within the same tolerance TaxiGraph uses to NAME hold nodes after a runway
    // (HOLDSHORT_RUNWAY_MATCH_M). Beyond that, an HS node inside the geometric
    // window could belong to a parallel runway (KSFO: 28R hold nodes sit ~450 m
    // from the 28L centerline — well inside MAX_LATERAL_M).
    private const double HS_NODE_MATCH_M = 150.0;

    /// <summary>
    /// Finds the hold-short node on the aircraft's side of <paramref name="runway"/>,
    /// preferring the scenery's designated hold-short nodes (closest to the runway,
    /// on <paramref name="lastTaxiway"/> when possible), falling back to the legacy
    /// geometric closest-to-centerline scan.
    /// </summary>
    public static TaxiNode? ResolveNearSide(
        TaxiGraph graph, Runway runway,
        double aircraftLat, double aircraftLon, double aircraftHeadingMag,
        string lastTaxiway)
    {
        double hdgRad = runway.Heading * Math.PI / 180.0;
        double rwEast = Math.Sin(hdgRad);
        double rwNorth = Math.Cos(hdgRad);

        double degToMLon = DEG_TO_M_LAT * Math.Cos(aircraftLat * Math.PI / 180.0);

        double SignedCT(double lat, double lon)
        {
            double pDy = (lat - runway.StartLat) * DEG_TO_M_LAT;
            double pDx = (lon - runway.StartLon) * degToMLon;
            return rwEast * pDy - rwNorth * pDx;
        }

        // Legacy setback (feet-as-metres heuristic — see class remarks; pass 2 only).
        double legacyMinLateralM = Math.Max(runway.Width > 0 ? runway.Width / 2.0 : 30.0, 15.0);
        // TRUE physical half-width in metres (Runway.Width is FEET) — pass 1's
        // sanity floor: a "designated" node on the runway pavement itself is a
        // scenery bug, not a hold line.
        double halfWidthTrueM = runway.Width > 0 ? runway.Width * 0.3048 / 2.0 : 23.0;

        double acSignedCT = SignedCT(aircraftLat, aircraftLon);

        // Near side = the aircraft's own side. If the aircraft is ON the runway,
        // use its heading to pick the side it is coming FROM (opposite the exit
        // side the far-side finder would pick). Uses the legacy setback so the
        // on-runway determination matches the pre-refactor behaviour exactly.
        int nearSign;
        if (Math.Abs(acSignedCT) >= legacyMinLateralM)
        {
            nearSign = Math.Sign(acSignedCT);
        }
        else
        {
            double perpComp = Math.Sin((runway.HeadingMag - aircraftHeadingMag) * Math.PI / 180.0);
            nearSign = perpComp >= 0 ? -1 : 1;
        }

        double runwayLengthM = runway.Length > 0
            ? runway.Length * 0.3048   // stored in feet
            : TaxiGraph.CalculateDistanceMeters(
                runway.StartLat, runway.StartLon, runway.EndLat, runway.EndLon);

        // Restrict candidates to the aircraft's connected component so the
        // resolved node is actually reachable (mirrors FindFarSideRunwayNode).
        int? aircraftComponentId = graph.FindNearestNode(aircraftLat, aircraftLon)?.ComponentId;

        bool InWindow(TaxiNode node, out double lateralAbs)
        {
            lateralAbs = 0;
            if (aircraftComponentId.HasValue && node.ComponentId != aircraftComponentId.Value)
                return false;

            double nodeSignedCT = SignedCT(node.Latitude, node.Longitude);
            if (Math.Sign(nodeSignedCT) != nearSign) return false;
            lateralAbs = Math.Abs(nodeSignedCT);
            if (lateralAbs > MAX_LATERAL_M) return false;

            double nPDx = (node.Longitude - runway.StartLon) * degToMLon;
            double nPDy = (node.Latitude - runway.StartLat) * DEG_TO_M_LAT;
            double along = rwEast * nPDx + rwNorth * nPDy;
            if (along < -MAX_ALONG_PAST_END_M) return false;
            if (along > runwayLengthM + MAX_ALONG_PAST_END_M) return false;
            return true;
        }

        bool OnLastTaxiway(TaxiNode node) => node.TaxiwayNames.Contains(lastTaxiway);

        // ---- PASS 1: designated hold-short nodes ----
        TaxiNode? hsBest = null;     double hsBestLateral = double.MaxValue;
        TaxiNode? hsBestOnTw = null; double hsBestLateralTw = double.MaxValue;

        foreach (var node in graph.Nodes.Values)
        {
            if (node.Type != TaxiNodeType.HoldShort && node.Type != TaxiNodeType.ILSHoldShort)
                continue;
            if (!InWindow(node, out double lateralAbs)) continue;
            if (lateralAbs < halfWidthTrueM) continue;   // on the pavement — bogus
            if (lateralAbs > HS_NODE_MATCH_M) continue;  // likely another runway's hold

            if (lateralAbs < hsBestLateral) { hsBestLateral = lateralAbs; hsBest = node; }
            if (lateralAbs < hsBestLateralTw && OnLastTaxiway(node))
            {
                hsBestLateralTw = lateralAbs;
                hsBestOnTw = node;
            }
        }

        if (hsBestOnTw != null || hsBest != null)
            return hsBestOnTw ?? hsBest;

        // ---- PASS 2: legacy geometric scan (verbatim behaviour) ----
        // Prefer a candidate that actually lies on the last cleared taxiway (where
        // the clearance ends); fall back to the global nearest-centerline node when
        // none qualifies.
        TaxiNode? bestNode = null;   double bestLateral = double.MaxValue;     // any taxiway (fallback)
        TaxiNode? bestOnTw = null;   double bestLateralTw = double.MaxValue;   // on lastTaxiway (preferred)

        foreach (var node in graph.Nodes.Values)
        {
            if (!InWindow(node, out double lateralAbs)) continue;
            if (lateralAbs < legacyMinLateralM) continue;

            // Closest to the centerline on the near side = the hold-short point.
            if (lateralAbs < bestLateral) { bestLateral = lateralAbs; bestNode = node; }
            if (lateralAbs < bestLateralTw && OnLastTaxiway(node))
            {
                bestLateralTw = lateralAbs;
                bestOnTw = node;
            }
        }

        return bestOnTw ?? bestNode;
    }
}

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Services;

public partial class TaxiGuidanceManager
{
    /// <summary>
    /// Formats a distance in meters using the active unit (via DistanceFormatter).
    /// Long distances (over ~6000 ft) switch to the big unit MATCHING the ground
    /// distance setting: kilometres in metres mode, nautical miles in feet mode
    /// (route summaries and status readouts — a metric user should never hear NM
    /// for a taxi distance).
    /// </summary>
    private static string FormatDistance(double meters)
    {
        double feet = meters * METERS_TO_FEET;
        if (feet > 6000)
        {
            if (DistanceFormatter.IsMetres)
            {
                double km = meters / 1000.0;
                return $"{km:F1} kilometres";
            }
            double nm = meters * METERS_TO_NM;
            return $"{nm:F1} nautical miles";
        }
        return DistanceFormatter.FromMetres(meters);
    }

    private static string CapFirst(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s[1..];
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle > 180) angle -= 360;
        while (angle < -180) angle += 360;
        return angle;
    }

    /// <summary>
    /// Computes the spoken turn direction ("left" / "slight right" / "continue")
    /// FROM THE AIRCRAFT'S CURRENT HEADING toward a target bearing. Used for
    /// turn callouts so the verbal direction always agrees with the steering
    /// tone (which is also driven by the aircraft's instantaneous heading).
    ///
    /// Why not just use the route's pre-computed `TaxiRouteSegment.TurnDirection`?
    /// That value is `nextSeg.bearing - currentSeg.bearing` — it assumes the
    /// aircraft is exactly on-axis with the current segment. When the aircraft
    /// is off-axis (e.g., still at the gate before pushback rotation, after a
    /// wide turn, after a brief deviation), the actual turn the aircraft must
    /// make to align with the next segment differs from the route's intent —
    /// sometimes in the OPPOSITE direction. The steering tone (real-time pan
    /// from aircraft-heading vs target-bearing) is always right; the static
    /// pre-computed verbal was wrong in those cases. Following the tone is
    /// the correct behavior; this helper makes the verbal match.
    ///
    /// Thresholds match `TaxiRouter.GetTurnDirection`: <20° → continue,
    /// 20–60° → slight, ≥60° → full. The "sharp" prefix is owned by callers
    /// that read `TurnAngleDegrees` separately.
    /// </summary>
    private static string ComputeTurnVerbalFromHeading(double targetBearing, double aircraftHeadingTrue)
    {
        double turn = NormalizeAngle(targetBearing - aircraftHeadingTrue);
        double absTurn = Math.Abs(turn);
        if (absTurn < 20) return "continue";
        if (absTurn < 60) return turn < 0 ? "slight left" : "slight right";
        return turn < 0 ? "left" : "right";
    }

    /// <summary>
    /// Returns the signed along-runway distance in meters from (refLat, refLon)
    /// to (pointLat, pointLon), measured along the runway heading. Positive
    /// values mean `point` lies past `ref` in the direction of flight; negative
    /// values mean `point` is still upfield of `ref`. Equirectangular projection
    /// — sub-cm accuracy at runway scale.
    ///
    /// Runway heading is measured clockwise from true north, so the unit vector
    /// along the runway in (east, north) coordinates is (sin H, cos H). The
    /// signed projection is the dot product of (dE, dN) with that unit vector.
    /// </summary>
    private static double SignedAlongRunwayMeters(
        double pointLat, double pointLon,
        double refLat, double refLon,
        double runwayHeadingTrueDeg)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double latMidRad = (pointLat + refLat) * 0.5 * Math.PI / 180.0;
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latMidRad);
        double dN = (pointLat - refLat) * METERS_PER_DEG_LAT;
        double dE = (pointLon - refLon) * metersPerDegLon;
        double hdgRad = runwayHeadingTrueDeg * Math.PI / 180.0;
        return dE * Math.Sin(hdgRad) + dN * Math.Cos(hdgRad);
    }

    /// <summary>
    /// Returns true when the tagged segment's HoldShortRunway designator (which
    /// may include the "runway " prefix added by
    /// <see cref="InsertRunwayCrossingHoldShorts"/>) names the same physical
    /// pavement as <paramref name="target"/> — either as the same designator
    /// or its reciprocal (e.g. "09" matches "27", "09L" matches "27R").
    /// Used by the Progressive Taxi suppression pass to strip the cleared
    /// crossing hold-short without touching other auto-inserted holds.
    /// </summary>
    private static bool RunwayDesignatorsMatch(string tagged, string target)
    {
        // Parse the designator out of the label with the shared extractor —
        // labels carry more than a bare "runway " prefix ("runway 15R at N",
        // "D5, Runway 22R"), which the old Replace-based strip mangled into
        // never-matching strings. Fall back to the strip for a bare designator
        // with no "runway" word at all.
        string a = RouteRunwayCrossings.ExtractRunwayDesignator(tagged)
            ?? RouteRunwayCrossings.NormalizeDesignator(
                   tagged.Replace("runway", "", StringComparison.OrdinalIgnoreCase).Trim());
        string b = RouteRunwayCrossings.NormalizeDesignator(target.Trim());
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        return RouteRunwayCrossings.Reciprocal(a).Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Projects the aircraft onto a route segment (equirectangular). Outputs the
    /// remaining along-track distance to the segment's END node
    /// (<paramref name="alongRemainingM"/>: positive = the node is still ahead in the
    /// segment direction, ≤ 0 = the aircraft is abeam or past it) and the signed
    /// perpendicular cross-track offset (<paramref name="crossM"/>). Used by the
    /// landing-exit "vacate" arrival so the closure fires even when the pilot rolls
    /// through the final node wide of the exact graph point.
    /// </summary>
    private static void AlongTrackToSegmentEnd(
        double lat, double lon, TaxiRouteSegment seg,
        out double alongRemainingM, out double crossM)
    {
        const double MPD = 111132.0;
        double latMid = (seg.FromNode.Latitude + seg.ToNode.Latitude) * 0.5;
        double mpl = MPD * Math.Cos(latMid * Math.PI / 180.0);
        double dx = (seg.ToNode.Longitude - seg.FromNode.Longitude) * mpl;
        double dy = (seg.ToNode.Latitude  - seg.FromNode.Latitude)  * MPD;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1.0) { alongRemainingM = 0.0; crossM = 0.0; return; }
        double ux = dx / len, uy = dy / len;             // unit vector along segment
        double ex = (seg.ToNode.Longitude - lon) * mpl;  // aircraft → end node
        double ey = (seg.ToNode.Latitude  - lat) * MPD;
        alongRemainingM = ex * ux + ey * uy;             // >0: end node ahead
        crossM = ex * (-uy) + ey * ux;                   // signed perpendicular
    }

    /// <summary>
    /// Absolute lateral (cross-runway) offset in meters of <paramref name="pointLat/Lon"/>
    /// from the runway centerline. The centerline passes through
    /// <paramref name="refLat/Lon"/> in direction <paramref name="runwayHeadingTrueDeg"/>.
    /// Companion to <see cref="SignedAlongRunwayMeters"/> — uses the perpendicular
    /// component of the same equirectangular projection.
    /// </summary>
    private static double AbsLateralFromRunwayMeters(
        double pointLat, double pointLon,
        double refLat, double refLon,
        double runwayHeadingTrueDeg)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double latMidRad = (pointLat + refLat) * 0.5 * Math.PI / 180.0;
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latMidRad);
        double dN = (pointLat - refLat) * METERS_PER_DEG_LAT;
        double dE = (pointLon - refLon) * metersPerDegLon;
        double hdgRad = runwayHeadingTrueDeg * Math.PI / 180.0;
        return Math.Abs(dE * Math.Cos(hdgRad) - dN * Math.Sin(hdgRad));
    }

    /// <summary>
    /// Perpendicular (cross-track) distance in meters from (plat, plon) to the segment
    /// (a→b), clamped to endpoints so points beyond either end use the nearest endpoint.
    /// Uses equirectangular projection — sub-cm accuracy at taxi scale.
    /// </summary>
    private static double PerpendicularDistanceToSegmentMeters(
        double plat, double plon,
        double alat, double alon,
        double blat, double blon)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double latMidRad = (alat + blat) * 0.5 * (Math.PI / 180.0);
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos(latMidRad);

        double bx = (blon - alon) * metersPerDegLon;
        double by = (blat - alat) * METERS_PER_DEG_LAT;
        double px = (plon - alon) * metersPerDegLon;
        double py = (plat - alat) * METERS_PER_DEG_LAT;

        double lenSq = bx * bx + by * by;
        if (lenSq < 1e-9)
            return Math.Sqrt(px * px + py * py);

        double t = (px * bx + py * by) / lenSq;
        if (t < 0.0) t = 0.0;
        else if (t > 1.0) t = 1.0;

        double fx = t * bx;
        double fy = t * by;
        double ex = px - fx;
        double ey = py - fy;
        return Math.Sqrt(ex * ex + ey * ey);
    }

}

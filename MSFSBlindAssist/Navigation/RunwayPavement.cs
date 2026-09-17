namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Runway geometry for "is this point on, or does this line touch, a runway?" questions. Every
/// centerline goes through <see cref="RunwayShape.For"/> — the ONE accessor for where a runway
/// physically is — rather than reading a centerline's <c>Pavement*</c> fields directly: it
/// rejects a pavement line that is non-finite, sits at (0,0), is under a metre long, or belongs
/// to a DIFFERENT runway (a heading-pass mis-pair), falling back to the start rows in each case,
/// and it caps an implausible width (<see cref="RunwayShape.MaxPlausibleHalfWidthMeters"/>) so
/// one malformed navdata row can't swallow an airport's whole taxi network. A degenerate shape
/// (no axis — <see cref="RunwayShape.IsDegenerate"/>) contributes nothing to either method below.
///
/// <para>Shared by <c>TaxiGraph.BridgeOrphanParkingIslands</c> (a fabricated bridge must never
/// end on or cross a runway) and <see cref="RouteReachability"/> (a route must never start
/// with a straight unmapped leg across a runway). Known limit: a runway Build could not pair
/// into a centreline (an unpaired start row) is invisible here.</para>
/// </summary>
public static class RunwayPavement
{
    /// <summary>
    /// True when the point lies within any runway's half-width of its centreline AND inside its
    /// extent (<see cref="RunwayShape.Contains"/>) — unlike the old clamped-distance-to-segment
    /// test, a point beyond a runway's end is off the runway even when it sits close to the end
    /// laterally.
    /// </summary>
    public static bool IsOnPavement(
        double lat, double lon, IReadOnlyList<TaxiGraph.RunwayCenterline> centerlines)
    {
        foreach (var cl in centerlines)
        {
            var shape = RunwayShape.For(cl);
            if (shape.IsDegenerate) continue;
            if (shape.Contains(lat, lon, 0.0)) return true;
        }
        return false;
    }

    /// <summary>
    /// True when the segment a→b strictly crosses a runway's centreline, or comes within that
    /// runway's half-width of it anywhere along its length. The minimum distance between two
    /// non-crossing segments is the smallest of the four clamped endpoint-to-segment distances.
    /// </summary>
    /// <param name="designator">The touched runway whose contact is NEAREST a along the
    /// segment — where the leg crosses that runway's centreline, the distance to the
    /// crossing point; otherwise the distance to the point on the leg closest to that
    /// centreline segment. Named via <see cref="RunwayShape.NameAt"/> (the end nearer the
    /// segment's midpoint, falling back to the other end when that one has no name); empty when
    /// nothing is touched.</param>
    public static bool SegmentTouchesPavement(
        double aLat, double aLon, double bLat, double bLon,
        IReadOnlyList<TaxiGraph.RunwayCenterline> centerlines, out string designator)
    {
        designator = "";
        bool found = false;
        double bestAlongMetersFromA = double.MaxValue;

        foreach (var cl in centerlines)
        {
            var shape = RunwayShape.For(cl);
            if (shape.IsDegenerate) continue;

            bool crosses = TaxiGraph.EdgeCrossesRunwayStatic(
                aLat, aLon, bLat, bLon,
                shape.Lat1, shape.Lon1, shape.Lat2, shape.Lon2);

            double distAToCl = TaxiGraph.PerpendicularDistanceMetersStatic(
                aLat, aLon, shape.Lat1, shape.Lon1, shape.Lat2, shape.Lon2);
            double distBToCl = TaxiGraph.PerpendicularDistanceMetersStatic(
                bLat, bLon, shape.Lat1, shape.Lon1, shape.Lat2, shape.Lon2);
            double distCl1ToLeg = TaxiGraph.PerpendicularDistanceMetersStatic(
                shape.Lat1, shape.Lon1, aLat, aLon, bLat, bLon);
            double distCl2ToLeg = TaxiGraph.PerpendicularDistanceMetersStatic(
                shape.Lat2, shape.Lon2, aLat, aLon, bLat, bLon);

            double minDistance = Math.Min(Math.Min(distAToCl, distBToCl), Math.Min(distCl1ToLeg, distCl2ToLeg));

            // The "touched" test itself is UNCHANGED — only the choice among several touched
            // runways changes, below.
            if (!crosses && minDistance > shape.HalfWidthMeters) continue;

            found = true;

            double alongMetersFromA = crosses
                ? CrossingDistanceFromA(
                    aLat, aLon, bLat, bLon,
                    shape.Lat1, shape.Lon1, shape.Lat2, shape.Lon2)
                : NearestTouchDistanceFromA(
                    aLat, aLon, bLat, bLon,
                    shape.Lat1, shape.Lon1, shape.Lat2, shape.Lon2,
                    distAToCl, distBToCl, distCl1ToLeg, distCl2ToLeg);

            if (alongMetersFromA >= bestAlongMetersFromA) continue;
            bestAlongMetersFromA = alongMetersFromA;

            // Unchanged: which end names a touched runway — through the runway's own shape, so a
            // name-swapped pair (AYCH) still speaks the physical threshold, and an end with no
            // name falls back to the other one instead of always defaulting to Name1.
            double midLat = (aLat + bLat) * 0.5, midLon = (aLon + bLon) * 0.5;
            designator = shape.NameAt(shape.Project(midLat, midLon).Along);
        }

        return found;
    }

    /// <summary>
    /// Metres from a to the point where the leg a→b crosses the pavement centreline (t1→t2) —
    /// the segment-intersection parameter along a→b, converted to a distance. Local
    /// equirectangular projection about the leg's own reference latitude, matching
    /// <see cref="TaxiGraph.EdgeCrossesRunwayStatic"/>'s convention. Only called once that
    /// method has confirmed a genuine crossing, so the two lines are not parallel.
    /// </summary>
    private static double CrossingDistanceFromA(
        double aLat, double aLon, double bLat, double bLon,
        double t1Lat, double t1Lon, double t2Lat, double t2Lon)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos((aLat + bLat) * 0.5 * (Math.PI / 180.0));

        double bx = (bLon - aLon) * metersPerDegLon, by = (bLat - aLat) * METERS_PER_DEG_LAT;
        double p3x = (t1Lon - aLon) * metersPerDegLon, p3y = (t1Lat - aLat) * METERS_PER_DEG_LAT;
        double p4x = (t2Lon - aLon) * metersPerDegLon, p4y = (t2Lat - aLat) * METERS_PER_DEG_LAT;

        double d2x = p4x - p3x, d2y = p4y - p3y;
        double denom = bx * d2y - by * d2x;
        double t = Math.Abs(denom) < 1e-9 ? 0.0 : (p3x * d2y - p3y * d2x) / denom;
        t = Math.Clamp(t, 0.0, 1.0);

        return t * Math.Sqrt(bx * bx + by * by);
    }

    /// <summary>
    /// Metres from a to the point on the leg a→b closest to a touched (non-crossing) pavement
    /// centreline — whichever of the leg's own endpoints or the centreline endpoints' leg
    /// projections produced the smallest of the four distances <see cref="SegmentTouchesPavement"/>
    /// already computed to decide the leg was touched. <c>Math.Min</c> returns one of its two
    /// arguments UNCHANGED, so comparing the result back against each candidate by value
    /// reliably identifies which one it was.
    /// </summary>
    private static double NearestTouchDistanceFromA(
        double aLat, double aLon, double bLat, double bLon,
        double t1Lat, double t1Lon, double t2Lat, double t2Lon,
        double distAToCl, double distBToCl, double distCl1ToLeg, double distCl2ToLeg)
    {
        double min = Math.Min(Math.Min(distAToCl, distBToCl), Math.Min(distCl1ToLeg, distCl2ToLeg));
        if (min == distAToCl) return 0.0;

        double legMeters = TaxiGraph.FastDistanceMeters(aLat, aLon, bLat, bLon);
        if (min == distBToCl) return legMeters;

        double t = min == distCl1ToLeg
            ? ProjectionParameter(t1Lat, t1Lon, aLat, aLon, bLat, bLon)
            : ProjectionParameter(t2Lat, t2Lon, aLat, aLon, bLat, bLon);
        return t * legMeters;
    }

    /// <summary>
    /// Clamped [0,1] parameter where point p projects onto segment a→b — the same projection
    /// <see cref="TaxiGraph.PerpendicularDistanceMetersStatic"/> uses internally to clamp a
    /// point-to-segment distance, exposed here because that method returns only the distance.
    /// </summary>
    private static double ProjectionParameter(
        double pLat, double pLon, double aLat, double aLon, double bLat, double bLon)
    {
        const double METERS_PER_DEG_LAT = 111132.0;
        double metersPerDegLon = METERS_PER_DEG_LAT * Math.Cos((aLat + bLat) * 0.5 * (Math.PI / 180.0));

        double bx = (bLon - aLon) * metersPerDegLon, by = (bLat - aLat) * METERS_PER_DEG_LAT;
        double px = (pLon - aLon) * metersPerDegLon, py = (pLat - aLat) * METERS_PER_DEG_LAT;

        double lenSq = bx * bx + by * by;
        if (lenSq < 1e-9) return 0.0;

        double t = (px * bx + py * by) / lenSq;
        return Math.Clamp(t, 0.0, 1.0);
    }
}

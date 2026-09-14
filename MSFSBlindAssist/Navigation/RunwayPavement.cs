namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Runway PAVEMENT geometry for "is this point on, or does this line touch, a runway?" questions.
/// Uses only the <c>Pavement*</c> fields of <see cref="TaxiGraph.RunwayCenterline"/>, which
/// come from the runway table (and fall back to the start rows when Build had no runway
/// table).
///
/// <para>Shared by <c>TaxiGraph.BridgeOrphanParkingIslands</c> (a fabricated bridge must never
/// end on or cross a runway) and <see cref="RouteReachability"/> (a route must never start
/// with a straight unmapped leg across a runway). Known limit: a runway Build could not pair
/// into a centreline (an unpaired start row) is invisible here.</para>
/// </summary>
public static class RunwayPavement
{
    /// <summary>
    /// True when the point lies within any runway's pavement half-width of its pavement
    /// centreline segment (distance clamped to the segment, so points beyond a runway end
    /// are measured to that end).
    /// </summary>
    public static bool IsOnPavement(
        double lat, double lon, IReadOnlyList<TaxiGraph.RunwayCenterline> centerlines)
    {
        foreach (var cl in centerlines)
        {
            double perp = TaxiGraph.PerpendicularDistanceMetersStatic(
                lat, lon, cl.PavementLat1, cl.PavementLon1, cl.PavementLat2, cl.PavementLon2);
            if (perp <= cl.PavementHalfWidthMeters) return true;
        }
        return false;
    }

    /// <summary>
    /// True when the segment a→b strictly crosses a runway's pavement centreline, or comes
    /// within that runway's pavement half-width of it anywhere along its length. The minimum
    /// distance between two non-crossing segments is the smallest of the four clamped
    /// endpoint-to-segment distances.
    /// </summary>
    /// <param name="designator">The touched runway, named after the end nearer the segment's
    /// midpoint (the <c>WhichRunwayCrossedByEdge</c> convention); empty when nothing is
    /// touched.</param>
    public static bool SegmentTouchesPavement(
        double aLat, double aLon, double bLat, double bLon,
        IReadOnlyList<TaxiGraph.RunwayCenterline> centerlines, out string designator)
    {
        designator = "";
        foreach (var cl in centerlines)
        {
            bool crosses = TaxiGraph.EdgeCrossesRunwayStatic(
                aLat, aLon, bLat, bLon,
                cl.PavementLat1, cl.PavementLon1, cl.PavementLat2, cl.PavementLon2);

            double minDistance = Math.Min(
                Math.Min(
                    TaxiGraph.PerpendicularDistanceMetersStatic(
                        aLat, aLon, cl.PavementLat1, cl.PavementLon1, cl.PavementLat2, cl.PavementLon2),
                    TaxiGraph.PerpendicularDistanceMetersStatic(
                        bLat, bLon, cl.PavementLat1, cl.PavementLon1, cl.PavementLat2, cl.PavementLon2)),
                Math.Min(
                    TaxiGraph.PerpendicularDistanceMetersStatic(
                        cl.PavementLat1, cl.PavementLon1, aLat, aLon, bLat, bLon),
                    TaxiGraph.PerpendicularDistanceMetersStatic(
                        cl.PavementLat2, cl.PavementLon2, aLat, aLon, bLat, bLon)));

            if (!crosses && minDistance > cl.PavementHalfWidthMeters) continue;

            double midLat = (aLat + bLat) * 0.5, midLon = (aLon + bLon) * 0.5;
            double d1 = TaxiGraph.FastDistanceMeters(midLat, midLon, cl.Lat1, cl.Lon1);
            double d2 = TaxiGraph.FastDistanceMeters(midLat, midLon, cl.Lat2, cl.Lon2);
            string name = d1 <= d2 ? cl.Name1 : cl.Name2;
            designator = string.IsNullOrEmpty(name) ? cl.Name1 : name;
            return true;
        }
        return false;
    }
}

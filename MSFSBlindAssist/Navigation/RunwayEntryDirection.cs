namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Which way a route FACES as it arrives on its departure runway. Where one cleared taxiway meets
/// the runway through two curves — one bending onto it in the takeoff direction, one bending onto
/// it the other way — the destination node nearest the full-length lineup point can be the tip of
/// the WRONG curve. EGLL 09R via NB10 (A220, 2026-09-24): NB10 splits 41 m from the centreline;
/// its west curve ends 11 m from the lineup point facing 270°, its east curve ends 150 m down the
/// runway facing 090°. The route took the west curve, the entry-stub tone turned the pilot right
/// onto it, and at the pavement edge the centreline intercept demanded a ~100° LEFT turn in 23 m.
/// The aircraft crossed the centreline and ran 94 m off the far side of a 50 m runway.
/// </summary>
internal static class RunwayEntryDirection
{
    /// <summary>An arrival within this many degrees of the RECIPROCAL of the takeoff heading is reversed.
    /// Tight on purpose: an entry angled back against the takeoff direction (EGLL 09L AB11, ~45°
    /// against, i.e. 135° off) is a normal rapid entry and must not count.</summary>
    public const double ReversedToleranceDeg = 35.0;

    /// <summary>A replacement arrival must be within this many degrees of the takeoff heading
    /// (a perpendicular entry is fine; anything facing backwards is not).</summary>
    public const double ForwardMaxOffDeg = 100.0;

    /// <summary>The arrival bearing is measured over at least this much of the route's end, so a
    /// 1-2 m node-placement stub cannot decide it.</summary>
    public const double ArrivalMinLengthM = 10.0;

    /// <summary>A turn sharper than this between consecutive legs near the end is a U-turn on the
    /// runway — a route that only "faces forward" because it turns round on the pavement.</summary>
    public const double HairpinDeg = 120.0;

    /// <summary>
    /// Bearing from the point at least <see cref="ArrivalMinLengthM"/> back along the polyline to its
    /// last point, or null when the polyline is shorter than that.
    /// </summary>
    public static double? ArrivalBearing(IReadOnlyList<(double Lat, double Lon)> pts)
    {
        if (pts == null || pts.Count < 2) return null;
        var end = pts[^1];
        double run = 0;
        for (int i = pts.Count - 2; i >= 0; i--)
        {
            run += TaxiGraph.FastDistanceMeters(pts[i].Lat, pts[i].Lon, pts[i + 1].Lat, pts[i + 1].Lon);
            if (run >= ArrivalMinLengthM)
                return NavigationCalculator.CalculateBearing(pts[i].Lat, pts[i].Lon, end.Lat, end.Lon);
        }
        return null;
    }

    public static bool IsReversed(double? arrivalBearing, double takeoffHeadingTrue)
        => arrivalBearing.HasValue
           && Math.Abs(TaxiGraph.NormalizeAngle(arrivalBearing.Value - (takeoffHeadingTrue + 180.0))) <= ReversedToleranceDeg;

    public static bool IsForward(double? arrivalBearing, double takeoffHeadingTrue)
        => arrivalBearing.HasValue
           && Math.Abs(TaxiGraph.NormalizeAngle(arrivalBearing.Value - takeoffHeadingTrue)) <= ForwardMaxOffDeg;

    /// <summary>
    /// True when the last <paramref name="withinLastM"/> metres of the polyline contain a turn
    /// sharper than <see cref="HairpinDeg"/> between consecutive legs of at least 3 m.
    /// </summary>
    public static bool HairpinsNearEnd(IReadOnlyList<(double Lat, double Lon)> pts, double withinLastM)
    {
        if (pts == null || pts.Count < 3) return false;
        double run = 0;
        double? laterBearing = null; // bearing of the leg AFTER the current one (walking backwards)
        for (int i = pts.Count - 2; i >= 0 && run <= withinLastM; i--)
        {
            double len = TaxiGraph.FastDistanceMeters(pts[i].Lat, pts[i].Lon, pts[i + 1].Lat, pts[i + 1].Lon);
            run += len;
            if (len < 3.0) continue;
            double brg = NavigationCalculator.CalculateBearing(pts[i].Lat, pts[i].Lon, pts[i + 1].Lat, pts[i + 1].Lon);
            if (laterBearing.HasValue && Math.Abs(TaxiGraph.NormalizeAngle(laterBearing.Value - brg)) > HairpinDeg)
                return true;
            laterBearing = brg;
        }
        return false;
    }
}

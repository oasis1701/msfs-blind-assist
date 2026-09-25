namespace MSFSBlindAssist.Services;

/// <summary>A point on the route ahead, with its cumulative route distance.</summary>
public readonly record struct GroundTrafficRoutePoint(double Lat, double Lon, string Taxiway, double RouteMetres);

/// <summary>Where a traffic aircraft sits relative to the route ahead; <see cref="SegmentBearingDeg"/> is the true bearing of the leg it projects onto, in route order.</summary>
internal readonly record struct RouteProjection(double LateralMetres, double RouteMetres, string Taxiway, double SegmentBearingDeg);

internal static partial class GroundTrafficLogic
{
    /// <summary>Flat-earth metres of (lat, lon) from the reference point: x east, y north.</summary>
    public static (double X, double Y) ToLocal(double refLat, double refLon, double lat, double lon)
    {
        double x = (lon - refLon) * MetresPerDegLonEquator * Math.Cos(refLat * Math.PI / 180.0);
        double y = (lat - refLat) * MetresPerDegLat;
        return (x, y);
    }

    /// <summary>Velocity in metres per second (x east, y north) from a true heading and ground speed.</summary>
    public static (double Vx, double Vy) Velocity(double headingTrueDeg, double groundSpeedKts)
    {
        double r = headingTrueDeg * Math.PI / 180.0;
        double v = Math.Max(0.0, groundSpeedKts) * KtsToMps;
        return (v * Math.Sin(r), v * Math.Cos(r));
    }

    /// <summary>
    /// True bearing (0 = north, clockwise, [0, 360)) of a local east/north offset in metres,
    /// as produced by <see cref="ToLocal"/>. A zero offset gives 0.
    /// </summary>
    internal static double LocalBearingDeg(double dxEast, double dyNorth)
        => (Math.Atan2(dxEast, dyNorth) * 180.0 / Math.PI + 360.0) % 360.0;

    /// <summary>
    /// Closest point of approach of two straight-line tracks. Inputs are the TRAFFIC's position and
    /// velocity RELATIVE to the own aircraft. Returns the time until closest approach (0 when the
    /// two are already opening or not moving relative to each other) and the distance then.
    /// </summary>
    public static (double TimeSec, double DistanceM) ClosestApproach(double rx, double ry, double rvx, double rvy)
    {
        double v2 = rvx * rvx + rvy * rvy;
        double t = v2 < 1e-6 ? 0.0 : Math.Max(0.0, -(rx * rvx + ry * rvy) / v2);
        double cx = rx + rvx * t, cy = ry + rvy * t;
        return (t, Math.Sqrt(cx * cx + cy * cy));
    }

    /// <summary>Signed smallest angle a − b, in (−180, 180].</summary>
    public static double AngleDiff(double a, double b)
    {
        double d = ((a - b) % 360.0 + 540.0) % 360.0 - 180.0;
        return d == -180.0 ? 180.0 : d;
    }

    /// <summary>Plain-language relative direction: 0 = dead ahead, clockwise.</summary>
    public static string DescribeDirection(double relBearing)
    {
        relBearing = ((relBearing % 360.0) + 360.0) % 360.0;
        bool right = relBearing < 180.0;
        double abs = right ? relBearing : (360.0 - relBearing);

        if (abs <= 20.0) return "ahead";
        if (abs <= 70.0) return right ? "ahead and to the right" : "ahead and to the left";
        if (abs <= 110.0) return right ? "to the right" : "to the left";
        if (abs <= 160.0) return right ? "behind and to the right" : "behind and to the left";
        return "behind";
    }

    /// <summary>"from the left" / "from the right" / "from ahead" / "from behind".</summary>
    public static string DescribeSide(double relBearing)
    {
        double d = AngleDiff(relBearing, 0.0);
        double abs = Math.Abs(d);
        if (abs <= 25.0) return "from ahead";
        if (abs >= 155.0) return "from behind";
        return d > 0 ? "from the right" : "from the left";
    }

    /// <summary>
    /// Nearest point of the route polyline to (lat, lon): lateral distance, route distance there
    /// (from the polyline's first point), and the taxiway of that stretch. Null when the route has
    /// fewer than two points.
    /// </summary>
    public static RouteProjection? ProjectOntoRoute(IReadOnlyList<GroundTrafficRoutePoint> route,
                                                    double lat, double lon)
    {
        if (route.Count < 2) return null;
        double refLat = route[0].Lat, refLon = route[0].Lon;
        var (px, py) = ToLocal(refLat, refLon, lat, lon);

        RouteProjection? best = null;
        double bestD = double.MaxValue;
        for (int i = 0; i + 1 < route.Count; i++)
        {
            var (ax, ay) = ToLocal(refLat, refLon, route[i].Lat, route[i].Lon);
            var (bx, by) = ToLocal(refLat, refLon, route[i + 1].Lat, route[i + 1].Lon);
            double dx = bx - ax, dy = by - ay;
            double len2 = dx * dx + dy * dy;
            double t = len2 < 1e-6 ? 0.0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / len2, 0.0, 1.0);
            double cx = ax + dx * t, cy = ay + dy * t;
            double d = Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
            if (d < bestD)
            {
                bestD = d;
                double segLen = route[i + 1].RouteMetres - route[i].RouteMetres;
                double bearing = LocalBearingDeg(dx, dy);
                best = new RouteProjection(d, route[i].RouteMetres + segLen * t, route[i + 1].Taxiway, bearing);
            }
        }
        return best;
    }
}

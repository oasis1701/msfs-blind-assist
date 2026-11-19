namespace MSFSBlindAssist.Services.PMDG;

/// <summary>Which map the Captain's ND draws: expanded MAP, centred MAP (CTR, with or without the VSD), PLN,
/// or another mode (APP, VOR).</summary>
public enum NdView { ExpandedMap, CentredMap, Plan, Other }

/// <summary>Where a route leaves the map: the screen edge nearest the crossing ("left", "right", "top",
/// "bottom") and the index of the piece (or the route point before it) that leaves.</summary>
public sealed record NdEdgeCrossing(string Edge, int AfterIndex);

/// <summary>
/// The ND map's geometry as PMDG draws it (its projection, screen centre, compass radius and clip; the
/// constants in <see cref="NdGeometry"/>): the map's top is the track, or the
/// heading while the IRS is degraded (true north in PLN); an item's place is its great-circle distance and
/// bearing from the map's centre, turned off the top of the map and scaled by the view's compass radius; it
/// is on the map while inside the view's clip and the screen. Pure.
/// </summary>
public static class Pmdg737NavDisplayGeometry
{
    public const double EarthRadiusNm = 3440.065;

    /// <summary>Segments per route leg when looking for where it leaves the map, and bisection steps.</summary>
    private const int LegSteps = 128, Bisections = 30;

    private static readonly string[] CompassWords =
    {
        "north", "north-north-east", "north-east", "east-north-east", "east", "east-south-east", "south-east", "south-south-east",
        "south", "south-south-west", "south-west", "west-south-west", "west", "west-north-west", "north-west", "north-north-west",
    };

    /// <summary>MAP not centred: expanded; MAP centred (the VSD keeps CTR set): centred; PLN; else another mode.</summary>
    public static NdView ViewOf(NdPanelState p, NdModeValues modes) =>
        p.Mode == modes.Map ? (p.Centered ? NdView.CentredMap : NdView.ExpandedMap) : p.Mode == modes.Plan ? NdView.Plan : NdView.Other;

    /// <summary>The view's screen and clip, or null for APP and VOR.</summary>
    public static NdViewport? Viewport(NdView view, NdGeometry g) => view switch
    {
        NdView.ExpandedMap => g.Expanded,
        NdView.CentredMap => g.Centred,
        NdView.Plan => g.Plan,
        _ => null,
    };

    /// <summary>PMDG turns the ND track-up only in MAP with its track-up option set and the side's IRU
    /// not in a degraded condition (PMDG's track-up flag); else heading-up, APP and VOR always.</summary>
    public static bool IsTrackUp(NavDisplaySample s, Pmdg737NavDisplayLayout l) =>
        s.TrackUpOption && s.Panel.Mode == l.Orientation.TrackUpMode && s.IruCondition < l.Orientation.DegradedIruCondition;

    /// <summary>
    /// The true direction the top of the map points (degrees): true north in PLN; else the track (or the
    /// heading, heading-up) turned true by the magnetic variation (PMDG's glass data values are magnetic; with
    /// MAG it turns each bearing magnetic instead, which is the same angle). Null while the value PMDG would
    /// use is invalid (cold and dark).
    /// </summary>
    public static double? UpDirectionDeg(NavDisplaySample s, Pmdg737NavDisplayLayout l)
    {
        if (s.Panel.Mode == l.Geometry.Modes.Plan) return 0;
        double up = IsTrackUp(s, l) ? s.TrackDeg : s.HeadingDeg;
        if (!double.IsFinite(up) || up <= l.Orientation.TrackInvalidAtOrBelow) return null;
        return Wrap360(up + s.MagneticVariationDeg);
    }

    /// <summary>The great-circle distance (NM) and initial true bearing (0 to 360) between two points.</summary>
    public static (double DistanceNm, double BearingTrueDeg) Measure(GeoPoint from, GeoPoint to)
    {
        double lat1 = from.LatitudeRad, lat2 = to.LatitudeRad, dLon = to.LongitudeRad - from.LongitudeRad;
        double a = Math.Pow(Math.Sin((lat2 - lat1) / 2), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow(Math.Sin(dLon / 2), 2);
        double distance = 2 * EarthRadiusNm * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0, 1 - a)));
        double bearing = Math.Atan2(Math.Sin(dLon) * Math.Cos(lat2), Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon));
        return (distance, Wrap360(bearing * 180 / Math.PI));
    }

    /// <summary>The deviation bar's place in dots, right positive: PMDG's clamp, then its dots per unit.</summary>
    public static double DeviationDots(double deviation, NdCdiFields cdi) =>
        Math.Clamp(deviation, -cdi.FullScale, cdi.FullScale) * cdi.DotsPerUnit;

    /// <summary>Degrees off the top of the map, right positive: more than -180, at most 180.</summary>
    public static double RelativeDeg(double bearingTrueDeg, double upTrueDeg)
    {
        double r = Wrap360(bearingTrueDeg - upTrueDeg);
        return r > 180 ? r - 360 : r;
    }

    /// <summary>
    /// PMDG's projection: the view's centre plus the compass radius × distance / (the range the radius stands
    /// for: the selected range × the view's factor, halved centred and in PLN), turned by the degrees off the
    /// top of the map (screen units, y down).
    /// </summary>
    public static (double X, double Y) Project(NdView view, double distanceNm, double relativeDeg, double selectedRangeNm, NdGeometry g)
    {
        var v = Viewport(view, g) ?? throw new ArgumentException("APP and VOR draw no map", nameof(view));
        double r = v.CompassRadius * distanceNm / (selectedRangeNm * v.RangeFactor), a = relativeDeg * Math.PI / 180;
        return (v.CentreX + r * Math.Sin(a), v.CentreY - r * Math.Cos(a));
    }

    /// <summary>Inside the screen.</summary>
    public static bool OnScreen((double X, double Y) p, NdGeometry g) => p.X >= 0 && p.X <= g.ScreenSize && p.Y >= 0 && p.Y <= g.ScreenSize;

    /// <summary>PMDG's cull turned round, in the full format: whether it draws a symbol at this point of the
    /// view (and, for the nearby lists, counts it toward EXCESS DATA) before the clip cuts it.</summary>
    public static bool Drawn(NdView view, (double X, double Y) point, NdGeometry g)
    {
        if (Viewport(view, g) is not { } v) return false;
        var cull = g.Cull;
        double radius = v.CompassRadius * (view == NdView.CentredMap ? cull.CentredRadiusFactor : 1);
        double x = point.X - v.CentreX, y = point.Y - v.CentreY;
        return !(x < -cull.HalfWidth || x > cull.HalfWidth || y < -(radius + cull.TopMargin) || v.CentreY + y > cull.BottomLimit);
    }

    /// <summary>Inside the view's clip (PMDG's own shape) and the screen.</summary>
    public static bool OnMap(NdView view, (double X, double Y) point, NdGeometry g)
    {
        if (Viewport(view, g) is not { } v || !OnScreen(point, g)) return false;
        var (x, y) = point;
        return v.Clip switch
        {
            NdClipShape.Circle => Math.Pow(x - v.CentreX, 2) + Math.Pow(y - v.CentreY, 2) <= v.ClipRadius * v.ClipRadius,
            NdClipShape.Rectangles => v.Rectangles.Any(r => x >= r.X && x <= r.X + r.Width && y >= r.Y && y <= r.Y + r.Height),
            NdClipShape.ArcAndBottom => InsidePolygon(ArcAndBottom(v), x, y),
            _ => false,
        };
    }

    /// <summary>
    /// Where the route leaves the map: for each leg between consecutive projected points, the last place it
    /// goes from on the map to off it (a leg can enter and leave), named by the screen edge nearest that place.
    /// </summary>
    public static IReadOnlyList<NdEdgeCrossing> RouteEdges(IReadOnlyList<(double X, double Y)> projected, NdView view, NdGeometry g) =>
        PieceEdges(projected.Zip(projected.Skip(1)).ToList(), view, g);

    /// <summary>
    /// Where pieces of the route line leave the map: for each piece (as drawn, from one projected point to
    /// another), the last place it goes from on the map to off it, named by the screen edge nearest that place
    /// (<see cref="NdEdgeCrossing.AfterIndex"/>: the piece's index).
    /// </summary>
    public static IReadOnlyList<NdEdgeCrossing> PieceEdges(IReadOnlyList<((double X, double Y) From, (double X, double Y) To)> pieces, NdView view, NdGeometry g)
    {
        var crossings = new List<NdEdgeCrossing>();
        for (int k = 0; k < pieces.Count; k++)
        {
            var (a, b) = pieces[k];
            (double, double) At(double t) => (a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            double? leaving = null;
            bool inside = OnMap(view, a, g);
            for (int i = 1; i <= LegSteps; i++)
            {
                double t = (double)i / LegSteps;
                bool now = OnMap(view, At(t), g);
                if (inside && !now)
                {
                    double lo = (double)(i - 1) / LegSteps, hi = t;
                    for (int j = 0; j < Bisections; j++)
                    {
                        double mid = (lo + hi) / 2;
                        if (OnMap(view, At(mid), g)) lo = mid; else hi = mid;
                    }
                    leaving = lo;
                }
                inside = now;
            }
            if (leaving is { } at) crossings.Add(new NdEdgeCrossing(NearestEdge(At(at), g), k));
        }
        return crossings;
    }

    /// <summary>The nearest of the 16 compass points to a true bearing.</summary>
    public static string CompassWord(double bearingTrueDeg) => CompassWords[(int)Math.Round(Wrap360(bearingTrueDeg) / 22.5, MidpointRounding.AwayFromZero) % 16];

    /// <summary>The selected range (NM) PMDG gives the range selector's index; past its table,
    /// the table's default (its last range).</summary>
    public static double SelectedRangeNm(NdPanelState p, NdGeometry g) =>
        p.Range >= 0 && p.Range < g.SelectedRangesNm.Count ? g.SelectedRangesNm[p.Range] : g.SelectedRangesNm[^1];

    /// <summary>A TCAS target's degrees off the top of the map: PMDG's traffic bearing is from the nose.</summary>
    public static double TrafficRelativeDeg(double bearingRad, NavDisplaySample s, Pmdg737NavDisplayLayout l) =>
        NoseRelativeDeg(bearingRad * 180 / Math.PI, s, l);

    /// <summary>Degrees off the top of the screen of a direction PMDG measures from the nose (a TCAS bearing, a
    /// bearing pointer): PMDG turns it by heading − its up direction, so by heading − track while track-up.</summary>
    public static double NoseRelativeDeg(double deg, NavDisplaySample s, Pmdg737NavDisplayLayout l) =>
        RelativeDeg(deg + (IsTrackUp(s, l) ? s.HeadingDeg - s.TrackDeg : 0), 0);

    private static double Wrap360(double deg)
    {
        double r = deg % 360;
        return r < 0 ? r + 360 : r;
    }

    /// <summary>GDI+'s figure: the arc (angles clockwise from +x, y down), joined to the bottom line, closed back.</summary>
    private static List<(double X, double Y)> ArcAndBottom(NdViewport v)
    {
        var points = new List<(double X, double Y)>();
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(v.ArcSweepDeg)));
        for (int i = 0; i <= steps; i++)
        {
            double a = (v.ArcStartDeg + v.ArcSweepDeg * i / steps) * Math.PI / 180;
            points.Add((v.CentreX + v.ClipRadius * Math.Cos(a), v.CentreY + v.ClipRadius * Math.Sin(a)));
        }
        points.Add((v.BottomFromX, v.BottomY));
        points.Add((v.BottomToX, v.BottomY));
        return points;
    }

    /// <summary>Even-odd point in polygon (GDI+'s default fill mode for a path clip).</summary>
    private static bool InsidePolygon(IReadOnlyList<(double X, double Y)> polygon, double x, double y)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }

    private static string NearestEdge((double X, double Y) p, NdGeometry g)
    {
        var edges = new[] { ("left", p.X), ("right", g.ScreenSize - p.X), ("top", p.Y), ("bottom", g.ScreenSize - p.Y) };
        return edges.MinBy(e => Math.Abs(e.Item2)).Item1;
    }
}

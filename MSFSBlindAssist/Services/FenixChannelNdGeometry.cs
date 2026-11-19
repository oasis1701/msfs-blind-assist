using System.Globalization;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What the ND's picture is drawn around: the mode, the range, the direction at the top of the screen and the point at
/// its center (the aircraft in every mode but PLAN, the PLAN center in PLAN). Measured on 3.0.0.7613 (evidence
/// 03-nd.md, Part B). It never needs a flight plan.
/// </summary>
/// <param name="Mode">"LS", "VOR", "NAV", "ARC" or "PLAN".</param>
/// <param name="RangeNm">The selected range in nautical miles.</param>
/// <param name="UpTrueDegrees">The true direction at the top of the screen: the true heading, or in PLAN magnetic north.</param>
/// <param name="Center">Where the picture is centered.</param>
/// <param name="MagneticVariation">East positive; true = magnetic + variation.</param>
internal sealed record FenixNdFrame(string Mode, double RangeNm, double UpTrueDegrees, FenixLatLon Center, double MagneticVariation)
{
    /// <summary>The upper message box is drawn (the captain's list holds "TA ONLY"): it hides what lies under it.</summary>
    public bool UpperBox { get; init; }

    /// <summary>The lower message box is drawn (the captain's list holds an entry other than "TA ONLY").</summary>
    public bool LowerBox { get; init; }

    /// <summary>
    /// The frame of a header, or null when a value it needs is missing: the mode (any of the five), a range that
    /// parses, the center (the PLAN center in PLAN, else the present position), the variation, and outside PLAN the
    /// heading. ARC, ROSE NAV, ROSE LS and ROSE VOR are heading-up on the true heading; PLAN is magnetic-north-up. The
    /// message boxes drawn follow the captain's list: "TA ONLY" the upper box, any other entry the lower one.
    /// </summary>
    public static FenixNdFrame? From(FenixNdData header)
    {
        string? mode = header.Mode;
        if (mode is not ("LS" or "VOR" or "NAV" or "ARC" or "PLAN")) return null;
        if (!NumberParsing.TryParseExternal(header.Range, out double range) || !(range > 0) || double.IsInfinity(range)) return null;
        if (header.MagneticVariation is not double variation) return null;
        bool plan = mode == "PLAN";
        FenixLatLon? center = plan ? header.PlanCenter : header.Position;
        if (center is not FenixLatLon c) return null;
        double up;
        if (plan) up = variation;
        else if (header.MagneticHeading is double heading) up = heading + variation;
        else return null;

        bool upper = header.ListedMessages.Any(entry => FenixChannelText.TrimmedText(entry) == TaOnly);
        bool lower = header.ListedMessages.Any(entry => FenixChannelText.TrimmedText(entry) != TaOnly);
        return new FenixNdFrame(mode, range, ((up % 360) + 360) % 360, c, variation) { UpperBox = upper, LowerBox = lower };
    }

    private const string TaOnly = "TA ONLY";
}

/// <summary>Where the ND draws a point: its distance from the center, its degrees off the top of the screen
/// (-180 to 180, negative to the left) and whether a symbol there is drawn cut by an edge.</summary>
internal readonly record struct FenixNdPlace(double DistanceNm, double DegreesOffUp, bool AtEdge);

/// <summary>
/// The ND's geometry: where it draws a point, how to say that place, and the along- and cross-track distances.
/// Works in the measured pixels of the 752 x 729 ND window, in nautical miles and true degrees. The clip is the one
/// the flight measured (evidence, sitting 2, "Where the ND draws: the edges, re-measured"; ROSE and PLAN map points
/// from sitting 1).
/// </summary>
internal static class FenixChannelNdGeometry
{
    private const double CenterX = 375.5;
    private const double RoseCenterY = 357;
    private const double RoseRingRadiusPx = 241.5;   // stands for half the range
    private const double ArcAircraftY = 597;
    private const double ArcRangePx = 473;           // stands for the whole range
    private const double WindowWidth = 752;
    private const double WindowBottom = 729;
    private const double EdgePx = 7;                 // half the size of a drawn symbol
    private const double EarthRadiusNm = 3440.065;

    // ARC, map points and route items alike: a circle around the aircraft symbol, the window's sides as far as a
    // symbol's center is seen, down to the window's bottom, minus the two bottom corners.
    private const double ArcClipRadiusPx = 478;
    private const double ArcLeftX = 5;
    private const double ArcRightX = 745;
    private const double LeftCornerX = 152;          // the left corner: x < 152 and y > 632
    private const double LeftCornerY = 632;

    // The right corner, a polygon (clockwise on the screen): nothing is drawn inside it.
    private static readonly (double X, double Y)[] RightCorner =
        [(632, 552), (752, 552), (752, 729), (557, 729), (557, 640)];

    // ROSE (NAV, LS, VOR) and PLAN: map points from y 115 and within 290 px of the center; route items in the
    // window from y 77 down (ARTUR's symbol was drawn centered at y 77; the line itself from about y 80).
    private const double MapTopY = 115;
    private const double RoseClipRadiusPx = 290;
    private const double RouteTopY = 77;

    // The message boxes: each hides its width from its top to the window's bottom while it is drawn.
    private const double BoxLeftX = 170;
    private const double BoxRightX = 567;
    private const double UpperBoxTopY = 646;
    private const double LowerBoxTopY = 673;

    private static readonly string[] CompassPoints =
        { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

    private static bool IsArc(FenixNdFrame f) => f.Mode == "ARC";

    /// <summary>The distance in nautical miles, the degrees off up (-180..180) and the pixel of a point.</summary>
    private static (double Distance, double Off, double X, double Y) Locate(FenixNdFrame f, FenixLatLon p)
    {
        double distance = NavigationCalculator.CalculateDistance(f.Center.Latitude, f.Center.Longitude, p.Latitude, p.Longitude);
        double bearing = NavigationCalculator.CalculateBearing(f.Center.Latitude, f.Center.Longitude, p.Latitude, p.Longitude);
        double off = ((bearing - f.UpTrueDegrees + 180) % 360 + 360) % 360 - 180;   // -180 up to but not including 180
        if (off == -180) off = 180;
        double ppn = PixelsPerNm(f);
        double cy = IsArc(f) ? ArcAircraftY : RoseCenterY;
        double rad = off * Math.PI / 180;
        return (distance, off, CenterX + Math.Sin(rad) * distance * ppn, cy - Math.Cos(rad) * distance * ppn);
    }

    /// <summary>The pixel the ND window draws a point at, drawn or not.</summary>
    public static (double X, double Y) Pixel(FenixNdFrame f, FenixLatLon p)
    {
        var l = Locate(f, p);
        return (l.X, l.Y);
    }

    /// <summary>
    /// Where the ND draws a map point (a navaid, fix, airport, NDB or traffic), or null when it draws nothing there.
    /// ARC: within 478 px of the aircraft symbol, 5 &lt;= x &lt;= 745, y &lt;= 729, outside the two bottom corners. ROSE
    /// and PLAN: y &gt;= 115 and within 290 px of the center. Never under a drawn message box.
    /// </summary>
    public static FenixNdPlace? Place(FenixNdFrame f, FenixLatLon p) => PlaceIn(f, p, route: false);

    /// <summary>
    /// Where the ND draws a route item (a waypoint, a marker, a point of the route line), or null when it draws
    /// nothing there. ARC: as <see cref="Place"/>. ROSE and PLAN: the window from y 77 to its bottom (0 &lt;= x &lt;= 752,
    /// 77 &lt;= y &lt;= 729; ARTUR drawn at y 77; PLAN's top measured in ROSE only). Never under a drawn message box.
    /// </summary>
    public static FenixNdPlace? PlaceRoute(FenixNdFrame f, FenixLatLon p) => PlaceIn(f, p, route: true);

    /// <summary>
    /// Where the ND draws a traffic symbol, or null when it draws nothing there. Traffic has a clip of its own
    /// (evidence03-nd.md, sitting 2, "Traffic: the ring and the edges"): in ARC, ROSE NAV, ROSE LS
    /// and ROSE VOR a symbol is drawn while its center lies inside the window (0 &lt;= x &lt;= 752, 0 &lt;= y &lt;= 729), in
    /// ARC down to the window's bottom, with no half symbol at an edge and nothing under a drawn message box. Not the map
    /// points' clip: no top line at y 115, no 290 px circle, no ARC circle or bottom corners. PLAN is not measured: null.
    /// </summary>
    public static FenixNdPlace? PlaceTraffic(FenixNdFrame f, FenixLatLon p)
    {
        if (f.Mode is not ("NAV" or "ARC" or "LS" or "VOR")) return null;

        var l = Locate(f, p);
        if (!(l.X >= 0 && l.X <= WindowWidth && l.Y >= 0 && l.Y <= WindowBottom)) return null;
        if (f.UpperBox && OutsideRectangle(l.X, l.Y, BoxLeftX, BoxRightX, UpperBoxTopY) < 0) return null;
        if (f.LowerBox && OutsideRectangle(l.X, l.Y, BoxLeftX, BoxRightX, LowerBoxTopY) < 0) return null;
        return new FenixNdPlace(l.Distance, l.Off, false);
    }

    private static FenixNdPlace? PlaceIn(FenixNdFrame f, FenixLatLon p, bool route)
    {
        var l = Locate(f, p);
        double margin = Margin(f, l.X, l.Y, route).Margin;
        if (!(margin >= 0)) return null;
        return new FenixNdPlace(l.Distance, l.Off, margin < EdgePx);
    }

    /// <summary>The window's size in pixels.</summary>
    internal const double WindowWidthPx = WindowWidth, WindowHeightPx = WindowBottom;

    /// <summary>
    /// Whether the ND draws a route item at a pixel (<see cref="PlaceRoute"/>'s clip) and, when it does not, the edge it
    /// lies beyond: "top" (the ARC's circle or the window's top), "left" (the frame or the ARC's left corner), "right"
    /// (the frame or the ARC's right corner) or "bottom" (the window's bottom or a message box).
    /// </summary>
    internal static (bool Drawn, string Edge) RoutePixel(FenixNdFrame f, double x, double y)
    {
        (double margin, string edge) = Margin(f, x, y, route: true);
        return (margin >= 0, edge);
    }

    /// <summary>The place a pixel stands for: its distance from the center and its degrees off up; never at an edge.</summary>
    internal static FenixNdPlace PlaceOfPixel(FenixNdFrame f, double x, double y)
    {
        double dx = x - CenterX, dy = (IsArc(f) ? ArcAircraftY : RoseCenterY) - y;
        double off = Math.Atan2(dx, dy) * 180 / Math.PI;
        if (off <= -180) off += 360;
        return new FenixNdPlace(Hypot(dx, dy) / PixelsPerNm(f), off, false);
    }

    /// <summary>Pixels per nautical mile: the ARC's arc stands for the whole range, the ROSE ring for half of it.</summary>
    internal static double PixelsPerNm(FenixNdFrame f)
        => IsArc(f) ? ArcRangePx / f.RangeNm : RoseRingRadiusPx / (f.RangeNm / 2);

    /// <summary>
    /// Whether a mark on the compass at <paramref name="degreesOffUp"/> is in view: anywhere on the ROSE ring, on the
    /// ARC's arc only where the arc lies inside the window's sides (about 51 degrees either way).
    /// </summary>
    internal static bool OnCompass(string mode, double degreesOffUp)
    {
        if (mode != "ARC") return true;

        double rad = degreesOffUp * Math.PI / 180;
        double x = CenterX + Math.Sin(rad) * ArcRangePx, y = ArcAircraftY - Math.Cos(rad) * ArcRangePx;
        return x >= ArcLeftX && x <= ArcRightX && y <= WindowBottom;
    }

    // How far a pixel lies inside the area the ND draws in: its distance to the nearest clip line that applies (the
    // circle, a side, the top, the bottom, a corner's edge, a box's edge), negative when it lies beyond one, with the
    // edge that line is on ("top", "left", "right" or "bottom"; each corner is on its own side, a box at the bottom).
    private static (double Margin, string Edge) Margin(FenixNdFrame f, double x, double y, bool route)
    {
        double margin = double.PositiveInfinity;
        string edge = "top";
        if (IsArc(f))
        {
            Nearest(ref margin, ref edge, ArcClipRadiusPx - Hypot(x - CenterX, y - ArcAircraftY), "top");
            Nearest(ref margin, ref edge, x - ArcLeftX, "left");
            Nearest(ref margin, ref edge, ArcRightX - x, "right");
            Nearest(ref margin, ref edge, WindowBottom - y, "bottom");
            Nearest(ref margin, ref edge, OutsideRectangle(x, y, double.NegativeInfinity, LeftCornerX, LeftCornerY), "left");
            Nearest(ref margin, ref edge, OutsidePolygon(x, y, RightCorner), "right");
        }
        else if (route)
        {
            Nearest(ref margin, ref edge, y - RouteTopY, "top");
            Nearest(ref margin, ref edge, x, "left");
            Nearest(ref margin, ref edge, WindowWidth - x, "right");
            Nearest(ref margin, ref edge, WindowBottom - y, "bottom");
        }
        else
        {
            Nearest(ref margin, ref edge, y - MapTopY, "top");
            Nearest(ref margin, ref edge, RoseClipRadiusPx - Hypot(x - CenterX, y - RoseCenterY), y < RoseCenterY ? "top" : "bottom");
        }

        if (f.UpperBox) Nearest(ref margin, ref edge, OutsideRectangle(x, y, BoxLeftX, BoxRightX, UpperBoxTopY), "bottom");
        if (f.LowerBox) Nearest(ref margin, ref edge, OutsideRectangle(x, y, BoxLeftX, BoxRightX, LowerBoxTopY), "bottom");
        return (margin, edge);
    }

    private static void Nearest(ref double margin, ref string edge, double line, string lineEdge)
    {
        if (line < margin)
        {
            margin = line;
            edge = lineEdge;
        }
    }

    private static double Hypot(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    // The signed distance from a pixel to the region left <= x <= right, y >= top (down to the window's bottom):
    // positive outside it, negative inside it.
    private static double OutsideRectangle(double x, double y, double left, double right, double top)
    {
        if (x >= left && x <= right && y >= top) return -Math.Min(Math.Min(x - left, right - x), y - top);

        double dx = Math.Max(0, Math.Max(left - x, x - right));
        double dy = Math.Max(0, top - y);
        return Hypot(dx, dy);
    }

    // The signed distance from a pixel to a polygon: positive outside it, negative inside it.
    private static double OutsidePolygon(double x, double y, (double X, double Y)[] polygon)
    {
        bool inside = false;
        double nearest = double.PositiveInfinity;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            (double xi, double yi) = polygon[i];
            (double xj, double yj) = polygon[j];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) inside = !inside;

            double ex = xj - xi, ey = yj - yi;
            double t = Math.Clamp(((x - xi) * ex + (y - yi) * ey) / (ex * ex + ey * ey), 0, 1);
            nearest = Math.Min(nearest, Hypot(x - (xi + t * ex), y - (yi + t * ey)));
        }

        return inside ? -nearest : nearest;
    }

    /// <summary>
    /// A place in words. Every mode but PLAN: "8.1 NM ahead", "8.1 NM behind", "8.1 NM, 103° left", or "at the
    /// aircraft". PLAN: "5.0 NM north-west of VAKSU" (eight compass points, "of the center" with no ident), or "at
    /// VAKSU". ", at the edge" is added when a symbol there is cut by the edge. Invariant culture.
    /// </summary>
    public static string Where(FenixNdFrame f, FenixNdPlace place, string? centerIdent)
    {
        string distance = place.DistanceNm.ToString("F1", CultureInfo.InvariantCulture);
        bool here = distance == "0.0";
        string text;
        if (f.Mode == "PLAN")
        {
            string name = centerIdent ?? "the center";
            if (here) text = "at " + name;
            else
            {
                double clockwise = ((place.DegreesOffUp % 360) + 360) % 360;
                int index = (int)Math.Floor((clockwise + 22.5) / 45) % 8;
                text = $"{distance} NM {CompassPoints[index]} of {name}";
            }
        }
        else if (here) text = "at the aircraft";
        else
        {
            int degrees = (int)Math.Round(place.DegreesOffUp, MidpointRounding.AwayFromZero);
            if (degrees == 0) text = $"{distance} NM ahead";
            else if (Math.Abs(degrees) == 180) text = $"{distance} NM behind";
            else text = $"{distance} NM, {Math.Abs(degrees).ToString(CultureInfo.InvariantCulture)}° {(degrees < 0 ? "left" : "right")}";
        }
        return place.AtEdge ? text + ", at the edge" : text;
    }

    /// <summary>
    /// The along-track and cross-track distances (nautical miles) of a point from the course <paramref name="from"/> to
    /// <paramref name="to"/>, by the aviation formulary. Cross-track is negative to the left of the course.
    /// </summary>
    public static (double AlongNm, double CrossNm) AlongCross(FenixLatLon from, FenixLatLon to, FenixLatLon p)
    {
        double d13 = NavigationCalculator.CalculateDistance(from.Latitude, from.Longitude, p.Latitude, p.Longitude) / EarthRadiusNm;
        double t13 = NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, p.Latitude, p.Longitude) * Math.PI / 180;
        double t12 = NavigationCalculator.CalculateBearing(from.Latitude, from.Longitude, to.Latitude, to.Longitude) * Math.PI / 180;
        double cross = Math.Asin(Math.Sin(d13) * Math.Sin(t13 - t12));
        double along = Math.Acos(Math.Clamp(Math.Cos(d13) / Math.Cos(cross), -1, 1));
        if (Math.Cos(t13 - t12) < 0) along = -along;
        return (along * EarthRadiusNm, cross * EarthRadiusNm);
    }
}

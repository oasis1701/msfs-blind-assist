using System.Globalization;
using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Services;

/// <summary>
/// What the ND's picture is drawn around: the mode, the range, the direction at the top of the screen and the point at
/// its center (the aircraft in every mode but PLAN, the PLAN center in PLAN). Measured on 3.0.0.7613 (docs/fenix-channel/
/// evidence/03-nd.md, Part B). It never needs a flight plan.
/// </summary>
/// <param name="Mode">"LS", "VOR", "NAV", "ARC" or "PLAN".</param>
/// <param name="RangeNm">The selected range in nautical miles.</param>
/// <param name="UpTrueDegrees">The true direction at the top of the screen: the true heading, or in PLAN magnetic north.</param>
/// <param name="Center">Where the picture is centered.</param>
/// <param name="MagneticVariation">East positive; true = magnetic + variation.</param>
internal sealed record FenixNdFrame(string Mode, double RangeNm, double UpTrueDegrees, FenixLatLon Center, double MagneticVariation)
{
    /// <summary>
    /// The frame of a header, or null when a value it needs is missing: the mode (any of the five), a range that
    /// parses, the center (the PLAN center in PLAN, else the present position), the variation, and outside PLAN the
    /// heading. ARC, ROSE NAV, ROSE LS and ROSE VOR are heading-up on the true heading; PLAN is magnetic-north-up.
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
        return new FenixNdFrame(mode, range, ((up % 360) + 360) % 360, c, variation);
    }
}

/// <summary>Where the ND draws a point: its distance from the center, its degrees off the top of the screen
/// (-180 to 180, negative to the left) and whether a symbol there is drawn cut by an edge.</summary>
internal readonly record struct FenixNdPlace(double DistanceNm, double DegreesOffUp, bool AtEdge);

/// <summary>
/// The ND's geometry: where it draws a point, how to say that place, and the along- and cross-track distances.
/// Works in the measured pixels of the 752 x 729 ND window, in nautical miles and true degrees.
/// </summary>
internal static class FenixChannelNdGeometry
{
    private const double CenterX = 375.5;
    private const double RoseCenterY = 357;
    private const double RoseRingRadiusPx = 241.5;   // stands for half the range
    private const double ArcAircraftY = 597;
    private const double ArcRangePx = 473;           // stands for the whole range
    private const double WindowWidth = 752;
    private const double MapTopY = 115;
    private const double MapBottomY = 630;
    private const double RoseClipRadiusPx = 290;
    private const double EdgePx = 7;                 // half the size of a drawn symbol
    private const double EarthRadiusNm = 3440.065;

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
        double ppn = IsArc(f) ? ArcRangePx / f.RangeNm : RoseRingRadiusPx / (f.RangeNm / 2);
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
    /// Where the ND draws a map point (a navaid, fix, airport, NDB or traffic), or null when it draws nothing there:
    /// the pixel must lie in 115 &lt;= y &lt;= 630 and, in ROSE and PLAN, within 290 px of the center, in ARC within
    /// 473 px of the aircraft and within the window's width.
    /// </summary>
    public static FenixNdPlace? Place(FenixNdFrame f, FenixLatLon p)
    {
        var l = Locate(f, p);
        double cy = IsArc(f) ? ArcAircraftY : RoseCenterY;
        double radius = Math.Sqrt((l.X - CenterX) * (l.X - CenterX) + (l.Y - cy) * (l.Y - cy));
        double limit = IsArc(f) ? ArcRangePx : RoseClipRadiusPx;
        if (l.Y < MapTopY || l.Y > MapBottomY || radius > limit) return null;
        bool atEdge = l.Y - MapTopY < EdgePx || MapBottomY - l.Y < EdgePx || limit - radius < EdgePx;
        if (IsArc(f))
        {
            if (l.X < 0 || l.X > WindowWidth) return null;
            atEdge |= l.X < EdgePx || WindowWidth - l.X < EdgePx;
        }
        return new FenixNdPlace(l.Distance, l.Off, atEdge);
    }

    /// <summary>
    /// Where the ND draws a route item. Provisional: Task 7 refines it from the flight's crops. The window's sides and
    /// top (0 &lt;= x &lt;= 752, y &gt;= 0) and the bottom at y 630, in every mode, with no ring.
    /// </summary>
    public static FenixNdPlace? PlaceRoute(FenixNdFrame f, FenixLatLon p)
    {
        var l = Locate(f, p);
        if (l.X < 0 || l.X > WindowWidth || l.Y < 0 || l.Y > MapBottomY) return null;
        bool atEdge = l.X < EdgePx || WindowWidth - l.X < EdgePx || l.Y < EdgePx || MapBottomY - l.Y < EdgePx;
        return new FenixNdPlace(l.Distance, l.Off, atEdge);
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

using System.Globalization;

namespace MSFSBlindAssist.Services.BBS757;

/// <summary>
/// The EHSI's geometry and its words: BlueBird's top-line distance and ETA, its map projection and clip, and the places
/// the window says. Pure math on doubles and invariant text, so a comma-decimal PC reads the same as an en-US one. The
/// map's constants (radius factor, aircraft Y factor, clip inset, the minimum ground speed) are the layout's values and
/// come in as parameters; the only literals here are BlueBird's own earth radii and unit conversions.
/// </summary>
public static class Bbs757EhsiGeometry
{
    /// <summary>The top line's haversine radius in meters (BlueBird's function at 4581).</summary>
    private const double HaversineRadiusMeters = 6371000.0;

    /// <summary>Meters in a nautical mile, the top line's divisor.</summary>
    private const double MetersPerNauticalMile = 1852.0;

    /// <summary>The map projection's radius in kilometers (BlueBird's function at 23475).</summary>
    private const double ProjectionRadiusKilometers = 6371.0;

    /// <summary>Kilometers in a nautical mile, the projection's divisor.</summary>
    private const double KilometersPerNauticalMile = 1.852;

    private const double DegreesToRadians = Math.PI / 180.0;

    /// <summary>Under this distance in nautical miles a plan place reads "at": it rounds to 0.0 on the screen's own digit.</summary>
    private const double AtDistanceNm = 0.05;

    /// <summary>A bearing within this many degrees of straight ahead or straight behind reads "ahead" or "behind", the
    /// whole-degree rounding's half step: it would otherwise print "0° right".</summary>
    private const double AlignedDegrees = 0.5;

    private static readonly string[] Compass =
        { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

    /// <summary>The top line's distance from the aircraft to the active waypoint in nautical miles: the haversine with
    /// R = 6,371,000 m, divided by 1852 (measured in BlueBird's top-line draw). The asin argument is clamped so rounding cannot give NaN.</summary>
    public static double TopLineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        double p1 = lat1 * DegreesToRadians, p2 = lat2 * DegreesToRadians;
        double dp = p2 - p1, dl = (lon2 - lon1) * DegreesToRadians;
        double sdp = Math.Sin(dp / 2), sdl = Math.Sin(dl / 2);
        double a = sdp * sdp + Math.Cos(p1) * Math.Cos(p2) * sdl * sdl;
        a = Math.Clamp(a, 0.0, 1.0);
        double meters = 2 * HaversineRadiusMeters * Math.Asin(Math.Sqrt(a));
        return meters / MetersPerNauticalMile;
    }

    /// <summary>
    /// Where an item sits on the screen: the great-circle distance (spherical law of cosines, R = 6371 km, divided by
    /// 1.852) and initial true bearing from the map's center to the item, then the pixel point. The pixel radius is
    /// distance / range * (Height * <paramref name="radiusFactor"/>); the screen angle is the bearing minus the map's
    /// rotation, clockwise from up. A range that is not positive gives NaN pixels, which <see cref="OnScreen"/> refuses,
    /// rather than an infinite radius. The acos argument is clamped, so identical points and antipodes give no NaN.
    /// </summary>
    public static (double X, double Y, double Nm, double BearingTrue) Project(
        Bbs757EhsiMap map, double range, double radiusFactor, double lat, double lon)
    {
        double phiC = map.CenterLatitude * DegreesToRadians, phi = lat * DegreesToRadians;
        double dLambda = (lon - map.CenterLongitude) * DegreesToRadians;

        double cosC = Math.Sin(phiC) * Math.Sin(phi) + Math.Cos(phiC) * Math.Cos(phi) * Math.Cos(dLambda);
        double nm = ProjectionRadiusKilometers * Math.Acos(Math.Clamp(cosC, -1.0, 1.0)) / KilometersPerNauticalMile;

        double y = Math.Cos(phiC) * Math.Sin(phi) - Math.Sin(phiC) * Math.Cos(phi) * Math.Cos(dLambda);
        double x = Math.Cos(phi) * Math.Sin(dLambda);
        double bearing = Math.Atan2(x, y) / DegreesToRadians;
        bearing = ((bearing % 360.0) + 360.0) % 360.0;

        if (!(range > 0))
            return (double.NaN, double.NaN, nm, bearing);

        double r = nm / range * (map.Height * radiusFactor);
        double angle = (bearing - map.Rotation) * DegreesToRadians;
        return (map.ScreenX + r * Math.Sin(angle), map.ScreenY - r * Math.Cos(angle), nm, bearing);
    }

    /// <summary>
    /// The point <paramref name="nm"/> nautical miles from (<paramref name="lat"/>, <paramref name="lon"/>) along the true
    /// <paramref name="bearingTrue"/>, on the same sphere as <see cref="Project"/> (R = 6371 km, 1.852 km a mile): the
    /// great-circle destination, its longitude folded to -180..180. For the map's circles and the altitude range arc.
    /// </summary>
    public static (double Latitude, double Longitude) Destination(double lat, double lon, double bearingTrue, double nm)
    {
        double d = nm * KilometersPerNauticalMile / ProjectionRadiusKilometers, b = bearingTrue * DegreesToRadians;
        double p1 = lat * DegreesToRadians, l1 = lon * DegreesToRadians;
        double p2 = Math.Asin(Math.Clamp(Math.Sin(p1) * Math.Cos(d) + Math.Cos(p1) * Math.Sin(d) * Math.Cos(b), -1.0, 1.0));
        double l2 = l1 + Math.Atan2(Math.Sin(b) * Math.Sin(d) * Math.Cos(p1), Math.Cos(d) - Math.Sin(p1) * Math.Sin(p2));
        double longitude = ((l2 / DegreesToRadians + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;
        return (p2 / DegreesToRadians, longitude);
    }

    /// <summary>
    /// BlueBird's clip (23406): the pixel must be inside the screen (0..Width, 0..Height) and, not centered, within
    /// Height * <paramref name="radiusFactor"/> - <paramref name="clipInset"/> pixels of the aircraft point
    /// (Width / 2, Height * <paramref name="aircraftYFactor"/>), in PLAN as well; centered, at or below
    /// Height * (aircraftYFactor - radiusFactor). A NaN pixel is never on screen.
    /// </summary>
    public static bool OnScreen(
        Bbs757EhsiMap map, bool centered, double x, double y,
        double radiusFactor, double aircraftYFactor, double clipInset)
    {
        if (double.IsNaN(x) || double.IsNaN(y)) return false;
        if (x < 0 || x > map.Width || y < 0 || y > map.Height) return false;

        if (centered)
            return y >= map.Height * (aircraftYFactor - radiusFactor);

        double dx = x - map.Width / 2.0;
        double dy = y - map.Height * aircraftYFactor;
        double limit = map.Height * radiusFactor - clipInset;
        return dx * dx + dy * dy <= limit * limit;
    }

    /// <summary>The bearing relative to the screen's up, -180 to 180 with minus left; exactly behind is +180.
    /// <paramref name="up"/> is the map's rotation: the true track in a track-up mode, the true heading otherwise, 0 in PLAN.</summary>
    public static double OffTop(double bearingTrue, double up)
    {
        double d = ((bearingTrue - up) % 360.0 + 360.0) % 360.0; // 0..360
        return d > 180.0 ? d - 360.0 : d;
    }

    /// <summary>The top line's distance as BlueBird prints it: under 100 one decimal ("37.5", and 99.96 prints "100.0" as
    /// the screen's own rounding does), 100 or more the integer part, truncated ("137").</summary>
    public static string TopLineText(double nm)
    {
        if (nm < 100.0)
            return nm.ToString("F1", CultureInfo.InvariantCulture);
        return Math.Truncate(nm).ToString("F0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The top line's ETA text, or null when it draws dashes: the ground speed under <paramref name="minGroundSpeed"/>
    /// (the layout's 100 kt), or a distance or clock that is not a number. The clock is HHMM.mm (hours, minutes and
    /// hundredths of a minute); the time to go is distance / ground speed. The sum is rounded to the second (halves away
    /// from zero), wrapped at 24 h and printed as HHMM.t, minutes to one decimal, width 6 zero filled. BlueBird prints
    /// the minutes' fraction rounded, so 15:59:58 (1559.97) prints "1560.0": kept as drawn, never carried into the hour.
    /// </summary>
    public static string? Eta(double clock, double nm, double groundSpeed, double minGroundSpeed)
    {
        if (double.IsNaN(clock) || double.IsNaN(nm) || double.IsNaN(groundSpeed) || double.IsInfinity(clock)
            || double.IsInfinity(nm) || groundSpeed < minGroundSpeed || !(groundSpeed > 0))
            return null;

        double hours = Math.Floor(clock / 100.0);
        double minutes = clock - hours * 100.0;
        double seconds = hours * 3600.0 + minutes * 60.0 + nm / groundSpeed * 3600.0;
        long whole = (long)Math.Round(seconds, MidpointRounding.AwayFromZero);
        whole = ((whole % 86400) + 86400) % 86400;

        long h = whole / 3600, m = whole % 3600 / 60, s = whole % 60;
        double shown = h * 100.0 + m + s / 60.0;
        return shown.ToString("0000.0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A place on the map for the pilot: "37.5 NM, 87° right". The distance has one decimal; the angle is rounded to
    /// whole degrees, "left" for a negative <paramref name="offTop"/>. Within half a degree of straight ahead
    /// (inclusive) it says "ahead", within half a degree of straight behind (inclusive) "behind", so it never says
    /// "0°" or "180°".
    /// </summary>
    public static string Place(double nm, double offTop) =>
        $"{nm.ToString("F1", CultureInfo.InvariantCulture)} NM, {Angle(offTop)}";

    /// <summary>A place's angle words without its distance: "87° right", "ahead", "behind" (as <see cref="Place"/>), for
    /// what the screen draws as a direction only (a bearing pointer).</summary>
    public static string Angle(double offTop)
    {
        double abs = Math.Abs(offTop);
        if (abs <= AlignedDegrees) return "ahead";
        if (abs >= 180.0 - AlignedDegrees) return "behind";
        string degrees = Math.Round(abs, MidpointRounding.AwayFromZero).ToString("F0", CultureInfo.InvariantCulture);
        return $"{degrees}° {(offTop < 0 ? "left" : "right")}";
    }

    /// <summary>
    /// A place in PLAN, from the plan's center: "13.4 NM north-east of KOLM". Eight compass words, each a 45-degree sector
    /// centered on its point (north covers 337.5 up to 22.5, and a bearing on a boundary takes the clockwise word); under
    /// 0.05 NM it says "at KOLM".
    /// </summary>
    public static string PlanPlace(double nm, double bearingTrue, string center)
    {
        if (nm < AtDistanceNm) return $"at {center}";
        double b = ((bearingTrue % 360.0) + 360.0) % 360.0;
        int index = (int)Math.Floor((b + 22.5) / 45.0) % 8;
        return $"{nm.ToString("F1", CultureInfo.InvariantCulture)} NM {Compass[index]} of {center}";
    }
}

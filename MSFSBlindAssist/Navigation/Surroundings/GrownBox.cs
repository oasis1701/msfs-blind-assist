namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// An airport's navdata box grown by a margin in metres — the ONE conversion the three box tests
/// share: <c>AirportFacilities.ContainsPoint</c> (a building beside the pavement), the scenery
/// census's cell score, and <see cref="CurrentAirportResolver"/> (which airport the aircraft is at).
/// Each carried its own copy and they had drifted: the resolver converted the longitude margin at the
/// AIRCRAFT's latitude, the other two at the box's (review CL-4). The box's middle latitude is the one
/// used — it is the box being grown — with the cosine floored at 0.05 so a polar box gets a bounded
/// margin rather than an unbounded one. Assumes Left &lt;= Right, as the navdata box SQL always
/// gives; a box spanning ±180° is not handled, and never was.
/// </summary>
public readonly record struct GrownBox(double Top, double Bottom, double Left, double Right)
{
    /// <summary>Metres per degree of latitude, the figure every box test here has always used.</summary>
    public const double MetresPerDegreeLatitude = 111_320.0;

    public static GrownBox Of(double topLat, double bottomLat, double leftLon, double rightLon, double marginMetres)
    {
        double dLat = marginMetres / MetresPerDegreeLatitude;
        double dLon = marginMetres / (MetresPerDegreeLatitude * Math.Max(0.05, Math.Cos((topLat + bottomLat) / 2.0 * Math.PI / 180.0)));
        return new GrownBox(topLat + dLat, bottomLat - dLat, leftLon - dLon, rightLon + dLon);
    }

    /// <summary>Inside, or on the edge.</summary>
    public bool Contains(double lat, double lon) => lat <= Top && lat >= Bottom && lon >= Left && lon <= Right;

    /// <summary>Whether a rectangle overlaps this box on both axes; touching counts.</summary>
    public bool Reaches(double bottomLat, double topLat, double leftLon, double rightLon)
        => bottomLat <= Top && topLat >= Bottom && leftLon <= Right && rightLon >= Left;
}

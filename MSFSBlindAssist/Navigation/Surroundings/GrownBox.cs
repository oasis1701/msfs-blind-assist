namespace MSFSBlindAssist.Navigation.Surroundings;

/// <summary>
/// An airport's navdata box grown by a margin in metres — the one conversion AirportFacilities,
/// the scenery census and <see cref="CurrentAirportResolver"/> share. The longitude margin is taken
/// at the box's middle latitude, cosine floored at 0.05 for polar boxes. Assumes Left &lt;= Right.
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

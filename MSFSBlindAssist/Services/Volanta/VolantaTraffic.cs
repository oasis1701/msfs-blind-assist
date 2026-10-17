using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Services.Volanta;

/// <summary>One flight placed relative to the pilot. <see cref="Latitude"/>/<see cref="Longitude"/> are where it
/// is estimated to be NOW (<see cref="VolantaTrafficCalculator.Extrapolate(VolantaFlight, DateTimeOffset)"/>), the
/// distance and clock position are measured from there.</summary>
public sealed record VolantaTrafficEntry(
    VolantaFlight Flight,
    double Latitude,
    double Longitude,
    double DistanceNm,
    double BearingTrue,
    double RelativeBearing,
    int ClockPosition,
    double AltitudeDiffFt,
    double? PositionAgeSeconds)
{
    /// <summary>Where the aircraft is, as the location data names it ("Pike Lake, Wisconsin, United States",
    /// "over Lake Michigan"); null until looked up (<see cref="VolantaPlaceCache{TPlace}"/>) or with no location data.</summary>
    public string? Location { get; init; }
}

public enum VolantaPhaseFilter { All, Airborne, OnGround }

/// <summary>What the Traffic tab shows. Every criterion is optional; the defaults show everything within
/// 100 nm.</summary>
public sealed record VolantaTrafficFilter
{
    public static readonly IReadOnlySet<VolantaSource> AllSources = new HashSet<VolantaSource>(Enum.GetValues<VolantaSource>());

    /// <summary>Null means no limit. Not applied while <see cref="Search"/> has text.</summary>
    public double? RadiusNm { get; init; } = 100;
    public IReadOnlySet<VolantaSource> Sources { get; init; } = AllSources;
    public VolantaPhaseFilter Phase { get; init; } = VolantaPhaseFilter.All;
    /// <summary>Matched, case-insensitively, against callsign, flight number, user, aircraft type, registration
    /// and both airports, at any distance (the owner's choice, 2026-10-16): a pilot searching for a callsign wants
    /// that aircraft wherever it is. Sources and phase still apply.</summary>
    public string Search { get; init; } = "";

    public bool Matches(VolantaTrafficEntry e)
    {
        var f = e.Flight;
        string q = Search.Trim();
        if (q.Length == 0 && RadiusNm is double r && e.DistanceNm > r) return false;
        if (!Sources.Contains(f.Source)) return false;
        if (Phase == VolantaPhaseFilter.Airborne && f.OnGround) return false;
        if (Phase == VolantaPhaseFilter.OnGround && !f.OnGround) return false;
        if (q.Length > 0)
        {
            bool hit = Contains(f.Callsign, q) || Contains(f.FlightNumber, q) || Contains(f.UserName, q)
                || Contains(f.AircraftIcao, q) || Contains(f.Registration, q) || Contains(f.Origin, q)
                || Contains(f.Destination, q);
            if (!hit) return false;
        }
        return true;
    }

    public IReadOnlyList<VolantaTrafficEntry> Apply(IEnumerable<VolantaTrafficEntry> entries)
        => entries.Where(Matches).OrderBy(e => e.DistanceNm).ToList();

    private static bool Contains(string field, string q) => field.Contains(q, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The ATC tab's search, so the pilot can look up an airport's ATC wherever it is ("KATL", "EGLL"): the
/// window lists every station it matches at any distance, as the Traffic tab's search does.</summary>
public static class VolantaAtcSearch
{
    /// <summary>Whether a station matches <paramref name="search"/> (blank matches every one): case-insensitively
    /// in its callsign, airport, controller or frequency. A US airport's code also finds the stations VATSIM names
    /// without its K ("KATL" finds ATL_TWR and ATL_APP, as well as KATL_ATIS).</summary>
    public static bool Matches(VolantaController c, string search)
    {
        string q = search.Trim();
        if (q.Length == 0) return true;
        if (Contains(c.Callsign, q) || Contains(c.AirportIcao, q) || Contains(c.UserName, q) || Contains(c.Frequency, q))
            return true;
        return q.Length == 4 && (q[0] is 'K' or 'k') && q.All(char.IsAsciiLetterOrDigit)
            && c.Callsign.StartsWith(q[1..] + "_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(string field, string q) => field.Contains(q, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Places Volanta's flights around the pilot. Pure. Volanta's positions are old by the time they are read
/// (median 11 s, one in ten past 100 s, measured 2026-10-15), so an airborne flight is moved along its heading
/// at its ground speed to now, for at most <see cref="MaxExtrapolationSeconds"/>. The pilot's own flight is in
/// Volanta's file too, with an equally old position, so it would read as traffic behind the pilot: it is left
/// out by its flight id or by the logged-in Volanta user's id, or, before Volanta has named either, by matching
/// Volanta's own position report.
/// </summary>
public static class VolantaTrafficCalculator
{
    public const double MaxExtrapolationSeconds = 180;
    /// <summary>Slower than this nothing is moved: taxiing and parked aircraft stay where they reported.</summary>
    public const double MinExtrapolationSpeedKt = 40;
    private const double EarthRadiusNm = 3440.065;

    public static IReadOnlyList<VolantaTrafficEntry> Compute(
        IEnumerable<VolantaFlight> flights, VolantaOwnPosition own, DateTimeOffset now,
        string? ownFlightId = null, VolantaOwnPosition? ownVolantaReport = null, string? ownUserId = null,
        IReadOnlyCollection<string>? ownCallsigns = null)
    {
        var list = new List<VolantaTrafficEntry>();
        foreach (var f in flights)
        {
            if (IsOwnFlight(f, ownFlightId, ownVolantaReport, ownUserId, ownCallsigns)) continue;
            var (lat, lon) = Extrapolate(f, now);
            double dist = NavigationCalculator.CalculateDistance(own.Latitude, own.Longitude, lat, lon);
            double brg = NavigationCalculator.CalculateBearing(own.Latitude, own.Longitude, lat, lon);
            double rel = RelativeDirection.Normalize360(brg - own.HeadingTrue);
            double? age = f.PositionTime is DateTimeOffset t ? Math.Max(0, (now - t).TotalSeconds) : null;
            list.Add(new VolantaTrafficEntry(f, lat, lon, dist, brg, rel, Clock(rel), f.AltitudeFt - own.AltitudeFt, age));
        }
        return list;
    }

    /// <summary>The pilot's own entry: Volanta's flight id for it, or the logged-in Volanta user's id (a user has
    /// one live flight), else (neither known yet) the entry whose position is the one Volanta itself last
    /// reported for the pilot. A network pilot's entry is the pilot's own when it has one of
    /// <paramref name="ownCallsigns"/>: Volanta leaves the pilot out of its network file only while it tracks their
    /// flight, so without Volanta tracking, their own VATSIM or IVAO entry is there.</summary>
    public static bool IsOwnFlight(VolantaFlight f, string? ownFlightId, VolantaOwnPosition? ownVolantaReport,
        string? ownUserId = null, IReadOnlyCollection<string>? ownCallsigns = null)
    {
        if (f.Source != VolantaSource.Volanta)
            return ownCallsigns != null && f.Callsign.Length > 0
                && ownCallsigns.Any(c => string.Equals(c.Trim(), f.Callsign, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(ownUserId) && string.Equals(f.UserId, ownUserId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrEmpty(ownFlightId)) return string.Equals(f.Id, ownFlightId, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(ownUserId)) return false;
        if (ownVolantaReport is not VolantaOwnPosition r || f.PositionTime is not DateTimeOffset t) return false;
        return Math.Abs((t - r.Time).TotalSeconds) < 2
            && NavigationCalculator.CalculateDistance(f.Latitude, f.Longitude, r.Latitude, r.Longitude) < 1;
    }

    /// <summary>Where the flight is estimated to be at <paramref name="now"/>.</summary>
    public static (double Latitude, double Longitude) Extrapolate(VolantaFlight f, DateTimeOffset now)
    {
        if (f.OnGround || f.GroundSpeedKt < MinExtrapolationSpeedKt || f.PositionTime is not DateTimeOffset t)
            return (f.Latitude, f.Longitude);
        double seconds = Math.Clamp((now - t).TotalSeconds, 0, MaxExtrapolationSeconds);
        return Project(f.Latitude, f.Longitude, f.HeadingTrue, f.GroundSpeedKt * seconds / 3600.0);
    }

    /// <summary>The pilot's position from Volanta's own report, moved on to now the same way.</summary>
    public static VolantaOwnPosition Extrapolate(VolantaOwnPosition own, DateTimeOffset now)
    {
        if (own.OnGround || own.GroundSpeedKt < MinExtrapolationSpeedKt) return own;
        double seconds = Math.Clamp((now - own.Time).TotalSeconds, 0, MaxExtrapolationSeconds);
        var (lat, lon) = Project(own.Latitude, own.Longitude, own.HeadingTrue, own.GroundSpeedKt * seconds / 3600.0);
        return own with { Latitude = lat, Longitude = lon, Time = now };
    }

    /// <summary>The great-circle point <paramref name="distanceNm"/> from a start along a true bearing.</summary>
    public static (double Latitude, double Longitude) Project(double lat, double lon, double bearingTrue, double distanceNm)
    {
        if (distanceNm <= 0) return (lat, lon);
        double d = distanceNm / EarthRadiusNm;
        double phi1 = lat * Math.PI / 180, lambda1 = lon * Math.PI / 180, theta = bearingTrue * Math.PI / 180;
        double phi2 = Math.Asin(Math.Sin(phi1) * Math.Cos(d) + Math.Cos(phi1) * Math.Sin(d) * Math.Cos(theta));
        double lambda2 = lambda1 + Math.Atan2(Math.Sin(theta) * Math.Sin(d) * Math.Cos(phi1),
            Math.Cos(d) - Math.Sin(phi1) * Math.Sin(phi2));
        double lon2 = (lambda2 * 180 / Math.PI + 540) % 360 - 180;
        return (phi2 * 180 / Math.PI, lon2);
    }

    /// <summary>12 = ahead, 3 = right, 6 = behind, 9 = left: the TCAS window's own clock
    /// (<see cref="RelativeDirection.ClockPosition"/>), so the two traffic windows agree.</summary>
    public static int Clock(double relativeBearing) => RelativeDirection.ClockPosition(relativeBearing);
}

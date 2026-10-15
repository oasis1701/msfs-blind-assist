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
    double? PositionAgeSeconds);

public enum VolantaPhaseFilter { All, Airborne, OnGround }

/// <summary>What the Traffic tab shows. Every criterion is optional; the defaults show everything within
/// 100 nm.</summary>
public sealed record VolantaTrafficFilter
{
    public static readonly IReadOnlySet<VolantaSource> AllSources = new HashSet<VolantaSource>(Enum.GetValues<VolantaSource>());

    /// <summary>Null means no limit.</summary>
    public double? RadiusNm { get; init; } = 100;
    public IReadOnlySet<VolantaSource> Sources { get; init; } = AllSources;
    public VolantaPhaseFilter Phase { get; init; } = VolantaPhaseFilter.All;
    /// <summary>Only aircraft within this many feet of the pilot's altitude; null means any altitude. Aircraft on
    /// the ground pass it only when the pilot is also within it of their altitude.</summary>
    public double? AltitudeWithinFt { get; init; }
    /// <summary>Matched, case-insensitively, against callsign, flight number, user, aircraft type, registration
    /// and both airports.</summary>
    public string Search { get; init; } = "";

    public bool Matches(VolantaTrafficEntry e)
    {
        var f = e.Flight;
        if (RadiusNm is double r && e.DistanceNm > r) return false;
        if (!Sources.Contains(f.Source)) return false;
        if (Phase == VolantaPhaseFilter.Airborne && f.OnGround) return false;
        if (Phase == VolantaPhaseFilter.OnGround && !f.OnGround) return false;
        if (AltitudeWithinFt is double band && Math.Abs(e.AltitudeDiffFt) > band) return false;
        string q = Search.Trim();
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
        string? ownFlightId = null, VolantaOwnPosition? ownVolantaReport = null, string? ownUserId = null)
    {
        var list = new List<VolantaTrafficEntry>();
        foreach (var f in flights)
        {
            if (IsOwnFlight(f, ownFlightId, ownVolantaReport, ownUserId)) continue;
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
    /// reported for the pilot.</summary>
    public static bool IsOwnFlight(VolantaFlight f, string? ownFlightId, VolantaOwnPosition? ownVolantaReport,
        string? ownUserId = null)
    {
        if (f.Source != VolantaSource.Volanta) return false;
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

    /// <summary>12 = ahead, 3 = right, 6 = behind, 9 = left, as the TCAS window says it.</summary>
    public static int Clock(double relativeBearing)
    {
        int c = (int)Math.Round(RelativeDirection.Normalize360(relativeBearing) / 30.0) % 12;
        return c == 0 ? 12 : c;
    }
}

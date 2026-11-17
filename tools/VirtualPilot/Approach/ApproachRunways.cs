using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Volanta;

namespace VirtualPilot.Approach;

/// <summary>
/// A synthetic runway the approach mode flies to; no navdata needed. <paramref name="ThresholdLat"/> and
/// <paramref name="ThresholdLon"/> are its painted landing threshold, where the harness's frame starts:
/// the runway end (<see cref="Runway.StartLat"/>) lies <see cref="Runway.ThresholdOffset"/> before it.
/// <paramref name="PathDeg"/> is the glide path the harness measures against, worked out here rather
/// than by the app, so a wrong datum in visual guidance shows as a path error.
/// </summary>
sealed record ApproachRunway(string Id, Runway Runway, Airport Airport, double ThresholdLat, double ThresholdLon, double PathDeg);

/// <summary>
/// The synthetic runways and the approach-frame geometry the harness measures in. "Along" is nm back
/// from the landing threshold on the approach side (negative = past it); "cross" is nm to the side,
/// positive = right of the extended centreline facing the runway. Each runway has 2 nm of runway past
/// its landing threshold.
/// </summary>
static class ApproachRunways
{
    private const double EarthRadiusNm = 3440.065;   // VolantaTrafficCalculator.Project's radius
    private const double FtPerNm = 6076.12;
    private const double GlidePathDeg = 3.0;
    private const double LandingLengthNm = 2.0;

    /// <summary>True course 004 with 9° E variation, so magnetic 355: the course wraps through north.</summary>
    public static readonly ApproachRunway A = Make("A", 47.0, 8.0, 4.0, 9.0, 1400.0);

    /// <summary>True course 268 with 13° W variation (magnetic 281), near sea level.</summary>
    public static readonly ApproachRunway B = Make("B", 40.0, -74.0, 268.0, -13.0, 20.0);

    /// <summary>
    /// A displaced threshold of 3,471 ft, KJFK 22R's: the runway end lies that far before the landing
    /// threshold. True course 270 with 5° W variation, 1,000 ft. An ILS at 3°.
    /// </summary>
    public static readonly ApproachRunway C = Make("C", 33.6, -84.4, 270.0, -5.0, 1000.0, thresholdOffsetFt: 3471.0);

    /// <summary>No ILS, a PAPI at 4°: visual guidance flies the PAPI's angle. True course 093, no variation, 300 ft.</summary>
    public static readonly ApproachRunway D = Make("D", 51.5, 0.5, 93.0, 0.0, 300.0, ilsDeg: 0.0, papiDeg: 4.0, pathDeg: 4.0);

    /// <summary>The runways the full matrix flies.</summary>
    public static IReadOnlyList<ApproachRunway> All { get; } = new[] { A, B };

    /// <summary>The runways the DATUM family flies: where the path starts and how steep it is.</summary>
    public static IReadOnlyList<ApproachRunway> Datum { get; } = new[] { C, D };

    public static double Norm(double d) => (d % 360 + 360) % 360;

    private static ApproachRunway Make(string id, double lat, double lon, double trueCourse, double magVar, double elevFt,
        double thresholdOffsetFt = 0.0, double ilsDeg = GlidePathDeg, double papiDeg = 0.0, double pathDeg = GlidePathDeg)
    {
        var (endLat, endLon) = VolantaTrafficCalculator.Project(lat, lon, trueCourse, LandingLengthNm);
        double startLat = lat, startLon = lon;
        if (thresholdOffsetFt > 0)
            (startLat, startLon) = VolantaTrafficCalculator.Project(lat, lon, Norm(trueCourse + 180), thresholdOffsetFt / FtPerNm);
        var runway = new Runway
        {
            AirportICAO = "ZZV" + id, RunwayID = id, Heading = trueCourse, HeadingMag = Norm(trueCourse - magVar),
            StartLat = startLat, StartLon = startLon, EndLat = endLat, EndLon = endLon,
            Length = LandingLengthNm * FtPerNm + thresholdOffsetFt, ThresholdOffset = thresholdOffsetFt, Width = 150,
            ThresholdElevation = elevFt, GlideslopeAngleDeg = ilsDeg, PapiAngleDeg = papiDeg,
        };
        var airport = new Airport
        {
            ICAO = "ZZV" + id, Name = "Approach harness " + id, City = "", Country = "",
            Latitude = lat, Longitude = lon, Altitude = elevFt, MagVar = magVar,
        };
        return new ApproachRunway(id, runway, airport, lat, lon, pathDeg);
    }

    /// <summary>The runway's far end, nm along (negative: past the landing threshold).</summary>
    public static double FarEndNm(ApproachRunway r) => -(r.Runway.Length - r.Runway.ThresholdOffset) / FtPerNm;

    /// <summary>The point <paramref name="alongNm"/> back from the threshold and <paramref name="crossNm"/> to the side (+ = right facing the runway).</summary>
    public static (double Lat, double Lon) PointOnApproach(ApproachRunway r, double alongNm, double crossNm)
    {
        double h = r.Runway.Heading;
        var (lat, lon) = alongNm >= 0
            ? VolantaTrafficCalculator.Project(r.ThresholdLat, r.ThresholdLon, Norm(h + 180), alongNm)
            : VolantaTrafficCalculator.Project(r.ThresholdLat, r.ThresholdLon, h, -alongNm);
        return crossNm >= 0
            ? VolantaTrafficCalculator.Project(lat, lon, Norm(h + 90), crossNm)
            : VolantaTrafficCalculator.Project(lat, lon, Norm(h - 90), -crossNm);
    }

    /// <summary>
    /// The extended centreline's true direction abeam a position: the threshold's course turned by
    /// the meridians' convergence between them (on runway B at 8 nm, 268.11° against 268.00°). A
    /// start "on the course" heads this way, so it flies parallel to the line where it is.
    /// </summary>
    public static double LocalCourse(ApproachRunway r, double lat, double lon)
    {
        double dLon = ((lon - r.ThresholdLon) % 360 + 540) % 360 - 180;
        return Norm(r.Runway.Heading + dLon * Math.Sin((lat + r.ThresholdLat) / 2 * Math.PI / 180));
    }

    /// <summary>The height of the runway's glide path (<see cref="ApproachRunway.PathDeg"/>) at <paramref name="alongNm"/> out, with the profile's glidepath bias, as visual guidance defines it.</summary>
    public static double PathMslFt(ApproachRunway r, double alongNm, VisualGuidanceProfile p)
        => r.Runway.ThresholdElevation + alongNm * FtPerNm * Math.Tan(r.PathDeg * Math.PI / 180) + p.GlideslopeAltitudeBiasFt;

    /// <summary>
    /// Along and cross of a position, as great-circle along-track and cross-track distances from
    /// the threshold on the runway's true course: the same reference visual guidance steers to
    /// (NavigationCalculator's perpendicular-intercept formulas), so a metric never reads a
    /// projection error as a tracking error.
    /// </summary>
    public static (double AlongNm, double CrossNm) Locate(ApproachRunway r, double lat, double lon)
    {
        const double D2R = Math.PI / 180;
        double phi1 = r.ThresholdLat * D2R, phi2 = lat * D2R, dLambda = (lon - r.ThresholdLon) * D2R;
        double a = Math.Sin((phi2 - phi1) / 2) * Math.Sin((phi2 - phi1) / 2)
                 + Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(dLambda / 2) * Math.Sin(dLambda / 2);
        double delta = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        double theta = Math.Atan2(Math.Sin(dLambda) * Math.Cos(phi2),
            Math.Cos(phi1) * Math.Sin(phi2) - Math.Sin(phi1) * Math.Cos(phi2) * Math.Cos(dLambda));
        double rel = theta - r.Runway.Heading * D2R;
        double cross = Math.Asin(Math.Sin(delta) * Math.Sin(rel));
        double alongPastThreshold = Math.Atan2(Math.Sin(delta) * Math.Cos(rel), Math.Cos(delta));
        return (-alongPastThreshold * EarthRadiusNm, cross * EarthRadiusNm);
    }
}

using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// A land-and-hold-short (LAHSO) constraint for a landing rollout: the position of
/// the estimated hold point on the landing runway, which crossing runway it guards,
/// and how far down the landing run it sits. Built by <see cref="Compute"/> from the
/// two runways' geometry (VATSIM gap analysis 2026-08-31, P5 — YMML's signature
/// procedure: "hold short runway 27, runway 34 cleared to land").
/// </summary>
public sealed class LahsoHold
{
    public double Latitude { get; }
    public double Longitude { get; }
    /// <summary>Designator of the crossing runway the hold protects (as the pilot picked it).</summary>
    public string CrossingRunwayId { get; }
    /// <summary>Hold point distance from the PAINTED landing threshold, feet — the
    /// same anchor <c>TaxiGraph.GetLandingExits</c> measures exits from, so the two
    /// are directly comparable.</summary>
    public double StopFromThresholdFeet { get; }

    public LahsoHold(double lat, double lon, string crossingRunwayId, double stopFromThresholdFeet)
    {
        Latitude = lat;
        Longitude = lon;
        CrossingRunwayId = crossingRunwayId;
        StopFromThresholdFeet = stopFromThresholdFeet;
    }

    private const double M_PER_DEG_LAT = 111132.0;
    private const double FT_TO_M = 0.3048;
    private const double M_TO_FT = 3.28084;

    /// <summary>
    /// Estimated LAHSO hold point setback (feet) short of the crossing runway's
    /// centerline. Navdata carries no LAHSO hold-line positions (they exist only on
    /// charts), so the hold is placed a conservative distance before the
    /// intersection; the announcement calls it "estimated".
    /// </summary>
    public const double DEFAULT_SETBACK_FT = 250.0;

    /// <summary>
    /// Intersects the landing runway's centerline with the crossing runway's and
    /// returns the hold point <see cref="DEFAULT_SETBACK_FT"/> short of it, or null
    /// when the two centerlines do not actually cross within both pavements (a
    /// parallel runway, or a designator typo). Pure geometry — flat-earth locally,
    /// which is exact enough at airport scale.
    /// </summary>
    public static LahsoHold? Compute(Runway landing, Runway crossing, double setbackFt = DEFAULT_SETBACK_FT)
    {
        if (landing == null || crossing == null) return null;
        if (string.Equals(landing.RunwayID, crossing.RunwayID, StringComparison.OrdinalIgnoreCase))
            return null;

        double lat0 = landing.StartLat, lon0 = landing.StartLon;
        double cosLat = Math.Cos(lat0 * Math.PI / 180.0);
        (double x, double y) P(double lat, double lon) =>
            ((lon - lon0) * M_PER_DEG_LAT * cosLat, (lat - lat0) * M_PER_DEG_LAT);

        var a1 = P(landing.StartLat, landing.StartLon);
        var a2 = P(landing.EndLat, landing.EndLon);
        var b1 = P(crossing.StartLat, crossing.StartLon);
        var b2 = P(crossing.EndLat, crossing.EndLon);

        double dax = a2.x - a1.x, day = a2.y - a1.y;
        double dbx = b2.x - b1.x, dby = b2.y - b1.y;
        double denom = dax * dby - day * dbx;
        if (Math.Abs(denom) < 1e-9) return null; // parallel / collinear

        double t = ((b1.x - a1.x) * dby - (b1.y - a1.y) * dbx) / denom;
        double u = ((b1.x - a1.x) * day - (b1.y - a1.y) * dax) / denom;
        if (t < 0.0 || t > 1.0 || u < 0.0 || u > 1.0) return null; // no crossing within both pavements

        double landingLenM = Math.Sqrt(dax * dax + day * day);
        if (landingLenM < 1.0) return null;
        double alongM = t * landingLenM;

        // Distance from the PAINTED threshold (physical end + displaced-threshold
        // offset), matching GetLandingExits' anchor.
        double thresholdOffsetM = landing.ThresholdOffset * FT_TO_M;
        double holdAlongM = alongM - setbackFt * FT_TO_M;
        double stopFromThresholdFt = (holdAlongM - thresholdOffsetM) * M_TO_FT;
        if (stopFromThresholdFt <= 0) return null; // hold at/behind the threshold — unusable

        // Hold point coordinates: setback applied along the landing centerline
        // back toward the threshold.
        double tHold = holdAlongM / landingLenM;
        double hx = a1.x + dax * tHold, hy = a1.y + day * tHold;
        double holdLat = lat0 + hy / M_PER_DEG_LAT;
        double holdLon = lon0 + hx / (M_PER_DEG_LAT * cosLat);

        return new LahsoHold(holdLat, holdLon, crossing.RunwayID, stopFromThresholdFt);
    }
}

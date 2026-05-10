using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// A taxiway exit from a landing runway. Computed from the taxi graph by projecting
/// hold-short / runway-intersection nodes onto the runway centerline.
///
/// "Exit" here means a place where the aircraft can leave the runway after landing.
/// Distances are measured FROM THE LANDING THRESHOLD along the runway centerline.
/// </summary>
public class LandingExit
{
    /// <summary>Node id of the exit point in the taxi graph.</summary>
    public int NodeId { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>Along-runway distance from the landing threshold to this exit, in feet.</summary>
    public double DistanceFromThresholdFeet { get; set; }

    /// <summary>
    /// Estimated distance from a typical jet touchdown point (threshold + 1000 ft aim point)
    /// to this exit, in feet. Can be negative for very early exits — those are filtered out.
    /// </summary>
    public double DistanceFromTouchdownFeet { get; set; }

    /// <summary>
    /// Taxiway name this exit leads to (e.g. "S5", "E3", "A"). Derived from the first
    /// edge leading off the runway toward a non-runway taxi_path.
    /// </summary>
    public string TaxiwayName { get; set; } = "";

    /// <summary>
    /// Angle of the exit taxiway relative to the runway, in degrees (0-180).
    /// 0° = aligned with runway direction (highly improbable),
    /// 30-45° = high-speed rapid exit taxiway (ICAO Annex 14 preferred ≤30°),
    /// 90° = perpendicular (normal taxiway exit),
    /// 180° = aligned opposite direction (runway end).
    /// </summary>
    public double ExitAngleDegrees { get; set; }

    /// <summary>
    /// Category derived from ExitAngleDegrees:
    ///   "High-speed" — ≤ 50° (RET / rapid exit)
    ///   "Normal"     — 50-110° (standard 90° exit)
    ///   "End"        — > 110° (at or near runway end, requires backtrack)
    /// </summary>
    public string ExitType { get; set; } = "";

    public override string ToString()
    {
        int distFt = (int)Math.Round(DistanceFromThresholdFeet);
        int angle = (int)Math.Round(ExitAngleDegrees);
        string nameLabel = string.IsNullOrEmpty(TaxiwayName) ? "(unnamed)" : TaxiwayName;
        return $"{nameLabel} — {distFt} ft from threshold ({ExitType}, {angle}°)";
    }
}

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
    /// <summary>Node id of the exit point in the taxi graph (on-runway junction).</summary>
    public int NodeId { get; set; }

    /// <summary>
    /// First graph node outside the runway lateral corridor on this exit path.
    /// For HS/IHS exits equals NodeId (the hold-short bar is already at the junction).
    /// For fallback implicit exits this is the node where the curved path first clears
    /// the runway strip — used to re-route at the LandingRollout → Taxiing handoff so
    /// the steering tone guides the pilot through the actual curve, not a wrong apron path.
    /// -1 when unknown (off-axis exits where BFS was not run).
    /// </summary>
    public int ApronNodeId { get; set; } = -1;

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
    /// True bearing (0–360°) of the best taxiway edge leading away from the
    /// runway at this exit node. Used during landing rollout to blend the
    /// steering tone toward the actual exit direction as the aircraft closes
    /// in — gives a clear pan cue even for exits whose node sits very close
    /// to the runway centreline (where bearing-to-node alone gives near-zero
    /// error). Set from the same "best edge" used to compute ExitAngleDegrees.
    /// 0 when no valid edge was found (tone falls back to bearing-to-node only).
    /// Due-north edges (raw bearing 0°) are stored as 360° to keep 0 unambiguous.
    /// </summary>
    public double ExitBearingTrue { get; set; }

    /// <summary>
    /// Category derived from ExitAngleDegrees:
    ///   "High-speed" — ≤ 50° (RET / rapid exit)
    ///   "Normal"     — 50-110° (standard 90° exit)
    ///   "End"        — > 110° (at or near runway end, requires backtrack)
    /// </summary>
    public string ExitType { get; set; } = "";

    /// <summary>
    /// Which side of the runway this exit is on — "Left" or "Right" relative to the
    /// landing direction. Empty string when the bearing is unknown (ExitBearingTrue == 0).
    /// </summary>
    public string ExitSide { get; set; } = "";

    public override string ToString()
    {
        string dist = MSFSBlindAssist.Services.DistanceFormatter.FromFeet(DistanceFromThresholdFeet, shortForm: true);
        int angle = (int)Math.Round(ExitAngleDegrees);
        string nameLabel = string.IsNullOrEmpty(TaxiwayName) ? "(unnamed)" : TaxiwayName;
        string sideLabel = string.IsNullOrEmpty(ExitSide) ? "" : $", {ExitSide.ToLower()}";
        return $"{nameLabel} — {dist} from threshold ({ExitType}{sideLabel}, {angle}°)";
    }
}

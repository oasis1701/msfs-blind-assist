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
    /// How steeply the exit's path leaves its own node, in degrees off the runway heading: its first
    /// stretch of at least <see cref="ExitBranch.MinStrokeMetres"/> from where the exit stands, never more
    /// than <see cref="ExitAngleDegrees"/>. The overshoot margin and the alignment handoff read this: a
    /// pilot correctly following a curved rapid exit is only as far off the runway as its first stretch
    /// takes them - EDDB 24L M3 leaves at 6.9° where its branch's sharpest turn is 24.3°, and read at
    /// 24.3° a correct turn was called missed 100 ft past the node. <see cref="ExitAngleDegrees"/> (type,
    /// too-fast line) stays the sharpest turn. Unset, it is <see cref="ExitAngleDegrees"/>.
    /// </summary>
    public double DivergenceAngleDegrees
    {
        get => double.IsNaN(_divergenceAngleDegrees) ? ExitAngleDegrees : _divergenceAngleDegrees;
        set => _divergenceAngleDegrees = value;
    }
    private double _divergenceAngleDegrees = double.NaN;

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

    /// <summary>
    /// False when the taxi graph offers NO route from this exit that gets the aircraft
    /// clear of the runway pavement — the navdata maps nothing past the junction (one
    /// graph edge, pointing back at the runway). Set by the Landing Exit form using the
    /// same <see cref="LandingExitDestination"/> resolution the rollout handoff runs, so
    /// the warning shown before the flight matches what actually happens on rollout.
    /// <para>Defaults TRUE — an exit is presumed usable unless the check has been run
    /// and failed, so nothing is ever labelled as bad merely because it wasn't tested.</para>
    /// </summary>
    public bool VacatesRunway { get; set; } = true;

    /// <summary>
    /// True when the exit's branch is a turnaround as read from its junction and forward only as met at the
    /// exit's own node (<see cref="ExitBranch.FromExitNode"/>): the junction the inward walk reached lies past
    /// the lead-in's own start (SBGL 15 F). Such an exit only fills a gap in the planner list - its per-name
    /// dedup takes it for its name only when no exit read forward from its junction holds the name, and it is
    /// never coverage for one. Taking the name's place instead, KLIT 22R's D crossing near the threshold
    /// displaced the D rapid exit 4,600 ft on, which came back 700 ft late on its own arc (whole-database
    /// sweep, 2026-09-26).
    /// </summary>
    public bool ForwardOnlyFromItsNode { get; set; }

    public override string ToString()
    {
        string dist = MSFSBlindAssist.Services.DistanceFormatter.FromFeet(DistanceFromThresholdFeet, shortForm: true, round: false);
        int angle = (int)Math.Round(ExitAngleDegrees);
        string nameLabel = string.IsNullOrEmpty(TaxiwayName) ? "(unnamed)" : TaxiwayName;
        string sideLabel = string.IsNullOrEmpty(ExitSide) ? "" : $", {ExitSide.ToLower()}";
        // The warning goes at the END so the screen reader speaks the identity of the
        // exit first — a blind pilot arrowing through the list needs the name and
        // distance immediately, not a caution prefix repeated on every bad entry.
        string warn = VacatesRunway ? "" : " — WARNING: no taxiway mapped clear of the runway";
        return $"{nameLabel} — {dist} from threshold ({ExitType}{sideLabel}, {angle}°){warn}";
    }
}

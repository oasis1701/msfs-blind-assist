namespace MSFSBlindAssist.Navigation;

/// <summary>What the post-landing backtrack does about its taxiway connection node on this frame.</summary>
public enum BacktrackHandoffAction
{
    /// <summary>Keep backtracking: the node is still ahead and the aircraft is still on the runway.</summary>
    Continue,
    /// <summary>The aircraft is clear of every runway corridor: say "Runway vacated" and hand to Taxiing.</summary>
    Vacated,
    /// <summary>At the node but still on runway pavement: hand into the runway-clearing phase.</summary>
    ClearingAhead,
}

/// <summary>
/// The connection-node leg of the post-landing backtrack (<c>UpdateBacktracking</c>): what is said
/// when the taxiway comes into range, where the tone steers from then on, and when the backtrack
/// hands off. Pure, pinned by <c>BacktrackConnectionHandoffTests</c>.
///
/// <para>Measured live before this existed (CYYZ 05, 2026-10-03, taxi-landing-port review finding 1):
/// the connection node is filtered to be CLEAR of every runway corridor, so at CYYZ it sat on taxiway
/// H about 70-88 m off the 05/23 centreline. The tone kept steering the centreline, "Taxiway ahead.
/// Vacate runway." named no side, and the hand-off needed the aircraft within <see cref="HandoffMetres"/>
/// of the node. A pilot following the tone passed the node abeam at 69 m and the distance climbed for
/// good: 197, 174, ... 69, 73, 84, 99, 117, 128, then stopped on the runway with nothing more said.
/// Main's unfiltered node sat on the centreline, so it handed off but claimed "vacated" on the pavement;
/// the filter fixed the false claim and removed the only way to reach the node.</para>
///
/// <para>The rule now: inside <see cref="AnnounceMetres"/>, once the aircraft has come round onto the
/// backtrack heading, the approach sentence names the taxiway and the side ("Taxiway H ahead on the
/// left. Vacate runway."), the tone swings onto the bearing to the node (the Normal-exit tone after
/// "turn now" does the same), and the backtrack hands off when the aircraft is laterally clear of every
/// runway corridor OR within <see cref="HandoffMetres"/> of the node, whichever comes first. Before the
/// approach has been announced nothing changes: a pilot backtracking on the grass beside the pavement
/// (LGZA 16, 2026-08-31) is clear of every corridor from the start, and must still be steered back to
/// the runway rather than told it was vacated.</para>
/// </summary>
public static class BacktrackConnectionHandoff
{
    /// <summary>Distance to the connection node at which the taxiway is announced and the tone turns
    /// toward it. Was <c>BACKTRACK_TAXI_ANNOUNCE_M</c>.</summary>
    public const double AnnounceMetres = 200.0;

    /// <summary>Distance to the connection node that hands off on its own, clear of the runway or not.
    /// Was <c>BACKTRACK_HANDOFF_M</c>.</summary>
    public const double HandoffMetres = 25.0;

    /// <summary>Below this angle between the backtrack heading and the bearing to the node no side is
    /// spoken, the same floor the exit turn direction uses (<c>EXIT_TURN_DIRECTION_MIN_DEG</c>): a
    /// confident wrong side is worse for a blind pilot than no side.</summary>
    public const double SideMinDeg = 10.0;

    /// <summary>True once the node is close enough for the approach sentence and the node-bearing tone.
    /// A negative distance means there is no node.</summary>
    public static bool InApproachWindow(double distToNodeMetres)
        => distToNodeMetres >= 0.0 && distToNodeMetres <= AnnounceMetres;

    /// <summary>
    /// "left" or "right" for the connection node as seen from an aircraft on the backtrack heading,
    /// or null when the node is within <see cref="SideMinDeg"/> of straight ahead. Judged from the
    /// aircraft's own bearing to the node, as the targeted backtrack and the exit turn direction are.
    /// </summary>
    public static string? SideWord(double bearingToNodeTrue, double backtrackHeadingTrue)
    {
        double delta = NormalizeAngle(bearingToNodeTrue - backtrackHeadingTrue);
        if (Math.Abs(delta) < SideMinDeg) return null;
        return delta < 0 ? "left" : "right";
    }

    /// <summary>
    /// The approach sentence: "Taxiway H ahead on the left. Vacate runway." A node whose edges carry no
    /// name says "Taxiway ahead"; a node near enough to straight ahead names no side.
    /// </summary>
    public static string ComposeApproach(string? taxiwayName, string? side)
    {
        string name = string.IsNullOrWhiteSpace(taxiwayName) ? "Taxiway" : $"Taxiway {taxiwayName.Trim()}";
        string sideClause = side == null ? "" : $" on the {side}";
        return $"{name} ahead{sideClause}. Vacate runway.";
    }

    /// <summary>
    /// The tone's heading error on this frame: toward the node once the approach has been announced,
    /// otherwise the centreline law the caller already computed. Positive means turn right.
    /// </summary>
    public static double ToneHeadingError(
        bool approachAnnounced, double bearingToNodeTrue, double headingTrue, double centerlineHeadingError)
        => approachAnnounced
            ? NormalizeAngle(bearingToNodeTrue - headingTrue)
            : centerlineHeadingError;

    /// <summary>
    /// The hand-off decision. Clear of every runway corridor after the approach was announced hands off
    /// as vacated wherever the aircraft is; within <see cref="HandoffMetres"/> of the node hands off
    /// regardless, as vacated when clear and into the clearing phase when still on pavement.
    /// </summary>
    public static BacktrackHandoffAction Decide(
        double distToNodeMetres, bool approachAnnounced, bool clearOfAllRunwayCorridors)
    {
        if (approachAnnounced && clearOfAllRunwayCorridors) return BacktrackHandoffAction.Vacated;
        if (distToNodeMetres >= 0.0 && distToNodeMetres <= HandoffMetres)
            return clearOfAllRunwayCorridors ? BacktrackHandoffAction.Vacated : BacktrackHandoffAction.ClearingAhead;
        return BacktrackHandoffAction.Continue;
    }

    private static double NormalizeAngle(double deg)
    {
        deg %= 360.0;
        if (deg > 180.0) deg -= 360.0;
        else if (deg <= -180.0) deg += 360.0;
        return deg;
    }
}

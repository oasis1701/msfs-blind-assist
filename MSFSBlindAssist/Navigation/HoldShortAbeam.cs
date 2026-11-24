namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Whether an aircraft has reached a hold-short it is passing BESIDE rather than over.
///
/// <para>A hold-short is latched when the aircraft comes within 25 m of the hold node (40 m when a
/// later segment already reads nearer). Both are circles round the node, while an aircraft counts
/// as on its route up to 25 to 61 m either side of the line (<see cref="PavementTolerance"/>), and a
/// route the pilot chose to keep is steered along from further out still. So a pass beside the node
/// latched nothing: no "Stop. Hold short", no hold state, the route index left on the hold segment
/// for good, and the tone steering at the route beyond the line. Measured live at OMDB, K4 to
/// runway 12R, 2026-11-24: passed 35 m to the side, the aircraft was taken from the hold (123 m from
/// the runway centreline) to 48 m from it.</para>
///
/// <para>The hold is a line across the taxiway, so the question is asked along the hold segment's
/// own direction: level with the node or past it, close enough beside it, and travelling the way
/// the segment runs.</para>
/// </summary>
public static class HoldShortAbeam
{
    /// <summary>
    /// How far beside the hold node the aircraft may be. Wider than any on-route tolerance (61 m at
    /// the widest taxiway), and for a runway hold a neighbouring connector inside it is level with
    /// the same runway's holding position, where stopping is right as well.
    /// </summary>
    public const double MaxCrossMetres = 75.0;

    /// <summary>
    /// How far past the node still counts as at the hold. The same reach as the skipped-hold
    /// announcement (<c>HOLD_SHORT_ANNOUNCE_MAX_DIST_M</c>): "Stop. Hold short" is said only where
    /// the aircraft is at the line, never well beyond it.
    /// </summary>
    public const double MaxPastMetres = 40.0;

    /// <summary>
    /// How far the heading may be off the hold segment's bearing. An aircraft still coming along
    /// the taxiway BEFORE a turn onto a short hold segment can already be level with that segment's
    /// end; it has not reached the hold until it has turned toward it.
    /// </summary>
    public const double MaxHeadingOffDegrees = 60.0;

    /// <param name="alongRemainingMetres">Along the hold segment to its end node: positive while the
    /// node is ahead, zero level with it, negative past it.</param>
    /// <param name="crossMetres">Perpendicular offset from the hold segment's line, either sign.</param>
    /// <param name="headingOffDegrees">Aircraft heading minus the hold segment's bearing, normalised
    /// to -180..180.</param>
    public static bool IsReached(double alongRemainingMetres, double crossMetres, double headingOffDegrees)
        => alongRemainingMetres <= 0.0
           && alongRemainingMetres >= -MaxPastMetres
           && Math.Abs(crossMetres) <= MaxCrossMetres
           && Math.Abs(headingOffDegrees) <= MaxHeadingOffDegrees;
}

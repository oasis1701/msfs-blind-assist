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

    /// <summary>
    /// The shortest segment that still has a direction to measure along. A sub-metre segment is a
    /// point (<c>GuidanceGeometry</c> skips them the same way): projected onto, it reads "level with
    /// the node, on the line" from anywhere on the airport.
    /// </summary>
    public const double MinAxisMetres = 1.0;

    /// <summary>
    /// How many segments back from the hold segment a direction is looked for when the hold segment
    /// itself is a sub-metre point: itself and the two before it.
    /// </summary>
    public const int AxisLookBackSegments = 3;

    /// <summary>
    /// Where the aircraft is relative to the hold node, along and across the axis
    /// <paramref name="axisFromLat"/>/<paramref name="axisFromLon"/> to
    /// <paramref name="axisToLat"/>/<paramref name="axisToLon"/>: the hold segment itself, or the
    /// segment before it when the hold segment is too short to have a direction. False, with nothing
    /// measured, when the axis is shorter than <see cref="MinAxisMetres"/>.
    /// </summary>
    /// <param name="alongRemainingMetres">As <see cref="IsReached"/> takes it: positive while the node
    /// is ahead along the axis, zero level with it, negative past it.</param>
    /// <param name="crossMetres">Perpendicular offset from the line through the node, either sign.</param>
    /// <param name="axisBearingDegrees">The axis's true bearing, 0 to 360.</param>
    public static bool TryMeasure(
        double lat, double lon,
        double axisFromLat, double axisFromLon, double axisToLat, double axisToLon,
        double nodeLat, double nodeLon,
        out double alongRemainingMetres, out double crossMetres, out double axisBearingDegrees)
    {
        const double MetresPerDegree = 111132.0;
        alongRemainingMetres = 0.0;
        crossMetres = 0.0;
        axisBearingDegrees = 0.0;

        double metresPerDegreeLon =
            MetresPerDegree * Math.Cos((axisFromLat + axisToLat) * 0.5 * Math.PI / 180.0);
        double dx = (axisToLon - axisFromLon) * metresPerDegreeLon;
        double dy = (axisToLat - axisFromLat) * MetresPerDegree;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < MinAxisMetres) return false;

        double ux = dx / length, uy = dy / length;
        double ex = (nodeLon - lon) * metresPerDegreeLon;   // aircraft to hold node
        double ey = (nodeLat - lat) * MetresPerDegree;
        alongRemainingMetres = ex * ux + ey * uy;
        crossMetres = ey * ux - ex * uy;
        axisBearingDegrees = (Math.Atan2(dx, dy) * 180.0 / Math.PI + 360.0) % 360.0;
        return true;
    }

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

    /// <summary>
    /// How much farther from its runway than the hold node an aircraft may be and still be taken
    /// as at the line whatever it is steering at. The intended passes with the tone at full pan
    /// were all nearer the runway than the node (KCLE, 45 m nearer); the missed turns were 26 m or
    /// more behind it.
    /// </summary>
    public const double MaxBehindLineMetres = 10.0;

    /// <summary>
    /// The steering error at which the taxi tone is at full pan
    /// (<c>TaxiSteeringTone.MAX_PAN_THRESHOLD_DEG</c>): from there on the aircraft is not steering
    /// where the tone leads.
    /// </summary>
    public const double FullPanSteeringErrorDegrees = 30.0;

    /// <summary>
    /// How much farther from the runway's centreline than the hold node the aircraft is, on the
    /// node's side of the runway: positive behind the line, negative past it or across the runway.
    /// Both arguments are signed offsets from the same centreline (<c>RunwayShape.Project</c>).
    /// </summary>
    public static double MetresBehindLine(double nodeLateralMetres, double aircraftLateralMetres)
        => aircraftLateralMetres * Math.Sign(nodeLateralMetres) - Math.Abs(nodeLateralMetres);

    /// <summary>
    /// Whether an aircraft that reads as level with the hold node (<see cref="IsReached"/>) has in
    /// fact missed the turn onto the hold's connector and is rolling on along the taxiway it was
    /// on. A connector that leaves a parallel taxiway at 30 to 60 degrees puts that taxiway inside
    /// every bound of <see cref="IsReached"/>: level with the node, 36 to 70 m from it, heading 29
    /// to 60 degrees off the segment. Two things tell it from a pass beside the node, and it takes
    /// both: the aircraft is still farther from the runway than the line is, and the tone is at
    /// full pan, so it is not being steered onto the route past the line either. A stop there is a
    /// stop on the wrong taxiway, and Continue would announce a crossing from it.
    ///
    /// <para>VirtualPilot <c>missturn</c>, 2026-11-27, 1,502 missed turns at 10 and 20 kt: without
    /// this 61 were stopped on the taxiway, each 26 to 53 m behind the line with the tone at full
    /// pan; with it none, and the 7 stops made no farther from the runway than the line are kept.
    /// The 11 stops the capture added to 7,617 ordinary flights were all either no more than 5 m
    /// behind the line or off full pan (VHHH J5, 21 m behind on a 180 degree turn the tone was
    /// leading; KSFO, a hold short of 01R, 35 m behind).</para>
    /// </summary>
    /// <param name="metresBehindLine"><see cref="MetresBehindLine"/>, or null for a hold that guards
    /// no known runway: there is then nothing to judge by, and the hold is taken.</param>
    /// <param name="steeringErrorDegrees">The heading error the steering tone is playing, either sign.</param>
    public static bool IsRollingOnBehindTheLine(double? metresBehindLine, double steeringErrorDegrees)
        => metresBehindLine is double behind
           && behind > MaxBehindLineMetres
           && Math.Abs(steeringErrorDegrees) >= FullPanSteeringErrorDegrees;
}

namespace MSFSBlindAssist.FirstOfficer;

/// <summary>
/// Detects the transition-altitude (climb → STD) and transition-level (descent → QNH)
/// crossings for the First Officer flight-phase monitors.
///
/// WHY TWO INDEPENDENT DETECTORS: the monitors previously tracked a single "in STD zone"
/// latch (<c>_prevInStd</c>) shared by BOTH thresholds, with a trailing
/// <c>if (nowAboveTrans) _prevInStd = true; else if (nowBelowTrans) _prevInStd = false;</c>.
/// That silently broke whenever the destination transition LEVEL sat more than ~600 ft
/// above the origin transition ALTITUDE (very common in Europe, e.g. TA 4000 / TL 6000):
/// the two ±hysteresis bands then OVERLAP — there is an altitude band that is
/// simultaneously "above the transition altitude" and "below the transition level". In
/// that band the QNH branch fired, then the trailing latch's <c>nowAboveTrans</c> arm
/// re-set the latch to true, so the "set local QNH" call-out re-fired on EVERY position
/// tick during descent (and flip-flopped STD/QNH if the aircraft levelled off in the
/// band). Tracking each threshold with its own arming latch removes the shared state, so
/// the two crossings can never contradict each other.
///
/// Pure logic (no sim/UI dependencies) so it is unit-testable; the monitors keep their own
/// aircraft-specific actions and announcements and simply ask this for the crossing.
/// </summary>
public sealed class TransitionCrossingDetector
{
    private const int HysteresisFt = 300;   // band around each threshold

    private int _transAltFt;   // 0 = not configured
    private int _transLvlFt;

    // Arming latches — each tracks position relative to ONLY ITS OWN threshold.
    // null = not yet determined (prevents a false trigger when starting mid-flight).
    private bool? _belowTa;    // were we below the transition ALTITUDE? (arms the climb→STD fire)
    private bool? _aboveTl;    // were we above the transition LEVEL?    (arms the descent→QNH fire)

    public enum Crossing
    {
        None,
        ClimbToStd,     // climbed up through the transition altitude
        DescendToQnh,   // descended down through the transition level
    }

    /// <summary>True once a transition altitude has been configured (SimBrief-sourced).</summary>
    public bool HasThresholds => _transAltFt > 0;

    /// <summary>
    /// Configure the thresholds (feet). A transition level of 0 falls back to the
    /// transition altitude so a single value still drives both crossings.
    /// </summary>
    public void SetThresholds(int transAltFt, int transLevelFt)
    {
        _transAltFt = transAltFt;
        _transLvlFt = transLevelFt > 0 ? transLevelFt : transAltFt;
    }

    /// <summary>Re-arm from the next sample's altitude (call on a new flight / SimBrief load).</summary>
    public void Reset()
    {
        _belowTa = null;
        _aboveTl = null;
    }

    /// <summary>
    /// Feed the current altitude (feet MSL) and climb/descent flags; returns which
    /// transition crossing (if any) just occurred. At most one crossing per call — the
    /// arming latches guarantee the climb and descent detectors never both fire, because
    /// a single flight arms one only after clearing the other's threshold.
    /// </summary>
    public Crossing Update(double altitudeFt, bool climbing, bool descending)
    {
        var result = Crossing.None;

        // ---- Transition ALTITUDE (climb → STD) ----
        bool aboveTa = altitudeFt > _transAltFt + HysteresisFt;
        bool belowTa = altitudeFt < _transAltFt - HysteresisFt;
        // "!descending" (not "climbing") so a VS lull on the exact crossing tick still
        // fires — the direction gate only blocks the OPPOSITE direction.
        if (!descending && aboveTa && _belowTa == true)
            result = Crossing.ClimbToStd;
        if (aboveTa)      _belowTa = false;
        else if (belowTa) _belowTa = true;

        // ---- Transition LEVEL (descent → QNH) ----
        bool belowTl = altitudeFt < _transLvlFt - HysteresisFt;
        bool aboveTl = altitudeFt > _transLvlFt + HysteresisFt;
        if (!climbing && belowTl && _aboveTl == true)
            result = Crossing.DescendToQnh;
        if (belowTl)      _aboveTl = false;
        else if (aboveTl) _aboveTl = true;

        return result;
    }
}

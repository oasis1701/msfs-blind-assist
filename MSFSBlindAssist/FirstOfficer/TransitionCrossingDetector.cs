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
/// WHY THE CLIMB THRESHOLD MOVES AT ARRIVAL: the climb crossing uses the ORIGIN transition
/// altitude and the descent crossing the DESTINATION transition level. Once the descent crossing
/// has fired, the flight is in its arrival, and a climb back above the origin's altitude is a
/// go-around or a missed approach, flown on QNH: its published altitudes all lie below the
/// destination's transition altitude, which is at or below its transition level. Measured from
/// the origin altitude (EHAM 3,000 ft to EDDF FL70), a go-around to 5,000 ft set the altimeters
/// to standard, and the second approach, never back above FL70, never armed the QNH crossing
/// again, so the aircraft landed on standard. After the descent crossing, a climb sets standard
/// only back above the destination transition level, which also re-arms the QNH crossing.
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
    private bool? _belowTa;    // were we below the climb threshold? (arms the climb→STD fire)
    private bool? _aboveTl;    // were we above the transition LEVEL? (arms the descent→QNH fire)

    // Set by the descent crossing: from then on the climb threshold is the transition level
    // (see the class doc), until Reset.
    private bool _arrived;

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
        _arrived = false;
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

        // ---- Climb → STD: the origin transition ALTITUDE, the transition LEVEL once arrived ----
        int climbFt = _arrived ? _transLvlFt : _transAltFt;
        bool aboveTa = altitudeFt > climbFt + HysteresisFt;
        bool belowTa = altitudeFt < climbFt - HysteresisFt;
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
        {
            result = Crossing.DescendToQnh;
            // Below the transition level, which is the climb threshold from here on: a climb
            // straight back above it sets standard again.
            _arrived = true;
            _belowTa = true;
        }
        if (belowTl)      _aboveTl = false;
        else if (aboveTl) _aboveTl = true;

        return result;
    }
}

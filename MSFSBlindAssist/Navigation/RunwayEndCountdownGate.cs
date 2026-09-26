namespace MSFSBlindAssist.Navigation;

/// <summary>What the runway-end countdown does on this frame.</summary>
public enum RunwayEndCountdownAction
{
    /// <summary>Keep counting down to the runway end.</summary>
    Continue,
    /// <summary>The aircraft is laterally clear of the runway: close with "Runway vacated".</summary>
    Vacated,
    /// <summary>At (or past) the runway end: "End of runway … Turn around … Backtracking."</summary>
    BacktrackAtEnd,
    /// <summary>Turned around away from the end: backtrack without claiming the runway ended.</summary>
    BacktrackMidRunway,
    /// <summary>Stopped away from the end: one factual notice, no command.</summary>
    StoppedMidRunwayNotice,
}

/// <summary>
/// Decides how the runway-end countdown ends (PR #236 review, finding F1). The countdown used to
/// hand ANY stop or 15-degree turn below 90 kt to backtracking, which is only right once no exit
/// remains near the end; a pilot turning off at a taxiway, or stopping for ATC, mid-runway was
/// told "End of runway. Turn around." on an active runway.
///
/// <para>Rules, in order: laterally clear of the runway → vacated; heading at least
/// <see cref="TurnedAroundMinDeg"/> off → backtrack (at the end when near it); near the end and
/// STOPPED → backtrack at the end; stopped away from the end → one notice; otherwise keep counting
/// down. A TURN near the end is never a trigger — a turn-off and the start of a turnaround look the
/// same there, and backtracking cannot be taken back — so a turn onto a taxiway, near the end or
/// mid-runway, says nothing until the aircraft is clear. Pure — <c>RunwayEndCountdownGateTests</c>.</para>
/// </summary>
public static class RunwayEndCountdownGate
{
    /// <summary>A heading this far off the runway means the aircraft has turned around on the
    /// pavement. Well past a normal exit turn, short of a full 180.</summary>
    public const double TurnedAroundMinDeg = 150.0;

    public static RunwayEndCountdownAction Decide(
        double distToEndFeet, double groundSpeedKts, double headingDeltaAbsDeg,
        bool laterallyClear, bool stoppedNoticeGiven)
    {
        if (laterallyClear) return RunwayEndCountdownAction.Vacated;

        bool nearEnd = distToEndFeet <= RolloutExitGate.NearRunwayEndFeet;
        if (headingDeltaAbsDeg >= TurnedAroundMinDeg)
            return nearEnd ? RunwayEndCountdownAction.BacktrackAtEnd : RunwayEndCountdownAction.BacktrackMidRunway;

        // A TURN near the end is deliberately NOT a backtrack trigger. Between 15 and 150 degrees a
        // turn onto the taxiway at the runway end and the start of a turnaround look identical, and
        // backtracking is a different guidance state — once entered, the "laterally clear" rule above
        // never runs again, so a wrong call could not be taken back and the pilot was told to turn
        // around while they were correctly leaving the runway. Keep counting: a turn-off becomes
        // laterally clear, a turnaround passes TurnedAroundMinDeg or stops.
        bool stopped = groundSpeedKts < RolloutExitGate.NoExitStoppedGroundSpeedKts;

        if (nearEnd && stopped) return RunwayEndCountdownAction.BacktrackAtEnd;
        if (stopped && !stoppedNoticeGiven) return RunwayEndCountdownAction.StoppedMidRunwayNotice;
        return RunwayEndCountdownAction.Continue;
    }
}

/// <summary>Spoken runway headings.</summary>
public static class RunwayHeadings
{
    /// <summary>
    /// The heading to speak for backtracking a runway: its MAGNETIC reciprocal, because every
    /// heading instrument a pilot turns to is magnetic (PR #236 review, finding F9 — KSEA 34L
    /// spoke 180 for a magnetic 165). <paramref name="headingMagDeg"/> is <c>Runway.HeadingMag</c>,
    /// which can be negative. Rounded, with north spoken as 360, never 0.
    /// </summary>
    public static int SpokenReciprocalMagnetic(double headingMagDeg)
    {
        double r = (headingMagDeg + 180.0) % 360.0;
        if (r < 0.0) r += 360.0;
        int rounded = (int)Math.Round(r, MidpointRounding.AwayFromZero);
        return rounded == 0 ? 360 : rounded;
    }
}

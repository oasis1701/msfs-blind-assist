namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Which exit the Landing Exit planner pre-selects: the first one that gets clear of the runway
/// (<see cref="LandingExit.VacatesRunway"/>) and is not a turnaround (angle ≤
/// <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/>); else the first that gets clear; else the first.
/// KMEM 36L lists M3 first — an 18R high-speed exit that is a 130° turnaround for 36L at 1,400 ft.
/// </summary>
public static class LandingExitDefault
{
    public static int Index(IReadOnlyList<LandingExit> exits)
    {
        if (exits == null || exits.Count == 0) return -1;
        for (int i = 0; i < exits.Count; i++)
            if (exits[i].VacatesRunway && exits[i].ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg)
                return i;
        for (int i = 0; i < exits.Count; i++)
            if (exits[i].VacatesRunway) return i;
        return 0;
    }
}

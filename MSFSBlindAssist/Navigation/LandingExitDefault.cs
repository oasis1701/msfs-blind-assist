namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Which exit the Landing Exit planner pre-selects: the first one that gets clear of the runway
/// (<see cref="LandingExit.VacatesRunway"/>) and is not a turnaround (angle ≤
/// <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/>); else the first that is not a turnaround; else the
/// first that gets clear; else the first. A turnaround is never pre-selected while a forward exit exists -
/// the touchdown re-plan never guides to one (<c>LandingExitReplan.IsUsable</c>), and a pilot who accepts
/// the default takes what it names. KMEM 36L lists M3 first — an 18R high-speed exit that is a 130°
/// turnaround for 36L at 1,400 ft.
/// </summary>
public static class LandingExitDefault
{
    public static int Index(IReadOnlyList<LandingExit> exits)
    {
        if (exits == null || exits.Count == 0) return -1;
        for (int i = 0; i < exits.Count; i++)
            if (exits[i].VacatesRunway && IsForward(exits[i])) return i;
        for (int i = 0; i < exits.Count; i++)
            if (IsForward(exits[i])) return i;
        for (int i = 0; i < exits.Count; i++)
            if (exits[i].VacatesRunway) return i;
        return 0;
    }

    /// <summary>
    /// Where the pilot's pick <paramref name="previous"/> sits in a rebuilt list (the online taxiway-name
    /// refresh can insert exits ahead of it): the same node; else the same-named exit of the same kind -
    /// forward, or turnaround - nearest the pick's distance from the threshold; else -1. The first entry of
    /// that name used to win, which in hold-short mode moved a pick of the forward C@3281 onto the C@2231
    /// turnaround listed ahead of it, with nothing said.
    /// </summary>
    public static int RestoreIndex(IReadOnlyList<LandingExit> exits, LandingExit? previous)
    {
        if (exits == null || previous == null) return -1;
        if (previous.NodeId != 0)
            for (int i = 0; i < exits.Count; i++)
                if (exits[i].NodeId == previous.NodeId) return i;
        if (string.IsNullOrEmpty(previous.TaxiwayName)) return -1;

        int best = -1;
        double bestFeet = double.MaxValue;
        for (int i = 0; i < exits.Count; i++)
        {
            var e = exits[i];
            if (!string.Equals(e.TaxiwayName, previous.TaxiwayName, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsForward(e) != IsForward(previous)) continue;
            double feet = Math.Abs(e.DistanceFromThresholdFeet - previous.DistanceFromThresholdFeet);
            if (feet < bestFeet) { best = i; bestFeet = feet; }
        }
        return best;
    }

    private static bool IsForward(LandingExit e) => e.ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg;
}

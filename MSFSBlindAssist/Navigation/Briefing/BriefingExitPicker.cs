namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>The exit the briefing expects the aircraft to take, and the next one if it is missed.</summary>
public sealed record ExitChoice(LandingExit Exit, LandingExit? NextExit, bool ComfortablyReachable);

/// <summary>
/// Which landing exit to brief. Candidates vacate the runway and turn no more than
/// <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/>; the first one comfortably reachable at the
/// aircraft's typical touchdown speed (<see cref="RolloutExitGate.ComfortableExitLeadFeet"/>, the
/// touchdown re-plan's own rule) is chosen, except that a High-speed exit no more than
/// <see cref="HighSpeedPreferenceFeet"/> further along wins over a Normal one. With nothing
/// reachable the furthest candidate is briefed and flagged.
/// </summary>
public static class BriefingExitPicker
{
    public const double HighSpeedPreferenceFeet = 1500.0;

    public static ExitChoice? Pick(IReadOnlyList<LandingExit> exitsSortedByThreshold, double touchdownSpeedKts)
    {
        if (exitsSortedByThreshold == null || exitsSortedByThreshold.Count == 0) return null;

        var candidates = exitsSortedByThreshold
            .Where(e => e.VacatesRunway && e.ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg)
            .OrderBy(e => e.DistanceFromThresholdFeet)
            .ToList();
        if (candidates.Count == 0)
            candidates = exitsSortedByThreshold.Where(e => e.VacatesRunway)
                .OrderBy(e => e.DistanceFromThresholdFeet).ToList();
        if (candidates.Count == 0) return null;

        var reachable = candidates
            .Where(e => e.DistanceFromTouchdownFeet >=
                        RolloutExitGate.ComfortableExitLeadFeet(touchdownSpeedKts, e.ExitAngleDegrees))
            .ToList();
        if (reachable.Count == 0)
            return new ExitChoice(candidates[^1], null, ComfortablyReachable: false);

        var chosen = reachable[0];
        if (!IsHighSpeed(chosen))
        {
            var rapid = reachable.FirstOrDefault(e => IsHighSpeed(e) &&
                e.DistanceFromThresholdFeet - chosen.DistanceFromThresholdFeet <= HighSpeedPreferenceFeet);
            if (rapid != null) chosen = rapid;
        }

        int index = candidates.IndexOf(chosen);
        var next = index >= 0 && index + 1 < candidates.Count ? candidates[index + 1] : null;
        return new ExitChoice(chosen, next, ComfortablyReachable: true);
    }

    private static bool IsHighSpeed(LandingExit e) =>
        string.Equals(e.ExitType, "High-speed", StringComparison.OrdinalIgnoreCase);
}

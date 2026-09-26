namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>The exit the briefing expects the aircraft to take, and the next one if it is missed.</summary>
public sealed record ExitChoice(LandingExit Exit, LandingExit? NextExit, bool ComfortablyReachable)
{
    /// <summary>When nothing briefable is comfortably reachable: the exits that ARE, set aside because their mapped
    /// route leaves the runway on the other side from the one they turn toward
    /// (<see cref="TaxiBriefingPlanner.BriefableExitRouteStarts"/>). While any is listed the runway is not short, and
    /// the block must not say so.</summary>
    public IReadOnlyList<LandingExit> ReachableExitsSetAside { get; init; } = Array.Empty<LandingExit>();
}

/// <summary>
/// Which landing exit to brief. Candidates vacate the runway and turn no more than
/// <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/>. The choice is made among the candidates comfortably
/// reachable at the aircraft's typical touchdown speed (<see cref="RolloutExitGate.ComfortableExitLeadFeet"/>,
/// the touchdown re-plan's own rule) that lie within <see cref="PreferenceWindowFeet"/> of the first of them:
/// High-speed exits before the rest, each in runway order — and, when the stand is known, the first of those
/// whose route to it stays clear of the runway just landed on (owner decision, 2026-09-26: prefer the exit on
/// the gate side). When every one crosses back, or no stand is known, the usual choice stands. With nothing
/// reachable the furthest candidate is briefed and flagged.
/// <para>The exit to take if that one is missed is the next candidate on the SAME side at least
/// <see cref="NextExitMinSeparationFeet"/> further along: a pilot who misses a left turn-off looks for the next
/// one on the left, and one a few feet on — or across the runway at the same junction (KPIT 32: 1 ft) — is
/// missed with it.</para>
/// </summary>
public static class BriefingExitPicker
{
    /// <summary>How much further along than the first comfortably reachable exit a preferred one may be: the
    /// window both the high-speed and the gate-side preferences search.</summary>
    public const double PreferenceWindowFeet = 1500.0;

    /// <summary>How much further along the exit to take if the briefed one is missed must be.</summary>
    public const double NextExitMinSeparationFeet = 500.0;

    /// <param name="exits">The runway's exits, in any order.</param>
    /// <param name="touchdownSpeedKts">The aircraft's typical touchdown ground speed.</param>
    /// <param name="routeAvoidsLandingRunway">True when the exit's taxi route to the stand exists and never crosses the
    /// runway just landed on (either end). Null when no stand is known: no gate-side preference.</param>
    public static ExitChoice? Pick(IReadOnlyList<LandingExit> exits, double touchdownSpeedKts,
                                   Func<LandingExit, bool>? routeAvoidsLandingRunway = null)
    {
        if (exits == null || exits.Count == 0) return null;

        var candidates = exits
            .Where(e => e.VacatesRunway && e.ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg)
            .OrderBy(e => e.DistanceFromThresholdFeet)
            .ToList();
        if (candidates.Count == 0)
            candidates = exits.Where(e => e.VacatesRunway)
                .OrderBy(e => e.DistanceFromThresholdFeet).ToList();
        if (candidates.Count == 0) return null;

        var reachable = candidates.Where(e => IsComfortablyReachable(e, touchdownSpeedKts)).ToList();
        if (reachable.Count == 0)
            return new ExitChoice(candidates[^1], null, ComfortablyReachable: false);

        // Preference order within the window: High-speed first, then the rest, each in runway order (OrderBy is
        // stable). Its head is the choice without a stand — the first comfortable exit, unless a high-speed one
        // lies within the window.
        double first = reachable[0].DistanceFromThresholdFeet;
        var preferred = reachable
            .Where(e => e.DistanceFromThresholdFeet - first <= PreferenceWindowFeet)
            .OrderBy(e => IsHighSpeed(e) ? 0 : 1)
            .ToList();
        var chosen = (routeAvoidsLandingRunway == null ? null : preferred.FirstOrDefault(routeAvoidsLandingRunway))
                     ?? preferred[0];

        var next = candidates.FirstOrDefault(e =>
            e.DistanceFromThresholdFeet - chosen.DistanceFromThresholdFeet >= NextExitMinSeparationFeet &&
            OnTheSameSide(e, chosen));
        return new ExitChoice(chosen, next, ComfortablyReachable: true);
    }

    /// <summary>Whether the aircraft can slow to the exit's turn-off speed before it with comfortable braking from its
    /// typical touchdown speed — the touchdown re-plan's own rule (<see cref="RolloutExitGate.ComfortableExitLeadFeet"/>).</summary>
    public static bool IsComfortablyReachable(LandingExit e, double touchdownSpeedKts) =>
        e.DistanceFromTouchdownFeet >= RolloutExitGate.ComfortableExitLeadFeet(touchdownSpeedKts, e.ExitAngleDegrees);

    /// <summary>Whether <paramref name="e"/> leaves the runway on <paramref name="chosen"/>'s side. A chosen exit of
    /// unknown side constrains nothing.</summary>
    private static bool OnTheSameSide(LandingExit e, LandingExit chosen) =>
        string.IsNullOrEmpty(chosen.ExitSide) ||
        string.Equals(e.ExitSide, chosen.ExitSide, StringComparison.OrdinalIgnoreCase);

    private static bool IsHighSpeed(LandingExit e) =>
        string.Equals(e.ExitType, "High-speed", StringComparison.OrdinalIgnoreCase);
}

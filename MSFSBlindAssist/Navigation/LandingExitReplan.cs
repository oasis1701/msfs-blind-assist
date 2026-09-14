namespace MSFSBlindAssist.Navigation;

/// <summary>Which rule picked the re-planned exit, for the landing_exit.log diagnostic.</summary>
public enum LandingExitReplanRule
{
    /// <summary>The pilot's own planned taxiway, re-measured from the end actually landed on.</summary>
    PilotsOwnTaxiway,
    /// <summary>The first usable exit at or beyond the planned exit's distance from its threshold.</summary>
    AtOrBeyondPlannedDistance,
    /// <summary>The usable exit closest before that distance.</summary>
    ClosestBeforePlannedDistance,
    /// <summary>No usable exit.</summary>
    None,
}

public readonly record struct LandingExitReplanChoice(LandingExit? Exit, LandingExitReplanRule Rule);

/// <summary>
/// Chooses the exit to guide to when the landing-exit plan turns out to be for a different runway,
/// or for the other end of the runway landed on (PR #236 review).
///
/// <para>An exit is usable only if its turn is at most
/// <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/> — which rejects a rapid exit that becomes a
/// backward hairpin from the other end — and it lies at least
/// <see cref="RolloutExitGate.ExitLeadFeet"/> ahead of the aircraft. The pilot's own taxiway wins
/// when it survives both tests; otherwise the planned exit's distance from its threshold stands in
/// for the pilot's braking plan: the first usable exit at or beyond it, else the closest before it.
/// </para>
///
/// <para>Pure — <c>LandingExitReplanTests</c>.</para>
/// </summary>
public static class LandingExitReplan
{
    public static LandingExitReplanChoice ChooseExit(
        IReadOnlyList<LandingExit>? exits,
        int? preferredNodeId,
        double plannedExitDistanceFromThresholdFeet,
        double aircraftDistanceFromThresholdFeet,
        double groundSpeedKts)
    {
        if (exits == null || exits.Count == 0)
            return new LandingExitReplanChoice(null, LandingExitReplanRule.None);

        double earliestFeet = aircraftDistanceFromThresholdFeet + RolloutExitGate.ExitLeadFeet(groundSpeedKts);

        if (preferredNodeId.HasValue)
        {
            foreach (var e in exits)
            {
                if (e != null && e.NodeId == preferredNodeId.Value && IsUsable(e, earliestFeet))
                    return new LandingExitReplanChoice(e, LandingExitReplanRule.PilotsOwnTaxiway);
            }
        }

        LandingExit? beyond = null;
        LandingExit? before = null;
        foreach (var e in exits)
        {
            if (e == null || !IsUsable(e, earliestFeet)) continue;
            if (e.DistanceFromThresholdFeet >= plannedExitDistanceFromThresholdFeet)
            {
                if (beyond == null || e.DistanceFromThresholdFeet < beyond.DistanceFromThresholdFeet)
                    beyond = e;
            }
            else if (before == null || e.DistanceFromThresholdFeet > before.DistanceFromThresholdFeet)
            {
                before = e;
            }
        }

        if (beyond != null) return new LandingExitReplanChoice(beyond, LandingExitReplanRule.AtOrBeyondPlannedDistance);
        if (before != null) return new LandingExitReplanChoice(before, LandingExitReplanRule.ClosestBeforePlannedDistance);
        return new LandingExitReplanChoice(null, LandingExitReplanRule.None);
    }

    /// <summary>Turn of at most <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/> (an angle of 0 is
    /// GetLandingExits' "not measured" sentinel and passes, as in
    /// <see cref="RolloutExitGate.FirstSuitableDownfieldExit"/>), and no earlier than
    /// <paramref name="earliestDistanceFromThresholdFeet"/>.</summary>
    public static bool IsUsable(LandingExit exit, double earliestDistanceFromThresholdFeet)
        => exit.ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg
           && exit.DistanceFromThresholdFeet >= earliestDistanceFromThresholdFeet;
}

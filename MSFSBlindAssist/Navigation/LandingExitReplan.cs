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

/// <summary>How far ahead of the aircraft a pass of the re-plan requires an exit to be.</summary>
public enum LandingExitLeadTier
{
    /// <summary>Reachable with comfortable braking: <see cref="RolloutExitGate.ComfortableExitLeadFeet"/>.</summary>
    Comfortable,
    /// <summary>Reachable only with firm braking: <see cref="RolloutExitGate.ExitLeadFeet"/>.</summary>
    Floor,
}

public readonly record struct LandingExitReplanChoice(LandingExit? Exit, LandingExitReplanRule Rule, LandingExitLeadTier Tier);

/// <summary>
/// Chooses the exit to guide to when the landing-exit plan turns out to be for a different runway,
/// or for the other end of the runway landed on (PR #236 review).
///
/// <para>The caller runs two passes. <see cref="LandingExitLeadTier.Comfortable"/> admits only exits the
/// aircraft can slow down for comfortably; <see cref="LandingExitLeadTier.Floor"/> falls back to
/// <see cref="RolloutExitGate.ExitLeadFeet"/>, so no exit the reachability floor offers is lost. That floor
/// was tuned below 50 kt and asks 4-6 m/s² at touchdown speed. In either pass an exit is usable only if its
/// turn is at most <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/>, which rejects a rapid exit that
/// becomes a backward hairpin from the other end.</para>
///
/// <para>Within a pass the pilot's own taxiway wins when it is usable (<see cref="IsSameTurnoffFromOtherEnd"/>);
/// otherwise the planned exit's distance from its threshold stands in for the pilot's braking plan: the
/// first usable exit at or beyond it, else the closest before it.</para>
///
/// <para>Pure — <c>LandingExitReplanTests</c>.</para>
/// </summary>
public static class LandingExitReplan
{
    private const double FeetPerMetre = 1.0 / 0.3048;

    /// <param name="reciprocalPlannedExit">The pilot's planned exit when the aircraft landed on the other
    /// end of the same runway; null for a different runway, where a namesake is a different junction.</param>
    public static LandingExitReplanChoice ChooseExit(
        IReadOnlyList<LandingExit>? exits,
        LandingExit? reciprocalPlannedExit,
        double plannedExitDistanceFromThresholdFeet,
        double aircraftDistanceFromThresholdFeet,
        double groundSpeedKts,
        LandingExitLeadTier tier)
    {
        if (exits == null || exits.Count == 0)
            return new LandingExitReplanChoice(null, LandingExitReplanRule.None, tier);

        // Prefer an exit the graph can actually get the aircraft off the runway on. The planner
        // DIALOG works this out per exit, flags the ones with "no taxiway mapped clear of the
        // runway" and refuses to default to one — "better to learn while choosing than at 60 knots
        // on the rollout". This chooses with nobody watching, at landing speed, so it needs the
        // same preference; without it a junction that dead-ends on the runway beat a real turn-off
        // a few hundred feet further on. A flagged exit is still offered when it is all there is,
        // exactly as the dialog keeps them in its list — at some airports they are the only ones.
        var vacating = new List<LandingExit>();
        foreach (var e in exits)
            if (e != null && e.VacatesRunway) vacating.Add(e);

        if (vacating.Count > 0)
        {
            var preferred = ChooseFrom(vacating, reciprocalPlannedExit,
                plannedExitDistanceFromThresholdFeet, aircraftDistanceFromThresholdFeet,
                groundSpeedKts, tier);
            if (preferred.Exit != null) return preferred;
        }

        return ChooseFrom(exits, reciprocalPlannedExit,
            plannedExitDistanceFromThresholdFeet, aircraftDistanceFromThresholdFeet,
            groundSpeedKts, tier);
    }

    private static LandingExitReplanChoice ChooseFrom(
        IReadOnlyList<LandingExit> exits,
        LandingExit? reciprocalPlannedExit,
        double plannedExitDistanceFromThresholdFeet,
        double aircraftDistanceFromThresholdFeet,
        double groundSpeedKts,
        LandingExitLeadTier tier)
    {
        if (reciprocalPlannedExit != null)
        {
            LandingExit? own = null;
            double ownMetres = double.MaxValue;
            foreach (var e in exits)
            {
                if (e == null || !IsUsable(e, aircraftDistanceFromThresholdFeet, groundSpeedKts, tier)) continue;
                if (!IsSameTurnoffFromOtherEnd(reciprocalPlannedExit, e)) continue;
                double metres = SeparationMetres(reciprocalPlannedExit, e);
                if (own == null || metres < ownMetres)
                {
                    own = e;
                    ownMetres = metres;
                }
            }
            if (own != null) return new LandingExitReplanChoice(own, LandingExitReplanRule.PilotsOwnTaxiway, tier);
        }

        LandingExit? beyond = null;
        LandingExit? before = null;
        foreach (var e in exits)
        {
            if (e == null || !IsUsable(e, aircraftDistanceFromThresholdFeet, groundSpeedKts, tier)) continue;
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

        if (beyond != null) return new LandingExitReplanChoice(beyond, LandingExitReplanRule.AtOrBeyondPlannedDistance, tier);
        if (before != null) return new LandingExitReplanChoice(before, LandingExitReplanRule.ClosestBeforePlannedDistance, tier);
        return new LandingExitReplanChoice(null, LandingExitReplanRule.None, tier);
    }

    /// <summary>Turn of at most <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/> (an angle of 0 is
    /// GetLandingExits' "not measured" sentinel and passes, as in
    /// <see cref="RolloutExitGate.FirstSuitableDownfieldExit"/>), and at least <see cref="LeadFeet"/> ahead
    /// of the aircraft.</summary>
    public static bool IsUsable(LandingExit exit, double aircraftDistanceFromThresholdFeet,
        double groundSpeedKts, LandingExitLeadTier tier)
        => exit.ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg
           && exit.DistanceFromThresholdFeet >= aircraftDistanceFromThresholdFeet + LeadFeet(exit, groundSpeedKts, tier);

    /// <summary>How far ahead of the aircraft <paramref name="exit"/> must be in <paramref name="tier"/>.</summary>
    public static double LeadFeet(LandingExit exit, double groundSpeedKts, LandingExitLeadTier tier)
        => tier == LandingExitLeadTier.Comfortable
            ? RolloutExitGate.ComfortableExitLeadFeet(groundSpeedKts, exit.ExitAngleDegrees)
            : RolloutExitGate.ExitLeadFeet(groundSpeedKts);

    /// <summary>
    /// The pilot's planned exit seen from the other end of the same runway. GetLandingExits keeps the node
    /// nearest ITS OWN threshold for a taxiway, so the node usually differs between the two ends (only 17%
    /// of planned exits came back with the same node across 408 fs2024 twin runway ends): the same node, or
    /// the same non-blank name within <see cref="RolloutExitGate.EarlyVacateMaxPassedFeet"/>, counts — and
    /// in both cases only on the same physical side of the runway, or the pilot vacates with the runway
    /// just landed on between the aircraft and the planned apron (16% of same-name exits leave the other way).
    /// </summary>
    public static bool IsSameTurnoffFromOtherEnd(LandingExit planned, LandingExit candidate)
    {
        if (planned == null || candidate == null) return false;
        if (!PhysicalSideAgreesAcrossTwinEnds(planned.ExitSide, candidate.ExitSide)) return false;
        if (planned.NodeId != 0 && candidate.NodeId == planned.NodeId) return true;
        return !string.IsNullOrWhiteSpace(planned.TaxiwayName)
            && string.Equals(planned.TaxiwayName, candidate.TaxiwayName, StringComparison.OrdinalIgnoreCase)
            && SeparationMetres(planned, candidate) * FeetPerMetre <= RolloutExitGate.EarlyVacateMaxPassedFeet;
    }

    /// <summary>
    /// "Left" seen from one end is "Right" seen from the other, so on twin ends the strings must DIFFER.
    /// A blank side means the graph could not tell, not that the side is wrong (as in
    /// <see cref="RolloutExitGate.MatchEarlyVacateExit"/>), so it never blocks a match.
    /// </summary>
    public static bool PhysicalSideAgreesAcrossTwinEnds(string plannedSide, string candidateSide)
        => string.IsNullOrEmpty(plannedSide) || string.IsNullOrEmpty(candidateSide)
           || !string.Equals(plannedSide, candidateSide, StringComparison.OrdinalIgnoreCase);

    /// <summary>Straight-line distance between two exits' junction nodes, in metres.</summary>
    public static double SeparationMetres(LandingExit a, LandingExit b)
        => TaxiGraph.FastDistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude);
}

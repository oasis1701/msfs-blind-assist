using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation.Briefing;

/// <summary>Whether an exit's taxi route to the stand exists, and whether it crosses the runway just landed on.</summary>
public enum ExitRoute { None, CrossesLandingRunway, Clear }

/// <summary>What the block says about the runway when no exit is comfortably reachable.</summary>
public enum UnreachableRunway
{
    /// <summary>The aircraft cannot stop on it with comfortable braking from its typical touchdown speed — by the jet
    /// re-plan's rule, so the block never says so to an unrecognised type or a code A or B aircraft
    /// (TaxiBriefingRenderer.UnreachableSentence).</summary>
    Short,
    /// <summary>It can stop on it: the exits all lie behind the touchdown, and it backtracks to the briefed one.</summary>
    LongEnoughToBacktrack,
    /// <summary>The runway's length is not in the navdata.</summary>
    LengthUnknown,
}

/// <summary>The exit the briefing expects the aircraft to take, and the next one if it is missed.</summary>
public sealed record ExitChoice(LandingExit Exit, LandingExit? NextExit, bool ComfortablyReachable)
{
    /// <summary>When nothing briefable is comfortably reachable: the exits that ARE, set aside because their mapped
    /// route leaves the runway on the other side from the one they turn toward
    /// (<see cref="TaxiBriefingPlanner.BriefableExitRouteStarts"/>). While any is listed the runway is not short, and
    /// the block must not say so.</summary>
    public IReadOnlyList<LandingExit> ReachableExitsSetAside { get; init; } = Array.Empty<LandingExit>();

    /// <summary>Comfortably reachable exits exist, but none has a mapped route to the stand: the briefed exit is one that
    /// does and cannot be made comfortably — the last one before them, or with none there the first one after them
    /// (<see cref="BriefedExitBehindReachable"/>) (re-review N-6: a stand on a piece of the taxi network whose only exit
    /// comes early). The block then says that, never that no exit is comfortably reachable.</summary>
    public bool ReachableExitsHaveNoRoute { get; init; }

    /// <summary>With <see cref="ReachableExitsHaveNoRoute"/>: the briefed exit lies BEFORE the first comfortably
    /// reachable exit (the aircraft stops and backtracks to it), rather than after it (it needs firmer than comfortable
    /// braking).</summary>
    public bool BriefedExitBehindReachable { get; init; }

    /// <summary>Whether a stand was known when the exit was picked. Without one nothing was routed: the briefed exit is
    /// then simply the first comfortable one, or the last exit when none is comfortable.</summary>
    public bool StandKnown { get; init; } = true;

    /// <summary>Whether the runway is short, long enough to stop on and backtrack, or of unknown length
    /// (<see cref="TaxiBriefingPlanner.RunwayLongEnoughToStop"/>). Meaningful only when
    /// <see cref="ComfortablyReachable"/> is false.</summary>
    public UnreachableRunway RunwayLength { get; init; } = UnreachableRunway.Short;
}

/// <summary>
/// Which landing exit to brief. Candidates vacate the runway and turn no more than
/// <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/>. The choice is made among the candidates comfortably
/// reachable at the aircraft's typical touchdown speed (<see cref="RolloutExitGate.ComfortableExitLeadFeet"/>,
/// the touchdown re-plan's own rule, measured from <see cref="AimPointFeet"/>) that lie within
/// <see cref="PreferenceWindowFeet"/> of the first of them: High-speed exits before the rest, each in runway
/// order — and, when the stand is known, the first whose route to it is <see cref="ExitRoute.Clear"/> of the
/// runway just landed on, else the first that <see cref="ExitRoute.CrossesLandingRunway"/> — an exit with NO
/// route at all (<see cref="ExitRoute.None"/>) is never preferred over one with a route, in or out of the
/// window: with nothing routable inside it, any routable exit anywhere reachable is briefed ahead of a dead
/// end inside the window — and with no reachable exit routable at all, the furthest candidate that routes is
/// briefed and flagged (<see cref="ExitChoice.ReachableExitsHaveNoRoute"/>) rather than a reachable dead end — the
/// last one BEFORE the first comfortable exit (<see cref="ExitChoice.BriefedExitBehindReachable"/>), else the first one
/// after it, and the exit to take if that one is missed is chosen among the exits that route.
/// With nothing reachable the furthest candidate that routes is briefed and flagged, or the furthest of all when
/// no stand is known.
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

    /// <summary>GetLandingExits' touchdown aim point, past the landing threshold (TaxiGraph's TOUCHDOWN_AIM_FT, which
    /// fills <see cref="LandingExit.DistanceFromTouchdownFeet"/>).</summary>
    public const double JetAimPointFeet = 1000.0;

    /// <summary>
    /// Where the reachability test measures from: the jet aim point, or nearer the threshold on a short runway, where
    /// ICAO Annex 14 (Table 5-2) puts the aiming point — 150 m with less than 800 m of landing distance, 250 m with less
    /// than 1,200 m. Measured from 1,000 ft, a Cessna needed an exit 2,213 ft in and every short strip read "short".
    /// A runway of unknown length keeps the jet aim point.
    /// </summary>
    public static double AimPointFeet(Runway rwy)
    {
        if (rwy.Length <= 0) return JetAimPointFeet;
        double ldaMetres = Math.Max(0.0, rwy.Length - rwy.ThresholdOffset) * 0.3048;
        if (ldaMetres < 800.0) return 150.0 / 0.3048;
        if (ldaMetres < 1200.0) return 250.0 / 0.3048;
        return JetAimPointFeet;
    }

    /// <param name="exits">The runway's exits, in any order.</param>
    /// <param name="touchdownSpeedKts">The aircraft's typical touchdown ground speed.</param>
    /// <param name="route">The exit's taxi route to the stand: null when no stand is known (no gate-side
    /// preference, and reachability alone decides among the comfortable candidates).</param>
    /// <param name="aimFeet">Where reachability is measured from — <see cref="AimPointFeet"/> on a short runway,
    /// else <see cref="JetAimPointFeet"/>.</param>
    public static ExitChoice? Pick(IReadOnlyList<LandingExit> exits, double touchdownSpeedKts,
                                   Func<LandingExit, ExitRoute>? route = null, double aimFeet = JetAimPointFeet)
    {
        if (exits == null || exits.Count == 0) return null;

        var candidates = Candidates(exits);
        if (candidates.Count == 0) return null;

        var reachable = candidates.Where(e => IsComfortablyReachable(e, touchdownSpeedKts, aimFeet)).ToList();
        if (reachable.Count == 0)
            return new ExitChoice(
                route == null ? candidates[^1] : candidates.LastOrDefault(e => route(e) != ExitRoute.None) ?? candidates[^1],
                null, ComfortablyReachable: false) { StandKnown = route != null };

        // Preference order within the window: High-speed first, then the rest, each in runway order (OrderBy is
        // stable). Its head is the choice without a stand — the first comfortable exit, unless a high-speed one
        // lies within the window.
        double first = reachable[0].DistanceFromThresholdFeet;
        var preferred = reachable
            .Where(e => e.DistanceFromThresholdFeet - first <= PreferenceWindowFeet)
            .OrderBy(e => IsHighSpeed(e) ? 0 : 1)
            .ToList();
        LandingExit? chosen = route == null ? preferred[0]
            // Within the window the gate side first (a route clear of the runway just landed on), then any route; an exit
            // with NO route is never briefed while one with a route exists anywhere reachable — a dead end made the whole
            // leg unavailable with a routable exit a few hundred feet on.
            : preferred.FirstOrDefault(e => route(e) == ExitRoute.Clear)
              ?? preferred.FirstOrDefault(e => route(e) == ExitRoute.CrossesLandingRunway)
              ?? reachable.FirstOrDefault(e => route(e) != ExitRoute.None);
        if (chosen == null)
        {
            // No reachable exit routes to the stand, but one the aircraft cannot make comfortably does (re-review N-6):
            // that one, flagged — never a reachable dead end ("no taxi route connects"). Behind the first comfortable exit,
            // the last such one: the aircraft stops and backtracks to it. With none behind, the FIRST routed one after it
            // (fix wave 3): a steep exit a few hundred feet past a comfortable rapid one is not comfortable, and it is
            // reached before any later one. The exit to take if that one is missed must route to the stand as well.
            int firstReachable = candidates.IndexOf(reachable[0]);
            var routedCandidates = candidates.Where(e => route!(e) != ExitRoute.None).ToList();
            var behind = routedCandidates.LastOrDefault(e => candidates.IndexOf(e) < firstReachable);
            var routed = behind ?? routedCandidates.FirstOrDefault(e => candidates.IndexOf(e) > firstReachable);
            if (routed != null)
                return new ExitChoice(routed, NextAfter(routedCandidates, routed), ComfortablyReachable: false)
                {
                    ReachableExitsHaveNoRoute = true, BriefedExitBehindReachable = behind != null,
                };
            chosen = preferred[0];
        }

        return new ExitChoice(chosen, NextAfter(candidates, chosen), ComfortablyReachable: true) { StandKnown = route != null };
    }

    /// <summary>The exit to take if <paramref name="chosen"/> is missed, among <paramref name="exits"/> — the rule
    /// <see cref="Pick"/> applies: the next candidate on the same side at least <see cref="NextExitMinSeparationFeet"/>
    /// further along. For a choice already made, when only the exits after it were looked for again.</summary>
    public static LandingExit? NextExit(IReadOnlyList<LandingExit> exits, LandingExit chosen) =>
        exits == null || exits.Count == 0 ? null : NextAfter(Candidates(exits), chosen);

    /// <summary>The exits that may be briefed, in runway order: those that vacate the runway and turn no more than
    /// <see cref="RolloutExitGate.MaxUsableExitTurnDeg"/>, or — when none does — every one that vacates it.</summary>
    private static List<LandingExit> Candidates(IReadOnlyList<LandingExit> exits)
    {
        var candidates = exits
            .Where(e => e.VacatesRunway && e.ExitAngleDegrees <= RolloutExitGate.MaxUsableExitTurnDeg)
            .OrderBy(e => e.DistanceFromThresholdFeet)
            .ToList();
        return candidates.Count > 0 ? candidates
            : exits.Where(e => e.VacatesRunway).OrderBy(e => e.DistanceFromThresholdFeet).ToList();
    }

    private static LandingExit? NextAfter(List<LandingExit> candidates, LandingExit chosen) =>
        candidates.FirstOrDefault(e =>
            e.DistanceFromThresholdFeet - chosen.DistanceFromThresholdFeet >= NextExitMinSeparationFeet &&
            OnTheSameSide(e, chosen));

    /// <summary>Whether the aircraft can slow to the exit's turn-off speed before it with comfortable braking from its
    /// typical touchdown speed — the touchdown re-plan's own rule (<see cref="RolloutExitGate.ComfortableExitLeadFeet"/>),
    /// measured from <paramref name="aimFeet"/> past the threshold rather than <see cref="LandingExit.DistanceFromTouchdownFeet"/>,
    /// which is always the fixed jet aim point.</summary>
    public static bool IsComfortablyReachable(LandingExit e, double touchdownSpeedKts, double aimFeet = JetAimPointFeet) =>
        e.DistanceFromThresholdFeet - aimFeet >= RolloutExitGate.ComfortableExitLeadFeet(touchdownSpeedKts, e.ExitAngleDegrees);

    /// <summary>Whether <paramref name="e"/> leaves the runway on <paramref name="chosen"/>'s side. A chosen exit of
    /// unknown side constrains nothing.</summary>
    private static bool OnTheSameSide(LandingExit e, LandingExit chosen) =>
        string.IsNullOrEmpty(chosen.ExitSide) ||
        string.Equals(e.ExitSide, chosen.ExitSide, StringComparison.OrdinalIgnoreCase);

    private static bool IsHighSpeed(LandingExit e) =>
        string.Equals(e.ExitType, "High-speed", StringComparison.OrdinalIgnoreCase);
}

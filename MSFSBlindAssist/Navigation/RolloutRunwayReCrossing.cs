using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// "Does this landing-exit handoff route drive back across the runway we just landed on?"
///
/// <para>Motivating defect (KATL 26R, 2026-08-27). The aircraft rolled past its planned
/// exit B1 (south side) without turning, so the overshoot monitor retargeted to the only
/// remaining exit, A at 8,843 ft — which leaves the runway on the NORTH side. The taxi
/// graph holds no runway edges, so A* cannot route along the runway to A's junction; it
/// routed B1 south, west along B, then north on H across the 08L threshold, 15-20 m inside
/// the runway's own threshold at 22 kt. 427 m and a 180 degree arc, which the pilot heard
/// as "very windy and curvy", and which left them on the wrong side of the field.</para>
///
/// <para><see cref="RolloutExitGate.IsHandoffRouteReachable"/> cannot catch this: it
/// measures only the FIRST segment's cross-track, and B1 started right at the aircraft.
/// Commit 425217ca records the same limitation from the other direction.</para>
///
/// <para>Pure (segments + a centerline in, bool out) so the rule is unit-testable without
/// a graph or a live position.</para>
/// </summary>
public static class RolloutRunwayReCrossing
{
    /// <summary>
    /// The graph centerline for the runway just landed on, or null when the graph does not
    /// carry it. Matched on EITHER designator through
    /// <see cref="RouteRunwayCrossings.NormalizeDesignator"/> — 26R and 08L are one piece
    /// of pavement, which is exactly the semantics wanted here, and the normalizer also
    /// makes "8L" and "08L" the same string.
    /// </summary>
    public static TaxiGraph.RunwayCenterline? FindLandingRunwayCenterline(
        IReadOnlyList<TaxiGraph.RunwayCenterline>? centerlines, string? runwayId)
    {
        if (centerlines is null || string.IsNullOrWhiteSpace(runwayId)) return null;
        string want = RouteRunwayCrossings.NormalizeDesignator(runwayId);
        foreach (var c in centerlines)
        {
            if (string.Equals(RouteRunwayCrossings.NormalizeDesignator(c.Name1), want, StringComparison.OrdinalIgnoreCase)
                || string.Equals(RouteRunwayCrossings.NormalizeDesignator(c.Name2), want, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }

    /// <summary>
    /// True when any segment from <paramref name="fromSegmentIndex"/> onward crosses the
    /// runway's centerline between its thresholds.
    ///
    /// <para>Uses <see cref="TaxiGraph.EdgeCrossesRunwayStatic"/> — a segment-vs-segment
    /// intersection, NOT a point-on-pavement test. The point test silently missed every
    /// crossing whose flanking nodes sit more than half-width + 5 m out (KBOS 33L via K/B/C,
    /// docs/taxi-guidance.md), which is most of them.</para>
    ///
    /// <para>Judged from <paramref name="fromSegmentIndex"/> because that is the segment
    /// the tone is about to steer at — a crossing already behind the aircraft is history,
    /// not a route it is about to fly.</para>
    /// </summary>
    public static bool RouteReCrossesRunway(
        IReadOnlyList<TaxiRouteSegment>? segments,
        int fromSegmentIndex,
        TaxiGraph.RunwayCenterline? runway)
    {
        if (segments is null || runway is null) return false;
        if (fromSegmentIndex < 0 || fromSegmentIndex >= segments.Count) return false;

        for (int i = fromSegmentIndex; i < segments.Count; i++)
        {
            var s = segments[i];
            if (s?.FromNode is null || s.ToNode is null) continue;
            if (TaxiGraph.EdgeCrossesRunwayStatic(
                    s.FromNode.Latitude, s.FromNode.Longitude,
                    s.ToNode.Latitude, s.ToNode.Longitude,
                    runway.Lat1, runway.Lon1, runway.Lat2, runway.Lon2))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The one thing a pilot is told when this rule declines a handoff: the exit is still
    /// AHEAD, so keep rolling to it.
    ///
    /// <para>Why it exists (PR review, 2026-08-27). The decline stays in LandingRollout and
    /// speaks nothing, on the reasoning that the rollout tone is a live cue. That only holds
    /// inside <see cref="RolloutExitGate.ExitToneArmFeet"/>. Further out
    /// <see cref="RolloutExitGate.SelectToneMode"/> has two states that produce no sound at
    /// all for an aircraft sitting still and aligned: the 300–1,000 ft turn-window
    /// <see cref="RolloutToneMode.Silent"/>, and a <see cref="RolloutToneMode.DriftCorrection"/>
    /// under <see cref="RolloutExitGate.DriftToneSilentDeg"/> of heading error, which is zero
    /// volume. And <c>trulyStopped</c> carries no distance gate, so a pilot who brakes to a
    /// stop 1,500 ft short of the exit could sit in the decline loop indefinitely with no
    /// tone and no words, stationary on an active runway.</para>
    ///
    /// <para>Three wording constraints, all safety-bearing. It must NOT claim the aircraft is
    /// clear of the runway (it is not). It must NOT say "stop" or "hold" — the other
    /// landing-exit closures do, and that wording is only safe off the pavement. And it must
    /// carry BOTH the exit name and the distance, because those are the two things a blind
    /// pilot needs to act. Shape follows the neighbouring rollout callouts
    /// ("Missed X. Retargeting taxiway Y, 400 feet ahead.").</para>
    /// </summary>
    /// <param name="taxiwayName">The exit's taxiway name; null/blank renders as "the exit".</param>
    /// <param name="distanceAheadFeet">
    /// Straight-line feet to the exit node — the same quantity the 1500/900/500 ft approach
    /// callouts use, so the number is calibrated against what the pilot has already heard.
    /// Zero or less drops the distance clause rather than announcing "0 feet"; so does any
    /// positive input that <see cref="Services.DistanceFormatter.FromFeet"/> itself rounds
    /// down to zero (feet mode rounds to the nearest 25 ft below 200 ft, so anything under
    /// ~12.5 ft, or ~2.5 m in metres mode, still renders "0 feet"/"0 metres" despite passing
    /// the raw &lt;= 0.0 check — reviewer-confirmed, e.g. 8 ft and 10 ft both produced
    /// "0 feet ahead").
    /// </param>
    public static string ComposeContinueToExit(string? taxiwayName, double distanceAheadFeet)
    {
        string exit = string.IsNullOrWhiteSpace(taxiwayName)
            ? "the exit"
            : $"taxiway {taxiwayName.Trim()}";
        if (distanceAheadFeet <= 0.0)
            return $"Continue rolling to {exit}.";
        string dist = Services.DistanceFormatter.FromFeet(distanceAheadFeet);
        // Inspect the FORMATTED string rather than duplicating DistanceFormatter's rounding
        // thresholds (25 ft / 5 m step sizes) here as a second magic-number guard: a "0 " lead
        // is true whenever the display would say zero, in either unit, and stays true if those
        // step sizes ever change. The method's own doc promises the distance clause is dropped
        // rather than announcing "0 feet" — this is what makes that hold for every input, not
        // just literal zero.
        if (dist.StartsWith("0 ", StringComparison.Ordinal))
            return $"Continue rolling to {exit}.";
        return $"Continue rolling to {exit}, {dist} ahead.";
    }
}

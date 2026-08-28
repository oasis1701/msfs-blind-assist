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
    // Delegates to the one by-designator centerline matcher rather than keeping a
    // fourth spelling of it beside the three that had already drifted.
    public static TaxiGraph.RunwayCenterline? FindLandingRunwayCenterline(
        IReadOnlyList<TaxiGraph.RunwayCenterline>? centerlines, string? runwayId)
        => RouteRunwayCrossings.FindCenterlineForDesignator(centerlines, runwayId);

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

    /// <summary>
    /// The declining frame's ONE utterance: <see cref="ComposeContinueToExit"/> plus the only
    /// content the rollout callouts it retires would have added that the instruction does not
    /// already carry.
    ///
    /// <para>Why it is one utterance and not two announcements (PR review, 2026-08-27). The
    /// decline speaks through <c>AnnounceInstruction</c>, i.e. <c>AnnounceImmediate</c>, and
    /// then RETURNS — before the approach/turn-now callout block, so none of that block's
    /// latches are set. On the very next frame (~16 ms) the crossing retry floor skips the
    /// handoff block, execution reaches the callouts, and one of them fires its own
    /// <c>AnnounceImmediate</c>, truncating a sentence that is one-shot and therefore
    /// unrecoverable. It is not a coincidence either: <c>speedNearExitHandoff</c> requires
    /// <c>distToExitFeet &lt; ROLLOUT_NEAR_EXIT_FT</c> (500 ft) and the 500 ft approach
    /// milestone triggers on that same boundary, so on any rollout already at taxi speed at
    /// 500 ft — the live 22 kt KATL trace included — both are true on ONE frame.</para>
    ///
    /// <para>This is the sixth instance of this codebase's two-announcements-stomp-each-other
    /// pattern, and every previous resolution (<c>4837e45d</c>, <c>6891c0e7</c>,
    /// <c>86744893</c>, <c>b772e845</c>, <c>c2b69455</c>) landed on the same remedy: compose
    /// ONE utterance rather than let two race. The alternative shapes were both worse. Setting
    /// the approach latch and saying nothing else throws away the <i>"Slow down."</i> advice
    /// and the turn direction. Suppressing the decline and letting the callout carry it throws
    /// away the instruction itself — <i>"Taxiway A, 500 feet. Slow down."</i> tells a pilot who
    /// has braked to a stop on an active runway neither that the exit is still ahead nor to
    /// keep rolling to it, which is the entire state this announcement exists to end.</para>
    /// </summary>
    /// <param name="slowDown">
    /// The 500 ft cue's own <i>"Slow down."</i> suffix, when that cue is being retired and its
    /// conditions hold (not a high-speed exit, above taxi speed). Nothing else of that cue is
    /// folded in: it renders as "{name}, 500 feet.", and the sentence in front of it already
    /// gives the same name with a LIVE distance.
    /// </param>
    /// <param name="turnPhrase">
    /// "Turn left" / "Gentle right" when the turn-now cue is being retired — the one thing it
    /// carries that no distance sentence can. Null or blank adds nothing. It lands LAST
    /// because it is the action and the sentence ahead of it is the action's premise.
    /// </param>
    public static string ComposeDeclineUtterance(
        string? taxiwayName, double distanceAheadFeet, bool slowDown, string? turnPhrase)
    {
        string s = ComposeContinueToExit(taxiwayName, distanceAheadFeet);
        if (slowDown) s += " Slow down.";
        if (!string.IsNullOrWhiteSpace(turnPhrase)) s += $" {turnPhrase.Trim()} now.";
        return s;
    }

    /// <summary>Feet per second in one knot. Matches <c>GroundTrafficMonitor</c>'s own.</summary>
    private const double FeetPerSecondPerKnot = 1.6878;

    /// <summary>
    /// True when a rollout callout armed at <paramref name="calloutTriggerFeet"/> is superseded
    /// by a crossing-decline utterance spoken at <paramref name="distanceAheadFeet"/> — i.e.
    /// the caller should mark it announced and fold whatever it uniquely adds into that one
    /// utterance instead of letting it fire on its own.
    ///
    /// <para>TWO halves, and both are needed. "Already inside" (<c>distance &lt;= trigger</c>)
    /// is what closes the structural 500 ft collision described on
    /// <see cref="ComposeDeclineUtterance"/>. The speed-derived lead closes the band just ABOVE
    /// a trigger, where the callout is a frame or two away and would truncate the sentence just
    /// as completely — a <c>trulyStopped</c> or <c>turnBegun</c> decline at 1,501 ft reaches the
    /// 1,500 ft milestone in a fifth of a second.</para>
    ///
    /// <para>The lead is a TIME, converted to distance at the aircraft's own ground speed,
    /// rather than a tuned distance constant: it then self-scales across the 0–90 kt range the
    /// decline branch actually spans (<c>RolloutExitGate.IsExitTurnBegun</c> permits up to
    /// 90 kt) and it degrades correctly at rest — a stopped aircraft can never reach the next
    /// trigger, so it supersedes nothing ahead of it and keeps its whole countdown. Getting the
    /// time slightly wrong is mild in both directions: too short returns a narrow band to
    /// today's behaviour, too long retires a distance restatement marginally early. It is NOT
    /// the kind of speech-duration estimate CLAUDE.md forbids — nothing here mutes speech.</para>
    ///
    /// <para>Deliberately NOT applied to the turn-now cue's own trigger by the caller: "now" is
    /// time-critical, and retiring it a lead-window early would speak it hundreds of feet out
    /// at rollout speed. See the call site for the truncation that asymmetry accepts.</para>
    /// </summary>
    public static bool DeclineSupersedesCallout(
        double distanceAheadFeet, double calloutTriggerFeet,
        double groundSpeedKts, double leadSeconds)
    {
        if (distanceAheadFeet <= calloutTriggerFeet) return true;
        if (groundSpeedKts <= 0.0 || leadSeconds <= 0.0) return false;
        return distanceAheadFeet - calloutTriggerFeet
            <= groundSpeedKts * FeetPerSecondPerKnot * leadSeconds;
    }
}

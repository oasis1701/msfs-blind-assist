using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Composes the spoken callout for a route the off-route detector RECALCULATED mid-taxi.
///
/// <para>The callout has always named the new taxiway sequence — a recalc can trim or replace
/// the entered clearance, and the old generic "Recalculating…" never said so (PHNL 2026-06-13:
/// "Z A L N Z D" silently became "Z D"). It did NOT name the runways the new route crosses,
/// even though <c>LoadRoute</c>'s route summary does. That clause exists because at KSFO
/// (2026-07-01) the only route onto Q re-crossed 28R twice, and a pilot who heard two
/// unexplained "hold short of runway 10L" callouts perceived a giant loop and doubted
/// guidance that was in fact correct. A recalculated route is the case where the crossings
/// are MOST likely to have changed and the pilot is LEAST likely to expect it, so the same
/// clause belongs here.</para>
///
/// <para>Pure (strings and segments in, one sentence out) so the exclusion rule it applies
/// through <see cref="RouteRunwayCrossings.ShouldExcludeFinalHold"/> — the subtle half — is
/// pinned by unit tests rather than only in the sim.</para>
/// </summary>
public static class RouteChangedCallout
{
    /// <summary>
    /// The full sentence, e.g. <c>"Route changed. Now via D, crossing runways 26R and 04L.
    /// 2.6 kilometres to Runway 04R."</c> The crossing clause is omitted entirely when the
    /// route crosses no runway, leaving the wording byte-identical to what it has always been.
    /// It sits AHEAD of the distance and never after the destination — see the comment on the
    /// composition below for the two reasons, both of which are safety ones.
    /// </summary>
    /// <param name="viaNames">Distinct consecutive taxiway names of the new route, in order.
    /// Empty yields the short form with no "Now via" clause.</param>
    /// <param name="distanceText">Already formatted in the pilot's active unit by the caller —
    /// <c>DistanceFormatter</c> is a display layer and must not be reached from pure logic.</param>
    /// <param name="isRunwayDestination">Whether the route ends at a runway. For a runway
    /// route <c>TruncateToHoldShort</c> tags the FINAL segment purely as the countdown rail
    /// for the destination's own hold-short; that is not an ATC crossing, and announcing it
    /// would tell the pilot they cross the runway they are taxiing to. A gate route has no
    /// such pass, so a hold-short on its final segment IS a real crossing and is named.
    /// Same exclusion <c>LoadRoute</c>'s summary applies.</param>
    public static string Compose(
        IReadOnlyList<string> viaNames,
        string distanceText,
        string destinationName,
        IReadOnlyList<TaxiRouteSegment> segments,
        bool isRunwayDestination)
    {
        // Only the crossing clause is used. The non-runway hold-short count is deliberately
        // dropped: a recalculated route carries none, because the recalc does not re-apply the
        // pilot's per-row hold-short picks at all (those bind to a taxiway-sequence index the
        // recalc legitimately rewrites). Speaking "0 hold short points" — or a count that
        // could only ever be zero — would be noise.
        var (crossingClause, _) = RouteRunwayCrossings.Describe(
            segments, RouteRunwayCrossings.ShouldExcludeFinalHold(segments, isRunwayDestination));

        // The crossing clause rides with the TAXIWAY list, ahead of the distance, and is NOT
        // appended after the destination. Two reasons, both real:
        //   - A destination name can contain commas of its own. ParkingSpot.Describe() appends
        //     the terminal and any online alias ("A 24A - Gate Medium, also A24 (online)"), so
        //     ", crossing runway 09L" tacked on after it reads as one more item in the name.
        //   - This is spoken through AnnounceInstruction, i.e. AnnounceImmediate, and the
        //     caller has just reset every announce latch — so the next position frame's
        //     turn/approach callout can cut this sentence off. Truncation takes the END, and
        //     the runway-safety clause must not be what is lost. The distance can be.
        // ⚠ Ordering alone does NOT save the clause from an AnnounceImmediate one frame later:
        // that takes essentially the whole sentence, not its end. The one caller that could do
        // so is the runway-incursion callout, and it is held off separately — the call site
        // stamps _lastIncursionWarningTime when it re-arms _lastIncursionWarnedNodeId, so the
        // cooldown covers this sentence. Ordering is what protects the clause from the
        // turn/approach callouts, which are latched rather than immediate.
        // With no taxiway list the clause attaches to "Route changed" instead. It must NEVER
        // become the standalone sentence "Crossing runway 26R." — that is byte-identical to
        // the runway-incursion callout in TaxiGuidanceManager ("Crossing {rwy}."), which means
        // "you are crossing that runway NOW". The pilot would hear one sentence twice within
        // seconds meaning two different things. Keeping the clause lower-case and attached is
        // also why nothing here has to capitalise it.
        string lead = viaNames.Count > 0
            ? $"Route changed. Now via {string.Join(", ", viaNames)}"
            : "Route changed";
        string crossings = crossingClause.Length > 0 ? $", {crossingClause}" : "";
        return $"{lead}{crossings}. {distanceText} to {destinationName}.";
    }
}

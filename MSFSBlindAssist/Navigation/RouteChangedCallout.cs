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
/// <para>Pure (strings and segments in, one sentence out) so the <c>excludeLastSegment</c>
/// rule below — the subtle half — is pinned by unit tests rather than only in the sim.</para>
/// </summary>
public static class RouteChangedCallout
{
    /// <summary>
    /// The full sentence, e.g. <c>"Route changed. Now via D. 2.6 kilometres to Runway 04R,
    /// crossing runways 26R and 04L."</c> The crossing clause is omitted entirely when the
    /// route crosses no runway, leaving the wording byte-identical to what it has always been.
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
        bool excludeLastHold = isRunwayDestination && segments.Count > 0 &&
            segments[^1].IsHoldShortPoint;

        // Only the crossing clause is used. The non-runway hold-short count is deliberately
        // dropped: a recalculated route carries none, because the recalc does not re-apply the
        // pilot's per-row hold-short picks at all (those bind to a taxiway-sequence index the
        // recalc legitimately rewrites). Speaking "0 hold short points" — or a count that
        // could only ever be zero — would be noise.
        var (crossingClause, _) = RouteRunwayCrossings.Describe(segments, excludeLastHold);

        string via = viaNames.Count > 0 ? $" Now via {string.Join(", ", viaNames)}." : "";
        string crossings = crossingClause.Length > 0 ? $", {crossingClause}" : "";
        return $"Route changed.{via} {distanceText} to {destinationName}{crossings}.";
    }
}

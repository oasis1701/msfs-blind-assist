namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The sentences of the "ask before switching to a recalculated route" option
/// (<c>UserSettings.TaxiGuidanceConfirmReroute</c>). With it on, a mid-taxi recalculation the
/// off-route detector produces is OFFERED, not adopted: guidance keeps steering to the route the
/// pilot was cleared for until the pilot presses Continue ([ Y) to take the new one.
///
/// <para>Why it exists (LFPG 2026-11-05, B, QB9, Q, E, TB2, G3 to B13): a slow, wide hairpin
/// left the aircraft outside the route line for the 3 s persistence window, so the detector
/// recalculated just as the aircraft finished the turn the tone had asked for, and the adopted
/// route opened with a 180° reversal. On VATSIM the pilot was following the controller's
/// clearance correctly; a route the pilot did not ask for is not one ATC gave.</para>
///
/// <para>The offer names the taxiways and the distance, never the runways the new route crosses:
/// those are only known once the route is adopted (its crossing holds are placed then), and the
/// "Route changed" callout spoken on acceptance names them (<see cref="RouteChangedCallout"/>).</para>
///
/// <para>Pure (values in, one sentence or verdict out) so the wording and the three decisions the
/// manager takes about a standing offer (withdraw it, let the key take it, what Repeat-last says
/// once it is gone) are pinned by unit tests.</para>
/// </summary>
public static class RerouteOffer
{
    /// <summary>How the pilot takes the offer, spoken the way the app names keys elsewhere.</summary>
    public const string AcceptHint = "Press left bracket, Y to accept.";

    /// <summary>Spoken when an accepted offer finds no route to switch to (the recalculation from the
    /// position at the press reproduces the route being flown, or is suppressed near the
    /// destination) and nothing else was said. Guidance carries on unchanged.</summary>
    public const string NothingToAccept = "No new route. Continuing on original route.";

    /// <summary>
    /// The offer, e.g. <c>"Off route. New route available via Q, E. 1.2 kilometres to B 13.
    /// Press left bracket, Y to accept."</c> It leads with "Off route" so the frame composer
    /// orders it with the other safety lines, and it never starts with "Route changed": that is
    /// the sentence for a route that HAS changed.
    /// </summary>
    /// <param name="viaNames">Distinct consecutive taxiway names of the offered route, in order.
    /// Empty yields the form with no "via" clause.</param>
    /// <param name="distanceText">Already formatted in the pilot's active unit by the caller.</param>
    /// <param name="notes">Whole sentences that are true NOW, whether or not the offer is taken: a
    /// Turn pick the aircraft has missed (<c>TurnPickMessages.Missed</c>). They go before the key
    /// hint, so the hint stays the last thing heard. Never a sentence about the offered route
    /// alone (a pick it "could not keep"): that is said by "Route changed" if it is taken.</param>
    public static string Compose(IReadOnlyList<string> viaNames, string distanceText, string destinationName,
        IReadOnlyList<string>? notes = null)
    {
        string lead = viaNames.Count > 0
            ? $"Off route. New route available via {string.Join(", ", viaNames)}"
            : "Off route. New route available";
        string noteText = notes is { Count: > 0 } ? string.Join(" ", notes) + " " : "";
        return $"{lead}. {distanceText} to {destinationName}. {noteText}{AcceptHint}";
    }

    /// <summary>Appended to the taxi status (] Y) while an offer stands, so an offer the pilot
    /// missed can still be found.</summary>
    public static string StatusSuffix => $" New route offered. {AcceptHint}";

    /// <summary>
    /// True when the excursion that raised an offer is over, so the offer is withdrawn: the aircraft
    /// is back within the route's own tolerance AND neither of the off-route detector's other two
    /// triggers still holds. Being inside the tolerance is not enough on its own: the detector also
    /// fires on a wrong-way roll from only 10 m behind the segment's start
    /// (<c>OFF_ROUTE_BEHIND_START_M</c>), well inside the 25 m tolerance, so an offer raised there
    /// was withdrawn on the very next frame, the key did nothing, and the offer was spoken again
    /// after the cooldown.
    /// </summary>
    public static bool BackOnRoute(bool withinRouteTolerance, bool farBehindStart, bool goingBackward)
        => withinRouteTolerance && !farBehindStart && !goingBackward;

    /// <summary>
    /// True when Continue may take a standing reroute offer. A paired-runway crossing offer that
    /// still stands keeps the key until its window closes (it is a runway clearance); once the
    /// pilot has taken it, or none was made, the key is free at once. Testing the window's end
    /// alone left a press unanswered for the rest of the window after the paired offer was taken.
    /// </summary>
    public static bool KeyIsFree(bool pairedCrossingOfferStands, DateTime now, DateTime pairedCrossingWindowUntil)
        => !pairedCrossingOfferStands || now > pairedCrossingWindowUntil;

    /// <summary>
    /// What Repeat-last (Ctrl+Y) holds once an offer is gone: if it still holds the offer, the
    /// instruction it replaced, so the pilot is never told to press a key for an offer that no
    /// longer stands; anything spoken since the offer is kept.
    /// </summary>
    public static string InstructionAfterOffer(string lastInstruction, string? offerText, string instructionBeforeOffer)
        => offerText != null && string.Equals(lastInstruction, offerText, StringComparison.Ordinal)
            ? instructionBeforeOffer
            : lastInstruction;
}

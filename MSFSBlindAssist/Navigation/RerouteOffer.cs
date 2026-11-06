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
/// <para>Pure (strings in, one sentence out) so the wording is pinned by unit tests.</para>
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
    public static string Compose(IReadOnlyList<string> viaNames, string distanceText, string destinationName)
    {
        string lead = viaNames.Count > 0
            ? $"Off route. New route available via {string.Join(", ", viaNames)}"
            : "Off route. New route available";
        return $"{lead}. {distanceText} to {destinationName}. {AcceptHint}";
    }

    /// <summary>Appended to the taxi status (] Y) while an offer stands, so an offer the pilot
    /// missed can still be found.</summary>
    public static string StatusSuffix => $" New route offered. {AcceptHint}";
}

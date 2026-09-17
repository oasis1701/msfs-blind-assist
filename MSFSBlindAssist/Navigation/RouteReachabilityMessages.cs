namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The one owner of the spoken wording for route reachability (see <see cref="RouteReachability"/>).
/// Every sentence lives here so the load-time and recalculation paths can never phrase the same
/// condition two ways, and so the exact text is pinned by tests.
///
/// <para>Distances arrive already formatted by the caller's ground-distance formatter, so the
/// wording follows the pilot's metres/feet setting without this class reading any settings.</para>
/// </summary>
public static class RouteReachabilityMessages
{
    /// <summary>
    /// The name a sentence speaks for a destination: the taxi form passes its whole dropdown label
    /// ("B 18R - Gate Heavy, Jetway", "A 24A - Gate Medium, also A24 (online)"), and only the part before
    /// the first spaced dash is the stand's identifier. A bare hyphen is part of the name ("A-9").
    /// </summary>
    public static string SpokenDestinationName(string destinationName)
    {
        int dash = destinationName.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? destinationName[..dash].Trim() : destinationName.Trim();
    }

    /// <summary>The destination is on a piece of taxi network the aircraft is not on.</summary>
    public static string DestinationNotConnected(string destinationName) =>
        $"No taxi route to {SpokenDestinationName(destinationName)}. It isn't connected to the taxiway network you're on.";

    /// <summary>The aircraft is on a disconnected piece of network, and the straight way onto the
    /// main network crosses a runway.</summary>
    public static string FirstLegCrossesRunway(string runwayDesignator) =>
        $"No taxi route from here. You aren't on the connected taxiway network, and the way onto it crosses runway {runwayDesignator}.";

    /// <summary>The destination is on a piece of taxi network the aircraft is not on, and the straight
    /// unmapped way to it crosses a runway.</summary>
    public static string DestinationLegCrossesRunway(string destinationName, string runwayDesignator) =>
        $"No taxi route to {SpokenDestinationName(destinationName)}. It isn't connected to the taxiway network you're on, and the way to it crosses runway {runwayDesignator}.";

    /// <summary>The route to a destination on a piece of taxi network the aircraft is not on starts with a
    /// straight unmapped leg.</summary>
    public static string UnmappedLegToDestination(
        string destinationName, double gapMeters, Func<double, string> formatDistance) =>
        $"{SpokenDestinationName(destinationName)} isn't connected to the taxiway network you're on. The first {formatDistance(gapMeters)} of the route aren't mapped.";

    /// <summary>The route starts with a straight unmapped leg from a disconnected position.</summary>
    public static string UnmappedFirstLeg(
        double gapMeters, Func<double, string> formatDistance, string? firstTaxiwayName)
    {
        string distance = formatDistance(gapMeters);
        return string.IsNullOrWhiteSpace(firstTaxiwayName)
            ? $"Your position isn't connected to the taxiway network. The first {distance} of the route aren't mapped."
            : $"Your position isn't connected to the taxiway network. The first {distance} to taxiway {firstTaxiwayName} aren't mapped.";
    }

    /// <summary>A recalculation refused because the destination is not connected.</summary>
    public static string RecalculationRefusedDestination(string destinationName) =>
        $"Off route. Unable to recalculate. {SpokenDestinationName(destinationName)} isn't connected to the taxiway network you're on.";

    /// <summary>A recalculation refused because the way onto the main network crosses a runway.</summary>
    public static string RecalculationRefusedRunway(string runwayDesignator) =>
        $"Off route. Unable to recalculate. The way onto the connected taxiway network crosses runway {runwayDesignator}.";

    /// <summary>A recalculation refused because the straight unmapped way to a destination on another
    /// piece of network crosses a runway.</summary>
    public static string RecalculationRefusedDestinationRunway(string destinationName, string runwayDesignator) =>
        $"Off route. Unable to recalculate. The way to {SpokenDestinationName(destinationName)} crosses runway {runwayDesignator}.";

    /// <summary>
    /// The straight way crosses a runway whose centerline carries no designator at either end.
    /// SegmentTouchesPavement still reports the crossing — the geometry is real, and a bridge or
    /// unmapped leg over it must still be refused — but with an empty designator, and a sentence
    /// built around that would have a hole where the runway name belongs ("...crosses runway.").
    /// Every crosses-runway refusal in this class falls back to this generic wording instead,
    /// whichever context (a first leg, a destination leg, or a recalculation) hit it.
    /// </summary>
    public static string CrossesUnnamedRunway() =>
        "No taxi route. The way crosses a runway that isn't named in this database.";

    /// <summary>
    /// One utterance for the start of guidance: the unmapped-leg warning first, then the route-start
    /// turn cue, so the direction to turn is the last thing heard. Null when there is nothing to say.
    /// Consecutive announcements stomp each other in this app; never speak the two separately.
    /// </summary>
    public static string? JoinStartSpeech(string? unmappedStartWarning, string? turnCue)
    {
        bool hasWarning = !string.IsNullOrEmpty(unmappedStartWarning);
        bool hasCue = !string.IsNullOrEmpty(turnCue);
        if (hasWarning && hasCue) return unmappedStartWarning + " " + turnCue;
        if (hasWarning) return unmappedStartWarning;
        return hasCue ? turnCue : null;
    }
}

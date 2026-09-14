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
    /// <summary>The destination is on a piece of taxi network the aircraft is not on.</summary>
    public static string DestinationNotConnected(string destinationName) =>
        $"No taxi route to {destinationName}. It isn't connected to the taxiway network you're on.";

    /// <summary>The aircraft is on a disconnected piece of network, and the straight way onto the
    /// main network crosses a runway.</summary>
    public static string FirstLegCrossesRunway(string runwayDesignator) =>
        $"No taxi route from here. You aren't on the connected taxiway network, and the way onto it crosses runway {runwayDesignator}.";

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
        $"Off route. Unable to recalculate. {destinationName} isn't connected to the taxiway network you're on.";

    /// <summary>A recalculation refused because the way onto the main network crosses a runway.</summary>
    public static string RecalculationRefusedRunway(string runwayDesignator) =>
        $"Off route. Unable to recalculate. The way onto the connected taxiway network crosses runway {runwayDesignator}.";

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

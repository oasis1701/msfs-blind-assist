namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Whether a route-reachability RECALCULATION refusal needs to be spoken, or is the same
/// refusal already spoken since the route was loaded (or since the destination/reason last
/// changed).
///
/// <para>PR #238 review follow-up. <c>TaxiGuidanceManager.TryRecalculateRoute</c>'s two
/// reachability refusals (destination not connected with no start node in range; the way
/// onto/to the network crosses a runway) are spoken through
/// <c>_announcer.AnnounceImmediate</c>, which INTERRUPTS. The off-route detector re-runs
/// <c>TryRecalculateRoute</c> every <c>RECALCULATION_COOLDOWN_SEC</c> (15 s) for as long as
/// the aircraft stays off-route, and a reachability verdict is a standing property of the
/// airport data and the current destination -- it refuses IDENTICALLY every cycle. Without
/// a latch, a pilot stuck off a disconnected destination heard the same ~20-word
/// interrupting sentence every 15 s for the rest of the taxi, cutting off hold-short and
/// runway-crossing callouts each time it fired.</para>
///
/// <para>This gate does not decide WHETHER to refuse -- <see cref="RouteReachability"/> and
/// the recalculation logic still do that every cycle, and the log line documenting the
/// refusal is unconditional. It decides only whether the identical refusal has already been
/// spoken, so the pilot hears it once per distinct refusal rather than once per cooldown
/// tick.</para>
///
/// <para>The key is the same destination name plus the same reachability verdict plus the
/// same runway designator (empty when no runway is involved). A recalculation refusal only
/// ever fires from a small, fixed set of shapes (see the call sites), so this is sufficient
/// to answer "have I already told the pilot exactly this" without needing to compare
/// composed sentences: a different destination, a different verdict (e.g. the aircraft
/// drifted from a merely-disconnected position onto a piece of network that puts the
/// destination itself out of reach), or a different runway all mean the pilot is hearing new
/// information and must be told again.</para>
/// </summary>
public static class ReachabilityRefusalGate
{
    /// <summary>
    /// The identity of a refusal: the same destination refused for the same reason is the
    /// same refusal, however many times the recalculation runs. <paramref
    /// name="runwayDesignator"/> should be the empty string when no runway is named by the
    /// refusal (e.g. the "no start node in range" refusal, which never crosses a runway).
    /// </summary>
    public static string KeyFor(ReachabilityClass verdict, string destinationName, string runwayDesignator) =>
        $"{verdict}|{destinationName}|{runwayDesignator}";

    /// <summary>
    /// True when this refusal has not already been spoken -- <paramref name="lastSpokenKey"/>
    /// is null (nothing spoken yet, e.g. a fresh route) or differs from <paramref
    /// name="currentKey"/>.
    /// </summary>
    public static bool ShouldAnnounce(string? lastSpokenKey, string currentKey) =>
        !string.Equals(lastSpokenKey, currentKey, StringComparison.Ordinal);
}

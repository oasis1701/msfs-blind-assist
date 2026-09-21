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
///
/// <para>PR #238 review, Important 2: this latch must live for the OFF-ROUTE EPISODE it was
/// raised for, never for the whole route. <c>TaxiGuidanceManager</c> clears
/// <c>_lastReachabilityRefusalKey</c> back to null both when the off-route condition ends
/// (the aircraft is back on route) and when a recalculation succeeds -- so a SECOND,
/// unrelated off-route episode later in the same taxi that refuses for the exact same reason
/// is not silently swallowed by a latch this gate itself has no way to expire on its own (it
/// is a pure equality check with no notion of time or position). Composing a key and
/// comparing it here answers only "is this the SAME refusal as last time"; deciding when
/// "last time" should stop counting is the caller's job.</para>
///
/// <para>PR #238 review, Minor 4: <see cref="KeyFor"/>'s original three parameters (verdict,
/// destination, runway) cannot distinguish "no start node found at all" (the first
/// recalculation refusal site, which never has a runway to name) from "a start node was
/// found but the first leg crosses an UNNAMED runway" (the second site's
/// <c>RecalculationRefusedUnnamedRunway</c> branch, which also passes an empty runway
/// designator) when both occur for the same destination under the same reachability class --
/// the two keys would collide and the second, materially different refusal would be silently
/// suppressed as a "repeat" of the first. The optional <paramref name="site"/> tag closes
/// that: the two call sites pass distinct tags, so the keys can never collide even when
/// every other input happens to match.</para>
/// </summary>
public static class ReachabilityRefusalGate
{
    /// <summary>
    /// The identity of a refusal: the same destination refused for the same reason is the
    /// same refusal, however many times the recalculation runs. <paramref
    /// name="runwayDesignator"/> should be the empty string when no runway is named by the
    /// refusal (e.g. the "no start node in range" refusal, which never crosses a runway).
    /// <paramref name="site"/> is an opaque caller-chosen tag distinguishing call sites whose
    /// other three inputs can otherwise coincide (Minor 4) -- defaulted to <c>""</c> so
    /// existing single-site comparisons are unaffected.
    /// </summary>
    public static string KeyFor(
        ReachabilityClass verdict, string destinationName, string runwayDesignator, string site = "") =>
        $"{verdict}|{destinationName}|{runwayDesignator}|{site}";

    /// <summary>
    /// True when this refusal has not already been spoken -- <paramref name="lastSpokenKey"/>
    /// is null (nothing spoken yet, e.g. a fresh route) or differs from <paramref
    /// name="currentKey"/>.
    /// </summary>
    public static bool ShouldAnnounce(string? lastSpokenKey, string currentKey) =>
        !string.Equals(lastSpokenKey, currentKey, StringComparison.Ordinal);
}

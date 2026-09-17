namespace MSFSBlindAssist.Navigation;

/// <summary>
/// Whether a "Taxiway X." change callout may be spoken immediately, must be deferred until
/// the start-warning chatter window (<c>TaxiGuidanceManager._startChatterSuppressUntil</c>)
/// closes, or is a repeat of the taxiway already announced (or already waiting to be).
///
/// <para>PR #238 review follow-up (Task 5 Defect B). Both taxiway-change call sites
/// (<c>AdvanceToNearestSegment</c>, <c>AdvanceSegment</c>) used to speak via a bare
/// <c>AnnounceInstruction</c> with NO window check at all -- unlike the turn,
/// destination-ahead and curve callouts, which all wait for the same window via <see
/// cref="StartWarningChatterGate"/> -- so it interrupted (and lost) the 10.4 s start
/// warning the window exists to protect, contrary to what both CLAUDE.md and
/// docs/taxi-guidance.md claimed the code already did.</para>
///
/// <para>Unlike those three callouts, the taxiway-change callout has no proximity LATCH
/// that clears early and loses the callout for good if held too long (see <see
/// cref="StartWarningChatterGate"/>'s own remarks on that hazard) -- it is simply "the name
/// of the taxiway the aircraft is currently on," which stays true for as long as the
/// aircraft stays on that taxiway. So there is no speed/distance projection needed here,
/// only two much simpler questions: while the window is open, defer instead of speaking;
/// once it closes, speak the deferred name only if it is still the taxiway the aircraft is
/// on -- if the route has since moved on to a different one, the deferred name is stale and
/// must be discarded silently rather than announced late (a wrong taxiway name is worse
/// than a missed announcement; a blind pilot still hears the CURRENT taxiway's own name the
/// next time it changes, or, having stayed on it, needed no further callout at all).</para>
/// </summary>
public static class TaxiwayChangeGate
{
    /// <summary>What to do with a candidate taxiway-change callout.</summary>
    public enum Decision
    {
        /// <summary>Not a real change (empty name, or the same taxiway already announced or
        /// already pending) -- do nothing.</summary>
        Skip,
        /// <summary>The window is closed: speak immediately, exactly as before this fix.</summary>
        SpeakNow,
        /// <summary>The window is open: remember the name and speak it once the window closes,
        /// if it is still current (<see cref="IsStillCurrent"/>).</summary>
        Defer,
    }

    /// <param name="newTaxiwayName">The taxiway name of the segment the aircraft just
    /// advanced onto.</param>
    /// <param name="lastAnnouncedTaxiway">The dedupe field (<c>_lastAnnouncedTaxiway</c>) --
    /// the most recent taxiway name this gate has already decided is worth announcing,
    /// whether it has been spoken yet or is still deferred. Never null; starts as
    /// <c>""</c>, which must never be confused with "already announced empty."</param>
    /// <param name="windowOpen">True while the start-warning chatter window is still open.</param>
    public static Decision Classify(string? newTaxiwayName, string lastAnnouncedTaxiway, bool windowOpen)
    {
        if (string.IsNullOrEmpty(newTaxiwayName)) return Decision.Skip;
        if (newTaxiwayName.Equals(lastAnnouncedTaxiway, StringComparison.OrdinalIgnoreCase)) return Decision.Skip;
        return windowOpen ? Decision.Defer : Decision.SpeakNow;
    }

    /// <summary>
    /// True when a deferred taxiway name is still the one the aircraft is on -- and should be
    /// spoken now that the window has closed -- rather than one the route has since moved past
    /// (e.g. a recalculation replaced the route between the defer and the flush, or a later
    /// advance already spoke a newer name immediately once the window had closed on its own).
    /// A missing pending name, or a missing current one (route gone), is never "still current."
    /// </summary>
    public static bool IsStillCurrent(string? pendingTaxiwayName, string? currentTaxiwayName) =>
        !string.IsNullOrEmpty(pendingTaxiwayName) &&
        !string.IsNullOrEmpty(currentTaxiwayName) &&
        pendingTaxiwayName.Equals(currentTaxiwayName, StringComparison.OrdinalIgnoreCase);
}

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
///
/// <para>PR #238 review follow-up (Important 1 / Minor 1). A first pass at this gate had
/// <c>TaxiGuidanceManager</c> stamp its dedupe field (<c>_lastAnnouncedTaxiway</c>) the
/// instant a name was DECIDED worth announcing -- at <see cref="Decision.Defer"/> time --
/// rather than once it was actually SPOKEN. That meant a deferred name later discarded as
/// stale (the route moved on before the window closed) was still permanently recorded as
/// "announced" even though the pilot never heard it: a route revisiting that same taxiway
/// name later (e.g. a short unnamed connector splitting two same-named segments) then
/// classified as a repeat and was silently skipped forever. <see cref="Classify"/> now takes
/// the CURRENTLY-pending name as a separate input, so a name only ever becomes "announced"
/// (and therefore skippable) once <c>FlushPendingTaxiwayAnnouncement</c> actually speaks it
/// -- the new parameter is what still stops a second advance onto the SAME still-waiting
/// name from re-deferring a redundant duplicate, without permanently locking out a name
/// that turned out to never be spoken at all.</para>
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
    /// the most recent taxiway name actually SPOKEN, whether immediately or by a flushed
    /// deferral. Never null; starts as <c>""</c>, which must never be confused with "already
    /// announced empty."</param>
    /// <param name="windowOpen">True while the start-warning chatter window is still open.</param>
    /// <param name="pendingTaxiwayName">The name currently waiting in
    /// <c>_pendingTaxiwayAnnouncement</c>, if any (null when nothing is pending). Distinct
    /// from <paramref name="lastAnnouncedTaxiway"/>: this one has been DECIDED but not yet
    /// SPOKEN. Checked so a second advance onto the same still-pending name is skipped as a
    /// duplicate without that name ever being marked "announced" prematurely.</param>
    public static Decision Classify(
        string? newTaxiwayName, string lastAnnouncedTaxiway, bool windowOpen,
        string? pendingTaxiwayName = null)
    {
        if (string.IsNullOrEmpty(newTaxiwayName)) return Decision.Skip;
        if (newTaxiwayName.Equals(lastAnnouncedTaxiway, StringComparison.OrdinalIgnoreCase)) return Decision.Skip;
        if (!string.IsNullOrEmpty(pendingTaxiwayName) &&
            newTaxiwayName.Equals(pendingTaxiwayName, StringComparison.OrdinalIgnoreCase)) return Decision.Skip;
        return windowOpen ? Decision.Defer : Decision.SpeakNow;
    }

    /// <summary>
    /// True when a deferred taxiway name is still the one the aircraft is on -- and should be
    /// spoken now that the window has closed -- rather than one the route has since moved past
    /// (e.g. a recalculation replaced the route between the defer and the flush, or a later
    /// advance already spoke a newer name immediately once the window had closed on its own).
    /// A missing pending name, or a missing current one (route gone), is never "still current."
    ///
    /// <para>PR #238 review, Minor 6: the second <c>IsNullOrEmpty(currentTaxiwayName)</c> guard
    /// this method used to carry is PROVABLY redundant given the first, for every input, not
    /// merely the ones the current caller happens to supply -- a non-empty
    /// <paramref name="pendingTaxiwayName"/> can never <c>.Equals</c> a null or empty
    /// <paramref name="currentTaxiwayName"/>, so once the first guard passes, the instance
    /// <c>.Equals</c> call alone already implies the second. The first guard stays: without it,
    /// <c>IsStillCurrent(null, null)</c> would read TRUE under a bare <c>string.Equals</c>,
    /// contradicting "a missing pending name... is never still current" above for any future
    /// caller, even though today's one caller (<c>FlushPendingTaxiwayAnnouncement</c>) never
    /// passes a null/empty pending name in practice.</para>
    /// </summary>
    public static bool IsStillCurrent(string? pendingTaxiwayName, string? currentTaxiwayName) =>
        !string.IsNullOrEmpty(pendingTaxiwayName) &&
        pendingTaxiwayName.Equals(currentTaxiwayName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a deferred taxiway name should actually be SPOKEN now that the start-warning
    /// chatter window has closed: it must still be <see cref="IsStillCurrent"/> AND must not
    /// already be <paramref name="lastAnnouncedTaxiway"/> -- i.e. not already spoken by
    /// something OTHER than the deferred-flush path itself in the meantime.
    ///
    /// <para>PR #238 review (Important C, a re-fix of Task 5 Defect B / Important 1).
    /// <c>TaxiGuidanceManager.TryRecalculateRoute</c> can adopt a brand-new route between a
    /// defer and the flush WITHOUT ever going through <c>AnnounceOrDeferTaxiwayChange</c> --
    /// it stamps <c>_lastAnnouncedTaxiway</c> itself, as part of the "Route changed. Now via
    /// ..." sentence it speaks immediately, and a recalculation run from the aircraft's own
    /// position normally starts the new route on the very taxiway the aircraft is already on.
    /// So the new route's CURRENT segment can carry the exact same name as a stale deferral by
    /// simple coincidence of geography, not because nothing has happened since the defer.
    /// <see cref="IsStillCurrent"/> alone cannot tell these apart -- it only asks whether the
    /// pending name still matches the CURRENT segment, and after such a recalculation it now
    /// does, same as it always did for an ordinary unchanged route. Without this extra check
    /// the flush both re-speaks a name the pilot was just told, and, because the flush's own
    /// <c>AnnounceInstruction</c> call is an interrupting <c>AnnounceImmediate</c>, can land
    /// close enough behind the recalculation's own announcement to cut it off mid-sentence --
    /// the one sentence that names which runways the new route crosses.</para>
    ///
    /// <para>The stale-discard case is untouched: a pending name the route has since moved
    /// PAST (a different current taxiway) is still silently dropped by
    /// <see cref="IsStillCurrent"/> regardless of <paramref name="lastAnnouncedTaxiway"/>, and
    /// remains eligible to be announced again later exactly as before (Minor 1) -- this method
    /// only adds a SECOND, narrower reason to stay silent: the name is current, but redundant.</para>
    /// </summary>
    public static bool ShouldSpeakDeferred(
        string? pendingTaxiwayName, string? currentTaxiwayName, string lastAnnouncedTaxiway) =>
        IsStillCurrent(pendingTaxiwayName, currentTaxiwayName) &&
        !string.Equals(pendingTaxiwayName, lastAnnouncedTaxiway, StringComparison.OrdinalIgnoreCase);
}

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// One Continue per runway at a stop that guards more than one.
///
/// <para>PR #238 deferred finding §6, implemented on the owner's ruling. When two runways resolve to
/// the SAME stop the label merges (<c>"runway 09 and runway 01"</c>,
/// <see cref="RouteRunwayCrossings.ComposeSharedLabel"/>) and one segment is tagged — and guidance
/// had exactly one <c>HoldShort</c> state and one <c>ContinuePastHoldShort</c> per stop point, so a
/// single Continue authorised crossing BOTH. This repo states the opposing rule in its own words in
/// <c>TaxiGuidanceManager</c>: explicit crossing clearance is required for EACH runway — controllers
/// issue them one at a time, and an aircraft must have crossed the previous runway before the next
/// crossing clearance is issued.</para>
///
/// <para>The stages are read from the LABEL rather than carried as a second list on the route. The
/// label is already the one place the merge is recorded, it survives every route mutation that
/// preserves the stop (including <c>StripClearedCrossing</c>, which removes the stop outright when
/// the cleared runway is the only one it names), and deriving them keeps the new state to a single
/// index in the manager.</para>
///
/// <para>Measured frequency: 0 of 2,518 sampled fs2024 routes produced a start hold naming more than
/// one runway, and shared segment stops are likewise rare — so this is real but uncommon, and the
/// ordinary single-runway hold must be byte-identical to what it was. <see cref="From"/> returning
/// an empty list is what guarantees that.</para>
/// </summary>
public static class RunwayHoldStages
{
    /// <summary>
    /// The runways a stop guards, in label order, when there is MORE THAN ONE — otherwise empty,
    /// which is the signal to behave exactly as a single-runway hold always has.
    ///
    /// <para>Reciprocal designators are folded: both ends of one pavement are one runway and one
    /// clearance. <see cref="RouteRunwayCrossings.ComposeSharedLabel"/> never composes such a label,
    /// but a kept scenery label could, and asking a pilot to press Continue twice for one runway
    /// would be worse than the defect this closes.</para>
    /// </summary>
    public static IReadOnlyList<string> From(string? holdShortLabel)
    {
        var named = RouteRunwayCrossings.ExtractRunwayDesignators(holdShortLabel);
        if (named.Count < 2) return Array.Empty<string>();

        var stages = new List<string>();
        foreach (string d in named)
        {
            string recip = RouteRunwayCrossings.Reciprocal(d);
            if (stages.Any(s => s.Equals(d, StringComparison.OrdinalIgnoreCase)
                             || s.Equals(recip, StringComparison.OrdinalIgnoreCase)))
                continue;
            stages.Add(d);
        }
        return stages.Count >= 2 ? stages : Array.Empty<string>();
    }

    /// <summary>
    /// The hold sentence for a staged stop: the label names every runway the stop guards, as it
    /// always has, and the Continue prompt names the ONE runway this press authorises.
    ///
    /// <para>The pilot has to know that pressing Continue does not buy them both crossings — that is
    /// the whole point — and the clearance they are waiting for is issued one runway at a time.</para>
    /// </summary>
    public static string ComposeHold(string? holdShortLabel, string firstRunway)
        => string.IsNullOrEmpty(holdShortLabel)
            ? $"Stop. Hold short. Press continue when cleared for runway {firstRunway}."
            : $"Stop. Hold short of {holdShortLabel}. Press continue when cleared for runway {firstRunway}.";

    /// <summary>
    /// The sentence a Continue produces when another runway's clearance is still outstanding: the
    /// aircraft does NOT move, so it must not read like a resume.
    ///
    /// <para>"Still holding" is load-bearing wording: every other Continue in this manager resumes
    /// guidance, and a blind pilot pressing the key has every reason to expect movement. The tone
    /// stays paused for the same reason.</para>
    /// </summary>
    public static string ComposeAdvance(string clearedRunway, string nextRunway)
        => $"Runway {clearedRunway} cleared. Still holding. Press continue when cleared for runway {nextRunway}.";
}

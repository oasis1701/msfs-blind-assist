namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Picks OUR stand out of a <c>gate.select</c> <c>ambiguous</c> reply's candidate list.
///
/// <para>Why this exists: live-probed against a running GSX (KATL, 2026-08-27),
/// <c>gate.select</c> answers only to a stand NUMBER (JSON int) or to a <c>bglName</c>
/// ("Gate T 5"). Every textual identifier GSX publishes per parking — <c>uiGateName</c>,
/// trimmed <c>uiGateName</c>, <c>uiName</c> — returns <c>not_found</c>. <c>bglName</c> is
/// NOT published in <c>handlerData.airport.parkings</c>; it appears there as the string
/// <c>"method"</c> (an unserialised Python accessor). The ambiguity list is the only place
/// a client can obtain one, which is why the number is sent first.</para>
///
/// <para><b>EXACT or nothing.</b> <c>GsxGateSelectAnnouncer</c>'s <c>Ambiguous</c> arm
/// exists to surface that GSX refused to guess. Resolving on a nearest, a first, or a
/// prefix match would replace GSX's refusal with OUR guess — strictly worse, because the
/// pilot would then taxi to a stand nobody chose. Returning null here means the announcer
/// speaks the ambiguity exactly as it does today.</para>
///
/// <para>Pure (lists and strings in, candidate out) so the rule is unit-testable without
/// a socket.</para>
/// </summary>
public static class GsxGateCandidateMatcher
{
    /// <summary>
    /// The single candidate that IS the given stand, or null when the answer is not unique.
    /// </summary>
    /// <param name="candidates">The <c>ambiguous</c> reply's candidate list.</param>
    /// <param name="uiName">The stand's <c>ParkingSpot.GsxUiName</c> — null for a stand GSX publishes none for.</param>
    /// <param name="uiGateName">The stand's <c>ParkingSpot.GsxIdentifier</c>, verbatim (leading space included).</param>
    /// <param name="number">The stand's parsed number.</param>
    public static GsxGateSelectCandidate? Match(
        IReadOnlyList<GsxGateSelectCandidate> candidates,
        string? uiName,
        string? uiGateName,
        int number)
    {
        if (candidates is null || candidates.Count == 0) return null;

        // uiName is the discriminating field: unique for 281 of KATL's 294 stands, where
        // uiGateName is shared by 235 of them. Ordinal and un-trimmed -- GSX's own strings
        // carry leading and trailing spaces and they are part of the value.
        if (!string.IsNullOrEmpty(uiName))
            return Single(candidates, c => string.Equals(c.UiName, uiName, StringComparison.Ordinal));

        // No uiName (KATL's unnamed GA ramps, 13 of 294). Fall back to the pair that GSX
        // does echo, requiring BOTH to agree -- gate alone is shared, number alone is shared.
        //
        // TRIMMED on both sides, unlike the uiName branch above, and the asymmetry is
        // deliberate. The stored uiGateName is GSX's per-parking string, which carries a
        // leading space on 281 of KATL's 294 stands; the guide's own candidate shape pairs a
        // full uiName ("Gate A12") with a BARE gate ("A12"), so the two need not be spelled
        // alike and an un-trimmed compare could match nothing at all -- a branch that reads
        // as live logic while never once firing. Trimming cannot manufacture a wrong answer
        // here: two candidates differing only in whitespace share the number conjunct too,
        // so Single sees both survive and returns null rather than guessing. Case stays
        // significant (see Gate_fallback_case_is_significant).
        if (!string.IsNullOrEmpty(uiGateName))
        {
            string wantGate = uiGateName.Trim();
            return Single(candidates, c =>
                string.Equals(c.Gate?.Trim() ?? string.Empty, wantGate, StringComparison.Ordinal)
                && c.Number.HasValue && c.Number.Value == number);
        }

        return null;
    }

    private static GsxGateSelectCandidate? Single(
        IReadOnlyList<GsxGateSelectCandidate> candidates,
        Func<GsxGateSelectCandidate, bool> predicate)
    {
        GsxGateSelectCandidate? found = null;
        foreach (var c in candidates)
        {
            if (!predicate(c)) continue;
            if (found != null) return null;   // not unique -- let the announcer surface it
            found = c;
        }
        // A candidate we cannot re-send is not a match: bglName is the only thing
        // gate.select will accept back.
        return string.IsNullOrWhiteSpace(found?.BglName) ? null : found;
    }
}

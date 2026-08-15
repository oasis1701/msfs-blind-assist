namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// The ONE rule for "is this spoken phrase worth saying, or is it just the last one
/// with a live counter ticking?" — a general filter for the spammy-announcement
/// problem, so a countdown or progress number embedded in a TEXT phrase is not read
/// aloud every second, WITHOUT enumerating the specific strings that carry one.
///
/// <para>
/// Given the last phrase SPOKEN (for whatever it describes — a GSX message banner, a
/// service's bus phase) and the current one, it answers whether the current one has
/// changed enough to speak again: an empty phrase says nothing; an exact repeat says
/// nothing; a change that is ONLY in STANDALONE runs of digits ("on the way, ETA 15
/// secs" → "… 14 secs", "Pushback in 5 seconds" → "… 4 seconds") is a countdown or
/// counter tick, not news; anything else speaks.
/// </para>
///
/// <para>
/// It is deliberately for TEXT-PHASE announcements only, never for a quantity whose
/// number IS the message (passenger count, bags percent, fuel loaded) — those are
/// milestone- or time-gated by <see cref="GsxServiceAnnouncer"/>, and collapsing their
/// digits would silence the very readings they exist to speak ("10 of 155" → "20 of
/// 155" would look like a tick). A caller applies it to a phase field and lets the
/// quantity gates own the quantities.
/// </para>
///
/// <para>
/// STANDALONE digit runs only — a run glued to a letter is an identifier ("engine 1"
/// is standalone, "B25" is not), so "…gate B25" → "…gate B27" is a reassignment that
/// DOES speak. A heuristic, and broader than the pre-Remote-API NormalizeStatusStableText
/// (which bucketed only hh:mm times, "N seconds/minutes" durations and prices): a
/// letterless "…gate 25" → "…gate 27" or "engine 1" → "engine 2" is silenced when one
/// phrase directly replaces another (a blank/other-words phrase between them rescues it).
/// Accepted for simplicity over a token grammar. Pure; pinned by GsxPhraseGateTests.
/// </para>
/// </summary>
public static class GsxPhraseGate
{
    // Both boundaries exclude digits as well as letters so the run is MAXIMAL: with
    // letters alone the engine matches the "5" of "B25" (preceded by "2", not a letter)
    // and "B25"→"B27" would collapse into a tick after all.
    private static readonly System.Text.RegularExpressions.Regex DigitRun =
        new(@"(?<![A-Za-z0-9])\d+(?![A-Za-z0-9])", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Whether <paramref name="current"/> should be spoken given that
    /// <paramref name="lastSpoken"/> was the last phrase actually spoken (empty when none was).
    /// </summary>
    public static bool ShouldAnnounce(string lastSpoken, string current)
    {
        if (string.IsNullOrWhiteSpace(current)) return false;
        if (string.Equals(lastSpoken, current, System.StringComparison.Ordinal)) return false;
        if (string.IsNullOrWhiteSpace(lastSpoken)) return true;
        return !IsDigitRunOnlyChange(lastSpoken, current);
    }

    /// <summary>True when <paramref name="before"/> and <paramref name="after"/> are identical once
    /// every standalone run of digits is blanked — i.e. they differ only in embedded numbers.</summary>
    public static bool IsDigitRunOnlyChange(string before, string after)
    {
        string[] beforeParts = DigitRun.Split(before);
        string[] afterParts = DigitRun.Split(after);
        if (beforeParts.Length != afterParts.Length) return false;
        for (int i = 0; i < beforeParts.Length; i++)
        {
            if (!string.Equals(beforeParts[i], afterParts[i], System.StringComparison.Ordinal))
                return false;
        }
        return true;
    }
}

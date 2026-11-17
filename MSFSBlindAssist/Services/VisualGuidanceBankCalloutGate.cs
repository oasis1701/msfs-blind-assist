namespace MSFSBlindAssist.Services;

/// <summary>
/// Decides when visual guidance speaks a bank callout ("3 right", "1 left", "matched", "On commanded
/// path"): at once, the update the phrase changes (VG-21). The owner, 2026-11-06, after the first sim
/// flight: "now it waits then tells us the command. before it said it instantly. we need to make it
/// instant again". Stage 3's holds, its one-second gap and its wait for a steady bank are gone.
///
/// With the error e = desired − actual (positive: roll right):
/// <list type="bullet">
/// <item>"Matched" is entered when |e| comes within 0.4° and left only when it reaches 1.0°, a band,
/// not a wait, so an error hovering around half a degree settles on "matched" instead of flipping with
/// "1 right" many times a second. While matched the callout is "Matched, level" when the command is
/// within 0.5° of wings level, else "matched"; the two count as one callout, so switching between them
/// never speaks. ("Matched, level" was "On commanded path" until 2026-11-06: the owner expected
/// "matched", and the mile calls' "on path" is the glide path.)</item>
/// <item>Otherwise the callout is "N left" or "N right", N the error rounded, at least 1.</item>
/// <item>A callout is spoken the update it differs from the last one spoken, never repeated while it
/// stays the same, and only when the caller may speak (the manual-query grace window, VG-19, and the
/// holds of visual guidance's own lines): the first update it may, it speaks the error as it is then.</item>
/// </list>
/// </summary>
internal sealed class VisualGuidanceBankCalloutGate
{
    public const double MatchedEnterDeg = 0.4;
    public const double MatchedLeaveDeg = 1.0;

    // Within this of wings level, a matched command is spoken as "Matched, level".
    private const double OnPathCommandDeg = 0.5;
    private const string MatchedKey = "matched";

    private bool _matched;
    private string? _lastSpokenKey;

    /// <summary>
    /// One update: both banks right-positive (the manager passes <c>StandardBank</c>, VG-7). Returns
    /// the callout to speak now, or null. Always updates the matched state, even when
    /// <paramref name="maySpeak"/> is false.
    /// </summary>
    public string? Next(double desiredBankDeg, double actualBankDeg, bool maySpeak)
    {
        double error = desiredBankDeg - actualBankDeg;
        double size = Math.Abs(error);
        if (_matched ? size >= MatchedLeaveDeg : size <= MatchedEnterDeg)
            _matched = !_matched;

        string key = _matched ? MatchedKey : ErrorPhrase(error);
        if (key == _lastSpokenKey || !maySpeak) return null;

        _lastSpokenKey = key;
        if (!_matched) return key;
        return Math.Abs(desiredBankDeg) < OnPathCommandDeg ? "Matched, level" : "matched";
    }

    /// <summary>Forgets what was spoken and the matched state.</summary>
    public void Reset()
    {
        _matched = false;
        _lastSpokenKey = null;
    }

    private static string ErrorPhrase(double error)
    {
        int n = Math.Max(1, (int)Math.Round(Math.Abs(error), MidpointRounding.AwayFromZero));
        string number = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return error < 0 ? number + " left" : number + " right";
    }
}

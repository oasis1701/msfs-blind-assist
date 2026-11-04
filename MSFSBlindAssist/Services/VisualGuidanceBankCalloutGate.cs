namespace MSFSBlindAssist.Services;

/// <summary>
/// Decides when visual guidance speaks a bank callout ("3 right", "1 left", "matched", "On commanded
/// path"), so callouts never chatter or cut each other off and still answer a gust quickly (VG-21).
///
/// With the error e = desired − actual (positive: roll right):
/// <list type="bullet">
/// <item>"Matched" is entered when |e| comes within 0.4° and left only when it reaches 1.0°. While
/// matched the callout is "On commanded path" when the command is within 0.5° of wings level, else
/// "matched"; the two count as one callout, so switching between them never speaks.</item>
/// <item>Otherwise the callout is "N left" or "N right", N the error rounded, at least 1.</item>
/// <item>A callout is due when it differs from the last one spoken, or is the same error callout
/// ("1 right") and 3 s have passed since it was spoken, so a pilot sitting one or two degrees off
/// keeps hearing it (the owner, 2026-11-04; "matched" is never repeated); and it has held: matched, 0.25 s; a
/// steady error under 1° outside matched, 1.5 s; an error the tones can still show (under the
/// profile's tone bank range), 0.25 s; a larger one, where both tones are pinned and only speech
/// says how far, once the aircraft's bank has stopped changing (no move over 0.5°) for 1 s.</item>
/// <item>A due callout is spoken only at least 1 s after the last bank callout, a fixed gap (VG-1
/// forbids sizing it by speaking time), and only when the caller may speak (the manual-query grace
/// window, VG-19). Nothing is queued: each call decides afresh from the state as it is then.</item>
/// </list>
/// </summary>
internal sealed class VisualGuidanceBankCalloutGate
{
    public const double MatchedEnterDeg = 0.4;
    public const double MatchedLeaveDeg = 1.0;
    public const double SmallHoldSeconds = 0.25;
    public const double SubDegreeHoldSeconds = 1.5;
    public const double LargeSteadySeconds = 1.0;
    public const double BankSteadyBandDeg = 0.5;
    public const double MinGapSeconds = 1.0;
    public const double RepeatSeconds = 3.0;

    // Within this of wings level, a matched command is spoken as "On commanded path".
    private const double OnPathCommandDeg = 0.5;
    private const string MatchedKey = "matched";

    private readonly double _pinnedErrorDeg;

    private bool _matched;
    private string? _candidateKey;
    private DateTime _candidateSince;
    private double? _steadyBank;
    private DateTime _steadySince;
    private string? _lastSpokenKey;
    private DateTime? _lastSpokenUtc;

    /// <param name="pinnedErrorDeg">The bank error past which both tones are pinned: the profile's <c>ToneBankRangeDeg</c> (VG-14).</param>
    public VisualGuidanceBankCalloutGate(double pinnedErrorDeg)
    {
        _pinnedErrorDeg = pinnedErrorDeg;
    }

    /// <summary>
    /// One update: both banks right-positive (the manager passes <c>StandardBank</c>, VG-7). Returns
    /// the callout to speak now, or null. Always updates its tracking, even when
    /// <paramref name="maySpeak"/> is false, so a callout held back by a manual readout speaks the
    /// error as it is when the window ends.
    /// </summary>
    public string? Next(DateTime utc, double desiredBankDeg, double actualBankDeg, bool maySpeak)
    {
        // The bank is steady from the last time it moved more than the band from where it was then.
        if (_steadyBank is not double anchor || Math.Abs(actualBankDeg - anchor) > BankSteadyBandDeg)
        {
            _steadyBank = actualBankDeg;
            _steadySince = utc;
        }

        double error = desiredBankDeg - actualBankDeg;
        double size = Math.Abs(error);
        if (_matched ? size >= MatchedLeaveDeg : size <= MatchedEnterDeg)
            _matched = !_matched;

        string key = _matched ? MatchedKey : ErrorPhrase(error);
        if (key != _candidateKey)
        {
            _candidateKey = key;
            _candidateSince = utc;
        }

        // An error callout the pilot is still sitting on is said again every RepeatSeconds: an
        // aircraft one or two degrees off must be told so (the owner, 2026-11-04). "Matched" is not.
        bool repeat = key == _lastSpokenKey && !_matched
            && _lastSpokenUtc is DateTime spokenAt && (utc - spokenAt).TotalSeconds >= RepeatSeconds;
        if ((key == _lastSpokenKey && !repeat) || !Due(utc, size)) return null;
        if (!maySpeak) return null;
        if (_lastSpokenUtc is DateTime last && (utc - last).TotalSeconds < MinGapSeconds) return null;

        _lastSpokenKey = key;
        _lastSpokenUtc = utc;
        if (!_matched) return key;
        return Math.Abs(desiredBankDeg) < OnPathCommandDeg ? "On commanded path" : "matched";
    }

    /// <summary>Forgets everything: what was spoken, the matched state, the holds.</summary>
    public void Reset()
    {
        _matched = false;
        _candidateKey = null;
        _candidateSince = default;
        _steadyBank = null;
        _steadySince = default;
        _lastSpokenKey = null;
        _lastSpokenUtc = null;
    }

    private bool Due(DateTime utc, double size)
    {
        double held = (utc - _candidateSince).TotalSeconds;
        if (_matched) return held >= SmallHoldSeconds;
        if (size < MatchedLeaveDeg) return held >= SubDegreeHoldSeconds;
        if (size < _pinnedErrorDeg) return held >= SmallHoldSeconds;
        return (utc - _steadySince).TotalSeconds >= LargeSteadySeconds;
    }

    private static string ErrorPhrase(double error)
    {
        int n = Math.Max(1, (int)Math.Round(Math.Abs(error), MidpointRounding.AwayFromZero));
        string number = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return error < 0 ? number + " left" : number + " right";
    }
}

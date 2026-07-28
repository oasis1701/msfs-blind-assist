using System.Text;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services.SayIntentions;

/// <summary>
/// Pure parsing of SayIntentions ATC speech into the identifiers Taxi Guidance
/// consumes. No I/O, no UI — everything here is covered by
/// SayIntentionsClearanceParserTests.
///
/// The load-bearing rule: a taxi clearance to a gate routinely ends
/// "hold short of runway NN", and a clearance to a runway routinely contains a
/// crossing. Destination extraction therefore runs against a copy of the text
/// with every hold-short/crossing span MASKED OUT, so a runway the pilot was
/// told to stop at can never become the place we route them to.
/// </summary>
public static class SayIntentionsClearanceParser
{
    /// <summary>Runway token: written ("15", "15L", "15 left") or spoken
    /// ("one five left"). The written branch absorbs an optional spoken side so
    /// "runway 15 left" doesn't truncate to "15".</summary>
    private const string RunwayToken =
        @"(?:[0-9]{1,2}\s*(?:LEFT|RIGHT|CENTER|CENTRE|[LCR])?" +
        @"|(?:ZERO|ONE|TWO|THREE|TREE|FOUR|FIVE|FIFE|SIX|SEVEN|EIGHT|NINER|NINE|LEFT|RIGHT|CENTER|CENTRE|[-\s])+)";

    private static readonly Regex HoldShortOrCrossing = new(
        @"\b(?:HOLD\s+SHORT(?:\s+OF)?|CROSS(?:ING)?)\s+(?:RUNWAY\s*)?" + RunwayToken,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HoldShortRunwayCapture = new(
        @"\bHOLD\s+SHORT(?:\s+OF)?\s+(?:RUNWAY\s*)?(?<runway>" + RunwayToken + @")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnyRunwayCapture = new(
        @"\bRUNWAY\s*(?<runway>" + RunwayToken + @")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TaxiClearanceShape = new(
        @"\b(?:TAXI|VIA)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NotATaxiClearance = new(
        @"\bCLEARED\s+TO\s+LAND\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Ordered longest-first so NINER is consumed before NINE and TREE
    /// before the bare digit words that share a prefix.</summary>
    private static readonly (string Word, string Digit)[] DigitWords =
    {
        ("ZERO", "0"), ("ONE", "1"), ("TWO", "2"), ("THREE", "3"), ("TREE", "3"),
        ("FOUR", "4"), ("FIVE", "5"), ("FIFE", "5"), ("SIX", "6"), ("SEVEN", "7"),
        ("EIGHT", "8"), ("NINER", "9"), ("NINE", "9"),
        ("LEFT", "L"), ("RIGHT", "R"), ("CENTER", "C"), ("CENTRE", "C")
    };

    /// <summary>True when the text is shaped like a taxi clearance. Guards the
    /// "fall back to the last radio transmission" path — without it a landing
    /// clearance heard on rollout ("cleared to land runway 23") became a taxi
    /// destination.</summary>
    public static bool LooksLikeTaxiClearance(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return TaxiClearanceShape.IsMatch(text) && !NotATaxiClearance.IsMatch(text);
    }

    /// <summary>The runway the clearance routes TO, or null when the clearance
    /// names no destination runway (e.g. it is a clearance to a gate).</summary>
    public static string? ParseDestinationRunway(string? clearance)
    {
        if (string.IsNullOrWhiteSpace(clearance)) return null;
        var match = AnyRunwayCapture.Match(MaskHoldShortAndCrossings(clearance));
        return match.Success ? CleanRunway(NormalizeSpokenRunway(match.Groups["runway"].Value)) : null;
    }

    /// <summary>The runway the clearance says to hold short OF, or null.</summary>
    public static string? ParseHoldShortRunway(string? clearance)
    {
        if (string.IsNullOrWhiteSpace(clearance)) return null;
        var match = HoldShortRunwayCapture.Match(clearance);
        return match.Success ? CleanRunway(NormalizeSpokenRunway(match.Groups["runway"].Value)) : null;
    }

    /// <summary>Replaces every hold-short/crossing span with spaces, preserving
    /// length so downstream indices still line up with the original text.</summary>
    internal static string MaskHoldShortAndCrossings(string clearance)
    {
        var masked = new StringBuilder(clearance);
        foreach (Match match in HoldShortOrCrossing.Matches(clearance))
        {
            for (int i = match.Index; i < match.Index + match.Length; i++)
                masked[i] = ' ';
        }
        return masked.ToString();
    }

    /// <summary>Spoken digits/sides to their characters, then everything else
    /// stripped: "one five left" → "15L".</summary>
    public static string NormalizeSpokenRunway(string value)
    {
        string normalized = value.ToUpperInvariant();
        foreach (var (word, digit) in DigitWords)
            normalized = Regex.Replace(normalized, $@"\b{word}\b", digit);
        return Regex.Replace(normalized, @"[^0-9LCR]", "");
    }

    /// <summary>Canonicalizes a runway identifier to zero-padded digits plus an
    /// optional side. Returns null when the text carries no runway number.</summary>
    public static string? CleanRunway(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string cleaned = Regex.Replace(value.Trim().ToUpperInvariant(), @"\bRUNWAY\b", "").Trim();
        var match = Regex.Match(cleaned, @"([0-9]{1,2})\s*([LCR])?");
        if (!match.Success) return null;
        string number = match.Groups[1].Value.PadLeft(2, '0');
        return number + match.Groups[2].Value;
    }
}

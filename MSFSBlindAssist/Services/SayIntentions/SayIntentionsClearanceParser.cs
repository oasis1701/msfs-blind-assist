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

    /// <summary>
    /// Every phrasing that means "stop before this runway rather than taxi onto it".
    /// SHARED between the mask and the capture on purpose: the first version of this
    /// parser spelled the two separately, handled CROSS(ING) but only bare
    /// "hold short", and so a pilot READBACK — "holding short of runway 15", which is
    /// exactly what SayIntentions publishes as the newest transmission — still made
    /// 15 the taxi destination. If the two regexes can drift, they will.
    /// </summary>
    private const string HoldPrefix =
        @"(?:(?:HOLD(?:ING)?|REMAIN(?:ING)?)[\s-]+SHORT(?:\s+OF)?(?:\s+THE)?" +
        @"|HOLD(?:ING)?[\s-]+POINT(?:\s+(?:OF|AT|FOR))?(?:\s+THE)?)";

    private const string CrossPrefix = @"(?:CROSS(?:ING)?(?:\s+THE)?)";

    private static readonly Regex HoldShortOrCrossing = new(
        @"\b(?:" + HoldPrefix + "|" + CrossPrefix + @")\s+(?:RUNWAY\s*)?" + RunwayToken,
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex HoldShortRunwayCapture = new(
        @"\b" + HoldPrefix + @"\s+(?:RUNWAY\s*)?(?<runway>" + RunwayToken + @")",
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

    private static readonly Regex ViaKeyword = new(
        @"\bVIA\b(?<route>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>Ends the route: everything after one of these is no longer a
    /// taxiway list. Deliberately EXCLUDES "cross" and "then" — a clearance
    /// continues across a runway crossing (KBOS pattern, docs/taxi-guidance.md);
    /// crossings are masked out instead of truncating the route.
    ///
    /// INFORMATION is here because the ATIS letter is spoken phonetically ("advise you
    /// have information Sierra"). Read as route text it silently appends a real taxiway
    /// S to the clearance, or — once unresolved names are reported — claims the airport
    /// is missing one.</summary>
    private static readonly Regex RouteTerminator = new(
        @"\b(?:CONTACT|MONITOR|SQUAWK|REMAIN|REPORT|GIVE\s+WAY|FOLLOW|INFORMATION)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Spoken forms per character. DIGITS matter as much as letters: without
    /// them "Bravo Four" decayed to taxiway B — a real taxiway, so the wrong route was
    /// delivered with full confidence and never reported as skipped.</summary>
    private static readonly Dictionary<char, string> Nato = new()
    {
        ['A'] = "ALPHA",   ['B'] = "BRAVO",   ['C'] = "CHARLIE",      ['D'] = "DELTA",
        ['E'] = "ECHO",    ['F'] = "FOXTROT", ['G'] = "GOLF",         ['H'] = "HOTEL",
        ['I'] = "INDIA",   ['J'] = "JULIET(?:T)?", ['K'] = "KILO",    ['L'] = "LIMA",
        ['M'] = "MIKE",    ['N'] = "NOVEMBER",['O'] = "OSCAR",        ['P'] = "PAPA",
        ['Q'] = "QUEBEC",  ['R'] = "ROMEO",   ['S'] = "SIERRA",       ['T'] = "TANGO",
        ['U'] = "UNIFORM", ['V'] = "VICTOR",  ['W'] = "WHISKEY",      ['X'] = "X-?RAY",
        ['Y'] = "YANKEE",  ['Z'] = "ZULU",
        // Longer variants first so the alternation cannot settle on a prefix.
        ['0'] = "ZERO",    ['1'] = "ONE",     ['2'] = "TWO",          ['3'] = "THREE|TREE",
        ['4'] = "FOUR",    ['5'] = "FIVE|FIFE", ['6'] = "SIX",        ['7'] = "SEVEN",
        ['8'] = "EIGHT",   ['9'] = "NINER|NINE"
    };

    private const string NatoLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string NatoDigits = "0123456789";

    /// <summary>Every spoken form from the ONE table above, each in a group named after
    /// the character it spells, so the pattern and the designator it maps back to can
    /// never drift apart. Group names have to be identifiers, hence the L/D prefix.</summary>
    private static string NatoAlternation(string characters, char prefix) =>
        string.Join("|", characters.Select(c => $"(?<{prefix}{c}>{Nato[c]})"));

    /// <summary>A taxiway spelled out in phonetics: a letter word plus an optional digit
    /// ("Kilo", "Bravo Four"). Used ONLY to notice that a clearance named something the
    /// airport does not have.
    ///
    /// IgnoreCase is safe here — and required — precisely because this pattern has NO
    /// bare-designator branch: it matches whole NATO words, never the single characters
    /// BuildTaxiwayPattern must keep case-sensitive. Bare designators are left out on
    /// purpose; they would false-positive on ordinary abbreviations, and a wrong "could
    /// not apply K" teaches the pilot to distrust the whole announcement, which is far
    /// worse than missing one.</summary>
    private static readonly Regex PhoneticTaxiway = new(
        $@"(?<![A-Za-z0-9])(?:{NatoAlternation(NatoLetters, 'L')})" +
        $@"(?:[\s-]*(?:{NatoAlternation(NatoDigits, 'D')}|(?<lit>[0-9])))?" +
        @"(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The designator a phonetic match spells: "Bravo Four" → "B4".</summary>
    private static string PhoneticDesignator(Match match)
    {
        var designator = new StringBuilder();

        foreach (char c in NatoLetters)
        {
            if (!match.Groups[$"L{c}"].Success) continue;
            designator.Append(c);
            break;
        }

        foreach (char c in NatoDigits)
        {
            if (!match.Groups[$"D{c}"].Success) continue;
            designator.Append(c);
            break;
        }

        if (match.Groups["lit"].Success) designator.Append(match.Groups["lit"].Value);
        return designator.ToString();
    }

    /// <summary>Resolves the spoken taxiway sequence against the airport's real
    /// taxiway names. Only names the graph actually knows are returned.</summary>
    public static List<string> ParseTaxiways(string? clearance, IReadOnlyList<string> knownTaxiways)
        => ScanTaxiways(clearance, knownTaxiways).Resolved;

    /// <summary>The taxiway sequence a clearance names, split into the names this
    /// airport really has and the ones it does not.
    ///
    /// The second list is the whole point. A name only the CLEARANCE knows used to
    /// disappear without a word — at CYYZ "via Alpha, Kilo, Romeo" announced "Via A, R"
    /// — so the pilot heard a shorter route with no way to tell that a leg, and with it
    /// the path ATC actually cleared, had gone missing.</summary>
    public static (List<string> Resolved, List<string> Unresolved) ScanTaxiways(
        string? clearance, IReadOnlyList<string> knownTaxiways)
    {
        var resolved = new List<string>();
        var unresolved = new List<string>();
        if (string.IsNullOrWhiteSpace(clearance) || knownTaxiways.Count == 0)
            return (resolved, unresolved);

        var via = ViaKeyword.Match(MaskHoldShortAndCrossings(clearance));
        if (!via.Success) return (resolved, unresolved);

        string route = via.Groups["route"].Value;
        var terminator = RouteTerminator.Match(route);
        if (terminator.Success) route = route.Substring(0, terminator.Index);

        // Collect every candidate hit, then resolve overlaps longest-first so
        // "Alpha-Tango" reads as AT rather than A followed by T.
        var hits = new List<(string Name, int Index, int End)>();
        foreach (string taxiway in knownTaxiways)
        {
            foreach (Match match in Regex.Matches(route, BuildTaxiwayPattern(taxiway)))
                hits.Add((taxiway, match.Index, match.Index + match.Length));
        }

        var selected = new List<(string Name, int Index, int End)>();
        foreach (var hit in hits.OrderBy(h => h.Index).ThenByDescending(h => h.End - h.Index))
        {
            if (selected.Any(s => hit.Index < s.End && hit.End > s.Index)) continue;
            selected.Add(hit);
        }

        foreach (Match match in PhoneticTaxiway.Matches(route))
        {
            // Touching ANY resolved name is enough to stay quiet: the two words of
            // "Alpha-Tango" both sit inside the AT that already matched, and reporting
            // them would name two missing taxiways against a route that is entirely fine.
            if (selected.Any(s => match.Index < s.End && match.Index + match.Length > s.Index))
                continue;

            string designator = PhoneticDesignator(match);
            if (designator.Length == 0 || unresolved.Contains(designator)) continue;

            // The graph can spell a taxiway in a form BuildTaxiwayPattern has no
            // phonetic branch for ("B 4"), which is a matching gap here, not a taxiway
            // the airport is missing.
            if (knownTaxiways.Any(t =>
                    NormalizeTaxiwayName(t).Equals(designator, StringComparison.OrdinalIgnoreCase)))
                continue;

            unresolved.Add(designator);
        }

        resolved.AddRange(CollapseConsecutive(
            selected.OrderBy(h => h.Index).Select(h => h.Name).ToList()));
        return (resolved, unresolved);
    }

    /// <summary>
    /// Matches a taxiway either as its literal designator or spelled out in NATO
    /// phonetics. The literal branch is CASE-SENSITIVE (uppercase only) while the
    /// phonetic branch is not — that asymmetry is what stops the English article
    /// "a" being read as taxiway A, and the preposition "at" as taxiway AT.
    /// Callers must therefore NOT pass RegexOptions.IgnoreCase.
    /// </summary>
    internal static string BuildTaxiwayPattern(string taxiway)
    {
        string trimmed = taxiway.Trim().ToUpperInvariant();
        var parts = new List<string>();

        if (Regex.IsMatch(trimmed, @"^[A-Z][A-Z0-9]*$"))
        {
            foreach (char c in trimmed)
            {
                parts.Add(Nato.TryGetValue(c, out string? word)
                    ? $"(?:{Regex.Escape(c.ToString())}|(?i:{word}))"
                    : Regex.Escape(c.ToString()));
            }
        }
        else
        {
            parts.Add(Regex.Replace(Regex.Escape(trimmed), @"(\\)?\s+", @"[\s-]*"));
        }

        return $@"(?<![A-Za-z0-9]){string.Join(@"[\s-]*", parts)}(?![A-Za-z0-9])";
    }

    /// <summary>A descriptor tail is separated by a SPACED dash ("A9 - Terminal 1").
    /// A bare hyphen is part of the stand name ("A-9") and must survive.</summary>
    private static readonly Regex ParkingDescriptorSuffix = new(
        @"\s+[-–—]\s+.*$", RegexOptions.Compiled);

    private static readonly Regex ParkingNoiseWords = new(
        @"\b(?:GATE|PARKING|STAND|SPOT|RAMP|POSITION)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The separator class admits a HYPHEN as well as a space: normalizing
    /// "A-9" to "A9" was not enough while the capture itself stopped at the bare
    /// letter, which routed the pilot to stand "A" — or, with no such stand, fell
    /// through to the departure RUNWAY as the destination.</summary>
    private static readonly Regex GateInClearance = new(
        @"\b(?:GATE|STAND|PARKING|RAMP|SPOT)\s+(?<gate>[A-Z]{0,2}[\s-]?[0-9]{1,3}[A-Z]?|[A-Z][0-9]{0,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Matches the keyword that introduces the stand id in a full gate label.</summary>
    private static readonly Regex ParkingKeyword = new(
        @"\b(?:GATE|STAND|PARKING|SPOT|RAMP|POSITION)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Canonical form for comparing a SayIntentions gate label against a navdata
    /// parking spot.
    ///
    /// SayIntentions publishes assigned_gate as the FULL label — a live EDDF arrival
    /// gave "Terminal 3 Gate J1", not "J1". Navdata names the spot "J1", so merely
    /// stripping noise words left "TERMINAL3J1", which matched nothing: the assigned
    /// gate could never resolve, and destination resolution fell through to a RUNWAY.
    /// The stand id is whatever FOLLOWS the last gate/stand keyword; everything before
    /// it is the terminal or concourse, which navdata does not carry in the spot name.
    /// A label with no such keyword ("A-9", "J1") is used whole.
    /// </summary>
    public static string NormalizeParkingName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string cleaned = ParkingDescriptorSuffix.Replace(value.Trim(), "").ToUpperInvariant();

        var keywords = ParkingKeyword.Matches(cleaned);
        if (keywords.Count > 0)
        {
            var last = keywords[^1];
            string tail = cleaned[(last.Index + last.Length)..];
            // Only take the tail when it actually carries an id — a bare "Gate"
            // must not normalize to nothing.
            if (Regex.IsMatch(tail, @"[A-Z0-9]")) cleaned = tail;
        }

        cleaned = ParkingNoiseWords.Replace(cleaned, "");
        return Regex.Replace(cleaned, @"[^A-Z0-9]", "");
    }

    /// <summary>The gate/stand a taxi clearance routes to, or null.</summary>
    public static string? ParseDestinationGate(string? clearance)
    {
        if (string.IsNullOrWhiteSpace(clearance)) return null;
        var match = GateInClearance.Match(MaskHoldShortAndCrossings(clearance));
        if (!match.Success) return null;
        string gate = NormalizeParkingName(match.Groups["gate"].Value);
        return string.IsNullOrWhiteSpace(gate) ? null : gate;
    }

    /// <summary>Strips punctuation/spacing for name comparison: "A 1" → "A1".</summary>
    public static string NormalizeTaxiwayName(string value) =>
        Regex.Replace(value.ToUpperInvariant(), @"[^A-Z0-9]", "");

    /// <summary>Collapses runs of the same name. Non-consecutive reuse survives —
    /// a clearance legitimately revisits a taxiway after a runway crossing.</summary>
    public static List<string> CollapseConsecutive(IReadOnlyList<string> values)
    {
        var result = new List<string>();
        foreach (string value in values)
        {
            if (result.Count == 0 || !result[^1].Equals(value, StringComparison.OrdinalIgnoreCase))
                result.Add(value);
        }
        return result;
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

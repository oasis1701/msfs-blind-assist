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

    /// <summary>
    /// What joins the hold/cross prefix to the runway it names. A HYPHEN counts as the
    /// separator, exactly as it already does inside <see cref="HoldPrefix"/>'s own
    /// "hold-short" — and for the same reason: SayIntentions writes one.
    ///
    /// KDTW Ground, live, 2026-07-31: *"cross-runway 4R, then continue taxi via K, Q"*.
    /// Spelled `\s+`, the mask missed that crossing entirely, and the crossing runway is
    /// precisely what <see cref="MaskHoldShortAndCrossings"/> exists to hide from
    /// <see cref="ParseDestinationRunway"/> — so the leftmost "runway 4R" became the
    /// DESTINATION and the import would have routed a taxiing aircraft at the active
    /// runway it had just been cleared to cross. Shared between the mask and the
    /// capture for the same reason the prefix itself is: two spellings of one concept
    /// drift, and this one had already drifted once.
    /// </summary>
    private const string PrefixToRunway = @"[\s-]+(?:RUNWAY\s*)?";

    private static readonly Regex HoldShortOrCrossing = new(
        @"\b(?:" + HoldPrefix + "|" + CrossPrefix + ")" + PrefixToRunway + RunwayToken,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HoldShortRunwayCapture = new(
        @"\b" + HoldPrefix + PrefixToRunway + @"(?<runway>" + RunwayToken + @")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AnyRunwayCapture = new(
        @"\bRUNWAY\s*(?<runway>" + RunwayToken + @")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>"Taxi" or a bare "via" — an abbreviated clearance can omit the verb
    /// ("Runway 15L via Bravo, Charlie"), so the word is not required. What that
    /// admits is ruled back out by <see cref="NotATaxiClearance"/>.</summary>
    private static readonly Regex TaxiClearanceShape = new(
        @"\b(?:TAXI|VIA)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Phrasings that rule a transmission out however taxi-shaped it looks.
    ///
    /// A landing clearance heard on rollout was the original reason. A live KBOS
    /// capture added the other: clearance delivery says *"Cleared to Miami VIA the
    /// SSOXS7 departure…"*, which passed on the strength of that "via" alone. Importing
    /// it found no taxiways, fell back to shortest path to the departure runway, and
    /// announced itself as a SayIntentions route — with nothing to tell the pilot it had
    /// not come from a taxi clearance at all. The pilot's READBACK of the same clearance
    /// is published as a transmission too, and is the newest thing on the frequency at
    /// exactly the moment someone might press the import key.
    ///
    /// Each phrase here belongs to clearance delivery and to nothing a ground controller
    /// says while taxiing you, so excluding on them costs no real taxi clearance.
    ///
    /// A SQUAWK is deliberately NOT one of them, though clearance delivery issues one:
    /// a squawk legitimately ENDS a taxi clearance too, which is why
    /// <see cref="RouteTerminator"/> lists it. Excluding on it rejected
    /// "Runway 22R, taxi via Alpha, Bravo. Squawk 4571." outright — ClearanceText then
    /// stayed null, the destination fell through to the departure runway, and the pilot
    /// heard "no taxiways matched, using shortest path": the same silent failure this
    /// regex exists to prevent, reached from the other side. The two live clearance-
    /// delivery captures are still excluded on "as filed" and "climb and maintain", so
    /// the clause bought nothing.
    /// </summary>
    private static readonly Regex NotATaxiClearance = new(
        @"\bCLEARED\s+TO\s+LAND\b" +
        @"|\bCLIMB\s+AND\s+MAINTAIN\b" +
        @"|\bAS\s+FILED\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline | RegexOptions.Compiled);

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
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

    /// <summary>
    /// The other way SayIntentions speaks a single letter: the compass word it stands
    /// for. Palma Ground, live — "Taxi to holding point runway 24R via LE, E, North,
    /// H2." — where LEPA's navdata calls that taxiway N. Not "November": the plain
    /// English word. It cost the route a leg twice over, because the pattern stopped at
    /// the trailing "orth" AND the phonetic-only unresolved scan had no branch for it
    /// either, so the pilot heard a three-taxiway route with nothing to say a leg was
    /// missing.
    ///
    /// These live in a table of their own only because the guard below has to know
    /// which words are compass words; they are MERGED into the NATO forms by
    /// <see cref="SpokenForms"/>, so a taxiway pattern and the unresolved scan pick
    /// them up from the same place ALPHA comes from and cannot diverge.
    /// </summary>
    private static readonly Dictionary<char, string> Compass = new()
    {
        ['C'] = "CENTER|CENTRE", ['E'] = "EAST", ['N'] = "NORTH",
        ['S'] = "SOUTH",         ['W'] = "WEST"
    };

    /// <summary>Every way a character can be spoken: its NATO word, plus the compass
    /// word for the five letters that have one.</summary>
    private static string SpokenForms(char c) =>
        Compass.TryGetValue(c, out string? compass) ? $"{Nato[c]}|{compass}" : Nato[c];

    private const string NatoLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string NatoDigits = "0123456789";

    /// <summary>Every spoken form from the ONE table above, each in a group named after
    /// the character it spells, so the pattern and the designator it maps back to can
    /// never drift apart. Group names have to be identifiers, hence the L/D prefix.</summary>
    private static string NatoAlternation(string characters, char prefix) =>
        string.Join("|", characters.Select(c => $"(?<{prefix}{c}>{SpokenForms(c)})"));

    /// <summary>Built from the ONE table above so it can never name a word the patterns
    /// do not match, or miss one they do.</summary>
    private static readonly Regex CompassWord = new(
        @"(?<![A-Za-z0-9])(?:" + string.Join("|", Compass.Values) + @")(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// What comes BEFORE a compass word and settles that it is a direction.
    ///
    /// "the" covers every place phrase whose noun this parser could not guess — "to the
    /// north end", "on the north pier". A taxiway is never given an article: ATC says
    /// "via Alpha", never "via the Alpha".
    ///
    /// A runway number covers the other half: CENTER is the SIDE of a runway
    /// designator. Hold-short and crossing runways are already blanked out by the mask,
    /// but a runway named after the via keyword ("taxi via Alpha to runway 24 Center")
    /// is not, and it ends with a comma or the end of the transmission — nothing else
    /// in the text says it is not a taxiway.
    /// </summary>
    private static readonly Regex DirectionPhrasePrefix = new(
        @"(?:\bTHE|\bRUNWAY[\s-]*" + RunwayToken + @")[\s-]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The very next word — within three separators, so a blanked-out
    /// hold-short span (twenty-odd spaces) reads as "nothing follows", which is what it
    /// is, rather than dragging in the first word on the far side of it.</summary>
    private static readonly Regex ImmediateNextWord = new(
        @"^[\s-]{0,3}(?<word>[A-Za-z]+)", RegexOptions.Compiled);

    /// <summary>The only English words that may sit between two taxiways.</summary>
    private static readonly Regex RouteConnector = new(
        @"^(?:AND|THEN)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// True when a compass word is being used as a DIRECTION rather than as the name of
    /// a taxiway. This is the price a compass word carries that a NATO word does not:
    /// nobody writes "alpha" in prose, but "taxi north on Bravo" and "the north side"
    /// are ordinary things for a controller to say, and they can sit after "via".
    ///
    /// Both failures are real and they are mirror images, so ONE test has to cover both
    /// scans or the announcement contradicts itself between airports: where the airport
    /// HAS the letter, prose silently adds a leg ATC never cleared; where it does not,
    /// prose is announced as "could not apply North" — and a false report teaches the
    /// pilot to distrust the whole announcement.
    ///
    /// A compass word is prose when a direction phrase leads into it, or when the very
    /// next word is English rather than the next designator in the list. Everything
    /// else — a comma, a full stop, the end of the route, "and"/"then", or another
    /// taxiway — leaves it a taxiway. The lowercase prose that follows a direction is
    /// safe to test against the designator list precisely because the literal branch is
    /// case-sensitive: "north apron" cannot see taxiway A in "apron".
    ///
    /// Capitalization is deliberately NOT the signal. SayIntentions' text is generated,
    /// and "North" being capitalized in one live clearance is not a contract.
    /// </summary>
    private static bool IsDirectionProse(
        string route, int index, int end, HashSet<int> designatorStarts)
    {
        if (!CompassWord.IsMatch(route[index..end])) return false;
        if (DirectionPhrasePrefix.IsMatch(route[..index])) return true;

        var next = ImmediateNextWord.Match(route[end..]);
        if (!next.Success) return false;

        var word = next.Groups["word"];
        if (RouteConnector.IsMatch(word.Value)) return false;
        return !designatorStarts.Contains(end + word.Index);
    }

    /// <summary>A taxiway spelled out in phonetics: a letter word plus an optional digit
    /// ("Kilo", "Bravo Four"). Used ONLY to notice that a clearance named something the
    /// airport does not have.
    ///
    /// IgnoreCase is safe here — and required — precisely because this pattern has NO
    /// bare-designator branch: it matches whole spoken words, never the single characters
    /// BuildTaxiwayPattern must keep case-sensitive. Bare designators are left out on
    /// purpose; they would false-positive on ordinary abbreviations, and a wrong "could
    /// not apply K" teaches the pilot to distrust the whole announcement, which is far
    /// worse than missing one.
    ///
    /// The word list has since gained the five compass words, which ARE ordinary English
    /// — that is the one widening this rule ever took, and it is bounded the same way:
    /// a closed list of whole words, no bare designators. What English costs is paid by
    /// <see cref="IsDirectionProse"/>, not by loosening the pattern.</summary>
    private static readonly Regex PhoneticTaxiway = new(
        $@"(?<![A-Za-z0-9])(?:{NatoAlternation(NatoLetters, 'L')})" +
        $@"(?:[\s-]*(?:{NatoAlternation(NatoDigits, 'D')}|(?<lit>[0-9])))?" +
        @"(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
            foreach (Match match in Regex.Matches(route, BuildTaxiwayPattern(taxiway), RegexOptions.CultureInvariant))
                hits.Add((taxiway, match.Index, match.Index + match.Length));
        }

        // Where a designator could start, taken BEFORE the prose filter so a compass
        // word followed by the next taxiway in an un-punctuated list ("via LE E North
        // H2") still reads as a route rather than as English.
        var designatorStarts = hits.Select(h => h.Index).ToHashSet();
        hits.RemoveAll(h => IsDirectionProse(route, h.Index, h.End, designatorStarts));

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

            // The same guard the resolved half uses, from the same helper: a direction
            // must not be reported as a taxiway the airport is missing any more than it
            // may be routed as one.
            if (IsDirectionProse(route, match.Index, match.Index + match.Length, designatorStarts))
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
                parts.Add(Nato.ContainsKey(c)
                    ? $"(?:{Regex.Escape(c.ToString())}|(?i:{SpokenForms(c)}))"
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
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The separator class admits a HYPHEN as well as a space: normalizing
    /// "A-9" to "A9" was not enough while the capture itself stopped at the bare
    /// letter, which routed the pilot to stand "A" — or, with no such stand, fell
    /// through to the departure RUNWAY as the destination.</summary>
    private static readonly Regex GateInClearance = new(
        @"\b(?:GATE|STAND|PARKING|RAMP|SPOT)\s+(?<gate>[A-Z]{0,2}[\s-]?[0-9]{1,3}[A-Z]?|[A-Z][0-9]{0,3})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Matches the keyword that introduces the stand id in a full gate label.</summary>
    private static readonly Regex ParkingKeyword = new(
        @"\b(?:GATE|STAND|PARKING|SPOT|RAMP|POSITION)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        cleaned = Regex.Replace(cleaned, @"[^A-Z0-9]", "");
        return PaddingZeros.Replace(cleaned, "");
    }

    /// <summary>
    /// A zero that only pads a stand number, so "B06" and "B6" compare equal.
    ///
    /// Live EDDB: SayIntentions assigned "Gate B06" while the scenery — and the navdata
    /// this app routes on — call that stand B6. The two never compared equal, so the
    /// assigned gate could not resolve and destination resolution ran to the end of its
    /// chain and took the ARRIVAL RUNWAY: a just-landed aircraft was routed at 24L,
    /// along exactly the taxiways ATC had given for the gate. Zero-padding is a
    /// rendering choice on either side of that comparison, never identity.
    ///
    /// Leading only, and BOTH guards are load-bearing. The lookbehind confines the run
    /// to the start of a digit group, or "100" loses its middle zero and reads as stand
    /// 10; the lookahead requires a digit to survive, so a lone "0" and any trailing
    /// zero stay. B10 must never collapse to B1 — that is the same wrong-stand failure
    /// this normalization exists to prevent, pointed the other way.
    /// </summary>
    private static readonly Regex PaddingZeros = new(@"(?<![0-9])0+(?=[0-9])", RegexOptions.Compiled);

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

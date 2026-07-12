# Route Advisory Decoding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the route-advisory parser for the real ActiveSky hit-path format (single-CRLF blocks, per-segment duplicates) and add a plain-English decoder — checkbox-gated in the Weather Radar box, always-on for the spoken announcement.

**Architecture:** All new logic is pure string work in `Services/ActiveSkyFormatting.cs` (the existing formatter pattern): the parser splits on advisory-header lines and dedups by key; a field decoder extracts hazard/time/extent/movement/trend per advisory; builders compose the box text and announce phrase. `WeatherRadarForm` and `MainForm` only pass flags/strings. Spec: `docs/design/2026-07-12-route-advisory-decoding-design.md`.

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit, Regex.

## Global Constraints

- Branch: `feat/activesky-improvements` (never commit to `main`; do not push).
- Build the SOLUTION only: `dotnet build MSFSBlindAssist.sln -c Debug` (a bare `.csproj` build writes to the wrong folder — CLAUDE.md).
- Test command: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
- 0 build warnings (current baseline is 0).
- `RouteAdvisoryTracker` and its Observe/ClearAnnouncedKeys/Reset contract must NOT change; the announce/dedup key stays the raw header line.
- Announcements use queued `announcer.Announce`, neutral phrasing (no "New") — the same announce fires as the 15-minute reminder.
- Every existing test in `RouteAdvisoriesTests.cs` must keep passing unchanged EXCEPT the two `BuildRouteAdvisoriesText` call sites, which gain the new `decode:` argument (Task 3).
- All numeric formatting in spoken/displayed text uses `System.Globalization.CultureInfo.InvariantCulture` (never culture-sensitive `N0` interpolation).

---

### Task 1: Parser fix — header-line splitting + dedup

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs` (the `ParseRouteAdvisories` method, ~line 230)
- Test: `tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs`

**Interfaces:**
- Consumes: existing `RouteAdvisory { string Key; List<string> Lines; }` and `ParseRouteAdvisories(string raw)` in `ActiveSkyFormatting`.
- Produces: `ParseRouteAdvisories` now returns one advisory per header-line block, deduplicated by `Key` (OrdinalIgnoreCase, first-seen order). Signature unchanged. Task 2 adds `DecodeFields`; Task 1 must compile and pass WITHOUT it.

- [ ] **Step 1: Add the live-capture fixture and failing tests**

Add to `tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs`, inside the class (below the existing consts). The body strings are the VERBATIM 2026-07-12 live capture — preserve the irregular spacing (`W09304- N1127` and the double space in `W09031  -`) exactly:

```csharp
    // --- Live capture (2026-07-12, /GetActiveSigmetsAt, port 19285) -------------------
    // Real hit-path format: each advisory is exactly 3 lines (header / Valid until /
    // body) separated by single CRLF — NO blank lines — and ActiveSky repeats the same
    // advisory once per route-segment intersection (MHTG J5 appeared 7×). Spacing
    // irregularities in the MHTG body are verbatim from the capture.
    private const string LiveMhtgBody =
        "MHCC CENTRAL AMERICAN FIR EMBD TS OBS AT 1830Z WI N1121 W10027 - N1258 W09506 - N1403 W09304- N1127 W09031  - N0950 W09306 - N0923 W09619 - N0904 W09940 TOP FL520 MOV W 05KT NC=";
    private const string LiveYmmmBody =
        "YMMM MELBOURNE FIR SEV TURB FCST WI S3640 E14800 - S3340 E15000 - S3410 E15100 - S3740 E14940 - S3820 E14550 - S3730 E14520 SFC/8000FT STNR NC=";
    private const string LiveMhtgBlock =
        "MHTG SIGMET J5 EMBD TS\r\nValid until: 2200z\r\n" + LiveMhtgBody + "\r\n";
    private const string LiveYmmmBlock =
        "YMMM SIGMET T07 TURB\r\nValid until: 2300z\r\n" + LiveYmmmBody + "\r\n";

    private static string LiveCapture()
        => string.Concat(Enumerable.Repeat(LiveMhtgBlock, 7)) + LiveYmmmBlock;

    [Fact]
    public void Live_capture_single_crlf_blocks_split_and_dedup_to_two()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture());
        Assert.Equal(2, advisories.Count);
        Assert.Equal("MHTG SIGMET J5 EMBD TS", advisories[0].Key);
        Assert.Equal("YMMM SIGMET T07 TURB", advisories[1].Key);
        Assert.Equal(new[] { "MHTG SIGMET J5 EMBD TS", "Valid until: 2200z", LiveMhtgBody },
            advisories[0].Lines);
    }

    [Fact]
    public void Header_line_starts_a_new_block_without_blank_separators()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "MHTG SIGMET J5 EMBD TS\r\nValid until: 2200z\r\nBODY A=\r\n"
            + "YMMM AIRMET T07 TURB\r\nValid until: 2300z\r\nBODY B=");
        Assert.Equal(2, advisories.Count);
        Assert.Equal("YMMM AIRMET T07 TURB", advisories[1].Key);
        Assert.Equal(new[] { "YMMM AIRMET T07 TURB", "Valid until: 2300z", "BODY B=" },
            advisories[1].Lines);
    }

    [Fact]
    public void Duplicate_blocks_dedup_case_insensitively_keeping_first()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "MHTG SIGMET J5 EMBD TS\r\nBody 1\r\nmhtg sigmet j5 embd ts\r\nBody 2");
        var a = Assert.Single(advisories);
        Assert.Equal("MHTG SIGMET J5 EMBD TS", a.Key);
        Assert.Equal(new[] { "MHTG SIGMET J5 EMBD TS", "Body 1" }, a.Lines);
    }

    [Fact]
    public void Leading_free_text_before_first_header_is_its_own_block()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "Some preamble text\r\nMHTG SIGMET J5 EMBD TS\r\nValid until: 2200z\r\nBODY=");
        Assert.Equal(2, advisories.Count);
        Assert.Equal("Some preamble text", advisories[0].Key);
        Assert.Equal("MHTG SIGMET J5 EMBD TS", advisories[1].Key);
    }
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteAdvisories"`
Expected: the 4 new tests FAIL (`Live_capture…` sees 1 advisory, not 2; `Header_line…` sees 1, not 2); all pre-existing RouteAdvisories tests still PASS.

- [ ] **Step 3: Rewrite ParseRouteAdvisories with header-line splitting + dedup**

In `MSFSBlindAssist/Services/ActiveSkyFormatting.cs`, replace the entire body of `ParseRouteAdvisories` and add the header regex. Update its doc comment — the format is now KNOWN from the live capture:

```csharp
    /// <summary>A line that begins an advisory block in the /GetActiveSigmetsAt
    /// response, e.g. "MHTG SIGMET J5 EMBD TS" (live capture 2026-07-12).</summary>
    private static readonly Regex AdvisoryHeaderLine = new(
        @"^\S{3,4}\s+(SIGMET|AIRMET)\s+\S+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses the route-advisories response. The REAL hit format (live capture
    /// 2026-07-12) is consecutive 3-line advisories — header / "Valid until:" / body —
    /// separated by single CRLF with NO blank lines, and ActiveSky repeats the same
    /// advisory once per route-segment intersection, so blocks are split on header
    /// lines and DEDUPLICATED by key (first line, case-insensitive, first-seen order).
    /// Defensive fallbacks stay: a truly-blank line still splits (the previously
    /// assumed format), text before the first header — or a response with no header
    /// lines at all — stays one verbatim block, and nothing is ever dropped or thrown.
    /// The known no-hit sentence (any response starting "No airmet/sigmet",
    /// case-insensitive) parses to an empty list.
    /// </summary>
    internal static List<RouteAdvisory> ParseRouteAdvisories(string raw)
    {
        var result = new List<RouteAdvisory>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        string trimmed = raw.Trim();
        if (trimmed.StartsWith("No airmet/sigmet", StringComparison.OrdinalIgnoreCase))
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string>? current = null;

        void CloseCurrent()
        {
            if (current == null || current.Count == 0) { current = null; return; }
            var adv = new RouteAdvisory { Key = current[0].Trim(), Lines = current };
            if (seen.Add(adv.Key)) result.Add(adv);
            current = null;
        }

        foreach (var rawLine in trimmed.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                // Truly-empty line = block separator (the previously assumed format).
                // A spaces-only line is dropped WITHOUT splitting (pinned by
                // Whitespace_only_separator_line_does_not_split_the_block).
                if (rawLine.Length == 0) CloseCurrent();
                continue;
            }
            if (AdvisoryHeaderLine.IsMatch(line)) CloseCurrent();
            current ??= new List<string>();
            current.Add(line);
        }
        CloseCurrent();
        return result;
    }
```

Note: the old `blocks = trimmed.Split(new[] { "\r\n\r\n", "\n\n" }, …)` loop is fully replaced by the line-based walk above.

- [ ] **Step 4: Run the RouteAdvisories tests — all pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteAdvisories"`
Expected: ALL PASS — the 4 new tests and every pre-existing one (`No_hit_sentence…`, `Blank_lines_split_blocks` — "CONVECTIVE SIGMET 18E" and "SIGMET B4" do NOT match the header shape (first token 10 / 6 chars), so they still rely on the blank-line split; `Whitespace_only…`; `Unknown_free_text…`; all tracker tests).

- [ ] **Step 5: Run the FULL suite, then commit**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all tests pass (baseline 1201 + 4 new = 1205).

```bash
git add MSFSBlindAssist/Services/ActiveSkyFormatting.cs tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs
git commit -m "fix(weather): split route advisories on header lines + dedup (live-capture format)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Field decoder + summary/announcement builders

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs` (extend `RouteAdvisory`, add `DecodeFields` + helpers + `BuildRouteAdvisoryAnnouncement`)
- Test: `tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs`

**Interfaces:**
- Consumes: Task 1's `ParseRouteAdvisories` (calls `DecodeFields(adv)` inside `CloseCurrent`, added in this task) and the `LiveCapture()` fixture.
- Produces: `RouteAdvisory` gains `string? Identity, Hazard, ObsFcst, VerticalExtent, Movement, Trend, ValidUntil` (null = not recognized). New `internal static string BuildRouteAdvisoryAnnouncement(RouteAdvisory a)` → `"MHTG SIGMET J5, embedded thunderstorms, tops FL520"`, or the raw `Key` when `Identity` or `Hazard` is null. Task 3 relies on the fields and this method.

- [ ] **Step 1: Write the failing decode tests**

Add to `RouteAdvisoriesTests.cs`:

```csharp
    // --- Field decoding (spec 2026-07-12-route-advisory-decoding §3.2) ----------------

    [Fact]
    public void Live_mhtg_advisory_decodes_all_fields()
    {
        var a = ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture())[0];
        Assert.Equal("MHTG SIGMET J5", a.Identity);
        Assert.Equal("embedded thunderstorms", a.Hazard);
        Assert.Equal("observed at 1830Z", a.ObsFcst);
        Assert.Equal("tops FL520", a.VerticalExtent);
        Assert.Equal("moving west at 5 knots", a.Movement);
        Assert.Equal("no change expected", a.Trend);
        Assert.Equal("2200Z", a.ValidUntil);
    }

    [Fact]
    public void Live_ymmm_advisory_decodes_all_fields()
    {
        var a = ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture())[1];
        Assert.Equal("YMMM SIGMET T07", a.Identity);
        Assert.Equal("severe turbulence", a.Hazard);
        Assert.Equal("forecast", a.ObsFcst);
        Assert.Equal("surface to 8,000 feet", a.VerticalExtent);
        Assert.Equal("stationary", a.Movement);
        Assert.Equal("no change expected", a.Trend);
        Assert.Equal("2300Z", a.ValidUntil);
    }

    [Fact]
    public void Undecodable_block_has_null_fields()
    {
        var a = Assert.Single(ActiveSkyFormatting.ParseRouteAdvisories(
            "No flight plan is currently loaded"));
        Assert.Null(a.Identity);
        Assert.Null(a.Hazard);
        Assert.Null(a.ObsFcst);
        Assert.Null(a.VerticalExtent);
        Assert.Null(a.Movement);
        Assert.Null(a.Trend);
        Assert.Null(a.ValidUntil);
    }

    // --- BuildRouteAdvisoryAnnouncement -----------------------------------------------

    [Fact]
    public void Announcement_speaks_identity_hazard_and_extent()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture());
        Assert.Equal("MHTG SIGMET J5, embedded thunderstorms, tops FL520",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(advisories[0]));
        Assert.Equal("YMMM SIGMET T07, severe turbulence, surface to 8,000 feet",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(advisories[1]));
    }

    [Fact]
    public void Announcement_falls_back_to_raw_key_when_nothing_decodes()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "No flight plan is currently loaded");
        Assert.Equal("No flight plan is currently loaded",
            ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(advisories[0]));
    }
```

- [ ] **Step 2: Run to verify they fail to COMPILE**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteAdvisories"`
Expected: build error — `RouteAdvisory` has no `Identity` member; `BuildRouteAdvisoryAnnouncement` not defined.

- [ ] **Step 3: Implement the fields, decoder, and announcement builder**

In `ActiveSkyFormatting.cs`:

3a. Extend `RouteAdvisory`:

```csharp
    /// <summary>One advisory block from /GetActiveSigmetsAt. Key = first trimmed line
    /// (dedup identity for the announce tracker — NEVER derived from decoded fields).
    /// The decoded fields are null when the corresponding tokens weren't recognized;
    /// a block where nothing decodes renders verbatim everywhere.</summary>
    internal sealed class RouteAdvisory
    {
        public string Key = "";
        public List<string> Lines = new();

        // Decoded fields (design 2026-07-12-route-advisory-decoding §3.2).
        public string? Identity;        // "MHTG SIGMET J5"
        public string? Hazard;          // "embedded thunderstorms"
        public string? ObsFcst;         // "observed at 1830Z" / "forecast"
        public string? VerticalExtent;  // "tops FL520" / "surface to 8,000 feet"
        public string? Movement;        // "moving west at 5 knots" / "stationary"
        public string? Trend;           // "no change expected" / "intensifying" / "weakening"
        public string? ValidUntil;      // "2200Z"
    }
```

3b. In `ParseRouteAdvisories`'s `CloseCurrent` local function, add the decode call after constructing `adv`:

```csharp
            var adv = new RouteAdvisory { Key = current[0].Trim(), Lines = current };
            DecodeFields(adv);
            if (seen.Add(adv.Key)) result.Add(adv);
```

3c. Add the decoder (private helpers) and the announcement builder:

```csharp
    private static readonly Regex IdentityPattern = new(
        @"^(\S{3,4})\s+(SIGMET|AIRMET)\s+(\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Ordered most-specific-first; first match wins. ICAO SIGMET hazard
    /// vocabulary (qualifier × phenomenon) per the design §3.2 table.</summary>
    private static readonly (string Pattern, string Phrase)[] HazardPatterns =
    {
        (@"\bEMBD TSGR\b", "embedded thunderstorms with hail"),
        (@"\bEMBD TS\b", "embedded thunderstorms"),
        (@"\bOCNL TSGR\b", "occasional thunderstorms with hail"),
        (@"\bOCNL TS\b", "occasional thunderstorms"),
        (@"\bFRQ TSGR\b", "frequent thunderstorms with hail"),
        (@"\bFRQ TS\b", "frequent thunderstorms"),
        (@"\bSQL TSGR\b", "squall-line thunderstorms with hail"),
        (@"\bSQL TS\b", "squall-line thunderstorms"),
        (@"\bISOL TS\b", "isolated thunderstorms"),
        (@"\bOBSC TS\b", "obscured thunderstorms"),
        (@"\bTSGR\b", "thunderstorms with hail"),
        (@"\bSEV TURB\b", "severe turbulence"),
        (@"\bMOD TURB\b", "moderate turbulence"),
        (@"\bSEV ICE \(FZRA\)", "severe icing from freezing rain"),
        (@"\bSEV ICE\b", "severe icing"),
        (@"\bMOD ICE\b", "moderate icing"),
        (@"\bSEV MTW\b", "severe mountain waves"),
        (@"\bMOD MTW\b", "moderate mountain waves"),
        (@"\bHVY DS\b", "heavy dust storm"),
        (@"\bHVY SS\b", "heavy sandstorm"),
        (@"\bSEV DS\b", "severe dust storm"),
        (@"\bSEV SS\b", "severe sandstorm"),
        (@"\bVA CLD\b", "volcanic ash cloud"),
        (@"\bVA ERUPTION\b", "volcanic ash eruption"),
        (@"\bRDOACT CLD\b", "radioactive cloud"),
        (@"\bTURB\b", "turbulence"),
        (@"\bTS\b", "thunderstorms"),
    };

    private static readonly Dictionary<string, string> CompassWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["N"] = "north", ["NNE"] = "north-northeast", ["NE"] = "northeast", ["ENE"] = "east-northeast",
        ["E"] = "east", ["ESE"] = "east-southeast", ["SE"] = "southeast", ["SSE"] = "south-southeast",
        ["S"] = "south", ["SSW"] = "south-southwest", ["SW"] = "southwest", ["WSW"] = "west-southwest",
        ["W"] = "west", ["WNW"] = "west-northwest", ["NW"] = "northwest", ["NNW"] = "north-northwest",
    };

    /// <summary>Extracts the §3.2 fields from a parsed block. Pure; every field is
    /// independently optional and unknown tokens are simply ignored (the WI lat/lon
    /// polygon is never extracted — dropping it from decoded output is implicit in
    /// rebuilding from fields). Hazard searches the BODY first, then the header's
    /// trailing tokens.</summary>
    private static void DecodeFields(RouteAdvisory a)
    {
        var idm = IdentityPattern.Match(a.Key);
        if (idm.Success)
            a.Identity = $"{idm.Groups[1].Value.ToUpperInvariant()} {idm.Groups[2].Value.ToUpperInvariant()} {idm.Groups[3].Value.ToUpperInvariant()}";

        foreach (var line in a.Lines)
        {
            var vm = Regex.Match(line, @"^Valid until:\s*(\S+)", RegexOptions.IgnoreCase);
            if (vm.Success) { a.ValidUntil = vm.Groups[1].Value.ToUpperInvariant(); break; }
        }

        // Body = everything except the header line and the "Valid until:" line.
        string body = string.Join(" ", a.Lines.Skip(1)
            .Where(l => !l.StartsWith("Valid until:", StringComparison.OrdinalIgnoreCase)));

        a.Hazard = MatchHazard(body) ?? (idm.Success ? MatchHazard(a.Key) : null);

        var obs = Regex.Match(body, @"\bOBS(?:\s+AT\s+(\d{4})Z)?\b", RegexOptions.IgnoreCase);
        var fcst = Regex.Match(body, @"\bFCST(?:\s+AT\s+(\d{4})Z)?\b", RegexOptions.IgnoreCase);
        if (obs.Success)
            a.ObsFcst = obs.Groups[1].Success ? $"observed at {obs.Groups[1].Value}Z" : "observed";
        else if (fcst.Success)
            a.ObsFcst = fcst.Groups[1].Success ? $"forecast at {fcst.Groups[1].Value}Z" : "forecast";

        a.VerticalExtent = MatchVerticalExtent(body);

        var mov = Regex.Match(body, @"\bMOV\s+([NSEW]{1,3})\s*(\d{1,3})\s*KT\b", RegexOptions.IgnoreCase);
        if (mov.Success && CompassWords.TryGetValue(mov.Groups[1].Value, out string? dir))
            a.Movement = $"moving {dir} at {int.Parse(mov.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)} knots";
        else if (Regex.IsMatch(body, @"\bSTNR\b", RegexOptions.IgnoreCase))
            a.Movement = "stationary";

        if (Regex.IsMatch(body, @"\bNC\b")) a.Trend = "no change expected";
        else if (Regex.IsMatch(body, @"\bINTSF\b", RegexOptions.IgnoreCase)) a.Trend = "intensifying";
        else if (Regex.IsMatch(body, @"\bWKN\b", RegexOptions.IgnoreCase)) a.Trend = "weakening";
    }

    private static string? MatchHazard(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        foreach (var (pattern, phrase) in HazardPatterns)
            if (Regex.IsMatch(text, pattern)) return phrase;
        return null;
    }

    private static string? MatchVerticalExtent(string body)
    {
        Match m;
        if ((m = Regex.Match(body, @"\bTOPS?\s+ABV\s+FL(\d{3})\b")).Success)
            return $"tops above FL{m.Groups[1].Value}";
        if ((m = Regex.Match(body, @"\bTOPS?\s+BLW\s+FL(\d{3})\b")).Success)
            return $"tops below FL{m.Groups[1].Value}";
        if ((m = Regex.Match(body, @"\bTOPS?\s+FL(\d{3})\b")).Success)
            return $"tops FL{m.Groups[1].Value}";
        if ((m = Regex.Match(body, @"\bSFC/FL(\d{3})\b")).Success)
            return $"surface to FL{m.Groups[1].Value}";
        if ((m = Regex.Match(body, @"\bSFC/(\d{3,5})FT\b")).Success)
            return $"surface to {FeetWord(m.Groups[1].Value)}";
        if ((m = Regex.Match(body, @"\bFL(\d{3})/(\d{3})\b")).Success)
            return $"FL{m.Groups[1].Value} to FL{m.Groups[2].Value}";
        if ((m = Regex.Match(body, @"\b(\d{4,5})/(\d{4,5})FT\b")).Success)
            return $"{FeetWord(m.Groups[1].Value)} to {FeetWord(m.Groups[2].Value)}";
        return null;
    }

    private static string FeetWord(string digits)
        => int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture)
            .ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " feet";

    /// <summary>The always-decoded announce phrase (design §3.4): identity + hazard
    /// (+ vertical extent). Falls back to the raw Key when the essentials didn't
    /// decode — a spoken announcement never goes blank. Movement/trend/validity are
    /// deliberately box-only: announcements are interruptions, the box is the briefing.</summary>
    internal static string BuildRouteAdvisoryAnnouncement(RouteAdvisory a)
    {
        if (a.Identity == null || a.Hazard == null) return a.Key;
        return a.VerticalExtent == null
            ? $"{a.Identity}, {a.Hazard}"
            : $"{a.Identity}, {a.Hazard}, {a.VerticalExtent}";
    }
```

Note: `FeetWord("8000")` → `"8,000 feet"` (InvariantCulture — never the machine locale).

- [ ] **Step 4: Run the RouteAdvisories tests — all pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteAdvisories"`
Expected: ALL PASS (including every pre-existing test — `DecodeFields` only adds nullable fields; keys and Lines are untouched).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/ActiveSkyFormatting.cs tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs
git commit -m "feat(weather): decode route-advisory SIGMET fields + announce phrase builder

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Wire the box (decode checkbox) and the announcement

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs` (`BuildRouteAdvisoriesText` gains `bool decode`)
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs` (`FetchRouteAdvisoriesAsync`, ~line 830)
- Modify: `MSFSBlindAssist/MainForm.Announcers.cs` (`CheckRouteAdvisoriesAsync` announce loop, ~line 2326)
- Test: `tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs` (2 new tests + 2 existing call sites updated)

**Interfaces:**
- Consumes: Task 2's decoded fields and `BuildRouteAdvisoryAnnouncement(RouteAdvisory)`.
- Produces: `internal static string BuildRouteAdvisoriesText(IReadOnlyList<RouteAdvisory> advisories, bool decode)` — the old single-argument form is REMOVED (update all callers in the same commit).

- [ ] **Step 1: Write the failing rendering tests + update the two existing call sites**

Add to `RouteAdvisoriesTests.cs`:

```csharp
    // --- Decoded vs raw rendering (decode checkbox, design §3.3) ----------------------

    [Fact]
    public void Decoded_rendering_shows_summary_and_validity()
    {
        string text = ActiveSkyFormatting.BuildRouteAdvisoriesText(
            ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture()), decode: true);
        string[] rows = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Assert.Equal(new[]
        {
            "MHTG SIGMET J5: embedded thunderstorms, observed at 1830Z, tops FL520, moving west at 5 knots, no change expected.",
            "Valid until 2200Z.",
            "",
            "YMMM SIGMET T07: severe turbulence, forecast, surface to 8,000 feet, stationary, no change expected.",
            "Valid until 2300Z.",
        }, rows);
    }

    [Fact]
    public void Raw_rendering_keeps_original_lines_with_blank_separators()
    {
        string text = ActiveSkyFormatting.BuildRouteAdvisoriesText(
            ActiveSkyFormatting.ParseRouteAdvisories(LiveCapture()), decode: false);
        string[] rows = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Assert.Equal(new[]
        {
            "MHTG SIGMET J5 EMBD TS",
            "Valid until: 2200z",
            LiveMhtgBody,
            "",
            "YMMM SIGMET T07 TURB",
            "Valid until: 2300z",
            LiveYmmmBody,
        }, rows);
    }

    [Fact]
    public void Undecodable_block_renders_verbatim_even_when_decoding()
    {
        string text = ActiveSkyFormatting.BuildRouteAdvisoriesText(
            ActiveSkyFormatting.ParseRouteAdvisories("No flight plan is currently loaded"),
            decode: true);
        Assert.Equal("No flight plan is currently loaded", text);
    }
```

Update the TWO existing call sites to the new signature (same file):
- In `Empty_list_reads_no_advisories`: `ActiveSkyFormatting.BuildRouteAdvisoriesText(ActiveSkyFormatting.ParseRouteAdvisories(NoHit), decode: false)`
- In `Blocks_render_with_blank_separator_rows`: `ActiveSkyFormatting.BuildRouteAdvisoriesText(ActiveSkyFormatting.ParseRouteAdvisories(TwoBlocks), decode: false)`

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~RouteAdvisories"`
Expected: build error — `BuildRouteAdvisoriesText` has no `decode` parameter.

- [ ] **Step 3: Implement the rendering + wire both call sites**

3a. In `ActiveSkyFormatting.cs`, replace `BuildRouteAdvisoriesText`:

```csharp
    /// <summary>Radar-box text: blocks separated by one blank row; empty list reads
    /// "No advisories on route." With decode=true (the "Decode advisories into plain
    /// English" checkbox) each advisory renders as its rebuilt plain-English summary —
    /// the WI lat/lon polygon is dropped (noise when read aloud) — falling back to the
    /// verbatim block when nothing was recognized, so decoding never hides data.</summary>
    internal static string BuildRouteAdvisoriesText(IReadOnlyList<RouteAdvisory> advisories, bool decode)
    {
        if (advisories.Count == 0) return "No advisories on route.";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < advisories.Count; i++)
        {
            var a = advisories[i];
            if (i > 0) sb.AppendLine();
            if (decode && HasDecodedContent(a))
            {
                sb.AppendLine($"{a.Identity ?? a.Key}: {JoinDecodedFields(a)}.");
                if (a.ValidUntil != null) sb.AppendLine($"Valid until {a.ValidUntil}.");
            }
            else
            {
                foreach (var line in a.Lines) sb.AppendLine(line);
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>ValidUntil/Identity alone don't count — a summary needs at least one
    /// content field or it would render as a degenerate "KEY: ." line.</summary>
    private static bool HasDecodedContent(RouteAdvisory a)
        => a.Hazard != null || a.ObsFcst != null || a.VerticalExtent != null
           || a.Movement != null || a.Trend != null;

    private static string JoinDecodedFields(RouteAdvisory a)
    {
        var fields = new List<string>();
        if (a.Hazard != null) fields.Add(a.Hazard);
        if (a.ObsFcst != null) fields.Add(a.ObsFcst);
        if (a.VerticalExtent != null) fields.Add(a.VerticalExtent);
        if (a.Movement != null) fields.Add(a.Movement);
        if (a.Trend != null) fields.Add(a.Trend);
        return string.Join(", ", fields);
    }
```

3b. In `WeatherRadarForm.cs`, `FetchRouteAdvisoriesAsync` becomes:

```csharp
    private async Task<string> FetchRouteAdvisoriesAsync()
    {
        if (_activeSkyAvailable != true) return "unavailable";
        string? raw = await _activeSky.GetRouteAdvisoriesTextAsync();
        if (raw == null) return "unavailable";
        // Decode gating rides the same checkbox as the Nearby Advisories box; the
        // CheckedChanged handler saves the setting and the next refresh (≤30 s auto
        // or F5) picks it up — same latency contract as the sibling box.
        return MSFSBlindAssist.Services.ActiveSkyFormatting.BuildRouteAdvisoriesText(
            MSFSBlindAssist.Services.ActiveSkyFormatting.ParseRouteAdvisories(raw),
            SettingsManager.Current.DecodeWeatherAdvisories);
    }
```

3c. In `MainForm.Announcers.cs`, `CheckRouteAdvisoriesAsync`'s announce loop becomes (the surrounding method — gate, 15-minute clear, tracker Observe, catch/finally — is unchanged):

```csharp
            var advisories = MSFSBlindAssist.Services.ActiveSkyFormatting.ParseRouteAdvisories(raw);
            var newKeys = _routeAdvisoryTracker.Observe(advisories.Select(a => a.Key).ToList());

            if (!IsHandleCreated || IsDisposed) return;
            foreach (string key in newKeys)
            {
                // ALWAYS decoded, independent of the radar form's decode checkbox — a
                // spoken announcement must never contain raw SIGMET abbreviations
                // ("EMBD TS"). Falls back to the raw key when nothing decodes.
                var adv = advisories.FirstOrDefault(a =>
                    string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
                string phrase = adv != null
                    ? MSFSBlindAssist.Services.ActiveSkyFormatting.BuildRouteAdvisoryAnnouncement(adv)
                    : key;
                Log.Debug("MainForm", $"route advisory: \"{key}\" -> \"{phrase}\"");
                // Neutral phrasing (no "New"): the same announce serves both first
                // appearance and the 15-minute reminder re-announce, matching the
                // sibling proximity alerts' "category: content" wording.
                announcer.Announce($"Route advisory: {phrase}.");
            }
```

- [ ] **Step 4: Run the FULL suite and build the solution**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass.
Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded, 0 Warning(s).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/ActiveSkyFormatting.cs MSFSBlindAssist/Forms/WeatherRadarForm.cs MSFSBlindAssist/MainForm.Announcers.cs tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs
git commit -m "feat(weather): decode-checkbox route-advisory rendering + decoded announce

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Documentation sync

**Files:**
- Modify: `docs/weather.md` (§12 — Route Advisories)

**Interfaces:**
- Consumes: the final behavior of Tasks 1-3.
- Produces: docs matching the shipped behavior; no code.

- [ ] **Step 1: Update docs/weather.md §12**

Read §12 in full first, then make these changes:

1. Where §12 describes the response format as only partially known / blank-line-separated blocks, replace with:

> The hit format is now KNOWN (live capture 2026-07-12): each advisory is exactly three
> lines — header (`MHTG SIGMET J5 EMBD TS`), `Valid until: 2200z`, and the raw SIGMET
> body — separated by single CRLF with NO blank lines, and ActiveSky repeats the same
> advisory once per route-segment intersection (the capture carried the identical MHTG
> J5 block seven times). `ParseRouteAdvisories` therefore splits on header lines
> (`^\S{3,4}\s+(SIGMET|AIRMET)\s+\S+`, case-insensitive) and DEDUPLICATES by key
> (first line, case-insensitive, first-seen order). The defensive fallbacks remain:
> truly-blank lines still split, unrecognized text still renders verbatim as its own
> block, and the "No airmet/sigmet" sentence still parses to an empty list.

2. After the §12(c) box-rendering description, add:

> **Decode gating.** The box follows the same "Decode advisories into plain English"
> checkbox (`UserSettings.DecodeWeatherAdvisories`) as the Nearby Advisories box:
> unchecked shows the raw blocks (split + deduplicated); checked shows a rebuilt
> plain-English summary per advisory ("MHTG SIGMET J5: embedded thunderstorms, observed
> at 1830Z, tops FL520, moving west at 5 knots, no change expected." / "Valid until
> 2200Z.") built from the decoded fields — hazard, observed/forecast time, vertical
> extent, movement, trend. The `WI` lat/lon polygon is deliberately dropped from the
> decoded view (noise when read aloud; flip the checkbox off to see it). A block where
> nothing decodes renders verbatim even with decoding on — decoding never hides data.

3. Where §12(d) describes the announce phrase, update it to:

> The spoken announcement is ALWAYS decoded, independent of the checkbox — a screen
> reader must never speak raw SIGMET abbreviations: `"Route advisory: MHTG SIGMET J5,
> embedded thunderstorms, tops FL520."` (`BuildRouteAdvisoryAnnouncement`: identity +
> hazard + vertical extent; movement/trend/validity stay box-only). It falls back to
> the raw key when nothing decodes. Neutral phrasing (no "New") is retained — the same
> announce fires as the 15-minute reminder.

4. In §12(f) (the honest-verification caveat), note that the RESPONSE FORMAT and the
   parser/decoder are now live-verified against the 2026-07-12 capture (pinned as the
   test fixture), while the end-to-end announce timing of a NEW advisory appearing
   mid-flight still awaits a live occurrence.

- [ ] **Step 2: Full suite + build one last time**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass.
Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add docs/weather.md
git commit -m "docs(weather): route-advisory format is live-verified; document decoding

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## In-sim test plan (sim-facing; Robin runs with ActiveSky live)

Robin currently has AS loaded with en-route SIGMETs — ideal conditions:

1. Shift+R with decode checkbox OFF: Route Advisories box shows TWO blocks (MHTG J5 + YMMM T07), blank row between, no duplicates, raw text intact.
2. Check "Decode advisories into plain English", wait ≤30 s (or F5): box re-renders as the two decoded summaries with "Valid until" lines; no coordinates.
3. Uncheck: raw form returns on the next refresh.
4. Announce path (route advisories toggle on): since both advisories are already baselined, no announcement fires from the format change alone — expected. A genuinely new advisory appearing mid-flight should announce as "Route advisory: <identity>, <hazard>, <extent>."

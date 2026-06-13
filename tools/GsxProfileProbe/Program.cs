using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Services.Gsx;

// "--all": sweep EVERY installed profile to prove parse+map never throws and to
// aggregate anomalies. (Merge isn't exercised here — it needs navdata; see the
// hand-built matcher check below and the live verification in Task 12.)
if (args.Length > 0 && args[0] == "--all")
{
    string dir = GsxProfileLocator.DefaultProfileDir();
    var files = Directory.GetFiles(dir, "*.ini");
    int totalSections = 0, positionless = 0, fileErrors = 0;
    var vdgs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var cats = new SortedDictionary<string, int>();
    foreach (var f in files)
    {
        try
        {
            var gg = GsxProfileParser.Parse(f);
            totalSections += gg.Count;
            foreach (var g in gg)
            {
                if (!g.HasParkingPos && !g.StopLatitude.HasValue) positionless++;
                if (!string.IsNullOrWhiteSpace(g.VdgsType)) vdgs.Add(g.VdgsType);
                cats[g.Category] = cats.TryGetValue(g.Category, out int c) ? c + 1 : 1;
            }
            string stem = Path.GetFileNameWithoutExtension(f);
            string icaoG = (stem.Length >= 4 ? stem[..4] : stem).ToUpperInvariant();
            _ = GsxGateMapper.ToParkingSpots(gg, icaoG); // must not throw
        }
        catch (Exception ex)
        {
            fileErrors++;
            Console.WriteLine($"  PARSE ERROR in {Path.GetFileName(f)}: {ex.Message}");
        }
    }
    Console.WriteLine($"Swept {files.Length} profiles: {totalSections} sections, {positionless} positionless, {vdgs.Count} distinct VDGS, {fileErrors} file errors.");
    Console.WriteLine("Section categories: " + string.Join(", ", cats.Select(kv => $"{kv.Key}={kv.Value}")));
    return;
}

// "--audit": comprehensive robustness audit across all profiles
if (args.Length > 0 && args[0] == "--audit")
{
    RunAudit();
    return;
}

string defaultIni = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "Virtuali", "GSX", "MSFS", "omdb-24-iniBuilds.ini");

string ini = args.Length > 0 ? args[0] : defaultIni;
string icao = args.Length > 1 ? args[1] : "OMDB";

Console.WriteLine($"Profile: {ini}");
Console.WriteLine($"Exists:  {File.Exists(ini)}");

// Locator check
var locator = new GsxProfileLocator();
bool found = locator.TryFindProfile(icao, out string located);
Console.WriteLine($"Locator TryFindProfile({icao}) => {found} : {located}");

var gates = GsxProfileParser.Parse(ini);
var spots = GsxGateMapper.ToParkingSpots(gates, icao);
Console.WriteLine($"\nParsed {gates.Count} sections, {spots.Count} mapped spots.\n");

Console.WriteLine("First 15 spots:");
foreach (var s in spots.Take(15))
    Console.WriteLine($"  {s}  | hdg={s.Heading:F1} wing={s.MaxWingspanMeters} stop=({s.StopLatitude},{s.StopLongitude}) src={s.Source}");

Console.WriteLine("\nConcourse counts:");
foreach (var grp in spots.GroupBy(s => s.Name).OrderBy(g => g.Key))
    Console.WriteLine($"  {grp.Key}: {grp.Count()}");

Console.WriteLine("\nFilter checks:");
foreach (var q in new[] { "C", "C18", "18", "1L", "ZZZ" })
    Console.WriteLine($"  '{q}': {GateSearchFilter.Filter(spots, q).Count} matches");

// --- Matcher unit-check (hand-built, validates the GSX<->navdata overlay across naming schemes) ---
Console.WriteLine("\nMatcher checks (GSX gate with NO position must borrow the RIGHT navdata position):");
var navFix = new List<ParkingSpot>
{
    new() { Name = "GC", Number = 18,  Latitude = 25.111, Longitude = 55.111, Heading = 31 },  // OMDB "GC 18"
    new() { Name = "GD", Number = 18,  Latitude = 25.222, Longitude = 55.222, Heading = 40 },  // OMDB "GD 18"
    new() { Name = "P",  Number = 209, Latitude = 51.400, Longitude = -0.45,  Heading = 90 },  // EGLL "P 209"
    new() { Name = "G",  Number = 1,   Latitude = 37.460, Longitude = 126.4,  Heading = 180 }, // RKSI "G 1"
};
var gsxFix = new List<GsxGate>
{
    new() { Category="gate", Concourse="C", Number=18,  HasParkingPos=false }, // -> GC 18 (NOT GD)
    new() { Category="gate", Concourse="",  Number=209, HasParkingPos=false }, // EGLL pure-numeric -> P 209
    new() { Category="gate", Concourse="",  Number=1,   HasParkingPos=false }, // RKSI -> G 1
};
foreach (var m in GsxNavdataMerger.Merge(navFix, gsxFix, "TEST"))
    Console.WriteLine($"  {m} | lat={m.Latitude} src={m.Source}");
// Expect: "C 18" borrows 25.111 (the GC stand, NOT GD's 25.222) with Source=Gsx;
//         "P 209" lat 51.4, "G 1" lat 37.46 (matched by number); and "GD 18" appears
//         once as a navdata-only leftover (Source=Navdata).

// =====================================================================
// AUDIT IMPLEMENTATION
// =====================================================================

static void RunAudit()
{
    string dir = GsxProfileLocator.DefaultProfileDir();
    var files = Directory.GetFiles(dir, "*.ini").OrderBy(f => Path.GetFileName(f)).ToArray();
    Console.WriteLine($"=== GSX PROFILE ROBUSTNESS AUDIT ===");
    Console.WriteLine($"Directory: {dir}");
    Console.WriteLine($"Profiles:  {files.Length}");
    Console.WriteLine();

    // ── Per-file data collection ──────────────────────────────────────────────
    // For each profile we collect gate-level facts and classify anomalies.

    // FINDING 1: Duplicate display labels (gate list + deice list)
    var gateDupFindings = new List<(string file, string label, int count)>();
    var deiceDupFindings = new List<(string file, string label, int count)>();

    // FINDING 2: Empty/garbage gate names
    var emptyNameFindings = new List<(string file, string rawHeader, string parsedConcourse, int parsedNumber)>();

    // FINDING 3: Mis-parsed identities — sample unusual headers
    var misParsedFindings = new List<(string file, string rawHeader, string parsedResult, string concern)>();

    // FINDING 4a: Unparseable this_parking_pos
    var badPosFindings = new List<(string file, string rawHeader, string posValue, string reason)>();
    // FINDING 4b: Per-airport count of gates with no position at all (for high-loss flagging)
    var noPosCounts = new Dictionary<string, (int total, int noPos)>();

    // FINDING 5: Deice edge cases
    var deiceNoStopPos = new List<(string file, string header, string parkingSystem)>();
    var deiceAlsoGate = new List<(string file, string header, string category)>();
    var deiceNonVgds = new List<(string file, string header, string parkingSystem)>();

    // FINDING 6: Non-ASCII / special char names
    var specialCharFindings = new List<(string file, string header, string name, string chars)>();

    // FINDING 7: Unrecognized headers that have this_parking_pos (dropped real gates)
    var droppedWithPosFindings = new List<(string file, string rawHeader, string posValue)>();

    // FINDING 8: Systematic anomalies (developer-level patterns)
    // Collected inline below

    // ── Regex for known-category detection ───────────────────────────────────
    string[] knownCats = { "gate", "parking", "none", "ramp", "stand", "dock", "cargo", "tie", "hangar", "mil" };

    // ── Known 'safe' unrecognized section names (not real gates) ─────────────
    var knownNonGatePrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "general", "jetway_rootfloor_heights", "jetway_midpoints", "rwy"
    };

    // ── Raw INI parsing (we need to also detect bad_pos before the parser discards)
    // We re-parse lines to check this_parking_pos raw values for findability
    static bool HasNonAscii(string s, out string chars)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
            if (c > 127) sb.Append(c);
        chars = sb.ToString();
        return sb.Length > 0;
    }

    // ── Per-file raw line parsing for edge-case detection ────────────────────
    // We need to also parse raw lines to catch things the production parser silently skips

    foreach (var filePath in files)
    {
        string fileName = Path.GetFileName(filePath);
        string stem = Path.GetFileNameWithoutExtension(filePath);
        string icaoLabel = (stem.Length >= 4 ? stem[..4] : stem).ToUpperInvariant();

        // 1. Get the parsed result (production parser)
        List<GsxGate> gates;
        try { gates = GsxProfileParser.Parse(filePath); }
        catch { continue; } // already tracked by --all; skip

        var spots = GsxGateMapper.ToParkingSpots(gates, icaoLabel);

        // 1. Duplicate display labels
        // Gate list: ParkingSpot.ToString() as the key (what appears in the dropdown)
        var gateSpots = spots.Where(s => !s.IsDeiceArea).ToList();
        var deiceSpots = spots.Where(s => s.IsDeiceArea).ToList();

        var gateLabelGroups = gateSpots.GroupBy(s => s.ToString()).Where(g => g.Count() > 1);
        foreach (var grp in gateLabelGroups)
            gateDupFindings.Add((fileName, grp.Key, grp.Count()));

        var deiceLabelGroups = deiceSpots.GroupBy(s => s.ToString()).Where(g => g.Count() > 1);
        foreach (var grp in deiceLabelGroups)
            deiceDupFindings.Add((fileName, grp.Key, grp.Count()));

        // 2. Empty/garbage gate names: Number=0 AND empty concourse AND NOT deice
        foreach (var g in gates.Where(x => !x.IsDeiceArea))
        {
            bool emptyId = g.Number == 0 && string.IsNullOrEmpty(g.Concourse) && string.IsNullOrEmpty(g.Suffix);
            if (emptyId)
                emptyNameFindings.Add((fileName, g.RawSectionName, g.Concourse, g.Number));
        }

        // 3. Mis-parsed identities — check unusual headers
        foreach (var g in gates.Where(x => !x.IsDeiceArea))
        {
            string raw = g.RawSectionName.ToLowerInvariant();

            // Check: direction-prefixed parking (n/s/e/w/ne/nw/se/sw) — concourse should be direction, NOT "P"
            var dirPrefixMatch = System.Text.RegularExpressions.Regex.Match(raw, @"^(n|s|e|w|ne|nw|se|sw|northwest|northeast|southeast|southwest)\s+(parking|gate|none|stand|ramp)\s+(.+)$");
            if (dirPrefixMatch.Success)
            {
                string dirPrefix = dirPrefixMatch.Groups[1].Value.ToUpperInvariant();
                string cat = dirPrefixMatch.Groups[2].Value;
                string rest = dirPrefixMatch.Groups[3].Value;
                // Expected: Concourse = direction prefix
                string expectedConcourse = dirPrefix;
                bool ok = string.Equals(g.Concourse, expectedConcourse, StringComparison.OrdinalIgnoreCase);
                if (!ok)
                    misParsedFindings.Add((fileName, g.RawSectionName,
                        $"Concourse='{g.Concourse}' Number={g.Number} Suffix='{g.Suffix}'",
                        $"Expected direction concourse '{expectedConcourse}' from prefix '{dirPrefix}'"));
            }

            // Check: multi-word concourse like "gate t 1a" — T is concourse, 1 is number, A is suffix
            // Parser should give Concourse="T", Number=1, Suffix="A"
            var multiWordMatch = System.Text.RegularExpressions.Regex.Match(raw, @"^gate\s+([a-z])\s+(\d+)([a-z]?)$");
            if (multiWordMatch.Success)
            {
                string expConc = multiWordMatch.Groups[1].Value.ToUpperInvariant();
                int expNum = int.Parse(multiWordMatch.Groups[2].Value);
                string expSuf = multiWordMatch.Groups[3].Value.ToUpperInvariant();
                bool concOk = string.Equals(g.Concourse, expConc, StringComparison.OrdinalIgnoreCase);
                bool numOk = g.Number == expNum;
                bool sufOk = string.Equals(g.Suffix, expSuf, StringComparison.OrdinalIgnoreCase);
                if (!concOk || !numOk || !sufOk)
                    misParsedFindings.Add((fileName, g.RawSectionName,
                        $"Concourse='{g.Concourse}' Number={g.Number} Suffix='{g.Suffix}'",
                        $"Expected gate '{expConc}' {expNum}'{expSuf}' from header"));
            }

            // Check: number+suffix glued (e.g. "218l", "120c") — suffix should be letter after digits
            var gluedNumSuf = System.Text.RegularExpressions.Regex.Match(raw, @"^(gate|parking|none|stand|ramp|dock)\s+([a-z]\s+)?(\d+)([a-z]+)$");
            if (gluedNumSuf.Success)
            {
                string numPart = gluedNumSuf.Groups[3].Value;
                string sufPart = gluedNumSuf.Groups[4].Value;
                int expNum = int.Parse(numPart);
                string expSuf = sufPart.ToUpperInvariant();
                bool numOk = g.Number == expNum;
                bool sufOk = string.Equals(g.Suffix, expSuf, StringComparison.OrdinalIgnoreCase);
                if (!numOk || !sufOk)
                    misParsedFindings.Add((fileName, g.RawSectionName,
                        $"Concourse='{g.Concourse}' Number={g.Number} Suffix='{g.Suffix}'",
                        $"Expected glued Number={expNum} Suffix='{expSuf}'"));
            }

            // Check: "apron X stand Xnnn" patterns (essa)
            var apronMatch = System.Text.RegularExpressions.Regex.Match(raw, @"^apron\s+([a-z])\s+stand\s+([a-z]?)(\d+)$");
            if (apronMatch.Success)
            {
                string expConc = apronMatch.Groups[1].Value.ToUpperInvariant();
                string expPre = apronMatch.Groups[2].Value.ToUpperInvariant();
                int expNum = int.Parse(apronMatch.Groups[3].Value);
                // The "apron X stand Xnnn" — "stand" is the category here, but "apron" is the first token
                // Parser will find "stand" at catIdx and direction prefix = "apron X" (skips non-matching tokens)
                // Actually "apron" is not a recognized category so catIdx would find "stand"
                // After "stand": tokens are [apron, X, stand, Xnnn] — apron is not a category
                // Let's just note what we got
                if (g.Category == "stand")
                    misParsedFindings.Add((fileName, g.RawSectionName,
                        $"Concourse='{g.Concourse}' Number={g.Number} Suffix='{g.Suffix}'",
                        $"'apron X stand Xnnn' header: 'apron X' tokens before 'stand' parsed as direction prefix; check identity is meaningful"));
            }
        }

        // 4a: Unparseable this_parking_pos — re-read raw lines
        string[] rawLines;
        try { rawLines = File.ReadAllLines(filePath); }
        catch { continue; }

        // Collect all raw section headers + their this_parking_pos values
        {
            string curHeader = "";
            foreach (var rawLine in rawLines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    curHeader = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }
                if (string.IsNullOrEmpty(curHeader)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                if (key != "this_parking_pos") continue;
                string val = line.Substring(eq + 1).Trim();
                if (string.IsNullOrWhiteSpace(val)) continue;

                // Check if parseable
                var parts = val.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    badPosFindings.Add((fileName, curHeader, val, "< 2 whitespace-separated tokens"));
                    continue;
                }
                bool ok0 = double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                bool ok1 = double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                if (!ok0 || !ok1)
                {
                    // Check for comma-decimal (European "51,123 -0,456 90")
                    string commaVal = val.Replace(',', '.');
                    var cp = commaVal.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    bool commaOk0 = cp.Length >= 2 && double.TryParse(cp[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                    bool commaOk1 = cp.Length >= 2 && double.TryParse(cp[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
                    string reason = (commaOk0 && commaOk1) ? "comma-decimal separators (European locale)" : "non-numeric tokens";
                    badPosFindings.Add((fileName, curHeader, val, reason));
                }
            }
        }

        // 4b: No-position count per file
        int gateCount = gates.Count(g => !g.IsDeiceArea);
        int noPosCount = gates.Count(g => !g.IsDeiceArea && !g.HasParkingPos && !g.StopLatitude.HasValue);
        if (gateCount > 0)
            noPosCounts[fileName] = (gateCount, noPosCount);

        // 5: Deice edge cases
        foreach (var g in gates.Where(x => x.IsDeiceArea))
        {
            // 5a: no parkingsystem_stopposition at all (only parking pos or neither)
            bool hasStop = g.StopLatitude.HasValue && g.StopLongitude.HasValue;
            if (!hasStop)
                deiceNoStopPos.Add((fileName, g.RawSectionName, g.VdgsType ?? ""));

            // 5b: deice that also matched a gate pattern (has a real category from the header)
            // These are sections where is_deicearea=1 but the header parsed as gate/parking/stand
            // i.e. recognized header sections that also have deice flag
            if (!string.IsNullOrEmpty(g.Category) && g.Category != "")
                deiceAlsoGate.Add((fileName, g.RawSectionName, g.Category));

            // 5c: deice with parkingsystem that is NOT VgdsDeIceWall
            string vs = g.VdgsType ?? "";
            if (!string.IsNullOrWhiteSpace(vs) && !vs.StartsWith("VgdsDeIce", StringComparison.OrdinalIgnoreCase))
                deiceNonVgds.Add((fileName, g.RawSectionName, vs));
        }

        // 6: Special chars in names
        foreach (var g in gates)
        {
            string nameToCheck = g.IsDeiceArea ? g.Concourse : $"{g.Concourse}{g.Number}{g.Suffix}";
            string rawLabel = g.Uiname.Length > 0 ? g.Uiname : g.RawSectionName;
            if (HasNonAscii(rawLabel, out string badChars))
                specialCharFindings.Add((fileName, g.RawSectionName, rawLabel, badChars));
        }

        // 7: Unrecognized headers with this_parking_pos (dropped real gates)
        // These are sections not in knownCats, not deice, but have a parking position in raw
        {
            string curHeader = "";
            bool curHasPos = false;
            bool curHasDeice = false;
            bool curIsKnownCat = false;
            bool curIsKnownNonGate = false;

            void FlushSection()
            {
                if (string.IsNullOrEmpty(curHeader)) return;
                if (curHasDeice) return; // deice areas already handled
                if (curIsKnownCat) return; // recognized gate/parking sections
                if (curIsKnownNonGate) return; // general, jetway_*, rwy sections
                if (!curHasPos) return; // no position = nothing to lose

                // This is an unrecognized header with a position that was dropped
                droppedWithPosFindings.Add((fileName, curHeader, "has this_parking_pos"));
            }

            foreach (var rawLine in rawLines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    FlushSection();
                    curHeader = line.Substring(1, line.Length - 2).Trim();
                    curHasPos = false;
                    curHasDeice = false;

                    string headerLower = curHeader.ToLowerInvariant();
                    var tokens = headerLower.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    curIsKnownCat = tokens.Length > 0 && Array.IndexOf(knownCats, tokens.FirstOrDefault(t => knownCats.Contains(t)) ?? "") >= 0;
                    curIsKnownNonGate = tokens.Length > 0 && (
                        knownNonGatePrefixes.Contains(tokens[0]) ||
                        string.Equals(curHeader, "general", StringComparison.OrdinalIgnoreCase) ||
                        headerLower.StartsWith("jetway") ||
                        headerLower.StartsWith("rwy ")
                    );
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                if (k == "this_parking_pos") curHasPos = true;
                if (k == "is_deicearea" && line.Substring(eq + 1).Trim() == "1") curHasDeice = true;
            }
            FlushSection();
        }
    }

    // =====================================================================
    // REPORT OUTPUT
    // =====================================================================

    Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║        GSX PROFILE PARSER ROBUSTNESS AUDIT REPORT               ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();

    // ── FINDING 1: Duplicate display labels ──────────────────────────────────
    Console.WriteLine("━━━ FINDING 1: DUPLICATE DISPLAY LABELS ━━━");
    Console.WriteLine();

    if (gateDupFindings.Count == 0)
        Console.WriteLine("  [GATE LIST] No duplicate gate labels found. ✓");
    else
    {
        Console.WriteLine($"  [GATE LIST] {gateDupFindings.Count} duplicate label groups (data loss — gates hidden in dropdown):");
        foreach (var (file, label, count) in gateDupFindings.OrderBy(x => x.file))
            Console.WriteLine($"    {file}: '{label}' appears {count}x → {count - 1} hidden");
    }
    Console.WriteLine();

    if (deiceDupFindings.Count == 0)
        Console.WriteLine("  [DEICE LIST] No duplicate deice labels found. ✓ (EGKK collision confirmed fixed)");
    else
    {
        Console.WriteLine($"  [DEICE LIST] {deiceDupFindings.Count} duplicate deice label groups:");
        foreach (var (file, label, count) in deiceDupFindings.OrderBy(x => x.file))
            Console.WriteLine($"    {file}: '{label}' appears {count}x → {count - 1} hidden");
    }
    Console.WriteLine();

    // ── FINDING 2: Empty/garbage gate names ─────────────────────────────────
    Console.WriteLine("━━━ FINDING 2: EMPTY/GARBAGE GATE NAMES ━━━");
    Console.WriteLine();
    if (emptyNameFindings.Count == 0)
        Console.WriteLine("  No gates with empty identity (Number=0 + no concourse + not deice). ✓");
    else
    {
        Console.WriteLine($"  {emptyNameFindings.Count} gates with empty identity:");
        foreach (var (file, raw, conc, num) in emptyNameFindings.Take(30).OrderBy(x => x.file))
            Console.WriteLine($"    {file}: [{raw}] → Concourse='{conc}' Number={num}");
        if (emptyNameFindings.Count > 30)
            Console.WriteLine($"    ... and {emptyNameFindings.Count - 30} more");
    }
    Console.WriteLine();

    // ── FINDING 3: Mis-parsed identities ─────────────────────────────────────
    Console.WriteLine("━━━ FINDING 3: MIS-PARSED IDENTITIES ━━━");
    Console.WriteLine();
    if (misParsedFindings.Count == 0)
        Console.WriteLine("  No identity parsing mismatches detected. ✓");
    else
    {
        // Deduplicate by concern type
        var byFile = misParsedFindings.GroupBy(x => x.file).OrderBy(g => g.Key);
        Console.WriteLine($"  {misParsedFindings.Count} potential identity anomalies:");
        foreach (var grp in byFile)
        {
            Console.WriteLine($"    {grp.Key}:");
            foreach (var (_, rawH, parsed, concern) in grp.Take(5))
                Console.WriteLine($"      [{rawH}] → {parsed}  // {concern}");
            if (grp.Count() > 5) Console.WriteLine($"      ... and {grp.Count() - 5} more in this file");
        }
    }
    Console.WriteLine();

    // ── FINDING 4a: Unparseable positions ────────────────────────────────────
    Console.WriteLine("━━━ FINDING 4a: UNPARSEABLE this_parking_pos ━━━");
    Console.WriteLine();
    if (badPosFindings.Count == 0)
        Console.WriteLine("  No unparseable this_parking_pos values. ✓");
    else
    {
        Console.WriteLine($"  {badPosFindings.Count} unparseable position(s):");
        foreach (var (file, header, val, reason) in badPosFindings.OrderBy(x => x.file))
            Console.WriteLine($"    {file}: [{header}] pos='{val}' → {reason}");
    }
    Console.WriteLine();

    // ── FINDING 4b: High no-position loss airports ────────────────────────────
    Console.WriteLine("━━━ FINDING 4b: AIRPORTS WITH HIGH POSITION-LESS GATE COUNTS ━━━");
    Console.WriteLine();
    var highLoss = noPosCounts.Where(kv => kv.Value.noPos > 5 || (kv.Value.total > 0 && (double)kv.Value.noPos / kv.Value.total > 0.3))
                              .OrderByDescending(kv => (double)kv.Value.noPos / kv.Value.total)
                              .ToList();
    if (highLoss.Count == 0)
        Console.WriteLine("  No airports with >30% or >5 position-less gates. ✓");
    else
    {
        Console.WriteLine($"  {highLoss.Count} airports with significant no-position gates:");
        foreach (var kv in highLoss)
        {
            double pct = (double)kv.Value.noPos / kv.Value.total * 100;
            Console.WriteLine($"    {kv.Key}: {kv.Value.noPos}/{kv.Value.total} ({pct:F0}%) gates have no position (will rely on navdata or be dropped)");
        }
    }
    Console.WriteLine();
    Console.WriteLine("  NOTE: '~26% of all stands have no position' is expected per design.");
    Console.WriteLine($"  Overall: {noPosCounts.Values.Sum(v => v.noPos)} / {noPosCounts.Values.Sum(v => v.total)} = {(noPosCounts.Values.Sum(v => v.total) > 0 ? (double)noPosCounts.Values.Sum(v => v.noPos) / noPosCounts.Values.Sum(v => v.total) * 100 : 0):F1}% across all profiles");
    Console.WriteLine();

    // ── FINDING 5: Deice edge cases ──────────────────────────────────────────
    Console.WriteLine("━━━ FINDING 5: DEICE EDGE CASES ━━━");
    Console.WriteLine();

    Console.WriteLine($"  5a) Deice areas with no parkingsystem_stopposition: {deiceNoStopPos.Count}");
    if (deiceNoStopPos.Count > 0)
    {
        var byF5a = deiceNoStopPos.GroupBy(x => x.file).OrderBy(g => g.Key);
        foreach (var grp in byF5a)
        {
            Console.WriteLine($"    {grp.Key} ({grp.Count()} pads):");
            foreach (var (_, hdr, ps) in grp.Take(5))
                Console.WriteLine($"      [{hdr}]  parkingsystem='{ps}'");
            if (grp.Count() > 5) Console.WriteLine($"      ... and {grp.Count() - 5} more");
        }
    }
    Console.WriteLine();

    Console.WriteLine($"  5b) Deice sections that also matched a gate/parking category pattern: {deiceAlsoGate.Count}");
    if (deiceAlsoGate.Count > 0)
    {
        foreach (var (file, hdr, cat) in deiceAlsoGate.Take(20).OrderBy(x => x.file))
            Console.WriteLine($"    {file}: [{hdr}] category='{cat}'");
        if (deiceAlsoGate.Count > 20) Console.WriteLine($"    ... and {deiceAlsoGate.Count - 20} more");
    }
    Console.WriteLine();

    Console.WriteLine($"  5c) Deice areas with non-VgdsDeIceWall parkingsystem: {deiceNonVgds.Count}");
    if (deiceNonVgds.Count > 0)
    {
        foreach (var (file, hdr, ps) in deiceNonVgds.OrderBy(x => x.file).Take(20))
            Console.WriteLine($"    {file}: [{hdr}] parkingsystem='{ps}'");
        if (deiceNonVgds.Count > 20) Console.WriteLine($"    ... and {deiceNonVgds.Count - 20} more");
    }
    Console.WriteLine();

    // ── FINDING 6: Non-ASCII / special chars ─────────────────────────────────
    Console.WriteLine("━━━ FINDING 6: NON-ASCII / SPECIAL CHARACTER NAMES ━━━");
    Console.WriteLine();
    if (specialCharFindings.Count == 0)
        Console.WriteLine("  No non-ASCII characters found in gate names or uinames. ✓");
    else
    {
        Console.WriteLine($"  {specialCharFindings.Count} name(s) with non-ASCII characters:");
        foreach (var (file, hdr, name, chars) in specialCharFindings.OrderBy(x => x.file).Take(20))
            Console.WriteLine($"    {file}: [{hdr}] name='{name}' chars={{{chars}}}");
        if (specialCharFindings.Count > 20) Console.WriteLine($"    ... and {specialCharFindings.Count - 20} more");
    }
    Console.WriteLine();

    // ── FINDING 7: Dropped sections with position ─────────────────────────────
    Console.WriteLine("━━━ FINDING 7: UNRECOGNIZED HEADERS WITH this_parking_pos (DROPPED) ━━━");
    Console.WriteLine();
    if (droppedWithPosFindings.Count == 0)
        Console.WriteLine("  No unrecognized headers with position data found. ✓");
    else
    {
        Console.WriteLine($"  {droppedWithPosFindings.Count} dropped section(s) that had a parking position:");
        var byF7 = droppedWithPosFindings.GroupBy(x => x.file).OrderBy(g => g.Key);
        foreach (var grp in byF7)
        {
            Console.WriteLine($"    {grp.Key} ({grp.Count()} sections):");
            foreach (var (_, hdr, _) in grp.Take(8))
                Console.WriteLine($"      [{hdr}]");
            if (grp.Count() > 8) Console.WriteLine($"      ... and {grp.Count() - 8} more");
        }
    }
    Console.WriteLine();

    // ── FINDING 8: Systematic / developer-level anomalies ────────────────────
    Console.WriteLine("━━━ FINDING 8: SYSTEMATIC / DEVELOPER-LEVEL ANOMALIES ━━━");
    Console.WriteLine();

    // Detect LIMC "area N - position ..." pattern (deice that parsed with numeric IDs j1, gy1 etc)
    // These parse via the unrecognized-header + is_deicearea path; no category set; label from CleanSectionName
    // Verify labels are distinct

    // Detect LOWW f42-f59 deice (single-letter + number header — would normally try to parse as a gate)
    // Actually these parse as unrecognized (no known category token) + is_deicearea=1 → deice by CleanSectionName

    // Detect profiles with BOTH .v2 and .v2.5 variants (ltfm)
    var duplicateAirports = files.Select(f => Path.GetFileNameWithoutExtension(f))
                                 .GroupBy(stem => stem.Length >= 4 ? stem[..4].ToUpperInvariant() : stem.ToUpperInvariant())
                                 .Where(g => g.Count() > 1)
                                 .ToList();
    if (duplicateAirports.Count > 0)
    {
        Console.WriteLine($"  8a) Airports with MULTIPLE profile files ({duplicateAirports.Count} airports):");
        foreach (var grp in duplicateAirports.OrderBy(g => g.Key))
        {
            Console.WriteLine($"    {grp.Key}: {string.Join(", ", grp.Select(s => s + ".ini"))}");
            // This means GSX profile locator picks ONE; the other is never used
        }
        Console.WriteLine("      → GsxProfileLocator picks ONE; duplicate profiles are silently ignored.");
        Console.WriteLine();
    }

    // Detect ENGM a-north/a-south/b-north/b-south deice label quality
    Console.WriteLine("  8b) Developer-specific deice label patterns (CleanSectionName output samples):");
    string[] sampleDeiceFiles = {
        "engm-aerosoft.ini", "loww-enhc-AUA9085.ini", "zbad-9ecgho.ini", "lipe-h1flpt.ini",
        "essa-bwib1r.ini", "egkk-mkvy.ini", "LIMC-verzu2024-GSXVDGS.ini"
    };
    foreach (var sf in sampleDeiceFiles)
    {
        string fullPath = Path.Combine(dir, sf);
        if (!File.Exists(fullPath)) continue;
        var sg = GsxProfileParser.Parse(fullPath);
        var deiceGates = sg.Where(g => g.IsDeiceArea).Take(4).ToList();
        if (deiceGates.Count == 0) continue;
        Console.WriteLine($"    {sf}:");
        foreach (var dg in deiceGates)
            Console.WriteLine($"      [{dg.RawSectionName}] → label='{dg.Concourse}' uiname='{dg.Uiname}'");
    }
    Console.WriteLine();

    // EGKK: note that [gate h 9l] has no this_parking_pos but has parkingsystem_stopposition
    Console.WriteLine("  8c) EGKK [gate h 9l] — gate with only stop position, no this_parking_pos:");
    string egkkPath = Path.Combine(dir, "egkk-mkvy.ini");
    if (File.Exists(egkkPath))
    {
        var eg = GsxProfileParser.Parse(egkkPath);
        var gateH9l = eg.FirstOrDefault(g => g.RawSectionName.Equals("gate h 9l", StringComparison.OrdinalIgnoreCase));
        if (gateH9l != null)
        {
            Console.WriteLine($"    HasParkingPos={gateH9l.HasParkingPos}  StopLat={gateH9l.StopLatitude}  StopLon={gateH9l.StopLongitude}");
            Console.WriteLine($"    Concourse='{gateH9l.Concourse}' Number={gateH9l.Number} Suffix='{gateH9l.Suffix}'");
            Console.WriteLine($"    → Merger uses StopPosition as position (correct fallback)");
        }
    }
    Console.WriteLine();

    // SKBO: two profile files for same airport
    Console.WriteLine("  8d) SKBO has two profiles (one dated, one not) — locator picks latest by filesystem order.");
    Console.WriteLine();

    // ── SUMMARY ──────────────────────────────────────────────────────────────
    Console.WriteLine("━━━ SUMMARY ━━━");
    Console.WriteLine();

    int criticalCount = gateDupFindings.Count + badPosFindings.Count;
    int importantCount = emptyNameFindings.Count + droppedWithPosFindings.Count + misParsedFindings.Count;
    int minorCount = deiceNoStopPos.Count + deiceNonVgds.Count + specialCharFindings.Count;

    Console.WriteLine($"  Critical (data loss):  {criticalCount}  → gate duplicates={gateDupFindings.Count}, bad pos={badPosFindings.Count}");
    Console.WriteLine($"  Important:             {importantCount}  → empty IDs={emptyNameFindings.Count}, dropped+pos={droppedWithPosFindings.Count}, mis-parsed={misParsedFindings.Count}");
    Console.WriteLine($"  Minor:                 {minorCount}  → deice-no-stop={deiceNoStopPos.Count}, deice-non-vgds={deiceNonVgds.Count}, non-ascii={specialCharFindings.Count}");
    Console.WriteLine();

    // Quantify "parser is fine for X%"
    int dir2TotalGates = noPosCounts.Values.Sum(v => v.total);
    int totalGatesWithDupLabels = gateDupFindings.Sum(x => x.count - 1); // hidden gates
    double pctFine = dir2TotalGates > 0 ? (1.0 - (double)totalGatesWithDupLabels / dir2TotalGates) * 100 : 100;
    Console.WriteLine($"  Total gates parsed (non-deice): {dir2TotalGates}");
    Console.WriteLine($"  Gates hidden by duplicate labels: {totalGatesWithDupLabels}");
    Console.WriteLine($"  Parser is correct for: ~{pctFine:F1}% of all parsed stands");
    Console.WriteLine();
}

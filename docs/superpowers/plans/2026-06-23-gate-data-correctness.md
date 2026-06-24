# Gate-data correctness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make gate identity correct and clean everywhere — online sources may add a searchable alias for the *same* stand but can never overwrite a gate's identity or position — so labels, GSX selection, taxi/VDGS, and search all behave.

**Architecture:** A new pure `StandId` parser + a pure `GateAliasResolver` replace the augmentation's "nearest online name within 30 m" gate fill with an **identity-matched, alias-only, idempotent** rule. `ParkingSpot` renders `Gate {n}` for empty-name gate types and tags online aliases `(online)`. Search consults aliases. `TaxiAssistForm` marks unreachable stands `(no taxi route)` and warns on select. GSX selection needs no change — it self-corrects once the identity is clean.

**Tech Stack:** C# 13 / .NET 9, WinForms; verification via the existing `tools/TaxiAugmentProbe` console probe (no xUnit in this repo) + an in-sim test plan the owner runs.

> **Execution gating:** The repo owner has a standing instruction — *do not build, commit, or push until they give the OK*. The build/commit steps below are part of the plan and run **at execution time, once authorized**. Build to `bin\x64` (`dotnet build MSFSBlindAssist.sln -c Debug`); close MSFSBA before building (the exe is file-locked while running). Commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## Spec

This plan implements [docs/superpowers/specs/2026-06-23-gate-data-correctness-design.md](../specs/2026-06-23-gate-data-correctness-design.md).

## File structure

| File | Responsibility | Change |
|---|---|---|
| `MSFSBlindAssist/Services/StandId.cs` | Parse any label/ref → `(letter, number, suffix)` | **Create** |
| `MSFSBlindAssist/Services/TaxiAugment/GateAliasResolver.cs` | Pure identity-matched alias resolver | **Create** |
| `MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs` | Use the resolver; delete the fill branch | Modify `GetParkingSpots` (~L94-159) |
| `MSFSBlindAssist/Database/Models/ParkingSpot.cs` | `Gate {n}` identity; `(online)` alias tag | Modify `Describe()`/`ToString()` (L153-189) |
| `MSFSBlindAssist/Services/GateSearchFilter.cs` | Match aliases too | Modify `Matches`/`Filter` (L24-35) |
| `MSFSBlindAssist/Forms/TaxiAssistForm.cs` | `(no taxi route)` mark + warn; `(online)` alias entries | Modify resolve loop (~L1034-1044), build loop (~L1090-1114), `OnCalculateClicked` (~L2005-2009) |
| `tools/TaxiAugmentProbe/Program.cs` | Probe assertions | Append a gate-correctness block |

`GsxGateSelector` and `DockingGuidanceManager` are intentionally **not** changed — both consume the now-clean identity and `GsxGateSelector` already announces its own outcome ([`TaxiAssistForm.cs:2103`](../../MSFSBlindAssist/Forms/TaxiAssistForm.cs)).

---

## Task 1: `StandId` parser

**Files:**
- Create: `MSFSBlindAssist/Services/StandId.cs`
- Test: `tools/TaxiAugmentProbe/Program.cs` (append)

- [ ] **Step 1: Write the failing test** — append to the END of `tools/TaxiAugmentProbe/Program.cs`, *before* the final `Console.WriteLine(failures==0 ...)` / `return` lines (L173-174). Also add `using MSFSBlindAssist.Services;` to the top usings (after L2).

```csharp
// ──────────────────────────────────────────────────────────────────────
// Task 1: StandId parser
// ──────────────────────────────────────────────────────────────────────
Check(StandId.Parse("Gate 11B") is { Letter: "", Number: 11, Suffix: "B", HasNumber: true }, "StandId: 'Gate 11B' -> (,11,B)");
Check(StandId.Parse("A51")      is { Letter: "A", Number: 51, HasNumber: true },              "StandId: 'A51' -> (A,51)");
Check(StandId.Parse("N 1")      is { Letter: "N", Number: 1,  HasNumber: true },              "StandId: 'N 1' -> (N,1)");
Check(StandId.Parse("51")       is { Letter: "", Number: 51, HasNumber: true },               "StandId: '51' -> (,51)");
Check(StandId.Parse("53A")      is { Letter: "", Number: 53, Suffix: "A", HasNumber: true },  "StandId: '53A' -> (,53,A)");
Check(StandId.Parse("F211")     is { Letter: "F", Number: 211, HasNumber: true },             "StandId: 'F211' -> (F,211)");
Check(StandId.Parse("P 209")    is { Letter: "P", Number: 209, HasNumber: true },             "StandId: 'P 209' -> (P,209)");
Check(StandId.Parse("").HasNumber == false,                                                   "StandId: '' -> no number");
Check(StandId.Parse("N") is { Letter: "N", HasNumber: false },                                "StandId: 'N' -> letter N, no number");
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project tools/TaxiAugmentProbe`
Expected: FAIL to compile ("StandId does not exist").

- [ ] **Step 3: Create `MSFSBlindAssist/Services/StandId.cs`**

```csharp
using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Canonical parse of a stand/gate label or ref into (leading letter, number, trailing suffix).
/// Shared by the gate-alias resolver and search so they agree on identity. Pure, allocation-light.
/// </summary>
public readonly record struct StandId(string Letter, int Number, string Suffix, bool HasNumber)
{
    private static readonly Regex Shape = new(@"^([A-Z]*)([0-9]+)([A-Z]*)$", RegexOptions.Compiled);

    /// <summary>Letter+number+suffix, e.g. "A51", "55A", "N3" (no number → letter+suffix).</summary>
    public string Canonical =>
        HasNumber ? Letter + Number.ToString(CultureInfo.InvariantCulture) + Suffix : Letter + Suffix;

    public static StandId Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new StandId("", 0, "", false);

        // Uppercase, drop ALL whitespace: "N 1" -> "N1", "P 209" -> "P209".
        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (char c in raw)
            if (!char.IsWhiteSpace(c)) sb.Append(char.ToUpperInvariant(c));
        string s = sb.ToString();

        // Strip ONE leading descriptor word: "GATE11B" -> "11B", "STAND5" -> "5", "PARKING209" -> "209".
        foreach (var w in new[] { "GATE", "STAND", "PARKING" })
            if (s.StartsWith(w, StringComparison.Ordinal) && s.Length > w.Length)
            { s = s.Substring(w.Length); break; }

        var m = Shape.Match(s);
        if (m.Success)
            return new StandId(m.Groups[1].Value,
                               int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                               m.Groups[3].Value, true);

        // No number (a bare letter like "N", or a word like "HAWKER").
        return new StandId(s, 0, "", false);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project tools/TaxiAugmentProbe`
Expected: the new `StandId:` lines PASS; existing lines still PASS; ends `ALL PASS`.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/StandId.cs tools/TaxiAugmentProbe/Program.cs
git commit -m "feat(gate-data): StandId identity parser + probe coverage

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `GateAliasResolver` — identity-matched, alias-only, idempotent

**Files:**
- Create: `MSFSBlindAssist/Services/TaxiAugment/GateAliasResolver.cs`
- Test: `tools/TaxiAugmentProbe/Program.cs` (append)

- [ ] **Step 1: Write the failing test** — append after the Task 1 block.

```csharp
// ──────────────────────────────────────────────────────────────────────
// Task 2: GateAliasResolver — identity-matched, alias-only, idempotent
// ──────────────────────────────────────────────────────────────────────
ParkingSpot Gate(string name, int num, int type = 11, string suffix = "")
    => new ParkingSpot { Name = name, Number = num, Type = type, Suffix = suffix, Latitude = 45.0, Longitude = -73.0 };

// Online stands (coords irrelevant here — distance check disabled with maxMeters: 0).
var onlineStands = new List<(string, double, double)>
{
    ("Gate 11B", 45.0, -73.0), ("Gate 15", 45.0, -73.0), ("51", 45.0, -73.0),
    ("A51", 45.0, -73.0), ("53A", 45.0, -73.0), ("S3", 45.0, -73.0), ("N3", 45.0, -73.0),
};

var a15 = GateAliasResolver.ResolveAliases(Gate("", 15), onlineStands, 0);
Check(a15.Count == 0, $"Resolver: gate 15 gets NO alias — '11B' rejected (number mismatch), '15' restatement (got [{string.Join(",", a15)}])");

var a51 = GateAliasResolver.ResolveAliases(Gate("", 51), onlineStands, 0);
Check(a51.Contains("A51"), $"Resolver: gate 51 aliases 'A51' (concourse prefix) (got [{string.Join(",", a51)}])");
Check(!a51.Contains("51"), "Resolver: gate 51 does NOT alias the bare '51' restatement");

var a53 = GateAliasResolver.ResolveAliases(Gate("", 53), onlineStands, 0);
Check(a53.Contains("53A"), $"Resolver: gate 53 aliases MARS '53A' (got [{string.Join(",", a53)}])");

var aN3 = GateAliasResolver.ResolveAliases(Gate("N", 3), onlineStands, 0);
Check(!aN3.Contains("S3"), "Resolver: gate 'N 3' never adopts 'S3' (letter disagreement)");
Check(aN3.Count == 0, $"Resolver: gate 'N 3' gets no alias ('N3' is a restatement) (got [{string.Join(",", aN3)}])");

var r1 = GateAliasResolver.ResolveAliases(Gate("", 51), onlineStands, 0);
var r2 = GateAliasResolver.ResolveAliases(Gate("", 51), onlineStands, 0);
Check(r1.SequenceEqual(r2), "Resolver: idempotent — two runs identical");

var farStands = new List<(string, double, double)> { ("A51", 46.0, -73.0) }; // ~111 km away
Check(GateAliasResolver.ResolveAliases(Gate("", 51), farStands, 150).Count == 0, "Resolver: same-number stand >150 m away rejected as data error");
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project tools/TaxiAugmentProbe`
Expected: FAIL to compile ("GateAliasResolver does not exist").

- [ ] **Step 3: Create `MSFSBlindAssist/Services/TaxiAugment/GateAliasResolver.cs`**

```csharp
using MSFSBlindAssist.Database.Models;

namespace MSFSBlindAssist.Services.TaxiAugment;

/// <summary>
/// Pure, idempotent resolver: given an authoritative gate (GSX/sqlite) and the online stands,
/// returns the searchable aliases the online layer contributes — number-matched, letter-agreeing,
/// extra-info-only. NEVER returns a position and NEVER affects which gates are selectable.
/// </summary>
public static class GateAliasResolver
{
    /// <param name="maxMeters">Distance sanity backstop; a same-number stand farther than this
    /// is treated as a data error and skipped. Pass 0 to disable (identity-only).</param>
    public static List<string> ResolveAliases(
        ParkingSpot gate,
        IReadOnlyList<(string Name, double Lat, double Lon)> onlineStands,
        double maxMeters = 150.0)
    {
        var result = new List<string>();
        if (gate == null || onlineStands == null || onlineStands.Count == 0) return result;
        if (gate.Number <= 0) return result; // no numeric identity → never match (safety)

        string gateLetter = MSFSBlindAssist.Services.StandId.Parse(gate.Name).Letter; // navdata letter ("N"/…) or ""
        string gateSuffix = gate.Suffix ?? "";
        string gateCanonical = (gateLetter + gate.Number + gateSuffix).ToUpperInvariant();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { gateCanonical };

        foreach (var (name, lat, lon) in onlineStands)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var oid = MSFSBlindAssist.Services.StandId.Parse(name);
            if (!oid.HasNumber || oid.Number != gate.Number) continue; // number must match

            // Letter agreement: a gate WITH a letter requires the same letter ("N3" never matches "S3"
            // or bare "3"); a gate WITHOUT a letter may take an online concourse prefix ("A51" on 51).
            if (gateLetter.Length > 0 && oid.Letter != gateLetter) continue;

            if (maxMeters > 0 &&
                TaxiGeo.HaversineMeters(gate.Latitude, gate.Longitude, lat, lon) > maxMeters) continue;

            string canonical = oid.Canonical; // "A51", "55A", …
            if (string.Equals(canonical, gateCanonical, StringComparison.OrdinalIgnoreCase)) continue; // restatement
            if (!seen.Add(canonical)) continue; // dedup

            result.Add(canonical);
        }
        return result;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project tools/TaxiAugmentProbe`
Expected: all Task 2 `Resolver:` lines PASS; ends `ALL PASS`.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiAugment/GateAliasResolver.cs tools/TaxiAugmentProbe/Program.cs
git commit -m "feat(gate-data): GateAliasResolver — identity-matched, alias-only, idempotent

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Rewire `AugmentingAirportDataProvider.GetParkingSpots`

Replaces the nearest-within-30 m fill/alias (which overwrote `Name`, grabbed neighbors, and could double) with the pure resolver. Never writes `Name`/position; recomputes aliases fresh (idempotent).

**Files:**
- Modify: `MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs` (`GetParkingSpots`, ~L94-159)

- [ ] **Step 1: Replace the method body.** Replace the whole `GetParkingSpots` method (from `public List<ParkingSpot> GetParkingSpots(string icao)` through its closing `}` at ~L159) with:

```csharp
    /// <summary>
    /// Returns parking spots, attaching online-sourced searchable ALIASES to existing navdata gates.
    /// Identity-matched (same number, agreeing letter), alias-only, idempotent. NEVER overwrites a
    /// gate's Name or position, and NEVER adds a selectable gate — so online data cannot move where
    /// you taxi (anti-grass). No-op when disabled or uncached.
    /// </summary>
    public List<ParkingSpot> GetParkingSpots(string icao)
    {
        var nav = _base.GetParkingSpots(icao);
        if (!Enabled || nav == null || nav.Count == 0) return nav;
        if (!_cache.TryLoad(icao, out var sources) || sources == null) return nav;

        var online = sources
            .SelectMany(s => s.Parking)
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .ToList();
        if (online.Count == 0) return nav;

        foreach (var spot in nav)
            spot.Aliases = GateAliasResolver.ResolveAliases(spot, online); // fresh + deterministic → idempotent

        return nav;
    }
```

- [ ] **Step 2: Confirm it compiles (build the solution).**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded. (`GateAliasResolver` and `_base`/`_cache` are already in scope; the deleted block's `maxMeters`/`TaxiGeo`/`TaxiDataMerger` references are gone.)

- [ ] **Step 3: Re-run the probe** (the resolver path is exercised by Task 2; this confirms no regression).

Run: `dotnet run --project tools/TaxiAugmentProbe`
Expected: `ALL PASS`.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Services/TaxiAugment/AugmentingAirportDataProvider.cs
git commit -m "fix(gate-data): identity-matched alias enrichment; stop overwriting gate identity

Replaces nearest-within-30m gate fill (which grabbed neighbors — e.g. CYUL gate 15 -> 'Gate 11B'
from offset apt.dat ramps — and could double the alias) with GateAliasResolver. Never writes Name
or position; aliases recomputed fresh each call (idempotent).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: `ParkingSpot` display — `Gate {n}` identity + `(online)` alias tag

**Files:**
- Modify: `MSFSBlindAssist/Database/Models/ParkingSpot.cs` (`Describe()` L153-177, `ToString()` L179-189)
- Test: `tools/TaxiAugmentProbe/Program.cs` (append)

- [ ] **Step 1: Write the failing test** — append after the Task 2 block.

```csharp
// ──────────────────────────────────────────────────────────────────────
// Task 4: ParkingSpot display — 'Gate {n}' identity + '(online)' alias tag
// ──────────────────────────────────────────────────────────────────────
var disp51 = new ParkingSpot { Name = "", Number = 51, Type = 13 /*Gate Heavy*/, HasJetway = true };
Check(disp51.Describe().StartsWith("Gate 51"), $"Display: empty-name gate -> 'Gate 51' (got '{disp51.Describe()}')");
Check(!disp51.Describe().Contains("Spot"),      "Display: gate type never says 'Spot'");

var rampSpot = new ParkingSpot { Name = "", Number = 7, Type = 6 /*Ramp Cargo*/ };
Check(rampSpot.Describe().StartsWith("Spot 7"), $"Display: non-gate empty-name keeps 'Spot 7' (got '{rampSpot.Describe()}')");

disp51.Aliases.Add("A51");
Check(disp51.ToString().Contains("A51") && disp51.ToString().Contains("(online)"),
      $"Display: alias tagged '(online)' (got '{disp51.ToString()}')");
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project tools/TaxiAugmentProbe`
Expected: FAIL — `disp51.Describe()` returns `"Spot 51 - Gate Heavy (Jetway)"`, and `ToString()` says `"(also A51)"`.

- [ ] **Step 3: Edit `ParkingSpot.cs`.**

(a) In `Describe()`, change the empty-name numbered branch (currently L164-165):

```csharp
        else if (!string.IsNullOrEmpty(numberPart))
            baseDescription = $"Spot {numberPart} - {GetParkingType()}";
```

to:

```csharp
        else if (!string.IsNullOrEmpty(numberPart))
            baseDescription = IsGateType()
                ? $"Gate {numberPart} - {GetParkingType()}"
                : $"Spot {numberPart} - {GetParkingType()}";
```

(b) Add this helper just below `GetFilterCategory()` (after its closing `}` at ~L98):

```csharp
    /// <summary>True for gate-type stands (Gate Small/Medium/Large/Heavy/Extra) — used to render
    /// an empty-name gate as "Gate {n}" rather than the generic "Spot {n}".</summary>
    private bool IsGateType() => Type is 9 or 10 or 11 or 13 or 14;
```

(c) In `ToString()`, change the alias suffix (currently L186-187):

```csharp
        if (Aliases.Count > 0)
            d += " (also " + string.Join(", ", Aliases) + ")";
```

to:

```csharp
        if (Aliases.Count > 0)
            d += ", also " + string.Join(", ", Aliases) + " (online)";
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project tools/TaxiAugmentProbe`
Expected: Task 4 `Display:` lines PASS; ends `ALL PASS`.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Database/Models/ParkingSpot.cs tools/TaxiAugmentProbe/Program.cs
git commit -m "feat(gate-data): empty-name gates read 'Gate {n}'; online aliases tagged '(online)'

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: `GateSearchFilter` — match aliases

**Files:**
- Modify: `MSFSBlindAssist/Services/GateSearchFilter.cs` (`Matches`/`Filter`, L24-35)
- Test: `tools/TaxiAugmentProbe/Program.cs` (append)

- [ ] **Step 1: Write the failing test** — append after the Task 4 block.

```csharp
// ──────────────────────────────────────────────────────────────────────
// Task 5: GateSearchFilter — alias-aware
// ──────────────────────────────────────────────────────────────────────
var search51 = new ParkingSpot { Name = "", Number = 51, Type = 13 };
search51.Aliases.Add("A51");
Check(MSFSBlindAssist.Services.GateSearchFilter.Matches(search51, "A51"), "Search: 'A51' matches gate 51 via alias");
Check(MSFSBlindAssist.Services.GateSearchFilter.Matches(search51, "51"),  "Search: '51' matches gate 51 via identity");
Check(!MSFSBlindAssist.Services.GateSearchFilter.Matches(new ParkingSpot { Name = "", Number = 15, Type = 10 }, "11B"),
      "Search: '11B' does NOT match gate 15");
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet run --project tools/TaxiAugmentProbe`
Expected: FAIL — `Matches(search51, "A51")` is false (aliases not consulted).

- [ ] **Step 3: Edit `GateSearchFilter.cs`.** Replace `Matches` (L24-28) and `Filter` (L30-35) with:

```csharp
    public static bool Matches(ParkingSpot s, string? query)
    {
        string q = Normalize(query);
        if (q.Length == 0) return true;
        if (NormalizeIdentity(s).Contains(q, StringComparison.Ordinal)) return true;
        foreach (var a in s.Aliases)
            if (Normalize(a).Contains(q, StringComparison.Ordinal)) return true;
        return false;
    }

    public static List<ParkingSpot> Filter(IEnumerable<ParkingSpot> spots, string? query)
    {
        string q = Normalize(query);
        if (q.Length == 0) return spots.ToList();
        return spots.Where(s => Matches(s, query)).ToList();
    }
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet run --project tools/TaxiAugmentProbe`
Expected: Task 5 `Search:` lines PASS; ends `ALL PASS`.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/GateSearchFilter.cs tools/TaxiAugmentProbe/Program.cs
git commit -m "feat(gate-data): gate search also matches online aliases

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: `TaxiAssistForm` — `(no taxi route)` mark + warn; `(online)` alias entries

WinForms/SimConnect code — not probe-testable; verified by build + the in-sim plan (Task 7). Keep unreachable stands visible (instead of silently dropping them) and block routing to them.

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` — resolve loop (~L1034-1044), build loop (~L1090-1114), `OnCalculateClicked` (~L2005-2009)

- [ ] **Step 1: Keep unreachable stands with a `-1` node marker.** Replace the resolve loop (the `foreach (var spot in sourceSpots)` block, ~L1034-1044) with:

```csharp
                foreach (var spot in sourceSpots)
                {
                    int nodeId = -1; // -1 = no reachable taxi-graph node (kept, marked "(no taxi route)")
                    var nearNode = _graph.FindNearestNode(spot.Latitude, spot.Longitude);
                    if (nearNode != null)
                    {
                        double dist = TaxiGraph.CalculateDistanceMeters(
                            nearNode.Latitude, nearNode.Longitude, spot.Latitude, spot.Longitude);
                        if (dist <= MAX_PARKING_TO_GRAPH_M)
                            nodeId = nearNode.NodeId;
                    }
                    resolved.Add((spot, nodeId));
                }
```

- [ ] **Step 2: Mark the label and tag alias entries.** In the build loop (`foreach (var (spot, nodeId) in parkingSpots)`, ~L1090), change the label line (L1093) and the alias entry label (L1106):

Change (L1093):
```csharp
                string label = spot.Describe();  // clean base; aliases added as separate entries below
```
to:
```csharp
                string label = spot.Describe();  // clean base; aliases added as separate entries below
                if (nodeId < 0) label += " (no taxi route)";
```

Change (L1106):
```csharp
                    string aliasLabel = $"{alias} ({label})";
```
to:
```csharp
                    string aliasLabel = $"{alias} (online) ({label})";
```

- [ ] **Step 3: Block routing to an unreachable stand.** In `OnCalculateClicked`, immediately after the destination lookup (after L2009's closing `}` of the `if (string.IsNullOrEmpty(destName) || !_destinationNodeMap.TryGetValue...)` guard), insert:

```csharp
        if (destNodeId < 0)
        {
            _announcer.Announce($"No taxi route to {destName}. This stand can't be reached by the taxi network.");
            lblStatus.Text = "Selected stand has no taxi route.";
            return;
        }
```

- [ ] **Step 4: Build the solution.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "feat(gate-data): mark unreachable stands '(no taxi route)' + warn; tag online alias entries

Keeps graph-unreachable stands visible (was: silently dropped) and refuses to build a route to them
(anti-grass), instead of silently routing across non-pavement.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: Full verification + in-sim test plan

**Files:** none (verification only).

- [ ] **Step 1: Run the full probe suite.**

Run: `dotnet run --project tools/TaxiAugmentProbe`
Expected: `ALL PASS`.

- [ ] **Step 2: Confirm the core taxi probes are green.**

Run: `dotnet run --project tools/TaxiGuidanceProbe` then `dotnet run --project tools/ProgressiveTaxiProbe`
Expected: both report ALL PASS (no regression from the gate changes — they exercise routing, not gate labels).

- [ ] **Step 3: Build the solution (Debug, x64).**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded. Verify `MSFSBlindAssist\bin\x64\Debug\net9.0-windows\MSFSBlindAssist.exe` LastWriteTime is "now" (close MSFSBA first — the exe is file-locked while running).

- [ ] **Step 4: Write the in-sim test plan into the PR description.** The repo owner runs it (no automated sim tests exist). Plan:
  1. **CYUL labels** (Shift+G and taxi-guidance gate list): gates read `Gate 51 - Gate Heavy (Jetway)` — **no** `Gate 11B 15`, **no** `Spot N`, **no** duplicate `(also …)`. Any online alias reads `…, also A51 (online)`.
  2. **GSX selection** (GSX running): pick a gate as a taxi destination → the *correct* stand is selected in the GSX menu (the selector announces it).
  3. **Taxi + VDGS**: taxi to the gate; docking/VDGS engages and stops cleanly (no wrong-stand routing).
  4. **Search**: typing `51` finds gate 51; `11B` does **not** return gate 15; an aliased concourse code (e.g. `A51`, if present in the data) finds its gate.
  5. **Anti-grass**: a stand with no taxi route shows `(no taxi route)` and, if chosen, announces the warning instead of routing.
  6. **Non-GSX airport**: labels are clean from the sqlite path; routing works.

- [ ] **Step 5: Final commit (docs/PR notes if any).**

```bash
git add -A
git commit -m "docs(gate-data): in-sim test plan for gate-data correctness

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Self-review

**Spec coverage:**
- Clean labels / `Gate {n}` / no doubling → Tasks 3, 4. ✅
- Identity-matched/alias-only/idempotent rule → Tasks 1, 2, 3. ✅
- `(online)` provenance tag → Task 4 (`ToString`) + Task 6 (alias entries). ✅
- Anti-grass (online never positions/adds gates) → Task 3 (resolver never writes Name/pos; only annotates `_base` spots). `(no taxi route)` signal → Task 6. ✅
- GSX correct selection / VDGS → falls out of clean identity (no code change; documented in file map + Task 7 in-sim). ✅
- Search appropriate results → Task 5. ✅
- Source priority (GSX → online-alias → sqlite) → realized: GSX-merge upstream of the decorator, resolver alias-only, sqlite baseline. ✅
- Verification (probe on captured data + in-sim) → Tasks 1/2/4/5 probes + Task 7. ✅

**Placeholder scan:** none — every step has concrete code or an exact command.

**Type consistency:** `StandId.Parse` (Task 1) → used by `GateAliasResolver.ResolveAliases` (Task 2) and `AugmentingAirportDataProvider` (Task 3); `ParkingSpot.Aliases` (existing `List<string>`) set in Task 3, rendered in Task 4, read in Task 5/6; `GateAliasResolver.ResolveAliases(ParkingSpot, IReadOnlyList<(string,double,double)>, double)` signature consistent across Tasks 2/3. `IsGateType()` defined and used only in Task 4. ✅

**Note on a deliberate trade-off:** the old position-based aliasing could cross *different* numbers (the doc's hypothetical "GN 3 ↔ 47"). The number-matched rule drops that, because it is the *same mechanism* that caused the corruption — distinguishing a legitimate cross-number alias from a neighbor-grab by position alone is the bug. This favors correctness/anti-grass per the approved spec. (The taxiway alias path — `TaxiDataMerger`, "HAWKER ↔ K" — is untouched.)

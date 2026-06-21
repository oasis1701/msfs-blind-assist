# Hold-Short Runway Labeling (Centerline Association) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Name a hold-short node after the runway it physically sits at (by nearest runway *centerline*, length-invariant) instead of falling back to a taxiway name when the crossing is far from a threshold, so guidance announces "Hold short of runway 15R at N" rather than "Hold short of N".

**Architecture:** Change one association step in `Navigation/TaxiGraph.cs`: add a pure static helper `MatchHoldShortRunwayName` (nearest centerline by clamped perpendicular distance, closer-end designator) and use it as the primary runway match in the hold-short naming loop, with the existing threshold-distance method kept as a fallback. Add console-probe coverage to the existing `tools/ProgressiveTaxiProbe`.

**Tech Stack:** .NET 9, screen-reader-first taxi guidance. No unit-test project — verification is `dotnet build` + a pure-geometry console probe + an in-sim re-run by the repo owner.

**Spec:** `docs/superpowers/specs/2026-06-17-holdshort-runway-label-centerline-design.md`

---

## File Structure

- **Modify:** `MSFSBlindAssist/Navigation/TaxiGraph.cs` — add `HOLDSHORT_RUNWAY_MATCH_M` const + `MatchHoldShortRunwayName(...)` static helper; rewrite the hold-short name-assignment block (~lines 293-303) to prefer the centerline match.
- **Modify:** `tools/ProgressiveTaxiProbe/Program.cs` — append assertions for the new helper (mid-runway match, far-node no-match, beyond-end clamp).

## Build / verify commands

- Solution build: `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`. Build the SOLUTION, never the bare `.csproj`. MSB3021 = app running (environment, not code). Do NOT push.
- Probe: `dotnet run --project tools/ProgressiveTaxiProbe -p:Platform=x64` → exit 0, all `PASS`. (The probe is standalone, not part of the solution; it links `TaxiGraph.cs`.)

> Line numbers are approximate (`~`); match on the exact code TEXT. READ each region before editing.

---

### Task 1: Centerline-based hold-short runway association

**Files:**
- Modify: `MSFSBlindAssist/Navigation/TaxiGraph.cs`

- [ ] **Step 1: Add the match-distance constant.**

Find the `RunwayCenterlines` declaration (~line 54):

```csharp
    public List<RunwayCenterline> RunwayCenterlines { get; } = new();
```

Insert the constant immediately after it:

```csharp
    public List<RunwayCenterline> RunwayCenterlines { get; } = new();

    // Max perpendicular distance (metres) from a hold-short node to a runway
    // centerline for the node to be named after that runway. Above a CAT III /
    // code-F holding-position setback (~107 m) so real hold lines match, below
    // major-airport parallel-runway spacing so a node binds to its OWN runway.
    private const double HOLDSHORT_RUNWAY_MATCH_M = 150.0;
```

- [ ] **Step 2: Add the `MatchHoldShortRunwayName` static helper.**

Find the `PerpendicularDistanceMetersStatic` wrapper (~line 913):

```csharp
    public static double PerpendicularDistanceMetersStatic(
        double plat, double plon,
        double alat, double alon,
        double blat, double blon)
        => PerpendicularDistanceMeters(plat, plon, alat, alon, blat, blon);
```

Insert this method immediately before it:

```csharp
    /// <summary>
    /// Runway designator a hold-short node holds short of, found by the NEAREST
    /// runway centerline (full length, perpendicular distance clamped to the
    /// threshold endpoints). Length-invariant: a hold-short where a taxiway
    /// crosses a long runway far from either threshold is still matched — unlike a
    /// distance-to-threshold test. Returns the closer-end designator (same
    /// convention as DescribeLocation / WhichRunwayContains), or null when no
    /// centerline is within <paramref name="maxMatchMeters"/> (the caller then
    /// falls back to the threshold heuristic). Public static for probe coverage.
    /// </summary>
    public static string? MatchHoldShortRunwayName(
        double lat, double lon,
        IReadOnlyList<RunwayCenterline> centerlines,
        double maxMatchMeters)
    {
        string? best = null;
        double bestPerp = double.MaxValue;
        foreach (var rwy in centerlines)
        {
            double perp = PerpendicularDistanceMeters(
                lat, lon, rwy.Lat1, rwy.Lon1, rwy.Lat2, rwy.Lon2);
            if (perp > maxMatchMeters || perp >= bestPerp) continue;

            // Closer-end designator (same convention as DescribeLocation): the end
            // the aircraft is nearer is the one it would line up to depart from.
            double d1 = FastDistanceMeters(lat, lon, rwy.Lat1, rwy.Lon1);
            double d2 = FastDistanceMeters(lat, lon, rwy.Lat2, rwy.Lon2);
            string name = d1 <= d2 ? rwy.Name1 : rwy.Name2;
            if (string.IsNullOrEmpty(name)) name = rwy.Name1;
            if (string.IsNullOrEmpty(name)) continue; // unnamed centerline — skip

            best = name;
            bestPerp = perp;
        }
        return best;
    }
```

- [ ] **Step 3: Prefer the centerline match in the name-assignment block.**

Find the existing assignment block at the end of the hold-short naming loop (~lines 293-303):

```csharp
                if (nearestRunway != null && nearestDist < 500)
                {
                    if (!string.IsNullOrEmpty(holdPointName))
                        node.HoldShortName = $"{holdPointName}, Runway {nearestRunway}";
                    else
                        node.HoldShortName = $"Runway {nearestRunway}";
                }
                else if (!string.IsNullOrEmpty(holdPointName))
                {
                    node.HoldShortName = holdPointName;
                }
```

Replace with (centerline match first, with the new "runway X at <holdpoint>" format; the existing threshold logic preserved verbatim as the fallback):

```csharp
                // Primary: associate by nearest runway CENTERLINE (length-invariant),
                // so a mid-runway crossing of a long runway is named after the runway,
                // not the taxiway. Format leads with the runway (the safety cue),
                // appending the holding-point/taxiway designator when present.
                string? centerlineRwy = MatchHoldShortRunwayName(
                    node.Latitude, node.Longitude, graph.RunwayCenterlines, HOLDSHORT_RUNWAY_MATCH_M);
                if (centerlineRwy != null)
                {
                    node.HoldShortName = !string.IsNullOrEmpty(holdPointName)
                        ? $"runway {centerlineRwy} at {holdPointName}"
                        : $"runway {centerlineRwy}";
                }
                // Fallback (no centerlines built, or none within tolerance): the
                // existing threshold-distance heuristic, unchanged — preserves
                // today's output for sparse navdata without reciprocal pairs.
                else if (nearestRunway != null && nearestDist < 500)
                {
                    if (!string.IsNullOrEmpty(holdPointName))
                        node.HoldShortName = $"{holdPointName}, Runway {nearestRunway}";
                    else
                        node.HoldShortName = $"Runway {nearestRunway}";
                }
                else if (!string.IsNullOrEmpty(holdPointName))
                {
                    node.HoldShortName = holdPointName;
                }
```

(The threshold scan above this block, which computes `nearestRunway`/`nearestDist`, is left in place — it now feeds only the fallback branch.)

- [ ] **Step 4: Build.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors. If `MatchHoldShortRunwayName` is reported unresolved in the loop, confirm Step 2 added it to the same class (`TaxiGraph`).

- [ ] **Step 5: Commit.**

```bash
git add MSFSBlindAssist/Navigation/TaxiGraph.cs
git commit -m "fix(taxi): name hold-shorts by runway centerline, not threshold distance

A hold-short where a taxiway crosses a long runway far from either threshold
(KBOS 15R on N) was named after the taxiway ('Hold short of N') because the
runway match used distance-to-threshold (<500m). Match by nearest runway
centerline (length-invariant) and format 'runway X at <holdpoint>'. The
threshold heuristic remains as a fallback for navdata without centerlines.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Probe coverage for the centerline association

**Files:**
- Modify: `tools/ProgressiveTaxiProbe/Program.cs`

- [ ] **Step 1: Append a hold-short-naming assertion block.**

Find the end of `Program.cs` — it ends with a final summary using the `failures` counter and `Check` helper (e.g. a line printing the pass/fail total and `return failures == 0 ? 0 : 1;`). Read the file to find the exact tail. Insert the following block **immediately before** that final summary/return (so the new checks increment the same `failures` counter):

```csharp
// ---------------------------------------------------------------------------
// Hold-short runway association (TaxiGraph.MatchHoldShortRunwayName)
//
// Synthetic runway 15R/33L: a straight N-S centerline along lon -71.0100 from
// lat 42.3500 (33L threshold) to lat 42.3800 (15R threshold) — ~3.3 km long,
// half-width 30 m. This mirrors the KBOS case: a hold-short where a taxiway
// crosses the runway far from BOTH thresholds.
// ---------------------------------------------------------------------------
const double MATCH_M = 150.0; // mirror TaxiGraph.HOLDSHORT_RUNWAY_MATCH_M
var rwy = new List<TaxiGraph.RunwayCenterline>
{
    new TaxiGraph.RunwayCenterline
    {
        Lat1 = 42.3800, Lon1 = -71.0100, Name1 = "15R", // primary end (north)
        Lat2 = 42.3500, Lon2 = -71.0100, Name2 = "33L", // opposite end (south)
        HalfWidthMeters = 30.0
    }
};

// Mid-runway node, ~60 m east of centerline, nearer the 15R (north) end.
// Far from both thresholds (would FAIL the old <500 m threshold test).
string? midName = TaxiGraph.MatchHoldShortRunwayName(42.3750, -71.00927, rwy, MATCH_M);
Check(midName == "15R", $"mid-runway hold-short names 15R (got '{midName ?? "null"}')");

// Node ~800 m east of the centerline (e.g. a real taxiway-only hold) — no match.
string? farName = TaxiGraph.MatchHoldShortRunwayName(42.3650, -70.9900, rwy, MATCH_M);
Check(farName == null, $"far node does not match a runway (got '{farName ?? "null"}')");

// Node ~2 km north of the 15R threshold (beyond the runway end). The clamped
// perpendicular distance measures to the endpoint, so it must NOT match.
string? beyondName = TaxiGraph.MatchHoldShortRunwayName(42.4000, -71.0100, rwy, MATCH_M);
Check(beyondName == null, $"node beyond runway end does not match (got '{beyondName ?? "null"}')");
```

- [ ] **Step 2: Run the probe.**

Run: `dotnet run --project tools/ProgressiveTaxiProbe -p:Platform=x64`
Expected: all lines `PASS`, process exits 0. In particular:
```
PASS mid-runway hold-short names 15R (got '15R')
PASS far node does not match a runway (got 'null')
PASS node beyond runway end does not match (got 'null')
```
If `mid-runway` fails with a runway name of `33L`, the synthetic node latitude is on the wrong half — it must be nearer `Lat1` (42.3800) than `Lat2` (42.3500); 42.3750 is correct, so re-check the centerline coordinates were entered as written.

- [ ] **Step 3: Commit.**

```bash
git add tools/ProgressiveTaxiProbe/Program.cs
git commit -m "test(taxi): probe centerline hold-short runway association

Asserts MatchHoldShortRunwayName matches a mid-runway crossing to the runway
(15R), rejects a far taxiway-only node, and rejects a node beyond the runway
end (clamped perpendicular distance).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Verify downstream phrasing + final verification

**Files:**
- Possibly modify: `MSFSBlindAssist/Services/TaxiGuidanceManager.cs` (only if the incursion-monitor phrasing reads wrong — see Step 1)

- [ ] **Step 1: Check the runway-incursion-monitor phrasing with the new name format.**

Read `TaxiGuidanceManager.cs` around line 2592 (the incursion monitor):
```csharp
string rwy = !string.IsNullOrEmpty(nearestHs.HoldShortName) ? nearestHs.HoldShortName : "runway";
```
Read how `rwy` is used in the surrounding announcement(s). With the new format, `HoldShortName` is now e.g. `"runway 15R at N"`. Confirm the resulting spoken sentence reads naturally (e.g. "Caution, approaching runway 15R at N" is fine; "Caution, approaching **runway** runway 15R at N" would be a double-word and must be fixed). If a double-word or grammatical break appears, adjust ONLY that announcement's wording (e.g. drop a redundant leading "runway"); do not change `HoldShortName`. If it reads fine, make no change and note that in the report.

- [ ] **Step 2: Clean solution build + probe.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`, 0 errors.
Run: `dotnet run --project tools/ProgressiveTaxiProbe -p:Platform=x64` → exit 0, all PASS.

- [ ] **Step 3: Confirm the build landed in the run path.**

Run (PowerShell): `(Get-Item 'MSFSBlindAssist\bin\x64\Debug\net9.0-windows\MSFSBlindAssist.exe').LastWriteTime`
Expected: a current timestamp.

- [ ] **Step 4: If Step 1 changed code, commit it.**

```bash
git add MSFSBlindAssist/Services/TaxiGuidanceManager.cs
git commit -m "fix(taxi): tidy incursion-monitor phrasing for new hold-short name format

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

- [ ] **Step 5: Record the in-sim test plan in the PR** (repo owner runs it):
  1. At KBOS, taxi a route that crosses runway 15R on taxiway N (the reported clearance). As you approach the 15R crossing, confirm the callout is **"Stop. Hold short of runway 15R at N. Press continue when cleared."** — not "Hold short of N".
  2. Confirm a hold-short near a runway *threshold* (short runway, or a hold close to the end) still names the runway correctly.
  3. Confirm a genuine taxiway-only hold (if reachable) still reads its taxiway name (no spurious runway).

- [ ] **Step 6: Hand off to `superpowers:finishing-a-development-branch`.**

---

## Self-Review Notes

- **Spec coverage:** centerline association replacing threshold (Task 1 Steps 2-3); 150 m tolerance const (Step 1); closer-end designator (helper); "runway X at <holdpoint>" / "runway X" format (Step 3); threshold fallback preserved verbatim (Step 3 else-branches); probe for matched/far/beyond-end (Task 2); downstream phrasing check (Task 3 Step 1); in-sim plan (Task 3 Step 5). All spec sections covered.
- **No placeholders:** every code step shows full before/after text and exact commands.
- **Type consistency:** `MatchHoldShortRunwayName(double, double, IReadOnlyList<RunwayCenterline>, double) -> string?` defined in Task 1 Step 2, called in Task 1 Step 3 (`graph.RunwayCenterlines`, a `List<RunwayCenterline>` → `IReadOnlyList`) and Task 2 (`TaxiGraph.MatchHoldShortRunwayName`, `List<TaxiGraph.RunwayCenterline>`). `RunwayCenterline` fields used (`Lat1/Lon1/Name1`, `Lat2/Lon2/Name2`, `HalfWidthMeters`) match the struct at TaxiGraph.cs:45-53. Helpers `PerpendicularDistanceMeters` / `FastDistanceMeters` are existing static members of `TaxiGraph`.
- **Clamp verified:** `PerpendicularDistanceMeters` clamps `t` to [0,1] (TaxiGraph.cs:940-942), so the beyond-end probe case is valid.

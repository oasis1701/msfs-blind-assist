# Hold-Short Runway Labeling — Centerline Association

**Date:** 2026-06-17
**Status:** Approved (brainstorm); pending spec review → implementation plan
**Area:** Taxi Guidance (`Navigation/TaxiGraph.cs`)

## Problem

At KBOS, taxiing across runway **15R** on taxiway **N**, guidance announced
**"Hold short of N"** instead of **"Hold short of runway 15R."** For a blind pilot
this drops the runway-incursion cue — the whole point of the callout.

## Root cause (verified from logs + code)

The hold-short was *detected* at the right place but *named* wrong:

1. **Graph build ([TaxiGraph.cs:293](../../MSFSBlindAssist/Navigation/TaxiGraph.cs)):**
   a hold-short node is labeled `"…Runway X"` only when it is within **500 m of a
   runway *threshold*** (`runwayStarts`); otherwise it falls back to the **taxiway
   name** (`holdPointName`). KBOS 15R-33L is ~3,000 m long, and N crosses it well
   away from either threshold, so the node is >500 m from both thresholds → it is
   named **"N"**.
2. **Route build ([TaxiRouter.cs:1047](../../MSFSBlindAssist/Navigation/TaxiRouter.cs)):**
   every segment seeds `HoldShortRunway = toNode.HoldShortName`, so the crossing
   segment inherits **"N"**.
3. **Crossing tagger ([TaxiGuidanceManager.cs:5302](../../MSFSBlindAssist/Services/TaxiGuidanceManager.cs)):**
   `InsertRunwayCrossingHoldShorts` correctly detects the 15R crossing
   (centerline-based) and sets `IsHoldShortPoint = true`, but only writes
   `"runway 15R"` when the label is empty. The pre-seeded **"N"** is non-empty, so
   the guard preserves it → the arrival announcement
   ([TaxiGuidanceManager.cs:3337](../../MSFSBlindAssist/Services/TaxiGuidanceManager.cs))
   says "Stop. Hold short of N…".

The threshold-distance heuristic is the source defect: it cannot associate a
hold-short with a long runway when the crossing is far from either threshold.

## Goal

A hold-short node that physically sits at a runway hold line is always named after
that **runway**, regardless of where along the runway it sits. Resulting callout
(user-chosen, ATC style): **"Stop. Hold short of runway 15R at N. Press continue
when cleared."**

## Non-goals

- No change to *where* hold-shorts are detected or to `InsertRunwayCrossingHoldShorts`
  geometry — only the runway-name association and its format.
- No change to genuine taxiway-only holds (a hold-short node not at any runway keeps
  its taxiway/holding-point name).
- No change to `DescribeLocation` / "Where Am I" (a separate code path).

## Design

### Centerline-based association (replaces threshold-distance)

In the hold-short naming loop in `TaxiGraph.Build` ([TaxiGraph.cs:209-303](../../MSFSBlindAssist/Navigation/TaxiGraph.cs)),
for each `HoldShort`/`ILSHoldShort` node:

- Compute `holdPointName` exactly as today (connector-style designator preferred,
  else longest taxiway name).
- **Find the runway by nearest centerline:** iterate `RunwayCenterlines`; for each,
  compute the **clamped perpendicular distance** from the node to the centerline
  segment (`Lat1/Lon1`–`Lat2/Lon2`) using the existing
  `PerpendicularDistanceMeters` helper (clamped to endpoints, so a node beyond a
  runway end is not falsely matched). Track the **nearest** centerline and its
  distance.
- **Accept** the match when the nearest distance ≤ `HOLDSHORT_RUNWAY_MATCH_M`
  (**150 m** — above a CAT III / code-F holding-position setback of ~107 m, below
  KBOS-class parallel-runway spacing so a node binds to its own runway). The
  designator is the **closer-end** name (`Name1` if the node is nearer end 1, else
  `Name2`) — same convention as `DescribeLocation`.

### Name format (matched node)

- Matched **and** `holdPointName` non-empty → `node.HoldShortName = "runway {X} at {holdPointName}"`
  (e.g. `"runway 15R at N"`).
- Matched, `holdPointName` empty → `node.HoldShortName = "runway {X}"`.
- Lowercase "runway" so it reads naturally after the announcement's "Hold short of "
  (matching the existing `InsertRunwayCrossingHoldShorts` "runway X" wording). The
  incursion-monitor consumer ([TaxiGuidanceManager.cs:2592](../../MSFSBlindAssist/Services/TaxiGuidanceManager.cs))
  also reads `HoldShortName`; verify its phrasing reads correctly with the new
  format during implementation.

### Fallback (no regression)

If `RunwayCenterlines` is empty, or no centerline is within
`HOLDSHORT_RUNWAY_MATCH_M`, fall back to the **existing** behavior:
- nearest runway *threshold* within 500 m → `"…Runway {X}"` (kept for airports whose
  navdata has no reciprocal centerline pairs);
- else → `holdPointName` (taxiway-only hold, unchanged).

The fallback preserves today's output everywhere centerlines aren't available, so the
only behavioral change is: long-runway mid-crossing hold-shorts that previously got a
taxiway name now get the correct runway name.

### Downstream (unchanged, now correct)

`TaxiRouter` still seeds `HoldShortRunway` from `HoldShortName`, and
`InsertRunwayCrossingHoldShorts` keeps its "don't overwrite" guard — it now respects
a *correct* label ("runway 15R at N") and still supplies "runway 15R" for crossings
whose node had no name. No changes to those files.

## Edge cases

- **Node near two runways (intersection):** nearest centerline wins; tolerance is
  below parallel spacing, so no cross-binding.
- **Node beyond a runway end (approach/RET stub):** clamped perpendicular distance
  measures to the threshold endpoint, so it is not falsely matched to that runway's
  body.
- **ILS hold-short (IHS):** same path; named after the runway it protects.
- **Sparse navdata (no centerlines):** unchanged via the threshold fallback.

## Testing

- **Console probe** (`tools/`-style, pure geometry): a KBOS 15R/N replica asserting a
  mid-runway hold-short node names `"runway 15R at N"`, plus a near-threshold node and
  a taxiway-only hold-short to confirm the matched/fallback/taxiway-only branches.
- **In-sim:** re-run the KBOS clearance crossing 15R on N and confirm the callout is
  "Stop. Hold short of runway 15R at N. Press continue when cleared."
- `dotnet build MSFSBlindAssist.sln -c Debug` (0 errors).

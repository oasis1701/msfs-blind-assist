# Taxi guidance: pavement lead-in onto the first cleared taxiway

**Date:** 2026-06-17
**Branch:** `feature/progressive-taxi`
**Status:** Design (approved, pending spec review)

## Problem

On a CYYZ departure (logs 2026-06-17 18:22–18:28, runway 23 cleared "via A, H") taxi
guidance:

1. Told the pilot the first taxiway was **~180° behind** them and made them pivot ~173°
   in place from a standstill (~90 s), then
2. Steered a **297 m straight beeline across the GB/GC apron** — ~60–70 m off any taxiway
   centreline, clipping the **AJ** junction ("in the grass crossing AJ") — to reach the
   start of taxiway A.

### Root cause

`TaxiGuidanceManager.LoadRoute` pre-snaps the constrained-route start to the nearest node
**on the first cleared taxiway** via `TaxiGraph.FindNearestNodeOnTaxiway`
(`TaxiGuidanceManager.cs:1151`). At CYYZ the gate (GB/GC apron, ~100 m from GB 19 / GC 25)
is **297 m** from the nearest node on taxiway A (node 649). The route therefore *starts*
at node 649, and the guidance steers the aircraft straight at it.

`TaxiRouter.FindConstrainedPath` **already** builds a pavement-following approach onto the
first taxiway — `AStarSearch(startNodeId, entryNode)` at `TaxiRouter.cs:139` — but **only
when the start node is not already on that taxiway**. Because `LoadRoute` pre-snaps the
start *onto* the taxiway, that approach is skipped and the aircraft beelines across
whatever lies between the gate and the taxiway (apron, grass, the AJ junction).

The pre-snap was introduced deliberately (commits `62c3f0d4`, `c01b7c2f`) to fix a LEPA
recalc-anchoring bug: the older *heading-aware* picker (`FindNearestNodeInDirection`) chose
a node **off** the cleared taxiway when the aircraft faced away from it, so
`FindConstrainedPath` could not anchor and fell back to shortest path. The fix here must
preserve correct anchoring for that case while restoring the pavement lead-in for the
large-gap case.

Both reported symptoms share this one root cause: there is no pavement-following lead-in
from the aircraft's actual position onto the first cleared taxiway when that taxiway is far
away.

## Goals

- When the first cleared taxiway is **far** from the aircraft, route the lead-in onto it
  along **pavement** (apron taxilanes), not a straight line across grass.
- Communicate the lead-in in the route **summary** when it fires (spoken + boxed).
- Apply only **at the start of taxiing** (`LoadRoute`), and never make routing worse than
  today.

## Non-goals

- No change to `TryRecalculateRoute` (mid-taxi recalc — small gap, has its own LEPA fix).
- No change to unconstrained (no-taxiway-sequence) routes.
- No change to the "180° behind" / U-turn initial cue, hold-short insertion, lineup, or
  off-route detection logic. They simply operate on the now-pavement-following first
  segments.
- No new approach/pathfinding algorithm — reuse the router's existing `AStarSearch`
  lead-in.

## Design

All changes are in `TaxiGuidanceManager.LoadRoute` plus `BuildRouteSummary`, for the
**constrained** case only (`taxiwaySequence != null && taxiwaySequence.Count > 0`).

### Trigger (conditional — "only when far")

1. Compute `firstTwNode = _graph.FindNearestNodeOnTaxiway(aircraftLat, aircraftLon,
   taxiwaySequence[0], requiredComponentId: destComponentId)` exactly as today.
2. If `firstTwNode == null` → today's fallback (`FindNearestNodeInDirection`); no lead-in
   logic (no first-taxiway node nearby).
3. If `firstTwNode != null`, let `gap = FastDistanceMeters(aircraft, firstTwNode)`:
   - `gap <= APPROACH_LEADIN_TRIGGER_M` → **unchanged**: start the constrained path at
     `firstTwNode`. The common gate-on-its-taxiway case is byte-for-byte identical to
     today.
   - `gap > APPROACH_LEADIN_TRIGGER_M` → **attempt the lead-in** (below).

`APPROACH_LEADIN_TRIGGER_M = 75.0` (named const near the other taxi constants; tunable).
CYYZ's gap (297 m) fires easily; typical gate-to-taxiway gaps (≤ ~60 m) do not.

### Lead-in attempt

1. `entryNode = nearest graph node to the aircraft in the destination's connected
   component`. `TaxiGraph.FindNearestNode` is component-unaware, so either add a
   component-filtered overload/parameter or post-filter; if the nearest node is not in
   `destComponentId`, treat as "no entry" (fall back, below).
2. If `entryNode != null`: build the constrained route with `entryNode.NodeId` as the
   start. `FindConstrainedPath`'s existing Step 1 builds the pavement lead-in from
   `entryNode` onto `taxiwaySequence[0]` and anchors there.

### Acceptance test + safeguard (silent fallback + notify)

Accept the `entryNode`-based route only if **both**:

- `route.ConstrainedFallbackReason == null` (the clearance was honoured — the router did
  not bail to shortest path), and
- the **lead-in distance** (sum of `DistanceMeters` for the leading segments whose
  `TaxiwayName != taxiwaySequence[0]`, i.e. before the first cleared-taxiway segment) is
  within a cap: `leadInDist <= gap * APPROACH_LEADIN_MAX_RATIO + APPROACH_LEADIN_MAX_PAD_M`.
  This rejects a dead-end/loop entry (mirrors the existing `RECALC_LENGTH_BLOWUP_RATIO` /
  `RECALC_LENGTH_BLOWUP_PAD_M` pattern). Suggested: `RATIO = 2.5`, `PAD = 300 m`.

If `entryNode` is missing/out-of-component, **or** acceptance fails → **rebuild** the route
with `firstTwNode` as the start (today's behaviour) and set a `leadInFallbackNotice` flag.
Building twice happens only in this rare failure path; the accepted large-gap case builds
once.

### Lead-in detection (for the summary)

When the accepted route started at `entryNode`, the lead-in segment block = the leading
segments before the first segment with `TaxiwayName == taxiwaySequence[0]`. Collect their
distinct, non-empty `TaxiwayName`s in order → `leadInTaxiways` (e.g. `[4, AJ]`). Unnamed
apron connectors contribute no name. Set `leadInAdded = leadInTaxiways.Count > 0 || (any
leading non-cleared-taxiway segment exists)`.

### Summary wording (separate lead-in clause)

`BuildRouteSummary` keeps the ATC `via` list intact and inserts a lead-in clause:

- Lead-in added with names: `"Route to Runway 23 via A, H. First taxi via 4 and AJ to
  reach A. 1.6 km."`
- Lead-in added without names (apron connectors only): `"… via A, H. First taxi onto A.
  1.6 km."`
- No lead-in (common case): unchanged.

The lead-in clause is informational and sits mid-summary. The **safeguard fallback notice**
is more important, so it is **prepended** to the whole summary (like `constrainedLengthWarning`),
because the first `AnnounceImmediate` tactical callout after the pilot starts rolling
interrupts the queued summary — a tail clause can be cut:

- Fallback fired: `"Could not compute a path onto taxiway A along the apron; route starts
  on A."` prepended, then the normal summary.

The summary is already both spoken (`_announcer.Announce`, when `announceSummary`) and
written to `LastRouteSummary` (the form's box), so "notify via route summary and
announcement" is satisfied by folding both notices into the summary string. No separate
announcement call is added.

### Interaction with the initial turn cue

No change. With the lead-in, segment 0 starts at `entryNode` (metres from the aircraft) and
`StartGuidance`'s "first named taxiway" becomes the lead-in taxiway (e.g. `4`). The initial
heading error is computed to the nearby lead-in target instead of the 297 m-away node 649,
so the U-turn cue (`TaxiGuidanceManager.cs:2065`) either does not fire or accurately
describes the real first turn onto the lead-in taxiway, following pavement.

## Affected code

- `MSFSBlindAssist/Services/TaxiGuidanceManager.cs`
  - `LoadRoute`: start-node selection block (~`:1148`–`:1171`); new constants; lead-in
    attempt + acceptance/fallback; capture `leadInTaxiways` / `leadInFallbackNotice`.
  - `BuildRouteSummary` (`:5865`): lead-in clause; thread the lead-in/fallback info in
    (e.g. via fields set in `LoadRoute`, consistent with how warnings are already
    assembled at `:1386`–`:1427`).
- `MSFSBlindAssist/Navigation/TaxiGraph.cs`
  - `FindNearestNode`: add component-filter capability (overload or optional
    `requiredComponentId`) — or post-filter in `LoadRoute`. Keep the existing
    component-unaware behaviour for current callers.

## Safeguards (summary)

- Trigger gated on a distance threshold → common case untouched, smallest blast radius.
- `entryNode` restricted to the destination's connected component → never strand A\* on an
  island.
- Acceptance test rejects clearance-bailout and dead-end/loop lead-ins → falls back to
  today's routing.
- Fallback is **silent on behaviour** (identical to current) and **announced** so the pilot
  knows the lead-in wasn't built.
- Only `LoadRoute`; recalc/unconstrained paths unchanged.

## Verification

No live sim needed for the core. A console probe (extend `tools/TaxiGuidanceProbe`, or add
`tools/TaxiLeadInProbe`) loads the real `fs2024.sqlite`, builds the CYYZ graph, and runs
`LoadRoute` from the GB/GC apron (`43.6845284, -79.6220943`, heading 52°) for "A H" →
runway 23. Assert:

1. The route has a leading non-A segment block (the lead-in).
2. The lead-in's first segment starts within a few metres of the aircraft (not ~297 m).
3. The lead-in follows named taxiways toward A (expect `4` and/or `AJ`).
4. `LastRouteSummary` contains the lead-in clause naming the lead-in taxiways.

Regression cases:

5. A gate already on the first cleared taxiway (`gap ≤ trigger`) → **no** lead-in block;
   route identical to today.
6. A LEPA-style on-taxiway start (aircraft on the first cleared taxiway) → still anchors on
   that taxiway, no spurious lead-in.

In-sim test plan (human owner runs): repeat the CYYZ GB/GC → runway 23 via A, H departure;
confirm the first cue routes onto an apron taxiway (4/AJ) along pavement rather than
"Taxiway A is behind you" + a grass crossing, and that the summary names the lead-in.

## Open risks / to confirm during implementation

- Confirm the existing `constrainedLengthWarning` comparison (`:1259`–`:1280`) does not
  spuriously fire now that `route.TotalDistanceMeters` includes the lead-in (its `direct`
  baseline also includes a lead-in from `FindNearestNodeInDirection`, so it should remain
  fair — verify on the CYYZ replica).
- Confirm `StartGuidance` sets `_lastAnnouncedTaxiway` to the lead-in's first named taxiway
  so the initial cue text is accurate.

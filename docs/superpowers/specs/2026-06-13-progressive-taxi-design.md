# Progressive Taxi — Design

**Date:** 2026-06-13
**Status:** Approved (brainstorm); pending spec review → implementation plan
**Area:** Taxi Guidance (`Forms/TaxiAssistForm.cs`, `Services/TaxiGuidanceManager.cs`, `Navigation/TaxiRouter.cs`)

## Summary

Add a **Progressive Taxi** mode that lets the pilot enter an ATC taxi clearance
*incrementally* — one cleared leg at a time — where each leg ends at an
intermediate point (hold short of a runway, hold short of a taxiway, or just
past a runway crossing) rather than at a final gate or runway. When the leg's
endpoint is reached, guidance announces the hold / route-end and stops; the
pilot then sets the next leg (or switches to a normal Runway/Gate destination to
finish). This mirrors how ATC issues progressive taxi instructions and replaces
the existing one-shot **Cross Runway** destination type, whose behavior folds in
as one of the leg terminators.

## Motivation

Real ATC progressive taxi gives instructions piecemeal: *"Taxi via A, B, hold
short runway 09"* → (later) *"Cross 09, continue B"* → (later) *"Continue to the
ramp."* The current planner only routes to a **final** destination (Runway /
Gate / Cross Runway / Deice Area). A blind pilot receiving progressive
instructions has no way to enter "guide me to this hold short and then stop and
wait." The **Cross Runway** mode is the closest existing primitive but is a
narrow one-shot (cross exactly one runway); generalizing it into per-leg
terminators covers all the real progressive-taxi end conditions.

## Background (current behavior)

- `TaxiAssistForm` has a **Destination Type** combo (`cmbDestType`):
  `Runway`, `Gate / Parking`, `Cross Runway`, `Deice Area`.
- **Cross Runway** lists all non-closed runways (`_crossRunwayMap`) and computes
  a far-side node across the chosen runway at Calculate time.
- Taxiway-sequence **rows** (first row + dynamic added rows) each carry a
  "Hold short" checkbox and a "Hold short of runway" combo. These map to
  `LoadRoute`'s `userHoldShortIndices` / `userRunwayHoldShorts`.
- `TaxiGuidanceManager.LoadRoute` builds the route via the constrained router,
  runs `TruncateToHoldShort` (for runway destinations) and
  `InsertRunwayCrossingHoldShorts` (auto hold-short at every crossed runway),
  then guidance plays the route with `ContinuePastHoldShort` resuming at each
  intermediate hold-short.
- Runway destinations end in a HoldShort → (Continue) → LiningUp → optional
  Takeoff-Assist auto-activate flow. Gate destinations end in arrival /
  docking. These end-flows are NOT wanted for progressive endpoints.

## Goals

- Enter a cleared taxi leg whose endpoint is an intermediate point.
- Support terminator types: **hold short of runway**, **hold short of taxiway**,
  **after crossing a runway**, and **end of last taxiway** (fallback).
- Announce the hold / route-end at the terminator and stop cleanly, with no
  lineup phase and no Takeoff-Assist auto-activation.
- Let the pilot set the next leg from their current position, or switch to a
  Runway/Gate destination to finish.
- Reuse the existing planner UI and routing machinery as much as possible.

## Non-goals

- A separate multi-leg queue engine. "Pre-enter the clearance up front" is
  achieved with the existing taxiway-sequence rows + per-row intermediate
  hold-shorts; the new work is the **endpoint terminator** and the **terminal
  end-state**, not a new sequencing engine.
- Changing how intermediate runway crossings are handled — they keep the
  current safety hold-short + Continue (see Decisions).
- Lineup / Takeoff-Assist behavior (progressive endpoints are never runway
  line-ups).

## Design

### 1. Mode & UI

- **Destination Type:** replace `"Cross Runway"` with `"Progressive Taxi"` in
  `cmbDestType`. Cross-runway behavior survives as the *After crossing a runway*
  terminator (§2).
- **Taxiway-sequence rows: unchanged.** Add-taxiway, per-row "Hold short", and
  per-row "Hold short of runway" continue to define the path and any
  *intermediate* holds/crossings.
- **Gate/runway destination picker:** hidden in Progressive Taxi mode (there is
  no final destination).
- **Last row carries the terminator.** The row that is currently last exposes a
  terminator selector — an end-condition **type** plus a **target** picker where
  applicable:
  - `Hold short of runway` + runway picker
  - `Hold short of taxiway` + taxiway picker
  - `After crossing runway` + runway picker
  - `End of last taxiway` (no target)
  When a new taxiway row is added, the terminator control relocates to the new
  last row; when the last row is removed, it relocates to the new last row.
  (Implementation note: this is a refresh of which row shows the terminator
  control — the chosen value travels with "last row," not with a specific row
  instance.)

### 2. Endpoint computation

Each terminator resolves to a destination node + end metadata, then hands off to
the **same `LoadRoute` + constrained router** path the normal dialog uses
(position-refreshed start, per-row intermediate hold-shorts applied as today):

| Terminator | Destination node | Reuses |
|---|---|---|
| Hold short of runway **X** | hold-short point on the last taxiway before X | existing per-row runway-hold-short geometry (`ApplyUserRunwayHoldShorts` / runway-centerline math) |
| Hold short of taxiway **Y** | node where the last taxiway meets taxiway Y (intersection) | **new** intersection-finder (nearest node on the last taxiway that also lies on / is adjacent to Y) |
| After crossing runway **Z** | far-side node past Z on the last taxiway | existing **Cross Runway** far-side computation (`_crossRunwayMap` + far-side node) |
| End of last taxiway | far end of the last taxiway | nearest-node-on-taxiway helpers |

- For **After crossing runway Z**, the hold-short for **Z** is **suppressed**
  (ATC has cleared the crossing). Any *other* runway crossed earlier in the path
  still receives the normal auto hold-short (see Decisions).
- The unreachable-runway safety net (route-reach warning + lineup bailout, added
  on `fix/taxi-route-unreachable-runway-lineup`) is **runway-lineup-specific and
  does not apply** to progressive endpoints.

### 3. Guidance & terminal end-state

- Taxiing, intermediate hold-shorts (Continue to resume), and auto
  runway-crossing holds behave exactly as today.
- At the terminator, guidance enters a **new terminal "progressive hold"
  state** instead of a gate/runway arrival:
  - announce the end message (§4),
  - stop the steering tone,
  - **no** lineup phase, **no** Takeoff-Assist auto-activate,
  - remain held until the pilot sets a new route.
- This is distinct from an *intermediate* hold-short: an intermediate hold-short
  expects `ContinuePastHoldShort` to resume along the same route; the terminal
  progressive hold has nothing left to continue to — the pilot re-plans.

### 4. End announcements

- Hold short of runway: *"Hold short of runway 09. Set a new route when cleared."*
- Hold short of taxiway: *"Hold short of taxiway C. Set a new route when cleared."*
- After crossing runway: *"Across runway 09. Hold position. Set a new route when cleared."*
- End of last taxiway: *"Route ended. Hold position. Set a new route when cleared."*

### 5. Continuation workflow

- Each leg is a **fresh route from the current aircraft position** — the
  planner's existing position-refresh-on-Calculate already supports this; no
  route appending.
- After a terminal progressive hold, the pilot re-opens the planner and either
  picks **Progressive Taxi** again (next cleared leg) or switches to **Runway /
  Gate** to finish.

## Decisions (confirmed in brainstorm)

1. **Terminators are fully flexible** — hold short of a runway, hold short of a
   taxiway, or after crossing a runway must all be supported (plus the
   end-of-taxiway fallback).
2. **Intermediate runway crossings keep the safety hold-short + Continue.** Only
   the *terminator* runway under "After crossing runway" is treated as cleared
   (hold-short suppressed). A runway crossed partway through a leg still stops
   the pilot for an explicit Continue.
3. **Build on the normal taxi-dialog machinery** rather than a new engine.
4. **End-announcement wording** as in §4.

## Reused vs. new

- **Reused:** taxiway-sequence rows, per-row hold-shorts, `LoadRoute`, the
  constrained router, `InsertRunwayCrossingHoldShorts` (intermediate crossings),
  `ContinuePastHoldShort` (intermediate holds), the Cross Runway far-side node
  math, position-refresh start.
- **New:** the last-row terminator control (type + target); the
  hold-short-of-taxiway intersection finder; the terminal progressive-hold state
  + announcements; suppression of the hold-short on the cleared crossing;
  removal of the standalone Cross Runway destination type (folded in).

## Edge cases

- Last taxiway does not actually reach the chosen terminator runway/taxiway →
  surface a clear "could not find …; check your entry" message (mirrors the
  existing per-row runway-hold-short mismatch warning) and do not silently
  mis-route.
- Terminator target picker should list only plausible options (e.g. taxiways
  the last taxiway can connect to; runways the path crosses or reaches).
- Removing all but the first row must keep a valid terminator on the first row.
- Switching Destination Type away from Progressive Taxi restores the hidden
  gate/runway picker and clears terminator state.
- A progressive route must NOT engage docking or lineup even if its endpoint
  happens to sit near a gate/runway node.

## Testing

- In-sim at an airport with both runway crossings and taxiway intersections
  (PHNL works). Exercise all four terminators; verify intermediate holds, the
  end announcements, no lineup/Takeoff-Assist firing, and the re-plan-to-gate
  handoff from the held position.
- Pure-geometry pieces (the hold-short-of-taxiway intersection finder and the
  far-side node reuse) get console-probe coverage in the existing `tools/`
  pattern (e.g. a `TaxiGuidanceProbe`-style replica), since the project has no
  unit-test project and verifies SimConnect-driven paths in the live sim.

## Open questions

None blocking. Terminator target-list filtering (edge cases above) to be refined
during implementation.

## Addendum (2026-06-16): cross-at-named-taxiway for the after-crossing terminator

After this branch was implemented, `main` gained a "cross at named taxiway"
refinement to the (now-removed) Cross Runway destination type — letting ATC's
*"cross runway 27 at Tango"* pin the crossing point rather than auto-picking the
nearest. On merging `main` into this branch, that capability was ported into the
**After crossing runway** terminator:

- The terminator row's taxiway combo (previously used only by *Hold short of
  taxiway*) doubles as an **optional "Cross at taxiway"** picker when the type is
  *After crossing runway*. `"(none)"` = nearest crossing automatically (the
  original §2 behaviour); any taxiway pins the crossing to that taxiway.
- The list is filtered to taxiways that physically cross the chosen runway
  (`TaxiAssistForm.GetTaxiwaysCrossingRunway`, ported from `main`), refreshed from
  the last row's runway pick on the combo's `DropDown` event so it tracks the
  runway regardless of fill order.
- Resolution reuses `FindFarSideRunwayNode(runway, crossAtTaxiway)`; a pinned
  taxiway that doesn't actually cross the runway yields the existing
  "Could not find taxiway X crossing runway Y … Check your entry." message.

This is purely additive: the default (`"(none)"`) leaves §2's auto-nearest
behaviour unchanged.

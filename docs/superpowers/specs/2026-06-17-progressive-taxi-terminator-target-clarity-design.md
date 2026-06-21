# Progressive Taxi — Terminator Target Clarity

**Date:** 2026-06-17
**Status:** Approved (brainstorm); pending spec review → implementation plan
**Area:** Taxi Guidance (`Forms/TaxiAssistForm.cs`)
**Builds on:** [`2026-06-13-progressive-taxi-design.md`](2026-06-13-progressive-taxi-design.md)

## Problem

In Progressive Taxi mode the last taxiway row exposes a **terminator type** combo
(`cmbTerminatorType`): *Hold short of runway / Hold short of taxiway / After
crossing runway / End of last taxiway*. The terminator's **target** is set in two
different places depending on type:

- **Runway** target (*Hold short of runway*, *After crossing runway*) → read from
  the last taxiway row's existing **"Hold short of runway"** combo
  (`LastRowHoldShortRunway()`).
- **Taxiway** target (*Hold short of taxiway*; optional *cross-at* for *After
  crossing runway*) → the terminator block's `cmbTerminatorTaxiway`.

This produces two confirmed clarity problems (user-reported):

1. **Where to set the target is unclear.** The runway target lives on the
   taxiway row, separated from the terminator type combo, so it is not obvious
   that the terminator's runway is configured there.
2. **The label contradicts the intent.** For *After crossing runway* the pilot
   picks the runway in a combo labeled **"Hold short of runway"** — the opposite
   of crossing it.

## Goal

Make the terminator block self-contained: terminator **type + its target** (runway
or taxiway) configured together in one place, with a label that always matches the
selected type. Restore a single consistent meaning to the per-row "Hold short of
runway" combo.

## Non-goals

- No change to terminator *behavior*, routing, end announcements, or the terminal
  ProgressiveHold state.
- No change to the four terminator type names.
- No change to intermediate hold-short handling on non-terminator rows.

## Design

### New controls

Add to the terminator block (inside `pnlTaxiways`, alongside `cmbTerminatorType`
and `cmbTerminatorTaxiway`):

- `lblTerminatorRunway` + `cmbTerminatorRunway` — the **runway** target, shown
  only for *Hold short of runway* and *After crossing runway*.

`cmbTerminatorRunway` is populated from the existing `_airportRunwayIds` list with
the `(none)` sentinel (`NO_RUNWAY_HOLDSHORT`) as the unselected default, using the
same `RebuildHoldShortRunwayCombo` helper the row combos use, and is repopulated on
airport load at the same sites those combos are.

It gets a unique Alt-mnemonic from the form's free pool (assigned during
implementation; the existing terminator mnemonics — Alt+N type, Alt+W / Alt+X
taxiway — must not collide).

### Layout (packed, no gaps)

The block shows 1–3 lines depending on type; visible target combos are packed
directly under the type combo:

| Terminator type | Line 1 | Line 2 | Line 3 |
|---|---|---|---|
| Hold short of runway | Type | **Runway** — "Runway to hold short of:" | — |
| Hold short of taxiway | Type | Taxiway — "Hold short of taxiway:" | — |
| After crossing runway | Type | **Runway** — "Runway to cross:" | Cross-at taxiway (optional) |
| End of last taxiway | Type | — | — |

- Block height becomes **dynamic**: a `_terminatorBlockHeightPx` field set by
  `RefreshTerminatorRow` based on the number of visible lines (≈28 px/line),
  read by `UpdateLayout` in place of the fixed `TERMINATOR_BLOCK_HEIGHT_PX = 55`.
- Tab order stays **Type → Runway → Taxiway** (hidden controls skip naturally).
- `cmbTerminatorRunway.SelectedIndexChanged` refreshes the cross-at taxiway list
  (type 2 filters that list to taxiways crossing the chosen runway).

### Labels & accessibility

`RefreshTerminatorRow` sets the runway combo's label / `AccessibleName` /
`AccessibleDescription` per type:

- Type 0: label **"Runway to hold short of:"**; description "Pick the runway this
  progressive leg holds short of."
- Type 2: label **"Runway to cross:"**; description "Pick the runway ATC cleared
  you to cross."

### Calculate-time changes (`OnCalculateClicked`, progressive branch)

- Read `runwayTarget` from `cmbTerminatorRunway` (reject `(none)`/empty with
  type-matched messages: "Pick the runway to hold short of." / "Pick the runway to
  cross.").
- **Delete** `LastRowHoldShortRunway()`; its other caller
  (`PopulateTerminatorTaxiwayList`, building the type-2 cross-at list) reads
  `cmbTerminatorRunway` instead.
- **Remove** `progRwyHoldShorts.Remove(progSeq.Count - 1)` — the last row's
  "Hold short of runway" combo is no longer the terminator target, so it is a
  plain intermediate hold-short like every other row.
- **Remove** the "last UI row diverges" guard (its sole rationale was that
  `LastRowHoldShortRunway()` read the last row's combo; with a dedicated combo the
  divergence is harmless, and empty trailing rows are already filtered by
  `GetSelectedTaxiwayNames`).

## Net effect

The terminator block fully describes the terminator (type + target, runway or
taxiway, in one place, with intent-matched labels). The per-row hold-short combos
regain one consistent meaning. The change is net-simplifying: one new combo + a
dynamic block height, against the removal of a helper, a special-case map edit, and
a guard.

## Edge cases

- Required-target validation unchanged (reject on `(none)`).
- Existing "could not find runway/taxiway crossing… check your entry." mismatch
  messages are unchanged.
- Switching Destination Type away from Progressive Taxi hides the runway combo with
  the rest of the block (`RefreshTerminatorRow` early-out).

## Testing

- Build the solution (`dotnet build MSFSBlindAssist.sln -c Debug`).
- In-sim at an airport with runway crossings and taxiway intersections (PHNL):
  exercise all four terminators, confirming the runway target is set in the block,
  labels match intent, and routing/end announcements are unchanged from the prior
  branch behavior.

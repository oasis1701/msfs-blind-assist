# Progressive Taxi — Hide Per-Row Runway Hold-Short

**Date:** 2026-06-17
**Status:** Approved (brainstorm); pending spec review → implementation plan
**Area:** Taxi Guidance (`Forms/TaxiAssistForm.cs`)
**Builds on:** [`2026-06-13-progressive-taxi-design.md`](2026-06-13-progressive-taxi-design.md),
[`2026-06-17-progressive-taxi-terminator-target-clarity-design.md`](2026-06-17-progressive-taxi-terminator-target-clarity-design.md)

## Problem

In Progressive Taxi mode the taxiway rows still show the per-row **"Hold short of
runway after this taxiway"** label + combo, while the terminator block ALSO offers
**"Hold short of runway"** as a leg-end condition. On the same row this presents
two "hold short of runway" controls with no cue distinguishing them; with a single
taxiway row (the common case) the row *is* the last row, so the per-row combo sits
directly alongside the terminator's runway combo. User-reported as confusing.

## Decision (confirmed in brainstorm)

In Progressive Taxi mode, **hide the per-row "Hold short of runway" label + combo on
every taxiway row** (first row + all dynamic rows). The terminator becomes the single
place a runway hold-short is expressed for the leg. The per-row **"Hold short"
checkbox stays visible** — it is a distinct concept ("stop at the end of *this*
taxiway", not a runway hold-short) and remains a legitimate intermediate stop on a
multi-taxiway progressive leg.

## Goal

One unambiguous runway-hold-short control in Progressive Taxi mode (the terminator).
No behavioral change to routing — hidden per-row runway holds contribute nothing,
exactly as if the user left them at `(none)`.

## Non-goals

- No change to non-progressive (Runway / Gate / Deice) modes — the per-row runway
  hold-short combos behave exactly as today there.
- No change to automatic runway-crossing hold-shorts (`InsertRunwayCrossingHoldShorts`)
  or to the terminator behavior.
- The per-row "Hold short" checkbox is unchanged (stays visible in all modes).

## Design

### Visibility helper

Add `SetRowRunwayHoldShortVisible(bool visible)`:

- **First row:** toggle `lblFirstHoldShortRunway` (promoted from a local var in
  `InitializeFormControls` to a field) and `cmbFirstHoldShortRunway`.
- **Dynamic rows:** toggle each row's `holdShortRunway` combo (from the
  `_additionalTaxiways` tuple) and its label. The label is located via
  `pnlTaxiways.Controls.OfType<Label>().Where(l => l.Text == HOLD_SHORT_RUNWAY_LABEL)`
  — only the per-row hold-short labels carry that exact constant text; the
  terminator's runway label reads `"R&unway to hold short of:"`, so there is no
  collision, and the taxiway labels read `"Taxiway N:"`.
- **On hide (`visible == false`), reset each combo to `(none)`** (`SelectedIndex = 0`).
  This guarantees a stale selection (e.g. a runway picked while in Runway mode, then
  switched to Progressive) cannot leak into the route: `GetUserRunwayHoldShorts()`
  skips `(none)` entries, and `OnAddTaxiwayClicked`'s `prevHasHoldShort` check reads
  `(none)` correctly. **No changes are required to `GetUserRunwayHoldShorts` or
  `OnAddTaxiwayClicked`.**

### Wiring

- Call `SetRowRunwayHoldShortVisible(cmbDestType.SelectedIndex != 2)` from
  `OnDestTypeChanged` (so toggling the destination type shows/hides + resets the
  per-row runway combos).
- Call it at the end of `AddTaxiwayRow` (after the new row's controls are added) so a
  row added while already in Progressive Taxi mode is created hidden + reset. (The
  helper iterating all rows each call is fine — at most `MAX_ADDITIONAL_TAXIWAYS` = 20
  rows.)

### Tab order

Hidden controls are skipped automatically by WinForms, so no TabIndex changes are
needed; in Progressive Taxi mode Tab flows taxiway combo → "Hold short" checkbox →
Remove (→ next row) → terminator block, with the per-row runway combos absent.

## Net effect

Progressive Taxi rows show: taxiway combo + "Hold short" checkbox + Remove. The
terminator block is the only runway-hold-short control. Routing is byte-identical to
entering no per-row runway holds (reset-to-`(none)` on hide). Non-progressive modes
are untouched.

## Edge cases

- **Switching Progressive → Runway/Gate/Deice:** the helper shows the combos again,
  all at `(none)` (their reset state). The user re-picks any intermediate hold-short
  as before. Acceptable — switching destination type is a deliberate reconfiguration.
- **Row added then mode switched, or mode switched then row added:** both paths route
  through the helper (`OnDestTypeChanged` toggles existing rows; `AddTaxiwayRow` calls
  the helper for the new row), so visibility is always consistent with the current
  mode.
- **Removing rows in progressive mode:** `RemoveTaxiwaysFrom` is unchanged; it removes
  the row's controls (including the hold-short label by its existing Y-coordinate
  lookup) regardless of visibility.

## Testing

- `dotnet build MSFSBlindAssist.sln -c Debug` (0 errors).
- In-sim (repo owner): in Progressive Taxi mode confirm no per-row "Hold short of
  runway" combo appears on any row (first or added), the "Hold short" checkbox still
  appears, and the terminator is the only runway-hold-short control. Switch to Runway
  mode and confirm the per-row combos reappear at `(none)`. Calculate a progressive
  leg that crosses a runway and confirm the automatic crossing hold-short still fires
  (unchanged routing).

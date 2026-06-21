# Taxiway Row Object — Refactor

**Date:** 2026-06-17
**Status:** Approved (brainstorm); pending spec review → implementation plan
**Area:** Taxi Guidance (`Forms/TaxiAssistForm.cs`)

## Problem

Each dynamically-added taxiway row in the planner form is a group of controls stored
as a 5-tuple in `_additionalTaxiways`
(`(Label label, ComboBox combo, CheckBox holdShort, ComboBox holdShortRunway, Button removeBtn)`).
The row's **second-line "Hold short of runway:" label is not tracked** in the tuple.
Consequently:

- `RemoveTaxiwaysFrom` finds that label by **matching its pixel Y-coordinate**
  (`l.Location.Y == rowIdx * DYNAMIC_ROW_HEIGHT_PX + 45`) by scanning all panel
  controls. If the layout ever shifts, this can grab the wrong label or miss it.
- `SetRowRunwayHoldShortVisible` likewise resorts to a **text match**
  (`Label.Text == HOLD_SHORT_RUNWAY_LABEL`) to find the per-row labels to hide.

Both are brittle workarounds for the same gap: the row doesn't own a reference to its
own label.

## Goal

Make each dynamic row a small object that holds a direct reference to **every** control
it owns, including the second-line label. Removal and per-row operations then iterate
"everything this row owns" — no panel scanning, no pixel or text guesswork. **Purely a
robustness/maintainability change; behavior is identical.**

## Non-goals

- No behavior change: same controls, positions, tab order, removal result, and
  Progressive-Taxi per-row hiding.
- The **first taxiway row** (its own fields, never removed) is left as-is — it has no
  removal / Y-lookup problem to fix. (Confirmed in brainstorm: dynamic rows only.)
- No layout, mnemonic, or routing changes.

## Design

### New nested type

A private nested class in `TaxiAssistForm`:

```csharp
private sealed class TaxiwayRow
{
    public Label Label = null!;                 // line 1: "Taxiway N:"
    public ComboBox Combo = null!;              // line 1: taxiway selector
    public CheckBox HoldShort = null!;          // line 1: "Hold short" checkbox
    public Label HoldShortRunwayLabel = null!;  // line 2: "Hold short of runway:" (now tracked)
    public ComboBox HoldShortRunway = null!;    // line 2: runway combo
    public Button RemoveBtn = null!;

    // All controls this row owns — used to remove/dispose the row in one pass.
    public IEnumerable<Control> Controls =>
        new Control[] { Label, Combo, HoldShort, HoldShortRunwayLabel, HoldShortRunway, RemoveBtn };
}
```

`private List<...tuple...> _additionalTaxiways` becomes
`private List<TaxiwayRow> _additionalTaxiways`.

### Call-site changes

- **`AddTaxiwayRow`** — after creating the controls (unchanged: same `Location`,
  `Width`, `TabIndex`, event wiring, `pnlTaxiways.Controls.Add(...)`), construct and
  store the row, including the second-line label `lblRunwayHs`:
  ```csharp
  _additionalTaxiways.Add(new TaxiwayRow
  {
      Label = label, Combo = combo, HoldShort = holdShortChk,
      HoldShortRunwayLabel = lblRunwayHs, HoldShortRunway = holdShortRunwayCmb,
      RemoveBtn = removeBtn
  });
  ```
- **`RemoveTaxiwaysFrom`** — replace the five explicit `Remove`/`Dispose` calls **and
  the Y-coordinate companion-label lookup block** (plus its comment) with a single loop
  over the row's controls:
  ```csharp
  while (_additionalTaxiways.Count > fromIndex)
  {
      var row = _additionalTaxiways[^1];
      foreach (var c in row.Controls) { pnlTaxiways.Controls.Remove(c); c.Dispose(); }
      _additionalTaxiways.RemoveAt(_additionalTaxiways.Count - 1);
  }
  ```
  The trailing `RefreshTerminatorRow()` / `UpdateAddTaxiwayButtonState()` calls are
  unchanged. (Only tail rows are removed, so no earlier-row repositioning exists to
  preserve.)
- **Tuple-destructuring consumers** → named-field access:
  - `OnAddTaxiwayClicked`: `_additionalTaxiways[^1].combo` → `.Combo`; the
    `var (_, _, hsChk, hsRwy, _) = _additionalTaxiways[^1];` destructure →
    `var last = _additionalTaxiways[^1];` then `last.HoldShort` / `last.HoldShortRunway`.
  - `UpdateAddTaxiwayButtonState`: `[^1].combo` → `.Combo`.
  - `GetSelectedTaxiwayNames`: `foreach (var (_, combo, _, _, _) in …)` →
    `foreach (var row in …) { … row.Combo … }`.
  - the user-hold-short-index reader: `foreach (var (_, combo, holdShortChk, _, _) in …)`
    → `row.Combo` / `row.HoldShort`.
  - `GetUserRunwayHoldShorts`: `foreach (var (_, combo, _, holdShortRunwayCmb, _) in …)`
    → `row.Combo` / `row.HoldShortRunway`.
  - `Count` / `[^1]` usages (RefreshTerminatorRow, UpdateLayout, AddTaxiwayRow index,
    guards) are unaffected — `List<TaxiwayRow>` has the same shape.
- **`SetRowRunwayHoldShortVisible`** — for each row, toggle `row.HoldShortRunway` and
  `row.HoldShortRunwayLabel` directly (resetting the combo to `(none)` on hide, as
  today). **Delete** the `foreach (Control ctrl in pnlTaxiways.Controls) { if (ctrl is
  Label lbl && lbl.Text == HOLD_SHORT_RUNWAY_LABEL) … }` text-match loop — the label is
  now owned by the row. First-row label/combo handling (`lblFirstHoldShortRunway` /
  `cmbFirstHoldShortRunway` fields) is unchanged.

## Edge cases

- **Disposal parity:** the new loop both removes from the panel AND `Dispose()`s each
  control — identical to today's explicit calls (which removed + disposed all six,
  counting the Y-found label).
- **`ClearAllAdditionalTaxiways` / `RemoveTaxiwaysAfter`** route through
  `RemoveTaxiwaysFrom`, so they inherit the cleaner removal automatically.

## Testing

- `dotnet build MSFSBlindAssist.sln -c Debug` — 0 errors.
- The `ProgressiveTaxiProbe` is graph-geometry only and does not exercise the form, so
  verification is build + a manual/in-sim check:
  - Add several taxiway rows; remove from the end and from the middle (via a row's
    Remove button / changing an earlier taxiway, which calls `RemoveTaxiwaysAfter`);
    confirm **no orphaned "Hold short of runway:" labels** remain in the panel and the
    panel resizes correctly.
  - In Progressive Taxi mode, confirm per-row "Hold short of runway" combos+labels are
    still hidden (and restored, at "(none)", when switching back to Runway/Gate).

# Hide Per-Row Runway Hold-Short in Progressive Taxi — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** In Progressive Taxi mode, hide the per-row "Hold short of runway" label + combo on every taxiway row (keeping the "Hold short" checkbox), so the terminator block is the single runway-hold-short control.

**Architecture:** Single-file change in `MSFSBlindAssist/Forms/TaxiAssistForm.cs`. Promote the first row's hold-short label to a field, add one visibility helper, and call it from the two places row visibility can change (`OnDestTypeChanged`, `AddTaxiwayRow`). On hide, reset each combo to `(none)` so routing is byte-identical to entering no per-row holds — no changes to `GetUserRunwayHoldShorts`/`OnAddTaxiwayClicked`.

**Tech Stack:** .NET 9, Windows Forms, screen-reader-first (NVDA/JAWS). No unit-test project — verification is `dotnet build` + an in-sim test plan run by the repo owner.

**Spec:** `docs/superpowers/specs/2026-06-17-progressive-taxi-hide-per-row-runway-holdshort-design.md`

---

## File Structure

- **Modify:** `MSFSBlindAssist/Forms/TaxiAssistForm.cs` — field declaration (~line 65), first-row label creation (~line 425), new helper `SetRowRunwayHoldShortVisible` (near `GetUserRunwayHoldShorts`, ~line 1567), and two call sites (`OnDestTypeChanged` ~line 1147, `AddTaxiwayRow` ~line 1383).

## Build / verify command (after the code task)

```
dotnet build MSFSBlindAssist.sln -c Debug
```
Expected: `Build succeeded`. ALWAYS build the SOLUTION (never the bare `.csproj` — it writes to a different folder than the app runs from). MSB3021 file-lock = the MSFSBA app is running; that is an environment issue, not a code error. Do NOT push.

> Line numbers below are approximate (`~`); match on the exact code TEXT shown. READ each region before editing.

---

### Task 1: Hide the per-row runway hold-short in Progressive Taxi mode

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs`

- [ ] **Step 1: Promote the first-row hold-short label to a field.**

Find the first-row field declarations (~lines 62-66):

```csharp
    private Label lblFirstTaxiway = null!;
    private ComboBox cmbFirstTaxiway = null!;
    private CheckBox chkFirstHoldShort = null!;
    private ComboBox cmbFirstHoldShortRunway = null!;
    private Button btnAddTaxiway = null!;
```

Replace with (add the `lblFirstHoldShortRunway` field):

```csharp
    private Label lblFirstTaxiway = null!;
    private ComboBox cmbFirstTaxiway = null!;
    private CheckBox chkFirstHoldShort = null!;
    private Label lblFirstHoldShortRunway = null!;
    private ComboBox cmbFirstHoldShortRunway = null!;
    private Button btnAddTaxiway = null!;
```

- [ ] **Step 2: Assign the field instead of a local in `InitializeFormControls`.**

Find (~line 425):

```csharp
        Label lblFirstHoldShortRunway = new Label
        {
            Text = HOLD_SHORT_RUNWAY_LABEL,
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Hold short of runway after first taxiway label"
        };
```

Replace the first line only — change `Label lblFirstHoldShortRunway = new Label` to `lblFirstHoldShortRunway = new Label` (drop the leading `Label ` type so it assigns the field, not a new local):

```csharp
        lblFirstHoldShortRunway = new Label
        {
            Text = HOLD_SHORT_RUNWAY_LABEL,
            Location = new System.Drawing.Point(labelX, y),
            AutoSize = true,
            AccessibleName = "Hold short of runway after first taxiway label"
        };
```

- [ ] **Step 3: Add the `SetRowRunwayHoldShortVisible` helper.**

Find the end of `GetUserRunwayHoldShorts` (~line 1567):

```csharp
        return result;
    }

    private List<string> SortTaxiwaysByDistance(List<string> taxiwayNames)
```

Insert the new method between `GetUserRunwayHoldShorts`'s closing brace and `SortTaxiwaysByDistance`:

```csharp
        return result;
    }

    /// <summary>
    /// Show or hide the per-row "Hold short of runway" label + combo across the
    /// first taxiway slot and every dynamic row. In Progressive Taxi mode these
    /// are hidden (the terminator block is the single runway-hold-short control);
    /// in all other destination modes they are shown. On hide, each combo is reset
    /// to "(none)" so a stale selection cannot leak into the route via
    /// GetUserRunwayHoldShorts / OnAddTaxiwayClicked. The "Hold short" checkbox is
    /// intentionally NOT touched (it is a separate concept and stays visible).
    /// </summary>
    private void SetRowRunwayHoldShortVisible(bool visible)
    {
        lblFirstHoldShortRunway.Visible = visible;
        cmbFirstHoldShortRunway.Visible = visible;
        if (!visible) cmbFirstHoldShortRunway.SelectedIndex = 0;

        foreach (var (_, _, _, holdShortRunwayCmb, _) in _additionalTaxiways)
        {
            holdShortRunwayCmb.Visible = visible;
            if (!visible) holdShortRunwayCmb.SelectedIndex = 0;
        }

        // The dynamic-row hold-short labels are not tracked in the row tuple. They
        // are the ONLY panel labels carrying the HOLD_SHORT_RUNWAY_LABEL text
        // (taxiway labels read "Taxiway N:"; the terminator runway label reads
        // "R&unway to hold short of:"), so matching on that exact text finds
        // exactly the per-row hold-short labels.
        foreach (Control ctrl in pnlTaxiways.Controls)
        {
            if (ctrl is Label lbl && lbl.Text == HOLD_SHORT_RUNWAY_LABEL)
                lbl.Visible = visible;
        }
    }

    private List<string> SortTaxiwaysByDistance(List<string> taxiwayNames)
```

- [ ] **Step 4: Wire the helper into `OnDestTypeChanged`.**

Find (~lines 1146-1149):

```csharp
        lblDestination.Visible = !isProgressive;
        cmbDestination.Visible = !isProgressive;

        PopulateDestinations();
```

Replace with (add the helper call after the destination-picker toggle, before `PopulateDestinations`):

```csharp
        lblDestination.Visible = !isProgressive;
        cmbDestination.Visible = !isProgressive;

        // Progressive Taxi mode hides the per-row "Hold short of runway" combos so
        // the terminator block is the single runway-hold-short control; other modes
        // show them. (Resets hidden combos to "(none)" so routing is unaffected.)
        SetRowRunwayHoldShortVisible(!isProgressive);

        PopulateDestinations();
```

- [ ] **Step 5: Wire the helper into `AddTaxiwayRow` so new rows respect the current mode.**

Find (~lines 1378-1383):

```csharp
        _additionalTaxiways.Add((label, combo, holdShortChk, holdShortRunwayCmb, removeBtn));

        // Update panel height and reposition controls below. RefreshTerminatorRow
        // relocates the Progressive Taxi terminator block onto this new last row
        // (and calls UpdateLayout to resize).
        RefreshTerminatorRow();
```

Replace with (apply current-mode visibility to the just-added row before laying out):

```csharp
        _additionalTaxiways.Add((label, combo, holdShortChk, holdShortRunwayCmb, removeBtn));

        // A row added while already in Progressive Taxi mode must start with its
        // per-row "Hold short of runway" control hidden (the terminator owns the
        // runway hold-short). Applies current-mode visibility to all rows.
        SetRowRunwayHoldShortVisible(cmbDestType.SelectedIndex != 2);

        // Update panel height and reposition controls below. RefreshTerminatorRow
        // relocates the Progressive Taxi terminator block onto this new last row
        // (and calls UpdateLayout to resize).
        RefreshTerminatorRow();
```

- [ ] **Step 6: Build.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors (pre-existing warnings only). If `lblFirstHoldShortRunway` is reported as used-before-assigned or unassigned, re-check Steps 1-2 (the field must be declared in Step 1 and assigned via Step 2's edit).

- [ ] **Step 7: Commit.**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "feat(taxi): hide per-row runway hold-short in Progressive Taxi mode

In Progressive Taxi mode the per-row 'Hold short of runway' label+combo
collided with the terminator's runway hold-short. Hide them on every row
(first + dynamic) via SetRowRunwayHoldShortVisible, called from
OnDestTypeChanged and AddTaxiwayRow; reset to (none) on hide so routing is
unchanged. The 'Hold short' checkbox stays. Other modes are unaffected.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Verification

**Files:** none (verification only)

- [ ] **Step 1: Clean solution build.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 2: Confirm the build landed in the run path.**

Run (PowerShell): `(Get-Item 'MSFSBlindAssist\bin\x64\Debug\net9.0-windows\MSFSBlindAssist.exe').LastWriteTime`
Expected: a timestamp from the current build.

- [ ] **Step 3: Record the in-sim test plan in the PR description** (repo owner runs it):
  1. Open Taxi Guidance. With Destination Type = **Runway**, confirm the first taxiway row shows the "Hold short of runway after this taxiway" combo (unchanged).
  2. Switch Destination Type to **Progressive Taxi**: the per-row "Hold short of runway" combo (and its label) disappears from the first row; the **"Hold short" checkbox remains**; the terminator block is the only runway-hold-short control.
  3. Add a taxiway row in Progressive Taxi mode: the new row also has **no** "Hold short of runway" combo, but keeps its "Hold short" checkbox.
  4. Switch back to **Runway**: the per-row combos reappear, all at "(none)".
  5. In Runway mode, pick a runway in a per-row hold-short combo, then switch to Progressive Taxi and back to Runway: the combo is back at "(none)" (reset on hide) — confirm this is acceptable.
  6. Calculate a progressive leg whose route crosses a runway: the **automatic** runway-crossing hold-short still fires (routing unchanged).
  7. Tab through a Progressive Taxi row: focus goes taxiway combo → "Hold short" checkbox → Remove → (next row / terminator), with no stop on a per-row runway combo.

- [ ] **Step 4: Hand off to `superpowers:finishing-a-development-branch`.**

---

## Self-Review Notes

- **Spec coverage:** hide first-row + dynamic-row runway combos+labels in progressive mode (Step 3 helper, Steps 4-5 wiring); keep the "Hold short" checkbox (helper never touches it); reset-to-(none) on hide so `GetUserRunwayHoldShorts`/`OnAddTaxiwayClicked` need no changes (Step 3); non-progressive modes unaffected (helper shows them when `visible == true`). All spec sections covered.
- **Type consistency:** `lblFirstHoldShortRunway` declared (Step 1) and assigned (Step 2); `SetRowRunwayHoldShortVisible(bool)` defined (Step 3) and called with `!isProgressive` (Step 4) and `cmbDestType.SelectedIndex != 2` (Step 5) — both evaluate to "visible when not progressive". Tuple destructure `(_, _, _, holdShortRunwayCmb, _)` matches the existing 5-tuple shape used in `GetUserRunwayHoldShorts`.
- **No placeholders:** every step shows full before/after text and exact build/commit commands.
- **Init-ordering safety:** `cmbDestType.SelectedIndex = 0` is set at ~line 309 BEFORE the `OnDestTypeChanged` handler is attached at ~line 310, so the handler does not fire during construction — `lblFirstHoldShortRunway` (created later in init) is always non-null when `SetRowRunwayHoldShortVisible` runs.

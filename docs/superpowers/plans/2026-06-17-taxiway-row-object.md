# TaxiwayRow Object Refactor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the dynamic taxiway-row 5-tuple in the planner form into a `TaxiwayRow` class that holds a direct reference to every control it owns (including the second-line "Hold short of runway:" label), retiring the Y-coordinate label lookup in `RemoveTaxiwaysFrom` and the text-match in `SetRowRunwayHoldShortVisible`.

**Architecture:** One file, `Forms/TaxiAssistForm.cs`. Behavior-preserving: same controls, positions, tab order, removal result, and Progressive-Taxi hiding. The type change breaks every `_additionalTaxiways` consumer until all are updated, so the whole refactor is a single atomic commit.

**Tech Stack:** .NET 9, Windows Forms. No automated UI tests — verification is `dotnet build` + a manual/in-sim check.

**Spec:** `docs/superpowers/specs/2026-06-17-taxiway-row-object-design.md`

---

## Build / verify command

```
dotnet build MSFSBlindAssist.sln -c Debug
```
`Build succeeded`, 0 errors. Build the SOLUTION (never the bare `.csproj`). MSB3021 = app running (environment). Do NOT push.

> Line numbers are approximate (`~`); match on the exact code TEXT. READ each region before editing. All edits are in `MSFSBlindAssist/Forms/TaxiAssistForm.cs`.

---

### Task 1: Convert the dynamic-row tuple to a `TaxiwayRow` class

**Files:** Modify `MSFSBlindAssist/Forms/TaxiAssistForm.cs`

- [ ] **Step 1: Replace the field declaration + comment with the typed list and the nested class.**

Find (~lines 116-120):

```csharp
    // Dynamic taxiway controls. Tuple now carries the runway-hold-short combo
    // alongside the existing combo / hold-short checkbox / remove button so
    // OnCalculateClicked can iterate them all in one pass.
    private Panel pnlTaxiways = null!;
    private List<(Label label, ComboBox combo, CheckBox holdShort, ComboBox holdShortRunway, Button removeBtn)> _additionalTaxiways = new();
```

Replace with:

```csharp
    private Panel pnlTaxiways = null!;

    /// <summary>
    /// One dynamically-added taxiway row in the planner. Holds a direct reference
    /// to every control the row owns — including the second-line
    /// "Hold short of runway:" label — so the row can be removed and its per-row
    /// controls toggled without scanning the panel by pixel position or label text.
    /// </summary>
    private sealed class TaxiwayRow
    {
        public Label Label = null!;                 // line 1: "Taxiway N:" label
        public ComboBox Combo = null!;              // line 1: taxiway selector
        public CheckBox HoldShort = null!;          // line 1: "Hold short" checkbox
        public Label HoldShortRunwayLabel = null!;  // line 2: "Hold short of runway:" label
        public ComboBox HoldShortRunway = null!;    // line 2: runway combo
        public Button RemoveBtn = null!;            // line 1: "Remove" button

        /// <summary>Every control this row owns — used to remove/dispose the row in one pass.</summary>
        public IEnumerable<Control> Controls =>
            new Control[] { Label, Combo, HoldShort, HoldShortRunwayLabel, HoldShortRunway, RemoveBtn };
    }

    private List<TaxiwayRow> _additionalTaxiways = new();
```

- [ ] **Step 2: Store the row object in `AddTaxiwayRow`.**

Find (~line 1384):

```csharp
        _additionalTaxiways.Add((label, combo, holdShortChk, holdShortRunwayCmb, removeBtn));
```

Replace with (also stores the second-line label `lblRunwayHs`, which is created earlier in this method):

```csharp
        _additionalTaxiways.Add(new TaxiwayRow
        {
            Label = label,
            Combo = combo,
            HoldShort = holdShortChk,
            HoldShortRunwayLabel = lblRunwayHs,
            HoldShortRunway = holdShortRunwayCmb,
            RemoveBtn = removeBtn
        });
```

- [ ] **Step 3: Simplify `RemoveTaxiwaysFrom` — remove via the row's owned controls.**

Find (~lines 1412-1453) — the whole `while` block including the leading comment and the Y-coordinate companion-label lookup:

```csharp
    private void RemoveTaxiwaysFrom(int fromIndex)
    {
        // Remove this taxiway and all after it. The 5-tuple gained the
        // hold-short-of-runway combo; we also need to find and remove the
        // small "Hold short of runway:" label that accompanies it (it's not
        // tracked in the tuple to keep the destructuring tidier; we look it
        // up by Y-coordinate in the panel below). Both the combo and that
        // label live on line 2 of the row.
        while (_additionalTaxiways.Count > fromIndex)
        {
            var (label, combo, holdShortChk, holdShortRunwayCmb, removeBtn) = _additionalTaxiways[^1];
            int rowIdx = _additionalTaxiways.Count - 1;
            int line2Y = rowIdx * DYNAMIC_ROW_HEIGHT_PX + 45;

            // The companion "Hold short of runway:" label sits at Y == line2Y
            // and is the only Label in the panel at that Y other than the
            // taxiway-row label (which is at Y == row's panelY, not panelY+45).
            Label? companionLabel = null;
            foreach (Control c in pnlTaxiways.Controls)
            {
                if (c is Label l && l.Location.Y == line2Y)
                {
                    companionLabel = l;
                    break;
                }
            }

            pnlTaxiways.Controls.Remove(label);
            pnlTaxiways.Controls.Remove(combo);
            pnlTaxiways.Controls.Remove(holdShortChk);
            pnlTaxiways.Controls.Remove(removeBtn);
            pnlTaxiways.Controls.Remove(holdShortRunwayCmb);
            if (companionLabel != null) pnlTaxiways.Controls.Remove(companionLabel);

            label.Dispose();
            combo.Dispose();
            holdShortChk.Dispose();
            removeBtn.Dispose();
            holdShortRunwayCmb.Dispose();
            companionLabel?.Dispose();
            _additionalTaxiways.RemoveAt(_additionalTaxiways.Count - 1);
        }
```

Replace with:

```csharp
    private void RemoveTaxiwaysFrom(int fromIndex)
    {
        // Remove this taxiway row and all after it. Each row owns its controls
        // (including the second-line "Hold short of runway:" label), so removal is
        // a single pass over row.Controls — no panel scanning by pixel position.
        while (_additionalTaxiways.Count > fromIndex)
        {
            var row = _additionalTaxiways[^1];
            foreach (var c in row.Controls)
            {
                pnlTaxiways.Controls.Remove(c);
                c.Dispose();
            }
            _additionalTaxiways.RemoveAt(_additionalTaxiways.Count - 1);
        }
```

(Leave the rest of the method — the trailing `RefreshTerminatorRow()` / `UpdateAddTaxiwayButtonState()` — unchanged.)

- [ ] **Step 4: Update `OnAddTaxiwayClicked` destructures.**

Find (~line 1189):

```csharp
            previousTaxiway = _additionalTaxiways[^1].combo.SelectedItem?.ToString();
```

Replace with:

```csharp
            previousTaxiway = _additionalTaxiways[^1].Combo.SelectedItem?.ToString();
```

Find (~lines 1234-1240):

```csharp
        else
        {
            var (_, _, hsChk, hsRwy, _) = _additionalTaxiways[^1];
            string? rwy = hsRwy.SelectedItem?.ToString();
            prevHasHoldShort =
                hsChk.Checked ||
```

Replace with:

```csharp
        else
        {
            var last = _additionalTaxiways[^1];
            string? rwy = last.HoldShortRunway.SelectedItem?.ToString();
            prevHasHoldShort =
                last.HoldShort.Checked ||
```

- [ ] **Step 5: Update `UpdateAddTaxiwayButtonState`.**

Find (~line 1487):

```csharp
            lastSelected = _additionalTaxiways[^1].combo.SelectedItem?.ToString();
```

Replace with:

```csharp
            lastSelected = _additionalTaxiways[^1].Combo.SelectedItem?.ToString();
```

- [ ] **Step 6: Update `GetSelectedTaxiwayNames`.**

Find (~lines 1501-1506):

```csharp
        foreach (var (_, combo, _, _, _) in _additionalTaxiways)
        {
            string? sel = combo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(sel) && !sel.StartsWith("(None"))
                names.Add(sel);
        }
```

Replace with:

```csharp
        foreach (var row in _additionalTaxiways)
        {
            string? sel = row.Combo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(sel) && !sel.StartsWith("(None"))
                names.Add(sel);
        }
```

- [ ] **Step 7: Update `GetUserHoldShortIndices`.**

Find (~lines 1526-1535):

```csharp
        foreach (var (_, combo, holdShortChk, _, _) in _additionalTaxiways)
        {
            string? sel = combo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(sel) && !sel.StartsWith("(None"))
            {
                if (holdShortChk.Checked)
                    indices.Add(seqIndex);
                seqIndex++;
            }
        }
```

Replace with:

```csharp
        foreach (var row in _additionalTaxiways)
        {
            string? sel = row.Combo.SelectedItem?.ToString();
            if (!string.IsNullOrEmpty(sel) && !sel.StartsWith("(None"))
            {
                if (row.HoldShort.Checked)
                    indices.Add(seqIndex);
                seqIndex++;
            }
        }
```

- [ ] **Step 8: Update `GetUserRunwayHoldShorts`.**

Find (~lines 1565-1567):

```csharp
        foreach (var (_, combo, _, holdShortRunwayCmb, _) in _additionalTaxiways)
        {
            string? sel = combo.SelectedItem?.ToString();
```

Replace with:

```csharp
        foreach (var row in _additionalTaxiways)
        {
            string? sel = row.Combo.SelectedItem?.ToString();
```

Then find, a few lines below within the same loop (~line 1569-1572):

```csharp
                string? rwy = holdShortRunwayCmb.SelectedItem?.ToString();
```

Replace with:

```csharp
                string? rwy = row.HoldShortRunway.SelectedItem?.ToString();
```

(READ the loop body to confirm `holdShortRunwayCmb` is referenced exactly once inside it; update that reference to `row.HoldShortRunway`.)

- [ ] **Step 9: Simplify `SetRowRunwayHoldShortVisible` — use the owned label, drop the text-match loop.**

Find (~lines 1598-1616):

```csharp
        foreach (var (_, _, _, holdShortRunwayCmb, _) in _additionalTaxiways)
        {
            holdShortRunwayCmb.Visible = visible;
            if (!visible && holdShortRunwayCmb.SelectedIndex != 0)
                holdShortRunwayCmb.SelectedIndex = 0;
        }

        // The first-row label/combo above live on this.Controls and are handled by
        // field reference. The dynamic-row hold-short labels in pnlTaxiways are not
        // tracked in the row tuple; they are the ONLY panel labels carrying the
        // HOLD_SHORT_RUNWAY_LABEL text
        // (taxiway labels read "Taxiway N:"; the terminator runway label reads
        // "R&unway to hold short of:"), so matching on that exact text finds
        // exactly the per-row hold-short labels.
        foreach (Control ctrl in pnlTaxiways.Controls)
        {
            if (ctrl is Label lbl && lbl.Text == HOLD_SHORT_RUNWAY_LABEL)
                lbl.Visible = visible;
        }
```

Replace with (each row owns its label, so toggle it directly — no panel text-scan):

```csharp
        // Each dynamic row owns both its second-line label and combo, so toggle
        // them directly. (The first-row label/combo above live on this.Controls
        // and are handled by field reference.)
        foreach (var row in _additionalTaxiways)
        {
            row.HoldShortRunwayLabel.Visible = visible;
            row.HoldShortRunway.Visible = visible;
            if (!visible && row.HoldShortRunway.SelectedIndex != 0)
                row.HoldShortRunway.SelectedIndex = 0;
        }
```

- [ ] **Step 10: Build and confirm no tuple-member references remain.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors. A compile error like "`(Label, ComboBox, …)` does not contain a definition for `combo`" means a consumer site was missed — search `TaxiAssistForm.cs` for `_additionalTaxiways` and `.combo` / `.holdShort` / `.holdShortRunway` (lowercase tuple members) and convert any stragglers to the `TaxiwayRow` named fields (`.Combo` / `.HoldShort` / `.HoldShortRunway`).

- [ ] **Step 11: Commit.**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "refactor(taxi): dynamic taxiway rows own their controls (TaxiwayRow)

Replace the _additionalTaxiways 5-tuple with a TaxiwayRow class that holds a
direct reference to every control the row owns, including the second-line
'Hold short of runway:' label. RemoveTaxiwaysFrom now removes a row via
row.Controls in one pass (no Y-coordinate label lookup), and
SetRowRunwayHoldShortVisible toggles the owned label directly (no text match).
Behavior-preserving.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Verification

**Files:** none (verification only)

- [ ] **Step 1: Clean solution build.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`, 0 errors.

- [ ] **Step 2: Confirm no stale tuple usage remains.**

Search `MSFSBlindAssist/Forms/TaxiAssistForm.cs` for `_additionalTaxiways` and confirm every access uses `TaxiwayRow` members (`.Combo`, `.HoldShort`, `.HoldShortRunway`, `.HoldShortRunwayLabel`, `.Label`, `.RemoveBtn`, `.Controls`) or `Count`/`[^1]`/`Add`/`RemoveAt` — no lowercase tuple-member names, no `foreach (var (...) in _additionalTaxiways)`, and no remaining `l.Location.Y == ... + 45` companion-label scan.

- [ ] **Step 3: Confirm the build landed in the run path.**

Run (PowerShell): `(Get-Item 'MSFSBlindAssist\bin\x64\Debug\net9.0-windows\MSFSBlindAssist.exe').LastWriteTime`
Expected: a current timestamp.

- [ ] **Step 4: Record the manual/in-sim test plan in the PR** (repo owner runs it; pure refactor, so this confirms no behavioral drift):
  1. Open Taxi Guidance, add several taxiway rows. Remove the last row (its Remove button) → its "Hold short of runway:" label and combo disappear with the rest of the row; no orphaned label remains; the panel resizes.
  2. Change an *earlier* taxiway's selection (which removes all rows after it via `RemoveTaxiwaysAfter`) → all trailing rows and their second-line labels are removed cleanly.
  3. Switch Destination Type to Progressive Taxi → per-row "Hold short of runway" labels+combos hide on every row; switch back to Runway → they reappear at "(none)".
  4. Build a route with per-row hold-shorts / the "Hold short" checkbox on dynamic rows and confirm routing/callouts are unchanged from before.

- [ ] **Step 5: Hand off to `superpowers:finishing-a-development-branch`.**

---

## Self-Review Notes

- **Spec coverage:** `TaxiwayRow` class with `Controls` (Step 1); store label in `AddTaxiwayRow` (Step 2); `RemoveTaxiwaysFrom` single-pass removal, Y-lookup deleted (Step 3); all destructuring consumers → named fields (Steps 4-8); `SetRowRunwayHoldShortVisible` uses owned label, text-match loop deleted (Step 9); first row untouched (no first-row edits in any step). All spec sections covered.
- **No placeholders:** every step shows full before/after text and exact commands.
- **Consumer completeness:** the `_additionalTaxiways` sites from the grep are: declaration (Step 1), `[^1].combo` ×2 (Steps 4, 5), `.Add` (Step 2), `RemoveTaxiwaysFrom` destructure + Y-lookup (Step 3), `foreach` ×4 (Steps 6, 7, 8, 9). `Count` / `[^1]` / `index` usages (OnAddTaxiwayClicked guard, AddTaxiwayRow index, RefreshTerminatorRow, UpdateLayout) need no change — `List<TaxiwayRow>` has the same shape. Step 10/Step 2-verify catch any straggler.
- **Behavior parity:** removal still removes + disposes all six controls (now via `Controls`, which includes the label that was previously Y-matched); `SetRowRunwayHoldShortVisible` still hides label+combo and resets to "(none)"; no `Location`/`TabIndex`/creation changes.

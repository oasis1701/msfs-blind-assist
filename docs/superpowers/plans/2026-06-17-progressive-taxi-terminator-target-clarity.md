# Progressive Taxi Terminator Target Clarity — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Progressive Taxi terminator block its own runway-target combo with a type-matched label, so the terminator type and its target are configured together (instead of reusing the last taxiway row's "Hold short of runway" combo).

**Architecture:** Single-file change in `MSFSBlindAssist/Forms/TaxiAssistForm.cs`. Add `lblTerminatorRunway` + `cmbTerminatorRunway` to the terminator block (inside `pnlTaxiways`), shown for the two runway terminator types with a label matching intent. The runway target is read from this combo at Calculate time, letting us delete `LastRowHoldShortRunway()`, the `progRwyHoldShorts.Remove(last)` special case, and the "last UI row diverges" guard — all of which existed only to support the old combo reuse.

**Tech Stack:** .NET 9, Windows Forms, screen-reader-first (NVDA/JAWS). No unit-test project — verification is `dotnet build` + an in-sim test plan run by the repo owner.

**Spec:** `docs/superpowers/specs/2026-06-17-progressive-taxi-terminator-target-clarity-design.md`

---

## File Structure

- **Modify:** `MSFSBlindAssist/Forms/TaxiAssistForm.cs` — the only file. Changes: field declarations, control creation, control registration + TabIndex, mnemonic-plan comment, airport-load repopulation, `RefreshTerminatorRow`, `PopulateTerminatorTaxiwayList`, `UpdateLayout`, the `OnCalculateClicked` progressive branch, and deletion of `LastRowHoldShortRunway()`.

## Build / verify command (used after every code task)

```
dotnet build MSFSBlindAssist.sln -c Debug
```
Expected: `Build succeeded`. The running x64 exe is file-locked while MSFSBA runs (MSB3021) — if the build fails with a file lock, the app must be closed first; that is an environment issue, not a code error.

> **NOTE on the build trap (from CLAUDE.md):** ALWAYS build the SOLUTION (`MSFSBlindAssist.sln`), never the bare `.csproj` — a bare csproj build writes to `bin\Debug\` (AnyCPU), a different folder from the `bin\x64\Debug\` the app runs from.

---

### Task 1: Add the runway-target control (declaration + creation + registration)

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` (field block ~74-86; mnemonic comment ~247-252; creation ~459-497; registration ~600-616; TabIndex unaffected)

- [ ] **Step 1: Update the terminator-controls field block.** Replace the comment + field declarations at the top of the class (currently lines ~67-77, ending just before `private static readonly string[] TerminatorTypeItems`) so it documents the self-contained block and declares the new label + combo.

Find this block:

```csharp
    // Progressive Taxi terminator controls. These two combos are form-level (not
    // per-row): RefreshTerminatorRow() repositions and shows them on whichever
    // taxiway row is CURRENTLY last, and only when the destination type is
    // Progressive Taxi (index 2). The chosen terminator therefore "travels with
    // the last row" as the spec requires. The runway TARGET reuses that last
    // row's existing "Hold short of runway" combo; cmbTerminatorTaxiway is the
    // separate target picker for the hold-short-of-taxiway case.
    private Label lblTerminatorType = null!;
    private ComboBox cmbTerminatorType = null!;
    private Label lblTerminatorTaxiway = null!;
    private ComboBox cmbTerminatorTaxiway = null!;
```

Replace with:

```csharp
    // Progressive Taxi terminator controls. These are form-level (not per-row):
    // RefreshTerminatorRow() repositions and shows them on whichever taxiway row
    // is CURRENTLY last, and only when the destination type is Progressive Taxi
    // (index 2). The chosen terminator therefore "travels with the last row" as
    // the spec requires. The terminator block is SELF-CONTAINED: it carries its
    // own target pickers — cmbTerminatorRunway for the runway terminators (Hold
    // short of runway / After crossing runway) and cmbTerminatorTaxiway for the
    // taxiway terminator (and the optional cross-at taxiway). The per-row "Hold
    // short of runway" combos are NOT reused for the terminator target; they keep
    // their single meaning of an intermediate hold-short on any row.
    private Label lblTerminatorType = null!;
    private ComboBox cmbTerminatorType = null!;
    private Label lblTerminatorRunway = null!;
    private ComboBox cmbTerminatorRunway = null!;
    private Label lblTerminatorTaxiway = null!;
    private ComboBox cmbTerminatorTaxiway = null!;
    // Computed height of the terminator block (1-3 visible lines depending on
    // type), read by UpdateLayout in place of the fixed TERMINATOR_BLOCK_HEIGHT_PX.
    private int _terminatorBlockHeightPx = TERMINATOR_BLOCK_HEIGHT_PX;
```

- [ ] **Step 2: Update the mnemonic-plan comment** so the new Alt+U is documented. Find (lines ~249-252):

```csharp
        //   Alt+N  Progressive-taxi termi&nator type combo (last row only, index 2)
        //   Alt+W  Progressive-taxi terminator taxi&way target combo (last row only,
        //          type "Hold short of taxiway"); the SAME combo becomes the optional
        //          "Cross at ta&xiway" picker (Alt+X) for type "After crossing runway"
```

Replace with:

```csharp
        //   Alt+N  Progressive-taxi termi&nator type combo (last row only, index 2)
        //   Alt+U  Progressive-taxi terminator R&unway target combo (last row only,
        //          types "Hold short of runway" / "After crossing runway")
        //   Alt+W  Progressive-taxi terminator taxi&way target combo (last row only,
        //          type "Hold short of taxiway"); the SAME combo becomes the optional
        //          "Cross at ta&xiway" picker (Alt+X) for type "After crossing runway"
```

- [ ] **Step 3: Create the runway combo in the control-creation section.** In `InitializeFormControls`, find the `cmbTerminatorType` setup and the start of the `lblTerminatorTaxiway` setup (lines ~466-483):

```csharp
        cmbTerminatorType = new ComboBox
        {
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AccessibleName = "Progressive taxi terminator",
            AccessibleDescription = "Choose how this progressive taxi leg ends: hold short of a runway, hold short of a taxiway, after crossing a runway, or at the end of the last taxiway. Pick the target runway in the Hold short of runway combo, or the target taxiway in the terminator taxiway combo."
        };
        cmbTerminatorType.Items.AddRange(TerminatorTypeItems);
        cmbTerminatorType.SelectedIndex = 0;
        cmbTerminatorType.SelectedIndexChanged += (s, ev) => RefreshTerminatorRow();
        lblTerminatorTaxiway = new Label
```

Replace with (updated AccessibleDescription + the new runway label/combo inserted before `lblTerminatorTaxiway`):

```csharp
        cmbTerminatorType = new ComboBox
        {
            Width = 200,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AccessibleName = "Progressive taxi terminator",
            AccessibleDescription = "Choose how this progressive taxi leg ends: hold short of a runway, hold short of a taxiway, after crossing a runway, or at the end of the last taxiway. Pick the target runway or taxiway in the combo that appears just below."
        };
        cmbTerminatorType.Items.AddRange(TerminatorTypeItems);
        cmbTerminatorType.SelectedIndex = 0;
        cmbTerminatorType.SelectedIndexChanged += (s, ev) => RefreshTerminatorRow();
        // Runway TARGET for the two runway terminators (Hold short of runway /
        // After crossing runway). The label text + accessibility strings are set
        // per-type in RefreshTerminatorRow ("Runway to hold short of:" vs "Runway
        // to cross:"). Populated from _airportRunwayIds (same source + sentinel as
        // the per-row hold-short combos) via RebuildHoldShortRunwayCombo.
        lblTerminatorRunway = new Label
        {
            Text = "R&unway to hold short of:",
            AutoSize = true,
            Visible = false,
            AccessibleName = "Progressive taxi terminator runway label"
        };
        cmbTerminatorRunway = new ComboBox
        {
            Width = 190,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false,
            AccessibleName = "Progressive taxi terminator runway",
            AccessibleDescription = "Pick the runway this progressive leg holds short of."
        };
        cmbTerminatorRunway.Items.Add(NO_RUNWAY_HOLDSHORT);
        cmbTerminatorRunway.SelectedIndex = 0;
        // When the target runway changes, refresh the optional cross-at taxiway
        // list (it is filtered to taxiways that cross the chosen runway for the
        // After-crossing terminator).
        cmbTerminatorRunway.SelectedIndexChanged += (s, ev) =>
        {
            if (cmbTerminatorType.SelectedIndex == 2)
                PopulateTerminatorTaxiwayList();
        };
        lblTerminatorTaxiway = new Label
```

- [ ] **Step 4: Register the runway controls in the panel + assign TabIndex.** Find the panel registration (lines ~600-616):

```csharp
        pnlTaxiways.Controls.Add(lblTerminatorType);
        pnlTaxiways.Controls.Add(cmbTerminatorType);
        pnlTaxiways.Controls.Add(lblTerminatorTaxiway);
        pnlTaxiways.Controls.Add(cmbTerminatorTaxiway);
```

Replace with:

```csharp
        pnlTaxiways.Controls.Add(lblTerminatorType);
        pnlTaxiways.Controls.Add(cmbTerminatorType);
        pnlTaxiways.Controls.Add(lblTerminatorRunway);
        pnlTaxiways.Controls.Add(cmbTerminatorRunway);
        pnlTaxiways.Controls.Add(lblTerminatorTaxiway);
        pnlTaxiways.Controls.Add(cmbTerminatorTaxiway);
```

Then find the TabIndex assignment (lines ~613-616):

```csharp
        lblTerminatorType.TabIndex = 8998;
        cmbTerminatorType.TabIndex = 8999;
        lblTerminatorTaxiway.TabIndex = 9000;
        cmbTerminatorTaxiway.TabIndex = 9001;
```

Replace with (runway combo tabs between type and taxiway, matching its visual position):

```csharp
        lblTerminatorType.TabIndex = 8998;
        cmbTerminatorType.TabIndex = 8999;
        lblTerminatorRunway.TabIndex = 9000;
        cmbTerminatorRunway.TabIndex = 9001;
        lblTerminatorTaxiway.TabIndex = 9002;
        cmbTerminatorTaxiway.TabIndex = 9003;
```

- [ ] **Step 5: Build.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`. (The new combo is created and hidden; no behavior wired yet.)

- [ ] **Step 6: Commit.**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "feat(taxi): add dedicated runway-target combo to progressive terminator block

Creates lblTerminatorRunway/cmbTerminatorRunway (Alt+U) inside the
terminator block, hidden by default. Not yet wired into layout or
calculate; behavior unchanged this commit.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: Lay out + label the runway combo per type; dynamic block height

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` — `RefreshTerminatorRow` (~1557-1604), `UpdateLayout` (~1658-1659)

- [ ] **Step 1: Rewrite `RefreshTerminatorRow`** to position/show/label the runway combo, pack the taxiway combo below it, and compute the dynamic block height. Replace the whole method body (lines ~1557-1604):

```csharp
    private void RefreshTerminatorRow()
    {
        bool progressive = cmbDestType.SelectedIndex == 2;
        if (!progressive)
        {
            lblTerminatorType.Visible = false;
            cmbTerminatorType.Visible = false;
            lblTerminatorRunway.Visible = false;
            cmbTerminatorRunway.Visible = false;
            lblTerminatorTaxiway.Visible = false;
            cmbTerminatorTaxiway.Visible = false;
            _terminatorBlockHeightPx = 0;
            UpdateLayout();
            return;
        }

        // The terminator block sits just below the current last taxiway row,
        // inside pnlTaxiways. When no additional rows exist, the "last row" is
        // the first-taxiway slot (outside the panel) and the block sits at the
        // top of the panel (blockY 0). Each visible line is LINE_PX tall.
        const int LINE_PX = 28;
        int blockY = _additionalTaxiways.Count * DYNAMIC_ROW_HEIGHT_PX;
        int Line(int n) => blockY + n * LINE_PX;

        // Line 0: terminator type (always shown in progressive mode).
        lblTerminatorType.Location = new System.Drawing.Point(0, Line(0) + 2);
        cmbTerminatorType.Location = new System.Drawing.Point(140, Line(0));
        lblTerminatorType.Visible = true;
        cmbTerminatorType.Visible = true;

        int tType = cmbTerminatorType.SelectedIndex;
        bool needRunwayTarget = tType == 0 || tType == 2;          // hold short / cross
        bool needTaxiwayTarget = tType == 1 || tType == 2;          // hold short taxiway / cross-at

        // Pack visible target combos on consecutive lines beneath the type combo.
        int nextLine = 1;

        // Runway target (line 1 when shown). Label + accessibility match the type.
        if (needRunwayTarget)
        {
            lblTerminatorRunway.Text = tType == 2
                ? "R&unway to cross:"
                : "R&unway to hold short of:";
            lblTerminatorRunway.AccessibleName = tType == 2
                ? "Runway to cross"
                : "Runway to hold short of";
            lblTerminatorRunway.AccessibleDescription = tType == 2
                ? "Pick the runway ATC cleared you to cross. Guidance ends just past this runway."
                : "Pick the runway this progressive leg holds short of. Guidance ends at the hold line.";
            cmbTerminatorRunway.AccessibleDescription = lblTerminatorRunway.AccessibleDescription;
            lblTerminatorRunway.Location = new System.Drawing.Point(0, Line(nextLine) + 2);
            cmbTerminatorRunway.Location = new System.Drawing.Point(180, Line(nextLine));
            nextLine++;
        }
        lblTerminatorRunway.Visible = needRunwayTarget;
        cmbTerminatorRunway.Visible = needRunwayTarget;

        // Taxiway target (next line). For type 1 it is the REQUIRED hold-short
        // taxiway; for type 2 it is the OPTIONAL cross-at taxiway.
        if (needTaxiwayTarget)
        {
            lblTerminatorTaxiway.Text = tType == 2
                ? "Cross at ta&xiway (optional):"
                : "Hold short of taxi&way:";
            lblTerminatorTaxiway.AccessibleName = tType == 2
                ? "Cross at taxiway, optional"
                : "Progressive taxi terminator taxiway label";
            cmbTerminatorTaxiway.AccessibleDescription = tType == 2
                ? "Optional: pick the taxiway at which to cross the runway, when ATC names a crossing point. Lists only taxiways that cross the runway picked above. Leave at \"(none)\" to cross at the nearest point automatically."
                : "Pick the taxiway to hold short of where it meets the last taxiway in your route.";
            lblTerminatorTaxiway.Location = new System.Drawing.Point(0, Line(nextLine) + 2);
            cmbTerminatorTaxiway.Location = new System.Drawing.Point(180, Line(nextLine));
            nextLine++;
        }
        lblTerminatorTaxiway.Visible = needTaxiwayTarget;
        cmbTerminatorTaxiway.Visible = needTaxiwayTarget;
        if (needTaxiwayTarget)
            PopulateTerminatorTaxiwayList();

        // Block height = number of visible lines (type + however many targets).
        _terminatorBlockHeightPx = nextLine * LINE_PX;

        UpdateLayout();
    }
```

- [ ] **Step 2: Make `UpdateLayout` use the dynamic block height.** Find (lines ~1656-1659):

```csharp
        // shown below the last row (cmbTerminatorType.Visible is set by
        // RefreshTerminatorRow before this runs).
        if (cmbTerminatorType.Visible)
            panelHeight += TERMINATOR_BLOCK_HEIGHT_PX;
```

Replace with:

```csharp
        // shown below the last row (cmbTerminatorType.Visible + the per-type
        // _terminatorBlockHeightPx are set by RefreshTerminatorRow before this runs).
        if (cmbTerminatorType.Visible)
            panelHeight += _terminatorBlockHeightPx;
```

- [ ] **Step 3: Build.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`.

- [ ] **Step 4: Commit.**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "feat(taxi): lay out + label terminator runway combo per type

RefreshTerminatorRow now packs the runway target (Hold short of runway /
After crossing runway) under the type combo with an intent-matched label,
moves the taxiway combo below it, and computes a per-type block height
(1-3 lines) consumed by UpdateLayout.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: Populate the runway combo; read it for the cross-at list

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` — airport-load repopulation (~697 and ~748), `PopulateTerminatorTaxiwayList` (~1624), the terminator-taxiway DropDown comment (~492-497)

- [ ] **Step 1: Repopulate `cmbTerminatorRunway` on airport load.** There are two `RebuildHoldShortRunwayCombo(cmbFirstHoldShortRunway);` calls in the airport-load path (the reset at ~697 and the populate-from-DB at ~748). Update BOTH to also rebuild the terminator runway combo.

Find (the reset, ~696-697):

```csharp
        _airportRunwayIds = new List<string>();
        RebuildHoldShortRunwayCombo(cmbFirstHoldShortRunway);
```

Replace with:

```csharp
        _airportRunwayIds = new List<string>();
        RebuildHoldShortRunwayCombo(cmbFirstHoldShortRunway);
        RebuildHoldShortRunwayCombo(cmbTerminatorRunway);
```

Find (the populate-from-DB, ~748):

```csharp
        RebuildHoldShortRunwayCombo(cmbFirstHoldShortRunway);

        // Populate first taxiway combobox sorted by distance, closest first
```

Replace with:

```csharp
        RebuildHoldShortRunwayCombo(cmbFirstHoldShortRunway);
        RebuildHoldShortRunwayCombo(cmbTerminatorRunway);

        // Populate first taxiway combobox sorted by distance, closest first
```

- [ ] **Step 2: Read the new combo in `PopulateTerminatorTaxiwayList`.** Find (lines ~1623-1626):

```csharp
            // After crossing: optional cross-at picker. "(none)" = nearest crossing.
            cmbTerminatorTaxiway.Items.Add(NO_RUNWAY_HOLDSHORT);
            string rwy = LastRowHoldShortRunway();
            if (!string.IsNullOrEmpty(rwy) &&
                _crossRunwayMap.TryGetValue($"Runway {rwy}", out var crossRwy))
```

Replace with:

```csharp
            // After crossing: optional cross-at picker. "(none)" = nearest crossing.
            cmbTerminatorTaxiway.Items.Add(NO_RUNWAY_HOLDSHORT);
            string rwy = TerminatorRunwayTarget();
            if (!string.IsNullOrEmpty(rwy) &&
                _crossRunwayMap.TryGetValue($"Runway {rwy}", out var crossRwy))
```

- [ ] **Step 3: Update the stale comment on the terminator-taxiway DropDown handler.** Find (lines ~492-497):

```csharp
        // For the "After crossing runway" terminator this combo doubles as the
        // optional "Cross at taxiway" picker, whose list depends on the runway
        // chosen in the last row's "Hold short of runway" combo. Refresh it just
        // before the dropdown opens so the cross-at options reflect the current
        // runway pick regardless of the order the user filled the controls in.
        cmbTerminatorTaxiway.DropDown += (s, ev) => PopulateTerminatorTaxiwayList();
```

Replace with:

```csharp
        // For the "After crossing runway" terminator this combo doubles as the
        // optional "Cross at taxiway" picker, whose list depends on the runway
        // chosen in cmbTerminatorRunway. Refresh it just before the dropdown opens
        // so the cross-at options reflect the current runway pick regardless of the
        // order the user filled the controls in.
        cmbTerminatorTaxiway.DropDown += (s, ev) => PopulateTerminatorTaxiwayList();
```

- [ ] **Step 4: Add the `TerminatorRunwayTarget()` helper.** This replaces `LastRowHoldShortRunway()` as the source of the terminator's runway. Add it immediately BEFORE the existing `LastRowHoldShortRunway()` method (which Task 4 deletes). Find the start of `LastRowHoldShortRunway` (line ~2032):

```csharp
    private string LastRowHoldShortRunway()
    {
```

Insert this method just above it:

```csharp
    /// <summary>
    /// The runway designator chosen as the Progressive Taxi terminator target
    /// (Hold short of runway / After crossing runway), read from the terminator
    /// block's own runway combo. Returns "" when unset ("(none)").
    /// </summary>
    private string TerminatorRunwayTarget()
    {
        string? sel = cmbTerminatorRunway.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(sel) || sel == NO_RUNWAY_HOLDSHORT) return "";
        return sel;
    }

    private string LastRowHoldShortRunway()
    {
```

- [ ] **Step 5: Build.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`. (`LastRowHoldShortRunway` is still referenced by `OnCalculateClicked` — it is deleted in Task 4. This compiles because both methods now exist.)

- [ ] **Step 6: Commit.**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "feat(taxi): populate terminator runway combo; drive cross-at list from it

Rebuild cmbTerminatorRunway on airport load; add TerminatorRunwayTarget()
and use it (not the last row's hold-short combo) to filter the After-crossing
cross-at taxiway list.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Calculate-time — read the new combo; remove the obsolete reuse machinery

**Files:**
- Modify: `MSFSBlindAssist/Forms/TaxiAssistForm.cs` — `OnCalculateClicked` progressive branch (~1718-1814), delete `LastRowHoldShortRunway()` (~2032-2040)

- [ ] **Step 1: Remove the "last UI row diverges" guard.** Its sole rationale was that `LastRowHoldShortRunway()` read the last row's combo; with a dedicated terminator combo, an empty trailing row is harmless (it is filtered out by `GetSelectedTaxiwayNames`, and the runway/taxiway targets come from the block's own combos). Find (lines ~1718-1732):

```csharp
            // Guard: if the last UI row has no taxiway selected, the "Hold short
            // of runway" combo on that row belongs to an incomplete entry — the
            // effective last taxiway (progSeq[^1]) and the UI last row diverge,
            // which would cause LastRowHoldShortRunway() to read the wrong combo.
            // Reject with a clear message so the user fixes the entry before
            // proceeding.
            if (_additionalTaxiways.Count > 0)
            {
                string? lastRowSel = _additionalTaxiways[^1].combo.SelectedItem?.ToString();
                if (string.IsNullOrEmpty(lastRowSel) || lastRowSel.StartsWith("(None"))
                {
                    _announcer.Announce("The last taxiway row has no taxiway selected. Select a taxiway or remove that row.");
                    return;
                }
            }

```

Delete that entire block (including the trailing blank line) so the code flows from the `lastTaxiway` empty-check straight into the `// Component + start node...` comment.

- [ ] **Step 2: Read the terminator runway from the new combo + drop the obsolete map edit.** Find (lines ~1745-1763):

```csharp
            // The runway TARGET is the last row's "Hold short of runway" combo;
            // the taxiway TARGET is cmbTerminatorTaxiway.
            string runwayTarget = LastRowHoldShortRunway();   // bare designator, "" if none
            string taxiwayTarget = cmbTerminatorTaxiway.SelectedItem?.ToString() ?? "";

            int terminatorTypeIndex = cmbTerminatorType.SelectedIndex;
            int destNode = -1;
            ProgressiveTerminator term;
            var progRwyHoldShorts = GetUserRunwayHoldShorts();

            // For the runway-type terminators (Hold short of runway / After
            // crossing runway), the LAST row's "Hold short of runway" combo IS
            // the terminator target — not an intermediate hold-short. Drop it
            // from the per-row hold-short map so the terminator owns it (avoids a
            // redundant tag and a spurious "not on route" mismatch warning). For
            // the taxiway/end terminators the last-row runway combo is a genuine
            // intermediate hold-short and is kept.
            if (terminatorTypeIndex == 0 || terminatorTypeIndex == 2)
                progRwyHoldShorts.Remove(progSeq.Count - 1);

            switch (terminatorTypeIndex)
```

Replace with:

```csharp
            // The runway TARGET is the terminator block's own runway combo; the
            // taxiway TARGET is cmbTerminatorTaxiway. Per-row "Hold short of runway"
            // combos are NOT consulted here — they remain plain intermediate
            // hold-shorts (carried in progRwyHoldShorts as on every other row).
            string runwayTarget = TerminatorRunwayTarget();   // bare designator, "" if none
            string taxiwayTarget = cmbTerminatorTaxiway.SelectedItem?.ToString() ?? "";

            int terminatorTypeIndex = cmbTerminatorType.SelectedIndex;
            int destNode = -1;
            ProgressiveTerminator term;
            var progRwyHoldShorts = GetUserRunwayHoldShorts();

            switch (terminatorTypeIndex)
```

- [ ] **Step 3: Update the two runway-target validation messages.** Find (case 0, lines ~1769-1772):

```csharp
                    if (string.IsNullOrEmpty(runwayTarget))
                    {
                        _announcer.Announce("Pick the runway to hold short of in the Hold short of runway combo on the last taxiway row.");
                        return;
                    }
```

Replace with:

```csharp
                    if (string.IsNullOrEmpty(runwayTarget))
                    {
                        _announcer.Announce("Pick the runway to hold short of in the terminator runway combo.");
                        return;
                    }
```

Find (case 2, lines ~1798-1802):

```csharp
                    if (string.IsNullOrEmpty(runwayTarget))
                    {
                        _announcer.Announce("Pick the runway to cross in the Hold short of runway combo on the last taxiway row.");
                        return;
                    }
```

Replace with:

```csharp
                    if (string.IsNullOrEmpty(runwayTarget))
                    {
                        _announcer.Announce("Pick the runway to cross in the terminator runway combo.");
                        return;
                    }
```

- [ ] **Step 4: Delete the now-unused `LastRowHoldShortRunway()` method.** Find and remove the whole method (lines ~2032-2040):

```csharp
    private string LastRowHoldShortRunway()
    {
        ComboBox combo = _additionalTaxiways.Count > 0
            ? _additionalTaxiways[^1].holdShortRunway
            : cmbFirstHoldShortRunway;
        string? sel = combo.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(sel) || sel == NO_RUNWAY_HOLDSHORT) return "";
        return sel;
    }

```

(Leave the `TerminatorRunwayTarget()` method added in Task 3 in place.)

- [ ] **Step 5: Build and confirm no remaining references to `LastRowHoldShortRunway`.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, with no `CS0103`/`CS0117` errors. If the build reports `LastRowHoldShortRunway` is undefined, a caller was missed — search the file for `LastRowHoldShortRunway` and ensure the only remaining occurrence is none (Task 3 moved the cross-at caller to `TerminatorRunwayTarget`, Task 4 moved the calculate caller).

- [ ] **Step 6: Commit.**

```bash
git add MSFSBlindAssist/Forms/TaxiAssistForm.cs
git commit -m "feat(taxi): read terminator runway from its own combo; drop reuse machinery

OnCalculateClicked reads the terminator runway from cmbTerminatorRunway.
Removes the last-row hold-short reuse: the progRwyHoldShorts.Remove(last)
special case, the last-row-diverges guard, and LastRowHoldShortRunway().
Per-row hold-short combos now have one consistent meaning.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Full verification + in-sim test plan

**Files:** none (verification only)

- [ ] **Step 1: Clean solution build.**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 2: Confirm the build landed in the run path.** Verify `MSFSBlindAssist\bin\x64\Debug\net9.0-windows\MSFSBlindAssist.exe` has a current (just-now) LastWriteTime — per CLAUDE.md, a stale timestamp means the build went to the wrong folder.

Run (PowerShell): `(Get-Item 'MSFSBlindAssist\bin\x64\Debug\net9.0-windows\MSFSBlindAssist.exe').LastWriteTime`
Expected: a timestamp from the current build.

- [ ] **Step 3: Record the in-sim test plan in the PR description** (the repo owner runs it against a live sim — PHNL has both runway crossings and taxiway intersections):
  1. Open Taxi Guidance, set **Destination Type = Progressive Taxi**.
  2. Add a taxiway sequence. On the last row, the terminator block shows **Terminator** + a target combo directly beneath it.
  3. **Hold short of runway:** the second combo reads **"Runway to hold short of:"** (Alt+U); pick a runway; Calculate. Guidance ends at the hold line with the hold announcement — no lineup, no Takeoff-Assist.
  4. **After crossing runway:** the second combo reads **"Runway to cross:"**; a third **"Cross at taxiway (optional)"** combo appears, filtered to taxiways crossing the chosen runway. Calculate; guidance ends just past the runway.
  5. **Hold short of taxiway:** only the **"Hold short of taxiway:"** combo appears (no runway combo); Calculate ends at the intersection.
  6. **End of last taxiway:** no target combo; Calculate routes to the taxiway end.
  7. Confirm the per-row "Hold short of runway" combo on the last row now acts as a normal intermediate hold-short (set one plus a runway terminator and verify both the intermediate hold and the terminator fire).
  8. Switch Destination Type away from Progressive Taxi and back: the terminator block hides/shows cleanly and the form re-lays-out without overlap.
  9. Tab order through the block: Type → Runway → Taxiway, each label's Alt-mnemonic (N / U / W or X) jumps to its combo.

- [ ] **Step 4: Hand off to `superpowers:finishing-a-development-branch`** to choose how to integrate the work (the branch already exists; this is a UI change with an in-sim test plan, no automated tests to run).

---

## Self-Review Notes

- **Spec coverage:** new runway combo (Task 1), type-matched labels + dynamic layout (Task 2), population + cross-at sourcing (Task 3), calculate-time read + deletion of `LastRowHoldShortRunway`/`Remove(last)`/divergence guard (Task 4), build + in-sim verification (Task 5). All spec sections are covered.
- **Type consistency:** new helper `TerminatorRunwayTarget()` is defined in Task 3 and consumed in Tasks 3 (cross-at list) and 4 (calculate). `_terminatorBlockHeightPx` field defined in Task 1, set in Task 2's `RefreshTerminatorRow`, read in Task 2's `UpdateLayout`. Control names `lblTerminatorRunway`/`cmbTerminatorRunway` consistent across Tasks 1-3.
- **No placeholders:** every code step shows full before/after text and exact build/commit commands.

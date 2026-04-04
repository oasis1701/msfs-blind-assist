# PMDG 777 CDU Accessibility Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all 8 CDU issues: broken button/scratchpad input, focus order, line numbers, ON/OFF indicators, remove LSK buttons from UI, remove Nav Rad, add missing hotkeys.

**Architecture:** All changes are in two files: `PMDG777CDUForm.cs` (main logic) and `PMDG777CDUForm.Designer.cs` (UI layout). The root cause of issues 7 and 8 (buttons/typing not working) is that `SendCDUKey()` sends parameter `null` → 0 ("not pressed") instead of `1` ("pressed"). The display needs enhanced text formatting using the CDU grid color data from `PMDG777DataManager.GetCDURows()` to indicate selected options.

**Tech Stack:** C# 13 / .NET 9, Windows Forms, PMDG SDK (CDA)

**Files to modify:**
- `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs` — event sending fix, display formatting, hotkeys, focus
- `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.Designer.cs` — remove LSK buttons, remove Nav Rad, rename scratchpad

**Critical context:**
- `SendCDUKey()` calls `_dataManager.SendEvent(eventName, eventId, null)` — the `null` becomes parameter 0 via `parameter ?? 0` in `SendEvent()`. PMDG CDU buttons are momentary and need parameter `1` to register.
- CDU display is 14 rows x 24 columns. Row 0 = title, rows 1-12 = 6 line pairs (header + data), row 13 = scratchpad.
- Line select keys: L1-L6 map to data rows 2,4,6,8,10,12. R1-R6 map to same rows.
- The CDU grid data includes per-cell `color` (green = selected) and `small` font flag. The text rows don't include this info.

---

## Task 1: Fix CDU button and scratchpad input (Issues 7 & 8)

**Files:**
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs`

This is the critical fix — without this, nothing else matters.

- [ ] **Step 1: Fix SendCDUKey to send parameter 1**

In `PMDG777CDUForm.cs`, find the `SendCDUKey` method. Change the last line from:

```csharp
_dataManager.SendEvent(eventName, (uint)eventId, null);
```

to:

```csharp
_dataManager.SendEvent(eventName, (uint)eventId, 1);
```

This sends parameter 1 ("pressed") instead of 0 ("not pressed") for all CDU button events, including line select keys and individual character keystrokes.

- [ ] **Step 2: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs
git commit -m "fix(pmdg777): CDU buttons now send parameter 1 (pressed) instead of 0 (no-op)"
```

---

## Task 2: Fix focus order and rename scratchpad (Issue 1)

**Files:**
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs`
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.Designer.cs`

- [ ] **Step 1: Change initial focus to CDU Display**

In `PMDG777CDUForm.cs`, find the `SetupEventHandlers` method. Change:

```csharp
this.Load += (s, e) =>
{
    scratchpadInput.Focus();
    _pollTimer.Start();
};
```

to:

```csharp
this.Load += (s, e) =>
{
    cduDisplay.Focus();
    _pollTimer.Start();
};
```

Also in `ShowForm()`, change `scratchpadInput.Focus();` to `cduDisplay.Focus();`

- [ ] **Step 2: Rename scratchpad accessible name**

In `PMDG777CDUForm.Designer.cs`, find the `scratchpadInput` creation and change:

```csharp
AccessibleName = "CDU Input",
```

to:

```csharp
AccessibleName = "Scratchpad",
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.Designer.cs
git commit -m "fix(pmdg777): CDU opens with focus on display, rename scratchpad accessible name"
```

---

## Task 3: Add line numbers to display (Issue 2)

**Files:**
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs`

The CDU has 14 rows. The layout is:
- Row 0: page title
- Row 1: line 1 header (small font)
- Row 2: line 1 data
- Row 3: line 2 header
- Row 4: line 2 data
- ... (pattern continues)
- Row 11: line 6 header
- Row 12: line 6 data
- Row 13: scratchpad

- [ ] **Step 1: Add line number prefixes in UpdateDisplay**

In the `UpdateDisplay` method, find the loop that builds display lines:

```csharp
var lines = new List<string>(rows.Length);
for (int i = 0; i < rows.Length; i++)
    lines.Add(rows[i]);
```

Replace with:

```csharp
var lines = new List<string>(rows.Length);
for (int i = 0; i < rows.Length; i++)
{
    if (i == 0)
        lines.Add(rows[i]); // Title row — no prefix
    else if (i == 13)
        lines.Add($"SP: {rows[i]}"); // Scratchpad
    else if (i % 2 == 1)
        lines.Add($"{(i + 1) / 2}H: {rows[i]}"); // Header rows (1H, 2H, 3H...)
    else
        lines.Add($"{i / 2}: {rows[i]}"); // Data rows (1, 2, 3...)
}
```

This produces: title, `1H: ...`, `1: ...`, `2H: ...`, `2: ...`, ..., `6H: ...`, `6: ...`, `SP: ...`

- [ ] **Step 2: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs
git commit -m "feat(pmdg777): add line numbers to CDU display for accessibility"
```

---

## Task 4: Add ON/OFF selection indicators using color data (Issue 3)

**Files:**
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs`
- Modify: `MSFSBlindAssist/SimConnect/PMDG777DataManager.cs` (if needed)

The CDU grid data includes per-cell color. On the MENU page, `OFF` in green means selected, `ON` in white/small means unselected. We need to pass the grid data through to the display formatter.

- [ ] **Step 1: Update PMDG777DataManager to expose grid data**

In `PMDG777DataManager.cs`, find the `GetCDURows()` method. Add a companion method that returns the raw grid data alongside text:

```csharp
public (string[] rows, byte[,] colors)? GetCDURowsWithColors(int cdu)
{
    if (cdu < 0 || cdu > 2) return null;
    var screen = _lastCDUScreen[cdu];
    if (screen == null || !screen.Value.Powered) return null;

    var rows = new string[14];
    var colors = new byte[14, 24]; // color per cell

    for (int row = 0; row < 14; row++)
    {
        var sb = new System.Text.StringBuilder(24);
        for (int col = 0; col < 24; col++)
        {
            var cell = screen.Value.Cells[col * 14 + row];
            byte sym = cell.Symbol;
            colors[row, col] = cell.Color;

            if (sym == 0xA1) sb.Append('<');
            else if (sym == 0xA2) sb.Append('>');
            else if (sym >= 0x20 && sym <= 0x7E) sb.Append((char)sym);
            else sb.Append(' ');
        }
        rows[row] = sb.ToString();
    }

    return (rows, colors);
}
```

- [ ] **Step 2: Update CDU form to use color data for selection indicators**

In `PMDG777CDUForm.cs`, update the polling to use the new method:

Change `PollTimer_Tick`:
```csharp
private void PollTimer_Tick(object? sender, EventArgs e)
{
    _dataManager.RequestCDUScreen(_selectedCDU);
    var result = _dataManager.GetCDURowsWithColors(_selectedCDU);
    if (result != null)
        UpdateDisplay(result.Value.rows, result.Value.colors);
    else
        UpdateDisplay(null, null);
}
```

Update `UpdateDisplay` signature and add color-based selection marking. The key insight: when a row contains `OFF←→ON` or similar toggle pattern, the selected option is rendered in green (color=2). We mark it with `[X]` prefix.

Change `UpdateDisplay` to accept colors:
```csharp
private void UpdateDisplay(string[]? rows, byte[,]? colors)
```

In the line-building loop, for data rows (even-numbered rows 2,4,6,8,10,12), scan for the selected option by checking if the green color text differs from the non-green text. Apply `[X]` to the green (selected) side:

```csharp
var lines = new List<string>(rows.Length);
for (int i = 0; i < rows.Length; i++)
{
    string row = rows[i];

    // Mark selected options: find green-colored text segments
    if (colors != null && i >= 2 && i <= 12 && i % 2 == 0)
    {
        row = MarkSelectedOption(row, colors, i);
    }

    if (i == 0)
        lines.Add(row); // Title
    else if (i == 13)
        lines.Add($"SP: {row}"); // Scratchpad
    else if (i % 2 == 1)
        lines.Add($"{(i + 1) / 2}H: {row}"); // Header
    else
        lines.Add($"{i / 2}: {row}"); // Data
}
```

Add the helper method:
```csharp
private static string MarkSelectedOption(string row, byte[,] colors, int rowIndex)
{
    // Check if this row has a toggle pattern (both left and right content with arrows between)
    // Green (color=2) indicates the selected option
    // We prefix green segments with [X] and non-green with [ ]

    bool hasGreen = false;
    bool hasMixedColors = false;
    byte? firstNonSpaceColor = null;

    for (int col = 0; col < 24 && col < row.Length; col++)
    {
        if (row[col] != ' ' && row[col] != '<' && row[col] != '>' &&
            row[col] != '\u2190' && row[col] != '\u2192') // Skip arrows and brackets
        {
            byte c = colors[rowIndex, col];
            if (firstNonSpaceColor == null) firstNonSpaceColor = c;
            if (c == 2) hasGreen = true; // green
            if (firstNonSpaceColor != null && c != firstNonSpaceColor.Value && c != 0)
                hasMixedColors = true;
        }
    }

    // Only apply markers if there are mixed colors (indicating a toggle)
    if (!hasGreen || !hasMixedColors) return row;

    // Build marked row: prefix green text with [X], others with [ ]
    var sb = new System.Text.StringBuilder();
    int col2 = 0;
    while (col2 < row.Length)
    {
        if (row[col2] == ' ' || row[col2] == '<' || row[col2] == '>' ||
            row[col2] == '\u2190' || row[col2] == '\u2192')
        {
            sb.Append(row[col2]);
            col2++;
            continue;
        }

        // Start of a text segment — determine its color
        byte segColor = colors[rowIndex, col2];
        string marker = segColor == 2 ? "[X]" : "[ ]";
        sb.Append(marker);

        // Copy text until next space/arrow/bracket
        while (col2 < row.Length && row[col2] != ' ' && row[col2] != '<' && row[col2] != '>' &&
               row[col2] != '\u2190' && row[col2] != '\u2192')
        {
            sb.Append(row[col2]);
            col2++;
        }
    }

    return sb.ToString();
}
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs MSFSBlindAssist/SimConnect/PMDG777DataManager.cs
git commit -m "feat(pmdg777): mark selected CDU options with [X] using color data"
```

---

## Task 5: Remove LSK buttons from UI, remove Nav Rad (Issues 4 & 5)

**Files:**
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.Designer.cs`
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs`

- [ ] **Step 1: Remove LSK button fields and creation from Designer**

In `PMDG777CDUForm.Designer.cs`:

1. Remove all 12 LSK button field declarations (`btnL1` through `btnL6`, `btnR1` through `btnR6`)
2. Remove the `btnNavRad` field declaration
3. Remove all LSK button creation lines (`btnL1 = CreateLineSelectButton(...)` through `btnR6 = CreateLineSelectButton(...)`)
4. Remove the `btnNavRad` creation line
5. Remove LSK buttons and `btnNavRad` from the `Controls.AddRange` array
6. Remove LSK buttons and `btnNavRad` from the TabIndex assignments
7. Remove the `CreateLineSelectButton` helper method (no longer needed)
8. Adjust the Y positioning: page buttons and special buttons move up since LSK buttons are removed. The page buttons should start where LSK buttons used to start (right after `scratchpadInput`).
9. Reduce form height accordingly (remove ~200px for 6 LSK rows)

- [ ] **Step 2: Remove LSK click handlers from CDU form**

In `PMDG777CDUForm.cs`, remove all 12 LSK button click handler registrations:
```csharp
btnL1.Click += (s, e) => OnLineSelect("L1", 1);
// ... through ...
btnR6.Click += (s, e) => OnLineSelect("R6", 6);
```

Also remove the `btnNavRad.Click` handler:
```csharp
btnNavRad.Click += (s, e) => SendCDUKey("NAV_RAD");
```

Keep the `OnLineSelect` method — it's still called from keyboard shortcuts.

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.Designer.cs
git commit -m "fix(pmdg777): remove LSK buttons and Nav Rad from CDU UI (keyboard-only)"
```

---

## Task 6: Add missing keyboard hotkeys (Issue 6)

**Files:**
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs`

Currently only these hotkeys exist: Ctrl+1-6 (LSK L), Alt+1-6 (LSK R), PageUp/Down, Ctrl+Enter (Exec), Alt+S (scratchpad), Alt+Home (display).

Add hotkeys for all page buttons. Proposed mapping:

| Hotkey | Action | CDU Key |
|--------|--------|---------|
| Alt+I | Init Ref | INIT_REF |
| Alt+R | Route | RTE |
| Alt+D | Dep/Arr | DEP_ARR |
| Alt+A | Altn | ALTN |
| Alt+V | VNAV | VNAV |
| Alt+F | Fix | FIX |
| Alt+G | Legs | LEGS |
| Alt+H | Hold | HOLD |
| Alt+P | Prog | PROG |
| Alt+E | Execute | EXEC |
| Alt+M | Menu | MENU |
| Alt+C | Clear | CLR |
| Alt+L | Delete | DEL |
| Alt+O | FMC Comm | FMCCOMM |
| Alt+S | Focus scratchpad | (existing) |
| Alt+Home | Focus display | (existing) |

- [ ] **Step 1: Add Alt+letter hotkeys to Form_KeyDown**

In the `Form_KeyDown` method, add after the existing Alt+Home handler:

```csharp
// Alt+letter: page button hotkeys
if (e.Alt && !e.Control && !e.Shift)
{
    string? key = e.KeyCode switch
    {
        Keys.I => "INIT_REF",
        Keys.R => "RTE",
        Keys.D => "DEP_ARR",
        Keys.A => "ALTN",
        Keys.V => "VNAV",
        Keys.F => "FIX",
        Keys.G => "LEGS",
        Keys.H => "HOLD",
        Keys.P => "PROG",
        Keys.E => "EXEC",
        Keys.M => "MENU",
        Keys.C => "CLR",
        Keys.L => "DEL",
        Keys.O => "FMCCOMM",
        _ => null
    };

    if (key != null)
    {
        SendCDUKey(key);
        e.Handled = true;
        e.SuppressKeyPress = true;
        return;
    }
}
```

- [ ] **Step 2: Remove the old Ctrl+Enter handler (now covered by Alt+E)**

Find and remove the Ctrl+Enter handler:
```csharp
// Ctrl+Enter → EXEC
if (e.Control && e.KeyCode == Keys.Return)
{
    SendCDUKey("EXEC");
    e.Handled = true;
    e.SuppressKeyPress = true;
    return;
}
```

Note: Keep it if you want both Ctrl+Enter and Alt+E to work. User specified Alt+E, but Ctrl+Enter is a reasonable alternative. Decision: keep both.

- [ ] **Step 3: Update button accessible descriptions to show hotkeys**

In `PMDG777CDUForm.Designer.cs`, update `CreatePageButton` or individual button accessible descriptions to mention the hotkey. For example, update each button's `AccessibleDescription` to include the shortcut:

In the `CreatePageButton` method, the `AccessibleDescription` is generic. Update individual buttons after creation:
```csharp
btnInitRef.AccessibleDescription = "CDU Init Ref page (Alt+I)";
btnRte.AccessibleDescription = "CDU Route page (Alt+R)";
btnDepArr.AccessibleDescription = "CDU Dep/Arr page (Alt+D)";
btnAltn.AccessibleDescription = "CDU Altn page (Alt+A)";
btnVnav.AccessibleDescription = "CDU VNAV page (Alt+V)";
btnFix.AccessibleDescription = "CDU Fix page (Alt+F)";
btnLegs.AccessibleDescription = "CDU Legs page (Alt+G)";
btnHold.AccessibleDescription = "CDU Hold page (Alt+H)";
btnProg.AccessibleDescription = "CDU Prog page (Alt+P)";
btnFmcComm.AccessibleDescription = "CDU FMC Comm (Alt+O)";
btnMenu.AccessibleDescription = "CDU Menu page (Alt+M)";
btnPrevPage.AccessibleDescription = "Previous page (PageUp)";
btnNextPage.AccessibleDescription = "Next page (PageDown)";
btnExec.AccessibleDescription = "Execute flight plan modification (Alt+E)";
btnClr.AccessibleDescription = "Clear scratchpad (Alt+C)";
btnDel.AccessibleDescription = "Delete selected field (Alt+L)";
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.Designer.cs
git commit -m "feat(pmdg777): add Alt+letter hotkeys for all CDU page buttons"
```

---

## Summary

| Task | Issue(s) | What |
|------|----------|------|
| 1 | 7, 8 | Fix `SendCDUKey` parameter (null→1) — enables all button presses and scratchpad typing |
| 2 | 1 | Focus CDU display on open, rename "CDU Input" to "Scratchpad" |
| 3 | 2 | Add line numbers (1-6, H for headers, SP for scratchpad) |
| 4 | 3 | Mark selected options with `[X]` using CDU grid color data |
| 5 | 4, 5 | Remove 12 LSK buttons + Nav Rad from UI |
| 6 | 6 | Add Alt+letter hotkeys for all CDU page buttons |

**Execution order matters:** Task 1 must be first (nothing works without it). Tasks 2-6 can be done in any order after that.

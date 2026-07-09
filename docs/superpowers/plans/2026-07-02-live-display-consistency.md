# Live-Display Consistency Pass Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every live-refreshing text display in MSFSBA uses the same mechanism — a navigable ListBox reconciled in place via `Forms.DisplayList.UpdateInPlace` — so the NVDA cursor is never yanked and only changed rows re-announce.

**Architecture:** A new `DisplayListBox : ListBox` subclass packages the proven status-display setup (single-select, no integral height, horizontal scrollbar, `SetLines`/`SetText` reconcile entry points, opt-in type-ahead suppression). Six pop-out windows swap their multiline TextBox for it; the ECL checklist window's Clear()+rebuild becomes an in-place reconcile; the seven hand-rolled CDU/MCDU/DCDU reconcile loops are consolidated onto the shared helper with their per-form selection semantics kept caller-side.

**Tech Stack:** C# 13 / .NET 9 Windows Forms. No test project exists (repo policy: verification = `dotnet build MSFSBlindAssist.sln -c Debug` + in-sim NVDA testing; do NOT add unit tests).

**Spec:** `docs/superpowers/specs/2026-07-02-live-display-consistency-design.md`

## Global Constraints

- Work in the existing worktree `C:\Users\robin\Downloads\msfs-blind-assist\.claude\worktrees\pr115-fixes`, branch `fix/system-display-refresh-review-fixes`. Do NOT switch branches.
- Build command after every task: `dotnet build MSFSBlindAssist.sln -c Debug` — must end `Build succeeded. 0 Warning(s) 0 Error(s)`. NEVER build the csproj bare (CLAUDE.md build-path trap).
- Refresh cadences, F5/Escape handling, focus behavior, and ALL announce logic stay byte-identical — this pass changes the display control and the reconcile only.
- Never announce UI interactions (repo screen-reader rule); none of these edits may add `Announce` calls.
- Line numbers below are from the investigation pass and may drift a few lines — always Read the region first and match on the quoted code, not the number.
- Commit after every task, message ending with: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. Commit only — NEVER push.

---

### Task 1: `DisplayListBox` shared control

**Files:**
- Create: `MSFSBlindAssist/Forms/DisplayListBox.cs`

**Interfaces:**
- Consumes: `Forms.DisplayList.UpdateInPlace(ListBox, IReadOnlyList<string>)` (exists).
- Produces (used by Tasks 2-6): `class MSFSBlindAssist.Forms.DisplayListBox : ListBox` with `void SetLines(IReadOnlyList<string> lines)`, `void SetText(string joined)`, `bool SuppressTypeAhead { get; set; }`.

- [ ] **Step 1: Write the control**

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MSFSBlindAssist.Forms
{
    /// <summary>
    /// Standard read-only, screen-reader-navigable status list: the ListBox setup proven by the
    /// MainForm status display, packaged so every live display window shares one configuration
    /// and one reconcile path (<see cref="DisplayList.UpdateInPlace"/> — only changed rows are
    /// rewritten, so the NVDA cursor never jumps and only a changed row re-announces while
    /// focused). Windows swap their multiline TextBox for this and call
    /// <see cref="SetLines"/>/<see cref="SetText"/> instead of DisplayText.SetPreserveCaret.
    /// </summary>
    public class DisplayListBox : ListBox
    {
        public DisplayListBox()
        {
            SelectionMode = SelectionMode.One;
            IntegralHeight = false;
            HorizontalScrollbar = true;
            TabStop = true;
            Font = new Font("Consolas", 10f);
        }

        /// <summary>
        /// When true, character keys that no KeyPress handler consumed are marked handled so the
        /// native ListBox incremental type-ahead can never move the selection. Set this on
        /// displays whose character keys are INPUT (the RMP routes digits to the radio); leave
        /// false on read-only displays, where first-letter navigation is harmless and matches
        /// the MainForm status list.
        /// </summary>
        public bool SuppressTypeAhead { get; set; }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e); // caller KeyPress handlers run first (RMP digit routing)
            if (SuppressTypeAhead && !e.Handled)
                e.Handled = true;
        }

        /// <summary>Reconcile the items to <paramref name="lines"/> in place. No-ops when unchanged.</summary>
        public void SetLines(IReadOnlyList<string> lines) => DisplayList.UpdateInPlace(this, lines);

        /// <summary>Split a joined multi-line string into rows and reconcile (same newline split
        /// as MainForm.UpdateDisplayText, so blank separator rows are preserved as items).</summary>
        public void SetText(string joined)
            => SetLines((joined ?? "").Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Forms/DisplayListBox.cs
git commit -m "Add DisplayListBox: shared navigable status-list control over DisplayList.UpdateInPlace"
```

---

### Task 2: E/WD window conversion (`FbwEwdWindow`, A320 + A380)

**Files:**
- Modify: `MSFSBlindAssist/Forms/FbwEwdWindow.cs` (field ~line 21, control creation ~39-51, refresh ~87)

**Interfaces:**
- Consumes: `DisplayListBox.SetText(string)` from Task 1.
- Produces: nothing new (window is self-contained; both aircraft defs call it via `ShowTrackedWindow` with a `Func<Task<string>>` builder — signature unchanged).

- [ ] **Step 1: Read `FbwEwdWindow.cs` fully** (123 lines). Identify: the `TextBox _text` field, its creation block (`Multiline = true, ReadOnly = true, ScrollBars = Vertical, WordWrap = false`, Consolas 11, `AccessibleName`/`AccessibleDescription`), and the `DisplayText.SetPreserveCaret(_text, txt)` call in `RefreshAsync`.

- [ ] **Step 2: Swap the control.** Change the field type to `DisplayListBox` and replace the creation block, preserving name/dock/size/anchors and the accessible metadata (update the description wording from caret to rows):

```csharp
private readonly DisplayListBox _text;
```

```csharp
_text = new DisplayListBox
{
    Font = new Font("Consolas", 11f),
    // keep the existing Location/Size/Anchor/Dock values verbatim from the old TextBox block
    AccessibleName = "E W D display, updates live",
    AccessibleDescription = "Read with the arrow keys. F5 refreshes now.",
};
```

Remove the TextBox-only properties (`Multiline`, `ReadOnly`, `ScrollBars`, `WordWrap`) — they don't exist on ListBox. Keep any `KeyDown`/`ProcessDialogKey` F5/Escape logic untouched.

- [ ] **Step 3: Swap the render sink.** In `RefreshAsync`, replace:

```csharp
DisplayText.SetPreserveCaret(_text, txt);
```

with:

```csharp
_text.SetText(txt);
```

Keep the F5 announce ("E W D refreshed") and the `_busy` guard untouched.

- [ ] **Step 4: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/FbwEwdWindow.cs
git commit -m "E/WD window: navigable live list instead of caret-preserved TextBox (A320 + A380)"
```

---

### Task 3: OANS conversion (`FBWA380OansForm` — BTV readout + Status tab)

**Files:**
- Modify: `MSFSBlindAssist/Forms/FBWA380/FBWA380OansForm.cs` (`_btvReadout` creation ~82-86 + write ~202; `_statusInfo` creation ~130-134 + write ~229). `_airportInfo` (~119-123, write ~220) is NOT converted — single line, stays a TextBox with `SetPreserveCaret`.

**Interfaces:**
- Consumes: `DisplayListBox.SetText(string)`.
- Produces: nothing new.

- [ ] **Step 1: Read the form's control-creation region and the three `SetPreserveCaret` sites.** Confirm `_btvReadout` and `_statusInfo` are multiline read-only TextBoxes with no KeyDown/KeyPress handlers (investigation-verified; re-confirm).

- [ ] **Step 2: Convert `_btvReadout` and `_statusInfo`.** Change both field types to `DisplayListBox`; replace each creation block the same way as Task 2 Step 2 (keep Location/Size/Anchor/Dock and AccessibleName verbatim; drop TextBox-only properties). Do NOT touch `_airportInfo`.

- [ ] **Step 3: Swap the two render sinks.** At the `_btvReadout` write (~202) and the `_statusInfo` write (~229), replace `DisplayText.SetPreserveCaret(<box>, <text>)` with `<box>.SetText(<text>)`. The `BtvReadoutBlock()` builder and `UpdateStatus()` composition stay unchanged (they already build line-by-line via `AppendLine`; `SetText` splits them back into rows). The `_airportInfo` `SetPreserveCaret` call (~220) stays.

- [ ] **Step 4: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/FBWA380/FBWA380OansForm.cs
git commit -m "OANS: BTV readout + Status tab become navigable live lists"
```

---

### Task 4: RMP conversion (`FBWA380RmpForm`)

**Files:**
- Modify: `MSFSBlindAssist/Forms/FBWA380/FBWA380RmpForm.cs` (field ~52, creation ~78-87, render ~576)

**Interfaces:**
- Consumes: `DisplayListBox.SetText(string)`, `SuppressTypeAhead`.
- Produces: nothing new.

**Background (investigation-verified, do not re-derive):** nothing in this form reads the caret. `_selectedRowIndex` is driven only by `PressLine()` (Ctrl+1/2/3 / Select buttons), `SwitchSide()`, and the one-time first-scrape sync. `OnDisplayKeyPress` routes digits to the radio and sets `e.Handled = true` for them; `OnDisplayKeyDown` handles Enter (LSK of `_selectedRowIndex`) and Backspace (`DIGIT_CLR`). The announce pipeline (`Apply` → `AnnounceLive` → `AnnounceVhfEntry`) formats from `_vhfEntry` and scrapes, never from the display control. The form sets no `AcceptButton`.

- [ ] **Step 1: Read the field/creation/handler region (~34-90, ~355-405) and `RenderFromSim` (~543-577).**

- [ ] **Step 2: Swap the control.** Field type → `DisplayListBox`; in the creation block keep Location/Size/Anchor/Font/AccessibleName verbatim, drop TextBox-only properties, and set:

```csharp
_display = new DisplayListBox
{
    SuppressTypeAhead = true,   // character keys are RADIO INPUT here, not list navigation
    Font = new Font("Consolas", 11f),
    // keep existing Location/Size/Anchor + AccessibleName verbatim
};
_display.KeyPress += OnDisplayKeyPress;   // unchanged wiring
_display.KeyDown += OnDisplayKeyDown;     // unchanged wiring
```

Do NOT modify `OnDisplayKeyPress`/`OnDisplayKeyDown` — `SuppressTypeAhead` handles the non-digit type-ahead risk at the control level, after the form's handlers have run.

- [ ] **Step 3: Swap the render sink.** In `RenderFromSim` (~575-576), replace `DisplayText.SetPreserveCaret(_display, text)` with `_display.SetText(text)`.

- [ ] **Step 4: Check the focus helpers still compile.** `FocusDisplay()` (~320) and the `ActiveControl = _display` sites (~145-146, ~187, ~350-351) work on any Control — no change expected; verify by build.

- [ ] **Step 5: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/Forms/FBWA380/FBWA380RmpForm.cs
git commit -m "RMP: display becomes a navigable live list; type-ahead suppressed (keys are radio input)"
```

---

### Task 5: HS787 display + EICAS conversion

**Files:**
- Modify: `MSFSBlindAssist/Forms/HS787/HS787DisplayForm.cs` (field ~26, creation ~44-56, writes ~89-97)
- Modify: `MSFSBlindAssist/Forms/HS787/HS787EicasForm.cs` (field ~19, creation ~40-50, refresh ~78-88)

**Interfaces:**
- Consumes: `DisplayListBox.SetLines(IReadOnlyList<string>)` and `SetText(string)`.
- Produces: nothing new.

- [ ] **Step 1: Convert `HS787DisplayForm`.** Field `_text` → `DisplayListBox` (creation block per Task 2 Step 2 pattern, Consolas 11, keep accessible metadata). In `OnRowsUpdated(List<string> rows)` (~84-99): delete the `string.Join` (~89) and replace BOTH write paths (~92 BeginInvoke, ~97 direct) with `_text.SetLines(rows)` — preserving the existing empty-content sentinel: if the current code substitutes a placeholder string when `rows` is empty, pass that placeholder as a single-item list (`_text.SetLines(new[] { sentinel })`). F5 (`ScrapeNowAsync`) and Escape handling unchanged.

- [ ] **Step 2: Convert `HS787EicasForm`.** Field `_box` → `DisplayListBox` (Consolas 10; `WordWrap` disappears — long alert rows rely on the subclass's `HorizontalScrollbar`, and NVDA reads the full item regardless). Replace the whole hand-rolled body of `RefreshText()` (~78-88: `_lastText` guard + raw `.Text =` + caret clamp) with:

```csharp
private void RefreshText()
{
    string text;
    try { text = _textProvider(); } catch { return; }   // keep the existing provider call + guard style
    _box.SetText(text);
}
```

Delete the now-unused `_lastText` field (the reconcile no-ops on unchanged content). Keep the 1 s timer, Escape handling, and the open-time initial refresh; drop the caret-reset-to-0 line if it references `SelectionStart` (ListBox has no caret — initial selection stays unset, NVDA starts at the top naturally).

- [ ] **Step 3: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/HS787/HS787DisplayForm.cs MSFSBlindAssist/Forms/HS787/HS787EicasForm.cs
git commit -m "HS787 display + EICAS windows: navigable live lists via shared reconcile"
```

---

### Task 6: GSX window menu box (`AccessGSXForm`)

**Files:**
- Modify: `MSFSBlindAssist/Forms/AccessGSXForm.cs` (`_menuTextBox` decl ~87-88, write ~582)

**Interfaces:**
- Consumes: `DisplayListBox.SetText(string)`.
- Produces: nothing new.

- [ ] **Step 1: Read the `_menuTextBox` declaration/creation and its write site (~582 `_menuTextBox.Text = sb.ToString();`), plus any KeyDown handlers on it.** `_statusTextBox` and `_tooltipTextBox` are NOT converted (single-line / free-text blob).

- [ ] **Step 2: Convert the menu box.** Field → `DisplayListBox` (creation per Task 2 Step 2 pattern, keep accessible metadata + layout); replace the write with `_menuTextBox.SetText(sb.ToString());`. If the box has selection/caret-dependent code (none found in investigation, re-verify with a grep for `_menuTextBox.Selection`), stop and reassess before converting.

- [ ] **Step 3 (optional hardening, only if trivial): unchanged-guard the two remaining raw writes.** At `_statusTextBox.Text = _gsxService.StatusText` (~557) and `_tooltipTextBox.Text = _gsxService.LastTooltip` (~587), wrap with `if (<box>.Text != <value>)` so identical pushes don't disturb the reader.

- [ ] **Step 4: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/AccessGSXForm.cs
git commit -m "GSX window: menu becomes a navigable live list; unchanged-guard status/tooltip writes"
```

---

### Task 7: ECL rebuild fix (`FBWA380ChecklistForm`)

**Files:**
- Modify: `MSFSBlindAssist/Forms/FBWA380/FBWA380ChecklistForm.cs` (rebuild ~381-393)

**Interfaces:**
- Consumes: `Forms.DisplayList.UpdateInPlace(ListBox, IReadOnlyList<string>)` directly (the existing `_list` ListBox is kept — no `DisplayListBox` swap needed; the fix is the reconcile).
- Produces: nothing new.

- [ ] **Step 1: Read the rebuild region.** Current shape (~381-393): on content change, `_list.Items.Clear()` + re-add every row, then select the FWS sim cursor `selIdx` when present, else the prior row `keep`, else row 0.

- [ ] **Step 2: Replace the Clear()+re-add with the reconcile, keeping the selection override VERBATIM after it:**

```csharp
// In-place reconcile — only changed rows are rewritten, so NVDA is not torn off the
// list on every checklist update (the old Items.Clear()+re-add reset the reader).
Forms.DisplayList.UpdateInPlace(_list, rows);

// The ECL deliberately follows the FWS sim cursor — this OVERRIDES the reconcile's
// keep-the-user's-row restore, exactly as the old rebuild did. Keep caller-side.
int target = selIdx >= 0 && selIdx < _list.Items.Count ? selIdx
           : (keep >= 0 && keep < _list.Items.Count ? keep : (_list.Items.Count > 0 ? 0 : -1));
if (target >= 0 && _list.SelectedIndex != target)
    _list.SelectedIndex = target;
```

Adapt variable names (`rows`, `selIdx`, `keep`) to the actual local names in the region; the unchanged-content early-skip that already exists stays in place above this block.

- [ ] **Step 3: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/FBWA380/FBWA380ChecklistForm.cs
git commit -m "ECL: reconcile checklist rows in place instead of Clear()+rebuild (cursor-follow kept)"
```

---

### Task 8: CDU consolidation — title-change family (5 forms)

**Files:**
- Modify: `MSFSBlindAssist/Forms/FlyByWireA320/FlyByWireMCDUForm.cs` (~347-375)
- Modify: `MSFSBlindAssist/Forms/FenixA320/FenixMCDUForm.cs` (~481-516)
- Modify: `MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs` (~158-181)
- Modify: `MSFSBlindAssist/Forms/PMDG737/PMDG737CDUForm.cs` (~159-181)
- Modify: `MSFSBlindAssist/Forms/HS787/HS787FMCForm.cs` (~218-241)

**Interfaces:**
- Consumes: `Forms.DisplayList.UpdateInPlace(ListBox, IReadOnlyList<string>)`.
- Produces: nothing new. STRICTLY behavior-preserving — announce logic, title detection, and selection outcomes must be identical.

All five share one shape. For EACH form, apply this transformation to its display-update method:

- [ ] **Step 1: Read the form's reconcile region fully** (the ranges above), noting: the saved-index capture, the grow/shrink+rewrite loop, the title-change branch (announce + force-select 0 or 1), and the else-restore-saved branch.

- [ ] **Step 2: Replace ONLY the loop.** The pattern (variable names differ per form — keep each form's own):

Before (shape):
```csharp
int savedIndex = _display.SelectedIndex;
_display.BeginUpdate();
while (_display.Items.Count > lines.Count) _display.Items.RemoveAt(_display.Items.Count - 1);
while (_display.Items.Count < lines.Count) _display.Items.Add("");
for (int i = 0; i < lines.Count; i++)
    if (!string.Equals(_display.Items[i] as string, lines[i], StringComparison.Ordinal))
        _display.Items[i] = lines[i];
_display.EndUpdate();
```

After:
```csharp
int savedIndex = _display.SelectedIndex;
// Shared in-place reconcile (grow/shrink tail + rewrite changed rows). This form's own
// selection semantics run BELOW and override the helper's content-based restore —
// CDU screens are positional (LSK rows), so index restore / page force-select wins.
Forms.DisplayList.UpdateInPlace(_display, lines);
```

- [ ] **Step 3: Keep the selection branches, made explicit and index-clamped.** The existing title-change/else branches stay, but because `UpdateInPlace` may have moved the selection by content, the else-branch must RESTORE BY INDEX unconditionally (not only "if it changed"):

```csharp
if (titleChanged)
{
    // form's existing announce + force-select (0 or 1) — UNCHANGED
}
else if (savedIndex >= 0 && savedIndex < _display.Items.Count && _display.SelectedIndex != savedIndex)
{
    _display.SelectedIndex = savedIndex;   // positional restore beats content-follow on a CDU
}
```

Keep every surrounding line (first-populate branches, scratchpad debounce kicks, `_previousRows` bookkeeping) untouched. If a form's structure deviates from this shape in a way that makes strict equivalence unclear, DO NOT force it — leave that form's inline loop in place, add a comment `// kept inline: <reason> — see 2026-07-02 spec §4`, and note it in the commit message.

- [ ] **Step 4: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/FlyByWireA320/FlyByWireMCDUForm.cs MSFSBlindAssist/Forms/FenixA320/FenixMCDUForm.cs MSFSBlindAssist/Forms/PMDG777/PMDG777CDUForm.cs MSFSBlindAssist/Forms/PMDG737/PMDG737CDUForm.cs MSFSBlindAssist/Forms/HS787/HS787FMCForm.cs
git commit -m "CDU forms: consolidate list reconcile onto DisplayList.UpdateInPlace (behavior-preserving)"
```

---

### Task 9: CDU consolidation — A380 MCDU (signature-gated selection)

**Files:**
- Modify: `MSFSBlindAssist/Forms/FBWA380/FBWA380MCDUForm.cs` (~516-557)

**Interfaces:**
- Consumes: `Forms.DisplayList.UpdateInPlace`.
- Produces: nothing new. Behavior-preserving.

- [ ] **Step 1: Read ~505-560 fully.** This form appends role words to interactive lines (~516-525) BEFORE the reconcile, then reconciles (~528-535), then runs the `_resetSelection` content-signature gate (~543-551: force `SelectedIndex = 0` only once the NEW page's content signature actually changes — the wrong-line-after-page-change guard), else restores `saved` (~552-555), then `UpdateStatusLabel()` (~557).

- [ ] **Step 2: Replace ONLY the loop (~528-535)** with `Forms.DisplayList.UpdateInPlace(_display, lines);` (adapt names). Keep the role-word pass, the signature gate, the saved-restore (made unconditional-by-index as in Task 8 Step 3), and `UpdateStatusLabel()` all exactly where they are.

- [ ] **Step 3: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/FBWA380/FBWA380MCDUForm.cs
git commit -m "A380 MCDU: consolidate reconcile onto DisplayList.UpdateInPlace (signature gate kept)"
```

---

### Task 10: CDU consolidation — DCDU (index-anchored)

**Files:**
- Modify: `MSFSBlindAssist/Forms/FlyByWireA320/FlyByWireDcduForm.cs` (`SetText`, ~223-241)

**Interfaces:**
- Consumes: `Forms.DisplayList.UpdateInPlace`.
- Produces: nothing new. Behavior-preserving. CLAUDE.md documents this form's reconcile explicitly ("SetText splits on newline and reconciles items in place... preserving SelectedIndex so the 1 s poll never yanks the braille reading position") — that contract must hold.

- [ ] **Step 1: Read `SetText` fully.** Shape: split text into lines, `saved = _list.SelectedIndex`, BeginUpdate/reconcile-loop/EndUpdate, then: first populate (`saved == -1`) → `SelectedIndex = 0` (braille anchor); else restore `saved` by index.

- [ ] **Step 2: Replace the loop** with `Forms.DisplayList.UpdateInPlace(_list, lines);`, keeping the split, the saved capture BEFORE the call, and both selection branches after it (index-clamped, unconditional restore as in Task 8 Step 3):

```csharp
int saved = _list.SelectedIndex;
Forms.DisplayList.UpdateInPlace(_list, lines);
if (saved < 0)
{
    if (_list.Items.Count > 0 && _list.SelectedIndex != 0)
        _list.SelectedIndex = 0;   // first populate: anchor the braille display to line 0
}
else if (saved < _list.Items.Count && _list.SelectedIndex != saved)
{
    _list.SelectedIndex = saved;   // positional restore — braille reading position
}
```

- [ ] **Step 3: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/FlyByWireA320/FlyByWireDcduForm.cs
git commit -m "DCDU: consolidate reconcile onto DisplayList.UpdateInPlace (braille index anchor kept)"
```

---

### Task 11: Documentation truth-up (DisplayList header + CLAUDE.md)

**Files:**
- Modify: `MSFSBlindAssist/Forms/DisplayList.cs` (header comment, ~lines 13-17)
- Modify: `CLAUDE.md` (the "Status-display boxes refresh live" section's ⚠️ note; the DCDU `SetText` description; the E/WD window sentence "The ONE display that keeps a pop-out window is the E/WD"; the ECL scrape/refresh notes; the HS787 EICAS window description)

- [ ] **Step 1: Restore the DisplayList header to a true single-home statement.** Replace the "NOT happened yet" caveat block with:

```csharp
    /// Parallel to <see cref="DisplayText.SetPreserveCaret"/> (the TextBox equivalent, now used
    /// only by single-line/blob boxes). This IS the single home for the list-update pattern:
    /// MainForm's status display, every DisplayListBox window (E/WD, OANS, RMP, HS787 display +
    /// EICAS, GSX menu), the ECL checklist, and all MCDU/CDU/DCDU forms reconcile through it.
    /// Per-form selection semantics (title/page force-select, positional index restore for CDU
    /// screens, the ECL's FWS cursor-follow) run CALLER-SIDE after this call and override the
    /// content-based restore below — keep it that way.
```

- [ ] **Step 2: Update CLAUDE.md.** In the "Status-display boxes refresh live" section, replace the "⚠️ The MCDU/CDU/DCDU forms still carry their own hand-rolled copies..." sentence with: "All MCDU/CDU/DCDU forms, the pop-out windows (E/WD, OANS, RMP, HS787 display + EICAS, GSX menu — via the shared `Forms/DisplayListBox` control), and the ECL now reconcile through `DisplayList.UpdateInPlace`; per-form selection semantics (title/page force-select, CDU positional index restore, ECL FWS cursor-follow) run caller-side after the call. `DisplayText.SetPreserveCaret` survives with a single caller (the OANS Airport tab's one-line box); the GSX status/tooltip boxes keep plain unchanged-guarded `.Text` writes." Search for other now-stale phrases: the DCDU "`SetText` splits on newline and reconciles items in place (BeginUpdate/EndUpdate, preserving `SelectedIndex`...)" description stays accurate (mechanism unchanged, now routed through the helper — append "(via `DisplayList.UpdateInPlace`)"); the HS787 EICAS "1 s caret-preserving refresh" phrase → "1 s in-place list refresh".

- [ ] **Step 3: Build** (CLAUDE.md is not compiled, but the DisplayList.cs comment edit is)

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/Forms/DisplayList.cs CLAUDE.md
git commit -m "Docs: DisplayList is now the single reconcile home; update CLAUDE.md display notes"
```

---

### Task 12: Final verification sweep

- [ ] **Step 1: Grep for leftovers.**

Run: `git grep -n "SetPreserveCaret" -- MSFSBlindAssist`
Expected remaining callers: `Forms/DisplayText.cs` (definition), `FBWA380OansForm.cs` (`_airportInfo`, 1 site). NOTHING else. If others remain, a task was missed — fix it.

Run: `git grep -n "Items.Clear()" -- MSFSBlindAssist/Forms | grep -iv "combo\|dropdown"`
Expected: no live-display ListBox rebuilds remain (static list fills like GateTeleportForm are fine).

- [ ] **Step 2: Full clean build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Report.** Summarize the commits and reproduce the spec §6 in-sim NVDA test plan (RMP typing first, CDU braille regression second) for the owner's live pass. Do NOT push.

# Live-Display Consistency Pass — Design

**Date:** 2026-07-02
**Branch:** stacks on `fix/system-display-refresh-review-fixes` (depends on `Forms/DisplayList.cs` introduced by PR #115 + the review-fix commit `47dd1d73`)
**Owner decision:** full consistency pass (all live displays + CDU reconcile consolidation)

## Goal

Every live-refreshing text display in MSFSBA uses the same mechanism: a navigable
`ListBox` reconciled in place via `Forms.DisplayList.UpdateInPlace` — only changed
rows are rewritten, the NVDA cursor is never yanked, and only a changed row
re-announces while focused. PR #115 delivered this for the in-panel Status Display;
this pass extends it to every pop-out window and consolidates the seven hand-rolled
copies of the reconcile pattern.

Non-goals: refresh cadences stay unchanged everywhere (this pass changes the control
and the reconcile only); no announce-behavior changes; no visual redesign.

## 1. New shared control — `Forms/DisplayListBox.cs`

A small `public class DisplayListBox : ListBox` encoding the proven status-display
setup once:

- Constructor sets: `SelectionMode = SelectionMode.One`, `IntegralHeight = false`,
  `HorizontalScrollbar = true`, `TabStop = true`, monospace font
  (`Consolas`; size settable via the normal `Font` property by callers that differ).
- `void SetLines(IReadOnlyList<string> lines)` → `DisplayList.UpdateInPlace(this, lines)`.
- `void SetText(string joined)` → splits on `\r\n` / `\n` (matching
  `MainForm.UpdateDisplayText`'s split), then `SetLines`. For callers that hold a
  joined string (E/WD builder, EICAS provider, RMP render).
- `bool SuppressTypeAhead` (default `false`): when `true`, `OnKeyPress` raises the
  event as normal and then marks any still-unhandled character as handled, so the
  native ListBox incremental type-ahead can never move the selection. Only the RMP
  sets this — its character keys are radio input, not navigation. Read-only windows
  keep default type-ahead (consistent with the MainForm panel status list, which
  doesn't suppress it).

`DisplayList.UpdateInPlace` itself is unchanged (content-based selection restore,
nearest-index duplicate matching, shrink clamping — all from the PR #115 fix pass).

## 2. Window conversions

Each conversion swaps the display `TextBox` for a `DisplayListBox` and replaces the
`DisplayText.SetPreserveCaret` call with `SetLines`/`SetText`. Refresh cadence,
F5/Escape handling, focus behavior, and announce logic are untouched.

### 2a. E/WD window — `Forms/FbwEwdWindow.cs` (A320 + A380, shared)

- Current: multiline read-only `TextBox` `_text` (lines ~39-51, `WordWrap = false`),
  refreshed by a 2 s WinForms timer + F5 via `SetPreserveCaret` (line ~87). Content
  comes from the aircraft's `BuildEwdWindowTextAsync` (A380 def ~6595, A320 def
  ~6850) — both internally build a `List<string>` and `string.Join` it.
- Change: `_text` → `DisplayListBox`; line ~87 becomes `_list.SetText(txt)`.
  Builder signature (`Func<Task<string>>`) unchanged.
- Notes: blank `""` separator rows and repeated `--` rows exist — handled by the
  nearest-index duplicate matching. Long A380 multi-engine rows become one
  horizontally-scrollable item (same reading unit NVDA already used). Keep the
  "E W D refreshed" F5 announce.

### 2b. OANS — `Forms/FBWA380/FBWA380OansForm.cs`

- `_btvReadout` (Map/BTV tab, built in `BtvReadoutBlock()` ~234-260, pushed via the
  dedup'd 1 s `oans_state` event, `SetPreserveCaret` at ~202): convert. The builder
  already appends line-by-line — return the lines and call `SetLines`.
- `_statusInfo` (Status tab, 1-3 rows, `SetPreserveCaret` at ~229): convert (trivial).
- `_airportInfo` (Airport tab, single line, `SetPreserveCaret` at ~220): **stays a
  TextBox** — a one-line list adds nothing. This becomes
  `DisplayText.SetPreserveCaret`'s single remaining caller; `DisplayText.cs` is kept.
- No keyboard handlers exist on any of the three boxes; `ProcessCmdKey` (Escape/F5)
  is form-level and unaffected.

### 2c. RMP — `Forms/FBWA380/FBWA380RmpForm.cs`

- Current: read-only multiline `TextBox` `_display` (~78-87) the user types into
  (KeyPress digits → radio, Backspace → `DIGIT_CLR`, Enter → `LSK_{_selectedRowIndex+1}`,
  Ctrl/Alt+1-3 at form level), rendered by `RenderFromSim()` → `SetPreserveCaret`
  (~576). Verified: **nothing reads the caret** — `_selectedRowIndex` is driven only
  by `PressLine()` / `SwitchSide()` / the one-time first-scrape sync (~508-510), and
  the announce pipeline (`Apply` → `AnnounceLive`, ~491-539) never touches the
  display control.
- Change: `_display` → `DisplayListBox` with `SuppressTypeAhead = true`; the
  `KeyPress`/`KeyDown` wiring (~86-87) carries over verbatim (both events fire on a
  ListBox; digit handlers already set `e.Handled = true`); `RenderFromSim` calls
  `SetText`.
- Behavior note: arrow keys now move the list selection instead of a caret — the
  desired NVDA reading model; the reconcile keeps the reader in place across the
  300 ms polls.
- **In-sim watch item:** the SQWK page's `"Squawk entry: NN__"` row (~572) and the
  moving `", selected"` suffix (~560) mutate while the user may be parked on them;
  the content-restore falls back to index-clamp there. Expected fine; verify.

### 2d. HS787 display window — `Forms/HS787/HS787DisplayForm.cs`

- Current: `TextBox _text` fed by `CoherentDisplayClient.RowsUpdated`
  (change-gated, 1.5 s poll) which already delivers `List<string> rows`, joined at
  ~89 and written via `SetPreserveCaret` (~92 BeginInvoke path, ~97 direct path).
- Change: `_text` → `DisplayListBox`; both sites call `SetLines(rows)` directly and
  the join is dropped (the empty-content sentinel becomes a single-item list).
  Cleanest conversion in the pass.

### 2e. HS787 EICAS — `Forms/HS787/HS787EicasForm.cs`

- Current: `TextBox _box` (`WordWrap = true`), 1 s timer, hand-rolled refresh
  (`RefreshText()` ~78-88: `_lastText` no-op guard, raw `.Text =`, manual caret
  clamp). Content from `MainForm.BuildHs787EicasText` (~3476-3499): newline-separated
  labeled rows + one alert per line under `Warnings/Cautions/Advisories` headers.
- Change: `_box` → `DisplayListBox`; `RefreshText()` collapses to
  `_list.SetText(text)` (the no-op and per-row diffing now live in the shared
  reconcile). Word-wrap is lost visually — long alert sentences rely on the
  horizontal scrollbar; NVDA speaks the full item regardless, so the target user
  loses nothing.
- Note: ~13 engine rows change nearly every tick. Same churn as today; the selected
  row re-announces only when its own value changes (the PR #115 semantics).

### 2f. GSX window — `Forms/AccessGSXForm.cs`

- `_menuTextBox` (multiline read-only, push-refreshed from GSX service callbacks via
  `BeginInvoke`, raw `.Text = sb.ToString()` at ~582): convert to `DisplayListBox`
  — the GSX menu is one line per menu entry, exactly row-shaped.
- `_statusTextBox` (single-line) and `_tooltipTextBox` (free-text sentence blob):
  **stay TextBoxes** — no row structure to navigate. Optional hardening (only if the
  diff is trivial): guard their raw `.Text =` writes with an unchanged-check so an
  identical push doesn't disturb the reader.

## 3. ECL rebuild fix — `Forms/FBWA380/FBWA380ChecklistForm.cs`

Already a `ListBox` (`_list`, ~97), pushed ~1 Hz from the shared E/WD monitor — but
a content change does `Items.Clear()` + full re-add (~381-387), tearing the list
down under NVDA. Change: replace the rebuild with `DisplayList.UpdateInPlace(_list,
rows)`, then re-apply the form's deliberate selection override **after** the
reconcile: follow the FWS sim cursor `selIdx` when present, else keep the prior row,
else row 0 (~391-393). The unchanged-content skip stays. (The control itself can
stay a plain ListBox; no need to re-parent it as `DisplayListBox` since its setup
already exists — consistency here is about the reconcile, not the subclass.)

## 4. CDU/MCDU/DCDU reconcile consolidation (7 forms, behavior-preserving)

Replace each inline grow/shrink/rewrite loop with `DisplayList.UpdateInPlace`, then
run the form's own selection semantics AFTER the shared call, exactly as today.
These forms are NVDA/braille-proven — the per-form diff is deliberately minimal, and
each form keeps its announce logic untouched:

| Form | Inline copy | Caller-side semantics to keep (run after the reconcile) |
|---|---|---|
| `FlyByWireA320/FlyByWireMCDUForm.cs` ~347-375 | loop 354-361 | title change → announce + `SelectedIndex = 1`; else restore saved index; scratchpad debounce kick |
| `FenixA320/FenixMCDUForm.cs` ~481-504 | loop 492-503 | title change → announce + select first content line; else restore saved index |
| `PMDG777/PMDG777CDUForm.cs` ~158-172 | loop 160-171 | title change → `SelectedIndex = 0`; else restore saved index |
| `PMDG737/PMDG737CDUForm.cs` ~159-172 | loop 161-172 | same as 777 |
| `FBWA380/FBWA380MCDUForm.cs` ~528-535 | loop 530-534 | `_resetSelection` content-signature gate → `SelectedIndex = 0` once the new page's content settles; else restore saved; role-word appending + `UpdateStatusLabel()` |
| `HS787/HS787FMCForm.cs` ~218-228 | loop 220-228 | title change → announce + `SelectedIndex = 0`; else restore saved |
| `FlyByWireA320/FlyByWireDcduForm.cs` `SetText` ~223-241 | loop 229-236 | first populate → `SelectedIndex = 0` (braille anchor); later updates restore saved INDEX |

Equivalence argument for the "restore saved index" cases: when the selected row's
text changes in place (the common CDU update), `UpdateInPlace`'s content search
misses and clamps to the old index — the same outcome as the forms' index restore.
Where a form *forces* a selection (title/page change, DCDU first populate), the
caller-side override sets `SelectedIndex` explicitly after the reconcile and wins.
Any form where strict equivalence can't be argued during implementation keeps its
inline copy and gets a comment saying why (correctness beats consistency).

This makes `DisplayList.UpdateInPlace` the actual single home for the pattern; its
header comment (softened in the PR #115 fix pass) is restored to a true "single
home" statement at the end of this pass.

## 5. Sequencing and commits

Work stacks on `fix/system-display-refresh-review-fixes` in the existing worktree.
One commit per section so anything reverts independently:

1. `DisplayListBox` control (+ build)
2. E/WD window
3. OANS + RMP
4. HS787 display + EICAS
5. GSX menu box
6. ECL rebuild fix
7. CDU consolidation (may be split per-form if any needs discussion)
8. CLAUDE.md updates (E/WD window note, RMP display description, ECL note, HS787
   EICAS note, DisplayList "single home" statement)

## 6. Verification

- `dotnet build MSFSBlindAssist.sln -c Debug` clean after every commit.
- Code-walk each conversion against the investigation notes (no caret reads left,
  no announce-path changes).
- In-sim NVDA test plan (for Robin), most-risky first:
  1. **RMP (Ctrl+Shift+R):** tune a VHF frequency by typing digits + Enter; set a
     squawk on the SQWK page while arrowing the display; confirm typing never moves
     the reading row (type-ahead suppressed) and the entry row updates in place.
  2. **CDU regression, one per family:** FBW A320 MCDU, PMDG 777 CDU, DCDU — page
     through, confirm title-change selection behavior and braille line stability
     match today's behavior exactly.
  3. **E/WD (Alt+E)** on either jet: arrow onto an engine row, change thrust,
     confirm the row updates without the cursor jumping.
  4. **ECL (Ctrl+Shift+C):** tick items through a checklist; confirm the cursor
     follows the FWS cursor as before and the list no longer "resets" on change.
  5. **HS787 EICAS (Alt+E):** park on an engine row during climb; confirm no cursor
     jump; open with a long CAS alert active and confirm the full alert reads.
  6. **OANS Map tab:** arm a BTV runway/exit, watch the readout rows update in place.
  7. **GSX window:** open a GSX menu, confirm menu rows read and update in place.

## Risks

- **RMP typing** — mitigated by `SuppressTypeAhead` + in-sim test 1.
- **CDU braille regressions** — mitigated by strict behavior-preservation, minimal
  per-form diffs, and the escape hatch of keeping any form's inline copy.
- **EICAS visual wrap loss** — accepted; horizontal scrollbar; NVDA unaffected.
- **ECL cursor-follow** — the sim-cursor override is reapplied verbatim after the
  reconcile; test 4 covers it.

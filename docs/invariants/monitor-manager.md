# Monitor Manager dialogs (Ctrl+M) — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/monitor-manager.md`, which Claude Code loads when it reads matching code. Background: [architecture.md](../architecture.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## MON-1

- Every per-aircraft monitor manager is a subclass of `Forms/MonitorManagerFormBase` supplying only a title, its rows, and its `*DisabledMonitorVariables` list — never re-add per-form UI, and never copy the filter into a form. The pure half (`Services/MonitorRowBuilder` + `Services/MonitorVariableFilter`) carries the xUnit coverage. (The MD-11's was the last hand-rolled one — no search box over ~530 rows — and was migrated 2026-09-06.)

Reworded 2026-10-07 from "All seven per-aircraft monitor managers are subclasses of `Forms/MonitorManagerFormBase` supplying only a title, their rows, and their `*DisabledMonitorVariables` list": there were seven when it was written, and the count was dropped so that each new aircraft's monitor manager need not edit it.

## MON-2

- The list rebuilds on exactly THREE events — form open, search text changed, Show filter changed — and NEVER from `ItemCheck`: re-filtering on a tick drops the row out from under the caret in Muted view, slides the next variable into its place, and makes a second Space press mute a variable the pilot never selected.

## MON-3

- `_suppressItemCheck` must wrap every rebuild. `SetItemChecked` raises `ItemCheck` per row, so without it one filter keystroke on the 400-row PMDG list fires ~400 `SettingsManager.Save()` disk writes on the UI thread.

## MON-4

- The list's `AccessibleName` names the ACTIVE FILTER and carries the count ("Muted variables, 12 of 300"), never a `Label` and never spoken — it is the only channel by which either reaches the pilot, read when focus lands on the list, with no app-generated speech over their typing. Compose it via `MonitorVariableFilter.DescribeList`; a fixed prefix made changing the Show filter completely inaudible (live report), so the three modes must never render the same leading phrase.

## MON-5

- `ItemCheck` maps `e.Index` through the VISIBLE rows, never the full row list — the two diverge the moment a filter is applied.

## MON-6

- Every rebuild must end by selecting row 0 when the list came back with no selection. `Items.Clear()` drops `SelectedIndex` to -1 and NOTHING restores it — not adding items, not the list receiving focus (measured, not assumed) — and at -1 the screen reader announces the list with no current item and the first Space press does nothing, because a `CheckedListBox` toggles the item at `SelectedIndex`. It is the same -1 the first-open path had to fix, and it applies to every search keystroke and every Show change.

## MON-7

- That write reaches `_lastIndexByForm` through `SelectedIndexChanged`, so `ShowForm` must read the remembered row BEFORE it calls `ApplyFilter` — read it after and it is always 0, and the pilot's last position is silently lost on every open.

## MON-8

- The dialog opens clean every time (search empty, Show on All): a remembered filter would open showing a fraction of the list for a reason the pilot cannot see. Do not add persistence. Both resets are made under `_suppressFilter` so the open rebuilds the list ONCE, not once per control.

## MON-9

- The A380 E/WD fold lives in `MonitorRowBuilder.BuildWithFold`, not on the form — a private static on a `Form` is unreachable from the test project. Its synthetic row is appended AFTER the sort (so it lands last) and only when a folded variable would otherwise have been listed: a fold row over a family that is entirely unannounced is a checkbox that silences nothing.

## MON-10

- `FBWA380MonitorManagerForm.EcamMemosKey` must stay public with that exact name — `MainForm.AircraftSwitch.cs` gates the Coherent E/WD scrape and the FWS failure client on it, so the one row mutes three independent speech sources.

## MON-11

- `MonitorVariableFilter` must use `OrdinalIgnoreCase`, never `ToLower()`/`ToLowerInvariant()` — tr-TR folds "I" to dotless "ı" and the search silently stops matching.

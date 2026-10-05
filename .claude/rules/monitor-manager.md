---
paths:
  - "MSFSBlindAssist/Forms/*MonitorManager*.cs"
  - "MSFSBlindAssist/Forms/**/*MonitorManager*.cs"
  - "MSFSBlindAssist/Services/MonitorRowBuilder.cs"
  - "MSFSBlindAssist/Services/MonitorVariableFilter.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Monitor*.cs"
---
# Monitor Manager dialogs (Ctrl+M) rules

Loaded when Claude reads matching code. Background: docs/architecture.md. Full text of each rule: docs/invariants/monitor-manager.md.

- [MON-1] All seven monitor managers subclass `Forms/MonitorManagerFormBase`, supplying only a title, rows and their `*DisabledMonitorVariables` list — never re-add per-form UI, and never copy the filter into a form. Full: docs/invariants/monitor-manager.md#mon-1
- [MON-2] The list rebuilds on exactly THREE events (form open, search text changed, Show filter changed), NEVER from `ItemCheck`: re-filtering on a tick slides the next row under the caret, so a second Space mutes a variable the pilot never selected. Full: docs/invariants/monitor-manager.md#mon-2
- [MON-3] `_suppressItemCheck` must wrap every rebuild: `SetItemChecked` raises `ItemCheck` per row, so without it one filter keystroke fires hundreds of `SettingsManager.Save()` disk writes on the UI thread. Full: docs/invariants/monitor-manager.md#mon-3
- [MON-4] The list's `AccessibleName` names the ACTIVE FILTER and the count ("Muted variables, 12 of 300"), composed by `MonitorVariableFilter.DescribeList` — never a `Label`, never spoken; the three Show modes must never render the same leading phrase. Full: docs/invariants/monitor-manager.md#mon-4
- [MON-5] `ItemCheck` maps `e.Index` through the VISIBLE rows, never the full row list — the two diverge the moment a filter is applied. Full: docs/invariants/monitor-manager.md#mon-5
- [MON-6] Every rebuild must end by selecting row 0 when the list has no selection: `Items.Clear()` leaves `SelectedIndex` at -1 and nothing restores it, so the reader announces no current item and the first Space does nothing. Full: docs/invariants/monitor-manager.md#mon-6
- [MON-7] `ShowForm` must read the remembered row (`_lastIndexByForm`) BEFORE it calls `ApplyFilter`: the rebuild's selection writes it through `SelectedIndexChanged`, so read after it is always 0. Full: docs/invariants/monitor-manager.md#mon-7
- [MON-8] The dialog opens clean every time (search empty, Show on All) — do not add filter persistence; both resets are made under `_suppressFilter` so the open rebuilds the list ONCE. Full: docs/invariants/monitor-manager.md#mon-8
- [MON-9] The A380 E/WD fold lives in `MonitorRowBuilder.BuildWithFold`, not on the form (the tests cannot reach a form); its synthetic row is appended AFTER the sort and only when a folded variable would otherwise be listed. Full: docs/invariants/monitor-manager.md#mon-9
- [MON-10] `FBWA380MonitorManagerForm.EcamMemosKey` must stay public with that exact name: `MainForm.AircraftSwitch.cs` gates the Coherent E/WD scrape and the FWS failure client on it, so one row mutes three speech sources. Full: docs/invariants/monitor-manager.md#mon-10
- [MON-11] `MonitorVariableFilter` must use `OrdinalIgnoreCase`, never `ToLower()`/`ToLowerInvariant()` — tr-TR folds "I" to dotless "ı" and the search silently stops matching. Full: docs/invariants/monitor-manager.md#mon-11

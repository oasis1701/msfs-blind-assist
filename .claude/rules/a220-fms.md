---
paths:
  - "MSFSBlindAssist/Aircraft/A220/A220DisplaysClient.cs"
  - "MSFSBlindAssist/Aircraft/A220/A220EfbClient.cs"
  - "MSFSBlindAssist/Aircraft/A220/A220Fms*.cs"
  - "MSFSBlindAssist/Aircraft/A220/A220Ecl*.cs"
  - "MSFSBlindAssist/Aircraft/A220/A220ChecklistPack.cs"
  - "MSFSBlindAssist/Aircraft/A220/SynapticA220EclData.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.Displays.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.FmsEcl.cs"
  - "MSFSBlindAssist/Aircraft/SynapticA220Definition.Efb.cs"
  - "MSFSBlindAssist/Forms/A220/A220FmsForm.cs"
  - "MSFSBlindAssist/Forms/A220/A220ChecklistForm.cs"
  - "MSFSBlindAssist/Forms/A220/A220EfbForm.cs"
  - "MSFSBlindAssist/Forms/A220/A220NavdataForm.cs"
  - "MSFSBlindAssist/Resources/coherent-a220-*.js"
  - "tests/MSFSBlindAssist.Tests/**/*A220Fms*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*A220Ecl*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*A220DialogList*.cs"
---
# Synaptic A220-300 FMS, ECL and EFB rules

Loaded when Claude reads matching code. Background: docs/a220.md. Full text of each rule: docs/invariants/a220-fms.md.

- [A220-18] One DisplayUnits inspector socket, owned by `A220DisplaysClient`: the display pump, FMS form and ECL form share it (`CallAgentAsync`); never open a second connection to that view. The EFB has its own view and client. Full: docs/invariants/a220-fms.md#a220-18
- [A220-19] Drive the FMS with `H:A220_KBD_*`/`H:A220_CTP_*` events (calc path, seq-prefixed) plus clicks on field value nodes, NEVER the hardware-keyboard Entry Mode (it disables all sim input). The input authority is the unencrypted `ModelBehaviorDefs\Synaptic\A220\`, not the JS bundles. Full: docs/invariants/a220-fms.md#a220-19
- [A220-20] The field-commit click hunt skips GRAY tokens (labels) and ranks INLINE above BELOW before nearest-dx (the FUEL page's right-aligned labels made nearest-dx click the wrong field). The agent's `bestValueNode` and `ParseFms` must stay identical. Full: docs/invariants/a220-fms.md#a220-20
- [A220-21] MKP typing is lossy: confirm every character against the scratchpad tap before sending the next; never "fix" a dropped keystroke by raising the inter-key delay. Full: docs/invariants/a220-fms.md#a220-21
- [A220-22] MKP CLEAR is a one-char backspace that arms `--DELETE--` on an empty scratchpad: never clear-before-typing with it. Clear via the agent's `resetScratchpad()` with tap-confirmed emptiness and `PredictScratchpadBuffer`, or entries after one INVALID ENTRY concatenate. Full: docs/invariants/a220-fms.md#a220-22
- [A220-23] ECL actuation: item -> `checklists.json` `"sensed"` NAME -> `A220EclActionMap` (all 215 vars mapped or excluded, coverage-pinned) -> `HandleUIVariableSet`. MSFSBA never ticks a checkbox itself, one item at a time; fire handles, thrust levers and sidestick stay excluded. Full: docs/invariants/a220-fms.md#a220-23
- [A220-24] Detect CLOSED FMS dropdowns via React fibers (`options` + `onSelect` -> `FmsField.IsDropdown`); Enter opens the list, never types the scratchpad into it. Never announce a closed dropdown's option count (it includes hidden options). Close an open one with `dropdownClose()`, never `taskMenuClose()`. Full: docs/invariants/a220-fms.md#a220-24
- [A220-25] Read an FMS follow-up dialog (INTC CRS, HOLD, CROSSING, FIX, AIRWAYS) via `agent.fmsDialogTokens()`, scoped to the dialog's own `<g>`, never a geometric clip of the page (it swallows the list beneath). Space-join tspan runs; each follow-up raises a MOD needing EXEC. Full: docs/invariants/a220-fms.md#a220-25
- [A220-26] Address every LEGS-page action by the aircraft's own `legIdx` from React fibers (`agent.legs()`, `openLegMenuAt`, `deleteLegAt`, calling the row's real `onClick`/`onDelete`), never ident text, occurrence or geometry: duplicate idents opened the wrong waypoint's menu. Full: docs/invariants/a220-fms.md#a220-26
- [A220-27] Pair `Row.LegIdx` to the fiber `legs()` rows by CONTENT (`A220FmsLegParsing.AlignLegIndices`: ident match, disco to placeholder, short lookahead, unmatched -1), never a positional zip: the fiber walk omits rows the grouper shows, so a zip shifted every index by 2. Full: docs/invariants/a220-fms.md#a220-27
- [A220-28] A discontinuity can share its `legIdx` with a neighbour: `legs()` never dedupes by idx alone, and `deleteLegAt(idx, true)` must REFUSE (`WRONG_ROW`) rather than run a non-placeholder row's `onDelete`. An idx dedupe once deleted real waypoints until the plan collapsed. Full: docs/invariants/a220-fms.md#a220-28
- [A220-29] Find the leg ident node STRUCTURALLY (row Input props with both `onDelete` and `value`), never by x: the track line shares its column and legIdx. Compare a menu header's first token only. A `SELECT CONSTRAINT` dialog after deleting a discontinuity is a prompt, not a failure. Full: docs/invariants/a220-fms.md#a220-29
- [A220-30] Open the LEGS revision menu ONLY from the row's fix-symbol icon (`agent.openLegMenu`), never by clicking the ident: it is a scratchpad Input that copies or submits into the leg (it corrupted a live plan). The discontinuity delete aborts when `armStatus()` never confirms. Full: docs/invariants/a220-fms.md#a220-30
- [A220-31] Render every FMS overlay (revision menu, dialog, open dropdown) MODALLY: one `agent.overlay()` per poll, dialogs clipped via `ClipTo`, Escape dismissing the overlay first; never flatten it into the page rows. Read out the amber rejection box (`#ffe300`): it says WHY an entry was refused. Full: docs/invariants/a220-fms.md#a220-31
- [A220-32] EFB `setField` dispatches `keyup` as well as `input`+`change`+`blur`: the EFB binds its Settings saves (the SimBrief ID the FMS uplink and loadsheet import need) and the Takeoff-perf sanitizers to `el.onkeyup`; without it the text shows but never commits. Full: docs/invariants/a220-fms.md#a220-32

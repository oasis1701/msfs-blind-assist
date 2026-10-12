# Synaptic A220-300 FMS, ECL and EFB — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/a220-fms.md`, which Claude Code loads when it reads matching code. Background: [a220.md](../a220.md).
The text is verbatim from CLAUDE.md on `feat/synaptic-a220` as of `fda36823`; a trailing "→ doc" pointer is the original's. One original bullet held two rules and is split across A220-28 and A220-29. These rules were in `.claude/rules/a220.md` until it outgrew the rule-file budget.

## A220-18

- ONE DisplayUnits inspector socket, owned by `A220DisplaysClient` — the display pump, FMS form, and ECL form all share it (`CallAgentAsync`); never open a second connection to that view. The EFB has its own view/client. → [a220.md](../a220.md)

## A220-19

- The A220 FMS is driven by `H:A220_KBD_*`/`H:A220_CTP_*` events (calc-path, seq-prefixed) + synthetic clicks on field value nodes — NEVER via the aircraft's hardware-keyboard Entry Mode (it disables ALL sim input; lethal trap for a blind user). The input authority is the UNENCRYPTED `ModelBehaviorDefs\Synaptic\A220\` on disk, not the JS bundles (pure renderers). → [a220.md](../a220.md)

## A220-20

- The A220 field-commit click hunt must SKIP GRAY tokens (a label is never a value) and rank INLINE (the label's own row) above BELOW before nearest-dx — the FUEL page right-aligns its labels into a column, so nearest-dx clicked GWT for ZFW and TAXI for BLOCK (live 2026-07-30). The agent's `bestValueNode` and `ParseFms` must stay identical, or the form reads one field and clicks another. → [a220.md](../a220.md)

## A220-21

- MKP typing is a LOSSY transport — a paced open-loop send drops keystrokes at random positions (live: `109270` → `10970`/`09270`/`1020`), so every character must be confirmed against the scratchpad tap before the next is sent; never "fix" a dropped keystroke by raising the inter-key delay. → [a220.md](../a220.md)

## A220-22

- The MKP CLEAR key is a ONE-CHAR backspace that ARMS `--DELETE--` on an empty scratchpad — never use it to "clear before typing"; a refused entry leaves its text in the scratchpad, so scratchpad clearing must go through the agent's `resetScratchpad()` store event with tap-confirmed emptiness + typed-buffer verification (`PredictScratchpadBuffer`), or every entry after one INVALID ENTRY concatenates into the next (live 2026-07-30 FUEL page). FMS FUEL page: FOB is NOT enterable on the ground (BLOCK/TAXI are the ground entries and are air-disabled), and entry ranges follow the aircraft's KG/LB setting — the "WT(LB)/WT(KG)" header is how the pilot tells. → [a220.md](../a220.md)

## A220-23

- ECL actuation goes item → `checklists.json` `"sensed"` NAME (a string, not an ABI index) → `A220EclActionMap` (all 215 vars mapped or explicitly excluded, coverage-pinned) → `HandleUIVariableSet`; MSFSBA never ticks a checkbox itself, item-at-a-time only, fire handles/thrust levers/sidestick stay excluded. → [a220.md](../a220.md)

## A220-24

- A220 CLOSED dropdowns must be detected structurally via React fibers (component with an `options` array + `onSelect` → `dd` flag on the value token → `FmsField.IsDropdown`), because a closed Fusion dropdown renders only its current value and is otherwise indistinguishable from a data row; Enter opens the list and must never type the scratchpad into a chooser. Never announce the closed dropdown's option count — the props array includes options the aircraft hides (plan selector says 3, opens 2). An open dropdown closes via `dropdownClose()`, never `taskMenuClose()` (which needs the menu frame and no-ops on a dropdown). → [a220.md](../a220.md)

## A220-25

- An A220 FMS follow-up dialog (direct-to's `INTC CRS`, HOLD, CROSSING, FIX, AIRWAYS…) must be read via `agent.fmsDialogTokens()` — scoped to the dialog's own `<g>` subtree — never by a geometric clip of the page: a dialog floats over the list, so a rect clip swallows the page text beneath it (live: RNP/DISCONTINUITY/DTG inside an INTC CRS dialog). Going direct DOES expose a settable course (`CRS` field → `ee.setCourse`); tspan runs must be space-joined ("HOLD ATRDS"), a value's separate gray unit label attached to it, and a graphic dialog's loose labels collapsed into one row so the entry field isn't buried. Each follow-up raises a MOD needing EXEC. → [a220.md](../a220.md)

## A220-26

- Every A220 LEGS-page action must be addressed by the aircraft's own `legIdx` read from React fibers (`agent.legs()` / `openLegMenuAt` / `deleteLegAt`, which CALL the row's real `onClick`/`onDelete` props), never by ident text + occurrence + geometry — duplicate idents and a list shifting under an edit made text addressing open the wrong waypoint's menu (live 2026-07-30).

## A220-27

- `Row.LegIdx` must be paired to the fiber `legs()` rows by CONTENT via `A220FmsLegParsing.AlignLegIndices` (ident match, disco ↔ placeholder, short lookahead, unmatched → -1), NEVER a positional zip — the fiber walk omits rows the grouper shows (origin, runway leg), so a zip shifted every index by 2 on a live plan (2026-07-30 evening): the disco addressed as a real waypoint (WRONG_ROW refusals) and twin-ident clicks landing on the wrong twin. → [a220.md](../a220.md)

## A220-28

- A discontinuity can SHARE its `legIdx` with a neighbouring waypoint: `legs()` must never dedupe rows by idx alone (only co-located same-idx texts), and same-idx actions must say which row they mean — `deleteLegAt(idx, true)` must REFUSE (`WRONG_ROW`) rather than run a non-placeholder row's `onDelete`. An idx-keyed dedupe shifted every row below a gap by one and DEL on the discontinuity silently deleted real waypoints until the plan collapsed into a ~710 nm direct (live 2026-07-30). → [a220.md](../a220.md)

## A220-29

- The ident node is found STRUCTURALLY (inside the row Input: props have both `onDelete` and `value`), never by x — the track line shares the row's left column and legIdx and is the earlier DOM node. A menu header is ident PLUS navaid detail ("KEA 115.00"), so compare the first token only. Deleting a discontinuity may raise the aircraft's real `SELECT CONSTRAINT` dialog (two identical SELECT buttons — name each by the value on its row); that is a prompt, not a failure. → [a220.md](../a220.md)

## A220-30

- The A220 LEGS revision menu opens ONLY from the row's fix-symbol icon (canvas x=144, `agent.openLegMenu`) — NEVER by clicking the waypoint ident: the ident is a Fusion scratchpad Input whose click COPIES (empty scratchpad) or SUBMITS (text) into the leg, which live-corrupted a flight plan (GENOS-over-RDS, 2026-07-30). The discontinuity delete must ABORT when `armStatus()` never confirms — an un-armed click submits the scratchpad instead of no-opping. → [a220.md](../a220.md)

## A220-31

- Every A220 FMS overlay (waypoint revision menu, dialog, open dropdown) must render MODALLY — one `agent.overlay()` call per poll, detected STRUCTURALLY from the renderer's constants (white-stroke menu / `#232323`+5px dialog / 41-unit transparent-rect dropdown column), with dialogs CLIPPED to their rect via `ClipTo` and Escape dismissing the overlay before the window. Never flatten an open overlay into the page rows — that was the "clunky/messy FMS" report. The FMS's amber rejection box (`#ffe300`, self-clears after 3 s) must be read out; it is the only place the aircraft says WHY an entry was refused. → [a220.md](../a220.md)

## A220-32

- A220 EFB `setField` must dispatch `keyup` in addition to `input`+`change`+`blur` — the EFB binds every Settings save handler (SimBrief ID → DataStore + `SetSimbriefUsername` CommBus push, which the FMS uplink AND loadsheet import both need) and the Takeoff-perf sanitizers to `el.onkeyup`; without the keyup the text sets visibly but never commits. → [a220.md](../a220.md)

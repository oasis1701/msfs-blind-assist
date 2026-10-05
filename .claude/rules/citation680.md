---
paths:
  - "MSFSBlindAssist/Aircraft/Citation680/**"
  - "MSFSBlindAssist/Forms/Citation680/**"
  - "MSFSBlindAssist/Resources/coherent-c680-*.js"
  - "MSFSBlindAssist/Resources/coherent-gtc-agent.js"
  - "tests/MSFSBlindAssist.Tests/**/*C680*.cs"
---
# Skyward Citation Sovereign+ (C680) rules

Loaded with the Sovereign+ code and its Coherent agents. Background: docs/citation680.md and docs/citation680-variables.md. Full text of each rule: docs/invariants/citation680.md.

- [C680-1] Every C680 write is built in ONE place, `C680Commands.For(key, value)`, as the calculator strings the cockpit's own click code runs, guard covers included; a guarded control acts only with its cover open. Full: docs/invariants/citation680.md#c680-1
- [C680-2] The vendor plugin intercepts the generator, APU generator, AP disconnect, autothrottle and pitch-trim stock events: drive the generators through their own input events (`B:ELECTRICAL_Alternator_n_*`, `B:ELECTRICAL_APU_Generator_1_Set`) and read `LINE CONNECTION ON:n` for on line. Full: docs/invariants/citation680.md#c680-2
- [C680-3] Switch positions a data definition cannot read (a `B:` input value, a named circuit) are copied into `L:MSFSBA_C680_*` by `C680SwitchMirror`, one calculator string per continuous-batch cycle. Full: docs/invariants/citation680.md#c680-3
- [C680-4] Every display is HTML on the Coherent debugger and each view accepts ONE inspector socket: each window owns its view's client, and the definition holds the one MFD client the engine strip and the synoptic reader share. Full: docs/invariants/citation680.md#c680-4
- [C680-5] The G3000 refuses every typed speed target in FMS mode: Ctrl+S, the panel entry and the autopilot window switch the speed source to Manual before setting the target, as pressing the speed knob does. Full: docs/invariants/citation680.md#c680-5
- [C680-6] The GTC agent reads only the layer the pilot can touch: liveness is per `.gtc-view` (skip `hidden`, `occlude-hidden`, close animations, and every view but the topmost live popup); rect checks are useless. Full: docs/invariants/citation680.md#c680-6
- [C680-7] GTC labels and rows are built from TEXT NODES, never leaf elements; adjacent text nodes join with no separator while a `<br>` keeps its space. Full: docs/invariants/citation680.md#c680-7
- [C680-8] A flight-plan leg's altitude and FPA/speed boxes are appended to `A._buttons` after every listed button and never listed, the row carrying `{alt=N;spd=M}` (stripped by `C680GtcRows.Parse`); the altitude display's meaning comes from the WT G3000 v2 source, never guessed colours. Full: docs/invariants/citation680.md#c680-8
- [C680-9] A choice button or a state-word toggle reads ", selected" when `toggle-status-bar-on`, only a real toggle reads ", on"/", off" (never "On, on"); a group title is only a `<label>` or a class ending in `title` before the group's first button. Full: docs/invariants/citation680.md#c680-9
- [C680-10] Marks the GTC and EFB agents leave on page elements (`__msfsbaGroupTitle`, `__msfsbaPair`, `scrapeId`) start from a RANDOM value per install: they outlive a re-installed agent, and counting from 0 or 1 hid content. Full: docs/invariants/citation680.md#c680-10
- [C680-11] The EFB agent reads Checklists through its own reader over ONE `.cl-checklist-group`, never the whole-page walk; reading order is by column LEFT EDGE; icon-only buttons are named from `data-tip`/`title`/`aria-label`; `act()` results speak through `C680EfbRows.SpokenAfterAct`. Full: docs/invariants/citation680.md#c680-11
- [C680-12] Output D reads the live FMS on the MFD view (`wtg3000-mfd` → `.fms` → `getPrimaryFlightPlan()`), because the G3000 publishes no destination distance; `C680Destination.Compose` never invents a distance. Full: docs/invariants/citation680.md#c680-12
- [C680-13] The synoptic diagrams are unclassed SVG tspans read by GROUP ID and colour (green `#00BF4A` = powered/running/open); the strip's HYDRAULICS and ELECTRICAL values are read by class, never position. Full: docs/invariants/citation680.md#c680-13
- [C680-14] The CAS monitor over the pilot PFD's list is baseline-first (a reconnect never re-reads the list), queued, toggled by Ctrl+E, with one Ctrl+M row per severity. Full: docs/invariants/citation680.md#c680-14
- [C680-15] Deliberate omissions, never added: cabin animation toys, pictures (maps, radar, charts, tapes), the PAX display and stock EFB, copilot yoke duplicates, controls this avionics does not take by event (bank limit, IAS/Mach units, transponder mode, ADF entry), and lamp-only reversion buttons. Full: docs/invariants/citation680.md#c680-15
- [C680-16] Verify a write through a Coherent view, never the SimConnect MCP's calculator reads (stale or garbage after K-events, a per-client registration cap); names come from the package XML; `C680InteractionSurfaceTests` covers every interaction node. Full: docs/invariants/citation680.md#c680-16
- [C680-17] Walking the touchscreens in flight never presses a choice button, a pane selector, a system test, Nav Source or Bearing, or anything inside a popup; revert a relabelled button by its POSITION, not its label. Full: docs/invariants/citation680.md#c680-17

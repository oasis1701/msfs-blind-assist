---
paths:
  - "MSFSBlindAssist/Resources/coherent-sim-tablet-agent.js"
  - "MSFSBlindAssist/Services/SimTablet*.cs"
  - "MSFSBlindAssist/SimConnect/CoherentPmdgEfbClient.cs"
  - "tests/MSFSBlindAssist.Tests/**/*SimTablet*.cs"
  - "tools/sim-tablet-test/**"
---
# Sim tablet (MSFS 2024's own EFB) rules

Loaded when Claude reads matching code. Background: docs/sim-tablet.md. Full text of each rule: docs/invariants/sim-tablet.md.

- [STAB-1] Never focus a sim tablet text box or send it a mousedown (`coherent-sim-tablet-agent.js`): focus fires `FOCUS_INPUT_FIELD`, which hands the pilot's keyboard to the tablet. Type with `.value` plus an `input` event; `releaseKeyboard` hands back a focus the OS takes by itself after a press. Full: docs/invariants/sim-tablet.md#stab-1
- [STAB-2] A press fires the ONE event the element listens to (`A.press`): `click()` for an onclick control, a lone mousedown for an onmousedown-only one (the Flight Planner's search results); never both, never a pointer sequence. Full: docs/invariants/sim-tablet.md#stab-2
- [STAB-3] Disabled is the OS's `disabled` ATTRIBUTE (on div list items too) or a shown `.disabled-layer`, read by `A.isDisabled`, never `el.disabled` alone; a press on a disabled control is refused (false), never sent. Full: docs/invariants/sim-tablet.md#stab-3
- [STAB-4] The SimBrief import ACTS by structure (`A.route.*`: the Folder-icon Import menu's item at `ENTER_ROUTE_STRING`, the airport steps in order, runways by designator), never by the English labels, which the OS translates; the reader may READ labels. Full: docs/invariants/sim-tablet.md#stab-4
- [STAB-5] The SimBrief import never sends the route to the avionics: Send stays the pilot's own press on the Route page, after reviewing what loaded (the owner's choice, 2026-10-19). Full: docs/invariants/sim-tablet.md#stab-5
- [STAB-6] The import's waits live in `SimTabletSimBriefImport`, one reader call per step, never a timer chain inside the page: the client re-installs the reader on any eval timeout, which would drop a chain mid-import. Full: docs/invariants/sim-tablet.md#stab-6
- [STAB-7] The tablet IGNORES a runway written into the route string ("KSAV/01", measured), so `SimTabletRoute.Build` writes none; the runways are picked afterwards on the airports' runway dropdowns, and the summary says every procedure the tablet then chose by itself. Full: docs/invariants/sim-tablet.md#stab-7
- [STAB-9] The route box opens holding the Flight Planner's current route, and its SAVE stays DISABLED while the text is that same route (measured), so `route.enter` compares the two and closes the box as unchanged, and the import skips SAVE; never retry SAVE on an unchanged route as a failure. Full: docs/invariants/sim-tablet.md#stab-9
- [STAB-10] Never write a speed and level group (`N0112F030`) into the route box: the tablet's own parser takes the level from the SPEED digits (measured: FL112). `SimTabletRoute.Build` drops any, and puts SimBrief's SID right after the origin and its STAR right before the destination, the only places the parser takes them. Full: docs/invariants/sim-tablet.md#stab-10
- [STAB-8] Shift+T opens the sim tablet only for an aircraft whose `UsesSimTablet` is true (default false), as the LAST branch of the tablet hotkey, so an aircraft with a tablet of its own never reaches it; on MSFS 2020 it says the tablet is MSFS 2024 only. Full: docs/invariants/sim-tablet.md#stab-8

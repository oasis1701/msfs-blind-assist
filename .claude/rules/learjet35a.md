---
paths:
  - "MSFSBlindAssist/Aircraft/Learjet35/**"
  - "MSFSBlindAssist/Forms/Learjet35/**"
  - "MSFSBlindAssist/Resources/coherent-gns-agent.js"
  - "MSFSBlindAssist/Services/GpsWaypointSequencer.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Lj35*.cs"
---
# Flysimware Learjet 35A rules

Loaded with the Learjet 35A code. Background: docs/learjet35a.md and docs/learjet35a-variables.md. Full text of each rule: docs/invariants/learjet35a.md.

- [LJ35-1] Every vendor switch is a plain L:var written directly through the calculator path (`L:GENERIC_<node>`, `L:GENERIC_Momentary_<node>` 0/1/2, `L:XMLVAR_<node>_Position`) and read back through a Coherent view; there is no read-only-mirror trap on this aircraft. Full: docs/invariants/learjet35a.md#lj35-1
- [LJ35-2] A control whose instrument JS listens for the H: event (the Davtron clock) is written through its B: input event, `(>B:GENERIC_<node>_Set)`, which fires the same `H:GENERIC_<node>` the cockpit click does. Full: docs/invariants/learjet35a.md#lj35-2
- [LJ35-3] The GNS 530/430 take `H:AS530_<Key>`/`H:AS430_<Key>` sent from inside the page over the GNS window's own Coherent socket; the GTX 345 takes `H:Transponder<Key>` through MobiFlight. Full: docs/invariants/learjet35a.md#lj35-3
- [LJ35-4] The panel tree is the vendor manual's cockpit map: every one of the model's 225 interactive components is a control or a named omission in `Lj35InteractionSurfaceTests.Surface`; no panel is silently empty, no control is in two panels, panel names are unique (`Lj35PanelStructureTests`). Full: docs/invariants/learjet35a.md#lj35-4
- [LJ35-5] Deliberately absent, never add them: the weather radar picture (a WASM canvas), the GTN slots, the decorative L/R breaker panels, anything the manual marks NOT SIMULATED, and copilot duplicates of yoke switches. Full: docs/invariants/learjet35a.md#lj35-5
- [LJ35-6] `IsAnnounced` governs background changes only: every settable switch is Continuous and announced, every numeric readout silent, except the radio frequencies (`Freq`), which announce on change because vPilot retunes COM 1. Full: docs/invariants/learjet35a.md#lj35-6
- [LJ35-7] The annunciator panel is DERIVED by `Lj35AnnunciatorLogic` from `Annunciators.xml`: each lamp is a Continuous pseudo-variable, baseline-first, silent while the annunciator test runs; `Lj35MasterCaution` latches each new cause; TEST-only lamps are not modelled. Full: docs/invariants/learjet35a.md#lj35-7
- [LJ35-8] The GNS agent renders ONE layer (startup, self-test, a dialog or the active page) by DOM structure, the first row is the context, and after a key `Lj35GnsSpeech` speaks what the key DID; on the self-test page only the large right knob and ENT work. Full: docs/invariants/learjet35a.md#lj35-8
- [LJ35-9] On the flight plan page the knob push puts the cursor on the legs, the LARGE knob moves through them, the SMALL knob on a leg opens the waypoint dialog to INSERT before it, and CLR on a leg is the delete prompt; never tell a pilot the small knob scrolls. Full: docs/invariants/learjet35a.md#lj35-9
- [LJ35-10] Typed ident entry (Ctrl+T) calls the instrument's own `AlphaNumInput.setValueFromOS`, found by walking the object graph, so the unit's search runs as for a sighted pilot; `typed()` reads back what it resolved. Full: docs/invariants/learjet35a.md#lj35-10
- [LJ35-11] D, Ctrl+W and Shift+D read the stock GPS SimVars through `LastGpsWaypoint` and `GpsWaypointSequencer`; route distance is `GPS ETE` × ground speed; Shift+D is an ESTIMATE (`Lj35Descent`) and says so; a passing is announced only when the TO fix becomes the FROM fix. Full: docs/invariants/learjet35a.md#lj35-11
- [LJ35-12] `Lj35Speeds` says which kind each number is: computed at weight (AFM stall speeds scaled by the square root of gross weight, read from ONE `TOTAL WEIGHT` key) or a published limitation; never register a second key on that SimVar name. Full: docs/invariants/learjet35a.md#lj35-12
- [LJ35-13] A control's `HelpText` is one line or nothing, and only where it carries a fact the name does not; the long form lives in docs/learjet35a.md. Full: docs/invariants/learjet35a.md#lj35-13
- [LJ35-14] The generator lives on the starter switch (Generator / Off / Start); the thrust lever's idle is 0 percent in this model; Bleed Air Circuit 43 is exposed because nothing in the cockpit drives it and pressurization fails without it. Full: docs/invariants/learjet35a.md#lj35-14

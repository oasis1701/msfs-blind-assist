# Flysimware Learjet 35A rules — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/learjet35a.md`, which Claude Code loads when it reads matching code. Background: [learjet35a.md](../learjet35a.md). The text is taken from that document.

## LJ35-1

- **Every vendor switch is a plain L:var and it is written directly.** `L:GENERIC_<node>` for a two-position switch, `L:GENERIC_Momentary_<node>` 0/1/2 for a three-position one, `L:XMLVAR_<node>_Position` for a rotary selector — written through the calculator path, read back through a Coherent view. Measured 2026-09-07: the write sticks AND the downstream effect follows (bus volts, pump active, circuit on, engine light-off). There is no read-only-mirror trap here; the vendor's own hardware-binding document names these L:vars as the interface. Reads during development are verified through a Coherent view, never the MCP's MobiFlight read path. → [learjet35a.md](../learjet35a.md)

## LJ35-2

- **A control whose instrument JS listens for the H: event is written through the B: input event.** `(>B:GENERIC_<node>_Set)` fires the same `H:GENERIC_<node>` the cockpit click does. The Davtron clock is the case; everything else is happy with the L:var. → [learjet35a.md](../learjet35a.md)

## LJ35-3

- **The Coherent instruments take H: events over their own socket.** The GNS 530 and 430 answer `H:AS530_<Key>` / `H:AS430_<Key>` (the Working Title `InteractionEventMap` names) sent as `SimVar.SetSimVarValue('H:…','number',1)` from inside the page — measured: ENT advanced the self-test screen. The GNS windows own that socket and send the bezel keys through it. The GTX 345 answers `H:Transponder<Key>`, sent through MobiFlight. → [learjet35a.md](../learjet35a.md)

## LJ35-4

- The panel tree is **the vendor manual's own cockpit map** (LEARJET_35A_MSFS_MANUAL pages 1–2) — 34 panels. Every one of the model's 225 interactive components is either a control or a named omission in `Lj35InteractionSurfaceTests.Surface`. **No panel may be silently empty**, **no control appears in two panels**, and **panel names are unique** — all pinned by `Lj35PanelStructureTests`. → [learjet35a.md](../learjet35a.md)

## LJ35-5

- Deliberately absent, so nobody looks for them: **the weather radar picture** (`MapViewWasmModule.wasm` drawing to a `wasm-sim-canvas`, no DOM; its range, mode and test knobs ARE on the GNS 530 panel); **the PMS50 GTN 750/650 and TDS GTNXi slots** (not installed on the reference machine; the tablet's GPS-unit selector is exposed so a later GTN window only needs adding); **the L and R circuit-breaker panels** (decorative — no breaker variables exist; the three tie breakers that do are on Engine Start); **anything the manual marks NOT SIMULATED** (auxiliary heat, the manual cabin pressure valve, oxygen, the VG erect switches, the AP SFT mode, the copilot audio panel; the spoileron reset is exposed, labelled as such); **copilot duplicates of yoke switches** and the copilot's annunciator test button; and **a First Officer profile** (not in this pass). → [learjet35a.md](../learjet35a.md)

## LJ35-6

- `IsAnnounced` governs BACKGROUND changes only (a switch thrown in the cockpit, by hardware or by a failure); MSFSBA's own combo sets are covered by the global `_uiSetEcho` wrap. So: **every settable switch is Continuous + announced**, **every numeric readout is silent** and read from the panel scan or a hotkey, and the derived annunciator lamps announce on lighting and clearing. The one exception is the RADIO FREQUENCIES (COM 1/2, NAV 1/2, both ADFs — the `Freq` helper): announced on change, because vPilot retunes COM 1 when a controller is picked and a pilot who cannot see the radio must hear that. COM 1 and NAV 1 were cached-silent until 2026-09-09, when a pilot reported hearing COM 2 change under vPilot but never COM 1. The GNS window's radio knob therefore speaks only the key it pressed, so a settled frequency is heard once. → [learjet35a.md](../learjet35a.md)

## LJ35-7

- The annunciator panel is DERIVED: `Lj35AnnunciatorLogic` transcribes each lamp's condition from `Annunciators.xml` as a function of cached variables, and the definition evaluates the lamps that read a variable whenever that variable is delivered. Each lamp is a Continuous pseudo-variable so it has its own Ctrl+M row; nothing announces while the annunciator test is running, and nothing announces until every input of a lamp has been read once (baseline-first). The master caution (`Lj35MasterCaution`, from `Alerts.xml`) latches each new cause and the reset button acknowledges the latched set. Lamps the vendor only lights under TEST (VG, ALC, FILTER, CUR, BAT 140/160, ENG CHIP, WSHLD OVHEAT, STAB, WING HEAT, AIU) are not modelled. → [learjet35a.md](../learjet35a.md)

## LJ35-8

- **GNS 530** (Alt+N or Alt+M) and **GNS 430** (Alt+P): `Forms/Learjet35/Lj35GnsDisplayForm`, reading the view with the GNS agent (`Resources/coherent-gns-agent.js`) and sending the bezel over the same socket. The agent renders ONE layer — startup, self-test, a dialog, or the active page — by DOM structure, then the radios and the status footer as fixed lines; the first row is always the context. The generic row-clustering agent it replaced read the self-test page and the NAV page drawn beneath it as one soup, stitched the radio pane into every row, and showed Garmin's private unit glyphs as "�" (`GNSNumberUnitDisplay.getUnitChar` in WT530B.js is the table). After a key the window speaks what the key DID (`Lj35GnsSpeech` over the agent's `state()` string).

  Measured live 2026-09-08: on the self-test page ONLY the large right knob (moves the highlight to "OK?") and ENT do anything — the default highlight sits on "Go To Checklists?", where ENT is a no-op; an earlier "two Ctrl+Enter reach NAV" note was wrong. FPL, VNAV and PROC are DETACHED page groups in Working Title's implementation: inside them the large knob turns no page, and CLR (or the same button again) is the way back. → [learjet35a.md](../learjet35a.md)

## LJ35-9

- **The flight plan page's knobs are not the list's knobs** (read out of `FPLPage` / `FPLEntry` and confirmed live): the knob push puts the cursor on the legs, the LARGE knob moves through them, the SMALL knob on a leg opens the waypoint-entry dialog to INSERT before it (`handleInnerKnobScroll` → `ViewService.getWaypoint()`), and CLR on a leg is the delete prompt. The list is fully in the DOM, so the window reads every leg and the cursor line follows the highlighted one. An earlier version told pilots the small knob scrolls, which on this page starts an insert. → [learjet35a.md](../learjet35a.md)

## LJ35-10

- **Typed ident entry, Ctrl+T in the GNS window.** The agent's `typeIdent` finds the `AlphaNumInput` component behind the visible ident field (a walk of the instrument's object graph from its main screen — FSComponent attaches no instance to the DOM) and calls its own `setValueFromOS`, the path the sim's on-screen keyboard uses, so the unit's search and facility lookup run exactly as for a sighted pilot; `typed()` then reads back what the unit resolved. Ctrl+Enter confirms, as with the knobs. → [learjet35a.md](../learjet35a.md)

## LJ35-11

- **The GNS flight plan on the hotkeys.** D, Ctrl+W and Shift+D read the stock GPS SimVars the Working Title unit writes, through the one-second `SimConnectManager.LastGpsWaypoint` frame and `Services/GpsWaypointSequencer`. Route distance to the destination is never published; it is recovered from `GPS ETE × ground speed`, exactly. The GNS has no top-of-descent point at all, so Shift+D is an ESTIMATE (`Lj35Descent`): three degrees to the GNS target when there is one, else to 1,500 ft, and the readout says which. V is the aeroplane's own vertical speed (the definition deliberately does not handle it); Shift+V is the FC-530 target.
- **Waypoint passing call.** "Passing SOXOM. Next VEKIN, 18 miles." through `OnGpsWaypointReceived` and `GpsWaypointSequencer`, which announces a passing ONLY when the fix flown TO has become the fix flown FROM — a Direct-To, a plan edit or a procedure load is not one. One Ctrl+M row ("Waypoint Passing Call") mutes it; the row rides `GPS IS ACTIVE FLIGHT PLAN`, a SimVar name no other Learjet key carries (the batch sorts by name). → [learjet35a.md](../learjet35a.md)

## LJ35-12

- **Characteristic speeds (Shift+1..6, `Lj35Speeds`).** Two kinds of number, and each readout says which. COMPUTED AT WEIGHT: the flight model's AFM stall speeds at 18,300 lb scaled by the square root of the live gross weight (the Tablet panel's `TOTAL WEIGHT` key — ONE registration; a second key on the same SimVar name would shift every later batch slot), Vref as 1.3 × Vs0. Flaps 8 and 20 stall speeds are ESTIMATED factors and takeoff speeds the FAR 25 minima; those readouts say so. PUBLISHED LIMITATIONS: Vmo 300, Mmo 0.81, Vfe 200/200/150, Vlo 200, Vle 260 — the owner accepted them as the 35A's on 2026-09-09; correct them in `Lj35Speeds` if the AFM says otherwise. → [learjet35a.md](../learjet35a.md)

## LJ35-13

- A control's `HelpText` earns its place only by carrying a fact the name does not — the starter dropping out at 45 % N2 with the fuel computer on, the GPU clearing itself when a generator comes on line. The long form lives in the document. → [learjet35a.md](../learjet35a.md)

## LJ35-14

- **The generator lives on the same switch as the starter.** With the fuel computer ON the starter drops out itself at 45 % N2, with it OFF the pilot moves the switch to GEN. Measured live 2026-09-07: setting the cut-off switch to Run with the thrust lever at 0 % — the lever's idle position in this model — lit the engine; the generator came on line only when the switch was moved to Generator.
- **Bleed Air Circuit** on the Pressurization panel is systems.cfg circuit 43 (`AIR_BL`, left essential bus). Nothing in the vendor's cockpit drives it, and `Pressurization.xml` takes its "no pressurization" branch whenever it is unpowered — measured live 2026-09-08 at FL350. The switch is exposed so it can be seen and set (a conditional `ELECTRICAL_CIRCUIT_TOGGLE`). → [learjet35a.md](../learjet35a.md)

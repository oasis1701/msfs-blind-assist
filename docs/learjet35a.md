# Flysimware Learjet 35A

MSFSBA's first business jet. One definition (`Aircraft/Learjet35/FlysimwareLearjet35ADefinition.*`,
`partial` across one file per vendor panel group), selected from the Aircraft menu
(code `FLYSIMWARE_LJ35A`).

The guiding rule is the one every other supported aircraft follows: **a blind pilot gets this
aeroplane at the depth a sighted pilot gets it.** Nothing is simplified away, nothing is
auto-completed, and MSFSBA reports rather than decides. The variable-level measurements live in
[learjet35a-variables.md](learjet35a-variables.md); the hotkeys in
`HotkeyGuides/Flysimware_LJ35A_Hotkeys.txt`.

## Three transport rules

1. **Every vendor switch is a plain L:var and it is written directly.** `L:GENERIC_<node>` for a
   two-position switch, `L:GENERIC_Momentary_<node>` 0/1/2 for a three-position one,
   `L:XMLVAR_<node>_Position` for a rotary selector — written through the calculator path, read
   back through a Coherent view. Measured 2026-09-07: the write sticks AND the downstream effect
   follows (bus volts, pump active, circuit on, engine light-off). There is no read-only-mirror
   trap here; the vendor's own hardware-binding document names these L:vars as the interface.
2. **A control whose instrument JS listens for the H: event is written through the B: input
   event.** `(>B:GENERIC_<node>_Set)` fires the same `H:GENERIC_<node>` the cockpit click does.
   The Davtron clock is the case; everything else is happy with the L:var.
3. **The Coherent instruments take H: events over their own socket.** The GNS 530 and 430
   answer `H:AS530_<Key>` / `H:AS430_<Key>` (the Working Title `InteractionEventMap` names) sent
   as `SimVar.SetSimVarValue('H:…','number',1)` from inside the page — measured: ENT advanced
   the self-test screen. The GNS windows own that socket and send the bezel keys through it.
   The GTX 345 answers `H:Transponder<Key>`, sent through MobiFlight.

## What gets a panel, and what does not

The panel tree is **the vendor manual's own cockpit map** (LEARJET_35A_MSFS_MANUAL pages 1–2):
Glareshield, Pilot Panel, Copilot Panel, Engine Panel, Navigation Panel, the two sidewalls,
Audio, Anti-Ice and Fuel Computer Panel, Start Panel, Test Panel, Lower Center Panel,
Pressurization Panel, Climate and Lights Panel, Throttle Quadrant, Fuel System, Center
Pedestal, Yoke, EFB Tablet, Cabin and Ground, and Simulation for the tablet's options —
34 panels. Every one of the model's 225 interactive components is either a control or a
named omission in `Lj35InteractionSurfaceTests.Surface`.

Deliberately absent, so nobody looks for them:

- **The weather radar picture.** `VCockpit14 - WasmInstrument` is `MapViewWasmModule.wasm`
  drawing to a `wasm-sim-canvas`; there is no DOM to read. Its range, mode and test knobs ARE
  on the GNS 530 panel.
- **The PMS50 GTN 750/650 and TDS GTNXi slots.** Neither is installed on the reference machine;
  the views are placeholders. The tablet's GPS-unit selector is exposed so a later GTN window
  only needs adding.
- **The L and R circuit-breaker panels.** They are decorative in this model — no breaker
  variables exist. The three tie breakers that do exist are on Engine Start.
- **Anything the manual marks NOT SIMULATED**: auxiliary heat, the manual cabin pressure
  valve, oxygen, the VG erect switches, the spoileron reset (exposed, labelled as such), the
  AP SFT mode (lamp only), the copilot audio panel.
- **Copilot duplicates of yoke switches** (MSW, pitch sync, manoeuvre) and the copilot's
  annunciator test button: same function, one control.
- **A First Officer profile** (not in this pass).

**No panel may be silently empty**, **no control appears in two panels**, and **panel names are
unique** — all pinned by `Lj35PanelStructureTests`.

## Announcements

`IsAnnounced` governs BACKGROUND changes only (a switch thrown in the cockpit, by hardware or by
a failure); MSFSBA's own combo sets are covered by the global `_uiSetEcho` wrap. So: **every
settable switch is Continuous + announced**, **every numeric readout is silent** and read from the
panel scan or a hotkey, and the derived annunciator lamps announce on lighting and clearing.
The one exception is the RADIO FREQUENCIES (COM 1/2, NAV 1/2, both ADFs — the `Freq` helper):
announced on change, because vPilot retunes COM 1 when a controller is picked and a pilot
who cannot see the radio must hear that. COM 1 and NAV 1 were cached-silent until
2026-09-09, when a pilot reported hearing COM 2 change under vPilot but never COM 1. The
GNS window's radio knob therefore speaks only the key it pressed, so a settled frequency
is heard once.

The annunciator panel is DERIVED: `Lj35AnnunciatorLogic` transcribes each lamp's condition from
`Annunciators.xml` as a function of cached variables, and the definition evaluates the lamps
that read a variable whenever that variable is delivered. Each lamp is a Continuous pseudo-
variable so it has its own Ctrl+M row; nothing announces while the annunciator test is running,
and nothing announces until every input of a lamp has been read once (baseline-first). The
master caution (`Lj35MasterCaution`, from `Alerts.xml`) latches each new cause and the reset
button acknowledges the latched set. Lamps the vendor only lights under TEST (VG, ALC, FILTER,
CUR, BAT 140/160, ENG CHIP, WSHLD OVHEAT, STAB, WING HEAT, AIU) are not modelled.

## Help text is one line, or nothing

A control's `HelpText` earns its place only by carrying a fact the name does not — the starter
dropping out at 45 % N2 with the fuel computer on, the GPU clearing itself when a generator
comes on line. The long form lives in this document.

## Windows

- **GNS 530** (Alt+N or Alt+M) and **GNS 430** (Alt+P): `Forms/Learjet35/Lj35GnsDisplayForm`,
  reading the view with the GNS agent (`Resources/coherent-gns-agent.js`) and sending the
  bezel over the same socket. The agent renders ONE layer — startup, self-test, a dialog,
  or the active page — by DOM structure, then the radios and the status footer as fixed
  lines; the first row is always the context ("NAV page 1 of 5, Navigation", "Direct To,
  select waypoint", "Self-test. Turn the large knob to OK? and press Enter"). The generic
  row-clustering agent it replaced read the self-test page and the NAV page drawn beneath
  it as one soup, stitched the radio pane into every row, and showed Garmin's private
  unit glyphs as "�" (they are Latin-1 letters the instrument font draws as ligatures —
  `GNSNumberUnitDisplay.getUnitChar` in WT530B.js is the table). After a key the window
  speaks what the key DID (`Lj35GnsSpeech` over the agent's `state()` string): the page
  or dialog now on top, the row the cursor landed on, the character an ident entry shows
  under the cursor, or the standby frequency after a radio knob.

  **Two facts about the unit, both measured live 2026-09-08, that the window's help text
  now carries.** On the self-test page ONLY the large right knob (moves the highlight to
  "OK?") and ENT do anything — FPL, PROC, the page groups and the small knob are all
  swallowed by the self-test's own control, and the default highlight sits on "Go To
  Checklists?", where ENT is a no-op. An earlier note here said "two Ctrl+Enter reach
  NAV"; it was wrong, and it is why the bezel looked dead. And FPL, VNAV and PROC are
  DETACHED page groups in Working Title's implementation: inside them the large knob turns
  no page, and CLR (or the same button again) is the way back.
- **The GNS flight plan on the hotkeys.** D (distance and time to the destination), Ctrl+W
  (the TO-waypoint: ident, distance, bearing, time) and Shift+D (top of descent) read the
  stock GPS SimVars the Working Title unit writes, through the one-second
  `SimConnectManager.LastGpsWaypoint` frame and `Services/GpsWaypointSequencer` (ported from
  the DA40 work — same Garmin SDK `GpsSynchronizer` underneath). Route distance to the
  destination is never published; it is recovered from `GPS ETE × ground speed`, exactly.
  ⚠️ The GNS has no top-of-descent point at all (its VNAV page is a vertical-speed-required
  calculator, and `GPS TARGET ALTITUDE` is written only while that target is armed — measured
  zero at FL350 with a 30-waypoint plan), so Shift+D is an ESTIMATE (`Lj35Descent`): three
  degrees to the GNS target when there is one, else to 1,500 ft, and the readout says which.
  V is the aeroplane's own vertical speed (MainForm's generic readout — the definition
  deliberately does not handle it); Shift+V is the FC-530 target.
- **Waypoint passing call.** The same GPS frame drives "Passing SOXOM. Next VEKIN, 18 miles."
  through `OnGpsWaypointReceived` (an `IAircraftDefinition` hook MainForm forwards for every
  aircraft) and `GpsWaypointSequencer`, which announces a passing ONLY when the fix flown TO
  has become the fix flown FROM — a Direct-To, a plan edit or a procedure load is not one.
  One Ctrl+M row ("Waypoint Passing Call") mutes it; the row rides `GPS IS ACTIVE FLIGHT
  PLAN`, a SimVar name no other Learjet key carries (the batch sorts by name). On a SID or
  STAR the unit leaves the idents blank, so only enroute fixes are named today.
- **Characteristic speeds (Shift+1..6, `Lj35Speeds`).** Two kinds of number, and each
  readout says which. COMPUTED AT WEIGHT: the flight model's AFM stall speeds at 18,300 lb
  (clean 129, full flap 105 — `[REFERENCE SPEEDS]`, marked "AFM DATA" by the vendor) scaled
  by the square root of the live gross weight (the Tablet panel's `TOTAL WEIGHT` key — ONE
  registration; a second key on the same SimVar name would shift every later batch slot),
  Vref as 1.3 × Vs0 — 124 kt at 15,000 lb and 111 kt at 12,000 lb, within a knot of the
  real 35A Vref chart. Flaps 8 and 20
  stall speeds are ESTIMATED factors (0.94 × clean, 1.06 × full) and takeoff speeds the FAR 25
  minima (1.10 / 1.20 × Vs flaps 8); those readouts say so. PUBLISHED LIMITATIONS, not
  derivable from anything on this machine: Vmo 300, Mmo 0.81, Vfe 200/200/150, Vlo 200,
  Vle 260 — the owner accepted them as the 35A's on 2026-09-09; correct them in `Lj35Speeds`
  if the AFM says otherwise.
- **The flight plan page's knobs are not the list's knobs** (read out of `FPLPage` /
  `FPLEntry` and confirmed live, at the cost of a "REMOVE WAYPOINT, Yes?" prompt that had to
  be backed out of): the knob push puts the cursor on the legs, the LARGE knob moves through
  them, the SMALL knob on a leg opens the waypoint-entry dialog to INSERT before it
  (`handleInnerKnobScroll` → `ViewService.getWaypoint()`), and CLR on a leg is the delete
  prompt. The list is fully in the DOM (26 legs in a 6-row scroll container), so the window
  reads every leg and the cursor line follows the highlighted one. The guide says so; an
  earlier version told pilots the small knob scrolls, which on this page starts an insert.
- **Typed ident entry, Ctrl+T in the GNS window.** The agent's `typeIdent` finds the
  `AlphaNumInput` component behind the visible ident field (a walk of the instrument's
  object graph from its main screen — FSComponent attaches no instance to the DOM) and
  calls its own `setValueFromOS`, the path the sim's on-screen keyboard uses, so the unit's
  search and facility lookup run exactly as for a sighted pilot; `typed()` then reads back
  what the unit resolved. Ctrl+Enter confirms, as with the knobs. Thirty knob clicks per
  ident was the alternative.
- **Bleed Air Circuit** on the Pressurization panel is systems.cfg circuit 43 (`AIR_BL`, left
  essential bus). Nothing in the vendor's cockpit drives it, and `Pressurization.xml` takes
  its "no pressurization" branch whenever it is unpowered — measured live 2026-09-08 at
  FL350: both starter-generator switches left at Off after start, batteries at 12.4 V, the
  left essential bus dead, the circuit switch off, the cabin following the aircraft up, and
  the cabin altitude horn. The switch is exposed so it can be seen and set (a conditional
  `ELECTRICAL_CIRCUIT_TOGGLE`); the readout also shows off while the bus is dead.
- **The tablet** is exposed as panels (Tablet, Cabin, Ground Equipment, Payload, Aircraft
  Options) because every tablet control is a plain L:var; a scraped tablet window is a
  follow-up.

## Panels

### Start Panel → Engine Start

Batteries 1 and 2, the emergency battery (Emergency / Standby / Off), the GPU, both inverters,
the three tie breakers, both starter-generator switches (Generator / Off / Start) and both
air-start ignition switches, with a scan of every bus, battery and generator voltage, the
starter, ignition and combustion state, N2 and ITT per engine.

**The generator lives on the same switch as the starter.** The manual's start procedure: fuel
computers on, thrust levers at idle cut-off, starter switch to START, at 10 % N1 move the lever
to idle; with the fuel computer ON the starter drops out itself at 45 % N2, with it OFF the
pilot moves the switch to GEN.

**Measured live 2026-09-07 (left engine, GPU on, fuel computer on):** with the cut-off switch at
Cut-off and the starter at Start, N2 cranks up (starter on within 5 s; an earlier run held at
a 25 % starter-only ceiling with no light-off). Setting the cut-off switch to Run with the
thrust lever at 0 % — the lever's idle position in this model — lit the engine: N2 6.6 → 52 %
within 12 s, ITT peaking about 645 °C, fuel flow 149 pph at idle. The stock starter flag
dropped to 0 by itself while the switch L:var stayed at Start; the generator came on line
(MASTER ALTERNATOR 1, 28 V) only when the switch was moved to Generator. Going back to
Cut-off shuts a running engine down. The GPU drops when the generator comes on line.

### Glareshield → FC-530 Autopilot

Every mode is the stock autopilot underneath (FC530.xml fires stock K: events from the bezel),
so each mode is a combo over the stock state and the router fires the vendor's own callback
code when the pick differs — ALT SEL, ALT HLD and SPD carry extra logic (the preselect arm,
the capture altitude, the IAS/Mach flip) and are transcribed verbatim. Measured: heading bug,
heading hold, V/S target and the alerter all take and read back; the AP master circuit switch
closes circuits 24 and 61.

### Throttle Quadrant → Thrust and Flaps

Thrust levers as typed percentages (both, or each), the two fuel cut-off gates, flaps as a
four-position combo (measured: `FLAPS HANDLE INDEX` 0–3 = Up/8/20/40, `FLAPS_2` gave 20°),
spoilers, parking brake and the emergency brake lever.

### Everything else

The other panels follow the vendor manual's sections one for one; their controls and readouts
are listed in [learjet35a-variables.md](learjet35a-variables.md).

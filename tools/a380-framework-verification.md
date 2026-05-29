# A380X Framework Verification

Precise map of five framework mechanisms in MSFS Blind Assist, with file:line
citations, used to verify the FlyByWire A380X (`FlyByWireA380Definition.cs`,
code `FBW_A380`) inherits them and to identify gaps.

Paths are relative to `MSFSBlindAssist/`. All line numbers as of this audit.

---

## 1. Background variable monitor + auto-announce enable/disable

### The continuous-monitor + auto-announce loop (framework-level, automatic)

A variable is auto-monitored and auto-announced iff its `SimVarDefinition` has
`UpdateFrequency = UpdateFrequency.Continuous` **and** `IsAnnounced = true`.
Nothing else (no per-aircraft wiring) is required.

- **Batch registration** — `SimConnect/SimConnectManager.cs:StartContinuousMonitoring()`
  (begins ~`:755`). It pulls `CurrentAircraft.GetVariables()` (`:776`) and selects
  every var with `UpdateFrequency.Continuous && IsAnnounced` (`:781-782`), skipping
  `PMDGVar` (`:785`). They are sorted by full SimVar name (`:792-798`) and packed into
  up to 5 `GenericBatch` structs of 100 doubles, registered with
  `SIMCONNECT_PERIOD.SECOND`. This is keyed entirely off the current aircraft's
  dictionary — **any** aircraft definition gets it.
- **Change detection + event fire** — `SimConnectManager.ProcessContinuousBatchImpl<T>()`
  (`:2248`). Reads each field by direct pointer (`:2301`), compares to
  `lastVariableValues` with 0.001 tolerance (`:2369-2373`), and on change fires
  `SimVarUpdated` with a formatted description (`:2401-2406`). (Special A320-only
  ECAM EWD decode at `:2312`, gated on the `A32NX_Ewd_LOWER_` prefix — does not
  affect other aircraft.)
- **Announce** — `MainForm.OnSimVarUpdated()` (`:475`). After aircraft-specific
  `ProcessSimVarUpdate` (`:512`) and panel/control updates, Step 6 (`:561-602`)
  re-checks `varDef.IsAnnounced && UpdateFrequency.Continuous` (`:566`) and routes
  through `simVarMonitor.ProcessUpdate(...)` (`:600`).
- **Change-detection / announce gate** — `SimConnect/SimVarMonitor.cs`. `ProcessUpdate`
  (`:14`) dedupes against `previousValues` (`:16-17`) and only raises `ValueChanged`
  when `AnnouncementsEnabled` (`:26`). `MainForm` wires `simVarMonitor.ValueChanged
  += OnSimVarValueChanged` (`:223`) and enables announcements after a 5 s grace
  period (`:420`, and again on aircraft swap `:3548`).

**A380 verdict — gets it free.** The A380 marks status vars via the local `Mon(...)`
helper (`FlyByWireA380Definition.cs:114-125`, sets `Continuous` + `IsAnnounced=true`):
e.g. Master Warning/Caution/Autoland (`:359-364`), Engine state 1-4 (`:419`), engine
& APU fire (`:273-278`), icing (`:266`), FMGC flight phase (`:473`). Base-class
universals (ground state, altitude, glideslope, elevator trim) come from
`BaseAircraftDefinition.GetBaseVariables()` (`:45-175`) which the A380's
`GetVariables()` merges in via `GetBaseVariables()` (`FlyByWireA380Definition.cs:62`).
No gap.

### User toggle to enable/disable background announcements

There is **no single global announce on/off** hotkey. The toggles are:

1. **Trim announcements — global, all aircraft.** `HotkeyAction.ToggleTrimAnnouncements`,
   handled in `BaseAircraftDefinition.HandleHotkeyAction` (`:242-249`), flips the
   base `_trimAnnouncementsEnabled` field which suppresses the trim callout in
   `BaseAircraftDefinition.ProcessSimVarUpdate` (`:515`). Hotkey: **Shift+T**,
   registered globally at `HotkeyManager.cs:678`. The A380 inherits this (it calls
   `base.HandleHotkeyAction` in its default branch, `FlyByWireA380Definition.cs:1642`).
2. **ECAM monitoring toggle — A320 only.** `HotkeyAction.ToggleECAMMonitoring`
   (**Ctrl+E**, `HotkeyManager.cs:680`). Handled in
   `FlyByWireA320Definition.HandleHotkeyAction` (`:3663`) →
   `ToggleA320ECAMMonitoring` → `simConnect.ToggleECAMMonitoring()` (A320 `:3887`).
   The A380 does NOT handle this case (its `HandleHotkeyAction` switch,
   `FlyByWireA380Definition.cs:1623-1643`, has no `ToggleECAMMonitoring`).
3. **Per-category / per-variable monitor manager — Fenix and PMDG only.**
   `HotkeyAction.MonitorManager` (**Ctrl+M**, `HotkeyManager.cs:681`). Routed via
   each aircraft's `HandleHotkeyAction`:
   - PMDG → `PMDG777Definition.cs:6315` → `MainForm.ShowPMDGAnnouncementMonitorDialog()`
     (`MainForm.cs:1963`), backed by `PMDGAnnouncementMonitorForm`.
   - Fenix → `FenixA320Definition.cs:13185` → `MainForm.ShowFenixMonitorManagerDialog()`
     (`MainForm.cs:1905`), backed by `FenixMonitorManagerForm`.
   The disabled set is persisted in `Settings/UserSettings.cs`:
   `FenixDisabledMonitorVariables` (`:132`) and `PMDGDisabledMonitorVariables`
   (`:140`). It is consulted in the announce loop at
   `MainForm.OnSimVarUpdated` `:569-573` (Fenix) and `:579-583` (PMDG) — these are
   the only per-category mutes, and they are gated by `AircraftCode`. **The A320 and
   A380 have no entry here**, so they cannot disable individual background
   announcements at all.

**A380 verdict — gap.** A380 has only the global Shift+T trim toggle. It has neither
the ECAM-monitoring toggle (Ctrl+E) nor a Monitor Manager (Ctrl+M) per-variable
mute. There is no global "all announcements off" anywhere in the codebase.

---

## 2. Panel display (read-only) rendering

Framework-level, automatic for any aircraft returning `GetPanelDisplayVariables()`.

- **Rendering** — `MainForm.cs:4886-4970` (inside the panel-build routine). When the
  current panel has display vars (`:4887`), it appends a "Status Display:" label
  (`:4898-4899`), a **read-only** multiline `TextBox` (`displayTextBox.ReadOnly =
  true`, `:4912`) and a Refresh button (`:4919-4962`), stored as `currentControls
  ["_DISPLAY_"]` (`:4969`).
- **Content formatting (one variable per line)** — `MainForm.UpdateDisplayText()`
  (`:1156-1224`). Iterates the panel's display vars (`:1163`), formats each as
  `"{DisplayName}: {value}"` (`:1213`, or `": --"` when not yet received `:1217`),
  with unit-aware numeric formatting incl. `volts` (`:1187-1190`), and joins with
  `\r\n` — one per line (`:1222`). This is the "battery voltage in ELEC" pattern.
- **Silent live update** — `MainForm.OnSimVarUpdated` Step 3 (`:526-549`) stores
  display values and refreshes the textbox without announcing.

This is driven entirely by `currentAircraft.GetPanelDisplayVariables()` — no
aircraft-specific gating in the rendering path. (The only aircraft-conditional block
nearby is PMDG control pre-population, `MainForm.cs:4974-4989`, which does not affect
display rendering.)

**A380 verdict — gets it free, and uses it.** `FlyByWireA380Definition.GetPanelDisplayVariables()`
(`:1254`) returns read-only readouts per panel, e.g. ELEC with `A32NX_ELEC_BAT_{n}_POTENTIAL`
battery voltages plus faults/bus-powered flags (`:1259-1276`), APU (`:1278-1282`), and
more. These render as read-only "DisplayName: value" lines automatically. No gap.

---

## 3. Combo-box / control auto-update on SimVar change

Framework-level; works for the A380 automatically.

- **Entry point** — `MainForm.OnSimVarUpdated` Step 4 calls
  `UpdateControlFromSimVar(e.VarName, e.Value)` (`:552`). (Also called when
  ProcessSimVarUpdate handled the var, via the lighter `UpdateButtonStateFromStateVariable`
  path at `:522`, and on PMDG panel population `:4988`.)
- **`UpdateControlFromSimVar`** — `MainForm.cs:1002-1053`. Looks up `currentControls
  [varName]` (`:1004`). For a `ComboBox` (`:1011`) it maps the incoming value through
  `varDef.ValueDescriptions` (`:1017`) to a display string and sets `SelectedIndex`
  to the matching item if different (`:1020-1024`) — under an `updatingFromSim` guard
  (`:1008/:1048`) so the change doesn't echo back as user input. For a `Button`
  (`:1028`) it relabels from `ValueDescriptions` (`:1039-1044`).
- **StateVariable feed** — `UpdateButtonStateFromStateVariable` (`:1060-1075`) scans
  all current controls and, for any button whose `varDef.StateVariable == varName`
  (`:1067`), relabels it On/Off (`:1069`). This is how `GetButtonStateMapping()` /
  `StateVariable` feed the UI.
- **Button-press state announcement** (separate, button feedback path) —
  `HandleButtonStateAnnouncement` (`:1127`) looks up `GetButtonStateMapping()`
  (`:1130`) after a hotkey/button event and requests + announces the mapped state
  var; the response comes back through the same `OnSimVarUpdated` pipeline (Step 5,
  `:554-559`).

All keyed off `currentAircraft.GetVariables()` and `ValueDescriptions` /
`StateVariable` — no aircraft-specific code.

**A380 verdict — gets it free.** The A380's combos carry `ValueDescriptions`
(via `Sel`/`OnOff`/`OffAuto`/`ReadEnum`, e.g. `:66-112`) and its
`GetButtonStateMapping()` exists (`:1477`). External or app-driven changes refresh
the panel combos automatically. No gap.

---

## 4. Hotkey system (global + aircraft)

### Registration model — globally registered, never aircraft-gated

`Hotkeys/HotkeyManager.cs` registers a fixed set of Win32 chords via `RegisterHotKey`
at startup (the two mode-activator keys `]`/`[` at `:186/:189`, plus the full
action set ~`:660-700+`). The chords are the SAME for every aircraft — there is **no
per-aircraft hotkey registration**. Each chord calls `TriggerHotkey(HotkeyAction.X)`
(e.g. `:233-373`), raising the `HotkeyTriggered` event.

### Routing — runtime delegation, no switch an aircraft must be added to

`MainForm.OnHotkeyTriggered` (`:1316`):

1. Offline-allowed actions checked (`:1319-1328`); SimConnect guard (`:1331`).
2. **Tier 1 — aircraft first:** `currentAircraft.HandleHotkeyAction(...)` (`:1338`).
   If it returns true, optional button-state announce (`:1341-1357`) then `return`.
3. **Tier 2 — base variable map:** inside `BaseAircraftDefinition.HandleHotkeyAction`
   (`:223-287`) via `GetHotkeyVariableMap()`; also handles the universal
   `ToggleTrimAnnouncements` (`:242`), `ReadLocalTime`/`ReadZuluTime` (`:255-283`).
4. **Tier 3 — MainForm universal fallback:** the big `switch (e.Action)` at
   `:1361-1565` (read SimVars, teleports, window launches, taxi, GSX, displays,
   MCDU/EFB launchers, etc.).

**Consequence for the A380:** because hotkeys are global and routing is by runtime
delegation, the A380 does **not** need to be added to any hotkey registration or
switch to make hotkeys fire. A hotkey reaches the A380 only if (a) the A380's
`HandleHotkeyAction` handles it, or (b) it falls through to a Tier-3 universal case.

### Full HotkeyAction set and where each is handled

Enum: `HotkeyManager.cs:1160-1265`. Classification:

**Universal (Tier 3, MainForm — work for every aircraft incl. A380):**
ReadAltitudeAGL/MSL, ReadAirspeedIndicated/True, ReadGroundSpeed, ReadMachSpeed,
ReadVerticalSpeed, ReadBankAngle, ReadPitch, ReadHeadingMagnetic/True
(`:1363-1395`); RunwayTeleport, GateTeleport, LocationInfo, ReadNearestCity
(`:1396-1407`); SimBriefBriefing, ShowTcasWindow, AnnounceTcasTraffic,
ShowWeatherRadar, ReadOutsideTemperature, ReadSquawkCode (`:1408-1425`);
SelectDestinationRunway, ReadDestinationRunwayDistance, ReadILSGuidance,
ReadWindInfo, ReadNavRadioInfo (`:1426-1440`); ShowMETARReport, ShowChecklist,
ShowElectronicFlightBag (`:1441-1448`); ShowTrackFixWindow, ToggleTakeoffAssist,
ToggleHandFlyMode, ToggleVisualGuidance, ReadTargetFPM, ReadTrackSlot1-5,
DescribeScene (`:1477-1524`); TaxiAssistForm, TaxiStatus, TaxiRepeat, TaxiContinue,
TaxiStop, TaxiWhereAmI, AnnounceGroundTraffic, LandingExitPlanner, ShowAccessGSX,
ReadGsxTooltip (`:1525-1562`).

**Universal base (Tier 2, BaseAircraftDefinition):** ToggleTrimAnnouncements,
ReadLocalTime, ReadZuluTime.

**MCDU / EFB — Tier 3 but AircraftCode-routed inside MainForm:**
- `ShowFenixMCDU` (`:1450-1466`): if PMDG → `ShowPMDG777CDUDialog`; else if
  `FBW_A380` → `ShowFBWA380MCDUDialog`; else → `ShowFenixMCDUDialog`. The single
  "show MCDU" chord is reused across aircraft (comment `:1451-1453`).
- `ShowPMDG777EFB` (`:1467-1476`): if PMDG → `ShowPMDG777EFBDialog`; else if
  `FBW_A380` → `ShowFBWA380EFBDialog`. **A380 IS already wired here.**

**Aircraft-gated (handled in aircraft `HandleHotkeyAction`, fall silent otherwise):**
- FCU set/read: FCUSetHeading/Speed/Altitude/VS, ReadHeading, ReadSpeed,
  ReadAltitude, ReadFCUVerticalSpeedFPA — A320 `:3566-3597`, **A380 `:1625-1640`
  (present)**.
- FCU push/pull + AP toggles: FCUHeadingPush/Pull, FCUAltitudePush/Pull,
  FCUSpeedPush/Pull, FCUVSPush/Pull, FCUSetAutopilot, ToggleAutopilot1/2,
  ToggleApproachMode — via `GetHotkeyVariableMap()` (A320). A380 routes its FCU push
  events as panel buttons but does **not** map these HotkeyActions (see Gap list §5).
- A320-only readouts/windows: ReadFuelQuantity, ReadFuelInfo, ReadWaypointInfo,
  ReadApproachCapability, ReadSpeedGD/S/F/VLS/VS/VFE, ShowPFD,
  ShowNavigationDisplay, ShowECAM, ShowStatusPage, ToggleECAMMonitoring — A320
  `:3600-3665`. **A380 handles none of these.**
- MonitorManager — PMDG `:6315`, Fenix `:13185`; A320/A380 none.
- ReadDisplayPFD/LowerECAM/UpperECAM/ND/ISIS — Fenix display-read actions.

### Where the A380 MCDU/EFB are opened (menu + hotkey) — wiring confirmed

- **MCDU:** hotkey `ShowFenixMCDU` → `MainForm.cs:1458-1460` → `ShowFBWA380MCDUDialog()`
  (`MainForm.cs:2035-2054`), which lazily starts `CoherentDebuggerClient` and shows
  `FBWA380MCDUForm`. Live-read via Coherent GT debugger (no injection).
- **EFB:** hotkey `ShowPMDG777EFB` → `MainForm.cs:1472-1474` → `ShowFBWA380EFBDialog()`
  (`MainForm.cs:2056-2072`), starts `CoherentEFBClient`, shows `FBWA380EFBForm`.
- **Aircraft load/swap wiring:** `FBW_A380` is registered in `LoadAircraftFromCode`
  (`MainForm.cs:150`), starts the Coherent client + EFB bridge on initial load
  (`:188-193`) and on swap (`SwitchAircraft`, `:3672-3682`), and disposes A380 forms
  + Coherent clients on swap (`:3617-3638`). Menu item: `flyByWireA380MenuItem` →
  `FlyByWireA380MenuItem_Click` (`:3498-3501`) → `SwitchAircraft(new
  FlyByWireA380Definition())`; menu check state in `UpdateAircraftMenuItems` (`:3735`).

**A380 verdict — fully wired for hotkeys/MCDU/EFB/FCU/read.** The only place the code
appears is `LoadAircraftFromCode` (`:150`) and the AircraftCode-routed MCDU/EFB/load
blocks — all already present. No additional registration needed for hotkeys to fire.

---

## 5. A320 portable features the A380 lacks

`FlyByWireA320Definition` declares the capability interfaces (`:7-10`):
`ISupportsECAM`, `ISupportsNavigationDisplay`, `ISupportsPFDDisplay`.
`FlyByWireA380Definition` declares only `ISupportsBridgedMCDU`, `ISupportsBridgedEFB`
(`:37-39`). NB: these marker interfaces are currently informational only — no code
path was found that gates behavior on them (routing is by `HandleHotkeyAction` +
`AircraftCode`), so adding a case to `HandleHotkeyAction` is what actually enables a
feature; implementing the matching interface is good-hygiene/forward-compat.

A320 `HandleHotkeyAction` cases the A380 does **not** implement
(A320 `:3563-3666`; A380 switch `:1623-1643`):

| Feature (HotkeyAction) | A320 site | Portable to A380? | A380 needs |
|---|---|---|---|
| ReadFuelQuantity | A320 `:3600` → `RequestFuelQuantity` | Yes | A380 already registers `A32NX_TOTAL_FUEL_QUANTITY` (`:207`); add a request like the variable-system Pattern 3 hotkey reader. |
| ReadFuelInfo (Fuel & Payload window) | A320 `:3648` → `ShowA320FuelPayloadWindow` | Maybe | Needs an A380 fuel/payload form + the 11-tank fuel vars; A320 form is A320-specific. Larger effort. |
| ReadWaypointInfo | A320 `:3604` → `RequestWaypointInfo` | Yes | Stock GPS/FMS waypoint SimVars; portable as a generic readout. |
| ReadApproachCapability | A320 `:3608` → `HandleReadApproachCapability` (`:3825`) | Likely | A380 FMA/approach vars (`A32NX_FCU_APPR_MODE_ACTIVE` already at `:668`); port the readout logic. |
| ReadSpeedGD / S / F / VLS / VS / VFE | A320 `:3613-3635` | Likely | A380 PFD speed-tape L:vars (A32NX-style); confirm var names then port the six `RequestSpeed*` readers. Hotkeys Shift+1..6 already global (`:662-667`). |
| ShowPFD | A320 `:3638` → `ShowA320PFDWindow` | Maybe | Needs an A380 PFD info form + ISupportsPFDDisplay; A320 form is bespoke. |
| ShowNavigationDisplay | A320 `:3643` → `ShowA320NavigationDisplay` | Maybe | Needs A380 ND form + ISupportsNavigationDisplay; A380 has ND mode/range vars (`:375-378`) but no readout form. |
| ShowECAM (Upper/Lower) | A320 `:3653` → `ShowA320ECAMDisplay` | Deferred | Needs ported EWD/SD message-code lookup (see `tools/ecam-display-readout-notes.md`); explicitly out of current A380 scope per the class header (`:28-31`). |
| ShowStatusPage | A320 `:3658` → `ShowA320StatusDisplay` | Deferred | Same ECAM/SD decode dependency. |
| ToggleECAMMonitoring (Ctrl+E) | A320 `:3663` → `ToggleA320ECAMMonitoring` (`:3887`) | Deferred | Depends on the ECAM monitoring/decode pipeline. |
| MonitorManager (Ctrl+M) | (not A320 either) PMDG `:6315` / Fenix `:13185` | Yes | Add an A380 case opening a monitor-manager form + an `A380DisabledMonitorVariables` setting + a mute check in `OnSimVarUpdated` (mirrors `:569-583`). Gives per-variable background-announce mute. |
| FCU push/pull + AP toggles via GetHotkeyVariableMap | A320 map | Yes | A380 already registers the FCU push events as panel buttons (`:627-644`); add a `GetHotkeyVariableMap()` override mapping FCUHeadingPush→`A32NX.FCU_TO_AP_HDG_PUSH`, etc., so the input-mode push/pull/AP chords work. |

Capability interfaces the A380 could additionally declare once the matching forms
exist: `ISupportsPFDDisplay`, `ISupportsNavigationDisplay`, `ISupportsECAM`
(`Aircraft/IAircraftCapabilities.cs:8-29`).

---

## Documented conventions & docs status

`CLAUDE.md` documents the add-aircraft recipe ("Adding New Aircraft": create class
inheriting `BaseAircraftDefinition`; override `GetVariables`/`GetPanelStructure`/
`BuildPanelControls`; add menu item; add to `LoadAircraftFromCode` switch), the
background-monitoring recipe (Continuous + IsAnnounced, not in BuildPanelControls),
and the panel-control recipe.

Detailed docs in `docs/`:
- `docs/hotkey-system.md` — three-tier delegation; how an aircraft handles or falls
  through. No A380 specifics.
- `docs/variable-system.md` — the three variable patterns (Panel / Monitoring /
  Hotkey-only) + batched continuous monitoring + button-state mapping.
- `docs/aircraft-definitions.md` — API reference for the dictionary methods incl.
  `GetPanelDisplayVariables`.
- `docs/architecture.md` — core components, multi-aircraft, FCU architecture.
- Others: `adding-features.md`, `QUICK-REFERENCE.md`, `development.md`,
  `fenix-increment-decrement.md`, `visual-guidance.md`, `taxi-guidance.md`.

Per-aircraft hotkey guides exist in `MSFSBlindAssist/HotkeyGuides/`:
`FBW_A320_Hotkeys.txt`, `Fenix_A320_Hotkeys.txt`, `PMDG_777_Hotkeys.txt`.
**There is NO A380 hotkey guide** (no `FBW_A380_Hotkeys.txt`) and **no A380 doc** in
`docs/` (grep for "A380"/"FBW_A380" in `docs/` returns nothing). A380 design notes
live only under `tools/` (e.g. `a380-simvars-catalog.md`, `a380-fcu-vars.md`,
`a32nx-gap-vs-a380.md`, `ecam-display-readout-notes.md`).

**Docs to create/update:** add `MSFSBlindAssist/HotkeyGuides/FBW_A380_Hotkeys.txt`
once the A380 hotkey surface is finalized; optionally a `docs/` A380 page. Update
`a32nx-gap-vs-a380.md` covers the variable/control gap already; this file covers the
framework/hotkey gap.

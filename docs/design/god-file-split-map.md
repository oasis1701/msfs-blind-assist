# God-File Partial-Class Split Map (Task 4.1)

**Status:** read-only analysis. No code moved. This is the map that a later Phase 4
task (4.2+) executes against.

## Method used

For each file: list top-level member (method/property) signatures with line numbers,
compute the line span between consecutive members, count `// ===...===` banner
comments (and `#region`), and determine whether each banner/cluster sits **between**
two top-level members (a real, movable seam) or **inside** one giant method's body
(a fake seam — just a comment, not a separable C# member).

This distinction matters because a partial class can only be split by **moving whole
members** to another file of the same `partial class` — the compiler concatenates
partial files at the member level, never at the statement level. A banner comment
inside a single 5,000-line method is not a seam; it is a label on unreachable
territory until that method is first broken up (extract-method), which is a real
code transformation, not a verbatim move, and carries materially more risk than a
pure cut-paste.

**Finding common to every aircraft-definition file** (Fenix, FlyByWire A320/A380,
HS787, PMDG 777): the single largest method is always `GetVariables()` /
`GetPMDGVariables()` — one `Dictionary` object-initializer expression, 2,600–8,400
lines, containing the *majority* of that file's banners as internal comments, not
member boundaries. Per the task brief's own instruction ("keep GetVariables() in the
root file"), this method is **not** split in the primary recommendation below — it
moves nowhere, verbatim, in its own root file. That caps how much any one of these
files can shrink today. A deeper "one file per cockpit panel" split (matching the
banner names literally) is possible but requires an extract-method prep step first;
it is called out separately per file as a **stretch option**, not the baseline.

## Summary table

| File | Lines | Banners | Seam quality | Verdict | # partials (primary) |
|---|---|---|---|---|---|
| `Aircraft/FenixA320Definition.cs` | 13,511 | 116 | Best in repo — but 8,884 of the 13,511 lines (66%) live inside two methods (`GetVariables` 8,404 ln, `HandleUIVariableSet` 2,743 ln) where banners are internal comments, not seams. Everything past `HandleUIVariableSet`/`ProcessSimVarUpdate` is already small, whole, separable methods. | **SPLIT** | 5 partials + root |
| `Aircraft/FlyByWireA320Definition.cs` | 8,685 | 7 (6 are inside `GetVariables`/other monoliths; only 4 sit at real member boundaries) | Bimodal: `GetVariables` alone is 5,017 lines (58%); everything from line 5,103 to EOF (3,582 lines / 41%) is a run of already-separate, movable whole methods. | **SPLIT** (modest — only the back third moves) | 3 partials + root |
| `Aircraft/PMDG777Definition.cs` (+ existing `.SystemDisplay.cs`, 330 ln) | 7,968 | ~85 (78 of them inside `GetPMDGVariables`, 4,803 ln / 60%) | Already partially split (SystemDisplay is a clean, self-contained extraction). What's left is dominated by the un-splittable `GetPMDGVariables` monolith plus a `HandleUIVariableSet`/`ProcessSimVarUpdate`/`HandleHotkeyAction` trio (~1,150 ln) that shares debounce/echo state and shouldn't be pulled apart. | **SKIP** further split | 0 (optional micro-split, see below) |
| `Aircraft/HorizonSim787Definition.cs` | 7,741 | 28 (20 of them inside `GetVariables` 3,931 ln / 51% and `HandleUIVariableSet` 561 ln) | `GetVariables` + `ProcessSimVarUpdate` (1,648 ln) together are 5,579 ln (72%) and stay put; the remaining ~2,160 lines are already clean, separate whole methods (5 `Show*Dialog`s, `HandleHotkeyAction`, `BuildPanelControls`, `CloseAuxWindows`). | **SPLIT** | 3 partials + root |
| `Aircraft/FlyByWireA380Definition.cs` | 7,116 | 22 (12 inside `GetVariables` 2,620 ln / 37%) | Best-structured of the four remaining aircraft files: beyond `GetVariables`, there are genuine multi-method subsystem clusters already separated as whole methods — RMP (5 methods), FCU (10 methods), seat-motor, slider-ramp, plus large-but-whole `ProcessSimVarUpdate`/`HandleUIVariableSet`/`TryGetDisplayOverride`/`OnDisplayPanelShown`. | **SPLIT** | 6 partials + root |
| `MainForm.cs` | 7,098 | 0 banners, 0 regions (only the standard WinForms `.Designer.cs`, which is boilerplate, not a concern split) | Zero visual scaffolding, but 140 members cluster cleanly by **name pattern** (dialogs, menu handlers, SimVar/announcer plumbing, hotkeys, aircraft-switch, DB lifecycle). One 1,107-line monolith (`PanelLoadTimer_Tick`) moves whole. | **SPLIT** (name-pattern based, not banner-based) | 6 partials + root |
| `SimConnect/SimConnectManager.cs` | 5,531 | 2 (bracket a cleanup block inside `Disconnect`) | ~135 methods, cleanly member-separable (no method spans two clusters) except one 812-line `SimConnect_OnRecvSimobjectData` monolith. Several fields are genuinely cross-cutting (`forceUpdateVariables`, `pendingCalcEvents`, `CalcPathVerified`, def-id counters) and must stay root-declared. | **SPLIT** | 6 partials + root |
| `Services/TaxiGuidanceManager.cs` | 6,552 | 0 (confirmed — no `#region`, no `// ===`) | Two giant methods (`UpdatePosition` 731 ln, `UpdateLandingRollout` 657 ln), both under the single `_stateLock`. Real clusters exist for routing, rollout/landing-exit, announcements, and a fully lock-free math/utils tail; the state-machine core (`UpdatePosition` + hold-short/arrival + query API, ~2,650 ln) is too lock-entangled to split further. | **SPLIT** (partial — core stays) | 4 partials + root |

**Totals: 7 files recommended to SPLIT, 1 to SKIP** (`PMDG777Definition.cs` — already
holds the one clean seam in `.SystemDisplay.cs`; what remains is low-value churn).

---

## Per-file decomposition (SPLIT files)

All moves below are **whole members, verbatim** — no method body is edited or
broken up. Every target file keeps `partial class <Name>` (or `partial class
<Name> : <Base>` matching the existing declaration) and no `using`/namespace
changes beyond what each file needs. All private fields stay declared in whichever
file already contains them today (recommended: keep ALL fields in the root file
regardless of which partial reads/writes them — partial-class members share the
same field storage regardless of file, so this is purely an organizational choice,
not a technical requirement).

### 1. `Aircraft/FenixA320Definition.cs` (13,511 → root ~8,480 + 5 partials)

Root keeps: fields, `AircraftName`/`AircraftCode`, the four `Get*ControlType()`
one-liners, `GetVisualGuidanceProfile()`, `TaxiTurnLeadSeconds`, **`GetVariables()`**
(8,404 ln, unavoidably stays — see "method used" above), `GetPanelStructure()` (71 ln).

| Target file | Members moved | Approx. lines |
|---|---|---|
| `FenixA320Definition.PanelControls.cs` | `BuildPanelControls()`, `GetPanelDisplayVariables()`, `GetButtonStateMapping()` | ~765 |
| `FenixA320Definition.UiVariableSet.cs` | `HandleUIVariableSet()` (whole; internally an if/else-if chain with ~42 internal banners — see stretch option) | ~2,743 |
| `FenixA320Definition.SimVarUpdate.cs` | `ProcessSimVarUpdate()` + the 12 `Request*WithStatus`/`RequestAltimeter`/`RequestGearPosition`/`RequestFlapPosition`/`RequestGrossWeight*`/`RequestFuelQuantity*` readout methods (share `pending*Value`/`isRequesting*` fields with `ProcessSimVarUpdate`) | ~750 |
| `FenixA320Definition.Hotkeys.cs` | `HandleHotkeyAction()`, `ExecuteButtonTransition()`, `AnnounceTakeoffConfigResult()`, `IncrementCounter()`, `DecrementCounter()`, `JumpCounter()`, `CalculateHeadingDelta()`, `ExecuteRudderTrimTransition()` | ~750 |
| `FenixA320Definition.Dialogs.cs` | `SetFCUVariable()`, `ShowFenixAltitudeWindow/HeadingWindow/SpeedWindow/VSWindow/AutopilotWindow()`, `ShowFenixBaroWindow()`, `AdjustBaroCounter()` | ~350 |

**Stretch option (needs extract-method prep first, not part of the zero-risk
baseline):** `HandleUIVariableSet()` is internally a `try { if (varKey == …) {…
return true;} … }` chain — every one of its 42 internal banner blocks is
self-contained (ends in `return true`, reads only its own `varKey`/`value`
parameters, no shared local state between blocks). The same is true of
`GetVariables()`'s dictionary entries (independent key/value pairs) and
`BuildPanelControls()`'s per-panel `List<string>` blocks. This makes them
*mechanically* — but not *literally* — extractable into ~12–14 domain-named
partials (`.Adirs.cs`, `.AirCon.cs`, `.Electrical.cs`, `.Fcu.cs`, `.RmpAcp.cs`,
`.Efis.cs`, `.EcamDcduEfb.cs`, `.FireHydraulicFuel.cs`, `.MainInstrumentPanel.cs`,
`.PedestalMisc.cs`, `.Lighting.cs`, `.Safety.cs`, `.Radio.cs`, `.Cockpit.cs`), each
holding a `Get<X>Variables()` dict-builder, `Build<X>PanelControls()` list, and
`Handle<X>VariableSet()` handler for that one panel, called from thinned-down
root dispatchers. **This is an extract-method refactor, not a verbatim move** —
flag it explicitly if Phase 4.2+ attempts it; it should be its own reviewed step,
separate from the pure-move baseline above.

### 2. `Aircraft/FlyByWireA320Definition.cs` (8,685 → root ~5,244 + 3 partials)

Root keeps: fields, simple control-type overrides, **`GetVariables()`** (5,017 ln).

| Target file | Members moved | Approx. lines |
|---|---|---|
| `FlyByWireA320Definition.PanelBuild.cs` | `GetPanelDisplayVariables()`, `GetPanelStructure()`, `BuildPanelControls()`, `GetButtonStateMapping()`, `GetHotkeyVariableMap()` | ~765 |
| `FlyByWireA320Definition.Display.cs` | `HandleReadApproachCapability()`, `ToggleA320ECAMMonitoring()`, `DecodeArmedModes()`, `BaroPhrase()`, `AnnounceBaroIfChanged()`, `UnpackSixBit()`, `DecodeFmMessageFlags()` (has internal local functions `CAir/PctAir/V/Pct/LElev` etc. — these travel with it, no risk), `BuildEwdDecodedTextAsync()`, `TryGetDisplayOverride()`, `ProcessSimVarUpdate()` | ~1,438 |
| `FlyByWireA320Definition.HotkeysAndUiSet.cs` | `HandleHotkeyAction()`, `HandleUIVariableSet()`, plus the ~35 small `Request*/Set*FCU*/Fire*` tail methods | ~1,238 |

**Hazard:** `_baroHpa`/`_baroMode`, `_sdWriteSeq`/`_sdBoxContent`/`_sdRefreshSeq`,
`_tcasRa`/`_tcasRaComposeTimer`, `_doorOpen`, `_gwCgMac`, `pendingHeadingValue`/
`isRequestingHeading` are all read/written across the members grouped into
`.Display.cs` and `.HotkeysAndUiSet.cs` above — the grouping already keeps each
field's readers/writers together, but if the grouping changes, keep these fields'
consumers in the same file.

### 3. `Aircraft/PMDG777Definition.cs` — SKIP, optional micro-split only

No primary decomposition recommended. If pursued anyway for pure line-count
hygiene (not complexity reduction — value is low):

| Target file | Members moved | Approx. lines |
|---|---|---|
| `PMDG777Definition.EventIds.cs` | the `EventIds` static readonly dictionary field (zero logic, read-only via `TryGetValue`, no shared mutable state) | ~755 |
| `PMDG777Definition.Dialogs.cs` | `ShowPMDGHeadingDialog/SpeedDialog/AltitudeDialog/VSDialog()`, `SendPMDGMomentary()`, `FormatEtaFromDistance()`, `AnnounceTODFromSDK()`, `AnnounceDestFromSDK()` | ~330 |

**Do not** attempt to separate `HandleUIVariableSet()`/`ProcessSimVarUpdate()`/
`HandleHotkeyAction()` (~1,150 ln combined) — they share debounce/echo state
(`_cockpitDoorSetEcho`, `_lastComActiveFreq1/2`, etc.) at lines ~5,295–5,344 and
form one cohesive "live SimVar/UI event handling" unit. `GetPMDGVariables()`
(4,803 ln, 60% of the file) is the same un-splittable dictionary-literal pattern
as every other aircraft file's `GetVariables()`.

### 4. `Aircraft/HorizonSim787Definition.cs` (7,741 → root ~4,300 + 3 partials)

Root keeps: fields, control-type overrides, `GetButtonStateMapping()`,
`GetPanelStructure()`, **`GetVariables()`** (3,931 ln), `TryGetDisplayOverride()`,
`GetPanelDisplayVariables()`.

| Target file | Members moved | Approx. lines |
|---|---|---|
| `HorizonSim787Definition.PanelControls.cs` | `BuildPanelControls()` | ~258 |
| `HorizonSim787Definition.SimVarUpdate.cs` | `ProcessSimVarUpdate()` | ~1,648 |
| `HorizonSim787Definition.UiAndHotkeys.cs` | `HandleUIVariableSet()`, `HandleHotkeyAction()` (share `_lastFdSetTicks`/`_lastFdDesired`) | ~953 |
| `HorizonSim787Definition.Dialogs.cs` | `CloseAuxWindows()`, `ShowHs787Display()`, 5× `Show*Dialog()`, `FormatEte()`, `FormatEteSeconds()`, `TryFireInputEvent()` | ~530 |

**Hazard:** `_autopilotWindow`/`_displayWindow` are used by `CloseAuxWindows` (moves
to `.Dialogs.cs`) AND `HandleHotkeyAction`/`ShowHs787Display` (also `.Dialogs.cs`
and `.UiAndHotkeys.cs` respectively) — keep as root-declared fields; both files
already reference them fine as partial-class members.

### 5. `Aircraft/FlyByWireA380Definition.cs` (7,116 → root ~2,720 + 6 partials)

Root keeps: fields, **`GetVariables()`** (2,620 ln, has ~9 local functions
`Btn/PressSilent/Act/Read/ReadEnum/ReadEnumQuiet/Mon/MonNum/Evt/Light/Detent` used
only inline — travel with the method, no risk), `GetPanelStructure()`.

| Target file | Members moved | Approx. lines |
|---|---|---|
| `FlyByWireA380Definition.PanelControls.cs` | `BuildPanelControls()`, `GetPanelDisplayVariables()`, `GetButtonStateMapping()`, `GetHotkeyVariableMap()` | ~830 |
| `FlyByWireA380Definition.SimVarUpdate.cs` | `ProcessSimVarUpdate()`, `MaybeAnnounceTcasRaGuidance()`, `TlaDetent()`, `UnpackSixBitIdent()`, `DecodeArmedModes()` | ~880 |
| `FlyByWireA380Definition.UiVariableSet.cs` | `HandleUIVariableSet()` | ~570 |
| `FlyByWireA380Definition.Rmp.cs` | `SendRmpKey()`, `SetSquawkFromForm()`, `SendTransponderIdent()`, `SendRmpKeyPress/Release()` | ~61 |
| `FlyByWireA380Definition.Displays.cs` | `TryGetDisplayOverride()`, `RefreshSdPageDisplayAsync()`, `OnDisplayPanelShown()`, `SetActiveFwsFailures()`, `IsTextAnActiveWarning()`, `BuildEwdWindowTextAsync()` | ~1,320 |
| `FlyByWireA380Definition.HotkeysAndMotion.cs` | `HandleHotkeyAction()`, the 10-method FCU request/set/fire cluster, `RampSliderTo/SliderRampTick/StopSliderRamp/StopAllMotion()`, `ToggleSeatMotor/SeatMotorTick/AnnounceSeatPosition/SeatBand()` | ~735 |

**Hazard:** `_gwCgMac`/`_gwKgCache` (set in `ProcessSimVarUpdate`, read by
`CgMacPhrase` — verify `CgMacPhrase` travels with `ProcessSimVarUpdate` into
`.SimVarUpdate.cs`, not left behind); baro/FCU state fields are read across
`ProcessSimVarUpdate`/`HandleUIVariableSet`/`TryGetDisplayOverride` (three
different target files here) — fine since fields stay shared, but a reviewer
should diff-check nothing was accidentally duplicated. `RefreshSdPageDisplayAsync`
and `BuildEwdWindowTextAsync` each define their own local `Grp`/`ThrPct` helpers —
harmless duplication, leave as-is (do not try to unify into one shared helper,
that would be a behavior-risk change beyond scope).

### 6. `MainForm.cs` (7,098 → root ~700 + 6 partials)

Root keeps: fields, constructor, `MainForm_Load`, `InitializeManagers()`,
`OnFormClosing`. (`MainForm.Designer.cs` is untouched — standard WinForms
boilerplate, not part of this split.)

| Target file | Members moved | Approx. lines |
|---|---|---|
| `MainForm.Dialogs.cs` | every `Show*Dialog`/`Open*Form`/`Show*Form` (Fenix/PMDG/FBW/HS787 MCDU/EFB/RMP/OANS, teleport, checklist, TCAS, weather radar, ~31 methods) | ~750 |
| `MainForm.MenuHandlers.cs` | every `*MenuItem_Click`, `AboutMenuItem_Click`, `UpdateApplicationMenuItem_Click` (~25 methods) | ~650 |
| `MainForm.Announcers.cs` | `OnSimVarUpdated`, `ProcessEventBatch`, `HandleSpecialAnnouncements` (474 ln), `AnnounceVariableState`, `UpdateControlFromSimVar`, `UpdateDisplayText`, `OnPMDGVariableChanged`, `AnnounceWhereAmI`, `AnnounceAmbientChanges`, weather/nearest-city timers, `CheckWeatherProximityAsync`, nav/wind request helpers (~42 methods) | ~2,900 |
| `MainForm.PanelBuilder.cs` | `PanelLoadTimer_Tick` (1,107 ln, whole — do not touch its body), `SectionsListBox_SelectedIndexChanged`, `PanelsListBox_SelectedIndexChanged` | ~1,200 |
| `MainForm.Hotkeys.cs` | `OnHotkeyTriggered` (377 ln) + toggle/mode-change handlers (takeoff-assist, hand-fly, visual-guidance, taxi-guidance auto-activate) (~13 methods) | ~700 |
| `MainForm.AircraftSwitch.cs` | `LoadAircraftFromCode`, `SwitchAircraft` (326 ln), `UpdateAircraftSpecificMenuItems`, `UpdateAircraftMenuItems`, database lifecycle (`ValidateDatabaseSimulatorMatch`, `RefreshDatabaseProvider`, `Close/ReopenDatabaseConnections`, `CheckAndSwitchDatabase`), aircraft feature monitors (A380 EWD, HS787 IRS/CAS/EICAS, PMDG prog-page, ~8 methods) | ~1,500 |

**Hazard (the big one for this file):** ~100 private fields — form/client
references like `fenixMCDUForm`, `coherentClient`, `hs787CasClient` — are read
across `.Dialogs.cs` (opens them), `.Announcers.cs` (reads their live state), and
`.AircraftSwitch.cs` (disposes/nulls all of them on aircraft swap). These **must**
stay declared in the root file. `IsManualReadoutAction` (static) and the cached
dictionaries `GetPanelDisplayVarsCached()`/`GetDisplayVarNamesCached()` are read by
both `.Announcers.cs` and `.PanelBuilder.cs` — keep their backing fields in root
too. `.Announcers.cs` at ~2,900 lines is itself large enough to warrant a second
pass later (e.g. splitting out the weather/nearest-city timer block, ~a few
hundred lines) — not attempted here to keep this pass to one level of grouping.

### 7. `SimConnect/SimConnectManager.cs` (5,531 → root ~700 + 6 partials)

Root keeps: fields (including the cross-cutting ones below), constructor,
`Connect`, `Disconnect`, `DetectRetryTimer_Tick`, `ReconnectTimer_Tick`,
`ProcessWindowMessage`, MobiFlight/PMDG init/dispose.

| Target file | Members moved | Approx. lines |
|---|---|---|
| `SimConnectManager.Setup.cs` | `SetupDataDefinitions`, `RegisterAllVariables`, `SafelyClearDataDefinition`, `ReregisterAllVariables`, `SetupEvents`, `RegisterClientEvents`, input-event catalog | ~700 |
| `SimConnectManager.DataRequests.cs` | `RequestVariable(s)`, `RebindVariableDataDefinition`, the ~30 thin `Request<X>()` wrappers, `RequestAircraftPosition(Async)`, nav/wind/weather/ILS callback requests | ~900 |
| `SimConnectManager.EventSend.cs` | `SetLVar`, `SetSimVar`, `ExecuteCalculatorCode`, `SendEvent`, `FireCalcEvent`/`FlushPendingCalcEvents`/`FlushPendingHEvents`, `SendHVar`, `SendButtonPressRelease`, PMDG send/walk methods | ~700 |
| `SimConnectManager.VarCache.cs` | `GetCachedVariableValue`, `GetCachedVariableSnapshot`, `GetEcamLineRaw`, `ProcessIndividualVariableResponse`, `ProcessContinuousBatch*` overloads + impl, `FormatVariableValue`/FMA formatters | ~600 |
| `SimConnectManager.Dispatch.cs` | all `SimConnect_OnRecv*` handlers (`OnRecvOpen/Quit/EventFilename/SimobjectData/SimobjectDataBytype/Exception/ClientData/EnumerateInputEvents`), including the 812-line `SimConnect_OnRecvSimobjectData` (whole, untouched), plus `Process*` payload handlers (FCU, position, ILS, wind, weather, ECAM, AI traffic) | ~1,900 |
| `SimConnectManager.Monitoring.cs` | monitoring start/stop pairs (guidance/takeoff/hand-fly), teleport, destination-runway getters/setters, misc getters | ~400 |

**Hazard:** `forceUpdateVariables` (written in `RequestVariable`, read/cleared in
`RegisterAllVariables`/`ReregisterAllVariables` and in batch processing, cleared
again in `Disconnect`), `pendingCalcEvents`/`CalcPathVerified` (touched by
`SendEvent`/`FireCalcEvent`/flush methods, reset in `Disconnect`), and
`nextDataDefinitionId`/`nextTempDefId` (incremented in `RegisterAllVariables` and
`SetLVar`/`SetSimVar`, reset in `ReregisterAllVariables`) are genuinely
cross-cutting across four of the six target files above. **Keep all of them
declared in the root file** — this is exactly the kind of field the codebase's own
invariants flag as safety-critical (`CalcPathVerified` gates the entire
MobiFlight-vs-native write routing decision), so do not let the split obscure
where they live.

### 8. `Services/TaxiGuidanceManager.cs` (6,552 → root ~2,650 + 4 partials)

Root keeps: fields including `_stateLock`/`_route`/`_state`/`_currentSegmentIndex`,
constructor, `UpdatePosition` (731 ln, holds `_stateLock` for its entire body —
do not touch), `SetState`, `StartGuidance`, `StopGuidance`,
`CheckHoldShortCountdown`, `CheckSpeedWarnings`, `CheckRunwayIncursion`,
`CheckParkingCountdown`, `HandleHoldShort`, `ContinuePastHoldShort`,
`HandleArrival`, the query API (`DescribeCurrentLocation`,
`TryGetRunwayLineupReference`, `TryDetectRunwayUnderAircraft`,
`OnAirportDataUpdated`, `ClearWhereAmICache`, docking setters).

| Target file | Members moved | Approx. lines |
|---|---|---|
| `TaxiGuidanceManager.Routing.cs` | `LoadRoute`, `TryRecalculateRoute`, `FindNearestSegmentIndexFullRoute`, `AdvanceToNearestSegment`, `AdvanceSegment`, `TruncateToHoldShort`, `InsertRunwayCrossingHoldShorts`, `ApplyUserRunwayHoldShorts`, `ApplyUserHoldShorts` | ~1,400 |
| `TaxiGuidanceManager.Rollout.cs` | `BeginLandingRollout`/`BeginLandingRolloutNoGraph`, `ResetRolloutApproachLatches`, `UpdateLandingRollout` (657 ln), `TryEarlyExitHandoff`, `FindExitExtensionNode`, `UpdateRunwayEndCountdown`, `RetargetLandingExit`, `EnterRunwayEndCountdown`, `FindBacktrackConnectionNode`, `EnterBacktracking`, `UpdateBacktracking`, `UpdateLineup` | ~1,650 |
| `TaxiGuidanceManager.Announcements.cs` | `CheckUpcomingAnnouncements`, `TryAnnounceCurve`, `TryAnnounceCrossing`, `AnnounceInstruction`, `RepeatLastInstruction`, `GetStatusAnnouncement`, `BuildRouteSummary` | ~500 |
| `TaxiGuidanceManager.MathUtils.cs` | `NormalizeAngle`, `ComputeTurnVerbalFromHeading`, `SignedAlongRunwayMeters`, `PerpendicularDistanceToSegmentMeters`, `FormatDistance`, `CapFirst`, and the other pure static/near-static geometry+formatting helpers | ~350 |

**Hazard — the defining one for this file:** `_stateLock` is acquired at 13+ call
sites spanning almost every cluster above (`LoadRoute`, both
`BeginLandingRollout*` variants, `RepeatLastInstruction`, `GetStatusAnnouncement`,
plus everything staying in root). Moving these methods to different files does
**not** change their locking behavior — the lock is a field, shared across all
partials of the class — but a reviewer must not assume file boundaries mirror
critical-section boundaries. The existing project invariant ("any new public
method touching `_route`/`_state`/`_currentSegmentIndex` must acquire
`_stateLock`") applies identically regardless of which file the method lives in.
`.MathUtils.cs` is the only target file that is provably lock-free (pure
functions, no field access) — the safest of the four to execute first if this
split is staged incrementally.

---

## Execution risk ranking (for sequencing Phase 4.2+)

Lowest risk → highest risk, based on how "whole" each moved member already is
(no internal banners needing extract-method) and how many hazard fields are shared
across the target split:

1. **`TaxiGuidanceManager.MathUtils.cs` extraction** — zero shared state, pure functions.
2. **`PMDG777Definition` micro-split** (if pursued) — `EventIds` is a read-only static table.
3. **`HorizonSim787Definition`** — clean 4-way split, only one two-field hazard.
4. **`FlyByWireA380Definition`** — 6-way split, more hazard fields but all well-understood (baro/FCU state already documented elsewhere in the codebase's own invariants).
5. **`FlyByWireA320Definition`** — 3-way split, similar hazard profile to A380.
6. **`SimConnectManager`** — 6-way split; the cross-cutting fields (`CalcPathVerified` etc.) are safety-critical, so this needs the most careful post-split review even though the member separation itself is clean.
7. **`TaxiGuidanceManager` routing/rollout/announcements** — clean member separation, but the shared `_stateLock` discipline is the single most safety-critical invariant in the taxi subsystem (runway-incursion risk if ever gotten wrong) — treat with commensurate review care.
8. **`MainForm`** — largest field-sharing surface (~100 fields across every cluster) — do this only after the aircraft-definition and manager files establish the pattern.
9. **`FenixA320Definition`** primary (5-way) split — safe (same shape as HS787/A380), but the file is the biggest so has the most surface for a copy-paste slip; do last among the "safe" splits, and treat the stretch (banner-level, extract-method) option as an entirely separate, later, individually-reviewed effort.

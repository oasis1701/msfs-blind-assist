# Fenix Variants Implementation Plan (plan 06)

**Claimed by:** danielw97 · **Status:** done (2026-10-22: code, reviews and the owner's in-sim checks on the A321 IAE SC and an A320 CFM; see "Results")

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Offer each Fenix airframe the panel controls it really has: the A321's ECON FLOW and CALLS MID/EXIT, the ACT PUMP on aircraft with extra centre tanks, the IAE N1 MODE lights, and hide what the loaded variant lacks (PACK FLOW on the A321, N1 MODE on CFM, the ADIRS keypad or IR pushbuttons, DCDU, ISIS or analogue standby controls, aft cargo heat). Also add four cockpit controls the app never offered, each on the panel where the aircraft has it: the headset, the clock, the ISIS baro knob push and the aft cargo temperature knob (Task 5).

**Architecture:** Everything lives in one new partial file, `MSFSBlindAssist/Aircraft/FenixA320Definition.Variants.cs`, which implements plan 01's variant seams (variables, panel controls, panel structure, panel display list and text, UI writes, visibility). A pure static table, `IsFitted(varKey, FenixVariant)`, decides visibility from plan 01's `Variant`; a field the app has not read yet always shows the control. The same table guards writes: a control the variant lacks is refused aloud and nothing is written.

**Tech Stack:** C# 13 / .NET 10 Windows Forms, xUnit.

**Spec:** `docs/fenix-v3/spec.md`, section 3. Index, file ownership and workflow: `docs/fenix-v3/plans/00-index.md`. Seams and `FenixVariant`: `docs/fenix-v3/plans/01-foundation.md`.

## Owner decisions

My calls (the owner can overturn any of them; each is one small edit):

1. **Unknown means shown.** A control is hidden only when the field it depends on has been read and says the aircraft lacks it (base contract, `BaseAircraftDefinition.IsPanelControlVisible`, BaseAircraftDefinition.cs:796-810; PMDG 777 precedent PMDG777Definition.cs:6119-6133). Future NEO engine codes 2 and 3 count as "not CFM56", so N1 MODE stays shown (the CEO model shows its `Engine_IAE_Panel` for any non-zero `N_CONFIG_ENGINETYPE`).
2. **A control the variant lacks is refused aloud**: "Not fitted on this aircraft." (a validation error, so CORE-7 lets it speak). This only triggers from a panel built before the variant was read, or from another caller of `HandleUIVariableSet`.
3. **ECON FLOW and ACT PUMP are Off/On combos written with `SetLVar`**, like the Fenix fuel pumps: both use the model's latched-button template, whose variable holds the pushed-in state. ECON FLOW's Off/On comes from Fenix's own tooltips ("Turn on econ flow" / "Turn off econ flow"). ACT PUMP's comes from the latched template's default ON/OFF verbs. Fenix ships no tooltip text for that button, so the pilot-facing legend is still unread.
4. **CALLS MID and EXIT are buttons with the full press-release pulse** (`ExecuteButtonTransition`, A320-3/A320-4): they use the model's momentary template. They are silent after the press, like every other Fenix `ExecuteButtonTransition` button (MainForm.PanelBuilder.cs:411-418 speaks only for unhandled buttons).
5. **New lights are named by position from their switch's label** ("Econ Flow Lower", "ACT Pump Upper", "Engine N1 Mode 1 Lower"), as every Fenix light in the definition is. No legend is inferred (spec 3; VAR-1 pair rule).
6. **Placement:** ECON FLOW straight after Pack Flow (only one of the two is ever shown). MID and EXIT between Forward and Aft (front-to-back cabin order). ACT Pump after Center Tank Pump 2.
7. **No live panel rebuild when the variant arrives.** Detection takes about a second after load; a panel opened before then shows everything until it is reopened (MainForm.PanelBuilder.cs:312-318 re-evaluates visibility on every build). A rebuild under the pilot's focus would move the focus.
8. **Out of this plan, recorded as follow-ups** (they need fields `FenixVariant` does not have, or are unmeasured): brake-fan visibility, GNADIRS IR ALIGN/FAULT lights, the analogue standby altimeter baro knob, the ACT refuel valves and FQI selector, the A321 centre-tank legends. Details are in the handoff section.
9. **`OnVariantSimVarUpdate` and `OnFenixSwitchedAway` stay unimplemented** unless Q1 is answered yes: nothing in this plan needs a per-update hook, so `OnFenixSwitchedAway` stays free for plan 12.
10. **The four new cockpit controls (Task 5) follow the aircraft's layout and the FBW A320's wording.** The clock gets its own "Clock" panel in the Main Instrument Panel section straight after "Landing Gear": the model's clock controls (`MainPanel_Clock_*`) sit on the centre panel beside the gear lever and the autobrake, and the FBW A320 (the same type) also has a "Clock" panel in its instrument section. Its labels and display texts are the FBW A320's ("Chronometer Start/Stop", "Elapsed Time" Run/Stop/Reset, "2 minutes 5 seconds", "Reset"). The headset goes at the end of Cockpit Other, the ISIS baro push after ISIS Baro Dec, and the aft cargo temperature knob between Aft Cargo Hot Air and the Aft Cargo Isolation Valve, the model's left-to-right order of the cargo heat panel. The knob shows 0.0 to 1.0 like the Fenix's cabin temperature knobs (never "harmonize" it to Cold/Hot here). The clock buttons are silent after the press, like every Fenix `ExecuteButtonTransition` button: the chronometer and elapsed time are read from the Clock panel's display.

Questions only the owner can answer:

- **Q1. Speak the detected variant once after loading** (for example plan 01's `Variant.Describe()`, "Fenix A321, IAE, two extra tanks")? Default **no**. If yes, run Task 4.
- **Q2. Menu and profile name.** Keep "Fenix A320 CEO", or show "Fenix A319/A320/A321 CEO"? The code `FENIX_A320CEO` stays either way: it is persisted in settings and keys the hotkey guide (HotkeyListForm.cs:138), the First Officer windows (FirstOfficerWindows.cs:38) and the aircraft switch (MainForm.AircraftSwitch.cs:152). Any change is handoff H1.
- **Q3. Convert the existing CALLS MECH/ALL/FWD/AFT controls?** Today they are Off/On combos written with `SetLVar` (FenixA320Definition.cs:4551-4599 and :10628-10657), but the model makes them momentary buttons. Converting them to press buttons like MID/EXIT would follow A320-3 and make the Calls panel consistent. Yes means handoff H3. Until then the Calls panel mixes four combos and two buttons.
- **Q4. Hide panels the variant leaves empty?** Without a DCDU, "DCDU Left" has no controls; with analogue standby, "ISIS" has none. Either hide those panels (handoff H2) or leave them empty.
- **Q5. Rebuild an open panel when the variant changes** (for example after the pilot switches ADIRS type in the Fenix EFB)? Default no; yes means handoff H4.

## Global Constraints

- Files: create `MSFSBlindAssist/Aircraft/FenixA320Definition.Variants.cs` and `tests/MSFSBlindAssist.Tests/FenixVariantControlsTests.cs`, nothing else. `FenixVariant.cs`, `FenixA320Definition.Hooks.cs` and the main `FenixA320Definition.cs` are not this plan's. Never read `N_CONFIG_*`/`B_CONFIG_*` directly: use `Variant`.
- Consume plan 01 exactly: `enum FenixAircraftType { Unknown = 0, A318 = 1, A319 = 2, A320 = 3, A321 = 4 }`; `enum FenixEngineFamily { Unknown = -1, Cfm56 = 0, IaeV2500 = 1, CfmLeap = 2, PwGtf = 3 }`; `sealed record FenixVariant(FenixAircraftType Type, FenixEngineFamily Engine, int? ActCount, bool? HasIsis, bool? HasDcdu, bool? IsNeo, bool? HasGnadirs, bool? HasCargoHeat = null)` with `Unknown`, `IsKnown`, `Describe()`; `FenixA320Definition.Variant`, `VariantChanged`. The seams have the signatures in plan 01 Task 1.
- L:var names exactly as in the package and the live data service: `S_OH_PNEUMATIC_ECON_FLOW`, `I_OH_PNEUMATIC_ECON_FLOW_L`, `S_OH_CALLS_MID`, `S_OH_CALLS_EXIT`, `S_OH_FUEL_ACT`, `I_OH_FUEL_ACT_U`, `I_OH_FUEL_ACT_L`, `I_OH_ENG_N1_MODE_1_L`, `I_OH_ENG_N1_MODE_2_L`; Task 5's `A320_Sound:Headset_On`, `S_MIP_CLOCK_CHR`, `S_MIP_CLOCK_RST`, `S_MIP_CLOCK_SET`, `S_MIP_CLOCK_ET`, `S_MIP_CLOCK_UTC`, `S_MIP_ISFD_BARO_BUTTON`, `A_OH_PNEUMATIC_CARGO_AFT_TEMP`.
- Data-definition budget (SIM-1): **+12 individual definitions** (the four switches of Tasks 1-3 and Task 5's eight controls, all `OnRequest`) and **+5 batch slots** (the five lights, `Continuous` + `IsAnnounced`, no individual definition). Task 5's readouts reuse the registered `N_MIP_CLOCK_CHRONO`/`_ELAPSED`. The Fenix uses about 490 of 1000.
- VAR-1: unique `DisplayName`s per panel; each light's label is its switch's label plus " Upper"/" Lower", built from one constant or read from the switch, never typed twice. VAR-6: lights never go into `BuildPanelControls`. VAR-7: no new `Name` duplicates an existing continuous one.
- Register with `Dictionary.Add`, never the indexer: if another plan registers the same key, the definition must fail at load, not silently replace it.
- Refusal text, exactly: `Not fitted on this aircraft.`

## Review Focus

- **Variant not read yet** (first second after load, or detection failed): every control shows and nothing is refused (Task 2 `Unknown_variant_fits_every_panel_control`, Task 3 `Unknown_variant_refuses_nothing`).
- **Variant partly read** (type known, ACT count not yet): each control follows only its own field (Task 2 `Each_control_reads_only_its_own_field`).
- **Future NEO engine codes 2/3, or `IsNeo` true**: nothing is hidden on a guess, and N1 MODE stays shown (Task 2 `Unmeasured_engine_families_keep_n1_mode`).
- **A control pressed on a panel built before the variant said it is not fitted**: one spoken refusal, no write, the main handler never reached (Task 3 `Not_fitted_control_is_refused_aloud`).
- **A repeated label in a panel this plan touches** (VAR-1, the screen reader could not tell two controls apart): Task 1 `Touched_panels_repeat_no_label`.

## Evidence (read on 2026-10-21; Fenix 3.0.0.7426)

| Control | Package source (`<MSFS Community>\…\attachments\fnx\`) | Shown when |
|---|---|---|
| `S_OH_PNEUMATIC_ECON_FLOW` (latched) + `I_OH_PNEUMATIC_ECON_FLOW_L` | `Part_Interior_Panel_Pneumatic_A321`, attached only by `fnx-aircraft-320\…\Function_Interior_A321_{SingleClass,TwoClass}_CEO` | Type A321 |
| `S_OH_PNEUMATIC_PACK_FLOW` (existing) | only in `Part_Interior_Panel_Pneumatic_A32X` (A319/A320 interiors) | Type not A321 |
| `S_OH_CALLS_MID`, `S_OH_CALLS_EXIT` (momentary) | `Part_Interior_Panel_Calls_A321` (A321 interiors only); MECH and AFT are in the common cockpit | Type A321 |
| `S_OH_FUEL_ACT` (latched) + `I_OH_FUEL_ACT_U/_L` | `Part_Interior_Panel_Fuel_ACT_Enabled`, visible when `N_CONFIG_ACT_COUNT` > 0; the A321 interiors attach both ACT panels, A319/A320 only `_Disabled` | ActCount > 0 |
| `S_OH_ENG_N1_MODE_1/2` (existing) + `I_OH_ENG_N1_MODE_1/2_L` | glTF: the buttons sit under `Engine_IAE_Panel`, visible when `N_CONFIG_ENGINETYPE` ≠ 0 (`Engine_CFM_Panel` when 0) | Engine not Cfm56 |
| `S_OH_ADIRS_KEY_0`…`_9`, `_CLR`, `_ENT`, `S_OH_NAV_DATA_DISP`, `S_OH_NAV_SYS_DISP` (existing) | only in `Panel_GNADIRS_Behavior.xml`, visible when `B_CONFIG_ADIRS` == 0 | HasGnadirs not false |
| `S_OH_NAV_IR1/2/3_SWITCH` (existing) | only in `Panel_ADIRS_Behavior.xml`, visible when `B_CONFIG_ADIRS` ≠ 0 | HasGnadirs not true |
| `S_DCDU1_DIM_BRT`, `S_DCDU1_LSK1L/1R/2L/2R`, `S_DCDU1_MSGUP/MSGDWN/PGUP/PGDN/PRINT`, `S_DCDU2_DIM_BRT` (existing) | glTF: under `MainPanel_CPDLC`, visible when `B_CONFIG_DCDU` ≠ 0 | HasDcdu not false |
| `S_MIP_ISFD_BUGS/LS/PLUS/MINUS/RST`, `E_MIP_ISFD_BARO_INC/DEC` (existing) | glTF: under `MainPanel_Standby_ISIS_Visibility` (`B_CONFIG_STDBY` == 0) | HasIsis not false |
| `S_STANDBY_ATTITUDE_CAGE` (existing) | glTF: under `MainPanel_Standby_Analog_Visibility` (`B_CONFIG_STDBY` ≠ 0) | HasIsis not true |
| `S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO` (existing), `A_OH_PNEUMATIC_CARGO_AFT_TEMP` (Task 5) | glTF: `CargoHeat_Hot_Air_Button` and `CargoHeat_Aft_Cargo_Knob` are children of `CargoHeat_CargoHeat`, visible when `B_CONFIG_CARGOMEAT` ≠ 0 (`CargoHeat_CargoVent`, a blank plate, when 0). `CargoHeat_Aft_Isolation_Button` is outside both: always fitted | HasCargoHeat not false |

Not variant-dependent (checked): `S_STANDBY_COMPASS` (both standby types), the ACP (`ACP_Modern` and `ACP_Original` use the same names; only `N_CONFIG_ACP` selects the model), the engine manual start buttons.

**Live reads on the IAE A321 "FenixA321 IAE SL TC", N932AM**

- L:vars: `N_CONFIG_AIRCRAFT_TYPE` 4, `N_CONFIG_ENGINETYPE` 1, `N_CONFIG_ACT_COUNT` 2, `B_CONFIG_STDBY` 0, `B_CONFIG_DCDU` 0 (this livery; the spec read 1 on a DCDU livery).
- `B_CONFIG_CARGOMEAT` 0 (read later the same day, after reconnecting the MCP; settings `cargo_heat_enabled` False).
- `B_CONFIG_ADIRS` and `B_CONFIG_BRAKEFANS` read **null** through the simconnect MCP (before a reconnect that later fixed null reads of other names: re-read them before trusting the null). Settings API on the same aircraft: `type_code` A321, `engine_family` IaeV2500, `act_count` 2, `adirs_type` Adirs (order Gnadirs, Adirs), `standby_type` Isis, `dcdu_enabled` False, `brake_fans_enabled` True.
- Data service: every new name exists and reads 0; `S_OH_PNEUMATIC_PACK_FLOW` still reads 1 on the A321.

---

### Task 1: Register the variant controls and place them on their panels

**Files:**
- Create: `MSFSBlindAssist/Aircraft/FenixA320Definition.Variants.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixVariantControlsTests.cs`
- Read only: `FenixA320Definition.cs`. The panels are "Air Conditioning and Pressurization" (:8653-8691), "Fuel" (:8734-8751) and "Calls" (:8850-8859). The effective N1 MODE switch definitions are the later duplicates at :5761-5778 ("Engine N1 Mode 1/2"). `OffOn` is at :120.

**Interfaces:**
- Consumes: plan 01's `partial void AddVariantVariables(Dictionary<string, SimConnect.SimVarDefinition>)`, called after the readout twins; `partial void AddVariantPanelControls(Dictionary<string, List<string>>)`.
- Produces: `internal const string EconFlowName = "Econ Flow";` and `internal const string ActPumpName = "ACT Pump";` on `FenixA320Definition`.

- [x] **Step 1: Write the failing tests** (class `FenixVariantControlsTests`; style of `FenixAcpReceptionTests`: one static `new FenixA320Definition()`, `GetVariables()`, `GetPanelControls()`).
  - `Switches_are_on_request_and_lights_are_announced`:
    - `S_OH_PNEUMATIC_ECON_FLOW` and `S_OH_FUEL_ACT`: `LVar`, `OnRequest`, values exactly `{0:"Off",1:"On"}`, `RenderAsButton` false.
    - `S_OH_CALLS_MID` and `S_OH_CALLS_EXIT`: `LVar`, `OnRequest`, `RenderAsButton` true, labels "Calls Mid" and "Calls Exit".
    - The five lights: `LVar`, `Continuous`, `IsAnnounced`, values `{0:"Off",1:"On"}`.
    - Each key's `Name` equals its key.
  - `Each_light_is_labelled_from_its_switch`:
    - `I_OH_PNEUMATIC_ECON_FLOW_L` == `EconFlowName + " Lower"`.
    - `I_OH_FUEL_ACT_U`/`_L` == `ActPumpName + " Upper"`/`" Lower"`.
    - `I_OH_ENG_N1_MODE_1_L` == `Vars["S_OH_ENG_N1_MODE_1"].DisplayName + " Lower"`, and the same for 2. Today that is "Engine N1 Mode 1 Lower".
  - `Each_switch_sits_once_where_the_plan_puts_it`:
    - In "Air Conditioning and Pressurization", ECON FLOW's index == Pack Flow's index + 1.
    - In "Calls", the subsequence is `S_OH_CALLS_FWD, S_OH_CALLS_MID, S_OH_CALLS_EXIT, S_OH_CALLS_AFT`.
    - In "Fuel", `S_OH_FUEL_ACT` comes straight after `S_OH_FUEL_CENTER_2`.
    - Across all panels, each of the four switches appears exactly once, and no light appears in any panel (VAR-6).
  - `Touched_panels_repeat_no_label`: in those three panels, the `DisplayName`s of the listed keys are distinct.
- [x] **Step 2: Run them; they fail.** `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter FenixVariantControlsTests`. Expected: failures on missing keys (`KeyNotFoundException`).
- [x] **Step 3: Implement `AddVariantVariables` and `AddVariantPanelControls`** in a `public partial class FenixA320Definition` (namespace `MSFSBlindAssist.Aircraft`), opening with a comment that cites spec 3 and the evidence table.
  - Register the nine definitions with `variables.Add`. Use `OffOn` for the switch and light values.
  - Read the N1 MODE light labels from `variables["S_OH_ENG_N1_MODE_n"].DisplayName`.
  - Place the switches by `IndexOf(anchor) + 1` on the existing lists. A missing anchor throws `InvalidOperationException` naming it: a silent append would hide a changed panel.
- [x] **Step 4: Run the filter, then the whole suite.** Same command without `--filter`. Expected: all green, `VarNameCollisionTests`, `ComboLabelCollapseTests`, `ClaudeContextBudgetTests` and plan 01's `FenixDefinitionSeamTests` (it pins no totals) included. Any failure is yours to fix.
- [x] **Step 5: Commit.** `git add` both files; message `feat(fenix): A321 ECON FLOW and CALLS MID/EXIT, ACT PUMP, IAE N1 MODE lights`.

### Task 2: Show only what the loaded variant has

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.Variants.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixVariantControlsTests.cs`

**Interfaces:**
- Consumes: `FenixVariant`, enums, `FenixVariant.Unknown`, the instance's `Variant`; plan 01's `partial void IsVariantControlVisible(string varKey, ref bool visible)`, which `IsPanelControlVisible` calls.
- Produces: `internal static bool IsFitted(string varKey, FenixVariant variant)`, true unless the variant's own field for that key says the airframe lacks it (the evidence table's last column); `true` for every key the table does not name.

- [x] **Step 1: Write the failing tests.** Add a helper `static FenixVariant V(int type, int engine, int? act, bool? isis, bool? dcdu, bool? neo, bool? gnadirs) => new((FenixAircraftType)type, (FenixEngineFamily)engine, act, isis, dcdu, neo, gnadirs);` so the measured raw values appear in the tests.
  - `A321_iae_with_act_as_read_on_2026_10_21`: `V(4, 1, 2, true, false, false, false)`.
    - Fitted: ECON FLOW, CALLS MID, CALLS EXIT, `S_OH_FUEL_ACT`, both N1 MODE switches, `S_OH_NAV_IR1_SWITCH`, `S_MIP_ISFD_BUGS`, `S_OH_ELEC_BAT1`.
    - Not fitted: `S_OH_PNEUMATIC_PACK_FLOW`, `S_OH_ADIRS_KEY_0`, `S_OH_NAV_DATA_DISP`, `S_DCDU1_LSK1L`, `S_DCDU2_DIM_BRT`, `S_STANDBY_ATTITUDE_CAGE`.
  - `A320_cfm_as_read_on_2026_10_21`: `V(3, 0, 0, false, false, false, false)`. Its settings said `standby_type` Analogue; ACT 0, because the A320 interiors attach only `Fuel_ACT_Disabled`.
    - Fitted: PACK FLOW, `S_STANDBY_ATTITUDE_CAGE`, `S_OH_NAV_IR2_SWITCH`.
    - Not fitted: ECON FLOW, MID, EXIT, `S_OH_FUEL_ACT`, both N1 MODE switches, `S_MIP_ISFD_RST`, `E_MIP_ISFD_BARO_INC`, `S_OH_ADIRS_KEY_ENT`.
  - `Gnadirs_and_dcdu_fitted`: `V(3, 0, 0, true, true, false, true)`.
    - Fitted: every keypad key, `S_OH_NAV_DATA_DISP`, `S_OH_NAV_SYS_DISP`, every `S_DCDU1_*` panel key, `S_DCDU2_DIM_BRT`.
    - Not fitted: `S_OH_NAV_IR1/2/3_SWITCH`.
  - `A319_and_a318_are_not_a321`: types 2 and 1, all other fields as the A320 case. ECON FLOW and MID are not fitted; PACK FLOW is fitted.
  - `Unknown_variant_fits_every_panel_control`: for `FenixVariant.Unknown`, `IsFitted` is true for every key in every list of `GetPanelControls()`. On a fresh definition (Variant Unknown), `IsPanelControlVisible(key, null)` is true for the same keys, which proves the seam is wired.
  - `Each_control_reads_only_its_own_field`: `V(4, -1, null, null, null, null, null)` fits `S_OH_FUEL_ACT`, both N1 MODE switches, `S_OH_ADIRS_KEY_0`, `S_OH_NAV_IR1_SWITCH`, `S_DCDU1_PRINT`, `S_MIP_ISFD_LS` and `S_STANDBY_ATTITUDE_CAGE`, and does not fit PACK FLOW. `V(0, 0, 2, null, null, null, null)` fits ECON FLOW and PACK FLOW (type unknown), and does not fit N1 MODE.
  - `Unmeasured_engine_families_keep_n1_mode`: engines 2 and 3 (`CfmLeap`, `PwGtf`) with `neo: true` fit both N1 MODE switches, and the NEO flag changes no other answer in the A321 case.
- [x] **Step 2: Run them; they fail to compile** (no `IsFitted`). Same filter command.
- [x] **Step 3: Implement `IsFitted`** as one `switch` on `varKey` with one arm per row of the evidence table (`ActCount is null or > 0`; `!= false` / `!= true` for the booleans; `Type is Unknown or A321` for A321-only; `Type != A321` for PACK FLOW; `Engine != Cfm56` for N1 MODE), `_ => true`. **Implement `IsVariantControlVisible`** as `if (!IsFitted(varKey, Variant)) visible = false;`.
- [x] **Step 4: Run the filter, then the whole suite; all pass.**
- [x] **Step 5: Commit.** `feat(fenix): hide the controls the loaded Fenix variant lacks`.

### Task 3: Write the new controls; refuse a control the variant lacks

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.Variants.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixVariantControlsTests.cs`
- Read only: `ExecuteButtonTransition` (FenixA320Definition.cs:12666-12739, private and callable from this partial); the PACK FLOW handler (:9770-9773), which the refusal must pre-empt; `SpeechCapture` (tests/MSFSBlindAssist.Tests/GroundTrafficMonitorHarness.cs:179).

**Interfaces:**
- Consumes: plan 01's `partial void HandleVariantUIVariableSet(string varKey, double value, SimConnect.SimVarDefinition varDef, SimConnect.SimConnectManager simConnect, Accessibility.ScreenReaderAnnouncer announcer, ref bool handled)`, which runs before the main handler (`if (handled) return true;`); `IsFitted` (Task 2).
- Produces:
  - `internal const string NotFittedMessage = "Not fitted on this aircraft.";`
  - `internal static bool TryRefuseNotFitted(string varKey, FenixVariant variant, Accessibility.ScreenReaderAnnouncer announcer)`: when `!IsFitted`, speaks `NotFittedMessage` with `AnnounceImmediate` and returns true; otherwise it is silent and returns false.

- [x] **Step 1: Write the failing tests.**
  - `Not_fitted_control_is_refused_aloud`: with the A321 case from Task 2, `TryRefuseNotFitted("S_OH_PNEUMATIC_PACK_FLOW", a321, speech)` is true and `speech.Interrupts` equals `["Not fitted on this aircraft."]`. With the A320 case, `TryRefuseNotFitted("S_OH_CALLS_MID", …)` is true. With the A321 case, `TryRefuseNotFitted("S_OH_PNEUMATIC_ECON_FLOW", …)` is false and nothing is spoken.
  - `Unknown_variant_refuses_nothing`: for every panel key, `TryRefuseNotFitted(key, FenixVariant.Unknown, speech)` is false and `speech.All` is empty.
  - `New_latched_switches_are_handled_silently`: on a fresh definition with a disconnected manager (`new SimConnectManager(IntPtr.Zero)`), `HandleUIVariableSet("S_OH_PNEUMATIC_ECON_FLOW", 1, …)` and `HandleUIVariableSet("S_OH_FUEL_ACT", 0, …)` both return true, and `speech.All` is empty. Not the CALLS buttons: their WinForms timers need a message loop.
- [x] **Step 2: Run them; they fail to compile.**
- [x] **Step 3: Implement `TryRefuseNotFitted` and `HandleVariantUIVariableSet`.**
  - First, if `TryRefuseNotFitted(varKey, Variant, announcer)`, set `handled = true` and return. This covers existing keys too: PACK FLOW on an A321 never reaches the main handler.
  - Then: `S_OH_PNEUMATIC_ECON_FLOW` and `S_OH_FUEL_ACT` call `simConnect.SetLVar(varKey, value)`.
  - `S_OH_CALLS_MID` and `S_OH_CALLS_EXIT`, when `value == 1`, call `ExecuteButtonTransition(varKey, varDef.DisplayName, simConnect, announcer)`.
  - Each of those sets `handled = true`. Every other key leaves `handled` false.
- [x] **Step 4: Run the filter, then the whole suite; all pass. Build the solution:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64`. Expected: 0 errors.
- [x] **Step 5: Commit.** `feat(fenix): write ECON FLOW, ACT PUMP and CALLS MID/EXIT; refuse controls the variant lacks`.

### Task 4 (decided: the owner answered Q1 yes, 2026-10-21; index answer 12): speak the variant once

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.Variants.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixVariantControlsTests.cs`

**Interfaces:**
- Consumes: `VariantChanged` (raised on the UI thread), `FenixVariant.IsKnown`, `FenixVariant.Describe()`, the main file's private `lastAnnouncer` (FenixA320Definition.cs:67, set by `ProcessSimVarUpdate`).
- Produces: `internal static string? VariantSpeech(string? lastSpokenText, FenixVariant now)`, returning `now.Describe()` when `now.IsKnown` and that text differs from `lastSpokenText`, else null. The config values arrive one per `ProcessSimVarUpdate` call within the same batch, so `VariantChanged` fires several times in a row: speech waits for a 1 s quiet period (a WinForms `Timer` restarted on each change) and then compares the text, so one load is spoken once and a field the sentence does not mention (ISIS, DCDU) never re-speaks it.

- [x] **Step 1: Write the failing test** `Variant_is_spoken_once_per_distinct_known_variant`, run under `CultureInfo.CurrentCulture = new CultureInfo("de-DE")`:
  - `VariantSpeech(null, FenixVariant.Unknown)` is null.
  - `VariantSpeech(null, a321)` == `a321.Describe()`.
  - `VariantSpeech(a321.Describe(), a321)` is null.
  - `VariantSpeech(a321.Describe(), a321 with { HasIsis = false })` is null (the sentence does not change).
  - `VariantSpeech(a321.Describe(), a321 with { ActCount = 0 })` is not null.
- [x] **Step 2: Run it; it fails to compile.**
- [x] **Step 3: Implement.**
  - `VariantSpeech`, plus a private `string? _spokenVariantText` and a 1 s one-shot `System.Windows.Forms.Timer` restarted on each `VariantChanged`; its Tick speaks `VariantSpeech(_spokenVariantText, Variant)` once (queued `Announce`, CORE-7 background change) and stores the text.
  - In `AddVariantVariables` (runs once per instance), subscribe a handler, unsubscribing it first (`VariantChanged -= OnVariantKnown; VariantChanged += OnVariantKnown;`). The handler speaks `VariantSpeech(_spokenVariant, v)` through `lastAnnouncer?.Announce`, then records `_spokenVariant`.
  - Implement `partial void OnFenixSwitchedAway()`: unsubscribe, stop and dispose the timer, clear `_spokenVariantText`.
- [x] **Step 4: Run the filter and the suite; all pass.**
- [x] **Step 5: Commit.** `feat(fenix): say which Fenix variant is loaded, once`.

### Task 5: Headset, clock, ISIS baro push and aft cargo temperature (owner request 2026-10-21, measured live the same day)

Why: the parity principle (index): a sighted pilot can work these four in the Fenix cockpit; the app offered none of them. Each goes on the panel where the aircraft has it (decision 10).

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.Variants.cs`
- Test: `tests/MSFSBlindAssist.Tests/FenixVariantControlsTests.cs`
- Read only: `FenixA320Definition.cs`: "Cockpit Other" (:8936), "ISIS" (:9251-9260), "Air Conditioning and Pressurization" (:8686-8691), the "Main Instrument Panel" section in `GetPanelStructure` (:8505-8575), `ExecuteButtonTransition` and `ButtonPressHoldMs` (:12666), the cabin knob `A_OH_PNEUMATIC_AFT_TEMP` (:3382), the registered readouts `N_MIP_CLOCK_CHRONO`/`_ELAPSED` (:6807-6822; `Continuous` + `IsAnnounced`, silent by default through `SettingsManager.SeedFenixMonitorDefaults`). For wording parity: the FBW A320's Clock panel (FlyByWireA320Definition.cs:5103-5135, :6346-6349, display texts :7978-7991).

**Measured on 2026-10-21** (A321 IAE on the ground, simconnect MCP; every value restored afterwards):

| Control | Variable and values | What the sim did | Panel |
|---|---|---|---|
| Headset | `A320_Sound:Headset_On`: 0 on its hook, 1 worn | The data-def write (what `SetLVar` does for a name with a colon, SIM-12, VAR-2) and the calculator write both stick; `A320_Sound:ANC`, the sound files' noise-cancelling level, follows 0 → 1 → 0. Model: clickspot toggle `Misc_Headset_Toggle`, always fitted | Cockpit > Cockpit Other, last |
| CHR | `S_MIP_CLOCK_CHR`, momentary | 1st press: `N_MIP_CLOCK_CHRONO` -1 → 0, then counts whole seconds; 2nd press stops it (held at 8); 3rd press runs on from the held value | Main Instrument Panel > Clock (new) |
| RST | `S_MIP_CLOCK_RST`, momentary | Stopped: chrono → -1 (blank). Running: restarts from 0 and keeps running | Clock |
| DATE | `S_MIP_CLOCK_SET`, momentary (tooltip "Toggle between current time and current date") | `N_MIP_CLOCK_UTC` (HHMMSS: 184807 = 18:48:07) still held the time after a press; no published date value found, so nothing to read back | Clock |
| ET | `S_MIP_CLOCK_ET`: 0 RUN, 1 STP, 2 RST | 0: `N_MIP_CLOCK_ELAPSED` -1 → 0, counting whole minutes (2 after 2 min 21 s); 1: holds the count; 2: → -1. The model (`Knob_3Position_SingleHold`) springs RST back to STP only on a mouse release: a written 2 stays 2 | Clock |
| UTC | `S_MIP_CLOCK_UTC`: 0 GPS, 1 INT, 2 SET (the model's own tooltips) | Writes stick; in SET, `N_MIP_CLOCK_MODE` goes -1 → 0 (the field being set), back to -1 on GPS | Clock |
| ISIS baro push | `S_MIP_ISFD_BARO_BUTTON`, momentary (model node `MainPanel_Standby_Baro_Button`, tooltip "BARO button"; HubHop calls it the baro knob's push) | Press and release work. No readable state found: `N_MIP_ISFD_BARO_HPA` 1013 and `_INCH` 2991 did not change, and no `B_MIP_ISFD_*` name was listed. Whether it selects STD or the unit is unmeasured; pressed twice to restore | Main Instrument Panel > ISIS, after ISIS Baro Dec |
| Aft cargo temperature | `A_OH_PNEUMATIC_CARGO_AFT_TEMP`, 0.0-1.0 in 0.1 steps (model `Knob_Increment`, tooltip "Increase/Decrease aft cargo temperature") | 0.1 sticks; restored to 0. No downstream check: this livery has no cargo heat | Overhead > Air Conditioning and Pressurization, between Aft Cargo Hot Air and the Aft Cargo Isolation Valve |

**Interfaces:**
- Consumes: plan 01's `AddVariantPanelStructure`, `AddVariantPanelDisplayVariables`, `TryVariantDisplayOverride` and `FenixVariant.HasCargoHeat`; Tasks 1-3's `AddVariantVariables`, `AddVariantPanelControls`, `IsFitted`, `HandleVariantUIVariableSet`.
- Produces: `internal const string ClockPanel = "Clock";`, `internal static string ChronometerText(double seconds)` and `internal static string ElapsedTimeText(double minutes)`: a negative value gives "Reset"; otherwise `$"{m} minutes {s} seconds"` from whole seconds and `$"{h} hours {m} minutes"` from whole minutes (the FBW A320's texts; the Fenix's units differ, as measured).

- [x] **Step 1: Write the failing tests.**
  - `New_cockpit_controls_are_registered` (each `LVar`, `OnRequest`, `Name` equal to its key):
    - `A320_Sound:Headset_On`: label "Headset", values `{0:"Off",1:"On"}`.
    - Buttons (`RenderAsButton` true): `S_MIP_CLOCK_CHR` "Chronometer Start/Stop", `S_MIP_CLOCK_RST` "Chronometer Reset", `S_MIP_CLOCK_SET` "Date", `S_MIP_ISFD_BARO_BUTTON` "ISIS Baro Push".
    - `S_MIP_CLOCK_ET` "Elapsed Time", values exactly `{0:"Run",1:"Stop",2:"Reset"}`; `S_MIP_CLOCK_UTC` "UTC Selector", values exactly `{0:"GPS",1:"Internal",2:"Set"}`.
    - `A_OH_PNEUMATIC_CARGO_AFT_TEMP` "Aft Cargo Temperature", values equal to `Vars["A_OH_PNEUMATIC_AFT_TEMP"].ValueDescriptions`.
  - `New_controls_sit_where_the_aircraft_has_them`:
    - `GetPanelStructure()["Main Instrument Panel"]`: index of "Clock" == index of "Landing Gear" + 1.
    - `GetPanelControls()["Clock"]` is exactly `S_MIP_CLOCK_CHR, S_MIP_CLOCK_RST, S_MIP_CLOCK_ET, S_MIP_CLOCK_UTC, S_MIP_CLOCK_SET`; `GetPanelDisplayVariables()["Clock"]` is exactly `N_MIP_CLOCK_CHRONO, N_MIP_CLOCK_ELAPSED`.
    - "ISIS": `S_MIP_ISFD_BARO_BUTTON` straight after `E_MIP_ISFD_BARO_DEC`. "Cockpit Other": `A320_Sound:Headset_On` is last. "Air Conditioning and Pressurization": the subsequence `S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO, A_OH_PNEUMATIC_CARGO_AFT_TEMP, S_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE`.
    - Each new key appears exactly once across all panels, and every key of `GetPanelControls()` is in some `GetPanelStructure()` list.
  - Extend `Touched_panels_repeat_no_label` to "Clock", "ISIS" and "Cockpit Other".
  - `Clock_display_texts_use_the_fbw_wording`, under `CultureInfo.CurrentCulture = new CultureInfo("de-DE")`: `ChronometerText(-1)` "Reset", `(0)` "0 minutes 0 seconds", `(125)` "2 minutes 5 seconds"; `ElapsedTimeText(-1)` "Reset", `(2)` "0 hours 2 minutes", `(135)` "2 hours 15 minutes". On a fresh definition `TryGetDisplayOverride("N_MIP_CLOCK_CHRONO", 125, out var t)` is true with `t` "2 minutes 5 seconds", and `TryGetDisplayOverride("S_OH_ELEC_BAT1", 1, out _)` is false.
  - `Cargo_heat_controls_follow_the_livery`: with the A321 case from Task 2 plus `HasCargoHeat: false` (as read on 2026-10-21), `S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO` and `A_OH_PNEUMATIC_CARGO_AFT_TEMP` are not fitted; with `true` or `null` both are; `S_OH_PNEUMATIC_CARGO_AFT_ISOL_VALVE` is fitted in all three.
  - `New_switches_are_handled_silently` (the Task 3 harness): `HandleUIVariableSet` returns true and speaks nothing for `A320_Sound:Headset_On` 1, `S_MIP_CLOCK_UTC` 1, `S_MIP_CLOCK_ET` 0 and `A_OH_PNEUMATIC_CARGO_AFT_TEMP` 0.3. Not the buttons and not ET Reset: their WinForms timers need a message loop.
- [x] **Step 2: Run them; they fail.** Same filter command as Task 1.
- [x] **Step 3: Implement.**
  - `AddVariantVariables`: register the eight with `variables.Add`. The cargo knob copies `variables["A_OH_PNEUMATIC_AFT_TEMP"].ValueDescriptions`, so the two knobs read the same.
  - `AddVariantPanelControls`: add the "Clock" list and place the other three keys as Step 1 says, by `IndexOf(anchor) + 1` (a missing anchor throws, as in Task 1); the headset with `Add`.
  - `AddVariantPanelStructure`: insert `ClockPanel` after "Landing Gear" in "Main Instrument Panel" (a missing anchor throws). `AddVariantPanelDisplayVariables`: add `ClockPanel` with the two readouts. `TryVariantDisplayOverride`: `N_MIP_CLOCK_CHRONO` → `ChronometerText`, `N_MIP_CLOCK_ELAPSED` → `ElapsedTimeText`, every other key leaves `displayText` null.
  - `IsFitted`: one more arm, `S_OH_PNEUMATIC_HOT_AIR_AFT_CARGO or A_OH_PNEUMATIC_CARGO_AFT_TEMP => variant.HasCargoHeat != false`.
  - `HandleVariantUIVariableSet` (after the refusal), each setting `handled = true`:
    - Headset, UTC, the cargo knob, and ET 0 or 1: `simConnect.SetLVar(varKey, value)`.
    - ET 2 (Reset): `SetLVar("S_MIP_CLOCK_ET", 2)`, then after `ButtonPressHoldMs` a one-shot `System.Windows.Forms.Timer` writes 1 (only while `IsConnected`, as `ExecuteButtonTransition` does), because the real knob springs back to Stop. The combo keeps saying Reset until the panel is reopened, and then shows Stop.
    - CHR, RST, DATE and the ISIS push, when `value == 1`: `ExecuteButtonTransition(varKey, varDef.DisplayName, simConnect, announcer)` (A320-3).
- [x] **Step 4: Run the filter, then the whole suite; all pass. Build the solution.**
- [x] **Step 5: Commit.** `feat(fenix): headset, clock, ISIS baro push and aft cargo temperature on their panels`.

### Task 6: Verify and hand back

- [x] **Step 1:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` and `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`. Expected: build 0 errors; every test green.
- [x] **Step 2:** `git diff --name-only <base-sha>` lists only `MSFSBlindAssist/Aircraft/FenixA320Definition.Variants.cs` and `tests/MSFSBlindAssist.Tests/FenixVariantControlsTests.cs`.
- [x] **Step 3:** Hand back with the section below filled in (commits, test count, the answers to Q1-Q5 if given).

---

## Results (2026-10-22)

**Commits** (FenixV3, base 8c1e4d6da): 07c100e14 (Task 1), d6247e801 (Task 2), b5a08e72b (Task 3), 4596cd9a5 (Task 5), 870f7699e (Task 4), 2f8222697 (rename `TryWritePlain`), 7319efbce (final-review fixes), c1f72e454 (Headset first). Only the two planned files changed. Tests: `FenixVariantControlsTests` 34, full suite 14876 green; solution builds with 0 errors.

**Owner answers:** Q1 yes (Task 4; spoken once, and reloading the same preset stays quiet, confirmed 2026-10-22). Q2 keep "Fenix A320 CEO". Q3 convert (done by plan 02). Q4 no: empty panels stay. Q5 no live rebuild. New on 2026-10-22: CALLS MID/EXIT, the clock CHR/RST/DATE buttons and ISIS Baro Push say "<name> pressed" (`confirmPress: true`, as plan 02 made CALLS MECH/ALL/FWD/AFT), overriding decisions 4 and 10's "silent". Headset is first in Cockpit Other, not last (decision 10): the panel has no physical order and the headset is its most-used control.

**Changes from the plan text**
- The plain writes (ECON FLOW, ACT PUMP, headset, UTC, ET Run/Stop, cargo knob) go through `TryWritePlain(key, value, writer)`: `SetLVar` cannot run in the test host (no SimConnect dll), so the tests check the writer and the in-sim checks prove the seam.
- Elapsed Time Reset goes through the existing `PulseSpringSwitch(EtKey, 2, …)`, which also re-reads, so the combo shows Stop again about half a second after Reset (the plan expected "Reset" until the panel is reopened). Proposed rule FNX-k should name `PulseSpringSwitch`.
- ISIS Baro Push is gated on `HasIsis` like the other ISIS controls: `MainPanel_Standby_Baro_Button` sits under `MainPanel_Standby_ISIS_Visibility` in `fnx-aircraft-320`'s `FNX_32X_Interior_Cockpit_LOD00.gltf`.
- Task 4: one field `_spokenVariantText`; `VariantChanged` only restarts the 1 s timer, the Tick speaks.

**Live variant reads, "FenixA321 IAE SL SC"** (simconnect MCP): type 4, engine 1, ACT 0, `B_CONFIG_ADIRS` 1, `B_CONFIG_STDBY` 0, `B_CONFIG_DCDU` 0, `B_CONFIG_CARGOMEAT` 1, `B_CONFIG_BRAKEFANS` 1. The single-class preset has no ACT and has cargo heat, unlike the two-class one. ADIRS and BRAKEFANS read fine: the 2026-10-21 nulls were the MCP (closes the plan 01 detection request).

**In-sim results (owner)**
- A321 IAE SC: the variant is spoken once after "connected… loading". Econ Flow shown, no Pack Flow; Aft Cargo Hot Air and Aft Cargo Temperature shown; no ACT Pump. Calls Mid and Exit say "pressed"; **only Calls Exit chimed** (open: click MID in the virtual cockpit to see whether Fenix chimes MID in the cockpit at all). Engine N1 Mode 1 on/off works. ADIRS shows IR Push, no keypad; DCDU Left empty; ISIS has its controls; Standby Instruments only the compass. Headset works. Chronometer runs and reads in the Clock display; Elapsed Time Reset springs back to Stop. ISIS Baro Push says "pressed", no readable effect (expected).
- A320 CFM: no Econ Flow, Calls without Mid/Exit, Standby Instruments as expected for its standby type.

**For plan 12, from the final review**
- The Clock display rows read "CLOCK CHRONO"/"CLOCK ELAPSED" from the main file's `DisplayName`s (FenixA320Definition.cs ~:6763, :6771), not the FBW A320's "Chronometer"/"Elapsed Time" (decision 10). Renaming them also changes the Ctrl+M rows.
- UTC Selector "Set" has no follow-through until the SET encoder `E_MIP_CLOCK_SET` is added; the guide should say so.
- Ctrl+M announcements of CHRONO/ELAPSED, if unmuted, speak raw values (they do not use `TryGetDisplayOverride`). Pre-existing.
- The First Officer's `PF_PACKFLOW` step (FenixChecklistDefinitions.cs ~:126) is not variant-gated; the FO writes directly, so no refusal happens (plan 05).

## Handoff to plan 12

**Pilot-facing changes (guide: `guide/airbus-aircraft.md`, Fenix section; plan 12 writes it)**
- On the A321, Overhead > Air Conditioning and Pressurization offers Econ Flow (Off/On) instead of Pack Flow. Overhead > Calls adds Calls Mid and Calls Exit buttons.
- With extra centre tanks, Overhead > Fuel offers ACT Pump (Off/On), and its upper and lower lights are announced.
- On IAE engines the N1 MODE lights are announced ("Engine N1 Mode 1 Lower: On"). On CFM engines the N1 MODE switches are no longer offered.
- The ADIRS panel offers the keypad and the data and system display selectors only on GNADIRS-fitted aircraft, and the IR pushbuttons only on plain ADIRS. DCDU controls appear only on liveries with a DCDU. ISIS controls appear only with an ISIS, and the standby attitude cage only with analogue standby instruments.
- Overhead > Air Conditioning and Pressurization offers Aft Cargo Temperature (0.0 to 1.0, like the cabin temperature knobs) and Aft Cargo Hot Air only on aircraft with aft cargo heat, which follows the livery.
- Main Instrument Panel has a new Clock panel after Landing Gear: Chronometer Start/Stop, Chronometer Reset, Elapsed Time (Run, Stop, Reset), UTC Selector (GPS, Internal, Set) and Date. Its display reads the chronometer ("2 minutes 5 seconds") and the elapsed time ("1 hours 12 minutes"), or "Reset" when blank.
- Main Instrument Panel > ISIS adds ISIS Baro Push, the baro knob's push.
- Cockpit > Cockpit Other adds Headset (Off, On): On puts the headset on, and the Fenix's noise cancelling quiets the cockpit sound.
- The app knows the variant about a second after the aircraft loads. A panel opened earlier shows every control until it is reopened. Pressing a control that turns out not to be fitted says "Not fitted on this aircraft."

**Proposed rule lines** (a32nx-fenix.md is at its cap; these go into `.claude/rules/fenix-data.md`, whose glob `MSFSBlindAssist/Aircraft/FenixA320Definition.*.cs` covers the Variants file; take the next free FNX numbers):
- `- [FNX-n] Fenix variant visibility is `FenixA320Definition.IsFitted`: a control is hidden only when its own `FenixVariant` field is read and says the airframe lacks it; an unread field always shows it, and `HandleUIVariableSet` refuses a not-fitted control aloud. Never hide on a guess: NEO engine codes 2/3 keep N1 MODE, as the model's `Engine_IAE_Panel` shows for any non-zero type. Full: docs/invariants/fenix-data.md#fnx-n`
- `- [FNX-m] Each Fenix variant's controls come from its own per-type package panel, never by analogy: the A321 has ECON FLOW and CALLS MID/EXIT and no PACK FLOW knob; the ADIRS keypad and DATA/SYS DISP are GNADIRS-only (`B_CONFIG_ADIRS` 0), the IR pushbuttons ADIRS-only. New lights are labelled by position from their switch, never by an inferred legend. Full: docs/invariants/fenix-data.md#fnx-m`
- `- [FNX-k] The Fenix clock's ET Reset is written 2 and then 1 after `ButtonPressHoldMs` (`HandleVariantUIVariableSet`): the model springs RST back to STP only on a mouse release, so a lone written 2 stays at Reset (measured 2026-10-21). Full: docs/invariants/fenix-data.md#fnx-k`
- Full text: the evidence table and live reads above (package paths, glTF ancestry, 2026-10-21 values) and Task 5's measured table.

**Handoff requests**
- **H1 (if Q2 is answered with a rename).**
  - `FenixA320Definition.cs:13` `AircraftName`.
  - `MainForm.Designer.cs:232-236`: the menu `Text` keeps its `&` accelerator, plus `AccessibleName` and `AccessibleDescription`.
  - The guide and README mentions.
  - Never `AircraftCode`.
- **H2 (if Q4 is yes).** In `FenixA320Definition.GetPanelStructure()` (:8505-8575), drop a panel when its `GetPanelControls()` list is non-empty and no key in it passes `IsPanelControlVisible(key, null)`. MainForm reloads a section's panel list only when the section is selected again (MainForm.PanelBuilder.cs:65-89), so this takes effect on the next section change.
- **H3 (moved to plan 02 Task 11, which owns the file; Q3 was answered "convert").** CALLS MECH/ALL/FWD/AFT become `RenderAsButton` with `ExecuteButtonTransition` on `value == 1`: definitions at FenixA320Definition.cs:4552-4583, handlers at :10629-10651. They keep their labels. `S_OH_CALLS_EMER` and its cover are not part of this.
- **H4 (if Q5 is yes).** MainForm.PanelBuilder: on the Fenix's `VariantChanged`, reload `currentPanel` (the `PanelLoadTimer_Tick` path) only when the focus is not inside `controlsContainer`.
- **H5 (after in-sim check 1).**
  - If the ECON FLOW light simply follows the switch (the screen reader says "On" and then "Econ Flow Lower: On" is heard), set `UiEchoVariable = "I_OH_PNEUMATIC_ECON_FLOW_L"` on `S_OH_PNEUMATIC_ECON_FLOW` in the Variants file (CORE-7). `FenixAcpReceptionTests.Every_ui_echo_partner_is_an_announced_light_with_the_same_values` then covers it.
  - If the owner reads ACT PUMP's real legends, the labels may move from Off/On to them.
- **H6 (moved to plan 02 Task 11, which keeps one `OnRequest` definition labelled "Engine N1 Mode 1"/"2", so this plan's light labels stay as tested).** `S_OH_ENG_N1_MODE_1/2` are registered twice in one initializer (FenixA320Definition.cs:4720-4735 and :5761-5778). The later entry wins (`Continuous`, `IsAnnounced`, "Engine N1 Mode 1"), which is also a panel control (VAR-6). Pick one definition.
- **H7 (resolved before work started).** Plan 01's `FenixDefinitionSeamTests` asserts the seams' wiring and pins no totals, so this plan's additions leave it green.
- **To plan 01 (detection).** `B_CONFIG_ADIRS` and `B_CONFIG_BRAKEFANS` read null through the simconnect MCP on 2026-10-21, while the settings API said Adirs and brake fans True. Confirm the app's own read gives 1 (in-sim check 3).
- **To plan 05 (First Officer).**
  - An A321 has no PACK FLOW knob; its ECON FLOW is `S_OH_PNEUMATIC_ECON_FLOW` (0 Off, 1 On).
  - The ACT pump is `S_OH_FUEL_ACT`.
  - The IAE N1 MODE lights are `I_OH_ENG_N1_MODE_1/2_L`.
  - A write through `HandleUIVariableSet` to a control the variant lacks is refused aloud, so flows check `Variant` first.
- **To plan 04 (displays).** ISIS vs analogue standby is `Variant.HasIsis`. The glTF groups are `MainPanel_Standby_ISIS_Visibility` and `MainPanel_Standby_Analog_Visibility`.
- **Follow-ups (new `FenixVariant` fields or unmeasured)**
  - Brake fans: `B_CONFIG_BRAKEFANS` shows `S_MIP_BRAKE_FAN`, node `MainPanel_Brake_Fan_Button_Mouse`.
  - Clock: the SET knob `E_MIP_CLOCK_SET` (infinite encoder; with the UTC selector at SET and the DATE button it sets the time and date) and a UTC time readout from `N_MIP_CLOCK_UTC` (HHMMSS; its definition is commented out at FenixA320Definition.cs:8408). No date value was found to read.
  - ISIS baro push: what it toggles (STD or the hPa/inHg unit) is unmeasured, and no readable flag was found; the Fenix changelog names a `B_MIP_ISFD_*` family that would allow a read-back once a name is confirmed from a source (never guessed).
  - GNADIRS IR lights: `I_OH_NAV_IR1/2/3_ALIGN` and `_FAULT` exist live but are not registered.
  - Analogue standby altimeter baro knob: `E_STANDBY_ALT_IMPERIAL_BARO`.
  - ACT refuel valves `S_EXT_REFUELING_ACT_VALVE_1/2` and the ACT FQI selector.
  - ACT fuel quantities: `aircraft.fuel.ACT1/2.amount.kg` read 0 on 2026-10-21 though `act_count` is 2.
  - A321 centre-tank legends: both Fuel panels carry a `texture.a321` folder; whether our "Center Tank Pump" labels match the A321 is unverified.

**In-sim checks still needed (owner; few, prioritised)**
1. **The A321 now loaded** (IAE, ACT, on the ground, engines at idle, parking brake set).
   - Overhead > Air Conditioning and Pressurization: you hear "Econ Flow" and no "Pack Flow". Set Econ Flow to On. Note whether "Econ Flow Lower: On" follows the screen reader's "On" (H5). Set it back to Off.
   - Overhead > Calls: press Calls Mid and Calls Exit; the cabin call chime should sound in the sim.
   - Overhead > Fuel: set ACT Pump to On, then Off. Note which of "ACT Pump Upper" and "ACT Pump Lower" is announced and when (never infer the legend).
2. **Engines.** On the same A321, Overhead > Engine offers Engine N1 Mode 1 and 2. Set Engine N1 Mode 1 to On: is "Engine N1 Mode 1 Lower: On" heard? Set it back to Off. Then load an A320 CFM preset: Overhead > Engine offers only the two Manual Start switches, and the Air Conditioning panel offers Pack Flow and no Econ Flow.
3. **ADIRS and standby, on the A321.**
   - Overhead > ADIRS shows IR 1/2/3 Push and no keypad. This also proves the app reads `B_CONFIG_ADIRS` as 1, which the MCP could not.
   - Optional: set ADIRS type to GNADIRS in the Fenix EFB, then switch panels away and back. The keypad and the data and system display selectors appear, and IR Push is gone.
   - Pedestal > DCDU Left is empty (this livery has no DCDU), Main Instrument Panel > ISIS has its controls, and Cockpit > Standby Instruments offers only the Standby Compass.
4. **The four new controls, on any Fenix.**
   - Cockpit > Cockpit Other: set Headset to On; the cockpit sound gets quieter. Set it back to Off.
   - Main Instrument Panel > Clock: press Chronometer Start/Stop and wait a few seconds; the display reads the seconds counting. Press it again, then Chronometer Reset: the display says "Reset". Set Elapsed Time to Run, then to Reset; reopen the panel: Elapsed Time shows Stop.
   - Overhead > Air Conditioning and Pressurization on this A321: no Aft Cargo Temperature or Aft Cargo Hot Air (no cargo heat on this livery).

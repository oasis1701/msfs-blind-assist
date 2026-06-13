# FBW Dev-Version Parity & Correctness Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix every source-verified bug, dead control, and coverage gap found in the 2026-06-11 audit of MSFSBA's FlyByWire A32NX/A380X integration against FBW source v2024.2.0-dev, plus redesign the Ctrl+B altimeter dialog on both jets to mirror the Fenix experience.

**Architecture:** All changes target the dev FBW builds (the team and users run dev). Aircraft-definition fixes live in `FlyByWireA320Definition.cs` / `FlyByWireA380Definition.cs`; Coherent agent fixes in `Resources/coherent-*.js` + their C# clients; transport fixes in `SimConnectManager.cs` / `MobiFlightWasmModule.cs` / `FlyByWireMCDUService.cs`. Dead controls are REMOVED (user decision: cleanest implementation). Items needing live-sim verification are deferred to the Pass-2 checklist at the end — nothing in Tasks 1–35 requires a running sim beyond a build.

**Tech Stack:** C# 13 / .NET 9 WinForms, ES5 JS (Coherent GT agents), jsdom node tests for flyPad agent changes.

---

## Conventions for every task

- **Build check** (the only automated verification for C# — this repo has no test project):
  `dotnet build MSFSBlindAssist.sln -c Debug` → expect `Build succeeded`. NEVER build the csproj bare (writes to the wrong folder — see CLAUDE.md).
- **Line numbers** are from branch `FlyByWire-Complete-Integration` at plan time; always `Grep` for the quoted code first — apply edits by exact string match, not line number.
- **A320 def has NO helper functions** — new vars there use full `["KEY"] = new SimConnect.SimVarDefinition {...},` object-initializer entries. The A380 def has local builders (`Mon`/`Sel`/`OnOff`/`ReadEnum`/`Btn`/`Read`) inside `GetVariables()`.
- **Removal rule:** when removing a var, grep its key across `MSFSBlindAssist\` and remove EVERY reference (def, panel list, display list, HandleUIVariableSet branch, ProcessSimVarUpdate branch). A build failure after a removal usually means a missed reference.
- **Commit after every task** (commit freely; never push — per Robin's standing instruction). Commit messages below.
- FBW source for cross-checking lives at `C:\Users\robin\Downloads\fbw\aircraft`.

---

# Phase A — A32NX definition correctness

### Task 1: A32NX label/value corrections (GPWS switches, signs, weather radar, TCAS names, RMP mode, recorder)

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs`

All six fixes are wrong `ValueDescriptions`/`DisplayName` metadata — no mechanism changes.

- [ ] **Step 1: Flip the four GPWS `*_OFF` switch labels** (lines ~1264–1295). FBW semantics: var = 1 means the function is OFF (`A320_NEO_INTERIOR.xml:3571-3592`). For each of `A32NX_GPWS_FLAP_OFF`, `A32NX_GPWS_GS_OFF`, `A32NX_GPWS_SYS_OFF`, `A32NX_GPWS_TERR_OFF` change:

```csharp
ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
```
to:
```csharp
ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Off" }
```
Do NOT touch `A32NX_GPWS_FLAPS3` (its 0=Off/1=On is correct). Each block is unique by its `["A32NX_GPWS_..."]` key — edit within each block individually.

- [ ] **Step 2: Fix NO SMOKING / EMER EXIT sign labels** (lines ~299–314). FBW semantics: 0=On, 1=Auto/Arm, 2=Off (`A32NX_LocalVarUpdater.ts:74-82`, ANIMTIPs). In the `XMLVAR_SWITCH_OVHD_INTLT_NOSMOKING_POSITION` block change the dictionary to:

```csharp
ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Auto", [2] = "Off" }
```
In the `XMLVAR_SWITCH_OVHD_INTLT_EMEREXIT_POSITION` block change it to:
```csharp
ValueDescriptions = new Dictionary<double, string> { [0] = "On", [1] = "Arm", [2] = "Off" }
```

- [ ] **Step 3: Fix the weather radar SYS switch** (lines ~2190–2197). It is a 3-position switch (0=SYS 1 on, 1=Off, 2=SYS 2 on — `Airbus.xml FBW_AIRBUS_WeatherRadar_Sys_Template`). In the `XMLVAR_A320_WeatherRadar_Sys` block change:

```csharp
ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
```
to:
```csharp
ValueDescriptions = new Dictionary<double, string> { [0] = "System 1", [1] = "Off", [2] = "System 2" }
```

- [ ] **Step 4: Swap the crossed TCAS DisplayNames** (lines ~2273–2288). In the `A32NX_SWITCH_TCAS_TRAFFIC_POSITION` block change `DisplayName = "TCAS Mode"` to `DisplayName = "TCAS Traffic Display"`. In the `A32NX_SWITCH_TCAS_POSITION` block change `DisplayName = "TCAS Traffic"` to `DisplayName = "TCAS Mode"`. (Value lists are already correct per var.)

- [ ] **Step 5: Fix the RMP mode combo** (lines ~4217–4227). Mode 0 "SEL" does not exist; real modes are 1-3 VHF, 6 VOR, 7 ILS, 8 GLS (`BaseRadioPanels.tsx:59-110`). Replace the dictionary:

```csharp
ValueDescriptions = new Dictionary<double, string>
{
    [0] = "SEL", [1] = "VHF1", [2] = "VHF2", [3] = "VHF3"
}
```
with:
```csharp
ValueDescriptions = new Dictionary<double, string>
{
    [1] = "VHF1", [2] = "VHF2", [3] = "VHF3", [6] = "VOR", [7] = "ILS", [8] = "GLS"
}
```

- [ ] **Step 6: Build.** Run `dotnet build MSFSBlindAssist.sln -c Debug` → `Build succeeded`.

- [ ] **Step 7: Commit.**
```bash
git add MSFSBlindAssist/Aircraft/FlyByWireA320Definition.cs
git commit -m "fix(a32nx): correct inverted/wrong labels - GPWS switches, signs, WX radar SYS, TCAS names, RMP modes"
```

### Task 2: A32NX oil-quantity var name fix

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:4504-4511, 4546-4553`

`A32NX_ENGINE_TANK_OIL` has zero occurrences in FBW; the real var is `A32NX_ENGINE_OIL_QTY:n` (FADEC, quarts — `FadecSimData_A32NX.hpp:317-318`).

- [ ] **Step 1:** In the `["A32NX_ENGINE_OIL_QTY:1"]` block change `Name = "A32NX_ENGINE_TANK_OIL:1",` to `Name = "A32NX_ENGINE_OIL_QTY:1",`. Same for `:2`.
- [ ] **Step 2: Build** → succeeded.
- [ ] **Step 3: Commit:** `git commit -am "fix(a32nx): oil quantity read the nonexistent ENGINE_TANK_OIL var - use A32NX_ENGINE_OIL_QTY"`

### Task 3: A32NX CALLS buttons — momentary reset + EMER as combo

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:1218-1253`

The four PUSH calls render as buttons via MainForm's LVar-button path (no ValueDescriptions), which writes 1 and never resets — `PUSH_OVHD_CALLS_MECH` is a HOLD var gated on a `Continuous="true"` Wwise loop (endless mech horn), and ALL/FWD/AFT are read per-frame by PseudoFWC (stuck call). `A32NX_CALLS_EMER_ON` is a latching toggle that the one-way button can set but never clear. MainForm already supports `IsMomentary` auto-reset (MainForm.cs:5970-5995, 150 ms).

- [ ] **Step 1:** Add `IsMomentary = true,` to each of the four PUSH blocks. Example for the first (repeat the same one-line addition for `PUSH_OVHD_CALLS_ALL`, `_FWD`, `_AFT`):

```csharp
        ["PUSH_OVHD_CALLS_MECH"] = new SimConnect.SimVarDefinition
        {
            Name = "PUSH_OVHD_CALLS_MECH",
            DisplayName = "Call MECH",
            Type = SimConnect.SimVarType.LVar,
            IsMomentary = true,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
```

- [ ] **Step 2:** Convert `A32NX_CALLS_EMER_ON` (lines ~1247–1253) from a bare button def to an Off/On combo by adding value descriptions:

```csharp
        ["A32NX_CALLS_EMER_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_CALLS_EMER_ON",
            DisplayName = "Emergency Call",
            Type = SimConnect.SimVarType.LVar,
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
```
(With ValueDescriptions present it renders as a combo; the write routes through the A32NX_ calc-path catch-all at ~7738.)

- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "fix(a32nx): CALLS buttons latched at 1 forever (endless mech horn) - momentary reset + EMER combo"`

### Task 4: A32NX FPA set scaling (×10, not ×100) + unify the VS/FPA set paths

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:7582-7613 (panel branch), 7900-7913 (SetFCUVSValue)`

The FCU consumes FPA×10 (`FcuComputer.cpp:1009-1014`, docs "15 for 1.5°"); `SetFCUVSValue` sends ×100 (every FPA ≥1.0° saturates at 9.9°). The panel branch uses ×10 correctly but pushes negatives through an undefined-behavior `(uint)` cast. Fix the window path, then delegate the panel path to it.

- [ ] **Step 1:** Replace the body line in `SetFCUVSValue`:

```csharp
        int toSend = Math.Abs(value) < 100 ? (int)(value * 100) : (int)value;
```
with:
```csharp
        // FPA is sent ×10 per the FBW protocol (FcuComputer consumes vs_fpa/10 in
        // TRK/FPA mode; a320-events.md: "FPA * 10, i.e. 15 for 1.5 degrees").
        // Edge case: FPA exactly -0.1° encodes to -1, the FCU's "no input" sentinel,
        // and is silently ignored by the aircraft — unfixable protocol quirk.
        int toSend = Math.Abs(value) < 100 ? (int)Math.Round(value * 10) : (int)Math.Round(value);
```

- [ ] **Step 2:** Replace the entire `if (varKey == "A32NX.FCU_VS_SET")` panel branch (lines ~7582–7613, the block ending with `return true; // Handled (rejected)` and its closing brace) with:

```csharp
        // VS/FPA set — delegate to SetFCUVSValue: the calc-code K: path (negatives
        // can't go through SendEvent's uint cast) with the correct FPA ×10 scaling.
        if (varKey == "A32NX.FCU_VS_SET")
        {
            bool isValidVS = value >= -6000 && value <= 6000 && Math.Abs(value) >= 100;
            bool isValidFPA = value >= -9.9 && value <= 9.9;
            if (!isValidVS && !isValidFPA && value != 0)
            {
                announcer.AnnounceImmediate("Invalid value. VS: -6000 to 6000, FPA: -9.9 to 9.9");
                return true;
            }
            SetFCUVSValue(value, simConnect, announcer);
            return true;
        }
```

- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "fix(a32nx): FPA entry was scaled x100 (saturated at 9.9 deg) - FBW protocol is x10; route panel VS set through the safe calc path"`

### Task 5: A32NX approach-capability re-key to the FMGC discrete word

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs`

`A32NX_APPROACH_CAPABILITY` exists nowhere in FBW. The PFD derives LAND capability from `A32NX_FMGC_1_DISCRETE_WORD_4` bits 23/24/25 (`PFD/FMA.tsx:1610-1643`) — already registered as the `PFD_AUTOLAND` display var with a correct decode.

- [ ] **Step 1:** Delete the entire `["A32NX_APPROACH_CAPABILITY"]` def block (lines ~3231–3242, from `["A32NX_APPROACH_CAPABILITY"] = new SimConnect.SimVarDefinition` through its closing `},`).
- [ ] **Step 2:** Delete the `"A32NX_APPROACH_CAPABILITY",` entry from the PFD display list (line ~5195).
- [ ] **Step 3:** Make `PFD_AUTOLAND` continuously monitored so the hotkey cache is fresh and capability changes announce live. In the `["PFD_AUTOLAND"]` block (lines ~2653–2659) change `UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,` to:

```csharp
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
```

- [ ] **Step 4:** Add a ProcessSimVarUpdate branch (place it near the other `if (varName == ...)` branches in `ProcessSimVarUpdate`; the raw ARINC word must never reach the generic announcer) plus a cache field next to the other private fields:

```csharp
    private string? _lastAutolandCap; // last decoded LAND capability ("none"/"LAND 2"/...)
```
```csharp
        // Autoland capability (FMGC FG discrete word 4, bits 23/24/25). Announce
        // decoded transitions only; suppress the raw ARINC word from the generic path.
        if (varName == "A32NX_FMGC_1_DISCRETE_WORD_4")
        {
            var w = new SimConnect.Arinc429Word(value);
            string cap = (!w.IsNormalOperation && !w.IsFunctionalTest) ? "none"
                : w.BitValueOr(25, false) ? "LAND 3 dual"
                : w.BitValueOr(24, false) ? "LAND 3 single"
                : w.BitValueOr(23, false) ? "LAND 2" : "none";
            if (_lastAutolandCap != null && _lastAutolandCap != cap && cap != "none")
                announcer.Announce($"Approach capability {cap}");
            _lastAutolandCap = cap;
            return true;
        }
```

- [ ] **Step 5:** Replace the body of `HandleReadApproachCapability` (lines ~6008–6029) with:

```csharp
    private void HandleReadApproachCapability(SimConnect.SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        // A32NX_APPROACH_CAPABILITY no longer exists in FBW — decode the FMGC FG
        // discrete word 4 (same source the PFD FMA uses).
        var cachedValue = simConnect.GetCachedVariableValue("PFD_AUTOLAND");
        if (cachedValue.HasValue)
        {
            var w = new SimConnect.Arinc429Word(cachedValue.Value);
            string cap = (!w.IsNormalOperation && !w.IsFunctionalTest) ? "none computed"
                : w.BitValueOr(25, false) ? "LAND 3 dual"
                : w.BitValueOr(24, false) ? "LAND 3 single"
                : w.BitValueOr(23, false) ? "LAND 2" : "none computed";
            announcer.AnnounceImmediate($"Approach capability: {cap}");
        }
        else
        {
            announcer.AnnounceImmediate("Approach capability not available");
        }
    }
```

- [ ] **Step 6:** Grep `A32NX_APPROACH_CAPABILITY` across `MSFSBlindAssist\` — expect zero remaining references in the A320 file (the A380's own references are handled in Task 24). **Build** → succeeded.
- [ ] **Step 7: Commit:** `git commit -am "fix(a32nx): approach capability re-keyed to FMGC discrete word 4 (A32NX_APPROACH_CAPABILITY no longer exists)"`

### Task 6: A32NX GPWS warning lights re-point to the Rust EGPWS vars

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:4421-4449`

The A32NX GPWS moved to the Rust EGPWS, which publishes `A32NX_GPWS_WARNING_LIGHT_ON` / `A32NX_GPWS_ALERT_LIGHT_ON` (`fbw-common .../surveillance/egpws/mod.rs:82-86`); the old `A32NX_GPWS_Warning_Active` / `A32NX_GPWS_GS_Warning_Active` have no writer.

- [ ] **Step 1:** In the `["A32NX_GPWS_Warning_Active"]` block change the key AND Name to `A32NX_GPWS_WARNING_LIGHT_ON` (DisplayName stays "Master GPWS Warning Light"). In the `["A32NX_GPWS_GS_Warning_Active"]` block change key AND Name to `A32NX_GPWS_ALERT_LIGHT_ON` and DisplayName to `"GPWS Alert Light"` (the amber alert light — covers the old glideslope-light role).
- [ ] **Step 2:** Grep `GPWS_Warning_Active|GPWS_GS_Warning_Active` across `MSFSBlindAssist\` and fix any other references (display lists, monitor-manager seeds).
- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "fix(a32nx): GPWS light monitors re-pointed to the Rust EGPWS _LIGHT_ON vars (old vars have no writer)"`

### Task 7: A32NX GEN 1 / GEN 2 / APU GEN — rebuild on stock simvars + toggle events

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:128-148 (defs), HandleUIVariableSet (new branch)`

Source-confirmed mirror trap: the Rust electrical system copies the STOCK simvars into aspects each tick (`a320_systems_wasm/lib.rs:423-440`); the cockpit buttons fire `K:TOGGLE_ALTERNATOR#ID#` / `K:APU_GENERATOR_SWITCH_TOGGLE` (`A32NX_Interior_Elec.xml:11,28`). The current L:var combos drive nothing.

- [ ] **Step 1:** Replace the three def blocks (lines ~128–148, keys `A32NX_OVHD_ELEC_ENG_GEN_1_PB_IS_ON`, `_GEN_2_`, `A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON`) with stock-simvar-backed combos (keep the same keys so panel lists don't change — only `Name`/`Type` change):

```csharp
        // ---- ELEC: generators. The FBW Rust system reads the STOCK simvars (copied
        // to aspects each tick — lib.rs:423-440); the _PB_IS_ON L:vars are dead
        // mirrors. State = stock simvar, set = toggle event when desired != current
        // (HandleUIVariableSet branch).
        ["A32NX_OVHD_ELEC_ENG_GEN_1_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "GENERAL ENG MASTER ALTERNATOR:1", DisplayName = "Generator 1",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_ENG_GEN_2_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "GENERAL ENG MASTER ALTERNATOR:2", DisplayName = "Generator 2",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "APU GENERATOR SWITCH", DisplayName = "APU Generator",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
```

- [ ] **Step 2:** Add a `HandleUIVariableSet` branch (place next to the `ENG_ANTI_ICE` branch at ~7558, BEFORE the calc-path catch-all — these keys start with `A32NX_` and must not fall through to a dead L:var write):

```csharp
        // Generators: toggle the stock event only when desired != current (no SET
        // event exists). The Rust elec system reads the stock simvars, not L:vars.
        if (varKey == "A32NX_OVHD_ELEC_ENG_GEN_1_PB_IS_ON" || varKey == "A32NX_OVHD_ELEC_ENG_GEN_2_PB_IS_ON"
            || varKey == "A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn)
            {
                if (varKey == "A32NX_OVHD_ELEC_APU_GEN_PB_IS_ON")
                    simConnect.SendEvent("APU_GENERATOR_SWITCH_TOGGLE");
                else
                    simConnect.SendEvent(varKey.Contains("_GEN_2_") ? "TOGGLE_ALTERNATOR2" : "TOGGLE_ALTERNATOR1");
            }
            return true;
        }
```

- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "fix(a32nx): GEN/APU GEN combos wrote dead mirror L:vars - rebuilt on stock simvars + TOGGLE_ALTERNATOR/APU_GENERATOR_SWITCH_TOGGLE"`

### Task 8: A32NX wipers — rebuild on electrical circuits 77/80

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:1203-1216 (defs), HandleUIVariableSet (new branch)`

`XMLVAR_A320_WiperSwitch_*` doesn't exist in FBW. The wiper knobs are `FBW_Airbus_Wiper_Knob` with `CIRCUIT_ID_WIPERS` **77 (Captain)** / **80 (F/O)** (`A320_NEO_INTERIOR.xml:3299-3320`, `systems.cfg:455,463`): OFF/ON = `ELECTRICAL_CIRCUIT_TOGGLE`, speed = `ELECTRICAL_CIRCUIT_POWER_SETTING_SET` (75 = slow, 100 = fast) — the exact A380 wiper pattern.

- [ ] **Step 1:** Replace the two wiper def blocks (lines ~1203–1216) with circuit-backed combos (same keys, new Name/Type):

```csharp
        // ---- Wipers: FBW_Airbus_Wiper_Knob drives electrical circuit 77 (Captain)
        // / 80 (F/O): off/on = ELECTRICAL_CIRCUIT_TOGGLE, speed = circuit power
        // setting 75 (slow) / 100 (fast). Same pattern as the A380 wipers (141/143).
        // State combo reads the circuit switch; the speed half is write-only (the
        // combo's set re-derives both). XMLVAR_A320_WiperSwitch_* does not exist.
        ["XMLVAR_A320_WiperSwitch_1"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT SWITCH ON:77", DisplayName = "Wiper Captain",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Slow", [2] = "Fast" }
        },
        ["XMLVAR_A320_WiperSwitch_2"] = new SimConnect.SimVarDefinition
        {
            Name = "CIRCUIT SWITCH ON:80", DisplayName = "Wiper First Officer",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Slow", [2] = "Fast" }
        },
```
NOTE: the circuit switch is a bool, so the combo can only show Off vs running (it reads 1 for both Slow and Fast). That is acceptable for a screen-reader combo — the set always drives the full state, and the spoken set confirmation comes from the combo selection itself. (Reading back the exact speed would need `CIRCUIT POWER SETTING:n`, which can be added later if wanted.)

- [ ] **Step 2:** Add a `HandleUIVariableSet` branch (next to the ENG_ANTI_ICE branch):

```csharp
        // Wipers: circuit 77 (Captain) / 80 (F/O). Off = circuit off; Slow/Fast =
        // circuit on + power setting 75/100 (the FBW_Airbus_Wiper_Knob sequence).
        if (varKey == "XMLVAR_A320_WiperSwitch_1" || varKey == "XMLVAR_A320_WiperSwitch_2")
        {
            int circuit = varKey.EndsWith("_2") ? 80 : 77;
            int pos = (int)Math.Round(value);
            bool wantOn = pos > 0;
            bool isOn = (simConnect.GetCachedVariableValue(varKey) ?? 0) > 0.5;
            if (wantOn != isOn)
                simConnect.ExecuteCalculatorCode($"{circuit} (>K:ELECTRICAL_CIRCUIT_TOGGLE)");
            if (wantOn)
                simConnect.ExecuteCalculatorCode($"{(pos >= 2 ? 100 : 75)} {circuit} (>K:2:ELECTRICAL_CIRCUIT_POWER_SETTING_SET)");
            return true;
        }
```

- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "fix(a32nx): wipers wrote a nonexistent XMLVAR - rebuilt on electrical circuits 77/80 (A380 pattern)"`

### Task 9: A32NX dome light — rebuild on the stock potentiometer

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:4927-4932 (def), HandleUIVariableSet (new branch)`

`A32NX_OVHD_INTLT_DOME` is A380-only. The A32NX DOME switch fires `CABIN_LIGHTS_SET` + `LIGHT_POTENTIOMETER_7_SET` with state from `LIGHT POTENTIOMETER:7` (100=BRT, 20=DIM, 0=OFF — `A320_NEO_INTERIOR.xml:2142-2160`).

- [ ] **Step 1:** Replace the def block:

```csharp
        // Dome light: stock 3-state switch — state = LIGHT POTENTIOMETER:7 (0 off /
        // 20 dim / 100 bright), set = CABIN_LIGHTS_SET + LIGHT_POTENTIOMETER_7_SET
        // (A320_NEO_INTERIOR.xml:2142). A32NX_OVHD_INTLT_DOME is an A380-only var.
        ["A32NX_OVHD_INTLT_DOME"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT POTENTIOMETER:7", DisplayName = "Dome Light",
            Type = SimConnect.SimVarType.SimVar, Units = "percent",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [20] = "Dim", [100] = "Bright" }
        },
```

- [ ] **Step 2:** Add a `HandleUIVariableSet` branch:

```csharp
        // Dome light: combo values are the potentiometer percents (0/20/100).
        if (varKey == "A32NX_OVHD_INTLT_DOME")
        {
            int pct = (int)Math.Round(value);
            simConnect.ExecuteCalculatorCode($"{(pct > 0 ? 1 : 0)} (>K:2:CABIN_LIGHTS_SET) {pct} (>K:LIGHT_POTENTIOMETER_7_SET)");
            return true;
        }
```
NOTE the cockpit's `CABIN_LIGHTS_SET` template uses `1 (>K:2:CABIN_LIGHTS_SET)` — the `K:2:` form passes (value, circuit-index). Mirror the cockpit XML exactly: `1 (>K:2:CABIN_LIGHTS_SET)` for on, `0 (>K:2:CABIN_LIGHTS_SET)` for off. If the build of the RPN string above is unclear, fire them as two separate `ExecuteCalculatorCode` calls.

- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "fix(a32nx): dome light wrote an A380-only L:var - rebuilt on LIGHT POTENTIOMETER:7 + stock K-events"`

### Task 10: A32NX Ram Air control re-point

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:4622-4627, panel line ~5407`

`A32NX_OVHD_COND_RAM_AIR_PB_IS_ON` is the A380 name; the A32NX control var is `A32NX_AIRCOND_RAMAIR_TOGGLE` (read by `PseudoFWC.ts:3111`), which MSFSBA already reads in the BLEED SD row.

- [ ] **Step 1:** In the def block change the key and Name from `A32NX_OVHD_COND_RAM_AIR_PB_IS_ON` to `A32NX_AIRCOND_RAMAIR_TOGGLE` (keep DisplayName "Ram Air" and the Off/On descriptions; the calc-path catch-all handles the write).
- [ ] **Step 2:** Update the panel entry at line ~5407 (`"A32NX_OVHD_COND_RAM_AIR_PB_IS_ON"` in Air Conditioning) to `"A32NX_AIRCOND_RAMAIR_TOGGLE"`.
- [ ] **Step 3:** Grep `A32NX_OVHD_COND_RAM_AIR_PB_IS_ON` in the A320 file — zero remaining. **Build** → succeeded.
- [ ] **Step 4: Commit:** `git commit -am "fix(a32nx): Ram Air control wrote the A380 var name - re-pointed to A32NX_AIRCOND_RAMAIR_TOGGLE"`

### Task 11: A32NX runway turn-off lights — cockpit-faithful TAXI_LIGHTS_SET

**Files:** Modify: `MSFSBlindAssist\MainForm.cs:5615-5638`, `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:418-443`

Toggling circuits 21/22 directly desyncs `LIGHT TAXI:2/3` (which the cockpit switch STATE_TEST, presets, and EFB read). The cockpit RWY switch fires indexed `TAXI_LIGHTS_SET` against `LIGHT TAXI:2`/`:3` (`A320_NEO_INTERIOR.xml:1880-1898`).

- [ ] **Step 1:** Change the two defs to read the light simvars instead of the circuits. In `["CIRCUIT_SWITCH_ON:21"]` change `Name = "CIRCUIT SWITCH ON:21"` to `Name = "LIGHT TAXI:2"`; in `["CIRCUIT_SWITCH_ON:22"]` change to `Name = "LIGHT TAXI:3"`. Keep keys/DisplayNames (panel list untouched). Update the comment block above them to describe the new mechanism.
- [ ] **Step 2:** In MainForm.cs replace the body of the `else if (capturedVarKey == "CIRCUIT_SWITCH_ON:21")` branch so both sides are set via the indexed event (replacing both `SendEvent("ELECTRICAL_CIRCUIT_TOGGLE", ...)` decisions):

```csharp
                            else if (capturedVarKey == "CIRCUIT_SWITCH_ON:21") // Runway Turn Off Lights (single switch -> both lights)
                            {
                                // Cockpit-faithful: the RWY switch fires indexed TAXI_LIGHTS_SET
                                // against LIGHT TAXI:2/:3 (the gear-gated circuits follow the
                                // light state). Toggling the circuits directly desynced the
                                // LIGHT TAXI simvars the cockpit/presets/EFB read.
                                int on = selectedValue == 1 ? 1 : 0;
                                simConnectManager?.ExecuteCalculatorCode($"2 {on} (>K:2:TAXI_LIGHTS_SET)");
                                simConnectManager?.ExecuteCalculatorCode($"3 {on} (>K:2:TAXI_LIGHTS_SET)");
                                simConnectManager?.RequestVariable("CIRCUIT_SWITCH_ON:21", forceUpdate: true);
                                simConnectManager?.RequestVariable("CIRCUIT_SWITCH_ON:22", forceUpdate: true);
                            }
```
(Check the surrounding branch for the exact `selectedValue` variable name before editing; mirror the existing code's null-conditional style. `K:2:TAXI_LIGHTS_SET` takes circuit-index + value — this is the exact `#INDEX# #STATE# (>K:2:TAXI_LIGHTS_SET)` form the cockpit template fires.)

- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "fix(a32nx): runway turn-off lights via indexed TAXI_LIGHTS_SET (circuit toggle desynced LIGHT TAXI:2/3)"`

### Task 12: A32NX thrust detents — target the FBW band centers

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:7489-7513`

Default calibration bands (`ThrottleAxisMapping.h:20-31`): REV [−1.00,−0.95], REV-IDLE [−0.85,−0.75], IDLE [−0.55,−0.45], CLB [−0.05,+0.05], FLX [+0.45,+0.55], TOGA [+0.95,+1.00]. Current −0.70 misses REV-IDLE entirely; −0.44/−0.10 sit just outside their bands.

- [ ] **Step 1:** In the thrust-detent block replace:

```csharp
            double[] detentAxis = { -1.0, -0.70, -0.44, -0.10, 0.53, 1.0 };
```
with:
```csharp
            // Band CENTERS of the FBW default calibration (ThrottleAxisMapping.h):
            // REV [-1,-0.95] / REV-IDLE [-0.85,-0.75] / IDLE [-0.55,-0.45] /
            // CLB [-0.05,0.05] / FLX [0.45,0.55] / TOGA [0.95,1]. The old -0.70 fell
            // in the gap between REV-IDLE and IDLE and never reached the detent.
            double[] detentAxis = { -1.0, -0.80, -0.50, 0.0, 0.50, 1.0 };
```
Also update the stale values in the comment block above the `if` (the "Reverse -1.0 / Rev Idle -0.70 / Idle -0.44 / Climb -0.10 / Flex-MCT 0.53" text).

- [ ] **Step 2: Build** → succeeded. **Step 3: Commit:** `git commit -am "fix(a32nx): thrust detent axis values target FBW default calibration band centers (Reverse Idle missed its band)"`

### Task 13: A32NX fire-agent interlock + crossfeed OPEN/CLOSE

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:562-596 + 864-870 + HandleUIVariableSet`

(a) The cockpit agent button only discharges when the fire handle is pulled and is one-way (`A32NX_Interior_Fire.xml:29-41`); MSFSBA's combos can discharge with the handle stowed and "un-discharge" a bottle. (b) Crossfeed is a stateless `FUELSYSTEM_VALVE_TOGGLE` button; convert to the proven OPEN/CLOSE + state-combo pattern (valve id 3, `flight_model.cfg:145`).

- [ ] **Step 1:** Add a `HandleUIVariableSet` branch BEFORE the A32NX_ calc-path catch-all:

```csharp
        // Fire-agent discharge: the cockpit button only fires when the matching fire
        // handle is PULLED, and a discharged squib can never be un-discharged. Mirror
        // both interlocks (A32NX_Interior_Fire.xml FBW_Airbus_FIRE_AGENT).
        if (varKey.StartsWith("A32NX_FIRE_", StringComparison.Ordinal) && varKey.EndsWith("_Discharge", StringComparison.Ordinal))
        {
            if (value < 0.5) { announcer.AnnounceImmediate("Agent bottles cannot be reset."); return true; }
            string handleVar = varKey.Contains("_APU_") ? "A32NX_FIRE_BUTTON_APU"
                : varKey.Contains("_ENG2_") ? "A32NX_FIRE_BUTTON_ENG2" : "A32NX_FIRE_BUTTON_ENG1";
            bool handlePulled = (simConnect.GetCachedVariableValue(handleVar) ?? 0) > 0.5;
            if (!handlePulled)
            {
                announcer.AnnounceImmediate("Pull the fire handle first.");
                return true;
            }
            simConnect.ExecuteCalculatorCode($"1 (>L:{varKey})");
            return true;
        }
```

- [ ] **Step 2:** Convert the crossfeed. Replace the `["FUELSYSTEM_VALVE_TOGGLE:3"]` Event def (lines ~864–870) with a state combo:

```csharp
        // Fuel crossfeed: state = the stock valve switch; set fires
        // FUELSYSTEM_VALVE_OPEN/_CLOSE (id 3) — the TOGGLE event could stick
        // mid-transition (same fix as the A380 crossfeeds).
        ["FUEL_XFEED"] = new SimConnect.SimVarDefinition
        {
            Name = "FUELSYSTEM VALVE SWITCH:3", DisplayName = "Fuel Crossfeed",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Closed", [1] = "Open" }
        },
```
Then grep `FUELSYSTEM_VALVE_TOGGLE:3` for the panel entry that references it (Fuel panel) and replace with `"FUEL_XFEED"`. Add the set branch next to the ENGINE_MASTER branch (~7527):

```csharp
        if (varKey == "FUEL_XFEED")
        {
            simConnect.SendEvent(value > 0.5 ? "FUELSYSTEM_VALVE_OPEN" : "FUELSYSTEM_VALVE_CLOSE", 3);
            return true;
        }
```

- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "feat(a32nx): fire-agent handle interlock + crossfeed as stateful OPEN/CLOSE combo"`

### Task 14: A32NX LS (ILS) buttons — re-point to the FCU event + light var (dev FCU)

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs:3944-3955 + HandleUIVariableSet`

`A32NX_EFIS_{L,R}_LS_BUTTON_IS_ON` has zero references in dev FBW. The control is the registered input event `A32NX.FCU_EFIS_{L,R}_LS_PUSH` (`SimConnectInterface.cpp:330/342`), state = `A32NX_FCU_EFIS_{L,R}_LS_LIGHT_ON` (`FlyByWireInterface.cpp:849`).

- [ ] **Step 1:** Replace the two def blocks (keep the keys so panel lists stay valid):

```csharp
        // LS (ILS) pushbutton per side — dev FCU: control = A32NX.FCU_EFIS_*_LS_PUSH
        // input event, state = the FCU's *_LS_LIGHT_ON output. The old
        // A32NX_EFIS_*_LS_BUTTON_IS_ON L:var no longer exists.
        ["A32NX_EFIS_L_LS_BUTTON_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_LS_LIGHT_ON", DisplayName = "ILS",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
        ["A32NX_EFIS_R_LS_BUTTON_IS_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_R_LS_LIGHT_ON", DisplayName = "ILS",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
```

- [ ] **Step 2:** Add the set branch (before the catch-all — these keys start with `A32NX_`):

```csharp
        // LS button: fire the FCU input event only when desired != current (toggle).
        if (varKey == "A32NX_EFIS_L_LS_BUTTON_IS_ON" || varKey == "A32NX_EFIS_R_LS_BUTTON_IS_ON")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue(varKey) ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn)
                simConnect.SendEvent(varKey.Contains("_L_") ? "A32NX.FCU_EFIS_L_LS_PUSH" : "A32NX.FCU_EFIS_R_LS_PUSH");
            return true;
        }
```

- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "fix(a32nx): LS buttons re-built on A32NX.FCU_EFIS_*_LS_PUSH + LS_LIGHT_ON (old L:var removed in dev FCU)"`

### Task 15: A32NX dead-weight removals

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs`, `MSFSBlindAssist\SimConnect\SimConnectManager.cs:5222-5258`

Remove, with ALL references each (def + panel/display lists + any branch). After each sub-step grep the key to confirm zero references remain.

- [ ] **Step 1: Lighting presets** — delete defs `A32NX_LIGHTING_PRESET_LOAD`/`_SAVE` (lines ~1104–1116) and their two entries in the `["Interior Lighting"]` panel list (~5428–5429). A380 keeps its own (different file).
- [ ] **Step 2: Cargo Air panel** — delete the three `A32NX_OVHD_CARGO_AIR_*` defs (~4750–4768), the `["Cargo Air"]` BuildPanelControls list (~5418–5423), and the `"Cargo Air"` string from the `["Overhead"]` structure list (~5285).
- [ ] **Step 3: Audio Control Panel** — delete the six `A32NX_RMP_{L,R}_VHF{1,2,3}_VOLUME` defs (~957–995), both `["Audio Control Panel Captain"]`/`["... First Officer"]` lists (~5676–5683), and the two panel names from the `["Pedestal"]` structure list (~5288). (The vars don't exist in FBW; the physical ACP is unmodeled.)
- [ ] **Step 4: ECAM STATUS feature** — delete all 36 `A32NX_STATUS_{LEFT,RIGHT}_LINE_*` defs incl. both header comments (lines ~3506–3763), and delete the entire `RequestStatusMessages()` method in `SimConnectManager.cs` (~5222–5258; it has no live caller — `Forms/A32NX/StatusDisplayForm.cs` doesn't exist).
- [ ] **Step 5: Misc dead vars** — delete the def blocks for: `A32NX_FMGC_1_CRUISE_FLIGHT_LEVEL` (~2877–2884, no consumer), `A32NX_EFIS_1_ND_FM_MESSAGE_FLAGS` (~3774–3782, never-existing duplicate; the real `A32NX_EFIS_L_...` stays), `A32NX_FMA_CRUISE_ALT_MODE` (~3356–3367, A380-only writer), `A32NX_FMGC_1_PRESEL_SPEED` + `_PRESEL_MACH` (~2827–2842, unused + would need ARINC decode; the legacy `A32NX_SpeedPreselVal`/`MachPreselVal` panel readouts stay), `A32NX_FAC_1_V_FE_NEXT` (~4360–4367, unused; VFE readout uses `A32NX_SPEEDS_VFEN`).
- [ ] **Step 6: APU N2 row** — in `SdSystemRows` page 4 delete the line `r.Add(("APU N2", "A32NX_APU_N2", PctAir));` (~6295; the APS3200 is single-spool, always 0). Also delete the `A32NX_APU_N2` def if it exists only for this row (grep first).
- [ ] **Step 7: Dead light/event defs** — delete `["LIGHT STROBE"]` (~381–387, mistyped LVar, Never-registered), `["LANDING_LIGHTS_ON"]`/`["LANDING_LIGHTS_OFF"]` (~1753–1765, unreferenced duplicates of the _THIRD_PARTY pair), `["CIRCUIT_SWITCH_ON_17"]`…`_20` (~1766–1793, vestigial), `["LIGHT_TAXI"]` (~1794–1799, bogus event name).
- [ ] **Step 8: Inert recorder/ELT/rain-repellent controls** (cockpit-XML-only, zero system consumers in dev FBW; same policy as the A380 INOP list) — delete defs + panel entries for `A32NX_RCDR_GROUND_CONTROL_ON` (~4771, panel 5433), `A32NX_RCDR_TEST` (~4795, panel 5434), `A32NX_ELT_ON` (~4777, panel 5435), `A32NX_ELT_TEST_RESET` (~4860, panel 5436), `A32NX_RAIN_REPELLENT_LEFT_ON`/`_RIGHT_ON` (~4805/4811, Wipers panel entries 5459–5460). KEEP `A32NX_DFDR_EVENT_ON` (real consumer).
- [ ] **Step 9: Build** → succeeded. Grep each removed key across `MSFSBlindAssist\` → zero hits (ignore the A380 file's same-named-but-separate registrations noted in Task 21).
- [ ] **Step 10: Commit:** `git commit -am "refactor(a32nx): remove dead controls/readouts (presets, cargo air, ACP volumes, STATUS page, inert recorder/ELT/rain-repellent, misc dead vars)"`

# Phase B — A32NX additions (all source-verified pilot inputs / published outputs)

### Task 16: A32NX small missing controls + EXT PWR readout

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs`

- [ ] **Step 1: Probe & Window Heat.** Add next to the Anti Ice defs (and add the key to the `["Anti Ice"]` panel list):

```csharp
        // PROBE & WINDOW HEAT PB: A32NX_MAN_PITOT_HEAT (A32NX_Interior_Misc.xml:263).
        // Auto logic forces heat on with engines running, so an Off set can revert —
        // correct aircraft behaviour (same as the A380 probe heat).
        ["A32NX_MAN_PITOT_HEAT"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_MAN_PITOT_HEAT", DisplayName = "Probe and Window Heat",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Auto", [1] = "On" }
        },
```
(Write goes through the existing A32NX_ calc-path catch-all.)

- [ ] **Step 2: Pressurization LDG ELEV knob.** Add a numeric-input def (keys containing `_SET` render as numeric input boxes in MainForm) + a set branch. Def, next to the Pressurization defs; add `"PRESS_LDG_ELEV_SET"` to the `["Pressurization"]` panel list:

```csharp
        // LDG ELEV knob: Rust ValueKnob input read every frame (air_conditioning.rs:987).
        // -4000 = AUTO detent, else landing elevation in feet.
        ["PRESS_LDG_ELEV_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_OVHD_PRESS_LDG_ELEV_KNOB", DisplayName = "Landing Elevation (feet, -4000 = Auto)",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            Units = "number"
        },
```
`HandleUIVariableSet` branch (before the catch-all):
```csharp
        // LDG ELEV: numeric feet, or -4000 for the AUTO detent. Range-check per the knob.
        if (varKey == "PRESS_LDG_ELEV_SET")
        {
            if (value != -4000 && (value < -2000 || value > 15000))
            {
                announcer.AnnounceImmediate("Landing elevation must be -2000 to 15000 feet, or -4000 for Auto.");
                return true;
            }
            simConnect.ExecuteCalculatorCode($"{(int)Math.Round(value)} (>L:A32NX_OVHD_PRESS_LDG_ELEV_KNOB)");
            announcer.Announce(value == -4000 ? "Landing elevation Auto" : $"Landing elevation {value:0} feet");
            return true;
        }
```

- [ ] **Step 3: ECAM ND XFR knob.** Add next to the other source-switching knobs and to the `["Source Switching"]` panel list (grep `A32NX_ECAM_ND_XFR_SWITCHING_KNOB` is read by PseudoFWC.ts:3292; same 0/1/2 family as ATT HDG):

```csharp
        ["A32NX_ECAM_ND_XFR_SWITCHING_KNOB"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_ECAM_ND_XFR_SWITCHING_KNOB", DisplayName = "ECAM ND XFR",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Captain", [1] = "Normal", [2] = "First Officer" }
        },
```

- [ ] **Step 4: Standby compass light.** Add + put in the `["Interior Lighting"]` panel list:

```csharp
        ["A32NX_STBY_COMPASS_LIGHT_TOGGLE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_STBY_COMPASS_LIGHT_TOGGLE", DisplayName = "Standby Compass Light",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
```

- [ ] **Step 5: EXT PWR available readout** (same fix as the A380 — the L:var name with `:1` mis-parses on the data-def path; use the stock simvar). Add + append to the `["ELEC"]` panel/display list:

```csharp
        ["EXT_PWR_AVAILABLE"] = new SimConnect.SimVarDefinition
        {
            Name = "EXTERNAL POWER AVAILABLE:1", DisplayName = "External Power Available",
            Type = SimConnect.SimVarType.SimVar, Units = "bool",
            UpdateFrequency = SimConnect.UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Not available", [1] = "Available" }
        },
```

- [ ] **Step 6: Build** → succeeded. **Step 7: Commit:** `git commit -am "feat(a32nx): probe heat, LDG ELEV knob, ECAM ND XFR, standby compass light, EXT PWR available"`

### Task 17: A32NX EFIS — NAVAID selectors, FO filter buttons, filter-light readbacks

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs`

The `A32NX_FCU_EFIS_{L,R}_NAVAID_{1,2}_MODE` vars are live FCU knob INPUTS read every frame (`FlyByWireInterface.cpp:2161-2171`; enum 0=Off, 1=ADF, 2=VOR) — same pattern as the working EFIS mode/range writes. The R-side filter push events are registered (`SimConnectInterface.cpp:348-352`). Filter/LS state is published as `*_LIGHT_ON` outputs.

- [ ] **Step 1: NAVAID selectors (4 combos).** Add defs (writes go through the A32NX_ catch-all) and append the L pair to the `["EFIS Captain"]` panel list (~5553 region), the R pair to `["EFIS First Officer"]` (~5564 region):

```csharp
        // EFIS NAVAID selectors (ADF/OFF/VOR x2 per side): the FCU_EFIS_* variants are
        // the live knob inputs (FlyByWireInterface.cpp:2161) — the bare A32NX_EFIS_*
        // NAVAID vars are computed outputs and stay read-only.
        ["A32NX_FCU_EFIS_L_NAVAID_1_MODE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_NAVAID_1_MODE", DisplayName = "Navaid 1 Selector",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "ADF", [2] = "VOR" }
        },
```
…repeat for `L_NAVAID_2_MODE` ("Navaid 2 Selector"), `R_NAVAID_1_MODE`, `R_NAVAID_2_MODE` (identical shape, key/Name swapped).

- [ ] **Step 2: FO filter buttons.** Locate the five L-side filter Event defs (grep `A32NX.FCU_EFIS_L_CSTR_PUSH`, lines ~1489–1515) and add five R-side twins (same shape, `_L_` → `_R_`, DisplayName unchanged — the panel name disambiguates). Append the five new keys to the `["EFIS First Officer"]` panel list in the same order as the Captain's.
- [ ] **Step 3: Filter/LS light readbacks.** Add ten read-only status defs (per side: CSTR/WPT/VORD/NDB/ARPT) and append to the matching EFIS panel lists after the push buttons:

```csharp
        ["A32NX_FCU_EFIS_L_CSTR_LIGHT_ON"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FCU_EFIS_L_CSTR_LIGHT_ON", DisplayName = "CSTR Filter",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            RenderAsReadOnlyStatus = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "On" }
        },
```
…repeat for WPT/VORD/NDB/ARPT × L/R (DisplayNames "WPT Filter" etc.).

- [ ] **Step 4: Build** → succeeded. **Step 5: Commit:** `git commit -am "feat(a32nx): EFIS NAVAID selectors, FO filter buttons, filter state readbacks"`

### Task 18: A32NX COM3 tuning + panel brightness knobs

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs`

- [ ] **Step 1: Read the existing COM1/COM2 RMP implementation** — grep `COM_STBY_RADIO_SET_HZ` and `COM2_RADIO_SWAP` in the file and read those def blocks, the `["RMP"]` panel list, and the index-aware set logic in `HandleUIVariableSet` (per CLAUDE.md: COM1 = un-numbered `COM_STBY_RADIO_SET_HZ`, COM2/3 = `COM{n}_STBY_RADIO_SET_HZ`; "Set Active" = set standby + 100 ms + swap).
- [ ] **Step 2: Clone the COM2 set-standby / set-active / XFER defs and handler cases for COM3** (`COM3_STBY_RADIO_SET_HZ` / `COM3_RADIO_SWAP` — the same stock events COM2 uses, live-verified family). Add `COM_ACTIVE_FREQUENCY:3` + `COM_STANDBY_FREQUENCY:3` defs mirroring the `:1`/`:2` ones (Continuous + IsAnnounced with the airband-gated announce — confirm the existing `ProcessSimVarUpdate` COM announce branch covers `:3` by extending its varName matching). Append the new keys to the `["RMP"]` panel list after the COM2 group.
- [ ] **Step 3: Brightness knobs.** Add six numeric-input defs (`_SET` keys) + one shared handler branch. Defs (append all six keys to the `["Interior Lighting"]` panel list):

```csharp
        // Panel brightness knobs — stock potentiometers (A320_NEO_INTERIOR.xml):
        // pedestal flood 76, main panel flood 85, glareshield flood 10/11,
        // glareshield integral 83, overhead integral 86. 0-100 percent.
        ["BRIGHT_PEDESTAL_SET"] = new SimConnect.SimVarDefinition
        {
            Name = "LIGHT POTENTIOMETER:76", DisplayName = "Pedestal Flood Brightness (0-100)",
            Type = SimConnect.SimVarType.SimVar, Units = "percent",
            UpdateFrequency = SimConnect.UpdateFrequency.OnRequest
        },
```
…repeat for `BRIGHT_MAINPANEL_SET` (85), `BRIGHT_GLARESHIELD_CAPT_SET` (10), `BRIGHT_GLARESHIELD_FO_SET` (11), `BRIGHT_GLARESHIELD_INTEG_SET` (83), `BRIGHT_OVERHEAD_INTEG_SET` (86).
Handler branch:
```csharp
        // Brightness knobs: percent 0-100 via the indexed potentiometer set event.
        if (varKey.StartsWith("BRIGHT_", StringComparison.Ordinal) && varKey.EndsWith("_SET", StringComparison.Ordinal))
        {
            int pot = varKey switch
            {
                "BRIGHT_PEDESTAL_SET" => 76, "BRIGHT_MAINPANEL_SET" => 85,
                "BRIGHT_GLARESHIELD_CAPT_SET" => 10, "BRIGHT_GLARESHIELD_FO_SET" => 11,
                "BRIGHT_GLARESHIELD_INTEG_SET" => 83, "BRIGHT_OVERHEAD_INTEG_SET" => 86,
                _ => -1
            };
            if (pot < 0) return false;
            int pct = Math.Clamp((int)Math.Round(value), 0, 100);
            simConnect.ExecuteCalculatorCode($"{pct} {pot} (>K:2:LIGHT_POTENTIOMETER_SET)");
            announcer.Announce($"{varDef.DisplayName.Split('(')[0].Trim()} {pct} percent");
            return true;
        }
```

- [ ] **Step 4: Build** → succeeded. **Step 5: Commit:** `git commit -am "feat(a32nx): COM3/VHF3 tuning + six panel brightness knobs"`

### Task 19: A32NX new readouts (SFCC, baro discrepancy, FMA triple click, PTU, TCAS state, DCDU)

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA320Definition.cs`

- [ ] **Step 1: SFCC actual flap/slat positions** (ARINC429 degrees, `A32NXSfccBusPublisher.ts:107-116`). Add two display defs using the generic ARINC auto-decode and append to the Flight Controls display list (grep the list containing `A32NX_FLAPS_HANDLE_INDEX`):

```csharp
        ["A32NX_SFCC_1_FLAP_ACTUAL_POSITION_WORD"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SFCC_1_FLAP_ACTUAL_POSITION_WORD", DisplayName = "Flaps Actual Position",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "degrees", Arinc429Format = "0.0"
        },
        ["A32NX_SFCC_1_SLAT_ACTUAL_POSITION_WORD"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_SFCC_1_SLAT_ACTUAL_POSITION_WORD", DisplayName = "Slats Actual Position",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsArinc429 = true, Arinc429Unit = "degrees", Arinc429Format = "0.0"
        },
```

- [ ] **Step 2: Baro-reference discrepancy callout** (`A32NX_FWC_1_DISCRETE_WORD_124`: bit 24 = STD discrepancy, bit 25 = baro discrepancy — `PseudoFWC.ts:1845`). Register Continuous+IsAnnounced (no ValueDescriptions) and add a ProcessSimVarUpdate rising-edge branch + two cache fields:

```csharp
        ["A32NX_FWC_1_DISCRETE_WORD_124"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FWC_1_DISCRETE_WORD_124", DisplayName = "Baro Reference Discrepancy",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true
        },
```
```csharp
    private bool _baroStdDiscrep, _baroRefDiscrep; // FWC word 124 bits 24/25 (announced edges)
```
```csharp
        // FWC word 124: baro-reference discrepancy between the two sides (a blind
        // pilot can't glance at both PFDs to compare). Announce rising edges only.
        if (varName == "A32NX_FWC_1_DISCRETE_WORD_124")
        {
            var w = new SimConnect.Arinc429Word(value);
            bool stdD = w.BitValueOr(24, false), refD = w.BitValueOr(25, false);
            if (stdD && !_baroStdDiscrep) announcer.Announce("Baro standard mode discrepancy between sides");
            if (refD && !_baroRefDiscrep) announcer.Announce("Baro reference discrepancy between sides");
            _baroStdDiscrep = stdD; _baroRefDiscrep = refD;
            return true;
        }
```

- [ ] **Step 3: FMA triple click + PTU + TCAS state.** Add three Continuous+IsAnnounced defs in the situational-awareness block (~2290):

```csharp
        ["A32NX_FMA_TRIPLE_CLICK"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_FMA_TRIPLE_CLICK", DisplayName = "FMA mode reversion cue",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [1] = "Mode reversion, check FMA" }
        },
        ["A32NX_HYD_PTU_ON_ECAM_MEMO"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_HYD_PTU_ON_ECAM_MEMO", DisplayName = "PTU transferring",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "PTU stopped", [1] = "PTU transferring" }
        },
        ["A32NX_TCAS_STATE"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_TCAS_STATE", DisplayName = "TCAS advisory",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string>
            { [0] = "clear of conflict", [1] = "traffic advisory", [2] = "resolution advisory" }
        },
```
(`A32NX_TCAS_STATE` is published by `TcasComputer.ts:263`; the 2→0 transition announces "clear of conflict". The first-detect grace suppresses the baseline 0.)

- [ ] **Step 4: DCDU message waiting + ACK** (port of the A380's). Add defs + append `"A32NX_DCDU_ATC_MSG_WAITING"` to the Transponder display list and `"A32NX_DCDU_ATC_MSG_ACK"` to the `["Transponder"]` panel list:

```csharp
        // ATC datalink (DCDU) — CPDLC message-waiting announce + acknowledge (A380 parity).
        ["A32NX_DCDU_ATC_MSG_WAITING"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_DCDU_ATC_MSG_WAITING", DisplayName = "ATC Message Waiting",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.Continuous,
            IsAnnounced = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "No", [1] = "Message Waiting" }
        },
        ["A32NX_DCDU_ATC_MSG_ACK"] = new SimConnect.SimVarDefinition
        {
            Name = "A32NX_DCDU_ATC_MSG_ACK", DisplayName = "ATC Message Acknowledge",
            Type = SimConnect.SimVarType.LVar, UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
            IsMomentary = true, RenderAsButton = true, SuppressRestingButtonState = true,
            ValueDescriptions = new Dictionary<double, string> { [0] = "Off", [1] = "Activate" }
        },
```

- [ ] **Step 5: Build** → succeeded. **Step 6: Commit:** `git commit -am "feat(a32nx): SFCC surface positions, baro-discrepancy callout, triple click, PTU memo, TCAS advisory state, DCDU msg waiting + ACK"`

# Phase C — A380X definition fixes

### Task 20: A380 fire-squib var rename (drop the `OVHD_` prefix)

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA380Definition.cs:1687-1698 (defs), 3131-3142 (d["Fire"])`

Real names have no `OVHD_` prefix (`fire_and_smoke_protection.rs:621-623`, `overhead/fire.xml:31-32`). Only the squib READOUTS are wrong; the agent buttons (`OVHD_FIRE_AGENT_*`) are correct.

- [ ] **Step 1:** In the def loop (lines ~1687–1698) change both interpolated names:

```csharp
                ReadEnum($"A32NX_OVHD_FIRE_SQUIB_{b}_ENG_{n}_IS_ARMED", $"Engine {n} Agent {b} Squib", armedVd);
                ReadEnum($"A32NX_OVHD_FIRE_SQUIB_{b}_ENG_{n}_IS_DISCHARGED", $"Engine {n} Agent {b} Discharged", dischVd);
```
to:
```csharp
                ReadEnum($"A32NX_FIRE_SQUIB_{b}_ENG_{n}_IS_ARMED", $"Engine {n} Agent {b} Squib", armedVd);
                ReadEnum($"A32NX_FIRE_SQUIB_{b}_ENG_{n}_IS_DISCHARGED", $"Engine {n} Agent {b} Discharged", dischVd);
```
and the APU line `ReadEnum("A32NX_OVHD_FIRE_SQUIB_1_APU_1_IS_DISCHARGED", ...)` to `"A32NX_FIRE_SQUIB_1_APU_1_IS_DISCHARGED"`.

- [ ] **Step 2:** Mirror the same rename in the `d["Fire"]` list builder (lines ~3131–3142: `fire.Add($"A32NX_OVHD_FIRE_SQUIB_...")` → `A32NX_FIRE_SQUIB_...`).
- [ ] **Step 3:** Grep `OVHD_FIRE_SQUIB` → zero hits. **Build** → succeeded. **Step 4: Commit:** `git commit -am "fix(a380): fire squib readouts read nonexistent OVHD_-prefixed vars - real names have no OVHD_"`

### Task 21: A380 dead-control removals + window/display var fixes

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA380Definition.cs`, `MSFSBlindAssist\Forms\FBWA380\EisDisplayVars.cs`, `MSFSBlindAssist\Forms\FBWA380\FBWA380AutopilotWindow.cs`

- [ ] **Step 1: Avionics Blower/Extract (A32NX-only vars, no A380 consumer).** Delete lines ~2147–2151 (the two `OnOff` + two `ReadEnum` calls + comment), the AddRange at line ~2998 (`p["Ventilation"].AddRange(... BLOWER_TOGGLE, EXTRACT_TOGGLE)`), and the display AddRange at ~3236 (`d["Ventilation"].AddRange(... BLOWER_FAULT, EXTRACT_FAULT)`). Do NOT touch the A320 file's same-named entries (separately handled — actually the A320's blower/extract ARE real on the A32NX per the audit; only the A380's are dead).
- [ ] **Step 2: Door slides (no writer).** Delete `ReadEnum("A32NX_SLIDES_ARMED", ...)` (~2161), remove `"A32NX_SLIDES_ARMED"` from `d["Calls"]` (~3312, keep `A32NX_EVAC_COMMAND_FAULT`), and delete the SD Doors-page row at ~5778 (`r.Add(("Escape slides", "A32NX_SLIDES_ARMED", ...))`). ALSO remove the A320 file's parallel `A32NX_SLIDES_ARMED` references? NO — the A32NX writes it (pedestal audit: "computed output, read-only ✓"); A320 keeps it.
- [ ] **Step 3: INOP recorder/ELT/rain-repellent set.** Delete `OnOff("A32NX_RCDR_GROUND_CONTROL_ON", ...)` + `OnOff("A32NX_ELT_ON", ...)` (~710–711), `OnOff("A32NX_RCDR_TEST"...)` + `OnOff("A32NX_ELT_TEST_RESET"...)` + `OnOff("A32NX_RAIN_REPELLENT_LEFT_ON"...)` + `OnOff("A32NX_RAIN_REPELLENT_RIGHT_ON"...)` (~2171–2175, KEEP `OnOff("A32NX_DFDR_EVENT_ON"...)` at 2173 — real consumer in `FlyByWireInterface.cpp`), and the matching entries in `p["Recorder and Misc"]` (~2802) and its AddRange (~3000–3005, keep `A32NX_DFDR_EVENT_ON` + the NSS entries).
- [ ] **Step 4: Stall-warning monitor (dead on the A380).** Delete the `ReadEnum("A32NX_AUDIO_STALL_WARNING", ...)` call (~1335–1337) including its comment. Nothing on the A380X writes this var (FBW's `FwsSoundManager.ts:117` maps the stall aural to the wrong L:var — an upstream FBW bug; see the Pass-2 list for the upstream report + re-add once fixed).
- [ ] **Step 5: EisDisplayVars stale var.** In `Forms\FBWA380\EisDisplayVars.cs` `NavVars` replace `"A32NX_FMGC_TRUE_REF",` with `"A32NX_PUSH_TRUE_REF",` (the A380's real TRUE-REF input the def already uses).
- [ ] **Step 6: AutopilotWindow FD labels.** In `Forms\FBWA380\FBWA380AutopilotWindow.cs` `UpdateLabels()` replace:

```csharp
        bool fdL = (simConnect.GetCachedVariableValue("A32NX_FCU_EFIS_L_FD_ACTIVE") ?? 0) > 0.5;
        bool fdR = (simConnect.GetCachedVariableValue("A32NX_FCU_EFIS_R_FD_ACTIVE") ?? 0) > 0.5;
```
with (the def's FD combos already use these stock-simvar-backed keys):
```csharp
        bool fdL = (simConnect.GetCachedVariableValue("FD_1_CTL") ?? 0) > 0.5;
        bool fdR = (simConnect.GetCachedVariableValue("FD_2_CTL") ?? 0) > 0.5;
```

- [ ] **Step 7: Build** → succeeded; grep each removed key in the A380 file → zero hits. **Step 8: Commit:** `git commit -am "refactor(a380): remove dead controls (blower/extract, slides, INOP recorder set, dead stall monitor); fix EisDisplayVars TRUE_REF + AutopilotWindow FD source"`

### Task 22: A380 — the 12 fuel TRANSFER pumps

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA380Definition.cs:2111-2141 (pump loop), 2652-2662 (p["Fuel"])`

Identical circuit mechanism as the verified feed pumps; circuit IDs from `A380_COCKPIT.xml:2592-2740`: L OUTR 70, L MID FWD/AFT 71/72, L INR FWD/AFT 73/74, R OUTR 75, R MID FWD/AFT 76/77, R INR FWD/AFT 78/79, TRIM L/R 80/81.

- [ ] **Step 1:** Extend the existing pump tuple array (after the `("FUELPUMP_FEEDTK4_STBY", 69, ...)` line, inside the same `foreach`):

```csharp
            ("FUELPUMP_OUTR_L", 70, "Left Outer Tank Pump"),
            ("FUELPUMP_MID_L_FWD", 71, "Left Mid Tank Forward Pump"),
            ("FUELPUMP_MID_L_AFT", 72, "Left Mid Tank Aft Pump"),
            ("FUELPUMP_INR_L_FWD", 73, "Left Inner Tank Forward Pump"),
            ("FUELPUMP_INR_L_AFT", 74, "Left Inner Tank Aft Pump"),
            ("FUELPUMP_OUTR_R", 75, "Right Outer Tank Pump"),
            ("FUELPUMP_MID_R_FWD", 76, "Right Mid Tank Forward Pump"),
            ("FUELPUMP_MID_R_AFT", 77, "Right Mid Tank Aft Pump"),
            ("FUELPUMP_INR_R_FWD", 78, "Right Inner Tank Forward Pump"),
            ("FUELPUMP_INR_R_AFT", 79, "Right Inner Tank Aft Pump"),
            ("FUELPUMP_TRIM_L", 80, "Trim Tank Left Pump"),
            ("FUELPUMP_TRIM_R", 81, "Trim Tank Right Pump"),
```
The loop body (def shape, `_fuelPumpCircuits` fill) and the `HandleUIVariableSet` toggle branch handle the new entries automatically. Update the comment above to mention the transfer pumps + circuit range 70–81.

- [ ] **Step 2:** Append the 12 new keys to `p["Fuel"]` (after `"FUELPUMP_FEEDTK4_STBY"`), in the tuple order above.
- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "feat(a380): 12 fuel transfer-pump pushbuttons (circuits 70-81, same mechanism as the verified feed pumps)"`

### Task 23: A380 anti-skid made settable

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA380Definition.cs:970 (def), 3241-3242 (display), HandleUIVariableSet`

Cockpit template fires `K:ANTISKID_BRAKES_TOGGLE`, state `A:ANTISKID BRAKES ACTIVE` (`A32NX_Interior_Handling.xml:633-641`).

- [ ] **Step 1:** Move the var from display-only into a settable combo: the existing `Stock("ANTISKID_BRAKES_ACTIVE", ...)` def already carries the right state source and Off/On descriptions — add the key to a CONTROL panel list (`p["Flaps and Brakes"]` — grep `p["Flaps and Brakes"]` and append `"ANTISKID_BRAKES_ACTIVE"`), keeping the `d["Autobrake"]` display entry.
- [ ] **Step 2:** Add a `HandleUIVariableSet` branch (next to the seatbelt toggle-if-differs branch ~4427):

```csharp
        // Anti-skid: TOGGLE-only event; fire when desired != current.
        if (varKey == "ANTISKID_BRAKES_ACTIVE")
        {
            bool desiredOn = value > 0.5;
            bool currentOn = (simConnect.GetCachedVariableValue("ANTISKID_BRAKES_ACTIVE") ?? (desiredOn ? 0.0 : 1.0)) > 0.5;
            if (desiredOn != currentOn) simConnect.SendEvent("ANTISKID_BRAKES_TOGGLE");
            return true;
        }
```
NOTE: MainForm's SimVar-combo set path calls `HandleUIVariableSet` first (CLAUDE.md "Enabler" note), so a SimVar-state combo with a K-event control works.

- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "feat(a380): anti-skid switch settable (ANTISKID_BRAKES_TOGGLE, state from the stock simvar)"`

### Task 24: A380 readout additions + approach-capability re-key

**Files:** Modify: `MSFSBlindAssist\Aircraft\FlyByWireA380Definition.cs`

- [ ] **Step 1: New Mon readouts** — add next to the BTV/autobrake Mon block (~976):

```csharp
        Mon("A32NX_AUTOBRAKES_ACTIVE", "Autobrake",
            new Dictionary<double, string> { [0] = "not braking", [1] = "braking" });
        Mon("A32NX_ROW_ROP_LOST", "Runway Overrun Protection",
            new Dictionary<double, string> { [0] = "available", [1] = "lost" });
        Mon("A32NX_BTV_APPR_DIFFERENT_RUNWAY", "BTV Runway Check",
            new Dictionary<double, string> { [0] = "matches approach", [1] = "DIFFERENT RUNWAY - BTV armed for another runway" });
        Mon("A32NX_TCAS_STATE", "TCAS advisory", new Dictionary<double, string>
            { [0] = "clear of conflict", [1] = "traffic advisory", [2] = "resolution advisory" });
```
(Sources: `autobrakes.rs:232/651/235`, `BrakeToVacate.ts:68`, `LegacyTcasComputer.ts:281`.)

- [ ] **Step 2: Approach-capability re-key.** Delete the `Mon("A32NX_APPROACH_CAPABILITY", ...)` block (~1816–1819) and the `_apprCapMap` field (~5278–5279). Replace the hotkey case at ~6120:

```csharp
            case HotkeyAction.ReadApproachCapability: RequestReadout(simConnect, "A32NX_APPROACH_CAPABILITY", "Approach capability", "", _apprCapMap); return true;
```
with:
```csharp
            case HotkeyAction.ReadApproachCapability:
            {
                // A32NX_APPROACH_CAPABILITY doesn't exist — decode the FCDC FG word 4
                // (same source as the PFD_AUTOLAND display + the PFD FMA).
                var w4 = simConnect.GetCachedVariableValue("PFD_AUTOLAND");
                string cap = "not available";
                if (w4.HasValue)
                {
                    var w = new SimConnect.Arinc429Word(w4.Value);
                    cap = (!w.IsNormalOperation && !w.IsFunctionalTest) ? "none computed"
                        : w.BitValueOr(25, false) ? "LAND 3 dual"
                        : w.BitValueOr(24, false) ? "LAND 3 single"
                        : w.BitValueOr(23, false) ? "LAND 2" : "none computed";
                }
                announcer.AnnounceImmediate($"Approach capability: {cap}");
                return true;
            }
```
In the `["PFD_AUTOLAND"]` def (~1158–1166) change `UpdateFrequency = UpdateFrequency.OnRequest` to `UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true`, and add a ProcessSimVarUpdate transition-announce branch identical to Task 5 Step 4 but keyed on `varName == "A32NX_FCDC_1_FG_DISCRETE_WORD_4"` (add the `_lastAutolandCap` field). Remove `"A32NX_APPROACH_CAPABILITY"` from `d["FCU"]` (~3207) and `d["PFD"]` (~3267) — `PFD_AUTOLAND` already sits in `d["PFD"]`'s tail.

- [ ] **Step 3:** Grep `A32NX_APPROACH_CAPABILITY` repo-wide → zero hits (Task 5 removed the A320's). **Build** → succeeded.
- [ ] **Step 4: Commit:** `git commit -am "feat(a380): autobrake-active/ROW-ROP-lost/BTV-different-runway/TCAS-state readouts; approach capability re-keyed to FCDC word 4"`

# Phase D — Ctrl+B altimeter redesign (Fenix-style, both jets, dev-FCU correct)

The Fenix UX to mirror (`Forms\FenixA320\FenixBaroWindow.cs`): a **Mode combo (QNH / STD)** seeded from live state — selecting STD applies immediately to both sides and disables value entry; a **Unit combo (hPa / inHg)**; a **Value textbox** with Enter = Set; per-unit validation (745–1100 hPa / 22.00–32.99 inHg); announce "Setting altimeter to N" then a confirmation. Both FBW windows currently use a button-based layout and (each in its own way) broken unit/STD vars on dev FBW.

### Task 25: A320 baro window — Fenix-style rebuild with the dev-FCU unit var

**Files:** Rewrite: `MSFSBlindAssist\Forms\FBWA320\FBWA320BaroWindow.cs` (full replacement)

Keeps the proven write paths (`A32NX.FCU_EFIS_L/R_BARO_SET` hPa×16; `_BARO_PULL`=STD / `_BARO_PUSH`=QNH). Fixes the unit toggle: `XMLVAR_Baro_Selector_HPA_*` was REMOVED from the A32NX — the live input is `A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG` (cockpit knob toggles it, `A32NX_Interior_EFIS.xml:205-207`; FCU reads it every frame, `FlyByWireInterface.cpp:2165/2172`). STD state reads `A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE` (0=STD/1=hPa/2=inHg) as today.

- [ ] **Step 1:** Replace the file content with:

```csharp
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.FBWA320;

// A320 Baro window (Ctrl+B) — Fenix-style: Mode combo (QNH/STD, STD applies
// immediately and disables entry), Unit combo (hPa/inHg), value box, Enter = Set.
// Writes: A32NX.FCU_EFIS_L/R_BARO_SET (hPa*16) for the value; _BARO_PULL (STD) /
// _BARO_PUSH (QNH) knob events; unit = A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG via the
// calc path (the dev-FCU live input — XMLVAR_Baro_Selector_HPA_* was removed from
// the A32NX and is DEAD; do not revert to it). STD state reads
// A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE (0=STD/1=hPa/2=inHg).
public class FBWA320BaroWindow : FBWA320FCUWindowBase
{
    private readonly ComboBox modeCombo;
    private readonly ComboBox unitCombo;
    private readonly TextBox valueTextBox;
    private readonly Button setButton;
    private bool suppressModeEvent;
    private System.Windows.Forms.Timer? _modeTimer;

    public FBWA320BaroWindow(FlyByWireA320Definition aircraft, SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
        : base(aircraft, simConnect, announcer)
    {
        Text = "Set Altimeter (both sides)";
        Size = new Size(420, 220);

        var modeLabel = new Label { Text = "Mode:", Location = new Point(20, 25), Size = new Size(50, 20) };
        modeCombo = new ComboBox
        {
            Location = new Point(75, 22), Size = new Size(80, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 0,
            AccessibleName = "Baro Mode", AccessibleDescription = "Select QNH or STD mode"
        };
        modeCombo.Items.AddRange(new object[] { "QNH", "STD" });

        var unitLabel = new Label { Text = "Unit:", Location = new Point(170, 25), Size = new Size(40, 20) };
        unitCombo = new ComboBox
        {
            Location = new Point(215, 22), Size = new Size(160, 25),
            DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 1,
            AccessibleName = "Unit", AccessibleDescription = "Hectopascals or inches of mercury, both sides"
        };
        unitCombo.Items.AddRange(new object[] { "QNH (hPa)", "Inches (inHg)" });

        var valueLabel = new Label { Text = "Value:", Location = new Point(20, 70), Size = new Size(50, 20) };
        valueTextBox = new TextBox
        {
            Location = new Point(75, 67), Size = new Size(100, 25), TabIndex = 2,
            AccessibleName = "Altimeter value", AccessibleDescription = "Enter altimeter value, Enter to set"
        };
        valueTextBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; HandleSet(); } };
        setButton = new Button { Text = "Set", Location = new Point(190, 65), Size = new Size(90, 30), TabIndex = 3, AccessibleName = "Set Altimeter" };
        setButton.Click += (s, e) => HandleSet();
        var closeButton = new Button { Text = "Close", Location = new Point(130, 130), Size = new Size(140, 35), TabIndex = 4, DialogResult = DialogResult.OK, AccessibleName = "Close" };
        closeButton.Click += (s, e) => Close();

        Controls.AddRange(new Control[] { modeLabel, modeCombo, unitLabel, unitCombo, valueLabel, valueTextBox, setButton, closeButton });
        AcceptButton = setButton;
        CancelButton = closeButton;

        SeedFromSim();
        modeCombo.SelectedIndexChanged += ModeChanged;
        unitCombo.SelectedIndexChanged += UnitChanged;

        // Track cockpit-side changes (FCU knob) while the window is open.
        _modeTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _modeTimer.Tick += (s, e) => SeedFromSim();
        _modeTimer.Start();
    }

    private void SeedFromSim()
    {
        suppressModeEvent = true;
        try
        {
            // 0=STD, 1=hPa, 2=inHg; while STD the mode carries no unit info — keep the last unit.
            double mode = simConnect.GetCachedVariableValue("A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE") ?? 1;
            bool std = mode < 0.5;
            modeCombo.SelectedIndex = std ? 1 : 0;
            if (!std) unitCombo.SelectedIndex = mode >= 1.5 ? 1 : 0;
            else if (unitCombo.SelectedIndex < 0) unitCombo.SelectedIndex = 0;
            UpdateControlState();
        }
        finally { suppressModeEvent = false; }
    }

    private void UpdateControlState()
    {
        bool std = modeCombo.SelectedIndex == 1;
        unitCombo.Enabled = !std;
        valueTextBox.Enabled = !std;
        setButton.Enabled = !std;
    }

    private void ModeChanged(object? sender, EventArgs e)
    {
        if (suppressModeEvent) return;
        bool std = modeCombo.SelectedIndex == 1;
        // PULL = STD, PUSH = QNH — the cockpit knob events, both sides.
        string action = std ? "PULL" : "PUSH";
        simConnect.SendEvent($"A32NX.FCU_EFIS_L_BARO_{action}", 0);
        simConnect.SendEvent($"A32NX.FCU_EFIS_R_BARO_{action}", 0);
        announcer.AnnounceImmediate(std ? "Standard, both sides" : "QNH, both sides");
        UpdateControlState();
    }

    private void UnitChanged(object? sender, EventArgs e)
    {
        if (suppressModeEvent) return;
        bool inHg = unitCombo.SelectedIndex == 1;
        // Dev-FCU live unit input (read every frame by the FCU model).
        simConnect.ExecuteCalculatorCode($"{(inHg ? 1 : 0)} (>L:A32NX_FCU_EFIS_L_BARO_IS_INHG)");
        simConnect.ExecuteCalculatorCode($"{(inHg ? 1 : 0)} (>L:A32NX_FCU_EFIS_R_BARO_IS_INHG)");
        announcer.AnnounceImmediate(inHg ? "Inches of mercury, both sides" : "Hectopascals, both sides");
    }

    protected override void SpeakInitialReadout() { valueTextBox.Focus(); }

    protected override void OnFormClosing(FormClosingEventArgs e)
    { _modeTimer?.Stop(); _modeTimer?.Dispose(); _modeTimer = null; base.OnFormClosing(e); }

    private void HandleSet()
    {
        if (!setButton.Enabled) return;
        string input = valueTextBox.Text.Trim();
        if (!double.TryParse(input, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
        { announcer.AnnounceImmediate("Invalid number format"); valueTextBox.SelectAll(); return; }
        bool inHg = unitCombo.SelectedIndex == 1;
        double hpa = inHg ? v * 33.8639 : v;
        if (inHg ? (v < 22.00 || v > 32.99) : (hpa < 745 || hpa > 1100))
        {
            announcer.AnnounceImmediate(inHg ? "Value must be between 22.00 and 32.99 inches" : "Value must be between 745 and 1100 hectopascals");
            valueTextBox.SelectAll();
            return;
        }
        uint encoded = (uint)Math.Round(hpa * 16);
        simConnect.SendEvent("A32NX.FCU_EFIS_L_BARO_SET", encoded);
        simConnect.SendEvent("A32NX.FCU_EFIS_R_BARO_SET", encoded);
        announcer.AnnounceImmediate(inHg ? $"Altimeter set to {v:F2} inches, both sides" : $"Altimeter set to {hpa:F0} hectopascals, both sides");
        valueTextBox.SelectAll();
    }
}
```

- [ ] **Step 2:** Grep `XMLVAR_Baro_Selector_HPA` in the A320 def file — fix any remaining A320 writer/reader the same way (the panel unit combo already uses `A32NX_FCU_EFIS_*_BARO_IS_INHG` per the audit; if a stray XMLVAR reference exists on the A320, re-point or remove it).
- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "feat(a32nx): Fenix-style Ctrl+B altimeter dialog; unit toggle on the live BARO_IS_INHG input (XMLVAR removed in dev FBW)"`

### Task 26: A380 baro — dev-FCU STD migration + Fenix-style window

**Files:** Rewrite: `MSFSBlindAssist\Forms\FBWA380\FBWA380BaroWindow.cs`; Modify: `MSFSBlindAssist\Aircraft\FlyByWireA380Definition.cs` (IS_STD sites)

On dev FBW the `A32NX_FCU_{LEFT,RIGHT}_EIS_BARO_IS_STD` L:vars are GONE (zero occurrences). STD/QNH is driven by `H:A380X_EFIS_CP_BARO_{PULL,PUSH}_{1,2}` (PULL=STD, PUSH=QNH — `MsfsBaroManager.ts:38`) and read back via the stock `KOHLSMAN SETTING STD:n` simvar (`MsfsBaroManager.ts:56`) — robust on both FCU generations. QNH set stays `KOHLSMAN_SET`. Unit: write `XMLVAR_Baro_Selector_HPA_1` AND `_2` as today, but note dev FBW only honors `_1` (`BaroManager.ts:62` FIXME) — read the unit from `_1` only.

- [ ] **Step 1 — def: STD state re-key.** In `FlyByWireA380Definition.cs`:
  (a) Replace the two `Sel("A32NX_FCU_LEFT_EIS_BARO_IS_STD", ...)` / `Sel("...RIGHT...")` registrations (~1922–1924) and their comment with stock-simvar-backed combos (SAME keys, so panels/window references keep working):

```csharp
        // STD(pull)/QNH(push) per side. Dev FBW removed the *_EIS_BARO_IS_STD L:vars;
        // STD is now driven by the H:A380X_EFIS_CP_BARO_PULL/PUSH_{1,2} events
        // (MsfsBaroManager.ts) and read back from the stock KOHLSMAN SETTING STD:n.
        var baroStd = new Dictionary<double, string> { [0] = "QNH", [1] = "Standard" };
        vars["A32NX_FCU_LEFT_EIS_BARO_IS_STD"] = new SimVarDefinition
        {
            Name = "KOHLSMAN SETTING STD:1", DisplayName = "Capt Altimeter STD",
            Type = SimVarType.SimVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = baroStd
        };
        vars["A32NX_FCU_RIGHT_EIS_BARO_IS_STD"] = new SimVarDefinition
        {
            Name = "KOHLSMAN SETTING STD:2", DisplayName = "F/O Altimeter STD",
            Type = SimVarType.SimVar, Units = "bool",
            UpdateFrequency = UpdateFrequency.Continuous, IsAnnounced = true,
            ValueDescriptions = baroStd
        };
```
  (b) Replace the `HandleUIVariableSet` IS_STD branch (~4578–4584) with the H-event path:

```csharp
        // EFIS baro STD(pull)/QNH(push): dev FBW removed the IS_STD L:vars — fire the
        // cockpit knob H-events (PULL=STD, PUSH=QNH; index 1=Capt, 2=F/O), only when
        // the desired state differs from the live KOHLSMAN SETTING STD readback.
        if (varKey == "A32NX_FCU_LEFT_EIS_BARO_IS_STD" || varKey == "A32NX_FCU_RIGHT_EIS_BARO_IS_STD")
        {
            int side = varKey.Contains("LEFT") ? 1 : 2;
            bool desiredStd = value > 0.5;
            bool currentStd = (simConnect.GetCachedVariableValue(varKey) ?? (desiredStd ? 0.0 : 1.0)) > 0.5;
            if (desiredStd != currentStd)
                simConnect.SendEvent($"H:A380X_EFIS_CP_BARO_{(desiredStd ? "PULL" : "PUSH")}_{side}", 0);
            return true;
        }
```
  (c) The `ProcessSimVarUpdate` IS_STD announce branch (~3996–4016) keys on `varName == "A32NX_FCU_LEFT_EIS_BARO_IS_STD" || ... RIGHT ...`. `ProcessSimVarUpdate` receives the registration KEY for batch vars, so the branch keeps working unchanged — verify by reading how `varName` is produced for these vars (grep how other SimVar-typed Mon vars announce); if it receives the NAME instead, re-key the branch to `KOHLSMAN SETTING STD:1/:2`.
  (d) The hotkey STD readback at ~6155–6156 (`GetCachedVariableValue("A32NX_FCU_LEFT_EIS_BARO_IS_STD")`) keeps working — the key is unchanged.

- [ ] **Step 2 — window rebuild.** Replace `FBWA380BaroWindow.cs` content with the same Fenix-style layout as Task 25's window, with these A380-specific differences (otherwise copy the Task 25 code, class name `FBWA380BaroWindow : FBWA380FCUWindowBase`, aircraft type `FlyByWireA380Definition`):
  - `SeedFromSim()` reads `bool std = (simConnect.GetCachedVariableValue("A32NX_FCU_LEFT_EIS_BARO_IS_STD") ?? 0) > 0.5;` and `bool inHg = (simConnect.GetCachedVariableValue("XMLVAR_Baro_Selector_HPA_1") ?? 1) < 0.5;`
  - `ModeChanged` calls `aircraft.ApplyUIVariable("A32NX_FCU_LEFT_EIS_BARO_IS_STD", std ? 1 : 0, simConnect, announcer);` and the RIGHT twin, then announces "Standard, both sides"/"QNH, both sides".
  - `UnitChanged` calls `aircraft.ApplyUIVariable("XMLVAR_Baro_Selector_HPA_1", inHg ? 0 : 1, ...)` and `_2` (set both; dev FBW honors `_1` — the FIXME is upstream).
  - `HandleSet` validates like Task 25 but with the A380 range (900–1100 hPa / 26.6–32.5 inHg to match `CAPT_QNH_SET`) and calls `aircraft.ApplyUIVariable("CAPT_QNH_SET", v, simConnect, announcer);` (which converts/validates/announces and fires `KOHLSMAN_SET` for both sides). Note: `CAPT_QNH_SET` interprets the value in the CAPTAIN's unit (`_baroInHgL`) — keep the window's unit combo synced from `XMLVAR_Baro_Selector_HPA_1` so they agree.

- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "feat(a380): Fenix-style Ctrl+B altimeter dialog; STD via H:A380X_EFIS_CP_BARO events + KOHLSMAN SETTING STD readback (IS_STD L:vars removed in dev FBW)"`

# Phase E — Coherent agent fixes

JS agents are ES5-only (Coherent GT = Chromium 49: `var`, no arrow functions, `.indexOf()` not `.includes()`). After each agent edit run a syntax check: `node --check MSFSBlindAssist/Resources/<file>.js` → no output = OK. (Node parses ES5 fine; do not use any ES6 in these files.)

### Task 27: A32NX flightinfo — step-descent misread as Top of Descent

**Files:** Modify: `MSFSBlindAssist\Resources\coherent-a32nx-flightinfo.js:48`

FBW gives `ident:'(T/D)'` to four checkpoint reasons; a cruise StepDescent carries `mcduIdent:'(S/D)'`, sits upstream of the real T/D, and the current `ident || mcduIdent` latch locks onto it.

- [ ] **Step 1:** Replace:

```js
      var id = ((pw[p].ident || pw[p].mcduIdent || "") + "").toUpperCase();
```
with:
```js
      // mcduIdent FIRST: a cruise StepDescent has ident '(T/D)' but mcduIdent '(S/D)'
      // and sits upstream of the real T/D — checking ident first latched onto the step.
      var id = ((pw[p].mcduIdent || pw[p].ident || "") + "").toUpperCase();
```

- [ ] **Step 2:** `node --check MSFSBlindAssist/Resources/coherent-a32nx-flightinfo.js` → OK. **Step 3: Commit:** `git commit -am "fix(a32nx): Shift+D no longer reports a cruise step descent as Top of Descent"`

### Task 28: OANS agent — arm read-backs, stop-distance validation, stale runway-ahead

**Files:** Modify: `MSFSBlindAssist\Resources\coherent-oans-agent.js`

- [ ] **Step 1: `armExit` false-success fix.** A rejected `selectExitFromOans` bare-returns, leaving `btvExit` at the PREVIOUS exit — `got != null` then reports the old exit as success and aborts the same-named-feature cascade. Replace inside the loop:

```js
        b.selectExitFromOans(name, f);
        var got = A.get(b.btvExit);
        if (got != null) return "Armed BTV exit " + got;
```
with:
```js
        b.selectExitFromOans(name, f);
        var got = A.get(b.btvExit);
        // Success ONLY when btvExit now equals the requested name — a rejected
        // select leaves the PREVIOUS exit in place (re-arm false-success bug).
        if (got != null && String(got) === String(name)) return "Armed BTV exit " + got;
```

- [ ] **Step 2: `armRunway` read-back.** Replace the try body:

```js
    try {
      o.btvUtils.selectRunwayFromOans((A.get(o.dataAirportIcao) || "") + name, rl.associatedFeature, thr);
      return "Armed BTV runway " + name;
    } catch (e) { return "ERR " + e; }
```
with:
```js
    try {
      var want = (A.get(o.dataAirportIcao) || "") + name;
      o.btvUtils.selectRunwayFromOans(want, rl.associatedFeature, thr);
      // selectRunwayFromOans is async — but the btvRunway observable is set
      // synchronously before its first await, so an immediate read-back is valid.
      var got = A.get(o.btvUtils.btvRunway);
      if (got != null && String(got) === String(want)) return "Armed BTV runway " + name;
      return "Runway " + name + " was not accepted";
    } catch (e) { return "ERR " + e; }
```

- [ ] **Step 3: Manual stop distance validation.** FBW ignores ≤400 m (`BrakeToVacate.ts:289-308`) and its own input clamps at 4000. Replace:

```js
    var v = parseFloat(metres); if (!isFinite(v) || v <= 0) return "bad value";
```
with:
```js
    var v = parseFloat(metres);
    if (!isFinite(v) || v <= 400 || v > 4000) return "Stop distance must be more than 400 and at most 4000 metres";
```

- [ ] **Step 4: Stale "Runway ahead" gate.** `btvUtils.rwyAheadQfu` persists when the monitor early-bails (stopped <1 kt / >40 kt). Gate the snapshot field on the live ARINC advisory bit. Add this helper next to `A.arinc`:

```js
  // "Runway ahead" gate: the OANS_WORD_1 ARINC discrete, bit 11 (1-based), SSM-valid.
  // btvUtils.rwyAheadQfu goes stale when the monitor early-bails (stopped / >40 kt).
  A.rwyAheadActive = function () {
    try {
      var raw = A.lvar("A32NX_OANS_WORD_1");
      if (typeof raw !== "number" || !isFinite(raw)) return false;
      var ssm = Math.floor(raw / 4294967296) % 4;
      if (ssm !== 3) return false;
      var bits = raw % 4294967296; if (bits < 0) bits += 4294967296;
      return (Math.floor(bits / 1024) % 2) === 1; // bit 11 = 2^10
    } catch (e) { return false; }
  };
```
(Confirm `A.lvar` exists in this agent — it is used for `A32NX_EFB_USING_METRIC_UNIT` in the same snapshot; if its name differs, adapt.) Then change the snapshot line:

```js
          rwyAheadQfu: b ? (b.rwyAheadQfu || "") : "",
```
to:
```js
          rwyAheadQfu: (b && A.rwyAheadActive()) ? (b.rwyAheadQfu || "") : "",
```

- [ ] **Step 5:** `node --check` → OK. **Step 6: Commit:** `git commit -am "fix(a380-oans): arm read-backs (re-arm false success), 400-4000m stop-distance validation, ARINC-gated runway-ahead"`

### Task 29: A380 E/WD — FWC color map, LAND ASAP/ANSA, inactive procedure lines

**Files:** Modify: `MSFSBlindAssist\Resources\coherent-ewd-agent.js`, `MSFSBlindAssist\SimConnect\CoherentEWDClient.cs`, `MSFSBlindAssist\SimConnect\CoherentFwsFailureClient.cs`

- [ ] **Step 1: A380 FWC color codes** (`FormattedFwcText.tsx:106-133`: `<5m`=Cyan, `<6m`=Magenta, `<7m`=White). Replace in `fwcColour`:

```js
    if (s.indexOf("<5m") >= 0) return "White";
    if (s.indexOf("<6m") >= 0) return "Cyan";
    if (s.indexOf("<7m") >= 0) return "Gray";
```
with:
```js
    if (s.indexOf("<5m") >= 0) return "Cyan";
    if (s.indexOf("<6m") >= 0) return "Magenta";
    if (s.indexOf("<7m") >= 0) return "White";
```
Then grep `EWDMessageLookup.cs` for its code-derived color mapping (`GetMessagePriority`, lines ~654-659) — it is shared with the A320, so do NOT change it there; instead check whether `EWDMessageLookupA380.GetMessagePriority` delegates to it (it does) and add an A380-specific override mapping in `EWDMessageLookupA380` that post-maps `<5m`→Cyan, `<6m`→Magenta, `<7m`→White before delegating for the remaining codes.

- [ ] **Step 2: LAND ASAP / LAND ANSA limitation names.** In `CoherentFwsFailureClient.cs`, the local `Cat(...)` function renders unknown codes as the raw code — and limitation keys 1/2 either miss the table or hit the unrelated `"000000001" = NORMAL` memo entry. Change the `Cat` local function signature to accept an optional special lookup:

```csharp
            int Cat(List<string>? codes, List<string> target, string header, string announcePrefix, bool withColour, bool alwaysHeader, bool perItemAnnounce, Func<string, string?>? special = null)
```
and at the top of its foreach body, before the `long.TryParse` lookup:
```csharp
                    string msg = "", prio = "";
                    string? sp = special?.Invoke(code.TrimStart('0'));
                    if (sp != null) { msg = sp; prio = sp.StartsWith("LAND ASAP") ? "Red" : "Amber"; }
                    else if (long.TryParse(code, out long num))
```
(adapt the existing `if (long.TryParse(...))` into the `else if`). Then pass the special map ONLY on the limits call:
```csharp
            int limN = Cat(r.limits, status, "Limitations", "", withColour: false, alwaysHeader: false, perItemAnnounce: false,
                special: c => c == "1" ? "LAND ASAP" : c == "2" ? "LAND ANSA" : null);
```
(`FwsLimitations.ts` publishes key 1 = LAND ASAP whenever `fws.landAsap` — exactly the serious-failure case this client exists for.)

- [ ] **Step 3: Inactive procedure lines surface as dimmed.** FBW marks ALL preview items of a not-yet-activated ABN procedure `Inactive` (`ProcedureLinesGenerator.ts:455-459`); the agent currently drops them, so a procedure preview reads only title + ACTIVATE. In `coherent-ewd-agent.js` change the scrape loop line:

```js
        if (hasTok(n, "HiddenElement") || hasTok(n, "Invisible") || hasTok(n, "Inactive")) continue;
```
to:
```js
        if (hasTok(n, "HiddenElement") || hasTok(n, "Invisible")) continue;
        var inactive = hasTok(n, "Inactive");
```
and add `inactive: inactive,` to the pushed warning object. Then in `CoherentEWDClient.cs`: READ the warning-consumption code first (grep how the parsed `warnings` array is deduped via `_seen` and announced vs rendered). Apply exactly this behavior split: (a) lines with `inactive == true` are EXCLUDED from the announce path AND from the `_seen` dedup set (so the same line announces normally when it later becomes active); (b) in the window/display text they are included with the suffix `" (not yet active)"`. Add an `Inactive` bool to the warning DTO class the JSON deserializes into.

- [ ] **Step 4:** `node --check` the agent; **build** → succeeded. **Step 5: Commit:** `git commit -am "fix(a380-ewd): A380 FWC color map, LAND ASAP/ANSA limitation names, ABN-procedure preview lines read as dimmed"`

### Task 30: A380 MFD agent — FO-side navigation, comma mapping, inactive-field detection

**Files:** Modify: `MSFSBlindAssist\Resources\coherent-a380-agent.js`, `MSFSBlindAssist\SimConnect\CoherentDebuggerClient.cs`, `MSFSBlindAssist\Forms\FBWA380\FBWA380MCDUForm.cs`

- [ ] **Step 1: Research the side state.** Grep `mfdFoRef` and `mfdCaptRef` across `coherent-a380-agent.js` and read how the SCRAPE resolves which MFD to read (the scrape honors the selected side; find the variable/parameter it uses). Also grep `navigate_uri` in `CoherentDebuggerClient.cs` and `SendNavigateUri` in `FBWA380MCDUForm.cs` to see how the command payload is built and whether the form knows the selected side (it has a Captain/FO selector).
- [ ] **Step 2: Thread the side into `mfdUiService`.** Change the function to prefer the requested side:

```js
  A.mfdUiService = function (side) {
    try {
      var mfd = document.querySelector("a380x-mfd");
      if (!mfd || !mfd.fsInstrument) return null;
      var fi = mfd.fsInstrument;
      // Both MFDs are always mounted, each with its OWN uiService — honor the
      // requested side (2 = First Officer) instead of always hitting the Captain's.
      var refs = (side === 2) ? ["mfdFoRef", "mfdCaptRef"] : ["mfdCaptRef", "mfdFoRef"];
      for (var i = 0; i < refs.length; i++) {
        var r = fi[refs[i]];
        if (r && r.instance && r.instance.uiService && r.instance.uiService.navigateTo)
          return r.instance.uiService;
      }
      if (fi.uiService && fi.uiService.navigateTo) return fi.uiService;
    } catch (e) {}
    return null;
  };
```
Update every `A.mfdUiService()` caller in the agent (grep) to pass the same side value the scrape uses (or the new parameter threaded from the command payload). In `CoherentDebuggerClient.cs` extend the `navigate_uri` command eval to pass the active side; in `FBWA380MCDUForm.SendNavigateUri` include the form's selected side index. Follow the exact payload pattern the existing `click_mcdu_element`/`navigate_by_id` commands use for side-awareness (read them first — if they carry no side either, thread it the same way for all three or reuse however the scrape side is communicated today).
- [ ] **Step 3: Comma → dot.** In `A.typeIntoField` add before the regex filter:

```js
      if (ch === ",") ch = ".";
```
(line order: `var ch = v.charAt(i); if (ch === ",") ch = "."; if (!/^[A-Z0-9/.+\- ]$/.test(ch)) continue;`).
- [ ] **Step 4: `inactive` class = dimmed + refuse typing.** (a) In the `items.push` of `enumerateLines` change `disabled: n.classList.contains("disabled"),` to `disabled: n.classList.contains("disabled") || n.classList.contains("inactive"),`. (b) In the function that sends text to a field (grep `typeIntoField` callers — the `sendToField`/scratchpad-commit path), add a guard before typing: if the target span or its field container carries class `inactive` or `disabled`, return the string `"inactive"` instead of `"ok"`, and in `FBWA380MCDUForm` announce "Field not active" when a set returns it (grep how `click_result`-style statuses are surfaced; mirror that).
- [ ] **Step 5:** `node --check` + build → OK. **Step 6: Commit:** `git commit -am "fix(a380-mfd): navigate honors the FO side, comma maps to decimal point, inactive fields read dimmed and refuse silent typing"`

### Task 31: flyPad agent — checklist state, Tailwind disabled idioms, stale comments

**Files:** Modify: `MSFSBlindAssist\Resources\coherent-flypad-agent.js`, `MSFSBlindAssist\SimConnect\CoherentEFBClient.cs` (comment only), Test: `tools\flypad-settings-test\checklist.test.js` (new)

- [ ] **Step 1: Checklist completion via the color token** (`ChecklistItemComponent.tsx:113-121`: with autofill ON, condition items ALWAYS contain a `Link45deg` svg — the svg test reads everything as checked; the row's `text-utility-green` class is present exactly when the item is complete, in both modes). In `A.valueOf` replace the checkitem branch:

```js
    if (kind === "checkitem") {
      // Completed = a check icon (svg) inside the item's checkbox box. ...
      var box = n.querySelector(".border-4");
      return (box && box.querySelector("svg")) ? "true" : "false";
    }
```
with:
```js
    if (kind === "checkitem") {
      // Completed = the row carries text-utility-green (true in BOTH manual and
      // auto-fill modes). The old "any svg in the box" test misread every
      // auto-sensed condition item as checked (they always contain a Link45deg svg).
      var cls = " " + ((n.className && n.className.baseVal !== undefined) ? n.className.baseVal : (n.className || "")) + " ";
      return cls.indexOf("text-utility-green") >= 0 ? "true" : "false";
    }
```

- [ ] **Step 2: jsdom regression test.** Read `tools\flypad-settings-test\doors.test.js` for the harness pattern (it loads the agent into jsdom with baked fixtures), then add `checklist.test.js` with two minimal fixture rows: (a) `<div class="flex-row space-x-4 text-utility-green ..."><div class="border-4"><svg/></div>Item A</div>` → `valueOf("checkitem", row) === "true"`; (b) the same row without `text-utility-green` but WITH an svg in the box (the auto-fill Link45deg case) → `"false"`. Run `node --test` in `tools\flypad-settings-test` → all tests pass (39 existing + new).
- [ ] **Step 3: Tailwind disabled idioms.** Replace `A.disabledFor` with:

```js
  // True when the control is disabled: native disabled / aria-disabled, or FBW's
  // Tailwind idioms — pointer-events-none / opacity-20 / grayscale on the node or
  // a near ancestor (FBW disables via wrapper divs; synthetic click() would bypass
  // pointer-events-none, so we must both REPORT and avoid actuating these).
  A.disabledFor = function (n) {
    try {
      if (n.disabled === true) return true;
      if (n.getAttribute && lower(n.getAttribute("aria-disabled") || "") === "true") return true;
      var cur = n, hops = 0;
      while (cur && cur.nodeType === 1 && hops < 4) {
        var c = " " + ((cur.className && cur.className.baseVal !== undefined) ? cur.className.baseVal : (cur.className || "")) + " ";
        if (c.indexOf(" pointer-events-none ") >= 0) return true;
        if (hops === 0 && (c.indexOf(" opacity-20 ") >= 0 || c.indexOf(" grayscale ") >= 0)) return true;
        cur = cur.parentElement; hops++;
      }
    } catch (e) {}
    return false;
  };
```
Then grep `clickElement` in the agent and add a guard at its top: `if (A.disabledFor(node)) return "disabled";` (and confirm `FbwEfbForm`/`CoherentEFBClient` surface a non-"ok" click result as an announcement — read the `click_result` handling; if it only logs, announce "Control is disabled"). Run `node --test` in BOTH `tools\flypad-settings-test` and `tools\perf-builder-test` — the disabled change touches shared classify paths; all existing tests must still pass. If a fixture legitimately carries `opacity-20`-style classes on an enabled control, scope the idiom check tighter (exact-token match as written, hops=0 for opacity/grayscale, ≤4 for pointer-events-none).
- [ ] **Step 4: Stale comments.** (a) In `A.powerOn` add a comment that the `L:A32NX_EFB_TURNED_ON` write is legacy/no-op in current FBW (zero references upstream) and the synthetic click on the shutoff overlay is the real wake mechanism — keep both. (b) In `A.setValue`, fix the comment: the commit happens in SimpleInput's `onFocusOut` — the `blur()` is load-bearing; the Enter keydown/keyup dispatches don't match FBW's `keypress` listener (kept as harmless belt-and-braces). (c) Same correction at the `CoherentEFBClient.cs` comment (~line 202) if it repeats the claim.
- [ ] **Step 5:** `node --check` + `node --test` both harnesses + build → OK. **Step 6: Commit:** `git commit -am "fix(flypad): checklist state via text-utility-green (auto-fill misread), Tailwind disabled idioms detected and not clickable, stale comments"`

### Task 32: A380 MFD — scrape the MSG LIST and DUPLICATE NAMES dialogs

**Files:** Modify: `MSFSBlindAssist\Resources\coherent-a380-agent.js`

The agent scopes to `.mfd-navigator-container`, but `MFD.tsx:407-412` renders `<MfdMsgList>` and `<MfdFmsFplnDuplicateNames>` as SIBLINGS. Duplicate-names is a modal flow-blocker: typing an ambiguous ident awaits a pick (`MFD.tsx:189-194`) the blind pilot never sees.

- [ ] **Step 1: Research the dialog DOM.** Read `C:\Users\robin\Downloads\fbw\aircraft\fbw-a380x\src\systems\instruments\src\MFD\MFD.tsx` (the render block ~380-415) and the two components (`MsgList`/`FplnDuplicateNames` — grep their file names under `MFD\pages\common\`). Record: the dialog container's class/ref, how visibility is toggled (`display:block` style vs class), the row structure (duplicate rows carry `id="mfd-fms-dupl-{i}"`), and the RETURN/CLOSE button structure.
- [ ] **Step 2: Scrape the open dialog instead of the page.** In `enumerateLines`, after `var page = root.querySelector(".mfd-navigator-container") || root;`, insert (substituting the EXACT selectors found in Step 1 — they are deterministic facts from the local FBW source, not guesses):

```js
    // A visible modal dialog (MSG LIST / DUPLICATE NAMES) overlays the page and
    // renders OUTSIDE .mfd-navigator-container — scrape IT instead, so the pilot
    // hears and can drive the modal (typing an ambiguous ident otherwise hangs
    // silently waiting for a pick).
    var dlg = null;
    var dlgCands = root.querySelectorAll("<DIALOG-CONTAINER-SELECTOR-FROM-STEP-1>");
    for (var dc = 0; dc < dlgCands.length; dc++) {
      try { if (window.getComputedStyle(dlgCands[dc]).display !== "none") { dlg = dlgCands[dc]; break; } } catch (e) {}
    }
    if (dlg) page = dlg;
```
Ensure the duplicate-option rows (id `mfd-fms-dupl-{i}`) and the RETURN/CLOSE buttons classify as clickable (extend `A.INTERACTIVE_SELECTOR`/`classify` if the rows don't match an existing branch — they are clickable divs with ids, so an `[id^="mfd-fms-dupl-"]` selector addition is the expected change).
- [ ] **Step 3:** `node --check` → OK. Build (no C# change expected — the dialog rows flow through the normal element list). **Step 4: Commit:** `git commit -am "feat(a380-mfd): MSG LIST and DUPLICATE NAMES modal dialogs are scraped and drivable (ambiguous-ident entry no longer hangs silently)"`

# Phase F — Transport layer

### Task 33: MobiFlight connected-gate uses the confirmed handshake

**Files:** Modify: `MSFSBlindAssist\SimConnect\SimConnectManager.cs:74`

With the WASM module absent, `Initialize()` still sets `IsConnected = true` (the failure arrives as an async SimConnect exception that never resets it) — so `SetLVar` routes every write into a dead channel forever and the documented data-def fallback never triggers. `IsRegistered` is only set by the `MF.Clients.Add.FBWBA.Finished` response — module-confirmed.

- [ ] **Step 1:** Change:

```csharp
    public bool IsMobiFlightConnected => mobiFlightWasm?.IsConnected == true;
```
to:
```csharp
    // Gate on the handshake-CONFIRMED registration, not the optimistic IsConnected
    // (set unconditionally at the end of Initialize even when the WASM module is
    // absent — the failure arrives as an async exception that never resets it).
    // Without the module, SetLVar must fall through to the data-def write and
    // H:/dotted events must queue; a permanently-true gate sent them into a dead
    // client-data area. Pre-registration L:var writes briefly use the data-def
    // path — acceptable; the pendingCalcEvents queue still flushes on the
    // "client registered" ConnectionStatusChanged.
    public bool IsMobiFlightConnected => mobiFlightWasm?.IsRegistered == true;
```

- [ ] **Step 2:** Verify the flush hook still fires: grep `FlushPendingCalcEvents` (line ~4269) — it runs on `ConnectionStatusChanged`, which the registration handler raises ("MobiFlight WASM client registered"), so queued events flush at the moment the gate opens. Confirm by reading the subscription site.
- [ ] **Step 3: Build** → succeeded. **Step 4: Commit:** `git commit -am "fix(simconnect): MobiFlight gate uses the registration handshake - no-WASM installs now fall back to data-def writes as documented"`

### Task 34: MCDU service — powered-side fallback + print messages

**Files:** Modify: `MSFSBlindAssist\Services\FlyByWireMCDUService.cs`, `MSFSBlindAssist\Forms\FlyByWireA320\FlyByWireMCDUForm.cs`

(a) `left` is empty when MCDU1 is unpowered while MCDU2 has content (AC ESS SHED — `A320_Neo_CDU_MainDisplay.ts:1557-1611`). (b) `print:{lines}` messages (sent when a PRINT prompt is pressed, `:1514-1530`) are silently dropped — announcing them gives print confirmation AND a free read-the-ATIS channel.

- [ ] **Step 1: Fallback to the powered side.** In `HandleMessage` replace:

```csharp
            var content = JObject.Parse(msg.Substring(idx + 1));
            if (content[CaptainSide] is not JObject side) { return; }
            var data = FbwMcduFormat.BuildDisplayData(side);
```
with:
```csharp
            var content = JObject.Parse(msg.Substring(idx + 1));
            if (content[CaptainSide] is not JObject side) { return; }
            // MCDU1 unpowered (AC ESS SHED) renders empty lines while MCDU2 may have
            // content — fall back to "right" when "left" carries no text at all.
            var data = FbwMcduFormat.BuildDisplayData(side);
            if (IsBlankScreen(data) && content["right"] is JObject rightSide)
            {
                var rightData = FbwMcduFormat.BuildDisplayData(rightSide);
                if (!IsBlankScreen(rightData)) data = rightData;
            }
```
and add the helper to the service:
```csharp
    private static bool IsBlankScreen(MCDUDisplayData d)
        => string.IsNullOrWhiteSpace(d.Title)
           && string.IsNullOrWhiteSpace(d.Scratchpad)
           && d.Lines.All(string.IsNullOrWhiteSpace);
```
(Read `MCDUDisplayData` first — if `Lines` is not a flat string list, adapt the emptiness test to its real shape.)
- [ ] **Step 2: Print handler.** In `HandleMessage`, extend the type filter:

```csharp
        if (type != "update") { return; }
```
to:
```csharp
        if (type == "print")
        {
            // print:{lines:[...]} — sent when an MCDU PRINT prompt is pressed.
            try
            {
                var p = JObject.Parse(msg.Substring(idx + 1));
                var lines = p["lines"]?.Select(t => (t ?? "").ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                            ?? new List<string>();
                if (lines.Count > 0) PostToUI(() => PrintReceived?.Invoke(lines));
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[FbwMCDU] Print parse error: {ex.Message}"); }
            return;
        }
        if (type != "update") { return; }
```
Add the event next to `DisplayUpdated`:
```csharp
    public event Action<List<string>>? PrintReceived;
```
BEFORE coding, verify the actual print payload shape in the FBW source (`A320_Neo_CDU_MainDisplay.ts:1514-1530` — read it; if the payload is `{lines: string[]}` with markup tags, run each line through the existing `FbwMcduFormat` cell decode used for display lines so colors/markup strip cleanly).
- [ ] **Step 3: Form side.** In `FlyByWireMCDUForm.SetupEventHandlers` subscribe `_service.PrintReceived += OnPrintReceived;` and add:

```csharp
    private void OnPrintReceived(List<string> lines)
    {
        if (IsDisposed || !IsHandleCreated) return;
        // Background state change (not a direct UI interaction) → announce.
        _announcer.Announce("Printer: " + string.Join(", ", lines));
    }
```
(Unsubscribe wherever `DisplayUpdated` is unsubscribed/disposed.)
- [ ] **Step 4: Stale remark.** In the service's class-level remarks replace the sentence "The two MCDU instruments each broadcast their own screen into both keys with no sender tag, interleaved." with "This FBW version runs a single MCDU instrument writing both keys (no sender tag); the no-side-separation conclusion is unchanged." 
- [ ] **Step 5: Build** → succeeded. **Step 6: Commit:** `git commit -am "feat(a32nx-mcdu): powered-side fallback when MCDU1 is dark; print: messages announced (ATIS/OFP print readout)"`

# Phase G — Documentation

### Task 35: CLAUDE.md corrections + Pass-2 checklist file

**Files:** Modify: `CLAUDE.md`; Create: `docs\fbw-dev-pass2-live-verification.md`

- [ ] **Step 1: Correct the now-wrong CLAUDE.md claims** (each was contradicted by the v2024.2.0 source audit). Locate each by the quoted phrase and rewrite minimally:
  - "the A32NX models audio as per-channel reception VOLUME (`A32NX_RMP_{L,R}_VHF{1,2,3}_VOLUME`...)" → note these vars do NOT exist in dev FBW; the ACP is unmodeled and the panels were removed.
  - The A380 baro note "STD/QNH ... via `*_EIS_BARO_IS_STD`" and "the A380 still uses them legitimately" → IS_STD L:vars are REMOVED in dev FBW; STD is `H:A380X_EFIS_CP_BARO_PULL/PUSH_{n}` + `KOHLSMAN SETTING STD:n` readback.
  - The A32NX FCU-window note "STD state reads `A32NX_FCU_EFIS_L_DISPLAY_BARO_VALUE_MODE`... unit via `XMLVAR_Baro_Selector_HPA_{1,2}`" → unit is now `A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG` (XMLVAR removed on the A32NX).
  - The "A32NX Panel Parity" claim that dotted `A32NX.FCU_*` events are "NOT [reachable] by the calc K: namespace" → reconcile with the shipping calc-path dispatch (the newer "SendEvent H:/dotted calc-path gate" section is the accurate one).
  - "EFIS ... The LS button `A32NX_EFIS_{L,R}_LS_BUTTON_IS_ON` IS directly settable" → corrected mechanism (event + light var).
  - "the real A320 has no MTRS button — A330+/A380 only" → note `A32NX.FCU_METRIC_ALT_TOGGLE_PUSH` IS registered in dev FBW (pass-2 probe pending).
  - Add one line to the VARIABLE/CONTROL TROUBLESHOOTING PLAYBOOK golden rules: "**The reliable existence test is the FBW source** (now locally available): a control var must appear in the cockpit XML/behavior templates AND have a systems-side reader — the write-stick test passes on ANY nonexistent L:var. The 2026-06 audit found six A32NX controls 'verified' that way which were dead (GEN PBs, wipers, dome, presets, cargo air, ACP volumes)."
- [ ] **Step 2: Create the Pass-2 live-verification checklist** at `docs\fbw-dev-pass2-live-verification.md` with this content:

```markdown
# FBW dev integration — Pass 2: live-sim verification checklist

Items deliberately deferred from the 2026-06-11 plan because they need a running sim.
Verify each against the DEV A32NX/A380X; check off + note results.

## Verify the Pass-1 changes that could not be bench-tested
- [ ] A380 STD/QNH via `H:A380X_EFIS_CP_BARO_PULL/PUSH_{1,2}` actually toggles (readback `KOHLSMAN SETTING STD:1/:2`). If the H-events no-op, try the stock `BAROMETRIC_STD_PRESSURE` K-event (also intercepted per MsfsBaroManager).
- [ ] A32NX unit toggle via `A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG` calc write changes the FCU display unit.
- [ ] A32NX LS via `A32NX.FCU_EFIS_L/R_LS_PUSH` + `*_LS_LIGHT_ON` readback.
- [ ] A32NX GEN/APU GEN toggle events + stock-simvar state combos.
- [ ] A32NX wipers on circuits 77/80 (off/slow/fast) and dome light potentiometer 7.
- [ ] A32NX runway turn-off via indexed `TAXI_LIGHTS_SET` (both lights + cockpit switch follows).
- [ ] A32NX thrust detents land in the FBW bands (esp. Reverse Idle at -0.80) — also confirm against a custom EFB throttle calibration.
- [ ] A32NX FPA set ×10 (enter 2.5 → FCU shows 2.5°); negative V/S via the panel path.
- [ ] A380 FPA set ×10 (enter 2.5 → FCU shows 2.5° — was silently ignored at ×100); negative V/S.
- [ ] A380 transfer pumps 70-81 toggle their circuits; anti-skid toggle.
- [ ] A32NX NAVAID selectors / FO filter pushes / filter light readbacks.
- [ ] A32NX LDG ELEV knob write (-4000 Auto detent + a real elevation).
- [ ] COM3 standby set/swap; brightness knobs move the cockpit pots.
- [ ] Approach-capability announce on an ILS approach (LAND 2 / LAND 3 transitions, both jets).
- [ ] MCDU print: press an MCDU PRINT prompt (e.g. D-ATIS) → announced; MCDU2 fallback under AC ESS SHED failure.
- [ ] OANS: re-arm exit E→K reports honestly; manual stop 400-4000 validation; runway-ahead clears when stopped.
- [ ] MFD duplicate-names dialog reads + is clickable (type an ambiguous VOR ident); FO-side URI navigation.
- [ ] flyPad checklist items read correctly with EFB_AUTOFILL_CHECKLISTS on AND off; disabled tiles announce as dimmed and don't actuate.
- [ ] E/WD: ABN-PROC preview lines read as "(not yet active)"; LAND ASAP/ANSA name correctly during a serious failure; `<5m` limitations announce as Cyan.

## Deferred features/probes (build after verification or live iteration)
- [ ] Probe: `A32NX.FCU_METRIC_ALT_TOGGLE_PUSH` on the A32NX (newly registered in dev) — expose if functional.
- [ ] Probe: gravity gear extension (`A32NX_GRAVITYGEAR_TURNED`=1 + `_ROTATIONS`=3 stickiness) — add controls if it actuates.
- [ ] A380 stall warning: NO working aural var (FBW `FwsSoundManager.ts:116-122` maps the stall sound to `A32NX_AUDIO_ROP_MAX_BRAKING` — apparent upstream copy-paste bug). REPORT UPSTREAM to FlyByWire; re-add the monitor on whatever var the fix lands on.
- [ ] A380 `A32NX_AUTOPILOT_AUTOLAND_WARNING`: no writer found in the A380X tree (suspected dead) — verify during an autoland; remove or re-key if confirmed.
- [ ] FCU `A32NX_FCU_AFS_DISPLAY_*_DASHES` (A32NX): use to say "managed" instead of a stale value in the FCU readouts — needs live confirmation of dash semantics.
- [ ] flyPad Performance page SelectInput dropdowns (takeoff/landing calculators) — live tour; extend the agent if unreachable.
- [ ] flyPad radio-altitude Automatic Call Outs page — live tour of the settings builder.
- [ ] flyPad EFB ATC page hover-revealed tune buttons — live tour.
- [ ] SDv2 scrape fallback: confirm which Coherent view hosts C/B + VIDEO on the installed build (source splits them between A380X_SDv2 and legacy A380X_SD).
- [ ] Dual-FWS-failure announce path (fwsCore destroyed → probe empty → fallback checklist scraped but silent) — design together with the EWD hidden-row visibility fix (regression-sensitive, see audit notes).
- [ ] F-PLN scrape polish batch (two-altitude constraints, SPD+ALT coexistence, ditto carry-forward, FPA column, hold-row speed, destination footer, TMPY/EO title flags) — needs live fixtures captured first.
- [ ] NEW FEATURE: native pushback control form (heading/speed L:var writes + spoken/tonal feedback from PUSHBACK ANGLE) — needs live tuning by design.
- [ ] NEW FEATURE: CG envelope verdict helper (transcribe envelope limits from FBW source, speak "within limits").
- [ ] A380 wheel chocks/cones tiles were REMOVED upstream (commented out in A380Services.tsx) — expect them to disappear from the flyPad after the next aircraft update; no MSFSBA action.
```

- [ ] **Step 3: Commit:** `git commit -am "docs: correct CLAUDE.md claims contradicted by the dev-source audit; add Pass-2 live-verification checklist"`

---

## Execution progress

- [x] Task 1 — DONE (commit 5048d32; spec ✅, quality ✅)
- [x] Task 2 — DONE (commit 841f69b; spec ✅, quality ✅)
- [x] Task 3 — DONE (commits 5c58bce + b9ac5eb comment; spec ✅, quality ✅)
- [x] Task 4 — DONE (commits b814de9 + 8065666; spec ✅, quality ✅ after fixes). BONUS: review found the SAME ×100 FPA bug in the A380's SetFCUVSValue (silently ignored by its FCU, source-confirmed VerticalSpeedManager.ts /10) — fixed in 8065666.
- [x] Task 5 — DONE (commits 9b309c5 + dfe4804 phrasing; spec ✅, quality ✅)
- [x] Task 6 — DONE (commit 142e4b7; spec ✅, quality ✅; probe txt is a live capture — intentionally not edited)
- [x] Task 7 — DONE (commit 8b03fce; spec ✅, quality ✅ — reviewer's "cache-miss double-toggle" flag rejected: established A380 toggle-if-differs pattern; panel-open requests populate OnRequest caches before interaction)
- [x] Task 8 — DONE (commits 4ba430c + 84be530 comment; spec ✅, quality ✅ — reviewer's "drop to 2 options" rejected: would remove the Fast SET option; limitation documented instead per plan)
- [x] Task 9 — DONE (commits b560d16 + ce14120 comment; spec ✅ — RPN mirrors cockpit XML verbatim incl. its single-arg K:2 quirk (deliberate: identical behavior to the cockpit switch); quality ✅)
- [x] Task 10 — DONE (commit 84a1c7e; combined review ✅ incl. catch-all routing + SD-loop add-if-absent checks)
- [x] Task 11 — DONE (commit 00fec3a; spec ✅ — implementer's key-rename deviation verified complete/self-consistent; quality ✅; "(state only)" label polish skipped, pre-existing + not panel-visible)
- [x] Task 12 — DONE (commits 21d0b80 + ed60490 stale-comment fix; review ✅)
- [x] Task 13 — DONE (commits 2776645 + 5945117; spec ✅; quality review's cache-freshness finding accepted → fire handles now Continuous+IsAnnounced (interlock cache stays live + handle pulls announce); "success announce" suggestion rejected (conflicts with no-combo-echo rule))
- [x] Task 14 — DONE (commit 76a54dc; combined review ✅ incl. load-bearing branch ordering + char-exact side detection)
- [x] Task 15 — DONE (commits aee6891 + 3472d78 formatter cleanup; rigorous spec review ✅ — per-key grep table, collateral scan clean, must-keeps verified; quality covered by the collateral/integrity scan). PHASE A COMPLETE.
- [x] Task 16 — DONE (commits 00f1d7b + d4e096e read-only fix; review ✅ — all five additions verified incl. _SET routing and read-only rendering)
- [x] Task 17 — DONE (commit 7ed8a75; spec ✅ — bonus: L-side filter pushes were never in any panel (oversight), now added for parity; quality ✅)
- [x] Task 18 — DONE (commit 044f7f5; review ✅ — the index-aware COM handler + announce already covered :3; only defs/panel entries were needed)
- [x] Task 19 — DONE (commits a68f5c7 + d1659b0 PTU phrasing; review ✅ — OnlyAnnounceValueDescriptionMatches confirmed needed + present for triple click). PHASE B COMPLETE.
- [x] Task 20 — DONE (commit cc1d23c; done inline after a subagent session-limit failure — 6-line interpolation rename, grep-verified 0 OVHD_FIRE_SQUIB left / 8 OVHD_FIRE_AGENT untouched, build ✅)
- [x] Task 21 — DONE (commit a658262; spec ✅ — full grep table, must-keeps verified, A320 parallel registrations untouched; FD window + EisDisplayVars confirmed re-pointed)
- [x] Task 22 — DONE (commit 85c16f9; review ✅; circuit pairing 70-81 verified directly by controller grep)
- [x] Task 23 — DONE (commit 3bf7077; done inline — toggle-if-differs branch beside the seatbelt pattern, panel entry + both stale comments updated, build ✅)
- [x] Task 24 — DONE (commit 77f265e; review ✅ — bonus: pre-existing wrong bit-priority in TryGetDisplayOverride fixed). PHASE C COMPLETE.
- [x] Task 25 — DONE (commit 9f02acf; review ✅ — caller-compatible, no regressions, suppress-guard verified; old window's inHg label was wrong, new validation is consistent)
- [x] Task 26 — DONE (commit 3fe2e5c; review ✅ — H-event RPN traced end-to-end, key-stability strategy verified across all 6 consumer sites, dual-announce confirmed pre-existing). PHASE D COMPLETE.
- [x] Task 27 — DONE (commit 88e3fe3; done inline — mcduIdent precedence swap, node --check ✅)
- [x] Task 28 — DONE (commit a445268; review ✅ — ES5 clean, cascade semantics preserved, JS/C# bit math agreement verified)
- [x] Task 29 — DONE (commit ce4c189; review ✅ — ABN-overlay re-announce regression path walked and confirmed clean; LAND ASAP survives the NORMAL guard)
- [x] Task 30 — DONE (commit ab5db4c; review ✅ — agent's existing A.activeMcdu reused so scrape and navigation agree; no C# change needed (pre-dispatch Disabled guard covers the announce))
- [x] Task 31 — DONE (commit 1a8f96c; review ✅ — hops-scoping verified exact, token matching live-tested, 43+12 jsdom tests pass)
- [x] Task 32 — DONE (commit 3b69bc5; review ✅ — DOM facts independently source-verified, builder bail-outs confirmed, FMS-PAGE-NOT-AVAIL edge benign). PHASE E COMPLETE.
- [x] Task 33 — DONE (commit c7663c1; done inline — gate now IsRegistered; flush hook verified to fire after registration sets the flag)
- [x] Task 34 — DONE (commit 8d76b6a; review ✅ — FBW print payload independently re-verified as pre-stripped plain strings). PHASE F COMPLETE.
- [x] Task 35 — DONE (commit 584925a; done inline — 7 CLAUDE.md corrections + golden rule 5 + docs/fbw-dev-pass2-live-verification.md). ALL 35 TASKS COMPLETE.

## Self-review notes (performed at plan time)

- **Spec coverage:** every Pass-1 finding from the audit maps to a task (Tasks 1–35); live-verification-dependent items are explicitly in the Pass-2 checklist (Task 35 Step 2) rather than silently dropped.
- **Cross-task consistency:** Task 5 and Task 24 both introduce `_lastAutolandCap` — one per aircraft definition file (separate classes, no collision). Task 15 removes the A320's `A32NX_APPROACH_CAPABILITY` references; Task 24 removes the A380's — Task 24 Step 3's repo-wide grep is the final gate. Task 21 keeps the A320's blower/extract and `A32NX_SLIDES_ARMED` (real on the A32NX) while removing the A380's — do not "clean up" the A320 side.
- **Ordering:** Tasks are independent except: Task 26 depends on nothing but should come after Task 21 (both touch the A380 def — avoid merge friction); Task 31 Step 3's disabled-idiom change can affect Task 31 Step 1's fixtures — run the full `node --test` suite after both.
- **Known judgment calls baked in:** (a) inert A32NX recorder/ELT/rain-repellent controls are REMOVED (user chose cleanest; FBW models them as animation-only); (b) the A380 stall-warning monitor is removed rather than re-pointed at the misnamed upstream var (ambiguous wording risk — upstream report instead); (c) wiper combos show Off vs running (circuit bool) — exact speed readback deferred.






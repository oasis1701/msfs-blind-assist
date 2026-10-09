# First Officer APU-Start Gating Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gate every First Officer ground-power→APU transfer on the aircraft's real "APU available" signal, aborting the flow on start failure instead of dropping ground power onto batteries — across PMDG 777, PMDG 737, Fenix A320, and FBW A380.

**Architecture:** Pure flow/checklist-definition changes plus two tiny evaluator additions. Each aircraft's Before Start APU wait becomes a `WaitForCondition` on a genuine availability signal with `FlowStepFailurePolicy.Stop`; After Landing waits use the same signals with the default Skip. The A380 additionally gains the missing APU START pushbutton press. No FlowManager/executor changes.

**Tech Stack:** C# 13 / .NET 10 WinForms; PMDG CDA structs read by reflection; FBW L:vars via the SimConnect batch cache.

**Spec:** `docs/superpowers/specs/2026-07-06-fo-apu-gating-design.md` (read it before starting a task if anything here seems ambiguous).

## Global Constraints

- Branch: work directly on `feature/first-officer` (already checked out; main is protected — never commit to main).
- Build command (ALWAYS the solution, NEVER the bare csproj): `dotnet build MSFSBlindAssist.sln -c Debug`. Success = "Build succeeded". The exe is file-locked while the app runs (MSB3021) — if the build fails with MSB3021, tell the user to close MSFSBlindAssist.
- **No automated test project exists** (project convention: SimConnect-driven UI, human in-sim verification). The per-task verify cycle is: build succeeds + the structural checks named in the task. Task 6 writes the in-sim test plans.
- Screen-reader rule: flow step labels ARE the spoken text. Keep new labels short, concrete, sentence-style — they are read as "Waiting for: {label}", "Timed out waiting for: {label}", "{Flow name} flow stopped. Unable to complete: {label}".
- Do not touch `FlowManager.cs`, `FlowStep.cs`, or any executor dispatch code — everything needed already exists.
- Comment style: match the files' existing comment density; comments state constraints/rationale (why), not narration.

---

### Task 1: Fenix A320 — Stop policy on the Before Start APU wait

**Files:**
- Modify: `MSFSBlindAssist\FirstOfficer\Fenix\FenixFlowDefinitions.cs:178-179`

**Interfaces:**
- Consumes: `WaitForField(id, label, field, condition, timeoutSec, onTimeout)` helper already in this file (line ~524) with `onTimeout` defaulting to Skip; `FlowStepFailurePolicy.Stop`.
- Produces: nothing consumed by other tasks.

Background: the Fenix Before Start already waits on the real AVAIL light (`I_OH_ELEC_APU_START_U`, registered Continuous+IsAnnounced in `FenixA320Definition.cs:5801`, so it is batch-cached and readable). The only defect is the default Skip policy: on a 180 s timeout (APU start failure) the flow continues and pulses external power off. The After Landing wait (`AL_APU_AVAIL`) stays Skip — do NOT change it.

- [ ] **Step 1: Make the edit**

In `BuildBeforeStart()`, change:

```csharp
            WaitForField("BS_APU_AVAIL", "Waiting for APU available",
                "I_OH_ELEC_APU_START_U", v => v > 0.5, 180),
```

to:

```csharp
            // Stop policy: an APU start failure aborts the flow HERE, before external
            // power is pulsed off the bus below — never a silent transfer to batteries.
            WaitForField("BS_APU_AVAIL", "Waiting for APU available",
                "I_OH_ELEC_APU_START_U", v => v > 0.5, 180,
                onTimeout: FlowStepFailurePolicy.Stop),
```

- [ ] **Step 2: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 3: Verify the After Landing wait was not touched**

Run: `git diff` — the only hunk is the `BS_APU_AVAIL` step in `BuildBeforeStart()`. `AL_APU_AVAIL` (~line 401) is unchanged.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs
git commit -m "fix(fenix-fo): abort Before Start on APU-avail timeout instead of dropping ext power"
```

---

### Task 2: PMDG 777 — real APURunning detection replaces the blind 90 s timer

**Files:**
- Modify: `MSFSBlindAssist\FirstOfficer\PMDG777FlowDefinitions.cs` (helper ~610-620, Before Start ~206-213, After Landing ~455-460)

**Interfaces:**
- Consumes: `PMDG777XDataStruct.APURunning` (bool, struct line ~712) — readable by name via the evaluator's reflection `GetValue` with **no registration** (NaN until the first CDA snapshot; NaN fails `v > 0.5`, so waits just keep waiting — correct). This is the same field the System Display reads (`PMDG777Definition.SystemDisplay.cs:137`).
- Produces: `WaitForField(..., FlowStepFailurePolicy onTimeout = Skip)` helper signature in this file (Task 6 documents the behavior; no code consumer).

- [ ] **Step 1: Add the `onTimeout` parameter to this file's WaitForField helper**

The 737/Fenix helpers already have it; the 777's hardcodes Skip. Change (~line 610):

```csharp
    private static FlowStep<AircraftStateEvaluator> WaitForField(string id, string label, string field,
        Func<double, bool> condition, int timeoutSec) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = FlowStepFailurePolicy.Skip,
        PostActionDelayMs = 0,
    };
```

to:

```csharp
    private static FlowStep<AircraftStateEvaluator> WaitForField(string id, string label, string field,
        Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,
        PostActionDelayMs = 0,
    };
```

- [ ] **Step 2: Replace the Before Start blind wait**

In `BuildBeforeStart()` (~lines 206-213), change:

```csharp
            // APU Start — always done here regardless of GPU status.
            // Selector: OFF → ON (1) → START (2, spring-loads back to ON internally).
            // Fixed 90-second wait because ELEC_APU_Selector returns to 1 immediately
            // after the ON command and is not a reliable "APU running" indicator.
            SW("BS_APU_ON",    "APU selector: ON",    "EVT_OH_ELEC_APU_SEL_SWITCH", 1),
            Wait("BS_APU_ON_WAIT", "Waiting before APU start", 2),
            SW("BS_APU_START", "APU selector: START",  "EVT_OH_ELEC_APU_SEL_SWITCH", 2),
            Wait("BS_APU_WAIT", "Waiting for APU to reach self-sustaining speed", 90),
```

to:

```csharp
            // APU Start — always done here regardless of GPU status.
            // Selector: OFF → ON (1) → START (2, spring-loads back to ON internally).
            // Gate on the SDK's APURunning bool — the same field the System Display reads.
            // (ELEC_APU_Selector returns to 1 immediately after the ON command and is not
            // a reliable "APU running" indicator, and the old fixed 90 s wait disconnected
            // ground power even when the APU never started.) Stop policy: a timeout aborts
            // the flow BEFORE the ground-power disconnect steps below. Re-run safe:
            // APURunning already 1 makes the wait pass instantly.
            SW("BS_APU_ON",    "APU selector: ON",    "EVT_OH_ELEC_APU_SEL_SWITCH", 1),
            Wait("BS_APU_ON_WAIT", "Waiting before APU start", 2),
            SW("BS_APU_START", "APU selector: START",  "EVT_OH_ELEC_APU_SEL_SWITCH", 2),
            WaitForField("BS_APU_WAIT", "Waiting for the APU to start", "APURunning",
                v => v > 0.5, 120, onTimeout: FlowStepFailurePolicy.Stop),
            // Short settle so the (preflight-armed) APU generator breaker has closed
            // before the load leaves ground power.
            Wait("BS_APU_SETTLE", "APU stabilising", 5),
```

- [ ] **Step 3: Fix the After Landing wait to the same signal**

In `BuildAfterLanding()` (~lines 455-460), change:

```csharp
            // APU Start sequence for on-ground power
            SW("AL_APU_ON",       "APU selector: ON",       "EVT_OH_ELEC_APU_SEL_SWITCH",  1),
            Wait("AL_APU_WAIT",   "Waiting for APU selector", 2),
            SW("AL_APU_START",    "APU selector: START",    "EVT_OH_ELEC_APU_SEL_SWITCH",  2),
            WaitForField("AL_APU_RUNNING", "Waiting for APU",
                "ELEC_APU_Selector", v => Math.Abs(v - 1) < 0.1, 90),
```

to:

```csharp
            // APU Start sequence for on-ground power. Skip policy (not Stop): engines are
            // running, so an APU failure here is announced but must not abort the
            // remaining cleanup. ELEC_APU_Selector was the old wait field — it reads 1
            // immediately after the ON command, so it proved nothing; APURunning is real.
            SW("AL_APU_ON",       "APU selector: ON",       "EVT_OH_ELEC_APU_SEL_SWITCH",  1),
            Wait("AL_APU_WAIT",   "Waiting for APU selector", 2),
            SW("AL_APU_START",    "APU selector: START",    "EVT_OH_ELEC_APU_SEL_SWITCH",  2),
            WaitForField("AL_APU_RUNNING", "Waiting for the APU to start",
                "APURunning", v => v > 0.5, 120),
```

- [ ] **Step 4: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 5: Structural check**

Confirm with `git diff`: (a) `Wait("BS_APU_WAIT", ...)` no longer exists — the id now belongs to a `WaitForField`; (b) no other call site of the 777 `WaitForField` helper broke (it previously had exactly 3 call sites: `ES_ENG2_STABLE`, `ES_ENG1_STABLE`, `AL_APU_RUNNING` — the new optional parameter is backward-compatible); (c) `"APURunning"` is spelled exactly as in `PMDG777XDataStruct.cs` line ~712 (reflection is name-exact).

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs
git commit -m "fix(777-fo): gate ground-power drop on real APURunning detection, abort on timeout"
```

---

### Task 3: PMDG 737 — generator-availability gate + EGT-based IsApuRunning

**Files:**
- Modify: `MSFSBlindAssist\FirstOfficer\PMDG737\AircraftStateEvaluator.cs:107-108`
- Modify: `MSFSBlindAssist\FirstOfficer\PMDG737\PMDG737FlowDefinitions.cs:143-157` (Before Start)

**Interfaces:**
- Consumes: NG3 struct fields `APU_Selector` (byte, 0 OFF/1 ON/2 START), `APU_EGTNeedle` (float), `ELEC_annunAPU_GEN_OFF_BUS` (bool) — all reflection-readable via `GetValue`, no registration. Evaluator returns NaN until the first CDA snapshot; NaN fails every comparison (safe for both waits and skip conditions).
- Produces: `AircraftStateEvaluator.IsApuRunning()` (EGT-based), `ApuEgt()`, `public const double ApuRunningEgt` — used by this task's flow skip conditions and named in Task 6's test plan.

Background: today `IsApuRunning() => ApuSelector() == 1` has **zero callers** and is semantically wrong (the selector reads ON the moment the flow sets it — it is switch position, not APU state). The selector spring-back START→ON marks starter cutout, which happens while the APU is still spooling, BEFORE the generator is available. The blue `ELEC_annunAPU_GEN_OFF_BUS` annunciator is the true "generator on line, not on a bus" signal.

- [ ] **Step 1: Evaluator — EGT-based running detection**

In `AircraftStateEvaluator.cs`, change (~lines 107-108):

```csharp
    public int  ApuSelector()      => (int)Math.Round(GetValue("APU_Selector"));              // 0=OFF,1=ON,2=START
    public bool IsApuRunning()     => ApuSelector() == 1; // START springs back to ON when available
```

to:

```csharp
    public int  ApuSelector()      => (int)Math.Round(GetValue("APU_Selector"));              // 0=OFF,1=ON,2=START
    // EGT-based "APU running": the selector position only proves where the switch is (it
    // reads ON the moment the flow sets it). A running/starting APU holds EGT well above
    // ambient; cold reads near ambient. Threshold verified in-sim (test plan) — tune here.
    public const double ApuRunningEgt = 100;
    public double ApuEgt()         => GetValue("APU_EGTNeedle");
    public bool IsApuRunning()     => ApuEgt() >= ApuRunningEgt;
```

(NaN note: `ApuEgt()` is NaN before the first CDA snapshot; `NaN >= 100` is false, so `IsApuRunning()` is false — skip conditions stay safe at cold start.)

- [ ] **Step 2: Flow — re-run skips, Stop on cutout wait, new generator gate**

In `PMDG737FlowDefinitions.cs` `BuildBeforeStart()`, change (~lines 143-157):

```csharp
            Captain("BS_MCP", "Set MCP airspeed, heading and initial altitude."),
            // APU start: ON → dwell → momentary START. A direct write to START (skipping ON)
            // does not spool the APU up. START springs back to ON when self-sustaining.
            SW("BS_APU_ON", "APU selector: ON", "EVT_OH_LIGHTS_APU_START", 1),
            Wait("BS_APU_DWELL", "APU spinning up before start", 2),
            SW("BS_APU_START", "APU selector: START", "EVT_OH_LIGHTS_APU_START", 2),
            WaitForField("BS_APU_WAIT", "Waiting for the APU to come on line", "APU_Selector", v => Math.Abs(v - 1) < 0.1, 90),
            // Transfer the electrical load to the APU: the 737's APU GEN switches are
            // momentary bus-transfer buttons that must be pressed AFTER the APU is on
            // line (unlike the 777, whose gen switch is armed during preflight). Then
            // drop ground power — skipped when no GPU was ever connected.
            Multi("BS_APUGEN", "APU generators: ON",
                ("EVT_OH_ELEC_APU_GEN1_SWITCH", 1), ("EVT_OH_ELEC_APU_GEN2_SWITCH", 1)),
            Skip(SW("BS_GPU_OFF", "Ground power: OFF", "EVT_OH_ELEC_GRD_PWR_SWITCH", 0),
                s => !s.IsGpuOn()),
```

to:

```csharp
            Captain("BS_MCP", "Set MCP airspeed, heading and initial altitude."),
            // APU start: ON → dwell → momentary START. A direct write to START (skipping ON)
            // does not spool the APU up. Each command step skips when the APU is already
            // running (EGT-based), so a flow re-run never re-commands a running APU.
            Skip(SW("BS_APU_ON", "APU selector: ON", "EVT_OH_LIGHTS_APU_START", 1),
                s => s.IsApuRunning()),
            Skip(Wait("BS_APU_DWELL", "APU spinning up before start", 2),
                s => s.IsApuRunning()),
            Skip(SW("BS_APU_START", "APU selector: START", "EVT_OH_LIGHTS_APU_START", 2),
                s => s.IsApuRunning()),
            // Starter cutout: START springs back to ON. Stop policy — if the APU never
            // starts, abort the flow HERE, before the generator transfer and ground-power
            // drop below. Re-run safe: with the APU already started the selector reads ON
            // and this passes instantly.
            WaitForField("BS_APU_WAIT", "Waiting for the APU to come on line", "APU_Selector",
                v => Math.Abs(v - 1) < 0.1, 90, onTimeout: FlowStepFailurePolicy.Stop),
            // Generator availability: the blue APU GEN OFF BUS annunciator lights only once
            // the generator is up and able to take a bus — strictly LATER than starter
            // cutout, which happens while the APU is still spooling. Deliberately Skip, not
            // Stop: "light off" also reads 0 when the APU is ALREADY powering the buses
            // (re-run after a completed transfer), and no stateless NG3 signal separates
            // that from "generator not up yet" at the moment this step is reached — a
            // running-and-light-off SkipCondition would skip the gate in the normal
            // just-past-cutout case. A post-transfer re-run costs one announced 30 s
            // timeout, then everything below no-ops.
            WaitForField("BS_APU_GEN_AVAIL", "Waiting for the APU generator",
                "ELEC_annunAPU_GEN_OFF_BUS", v => v > 0.5, 30),
            // Transfer the electrical load to the APU: the 737's APU GEN switches are
            // momentary bus-transfer buttons that must be pressed AFTER the APU is on
            // line (unlike the 777, whose gen switch is armed during preflight). Then
            // drop ground power — skipped when no GPU was ever connected.
            Multi("BS_APUGEN", "APU generators: ON",
                ("EVT_OH_ELEC_APU_GEN1_SWITCH", 1), ("EVT_OH_ELEC_APU_GEN2_SWITCH", 1)),
            Skip(SW("BS_GPU_OFF", "Ground power: OFF", "EVT_OH_ELEC_GRD_PWR_SWITCH", 0),
                s => !s.IsGpuOn()),
```

Do NOT touch the After Landing flow (`AL_APU_ON`/`AL_APU_DWELL`/`AL_APU_START`, ~lines 358-360) — it deliberately starts the APU without waiting.

- [ ] **Step 3: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 4: Structural check**

`git diff` shows: exactly one `WaitForField` gained `onTimeout: FlowStepFailurePolicy.Stop`; the new `BS_APU_GEN_AVAIL` step sits BETWEEN `BS_APU_WAIT` and `BS_APUGEN`; field name `"ELEC_annunAPU_GEN_OFF_BUS"` matches `PMDGNG3DataStruct.cs` (~line 293) exactly; `"APU_EGTNeedle"` matches (~line 302). Grep the repo for `IsApuRunning` — the only callers are the three new Skip conditions in this flow (it had zero before this task).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG737/AircraftStateEvaluator.cs MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs
git commit -m "fix(737-fo): wait for APU GEN OFF BUS before the bus transfer; abort on start failure"
```

---

### Task 4: FBW A380 — press the START pushbutton and gate EXT PWR off on AVAIL (flows)

**Files:**
- Modify: `MSFSBlindAssist\FirstOfficer\FBWA380\FbwA380FlowDefinitions.cs` (Before Start ~146-154, After Start ~215-217, After Landing ~359-360)
- Modify: `MSFSBlindAssist\FirstOfficer\FBWA380\FbwA380StateEvaluator.cs:37`

**Interfaces:**
- Consumes: `SW(id, label, eventName, target)` / `Skip(step, cond)` / `Wait(id, label, seconds)` / `WaitForField(id, label, field, cond, timeoutSec, onTimeout = Skip)` helpers already in this file (~lines 423-490). L:vars: `A32NX_OVHD_APU_MASTER_SW_PB_IS_ON` (master, R/W), `A32NX_OVHD_APU_START_PB_IS_ON` (START PB, R/W — the executor's `Set` produces a single level calc write via the def's `A32NX_OVHD_` catch-all; it is NOT auto-pulsed), `A32NX_OVHD_APU_START_PB_IS_AVAILABLE` (read-only, 1 = available; registered Continuous+IsAnnounced in the def, so batch-cached and readable by the evaluator with no changes).
- Produces: the flow-step id `BS_APU_START` / `AL_APU_START` naming convention Task 5's checklist comment references; nothing else.

Background: master ON alone does NOT start the FBW APU — the START PB press is required. Today the flow writes master only and immediately switches all four EXT PWR PBs off: the APU never starts and the aircraft is dropped onto batteries. There is no in-repo evidence FBW auto-clears a latched START PB, and a stale latched 1 could surprise-start the APU on a later master-ON — hence the defensive clear steps.

- [ ] **Step 1: Evaluator poll list — add the START PB (belt-and-braces read for the clear-step skips)**

In `FbwA380StateEvaluator.cs`, change (~line 37):

```csharp
        "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
```

to:

```csharp
        "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON",
        "A32NX_OVHD_APU_START_PB_IS_ON",
```

(`A32NX_OVHD_APU_START_PB_IS_AVAILABLE` is NOT added — it is Continuous-registered and always batch-cached.)

- [ ] **Step 2: Before Start — full APU block, EXT PWR off moves behind the AVAIL gate**

In `BuildBeforeStart()`, change (~lines 146-154):

```csharp
            Captain("BS_ECAMPAGE", "ECAM page: APU"),
            Skip(SW("BS_APU", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON")),
            Skip(SW("BS_APUBLEED", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")),
            Multi("BS_GPU_OFF", "Ground power: OFF",
                ("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 0),
                ("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 0)),
            Captain("BS_GPU_DISC", "Disconnect ground power on the EFB"),
```

to:

```csharp
            Captain("BS_ECAMPAGE", "ECAM page: APU"),
            // APU block: master on, dwell, START pushbutton, wait for AVAIL. The master
            // switch alone does NOT start the FBW APU — the START PB press is required —
            // and external power must stay on the buses until the APU is actually
            // available. Stop policy on the AVAIL wait: a start failure aborts the flow
            // BEFORE the external-power disconnect below. Steps skip once AVAIL is lit.
            Skip(SW("BS_APU", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON")),
            Skip(Wait("BS_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(SW("BS_APU_START", "APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            WaitForField("BS_APU_AVAIL", "Waiting for APU available",
                "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180,
                onTimeout: FlowStepFailurePolicy.Stop),
            // Defensive: release the latched START PB (no repo evidence FBW auto-clears
            // it, and a stale latched 1 could surprise-start the APU on a later
            // master-ON). Silently skipped when FBW already cleared it.
            Skip(SW("BS_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => !s.IsOn("A32NX_OVHD_APU_START_PB_IS_ON")),
            Skip(SW("BS_APUBLEED", "APU bleed: ON", "A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_PNEU_APU_BLEED_PB_IS_ON")),
            Multi("BS_GPU_OFF", "Ground power: OFF",
                ("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 0),
                ("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 0)),
            Captain("BS_GPU_DISC", "Disconnect ground power on the EFB"),
```

- [ ] **Step 3: After Start — clear a possibly-latched START PB before master off**

The checklist path (Task 5) presses START without a downstream clear, so the OFF site must guarantee master-off never coexists with a latched START=1. In `BuildAfterStart()`, change (~lines 215-217):

```csharp
            Captain("AS_ECAMPAGE", "ECAM page: APU"),
            Skip(SW("AS_APU_OFF", "APU: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)),
```

to:

```csharp
            Captain("AS_ECAMPAGE", "ECAM page: APU"),
            // Release a still-latched START PB first — master-off must never coexist with
            // START=1 (a latched 1 would surprise-start the APU on the next master-ON).
            Skip(SW("AS_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => !s.IsOn("A32NX_OVHD_APU_START_PB_IS_ON")),
            Skip(SW("AS_APU_OFF", "APU: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0),
                s => s.IsPosition("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)),
```

- [ ] **Step 4: After Landing — same block, Skip policy**

In `BuildAfterLanding()`, change (~lines 359-360):

```csharp
            Skip(SW("AL_APU", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON")),
```

to:

```csharp
            // APU for the gate — same master → START → AVAIL sequence as Before Start,
            // but Skip policy on the wait: engines are running (no power-loss hazard) and
            // an abort would kill the unrelated cleanup steps below. Whole block no-ops
            // when the APU is already available (Fenix After Landing parity).
            Skip(SW("AL_APU", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(Wait("AL_APU_DWELL", "Waiting before APU start", 3),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            Skip(SW("AL_APU_START", "APU start", "A32NX_OVHD_APU_START_PB_IS_ON", 1),
                s => s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE")),
            WaitForField("AL_APU_AVAIL", "Waiting for APU available",
                "A32NX_OVHD_APU_START_PB_IS_AVAILABLE", v => v > 0.5, 180),
            Skip(SW("AL_APU_START_OFF", "APU start button: released", "A32NX_OVHD_APU_START_PB_IS_ON", 0),
                s => !s.IsOn("A32NX_OVHD_APU_START_PB_IS_ON")),
```

- [ ] **Step 5: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 6: Structural check**

`git diff` shows: `BS_GPU_OFF` + `BS_GPU_DISC` now appear AFTER `BS_APU_AVAIL` in the Steps list; every new skip condition on AVAIL spells `A32NX_OVHD_APU_START_PB_IS_AVAILABLE` exactly (compare `FlyByWireA380Definition.cs:356`); the After Start hunk adds `AS_APU_START_OFF` BEFORE `AS_APU_OFF`; `FbwA380StateEvaluator.cs` gained exactly one poll field.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/FBWA380/FbwA380FlowDefinitions.cs MSFSBlindAssist/FirstOfficer/FBWA380/FbwA380StateEvaluator.cs
git commit -m "fix(a380-fo): press APU START and gate ext-power drop on AVAIL; flows never left master-only"
```

---

### Task 5: FBW A380 — checklist APU tick actions press START

**Files:**
- Modify: `MSFSBlindAssist\FirstOfficer\FBWA380\FbwA380ChecklistDefinitions.cs:184-185, 404-405`

**Interfaces:**
- Consumes: `CheckFn = Func<FbwA380ActionExecutor, FbwA380StateEvaluator, Task>` (file line 7); `FbwA380ActionExecutor.Set(string, int) : Task<bool>` (executor paces writes 200 ms apart internally); evaluator `IsOn(field)` (false on NaN). L:var semantics identical to Task 4.
- Produces: nothing consumed elsewhere.

Background: the checklist "APU: ON" tick actions mirror the old flow bug — master only, APU never starts. They become master → dwell → START, with the START press conditional on AVAIL not already lit (checklists are pilot-paced; no inline AVAIL wait — the flows' `AS_APU_START_OFF` step from Task 4 guarantees the latched-PB invariant for this path).

- [ ] **Step 1: Before Start item**

Change (~lines 184-185):

```csharp
            Auto("BS_APU", "BEFORE_START", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1)),
```

to:

```csharp
            // Master alone does not start the FBW APU — the tick also presses the START
            // PB (unless AVAIL is already lit). Auto-detect stays master-based; the
            // checklist is pilot-paced, so there is no inline AVAIL wait (the After Start
            // flow's AS_APU_START_OFF step releases a still-latched START PB).
            Auto("BS_APU", "BEFORE_START", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => v > 0.5, async (e, s) =>
                {
                    await e.Set("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1);
                    if (!s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE"))
                    {
                        await System.Threading.Tasks.Task.Delay(3000);
                        await e.Set("A32NX_OVHD_APU_START_PB_IS_ON", 1);
                    }
                }),
```

- [ ] **Step 2: After Landing item**

Change (~lines 404-405):

```csharp
            Auto("AL_APU", "AFTER_LANDING", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => v > 0.5, (e, _) => e.Set("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1)),
```

to:

```csharp
            // Same master → dwell → START press as the Before Start item.
            Auto("AL_APU", "AFTER_LANDING", "APU: ON", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON",
                v => v > 0.5, async (e, s) =>
                {
                    await e.Set("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 1);
                    if (!s.IsOn("A32NX_OVHD_APU_START_PB_IS_AVAILABLE"))
                    {
                        await System.Threading.Tasks.Task.Delay(3000);
                        await e.Set("A32NX_OVHD_APU_START_PB_IS_ON", 1);
                    }
                }),
```

- [ ] **Step 3: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded. (If the compiler rejects the async lambda against `CheckFn?`, the fix is NOT to change the delegate — `CheckFn` is `Func<..., Task>` and async lambdas convert to it; re-check for a typo instead.)

- [ ] **Step 4: Structural check**

Confirm the `*_CL` action-free invariant is untouched: `BS_APU`/`AL_APU` are in groups `BEFORE_START` / `AFTER_LANDING` (action groups), NOT `*_CL` readback groups. `git diff` touches only those two items.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/FBWA380/FbwA380ChecklistDefinitions.cs
git commit -m "fix(a380-fo): checklist APU tick presses the START pushbutton, not master only"
```

---

### Task 6: In-sim test plans + CLAUDE.md gotcha

**Files:**
- Modify: `docs\pmdg-737-first-officer-test-plan.md` (append a new Part at the end; covers 737 + 777)
- Modify: `docs\fenix-first-officer-test-plan.md` (append a section)
- Modify: `docs\fbw-a380-first-officer-test-plan.md` (append a section)
- Modify: `CLAUDE.md` (one new bullet in the First Officer GOTCHAS list)

**Interfaces:**
- Consumes: step ids, labels, timeouts, and announcement wording exactly as implemented in Tasks 1-5 (read the committed flow files, not this plan, if in doubt — announcements are `"Waiting for: {label}"`, `"Timed out waiting for: {label}"`, `"{Flow name} flow stopped. Unable to complete: {label}"`, `"Already set: {label}"`, `"Skipping: {label}"`).
- Produces: nothing.

- [ ] **Step 1: Append the PMDG part to `docs\pmdg-737-first-officer-test-plan.md`**

Find the last "Part" heading letter in the doc and use the next letter (call it Part X below). Append:

```markdown
## Part X — APU-start gating (2026-07-06 pass; 737 + 777)

Ground-power→APU transfers now wait on real availability signals and ABORT the Before
Start flow on a start failure (ground power stays connected). After Landing APU waits
announce a timeout but continue.

### X.1 737 Before Start, happy path (cold & dark + GPU)
1. Cold & dark, GPU connected via the power-up flow. Run Before Start.
2. Expect, in order: "APU selector: ON" → "Waiting 2 seconds…" → "APU selector: START" →
   "Waiting for: Waiting for the APU to come on line" (selector spring-back) →
   "Waiting for: Waiting for the APU generator" → "APU generators: ON" →
   "Ground power: OFF" → rest of flow.
3. PASS: the APU GEN transfer and ground-power drop happen only after the blue
   APU GEN OFF BUS light is lit (spot-check the overhead panel state); no bus power loss.
4. Timing note for the 30 s budget on the generator wait: from the START command, note the
   seconds between the selector springing back and "APU generators: ON" being spoken —
   report if the generator wait ever announces "Timed out" on a healthy start.

### X.2 737 EGT threshold calibration
1. Cold & dark: FO window open, APU never started. Re-run Before Start — the three APU
   command steps must RUN (not "Already set"), proving IsApuRunning() reads false cold.
2. With the APU running: re-run Before Start — the three APU command steps must announce
   "Already set: …", proving EGT ≥ 100 while running. If either direction misbehaves,
   tune `AircraftStateEvaluator.ApuRunningEgt` (currently 100).

### X.3 737 re-run after completed transfer
1. Immediately after a successful X.1, run Before Start again.
2. Expect: APU command steps "Already set", the come-on-line wait passes instantly, the
   generator wait announces "Timed out waiting for: Waiting for the APU generator" then
   "Skipping: …" after 30 s (the blue light is out because the APU is on the buses), and
   the flow CONTINUES to completion. PASS: no "flow stopped", no state change.

### X.4 777 Before Start, happy path
1. Cold & dark + GPU (power-up flow), run Before Start.
2. Expect "Waiting for: Waiting for the APU to start" to END as the APU reaches speed
   (not a fixed 90 s — on the 777 the APU start typically completes near 60-90 s), then
   "Waiting 5 seconds: APU stabilising", then hydraulics/fuel/beacon, then the two
   ground-power disconnects. PASS: EICAS shows the APU running before the GPU drops and
   the buses never blink.
3. Re-run: the wait passes instantly (APURunning already 1).

### X.5 777 After Landing
1. After landing + rollout, run After Landing.
2. Expect the APU block to end with "Waiting for: Waiting for the APU to start" completing
   when the APU is actually up (previously it keyed on the selector and completed
   immediately). A timeout announces and the flow continues (Skip policy).

### X.6 Timeout/abort path (both PMDG jets, optional)
Hard to force without a scripted APU failure; the Stop mechanism is the same code path as
the shipped engine-start N2 aborts (Part tested earlier). If a start failure ever occurs
naturally: expect "Timed out waiting for: …" then "Before Start flow stopped. Unable to
complete: …", with ground power still connected.
```

- [ ] **Step 2: Append to `docs\fenix-first-officer-test-plan.md`**

```markdown
## APU-start gating (2026-07-06 pass)

One change: the Before Start "Waiting for APU available" wait now ABORTS the flow on its
180 s timeout instead of continuing to pulse external power off.

1. Happy path regression: cold & dark + ext power, run Before Start. Behaviour is
   unchanged — AVAIL light gates APU bleed / fuel pumps / external power off.
2. Failure path (forceable on the Fenix: run Before Start with no fuel on board, or pull
   the APU fire handle first): expect "Timed out waiting for: Waiting for APU available"
   then "Before Start flow stopped. Unable to complete: Waiting for APU available" after
   ~3 minutes, with external power still on the bus. Fix the cause, re-run the flow —
   completed steps announce "Already set" and the flow proceeds.
3. After Landing APU block: unchanged (timeout announces and continues).
```

- [ ] **Step 3: Append to `docs\fbw-a380-first-officer-test-plan.md`**

```markdown
## APU-start gating (2026-07-06 pass)

The FO now actually starts the A380 APU: master ON → 3 s → START pushbutton → wait for
AVAIL (Stop on timeout) → release START → APU bleed → external power off. Previously only
the master was set and external power was dropped immediately (APU never started).

1. Before Start happy path: cold & dark, ext power on (EFB). Run Before Start. Expect:
   "APU: ON" → "Waiting 3 seconds…" → "APU start" → "Waiting for: Waiting for APU
   available" (~1-2 min) → "APU start button: released" OR "Already set: APU start
   button: released" → "APU bleed: ON" → "Ground power: OFF" → captain EFB-disconnect
   reminder. PASS: ECAM shows APU AVAIL before the EXT PWR pushbuttons go off; buses
   never drop to batteries.
2. START PB auto-clear observation: at the "released" step, note which announcement fired.
   "Already set" = FBW cleared the latched PB itself; the explicit release = it did not.
   Report either way (informs whether the defensive clears stay).
3. Re-run idempotence: run Before Start again with the APU available — the whole APU block
   announces "Already set"/skips instantly; no re-start attempt.
4. Checklist path: reset to cold & dark + ext power. Tick "APU: ON" in the BEFORE_START
   checklist group instead of running the flow. PASS: the APU actually starts (master +
   START press) and AVAIL appears on ECAM ~1-2 min later. Then run the After Start flow
   and confirm "APU start button: released" precedes "APU: OFF".
5. After Landing: run the flow after vacating. Same block with a Skip-policy wait — a
   timeout announces "Skipping: …" and the remaining cleanup (anti-ice off etc.) still
   runs.
6. Timeout/abort: pull the APU FIRE pb (or run with empty feed tanks) and run Before
   Start. Expect the flow to stop after 3 min with external power untouched.
```

- [ ] **Step 4: CLAUDE.md gotcha bullet**

In the "First Officer Automation" section's GOTCHAS list (the `- **...**` bullets), append one bullet at the end:

```markdown
- **APU-start gating (2026-07-06, all four aircraft): Before Start never drops ground power until the aircraft's REAL "APU available" signal, and a timeout ABORTS the flow (Stop) with ground power connected.** Signals: 777 = the SDK `APURunning` bool (the SD's field — `ELEC_APU_Selector` reads 1 immediately after the ON command and proves nothing; the old fixed 90 s wait is gone); 737 = selector spring-back (starter cutout, Stop) THEN the blue `ELEC_annunAPU_GEN_OFF_BUS` annunciator (generator actually available — cutout happens while the APU is still spooling) before the GEN transfer buttons; Fenix = the `I_OH_ELEC_APU_START_U` AVAIL light (now Stop); A380 = master → dwell → **START PB press** (`A32NX_OVHD_APU_START_PB_IS_ON` — master alone never starts the FBW APU; the old flow left it master-only and dropped EXT PWR onto batteries) → `A32NX_OVHD_APU_START_PB_IS_AVAILABLE` wait → defensive START release (no evidence FBW auto-clears the latched PB). The 737 generator wait is deliberately Skip-30s (light-off is ambiguous with "already on the buses" on a re-run — do NOT add a running-and-light-off SkipCondition; that is exactly the normal just-past-cutout state). After Landing APU waits are Skip by design (engines running, no hazard). 737 `IsApuRunning()` is EGT-based (`ApuRunningEgt`, tunable) — the selector position only proves where the switch is.
```

- [ ] **Step 5: Build (docs don't compile, but catch accidental code edits)**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add docs/pmdg-737-first-officer-test-plan.md docs/fenix-first-officer-test-plan.md docs/fbw-a380-first-officer-test-plan.md CLAUDE.md
git commit -m "docs(fo): APU-gating in-sim test plans + CLAUDE.md gotcha"
```

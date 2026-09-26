# FO Shutdown/Secure Tightening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. NOTE: this plan is intended to be executed via a per-aircraft Workflow (Tasks 1–5 are independent files and parallelize; Task 6 depends on all).

**Goal:** Defer transponder→standby to the Shutdown flow, turn Airbus LS pushbuttons off at Shutdown, and make Secure/Parking power down ground power + APU — consistently across the PMDG 777, PMDG 737, FBW A320, Fenix A320, and FBW A380 First Officer.

**Architecture:** Data-definition edits only, in `MSFSBlindAssist/FirstOfficer/`. Each aircraft owns a `*FlowDefinitions.cs` (proactive FO flows) and a `*ChecklistDefinitions.cs` (read-and-confirm; `Auto` items also actuate). Flows and checklists change together. Every event/L:var/pattern already exists — the "off" forms are copied from each aircraft's own Before-Start (Boeing GPU) or approach (Airbus LS) steps. No executor / state-evaluator / panel changes.

**Tech Stack:** C# 13 / .NET 10, xUnit for the pure-logic guardrail tests.

## Global Constraints

- Build the **solution**, never the bare csproj: `dotnet build MSFSBlindAssist.sln -c Debug` (or `-p:Platform=x64`). Close the running app first (exe is file-locked, MSB3021).
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
- `main` is protected — work stays on `feature/first-officer`.
- **Id alignment invariant:** whenever a checklist item moves group or is renamed, update the flow step `Id`, its `CompletesChecklistItemId`, and the checklist item `Id` together so they stay in agreement (completion-latch depends on this — see `docs/first-officer.md`).
- **Idempotent off-steps:** every added off/disconnect step carries a skip-guard so re-running no-ops and so it does nothing when a GPU was never connected.
- **Fenix pulses:** LS off uses `e.Pulse("S_FCU_EFISn_LS")` (base var, never `_PRESS`), guarded so it fires only when the indicator `I_FCU_EFISn_LS` reads on.
- No new SimConnect definitions; no FMC/CDU programming.

---

### Task 1: FBW A320 — move XPDR+TCAS to Shutdown, add LS-off

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320FlowDefinitions.cs` (After Landing ~L433–436; Shutdown ~L463–498)
- Modify: `MSFSBlindAssist/FirstOfficer/FBWA320/FbwA320ChecklistDefinitions.cs` (AFTER_LANDING group L389; SHUTDOWN group L424–466; approach LS pattern to mirror L360–363)

**Interfaces:**
- Produces (asserted by Task 6): Shutdown flow contains steps `SD_XPDR_STBY` and `SD_TCAS_STBY`; After-Landing flow contains neither `AL_XPDR_STBY` nor `AL_TCAS_STBY`. Shutdown flow contains `SD_LS1`/`SD_LS2`.

- [ ] **Step 1: Flow — remove the two After-Landing transponder steps.** In `BuildAfterLanding()`, delete:
```csharp
Done(Skip(SW("AL_XPDR_STBY", "Transponder: STANDBY", "A32NX_TRANSPONDER_MODE", 0),
    s => s.IsPosition("A32NX_TRANSPONDER_MODE", 0)), "AL_XPDR_STBY"),
Skip(SW("AL_TCAS_STBY", "TCAS: STANDBY", "A32NX_SWITCH_TCAS_POSITION", 0),
    s => s.IsPosition("A32NX_SWITCH_TCAS_POSITION", 0)),
```

- [ ] **Step 2: Flow — add the renamed steps to Shutdown.** In `BuildShutdown()`, after `SD_ENG2_OFF` (engine masters off), insert:
```csharp
Done(Skip(SW("SD_XPDR_STBY", "Transponder: STANDBY", "A32NX_TRANSPONDER_MODE", 0),
    s => s.IsPosition("A32NX_TRANSPONDER_MODE", 0)), "SD_XPDR_STBY"),
Skip(SW("SD_TCAS_STBY", "TCAS: STANDBY", "A32NX_SWITCH_TCAS_POSITION", 0),
    s => s.IsPosition("A32NX_SWITCH_TCAS_POSITION", 0)),
```

- [ ] **Step 3: Flow — add LS-off to Shutdown.** Immediately after the steps from Step 2, insert (mirrors approach `AP_LS1`/`AP_LS2` at L404–407, inverted to 0):
```csharp
Done(Skip(SW("SD_LS1", "LS captain: OFF", "A32NX_EFIS_L_LS_BUTTON_IS_ON", 0),
    s => !s.IsOn("A32NX_EFIS_L_LS_BUTTON_IS_ON")), "SD_LS1"),
Done(Skip(SW("SD_LS2", "LS first officer: OFF", "A32NX_EFIS_R_LS_BUTTON_IS_ON", 0),
    s => !s.IsOn("A32NX_EFIS_R_LS_BUTTON_IS_ON")), "SD_LS2"),
```

- [ ] **Step 4: Checklist — move XPDR item to SHUTDOWN.** In `FbwA320ChecklistDefinitions.cs`, delete from the AFTER_LANDING group (L389):
```csharp
Auto("AL_XPDR_STBY", "AFTER_LANDING", "Transponder: STANDBY", "A32NX_TRANSPONDER_MODE",
    v => v < 0.5, (e, _) => e.Set("A32NX_TRANSPONDER_MODE", 0)),
```
and add to the SHUTDOWN group (after `SD_ENG2_OFF`, renamed):
```csharp
Auto("SD_XPDR_STBY", "SHUTDOWN", "Transponder: STANDBY", "A32NX_TRANSPONDER_MODE",
    v => v < 0.5, (e, _) => e.Set("A32NX_TRANSPONDER_MODE", 0)),
```

- [ ] **Step 5: Checklist — add LS-off to SHUTDOWN.** After the item from Step 4, insert (mirrors `AP_LS1`/`AP_LS2` L360–363, inverted):
```csharp
Auto("SD_LS1", "SHUTDOWN", "LS captain: OFF", "A32NX_EFIS_L_LS_BUTTON_IS_ON",
    v => v < 0.5, (e, _) => e.Set("A32NX_EFIS_L_LS_BUTTON_IS_ON", 0)),
Auto("SD_LS2", "SHUTDOWN", "LS first officer: OFF", "A32NX_EFIS_R_LS_BUTTON_IS_ON",
    v => v < 0.5, (e, _) => e.Set("A32NX_EFIS_R_LS_BUTTON_IS_ON", 0)),
```

- [ ] **Step 6: Build.** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded.

- [ ] **Step 7: Commit.**
```bash
git add MSFSBlindAssist/FirstOfficer/FBWA320/
git commit -m "feat(fo): A320 — XPDR/TCAS standby moves to shutdown, LS off at shutdown"
```

---

### Task 2: Fenix A320 — move XPDR+TCAS to Shutdown, add LS-off

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixFlowDefinitions.cs` (After Landing ~L382–385; Shutdown ~L412–445; approach LS pattern L353)
- Modify: `MSFSBlindAssist/FirstOfficer/Fenix/FenixChecklistDefinitions.cs` (AFTER_LANDING XPDR L341–342; SHUTDOWN group L366–399; approach LS pattern L314–319)

**Interfaces:**
- Produces (asserted by Task 6): Shutdown flow contains `SD_XPDR_STBY`, `SD_TCAS_STBY`, `SD_LS1`, `SD_LS2`; After-Landing flow contains no XPDR/TCAS standby step.

- [ ] **Step 1: Flow — remove After-Landing transponder steps.** In `BuildAfterLanding()`, delete:
```csharp
Done(Skip(SW("AL_XPDR_STBY", "Transponder: STANDBY", "S_XPDR_OPERATION", 0),
    s => s.IsPosition("S_XPDR_OPERATION", 0)), "AL_XPDR_STBY"),
Skip(SW("AL_TCAS_STBY", "TCAS: STANDBY", "S_XPDR_MODE", 0),
    s => s.IsPosition("S_XPDR_MODE", 0)),
```

- [ ] **Step 2: Flow — add renamed steps to Shutdown.** In `BuildShutdown()`, after `SD_ENG2_OFF`, insert:
```csharp
Done(Skip(SW("SD_XPDR_STBY", "Transponder: STANDBY", "S_XPDR_OPERATION", 0),
    s => s.IsPosition("S_XPDR_OPERATION", 0)), "SD_XPDR_STBY"),
Skip(SW("SD_TCAS_STBY", "TCAS: STANDBY", "S_XPDR_MODE", 0),
    s => s.IsPosition("S_XPDR_MODE", 0)),
```

- [ ] **Step 3: Flow — add LS-off (pulse, guarded) to Shutdown.** After Step 2's steps, insert (mirrors approach `AP_LS1` flow at L353 which pulses `S_FCU_EFIS1_LS` when off; here pulse only when the indicator is ON):
```csharp
Skip(SW("SD_LS1", "LS captain: OFF", "S_FCU_EFIS1_LS", 1),
    s => !s.IsOn("I_FCU_EFIS1_LS")),
Skip(SW("SD_LS2", "LS first officer: OFF", "S_FCU_EFIS2_LS", 1),
    s => !s.IsOn("I_FCU_EFIS2_LS")),
```
(`S_FCU_EFISn_LS` is a momentary pulse actuator per `FenixActionExecutor` dispatch table; the flow `SW` fires it and the skip-guard prevents pulsing when the LS is already off. Confirm the flow `SW` helper routes `S_FCU_EFISn_LS` through the pulse dispatch — it is the same actuator the approach step uses.)

- [ ] **Step 4: Checklist — move XPDR item to SHUTDOWN.** In `FenixChecklistDefinitions.cs`, delete from AFTER_LANDING (L341):
```csharp
Auto("AL_XPDR_STBY", "AFTER_LANDING", "Transponder: STANDBY",
    "S_XPDR_OPERATION", v => v < 0.5, (e, _) => e.Set("S_XPDR_OPERATION", 0)),
```
add to SHUTDOWN (after `SD_ENG2_OFF`, renamed):
```csharp
Auto("SD_XPDR_STBY", "SHUTDOWN", "Transponder: STANDBY",
    "S_XPDR_OPERATION", v => v < 0.5, (e, _) => e.Set("S_XPDR_OPERATION", 0)),
```

- [ ] **Step 5: Checklist — add LS-off to SHUTDOWN.** After Step 4's item, insert (mirrors `AP_LS1`/`AP_LS2` L314–319, inverted guard — pulse only when currently on):
```csharp
Auto("SD_LS1", "SHUTDOWN", "LS captain: OFF",
    "I_FCU_EFIS1_LS", v => v < 0.5,
    (e, s) => !s.IsOn("I_FCU_EFIS1_LS") ? Task.CompletedTask : e.Pulse("S_FCU_EFIS1_LS")),
Auto("SD_LS2", "SHUTDOWN", "LS first officer: OFF",
    "I_FCU_EFIS2_LS", v => v < 0.5,
    (e, s) => !s.IsOn("I_FCU_EFIS2_LS") ? Task.CompletedTask : e.Pulse("S_FCU_EFIS2_LS")),
```

- [ ] **Step 6: Build.** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded.

- [ ] **Step 7: Commit.**
```bash
git add MSFSBlindAssist/FirstOfficer/Fenix/
git commit -m "feat(fo): Fenix — XPDR/TCAS standby moves to shutdown, LS off at shutdown"
```

---

### Task 3: PMDG 777 — drop duplicate After-Landing XPDR, add Secure ground-power off

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs` (After Landing `AL_XPNDR_STBY` L465; Secure L531–551)
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG777ChecklistDefinitions.cs` (After Landing `AL_TRANSPONDER` L652–654; Secure group L746+)

**Interfaces:**
- Produces (asserted by Task 6): After-Landing flow has no `AL_XPNDR_STBY`; After-Landing checklist has no `AL_TRANSPONDER`; Secure flow contains a ground-power-off step; Shutdown's `SD_XPNDR_STBY` unchanged.

- [ ] **Step 1: Flow — remove the After-Landing XPDR step.** In `BuildAfterLanding()`, delete:
```csharp
SW("AL_XPNDR_STBY",   "Transponder: STBY",     "EVT_TCAS_MODE",               0),
```

- [ ] **Step 2: Flow — add ground-power-off to Secure.** In `BuildSecure()`, after `SEC_APU_OFF`/`SEC_APU_WAIT` (before `SEC_BATTERY_OFF`), insert (mirrors Before-Start disconnect at L242–246):
```csharp
Skip(Momentary("SEC_GND_PWR_PRIM", "Ground power primary: PUSH",
    "EVT_OH_ELEC_GRD_PWR_PRIM_SWITCH"), s => !s.IsGpuPower1On()),
Skip(Momentary("SEC_GND_PWR_SEC", "Ground power secondary: PUSH",
    "EVT_OH_ELEC_GRD_PWR_SEC_SWITCH"),  s => !s.IsGpuPower2On()),
```

- [ ] **Step 3: Checklist — remove the After-Landing XPDR item.** In `PMDG777ChecklistDefinitions.cs`, delete from AFTER_LANDING (L652–654):
```csharp
Auto("AL_TRANSPONDER", "AFTER_LANDING", "Transponder: STBY",
    "XPDR_ModeSel", v => v < 0.5,
    action: (e, _) => e.SetTransponderMode(0)),
```

- [ ] **Step 4: Checklist — add ground-power-off to Secure.** Read the SECURE group body (from L746) and the ELEC_POWER_UP ground-power-on checklist item; add a Secure item that turns ground power off, mirroring the on-item's actuation inverted and guarded by GPU state. Use the momentary push executor and gate on `IsAnyGpuOn()`:
```csharp
ActionManual("SEC_GND_PWR_OFF", "SECURE", "Ground power: OFF",
    async (e, s) => { if (s.IsAnyGpuOn()) await e.PushBothGroundPowerAsync(); }),
```
(If the SECURE checklist group uses `SECURE_CL`/`ELEC_POWER_DOWN` related ids, place this alongside the APU/battery items; keep it a single confirm line. Match the exact `ActionManual`/`ActionManualAsync` helper signature present in this file.)

- [ ] **Step 5: Build.** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded.

- [ ] **Step 6: Commit.**
```bash
git add MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs MSFSBlindAssist/FirstOfficer/PMDG777ChecklistDefinitions.cs
git commit -m "feat(fo): PMDG 777 — drop duplicate after-landing XPDR, secure drops ground power"
```

---

### Task 4: PMDG 737 — add Secure APU-off + ground-power-off, remove EPD_PWR reminder

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs` (Secure L447–461)
- Modify: `MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs` (Secure group L385–393; ELEC_POWER_DOWN `EPD_PWR` L404)

**Interfaces:**
- Produces (asserted by Task 6): Secure flow contains an APU-off step and a ground-power-off step; the `EPD_PWR` reminder no longer exists.

- [ ] **Step 1: Flow — add APU-off + ground-power-off to Secure.** In `BuildSecure()`, after `SE_PACKS_OFF`, insert (APU selector 0=OFF via `EVT_OH_LIGHTS_APU_START`; GPU off via `EVT_OH_ELEC_GRD_PWR_SWITCH`, guarded by `IsGpuOn()`):
```csharp
SW("SE_APU_OFF", "APU: OFF", "EVT_OH_LIGHTS_APU_START", 0),
Skip(SW("SE_GND_PWR_OFF", "Ground power: OFF", "EVT_OH_ELEC_GRD_PWR_SWITCH", 0),
    s => !s.IsGpuOn()),
```

- [ ] **Step 2: Checklist — add APU-off + ground-power-off to SECURE.** In `PMDG737ChecklistDefinitions.cs`, after `SE_PACKS`, insert:
```csharp
Auto("SE_APU_OFF", "SECURE", "APU: OFF", "APU_Selector", v => v < 0.5, (e, _) => e.SetApuSelector(0)),
ActionManual("SE_GND_PWR_OFF", "SECURE", "Ground power: OFF",
    (e, s) => { if (s.IsGpuOn()) e.SetGroundPower(0); return Task.CompletedTask; }),
```
(Match the exact `ActionManual` helper signature used in this file — see `EPU_GPU` at L77 for the ground-power on form; here it is inverted to OFF and guarded.)

- [ ] **Step 3: Checklist — remove the redundant reminder.** Delete the `ELEC_POWER_DOWN` `EPD_PWR` reminder (L404):
```csharp
Reminder("EPD_PWR", "ELEC_POWER_DOWN", "APU or ground power: OFF (after the 2-minute APU cooldown)"),
```
If that leaves the `ELEC_POWER_DOWN` group empty, remove the now-empty group definition and any reference to `"ELEC_POWER_DOWN"` in a flow's `RelatedChecklistGroupIds`.

- [ ] **Step 4: Build.** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded.

- [ ] **Step 5: Commit.**
```bash
git add MSFSBlindAssist/FirstOfficer/PMDG737/
git commit -m "feat(fo): PMDG 737 — secure turns off APU + ground power, drop manual power-down reminder"
```

---

### Task 5: FBW A380 — Parking ext-power off + APU off, drop redundant After-Landing TCAS reminder

**Files:**
- Modify: `MSFSBlindAssist/FirstOfficer/FBWA380/FbwA380FlowDefinitions.cs` (After Landing `AL_TCAS` L418; Parking L452–497; Before-Start ext-pwr-off pattern L190–191)
- Modify: `MSFSBlindAssist/FirstOfficer/FBWA380/FbwA380ChecklistDefinitions.cs` (`AFTER_LANDING_CL` TCAS reminder if present; `PARKING_CL` group)

**Interfaces:**
- Produces (asserted by Task 6): After-Landing flow has no `AL_TCAS` reminder; Parking flow contains `PK_EXTPWR_OFF` and `PK_APU_OFF`.

- [ ] **Step 1: Flow — remove the redundant After-Landing TCAS reminder.** In `BuildAfterLanding()`, delete:
```csharp
Captain("AL_TCAS", "TCAS mode: standby"),
```

- [ ] **Step 2: Flow — append ext-power off + APU off to Parking.** In `BuildParking()`, after `PK_COCKPITDOOR` (end of the flow, before/after the final `PK_WAIT_SB`), insert (mirrors Before-Start ext-pwr-off Multi at L190–191, with a guard so it no-ops when no external power is on):
```csharp
Skip(Multi("PK_EXTPWR_OFF", "External power: OFF",
    ("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON", 0),
    ("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON", 0), ("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON", 0)),
    s => !s.IsOn("A32NX_OVHD_ELEC_EXT_PWR_1_PB_IS_ON")
      && !s.IsOn("A32NX_OVHD_ELEC_EXT_PWR_2_PB_IS_ON")
      && !s.IsOn("A32NX_OVHD_ELEC_EXT_PWR_3_PB_IS_ON")
      && !s.IsOn("A32NX_OVHD_ELEC_EXT_PWR_4_PB_IS_ON")),
Skip(SW("PK_APU_OFF", "APU master: OFF", "A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0),
    s => s.IsPosition("A32NX_OVHD_APU_MASTER_SW_PB_IS_ON", 0)),
```
(Verify `FbwA380StateEvaluator` exposes `IsOn(...)` / `IsPosition(...)` — it registers the four `A32NX_OVHD_ELEC_EXT_PWR_{1..4}_PB_IS_ON` vars; the four are already used by `CP_GPU`/Before-Start so they are in the def set.)

- [ ] **Step 3: Checklist — mirror in PARKING_CL, drop After-Landing TCAS if present.** In `FbwA380ChecklistDefinitions.cs`: if the `AFTER_LANDING_CL` group has a TCAS-standby reminder mirroring `AL_TCAS`, remove it. Add to the `PARKING_CL` group two confirm items for external-power-off and APU-master-off, matching the group's existing item helper form (a `Reminder`/`Auto` as used by the other Parking checklist items; if they are captain reminders, add `Reminder` lines "External power: OFF" and "APU master: OFF").

- [ ] **Step 4: Build.** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded.

- [ ] **Step 5: Commit.**
```bash
git add MSFSBlindAssist/FirstOfficer/FBWA380/
git commit -m "feat(fo): A380 — parking drops ext power + APU, remove redundant after-landing TCAS reminder"
```

---

### Task 6: Guardrail tests + full build + test run

**Files:**
- Create: `tests/MSFSBlindAssist.Tests/FoShutdownSecureTighteningTests.cs`
- Test: the five flow/checklist definition classes edited in Tasks 1–5.

**Interfaces:**
- Consumes: the flow/checklist definitions from Tasks 1–5 (public static builders or the assembled group/flow lists — inspect each `*FlowDefinitions`/`*ChecklistDefinitions` for how flows/groups are exposed; use the same accessor the app uses to enumerate them).

- [ ] **Step 1: Write the failing tests.** Create `FoShutdownSecureTighteningTests.cs`. For each aircraft, walk its flow list and assert step ids. Example shape (adapt the enumeration accessor to what each definitions class exposes — e.g. a static method returning `IReadOnlyList<FlowDefinition<...>>`):
```csharp
using Xunit;
using MSFSBlindAssist.FirstOfficer; // + per-aircraft namespaces

public class FoShutdownSecureTighteningTests
{
    private static IEnumerable<string> StepIds(dynamic flow) =>
        ((IEnumerable<dynamic>)flow.Steps).Select(s => (string)s.Id);

    [Fact] public void A320_Xpdr_Not_In_AfterLanding_And_In_Shutdown()
    {
        var flows = FbwA320FlowDefinitions.GetFlows(); // adapt accessor
        var al = flows.First(f => f.Id == "AFTER_LANDING");
        var sd = flows.First(f => f.Id == "SHUTDOWN");
        Assert.DoesNotContain("AL_XPDR_STBY", StepIds(al));
        Assert.DoesNotContain("AL_TCAS_STBY", StepIds(al));
        Assert.Contains("SD_XPDR_STBY", StepIds(sd));
        Assert.Contains("SD_TCAS_STBY", StepIds(sd));
        Assert.Contains("SD_LS1", StepIds(sd));
        Assert.Contains("SD_LS2", StepIds(sd));
    }
    // Fenix: same as A320 with FenixFlowDefinitions.
    // PMDG777: AFTER_LANDING has no "AL_XPNDR_STBY"; SECURE contains "SEC_GND_PWR_PRIM".
    // PMDG737: SECURE contains "SE_APU_OFF" and "SE_GND_PWR_OFF".
    // A380: AFTER_LANDING has no "AL_TCAS"; PARKING contains "PK_EXTPWR_OFF" and "PK_APU_OFF".
}
```
Write one `[Fact]` per aircraft (five total) covering the ids listed in each task's **Produces** block. If the definitions classes expose flows via a different accessor (instance property, `IFoProfile`), use that — inspect the class before writing.

- [ ] **Step 2: Run tests — verify they fail** (or fail to compile against the pre-edit tree if run before Tasks 1–5). Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`. Expected: the new asserts FAIL before the edits land / PASS after.

- [ ] **Step 3: Run the full suite — verify green.** Run the same command. Expected: all tests PASS (the five new + the existing suite).

- [ ] **Step 4: Full solution build.** `dotnet build MSFSBlindAssist.sln -c Debug` → Build succeeded, 0 errors.

- [ ] **Step 5: Commit.**
```bash
git add tests/MSFSBlindAssist.Tests/FoShutdownSecureTighteningTests.cs
git commit -m "test(fo): guardrail tests for shutdown/secure transponder+LS+power-down placement"
```

---

## In-sim test plan (human-run, include in PR)

Per aircraft:
1. **After Landing:** run the flow — confirm the transponder stays **active** (not STBY) after vacating.
2. **Shutdown:** run the flow — confirm XPDR → STBY, and (A320/Fenix) both LS pushbuttons go OFF.
3. **Secure / Parking:** run the flow — confirm APU goes OFF and ground power goes OFF; re-run to confirm the off-steps no-op cleanly; confirm they announce nothing untoward when no GPU is connected.
4. **A380 caveat:** confirm Parking's APU-off leaves the aircraft on battery (expected) and doesn't strand any downstream step.

## Self-review notes

- **Spec coverage:** Change 1 → Tasks 1,2,3,5 (737 already correct); Change 2 → Tasks 1,2; Change 3 → Tasks 3,4,5 (A320/Fenix already correct). Guardrails → Task 6. All spec sections mapped.
- **Id consistency:** moved Airbus ids are uniformly `SD_XPDR_STBY`/`SD_TCAS_STBY`/`SD_LS1`/`SD_LS2`; 777 secure GPU = `SEC_GND_PWR_PRIM`/`_SEC`; 737 secure = `SE_APU_OFF`/`SE_GND_PWR_OFF`; A380 parking = `PK_EXTPWR_OFF`/`PK_APU_OFF`. Task 6 asserts these exact ids.
- **Accessor caveat:** Task 6 Step 1 depends on how each `*FlowDefinitions` class exposes its flow list; the implementing agent must inspect the class and use the real accessor rather than the illustrative `GetFlows()`.

# FO Preflight Fire / Warning Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Real, self-completing fire tests in the FO preflight flows of the PMDG 737 (plus stall-shaker and overspeed-clacker tests), PMDG 777 and FBW A380, mirrored as checklist-tab items — Fenix parity.

**Architecture:** Each aircraft executor gains a gated press→hold→release method and a pseudo-key interception in `ExecuteStepAsync` (the Fenix `FIRE_TEST_*` pattern). Flow steps reference the pseudo-keys and tick their checklist items via `CompletesChecklistItemId`; checklist items are `Actionable` (manual-tick) firing the same executor methods. No shared-model changes.

**Tech Stack:** C# 13 / .NET 10 WinForms, PMDG SDK CDA + TransmitClientEvent, FBW L:var calc path.

**Spec:** [docs/design/2026-07-11-fo-fire-tests-design.md](2026-07-11-fo-fire-tests-design.md)

## Global Constraints

- Build ONLY via `dotnet build MSFSBlindAssist.sln -c Debug` (never the bare .csproj — AnyCPU trap). Verify `MSFSBlindAssist\bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` timestamp when a run matters.
- Branch `feature/first-officer`; `main` is protected — commit here, never to main.
- Sim-facing behavior is NOT unit-tested (repo convention overrides TDD): verification = solution build + existing suite green (`dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`) + in-sim test-plan docs.
- No `*_CL` readback group may gain a CheckAction. All new checklist items go in STATE/ACTION groups only.
- All writes stay behind each executor's serialize gate with its pacing (`CdaWriteSpacingMs` 350 PMDG / `WriteSpacingMs` 200 A380). Never two CDA writes in one frame.
- No new announcements — FlowManager step narration + the audible warnings are the whole UX (screen-reader rules).
- Hold times (user-specified): 737 fire 2000 ms, stall 3500 ms, overspeed 1500 ms; 777 fire 3000 ms; A380 fire 3000 ms.
- Live-verified facts (do not re-litigate): 737 `EVT_FIRE_DETECTION_TEST_SWITCH` CDA position write HOLDS (0=FAULT/INOP, 1=neutral, 2=OVHT/FIRE); 737 warning-test buttons actuate via transmit LEFTSINGLE (0x20000000) / LEFTRELEASE (0x00020000); 777 CDA param 0 is NEVER a release (doctrine) — its fire test must use the transmit press/release shape.

---

### Task 1: 737 executor — held-test methods + pseudo-key interception

**Files:**
- Modify: `MSFSBlindAssist\FirstOfficer\PMDG737\AircraftActionExecutor.cs`

**Interfaces:**
- Consumes: `SimConnectManager.SendPMDGEvent(string, uint, int?)` (CDA write), `SimConnectManager.SendPMDGEventViaTransmitWithTarget(uint, uint)` (single-flag transmit), `PMDG737Definition.EventIds`, existing `_dispatchGate`/`PaceAsync()`/`_lastWriteUtc`.
- Produces: `public Task<bool> FireDetectionTestAsync()`, `public Task<bool> WarningTestAsync(string eventName, int holdMs)`, pseudo-keys `"FIRE_TEST"`, `"STALL_TEST_1"`, `"STALL_TEST_2"`, `"OVSPD_TEST_1"`, `"OVSPD_TEST_2"` handled in `ExecuteStepAsync`, and `public const int StallTestHoldMs = 3500; public const int OverspeedTestHoldMs = 1500; public const int FireTestHoldMs = 2000;` (public so the checklist defs reference them).

- [ ] **Step 1: Add constants** — next to `CdaWriteSpacingMs` (~line 59):

```csharp
    /// <summary>Held-test durations (user-tuned 2026-07-11): the OVHT/FIRE detection test
    /// switch is HELD at position 2 (fire bell + staggered annunciators, live-probed:
    /// FIRE WARN immediately, handle lights by ~1.7 s), the stall/Mach-IAS warning test
    /// buttons are held via transmit press/release (stick shaker / clacker sound while
    /// held; PMDG drives no readable state for them — audio IS the verification).</summary>
    public const int FireTestHoldMs = 2000;
    public const int StallTestHoldMs = 3500;
    public const int OverspeedTestHoldMs = 1500;

    // Transmit mouse flags for held warning-test buttons (press … release). The CDA
    // write path is NOT used for these — only the '#id' transmit moves them (live-probed
    // 2026-07-11, same finding family as the walked rotaries).
    private const uint MouseFlagLeftSingleU  = 0x20000000u;
    private const uint MouseFlagLeftReleaseU = 0x00020000u;
```

- [ ] **Step 2: Intercept pseudo-keys in `ExecuteStepAsync`** (currently ~line 207) — insert the Fenix-style switch BEFORE the ActionType switch:

```csharp
    public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        if (!IsAvailable) return Task.FromResult(false);
        // Pseudo-keys for held self-completing tests (Fenix FIRE_TEST_* precedent):
        // not real PMDG events — intercepted here so flow steps stay data-driven.
        if (step.ActionType == Models.FlowStepActionType.SetSwitch)
        {
            switch (step.EventName)
            {
                case "FIRE_TEST":    return FireDetectionTestAsync();
                case "STALL_TEST_1": return WarningTestAsync("EVT_OH_WARNING_TEST_STALL_1_PUSH", StallTestHoldMs);
                case "STALL_TEST_2": return WarningTestAsync("EVT_OH_WARNING_TEST_STALL_2_PUSH", StallTestHoldMs);
                case "OVSPD_TEST_1": return WarningTestAsync("EVT_OH_WARNING_TEST_MACH_IAS_1_PUSH", OverspeedTestHoldMs);
                case "OVSPD_TEST_2": return WarningTestAsync("EVT_OH_WARNING_TEST_MACH_IAS_2_PUSH", OverspeedTestHoldMs);
            }
        }
        return step.ActionType switch
        {
            Models.FlowStepActionType.SetSwitch         => DispatchAsync(step.EventName!, step.TargetValue),
            Models.FlowStepActionType.SetSwitchMultiple => MultiAsync(step.MultiActions),
            _                                           => Task.FromResult(false),
        };
    }
```

- [ ] **Step 3: Add the two held-test methods** — place near `FireMomentaryToggleAsync` (~line 395). Both hold `_dispatchGate` for the WHOLE press-hold-release (serialized like a walk; `WaitForDispatchDrainAsync` then covers the checklist revert grace automatically):

```csharp
    /// <summary>OVHT/FIRE detection test: HOLD the spring-loaded switch at OVHT/FIRE
    /// (position 2 — fire bell + FIRE WARN masters + handle lights), then release back
    /// to neutral (1). CDA position writes move AND hold this switch (live-probed
    /// 2026-07-11); the write-back is required — nothing auto-releases it.</summary>
    public async Task<bool> FireDetectionTestAsync()
    {
        var sc = _sc;
        if (sc == null || !PMDG737Definition.EventIds.TryGetValue("EVT_FIRE_DETECTION_TEST_SWITCH", out int evId))
            return false;
        await _dispatchGate.WaitAsync();
        try
        {
            await PaceAsync();
            sc.SendPMDGEvent("EVT_FIRE_DETECTION_TEST_SWITCH", (uint)evId, 2);
            _lastWriteUtc = DateTime.UtcNow;
            await Task.Delay(FireTestHoldMs);
            sc.SendPMDGEvent("EVT_FIRE_DETECTION_TEST_SWITCH", (uint)evId, 1);
            _lastWriteUtc = DateTime.UtcNow;
            return true;
        }
        finally { _dispatchGate.Release(); }
    }

    /// <summary>Held aft-overhead warning-test button (stall shaker / overspeed clacker):
    /// transmit LEFTSINGLE press, hold, LEFTRELEASE. These buttons have no CDA state
    /// field and no CDA-write actuation — transmit-only, sound-only feedback.</summary>
    public async Task<bool> WarningTestAsync(string eventName, int holdMs)
    {
        var sc = _sc;
        if (sc == null || !PMDG737Definition.EventIds.TryGetValue(eventName, out int evId))
            return false;
        await _dispatchGate.WaitAsync();
        try
        {
            await PaceAsync();
            sc.SendPMDGEventViaTransmitWithTarget((uint)evId, MouseFlagLeftSingleU);
            _lastWriteUtc = DateTime.UtcNow;
            await Task.Delay(holdMs);
            sc.SendPMDGEventViaTransmitWithTarget((uint)evId, MouseFlagLeftReleaseU);
            _lastWriteUtc = DateTime.UtcNow;
            return true;
        }
        finally { _dispatchGate.Release(); }
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 Errors.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG737/AircraftActionExecutor.cs
git commit -m "feat(fo): 737 executor held fire/stall/overspeed test methods + pseudo-keys"
```

---

### Task 2: 737 flow + checklist — replace PF_TESTS with the five real tests

**Files:**
- Modify: `MSFSBlindAssist\FirstOfficer\PMDG737\PMDG737FlowDefinitions.cs`
- Modify: `MSFSBlindAssist\FirstOfficer\PMDG737\PMDG737ChecklistDefinitions.cs`

**Interfaces:**
- Consumes: Task 1's pseudo-keys + `FireDetectionTestAsync()` / `WarningTestAsync(eventName, holdMs)` + the public hold constants; existing helpers `SW(id, label, eventName, target, verifyField, verifyCond, checklistItemId, isMomentary)` and `WalkAround`; checklist helpers `ActionManual`/`Reminder` + the `Item` alias.
- Produces: flow step ids `PF_FIRE_TEST`, `PF_STALL_TEST1`, `PF_STALL_TEST2`, `PF_OVSPD_TEST1`, `PF_OVSPD_TEST2`; checklist item ids identical; new checklist helper `ActionManualAsync`.

- [ ] **Step 1: Flow — insert the five steps right after `PF_WALK`** (line 89 `WalkAround("PF_WALK", "Exterior walk-around", 120),`):

```csharp
            // Fire + warning tests (held self-completing tests via executor pseudo-keys;
            // the fire bell / stick shaker / overspeed clacker are the blind-pilot
            // verification — Fenix parity, live-probed 2026-07-11).
            SW("PF_FIRE_TEST", "Fire warning test — listen for the fire bell",
                "FIRE_TEST", 1, checklistItemId: "PF_FIRE_TEST"),
            SW("PF_STALL_TEST1", "Stall warning test 1 — listen for the stick shaker",
                "STALL_TEST_1", 1, checklistItemId: "PF_STALL_TEST1"),
            SW("PF_STALL_TEST2", "Stall warning test 2 — listen for the stick shaker",
                "STALL_TEST_2", 1, checklistItemId: "PF_STALL_TEST2"),
            SW("PF_OVSPD_TEST1", "Overspeed warning test 1 — listen for the clacker",
                "OVSPD_TEST_1", 1, checklistItemId: "PF_OVSPD_TEST1"),
            SW("PF_OVSPD_TEST2", "Overspeed warning test 2 — listen for the clacker",
                "OVSPD_TEST_2", 1, checklistItemId: "PF_OVSPD_TEST2"),
```

- [ ] **Step 2: Flow — delete the dead catch-all reminder** (line 129):

Delete exactly: `Captain("PF_TESTS", "Perform the overhead and fire tests as required."),`
(`Captain("PF_ALT", "Set the altimeters to the local QNH."),` on line 128 STAYS.)

- [ ] **Step 3: Checklist — add the `ActionManualAsync` helper** next to `ActionManual` (~line 601), mirroring the 777's existing helper verbatim in shape:

```csharp
    // Actionable manual-tick item whose CheckAction is ASYNC (a held test / spaced CDA
    // writes) — the revert-grace machinery awaits the Task, so the tick's protection
    // holds until the whole press-hold-release completes (777 helper precedent).
    private static Item ActionManualAsync(string id, string groupId, string label,
        Func<AircraftActionExecutor, AircraftStateEvaluator, Task> action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = action,
    };
```

- [ ] **Step 4: Checklist — insert the five items at the TOP of the PREFLIGHT group** (before `Auto("PF_YD", …)`, mirroring the flow order):

```csharp
            // Fire + warning tests — no persistent sim state exists for a completed test,
            // so these are manual-tick actions: the box records "test performed" and the
            // tick fires the same held test the flow runs (Fenix PF_FIRE_* pattern).
            ActionManualAsync("PF_FIRE_TEST", "PREFLIGHT", "Fire warning test",
                (e, _) => e.FireDetectionTestAsync()),
            ActionManualAsync("PF_STALL_TEST1", "PREFLIGHT", "Stall warning test 1",
                (e, _) => e.WarningTestAsync("EVT_OH_WARNING_TEST_STALL_1_PUSH", AircraftActionExecutor.StallTestHoldMs)),
            ActionManualAsync("PF_STALL_TEST2", "PREFLIGHT", "Stall warning test 2",
                (e, _) => e.WarningTestAsync("EVT_OH_WARNING_TEST_STALL_2_PUSH", AircraftActionExecutor.StallTestHoldMs)),
            ActionManualAsync("PF_OVSPD_TEST1", "PREFLIGHT", "Overspeed warning test 1",
                (e, _) => e.WarningTestAsync("EVT_OH_WARNING_TEST_MACH_IAS_1_PUSH", AircraftActionExecutor.OverspeedTestHoldMs)),
            ActionManualAsync("PF_OVSPD_TEST2", "PREFLIGHT", "Overspeed warning test 2",
                (e, _) => e.WarningTestAsync("EVT_OH_WARNING_TEST_MACH_IAS_2_PUSH", AircraftActionExecutor.OverspeedTestHoldMs)),
```

- [ ] **Step 5: Checklist — delete the dead reminder** (line 130):

Delete exactly: `Reminder("PF_TESTS", "PREFLIGHT", "Perform the overhead and fire tests as required"),`
(`Reminder("PF_ALT", …)` on line 129 STAYS.)

- [ ] **Step 6: Build + existing suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 Errors.
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass (no FO structural tests exist; this guards against collateral damage).

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737FlowDefinitions.cs MSFSBlindAssist/FirstOfficer/PMDG737/PMDG737ChecklistDefinitions.cs
git commit -m "feat(fo): 737 preflight fire/stall/overspeed tests replace the PF_TESTS reminder"
```

---

### Task 3: 777 — fire/overheat test (executor + flow + checklist)

**Files:**
- Modify: `MSFSBlindAssist\FirstOfficer\AircraftActionExecutor.cs` (the 777 FO executor)
- Modify: `MSFSBlindAssist\FirstOfficer\PMDG777FlowDefinitions.cs`
- Modify: `MSFSBlindAssist\FirstOfficer\PMDG777ChecklistDefinitions.cs`

**Interfaces:**
- Consumes: `SimConnectManager.SendPMDGEventViaTransmitWithTarget(uint, uint)`, `PMDG777Definition.EventIds`, existing `_dispatchGate`/`PaceAsync()`; 777 flow helper `SW(id, label, eventName, target, verifyField, verifyCond, checklistItemId, isMomentary)`; existing checklist helper `ActionManualAsync` (already present at ~line 855).
- Produces: `public Task<bool> FireOvhtTestAsync()`, pseudo-key `"FIRE_OVHT_TEST"`, flow step `CP_FIRE_TEST`, checklist item `PF_FIRE_TEST`.

- [ ] **Step 1: Executor — constants** next to `MouseFlagLeftSingle` (~line 24):

```csharp
    // Held FIRE/OVHT TEST button: transmit press/release with the hold between. The 777
    // CDA doctrine has NO release semantics (param 0 also registers as a press — see
    // docs/pmdg-777.md), so the held test uses the '#id' transmit path like the VC click.
    private const uint MouseFlagLeftSingleU  = 0x20000000u;
    private const uint MouseFlagLeftReleaseU = 0x00020000u;
    public const int FireOvhtTestHoldMs = 3000;   // Fenix-parity hold
```

- [ ] **Step 2: Executor — pseudo-key interception** in `ExecuteStepAsync` (~line 57), inserted before the ActionType switch:

```csharp
    public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        if (!IsAvailable) return Task.FromResult(false);
        // Pseudo-key: held self-completing FIRE/OVHT test (Fenix FIRE_TEST_* precedent).
        if (step.ActionType == Models.FlowStepActionType.SetSwitch && step.EventName == "FIRE_OVHT_TEST")
            return FireOvhtTestAsync();
        return step.ActionType switch
        {
            Models.FlowStepActionType.SetSwitch =>
                DispatchAsync(step.EventName!, step.TargetValue, step.UsesMouseFlag, step.IsMomentary),
            Models.FlowStepActionType.SetSwitchMultiple => MultiAsync(step.MultiActions),
            _ => Task.FromResult(false),
        };
    }
```

- [ ] **Step 3: Executor — the held-test method** (near `PushBothGroundPowerAsync`, ~line 164):

```csharp
    /// <summary>FIRE/OVHT TEST button, HELD: transmit LEFTSINGLE, hold, LEFTRELEASE —
    /// fire bell + fire warning lights while held, self-releasing. NOT the panel's
    /// momentary CDA param-1 press (that is a bare blip; the test wants a real hold,
    /// and CDA has no release). In-sim verification: 737 test plan Part U (the 777
    /// shares that plan); fallback if transmit proves inert on this button is the
    /// CDA param-1 press.</summary>
    public async Task<bool> FireOvhtTestAsync()
    {
        var sc = _simConnect;
        if (sc == null || !PMDG777Definition.EventIds.TryGetValue("EVT_OH_FIRE_OVHT_TEST", out int evId))
            return false;
        await _dispatchGate.WaitAsync();
        try
        {
            await PaceAsync();
            sc.SendPMDGEventViaTransmitWithTarget((uint)evId, MouseFlagLeftSingleU);
            _lastWriteUtc = DateTime.UtcNow;
            await Task.Delay(FireOvhtTestHoldMs);
            sc.SendPMDGEventViaTransmitWithTarget((uint)evId, MouseFlagLeftReleaseU);
            _lastWriteUtc = DateTime.UtcNow;
            return true;
        }
        finally { _dispatchGate.Release(); }
    }
```

- [ ] **Step 4: Flow — insert after `CP_BACKUP_GENS`** (PMDG777FlowDefinitions.cs:121-122, before `SW("CP_WINDOW_HEAT_1", …)`):

```csharp
            // Held FIRE/OVHT test (executor pseudo-key; fire bell audible while held).
            SW("CP_FIRE_TEST", "Fire and overheat test — listen for the fire bell",
                "FIRE_OVHT_TEST", 1, checklistItemId: "PF_FIRE_TEST"),
```

- [ ] **Step 5: Checklist — insert after `PF_BACKUP_GENS`** (PMDG777ChecklistDefinitions.cs:150-153, before `Auto("PF_WINDOW_HEAT", …)`):

```csharp
            // Manual-tick held test — no persistent state for "test performed"
            // (Fenix PF_FIRE_* pattern); ticking runs the same held test as the flow.
            ActionManualAsync("PF_FIRE_TEST", "PREFLIGHT", "Fire and overheat test",
                (e, _) => e.FireOvhtTestAsync()),
```

- [ ] **Step 6: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 Errors.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/AircraftActionExecutor.cs MSFSBlindAssist/FirstOfficer/PMDG777FlowDefinitions.cs MSFSBlindAssist/FirstOfficer/PMDG777ChecklistDefinitions.cs
git commit -m "feat(fo): 777 held fire/overheat test in cockpit prep flow + checklist"
```

---

### Task 4: A380 — fire test (executor + flow + checklist)

**Files:**
- Modify: `MSFSBlindAssist\FirstOfficer\FBWA380\FbwA380ActionExecutor.cs`
- Modify: `MSFSBlindAssist\FirstOfficer\FBWA380\FbwA380FlowDefinitions.cs`
- Modify: `MSFSBlindAssist\FirstOfficer\FBWA380\FbwA380ChecklistDefinitions.cs`

**Interfaces:**
- Consumes: the executor's `_gate`/`PaceAsync()`/`ApplySilent(string, double)`/`_lastWriteUtc`; the def's dedicated fire-test branch (`FlyByWireA380Definition.UiVariableSet.cs:68-89` — write 1 starts the test, write 0 ends it AND pulses the MASTERAWARN acknowledge); flow helpers `SW`/`Done`/`Captain`; checklist helpers `ActionManual` (exists, line ~717) / `Reminder`.
- Produces: `public Task<bool> FireTestAsync()`, pseudo-key `"FIRE_TEST"`, flow step `CP_FIRETEST` (moved + realized), checklist item `CP_FIRETEST` (Reminder → ActionManual).

- [ ] **Step 1: Executor — constant + held-test method** (place after `Set`, ~line 144):

```csharp
    /// <summary>Fenix-parity hold for the overhead fire TEST pushbutton.</summary>
    public const int FireTestHoldMs = 3000;

    /// <summary>Held fire test: write A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED 1 → hold → 0
    /// through the def's dedicated branch (UiVariableSet.cs), which on the 0-write also
    /// pulses the MASTERAWARN acknowledge so the continuous repetitive chime cancels.
    /// Master warning + CRC while held are the blind-pilot verification. Holds the gate
    /// for the whole test so WaitForDispatchDrainAsync covers the checklist grace.</summary>
    public async Task<bool> FireTestAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_sc is not { IsConnected: true } || _def == null || _announcer == null) return false;
            await PaceAsync();
            ApplySilent("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", 1);
            _lastWriteUtc = DateTime.UtcNow;
            await Task.Delay(FireTestHoldMs);
            ApplySilent("A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED", 0);
            _lastWriteUtc = DateTime.UtcNow;
            return true;
        }
        finally { _gate.Release(); }
    }
```

- [ ] **Step 2: Executor — pseudo-key interception** in `ExecuteStepAsync` (~line 51). The interception must run BEFORE `DispatchAsync` (`DispatchCoreAsync`'s name-switch would otherwise fall through to `ApplySilent("FIRE_TEST", 1)` → a garbage `SetLVar` fallback):

```csharp
        case FlowStepActionType.SetSwitch:
            if (step.EventName == null) return false;
            // Held self-completing fire test (Fenix FIRE_TEST_* precedent) — must not
            // reach the generic dispatch (its SetLVar fallback would write a bogus L:var).
            if (step.EventName == "FIRE_TEST") return await FireTestAsync();
            return await DispatchAsync(step.EventName, step.TargetValue);
```

- [ ] **Step 3: Flow — move + realize the step.** In `FbwA380FlowDefinitions.cs`, DELETE line 113 `Captain("CP_FIRETEST", "Fire test: perform"),` and INSERT after `CP_OXY` (lines 91-92, before `Captain("CP_GNDCTL", "Ground control: on"),`):

```csharp
            // Held fire test (executor pseudo-key): master warning + continuous
            // repetitive chime while held; the def's release write auto-acknowledges
            // the master warning. Fenix order: oxygen → fire test.
            Done(SW("CP_FIRETEST", "Fire test — listen for the fire warning", "FIRE_TEST", 1),
                "CP_FIRETEST"),
```

- [ ] **Step 4: Checklist — Reminder → ActionManual, moved.** In `FbwA380ChecklistDefinitions.cs`, DELETE line 139 `Reminder("CP_FIRETEST", "COCKPIT_PREP", "Fire test: perform"),` and INSERT directly after the `CP_OXY` item (lines 106-107):

```csharp
            // Manual-tick held test (no persistent "test performed" state; ticking runs
            // the same held test the flow runs — Fenix PF_FIRE_* pattern).
            ActionManual("CP_FIRETEST", "COCKPIT_PREP", "Fire test",
                (e, _) => e.FireTestAsync()),
```

- [ ] **Step 5: Verify the COCKPIT_PREP_CL readback group is untouched** — grep `FbwA380ChecklistDefinitions.cs` for `FIRETEST`/`Fire test` below line 543 (the CL group): any readback mention stays action-free; do NOT add a CL item.

- [ ] **Step 6: Build**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 Errors.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/FirstOfficer/FBWA380/FbwA380ActionExecutor.cs MSFSBlindAssist/FirstOfficer/FBWA380/FbwA380FlowDefinitions.cs MSFSBlindAssist/FirstOfficer/FBWA380/FbwA380ChecklistDefinitions.cs
git commit -m "feat(fo): A380 held fire test replaces the cockpit-prep reminder"
```

---

### Task 5: Docs + test plans + final verification

**Files:**
- Modify: `docs/first-officer.md` (new gotcha bullet)
- Modify: `docs/pmdg-737-first-officer-test-plan.md` (append `## Part U`)
- Modify: `docs/fbw-a380-first-officer-test-plan.md` (append dated section)
- Modify: `docs/pmdg-737.md` (fire-test switch facts under the fire-handles section)

**Interfaces:** none (docs only).

- [ ] **Step 1: `docs/first-officer.md`** — append to the GOTCHAS list:

```markdown
- **Preflight fire / warning tests are HELD self-completing tests on all four aircraft (2026-07-12, Fenix parity).** Pseudo-keys intercepted in each `ExecuteStepAsync` (Fenix `FIRE_TEST_*` precedent); flow steps tick their `ActionManual`/`Actionable` checklist items via `CompletesChecklistItemId` (no persistent "test performed" state exists — do NOT convert them to Auto items). 737 (live-probed): `EVT_FIRE_DETECTION_TEST_SWITCH` CDA position write HOLDS the spring switch (0=FAULT/INOP, 1=neutral, 2=OVHT/FIRE — write 2, hold `FireTestHoldMs` 2 s, write 1; the release write is mandatory), and the stall/Mach-IAS warning-test buttons are transmit-only (`SendPMDGEventViaTransmitWithTarget` LEFTSINGLE → hold 3.5 s/1.5 s → LEFTRELEASE; no CDA state, no CDA actuation — audio is the only feedback). 777: `EVT_OH_FIRE_OVHT_TEST` uses the same transmit press/hold/release (3 s) — NEVER a CDA 1→0 pair; 777 CDA param 0 is a press, not a release. A380: `FireTestAsync` writes `A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED` 1 → 3 s → 0 through the def's dedicated branch, whose release write auto-pulses the MASTERAWARN acknowledge — don't bypass `ApplySilent`/`ApplyUIVariable` for it. Hold times are user-tuned constants on each executor.
```

- [ ] **Step 2: `docs/pmdg-737-first-officer-test-plan.md`** — append after `### T.6 …` (end of file):

```markdown
## Part U — preflight fire / warning tests (2026-07-12)

Five real held tests replace the old "Perform the overhead and fire tests" reminder at
the TOP of the 737 Preflight flow (right after the walk-around), each mirrored as a
manual-tick checklist item. The 777's single FIRE/OVHT test is included here (shared
plan). Mechanisms: 737 fire = CDA position write 2→hold 2 s→1 (live-probed, holds);
737 stall/overspeed + 777 fire = transmit LEFTSINGLE → hold → LEFTRELEASE.

### U.1 737 Preflight flow runs the five tests
1. Cold-and-dark or powered gate state, run the Preflight flow.
2. Right after the walk-around step: "Fire warning test" — fire bell rings ~2 s, both
   master FIRE WARN lights + all three handle lights illuminate, then everything clears
   (switch back to neutral — confirm no stuck bell/lights).
3. Then stall tests 1 and 2: stick shaker rattle ~3.5 s each.
4. Then overspeed tests 1 and 2: clacker ~1.5 s each.
5. Flow ticks the five PREFLIGHT checklist boxes (CompletesChecklistItemId).
6. Confirm the flow NO LONGER speaks the old "Perform the overhead and fire tests"
   captain reminder.

### U.2 737 checklist ticks fire the same tests
1. Reset the Preflight checklist section; tick each of the five items by hand.
2. Each tick runs its held test (audible) and the box STAYS ticked (Actionable items
   never auto-revert; re-tick after an untick re-runs the test — idempotent).

### U.3 777 fire and overheat test (777 loaded)
1. Run Cockpit Preparation; after the backup generators step: "Fire and overheat test"
   — fire bell + fire warning lights while held ~3 s, self-releasing, then clears.
2. If the transmit press proves inert on this button (no bell): fall back to the CDA
   param-1 press in `FireOvhtTestAsync` and re-verify (the executor doc comment marks
   this fallback).
3. Checklist "Fire and overheat test" tick runs the same test; box stays ticked.
```

- [ ] **Step 3: `docs/fbw-a380-first-officer-test-plan.md`** — append at end of file:

```markdown
## Preflight fire test (2026-07-12)

`CP_FIRETEST` is now a real held test (was a captain reminder), moved up to right after
crew oxygen (Fenix order): the FO writes `A32NX_OVHD_FIRE_TEST_PB_IS_PRESSED` 1 → 3 s →
0 through the def's fire-test branch; the release write also pulses the MASTERAWARN
acknowledge so the CRC cancels.

1. Run Cockpit Preparation with the aircraft powered: after "Crew oxygen: ON" the FO
   announces "Fire test — listen for the fire warning"; master warning + continuous
   repetitive chime sound ~3 s, then STOP (the auto-acknowledge must cancel the CRC —
   a chime that keeps sounding is a fail).
2. The COCKPIT_PREP checklist "Fire test" box ticks from the flow; ticking it by hand
   re-runs the test and the box stays ticked.
3. The Continuous+IsAnnounced monitor may speak "Fire Test: On/Off" around the test —
   note whether this is acceptably chatty; if not, a follow-up adds echo suppression.
4. If 3 s proves too short for the full A380 test sequence, tune
   `FbwA380ActionExecutor.FireTestHoldMs`.
```

- [ ] **Step 4: `docs/pmdg-737.md`** — under the `## Fire handles need an active fire to test` section, append:

```markdown
The OVHT/FIRE detection TEST switch, by contrast, needs no fire: `FIRE_DetTestSw` is
0=FAULT/INOP / 1=neutral / 2=OVHT/FIRE, and a Control-CDA position write MOVES AND
HOLDS the spring-loaded switch (live-probed 2026-07-11 — write 2: fire bell + both
FIRE WARN masters + all three handle lights, staggered over ~1.7 s; the write back to
1 is mandatory, nothing auto-releases). The aft-overhead stall / Mach-IAS warning-test
buttons are the opposite: NO CDA state field and NO CDA actuation — transmit-only
(`#id` LEFTSINGLE press … LEFTRELEASE), sound-only feedback (PMDG does not drive the
stock `STALL WARNING`/`OVERSPEED WARNING` simvars).
```

- [ ] **Step 5: Final verification**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 Errors.
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add docs/first-officer.md docs/pmdg-737-first-officer-test-plan.md docs/fbw-a380-first-officer-test-plan.md docs/pmdg-737.md
git commit -m "docs(fo): fire/warning test gotchas + in-sim test plan sections"
```

---

## In-sim verification (post-implementation, user-run)

- 737: user loads the NG3 → run test plan Part U.1/U.2 (all mechanisms already live-probed this session; this validates the integrated flow/checklist behavior + hold feel).
- 777: next 777 session → Part U.3 (transmit shape unverified on this button; CDA-press fallback documented).
- A380: next A380 session → the A380 plan's new section (hold length + CRC cancel + announce chattiness).

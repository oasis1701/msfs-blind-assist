# PMDG 777 CDA Migration: Replace RPN Stepping with Direct Position Values

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate all two-position toggles and multi-position selectors from RPN/ROTOR_BRAKE mouse-click stepping to CDA (SetClientData) with direct position values, then remove dead RPN infrastructure.

**Architecture:** The PMDG SDK's CDA method sends `{EventId, PositionValue}` directly — no stepping, no direction ambiguity, works for spring-loaded positions. RPN/ROTOR_BRAKE was a MobiFlight workaround that encoded mouse clicks, but CDA is the SDK-native approach shown in the PMDG connection test example. After migration, RPN is only needed for momentary button presses (which have no target position).

**Tech Stack:** C# 13 / .NET 9, PMDG SDK via SimConnect Client Data Areas

---

## File Map

| File | Changes |
|------|---------|
| `MSFSBlindAssist/Aircraft/PMDG777Definition.cs` | Rewrite HandleUIVariableSet branches 4 & 5, remove APU special case, remove PMDG_WHEEL constants |
| `MSFSBlindAssist/SimConnect/PMDG777DataManager.cs` | Remove SendViaRPN, remove RPN nonce, remove ROTOR_BRAKE encoding, simplify SendEvent to always use CDA |
| `MSFSBlindAssist/SimConnect/SimConnectManager.cs` | Remove SendPMDGEventViaCDA (no longer needed when SendPMDGEvent uses CDA), clean up |

**Build command:** `dotnet build MSFSBlindAssist.sln -c Debug`

---

## Task 1: Migrate SendEvent to Always Use CDA

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/PMDG777DataManager.cs`

The `SendEvent` method currently routes low-offset events through RPN and high-offset through CDA. Since CDA works for all events, remove the routing and always use CDA.

- [ ] **Step 1: Replace SendEvent to always use CDA**

Replace the current `SendEvent` method (lines ~385-404) from:

```csharp
public void SendEvent(string eventName, uint eventId, int? parameter)
{
    try
    {
        int offset = (int)(eventId - THIRD_PARTY_EVENT_ID_MIN);
        string method = offset >= DIRECT_SET_OFFSET_THRESHOLD ? "CDA (SetClientData)" : "ROTOR_BRAKE RPN";
        PMDG777Debug.Log($"[PMDG777DataManager.SendEvent] eventName={eventName} eventId={eventId} (0x{eventId:X}) parameter={parameter?.ToString() ?? "null"} offset={offset} method={method}");

        if (offset >= DIRECT_SET_OFFSET_THRESHOLD)
            SendViaCDA(eventId, (uint)(parameter ?? 0));
        else
            SendViaRPN(offset, parameter);
    }
    catch (Exception ex)
    {
        PMDG777Debug.Log($"[PMDG777DataManager.SendEvent] EXCEPTION for '{eventName}': {ex.Message}");
        System.Diagnostics.Debug.WriteLine(
            $"[PMDG777DataManager] SendEvent '{eventName}' failed: {ex.Message}");
    }
}
```

To:

```csharp
public void SendEvent(string eventName, uint eventId, int? parameter)
{
    try
    {
        PMDG777Debug.Log($"[PMDG777DataManager.SendEvent] eventName={eventName} eventId=0x{eventId:X} parameter={parameter?.ToString() ?? "null"}");
        SendViaCDA(eventId, (uint)(parameter ?? 0));
    }
    catch (Exception ex)
    {
        PMDG777Debug.Log($"[PMDG777DataManager.SendEvent] EXCEPTION for '{eventName}': {ex.Message}");
        System.Diagnostics.Debug.WriteLine(
            $"[PMDG777DataManager] SendEvent '{eventName}' failed: {ex.Message}");
    }
}
```

- [ ] **Step 2: Remove SendEventViaCDA (no longer needed)**

Delete the `SendEventViaCDA` public method (lines ~406-410) — it was a workaround to bypass offset routing. Now that `SendEvent` always uses CDA, callers should use `SendEvent` directly.

```csharp
// DELETE THIS ENTIRE METHOD:
public void SendEventViaCDA(uint eventId, uint parameter)
{
    SendViaCDA(eventId, parameter);
}
```

- [ ] **Step 3: Remove SendViaRPN and related infrastructure**

Delete the entire `SendViaRPN` method (lines ~437-470):

```csharp
// DELETE THIS ENTIRE METHOD:
private void SendViaRPN(int offset, int? parameter)
{
    // ... all RPN/ROTOR_BRAKE encoding logic
}
```

Delete the `_rpnNonce` field (search for `_rpnNonce` declaration, around line 45):

```csharp
// DELETE:
private int _rpnNonce;
```

Delete the `DIRECT_SET_OFFSET_THRESHOLD` constant (line ~50):

```csharp
// DELETE:
private const int  DIRECT_SET_OFFSET_THRESHOLD = 14500;
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build errors in SimConnectManager.cs (SendPMDGEventViaCDA reference) and possibly PMDG777Definition.cs. These are fixed in Tasks 2 and 3.

Note: This step is expected to fail. Proceed to Task 2.

---

## Task 2: Clean Up SimConnectManager

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.cs`

- [ ] **Step 1: Remove SendPMDGEventViaCDA**

Delete the `SendPMDGEventViaCDA` method (around line 3500):

```csharp
// DELETE THIS ENTIRE METHOD:
public void SendPMDGEventViaCDA(uint eventId, uint parameter)
{
    pmdg777DataManager?.SendEventViaCDA(eventId, parameter);
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build errors in PMDG777Definition.cs where `SendPMDGEventViaCDA` is called. Fixed in Task 3.

---

## Task 3: Rewrite HandleUIVariableSet Control Dispatch

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/PMDG777Definition.cs`

This is the main task. Replace the old toggle/stepping branches with CDA direct-position sends.

- [ ] **Step 1: Remove PMDG_WHEEL constants**

Delete these two lines (around line 4685):

```csharp
// DELETE:
private const int PMDG_WHEEL_UP   = 0x00004000; // 16384
private const int PMDG_WHEEL_DOWN = 0x00002000; // 8192
```

- [ ] **Step 2: Replace APU special case + branches 4 & 5 with unified CDA logic**

In `HandleUIVariableSet`, replace everything from the `// 2b. APU Selector` comment through the end of branch 5 (lines ~5121-5205) with:

```csharp
        // ------------------------------------------------------------------
        // 2b. APU Start — special: send position 2 to the APU selector event,
        //     but only when the selector is already at On (1)
        // ------------------------------------------------------------------
        if (varKey == "ELEC_APU_Start")
        {
            var dm = simConnect.PMDG777DataManager;
            int current = dm != null ? (int)dm.GetFieldValue("ELEC_APU_Selector") : 0;
            if (current == 1)
                simConnect.SendPMDGEvent(eventName, eventId, 2); // 2 = Start position
            return true;
        }

        // ------------------------------------------------------------------
        // 3. Momentary / button press — send once with no parameter
        // ------------------------------------------------------------------
        if (varDef.RenderAsButton || varDef.IsMomentary)
        {
            SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] Branch: MOMENTARY/BUTTON — calling SendPMDGEvent({eventName}, {eventId})");
            simConnect.SendPMDGEvent(eventName, eventId);
            return true;
        }

        // ------------------------------------------------------------------
        // 4. Switches with ValueDescriptions — send target position directly via CDA.
        //    Works for both two-position toggles and multi-position selectors.
        //    CDA sends {EventId, PositionValue} to set the switch to the exact
        //    target position — no stepping, no direction ambiguity.
        // ------------------------------------------------------------------
        if (varDef.ValueDescriptions.Count >= 2)
        {
            int target = (int)value;
            var dm = simConnect.PMDG777DataManager;
            if (dm != null)
            {
                int current = (int)dm.GetFieldValue(varDef.Name);
                SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] Branch: CDA DIRECT current={current} target={target}");
                if (current == target)
                {
                    SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] CDA DIRECT: already at target — skipping");
                    return true;
                }
            }
            SimConnect.PMDG777Debug.Log($"[PMDG777Definition.HandleUIVariableSet] CDA DIRECT: sending position {target} for {eventName}");
            simConnect.SendPMDGEvent(eventName, eventId, target);
            return true;
        }
```

This replaces:
- The APU Selector special case (branch 2b) — now handled by the unified branch 4 like any other switch
- The old two-position toggle branch (branch 4) — no longer needs toggle logic, just sends target position
- The old multi-position stepping branch (branch 5) — no longer needs step calculation, just sends target position

Note: The APU Start special case remains because it's a button that sends a position value to a *different* variable's event, which doesn't fit the generic pattern.

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit all three tasks together**

```bash
git add MSFSBlindAssist/SimConnect/PMDG777DataManager.cs MSFSBlindAssist/SimConnect/SimConnectManager.cs MSFSBlindAssist/Aircraft/PMDG777Definition.cs
git commit -m "refactor(pmdg777): migrate all switches from RPN stepping to CDA direct position values

Replace RPN/ROTOR_BRAKE mouse-click stepping with PMDG SDK CDA approach
(SetClientData with direct position values) for all two-position toggles
and multi-position selectors. CDA is the SDK-native method shown in the
PMDG connection test example — more reliable, no direction ambiguity,
works for spring-loaded positions.

Removed: SendViaRPN, ROTOR_BRAKE encoding, PMDG_WHEEL constants,
RPN nonce, DIRECT_SET_OFFSET_THRESHOLD, SendPMDGEventViaCDA workaround.
Unified two-position and multi-position branches into single CDA dispatch."
```

---

## Task 4: Verify Guarded Switches Still Work via CDA

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/PMDG777DataManager.cs` (if needed)

Guarded switches call `SendEvent(guardEventName, guardEventId, null)` three times (open guard → toggle → close guard). With `parameter=null`, `SendEvent` now calls `SendViaCDA(eventId, 0)`. Verify this is correct — guard events with parameter 0 should toggle the guard open/closed.

- [ ] **Step 1: Verify guard event logic**

Read the `SendGuardedToggle` method. It sends:
1. Guard event with `null` parameter → CDA sends `{guardEventId, 0}`
2. Switch event with `null` parameter → CDA sends `{switchEventId, 0}`
3. Guard event again with `null` parameter → CDA sends `{guardEventId, 0}`

The SDK connection test example shows `toggleTaxiLightSwitch` using `Control.Parameter = 0` to set OFF and `Control.Parameter = 1` to set ON. For guard events, parameter 0 might mean "toggle guard" or "open guard", and sending it again toggles it closed. This matches the existing behavior.

If guarded switches don't work during live testing, the fix would be to pass explicit position values (1 for open, 0 for close) in the guard sequence. But this should be verified in-sim first, not pre-emptively changed.

- [ ] **Step 2: No code change needed if guards work**

If guards work correctly with CDA parameter 0, just verify and move on.

- [ ] **Step 3: Build final verification**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeds with 0 errors.

- [ ] **Step 4: Commit (only if changes were needed)**

Only commit if Task 4 required code changes. Otherwise skip.

---

## Post-Migration Notes

### What was removed
- `SendViaRPN` method and all ROTOR_BRAKE encoding logic
- `_rpnNonce` field (MobiFlight dedup workaround)
- `DIRECT_SET_OFFSET_THRESHOLD` constant (14500 — no longer needed)
- `PMDG_WHEEL_UP` / `PMDG_WHEEL_DOWN` constants
- `SendPMDGEventViaCDA` / `SendEventViaCDA` workaround methods
- Multi-position stepping loop (calculating steps, sending wheel events per step)
- Two-position toggle-away-from-state prevention logic

### What replaced it
- Single unified branch in HandleUIVariableSet for all switches with ValueDescriptions
- `SendEvent` always routes through CDA `{EventId, PositionValue}`
- Direct position value send — no stepping, no direction logic

### What stayed the same
- Momentary button handling (RenderAsButton/IsMomentary → send event with no parameter)
- Guarded switch handling (_guardedMap → guard open/toggle/guard close sequence)
- APU Start special case (button that sends position 2 to APU Selector event)
- Event lookup flow (_simpleEventMap → EventIds)

### MobiFlight WASM dependency
After this migration, MobiFlight WASM is still used for:
- Reading L-vars (if any)
- CDU communication
- H-variable events (if any)

It is NO LONGER used for sending PMDG switch events via ROTOR_BRAKE RPN. If MobiFlight is only needed for the above, the `_mobiFlightWasm` field in PMDG777DataManager can stay but the RPN path is gone.

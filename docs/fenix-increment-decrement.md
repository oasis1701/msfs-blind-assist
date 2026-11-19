# Fenix A320 Counter-Based Increment/Decrement Pattern

This document explains how the Fenix A320 definition steps the rotary encoders and push/pull knobs of the FCU, the EFIS baro and the ISIS baro: a step is a relative change from the sim's own value, never an absolute count kept by the app. The rule is [FNX-5] (`docs/invariants/fenix-data.md#fnx-5`); background for the Fenix as a whole is [a32nx.md](a32nx.md).

## Overview

A Fenix knob L:var is a **counter**. The cockpit adds 1 to the same L:var for every click one way and takes 1 away for every click the other way, and the Fenix acts on the DIFFERENCE between successive values: a value that went up is an increase, a value that went down is a decrease, an unchanged value does nothing. The absolute number means nothing, and it keeps moving with every cockpit click.

That is why the app never writes a count of its own. Measured on 2026-10-21 (A320-214 CFM on the ground, every value restored, then again on a reloaded flight through a direct SimConnect connection): `E_FCU_SPEED` read 3 after three cockpit clicks (speed 100 to 103), and the app's first "increase", which wrote its own count of 1, moved the speed from 103 to 101, down 2. With the baro STD counter at 2 after two cockpit pulls, the app's old "STD" (a straight write of 1, a decrease) selected QNH. An app-side count is wrong after any cockpit click, any turn in the virtual cockpit and any app restart, and the knob then moves by the difference, possibly the wrong way. So every step is a read-modify-write of the sim's own value.

## When to Use

Use a relative step for every Fenix control that is a counter:

- **Rotary encoders**: the FCU knobs `E_FCU_SPEED`, `E_FCU_HEADING`, `E_FCU_ALTITUDE` and `E_FCU_VS`; the EFIS baro knobs `E_FCU_EFIS1_BARO` and `E_FCU_EFIS2_BARO`; the ISIS baro knob `E_MIP_ISFD_BARO`.
- **Push/pull knobs**: the FCU speed, heading and altitude push/pulls `S_FCU_SPEED`, `S_FCU_HEADING` and `S_FCU_ALTITUDE` (a pull adds 1, a push takes 1 away), the V/S push/pull `S_FCU_VERTICAL_SPEED`, and the baro STD push/pulls `S_FCU_EFIS1_BARO_STD` and `S_FCU_EFIS2_BARO_STD` (a pull adds 1 and selects STD, a push takes 1 away and selects QNH; HubHop gives the same: any increase selects STD, any decrease QNH, extra pulls stay STD and extra pushes QNH).

Not a counter, and not covered here: the **RMP**. The Fenix RMP's frequencies are set through the stock COM standby events (`COM_STBY_RADIO_SET_HZ`, `COM2_STBY_RADIO_SET_HZ`, `COM3_STBY_RADIO_SET_HZ`, from the panel's COM1/2/3 Standby boxes) and its transfer keys are press buttons (`S_PED_RMPn_XFER`); the `N_PED_RMPn_ACTIVE` and `_STDBY` L:vars (kHz) are only read and announced. An earlier design drove inner and outer frequency encoders (`E_PED_RMP*`) with INC/DEC buttons; no code uses them any more, so do not look for them. A control that is a state, not a counter (a switch with a readable position, a latched pushbutton with a light) is written as a state.

The baro STD counter is not the mode: it holds any number after cockpit pulls and pushes (it read 3 in STD, 2026-10-21) and can be 0 while STD is selected. Read STD or QNH from the EFIS QNH light `I_FCU_EFISn_QNH` (1 QNH, 0 STD, announced as "EFIS1 BARO STD Status"), never from the counter. The First Officer's `FenixStateEvaluator.BaroText` does the same.

## How It Works

One relative step is the RPN `(L:x) n + (>L:x)` (or `n -` for a negative step), built by `FenixA320Definition.CounterStepCode(lvar, steps)`:

```
Press INC:  (L:E_FCU_SPEED) 1 + (>L:E_FCU_SPEED)    sim count 3 -> 4
Press INC:  (L:E_FCU_SPEED) 1 + (>L:E_FCU_SPEED)    sim count 4 -> 5
Press DEC:  (L:E_FCU_SPEED) 1 - (>L:E_FCU_SPEED)    sim count 5 -> 4
```

The sim performs the read and the write in one calculator string, so a click made in the cockpit a moment earlier is never overwritten. Three details are load-bearing:

- **`ExecuteCalculatorCodeUnique`, never a plain `SetLVar` or `ExecuteCalculatorCode`** ([SIM-17]). MobiFlight's command channel coalesces two consecutive IDENTICAL calc strings, and a step is always the same fixed string (`+ 1`, `- 1`), so a repeated step would be dropped. `ExecuteCalculatorCodeUnique` prefixes the string with `{seq} 0 *` (a numeric no-op) from the manager's ONE counter, shared by the panel, the hotkeys and the First Officer, so two writers can never build the same string twice in a row. These steps therefore need the MobiFlight WASM module.
- **Invariant digits** ([VAR-5]). `CounterStepCode` formats with `FormattableString.Invariant` and writes the sign itself (`+` or `-`, with `Math.Abs` of the count), because sv-SE writes a minus as U+2212.
- **A read-modify-write is atomic in the sim**, so two writers (the panel and the First Officer, or two hotkeys) stay coherent whichever fires. Never read the cache in C# and write back a computed absolute value.

## Implementation

The helpers are in `Aircraft/FenixA320Definition.cs`:

<!-- fragment: MSFSBlindAssist/Aircraft/FenixA320Definition.cs#CounterStepCode -->
```csharp
internal static string CounterStepCode(string lvar, int steps) =>
    FormattableString.Invariant($"(L:{lvar}) {Math.Abs(steps)} {(steps < 0 ? "-" : "+")} (>L:{lvar})");

private void IncrementCounter(string varName, SimConnect.SimConnectManager simConnect)
{
    if (simConnect != null && simConnect.IsConnected)
        simConnect.ExecuteCalculatorCodeUnique(CounterStepCode(varName, 1));
}
```

`DecrementCounter` is the same with `-1`, and `JumpCounter(varName, steps, simConnect)` sends a whole signed delta in one string (the Fenix reads the delta each sim frame and processes all the steps at once). The push/pull knobs use `AdjustFcuPushPullCounter(counterVar, delta, simConnect)` and the baro knobs `AdjustBaroCounter(counterVar, delta, simConnect)`, which are the same relative step through the same unique channel.

### Variable definitions

Each knob has two panel buttons, an increase and a decrease. The button variable's KEY is what the handler matches; its `Name` is the counter L:var the step goes to:

```csharp
["E_FCU_ALTITUDE_INC"] = new SimConnect.SimVarDefinition
{
    Name = "E_FCU_ALTITUDE",
    DisplayName = "FCU Altitude Inc",
    Type = SimConnect.SimVarType.LVar,
    UpdateFrequency = SimConnect.UpdateFrequency.OnRequest,
    RenderAsButton = true
},
```

### HandleUIVariableSet

```csharp
// Altitude Controls
if (varKey == "E_FCU_ALTITUDE_INC" && value == 1)
{
    IncrementCounter("E_FCU_ALTITUDE", simConnect);
    return true;
}

if (varKey == "E_FCU_ALTITUDE_DEC" && value == 1)
{
    DecrementCounter("E_FCU_ALTITUDE", simConnect);
    return true;
}
```

The FCU speed, heading, altitude and V/S, the EFIS 1 and 2 baro and the ISIS baro knobs all follow this shape. The FCU push/pull hotkeys (`FCUHeadingPush` / `FCUHeadingPull` and the speed and altitude pairs) call `AdjustFcuPushPullCounter` with -1 (push) or +1 (pull), and the baro windows' and the panel's STD controls send `CounterStepCode("S_FCU_EFISn_BARO_STD", 1 or -1)`.

### Typed values

A typed FCU value (the Ctrl+S/H/A/V windows, `SetFCUSpeed`, `SetFCUHeading`, `SetFCUAltitude`, `SetFCUVS`, all through `SetFcuKnobAsync`) is a closed loop, `FenixA320FcuKnobSet.RunAsync` ([FNX-20]): it reads the knob fresh through its on-request twin, works out the clicks to go (a heading by the shortest way round, 340 to 20 is +40), sends them with `JumpCounter`, waits, reads again and steps from what it reads. With the Fenix's Encoder Acceleration on (or unread) each write carries at most 20 clicks, 500 ms apart; with it off one write carries the whole difference. A knob that has not moved is never written again, the loop is bounded, and a set that ends off target says so once ("Altitude set to 7100, not 7000."). The typed altimeter (Ctrl+B, `SetFCUBaro`) steps each EFIS baro counter by the delta from its cached reading and verifies with a fresh read. A typed QNH also leaves STD with a push step, so it always ends in QNH whatever the counter holds.

Two Fenix options touch these. `encoder_acceleration_enabled` on multiplies a large or quick turn of an FCU knob (measured 2026-10-24: one -80 click write took the altitude to the 100 ft floor; single writes of up to 20 clicks landed exactly), which is why the typed FCU values turn in checked 20-click steps ([FNX-20]); the baro knobs are not accelerated (-30 and +69 clicks in one write landed exactly). With `efis_baro_linking_type` linked either baro knob moves both sides; measured 2026-10-24 with Linked Left+Right+Standby, the typed altimeter still lands on the value typed on both sides ("Both altimeters set: 1013, 29.92." heard; Linked Left+Right was not tried), so the app works with any linking and the option's note carries no tested value (the measurements are under "Fenix options" in [a32nx.md](a32nx.md)).

## Key Concepts

### Counter Values

The counter can be any number, positive or negative: only changes matter. It is shared with the cockpit, so it is never reset or assumed.

### Variable Isolation

Each counter is its own L:var and is stepped on its own. The panel, the hotkeys and the First Officer reach the same counter through the same helper, so they never need to agree on a number.

### Change Detection

The Fenix detects the direction of change, not absolute values:

- Counter 5 to 6: increase detected
- Counter 6 to 5: decrease detected
- Counter 6 to 6: no change, nothing happens

A Pull when the baro is already in STD (or a Push when it is already in QNH) therefore changes the counter but not the mode, and speaks nothing: the read-back is the QNH light changing.

## Reference

- `Aircraft/FenixA320Definition.cs`: `CounterStepCode`, `IncrementCounter`, `DecrementCounter`, `JumpCounter`, `AdjustFcuPushPullCounter`, `AdjustBaroCounter`, `SetFcuKnobAsync`.
- `Aircraft/FenixA320FcuKnobSet.cs`: the typed-value loop; `Services/FenixEncoderAcceleration.cs`: the option, read from the Fenix with a short cache.
- `FirstOfficer/Fenix/FenixActionExecutor.cs`: `PushFcuManaged` and `SetBaroStdBoth` step the same counters the same way.
- `Forms/FenixA320/FenixBaroWindow.cs`: the altimeter window's QNH/STD pick pulls or pushes the STD counters.
- Pinned by `FenixV3FixPanelControlsTests` (`Counters_step_from_the_sims_value`, `Baro_std_is_a_pull_and_a_push`, `Typed_baro_leaves_std_with_a_push_step`, `Baro_window_mode_pick_steps_the_std_counter`) and `FenixFoBaroStdTests`.

## Related Documentation

- [Variable System](variable-system.md) - Three patterns for managing variables
- [Adding Features](adding-features.md) - Step-by-step workflows for new controls
- [Aircraft Definitions](aircraft-definitions.md) - Multi-aircraft dictionary system

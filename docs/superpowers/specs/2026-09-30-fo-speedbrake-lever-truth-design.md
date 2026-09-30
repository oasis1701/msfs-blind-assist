# First Officer speed brake: judge the lever, leave a deployed one alone

Date: 2026-09-30. PR #160 (First Officer). Follows main's #261, which measured the three Boeing
speed-brake levers exactly.

## Problem

#261 measured, with hydraulics pressurised, that ARM is an EXACT position on all three Boeings
this First Officer serves, and that the signals #160 judges "armed" by cannot tell ARM from a
speed brake that is already partly up:

- **PMDG 737**: `L:switch_679_73X` rests at 100 armed; at 101 the spoilers are 34 % up. The
  `MAIN_annunSPEEDBRAKE_ARMED` light stays lit from 100 to about 342. #160 judges "armed" by that
  light alone, and its arm action counts an EXTENDED light as "already done", so a speed brake
  that is partly up in flight is reported "Speedbrake: ARMED" and nothing is done.
- **PMDG 777**: `L:switch_498_a` rests at 0 / 200 / 300 / 400. #160 reads the SDK byte
  `FCTL_Speedbrake_Lever`, which is that value divided by 4 and TRUNCATED, so a lever at 201-203
  (spoilers 34 % up) reads exactly 50, "armed". A hardware axis parks the lever at 22 for DOWN,
  which the byte reads as 5, so After Landing "Speed Brake lever: DOWN" never ticks for a pilot
  who uses an axis. The Landing flow's guard also says "Already set" over a DEPLOYED lever.
- **iFly 737 MAX**: `Spoiler_Lever_Status` rests at 34 armed, and `SPEED_BRAKE_ARMED_Light_Status`
  is lit from 34 all the way to 224 (fully up), so the light alone cannot tell armed from
  deployed. #160 leaves arming to the Captain because the lever had no write path; #261 added one
  (`FLTCTRL_SPOILER`, Value2 on the same 0-224 scale, lands exactly and at once).

#160's notes also say the iFly lever is read-only and that the 737 First Officer cannot read the
lever. Both are now wrong.

## Decisions (owner, 2026-09-30)

1. A deployed speed brake is never moved by the First Officer. It says why and leaves the
   checklist lines open; they tick themselves when the pilot arms.
2. The rule is a shared one in the First Officer engine (flow steps and checklist lines), not
   speech inside each arm action.
3. On the 737 and the iFly, "armed" needs the lever exactly at ARM **and** the ARMED light lit.
   The 777 SDK has no such light, so it is the lever alone.
4. The iFly First Officer arms the speed brake itself, as the PMDG 737's does (same aircraft
   type, CLAUDE.md "Before changing behaviour").

## Design

### 1. Lever truth (pure)

`FirstOfficer/SpeedbrakeLeverState.cs`: `enum SpeedbrakeLeverPosition { Unknown, Down, Armed,
Deployed }` and a static class `SpeedbrakeLeverState` holding one classifier over main's detent
tables (`PmdgSpeedBrakeLever.Ng3`, `.B777`,
`IFly737SpeedBrakeLever.Detents`) and their settle tolerances. Never a copy of the numbers.

- Unknown: NaN.
- Armed: within the ARM row's own (exact) tolerance.
- Down: anything short of ARM. This is main's `PositionIndex` rule, which also covers the 777's
  axis Down at 22.
- Deployed: anything past ARM.

One entry per aircraft names its table, its tolerance and the key the lever is read from:
`MON_PMDG737_SpeedBrake`, `FCTL_Speedbrake` (main's definition keys, both on the continuous
batch) and `Spoiler_Lever_Status`. A drift test pins each key to main's definition and its L-var
name.

### 2. Reading the lever

- **737 and 777 evaluators** read the lever from `SimConnectManager.GetCachedVariableValue` (a
  `ConcurrentDictionary`, already read by other First Officer code) through an injectable source
  set in `BindDataManager`. A missing value is NaN.
- **iFly evaluator** reads `Spoiler_Lever_Status` from the SDK snapshot like any other field.
- **New synthetic fields:**
  - 737: `FO_SPEEDBRAKE_ARMED`: 1 when the lever is Armed and the ARMED light is lit; 0 when
    either is not; NaN when the CDA is not ready or the lever is unknown.
  - 777: `FO_SPEEDBRAKE_LEVER`: the raw lever value, NaN when unknown. `IsSpeedbrakeDown`,
    `IsSpeedbrakeArmed` and `IsSpeedbrakeDeployed` classify it.
  - iFly: `FO_SPEEDBRAKE_ARMED`, as on the 737, from the lever and `SPEED_BRAKE_ARMED_Light_Status`.

### 3. "Leave it alone" (engine)

- **Flow steps** get `LeaveAloneWhen` (`Func<TState,bool>`) and `LeaveAloneText`. `FlowManager`
  checks it after `SkipCondition` ("Already set" still wins) and before `RequiresStepId`. When it
  holds:
  - nothing is dispatched and `LeaveAloneText` is spoken;
  - the step's linked checklist lines are kept out of the completion latch;
  - its id joins the skipped set, so dependent steps skip;
  - `StepSkipped` is raised and the flow continues.
- **Checklist items** get the same pair. `ChecklistManager.ToggleItem` gains an overload with
  `out string? leftAloneText`. When a hand-tick's condition holds, the action is not run, the
  tick is refused (the line stays unticked), and the reason comes back through that parameter.
  The First Officer window speaks it in place of its "Label: checked" line, as one utterance.
  An event was rejected: its queued speech would be cut off by the window's interrupting status
  line. This is an error-condition announcement, allowed by the screen-reader rules.
- **Sentence:** one constant, `SpeedbrakeLeverState.LeaveAloneText` = "Speedbrake extended, not
  armed. Left as it is."
- The condition is "Deployed" only. An Unknown lever reaches the arm action, whose backstop
  refuses to act on a lever it cannot read.

### 4. Per aircraft

**PMDG 737**
- Landing flow `LD_SPDBRK`:
  - verify field `FO_SPEEDBRAKE_ARMED`;
  - `SkipCondition` armed ("Already set");
  - `LeaveAloneWhen` deployed.
- `LDA_SPDBRK` (arm-on-tick): field `FO_SPEEDBRAKE_ARMED`, leave-alone deployed.
  `LDC_SPDBRK` (check only): field `FO_SPEEDBRAKE_ARMED`.
- `ArmSpeedbrakeAsync`:
  - lever Armed and light lit: true, no click;
  - lever Unknown or Deployed, or EXTENDED lit: false, no click;
  - Down: click, then confirm armed within 3 s. The lever arrives on the 1 Hz batch, so the old
    1.2 s window is too short.

**PMDG 777**
- `Pmdg777SpeedbrakeLever` re-pointed at main's `B777` table and `FO_SPEEDBRAKE_LEVER`. The SDK
  byte is not read.
- Landing `LD_SPEEDBRAKE_ARM` goes through a verified `SPEEDBRAKE_ARM`
  (`ArmSpeedbrakeAsync`, waiting up to 8 s for the lever). The lever takes about 5 s from DOWN
  to ARM, and a flow step's own verify reads only 600 ms after dispatch, so the old step said
  "Skipping" over a lever that armed a moment later.
  - `SkipCondition` armed;
  - `LeaveAloneWhen` deployed;
  - verify field `FO_SPEEDBRAKE_LEVER`.
- `LDG_SPEEDBRAKE`: field `FO_SPEEDBRAKE_LEVER`, leave-alone deployed.
- After Landing `AL_SPEEDBRAKE_DN` / `AL_SPEEDBRAKE`: field `FO_SPEEDBRAKE_LEVER`, Down = short
  of ARM.
- Backstop in the 777 executor, where `EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM` is sent:
  - lever Armed: true, no click;
  - lever not known Down: false, no click;
  - lever read from the SimConnect cache it already holds.

**iFly 737 MAX**
- New pseudo-key `SPEEDBRAKE_ARM` in `IFly737ActionExecutor`, handler `ArmSpeedbrakeAsync`:
  - Armed (lever and light): true, no write;
  - Unknown or Deployed: false, no write;
  - Down: write `Spoiler_Lever_Status` = ARM through the panel's own `ApplyUIVariable` path
    (`FLTCTRL_SPOILER`, which records the pick, so main's settle announcer stays quiet), then
    confirm armed within 1.5 s (SDK polled every 250 ms).
- Landing flow: `Captain("LD_SPDBRK")` and the 15 s `LD_SPDBRK_CHECK` wait are replaced by one
  step, `Also(SW("LD_SPDBRK", …, SPEEDBRAKE_ARM, …, FO_SPEEDBRAKE_ARMED, "LDA_SPDBRK"),
  "LDC_SPDBRK")`, with the same skip and leave-alone rules as the 737.
- `LDA_SPDBRK` becomes arm-on-tick (`AutoAsync`) with leave-alone. `LDC_SPDBRK` stays check only.
  Both read `FO_SPEEDBRAKE_ARMED`.

### Checklist lines covered

- 737: `LDA_SPDBRK`, `LDC_SPDBRK`.
- 777: `LDG_SPEEDBRAKE`, `AL_SPEEDBRAKE`.
- iFly: `LDA_SPDBRK`, `LDC_SPDBRK`.

These are all six speed-brake lines on the three jets. The 737 and iFly have no After Landing
"Speed brake lever: DOWN" line while the 777 does; the two 737s agree, so it is not added here.

## Notes corrected

- iFly flow and checklist class notes, and `HasWriteCommand`'s example field (the iFly lever is
  writable now).
- `SpeedbrakeArmLadder`'s "the evaluator cannot reach the lever".
- `Pmdg777SpeedbrakeLever`'s SDK-byte history.
- The related test comments.
- `docs/first-officer.md` and the PMDG 737 / iFly First Officer test plans.

No changelog fragment: this repairs #160's own unreleased First Officer.

## Testing

TDD, red first:

1. Classifier on 99/100/101, 199/200/201, 33/34/35, 22, 0, 400/224, NaN.
2. Leave-alone in `FlowManager` and `ChecklistManager`.
3. Each evaluator's new fields with a fake cache or snapshot.
4. The iFly arm handler and the 777 dispatch backstop.
5. Flow and checklist wiring.

Existing tests pinning the SDK byte, the light-only check or the iFly Captain reminder move to
the new truth. Then the full CI set: solution build, xUnit, Python, Node.

## In-sim (owner)

1. PMDG 737 Landing flow, lever Down: arms, both lines tick. Speed brake at the flight detent:
   "Speedbrake extended, not armed. Left as it is.", nothing moves.
2. The same two runs on the iFly.
3. PMDG 777 After Landing, lever put down by a hardware axis: "Speed Brake lever: DOWN" ticks.

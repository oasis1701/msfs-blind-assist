# Plan 05 evidence: the SD window

Fenix build 3.0.0.7613 (the installed `FenixDisplay.exe` reports 3.0.0.7454 as its own file version, as it did for plan 02's
checks). Worked by kn4iee with Claude. Raw fields come from the team's local channel tools, which stay local.

## The fields ([FNX-23]): Task 2 Step 2, 2026-11-03

Every number and wire shape plan 05 adds in `FenixChannelFields.Sd.cs`, and every member of plan 01's it reads, was read
fresh from the installed `FenixDisplay.exe` with the team's metadata tool and compared with the team's contract dump of
2026-11-01. They stand, with no change:

- `Remote` 16 to 23, 25, 26 (nested), 132 (`int32`), 133 (nested), 154 (bool), 175, 176 and 178 (`string[]` marked
  NullWrappedValue).
- `ElecFlags` 1 to 7, `BusStatus` 1 to 8 (bools); `GeneratorData` 1, 2, 3, 5 (doubles) and 4 (bool); `VoltAmps` 1, 2 and
  `ExtPwrData` 1, 2 (doubles): `tr1`, `tr2`, `essTR`, `bat1` and `bat2` are `VoltAmps`, `extPwr` is `ExtPwrData`.
- `HydraulicState` 1 to 3 (`HydSystemState`), 4 to 7 and 10 to 12 (bools), 8 and 9 (`HydElecPumpState`);
  `HydSystemState` 1, 2 (doubles), 6, 7 (bools); `HydElecPumpState` 1 to 3 (bools).
- `FuelState` 1 (`JetPumpData`), 2 to 7 (`FuelPumpState`), 8 (`FuelAdvisoryData`), 9, 10 (bools), 11 (`FuelValveStates`),
  12 (`FuelQuantityData`), 13 (`FuelUsedData`), 14 (`FuelTemperatureData`), 15 (`ActPumpData`); `JetPumpData` 3, 4,
  `FuelPumpState` 1, 3 (bools); `FuelValveStates` 1 to 8 (`ValveStateEnum`); `FuelQuantityData` 1 to 8 and
  `FuelTemperatureData` 1 to 4 (doubles).
- Plan 01's: `EngineControlState` and `SingleEngineState` (all 32 members), `ApuData` (all 14), `FuelUsedData`, and
  `ValveStateEnum` declared Open, Closed, Transit, FailedOpen, FailedClose, DataInvalid. `SingleEngineState.StartValve` is a
  `ValveStateEnum`, so the ENG page's start valve reads valve words.

## Inferred, not yet measured (Task 1)

Without a sim run, Tasks 2 to 8 took these from the plan and the team's decoded cruise push of 2026-11-01 (an A321 at
FL340, both engines running); each is marked "inferred" in the code:

- 132 `ptuState` was left out (0) with both engines running and the PTU in AUTO: `PtuStates.Idle = 0`, spoken "PTU: idle".
  Every other value reads "PTU: not available" and is reported.
- 133.10 and 133.11 (`ShutoffLeft`, `ShutoffRight`) were set with the engines running: a fire valve reads "open" while set.
- 16 `elecFlags`, 22 `essTR` and 133.9 (the yellow electric pump, off) came as empty messages, so a part with everything
  false is sent, not left out; 24 `apuData` carried EgtCaution 1067 and EgtMax 1100 with the APU off.
- 134.11: `Engine1` and `Engine2` were left out (Open) while the closed valves came; the A321 had no outer tank quantities,
  its outer temperatures were NaN, and `Center1`/`Center2` set LowPressure with no switch.
- 178: `g-26`, `g-53`, `  g0`, empty, `17`, `18`, `g75600`, empty, empty.
- Which flag means what (ELEC, HYD, FUEL), the igniter letters (17 A, 18 B), the A321 FUEL layout (no outer tanks,
  transfer valves or center pumps; its extra rows after the center tank) and the A318 following the A320's layout.

## Waiting on the owner

Task 1 [sim] (pages 0 and 13, presence and shapes at the gate, the X FEED and PTU values with captures, the variant on an
A320 and an A321 with the A321's FUEL capture, field 154 in kilograms and pounds, the frame's 178/175/176, labels against
captures) and Task 10 [sim] (each page against the cockpit, the changes, Alt+S, the log; in flight; by ear). Also for
those runs: whether the SD draws OVHT on the yellow electric pump (133.9.3, not read today), and the ELEC page cold and
dark (whether `apuData` and `elecFlags` still come).

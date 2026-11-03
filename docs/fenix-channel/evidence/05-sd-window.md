# Plan 05 evidence: the SD window

Fenix build **3.0.0.7454**, kn4iee's install: the channel's log names that build, one update behind the team's checked
3.0.0.7613 (`FenixChannelFields.VerifiedBuilds`). An earlier version of this file said 7613: that was wrong. Worked by
kn4iee with Claude. Raw fields come from the team's local channel tools, which stay local.

Because 7454 is not a checked build, the app's gate trusted no display on it, and Alt+S gave the AI read in the app
(`fenix_channel.log`: "build 3.0.0.7454: not checked; every display uses the AI read"). Everything below that reads rows
was made by the app's own composer from saved pushes, with the verdict given as trusted, so it shows what the window
would say. On 2026-11-03 the whole channel contract of the installed 7454 (`FenixDisplay.exe`, all 232 `ProsimRemoting`
types, 7,042 lines of the team's metadata dump, enum members and defaults included) was compared with the team's dump
of 2026-11-01: identical but for the obfuscated internal names. Whether 7454 joins `VerifiedBuilds` ([FNX-23]) is the
owner's decision; this plan does not add it.

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

## In the sim, 2026-11-03 (Task 1, and Task 10's checks of the rows against the data)

kn4iee's Fenix A320 CFM (N328NW) at KATL on 3.0.0.7454, MSFS 2024, both engines running at first, the parking brake
set. Claude worked the aircraft through the simconnect MCP as the definition writes it (an ECP key 1 then 0; switches by
value), with the owner's go-ahead, and left it as found (both engines running, APU off, the SD back on WHEEL). The fields
came from the team's listener (a push saved per page or after each action, read-only); the rows were made from those
pushes by the app's own `FenixChannelSd.Compose`, through a throwaway probe that is not committed. No sim captures were
taken, so the labels against the SD's drawing still wait (below).

- **Pages.** ENG 8, ELEC 7, HYD 11, FUEL 10, STS 14 (shown 4.9 s with NORMAL, then back to WHEEL 12, the ground's
  automatic page). ALL, pressed and held, cycled 8, 2, 3, 7, 11, 10, 1, 4, 6, 12, 9 and round again. Pages 0, 5 and 13
  never showed on the ground; the power-up and self-test from cold and dark were not run. `NameOf` is unchanged.
- **Presence and shapes.** Every field the plan reads came in the shape `FenixChannelFields.Sd.cs` declares. Sent as
  empty messages when all false or 0: 16 `elecFlags`, 22 `essTR`, 133.9 (the yellow electric pump, off), 134.8, 134.15
  and 177. 23 `extPwr` carried 23.2 = 400 with no external power (17.8 clear), so the page reads it only while 17.8 is
  set, as it does. 154 was left out: this Fenix is set to pounds, and FOB, F.USED and GW read LBS. The vibrations
  (129.n.11, .12) were left out at idle, so VIB reads 0.0. 175 came as seven empty elements and `            gNORMAL`;
  176 was left out with nothing to report.
- **The frame.** 178 at the gate: `g+17`, `g+17`, empty, empty, `16`, `25`, `c--`, empty, empty (GW not computed: no
  ZFW entered). With GEN 1 off, 176 read `  wuINOP SYSu`, `aMAIN GALLEY`, `aGEN 1`, and 175 was left out: the letters
  seen were `g`, `c`, `w`, `u` and `a` (amber, measured for the first time). The STATUS page read "STATUS", "INOP SYS,
  white", "MAIN GALLEY, amber", "GEN 1, amber".
- **Valves.** X FEED on: 134.11.4 went 1 (closed), 2 (in transit, about 3 s), then left out (open); off: 2, then 1. An
  engine master off: its LP valve went 2, then 1. The start valve read 1 (closed) running, 2 then left out (open) during
  the start, and 5 (DataInvalid) while the ENG 2 FIRE pushbutton was out. `ValveStateEnum` holds.
- **PTU (132).** -1 with the PTU pushbutton off and both engines running, and, with the pushbutton in AUTO, while the
  parking brake was set and one engine master on (the PTU inhibit); 0 normal; 1 with both engines off and the yellow
  electric pump on (green rose to 2650 PSI through the PTU), and while engine 1 started with engine 2 running; 2 for
  about 50 s after engine 1's master went off with engine 2 already off (yellow at 0, green falling). They became
  `PtuStates` Off, Idle, YellowToGreen and GreenToYellow.
- **ELEC.** GEN 1 off: "GEN 1: OFF" at once, and the Fenix set GALLEY SHED (16.1). The APU master on: 24.12 and 24.13
  (the amber title), "APU GEN: 0 %, 0 V, 0 HZ"; AVAIL (24.8) after about 75 s, "APU GEN: 0 %, 115 V, 399 HZ"; the APU
  GEN pushbutton off set 24.14 (Off) while the row still spoke the figures, so the row now reads "APU GEN: OFF" then.
  24.12 cleared about 80 s after AVAIL did, once the APU stopped. After the ENG 2 FIRE test, GEN 2 stayed off line
  until its pushbutton was cycled.
- **HYD.** Engine 2 off: "YELLOW: 0 PSI", "YELLOW ENG 2 pump: LO"; both off: the blue electric pump stopped ("off");
  the yellow electric pump on: "YELLOW ELEC pump: running". The ENG 2 FIRE pushbutton out: 133.11 left out, "YELLOW fire
  valve: closed", which confirms that a fire valve is open while its field is set.
- **ENG.** The mode selector at IGN/START set 129.6, and the start rows read "IGN: off, B", "start valve: closed, open",
  "PSI: 38, 38" for engine 2's start and "IGN: A, off", "start valve: open, closed" for engine 1's; 129.6 cleared about
  20 s after the selector went back to NORM. NAC (129.7) was never set on the ground.
- **Against the Fenix's own values.** BAT 1 and BAT 2 27.6 and 27.4 V (`N_ELEC_VOLT_BAT_1`, `_2`), "28 V" and "27 V";
  OIL QT, PSI and °C against the data service's `aircraft.engines.n.oil.*` (14.42, 14.27; 33.7, 33.7; 47.3, 47.4): "14.4,
  14.3", "34, 34", "47, 47"; TAT and SAT against `aircraft.temperature.tat` and `.oat` (16.97): "+17"; FOB 6080 against
  the E/WD's FOB line (`wFOB:   g6080 cs LBS`); UTC against the sim's ZULU TIME (16:48). GW was "--": no FMS gross weight
  to compare.

## Inferred before the sim run

Before the run, Tasks 2 to 8 took these from the plan and the team's decoded cruise push of 2026-11-01 (an A321 at FL340,
both engines running), each marked "inferred" in the code. The run above settled the PTU (four states, not one), the
fire valves (open while set: confirmed), the empty messages (confirmed on the ground) and the igniter letters (A for
engine 1's start, B for engine 2's, consistent with 17 and 18). Still inferred: the A321 FUEL layout, the A318
following the A320's, and the meaning of the flags the run did not change one by one.

- 132 `ptuState` was left out (0) with both engines running and the PTU in AUTO, so `PtuStates` first held only
  `Idle = 0` ("PTU: idle"), every other value "not available"; replaced by the four measured states above.
- 133.10 and 133.11 (`ShutoffLeft`, `ShutoffRight`) were set with the engines running: a fire valve reads "open" while set.
- 16 `elecFlags`, 22 `essTR` and 133.9 (the yellow electric pump, off) came as empty messages, so a part with everything
  false is sent, not left out; 24 `apuData` carried EgtCaution 1067 and EgtMax 1100 with the APU off.
- 134.11: `Engine1` and `Engine2` were left out (Open) while the closed valves came; the A321 had no outer tank quantities,
  its outer temperatures were NaN, and `Center1`/`Center2` set LowPressure with no switch.
- 178: `g-26`, `g-53`, `  g0`, empty, `17`, `18`, `g75600`, empty, empty.
- Which flag means what (ELEC, HYD, FUEL), the igniter letters (17 A, 18 B), the A321 FUEL layout (no outer tanks,
  transfer valves or center pumps; its extra rows after the center tank) and the A318 following the A320's layout.

## Waiting on the owner

What the 2026-11-03 run could not do:

- **In the app (Task 10's Alt+S, list and log steps, by ear).** MSFSBA from this plan's build: Output Alt+S opens the
  window on the page shown and a second press brings it forward; the Fenix windows list's "SD" opens it; over the
  checks, `fenix_channel.log` holds no "SD:" problem line; NVDA reads the STATUS page and the bottom line naturally,
  colors after each stretch. Cold and dark: Alt+S gives the AI read with nothing before it ("SD not trusted: display
  off" in the log), and the list's entry opens "SD: not available, display off".
- **Labels against the SD's drawing.** No sim captures were taken: the rows' labels, order and rounding against a
  picture of each page.
- **Other airframes and settings.** The A321's FUEL page (its layout and its ACT rows), the variant on each airframe,
  field 154 with the Fenix's weight unit in kilograms, and whether an A319 or A320 reports extra tanks.
- **Cold and dark.** The power-up and the SD's self-test (pages 0 and 13), and whether `apuData` and `elecFlags` still
  come.
- **A failure.** The yellow electric pump's overheat, set from the Fenix failures window: whether the SD draws OVHT on
  it (133.9.3, not read today).
- **In flight** (gates nothing): ENG and FUEL in cruise, SAT below zero, what elements 2, 3, 7 and 8 of the bottom line
  hold.

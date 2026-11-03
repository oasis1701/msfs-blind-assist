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

## In the app, 2026-11-03 (Task 10's Alt+S, list, log and by-ear steps)

kn4iee, by ear with NVDA, on Fenix 3.0.0.7613 (after updating from 7454) in the A320 CFM, with MSFSBA built from this
plan's branch, at 13:13 and again at 13:49 with the page keys: Output Alt+S opened "Fenix SD" on the page shown and a second press brought
it forward; the Fenix windows list's "SD" opened it; the STATUS page and the bottom line read naturally, colors after
each stretch; the page keys after the list switched the page as the panel's do. `fenix_channel.log`: "build 3.0.0.7613:
checked", "SD trusted", and no "SD:" problem line over either run. The one wording the claimant found odd, "UTC: 18 H
16", is now "UTC: 18:16". On the unchecked 7454, earlier the same day, Alt+S gave the AI read with nothing before it
("SD not trusted: build 3.0.0.7454 not checked"): the fallback path works.

## Cold and dark, power-up, kilograms and the A321, 2026-11-03

On 3.0.0.7613, kn4iee's A321 IAE (N934AA, two extra tanks) cold and dark at KATL, with the Fenix's weight unit switched
to kilograms; Claude through the simconnect MCP, the aircraft left cold and dark again afterwards (DOOR page, external
power connected but off, batteries off). (An earlier version of this section said A320: the A321 had been loaded at
13:55, after the in-app runs.)

- **The variant.** `debug.log`'s "Variant:" line read `Type = A320, Engine = Cfm56, ActCount = 0` for the A320 at 13:49
  and `Type = A321, Engine = IaeV2500, ActCount = 2` for the A321 at 13:55 (N_CONFIG_AIRCRAFT_TYPE 4, N_CONFIG_ACT_COUNT
  2): the FUEL page's layout follows it.

- **Cold and dark.** Every display unit's power was 0 (12.1), so the SD is not live: the gate reads "display off" and
  Alt+S keeps the AI read. `elecFlags` (16) still came empty and `apuData` (24) still came, its values NaN; 22 and
  133.1.1 were NaN. The bottom line held ` aXX` for TAT and SAT.
- **Power-up.** Batteries on: DC BAT powered (17.6), the SD still dark. External power on: AC and DC powered, and both
  ECAM units on with content 7 (SelfTest) for 25 to 45 s, then 5 (the lower ECAM: the SD live). Field 10 stayed 6 (DOOR)
  from cold and dark through the self-test: pages 0 and 13 were never seen, so `NameOf` stays unchanged. TAT and SAT
  read "XX, amber" with the IRs off, as the SD draws them.
- **Kilograms and the A321's FUEL page.** With the weight unit in kilograms, 154 was set. The FUEL page on external
  power, engines off, composed with the A321 variant: "F.USED 1+2: 0 KG", "APU valve: closed", "LP valves: closed,
  closed", "X FEED valve: closed", "left tank: 1500", "center tank: 0", "center transfer valves: closed, closed", "ACT 1:
  0", "ACT 2: 0", "right tank: 1500", "left pumps: off, off", "right pumps: off, off", "FOB: 3000 KG" (the E/WD's FOB
  line `g3000 cs KG`), "tanks °C: 18, 18". The channel still sent the parts an A321 lacks, and the page read none of
  them: the outer tank temperatures (134.14.1, .4, NaN), Center1 and Center2 (134.4, .5, low pressure set), and all
  four outer transfer valves (134.11.5 to .8, closed). It also sent `ActPump` (134.15) with Act1Available and
  Act2Available set, and JetPumps (134.1) with fields 5 to 8 (the amber flags) but not 3 and 4, so the center transfer
  valves read closed with the center tank empty; their open state was not seen. The ELEC page on external power:
  "EXT PWR: 115 V, 400 HZ", "GEN 1: 0 %, 0 V, 0 HZ" with the engines off.

## The A321 fuelled, engines running, 2026-11-03 (15:20 to 15:40 local)

Same A321 IAE, powered by Claude (batteries, external power); the engines were then started from the cockpit side
(thrust levers a little above idle, parking brake set, 0 knots). 22,000 kg were loaded through the Fenix EFB's Mass and
Balance page (fast time, 8 minutes, opened in Claude's own browser pane), with a push saved every 5 to 8 seconds and
composed with the A321 variant:

- **The FUEL page while fuelling.** Every row moved with the fuel: from FOB 2840 (wings 1420 each, center 0, ACTs 0),
  through 11,140 (left 4750, right 3510, "center transfer valves: open, open", ACT 1 1880, ACT 2 1010) and 13,260, to
  16,700 (left 6060, right 6080, center 1250, "center transfer valves: closed, closed", ACT 1 2310, ACT 2 1010) and
  21,560 (left 6050, right 6040, center 6160, valves closed, ACT 1 2310, ACT 2 1010, "GW KG: 71700, green"). The
  center transfer valves (134.1.3, .4) read open while the center tank fed the wings, with JetPumps' flow flags (134.1.1,
  .2) set, and closed once the wings were full. ACT fuel showed in both ACT rows. The SD's FOB matched the E/WD's FOB line
  in every push but one taken mid-flow (3070 against 3080).
- **GW.** The EFB's Send to MCDU gave the FMS its weights, and 178[6] became `g59000`: "GW KG: 59000, green" (the
  EFB's live figure 58,808 a moment before, the zero-fuel weight 50,158 plus 8,880 aboard), then 61,300, 63,400 and
  66,900 as the fuel came: the SD's gross weight to the nearest 100. Elements 2, 3, 7 and 8 stayed empty.
- **ENG with IAE engines.** "OIL QT: 14.5, 14.2", "OIL PSI: 151, 151", "OIL °C: 118, 118" against the data service's
  14.52, 14.23; 151.1, 151.3; 117.9, 117.6. Unlike the CFM at idle, the IAE sends its vibrations (129.n.11 0.03 and
  0.04, .12 0.12 and 0.11): "VIB N1: 0.0, 0.0", "VIB N2: 0.1, 0.1". F.USED counted from the start.
- **ELEC and HYD.** Both generators on line, external power no longer shown once 17.8 cleared; HYD normal with "PTU:
  idle".
- **The ACT pump.** The fuel panel's ACT pushbutton (`S_OH_FUEL_ACT`) read 0, out; the app's Fuel panel already offers
  it as "ACT Pump" on an airframe with extra tanks (`FenixA320Definition.Variants.cs`).

## Waiting on the owner

What the 2026-11-03 runs could not do; none of it gates the window, which the claimant has heard working:

- **Labels against the SD's drawing.** No sim captures were taken: the rows' labels, order and rounding against a
  picture of each page. The claimant's by-ear check found only the UTC wording; the owner rules whether that is enough.
- **The list entry with the SD off.** "SD: not available, display off" in the window opened from the Fenix windows list
  while cold and dark (the verdict itself was measured above).
- **The A321's FUEL page against a picture.** Its row order and labels against what the SD draws.
- **A failure.** The yellow electric pump's overheat, set from the Fenix failures window: whether the SD draws OVHT on
  it (133.9.3, not read today).
- **In flight** (gates nothing): ENG and FUEL in cruise, SAT below zero, what elements 2, 3, 7 and 8 of the bottom line
  hold.

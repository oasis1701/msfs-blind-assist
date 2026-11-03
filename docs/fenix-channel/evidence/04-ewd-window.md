# Plan 04 evidence: the E/WD window

Fenix build 3.0.0.7613, MSFS 2024. Worked by Robin Kipp with Claude: the aircraft handed over, Claude driving it through
the simconnect MCP with the definition's write paths. Raw fields come from the team's local channel tools, which stay
local; each comparison reads one push beside a capture of the sim window (PrintWindow, center panel view, instrument
index 8) taken within a second of it.

## The fields ([FNX-23]): Task 2 Step 6, 2026-11-03

`fenixmeta` on the installed `FenixDisplay.exe` (file of 2026-10-29): `field string flapEcamState [ProtoMemberAttribute 152]`,
`field boolean isKg [ProtoMemberAttribute 154]`, `field EngineControlState engineState [ProtoMemberAttribute 129]`. They
stand, with no change. Live, 152 came length-delimited as a string (`g1+F`).

## What the E/WD draws: Task 1, A321 IAE, 2026-11-03

`FenixA321 IAE SL TC`, N106NN, EDDF. `debug.log`: `Variant: FenixVariant { Type = A321, Engine = IaeV2500, ... IsKnown = True }`.

### The engine block

Three levels, each engine's value either side of the center column:

| Level | Left and right of center | Center |
|---|---|---|
| 1 | | EPR |
| 2 | FF LBS/H (or KG/H) | EGT °C |
| 3 | N2 % | N1 % |

The label is "FF", no dot. "FF" and "N2" sit outside the gauges, level with EGT and N1, so the plan's expected order
(EPR, EGT, N1, N2, F.F) is not what is drawn.

Digits as drawn, against the channel:

| Value | Channel | Drawn | Reading |
|---|---|---|---|
| EPR | 0.99693, steady, engines off | 0.996 | truncated |
| EPR | 1.00978, 1.00994 | 1.009 | truncated |
| EPR | 1.01045 | 1.010 | |
| N1 | 21.5670, steady over six frames | 21.5 | truncated |
| N1 | 21.5861 | 21.5 | truncated |
| N1 | 21.6023 | 21.6 | |
| N2 | 59.88 to 60.03, noisy | 59.8, 59.9, once 60.0 | truncated: 59.8 needs below 59.85 if it rounded, and no sample of about 30 was |
| EGT | 7.98, steady, engines off | 8 | rounded |
| EGT | 422.958, 423.69, 424.87 | 423, 424, 425 | rounded |
| EGT | 344.97, 343.95 | 345, 344 | rounded |
| FF | 900, 420 | 900, 420 | as sent; the Fenix sends it in steps of 20 |

So N1, N2 and EPR truncate and EGT rounds. The plan's "81.0801 drawn 81.1" was its own example, with no capture behind it.

### The thrust limit

129.8 as drawn, in three shapes: `cCLB`, `g1.366`, empty (engines off); `cTOGA`, `g1.619`, empty (engines running, on the
ground); and with both N1 MODE pushbuttons on, `N1 MODE`, `cTOGA`, `g95.4c%`, drawn boxed, the first element white. With one
pushbutton on, the limit stays EPR. While the FADECs powered up, `N1 MODE`, `cCLB`, `g81.5c%`, and before that, unpowered,
`aXX`, `aXX`, empty.

### Engines shut down

E/WD powered from the APU, both masters off, measured twice: before any start, and about 90 s after a shut-down.

| | Channel | Drawn |
|---|---|---|
| N1, N2 | NaN | XX, amber |
| EPR | 0.99698 | 0.996 |
| EGT | 344.97 (cooling), 7.98 (cold) | 345, 8 |
| FF | left out | 0 |

129.1 and 129.2 hold `EngineStatus` (15) and `EiuFault` (31) only while the FADECs are unpowered (both 1, every value NaN);
neither is set once the E/WD is drawn. NaN reads "XX" as planned, an absent FF is protobuf's 0 as drawn, and no flag is
needed, so Task 2 stands.

A pushbutton's N1 MODE (field 14 set) makes that engine's EPR NaN, drawn XX; N1 keeps its digits, the block its order.

### Flaps

152 beside the E/WD, engines running. Every text as drawn, in its color:

| Lever | Moving | At rest |
|---|---|---|
| 0 | `c0` | left out, nothing drawn |
| 1 | `c1+F` | `g1+F` |
| 2 | `c2` | `g2` |
| 3 | `c3` | `g3` |
| FULL | `cFULL` | `gFULL` |

### Weight unit

The Fenix's unit here came from the livery (`weight_unit` Pounds, source Livery). Set to Kilograms as a session change (Robin's
go-ahead), then reset to default, which read back Pounds, source Livery.

| | Pounds | Kilograms |
|---|---|---|
| 154 | left out | 1 |
| FOB line | `wFOB:   g6500 cs LBS` | `wFOB:   g2860 cs KG` |
| Ff, idle | 900 | 420, 400 |
| Drawn | FF LBS/H 900 | FF KG/H 420 |

The channel's Ff is already in the display's unit: shown as sent, no conversion.

### The lines

Each as sent and as drawn, matching: left `gIR IN ALIGN > 7 MN`, `gFOB BELOW 6600 LBS`; a caution, `uaNAVu aGPWS TERR DET FAULT`,
`c -GPWS TERR..........OFF`; the take-off memo, by itself some minutes after the second engine's start (not yet at 18:42, there at 18:45, engine 2's master at 18:39),
`guT.Ou AUTO BRKc.....MAX    `, `g    SIGNS ON`, `g    CABINc......CHECK    `, `g    SPLRSc........ARM    `,
`g    FLAPSc........T.O    `, `g    T.O CONFIGc..TEST    `; right `gPARK BRK`, `gPRED W/S OFF`, `gTCAS STBY`, `gAPU AVAIL`, `gAPU BLEED`.
Unpowered, 173 and 174 came empty and the FOB line read `wFOB:    aXXX cs LBS`.

## What the E/WD draws: Task 1, A320 CFM, 2026-11-03

`FenixA320 CFM WF`, D-AIZA, EDDF. `weight_unit` Kilograms, source Livery.

### The engine block

One column, top to bottom, each engine's value either side of it: N1 %, EGT °C, N2 %, FF KG/H. "FF" has no dot here either.

| Value | Channel | Drawn | Reading |
|---|---|---|---|
| N1 | 18.2898, 18.2156 | 18.2, 18.2 | truncated |
| N1 | 18.34 | 18.3 | |
| N1 | 0.8824, spooling down | 0.8 | truncated |
| N2 | 58.7576, 58.8590 | 58.7, 58.8 | truncated |
| EGT | 387.78, 386.79 | 388, 387 | rounded |
| EGT | 219.78 | 220 | rounded |
| EGT | -48.52 | -49 | rounded |
| FF | 300 | 300 | as sent |

129.1.5 and 129.2.5 carry an EPR here too (1.000 with the engines off, 0.999 to 1.001 at idle); the E/WD draws none. The thrust
limit is `cCLB`, `g79.1c%`, empty, and `cTOGA`, `g90.6c%`, empty once running: its number moves with the temperature.

### Engines shut down

Powered from the APU, both masters off, three states:

| | FADECs on, never started | Spooling down, 90 s after the masters | FADECs off |
|---|---|---|---|
| N1 | left out, drawn 0.0 | 0.8824, drawn 0.8 | NaN, XX |
| N2 | 0.1196, drawn 0.1 | 2.6494, drawn 2.6 | NaN, XX |
| EGT | -49, cooling toward ambient | 233.18, drawn 233 | NaN, XX |
| FF | left out, drawn 0 | left out, drawn 0 | NaN, XX |
| Thrust limit | `cCLB`, `g79.1c%` | `cCLB`, `g83.2c%` | `aXX`, `aXX`, drawn XX over XX, amber |

The FADECs dropped out by themselves about five minutes after the batteries came on, with the engines never started (19:00:04).
Every state reads as sent: no flag is needed.

### Flaps

152 as on the A321: `c1+F`, `c2`, `c3`, `cFULL` moving; `gFULL` at rest; the texts the same on both airframes.

## What changes for Task 3

- The fuel flow label is "FF", not "F.F".
- The IAE block is drawn EPR, then FF and EGT, then N2 and N1. Rows go top to bottom and left to right (spec 2.4): EPR, FF, EGT, N2, N1.
- N1, N2 and EPR truncate; EGT and FF round. The plan's one rounding rule cannot hold both.
- 129's FF is already in the display's unit: shown as sent.
- NaN reads "XX", an absent value reads 0, as planned; no `SingleEngineState` flag is needed, so Task 2 stands.

## The window and the key in the sim: Task 6, 2026-11-03

MSFSBA from this plan's branch, run from `C:\MSFSBA-FenixChannel`: `791e922cf` for the CFM A320's runs, `7fb25677e` (with row 57) for the IAE A321's and the dark keys. Rows read through UI Automation, read only, beside a capture of the E/WD; the list's name "E/WD display" and the window's "Fenix E/WD" confirmed the same way. Robin ran the keys and listened.

**CFM A320, D-AIZA, EDDF.**

- Powered at the gate, engines running, flaps at 1, `fenix_channel.log` "E/WD trusted" (19:39:39), `Variant` `Engine = Cfm56`. Input Ctrl+Shift+F, "E/WD" opened it. Rows at 20:23:50, capture in the same second: `N1 %: 18.1, 18.2`, `EGT °C: 387, 387`, `N2 %: 58.7, 58.8`, `FF KG/H: 300, 300`, `TOGA cyan, 90.6 green, % cyan`, `FOB: white, 2260 green, KG cyan`, `1+F, green`, the take-off memo (`T.O AUTO BRK green, .....MAX cyan`, `SIGNS ON, green`, `CABIN green, ......CHECK cyan`, `SPLRS green, ........ARM cyan`, `FLAPS T.O, green`, `T.O CONFIG green, ..TEST cyan`), `PARK BRK, green`, `PRED W/S OFF, green`, `TCAS STBY, green`, `APU BLEED, green`. Each matched.
- TO CONFIG pressed: `T.O SPEEDS NOT INSERTED, amber` and `PRED W/S OFF, amber`, as drawn. The list went from 17 rows to 12 and kept the selection.
- By ear (Robin: "All as expected"): the selection stays on the EGT row for 10 s, F5 refreshes, Escape closes.
- Output Alt+E opened it; pressed again from another window, the same window came forward; UI Automation found one, selection on the first row, and `debug.log` no AI read.
- Engines shut down, the APU still on: `N1 %: 0.7, 0.7`, `EGT °C: 234, 234`, `N2 %: 2.2, 2.2`, `FF KG/H: 0, 0`, `CLB cyan, 83.2 green, % cyan`, `OUTR TK FUEL XFRD, green`, each as drawn.
- Cold and dark: the open window went to the one row `E/WD: not available, display off` (log 20:32:03). As the plan first had it, Alt+E gave the AI read with no extra words and the list entry the status row. Robin ruled that this is not what he wants (spec row 57).

**IAE A321, N106NN, EDDF**, in pounds by its livery.

- `Variant` `Engine = IaeV2500`; "E/WD trusted" at 21:45:58. Output Alt+E opened it. Rows at 21:47:29, capture in the same second: `EPR: 1.010, 1.010`, `FF LBS/H: 900, 900`, `EGT °C: 424, 421`, `N2 %: 59.9, 59.9`, `N1 %: 21.5, 21.4`, `TOGA cyan, 1.619 green`, `FOB: white, 4500 green, LBS cyan`, `1+F, green`, the memo as above, each as drawn (N2 within its last digit).
- Both N1 MODE pushbuttons on: `EPR: XX, XX`, `N1 MODE, TOGA cyan, 95.3 green, % cyan`.
- Flaps to 0: `0, cyan` while moving, then no row.
- Engines shut down, the APU still on: `EPR: 0.997, 0.997`, `FF LBS/H: 0, 0`, `EGT °C: 345, 345`, `N2 %: XX, XX`, `N1 %: XX, XX`, `CLB cyan, 1.366 green`, `FOB BELOW 6600 LBS, green`, each as drawn.
- Cold and dark, six displays "display off" in the log (ISIS last, at 21:53:23). Row 57: output Alt+E, Alt+P and Alt+S each opened the window on its one status row, with no AI read (Robin: "All three did"; `debug.log` none). Alt+N and Alt+I have no window on this build yet.

**The log.** No "E/WD:" line (no problem reported) through either airframe's runs.

**A red warning, by accident.** With the IRSs left off and both engines started, `urNAVu rADR 1+2+3 FAULT` and its cyan lines (`c -AP+FD..............OFF` and four more) came with the continuous chime. That was Claude's own omission; IRSs to NAV and Master Warning cleared it. It shows a red E/WD title arriving in the shape the rows test expects.

## Not measured

An alert while the window is open: its rows follow the lines above, and spoken alerts are plan 09's. The E/WD's fuel check still latches on every landing roll (plan 02's evidence), so after a landing this window shows its status row until reconnect: for plan 12.

LEAP and PW GTF: no NEO on 3.0.0.7613. Task 3 gives them the real A320neo's N1 block (owner question, as recommended),
unmeasured. Pounds on the CFM airframe and kilograms beyond a minute on the IAE: 154 and FF behaved the same way in both
units on the A321, and plan 05's run on an A320 CFM in pounds also had 154 left out and LBS on the FOB line.

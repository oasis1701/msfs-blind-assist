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

## Not measured

LEAP and PW GTF: no NEO on 3.0.0.7613. Task 3 gives them the real A320neo's N1 block (owner question, as recommended),
unmeasured. Pounds on the CFM airframe and kilograms beyond a minute on the IAE: 154 and FF behaved the same way in both
units on the A321, and plan 05's run on an A320 CFM in pounds also had 154 left out and LBS on the FOB line.

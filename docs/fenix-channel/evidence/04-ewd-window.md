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

## Not measured

LEAP and PW GTF: no NEO on 3.0.0.7613. Task 3 gives them the real A320neo's N1 block (owner question, as recommended),
unmeasured.

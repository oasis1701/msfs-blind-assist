# Plan 08 evidence: the FMA announcements

Fenix build **3.0.0.7613**, kn4iee's install, flown by kn4iee, recorded by Claude. Raw fields come from a
subscribe-only recorder built from the team's local channel tools, which stay local and are never committed.

## Task 1 in flight: the FMA's slots, 2026-11-04

The airframe: a Fenix **A319, IAE V2500** (the app's variant read), MSFS 2024, flown with the FDs on from take-off
(about 14:37 local) to an ILS autoland (CAT3 DUAL) on **KDFW 18R** (touchdown about 17:00:24). The recorder logged
every change of `RemoteData` field 111 (all 16 `FmaEntry` slots, the coded text exactly as sent and `ChangeBox`) and
field 112 (`fmaPitchRollCombined`) from 14:18 to 17:11: 173,001 pushes, 148 changes, never more than 409 ms between
two pushes. Times are local. Below, a slot is `slot:"coded text"`, and `[B]` marks the change box.

### The moments

- **On the ground (14:18:09).** 6 `cCLB`, 7 `cNAV`, 9 `w1   2` (FD 1 and 2).
- **Take-off thrust (14:37:50.048).** 1 `gSRS`[B], 14 `cA/THR`[B]; **782 ms later** 0 `wbMAN`[B] and 5 `FLX c+52`.
  No RWY or RWY TRK showed: the lateral column stayed blank until NAV.
- **NAV (14:38:32.182).** 2 `gNAV`[B], 7 cleared, in one push. **AP1 (14:38:42.505):** 4 `wAP1`[B].
- **Thrust reduction (14:39:05.549).** 10 `wfLVR CLB`; 122 ms later 1 `gCLB`[B] with 6 `mALT` (a constraint) in one
  push. 10 cleared at 14:39:06.629 (the levers in CL after 1.1 s); 181 ms later 0 `gTHR CLB`[B], 5 cleared and 14
  `wA/THR`[B].
- **A constraint in the climb (14:46:17.935).** 1 `gALT CST*`[B] with 6 `cCLB` in one push; 185 ms later 0 `gSPEED`.
  1 `gALT CST` at 14:47:05.227.
- **OP CLB (14:47:25.030).** 1 `gOP CLB`[B], 6 cleared; 61 ms later 6 `cALT`; 302 ms after the first push, 0
  `gTHR CLB`.
- **Level-off and cruise.** 1 `gALT*`[B] with 6 cleared (14:52:43.390); 420 ms later 0 `gMACH`. 1 `gALT!CRZ` at
  14:53:15.076.
- **The levers out of CL in the cruise (14:53:26 to 14:53:47).** 10 `wfLVR CLB` came and went four times (the shortest
  absence 117 ms, the shortest showing 124 ms), then 0 `abwMAN`[B] with 5 `THR` and 14 `cA/THR`[B] (MAN THR, its box
  amber), 5 `MCT`, and back to 0 `gMACH`, 14 `wA/THR`.
- **The minimum (16:19:45.557 to 16:22:41.025).** 13 `RADIOdc⠀⠀ 200`, drawn "RADIO 200" (plan 02's record), in the
  cruise and early descent only: slot 13 held nothing on the approach.
- **The descent.** 1 `gDES`[B] (16:21:25.902), 49 ms later 6 `cALT`; 2.2 s later 0 `gTHR IDLE`. A short excursion: 2
  `gHDG` and, 55 ms later, 1 `gV/Sc-2800`, 0 `gMACH` 0.66 s after; NAV, DES and THR IDLE back by 16:23:22. From 16:27 to
  16:38, 0 changed between `gTHR IDLE` and `gSPEED` 17 times (twice only 60 ms apart), and 11 `MORE DRAG` came and went
  seven times. 1 `gALT*`[B] with 6 cleared (16:39:05.281), 120 ms later 0 `gSPEED`; 1 `gALT` at 16:39:30.546. HDG
  (16:43:30.645), then OP DES (1 `gOP DES`, 59 ms later 6 `cALT`, 0.54 s later 0 `gTHR IDLE`), ALT* and ALT again.
- **APPR (16:54:37.800).** In one push: 3 `CAT3`[B], 6 ` cG/S` (a space before the color letter), 7 `cLOC`, 8
  `SINGLE`. 4 `wAP1+2` at 16:54:46.074, 8 `DUAL` 120 ms later.
- **LOC* (16:55:47.168).** 2 `gLOC *` (with a space), 7 cleared, in one push. 2 `gLOC` at 16:56:27.434.
- **G/S* (16:57:25.947).** 1 `gG/S*`, 6 cleared, in one push. 1 `gG/S` at 16:57:43.351.
- **The autopilots on final (16:58:07 to 16:58:36).** 4 went blank, AP1+2, AP1, AP2 and AP1+2 again; 3 between `CAT3`
  and `CAT1`, 8 between `DUAL`, `SINGLE` and blank, 13 changes in 29 s.
- **LAND (16:59:56.519).** 1 `gLAND`[B], and **field 112 became 1**. Slot 2 kept `gLOC`.
- **FLARE and touchdown.** 1 `gFLARE`[B] (17:00:22.866), 0 `gTHR IDLE` (17:00:24.066), 0 blank and 14 cleared
  (17:00:26.407).
- **ROLL OUT (17:00:28.387).** 1 `gROLL OUT`[B]. 4 blank (the autopilots off, 17:00:51.132), 3 `CAT1` and 8 cleared
  (17:00:52.994), and **2 `gLOC *`[B] (17:00:58.453), under ROLL OUT, field 112 still 1**.
- **The modes clear (17:01:01.874).** 1, 2 and 3 blank, field 112 back to 0; only 9 `w1   2` stays.

### What the record shows

- **The change box** comes on in the push that brings a new mode and goes off 10.0 s (to within 0.1 s) after its
  column's last change, in a push that changes nothing else: CAT3's box held 18.4 s, until 10 s after DUAL replaced
  SINGLE.
- **Field 112** was 1 exactly from LAND until the modes cleared after ROLL OUT (16:59:56.519 to 17:01:01.153), and 0
  for the rest of the flight.
- **LVR CLB** came as `wfLVR CLB`, the same text in every push that held it: the flash is the `f` letter, drawn by the
  display, not a text that changes.
- **An armed mode becoming active** came in one push every time (NAV, CLB, ALT CST*, ALT*, LOC*, G/S*: the new row 1
  and the cleared row 2 together). What follows in another column came 49 ms to 2.2 s later: SPEED 120 to 185 ms after
  an ALT* or ALT CST*, MACH 420 ms after ALT*, THR CLB 302 ms after OP CLB, THR IDLE 0.54 to 2.2 s after DES and OP DES.
- **The letters.** Active modes `g`. Armed modes `c` (CLB, NAV, ALT, G/S, LOC, and A/THR armed in column 5). `m` once:
  ALT armed in the climb (6 `mALT`, 14:39:05.671). `a` once, MAN THR's box (`abwMAN`), its text white. The FLX
  temperature, the V/S target and the minimum's figure `c`. No color letter at all: CAT3, CAT1, SINGLE, DUAL, MORE
  DRAG, and row 2's THR and MCT. Also `!` (ALT CRZ) and, in slot 13, `d` and U+2800, all codes `FenixChannelText`
  knows. No `y`, and no letter it does not know: the PFD stayed trusted from 15:00:15 to 17:11:41 (fenix_channel.log).
- **Not seen:** RWY or RWY TRK, TOGA, a text in slot 12 or slot 15, and DH or MDA on the approach.

### The outcome rows

| Row | Holds | What the record shows |
| --- | --- | --- |
| O1 | **yes** | Every slot that held text sits at row x 5 + column: 0 thrust (MAN, THR CLB, SPEED, MACH, THR IDLE), 1 vertical (SRS, CLB, ALT CST*, OP CLB, ALT*, ALT CRZ, DES, V/S, ALT, OP DES, G/S*, G/S, LAND, FLARE, ROLL OUT), 2 lateral (NAV, HDG, LOC *, LOC), 3 approach (CAT3, CAT1), 4 engagement (AP1, AP2, AP1+2); 5 thrust (FLX +52, THR, MCT), 6 vertical armed (CLB, ALT, G/S), 7 lateral armed (NAV, LOC), 8 approach (SINGLE, DUAL), 9 engagement (FD 1 2); 10 thrust message (LVR CLB), 11 vertical message (MORE DRAG), 13 approach (RADIO 200), 14 engagement (A/THR) |
| O2 | no | |
| O3a, O3b | no | Slot 15 never held text. |
| O3c | **yes** | `Of(15)` stays (0, Unknown). |
| O4a | **yes** | LAND, FLARE and ROLL OUT came in slot 1 alone, heard under Vertical. But the partner slot 2 was not blank: see below. |
| O4b | no | |
| O5 | no | MORE DRAG came whole in slot 11; slot 12 never held text. |
| O6 | **yes** | Slot 13 held `RADIOdc⠀⠀ 200`. Spec section 5 row 35 has ruled it: the Approach column's third row, spoken in its phrase, `IsMessage(13)` false. |
| O7 | no | |
| O8 | no | LVR CLB's text was the same in every push, and each armed mode became active in one push. |

### Outside the rows: the lateral mode under LAND, FLARE and ROLL OUT

While field 112 was 1, slot 2 still held the lateral mode: `gLOC` from LAND to the rollout, then `gLOC *`[B] after the
autopilots went off. The real A320 draws LAND, FLARE and ROLL OUT across the Vertical and Lateral columns, with no
lateral mode beside them, and field 112's name says the Fenix does too (this was not checked on the screen). This
plan's build, flown on this flight, spoke "FMA: Lateral: LOC *, green" on the rollout for a mode the PFD does not draw.
The owner ruled (2026-11-04): the Lateral column is silent while slot 1 shows LAND, FLARE or ROLL OUT.

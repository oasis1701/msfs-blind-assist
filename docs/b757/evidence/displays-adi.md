# The ADI from memory: what plan 27 reads (plan 27, Task 1)

Measured 2026-11-16, MSFS 2024 (1.9.24.0), the BlueBird 757 parked at a KSEA gate, loaded cold and dark, BBS Nest
running, MSFSBA closed. Memory was read only (`PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ`, the probe's
ctypes reader, the module found by the published layout's anchor). Simulator writes, at the owner's word ("feel free
to tune the ILS and perform any actions you like with this aircraft"): the battery, STBY power to AUTO, the APU
started, the three IRS selectors to NAV, the ILS panel tuned to 110.30, the Captain's F/D on. Function numbers are
WAT function indices of this build; offsets are from the object's start. "From the code" marks what could not be
exercised on the ground.

- `BBS752.wasm`: 29,765,919 bytes, dated 2026-10-31, SHA-256
  `357292c941c563fd9d66878a96210ed16917e086547a45a2bf60f8d2d45efc3d`: the build of the earlier display plans.
- **The ADI has a speed tape.** The probe's function list (`findings.md` §4) named 25862 "GS, V1, INOP"; it draws a
  whole speed tape on the left of the ADI: airspeed, the target speed, V1, the `1`, `R` and `F` bugs, the speed
  limit bands, the trend arrow, Mach and ground speed. Plan 27's text did not know it.

## Step 1: the objects

- `pfd`: class `N3bbs3pfdE`, vptr **82028** (the FMA layout's `pfdVptr`). The Captain's is the first manager pointer
  with that vptr (manager 0xf1334 + 16, live 0x2e9430; the first officer's at + 24). `i32 pfd+88` is the side: 2
  the Captain's, 3 the first officer's (f8585). Virtuals: slot 3 25847 init, **slot 4 25854 the draw**, slot 6
  25893 the per-frame update (speed trend, the ILS deviation alert 25894).
- Init (25847) fills: `pfd+608` the `mcp` (system 37), `+612` the `afds` (17), `+616` the `tmc` (41), `+620` the
  **`instr_source_selector_panel`** of the side (54 Captain, 55 first officer), `+624` `notify`, `+628` the `fms`
  (34), `+632` `flight_controls` (90). Four simulator variables by handle: `pfd+480` `PLANE BANK DEGREES` (Radians),
  `+512` `PLANE PITCH DEGREES` (Radians), `+544` `GROUND VELOCITY` (Knots), `+576` `AIRSPEED MACH` (Mach). These
  handles hold no cached value (unlike an L:var handle), so the reader takes the four as simulator inputs (S10).
- The source selector `S = [pfd+620]`: `[S+712]` the selected `irs` (getter 44756), `[S+716]` the `adc` (44757),
  `[S+740]` the `ra` (44761). `u8 S+748` is read and cleared by the draw: a one-second blank when the instrument
  source changes (as on the EHSI).
- A device is "failed or unpowered" (f73997) when `u8 dev+5` is set or `u8 dev+4` is clear. Used for the adc and
  the ra.

## Step 2: power

The shared gauge wrapper (23059) calls the draw only while `u8 pfd+4` = 1 (powered), `u8 pfd+5` = 0 (failed) and
`u8 pfd+64` = 0 (timed blank), as on the EHSI. Live: cold and dark 0, 0, 0 (dark); battery alone 0 (the adc and irs
power up, the pfd does not); with the APU on line 1, 0, 0 and the picture drew.

## Step 3: what the draw draws

25854 calls, in order: 25856 the FMA (plan 23), 25857 the localizer and glideslope scales, 25858 the horizon,
25859 the pitch limit indicator, 25860 the rising runway, 25861 the aircraft symbol, 25862 the speed tape, 25863
the `G/S` and `LOC` flags, 25864 the decision height, 25865 the radio altitude, 25866 the `ATT` flag, 25867 the
flight director bars. Screen places are fractions of the picture's width W and height H.

### Attitude (center)

- Valid (the draw's `u8 pfd+662`, f23414 on the irs): `i32 irs+3428` = 5 **and** `u8 irs+3040` **and**
  `i32 irs+3420` = 4. While valid the horizon is drawn: bank = `PLANE BANK DEGREES` × 57.2958, pitch =
  `PLANE PITCH DEGREES` × 57.2958 (f23610), the simulator's own signs (pitch positive nose down, bank positive
  left). The horizon stops moving below 18° nose up (pitch < −18 is drawn as −18).
- **`ATT` flag** (25866): while not valid and `i32 irs+3420` ≠ 3. Amber `ATT` in an amber box at (W × 0.46,
  H × 0.38). While the IRS aligns (`irs+3420` = 3) the center is blank: no horizon and no flag.
- Live: cold and dark `irs+3420` 0, `+3428` 0; aligning 3 with `+3428` going 1, 2, 4; the capture while aligning
  showed the bank scale, the aircraft symbol, no horizon and no flag. The alignment waited at 4 until the present
  position was set on POS INIT (GPS POS copied into SET IRS POS on the Captain's CDU); then `irs+3420` 4, `+3428`
  5, `u8 irs+3040` 1, and the capture showed the horizon (pitch 0.53, bank −0.40 in memory: on the aircraft
  symbol), `GS 0` at the bottom left and `DH200` in green above the radio altitude.
- The bank scale and pointer (25881, 25878) and the aircraft symbol (25861) are fixed drawings.

### Flight director bars (center, 25867)

- Drawn while the attitude is valid and the side's F/D byte is set (f30471: `u8 afds+11957` Captain, `+11958`
  first officer: the FMA layout's `fdCaptain`).
- Pitch command `f64 afds+15848` (f30536), roll command `f64 afds+15856` (f30537), degrees. The pitch bar sits
  (command + pitch) × scale above the aircraft symbol, pitch in the simulator's sign: **the bar is command −
  nose-up pitch degrees above the symbol**. The roll bar sits (bank − roll command) × scale × 0.1 right of center.
- Live, F/D on at the gate: `afds+11924` 1 (`TO`), `afds+11932` 8 (`TO`), `afds+15848` **7.941** (the take-off
  pitch command, 8° nose up: positive is up), `+15856` 0.000. With the attitude valid the capture showed the
  magenta pitch bar just under the 10° line (8.000 in memory) and the roll bar on the center.

### Speed tape (left, 25862)

- **`SPD` flag:** when the adc is failed or unpowered (f73997): amber `S`, `P`, `D` stacked in a box; nothing else
  of the tape, the ground speed and Mach included, is drawn.
- **Airspeed:** `f64 adc+368+24` (f30362 returns adc+368), shown never below 30 (the box's rolling digits and the
  tape; the tape runs 30 to 450). Live: 2.5 kt in memory, the box read 30.
- **Target speed** (magenta): its source by the AFDS pitch mode `i32 afds+11924` (the FMA's pitch field):
  - not 6 (`VNAV PTH`) or 7 (`VNAV SPD`): the MCP's `f64 mcp+872` (f22139);
  - 6 or 7 with `u8 afds+15584` set (f30636: speed intervention): the MCP's too;
  - 6 or 7 otherwise: the FMC's target, through the active route's path object (f39247, f37385, f24303 a
    waypoint speed restriction, else `f64 P+3944`, else `f64 afds+11408`). **Not read** by this plan: f24303's
    lookup was not traced, and none of it can be exercised on the ground.
  - Drawn as a bug on the tape while within 41 kt of the airspeed shown; else as a number above the tape (target
    more than 41 above) or below it (more than 41 below). Live: `mcp+872` 200, airspeed shown 30: `200` in magenta
    above the tape.
- **V1** (`i32 fms+1664`, f40828), **VR** (`i32 fms+1668`, f25891), and `u8 fms+38132` (f25886):
  - V1 = 0 and the byte set: amber `V1` over `INOP`. Live: 0, 0, 1 and the picture showed it.
  - else, while the AFDS timer (below) reads under 120 s and V1 is more than 41 kt above the airspeed shown: green
    `V1` and its number at the top of the tape.
  - Bugs on the tape while the timer reads under 120 s: `1` at V1 (if not 0) and `R` at VR (if not 0), both
    hidden while the `F` speed is not 100 and VR is within 4 kt of it.
- **`F` bug:** the maneuvering speed for the flap setting, `f64 afds+16184` (f30541), drawn while it is not 100,
  the adc's altitude `f64 adc+368+32` is at most 20,000 ft and the timer reads at least 10 s. The AFDS computes it
  (30467) from the FMC's VREF 30 (f30479) by flap setting: up + 80, 1 + 60, 5 + 40, 15 and 20 + 20, 25 the FMC's
  VREF 25 (f30480), 30 VREF 30. Live at the gate: 80.0 (VREF 30 not yet computed), not drawn (timer 0).
- **The AFDS timer** (f30542, f9592): a stopwatch at `afds+15744`: `f64 +0` the time kept, `i64 +8` the start,
  `u8 +16` stopped. Its value is `+0` while stopped, else `+0` + (the clock `f64 0x1244E8` (1197288) − `+8`).
  Live at the gate: 0, 0, stopped. From the code it runs from lift-off.
- **Mach** (25890, bottom left): `AIRSPEED MACH`; `----` below 0.40 (once shown it stays until 0.38); else three
  decimals without the leading zero (`.780`). Live: `----`.
- **Ground speed** (bottom left, only while the attitude is valid): `GS` and `GROUND VELOCITY` as a whole number.
- **Not read:** the speed limit bands (25887 to 25889: the maximum speed `f64 adc+368+120`, live 270 with the gear
  down, and the minimum speeds from `flight_controls`' two channels at +8368 and +8624, drawn from 10 s after
  lift-off) and the trend arrow (`f64 pfd+440`, computed by the update 25893).

### Localizer and glideslope (bottom and right, 25857, 25863)

The PFD's ILS block `g = pfd+136` (getter 23560), written by ILS receiver L (44743, 44746) whether or not the
display draws:

| Field | Meaning |
|---|---|
| `f64 g+0` | the localizer pointer, −1 to 1 of the scale's half width |
| `f64 g+8` | the localizer deviation in dots, clamp(−deviation ÷ (width ÷ 4), ±2.2) |
| `f64 g+16` | the glideslope pointer in dots, ±2.2, positive up |
| `u8 g+32` | the localizer scale is drawn |
| `u8 g+33` | the glideslope scale is drawn |
| `u8 g+34` | the localizer pointer is valid |
| `u8 g+35` | the glideslope pointer is valid |
| `u8 g+36` | the expanded localizer scale (rectangles instead of dots) |
| `u8 g+37` | the `LOC` flag |
| `u8 g+38` | the `G/S` flag |
| `u8 g+39` | the self-test (amber scales) |

- **Localizer:** normal scale (`g+36` 0): `g+0` = clamp(−deviation ÷ (width ÷ 2), ±1), so **dots = `g+0` × 2**,
  pinned at 2 dots. Expanded (`g+36` 1, set while the deviation is at most 0.625 dot): `g+0` = clamp(−deviation ÷
  (width ÷ 8), ±1), so **dots = `g+0` × 0.5**. Positive is the pointer right of center (fly right), as on the
  EHSI.
- **Glideslope:** the pointer at `g+16` dots, positive up (fly up), pinned at ±2.2 (drawn at the outer dot).
- **Flags:** amber `LOC` in a box at (W × 0.46, H × 0.926) while `g+37`; amber `G`, `/`, `S` stacked in a box at
  (W × 0.908, H × 0.41) while `g+38`. From the code (the receiver unpowered or failed with a frequency set, and
  the first three seconds of the self-test).
- **Live:** parked, no frequency: flags all 0, nothing drawn. 111.10 (no station): `g+32` 1, `g+33` 1, pointers 0;
  the scales alone. 110.30 (ISNQ, received on the ramp): `g+0` 1.000, `g+8` 2.200, `g+16` 2.200, `g+32` to `+35`
  1, `g+36` 0: the capture showed the localizer pointer on the right outer dot and the glideslope pointer at the
  top of its scale.
- **Rising runway** (25860, from the code): replaces the localizer pointer while the attitude is valid, `g+36`,
  `g+32` and `g+34` are set, the AFDS roll mode `i32 afds+11932` is 3 (`LOC`) or 4 (`ROLLOUT`), and the ra is
  valid and reads at most 2,500 ft. It moves sideways with `g+0` and rises to the aircraft symbol from 200 ft.
- **Not read:** the deviation alert (25894: the scale turns amber and the pointer flashes below 500 ft radio
  altitude in `LOC`, `ROLLOUT`, `G/S` or `FLARE`, `u8 pfd+656` to `+658`): state kept by the display's own update.

### Decision height and radio altitude (right, 25864, 25865)

- **Radio altitude** (f46419): `f64 ra+2920` − 16.93; while `u8 ra+2868` (its self-test) with `i32 ra+344` 2 or 4,
  `f64 ra+2880` instead, and with 3 the sum of both. Truncated to a whole number; from 100 up rounded to the
  nearest 10. Drawn while at most 2,500, white, at (W × 0.7941 + 5, H × 0.75 + 35). Live at the gate: `ra+2920`
  10.56, so −6.37: the picture showed `-6`.
- **`RA` flag:** the ra failed or unpowered: amber `RA` in a box in the number's place.
- **Decision height:** drawn while `u8 g+46` (pfd+182) and the attitude is valid: `DH` and `i32 g+40` (pfd+176),
  written by the EFIS control panel's object (36232; `g+46` is clear while the knob's value is negative). Live:
  200, 1; not drawn while the IRS aligned.
- **`ALT`** (white, above the DH): while the radio altitude is 500 to 2,500 ft and the adc's vertical speed
  `f64 adc+368+56` is −150 ft/min or less. From the code.
- **Not read:** the decision height alert (the DH turns amber and flashes for three seconds once the radio
  altitude passes it descending; its timer `pfd+664` and blink byte `pfd+720` are kept by the draw). BlueBird
  plays its own minimums callout.

### TCAS resolution advisory (center, 25858, from the code)

The TCAS object (46527 to 46529) writes both PFDs' blocks: `f64 g+56` not 0 with `f64 g+72` a pitch angle: a red
outline whose top edge is at that pitch, opening downward (keep the pitch above it); `f64 g+64` not 0 with
`f64 g+80`: its bottom edge at that pitch, opening upward (keep the pitch below it). Both cleared when the
advisory ends. Drawn only while the attitude is valid. Never seen live.

### Pitch limit indicator (center, 25859): not read

Drawn while the attitude is valid and `L:BBS757_PLI_VISIBLE` (handle 1009744) is not 0, at
`L:BBS757_PLI_PITCH_LIMIT_DEG` (handle 1009816), both written by `flight_controls` (35712). Live at the gate: 0 and
0. The draw places it at −(limit − pitch) × scale, the opposite sign to the horizon's and the flight director's
use of the pitch, so the limit's sign could not be settled without seeing it drawn in flight. Left for a recorder
on a flight.

### The FMC's reference speeds (not drawn on the ADI; for Shift+1 to Shift+6)

The owner's word, 2026-11-16: "Please also move Shift+1 to 6 onto the FMC's VREF." The `F` bug's source is the
FMC's own VREF, in the fms (`[afds+11896]`, the same object as `[pfd+628]`): **`f64 fms+2128` VREF 30** (f30479),
**`f64 fms+2120` VREF 25** (f30480), and `f64 fms+2112` VREF 20 (not used). The FMC computes all three (39562)
from its gross weight (`f32 fms+1652`, pounds; a trial weight typed on APPROACH REF at `+1660` while `u8 +1656`)
by its own table, for 134,000 to 251,000 lb, once PERF INIT has a fuel and a zero fuel weight; otherwise all three
are 0. They are the numbers APPROACH REF prints.

- Live: before PERF INIT 0, 0, 0 (and the `F` speed 80.0). ZFW 150.0 typed on PERF INIT (GR WT 170.0): **VREF 20
  132, VREF 25 123, VREF 30 121**, and the `F` speed 201.0 (121 + 80, flaps up). The app's measured table gives
  121 and 123 at 170,000 lb: the same numbers.
- The maneuvering schedule is BlueBird's own (30467): VREF 30 + 80 flaps up, + 60 flaps 1, + 40 flaps 5, + 20
  flaps 15 and 20: the schedule the app already used.

**The readouts:** while the ADI reader runs and its reading has a VREF 30 above 0, Shift+1 to Shift+5 say the
maneuvering speed from it ("Flaps 5 maneuver speed 161 knots") and Shift+6 says "VREF 30 121 knots, VREF 25 123
knots": no "calculated", since these are the aircraft's own numbers. Otherwise (the reader not running, the part
missing, PERF INIT not done, the weight outside the FMC's table) the readouts stay as they were: the measured
table by the simulator's weight, with "calculated", or "Flap speeds unavailable."

## Step 4: the reading (binding for Tasks 2 to 6)

**The layout's parts** (each optional but the first; a part the recipe cannot derive is null with its reason):

| Part | Fields |
|---|---|
| Core (required) | anchor, manager base and span, `pfdVptr`; `powered` 4, `failed` 5, `blanked` 64; `pfdToSource` 620, `sourceToIrs` 712, `sourceToAdc` 716, `sourceToRa` 740; `devicePowered` 4, `deviceFailed` 5 |
| Attitude | `irsAttitude` 3040, `irsMode` 3428, `irsModeNav` 5, `irsState` 3420, `irsStateValid` 4, `irsStateAligning` 3; word `ATT` |
| Speed | `adcBlock` 368, `adcAirspeed` 24, `adcAltitude` 32, `minAirspeed` 30; `pfdToMcp` 608, `mcpSpeed` 872; `pfdToAfds` 612, `afdsPitchMode` 11924, `vnavModes` [6, 7], `afdsSpeedIntervene` 15584; `pfdToFms` 628, `fmsV1` 1664, `fmsVr` 1668, `fmsV1Inop` 38132; `afdsTimer` 15744, `timerStart` 8, `timerStopped` 16, `clock` 1197288, `bugSeconds` 120, `flapSpeedAfterSeconds` 10; `afdsFlapSpeed` 16184, `noFlapSpeed` 100, `flapSpeedMaxAltitude` 20000, `flapSpeedNearVr` 4; `tapeHalfRange` 41; `machShownFrom` 0.4, `machShownUntil` 0.38; words `SPD` (its three letters), `V1`, `INOP`, `1`, `R`, `F`, `GS`, `----` |
| ILS | `ilsBlock` 136, `locPointer` 0, `locDots` 8, `gsPointer` 16, `locScale` 32, `gsScale` 33, `locValid` 34, `gsValid` 35, `expanded` 36, `locFlag` 37, `gsFlag` 38, `test` 39; `afdsRollMode` 11932, `runwayModes` [3, 4], `runwayBelow` 2500; words `LOC`, `G/S` (its three letters) |
| Radio altitude | `raAltitude` 2920, `raOffset` 16.93, `raTest` 2868, `raTestState` 344, `raTestAltitude` 2880, `raTestAlone` [2, 4], `raTestAdded` 3, `raMaxShown` 2500, `raRoundFrom` 100; `dhValue` 40 and `dhShown` 46 in the ILS block's object (so from pfd: 176 and 182); `adcVerticalSpeed` 56, `altDescent` −150, `altFrom` 500; words `DH`, `RA`, `ALT` |
| Flight director | `fdCaptain` 11957, `fdPitch` 15848, `fdRoll` 15856 (in the afds, through `pfdToAfds`) |
| TCAS | `tcasAbove` 56, `tcasBelow` 64, `tcasAbovePitch` 72, `tcasBelowPitch` 80 (in the ILS block's object) |
| Reference speeds | `fmsVref30` 2128, `fmsVref25` 2120 (in the fms, through `pfdToFms`); in the reading whatever the display's power, never a row |

The core also holds `pfdToMcp` 608, `pfdToAfds` 612, `pfdToFms` 628, `ilsBlock` 136 and `adcBlock` 368, which
several parts need; the parts' own offsets are relative to those objects.

**Simulator inputs (4):** `PLANE BANK DEGREES` (radians), `PLANE PITCH DEGREES` (radians), `GROUND VELOCITY`
(knots), `AIRSPEED MACH` (mach): stock variables, never L:vars of those names.

**Lit:** `powered` set, `failed` and `blanked` clear.

**Corrupt:** a mode or state value below 0 or above 255; a speed, altitude, deviation or command that is not
finite or beyond 1e6 in size; a decision height below 0 or above 5,000. Checked only while the display is lit
(nothing is drawn while it is dark), the pointers' structure always.

**Rows**, in this order, each starting with where it sits; a row is absent when its item is not drawn:

1. `Left: SPD flag` (then no other Left row), else:
2. `Left: airspeed 250` (never below 30)
3. `Left: target speed 200` (only while the MCP is its source)
4. `Left: V1 INOP` (V1 0 and the byte set); else, while the timer reads under 120 s: `Left: V1 135` (V1 not 0,
   and either the bugs are not hidden or V1 is more than 41 kt above the airspeed shown, where the tape prints
   its number) and `Left: VR 140` (VR not 0 and the bugs not hidden). The bugs are hidden while the `F` speed is
   not 100 and VR is within 4 kt of it.
5. `Left: flap maneuvering speed 210` (the `F` bug)
6. `Left: Mach .780` (absent while the tape shows `----`)
7. `Left: ground speed 250` (only while the attitude is valid)
8. `Center: ATT flag`, or `Center: no attitude` (blank while aligning), or
   `Center: pitch 2.5 up, bank 10 left` (one decimal for the pitch, whole degrees for the bank; `level` for a
   pitch that rounds to 0.0 and `wings level` for a bank that rounds to 0)
9. `Center: flight director, pitch bar 7.4 above, roll bar 3 right` (`centered` within 0.05 and 0.5; the roll in
   degrees of bank)
10. `Center: TCAS, keep the pitch above 5.0 up` and/or `below`
11. `Center: rising runway`
12. `Right: G/S flag`, or `Right: glideslope, pointer 0.5 dots up` (`centered` within 0.05; `at full scale, up`
    at 2.2), or `Right: glideslope scale, no pointer`
13. `Right: ALT`
14. `Right: DH 200`
15. `Right: RA flag`, or `Right: radio altitude 150` (absent above 2,500)
16. `Bottom: LOC flag`, or `Bottom: localizer, pointer 1.2 dots right` (`centered`; `at full scale, right` when
    pinned on the normal scale; `, expanded scale` appended while expanded), or
    `Bottom: localizer scale, no pointer`

**Spoken by itself** (Ctrl+M row "ADI messages"): only the five flags, `ADI flag: ATT`, `ADI flag gone: ATT`
(`SPD`, `LOC`, `G/S`, `RA` alike). Nothing else changes often enough to be anything but chatter, and nothing here
repeats a callout BlueBird plays (its minimums, altitude, windshear and terrain aurals).

Numbers are formatted with the invariant culture (CORE-17's spirit; tests under de-DE and sv-SE).

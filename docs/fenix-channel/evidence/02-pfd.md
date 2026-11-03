# Plan 02 evidence: the PFD

Measured on 2026-11-03 on Fenix build 3.0.0.7613, MSFS 2024, an A321 with IAE engines. On the ground at KSFO gate D1 (Claude,
with the aircraft Robin handed over: batteries and APU, IRs NAV, MCDU through the gateway's key writes, panels with the app's
own L:vars and counter steps). In flight EDDF 07C to EDDF ILS 07R: Robin flew the take-off, the climb and the approach to an
autoland; Claude flew the cruise points with AP1 on (12:08 to 12:17). Raw fields came from the team's local channel tools
(a push every 500 ms), the PFD from captures of the sim window (every 4 s, and rapid bursts), the verdicts from
`fenix_channel.log` of a running app (plan 01's build). The sim's own values were read beside them.

## The fields ([FNX-23])

Every number Task 1 added, read fresh from the installed `FenixDisplay.exe`, stands: `Remote` 28, 46, 74, 82, 98, 100, 107, 113,
117, 125, 127, 155, 157; `AdirsData` 1, 2, 4, 5, 6; `AdirsAvailData` 1, 2, 8; `IrAvailData` 1, 2; `AdrAvailData` 1, 2;
`PfdLsInfoData` 1, 2, 3; `ApproachLabelData` 1, 2, 3; `LsInputData` 1, 2; `FcuData` 4, 5, 9; `PfdFlagsData` 1, 3;
`SpeedLimitsData` 1, 2, 3, 5; `SpeedsData` 1 to 7. Shapes seen live as the plan has them, 155 included (fixed64, absent until
set). Two more, used since this evidence: `AltStatusData.Metric` 122.3 (varint bool) and `dhMessage` 205 (varint bool).

## Baro (Task 2 Step 1): outcome A

| Setting | 1.10 STD | 1.11 inHg | 1.14 | 1.15 | L:vars | 28.4 / INDICATED | Text in 105 | Drawn |
|---|---|---|---|---|---|---|---|---|
| Inch, as loaded | - | set | absent | 29.9139 | QNH 1, MODE 0, HPA 1013, INCH 29.91389 | -121.0 / -112.0 | `QNH c29.91` | QNH 29.91 |
| STD | set | set | absent | 29.9139 | QNH 0 | -115.1 / -106.0 | `  fcSTDyf` | STD, cyan, in a box |
| hPa 1029 | - | - | 1029 | 30.3864 | QNH 1, MODE 1, HPA 1029 | 310.2 / 322.7 | `QNH c1029` | QNH 1029 |
| hPa 1013 | - | - | absent | 29.9139 | HPA 1013 | -121.8 | `QNH c1013` | QNH 1013 |
| 29.80 | - | set | 1009.1432 | 29.8000 | MODE 0, INCH 29.80 | -223.1 / -216.3 | `QNH c29.80` | QNH 29.80 |
| 29.92 | - | set | 1013.2069 | 29.9200 | | -116.2 / -114.0 | `QNH c29.92` | QNH 29.92 |

122.6 follows STD (122.7 too). 1.7 was never set.

- **Preselect: left unused.** The knob turned twice at STD: 1.12 never set, nothing drawn beside STD, and QNH came back unchanged.
- **Metric: `Efis.Meters` left unused.** "FCU Metric Alt" never set 1.5. It set 122.3, exactly while the PFD drew a metric altitude
  in green under the baro box: "-40 M" at -115 ft and "100 M" at 341 ft, so a step of 10 m. (A cyan "30 M" above the attitude
  is the selected altitude.)
- **A negative altitude** draws NEG beside the tape.

## Speeds and the target (Task 2 Step 2, Task 3 Step 1): outcome A

- **On the ground:** V1, VR, V2 numbers once entered (141, 143, 147 drawn as the off-scale "141"), F, S, green dot and every 125 member NaN.
  VFE next by lever 0 to FULL: 235, 215, 195, 190, NaN. The tape stops at 30 knots; 28.5 is absent at rest and about 7 knots in the wind.
- **113:** the target is drawn with 113.5 true: "100" cyan at power-up, 113.4 100. Speed push: 113.9 set some seconds later, "147" magenta, 113.4 147 (V2).
- **In the air (EDDF, F 151, S 194):** V1, VR and V2 are numbers (149, 158, 158) until lift-off's phase change (12:01:41), then F 151 with VFE next 195; S 194 with 215; green dot 213.6 with 235. Approach: S 190 with 215; F 148 with 195; F 148 with 190; FULL all NaN. Each drawn on the tape as the captures show (124043, 124235, 124908).
- **125:** NaN on the ground; from lift-off VMAX 215, VLS 128, alpha prot 105.6 with flaps; clean at FL120 VMAX 280, VLS 206, alpha prot 179, each drawn. VSW NaN throughout (normal law).

## Air data flags (Task 2 Step 3): outcome A

| Press | 46 | Drawn | Verdict |
|---|---|---|---|
| ADR 1 off | 46.2.1, .2, .3 clear, 46.8 clear | red SPD, ALT, V/S; Mach text `rMACH` | PFD trusted |
| IR 1 off | 46.1.1, .2, .3 clear, 46.8 stays | red ATT, HDG (the heading flag in a box) | PFD trusted; 194.1 stays 345.47 |
| Aligning, at power-up | 46.1.2 clear | red HDG alone | not logged: the app was not running yet |

The IR 1 press also sets 28.3 and 28.9 to the negated magnetic variation. A letter in the Mach line: `r`, with ADR off: the row is gone under the SPD flag.

## Texts (Task 2 Step 4, Task 3 Step 5)

- **Mach:** blank on the ground, `g.469` at FL120, as drawn. The Mach row flipped 42 times between 12:08 and 12:28 around M.48, and the PFD drew no Mach in capture 121631: the row follows the text the channel sends.
- **Radio altitude:** `a0`, `a-1`, `a220`, amber as drawn; blank above 2,500 ft.
- **LS information:** on the wire with LS on or off (the plan gates it on `Ls` either way); drawn with LS on only: `mIGWQ`, `m111.70`, `m1s.0l  scNM` ("1.0 NM").
- **Approach labels 100, marker 117.1:** blank through both flights, so their rows are unmeasured and only show if the channel sends text. **LS flag 117.3:** blank in the first flight, where LS was on for the approach; `ax1ILS` in the second (below).
- **The FMA's Approach column** on the ILS: slot 3 `CAT3`, slot 8 `SINGLE` then `DUAL`, slot 13 `BAROdc⠀  700` or `RADIOdc⠀⠀ 200`, drawn "RADIO 200", white and cyan.
- **FMA engagement:** slot 9 `w1   2`, `w1   -`, `w-   2`, blank with both off, drawn "1 FD 2", "1 FD -", "- FD 2". Slot 11 `CHECK SPEED MODE` with no letter. Slot 6 ` cG/S`.

## Field 155 is not the setting

700.0 with BARO 700. With RADIO 200: -701.76 at FL150, -124.87 at a radio altitude of 200 and a baro altitude of 518, moving with the aircraft. Absent with none set. Whatever it is, it is not a figure a pilot set, and slot 13 already says it, so the app does not read 155.

## Vertical speed (Task 3 Step 2): feet per minute

28.6 -999.9 beside the sim's -995.6 in a V/S descent; +1786.8 beside +1781.4 in a climb. Factor 1, climb positive.

## Bank (Task 3 Step 3): positive is right

28.2 +16.8 turning right (heading 64 to 104); -20.1 turning left. SimConnect's PLANE BANK and PLANE PITCH have the opposite signs here.

## Speed trend (Task 3 Step 4): outcome A

The arrow ends at 28.5 plus 98: 240.05 with -13.84 drew a tip at about 226. No arrow at 1.19 or less; sometimes from 1.40; always from 2.43. 2 knots stays the threshold.

## Deviations (Task 3 Step 5): positive is right and above

82.1 +2.35: half diamond at the right end of the scale; +4.0 at a gate: the same. 82.2 +2.87: half diamond at the top; +1.18: diamond above center, climbing to it from below. NaN (82.2 on the ground): no diamond. The stock NAV CDI:1 and GSI:1 read 0: the Fenix does not feed them. A value past the scale's end is not clipped on the wire. The scales have two dots on each side, so the rows say "full scale" from 2.0 and give no figure (`FullScaleDots`). In flight 82.1 read a fixed 4.0 from 12:35:44 to about 12:40; below about 50 ft 82.2 runs 0.22, 3.98, -3.33, NaN, -3.08, 2.08 within seconds while the drawing stays near center (124924): the row follows the wire.

## The other flags (Task 3 Step 6)

Seen set during the flight and captured:
- 205 `dhMessage` from a radio altitude of 200 (12:49:07.968) to landing clean-up, 206 `dhMessageFlashing` from 12:49:08.5: amber "DH" above the radio altitude. Not in the plan's list; read the same way.
- 122.8 `AltAmber` over the same stretch: the altitude readout amber. No words of its own, so no row.
- 122.3 `Metric`: above.

Seen set with no capture: 118.1 and 118.2 (`FdFlag`, `FdFlagFlash`) for 2 s at 12:50:02.5, stopped on the runway. Never set in this flight: 93, 94, 96, 115, 120, 122.1, 122.2, 122.4, 122.5. 114 was set once, cold and dark, with no PFD drawn. The second flight (below) saw 114, 120, 122.1 and 122.2 set.

## The flight director (Task 2 Step 5, Task 3 Step 7)

**Outcome B on the unit, ruled by Robin on 2026-11-03: the row says where the bars are.** 110.1 and 110.3 hold the commanded pitch attitude and the commanded bank, not the bars' positions. Level at FL120: 110.1 2.79 with a pitch of 2.78, bars centered. A heading turn: 110.3 25.0 with a bank of 16.8, the roll bar drawn right; 110.3 -25.0 with -20.1, the bar left. The take-off roll: 110.1 9.5 with a pitch of 0.46, the pitch bar 9 degrees up on the scale. V/S -1000: 110.1 0.82 with 1.93, the bar just under the symbol. So a bar sits at the value minus the attitude, positive up and right.

- **The FDs:** 110.5 and 110.6 follow the buttons. No captain's bars while 110.5 is false, on the ground or in cruise. All numbers can be present then.
- **Not drawn:** 110.1 and 110.3 NaN on the ground with no mode, even lined up, with both FDs on.
- **The yaw bar:** a green rectangle under the symbol, drawn exactly while 110.8 `RollGuidance` is set: on the take-off roll (RWY) and from a radio altitude of 40 ft to the end of the roll-out. It sits at 110.4 minus the magnetic heading: 67.39 with 64.91 draws it right. 110.4 alone says nothing: it holds a number in cruise with an ILS tuned (no bar), and stays NaN for the last 3 s of a drawn bar, after the ILS was cleared.
- **Inhibits:** 110.11 and 110.12 together at touchdown: the pitch and roll bars go; the yaw bar stays. 110.11 alone was set from 12:01:07.2 to 12:01:41.2 on the take-off roll, with no roll bar drawn (120121); 110.12 was seen only with it, at touchdown. 110.10 never seen set.
- **TRK/FPA:** 110.7 set, the bars gone, the bird and its flight path director drawn in their place while 110.1 and 110.3 stay numbers. So no row.
- **The flash:** in OP CLB with AP1 on, the FCU altitude turned below the aircraft: the FMA reverts to V/S at once, and 5 s later 110.9 sets for 9.6 s (12:13:25.4 to 12:13:34.6; again 12:14:55.7 to 12:15:04.9). Rapid captures: the bars on about 0.5 s and off about 0.5 s while it is set. 110.9 was also set 12:02:13 to 12:02:22 and 12:11:09 to 12:11:19, with no `p` in the FMA, and those two have no captures; `p` came with the two V/S reversions.

## New letters: `p` and `d`

Both make their display untrusted while unknown, and both now join `FenixChannelText` as attributes that are never spoken (Robin, 2026-11-03); `d` also puts a space between the words either side of it, since speech merges stretches by color alone.

- `p`: FMA slot 1 `bgpV/Scp+1800` for 10 s with each of the two V/S reversions, when the FD flash was set too ("PFD not trusted: unknown code 'p' in the FMA", 12:13:25.8 to 12:13:35.8, and 12:14:55.8 to 12:15:05.8). The flash was set twice more with no `p` (above). The text stays drawn in both phases of the bars' flash. Also E/WD `pgIR IN ALIGN 1 MN` ("E/WD not trusted", 11:25:47).
- `d`: FMA slot 13, for as long as a minimum is shown ("PFD not trusted: unknown code 'd' in the FMA", 12:17:03.8 to 12:49:59.8). Plan 01 read in the program's code that `d` only moves the drawing position right and draws nothing.
- `x1`: below, under the second flight.

## Seen in passing

- **Each press of an FD button** makes the PFD "not trusted: FD captain disagrees" for about a second (11:28:27.9 to 11:28:28.9; three times), since the lamp comes at 1 Hz and the channel at once. The gate behaves as designed, and the window shows its status row for that second.
- **The E/WD's fuel check latched on the landing roll:** "E/WD not trusted: fuel disagrees: channel 1,715 kg, sim 1,609 kg" at 12:49:48.8, ground speed falling, then "fuel latched until reconnect" at 12:49:51.8, and the E/WD stayed untrusted until the app ended. 106 kg between the Fenix's FOB and the sim's total during heavy deceleration. For plans 04 and 12. The second flight latched the same way: 2,259 kg against 2,158 kg at 17:53:00, latched at 17:53:03.

## The second flight (Task 9)

Same build and aircraft, 2026-11-03, 16:40 to 18:02: EDDF 18 with no ILS tuned, an ILS approach to 07R at BARO 700, a go-around from a low point of 598 ft (radio altitude 270), and a second approach to an autoland. Robin flew and pressed the keys; Claude flew the points from 17:02 to 17:08 with AP1 on, and worked the aircraft on the ground. The rows came from the open window through a local reader every second, beside the raw fields (500 ms) and captures (4 s). The app was the review's fixes; from 17:38 it also had `x1`, deployed in flight.

No PFD captures of the take-off: the camera sat on the pedestal view from before 16:38:41 until it was put back at 16:58. What moved it is not known; Shift+1 to Shift+6 at 17:02, with the PFD window in front, left it alone. So the roll bar on the ground was captured after landing instead.

- **Speed keys.** At the stand the lever was at FULL, left from the last landing (S_FC_FLAPS 4; the stock handle index said 2), so all six said "not available", rightly: 127.7 NaN, no VFE drawn. Lever 0: 127.7 235, Shift+6 "VFE next 235 knots". ADR 1 off: 127.7 still 235, rows SPD, ALT and V/S flag, Shift+6 "VFE next not available". Clean at 12,000 ft: green dot 213, S and F not available, VLS 206, alpha prot 180, VFE next 235, each its row. Flaps 1: S 193, VFE next 215. Flaps 2: F 149, 195. Flaps 3: F 149, 190.
- **Cursor.** Parked on Altitude from 16:51 through lift-off (about 16:53:14) to 16:54:38, the number climbing on Robin's braille display. One read in 82, 16:53:35.373, had the selection on the row above in a tick where that row changed too; the next was back. Had that been the state, `RowToFollow` would have followed Alpha prot from then on, so the read landed inside a refresh. The reader caught a half-shrunk list the same way at 17:06:03.668.
- **Take-off, no ILS, HDG pulled at line-up.** 110.8 stayed clear, so no yaw row; 110.11 stayed clear too, unlike the first flight's RWY roll. The row read "pitch bar 9.2 degrees up, roll bar 4.9 degrees right" at the line-up, numbers only (no capture).
- **Roll bar on the ground.** HDG pulled on the runway after landing, FCU 15 degrees right: 110.3 0.90, 110.1 NaN, row "Flight director: roll bar 0.9 degrees right". The capture (17:55:04) draws the roll bar alone, just right of center.
- **Turn and bars.** "bank 25 degrees right", bars centered, drawn so (17:04:15). Roll bar 10.9 right with bank 14 right (17:05:42). Pitch bar 1.1 up, roll bar 2.9 left, bank 22 left (17:07:12). Go-around: pitch bar 11.6 up with SRS, NAV and MAN TOGA in boxes, and "Glideslope diamond 0.9 dots below", the diamond about a dot below center (17:33:15). All on the sides drawn.
- **Vertical speed.** 1000 up beside the sim's 996 (17:04:55); 1400 down beside -1358 (17:05:52).
- **Target.** "Speed target 251 knots, selected" from Robin's pull at 16:55:26, then 250, the triangle cyan (17:04:15).
- **Flash.** OP CLB, FCU altitude turned below the aircraft: FMA "V/S green, +1000 cyan" at once; 110.9 from 17:05:08.3 to 17:05:18.1; the row ended ", flashing" from 17:05:09.2 to 17:05:18.8 and then not. No status row, nothing in the log.
- **FD buttons.** Captain's off, 17:05:58: one second of status row ("FD captain disagrees"), then no flight director row and "- FD 2", no bars drawn. Both off: no row, no FD in the Engagement row. Both on: restored. Later presses did not always trip the gate: 17:56:40 and 17:57:03 gave no status row at all, as the compare only sees the two disagree if it runs in that moment.
- **Radio altitude and approach rows.** "Radio altitude 2490, green" first at 17:30:08, amber below about 400 (410 green, 360 amber). "Approach: CAT3 SINGLE, BARO white, 700 cyan", then DUAL, as drawn. "FMA message: LVR CLB, white" at the go-around.
- **Deviations.** "Localizer diamond at full scale, left" with the half diamond at the left end, 82.1 -2.04 (17:26:51); "Glideslope diamond 1.2 dots above", drawn so.
- **BARO minimum.** 205 stays clear at BARO: it is the radio minimum's. 122.8 alone is set from 700 ft down (17:33:04.1 to 17:33:09.5; second approach 17:52:02.1 to 17:53:07.5), and the altitude digits turn amber (173307), with no message. The altitude row gives the number and no word for it, as rows the app builds speak no color. Slot 13 keeps showing the setting.
- **Power-down.** Engine masters off with the window open, batteries still on: 17:59:04 one row, "PFD: not available, display off". Alt+P the AI read with no words first, Shift+1 "Green dot not available" (Robin). The ISIS followed at 18:02:42 with the batteries.
- **The log.** One "PFD: " line in the whole session, the `x1` one below, from the build before it was known.

`x1`, a known code since 17:38. With the LS button off and the ILS approach armed, 117.3 holds `ax1ILS`: it came with the arming at 17:23:51 (slots 6 and 7 `G/S`, `LOC`), went with the disarming at 17:24:03, came back at 17:24:10 and went with LS on at 17:26:18; and again 17:45:20 to 17:48:10, LS on then too. The PFD draws an amber ILS beside the baro box (pt-f2-ils1). The frames show it on and off while the text stays: absent in every frame 17:45:33 to 17:47:17, present in every frame 17:47:21 to 17:47:57, at one frame every 4 s. While x was unknown this dropped the whole window to "PFD: not available, unknown code 'x' in the LS flag" until LS was pressed, the speed keys with it. The same code came in 105 passing the transition altitude with QNH still set (`x1QNH c30.36`, 16:55:14.9 to 16:55:20.3, climbing through about 5,000 ft) and passing the transition level with STD still set (`x1  fcSTDyf`, first flight, 12:30:04 to 12:30:11), each until the setting changed. All three are x with the digit 1 and nothing else was seen, so that alone is known: it draws nothing and its digit is no text (the row would otherwise read "1ILS"). A bare x, or another digit, still fails closed. With it known: all rows kept, "LS flag: ILS, amber" last, 17:45:20 to 17:48:10.

Flags seen set for the first time, with no row and no capture of what they draw: 122.2 in and out around each altitude capture (16:57:13.6 to 16:57:29.3, 17:04:49.4 to 17:05:02.9, and more); 122.1 from 17:05:19.7 to 17:05:55.3, climbing away from an FCU altitude below; 120.4 from 17:33:17.1 and 120.6 from 17:33:18.7, both to 17:33:20.9, in the go-around; 120.5 at 17:34:21.4 to 17:34:23.0 and 17:35:09.4 to 17:36:13.2; 120.1, 120.2, 120.8, 120.9 and 114.1 at the power-down, with 118.1 and 118.2, also seen at 17:53:11.3 to 17:53:13.4.

Unexplained, measured: "UI thread stalled 5.3 s" (17:05:58.8 to 17:06:04) and "5.1 s" (17:23:51.9 to 17:23:57): the list going from 17 or 18 rows to one status row, the cursor on the flight director row, the local reader attached, and the reader saw lists of 1, 10, 1, 3 rows in between. Not with the cursor on row 0 and 17 rows (17:06:23, reader attached), the bottom row of 10 with no reader (17:56:08) or with it (17:59:04). Robin noticed no freeze. `DisplayList.UpdateInPlace` removes rows one at a time, and it is shared with other aircraft. The case with no reader and 17 rows has not been run.

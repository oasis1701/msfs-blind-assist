# Fenix display channel for pilots: design

Branch `feat/fenix-display-channel` (shared, pushed to `origin`; no PR until every plan has landed). This spec is the
authority the plans in `plans/` argue from. A plan that disagrees with it is wrong; a gap neither answers is ruled by the
person running the plan and recorded in that plan's Execution record.

## 1. Goal and scope

Fenix A320/A321 pilots get exact, live readouts from the Fenix's own display channel instead of AI descriptions, and
new spoken announcements, without ever hearing a value the app cannot trust.

In scope:

- **Windows** for the five displays the Fenix reads through the AI today: PFD (Alt+P), ND (Alt+N), E/WD (Alt+E), SD
  (Alt+S) and ISIS (Alt+I), all in output mode. Each opens a live window when the channel can read that display, and
  gives the AI read otherwise.
- **Announcements**, each with its own row in the Announcement Monitor (Ctrl+M): FMA changes, E/WD warnings, E/WD
  cautions, E/WD memos and new DCDU messages.
- **Keys**: the speed tape on output Shift+1 to Shift+6, the next waypoint on Ctrl+W, distance to destination on D and
  distance to top of descent on Shift+D.
- **The DCDU**: the "DCDU Left" panel is removed and replaced by a DCDU window on input Ctrl+Shift+D.

Out of scope: the MCDU (it keeps its 8083 window), any write on the channel ([FNX-21]), an ND map list (the channel
draws none; a later plan may compute one from the route legs), and every aircraft other than the Fenix.

## 2. Decisions

Robin's answers of 2026-11-02 are in section 5; this section is what they mean for the code.

### 2.1 Data flow

- The channel client, trust gate and monitor already on this branch stay (`docs/a32nx.md`, "Fenix display channel";
  `docs/invariants/fenix-channel.md`). `FenixChannelMonitor` keeps the latest RemoteData message (the raw bytes of push
  field 1) beside the gate's frame, both replaced under the one lock; its snapshot hands a read the verdict, the gate's
  frame and that message from one instant ([FNX-22]).
- **Each feature decodes its own fields** from the snapshot's message, in its own files, with the existing
  `FenixChannelWire` reader. Its decoder checks the wire shape of every field it uses and the presence of the content it
  needs. A wrong shape or missing content stops that feature only: its window shows one status row, its keys say "not
  available", its announcer stays silent, and the monitor's log names the field once.
- **Announcements are polled** by one UI-thread timer every 100 ms (the PMDG 737 model), never driven from the channel's
  pool thread. Each announcer looks at the latest snapshot and compares it with what it saw last.

### 2.2 Trust

- The gate judges six displays: PFD, E/WD and SD (today) plus ND, ISIS and DCDU (new). A display is trusted only while
  the channel streams, the build is checked ([FNX-23]), every field the gate decodes has its shape, the display is live
  and its cross-checks pass.
- Display live: a powered display unit shows it (duData, field 12): PFD on PFDCapt (0), ND on NDCapt (2), E/WD on
  ECAMUpper (4), SD on ECAMLower (5), DCDU on DCDUCapt (9). The ISIS is live while its own `Powered` flag (field 182.1)
  is set.
- Cross-checks (answer 12, "fewer"): only the existing ones. PFD: both FD lights, true altitude, heading. E/WD: fuel. ND:
  the existing heading check, reused. SD, ISIS and DCDU have none: their display-live condition, the shape checks and the
  build check decide.
- Features use their display's verdict: the PFD window and the speed keys use the PFD; the ND window, Ctrl+W, D and
  Shift+D use the ND; the E/WD window and the E/WD alert announcer use the E/WD; the SD window uses the SD; the ISIS
  window uses the ISIS; the DCDU window and its announcer use the DCDU. The FMA announcer uses the PFD.

### 2.3 Safety work that lands before anything speaks (plan 01)

- **Stall guard** (deferred when the client was built, until the first read): a UI-thread heartbeat. While the UI thread has missed its
  timer for more than 1,000 ms, every comparison waits instead of agreeing or disagreeing (SimConnect values may be
  stale). Measured on 2026-11-02: the largest SimConnect delivery gap in a whole flight was 1.1 s.
- **Content conditions** (required of the first read when the client was built): a display needs its own content
  present, so an all-empty RemoteData can never read trusted. Each feature decoder enforces its own; the gate's display-live condition already
  blocks an unpowered screen.
- **Format letters**: a letter `FenixChannelText` does not know keeps its display untrusted ([FNX-24]). Box (`b`) and
  flash (`f`) are attributes that are never spoken until a capture confirms what they draw; `y` and `l` likewise. The
  FMA's `!` (as in "gALT!CRZ") is spoken as a space. The `d` letter (in the ND's ground speed text) is measured by plan
  03 before any read uses that field.
- **Session notice** (answer 10): one line per session, spoken when the Fenix loads and the build is not checked, the
  channel refuses, or no data arrives within 60 s of the Fenix being ready: "Fenix display channel not available for
  Fenix build <build>: displays use AI reading; FMA and E/WD announcements are off." Short gaps in a session stay
  silent.

### 2.4 Windows (answer 1)

- A display key opens its window only when that display is trusted at the press; otherwise it gives the AI read, with
  no extra words (the PMDG 737's silent fallback, WASM-29). Pressing the key again brings the window to the front.
- The window is one live list named "<Display> display", refreshed in place every second (the selection is kept),
  then any controls. Rows go top to bottom, left to right, in the display's own words ([FNX-24]; keep cockpit words as
  presented), with colors spoken after each stretch (2.6).
- When the display stops being trusted or a field it uses has the wrong shape, one status row replaces the content:
  "<Display>: not available, <reason>". Never stale rows. F5 refreshes now; Escape closes. One window per display, each
  in the Fenix windows list (Ctrl+Shift+F).
- Content:
  - PFD: the FMA by column (2.7); speed (airspeed, target, managed or selected, trend); the speed tape top to bottom
    (VMAX, VFE next, F, S, green dot, VLS, alpha prot or VSW; V1, VR and V2 while shown); Mach as displayed; altitude
    with its baro reference ("STD" or "QNH 1029"); vertical speed; pitch and bank; the flight director bars while
    drawn, with "flashing" while they flash (no row when none is drawn, as for every other row); heading and track;
    radio altitude, minimums and the ILS lines when shown; flags and messages.
  - ND: mode and range; ground speed, true airspeed and wind; track; the next-waypoint line; the ND's messages.
  - E/WD: the engine block for the fitted engine family (N1 or EPR, EGT, N2, fuel flow); the thrust limit line ("CLB
    1.618"); fuel on board; flaps and slats; the warning lines (left) and memos (right).
  - SD: the page name, that page's content, the STATUS text on the STATUS page, and the bottom line (TAT, SAT, UTC,
    gross weight).
  - ISIS: speed, altitude, baro (STD or QNH), pitch, bank and slip, the bugs, ILS deviations when shown.

### 2.5 Keys (answers 5, 6, 9)

Key readouts speak plain values, never colors, and say "<name> not available" when their display is not trusted or the
value is not shown (NaN or absent).

| Key (output mode) | `HotkeyAction` | Reads | Example |
| --- | --- | --- | --- |
| Shift+1 | `ReadSpeedGD` | green dot (127.6) | "Green dot 237 knots" |
| Shift+2 | `ReadSpeedS` | S (127.5) | "S speed 215 knots" |
| Shift+3 | `ReadSpeedF` | F (127.4) | "F speed 180 knots" |
| Shift+4 | `ReadSpeedVLS` | VLS (125.2) | "VLS 189 knots" |
| Shift+5 | `ReadSpeedVS` | VSW (125.5) when it is a number (alternate law), else alpha prot (125.3) | "Alpha prot 162 knots" / "VSW 158 knots" |
| Shift+6 | `ReadSpeedVFE` | VFE next (127.7) | "VFE next 200 knots" |
| Ctrl+W | `ReadNDWaypoint` | the ND's next-waypoint lines (68) | "VAKSU, 318 degrees, 84 nautical miles, 17:29"; with no flight plan (PPOS) "No active waypoint" |
| D | `ReadDistanceToDest` | along-route distance to the destination, time from ground speed | "412 miles to destination: 01:02:30" |
| Shift+D | `ReadDistanceToTOD` | along-route distance to the top-of-descent marker | "96 miles to top of descent: 00:14:10", "Past top of descent", "Top of descent not available" |

D and Shift+D are computed (the channel has no figure for either), and checked in the sim against the MCDU's F-PLN
before they may speak; if they cannot match within the plan's tolerance they say "not available" rather than guess. The
sentences and the time format follow `BaseAircraftDefinition`'s `AnnounceDestFromSDK` and `AnnounceTODFromSDK` (time
only at 30 knots or more, as `FormatEtaFromDistance` does).

### 2.6 Speaking colors (answer 2)

Every channel text read in a window or an announcement goes through one speech function (plan 01): a one-color line ends
with its color ("ENG 1 FIRE, red"); a mixed line names each stretch's color after its words ("MAN FLX white, +56
cyan"); adjacent stretches of the same color merge; an empty stretch is skipped. Colors: amber, cyan, green, magenta,
white, red; yellow is spoken only once a capture confirms it. Sizes, box and flash are never spoken (the flight
director bars' flash is a flag, not text: plan 02's row says "flashing"). `!` is a space. A line with an unknown
letter is never spoken: its display is untrusted.

### 2.7 Announcements (answers 3, 4, 8)

Shared behavior:

- Each announcer checks its own Ctrl+M row before speaking and keeps tracking while muted (unmuting replays nothing).
- It is silent while the display it relies on is not trusted. Its first good reading after a start, a reconnect or a
  trust gap is taken silently as the baseline.
- Ctrl+M rows (all declared by plan 01, keys never owned by a SimConnect variable, the cabin rows' precedent):

  | Row | Default | Owner |
  | --- | --- | --- |
  | FMA changes | on | plan 08 |
  | Warnings | on | plan 09 |
  | Cautions | on | plan 09 |
  | Memos | off | plan 09 |
  | DCDU messages | on | plan 11 |

FMA (plan 08): the FMA is three rows of five columns (slot = row x 5 + column; columns Thrust, Vertical, Lateral,
Approach, and column 5 for AP, FD and A/THR engagement), to be confirmed in the sim. A change counts after it has held
300 ms. Spoken: "FMA: " and each changed column among Thrust, Vertical, Lateral and Approach, with its non-empty rows in
order and colors after each stretch, columns joined by "; " ("FMA: Thrust: THR CLB, green; Vertical: CLB, green"). Row 3
is spoken as "FMA message: <text>". Column 5 is never announced (the FCU lights already are). An armed mode becoming
active is heard once, as its column's new row 1.

E/WD alerts (plan 09): the ECAM entries stream decides what appeared and what cleared. Its entries with `IsAlert` set
are failure titles; entries named "MEMO-…" are memos; entries named "…-STS-…" are status items and never announced.
A new failure title is spoken "<title>, <color>", the color taken from that title's line on the E/WD (173): red
interrupts (`AnnounceImmediate`), amber does not; a title the E/WD is not showing is spoken without a color, never a
guessed one. A cleared title: "<title> cleared". Memos, when their row is on: "<memo>, <color>" from the E/WD's right
column (174). Action lines are never announced.

DCDU messages (plan 11): a new message is announced "DCDU: <header>, <first message line>".

### 2.8 The DCDU window (answers 7, 8)

- Opened with input Ctrl+Shift+D (the app-wide `ShowDCDU`, its gate in `MainForm.Hotkeys.cs` widened to the Fenix) or
  from the Fenix windows list. A Fenix without a DCDU (`FenixVariant`) answers "This Fenix has no DCDU."
- One list, "DCDU display" ([A320-15]): the header, the message lines, the info lines and the four soft-key labels by
  position ([A320-16]). Then a brightness control the pilot tabs to.
- Keys, as the A32NX window: Ctrl+1/Ctrl+2 the left soft keys, Alt+1/Alt+2 the right (F1/F2 and F7/F8 with the
  alternate key setting); PageUp/PageDown within the message, Ctrl+PageUp/Ctrl+PageDown between messages ([A320-17],
  [A320-18]); Ctrl+P PRINT; F5 refresh; Escape closes.
- A press is a full press-release of the existing DCDU variable ([FNX-6], [A320-3]). A soft key with no label answers
  "No action on that key." and sends nothing ([A320-19]); otherwise the result is heard through the refreshed list,
  never the key's name repeated ([CORE-7]).
- The "DCDU Left" panel and its controls leave the panel structure; their variables stay for the window.

## 3. Facts the design relies on

Field numbers are RemoteData field numbers on build 3.0.0.7613 unless stated. "Measured" means seen live; "inferred"
means from the contract or the cruise capture and still to be confirmed by the plan that uses it. Every field a plan adds
goes into `FenixChannelFields` as the app's own constant, with its wire shape in a comment, as the existing ones are.

### 3.1 Already read by the app

10 fwcPage, 11 fwcPhase, 12 duData (Power, Content), 110 fdData, 111 fma (16 slots), 153 FOB text, 173/174 upper
ECAM left/right, 194 heading, 197 true altitude, and the ECAM entries stream (Ata, Name, Title, IsAlert). See
`docs/invariants/fenix-channel.md` for what was measured on 2026-11-02 (true altitude, the light test, display power,
format letters, fuel, the FMA's take-off and engine-failure lines, the negative heading near north).

### 3.2 PFD (plan 02)

- 28/29 ADR, captain and first officer (nested; doubles): 1 pitch, 2 bank, 3 heading, 4 altitude (the PFD's baro
  altitude, equal to the ISIS's 182.13; follows the baro setting), 5 indicated airspeed, 6 vertical speed (unit to
  measure), 7 OAT, 8 TAT, 9 track, 10 wind direction, 11 wind speed, 12 acceleration, 13/14 pitch and roll rate, 15
  pressure altitude.
- 125/126 speed limits (doubles): 1 VMAX, 2 VLS, 3 alpha prot, 4 alpha max, 5 VSW (NaN in normal law), 6 VMAX
  protection.
- 127 speeds (doubles, NaN when not drawn): 1 V1, 2 V2, 3 VR, 4 F, 5 S, 6 green dot, 7 VFE next. Only green dot was
  populated in cruise: each plan measures when the others are drawn.
- 113 FCU: 1 target speed (below 1 means Mach), 2 selected heading, 3 altitude target, 4 target IAS, 5 bugs visible, 6
  selected vertical speed, 7/8 altitude strings, 9 speed managed, 10 heading managed, 11 altitude managed, 12 altitude
  magenta, 13 altitude acquire inhibit.
- 98 speed trend (double; 10 x acceleration, knots per 10 s).
- Text: 107/108 Mach ("g.775"), 105/106 baro ("QNH c1029"; "  fcSTDyf" at STD; "x1QNH c1029" in flight at QNH, the only
  `x` seen), 100/101 approach labels, 74/75 LS information, 157 radio altitude text, 155 minimums; 27 radio altitude
  (double, raw).
- Flags: 46/47, 114, 115, 117, 118/119, 120/121, 122 (6/7 QNH-STD, 9 vertical deviation), 93 stall, 94 alpha floor, 96
  vertical speed amber. Meanings to measure before any is spoken.
- FMA (111): measured lines include "wbMAN", "FLX c+56", "gSRS", "cCLB", "cNAV", "w1   2", "cA/THR", "wfLVR CLB",
  "wfLVR MCT", "aLVR ASYM", "CHECK SPEED MODE", "gALT!CRZ", "gTHR CLB".
- 110 flight director (nested; the gate already decodes it into the frame as `FenixFdState` and checks its shape): 1
  pitch bar, 2 flight path angle, 3 roll bar, 4 yaw bar (doubles; every bar was NaN, not drawn, at power-up with no
  mode engaged), 5/6 FD on, captain and first officer (the gate's cross-check), 7 TRK/FPA, 8 roll guidance, 9 flash, 10
  inhibit, 11/12 inhibit roll and inhibit pitch (varint bools). The bars' unit and signs, the yaw bar, TRK/FPA and what
  each flag does to the bars: to measure (plan 02).

### 3.3 ND (plans 03 and 10)

- 68/69 next-waypoint lines (captain, first officer): 1 ident and bearing ("VAKSU g318c"), 2 distance ("g84 csNM"), 3
  ETA ("g17:29"); "PPOS" with no flight plan. Measured in cruise against the MCDU's F-PLN.
- 194 heading: 1 magnetic heading (goes negative near north: -1.44 measured), 2 magnetic track, 3 magnetic variation;
  193 true track; 56/57 ND position (1 latitude, 2 longitude).
- 163/164 ground speed and wind text: 1 ground speed and TAS (`wsGSlgd456wsdTASlgd448`, with the unknown `d`), 2 wind
  ("g213w / g37").
- 1 EFIS (repeated, captain then first officer): 1 mode (enum; declared order ILS, VOR, NAV, ARC, PLN, to confirm), 3
  range, 4-9 option flags, 10 QNH-STD, 14-16 QNH values.
- 103/104 ND messages (repeated strings; "GPS PRIMARY" seen on the first officer's side only); 62-67, 87, 88, 102 TCAS and
  flag texts (unseen).
- Route (plan 10): 2/3 route data: 4 active route, 5 modified, 6 secondary, 7 alternate, 8 modified alternate, 9 offset
  base, 10 debug (repeated legs), 11 active leg, 12 map markers. Leg: 1 latitude, 2 longitude, 3 varint, 6 ident, 8
  turn side, 9 double, 10/11 courses in and out (meanings of 3 and 9-11 inferred). Map marker: 1 latitude, 2 longitude,
  3 label, 4 type (enum: TOD first, then TOC, EOD, SOC, Decel, ETP; the cruise marker had no type, so 0 = TOD is
  inferred), 5 color, 6 go-around, 7 remove on declutter. The MCDU's F-PLN shows "DEST UTC DIST EFOB" (the reference for
  plan 10's check).

### 3.4 E/WD (plans 04 and 09)

- 129 engines: 1 left, 2 right (nested): 1 limits (max EGT, EGT redline, end of band, amber line), 2 N1, 3 N2, 4 fuel
  flow, 5 EPR, 6 EGT, 7 nacelle temperature, 8 start valve, 9 starter pressure, 11/12 vibration, 22 engine label, 23-25
  oil quantity, pressure, temperature; then 3 EPR limit, 4 engine mode switch, 8 thrust limit text (["cCLB",
  "g1.618", ""]). 130 autothrust state. Engine family (CFM56, IAE V2500, LEAP, PW GTF) decides N1 or EPR first; its field
  is to be found by plan 04 (6.5 in the contract's features, inferred).
- 153 FOB text, 173/174 upper ECAM (repeated, may be null), 177 ECAM status flags, 149 flaps and slats (3 flap target,
  4 slat target), 152 flap text.
- Measured E/WD lines (2026-11-02): "uaELECu aGEN 1 FAULT", "c -GEN 1......OFF THEN ON", "w   .IF UNSUCCESSFUL:",
  "urENG 1 FIREu ", "w -AGENT1 AFTER 4S.DISCH", "uaENG 1u afSHUT DOWNf", "rLAND ASAP" then "aLAND ASAP",
  "a*AIR BLEED", "mT.O INHIBIT", "gSEAT BELTS".
- ECAM entries (measured): "01-24-  1" "ELEC-GEN 1 FAULT" title "ELEC GEN 1 FAULT" alert; "01-26- 10" "FIRE - ENG 1
  FIRE" title "ENG 1 FIRE" alert; "01- 0-250" "MEMO-SEAT BELTS"; "01-24-190" "ELEC-STS-GEN 1 INOP".

### 3.5 SD (plans 05 and 06)

- 10 page: 1 APU, 2 BLEED, 3 PRESS, 4 COND, 5 CRUISE (inferred), 6 DOOR, 7 ELEC, 8 ENG, 9 F/CTL, 10 FUEL, 11 HYD, 12
  WHEEL, 14 STATUS; 0 and 13 unknown. Measured: 5 in cruise, 6/7/8/12 on the ground and in the fire drill.
- Content, by name only (each plan confirms in the sim): APU 24 (4 N, 5 EGT, 8 available, 10 flap open; 2, 3 to
  confirm); ELEC 16, 17, 18/19 generators, 20-22 transformer-rectifiers, 23 external power, 25/26 batteries; HYD 132, 133
  (green, blue, yellow: pressure, quantity, flags; pumps); FUEL 134 (pumps, valves, 12 quantity with 6 total, 13 used, 14
  temperature); ENG 129; BLEED, PRESS and COND 151 (valves, bleed pressure and temperature, packs, zones, pressure data:
  differential, cabin vertical speed, cabin altitude, outflow valve); DOOR 179, 180 crew oxygen; WHEEL 135-144; F/CTL
  145-150; CRUISE from 129, 134.13 and 151.10 (by analogy).
- 175/176 STATUS text (repeated), 177 status flags, 178 bottom line (repeated, 9: 0 TAT, 1 SAT, 4 UTC hour, 5 minute, 6
  gross weight; 2, 3, 7, 8 unlabelled).

### 3.6 ISIS (plan 07)

182: 1-9 flags (powered, initializing, standard, approach, bugs page, hectopascal, LOC failed, GS failed, new style); 10
pitch, 11 bank, 12 airspeed, 13 altitude, 14 Mach, 18 QNH, 23 slip (doubles); 16, 17 alignment (nullable int); 21
selected bug; 22 bugs (enabled, value). Values matched the ADRs in cruise.

### 3.7 DCDU (plan 11)

186/187 DCDU (captain, first officer): 1 print frame, 2 header left, 3 header right, 4-8 message lines 1-5, 9-11 info
lines 1-3, 12 left key 1, 13 left key 2, 14 right key 1, 15 right key 2 (strings). Only empty strings were seen: no
message sample yet (needs a CPDLC uplink). Keys and brightness: the existing "DCDU Left" variables
(`FenixA320Definition`, the DCDU section of its variables and their handlers), first officer brightness
`S_DCDU2_DIM_BRT`.

### 3.8 What the app does today

- PMDG 737 (the newest model; PR #339): display keys open live windows (`PMDG737Definition.HandleHotkeyAction`, the
  `KeyFallsBackToAi` gate), rows in the display's own words (`Pmdg737PfdText`, `Pmdg737NavDisplayText`,
  `Pmdg737EngineDisplayPages`), FMA changes by column after a 300 ms settle with a silent first reading
  (`Pmdg737FmaMonitor`, `Pmdg737FmaSpeech`), display alerts diffed by item.
- FBW A32NX: the E/WD window on Alt+E (`FbwEwdWindow`), the DCDU window (`FlyByWireDcduForm`), Ctrl+W
  (`NdWaypointReadout`), D and Shift+D through the MCDU's FMS.
- Fenix: `FenixA320Definition.HandleHotkeyAction` calls `TryChannelDisplayRead` first, then its own keys; the AI reads
  are `FenixA320DisplayReads`; windows go through `ShowFenixWindow` and `FenixWindowRegistry` (one partial file per
  window); Ctrl+M extra rows are `FenixCabinAnnouncements.Rows`, appended by `FenixMonitorManagerForm`, and a speaker
  checks `FenixDisabledMonitorVariablesSet` itself (`FenixCabinReadyMonitor`). Output Shift+1-6, Ctrl+W, D and Shift+D
  are unbound on the Fenix today; input Ctrl+Shift+D answers "only available on the A32NX and the Headwind A330".

## 4. Conventions for every plan

- **Rules that bind**: [FNX-21] read only (buttons stay on L:vars and the 8083 named values); [FNX-22] speak only
  trusted data; [FNX-23] the build check; [FNX-24] colors, unknown letters; [FNX-25] true altitude; [FNX-2] and
  [A380C-6] marshal background work with `BeginInvoke` inside try/catch; [FNX-6] and [A320-3] full press-release;
  [CORE-7] never repeat the screen reader; [VAR-8] and [VAR-9] mutes; [A320-15]-[A320-20] the DCDU; [CORE-17] numbers
  the Fenix writes with `NumberParsing.TryParseExternal`; [CORE-14]/[CORE-15] logs through `Log`; US spelling; keep
  cockpit words as presented; established hotkeys never move (the keys above are all unbound on the Fenix or approved
  in section 5).
- **Field data**: each plan carries the field numbers and shapes it uses and adds them to `FenixChannelFields` (or a
  partial of it in the plan's own file). The decoded map and the team's tools stay local and are never committed. An
  unknown is measured in the sim by the plan that needs it, as one of its tasks.
- **Tests**: pure logic (decoders, composers, speech, geometry, announcer state machines) gets characterization tests in
  new files named for the feature, so the channel's rule globs load them (`*FenixChannel*`). Tests build their own
  protobuf bytes (`FenixChannelProtoWriter`), never a capture. Anything spoken or formatted gets a de-DE or sv-SE culture
  test.
- **In-sim tasks** are marked [sim] or [owner]: the person running the plan does them (or asks Robin), never a subagent.
  Their results go in the plan's `evidence/NN-slug.md`.
- **No pilot-visible change ships half-done**: a feature speaks only once its in-sim checks pass; until then its key
  keeps the AI read or stays silent.

## 5. Questions and answers (2026-11-02, Robin)

| # | Plan | Question | Recommendation | Answer |
| --- | --- | --- | --- | --- |
| 1 | 01-07 | What does a display key do once the channel can read that display? | a live window, AI fallback | Live window |
| 2 | 01 | How are colors spoken? | after each stretch, merged | After each stretch |
| 3 | 08 | How are FMA changes announced? | column names, row 3 as "FMA message", column 5 left out | Column names |
| 4 | 09 | Which E/WD items are announced? | warnings and cautions on (red interrupts), "cleared", memos off by default | As recommended |
| 5 | 02 | Which wording for Shift+1 to Shift+6? | the PFD's own terms | The PFD's own terms |
| 6 | 03 | How does Ctrl+W read the next waypoint? | the ND's order, no colors | The ND's order, no colors |
| 7 | 11 | Does input Ctrl+Shift+D open the Fenix DCDU window? | yes, plus the windows list | Yes |
| 8 | 11 | What else is in the DCDU window? | Ctrl+P PRINT, a brightness control, a new-message announcement | As recommended |
| 9 | 10 | How do D and Shift+D work? | computed from the channel, checked against the MCDU | Computed from the channel |
| 10 | 01 | What does a pilot hear when the channel cannot be used for a session? | one notice per session | One notice per session |
| 11 | all | How is the work split? | groundwork first, parallel plans, integration last (12 plans) | As recommended |
| 12 | 01 | Which cross-checks? | add airspeed, engine and temperature checks | Fewer: only the existing ones |
| 13 | all | Windows and keys, announcements and the DCDU, team process | as presented | Approved |

Rows 14-45 are the questions the plans raised, answered on 2026-11-03 ("Accept all"). Each plan follows them; a plan's
"(owner question)" mark is settled here.

| # | Plan | Question | Recommendation | Answer |
| --- | --- | --- | --- | --- |
| 14 | all | A comma before the color of a one-color line? | yes, as 2.6 ("THR CLB, green") | As recommended; 2.7's example corrected |
| 15 | 04-06 | A value a display draws as crosses | "XX" | As recommended |
| 16 | 02 | The FMA's FD row, sent as "1   2" | "1 FD 2" once a capture confirms it | As recommended |
| 17 | 02, 07 | Airspeed under the speed tape's floor | "Airspeed below 30 knots" | As recommended |
| 18 | 02 | Alpha max, the selected altitude and heading, the FD bars | left out | As recommended, except the FD bars: changed on 2026-11-02, after the "Accept all", by Hadi with Robin's agreement, to shown in the PFD window while drawn, with "flashing" while they flash, no row when none is |
| 19 | 03 | Ctrl+W in ROSE LS and ROSE VOR | the waypoint the ND sends | As recommended |
| 20 | 03 | A Ctrl+W part shown as dashes | left out | As recommended |
| 21 | 03 | ND lines with no words of their own | app labels, as the PMDG 737's ND window | As recommended |
| 22 | 03 | An unknown letter in any ND line | the whole ND not available, Ctrl+W included | As recommended |
| 23 | 03 | Map options, option flags, status flags, VOR and LS blocks | left out | As recommended |
| 24 | 03 | Ctrl+W's order against the FBW A32NX's | keep answer 6; the FBW's for its own issue | As recommended |
| 25 | 04 | The LEAP and PW GTF engine block | the A320neo's, N1 first | As recommended |
| 26 | 05, 06 | SD symbols and state words | plain words; the FBW A32NX SD rows' state words | As recommended |
| 27 | 05, 06 | Values drawn only as a gauge | spoken as numbers | As recommended |
| 28 | 05, 06 | The bottom line's time; door names | "UTC"; the cabin window's names | As recommended |
| 29 | 06 | WHEEL's green and amber labels | without the color, for now | As recommended |
| 30 | 05, 06 | A wrong shape on one SD page | that page's rows only; page name and bottom line stay | As recommended |
| 31 | 07 | The ISIS entry on a round-gauge Fenix | "This Fenix has no ISIS." | As recommended |
| 32 | 07 | Controls in the ISIS window | none | As recommended |
| 33 | 02, 08 | Two rows of one FMA column | one line, each row with its color | As recommended |
| 34 | 08 | An FMA column or message that goes blank | nothing spoken | As recommended |
| 35 | 08 | DH, MDA or NO DH in the approach column's row 3 | part of the approach column, confirmed in flight | As recommended |
| 36 | 09 | A title wrapped onto two lines, or in the other column | matched for its color | As recommended |
| 37 | 09 | A failure the E/WD is not showing | heard unless both the warning and caution rows are muted | As recommended |
| 38 | 09 | A memo that goes | never "cleared" | As recommended |
| 39 | 10 | The D and Shift+D tolerance | 1 NM + 0.5 % (T/D: + 0.5 NM per leg) | As recommended |
| 40 | 10 | Shift+D once the T/D marker is gone | "Top of descent not available", unless a passed sign is found | As recommended |
| 41 | 10 | Route data short of the destination | the plan stops for the owner's ruling | As recommended |
| 42 | 11 | Soft-key rows | as the A32NX window ("1 <STBY", "2 RECALL>") | As recommended |
| 43 | 11 | Ctrl+Shift+D on an unreadable DCDU | the window, with its status row | As recommended |
| 44 | 11 | A new DCDU message | new header and first line, held 300 ms; quiet 3 s after the pilot's own page or message key | As recommended |
| 45 | 11 | The status word in the new-message call | left out | As recommended |

## 6. Open checks (each owned by the plan named)

| Check | Plan |
| --- | --- |
| The FMA slot layout (row x 5 + column), slot 15, and the colors of armed and managed modes | 08 (02 shows the rows) |
| When V1, VR, V2, F, S and VFE next are drawn; the vertical speed unit; which PFD flags mean what; the flight director bars' unit, signs and flags | 02 |
| The `d` letter in the ground speed text; the EFIS mode numbering; ND message sides | 03 |
| The engine family field and the order of the engine block per family | 04 |
| SD pages 0 and 13; per-page content and enum values | 05, 06 |
| Leg fields 3 and 9-11; the top-of-descent marker type; the along-route distance against the MCDU | 10 |
| A real DCDU message: layout, colors, the soft-key labels, how MSG and PGE move it | 11 |
| A whole flight with every feature on, from cold and dark | 12 |

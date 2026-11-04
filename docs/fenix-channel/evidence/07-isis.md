# Plan 07 evidence: the ISIS

Fenix build **3.0.0.7613**, Daniel Wolak's install, worked by Daniel with Claude. Raw fields come from the team's local
channel tools and a subscribe-only recorder like the team's watcher, which stay local and are never committed.

## The fields ([FNX-23]): Task 2 Step 6, 2026-11-04

`IsisState` and `BugState`, and `RemoteData`'s field 182, read fresh from the installed
`C:\Program Files\FenixSim A320\deps\displays\FenixDisplay.exe` (product version 3.0.0.7613) with the team's metadata
tool (233 `ProsimRemoting` types), with the sim closed. Every number and wire shape Task 2 adds stands, with no change:

- `RemoteData.isis` is 182, `IsisState`.
- `IsisState`: 1 Powered to 9 NewStyle `boolean`; 10 Pitch, 11 Bank, 12 Airspeed, 13 Altitude, 14 Mach, 15 Brightness
  `double`; 16 Init and 17 QuickRealign `Nullable<int32>`; 18 Qnh, 19 LocDots, 20 GsDots `double`; 21 SelectedBug
  `int32`; 22 Bugs `BugState[]`; 23 Slip `double`. The same as the team's contract dump of 2026-11-01.
- `BugState`: 1 Enabled `boolean`, 2 Value `double`.

The decoder reads 16 and 17 as a plain varint, which is how protobuf-net writes a `Nullable<int32>` with a value; Task 1
Step 1 (below) saw exactly that on the live stream.

## Task 1 in the sim: Fenix A321 CFM at CYYZ and in the climb, 2026-11-04

The airframe: `TITLE` "FenixA321 CFM WF TC" (Air Canada livery), `B_CONFIG_STDBY` 0 (the ISIS). The recorder logged
field 182 once a second and at every change (decimals compared to 0.1), and the ISIS was read from captures of the sim
window on the Fenix's centre panel view (instrument view 8, `CAMERA VIEW TYPE AND INDEX` 2/8, written by Claude through
the simconnect MCP at the owner's word, as the app's AI read writes it). Times are local. The ISIS buttons were the
app's ISIS panel controls, pressed by the owner, except the held reset (Step 2).

- **Step 1, power-up (02:10:37, batteries on).** Before power, cold and dark, 182 is **sent unpowered**: .1 left out,
  with .2, .6, .7 and .8 set, .16 = 90, .18 = 1013 and the six bugs at their defaults; the gate reads 182.1, so a dark
  ISIS stays "display off". At power-up .1 comes with .2 (Initializing), .7 and .8 (LOC and GS failed), .16 = 90 and
  **.17 = 10**. 9.5 s later .7 and .8 clear; at 10 s .17 is left out; at 90 s .2 clears and .16 is left out.
  **.16 and .17 arrive as plain varints** (the decoder's reading), and **neither counts down**: .16 read 90 for all 90 s
  and .17 read 10 for all 10 s, so each is the length of its phase, not the seconds left. The ISIS's words during the
  90 s were not captured (the camera was elsewhere); the 10 s phase draws "ATT 10s" (Step 2).
- **The altitude at power-up is stale.** 182.13 read **367.7** (feet; the field is about 553 ft, QNH 1016) from before
  power-up until 02:15:00, about 4.4 minutes after it, when it rose to 464 (the right altitude at 1013) over a few
  seconds; the IRs were still aligning ("IR IN ALIGN > 7 MN" on the E/WD), and the airspeed field twitched at the same
  moment. Robin's KATL push of 2026-11-01, with the engines running, carried 372.9 at a field of about 1,026 ft: so the
  stale value is not only a power-up one. Nothing cross-checks the ISIS (spec 2.2): a question for the owner and for
  Task 3 (see "Open").
- **Speed scale floor.** At rest (182.12 about 0.06 kt, then the wind) the tape draws its labels at 40, 60 and 80 with
  the pointer at the bottom end, about **30**: `TapeFloorKnots` 30, as the PFD's.
- **Step 2, quick realignment.** A press-release of `S_MIP_ISFD_RST` (the app's ISIS Reset) **started nothing** (02:25:4x,
  no .17, no change in six captures). **Held**: written 1 at 02:26:59.6 through the MCP, 0 about 3 s later, .17 = **10**
  from 02:26:59.6 to 02:27:09.6, a constant 10, as at power-up. The ISIS drew **"ATT 10s"** in an amber box in place of the
  attitude (sky and ground gone, the aircraft symbol kept; speed, altitude and baro kept), "10s" still at 7 s, then the
  attitude came back. So the app's one pulse cannot reach the quick realignment (plan 12, "also seen").
- **Step 3, baro: Outcome B.** QNH in hPa: .6 set, .18 = 1016, drawn "1016" in cyan. ISIS Baro Push (02:28:25.7): .3 set,
  **.18 stays 1016** (the kept QNH), the altitude 474 (pressure altitude), drawn **"STD"** in cyan. The unit **follows the
  captain's EFIS baro selector**: inHg at 02:29:37.9 cleared .6 while **.18 stayed 1016 (hPa)**, drawn **"30.00"** (1016 hPa
  = 30.002 inHg, two decimals). So Task 3 converts .18 with `AltimeterPhrase.HpaPerInHg` while .6 is clear. There is no ISIS
  unit option.
- **Step 4, LS.** Lined up on CYYZ 23 with the ILS tuned, LS scales off: .19 = **0.23** and .20 = **4** already on the wire, not
  drawn. ISIS LS pressed (02:51:30.9): .4 (App) set, and the ISIS drew a localizer scale along the bottom and a glideslope
  scale on the right, **two dots either side**, magenta diamonds: the glideslope diamond **pinned at the top** (4, below the
  glide path on the ground), the localizer diamond **just right of centre** (0.23). Positive is diamond right and diamond
  up, as the PFD's 82.1 and 82.2. .19 and .20 were NaN in the climb with LS still on (the ILS out of range). The 30 m
  offsets were left for the approach. The stock NAV CDI:1 read -127 and NAV HAS LOCALIZER 0: the Fenix does not feed them.
- **Step 5, bugs.** Cold and dark and since: four speed bugs at **30** and two altitude bugs at **6000** and **10000**, none
  enabled (Robin's six empty bugs were another state). ISIS Bugs sets .5 and draws the page: a "BUGS" title, "EXIT ↑" by the
  Bugs button, "SET/SELECT" by the baro knob, an SPD column of four boxes and an ALT column of two, each "OFF" while not
  enabled, the selected one between "(+)" and "(−)" marks. .21 is the selected bug's **0-based index into .22** (left out:
  0, the first speed bug); Plus moved it 0 to 4, Minus 4 to 5, along the lines that join the boxes. The baro knob steps the
  selected value (an altitude bug by 100: 10000 to 10100). **The baro push enables the selected bug** (.22[5] gained .1 = 1
  at 02:34:30.5; its "OFF" went and it lost its box). The page **closes itself about 15 s after the last press** (02:30:43.8
  to 02:30:58.8; 02:33:12.7 to 02:33:27.6), and a baro push after that sets STD instead.
- **Step 6, style.** The Fenix's ISIS option (Old, New, Auto; the contract's `IsisType`): Auto gave the **old style** on this
  airframe (.9 left out; Robin's KATL aircraft had .9 set). New (02:37:00.1) set .9 and drew the same words and layout, with
  a rounded attitude frame and the altitude drum without its leading zero ("5" for "05"). **No word differs**: the window
  ignores the style. **Slip on the ground: none.** A taxi turn right left .23 out (0) and the index centred throughout; the
  Fenix moves the slip in the air only (Step 8).
- **Step 8, in the climb.** **Pitch positive up** (.10 +9.4 to +16 in the climb, SimConnect's PLANE PITCH -9.4).
  **Bank positive right** (.11 +27 drew the horizon high on the right and the roll pointer past 20 right; SimConnect's
  PLANE BANK the opposite sign). **Slip**: +1.5 at a bank of +22 in the first turn, about +0.3 in a steady 30-degree right
  turn, within 0.01 of 0 straight and level; where the index leaves centre (`SlipCenteredBelow`) is still to be captured.
  **Mach**: NaN below M.50, first a number at **0.5000** (02:59:07.8), drawn **".50"** in green under the speed scale at once,
  then ".55" for 0.5494 (one frame about 1 s after its log line, so the rounding is not settled) and ".56" for 0.564:
  **`MachShownFrom` 0.50**, the wire's own floor.

## The approach into KLAX and a power cycle at the gate, 2026-11-04

The same airframe, after CYYZ to KLAX (the ANJLL4 arrival, an ILS to touchdown). The recorder as above; captures every 5 s
while the Mach was 0.40 to 0.60, the bank beyond 10 degrees or .19 a number, and every 3 s through the power cycle.

- **Localizer, large.** .19 read **-4** (full scale) from 11:32:08, when the ILS came in range, then fell steadily through
  -3, -2 and -1 to about **-0.05** on the centreline (11:50:30 on). At **-1.54** (11:49:42) the ISIS drew the diamond about
  1.5 dots **left** of centre. With +0.23 drawn right at CYYZ: negative left, positive right. -4 is the pinned value either
  way, as the PFD's 4.0.
- **Glideslope.** .20 about **+1.3** on the intercept from below, drawn **above** centre, then toward 0 down the glide path.
- **Mach, coming down.** Still drawn ".47" at 0.467 (11:49:42), and on the wire until **0.4500**, NaN from 11:50:36.7. So
  the ISIS shows the Mach from 0.50 going up and until 0.45 coming down: a hysteresis the wire carries, so the window needs
  no threshold of its own (`MachShownFrom` is informative only).
- **The flare** (informative, the owner's landing): pitch 2.2 to 2.3 degrees down the approach at 142 kt; from 12:01:01 the
  pitch rose to **10.8** (12:01:06) and the speed fell to 122 kt; touchdown about 12:01:16 at about 5 to 6 degrees. The
  pitch sign holds through the flare, positive up.
- **Power cycle (12:08:30.7, batteries on).** The ISIS drew **no tapes and no attitude**: amber boxes **"ATT"** (top),
  **"SPD"** (left), **"ALT"** (right) and **"INIT 90s"** (below), with red **"LOC"** and **"G/S"** flags for the first 8 s
  (.7 and .8 set, LS on), and the baro "1015" in cyan. .16 = 90 for the 90 s, .17 = 10 for the first 10 s, but the ISIS drew
  **INIT 90s, not ATT 10s**: initializing takes precedence, and "ATT 10s" is the quick realignment's alone (.17 with .2
  clear). All came back at 12:10:00.8, 90 s after power-up.

### What Task 3 takes from this

- While .2 is set: a row "INIT 90s" (the ISIS's own words, the figure from .16 as sent, never a count), the SPD, ALT and
  ATT flags in place of the speed, altitude, pitch, bank and slip rows, and the LOC and G/S flags while .7 and .8 are set.
- While .17 is a number with .2 clear: "ATT 10s" (from .17 as sent) in place of the pitch, bank and slip rows; speed,
  altitude and baro as usual.
- Baro: "STD" while .3; else the QNH from .18 in hPa while .6, or converted to inches to two places while .6 is clear.
- Signs: pitch positive up, bank positive right, the deviations positive right and above, two dots full scale, the
  pinned value 4; worded as plan 02's rows.
- The bugs: four speed then two altitude, by index; only enabled ones (.22.n.1) are worth a row on the main display.
- The style (.9) changes no word.

### Open

- The slip index's threshold (`SlipCenteredBelow`): the turns gave slip up to 1.5 with no capture of the index leaving
  centre; a flight with rudder or a crosswind.
- What the ISIS draws for an enabled bug on the main display (one at 10100 ft was off the tape).
- Step 7 (a round-gauge livery).
- For the owner: the stale altitude (367.7 for 4.4 minutes after power-up here, 372.9 with engines running at KATL) reaches a
  pilot as sent once the ISIS is trusted. A check against SimConnect is ruled out since spec row 58 and [FNX-22]
  (2026-11-04), and no other field of 182 changed when the altitude woke (only .12, the airspeed, twitched), so the channel
  carries no sign of its own. Unless the owner rules otherwise, Task 3 reads it as sent.

## Robin's ground push of 2026-11-01

Field 182 of `sample-aircraft-push-ground-2026-11-01.bin` (KATL, on the ground, the team's earlier sample), not this
install: .1, .6 and .9 set (powered, hPa, new style); .10 0.193, .11 -0.102, .12 9.30, .13 372.9; .14 NaN; .15 255;
.18 1013; .19 and .20 NaN; .16 and .17 left out; **six bugs, each an empty `BugState`** (not enabled, value 0). On
Daniel's install the bugs carried their defaults from cold and dark (Step 5), so the empty six are another state, not
the rule. The altitude 372.9 at KATL (elevation about 1,026 ft) is the stale value Step 1 found at CYYZ.

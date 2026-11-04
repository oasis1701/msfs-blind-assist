# Plan 06 evidence: the SD pages

Fenix build **3.0.0.7613** (a checked build: `fenix_channel.log`, "build 3.0.0.7613: checked"), kn4iee's install, worked
by kn4iee with Claude. Raw fields come from the team's local channel tools and a subscribe-only recorder like the
team's watcher (one push a second and one at each SD page change), which stay local and are never committed. Values
below are as the push carried them; "left out" means the field was absent from the push.

## The fields ([FNX-23]): Task 2 Step 2, 2026-11-03

Every constant in `FenixChannelFields.SdPages.cs` (165 of them: the 13 `Remote` numbers and every member of the nested
classes) was compared with the contract read fresh from the installed
`C:\Program Files\FenixSim A320\deps\displays\FenixDisplay.exe` (the team's metadata tool; 233 `ProsimRemoting` types):
the member name, the number and the wire shape (double as fixed64, boolean as a varint bool, `ValveStateEnum` as a
varint, a message as nested). All 165 match; nothing moved. `ValveStateEnum` declares Open, Closed, Transit,
FailedOpen, FailedClose, DataInvalid in that order. Two members the plan does not read were named by the contract:
`RudderData.TravelLimit` (146.1, 4.2 in cruise: the rudder travel limit) and `ZoneTemperatureData.HotAirValve`
(151.7.10) is a **double**, as the trim valves are, not an enum: Task 1's HOT AIR check measures what its values mean.

## In flight: Fenix A321 CFM in cruise, 2026-11-03 about 20:04 local

The airframe from `debug.log` ("Variant: FenixVariant { Type = A321, Engine = Cfm56, ... HasCargoHeat = False }").
The SD showed page **5** in cruise (field 10), which the plan names CRUISE. Read only; nothing was written to the sim.

- **APU (24), off:** only .5 EGT -55.2, .6 1067 and .7 1100 (EGT caution and maximum, not read); .8 and .12 left out.
  151.2.3, the APU's bleed pressure: **NaN** with the APU off, as the plan expected (reads "XX").
- **Valves (151.1):** .2, .4, .5, .6, .7, .8 and .11 at 1 (Closed); .1, .3, .9 and .10 left out (Open). An open valve
  is left out on the wire, again.
- **Bleed (151.2, 151.3):** 43.15 and 43.15 PSI; 200.0 and 200.0 °C. **Packs (151.5, 151.6):** compressor 175.27 °C,
  outlet 22.79 °C; .1 0.95 and .2 0.2104, not read. **Flags (151.4):** .5 to .8 set (not read); .1 (WAI) left out.
- **Zones (151.7):** cockpit, forward and aft 24.0 °C; ducts 37.6, 33.9 and 25.1 °C; trim valves 0.336, 0.406 and
  0.114 (fractions: 34, 41 and 11 %); .10 (HOT AIR) left out; cargo .11 6.3 and .12 5.8 °C with .13 to .16 left out.
  **151.8** present and empty: no cargo heating on this A321, as its variant says, so the cargo rows stay hidden.
- **Pressurization:** 151.9 only .15 (SYS 2 active); 151.10 .1 7.98 PSI, .2 -0.1665 (cabin V/S: its unit is checked in
  the descent), .3 6399.7 FT, .4, .5 and .9 at 1 (Closed), .6 0.0842 (OUTFLOW 8 %, a fraction as inferred), .7 **550**
  (LDG ELEV: compared with the destination's elevation once known), .8 4.0 (not read). Field 116 left out.
- **CRUISE's engines and fuel:** 129.1/.2 VIB N1 0.48/0.45, VIB N2 0.91/0.94, OIL QT 12.79/12.98; 134.13 2770, 2770 and
  5540; 154 left out (LBS).
- **WHEEL:** 137 .3 and .19 set (TPIS fitted; .3 not read); 138 19.8, 21.0, 19.4 and 19.8 °C; 139 165, 165, 210, 205, 210
  and 205 PSI; 141 present and empty. **Gear:** 143.n.1 and 143.n.2 = **2.0** for the nose, left and right gear (up, on
  both LGCIUs); 144.1 to .3 = **1.0** (the doors closed). These are `GearUnit.Up` and `GearDoorPosition.Closed`.
- **F/CTL:** the ten spoilers (145.2 to .6, .8 to .12) present and empty (position 0, no fault); ailerons 145.1 0.0519
  and 145.7 -0.0519 (mirror signs); elevators 0.0112 and 0.0112; THS 145.15 **-1.6117** (a cruise trim, nose up: negative
  is UP, as inferred); rudder 0.0524 and rudder trim 146.2 0.0524. 147 .2 to .4 set (FCDC data); 148.1 to .5 set (ELAC
  1 and 2, SEC 1 to 3).
- **DOOR on the A321:** 179.1 to **179.25 all set**, every door closed and **every slide armed, the overwing slides
  (22 to 25) included**, on an airframe with no overwing exit; **179.26 and 179.27 left out**, though the EFB lists a
  bulk cargo door for the A321 (the ground check reads 179.27 again). 180 .2 and .3 set, .5 and .6 1800 PSI.

## In flight: the descent, approach and landing at CYYZ, 2026-11-03 20:30 to 22:52 local

The same flight, the same recorder; the aircraft's heading comes from 28.3 (`AdirsData.Heading`), its bank from 28.2 and
its radio altitude from 27. Read only: the SD page keys were read with the MCP and nothing was written to the sim.

- **LDG ELEV (151.10.7):** 550 throughout, approaching CYYZ, whose elevation is 546.8 ft in the sim (569 ft
  published): the landing elevation in the SD's 50 ft steps. PRESS's and CRUISE's LDG ELEV rows are right as built.
- **Page 5 is CRUISE:** in cruise all twelve SD page key lights (`I_ECAM_ENGINE` to `I_ECAM_STATUS`) read 0 through
  the MCP, with `S_OH_ELEC_BAT1` reading 1 (the reads were live): page 5 is the automatic page with no key lit.
- **Cabin V/S (151.10.2):** feet a minute, climb positive: -350 while the cabin altitude (151.10.3) fell from 6400 to
  6096 ft, +200 then +311 while it rose from 6444 to 7001 ft.
- **Turns (22:18 to 22:20, autopilot):** the roll spoilers 2 to 5 on the left (145.3 to .6) rose to 0.1081 while the
  left aileron (145.1) read -1.35 and the right +1.35 (always the mirror): roll spoilers rise on the up-going aileron's
  side, so an aileron's **negative is up**. Spoiler 1 never moved for roll (the A320's roll spoilers are 2 to 5). In a
  right turn (bank +19.7, heading rising 169 to 180) the rudder (145.16) went to +0.29; in a left one (bank -17, heading
  falling) to -0.10.
- **Speed brakes (22:35:00 to 22:35:08):** spoilers 2 to 4 on both sides ran 2.69, 5.56, 8.87, 11.78, 14.76, 17.64,
  20.88, 23.76 to **25.0** and held, about 3 a second; spoiler 1 never moved. A value of 25.0 is neither radians nor a
  fraction: the spoilers' unit is **degrees**, positive up.
- **Gear extension (22:44:46 to 22:45:05):** every LGCIU value (143.n.1 and .2) went 2.0 (up) to **1.0** for 10 s and
  then was left out (**0**) once down, and stayed so: `GearUnit` Up 2, InTransit 1, Down 0. With the gear down, 143.1
  to .3 are present as empty messages. The doors (144.n) went 1.0 (closed) to **0** for 3 to 5 s, then **2.0** for 13
  to 15 s (until 8 s after the gear locked down), then 1.0; 144 stayed present (empty while all three were 0). The
  contract names none of these values (bare doubles). The A320 WHEEL page draws a door that is not closed one way,
  amber (secondary sources: [aviationhunt, A320 ECAM WHEEL page](https://www.aviationhunt.com/?p=5827); [Brainscape
  landing gear cards](https://brainscape.com/flashcards/landing-gear-13370535/packs/21383196): in transit "the gear
  door symbol is amber"), so both 0 and 2 read "open".
- **Field 116 (`landingAlt`, a nullable double):** left out in cruise and the descent; present from 22:46:05, about
  770 ft above the ground on the approach, reading **547.0**; left out again after touchdown (22:47:22). A note for
  plan 02.
- **Approach, flaps full (22:46 to 22:47:03):** both ailerons at **+4.9**, the A320's aileron droop (down) with the
  flaps out: positive is down. The flare (22:47:03): the elevators (145.13, .14) at **-5.0**, nose up: an elevator's
  negative is up.
- **Touchdown (22:47:08):** all ten spoilers 10.8, 32.4, then **50.0**, held (the ground spoilers' 50° maximum:
  degrees again); both ailerons **-25.0** (both up, lift dumping); the elevators then **+15.0** (down); the THS ran from
  -5.88 to -0.0078 in 7 s (the pitch trim reset after touchdown). 146.1 (the rudder travel limit) read 4.2 in cruise,
  5.2 in the descent and 25.0 on the approach and the ground.
- **Rollout and taxi (22:47:26 to 22:48:02):** the rudder at -18.75 and -20.27 while the heading fell from 236.1 to
  220.9 (a left turn), +8.38 and +4.75 while it rose from 222.2 to 232.3 (right), -10.72 while it fell from 232.1 to
  212.5, -15.88 while it fell from 209.5 to 193.4: the rudder's **positive is right**. The wind was 150° at 2.5 kt
  (the sim), too light to show anything on the approach.
- **Rudder trim (146.2):** equal to the rudder straight and level (0.0823); held 0.1873 while the rudder peaked at 0.29
  in a turn; rose with the rudder in the flare to 1.66, and was left out (0) after touchdown. The page reads it as the
  Fenix sends it.
- **Brakes:** AUTO BRK LO (137.4) from 22:27:26 (MED, 137.5, for 2 s at 22:28:13) until 22:47:29, after touchdown.
  141 present and empty throughout (no brake released). The brake temperatures (138.1 to .4) read 15.3 °C on the
  approach and rose after the landing to 125, 142, 129 and 136 °C by 22:51:54.

Every sign the F/CTL page assumed was right; the spoilers' degree unit and the gear's InTransit and Down values and the
doors' open values went in as two fix commits.

## On the ground at the CYYZ gate: Task 1's checks, 2026-11-03 23:00 to 23:31 local

The same A321 CFM, engines off, the APU running with its bleed on, packs on. The owner handed the aircraft over ("This
is your airplane"). Every write went the way the app's definition writes it: a pushbutton or selector as its L:var
value, a press button (APU START, EXT PWR, the ECAM keys) 0, 1, 0; the doors through the EFB's ground-service names the
cabin window writes (`doors.control.<key>` Int 2 Open and 1 Disarmed, never Armed; the cargo doors Bool); ground power
through the EFB's own ground-service name `groundservice.groundpower` (Boolean, EFB chunk 964), on the owner's request.
Nothing was written on the channel.

- **Valves, the enum in context.** X BLEED SHUT: 151.1.6 read 2 (Transit) for 3 s, then 1 (Closed); OPEN: 2 for 3 s, then
  left out (Open). With X BLEED shut the right bleed pressure fell from 32.9 to 0 and pack 2's valve read **4**
  (FailedClose) for 9 s, until X BLEED opened again. RAM AIR and the wing anti-ice valves: 2 for 3 s each way. The APU
  bleed valve alone reads **3** (FailedOpen) for its 3 s in transit, both ways; with the APU bleed off, both packs'
  valves read 4. PACK 1 off: 151.1.9 to 1.
- **Wing anti-ice** on: 151.4.1 set, both valves open, then closed by themselves after about 29 s (the ground test), the
  pushbutton still on.
- **Outflow (151.10.6)** 1.0 on the ground (fully open), 0.5 with RAM AIR on: a fraction, as the page reads it.
- **HOT AIR (151.7.10)** carries the valve enum as a double: turning HOT AIR on, 1.0, then 2.0 for 3 s, then left out
  (open); it follows the button, so COND has its HOT AIR row (`HotAirStates`).
- **CAB FANS** off: 151.8.1 and 151.8.7 both set (the FAN 1 and FAN 2 rows). **ELAC 1** off: 148.1 clear.
- **LDG ELEV knob** (`A_OH_PNEUMATIC_LDG_ELEV`, -3 is AUTO at the bottom stop) at 2: 151.10.7 2000.0 and 151.9.16
  (ManualLandingElev) set within 1 s. In AUTO at the gate 151.10.7 is left out, so the row reads 0 FT: what the Fenix's
  own SD draws then is not yet compared.
- **The doors**, each opened and closed in turn from Disarmed: d1l 179.4, d1r .5, d2l .8, d2r .10, d3l .9, d3r .11,
  d4l .14, d4r .15, forward cargo .7, aft cargo .12; each moved only its own field, 2 to 3 s after the write. The bulk
  cargo door cleared 179.13 for one push and came back, its data name reading back closed: this A321 has none, matching
  179.27 left out. The DOOR table's A321 doors 2 and 3 (8 and 10, 9 and 11) are right.
- **The slides.** At the gate the cabin's own slide names read doors 1 to 4 disarmed and the overwing slides armed; on the
  channel 179.16, .17 and .20 to .25 were clear and .18 and .19 (SlideCenterLeft and Right) still set: 18 and 19 follow
  the overwing slides the A321 does not have. A slide cannot be armed on purpose (the EFB never writes Armed), and the
  disarm after shutdown (16 and 25, then 20, 21, 22 to 24, 17, a second apart) shows no door-by-door pairing, so the A321's
  doors 2 and 3 still say no slide.
- **The APU.** Master off (on ground power): N 100 and AVAIL for about 110 s of cooldown; then AVAIL clear, the amber
  title (24.13) set, N and EGT falling; FLAP OPEN clear at N 6.7; N, MasterPower and the amber title clear, the APU bleed
  pressure NaN. Master on: MasterPower and the amber title set within 1 s; FLAP OPEN after 20 s; N rising, EGT peaking
  at 796; AVAIL at N 95, 65 s after master on, the amber title clear 1 s later; the generator 115 V, 394 to 401 HZ.
- **The ECAM keys** gave APU 1, BLEED 2, PRESS 3, COND 4, DOOR 6, F/CTL 9, WHEEL 12; one press of ALL moves to the next
  page of the cycle (WHEEL to F/CTL to ENG).
- **WHEEL at the gate** (hydraulics off): ALTN BRK green (137.2), NORM BRK amber (.8), N/W STEERING (.9), AUTO BRK amber
  (.13), ANTI SKID amber (.17), ACCU ONLY (.18); the brakes at 179 to 204 °C; no brake amber (140 empty).
- **The window, by ear.** The owner toured every page in the app with NVDA: each came up with its name, and no "SD:"
  problem line was logged in `fenix_channel.log` after the build started (23:26). The tour asked for the wheels, spoilers
  and gear computers to be named by position and for the colors the Fenix sends to be spoken; those changes followed.

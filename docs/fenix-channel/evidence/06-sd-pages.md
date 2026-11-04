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

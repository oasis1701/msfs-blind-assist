# COWS DA40 radios, altimeters, autopilot and hotkey rules — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/da40-avionics.md`, which Claude Code loads when it reads matching code. Background: [da40.md](../da40.md).
The text is verbatim from the CLAUDE.md invariants PR #242 added; where two bullets told one rule they sit together under one ID. A trailing "→ doc" pointer is the original's.

## DA40A-1

- **⚠️ THE DA40 GFC 700 IS THE WORKING TITLE AUTOPILOT, AND ONLY THE EVENTS ITS STATE MANAGER INTERCEPTS DO ANYTHING.** Vertical speed is `AP_VS_ON`/`AP_VS_OFF` (or `AP_PANEL_VS_*`); `AP_VS_HOLD_ON/OFF` are not in its list and left the autopilot in pitch hold. Read the intercept list from the live instrument (`wtg1000-mfd`'s `autopilot.stateManager`) before choosing an event, never from the SDK's event table. The ELT is `ELT_SET` 0 ARM / 1 ON (the cockpit switch `SAFETY_ELT_1` has no 2; `TOGGLE_ELT` is dead). `COWS_KILL_FMA` is model-written, a status, never an option. → [da40.md](../da40.md)

## DA40A-2

- **The G1000's selected altitude counts as SELECTED only through its own key events.** A direct `AUTOPILOT ALTITUDE LOCK VAR` write leaves `isAltSelectInitialized` false and the GFC 700 refuses FLC; send one `AP_ALT_VAR_INC`, then write the exact value after it lands. → [da40.md](../da40.md)

## DA40A-3

- **The DA40 GFC 700 autopilot has NO airframe variables — do not go hunting for one.** Nothing in the COWS package writes an `L:AP_*`, there is no `STATE_` mirror for a mode and no vendor event for a button; the model XML and the vendor `DA40 LVAR bindings.txt` are both empty on it. The GFC 700 is integrated into the G1000, and this package ships the STOCK Working Title instrument, so the whole autopilot is stock K: events and stock SimVars. → [da40.md](../da40.md)

- **Every DA40 autopilot mode writes the DISTINCT ON or OFF event, NEVER the stock `_TOGGLE`** (`AUTOPILOT_ON`/`AUTOPILOT_OFF`, `AP_HDG_HOLD_ON`/`AP_HDG_HOLD_OFF`, …). A combo asks for a STATE and a toggle can only ask for a CHANGE, so one disagreement between panel and aeroplane — a mode armed from a yoke button, a mode the G1000 dropped on its own — sends the autopilot to the OPPOSITE of what the pilot just selected, silently. Every write still goes through `ExecuteCalculatorCodeUnique`: ON pressed twice running is two byte-identical calc strings. → [da40.md](../da40.md)

## DA40A-4

- The DA40 heading bug and course knob live on the **GFC 700 panel, not Radios** — they are PFD bezel knobs (which is why they started beside the radio knobs) but functionally they are what the autopilot flies. MOVED, never copied: two panels showing one knob lets a pilot set it in one place and read a stale value in the other (`TheAutopilotOwnsTheHeadingBugAndCourse` pins both halves). `AutopilotDisplay` is EMPTY for the same reason — every selected value is already a control, and a control shows its own value. → [da40.md](../da40.md)

## DA40A-5

- **⚠️ THE GFC 700's PRE-FLIGHT TEST WAS INVISIBLE TO MSFSBA** - `AFCS_TEST` (0 idle, 1 running 10 s, 2 complete, -1 AFCS failed) and `AFCS_PFT` (0 not started, 1 running, 2 passed, 3 latched, -1 failed, -2 latched). On a real GFC 700 this is what says the autopilot may be relied on at all. ⚠️ PRESSING THE DISCONNECT DURING THE TEST FAILS IT: while `AFCS_PFT` is 1, `INPUT_AP_DISC` or a servo failure reaching 2 drives it to -1; it needs five seconds undisturbed. All three states are announced now, and they are the first read-only rows the GFC 700 panel has ever had. → [da40.md](../da40.md)

## DA40A-6

- **⚠️ RETRACTED: "COM IGNORES `COM_STBY_RADIO_SET_HZ` AND IS 25 kHz ONLY" IS WRONG, AND THE BCD16 IT JUSTIFIED WAS THE "COM TUNING IS UNRELIABLE" BUG.** Re-measured live on the DA40: `121705000 (>K:COM_STBY_RADIO_SET_HZ)` reads back **121.705** and `118185000 (>K:COM2_STBY_RADIO_SET_HZ)` reads back **118.185** — full 8.33 kHz resolution on both radios. BCD16 carries four digits, so it could not express `.705` at all and silently ROUNDED every 8.33 channel to the nearest 25 kHz. BOTH radio families now take RAW HERTZ (`NAV{n}_STBY_SET_HZ`, `COM_STBY_RADIO_SET_HZ`/`COM2_STBY_RADIO_SET_HZ`) and differ only in the event name. `ComBcd16` is retained with its test because the encoding is correct for what it is; nothing in the tuning path calls it. → [da40.md](../da40.md)

## DA40A-7

- **⚠️ A DA40 RADIO CAN BE FAILED, AND A KNOB ON A FAILED RADIO DOES NOTHING AND LOOKS BROKEN.** Measured live: NAV 2 and COM 2 carry the class `failed-instr` while NAV 1 and COM 1 do not, and it does NOT follow the tuning cursor — pushing the cursor from COM 2 to COM 1 left the class on COM 2 and did not put it on COM 1, so it is a real state and not selection styling. Two knob PUSHES park the cursor on radio 2 (that is the push doing its job), the knob then moves nothing, and the radios read as broken; one more push returns to radio 1 and tuning resumes (verified, 119.650 → 119.655). `A.radios()` appends **FAILED** so a scan says why. ⚠️ NOTHING IS ANNOUNCED FOR IT — the pilot's ruling is that a state the DISPLAY can report is scanned for, not spoken at them; the row must CARRY it or there is nothing to scan. ⚠️ THE CAUSE IS THE AVIONICS BUS, AND THE OBVIOUS DISPROOF OF THAT IS ITSELF WRONG. `systems.cfg` puts COM 1/NAV 1 on **bus.1 (BUS_BAT)** and COM 2/NAV 2 on **bus.2 (BUS_AVN)**, so the avionics master powers the SECOND radios ONLY — the G1000 displays and the first radios run without it. `AVIONICS MASTER SWITCH:1` reading 0 therefore coexists with a fully lit PFD, live CAS messages, working softkeys, `COM AVAILABLE:1` = 1 and COM 1 tuning perfectly, and NONE of those disproves it. Verified both ways: with it off COM 2 would not move across repeated presses; `1 (>K:TOGGLE_AVIONICS_MASTER)` cleared FAILED from both boxes and NAV 2 stepped 113.900 → 113.950 on the next click. ⚠️ I diagnosed this correctly, then RETRACTED it on the strength of "if the avionics were off the PFD would be dark" — a reasonable-sounding argument that is false on this airframe, and over-correcting cost more than the original error. Read the bus wiring before accepting either verdict. → [da40.md](../da40.md)

## DA40A-8

- **⚠️ A KEY THE PILOT JUST PRESSED MUST BE READ BACK AFTER A FRAME, NOT IN THE SAME BREATH.** The DA40 radio knobs fired the event and scraped immediately, so the read came from BEFORE the keystroke — the row looked unchanged, nothing was announced, and the pilot's NEXT press read back the PREVIOUS one's frequency ("it was at 710, I pressed twice, I got 715"; 715 was the first press and the radio was already on 725). `PressBezel` has carried the identical retry-if-unchanged fix all along and its own comment names the symptom — the knobs simply never got it. Read over the COHERENT SOCKET for a key the pilot pressed (prompt, and what makes an answer possible at all); the 1 Hz batch + settle announcer is for changes made ELSEWHERE. ⚠️ AND `710 → 715 → 725 → 730` IS CORRECT 8.33 kHz STEPPING, not a bug: the channel NAMES are unevenly spaced and `720`/`745`/`770`/`795` are not channels, so the radio steps over them. → [da40.md](../da40.md)

## DA40A-9

- **⚠️ A SETTLED KNOB VALUE MUST USE `AnnounceImmediate`, NOT `Announce`.** `Announce` speaks with interrupt off, so it queues behind everything - and the moment a pilot turns a knob is exactly when the queue is busy, because they are working a display window that is reading itself back. Reported as the altimeter setting arriving "very late"; it was in a queue. Interrupting is correct for a settle specifically: it fires several hundred ms AFTER the pilot stopped turning, so it carries the number they are waiting for. → [da40.md](../da40.md)

## DA40A-10

- **The DA40 standby altimeter READS THE MIRROR AND WRITES THE INPUT.** Its input `L:KOHLSMAN SETTING HG:2` carries a space AND a colon, so `SetLVar` refuses the calculator path and its data-def fallback lands on the STOCK SimVar of that name - a different variable (measured: the L:var moved to 30.11 while the SimVar stayed 29.85). Read `L:STATE_BARO2` (the airframe own clean-named mirror, unit `number`) and write the input through the calculator. The COLON alone is NOT the read bug - `FADEC_ECUTEST_TIMER:1` reads fine through the same path; the unit was the read bug and the space-and-colon name is the WRITE bug, separate and both present. Ctrl+B sets BOTH altimeters and always did. -> [da40.md](../da40.md)

## DA40A-11

- **Both DA40 altimeters announce an EXTERNAL change, debounced 700 ms** - a subscale steps 0.01 inHg and a knob sweeps dozens of steps a second, so a change arms a settle timer and only the resting value is spoken; MSFSBA own writes are skipped for 3 s (longer than the settle, because one set writes both subscales). That timer checks `DA40DisabledMonitorVariablesSet` ITSELF - it speaks from a callback outside the wrap that mutes `ProcessSimVarUpdate`, the same rule the A32NX armed-altitude flush follows - and `DA40_G1000_BARO` is consequently NOT `ExcludeFromMonitorManager` any more. -> [da40.md](../da40.md)

## DA40A-12

- **The G1000's unit settings are not SimVars or L:vars** - they are Working Title user settings (`unitsNavAngle`, `unitsDistance`, `unitsAltitude`, `unitsTemperature`, `unitsWeight`, `unitsFuel`) held by the instrument's `settingSaveManager` and persisted into the aeroplane's own profile, so they reach MSFSBA only down the Coherent socket, as a row on the ordinary scrape. BOTH displays carry the same six (verified live), which is why the always-on CAS monitor's PFD socket is enough and no display window need be open. Every DA40 readout measured in celsius/pounds/gallons/knots/feet now follows them, keyed on the variable's DECLARED UNIT and never on its name. ⚠️ A gauge's BAND is looked up from the RAW value - an arc is a physical span of heat or fuel and does not move with the pilot's chosen scale - and a fuel unit that cannot be converted without a density (the Garmin's weight-based options) is left in gallons rather than guessed at. → [da40.md](../da40.md)

- **The G1000's BAROMETRIC unit is a seventh setting and is NOT in the setup page's units box** — `baroHpa`, set on the PFD under PFD Opt then ALT Units. It rides the same units row, and the DA40's B readout leads with whichever the pilot chose (both are still given, because the aeroplane has two altimeters). Ctrl+B already accepted either unit and told them apart by magnitude: above 100 is hectopascals (948–1066), below is inches (28.00–31.50). Outside North America every clearance is in hectopascals, so the ORDER is not cosmetic. → [da40.md](../da40.md)

## DA40A-13

- **⚠️ NAV ANGLE IS READ AND DELIBERATELY NOT APPLIED.** Magnetic to true is a change of DATUM, not of units, and the heading bug and course are WRITTEN through `AUTOPILOT HEADING LOCK DIR` and `NAV OBS`, both magnetic - so showing a true bearing in a field the pilot then types into would put the aeroplane several degrees off the heading they asked for, silently. Never "finish" this without converting the WRITE side too. → [da40.md](../da40.md)

## DA40A-14

- **The G1000 has no radio PAGE and is not missing one** — COM and NAV are tuned from the PFD bezel's frequency boxes, live whatever the MFD is showing, and the MFD has no COM or NAV knob at all. What a G1000 offers instead is the FREQ softkey on the Nearest Airports and WPT Information pages, which loads a frequency straight into standby. → [da40.md](../da40.md)

## DA40A-15

- **⚠️ RETRACTION: THE AUDIT SAID Ctrl+W ANSWERED THE WAYPOINT ON THE DA40 AND IT DID NOTHING AT ALL.** Ctrl+W is `HotkeyAction.ReadNDWaypoint`, which the DA40 never handled, and `Services/NdWaypointReadout` behind it reads `A32NX_EFIS_L_TO_WPT_*` — FlyByWire variables absent from a Diamond. So the gap was BOTH halves, not just the automatic call, and the audit reported a dead key as working. The general lesson: a hotkey ACTION existing app-wide says nothing about whether THIS aircraft handles it — grep the def's own `case HotkeyAction.` list before claiming a key works. → [da40.md](../da40.md)

- **The waypoint readout and the passing call both come from the stock GPS SimVars, which the aeroplane's OWN navigator writes** — read live out of the Working Title G1000's `GpsSynchronizer`: `GPS WP NEXT ID` is the active lateral leg's name, `GPS WP PREV ID` the leg before it (so PREV ID IS "the waypoint just passed" — the aeroplane answering, not this app inferring), plus distance, bearing and ETE. ⚠️ `GPS WP BEARING` is written in RADIANS (ask for degrees and let SimConnect convert) and ⚠️ `GPS FLIGHT PLAN WP COUNT`'s write is COMMENTED OUT in the shipping build, so it reads 0 with a plan loaded — never trust it as "no flight plan". ⚠️ A PASSING IS A STRUCTURAL TEST, NEVER "the ident changed": the fix we were flying TO must have become the fix we are flying FROM. A Direct-To, a plan edit and a plan activation all change the TO-waypoint and none is a passing — an "it changed" rule would have a pilot report passing a fix they had merely re-routed to. ⚠️ MSFSBA announces the passing and NEVER makes the call; which report to make is the pilot's job. → [da40.md](../da40.md)

## DA40A-16

- `BaseAircraftDefinition` implements NO readout hotkeys — every aircraft answers its own, from the CACHE, immediately. → [da40.md](../da40.md)

- **A new readout hotkey should be a GENERAL action, not an aircraft-specific one** - `ReadEngineRpm`, `ReadEnginePower`, `ReadEngineTemps` (output mode P, E, Shift+O) ask the same question on any propeller aeroplane and each definition answers it in its own terms: load percent and a single lever on a FADEC diesel, manifold pressure and mixture on a piston, torque and ITT on a turboprop. Same shape as the V-speed keys. ⚠️ Check a candidate letter against the WHOLE registration table before taking it - a global registration silently takes that letter from every other aircraft. → [da40.md](../da40.md)

## DA40A-17

- **⚠️ A HOTKEY CAN ONLY READ WHAT IS IN THE BATCH CACHE, and promoting a variable into it is NOT always safe.** The continuous batch SORTS BY SimVar NAME, so two batched keys on one SimVar shift every later variable's struct slot and corrupt the whole read. On the DA40 four engine readouts have OnRequest twins on the same SimVar (`DISP_LD`, `PROP_RPM_SENS:1`, `DISP_GT`, `DISP_FF`) which must STAY OnRequest, and standby airspeed/altitude share theirs with `DA40_AIRSPEED`/`INDICATED_ALTITUDE` which were already batched - so the hotkey reads the batched twin instead. `CowsDA40HotkeyCacheTests` now SCANS the hotkey source for every `ReadNow`/`Add` call and checks each, because the previous test listed the keys by hand and passed straight through Alt+S and Shift+F being shipped against thirteen OnRequest variables. → [da40.md](../da40.md)

## DA40A-18

- **⚠️ TWO `case HotkeyAction.X:` LABELS ON ONE BODY IS A WASTED KEY.** ReadFuelQuantity and ReadFuelInfo shared a case on the DA40, so F and Shift+F said the same sentence. Legal C#, invisible to the compiler, and invisible in the cockpit until someone presses both. `CowsDA40HotkeyCoverageTests` now fails on any adjacent pair. F answers the TANKS, Shift+F answers how long they last; NEITHER matches the MFD's Fuel Calculator, which is a totalizer the pilot sets with INC/DEC/RST FUEL and which counts down by fuel USED (measured: tanks 37.2 gal, calculator 32.2 remaining). → [da40.md](../da40.md)

## DA40A-19

- **An Airbus-named hotkey should do the equivalent JOB on a non-Airbus, not nothing.** The DA40 has no lower ECAM and no ISIS but has what those keys are for: Alt+S is the engine at a glance (load, RPM, oil, coolant, gearbox, flow, volts, amps, each with its arc) and Alt+I the standby instruments (including CAGED/TOPPLED gyro). Both render through `TryGetDisplayOverride`, never their own formatting, so a hotkey can never disagree with the panel beside it. → [da40.md](../da40.md)

## DA40A-20

- **The CRS knob sets the course of whatever the CDI is on** (one `AS1000_PFD_CRS_INC` each, measured on the NG, 2026-10-06): NAV 2 moved `NAV OBS:2` only, GPS moved `GPS OBS VALUE`. The typed Course wrote `VOR1_SET` regardless. `CourseWrite` chooses in RPN at write time from `GPS DRIVES NAV1` (1 on GPS) and `AUTOPILOT NAV SELECTED` (the NAV radio) - measured stepping the CDI softkey GPS, LOC1, LOC2: 1/1, 0/1, 0/2 - so a stale cache cannot send it to the wrong receiver. The three courses ride the settle announcer as "Course" and only `CdiCourseKeys()`'s speaks. Until `GPS DRIVES NAV1` and `AUTOPILOT NAV SELECTED` have both been delivered the source is unknown and all three may speak (assuming GPS dropped a NAV 2 course turned right after a connect), and a typed course marks all three as MSFSBA's own write. → [da40.md](../da40.md)

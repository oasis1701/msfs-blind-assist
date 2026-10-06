---
paths:
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Radios.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.RadioAnnounce.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.BaroAnnounce.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Standby.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Autopilot.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.ApDialogs.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Waypoint.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Units.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Hotkeys.cs"
  - "MSFSBlindAssist/Services/GpsWaypointSequencer.cs"
---
# COWS DA40 radios, altimeters, autopilot and hotkey rules

Loaded with the DA40 radio, altimeter, autopilot, waypoint, unit and hotkey code. Full text of each rule: docs/invariants/da40-avionics.md.

- [DA40A-1] The GFC 700 is the WT autopilot: only events its state manager intercepts work (`AP_VS_ON`/`AP_VS_OFF`, never `AP_VS_HOLD_*`); the ELT is `ELT_SET` 0 ARM / 1 ON; `COWS_KILL_FMA` is a status, never an option. Full: docs/invariants/da40-avionics.md#da40a-1
- [DA40A-2] The selected altitude counts as SELECTED only through the G1000's own key events: send one `AP_ALT_VAR_INC`, then write the exact value after it lands, or FLC is refused. Full: docs/invariants/da40-avionics.md#da40a-2
- [DA40A-3] The GFC 700 has no airframe variables; every mode writes the distinct ON or OFF event through `ExecuteCalculatorCodeUnique`, never `_TOGGLE`. Full: docs/invariants/da40-avionics.md#da40a-3
- [DA40A-4] The heading bug and course knob live on the GFC 700 panel, moved there, never copied: two panels showing one knob go stale. Full: docs/invariants/da40-avionics.md#da40a-4
- [DA40A-5] The GFC 700 pre-flight test (`AFCS_TEST`, `AFCS_PFT`) is announced; pressing AP DISC while it runs fails it. Full: docs/invariants/da40-avionics.md#da40a-5
- [DA40A-6] COM and NAV take raw hertz (`COM_STBY_RADIO_SET_HZ`, `NAV{n}_STBY_SET_HZ`) at full 8.33 kHz resolution; BCD16 rounded every 8.33 channel and is unused for tuning. Full: docs/invariants/da40-avionics.md#da40a-6
- [DA40A-7] A failed radio (`failed-instr`) is scanned for, never announced; COM 2 and NAV 2 are on the avionics bus and COM 1/NAV 1 are not, so a lit PFD never disproves an avionics-master cause. Full: docs/invariants/da40-avionics.md#da40a-7
- [DA40A-8] A key the pilot just pressed is read back over the Coherent socket after a frame, never in the same breath; uneven 8.33 kHz steps (710, 715, 725) are correct. Full: docs/invariants/da40-avionics.md#da40a-8
- [DA40A-9] A settled knob value uses `AnnounceImmediate`, never `Announce`: it fires after the pilot stopped turning and must not queue behind the display window. Full: docs/invariants/da40-avionics.md#da40a-9
- [DA40A-10] The standby altimeter reads the mirror `L:STATE_BARO2` and writes the input `L:KOHLSMAN SETTING HG:2` through the calculator; Ctrl+B sets both altimeters. Full: docs/invariants/da40-avionics.md#da40a-10
- [DA40A-11] Both altimeters announce an external change debounced 700 ms, skip MSFSBA's own writes for 3 s, and the timer checks `DA40DisabledMonitorVariablesSet` itself. Full: docs/invariants/da40-avionics.md#da40a-11
- [DA40A-12] The G1000's unit settings (and `baroHpa`) are WT user settings read from the units row; readouts follow them by declared unit, and a gauge band is looked up from the RAW value. Full: docs/invariants/da40-avionics.md#da40a-12
- [DA40A-13] Nav angle is read and deliberately NOT applied: the heading bug and course are written magnetic, so never show true without converting the write side. Full: docs/invariants/da40-avionics.md#da40a-13
- [DA40A-14] The G1000 has no radio page and is not missing one: tuning is on the PFD bezel, plus FREQ softkeys on the Nearest and WPT pages. Full: docs/invariants/da40-avionics.md#da40a-14
- [DA40A-15] Ctrl+W and the waypoint-passing call read the stock GPS SimVars the G1000 writes (`GPS WP PREV ID` is the fix just passed); a passing is TO becoming FROM, never an ident change, and is announced, never a call made for the pilot. Full: docs/invariants/da40-avionics.md#da40a-15
- [DA40A-16] `BaseAircraftDefinition` implements no readout hotkeys: each aircraft answers from the cache, and a new readout key is a general action (P, E, Shift+O) checked against the whole registration table. Full: docs/invariants/da40-avionics.md#da40a-16
- [DA40A-17] A hotkey reads only what is in the batch cache; never batch a key whose SimVar another batched key shares (`CowsDA40HotkeyCacheTests` scans the source). Full: docs/invariants/da40-avionics.md#da40a-17
- [DA40A-18] Two `case HotkeyAction.X:` labels on one body is a wasted key (`CowsDA40HotkeyCoverageTests`); F answers the tanks, Shift+F endurance. Full: docs/invariants/da40-avionics.md#da40a-18
- [DA40A-19] An Airbus-named hotkey does the equivalent job on the DA40 (Alt+S engine glance, Alt+I standby) through `TryGetDisplayOverride`, never its own formatting. Full: docs/invariants/da40-avionics.md#da40a-19
- [DA40A-20] The course is the CDI's: the CRS write picks `GPS_OBS_SET`, `VOR2_SET` or `VOR1_SET` in the sim at write time (`CourseWrite`), and only the CDI's course (`CdiCourseKeys`) speaks on settle, all three until the source is delivered; never write NAV 1's course whatever the CDI says. Full: docs/invariants/da40-avionics.md#da40a-20

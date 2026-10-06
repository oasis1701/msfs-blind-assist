---
paths:
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Electrical.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Lighting.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.LampWatch.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Doors.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.DoorAnnounce.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Breakers.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Brakes.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.FlightControls.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Trim.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Flaps.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Payload.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Options.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.CabinAir.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Elt.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Annunciators.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.PowerUpAnnounce.cs"
  - "MSFSBlindAssist/Aircraft/DA40/CowsDA40Definition.Audio.cs"
  - "MSFSBlindAssist/Aircraft/DA40/DA40BreakerPlacards.cs"
---
# COWS DA40 airframe rules (electrical, lights, doors, breakers, controls, options)

Loaded with the DA40 airframe partials. Full text of each rule: docs/invariants/da40-airframe.md.

- [DA40F-1] The DA40's own breakers are `L:CB_*` toggles (0 in, 1 pulled, 34 per variant) and are independent of the sim's per-bus breakers: a sim-level pulled breaker can kill a circuit while every `L:CB_*` reads in. Full: docs/invariants/da40-airframe.md#da40f-1
- [DA40F-2] `L:STATE_LIGHT_*` are save-state mirrors, not lamps; what gates a light is `CIRCUIT ON:n`. Announce a light fault in one short sentence and say nothing when it clears. Full: docs/invariants/da40-airframe.md#da40f-2
- [DA40F-3] The XLS master is a split rocker: `DA40_ELEC_ALT_MASTER` (XLS-only) applies the interlock from the state read BEFORE toggling, through `ExecuteCalculatorCodeUnique`; only `TOGGLE_ALTERNATOR1` moves it, `ALTERNATOR1_SET` is inert. Full: docs/invariants/da40-airframe.md#da40f-3
- [DA40F-4] `TOGGLE_AIRCRAFT_EXIT` goes through `ExecuteCalculatorCodeUnique` (event index = SimVar index + 1); a door announces Open or Closed on a settle, never a percentage; the canopy has no latch state to model. Full: docs/invariants/da40-airframe.md#da40f-4
- [DA40F-5] DA40 rudder trim is not adjustable: COWS writes `RUDDER TRIM PCT` every frame, so a `RUDDER_TRIM_SET` write is overwritten. Full: docs/invariants/da40-airframe.md#da40f-5
- [DA40F-6] DA40 brakes fade (authority drops above 400 C, 90 percent gone by 760 C) and there is no gauge, so per-wheel temperature and fade stay on the scan. Full: docs/invariants/da40-airframe.md#da40f-6
- [DA40F-7] Flight Controls reads SURFACE position, never the stick inputs, and never subtracts a bias: the stick is named beside the surface when they disagree (`DescribeElevatorStickAgreement`). Full: docs/invariants/da40-airframe.md#da40f-7
- [DA40F-8] The DA40 has no computed take-off trim: the wheel's T/O mark is physical and the AFM gives no number, so never add one. Full: docs/invariants/da40-airframe.md#da40f-8
- [DA40F-9] Refuelling is a ground-only, engine-off transaction (gallons per tank, fill both), refused otherwise; gate on `SIM_ON_GROUND`, never the OnRequest `DA40_ECU_PRE_ON_GROUND`. Full: docs/invariants/da40-airframe.md#da40f-9
- [DA40F-10] The model sets `L:GSX_PARK` (on ground, brake set, <=1 kt, both pedals >=25%); which GSX services a light single gets is GSX's decision, answered with GSX running. Full: docs/invariants/da40-airframe.md#da40f-10
- [DA40F-11] The COWS options list is variant-specific (the XLS adds Engage Starter w/ Mixture and Priming Assist); both settings sets (COWS options, WT G1000 settings) are written through the display window. Full: docs/invariants/da40-airframe.md#da40f-11
- [DA40F-12] The GMA 1347 keys are on neither display, so they are Audio panel controls; each is written through its `AS1000_MID_*` input event BY VALUE (1 on, 0 off; a second 1 does nothing), K events only as the fallback. MKR/MUTE is `MARKER BEACON TEST MUTE`, and a key with no push node in the model is not offered. Full: docs/invariants/da40-airframe.md#da40f-12

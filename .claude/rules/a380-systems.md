---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA380Definition*.cs"
  - "MSFSBlindAssist/Aircraft/A380*.cs"
  - "MSFSBlindAssist/Forms/FBWA380/**"
  - "tests/MSFSBlindAssist.Tests/**/*A380*.cs"
---
# FlyByWire A380X systems and panels rules

Loaded when Claude reads matching code. Background: docs/a380x.md. Full text of each rule: docs/invariants/a380-systems.md.

- [A380-1] The LT-TEST knob is render-only: never narrate what the bulbs would show; announce only the knob's own position and let real per-system fault lights announce genuine faults. Full: docs/invariants/a380-systems.md#a380-1
- [A380-2] Every A380 panel control renders as a COMBO, never a hardware button, except true one-shot momentary actions (ECAM-CP keys, chrono, calls, ATC ack) and the seat-motor `RenderAsButton` toggles [A380-10] prescribes; a control showing ongoing state is never a plain button. Full: docs/invariants/a380-systems.md#a380-2
- [A380-3] Never implement the FBW flyPad pushback controls; pushback is done with GSX, a permanent decision. Full: docs/invariants/a380-systems.md#a380-3
- [A380-4] Never treat the Surveillance pedestal panel as working (FBW has not implemented it); transponder AUTO mode and squawk are the only real controls, via the MFD SURV page. Full: docs/invariants/a380-systems.md#a380-4
- [A380-5] Before declaring an announced ARINC var untestable by injection, check for a per-frame writer: only vars the writer leaves alone are injectable; writer-owned vars need a live scenario. Full: docs/invariants/a380-systems.md#a380-5
- [A380-6] FBW buses and batteries publish as `A32NX_ELEC_{rawBusName}_BUS_IS_POWERED` with the raw bus id; never invent a descriptive name, confirm the id in the Rust source. Full: docs/invariants/a380-systems.md#a380-6
- [A380-7] Every A380 RMP calc-path write must be unique per call with a `{seq} 0 *` prefix: MobiFlight coalesces consecutive identical calc strings, dropping a repeated digit or a double LSK/ADK press. Full: docs/invariants/a380-systems.md#a380-7
- [A380-8] Every A32NX DCDU H-event fire (`FlyByWireDcduForm.FireDcduEvent`; the A380 has no DCDU) must be sequence-uniquified too, or the WILCO then SEND second press on the same slot is silently dropped. Full: docs/invariants/a380-systems.md#a380-8
- [A380-9] Seat-motor writes need a per-frame unique calc string (`<seq> 0 *` prefix); MobiFlight fires identical strings once, so the motor ticks once instead of running. Full: docs/invariants/a380-systems.md#a380-9
- [A380-10] Never periodically re-read and snap a combo whose synthetic motor var idles at 0 (it loops restart/stop/announce); use a `RenderAsButton` toggle so state changes only on the click edge. Full: docs/invariants/a380-systems.md#a380-10
- [A380-11] Every FBW unit/feature with an observable effect must be wired into MSFSBA's own read-outs; MSFSBA bypasses the cockpit displays, so a display-only conversion never reaches the pilot. Full: docs/invariants/a380-systems.md#a380-11
- [A380-12] Never regress the ROW/ROP and BTV rollout call-outs (safety call-outs, verifiable only live); decode `A32NX_ROW_ROP_WORD_1` with the current `RowRopWord1Bits` map (11 max braking, 12/13 reverse, 14/15 too short), as the PFD does. Full: docs/invariants/a380-systems.md#a380-12
- [A380-13] ECP keys (`L:A32NX_BTN_*`) are held `A380EcpKeyPulse.HoldMs` (250 ms) and released `ReleaseMs` (250 ms) between presses, on the one shared `A380EcpKeyPulse.Shared` clock reserving a slot at press start; never shorten them or guard per caller. Full: docs/invariants/a380-systems.md#a380-13
- [A380-14] When FBW moves a subsystem, diff removed tokens per commit against var `Name`s from the BUILT assembly, never source text; also check constant writers, changed writers, live reads (clear only) and stock names. (more: see full) Full: docs/invariants/a380-systems.md#a380-14
- [A380-15] The FBW-drift intersection must cover input EVENTS too (`Type == SimVarType.Event` and every `H:` literal): a deleted event fails silently. Pinned by `FlyByWireA380EventContractTests`. Full: docs/invariants/a380-systems.md#a380-15
- [A380-16] A readout-only var (a `TryGetDisplayOverride` decoder with no hotkey, window or auto-announce) must be in a panel display list, or it is never requested and its decoder never runs. Full: docs/invariants/a380-systems.md#a380-16
- [A380-17] Register the TCAS RA-guidance V/S bands as the `:1`/`:2` indexed L:vars, never only the unindexed names, which FBW never writes. Full: docs/invariants/a380-systems.md#a380-17
- [A380-18] Defer the TCAS RA-guidance compose (~800 ms), never synchronous off the state edge: FBW resets the V/S band vars only in STBY, so RA onset can speak the previous RA's sense. Full: docs/invariants/a380-systems.md#a380-18
- [A380-19] Register the TCAS `VSPEED_GREEN/RED:1/:2` and `RA_RATE_TO_MAINTAIN` L:vars with `Units="number"`, never a velocity unit: they are already fpm, and a velocity unit multiplies them by 196.85. Full: docs/invariants/a380-systems.md#a380-19
- [A380-20] Strip annunciators from panel DISPLAY variable sets; keep numeric/analog and 3+-state status fields. Full: docs/invariants/a380-systems.md#a380-20
- [A380-21] When a status has both a PB-light L:var and an ECAM memo, make the L:var `ReadEnumQuiet` and the memo the single call-out; never announce one condition from two sources. Full: docs/invariants/a380-systems.md#a380-21
- [A380-22] Master Warning/Caution acknowledge must pulse the exact glareshield L:var name, misspelled `MASTERAWARN` on the A380; the plausible spelling is a silent no-op that never clears the aural. Full: docs/invariants/a380-systems.md#a380-22
- [A380-23] A380 overhead PBs (packs, bleeds, fans, hyd pumps, bus-tie, galley, PTU, exit sign) are settable via the calculator path; `XMLVAR_` sign combos use the OVHD calc catch-all. Test FBW L:var writes via the calculator path, never `set_lvar`. Full: docs/invariants/a380-systems.md#a380-23
- [A380-24] Multi-position switches stay multi-position combos, never On/Off: Nose light T.O./Taxi/Off, Seat Belts ON/AUTO/OFF. `LIGHTING_LANDING_1` and `XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position` hold the position but drive nothing: actuate via indexed `LANDING_LIGHTS_SET`/`TAXI_LIGHTS_SET` and `CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE`. Full: docs/invariants/a380-systems.md#a380-24
- [A380-25] A380 wing anti-ice is the stock `K:TOGGLE_STRUCTURAL_DEICE` / `A:STRUCTURAL DEICE SWITCH`, never the A32NX's L:var (a dead mirror), sent by `SendEvent` via `A380ToggleCommand.ShouldFire`. Never infer one FBW airframe's wiring from the other's. Full: docs/invariants/a380-systems.md#a380-25
- [A380-26] SD-page row registration classifies by FBW prefix, not space/colon: a colon-indexed FBW L:var (`A32NX_FUEL_USED:n`) is an L:var; only names without an FBW prefix register as stock SimVars. Full: docs/invariants/a380-systems.md#a380-26
- [A380-27] Frequency readouts need explicit formatting: stock `COM ACTIVE/STANDBY FREQUENCY:n` needs a "0.000 MHz" display override, and ND ADF/VOR need kHz/MHz unit labels. Full: docs/invariants/a380-systems.md#a380-27
- [A380-28] "Passengers on Board" sums the per-station `A32NX_PAX_<st>_DESIRED` bitmasks, never the boarded `A32NX_PAX_<st>` set, which lags and under GSX boarding settles below target. Full: docs/invariants/a380-systems.md#a380-28
- [A380-29] Wipers are a synthetic 3-position OFF/SLOW/FAST combo per side (circuits 141/143): read BOTH `CIRCUIT SWITCH ON` and `CIRCUIT POWER SETTING` (75/100%), switch first, since power stays 100% while off. Full: docs/invariants/a380-systems.md#a380-29

Mirrored from a380-coherent.md and takeoff-and-callouts.md (they govern code in the A380 definition; change them there and here together):
- [A380C-4] Never construct a second `CoherentDisplayClient("A380X_EWD")` while `EwdMonitor` exists; the SD Upper-E/WD fallback goes through the one always-on monitor socket. Full: docs/invariants/a380-coherent.md#a380c-4
- [A380C-16] Keep `A.DOOR_NAMES` (flyPad agent) in sync with each aircraft def's `_doorDefs` table, so the flyPad label and the spoken door name agree. Full: docs/invariants/a380-coherent.md#a380c-16
- [A380C-18] Never re-add an A380 RMP "Radios" panel on stock COM standby-set/swap events, which the FBW A380 ignores; anything else tuning COM with stock events must ask `IAircraftDefinition.StockComTuningRefusal` first and speak it. Full: docs/invariants/a380-coherent.md#a380c-18
- [TKO-5] Take-off calls are ONE `AnnounceImmediate` per sample via `TakeoffVSpeedCallouts.Compose`, never one per call; every definition drops the ARM, never the speeds, on reconnect AND `OnSimContextReset`, and names keys through the one `TakeoffCalloutKeys` (more: see full). Full: docs/invariants/takeoff-and-callouts.md#tko-5

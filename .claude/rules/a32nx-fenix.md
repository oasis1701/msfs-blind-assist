---
paths:
  - "MSFSBlindAssist/Aircraft/FlyByWireA320Definition*.cs"
  - "MSFSBlindAssist/Aircraft/FenixA320*.cs"
  - "MSFSBlindAssist/Aircraft/HeadwindA330Definition.cs"
  - "MSFSBlindAssist/Services/FbwMcdu*.cs"
  - "MSFSBlindAssist/Services/Fenix*.cs"
  - "MSFSBlindAssist/Services/FlyByWire*.cs"
  - "MSFSBlindAssist/SimConnect/CoherentA32nxMcduClient.cs"
  - "MSFSBlindAssist/Forms/FBWA320/**"
  - "MSFSBlindAssist/Forms/FlyByWireA320/**"
  - "MSFSBlindAssist/Resources/coherent-a32nx-*.js"
  - "MSFSBlindAssist/Forms/Fenix*/**"
  - "MSFSBlindAssist/Aircraft/Fcu*.cs"
  - "MSFSBlindAssist/SimConnect/CoherentEvalClient.cs"
  - "MSFSBlindAssist/SimConnect/CoherentLinkState.cs"
  - "MSFSBlindAssist/SimConnect/CoherentViewOwnership.cs"
  - "tests/MSFSBlindAssist.Tests/**/*A32nx*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Fenix*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FbwMcdu*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Fcu*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FlyByWireMCDU*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FbwAutothrust*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*FbwVSpeed*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CoherentLinkState*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*CoherentViewOwnership*.cs"
  - "MSFSBlindAssist/Aircraft/ArmedAltitudeMode.cs"
  - "MSFSBlindAssist/Aircraft/WiperPosition.cs"
---
# FlyByWire A32NX and Fenix A320 rules

Loaded when Claude reads matching code. Background: docs/a32nx.md. Full text of each rule: docs/invariants/a32nx-fenix.md.

- [A320-1] Do NOT build an A32NX circuit-breaker panel: only ONE breaker (49VU E12) is modelled as clickable; the 196 positions in the `A32NX_CB_*_TRIPPED_*` bitmasks are SDAC monitoring inputs with no 3-D object or name. Full: docs/invariants/a32nx-fenix.md#a320-1
- [A320-2] The A32NX C/B TRIPPED cautions need a monitored bit AND flight phase 1/2/6 AND a full 60-second confirm; don't declare them broken after a short wait. Full: docs/invariants/a32nx-fenix.md#a320-2
- [A320-3] Every Fenix panel pushbutton must get a full PRESS-RELEASE pulse (0→1→0), never press-only: a press-only pulse leaves the button held down for the whole session (the stuck TO CONFIG / ECAM STATUS bug). Full: docs/invariants/a32nx-fenix.md#a320-3
- [A320-4] Do not revert `ExecuteButtonTransition` to the press-only form: the release is safe for all ~150 Fenix buttons, since systems latch on the 0→1 rising edge into a separate indicator var. Full: docs/invariants/a32nx-fenix.md#a320-4
- [A320-6] The A32NX Flight Director controls are the `A32NX.FCU_EFIS_{L,R}_FD_PUSH` events, with state `A32NX_FCU_EFIS_{L,R}_FD_LIGHT_ON` (`_FD_ACTIVE` does not exist), NOT `TOGGLE_FLIGHT_DIRECTOR`/`A320_Neo_FCU_FD_n_PUSH`/`A380X_EFIS_L_FD_BUTTON_IS_ON`; those genuinely fail, so don't re-conclude "uncontrollable". Full: docs/invariants/a32nx-fenix.md#a320-6
- [A320-7] Never test A32NX overhead L:var writes via the unreliable data-def `SetLVar` path: the calculator path sticks for all of them, and the old "computed outputs that revert" verdict was an artifact of the wrong path. Full: docs/invariants/a32nx-fenix.md#a320-7
- [A320-8] The `A32NX_RMP_{L,R}_VHF{n}_VOLUME` L:vars do not exist in dev FBW; do not re-add the ACP volume combos, the physical ACP is unmodeled. Full: docs/invariants/a32nx-fenix.md#a320-8
- [A320-9] The armed-ALT constraint qualifier is the SSM of `A32NX_FMGC_{1,2}_FM_ALTITUDE_CONSTRAINT` (both FMGCs ORed), not a discrete bit; never port the A380 rule by assumption. Anything spoken from a callback outside `ProcessSimVarUpdate` must check `A32NXDisabledMonitorVariablesSet` itself. (more: see full) Full: docs/invariants/a32nx-fenix.md#a320-9
- [A320-10] The FMS's post-takeoff clear of V1/VR/V2 is consumed SILENTLY once `A32NX_FMGC_FLIGHT_PHASE` is TAKEOFF or later, never "past TAKEOFF"; a clear before takeoff thrust is still spoken. `SimVarDefinition.IsNotSet` is the ONE "not set" test. Full: docs/invariants/a32nx-fenix.md#a320-10
- [A320-11] The A32NX predicted takeoff pitch-trim sign is INVERTED vs the A380 (`-ths`, negative = nose up); never copy a sign convention between the two FMSes without checking the writer. Full: docs/invariants/a32nx-fenix.md#a320-11
- [A320-12] FUEL MODE SEL junction options are 1-based (`t+1`, not `t`): the raw toggle value selects the wrong option even though the L:var/light appears to follow. Full: docs/invariants/a32nx-fenix.md#a320-12
- [A320-13] The evacuation-horn shut-off L:var write is ONE-WAY; never pulse it back to 0, that resumes the horn. Full: docs/invariants/a32nx-fenix.md#a320-13
- [A320-14] Momentary FBW L:var button pulses must be TWO SEPARATE calc calls, never one same-frame `1 (>L:X) 0 (>L:X)` string: the Rust sampler doesn't see a same-tick pulse. Full: docs/invariants/a32nx-fenix.md#a320-14
- [A320-15] The A32NX DCDU display must be a ListBox, not a multiline TextBox: in a TextBox a right-aligned key label reads on a separate braille line from its leading key number. Full: docs/invariants/a32nx-fenix.md#a320-15
- [A320-16] Map a DCDU soft-key slot by POSITION (a Y-threshold), never by simple L/R order: an empty-state key can be alone on a side yet still live on the second slot. Full: docs/invariants/a32nx-fenix.md#a320-16
- [A320-17] DCDU page-scroll direction must be DOWN=forward everywhere: answer keys stay inactive until the pilot pages to the end of a multi-page uplink, so an inverted direction silently blocks answering. Full: docs/invariants/a32nx-fenix.md#a320-17
- [A320-18] Plain PageUp/PageDown scroll WITHIN the displayed DCDU message (`POEMINUS`/`POEPLUS`) and Ctrl+PageUp/Down step between messages, never the reverse: every CDU window scrolls on plain PageUp/Down, and within-message paging unlocks the answer keys. Full: docs/invariants/a32nx-fenix.md#a320-18
- [A320-19] Check the per-DCDU-key ACTIVE flags before firing; an inactive key must never falsely confirm a press. Full: docs/invariants/a32nx-fenix.md#a320-19
- [A320-20] An inactive DCDU key must not dead-end on "read to the end first" (FBW loses `reachedEndOfMessage`): re-assert with `POEPLUS` and retry, but ONLY when the scraped page counter shows nothing left to read; never page past unread text. Full: docs/invariants/a32nx-fenix.md#a320-20
- [A320-21] A32NX FCU baro polarity is PULL=STD/PUSH=QNH, the opposite of the A380's PUSH=STD/PULL=QNH; never harmonize them. Full: docs/invariants/a32nx-fenix.md#a320-21
- [A320-22] FCU dial callouts (A32NX, Headwind A330, A380) listen only to sources that say themselves whether the window shows a selection, never the `A32NX_FCU_AFS_DISPLAY_*_VALUE` values; changes are STAGED, released at batch end only while the FCU is available. (more: see full) Full: docs/invariants/a32nx-fenix.md#a320-22
- [A320-23] The A32NX autobrake set must use the MobiFlight calculator path, never `SetLVar` (the data-def write is unreliable for this FBW L:var). Full: docs/invariants/a32nx-fenix.md#a320-23
- [A320-24] Never re-fold the A380's metric-altitude (MTRS) feature into the A32NX: the real A320 has no MTRS button (A330+/A380 only). Full: docs/invariants/a32nx-fenix.md#a320-24
- [A320-25] A32NX approach minimums read the plain-feet `AIRLINER_*` L:vars, never the `A32NX_FM1_*` ARINC words: those are NCD until near destination, so a gate-entered MDA/DH read "Not set". Full: docs/invariants/a32nx-fenix.md#a320-25
- [A320-26] A32NX wing anti-ice writes `A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION` (the Rust input), never `_SYSTEM_SELECTED`: that is a Rust per-frame OUTPUT and any write reverts. Full: docs/invariants/a32nx-fenix.md#a320-26
- [A320-27] A32NX nose/landing lights use the indexed stock events in the FBW template's verbatim RPN form `<value> <index> r (>K:2:LANDING_LIGHTS_SET/TAXI_LIGHTS_SET)`; the `LIGHTING_LANDING_x` L:vars drive nothing. Keep the template-verbatim form. Full: docs/invariants/a32nx-fenix.md#a320-27
- [A320-28] A32NX wipers are circuits 77 (Capt) / 80 (F/O), not the A380's 141/143; OFF/SLOW/FAST needs BOTH circuit switch AND power (power rests at 100% while off), and `XMLVAR_A320_WiperSwitch_*` does not exist in FBW. Full: docs/invariants/a32nx-fenix.md#a320-28
- [A320-29] A32NX seat belts is genuinely 2-position ON/OFF in the FBW model (no AUTO, unlike the A380); don't "fix" it to 3-position. Full: docs/invariants/a32nx-fenix.md#a320-29
- [A320-30] A32NX "Passengers on Board" sums the `A32NX_PAX_{A..D}_DESIRED` planned bitmasks, not the lagging boarded set. The station list is PER AIRFRAME (`PaxStationVars`; the Headwind A330 has ten, A..J): fix a gap by registering, never by whitelisting in the station-agnostic consumer. Full: docs/invariants/a32nx-fenix.md#a320-30
- [A320-31] The Fenix MCDU marks selection with cyan AND large font: never gate the `*` marker on green alone nor broaden the colour test to cyan; detect it in `FenixMcduFormat`'s conservative size rule, run after the colour rule. Keep `SpecialChars`' `\uXXXX` escapes. (more: see full) Full: docs/invariants/a32nx-fenix.md#a320-31
- [A380-8] Every A32NX DCDU H-event fire (`FlyByWireDcduForm.FireDcduEvent`; the A380 has no DCDU) must be sequence-uniquified too, or the WILCO then SEND second press on the same slot is silently dropped. Full: docs/invariants/a32nx-fenix.md#a380-8
- [A320-36] FCU V/S and FPA callouts read the ARINC words (`FcuSources`: A32NX `A32NX_FCU_SELECTED_{VERTICAL_SPEED,FPA}`, A380 `A32NX_PRIM_1_SELECTED_*`), never the `A32NX_AUTOPILOT_{VS,FPA}_SELECTED` shims: unlike the heading/speed shims they never read -1 when dashed (live value on the A32NX, 0 on the A380). Full: docs/invariants/a32nx-fenix.md#a320-36
- [A320-37] FCU dial callouts are released by `BaseAircraftDefinition.OnContinuousBatchDelivered`, OUTSIDE MainForm's `announcer.Suppressed` wrap: every `AnnounceFcuValue` caller passes `muted:` from its own Ctrl+M set (`A32NX`/`A380DisabledMonitorVariablesSet`) or a pending readout; never rely on the wrap. Full: docs/invariants/a32nx-fenix.md#a320-37
- [A320-38] Every MSFSBA-origin FCU write arms its dial-callout echo BEFORE the send, keys from `FcuEchoKeys.For(evt, FcuSources, FcuConfirmation)` (`ArmFcuEchoFor`, `OnPanelButtonFiring`; the calc-code V/S set arms the same two directly); a queued dotted event re-arms when `FlushPendingCalcEvents` sends it (`QueuedEventDispatched`). Full: docs/invariants/a32nx-fenix.md#a320-38
- [A320-39] `SwitchAircraft` starts the new definition's FCU callout settle (`BeginFcuValueSettle`) when the switch falls within `AircraftLoadSettleWindowMs` (60 s, a judgement) of `AircraftLoaded`; without it a loading flight's first published FCU values are spoken as knob turns. Full: docs/invariants/a32nx-fenix.md#a320-39

Mirrored from flypad.md (it governs `_doorDefs` in FlyByWireA320Definition.cs; change it there and here together):
- [A380C-16] Keep `A.DOOR_NAMES` (flyPad agent) in sync with each aircraft def's `_doorDefs` table, so the flyPad label and the spoken door name agree. Full: docs/invariants/flypad.md#a380c-16

Mirrored from a380-systems.md (they govern the TCAS RA registrations in FlyByWireA320Definition.cs; change them there and here together):
- [A380-17] Register the TCAS RA-guidance V/S bands as the `:1`/`:2` indexed L:vars, never only the unindexed names, which FBW never writes. Full: docs/invariants/a380-systems.md#a380-17
- [A380-18] Defer the TCAS RA-guidance compose (~800 ms), never synchronous off the state edge: FBW resets the V/S band vars only in STBY, so RA onset can speak the previous RA's sense. Full: docs/invariants/a380-systems.md#a380-18
- [A380-19] Register the TCAS `VSPEED_GREEN/RED:1/:2` and `RA_RATE_TO_MAINTAIN` L:vars with `Units="number"`, never a velocity unit: they are already fpm, and a velocity unit multiplies them by 196.85. Full: docs/invariants/a380-systems.md#a380-19

Mirrored from first-officer-airbus.md (they govern code in FlyByWireA320Definition and HeadwindA330Definition; change them there and here together):
- [FOA-4] An Event-typed key with no `varKey ==` branch in `FlyByWireA320Definition.HandleUIVariableSet` is a SILENT DEAD WRITE that reports success: both FBW FO executors REFUSE an unclaimed Event key, swept by `FoFbwUnclaimedEventKeyTests`. The FD pushes stay GUARDED on `A32NX_FCU_EFIS_{L,R}_FD_LIGHT_ON` (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-4
- [FOA-5] The A330 panel inherits the A32NX's, so `HeadwindA330Definition.BuildPanelControls` overrides three controls: the `LIGHTING_LANDING_2/_3` rows become the read-back `LIGHT LANDING:2`, and pots 10/11 (the A339X ceiling and map lights) are dropped. Never over-apply either to the A32NX. Full: docs/invariants/first-officer-airbus.md#foa-5
- [FOA-6] `A32NX_SPEEDS_LANDING_CONF3` is registered by `FlyByWireA320Definition` as `OnRequest` (like its `A32NX_SPEEDS_*` siblings) and polled by the FBW-family FO evaluators; never `Continuous` without `IsAnnounced`, an unbacked declaration that leaves a panel combo listing it blank (more: see full). Full: docs/invariants/first-officer-airbus.md#foa-6

# A32NX (FlyByWire A320) feature-parity TODO

Everything done for the **A380** that still needs porting/checking on the **FlyByWire A32NX**
(`FlyByWireA320Definition.cs`). This is the single source of truth for the A320 parity work.

**Workflow:** do ONE task at a time, verify it live, then **delete that task from this file** and
commit. Build with `-p:Platform=x64` (or the .sln) — a bare `dotnet build csproj` outputs AnyCPU
to `bin\Debug` and you'll launch a STALE `bin\x64` exe.

**Golden rules (apply to every task):**
- VERIFY each var name against the A32NX source / live MCP — do NOT blind-copy A380 var names.
- Write-stick test via the calculator path (`(>L:VAR)`), never `set_lvar`.
- A display var must NOT reuse one of MainForm's special-announce keys (`GROSS_WEIGHT_KG`,
  `FUEL_QUANTITY*`, `FLAP_POSITION`, `GEAR_POSITION`, `SPEED_*`, `OUTSIDE_TEMP`, `SQUAWK_CODE`,
  `ALTITUDE_*`, `AIRSPEED_*` — MainForm.cs ~1099) or the status box auto-announces it on every
  refresh. Use a distinct `PFD_*`/`ND_*` key (the A380 GW display key is `PFD_GROSS_WEIGHT`).
- Stock SimVar names (with a space or colon) MUST register as `Type = SimVar`, never L:var.

---

## A. Connection-safety / shared

- [ ] **SimConnect-ceiling strengthening — live A32NX connect check.** The strengthening is in the
  SHARED `SimConnectManager` (batch-covered vars skip their individual def). Architecturally
  identical, fewer continuous vars. Just confirm a clean A32NX connect + check `registration.log`
  (`approxTotalDefs` well under 1000, FULLY CONNECTED). (FCU `ExcludeFromBatch` regression fix is
  ALREADY applied to the A32NX + Fenix — done.)

## B. Displays (status boxes)

- [ ] **PFD box additions** (port from A380, distinct keys, verify each var live):
  transition LEVEL (`A32NX_FM1_TRANS_LVL`, ARINC, decode to "flight level N"), FCU selected
  ALT/HDG (stock `AUTOPILOT ALTITUDE LOCK VAR:3` / `HEADING LOCK DIR`), SAT/TAT
  (`A32NX_ADIRS_ADR_1_STATIC/TOTAL_AIR_TEMPERATURE`, ARINC celsius). Check the A320 PFD box
  doesn't already have GW under a colliding key.
- [ ] **ND box additions:** tuned nav-radio frequencies (VOR 1/2 `NAV ACTIVE FREQUENCY:1/2` + DME,
  ADF 1/2 `ADF ACTIVE FREQUENCY:1/2`). (A320 ND labels already have friendly DisplayNames — no
  raw-key bug there. Idents stay in the Output+N hotkey.)
- [ ] **Full A32NX display audit (PFD/EWD/ND/SD/ISIS) — STILL OWED.** Run the same multi-agent
  sweep done on the A380 (dump every status box, classify genuine-vs-bug, fix). The A320 EWD has
  no decoded thrust/N1 path yet (scrape-only) — port the A380 EWD decode incl. computed THR%
  (ThrustGauge formula: `clamp01((N1-idle)/(toga-idle))*(1-off)+off`, off=0.042 when
  ENGINE_STATE==1; vars `A32NX_AUTOTHRUST_THRUST_LIMIT_IDLE/_TOGA`, `A32NX_ENGINE_N1:n`).
- [ ] **PRESS overhead panel** — verify the A320 overhead PRESS readouts use the CPC ARINC words,
  not dead `_ANIM`/`_AUTO_LANDING_ELEVATION` (the A380 had Landing Elev + Outflow Valves reading 0;
  fixed to `FM1_LANDING_ELEVATION` + `OUTFLOW_*_B1`). A320 uses CPC words already — just confirm.

## C. Controls

- [ ] **Doors → read-only auto-announced status (remove combos).** The A32NX has working door
  COMBOS; give them the A380's read-only-status treatment (NO settable combos — open/close via the
  flyPad). Register every `INTERACTIVE POINT OPEN:n` as `Type = SimVar`; re-map the ip indices +
  door count from the A32NX `flight_model.cfg [INTERACTIVE POINTS]` (do NOT copy the A380 map).
  Cargo doors: check the A32NX's own LOCKED vars. Keep the real `A32NX_SLIDES_ARMED` readout.
- [ ] **Pushback panel REMOVED + Aircraft Preset panel REMOVED** (load from flyPad). Remove the
  `A32NX_PUSHBACK_*` controls and the preset LOAD combo from the A320; make the preset LOAD var
  OnRequest + not-announced (else it announces "...: None" on reset). Add the preset PROGRESS
  auto-announce ("Aircraft preset loading N percent" / "complete" — `A32NX_AIRCRAFT_PRESET_LOAD_PROGRESS`
  Continuous+IsAnnounced + a milestone-throttled ProcessSimVarUpdate branch).
- [ ] **Ground Equipment combos → push-BUTTONS.** Use the `EvtBtn` pattern (RenderAsButton var NOT
  added to `_momentaryButtons`, so HandleUIVariableSet falls through to the stock-event branch).
  Jetway/stairs `TOGGLE_JETWAY`/`TOGGLE_RAMPTRUCK`; trucks `REQUEST_FUEL_KEY`/`_LUGGAGE`/`_CATERING`.
  Add the `JETWAY MOVING` readout (stock continuous SimVar).
- [ ] **Oxygen Timer Reset combo → button** (`Btn`), if the A320 has it.
- [ ] **Fire-test aural cancel** — on Fire Test / Cargo Smoke Test OFF, also acknowledge master
  warning so the CRC stops. A320 `CLEAR_MASTER_WARNING` already pulses `PUSH_AUTOPILOT_MASTERWARN_L`
  (no extra-A typo unlike the A380); confirm the A320 fire-test var name + that the pulse clears CRC.
- [ ] **Rudder-trim nudge buttons** — stock `RUDDER_TRIM_LEFT`/`RUDDER_TRIM_RIGHT` (port directly).
  Plus readouts nosewheel angle `A32NX_NOSE_WHEEL_POSITION` (0.5=centred, ×140°) + tiller
  `A32NX_TILLER_HANDLE_POSITION` (±1) — verify they exist on the A32NX.
- [ ] **Cockpit sliding windows + flight-deck door + sunshades** — A380 uses plain L:vars
  `CPT_SLIDING_WINDOW`/`FO_SLIDING_WINDOW`/`COCKPITDOOR_OPEN` + `SUNSHADE_CPT/FO_OPENING`
  (calc-path write). The A32NX names LIKELY DIFFER — re-map from the A32NX `interactive-parts.xml`.
  `A32NX_CABIN_READY` is shared and already auto-announces (Mon).

## D. Radios / formatting (most already OK on A320 — just verify)

- [ ] Squawk readout BCD decode — only if a squawk read-back is added to the A320 (it currently
  only has `TRANSPONDER_CODE_SET` input). The A380 decode: nibbles of `TRANSPONDER CODE:1`.
- [ ] VHF COM freq — A320 ALREADY uses `"kHz"` units (formats "123.450 MHz"); no fix.
- [ ] RMP — A320 has no `FBW_RMP_FREQUENCY_*` vars; N/A.

## E. Not feasible / skip (recorded so they aren't re-attempted)

- Escape-slide door ARM/DISARM as a CONTROL — not modelled (A32NX only has the read-only
  `A32NX_SLIDES_ARMED`; no settable per-door arm). Keep the readout.
- PFD CG readout — Gus already added GW-CG to the A320 W/Shift+W readouts; no PFD CG line.

## G. NEW A380 additions (2026-06-03 exhaustive pass) — port + verify on A32NX

The A380 had a full "add everything addable" pass. Port each to the A32NX **and verify the var name
exists on the FBW A32NX first** — many `A32NX_`-prefixed vars are shared, but some are A380-only
(4 engines, CPIOM, FQMS, 4 OCSM, A380X_RMP) and the A320 equivalent differs or doesn't exist. Do one
at a time, verify live, delete the line.

### G1. SD-page additions (A380 `A380SdRows`, mostly shared `A32NX_` vars)
- [ ] **FUEL valve/pump layer** — engine LP / crossfeed / jettison valves (stock `FUELSYSTEM VALVE OPEN:n`;
  **A320 indices differ** — re-map from the A320 `flight_model.cfg`), + pump-running bits. The A320 has
  no FQMS word (`A32NX_FQMS_*_FUEL_PUMP_RUNNING_WORD` is A380-only) — the A320 uses per-pump
  `A32NX_FUELSYSTEM_PUMP_*` / `CIRCUIT CONNECTION ON:n`; **verify + re-map**.
- [ ] **ELEC contactors + battery direction + gen-OFF** — `A32NX_ELEC_CONTACTOR_*_IS_CLOSED` (A320 has
  fewer: 2 gens not 4, different contactor IDs — verify), battery charge/discharge from current sign,
  `A32NX_OVHD_ELEC_BAT_{1,2}_PB_IS_AUTO`.
- [ ] **Controller channel-failure flags** — A320 has FDAC/TADD/VCM/OCSM? The A320 air-con/press
  architecture differs (no CPIOM/OCSM ×4). **Verify which exist**; skip the A380-only ones.
- [ ] **APU avail/master/flap memo**, **escape slides** (`A32NX_SLIDES_ARMED` shared), **CRUISE fill**
  (fuel-used + cargo temps — A320 has 1 cargo zone, fewer deck zones; re-map), **HYD pump
  section-switch/fire-valve/elec-OFF-PB** (A320 is Green/Blue/Yellow, 3 systems — totally different
  pump map; re-derive from the A320 HydPage source).

### G2. EWD additions
- [ ] **Autothrust mode-message** (THR LK / LVR TOGA/CLB/MCT/ASYM via `A32NX_AUTOTHRUST_MODE_MESSAGE`,
  shared enum) + **reverser deployed** (`A32NX_AUTOTHRUST_REVERSE:n` — A320 has 2 engines, both reverse;
  use `:1`/`:2`).

### G3. PFD/ND/ISIS status-box additions (mostly shared)
- [ ] **PFD**: managed speed (`A32NX_SPEEDS_MANAGED_PFD`), preselect speed/Mach (`A32NX_SpeedPreselVal`/
  `A32NX_MachPreselVal`), selected V/S (`A32NX_AUTOPILOT_VS_SELECTED`), expedite (`A32NX_FMA_EXPEDITE_MODE`),
  flight directors (`AUTOPILOT FLIGHT DIRECTOR ACTIVE:1/2`), autobrake mode. All shared — verify + add to the A320 `d["PFD"]` + TryGetDisplayOverride decodes.
- [ ] **ND**: GS/TAS/wind (`A32NX_ADIRS_IR_1_GROUND_SPEED` / `A32NX_ADIRS_ADR_1_TRUE_AIRSPEED` /
  `_WIND_DIRECTION_BNR` / `_WIND_SPEED_BNR`, ARINC) into the monitored `d["ND"]`, heading-reference
  (`A32NX_FMGC_TRUE_REF`), cross-track L/R formatting. All shared.
- [ ] **ISIS**: `A32NX_ISIS_BUGS_ACTIVE` friendly label (bug VALUES + ATT-10s are JS-only — not modelled).

### G4. Auto-announces (shared enums)
- [ ] TCAS mode (`A32NX_TCAS_MODE`) + TCAS fault (`A32NX_TCAS_FAULT`) + FMA speed-protection
  (`A32NX_FMA_SPEED_PROTECTION_MODE`) + FMA mode-reversion (`A32NX_FMA_MODE_REVERSION`). All shared.

### G5. Controls
- [ ] **APU auto-exit test** (`A32NX_APU_AUTOEXITING_TEST_ON`), **emer-gen test** (`A32NX_EMERELECPWR_GEN_TEST`),
  **lighting preset load/save** (`A32NX_LIGHTING_PRESET_LOAD/_SAVE`) — all shared L-vars, add as buttons.
- [ ] **RMP transmit selectors** — A380-only (`A380X_RMP_*`). The A320 RMP is different (`A32NX_RMP_*` or
  the legacy ACP); **verify the A320 RMP/ACP var scheme** before porting transmit selects.

### G6. Correctness
- [ ] **B1→B4 CPCS** best-source for PRESS/CRUISE — A320 has a different pressurization system (single CPC,
  no B1-B4 CPIOM). **Likely N/A** — verify; the A320 may already be correct with its single source.
- [ ] **SEC1→SEC3 rudder-trim** fallback — A320 has 2 ELACs/3 SECs but a different rudder-trim source;
  verify the A320 rudder-trim word + whether a fallback applies.

---

## F. A380 base — all items RESOLVED (2026-06-03)

The A380 has no open items left. For the record, the final disposition of everything that was
once on this list:

IMPLEMENTED + verified: EWD computed THR%, EWD BLEED line + IDLE memo, beta-target (+ active
announce), TCAS RA VS-band, SAT/TAT, FCU selected ALT/HDG, transition LEVEL, nav-radio freqs,
EFIS preselect-QNH made SETTABLE (numeric input), the clock ": Idle" cosmetic fix.

VERIFIED already-working (no change needed): chronometer (CHR advances on toggle) AND the ET
elapsed-time counter (advances once its ET switch is set to Run — it was just at Stop); FMGC
flight-phase + Cabin Ready already auto-announce.

CLOSED — covered elsewhere / not worth a dedicated path:
- ILS/VOR/ADF tuned-station IDENTS — delivered via the Output+N nav-radio hotkey (NAV IDENT
  STRING256 struct). A display readout would need a Coherent scrape; the hotkey covers it.
- ND chrono — the Clock panel chronometer (verified working) covers timing; the ND chrono is a
  JS-only duplicate (no SimVar).
- ETA/EFOB at destination — needs a `flightInfo()` JS-agent change AND is in-flight-only
  (predictions are null on the ground), so it can't be implemented + verified from a cold gate.
  Left for an in-flight session.
- ACP TX channels / pushback Call-Release Tug — need write plumbing + state-machine testing
  (mirror-var/name mismatch per the finish-pass); audited, not cleanly feasible now.

### 2026-06-03 EXHAUSTIVE "add everything addable" pass — DONE

A full survey (3-agent sweep of EWD/SD, PFD/ND/ISIS, flyPad/MFD, the whole cockpit L-var surface)
was run and **every actionable item implemented** (see section G for the A32NX port list). Added:
SD fuel valve/pump layer + ELEC contactors/battery-direction + TR contactors + COND/BLEED/PRESS
controller channel-failure flags + APU avail/master + cabin-VS mode + escape slides + HYD pump
section-switch/fire-valve/elec-OFF-PB + CRUISE fuel-used/cargo-temps/landing-elev; EWD autothrust
mode-message + inboard reverser; PFD managed/preselect speed + selected V/S + expedite + FD 1/2 +
autobrake; ND GS/TAS/wind/heading-ref + cross-track L/R; ISIS bugs label; TCAS mode/fault + FMA
speed-protection/mode-reversion auto-announces; RMP transmit selectors (both ACPs); APU auto-exit
test + emer-gen test + lighting preset load/save; **B1→B4 CPCS + SEC1→SEC3 rudder-trim correctness
fixes**. New build connects FULLY CONNECTED at approxTotalDefs~602 (ceiling 1000), 0 capped. The
standalone "addable backlog" doc was retired; the live-flight audit checklist is now
`docs/live-flight-audit-checklist.md`.

GENUINELY NOT MODELLED on this FBW A380 dev build (do-not-chase — recorded so they're not
re-attempted): crew/cabin oxygen PSI (hardcoded 1829/1854), ENG nacelle temp, tire pressure (220),
WING ACCU / A-SKID / BRK-STEER-LG computers, FUEL collector cells, F/CTL SFCC computer health, HYD
reservoir normal-filling band, AVNCS/BULK cargo doors, all cargo SMOKE/OVHT + trim-air duct "H",
PRESS avncs/cab-air extract valves, CIDS/cabin-lighting-scenes/water-waste/service-interphone (no
L-vars — flyPad/EFB only), individual cockpit dome/flood light knobs, OIS/laptop power,
ASU/SATCOM/ACARS/DLS/printer (EFB-side), **ANP on ND** (FBW publishes only RNP), ISIS bug VALUES +
ATT-10s flag (JS-only), PFD autoland-capability (FCDC discrete words — not yet decoded), MFD FMS
PERF/FUEL&LOAD computed results (no SimVars — reachable via the existing MFD page scrape), ND
FM/TCAS/WXR message lines + RWY-AHEAD QFU text (reachable via the ND form's F6 live scrape). The
0..100 drag-axis comfort items (seats/armrests/forward visors) stay intentionally skipped as panel
clutter.

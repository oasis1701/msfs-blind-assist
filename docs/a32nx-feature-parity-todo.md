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

## F. A380 items — audited status (2026-06-03)

DONE this session (then port to A320):
- **Beta-target** (sideslip target) on the PFD box — `A32NX_BETA_TARGET` decoded "X.X degrees
  left/right" gated on `A32NX_BETA_TARGET_ACTIVE` (now Continuous+IsAnnounced, announces
  "Sideslip target active/inactive" on an engine-out/crosswind approach; cached for the decode).
- **TCAS RA vertical-speed band** on the PFD box — `A32NX_TCAS_VSPEED_GREEN/_RED` decoded
  "N feet per minute" / "no advisory".
- **SAT/TAT** (ADIRS ARINC) — added earlier this session.

AUDITED but deferred (each was checked; reason it isn't done):
- **EFIS preselect-QNH settable** — CONFIRMED settable live (`1013 (>L:A380X_EFIS_L_BARO_PRESELECTED)`
  stuck). It's currently a read-only readout; making it settable needs a numeric-input control
  (a `_SET` key + a HandleUIVariableSet calc-path branch, hPa). Tractable follow-up, no blocker.
- **Clock chronometer button ": Idle" cosmetic** — the CHRONO toggle WORKS (fires the H-event);
  the navigation announce reads ": Idle" (the button's value). Cosmetic only. **ET elapsed-time**
  needs a live test (set the ET knob to Run with DC power, confirm `A32NX_CHRONO_ET_ELAPSED_TIME`
  advances).
- **EWD BLEED line + IDLE memo** — source-safe (BLEED: AGS word bits 13/14 = PACKS, `ENG ANTI ICE:n`
  = NAI, `STRUCTURAL DEICE SWITCH` = WAI; IDLE: ≥3 eng N1 ≤ idle+2 in phase 6–9). Not added yet —
  niche, adds vars to the EWD builder.
- **ILS/VOR/ADF tuned-station IDENTS in the DISPLAY** — strings; ALREADY delivered via the Output+N
  hotkey's `NAV IDENT` STRING256 struct (works). A display readout would need a Coherent scrape;
  low value given the hotkey covers it.
- **ND chrono** — computed in JS, no SimVar; scrape-only. Deferred.
- **ETA/EFOB at destination** — via the Coherent `flightInfo()` agent (`getDestEFOB` /
  `getDestinationPrediction().secondsFromPresent`); needs a JS-agent change + in-flight to verify
  (predictions null on the ground).
- **ACP TX channels / pushback Call-Release Tug** — need write plumbing + state-machine testing.

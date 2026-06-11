# FBW dev integration — Pass 2: live-sim verification checklist

Items deliberately deferred from the 2026-06-11 plan (`docs/superpowers/plans/2026-06-11-fbw-dev-parity-fixes.md`)
because they need a running sim. Verify each against the DEV A32NX/A380X; check off + note results.

## Verify the Pass-1 changes that could not be bench-tested

- [x] A380 STD/QNH — LIVE-VERIFIED 2026-06-11 via MCP: semantics are PUSH=STD / PULL=QNH (OPPOSITE of the snapshot-source reading and of the A32NX); `KOHLSMAN SETTING STD:n` is written on transitions only (stale-at-session-join case covered by the MainForm MB watchdog). Re-confirm once via the app combo after the fix build.
- [ ] A32NX unit toggle via `A32NX_FCU_EFIS_{L,R}_BARO_IS_INHG` calc write changes the FCU display unit.
- [ ] A32NX LS via `A32NX.FCU_EFIS_L/R_LS_PUSH` + `*_LS_LIGHT_ON` readback.
- [ ] A32NX GEN/APU GEN toggle events + stock-simvar state combos.
- [ ] A32NX wipers on circuits 77/80 (off/slow/fast) and dome light potentiometer 7.
- [ ] A32NX runway turn-off via indexed `TAXI_LIGHTS_SET` (both lights + cockpit switch follows).
- [ ] A32NX thrust detents land in the FBW bands (esp. Reverse Idle at -0.80) — also confirm against a custom EFB throttle calibration.
- [ ] A32NX FPA set ×10 (enter 2.5 → FCU shows 2.5°); negative V/S via the panel path.
- [ ] A380 FPA set ×10 (enter 2.5 → FCU shows 2.5° — was silently ignored at ×100); negative V/S.
- [ ] A380 transfer pumps 70-81 toggle their circuits; anti-skid toggle.
- [ ] A32NX NAVAID selectors / FO filter pushes / filter light readbacks.
- [ ] A32NX LDG ELEV knob write (-4000 Auto detent + a real elevation).
- [ ] COM3 standby set/swap; brightness knobs move the cockpit pots.
- [ ] Approach-capability announce on an ILS approach (LAND 2 / LAND 3 transitions, both jets).
- [ ] A32NX fire-handle pull announce + agent-discharge interlock (pull handle via the panel, then discharge).
- [ ] MCDU print: press an MCDU PRINT prompt (e.g. D-ATIS) → announced; MCDU2 fallback under AC ESS SHED failure.
- [ ] OANS: re-arm exit E→K reports honestly; manual stop 400-4000 validation; runway-ahead clears when stopped.
- [ ] MFD duplicate-names dialog reads + is clickable (type an ambiguous VOR ident); FO-side URI navigation.
- [ ] flyPad checklist items read correctly with EFB_AUTOFILL_CHECKLISTS on AND off; disabled tiles announce as dimmed and don't actuate.
- [ ] E/WD: ABN-PROC preview lines read as "(not yet active)"; LAND ASAP/ANSA name correctly during a serious failure; `<5m` limitations report Cyan.
- [ ] A32NX GPWS `_LIGHT_ON` monitors fire (trigger a GPWS test); GPWS switch labels (var=1 now reads Off).
- [ ] A32NX TCAS advisory state announce (TA/RA onset + "clear of conflict"); DCDU message-waiting announce + ACK on a CPDLC message.

## Deferred features/probes (build after verification or live iteration)

- [ ] MobiFlight no-module detection via an END-TO-END probe: calc-write a nonce to `L:MSFSBA_BRIDGE_PROBE`, read it back via the data-def path; only that proves the calc channel. Response-based gates (IsRegistered / any-response) are BOTH invalid — live-verified 2026-06-11 on an install whose module executes every command but never sends a single response (no registration Finished, no MF.Pong). Until built, the gate is "module object initialized" (pre-existing behavior) and a missing module surfaces via the status text only.
- [ ] Related pre-existing question on the same install: with the response channel dead, `IsRegistered` stays false → the per-client FBWBA channel (`SendFBWBACommand`, HVar press/release paths) never opens. Check whether the A32NX ECP HVar buttons work on this machine; if they do, find which path they actually take — if they don't, they were broken before this branch too.

- [ ] Probe: `A32NX.FCU_METRIC_ALT_TOGGLE_PUSH` on the A32NX (newly registered in dev FBW) — expose if functional.
- [ ] Probe: gravity gear extension (`A32NX_GRAVITYGEAR_TURNED`=1 + `_ROTATIONS`=3 stickiness) — add controls if it actuates (the A380 already has gravity-gear controls; the A320 has none).
- [ ] A380 stall warning: NO working aural var (FBW `FwsSoundManager.ts:116-122` maps the stall sound to `A32NX_AUDIO_ROP_MAX_BRAKING` — apparent upstream copy-paste bug). REPORT UPSTREAM to FlyByWire; re-add the monitor on whatever var the fix lands on.
- [ ] A380 `A32NX_AUTOPILOT_AUTOLAND_WARNING`: no writer found in the A380X tree (suspected dead) — verify during an autoland; remove or re-key if confirmed.
- [ ] FCU `A32NX_FCU_AFS_DISPLAY_*_DASHES` (A32NX): use to say "managed" instead of a stale value in the FCU readouts — needs live confirmation of dash semantics.
- [ ] flyPad Performance page SelectInput dropdowns (takeoff/landing calculators) — live tour; extend the agent if unreachable (13 + 11 dropdowns at stake).
- [ ] flyPad radio-altitude Automatic Call Outs page — live tour of the settings builder (directly relevant: callouts are the blind pilot's landing instrument).
- [ ] flyPad EFB ATC page hover-revealed tune buttons — live tour.
- [ ] SDv2 scrape fallback: confirm which Coherent view hosts C/B + VIDEO on the installed build (source splits them between A380X_SDv2 and legacy A380X_SD).
- [ ] Dual-FWS-failure announce path (fwsCore destroyed → probe empty → fallback checklist scraped but silent) — design together with the EWD hidden-row visibility fix (regression-sensitive; see the audit notes about `_seen` mass-invalidation).
- [ ] F-PLN scrape polish batch (two-altitude constraints, SPD+ALT coexistence, ditto carry-forward, FPA column, hold-row speed, destination footer, TMPY/EO title flags) — capture live fixtures first.
- [ ] NEW FEATURE: native pushback control form (heading/speed L:var writes + spoken/tonal feedback from `PUSHBACK ANGLE`) — needs live tuning by design; pairs with the taxi-guidance audio stack.
- [ ] NEW FEATURE: CG envelope verdict helper (transcribe envelope limits from FBW source, speak "within limits").
- [ ] A380 wheel chocks/cones tiles were REMOVED upstream (commented out in A380Services.tsx) — expect them to disappear from the flyPad after the next aircraft update; no MSFSBA action.
- [ ] STATUS/INOP/LIMITATIONS: dev FBW now POPULATES `inopSys*`/`limitations*` (FwsInopSys.ts/FwsLimitations.ts) — the CoherentFwsFailureClient path should go live; verify during a failure scenario. Note the subjects were made `private` upstream (runtime-fine, rename risk on FBW updates).

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
- [x] A380 FPA set ×10 — LIVE-VERIFIED 2026-06-11 via MCP: `25 (>K:A32NX.FCU_VS_SET)` in TRK/FPA mode → `A32NX_AUTOPILOT_FPA_SELECTED` = 2.5°.
- [x] A380 transfer pumps + anti-skid — LIVE-VERIFIED 2026-06-11: circuit 70 toggled 1→0→1 via the pump RPN; `ANTISKID_BRAKES_TOGGLE` flipped `ANTISKID BRAKES ACTIVE` both ways (states restored). Same template covers circuits 71-81.
- [ ] A32NX NAVAID selectors / FO filter pushes / filter light readbacks.
- [ ] A32NX LDG ELEV knob write (-4000 Auto detent + a real elevation).
- [ ] COM3 standby set/swap; brightness knobs move the cockpit pots.
- [ ] Approach-capability announce on an ILS approach (LAND 2 / LAND 3 transitions, both jets). PARTIAL 2026-06-11: A380 cruise word reads SSM=NormalOp, zero bits → "none computed" decode path verified; the LAND transitions still need an approach.
- [ ] A32NX fire-handle pull announce + agent-discharge interlock (pull handle via the panel, then discharge).
- [ ] MCDU print: press an MCDU PRINT prompt (e.g. D-ATIS) → announced; MCDU2 fallback under AC ESS SHED failure.
- [ ] OANS: re-arm exit E→K reports honestly; manual stop 400-4000 validation; runway-ahead clears when stopped.
- [ ] MFD duplicate-names dialog reads + is clickable (type an ambiguous VOR ident); FO-side URI navigation.
- [ ] flyPad checklist items read correctly with EFB_AUTOFILL_CHECKLISTS on AND off; disabled tiles announce as dimmed and don't actuate.
- [ ] E/WD: ABN-PROC preview lines read as "(not yet active)"; LAND ASAP/ANSA name correctly during a serious failure; `<5m` limitations report Cyan.
- [ ] A32NX GPWS `_LIGHT_ON` monitors fire (trigger a GPWS test); GPWS switch labels (var=1 now reads Off).
- [ ] A32NX TCAS advisory state announce (TA/RA onset + "clear of conflict"); DCDU message-waiting announce + ACK on a CPDLC message.

## Deferred features/probes (build after verification or live iteration)

- [x] MobiFlight end-to-end probe — IMPLEMENTED 2026-06-11 (nonce round-trip via MainForm.BridgeProbeTimer_Tick → MSFSBA_BRIDGE_PROBE → MarkCalcPathVerified; verdict in transport.log). Gate intentionally stays IsConnected; the probe is observability + the basis for a future gate. Verify the "[Probe] calc path VERIFIED" line on the next launch.
- [ ] Related pre-existing question on the same install: with the response channel dead, `IsRegistered` stays false → the per-client FBWBA channel (`SendFBWBACommand`, HVar press/release paths) never opens. Check whether the A32NX ECP HVar buttons work on this machine; if they do, find which path they actually take — if they don't, they were broken before this branch too.

- [ ] Probe: `A32NX.FCU_METRIC_ALT_TOGGLE_PUSH` on the A32NX (newly registered in dev FBW) — expose if functional.
- [ ] Probe: gravity gear extension (`A32NX_GRAVITYGEAR_TURNED`=1 + `_ROTATIONS`=3 stickiness) — add controls if it actuates (the A380 already has gravity-gear controls; the A320 has none).
- [ ] A380 stall warning: NO working aural var (FBW `FwsSoundManager.ts:116-122` maps the stall sound to `A32NX_AUDIO_ROP_MAX_BRAKING` — apparent upstream copy-paste bug). REPORT UPSTREAM to FlyByWire; re-add the monitor on whatever var the fix lands on.
- [ ] A380 `A32NX_AUTOPILOT_AUTOLAND_WARNING`: no writer found in the A380X tree (suspected dead) — verify during an autoland; remove or re-key if confirmed.
- [ ] FCU `A32NX_FCU_AFS_DISPLAY_*_DASHES` (A32NX): use to say "managed" instead of a stale value in the FCU readouts — needs live confirmation of dash semantics.
- [ ] flyPad Performance SelectInput dropdowns — A32NX-ONLY (live-confirmed 2026-06-11: the A380 Performance page has only ToD + Temp Correction sub-tabs, both fully accessible). Tour when the A320 is loaded.
- [x] flyPad Automatic Call Outs page — LIVE-VERIFIED 2026-06-11 (A380): all 19 per-altitude toggles read with name + state, Reset reachable. No code needed.
- [x] flyPad ATC page — LIVE-VERIFIED 2026-06-11 (A380): tune buttons DO surface despite the hover gating; now labeled "CALLSIGN FREQ: Set Active/Standby" (agent fix + jsdom tests). Controller cards + frequencies read.
- [x] SDv2 view split — RESOLVED 2026-06-11: the installed build has NO legacy A380X_SD view at all (pagelist shows only "SD - A380X_SDv2"), so the existing needle is the only possible target; nothing to change.
- [ ] Dual-FWS-failure announce path (fwsCore destroyed → probe empty → fallback checklist scraped but silent) — design together with the EWD hidden-row visibility fix (regression-sensitive; see the audit notes about `_seen` mass-invalidation).
- [x] F-PLN polish batch — IMPLEMENTED 2026-06-11 (commits d3190e1 + 1e57de9): destination footer, WINDOW→"altitude window", SPD+ALT constraint coexistence, SPD/ALT ditto resolution, FPA column, hold-row speed, TMPY/EO/PENALTY title flags — all in the SPOKEN phrasing (a terse-token regression was caught in review and reverted; a guard test now blocks it). Live MFD socket is held by the running app (connects at aircraft detect, not window-open), so fixtures are source-derived for window/hold; re-eyeball the F-PLN read after the next MSFSBA restart.
- PUSHBACK: permanently OUT OF SCOPE — Robin's team uses GSX for pushback (much better than the FBW implementation; decision 2026-06-11). Never re-add.
- [ ] NEW FEATURE: CG envelope verdict helper (transcribe envelope limits from FBW source, speak "within limits").
- [ ] A380 wheel chocks/cones tiles were REMOVED upstream (commented out in A380Services.tsx) — expect them to disappear from the flyPad after the next aircraft update; no MSFSBA action.
- [ ] STATUS/INOP/LIMITATIONS: dev FBW now POPULATES `inopSys*`/`limitations*` (FwsInopSys.ts/FwsLimitations.ts) — the CoherentFwsFailureClient path should go live; verify during a failure scenario. Note the subjects were made `private` upstream (runtime-fine, rename risk on FBW updates).

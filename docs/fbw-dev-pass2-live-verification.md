# FBW dev integration — Pass 2: live-sim verification checklist

Items deliberately deferred from the 2026-06-11 plan (`docs/superpowers/plans/2026-06-11-fbw-dev-parity-fixes.md`)
because they need a running sim. Verify each against the DEV A32NX/A380X; check off + note results.

## Verify the Pass-1 changes that could not be bench-tested

- [x] A380 STD/QNH — LIVE-VERIFIED 2026-06-11 via MCP: semantics are PUSH=STD / PULL=QNH (OPPOSITE of the snapshot-source reading and of the A32NX); `KOHLSMAN SETTING STD:n` is written on transitions only (stale-at-session-join case covered by the MainForm MB watchdog). Re-confirm once via the app combo after the fix build.
- [ ] A32NX baro unit toggle — PARTIAL 2026-06-12: the IS_INHG calc write sticks both ways, but the aircraft was on STD at cruise (DISPLAY_BARO_VALUE_MODE=0) where the display can't re-render the unit; confirm the displayed-unit switch during a QNH phase.
- [x] A32NX LS — LIVE-VERIFIED 2026-06-12: LS_PUSH flips LS_LIGHT_ON both ways.
- [ ] A32NX GEN/APU GEN toggle events — DEFERRED TO GROUND (mid-cruise generator toggling causes an ELEC transient + master caution).
- [x] A32NX wipers + dome — LIVE-VERIFIED 2026-06-12: circuit 77 toggled on at power 75 (slow) and back off; dome pot 7 followed 0→50→0 via the def's exact RPNs.
- [x] A32NX runway turn-off — LIVE-VERIFIED 2026-06-12: `2 1 (>K:2:TAXI_LIGHTS_SET)` lit LIGHT TAXI:2 and off restored (index 3 = same template; the simvar IS the cockpit-switch state source).
- [ ] A32NX thrust detents — DEFERRED TO GROUND (an axis write mid-flight commands real thrust).
- [x] A32NX FPA ×10 — LIVE-VERIFIED 2026-06-12: `25 (>K:A32NX.FCU_VS_SET)` in TRK/FPA → FPA_SELECTED = 2.5 AND AFS_DISPLAY_VS_FPA_VALUE = 2.5 (mode restored, nothing engaged).
- [x] A380 FPA set ×10 — LIVE-VERIFIED 2026-06-11 via MCP: `25 (>K:A32NX.FCU_VS_SET)` in TRK/FPA mode → `A32NX_AUTOPILOT_FPA_SELECTED` = 2.5°.
- [x] A380 transfer pumps + anti-skid — LIVE-VERIFIED 2026-06-11: circuit 70 toggled 1→0→1 via the pump RPN; `ANTISKID_BRAKES_TOGGLE` flipped `ANTISKID BRAKES ACTIVE` both ways (states restored). Same template covers circuits 71-81.
- [x] A32NX NAVAID + FO filters — LIVE-VERIFIED 2026-06-12: FCU NAVAID_1_MODE input held VOR AND the FCU republished the old EFIS output var (downstream confirmed); FO CSTR push flipped CSTR_LIGHT_ON both ways.
- [x] A32NX LDG ELEV — LIVE-VERIFIED 2026-06-12: knob held 2000 ft and restored to -4000 Auto.
- [x] COM3 + brightness — LIVE-VERIFIED 2026-06-12: COM3 standby set 121.50, swap moved it active, both restored; pedestal flood pot 76 followed 0→40→0 via the def's indexed LIGHT_POTENTIOMETER_SET.
- [ ] Approach-capability announce on an ILS approach (LAND 2 / LAND 3 transitions, both jets). PARTIAL 2026-06-11: A380 cruise word reads SSM=NormalOp, zero bits → "none computed" decode path verified; the LAND transitions still need an approach.
- [ ] A32NX fire-handle pull + interlock — DEFERRED TO GROUND (never pull a fire handle in flight).
- [ ] MCDU print: press an MCDU PRINT prompt (e.g. D-ATIS) → announced; MCDU2 fallback under AC ESS SHED failure.
- [ ] OANS: re-arm exit E→K reports honestly; manual stop 400-4000 validation; runway-ahead clears when stopped.
- [ ] MFD duplicate-names dialog reads + is clickable (type an ambiguous VOR ident); FO-side URI navigation.
- [x] flyPad Checklists page — LIVE-TOURED 2026-06-12 (manual mode): checklist selector buttons, items as checkboxes with state, Mark/Reset reachable. The autofill-ON variant + dimmed-tile actuation check remain for a session with autofill enabled (not flipping the user's persistent setting).
- [ ] E/WD: ABN-PROC preview lines read as "(not yet active)"; LAND ASAP/ANSA name correctly during a serious failure; `<5m` limitations report Cyan.
- [ ] A32NX GPWS — PARTIAL 2026-06-12: light + switch vars read clean 0 baselines (registered correctly); the TEST-triggered light flash is DEFERRED TO GROUND (aural + inhibits in flight).
- [ ] A32NX TCAS advisory state announce (TA/RA onset + "clear of conflict"); DCDU message-waiting announce + ACK on a CPDLC message.

## Deferred features/probes (build after verification or live iteration)

- [x] MobiFlight end-to-end probe — IMPLEMENTED 2026-06-11 (nonce round-trip via MainForm.BridgeProbeTimer_Tick → MSFSBA_BRIDGE_PROBE → MarkCalcPathVerified; verdict in transport.log). Gate intentionally stays IsConnected; the probe is observability + the basis for a future gate. Verify the "[Probe] calc path VERIFIED" line on the next launch.
- [x] ECP HVar channel — RESOLVED 2026-06-12: `SendHVar` already falls back to the DEFAULT channel after the registration timeout, so the A32NX ECP buttons work on this install; only the ≤2 s pre-timeout startup window drops HVars (now logged to transport.log).

- [x] Metric-alt probe — RESOLVED 2026-06-12: the event is registered but INERT on the installed build (A32NX_METRIC_ALT_TOGGLE never moves; no consumer in any installed bundle — only the ISIS/EFB metric settings exist). Do not expose; the real A320 has no MTRS button.
- [ ] Probe: gravity gear extension (`A32NX_GRAVITYGEAR_TURNED`=1 + `_ROTATIONS`=3 stickiness) — add controls if it actuates (the A380 already has gravity-gear controls; the A320 has none).
- [ ] A380 stall warning: NO working aural var (FBW `FwsSoundManager.ts:116-122` maps the stall sound to `A32NX_AUDIO_ROP_MAX_BRAKING` — apparent upstream copy-paste bug). REPORT UPSTREAM to FlyByWire; re-add the monitor on whatever var the fix lands on.
- [ ] A380 `A32NX_AUTOPILOT_AUTOLAND_WARNING`: no writer found in the A380X tree (suspected dead) — verify during an autoland; remove or re-key if confirmed.
- [x] FCU _DASHES — RESOLVED 2026-06-12: INERT on the installed build (HDG_DASHES=0 while lateral mode = NAV/managed, where the real window shows dashes). Unusable; the managed-dot detection stays.
- [x] flyPad Performance SelectInput dropdowns — IMPLEMENTED 2026-06-12 (commit 2e82a16): the closed control had NO direct text so the scrape missed all 24 calculator dropdowns entirely; class-shape detection + "Field: Value" labels + cursor-not-allowed=dimmed, jsdom-tested. LIVE-VERIFIED 2026-06-12 on the KORD landing calculator: all dropdowns read composed ("Runway Condition: Dry (6)", "Flaps Configuration: CONF 3", unit selectors); opening lists the options as buttons; orderSelectOptions groups them contiguously under the dropdown (dac439d); a cyclic-serialization crash from the regroup key was caught live and fixed + suite-locked.
- [x] flyPad Automatic Call Outs page — LIVE-VERIFIED 2026-06-11 (A380): all 19 per-altitude toggles read with name + state, Reset reachable. No code needed.
- [x] flyPad ATC page — LIVE-VERIFIED 2026-06-11 (A380): tune buttons DO surface despite the hover gating; now labeled "CALLSIGN FREQ: Set Active/Standby" (agent fix + jsdom tests). Controller cards + frequencies read.
- [x] SDv2 view split — RESOLVED 2026-06-11: the installed build has NO legacy A380X_SD view at all (pagelist shows only "SD - A380X_SDv2"), so the existing needle is the only possible target; nothing to change.
- [ ] Dual-FWS-failure announce path (fwsCore destroyed → probe empty → fallback checklist scraped but silent) — design together with the EWD hidden-row visibility fix (regression-sensitive; see the audit notes about `_seen` mass-invalidation).
- [x] F-PLN polish batch — IMPLEMENTED 2026-06-11 (commits d3190e1 + 1e57de9): destination footer, WINDOW→"altitude window", SPD+ALT constraint coexistence, SPD/ALT ditto resolution, FPA column, hold-row speed, TMPY/EO/PENALTY title flags — all in the SPOKEN phrasing (a terse-token regression was caught in review and reverted; a guard test now blocks it). Live MFD socket is held by the running app (connects at aircraft detect, not window-open), so fixtures are source-derived for window/hold; re-eyeball the F-PLN read after the next MSFSBA restart.
- PUSHBACK: permanently OUT OF SCOPE — Robin's team uses GSX for pushback (much better than the FBW implementation; decision 2026-06-11). Never re-add.
- [ ] NEW FEATURE: CG envelope verdict helper (transcribe envelope limits from FBW source, speak "within limits").
- [ ] A380 wheel chocks/cones tiles were REMOVED upstream (commented out in A380Services.tsx) — expect them to disappear from the flyPad after the next aircraft update; no MSFSBA action.
- [ ] STATUS/INOP/LIMITATIONS: dev FBW now POPULATES `inopSys*`/`limitations*` (FwsInopSys.ts/FwsLimitations.ts) — the CoherentFwsFailureClient path should go live; verify during a failure scenario. Note the subjects were made `private` upstream (runtime-fine, rename risk on FBW updates).

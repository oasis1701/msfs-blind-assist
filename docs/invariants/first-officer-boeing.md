# First Officer: PMDG 777/737 and iFly 737 MAX8 — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/first-officer-boeing.md`, which Claude Code loads when it reads matching code. Background: [first-officer.md](../first-officer.md).
The text is verbatim from PR #160's CLAUDE.md as of `642f48c1`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below" or "above" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## FOB-1

- 737 FO state groups are ALL `RevertToState` (never `StayComplete`), made safe by NaN-until-`IsReady` evaluator gating + the manual-tick grace — with ONE sanctioned exception, the Boeing engine-start SELECTOR items (PMDG 737/iFly 737 `ES_E1_GRD`/`ES_E2_GRD`, PMDG 777 `ES_ENG1_START_SEL`/`ES_ENG2_START_SEL`), owner-approved 2026-08-27. They were `ActionManual`, so NOTHING but a hand tick could ever complete them and the ENGINE_START group sat permanently incomplete for a pilot who started from the MSFSBA panel or the cockpit (Airbus profiles were never affected — their masters/mode selector are persistent positions). They cannot be `RevertToState` on the switch position (the selector is held by the starter solenoid and springs back at cutout, so it would un-tick the instant the start succeeded — the very reason they were action-only) NOR on N2 (a start is a HISTORICAL event; the tick must survive N2 falling at shutdown), so they `AutoLatch` off `FO_ENG{1,2}_N2` >= `EngineRunningN2` (50.0 on all three) with `StayComplete`. The blanket rule's rationale does not apply: it guards against a target that coincidentally matches an EARLIER phase (packs OFF cold-and-dark, APU OFF before it ever ran) latching while the switch is elsewhere, and "engine running" is false at cold-and-dark and unreachable without a real start. NOT a reintroduction of the "Engine 1/2: running" items removed 2026-08-16 and pinned out by `EngineStartChecklistShapeTests` — no item is added, N2 is the DETECTION for the existing one; hand-ticking still fires the selector, and the start-LEVER/fuel-control items stay `RevertToState` (never let the latch spread). The 777 evaluator's `SetEngineN2` was a deliberate no-op and had to be wired; it serves `FO_ENG{1,2}_N2` AHEAD of the `CdaReady` gate because N2 comes from SimConnect, not the CDA — gating it returns NaN exactly when a start happens — and both start NaN, never 0. → [first-officer.md](../first-officer.md)

## FOB-2

- **The First Officer reads the PMDG 777 speed-brake lever from main's `L:switch_498_a` (key `FCTL_Speedbrake`, 0 / 200 / 300 / 400) through `FirstOfficer/Pmdg777SpeedbrakeLever` → `SpeedbrakeLeverState` over main's `PmdgSpeedBrakeLever.B777` — NEVER the SDK byte `FCTL_Speedbrake_Lever`.** The byte is that value / 4, TRUNCATED: a lever at 201-203 reads exactly 50, "armed", with the spoilers already 34 percent up, and a hardware axis's DOWN at 22 reads 5, "not down" (measured 2026-09-30). ARM is EXACT and DOWN is anything short of ARM; never hand-write a range, and never "correct" the table toward the SDK header. History: the header's `0: DOWN, 25: ARMED, 26...100: DEPLOYED` was wrong too (the byte measured 0 / 50 / 75 / 100 on 2026-08-29), and the whole FO profile once tested `v > 0.5 && v < 1.5` for ARMED, a detent index the lever cannot produce, so the Landing flow armed the lever and then announced *"Skipping: Speedbrake: ARM"*. Armed is "Already set"; a deployed lever is LEFT ALONE with *"Speedbrake extended, not armed. Left as it is."* — a flow step through `FlowStep.LeaveAloneWhen`, a hand-tick through `ChecklistItem.LeaveAloneWhen` — and the executor's `DispatchCoreAsync` never clicks ARM over a lever not known to be DOWN, because ARM over a raised lever retracts it. The Landing step goes through the verified `SPEEDBRAKE_ARM` (`ArmSpeedbrakeAsync`, up to 8 s): the lever takes about 5 s from DOWN to ARM, and a flow step's own verify reads after only 600 ms. → [pmdg-777.md](../pmdg-777.md)

## FOB-3

- **PMDG's two ground-power event NAMES are REVERSED against `ELEC_annunExtPowr_ON[2]`** — the event named SEC drives array index 0, the one named PRIM drives index 1 (live-verified, commit e051748d). `GroundPowerGate.EventForAnnunciatorIndex` is the single source of truth and BOTH the panel (`_simpleEventMap`) and the First Officer must go through it; the FO profile once paired each index with the SAME-NAMED event, so every one of its six flow steps and three checklist actions gated on one receptacle and pressed the other. ⚠️ Do NOT re-derive the direction from event-id order (SEC = MIN+7 < PRIM = MIN+8) — `PMDG777Definition` explicitly forbids that inference: ids order EVENTS, not array slots. → [pmdg-777.md](../pmdg-777.md)

## FOB-4

- 777 flow/checklist ORDER follows PMDG's own shipped `B777_Checklist.xml` (in the aircraft package under `SimObjects/.../checklist/`) — it is the procedure authority, and it settled that trim is the LAST operational block of Before Start (after the transponder, with the hydraulics running) and that no trim checkpoint exists in Before Taxi or Before Takeoff at all. The ONE sanctioned divergence is the oxygen tests, which the vendor runs in the forward-panel block but MSFSBA runs before the fire test so the oxygen-flow sound never sits under the fire bell — flow and checklist must AGREE on that position (pinned by `Pmdg777FlowOrderingTests`). Seat belts ON rather than the vendor's AUTO is a second, owner-ruled divergence. The vendor's Before Start LNAV and VNAV arm lines and its clearance to pressurize the hydraulics are owner-ruled OMISSIONS (2026-09-22, "not needed there") — Before Takeoff's "Verify armed" pair, which presses a mode that is unarmed, is the First Officer's only LNAV/VNAV handling; never restore any of the three from the vendor page. → [first-officer.md](../first-officer.md)

## FOB-5

- The 737 First Officer does NOT touch the gear lever's OFF detent — no After Takeoff step, no checklist item, nothing spoken asks for it (owner decision 2026-09-22: 21 write shapes across CDA, `TransmitClientEvent`, `K:ROTOR_BRAKE` and a direct `switch_455_73X` write never moved `MAIN_GearLever` to OFF; the short-lived `GearOffLadder`/`SetGearLeverOffAsync` retry ladder was deleted with the step). Never re-add one without a write path verified IN FLIGHT by reading `MAIN_GearLever` back — the audible click `TransmitClientEvent`+LEFTSINGLE makes on `EVT_GEAR_LEVER` is not movement, and weight-on-wheels latches the lever at DOWN so ground tests settle nothing. Both gear lines are confirmed by the GEAR LIGHTS, never the lever alone (`GearConfirmation`): the After Takeoff Checklist's "Landing gear: UP" (`ATC_GEAR`, synthetic `FO_GEAR_UP`) is "gear up, lights out" (lever not DOWN AND all nine gear lights out; a lever moved to OFF by hand still satisfies it once the gear is up), and the Landing Checklist's "Landing gear: DOWN" (`LDC_GEAR`, `FO_GEAR_DOWN`) is "three green" (lever DOWN AND all three main-panel greens on AND no red — the overhead greens are deliberately not required). Each is completed by a read-only wait that is the LAST step of its flow (`AT_GEAR_UP_CHECK` / `LD_GEAR_DOWN_CHECK`, 20 s, Skip); a timeout is announced and FlowManager keeps the line out of `MarkGroupComplete`'s latch, so finishing a flow no longer latches a gear line over gear in the other position — a line already ticked when the flow finishes — above all a GO-AROUND, where the first approach latched "Landing gear: DOWN" ticked — is exempted too when its live state reads definitively false (never on NaN), so the next evaluation un-ticks it (`ChecklistManager.MarkGroupComplete`). Pinned by `FoPr160ProcedureFixTests` and `Pmdg737GearConfirmationTests`. → [pmdg-737.md](../pmdg-737.md)

## FOB-6

- The First Officer's 737 speedbrake-arm ladder is ONE proven rung (`CDA + MOUSE_FLAG_LEFTSINGLE` on `EVT_CONTROL_STAND_SPEED_BRAKE_LEVER_ARM`) plus a read-back of the lever at ARM and `MAIN_annunSPEEDBRAKE_ARMED` — do not restore the old 3-transport escalation; it only ever cost time on an aircraft where rung 1 failed for a real reason, which `MAIN_annunSPEEDBRAKE_DO_NOT_ARM` already catches. → [pmdg-737.md](../pmdg-737.md)

## FOB-7

- **PMDG 737 transponder STBY (`XPDR_ModeSel` 0) is UNREACHABLE by every input path — do not re-probe it.** Live-probed 2026-08-27: `EVT_TCAS_MODE` steps 4→3→2→1 and is then inert at 1 under `TransmitClientEvent` (LEFTSINGLE / WHEEL_DOWN / LEFTDOUBLE / MIDDLESINGLE / LEFTDRAG / DOWN_REPEAT / LEFTRELEASE / absolute param 0), `K:ROTOR_BRAKE` action codes 0-9 on index 800, the CDA write, and the undocumented `+20000` alias event (90432). `L:switch_800_73X` and the stock `A:TRANSPONDER STATE:1` both revert within a frame (PMDG-owned read-backs; `CTCAS::updateSquawkbox` rewrites the simvar every frame). It is NOT an MSFSBA transport bug: `73X_Cockpit_Behavior.xml` shows the VC's own left-half click emitting exactly `ROTOR_BRAKE 80001`, the same code we send, so a human cannot reach STBY either; PMDG's `B738_Checklist.xml` never asks for STBY ("As needed"/"Set"); and FSFO V6 fails identically and silently. WASM disassembly confirms 0 is real, not vestigial (`CTCAS::setMode`'s analog branch writes the field directly with a live `case 0`, `update()` has a `mode == 0` branch, and `setMode` is its only writer). So `PF_XPDR`/`SD_XPDR` target **ALT RPTG OFF (1)** — the lowest reachable position; there is NO reachable non-transmitting state on this airframe. The accept predicate is `v < 1.5`, admitting STBY AND ALT RPTG OFF so the item still passes on an airframe where 0 IS reachable — never narrow it to `v < 0.5` or `v == 1`. `BS_XPDR`/`BTKO_XPDR` (TA/RA) are unaffected. The alternate digital panel (`STBY/ON/AUTO` switch `switch_1299`, event 70931, airframe key `Transponder New Style Installed`) is a recorded but UNTESTED lead expected to make things worse, not better — its `setMode` branch also never emits 0. → [pmdg-737.md](../pmdg-737.md)

## FOB-8

- 737 `EVT_TCAS_MODE` (transponder) and `EVT_OH_LIGHTS_POS_STROBE` (position lights) are CDA-deaf walked rotaries — they only step on transmit mouse-clicks; probe actuation with `tools/CDUTest cda`, never the simconnect MCP's `send_pmdg_event` (its CDA write silently fails on the NG3). → [first-officer.md](../first-officer.md)

## FOB-9

- PMDG system self-tests (TCAS/WXR/GPWS) actuate ONLY via transmit press/release — the 777 CDA param-1 momentary is silent for `EVT_TCAS_TEST` (`XPDR_Test` dispatches through a dedicated `HandleUIVariableSet` transmit branch, removed from `_simpleEventMap`); the WXR test is a managed overlay-on→settle→TEST→callout-wait→WX-mode→overlay-off sequence (the EFIS WXR overlay is a stateless blind toggle, TEST mode latches); the 737 GPWS short/long variant resolves from `FOGpws737LongTest` at dispatch time; no GPWS test exists on the 777. Checklist items are `ActionManualAsync` (no persistent "test performed" state — never Auto). → [first-officer.md](../first-officer.md)

## FOB-10

- Center-pump ON gating lives at `DispatchCoreAsync` in BOTH FO executors (the sole all-paths chokepoint — single writes, flow `Multi` tuples, and checklist actions all funnel through it), keyed on `EVT_OH_FUEL_PUMP_L/R_CENTER` + the ON param, no-op returning `true` BEFORE `PaceAsync`; **never gate only `ExecuteSingle`/`Fire`/`FireBoth`** (the flow's `Multi` steps never call them → empty-center dry-run). Param 0 (OFF) is NEVER gated. → [first-officer.md](../first-officer.md)

## FOB-11

- ONE universal "Fuel pumps" item per phase on both PMDG jets (Preflight all-off, Before-Start wing-on + center-on-iff-fuel, Shutdown all-off); the merged Before-Start detection is the SINGLE `FO_FUEL_PUMPS_BS_OK` synthetic (never a primary+additional split — `Auto()` applies one condition to every field); never reintroduce `FO_CTR_PUMPS_*` or a standalone `BS_CTR_PUMPS_ON`. → [first-officer.md](../first-officer.md)

## FOB-12

- The merged Shutdown fuel action/flow MUST order wing-off BEFORE center-off (serialized dispatch → center falling edge sees wing already off → no spurious manual-off latch — R-8/proof 5); it is a required invariant, not cosmetic. → [first-officer.md](../first-officer.md)

## FOB-13

- The center-pump OFF trigger is QUANTITY-BASED, not annunciator-based: OFF fires once center fuel quantity reads confirmed below `OffThresholdLbs` (1000 lbs) for `QtyOffConfirmSeconds` (2 s) continuously, in ANY phase of flight. This replaced the low-press-annunciator debounce after it failed in the field TWICE — a 2026-08 flicker defect, then a 2026-08-16 log-proven case where quantity fell 922→304→0 lbs with the pumps running and the debounced dry signal never accrued a second of evidence. **Never reintroduce an annunciator term (dry/credible/low-press) into this policy.** Auto-arm (ON) still requires ground + wing pumps on + center quantity above `ArmThresholdLbs` (1500 lbs) — the 500 lb gap above `OffThresholdLbs` is deliberate hysteresis so the automation can never arm pumps it would immediately switch back off. `center_pumps.log` remains the trace to ask for; its line shape changed (no more `dry=`/`cred=`/`dryMs=`). → [first-officer.md](../first-officer.md)

## FOB-14

- The center-pump policy's OFF branch never reads `_switchedOffThisLeg` (that is THE TRAP — a clear narrowed until it can't fire); the only OFF suppressor is the self-clearing `_pendingCommand`; the manual-off/dry-off latches only suppress ARM and are cleared together by a ground refuel or the settings edge, but the pump switch's OWN rising edge clears only the manual-off latch, never the dry-off latch; `_qtyFloor` is NaN iff a latch is set. The dry-off latch is now set by the quantity-based OFF trigger, not by an annunciator debounce. → [first-officer.md](../first-officer.md)

## FOB-15

- HISTORICAL, no longer driving automation: the CENTER low-press annunciator IS gated on its OWN switch — a lit center light implies its switch is on (`lpCtrN ⟹ swCtrN`, live-measured M-2; the converse is FALSE: switch on + fuel → light out), and the WING annunciator tracks output PRESSURE, not the switch (M-3). Both measurements remain true of the annunciators themselves, but the center-pump policy no longer reads either — see the quantity-based OFF trigger bullet above. → [first-officer.md](../first-officer.md)

## FOB-16

- `(int)Math.Round(double.NaN)` is `int.MinValue` on x64 — every FO `Fuel*Lbs` reader must route through `FuelSystemLogic.SafeRoundToInt` (NaN→0); a NaN-derived quantity pinning the refuel floor would oscillate the pumps. → [troubleshooting-playbook.md](../troubleshooting-playbook.md)

## FOB-17

- iFly 737 MAX8 FO writes go through `IFly737MAXDefinition.ApplyUIVariable`, with TWO sanctioned bypasses — pressurization altitudes (via `SendDirect`/`Sdk.SendCommand`, because the def's NumSet path fires an unsuppressible `AnnounceImmediate` that would talk over the flow's step narration) and altimeters-to-standard (by VALUE via the stock `KOHLSMAN_SET`, the Ctrl+B dialog's live-verified mechanism, because `BARO_STD_Status` is MOMENTARY — "switch released/pressed", no persistent STD var in the model XML — so the guarded-toggle press it once used flipped an already-standard side back to QNH and announced a false failure on every success; never re-add a `BARO_STD_Status` guard) — never a second command table beyond those; nothing on this aircraft is ever written as an L:var, so a key `ApplyUIVariable` doesn't recognize is a mapping bug, not a control needing another path. → [first-officer.md](../first-officer.md)

## FOB-18

- iFly 737 MAX8 center-pump ON gating sits at `IFly737ActionExecutor.DispatchCoreAsync`, the executor's single all-paths chokepoint — same rule as the PMDG jets, never gate a shallower call. → [first-officer.md](../first-officer.md)

## FOB-19

- iFly 737 MAX8 takeoff flaps and landing autobrake are Captain items; the speed brake is ARMED BY THE FIRST OFFICER since main's PR #261 measured the lever write (`FLTCTRL_SPOILER`, the same 0-224 scale as `Spoiler_Lever_Status`, ARM exactly 34), through the verified `SPEEDBRAKE_ARM` (`ArmSpeedbrakeCoreAsync`; parity with the PMDG 737, the same aircraft type). Both "Speedbrake: ARMED" lines (`LDA_SPDBRK`, `LDC_SPDBRK`) read `FO_SPEEDBRAKE_ARMED` = the lever exactly at ARM AND the ARMED light (the light alone is lit 34-224, so it cannot tell armed from deployed), and the step completes both through `FlowStep.AlsoCompletesChecklistItemIds`. A deployed speed brake is left alone with *"Speedbrake extended, not armed. Left as it is."*, because ARM over it retracts it. `LD_SPDBRK_CHECK` and the Captain reminder are gone. → [first-officer.md](../first-officer.md), [ifly-737.md](../ifly-737.md)

## FOB-20

- iFly 737 MAX8 descent never commands standard pressure — the transition-level crossing is announce-only ("set local altimeter pressure now"); only the climb-through-transition-altitude crossing presses STD, and only on a side not already confirmed STD. → [first-officer.md](../first-officer.md)

## FOB-21

- iFly 737 MAX8 LNAV/VNAV latch must not burn on an unreadable snapshot — `ShouldBurnLnavVnavLatch` only spends the 400 ft one-shot when a push actually fired or both annunciators read definitively known; an all-NaN tick must leave the latch unset so the next tick can retry. → [first-officer.md](../first-officer.md)

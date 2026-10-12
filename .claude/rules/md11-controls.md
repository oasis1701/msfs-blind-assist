---
paths:
  - "MSFSBlindAssist/Aircraft/MD11/**"
  - "MSFSBlindAssist/Aircraft/TFDiMD11*.cs"
  - "MSFSBlindAssist/SimConnect/MD11/**"
  - "MSFSBlindAssist/MainForm.MD11.cs"
  - "MSFSBlindAssist/Forms/MD11/**"
  - "tests/MSFSBlindAssist.Tests/**/*Md11*.cs"
---
# TFDi MD-11 control writes and control state rules

Loaded when Claude reads matching code. Background: docs/md11.md. Full text of each rule: docs/invariants/md11-controls.md.

- [MD11-1] Every MD-11 control write is a CEVENT through `Md11EventBus` with the `{seq} 0 *` prefix; never `SetLVar` a control var. Only three writes bypass it (`Md11EventBus.WriteExternal`), a CLOSED set, and `Md11DirectSet.Refuse` is never loosened. (more: see full) Full: docs/invariants/md11-controls.md#md11-1
- [MD11-2] `Md11ControlState.Compose` orders lit legends, the PROVEN latch, "unpowered", then the dark meaning; dark is never "normal" without the DC gate, which is TWO vars: `MD11_DC_BUS_VOLTAGE` ≥ 20 V AND `MD11_OVHD_ELEC_DC1_BUS_OFF_LT` dark, never voltage alone (more: see full). Full: docs/invariants/md11-controls.md#md11-2
- [MD11-8] A walked control's state var is consumed by `ProcessSimVarUpdate` during the walk, so no forced read reaches MainForm's combo refresh mid-walk; the walker reads via `ReadFreshAsync` on the read's OWN request id, never the subscription's, and SIM_FRAME subscriptions are seeded. (more: see full) Full: docs/invariants/md11-controls.md#md11-8
- [MD11-9] Every spoken MD-11 read-back verdict reads through `SimConnectManager.ReadFreshAsync`, never a fixed sleep and the cache, and speaks only on a DELIVERED value; `FeedbackDelayMs` is a deadline inside `Md11AnnouncementGate.EchoWindowMs`, never clamp the guard's settle. (more: see full) Full: docs/invariants/md11-controls.md#md11-9
- [MD11-10] NAV and FMS SPD engagement are read from the FCP windows' dashes (`Md11AutoflightState`: `MD11_AFS_HDG`/`MD11_AFS_SPD` = -999), never the `MD11_CGS_*_BT` button vars; a dashed V/S window never claims PROF or APPR/LAND. (more: see full) Full: docs/invariants/md11-controls.md#md11-10
- [MD11-11] A lamp change speaks its OWNER's composed state; a press is confirmed once after settle, its lamp echo swallowed until then; a lamp going DARK is never spoken on the spot (deferred `DarkSettleMs`, re-decided). `ExcludeFromMonitorManager` never sits on a var that speaks. (more: see full) Full: docs/invariants/md11-controls.md#md11-11
- [MD11-14] `Md11TestButtons` holds eleven test buttons 3 s via `PressAndHoldAsync`, but never the ANNUNCIATOR LIGHT TEST (it stays a tap). `Md11EventBus.Dispose` stops the pump first and writes every owed UP before draining, bounded; an UP never precedes its DOWN. (more: see full) Full: docs/invariants/md11-controls.md#md11-14
- [MD11-15] `MD11_EXTCTL_{CAP,FO}_MIN` set the BARO minimums only and idle at -9999 (not -1); the rows' mode word comes from the silent batch-covered mirror, never the OnRequest switch, since batch-covering the switch breaks its walk. Full: docs/invariants/md11-controls.md#md11-15
- [MD11-18] Anything the MD-11 definition speaks from a timer checks `Md11DisabledMonitorVariablesSet` itself (MainForm's `Suppressed` wrap covers only `ProcessSimVarUpdate`), and an unchanged redelivery must not re-arm a settle timer. Full: docs/invariants/md11-controls.md#md11-18
- [MD11-19] Wipe baseline-first trackers in `OnSimContextReset` (dropping both roll-cue arms, never their V-speeds), NEVER in `ResetAnnouncementBaselines`, which drops the V-speed roll's arm ([TKO-5]), never the N1 cue's; re-seed them from cache when `Md11SeedGate` decides on batch-delivery evidence, NEVER on a wall clock. (more: see full) Full: docs/invariants/md11-controls.md#md11-19
- [MD11-20] The "N1 70 percent" cue (`Md11N1Cue`) is a take-off-roll cue with `TakeoffVSpeedCallouts`' gate (arms on the ground, slow, every engine below 60 %; fires once at 70 % on the ground), never a bare N1 latch; `Reset` drops only the arm and runs only from `OnSimContextReset`. Full: docs/invariants/md11-controls.md#md11-20
- [MD11-22] Never seed an MD-11 flap combo by an EXACT key: map values through `SimVarDefinition.ValueToDescriptionKey` (`NearestDialChoice` for the wheel, `LeverDetentKey` for the handle), or the first Down-arrow commits row 0 and can retract the flaps. (more: see full) Full: docs/invariants/md11-controls.md#md11-22
- [MD11-23] The MD-11 must register `MSFSBA_BRIDGE_PROBE`, its ONLY opt-in to the calc-path probe and `CalcPathVerdict.PilotWarning`; never remove it, never add a warning of its own in the bus, and never put the probe var in `SeededScalarKeys`. Full: docs/invariants/md11-controls.md#md11-23
- [MD11-27] `DebouncedWalk`'s `finally` force-reads the walked var ONCE, only when the walk is still its node's own and was not cancelled, so the combo re-syncs once (silently on success); never drop it or re-sync a superseded or cancelled walk. The flap handle and speedbrake keep the pilot's pick: `ProcessSimVarUpdate` consumes their delivery. Full: docs/invariants/md11-controls.md#md11-27

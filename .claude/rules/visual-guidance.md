---
paths:
  - "MSFSBlindAssist/Services/VisualGuidance*.cs"
  - "MSFSBlindAssist/Services/HandFlyManager.cs"
  - "MSFSBlindAssist/Services/LiftoffHandoffBreadcrumb.cs"
  - "MSFSBlindAssist/Hotkeys/**"
  - "MSFSBlindAssist/MainForm.Hotkeys.cs"
  - "tests/MSFSBlindAssist.Tests/**/*HandFly*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LiftoffHandoff*.cs"
  - "MSFSBlindAssist/Forms/Settings/HandFlyPanel.cs"
---
# Visual guidance, hand fly and the liftoff handoff rules

Loaded when Claude reads matching code. Background: docs/visual-guidance.md. Full text of each rule: docs/invariants/visual-guidance.md.

- [VG-1] The liftoff cue wording and its mute are one decision in `LiftoffHandoffBreadcrumb.For(...)`; never split them or size a mute by estimate (measure at SAPI `Rate = 0`), and keep every mute under the 3504 ms ceiling by shortening the phrase, never widening the mute (more: see full). Full: docs/invariants/visual-guidance.md#vg-1
- [VG-2] Every path that activates hand fly or visual guidance must leave output hotkey mode first, or the quick-access keys stay dead for the rest of the session; the liftoff auto-handoff calls `hotkeyManager.ExitOutputHotkeyMode()` itself, and that exit must stay silent. Full: docs/invariants/visual-guidance.md#vg-2
- [VG-3] Visual guidance must not require hand fly mode: never reintroduce a `!handFlyManager.IsActive` gate, which produced a confusing three-tone overlap. Full: docs/invariants/visual-guidance.md#vg-3
- [VG-4] Hand fly's tone must auto-mute while VG is active (`SuppressAudio`/`ResumeAudio`): VG's two tones share hand fly's Hz/pan mapping, so all three together is acoustically incoherent. Full: docs/invariants/visual-guidance.md#vg-4
- [VG-5] The quick-access hotkey set must stay reference-counted and shared between hand fly and VG; never split it into per-mode key sets, which caused a double-register conflict. Full: docs/invariants/visual-guidance.md#vg-5
- [VG-6] There is no single-tone VG mode: never reintroduce a flag that gates off the current tone; the dual tone is the design. Full: docs/invariants/visual-guidance.md#vg-6
- [VG-7] Always route `cachedBank` through `VisualGuidanceManager.StandardBank()` before any tone or bank-error use: SimConnect's `PLANE_BANK_DEGREES` is left-positive, the tone API right-positive. Full: docs/invariants/visual-guidance.md#vg-7
- [VG-8] `VisualGuidanceManager.Initialize` must stay idempotent by disposing existing tones first (`DisposeTones()`), never by calling `Stop()`, which announces "Visual guidance off" just before "Visual guidance active". Full: docs/invariants/visual-guidance.md#vg-8
- [VG-9] Never re-add a `Start()` call inside `Initialize`: tone start stays deferred to the first `ProcessUpdate`, or a brief fused-tone glitch reappears. Full: docs/invariants/visual-guidance.md#vg-9
- [VG-10] The follower (current) tone must start only if the desired tone started; don't reorder `StartTonesIfNeeded`'s try blocks. Full: docs/invariants/visual-guidance.md#vg-10
- [VG-11] VG auto-deactivation on the airborne-to-on-ground edge must not be gated on ground speed or any other condition: a landing at any speed must trigger it. Full: docs/invariants/visual-guidance.md#vg-11
- [VG-12] Never split VG's lateral and vertical guidance into separate tones: the matching idiom needs one oscillator per role (desired and current). Full: docs/invariants/visual-guidance.md#vg-12
- [VG-13] Desired and current tone waveforms must default to different shapes (triangle and sine, the user settings `VisualGuidanceToneWaveform` and `VisualGuidanceCurrentToneWaveform`): identical waveforms phase-cancel at a matched state, exactly when the pilot most needs the difference audible. Full: docs/invariants/visual-guidance.md#vg-13
- [VG-14] Never bake aircraft-specific VG numbers into `VisualGuidanceManager` as consts; they belong on `IAircraftDefinition.GetVisualGuidanceProfile()`. Full: docs/invariants/visual-guidance.md#vg-14
- [VG-15] Never collapse `GlideslopeAltitudeBiasFt` and `FlareAltitudeBiasFt` into one shared constant: they apply in different code paths (glideslope error vs phase detection) and were measured separately. Full: docs/invariants/visual-guidance.md#vg-15
- [VG-18] Visual guidance must fail as a missing audible reference, never as a wrong steering command: tone work leaves the guidance math alone, and a deliberate change to the guidance math keeps left and right as the sign tests pin them and is measured in the VirtualPilot approach harness against the baseline first. Full: docs/invariants/visual-guidance.md#vg-18
- [VG-19] VG's manual-query grace window may suppress only the bank callouts; phase changes and distance callouts must still fire during a manual readout. Full: docs/invariants/visual-guidance.md#vg-19
- [VG-20] Every rate limit, smoothing filter and derivative in `VisualGuidanceManager` runs on the measured frame time through `VisualGuidanceTiming` (rate × dt, per-second smoothing factors, rates over a time window, dt capped at 1 s); never a per-update constant, since guidance updates every sim frame. Full: docs/invariants/visual-guidance.md#vg-20
- [VG-21] Bank callouts speak only through `VisualGuidanceBankCalloutGate`, at once, the update the phrase changes (the error rounded to a degree; "matched" in at 0.4°, out at 1.0°), interrupting: never a hold, a gap or a wait for a steady bank (the owner, 2026-11-06). Only visual guidance's own lines hold them, for their measured speaking time. Full: docs/invariants/visual-guidance.md#vg-21
- [VG-22] F (`HotkeyAction.ReadTargetFPM`) speaks `VisualGuidanceManager.GetStatusReadout()`: the phase, the sideways position, then the vertical, composed by `VisualGuidanceStatusText`, culture-invariant. Whatever visual guidance speaks on its own in a phase belongs in that phase's readout. Full: docs/invariants/visual-guidance.md#vg-22
- [VG-23] While joining final, visual guidance holds the heading being flown (bank 0) until "Turn onto final", which comes when the capture law, clamped to the intercept being flown (`VisualGuidanceJoin.TurnCueCrossNm`), first asks 5° of bank; it never asks for a steeper intercept, and Joining never becomes Vectoring by itself. Full: docs/invariants/visual-guidance.md#vg-23
- [VG-24] Above the reference (the path on final, BA's plan while vectoring, the held altitude while joining) means a larger target descent and a lower desired pitch (`VisualGuidanceVertical.TargetDescentDeg`, desired = trim − descent); test-pinned, never flipped. Full: docs/invariants/visual-guidance.md#vg-24
- [VG-25] The desired pitch is the learned trim (`VisualGuidanceTrimLearner`: pitch minus flight path, smoothed) minus the target descent angle (in the flare plus VG-27's correction from the smoothed flight path); it never uses the instantaneous vertical-speed derivative or the live AoA, which made the old law run away from the pilot. Full: docs/invariants/visual-guidance.md#vg-25
- [VG-26] On final and while vectoring the vertical cue never asks for a climb: below the path or the plan it asks for level flight (`TargetDescentDeg`'s minimum 0); only the joining level hold asks back up, to its held altitude. Full: docs/invariants/visual-guidance.md#vg-26
- [VG-27] The flare's cue is the held level attitude minus the descent angle of a sink easing from the aircraft's own to 150 fpm at touchdown, plus `FlareCorrectionGain` × how much steeper the smoothed descent still is (`VisualGuidanceFlare.CueDeg`); it starts by time to touchdown or at the flare height; never a fixed pitch. Full: docs/invariants/visual-guidance.md#vg-27
- [VG-28] The capture gain is capped at 90% of the turn rate the profile's largest approach bank gives at the ground speed (`VisualGuidanceJoin.TurnLimitedCaptureGain`); every turn, cue and the pattern's size (`ResizePattern`, each update until "Turn base") use it at the speed now. Only the decision takes the full gain, planning for a pilot who slows. Full: docs/invariants/visual-guidance.md#vg-28
- [VG-29] The lateral law steers the ground track to touchdown, crab and all (`VisualGuidanceManager.LateralTrackDeg`); never the heading low down to decrab: bank turns the path with the nose, and the wind carries the aircraft off the centreline. The decrab is the pilot's, with the rudder in the flare. Full: docs/invariants/visual-guidance.md#vg-29

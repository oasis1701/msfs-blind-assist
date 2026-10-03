---
paths:
  - "MSFSBlindAssist/Services/VisualGuidanceManager.cs"
  - "MSFSBlindAssist/Services/HandFlyManager.cs"
  - "MSFSBlindAssist/Services/LiftoffHandoffBreadcrumb.cs"
  - "MSFSBlindAssist/Hotkeys/**"
  - "MSFSBlindAssist/MainForm.Hotkeys.cs"
  - "tests/MSFSBlindAssist.Tests/**/*HandFly*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*LiftoffHandoff*.cs"
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
- [VG-8] `VisualGuidanceManager.Initialize` must stay idempotent: never remove its `Stop()`-first guard. Full: docs/invariants/visual-guidance.md#vg-8
- [VG-9] Never re-add a `Start()` call inside `Initialize`: tone start stays deferred to the first `ProcessUpdate`, or a brief fused-tone glitch reappears. Full: docs/invariants/visual-guidance.md#vg-9
- [VG-10] The follower (current) tone must start only if the desired tone started; don't reorder `StartTonesIfNeeded`'s try blocks. Full: docs/invariants/visual-guidance.md#vg-10
- [VG-11] VG auto-deactivation on the airborne-to-on-ground edge must not be gated on ground speed or any other condition: a landing at any speed must trigger it. Full: docs/invariants/visual-guidance.md#vg-11
- [VG-12] Never split VG's lateral and vertical guidance into separate tones: the matching idiom needs one oscillator per role (desired and current). Full: docs/invariants/visual-guidance.md#vg-12
- [VG-13] Desired and current tone waveforms must stay different (triangle and sine): identical waveforms phase-cancel at a matched state, exactly when the pilot most needs the difference audible. Full: docs/invariants/visual-guidance.md#vg-13
- [VG-14] Never bake aircraft-specific VG numbers into `VisualGuidanceManager` as consts; they belong on `IAircraftDefinition.GetVisualGuidanceProfile()`. Full: docs/invariants/visual-guidance.md#vg-14
- [VG-15] Never collapse `GlideslopeAltitudeBiasFt` and `FlareAltitudeBiasFt` into one shared constant: they apply in different code paths (glideslope error vs phase detection) and were measured separately. Full: docs/invariants/visual-guidance.md#vg-15
- [VG-16] The `MAX_DESCENT_RATE_FPM` safety clamp must stay dynamic (`min(-1500, natural×1.3)`) so a legitimate steep-approach descent rate is never clipped. Full: docs/invariants/visual-guidance.md#vg-16
- [VG-17] The pitch PID's `fpmError`/`fpmErrorRate` coefficients must stay positive (same sign as the error); never reintroduce the old leading minus, which produced wrong-direction guidance. Full: docs/invariants/visual-guidance.md#vg-17
- [VG-18] Tone work must leave the PID math, phase machine and lateral arc capture untouched: VG's failure mode must always be "missing audible reference", never "wrong steering command". Full: docs/invariants/visual-guidance.md#vg-18
- [VG-19] VG's manual-query grace window may suppress only the two chatty per-second callouts (bank guidance, centerline deviation); phase changes and distance callouts must still fire during a manual readout. Full: docs/invariants/visual-guidance.md#vg-19

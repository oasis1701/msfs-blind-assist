---
paths:
  - "MSFSBlindAssist/Services/Audio*.cs"
  - "MSFSBlindAssist/Services/ProximityBeeper.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Audio*.cs"
  - "MSFSBlindAssist/Services/VisualGuidanceManager.cs"
  - "MSFSBlindAssist/Forms/Settings/TestTone*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*TestTonePlayer*.cs"
  - "tests/MSFSBlindAssist.Tests/**/*GuidanceToneDevice*.cs"
  - "MSFSBlindAssist/Services/PhaseContinuousOscillator.cs"
  - "MSFSBlindAssist/Services/LowPassFilterProvider.cs"
  - "MSFSBlindAssist/Forms/Settings/AudioPanel.cs"
  - "MSFSBlindAssist/Forms/Settings/HandFlyPanel.cs"
  - "MSFSBlindAssist/Forms/Settings/TaxiGuidancePanel.cs"
---
# Guidance tone output device rules

Loaded when Claude reads matching code. Background: docs/audio.md. Full text of each rule: docs/invariants/audio-output.md.

- [AUD-1] Lock order is owner lock → `AudioToneGenerator.startStopLock` → `AudioOutputRouter.Gate`, never the reverse: `RunSweep` releases `Gate` before `RebindTo`, sweeps stay on the router's own worker thread, and `CurrentDeviceId`/`NeedsDevice` stay lock-free volatile reads. Full: docs/invariants/audio-output.md#aud-1
- [AUD-2] A tone moves iff its actual bound endpoint differs from the resolved target or it is flagged `NeedsDevice` (`AudioRebindPlanner.Plan`); never reintroduce a process-global "last applied device". A `DevicePinned` or empty-bound-id tone is not planned on mismatch alone. Full: docs/invariants/audio-output.md#aud-2
- [AUD-3] Registration in the router's registry means "alive and not Stopped by its owner", never "currently playing": a tone whose open failed stays registered with `NeedsDevice` set, so a later sweep can retry it. Full: docs/invariants/audio-output.md#aud-3
- [AUD-4] `AudioDeviceResolution.DeviceId` carries the real effective endpoint id (the saved device when present, else the live Windows default, told apart by `FellBack`); empty means nothing is resolvable, and a fallback never rewrites the saved preference. Full: docs/invariants/audio-output.md#aud-4
- [AUD-5] `OpenFor`'s `deviceIdOverride` is three-state (`null` = saved setting, `""` = `AudioDeviceSelector.FollowWindowsDefaultId`, else that endpoint); never collapse `""` to `null` with `IsNullOrWhiteSpace`, or auditioning the Windows default plays on the saved device. Full: docs/invariants/audio-output.md#aud-5
- [AUD-6] WASAPI shared mode only (`AudioClientShareMode.Shared`): exclusive mode would take the endpoint away from the simulator and from the screen reader. Full: docs/invariants/audio-output.md#aud-6
- [AUD-7] Generate the tone at the endpoint's own mix rate, read off `WasapiOut.OutputWaveFormat`, never a second `AudioClient` probe (a quality choice only); a rebind must rebuild the oscillator, not swap the player under it, since its phase step derives from the rate. Full: docs/invariants/audio-output.md#aud-7
- [AUD-8] The four device notices (fell back, recovered, default changed, no device) are all queued, raised from `RunSweep` with `Gate` released after that sweep's rebinds; `OpenFor` never announces, and the sink marshals to the UI thread with non-blocking `BeginInvoke`. Full: docs/invariants/audio-output.md#aud-8
- [AUD-9] The session's first sweep is a baseline (`AudioOutputRouter.RequestBaselineSweep`, from `MainForm`'s connect timer after "Initializing, please wait"): at it `FellBackToDefault` and `NoDeviceAvailable` speak and `DefaultDeviceChanged`/`RecoveredPreferred` stay silent; never re-silence the degraded pair (more: see full). Full: docs/invariants/audio-output.md#aud-9
- [AUD-10] `AudioToneGenerator`'s default pitch mapping stays asymmetric (−10°/+20° over 200–800 Hz) and piecewise, 0° at the centre frequency, because hand fly starts at liftoff already nose up; never make it symmetric ±10° or one straight line, and never add an asymmetric `Configure` overload. Full: docs/invariants/audio-output.md#aud-10
- [AUD-11] `IMMNotificationClient` callbacks must not block or re-enter the enumerator: they may only request a sweep (an `Interlocked` write plus an event `Set`), inside a catch so no failed HRESULT reaches the Windows audio service. Full: docs/invariants/audio-output.md#aud-11
- [AUD-12] `RegisterEndpointNotificationCallback` is not `PreserveSig`: a refusal throws a `COMException`. Never narrow the constructor's catch around it, or `Shared`'s `Lazy` caches the failure and silences every guidance tone for the session. Full: docs/invariants/audio-output.md#aud-12
- [AUD-13] `VisualGuidanceManager` re-arms its tone pair on `NeedsDevice` (watching BOTH tones), never on `!IsPlaying`, once per outage (`toneReArmSpent`); `StartTonesIfNeeded` must keep a tone whose Start failed, never dispose or null it, since it is the router's retry set. Full: docs/invariants/audio-output.md#aud-13
- [AUD-14] The Test Tone audition sweep must reach both channels at every duration used (20/40/60 ticks, via the shared `TestTonePan.FullCycle`), or a dead left driver passes; pin every length, not one. Full: docs/invariants/audio-output.md#aud-14

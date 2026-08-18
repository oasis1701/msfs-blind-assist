# Guidance Tone Output Device

> **Read this when:** working on which Windows audio endpoint MSFS BA's own guidance tones
> play on — taxi steering, takeoff assist centerline, hand fly, visual landing guidance, the
> GSX docking proximity beeps. **Not** screen-reader speech — NVDA and JAWS pick their own
> output device and this feature never touches that.

## Overview

One setting (Settings dialog → Audio tab, `Forms/Settings/AudioPanel.cs`) covering every
guidance tone the app generates, so a pilot can send those tones to a headset while the
simulator itself keeps the speakers. Persisted as `UserSettings.GuidanceToneDeviceId` /
`GuidanceToneDeviceName`.

**Key files:**
- `Services/AudioOutputDeviceService.cs` — static, process-wide: enumerates WASAPI render
  endpoints, opens the effective output for a tone, and moves already-sounding tones when the
  pilot changes the setting.
- `Services/AudioToneGenerator.cs` — one instance per guidance tone (`TaxiSteeringTone`,
  `TakeoffAssistManager`, `HandFlyManager`, `VisualGuidanceManager`'s two tones,
  `ProximityBeeper`'s docking beep), plus test-tone audition instances on
  `Forms/Settings/HandFlyPanel.cs` (line 630), `Forms/Settings/TaxiGuidancePanel.cs`
  (line 518), and the main Audio Settings panel. Owns the oscillator, `Start`/`Stop`/`RebindOutput`.
- `Services/AudioDeviceSelector.cs` — pure resolution logic (saved id vs. what currently
  exists vs. the live default), deliberately free of any NAudio reference so it is
  unit-testable with no audio hardware. Also owns the status-line and fallback-announcement
  wording.
- `Services/AudioOutputDevice.cs` / `AudioOutputSession.cs` / `AudioDeviceResolution.cs` — the
  three small data types the above pass around.
- `Forms/Settings/AudioPanel.cs` — the settings UI: device combo, status line, Test Tone
  audition button.

## Invariants

- **Lock order: `AudioToneGenerator.startStopLock` → `AudioOutputDeviceService.Gate`, never
  the reverse.** `ApplyDeviceChange` snapshots the live-generator registry under `Gate` and
  **releases it before calling `RebindOutput()`**, because `RebindOutput` takes the
  generator's own lock and then re-enters `Register`/`Unregister`, which take `Gate` again. A
  UI thread that took the locks in the other order would deadlock against a tone's own
  start/stop.

- **`_lastAppliedDeviceId` is seeded ONCE per session, in `CreatePlayer`, guarded by
  `_lastAppliedSeeded`.** Re-seeding on every tone start (the original bug) lets a tone that
  starts in the window between a settings save and `MainForm`'s `ApplyDeviceChange()` call
  re-latch the field onto the *new* id first, so `ApplyDeviceChange`'s comparison reads
  new==new, early-returns, and any tone already sounding on the *old* device is stranded
  there — and re-saving the same device can't recover it either, since the comparison still
  matches. After the first seed of a process, `ApplyDeviceChange` owns the field exclusively;
  `CreatePlayer` never touches it again.

- **`CreatePlayer`'s `deviceIdOverride` is a three-state contract** — `null` means "use the
  saved setting" (what every real guidance tone passes, and the only value that participates
  in the seed/tracking above); `""` (`AudioDeviceSelector.FollowWindowsDefaultId`) means
  *explicitly* the Windows default device, regardless of what is saved; any other value is
  that specific endpoint id. Only the settings panel's Test Tone audition ever passes `""` or
  a real id. **Never collapse `""` to `null`** with an `IsNullOrWhiteSpace`-style check before
  calling — that folds the second state into the first, so auditioning "Windows default
  device" silently plays on the *saved* device instead (the bug that made the one control
  built to prove which device is which lie about it). See the `<param>` docs on
  `AudioOutputDeviceService.CreatePlayer` and `AudioToneGenerator.Start`.

- **WASAPI SHARED mode only** (`AudioClientShareMode.Shared` in `Build`). Exclusive mode would
  take the endpoint away from the simulator and from the screen reader, which may well be
  using the same one.

- **The tone is generated at the endpoint's own mix sample rate**, read once per open
  (`AudioClient.MixFormat.SampleRate`, falling back to 44100 Hz if that throws). A device
  change rebuilds the oscillator from scratch (`RebindOutput` tears down and calls
  `StartLocked` again) rather than swapping the player under the same oscillator, because the
  new endpoint may be clocked differently — reusing an oscillator built for the old rate would
  play the tone audibly sharp or flat.

- **The fallback-announcement sink is a NON-BLOCKING marshal, dispatched on the thread pool,
  and must never be invoked while any `AudioToneGenerator.startStopLock` is held.**
  `AnnounceFallbackOnce` is only ever reached from `CreatePlayer`, which is only ever reached
  from `AudioToneGenerator.StartLocked` — a context that always holds that generator's own
  lock. `MainForm` assigns `AudioOutputDeviceService.AnnounceFallback` at startup, and that
  delegate must itself marshal to the UI thread with `Control.BeginInvoke`, never
  `Control.Invoke` — `Start()` runs on the `ProximityBeeper` timer thread and on the taxi
  position thread, and a synchronous wait there can park behind a UI thread that is itself
  inside `ApplyDeviceChange → RebindOutput` waiting on the same generator lock.

- **A fallen-back tone can recover onto a reconnected device, but only via a Settings save (or
  a fresh tone starting), never automatically.** `_lastAppliedFellBack` records whether the
  saved preference's *last* resolution had to fall back to the default endpoint (written only
  by `CreatePlayer`'s saved-preference path). `ApplyDeviceChange`'s usual guard — "did the
  saved id change" — is not enough on its own: once a tone has fallen back, the id a pilot
  could re-select is still the very same device, so re-saving it compares
  unchanged-to-unchanged and would silently no-op forever. `ApplyDeviceChange` additionally
  rebinds when the id is unchanged but `_lastAppliedFellBack` is true, which resets the
  fallback-announcement latch and forces a fresh resolution attempt through whatever tones are
  still registered. There is **deliberately no `IMMNotificationClient` device-arrival
  listener** — that would make the recovery automatic and is real, but out-of-scope, follow-up
  work; today the pilot (or the next tone that happens to start) still has to trigger the
  retry.

- **`AudioPanel` caches one WASAPI enumeration pass (`Enumerate()` + `DefaultEndpointInfo()`)
  for the lifetime of a `LoadFrom` call and reuses it in `UpdateStatusText`.**
  `UpdateStatusText` is wired to the device combo's `SelectedIndexChanged`, so a screen-reader
  user arrowing the dropdown fires it on every keystroke; resolving through
  `AudioOutputDeviceService.ResolveCurrent` there would re-enumerate WASAPI (two fresh
  `MMDeviceEnumerator` instances) per keystroke on the UI thread. `UpdateStatusText` instead
  calls the pure `AudioDeviceSelector.Resolve` directly against the cached lists.

- **The Test Tone button's state is set from what `PlayTestTone` actually achieved
  (`tone.IsPlaying` after `Start`), never assumed.** `AudioToneGenerator.Start` swallows its
  own exceptions by contract (audio is optional feedback). When the selected device fails to
  open, `CreatePlayer` falls back to `TryOpenDefault()` and the tone plays on the Windows
  default device with `IsPlaying == true`; the tone stays silent only when no endpoint can be
  opened at all. Assuming success left the button reading "Stop Test" for a tone that was never
  sounding, so the pilot's next press took the start branch again instead of stopping anything.
  A silent failure now also writes a reason into the status `TextBox` (never a `MessageBox` alone
  — a screen reader needs to be able to reach the reason by tabbing back to the status line, not
  just hear a modal at the moment it appeared).

## Related documentation

- [Visual Guidance](visual-guidance.md) — the dual-tone landing-guidance feature that
  `AudioToneGenerator` was originally built for; still the most detailed reference for the
  oscillator/pan/frequency-mapping side of the class, which this feature does not change.

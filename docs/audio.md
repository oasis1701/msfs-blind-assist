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

The tones **follow the hardware on their own**. Unplug the chosen headset and every sounding
tone moves to the Windows default endpoint; plug it back in and they move back; promote a
different default while the setting is "follow the default" and they follow that. Each of those
outcomes is spoken once, *after* the move has happened — with two deliberate exceptions at
startup, both of them silence rather than noise:

- **The session's first sweep is the silent baseline** (`RequestBaselineSweep`, called once from
  `MainForm` right after the announcement sink is wired). It seeds the router's last-target state
  from current reality and says nothing, because a pilot who has just launched the app has
  changed nothing. So the state the app *starts* in is never announced: a saved headset that was
  already unplugged at launch is not spoken about, only shown in the Audio tab's status line.
  A real sweep that coalesces into the baseline (an endpoint notification landing in the same few
  milliseconds) is silenced with it — a window that was silent before the baseline existed
  anyway.
- **`NoDeviceAvailable` is therefore unreachable at startup.** On a machine where nothing
  resolves at launch, the baseline is the only sweep that runs and it does not speak; the pilot
  is told the first time something else asks for a sweep (a device arriving, a settings save).
  The baseline does not dedup it away — it never touches the last-*spoken* pair — so that later
  sweep does say it.

**Key files:**
- `Services/AudioOutputRouter.cs` — the router. An **instance** (`IDisposable`) with a
  process-wide `Shared` singleton, owning: the live-tone registry, every WASAPI call
  (enumerate / open / default endpoint), the `IMMNotificationClient` subscription, and ONE
  dedicated background worker thread that performs every rebind.
- `Services/AudioRebindPlanner.cs` — the pure decision layer: given each live tone's actual
  binding and the resolved target, which tokens must move and which notice (if any) the pilot
  hears. No NAudio, no statics, no clock, so every routing decision is unit-tested on a CI
  runner with no audio hardware.
- `Services/AudioToneGenerator.cs` — one instance per guidance tone (`TaxiSteeringTone`,
  `TakeoffAssistManager`, `HandFlyManager`, `VisualGuidanceManager`'s two tones,
  `ProximityBeeper`'s docking beep), plus the audition instances the three settings panels
  build. Owns the oscillator, `Start`/`Stop`/`RebindTo`, and its own registry membership.
- `Services/AudioDeviceSelector.cs` — pure resolution logic (saved id vs. what currently
  exists vs. the live default), deliberately free of any NAudio reference. Also owns the
  status-line wording and all four spoken notices.
- `Services/AudioOutputDevice.cs` / `AudioOutputSession.cs` / `AudioDeviceResolution.cs` — the
  three small data types the above pass around.
- `Forms/Settings/AudioPanel.cs` — the settings UI: device combo, status line, Test Tone
  audition button. `Forms/Settings/TestTonePlayer.cs` is the shared audition driver behind all
  three Test Tone buttons (Audio, Hand Fly, Taxi Guidance).

## Invariants

- **LOCK ORDER: owner lock → `AudioToneGenerator.startStopLock` → `AudioOutputRouter.Gate`,
  never the reverse.** `Gate` is the INNERMOST lock in the audio stack. `RunSweep` takes it to
  snapshot the registry, **releases it before calling `RebindTo`**, and takes it again to store
  the result — because `RebindTo` takes the generator's own lock and then re-enters
  `Register`/`Unregister`, which take `Gate` again. Two consequences that must not be eroded:
  the sweep runs on a **dedicated worker thread** precisely so it never runs on a thread that
  already holds an owner's lock; and `CurrentDeviceId`/`NeedsDevice` must stay **lock-free
  volatile field reads**, because the snapshot reads them while holding `Gate` and giving
  either one a lock would make that a `Gate → startStopLock` acquisition, i.e. exactly the
  reversal.

- **A tone must move iff its ACTUAL bound endpoint differs from the resolved target, or it is
  flagged `NeedsDevice`.** That is a per-generator fact, decided in `AudioRebindPlanner.Plan`.
  The predecessor compared saved-setting ids in three process-global fields
  (`_lastAppliedDeviceId` / `_lastAppliedSeeded` / `_lastAppliedFellBack`, all deleted), which
  could not represent "generator A is on the speakers while generator B is on the headset" — a
  state reachable whenever one tone starts before a settings save and another after — and whose
  id-only guard silently no-opped whenever the id had not changed, so a fallen-back tone could
  never recover. Do not reintroduce a process-global "last applied device" in any form.

- **Registration means "alive and its owner has not stopped it", NEVER "currently sounding".**
  `AudioToneGenerator` registers in its constructor and again in `EnsureRegisteredLocked` on a
  start, and unregisters only on `Stop()`/`Dispose()`. A start whose open FAILED stays
  registered with `NeedsDevice` set — that is the whole mechanism by which a later sweep retries
  it. `Register` is not idempotent on the router side, so the generator's own `registered` flag
  is what stops a second entry (two entries would tear one tone down and rebuild it twice for a
  single device change).

- **`AudioDeviceResolution.DeviceId` carries the REAL effective endpoint id**, not the saved
  preference: the saved device when it is present, otherwise the live Windows default, with
  `FellBack` telling the two apart. Empty means nothing is resolvable at all. The saved
  preference is never rewritten on a fallback — it is what brings the headset back on reconnect.

- **`OpenFor`'s `deviceIdOverride` is a three-state contract** — `null` means "use the saved
  setting" (what every real guidance tone passes); `""`
  (`AudioDeviceSelector.FollowWindowsDefaultId`) means *explicitly* the Windows default device,
  regardless of what is saved; any other value is that specific endpoint id. Only the settings
  panels' Test Tone audition ever passes `""` or a real id. **Never collapse `""` to `null`**
  with an `IsNullOrWhiteSpace`-style check before calling — that folds the second state into the
  first, so auditioning "Windows default device" silently plays on the *saved* device instead
  (the bug that made the one control built to prove which device is which lie about it).

- **WASAPI SHARED mode only** (`AudioClientShareMode.Shared` in `Build`). Exclusive mode would
  take the endpoint away from the simulator and from the screen reader, which may well be using
  the same one.

- **The tone is generated at the endpoint's own mix sample rate, read off the player**
  (`WasapiOut.OutputWaveFormat.SampleRate`, which the constructor has already set from
  `AudioClient.MixFormat`) — never a second `device.AudioClient` probe, which activates an
  `IAudioClient` nothing owns. This is a **quality** choice, not a correctness one, and both
  claims that used to justify it were wrong against NAudio 2.3.0. Shared mode always opens with
  `AutoConvertPcm | SrcDefaultQuality` and converts whatever it is handed — the whole
  `IsFormatSupported` / `ResamplerDmoStream` / `dmoResamplerNeeded` block sits inside
  `if (shareMode == AudioClientShareMode.Exclusive)`, so **NAudio's DMO resampler never ran on
  this path at any rate**. And the oscillator declares the same rate it generates at while
  `Init` sets `OutputWaveFormat = waveProvider.WaveFormat`, so declared and generated **cannot
  diverge** and a rebind to a differently-clocked endpoint could never have played the tone
  sharp either. Generating at the endpoint's own rate is still worth doing — it keeps the
  engine's sample-rate converter out of the chain — but for that reason and no other. A rebind
  still rebuilds the oscillator rather than swapping the player under it, because the new
  endpoint may mix at a different rate and the oscillator's phase step is derived from it.

- **There are exactly four spoken notices, all queued, all spoken AFTER the outcome is known:**
  fell back to the default / recovered the preferred device / the default changed underneath a
  "follow the default" setting / no device available at all. They are raised **only from
  `RunSweep`, only with `Gate` released, and only after that sweep's rebinds have run** — so
  what the pilot hears has already happened. `OpenFor` never announces: the predecessor spoke
  "using the Windows default device" from inside the open path, *before* the default endpoint
  had been tried at all, so it said so even in the case where the default then failed to open
  and there was no tone. `AudioOutputRouter.Announce` names its cases explicitly with a discard
  arm, so a new `AudioRouteNotice` member stays silent until someone deliberately gives it a
  voice.

- **The session's first sweep is a SILENT baseline, requested once from `MainForm`.**
  `RequestBaselineSweep` seeds `_lastTargetDeviceId`, `_lastFellBack` and
  `_lastFollowingWindowsDefault` from current reality so that the first *real* change is judged
  against a true baseline. Without it those fields stayed at their blank initial values until
  something changed, and `AudioRebindPlanner.ChooseNotice` reads them: with the setting on
  "Windows default device", the first mid-flight default-device change suppressed
  `DefaultDeviceChanged` (because `previouslyFollowingWindowsDefault` read false, i.e. "the pilot
  must have just chosen this") while the same plan **still moved every sounding tone** — an
  unexplained jump to another endpoint, exactly once per session. The baseline flag suppresses
  the **announcement only**: the rebinds run and the last-target trio is stored, which is the
  entire point. It must **never** seed `_lastNotice`/`_lastNoticeDeviceId` — those record what the
  pilot has *heard*, so seeding them from an unspoken sweep would dedup away the first real
  announcement of that kind. The flag is consumed on the same worker pass it is observed, so it
  can never carry forward and silence a later sweep.

- **The announcement sink must marshal to the UI thread with a NON-BLOCKING
  `Control.BeginInvoke`, never `Control.Invoke`.** The marshal is required because the sink is
  invoked on the **router's own worker thread**, and `ScreenReaderAnnouncer` silently no-ops off
  the UI thread. (It is *not* required because a tone's `Start()` runs on a background thread —
  an earlier version of this bullet said so and it was false: every production tone owner runs
  on the UI thread, since all SimConnect dispatch does, and `ProximityBeeper`'s timer thread
  calls `UpdateVolume`, not `Start`.) It must be non-blocking because the UI thread can be
  inside the settings save that asked for the sweep, and a synchronous wait would park the
  worker behind a message pump that is itself waiting. Queued `Announce`, never
  `AnnounceImmediate` — a device notice must never interrupt a hold-short or landing callout.

- **`IMMNotificationClient` callbacks must not block and must not re-enter the enumerator.**
  Every callback does an `Interlocked` write plus an event `Set` (`RequestSweep`) and returns,
  inside a `catch` so nothing can cross back into the Windows audio service as a failed HRESULT.
  `OnDefaultDeviceChanged` is filtered to one `(flow, role)` pair — Windows raises it once per
  pair, i.e. six times per change — which is for the log's sake, not for correctness (they would
  coalesce into one sweep anyway). `OnPropertyValueChanged` requests nothing: it fires on every
  volume step, and the one property change that might look routing-relevant, a rename, cannot
  be, because an endpoint id is stable across renames.

- **`RegisterEndpointNotificationCallback` is NOT `PreserveSig`**, despite its `int` return —
  the method-impl flags are IL-only on both the NAudio wrapper and the underlying
  `IMMDeviceEnumerator` method, so the CLR applies HRESULT transformation and **a refusal
  arrives as a thrown `COMException`** (probed against this exact package: register → 0, first
  unregister → 0, second unregister → `COMException 0x80070490` "Element not found"). **Do not
  narrow the constructor's catch** around that call on the strength of it "returning" a status:
  a refusal escaping the constructor would be cached permanently by `Shared`'s `Lazy`
  (`ExecutionAndPublication` caches the factory's exception) and **every guidance tone in the
  process would then be silent for the whole session**. The return is still read, so the code
  stays correct if a future NAudio ever marks the method `PreserveSig`.

- **`VisualGuidanceManager` re-arms its tone pair on `NeedsDevice`, never on `!IsPlaying`.**
  `isPlaying` is deliberately false for the *whole* of a healthy rebind (`RebindTo` cleans up,
  and `StartLocked` flips it only once there is a working, playing chain), so an `!IsPlaying`
  guard races the router and tears down a rebind that was about to succeed. `NeedsDevice` is the
  flag that means "this generator does not have a working output right now".

  **The re-arm watches BOTH tones**, not just the reference one. A sweep rebinds the two
  generators independently, so either can fail on its own: a successful `desiredAttitudeTone`
  rebind followed by a failed `currentAttitudeTone` open left the follower registered with
  `NeedsDevice` set and silent, the re-arm never fired, and the pilot flew the rest of the
  approach hearing the commanded attitude with nothing to zero-beat it against — the mirror
  image of the lone-drone state this block exists to prevent. `currentAttitudeTone` is read
  null-conditionally because it can legitimately be null while the reference is not
  (`StartTonesIfNeeded` starts the follower only if the reference started).

- **The Test Tone audition sweep must reach BOTH channels at every duration used** (20 / 40 / 60
  ticks — Audio, Taxi Guidance, Hand Fly). `TestTonePlayer.FullCycle` is shared by all three and
  pinned by `FullCycle_ReachesBothChannelsAtEveryPanelDuration`. The old per-panel
  `sin(i * 0.15)` never went negative over 20 ticks (0–2.85 rad, entirely inside `[0, π]`), so
  the Audio panel's own audition — the one control built to prove which device is which — never
  exercised the left channel, and a dead left driver passed it. The defect was
  duration-dependent, so pinning only one length would let the same class of bug back in at
  another.

- **The Test Tone button's state is set from what actually happened, never assumed.**
  `TestTonePlayer.TryStart` reads `tone.IsPlaying` back after the caller's `start` lambda
  returns; a null return, a silent `Start` failure and a thrown exception all end as "not
  playing" — tone disposed, failure reported, button left reading "Test Tone". `Text` and
  `AccessibleName` are written together by one private setter and nowhere else. A silent failure
  also writes a reason into the Audio panel's status `TextBox`: a screen reader must be able to
  tab back to the reason, not merely hear a modal at the moment it appeared.

- **`AudioPanel` caches one WASAPI enumeration pass (`Enumerate()` + `DefaultEndpointInfo()`)
  for the lifetime of a `LoadFrom` call and reuses it in `UpdateStatusText`.**
  `UpdateStatusText` is wired to the device combo's `SelectedIndexChanged`, so a screen-reader
  user arrowing the dropdown fires it on every keystroke. `Enumerate()` walks every active
  render endpoint and `DefaultEndpointInfo()` does a `GetDefaultAudioEndpoint` lookup; doing
  either per keystroke on the UI thread is what the cache exists to avoid. `UpdateStatusText`
  calls the pure `AudioDeviceSelector.Resolve` against the cached lists instead.

## Related documentation

- [Visual Guidance](visual-guidance.md) — the dual-tone landing-guidance feature that
  `AudioToneGenerator` was originally built for; still the most detailed reference for the
  oscillator/pan/frequency-mapping side of the class, which this feature does not change.

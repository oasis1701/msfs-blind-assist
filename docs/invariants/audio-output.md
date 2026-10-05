# Guidance tone output device — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/audio-output.md`, which Claude Code loads when it reads matching code. Background: [audio.md](../audio.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## AUD-1

- LOCK ORDER: owner lock → `AudioToneGenerator.startStopLock` → `AudioOutputRouter.Gate`, never the reverse — `RunSweep` must release `Gate` before calling `RebindTo` (which re-enters `Register`/`Unregister` from inside the generator's own lock), the sweep must stay on the router's OWN dedicated worker thread (so it never runs on a thread already holding an owner's lock), and `CurrentDeviceId`/`NeedsDevice` must stay lock-free volatile field reads — the snapshot reads them while holding `Gate`, so giving either one a lock IS the reversal. → [audio.md](../audio.md)

## AUD-2

- A tone must move iff its ACTUAL bound endpoint differs from the resolved target, or it is flagged `NeedsDevice` (`AudioRebindPlanner.Plan`) — never a process-global saved-id compare. The deleted `_lastAppliedDeviceId`/`_lastAppliedSeeded`/`_lastAppliedFellBack` trio could not express "one tone on the speakers, another on the headset", and its id-only guard made a fallen-back tone permanently unrecoverable; do not reintroduce a "last applied device" global in any form. Two carve-outs on the MISMATCH half only, never the `NeedsDevice` half: a `DevicePinned` tone (explicit `deviceIdOverride` — the audition) and a tone whose bound id read back EMPTY are not planned on mismatch — for both, the "move" reopens the same device (or can never converge), an audible gap per sweep for zero routing effect. → [audio.md](../audio.md)

## AUD-3

- Registration in the router's registry means "alive and its owner has not Stopped it", NEVER "currently playing" — a tone whose open FAILED stays registered with `NeedsDevice` set, and that is the only thing that lets a later sweep find and retry it. → [audio.md](../audio.md)

## AUD-4

- `AudioDeviceResolution.DeviceId` carries the REAL effective endpoint id (the saved device when present, else the live Windows default, `FellBack` telling the two apart) — empty means nothing is resolvable at all; the saved preference is never rewritten on a fallback. → [audio.md](../audio.md)

## AUD-5

- `OpenFor`'s `deviceIdOverride` is a three-state contract — `null` = use the saved setting, `""` (`AudioDeviceSelector.FollowWindowsDefaultId`) = explicitly the Windows default device, anything else = that endpoint — never collapse `""` to `null` with an `IsNullOrWhiteSpace` check before calling; that makes auditioning "Windows default device" silently play on the saved device instead. → [audio.md](../audio.md)

## AUD-6

- WASAPI SHARED mode only (`AudioClientShareMode.Shared`) — exclusive mode would take the endpoint away from the simulator and from the screen reader, which may be using the same one. → [audio.md](../audio.md)

## AUD-7

- The tone is generated at the endpoint's OWN mix rate, read off `WasapiOut.OutputWaveFormat` (never a second `AudioClient` probe) — but that is a QUALITY choice only. Shared mode always opens with `AutoConvertPcm | SrcDefaultQuality` (NAudio 2.3.0's DMO-resampler block is entirely inside the EXCLUSIVE branch and never ran on this path), and the oscillator declares the same rate it generates at while `Init` copies that format, so a mismatch could never play the tone sharp — both of those old justifications were false. A rebind must still rebuild the oscillator rather than swap the player under it, because its phase step is derived from the rate. → [audio.md](../audio.md)

## AUD-8

- FOUR spoken notices (fell back / recovered / default changed / no device available), all QUEUED, all raised from `RunSweep` with `Gate` released and only AFTER that sweep's rebinds have run — `OpenFor` never announces (the predecessor spoke "using the Windows default device" before the default had even been tried). The sink must marshal to the UI thread with a NON-BLOCKING `BeginInvoke` because it is invoked on the ROUTER'S WORKER thread — NOT because a tone's `Start()` runs off the UI thread: it doesn't (every production tone owner is on the UI thread; `ProximityBeeper`'s timer thread calls `UpdateVolume`, not `Start`). → [audio.md](../audio.md)

## AUD-9

- The session's FIRST sweep is a BASELINE (`AudioOutputRouter.RequestBaselineSweep`, called once from `MainForm`'s connect timer right AFTER the "Initializing, please wait" announcement — after the sink is wired, which it needs, and never earlier in startup: requested at manager-init its startup phrase was spoken into the screen reader's own launch speech and cut off) — nothing else asks for a sweep at startup, so `_lastTargetDeviceId`/`_lastFellBack`/`_lastFollowingWindowsDefault` stayed blank until something changed: the session's FIRST default-device change was suppressed by the planner while STILL moving every tone (an unexplained endpoint jump, once per session), and `RecoveredPreferred` could never fire at all, since recovery needs a previous fallback to recover from. It NARROWS the announcement rather than suppressing it: `FellBackToDefault` and `NoDeviceAvailable` SPEAK at baseline (the saved configuration is not being honoured before the pilot touched anything; the only other channel is a Settings tab a blind pilot may never open; and in the second case every guidance tone is silent for the whole session — the same judgement the VATSIM startup check makes for Locked/Failed/first-install Installed), while `DefaultDeviceChanged`/`RecoveredPreferred` stay silent because nothing is wrong in either. Do NOT re-silence the degraded pair, and do NOT "fix" this by leaving `_lastFellBack` false at baseline — that half-seeds the trio, so the fallback lands at an arbitrary later moment describing a fact true since launch AND re-breaks recovery. `_lastNotice`/`_lastNoticeDeviceId` follow the SPEECH, not the sweep (untouched by a notice the baseline swallows, updated normally by one it speaks). → [audio.md](../audio.md)

## AUD-10

- `AudioToneGenerator`'s DEFAULT pitch mapping is ASYMMETRIC (−10° / +20° over 200–800 Hz) and PIECEWISE, anchored so 0° sits at the centre frequency — never "simplify" it back to a symmetric ±10° or to one straight end-to-end line. Those defaults ARE hand fly's mapping, and hand fly is AUTO-ACTIVATED AT LIFTOFF where an airliner is already 12–18° nose up: at ±10° the tone sat pinned at 800 Hz for the whole initial climb, carrying no information at the one moment attitude matters most. Equal down/up ranges collapse to the old single line, which is why visual guidance and the landing-flare assist (both `Configure` symmetric ranges) are untouched. TWO consumers inherit the defaults AND drive pitch — `HandFlyManager` and the settings-panel Test Tone preview; five more never call `Configure` but are safe only because they never call `UpdatePitch`. The price is nose-up resolution: 30 → 15 Hz/°, for the WHOLE flight, cruise included. `Configure` takes ONE symmetric pitch range and must not regain an asymmetric overload — a second one differing only by a trailing `double` binds silently with the bank range landing on the nose-up range. → [visual-guidance.md](../visual-guidance.md)

## AUD-11

- `IMMNotificationClient` callbacks must not block and must not re-enter the enumerator — they may only request a sweep (an `Interlocked` write plus an event `Set`), inside a catch so nothing crosses back into the Windows audio service as a failed HRESULT. → [audio.md](../audio.md)

## AUD-12

- `RegisterEndpointNotificationCallback` is NOT `PreserveSig` despite its `int` return — a refusal arrives as a THROWN `COMException`. Never narrow the constructor's catch around it: a refusal escaping the constructor is cached permanently by `Shared`'s `Lazy` and would silence every guidance tone in the process for the whole session. → [audio.md](../audio.md)

## AUD-13

- `VisualGuidanceManager` re-arms its tone pair on `NeedsDevice`, NEVER on `!IsPlaying` — `isPlaying` is deliberately false for the whole of a healthy rebind, so an `!IsPlaying` gate tears down a rebind that was about to succeed. The re-arm must watch BOTH tones (`currentAttitudeTone?.NeedsDevice == true` as well as the reference's), because a sweep rebinds the two independently and either can fail alone: watching only the reference left a failed follower silent and unretried, and the pilot flew the approach hearing a commanded attitude with nothing to match against. The re-arm is ONE-SHOT per outage episode (`toneReArmSpent`, cleared when both tones play again), and `StartTonesIfNeeded` must KEEP a tone whose Start failed — never dispose/null it: a failed Start leaves the generator registered with `NeedsDevice` set, which is the router's retry set, and disposing it was what made one failed retry permanently silence the rest of the approach even after audio returned. → [audio.md](../audio.md)

## AUD-14

- The Test Tone audition sweep must reach BOTH channels at every duration used (20/40/60 ticks, via the shared `TestTonePan.FullCycle`) — the old per-panel `sin(i*0.15)` never went negative over 20 ticks, so a dead left driver passed the one control built to catch it; the defect was duration-dependent, so pin every length, not one. → [audio.md](../audio.md)

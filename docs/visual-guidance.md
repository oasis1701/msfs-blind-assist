# Visual Guidance PID Tuning

Reference guide for understanding and tuning the visual guidance PID controller for runway approaches.

## Overview

The visual guidance system uses a **PID controller** to generate pitch and bank commands that guide the aircraft onto the runway centerline and glideslope. It accounts for wind drift using GPS ground track monitoring. The PID outputs are rendered to the pilot as a **dual-tone audio cue** the pilot matches by ear (see [Dual-Tone Audio Cue](#dual-tone-audio-cue)).

**Key files:**
- `MSFSBlindAssist/Services/VisualGuidanceManager.cs` — PID, phase machine, tone modulation, `StandardBank` helper, on-ground auto-deactivation hook
- `MSFSBlindAssist/Services/AudioToneGenerator.cs` — default 200–800 Hz pitch→Hz over −10°/+20° pitch (asymmetric — see below) / ±10° bank→pan; per-instance `Configure(minHz, maxHz, pitchRangeDeg, bankRangeDeg)` for aircraft-specific ranges. That pitch range is applied SYMMETRICALLY and there is deliberately no asymmetric overload — a second one differing only by a trailing `double` made `Configure(min, max, 6.0, 5.0, 12.0)` bind silently with the bank range landing on the nose-up range
- `MSFSBlindAssist/Aircraft/IAircraftDefinition.cs` — `VisualGuidanceProfile` (per-aircraft tunables incl. tone frequency range)
- `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` — default A320 profile
- `MSFSBlindAssist/Settings/UserSettings.cs` — `VisualGuidanceToneWaveform/Volume`, `VisualGuidanceCurrentToneWaveform/Volume`, `VisualGuidanceHardPanTone`
- `MSFSBlindAssist/Forms/Settings/HandFlyPanel.cs` — UI for all visual-guidance audio settings
- `MSFSBlindAssist/MainForm.Announcers.cs` — `HandleSpecialAnnouncements` method contains the `SIM_ON_GROUND` handler that auto-deactivates visual guidance on touchdown

## Dual-Tone Audio Cue

Two `AudioToneGenerator` instances always run side-by-side while visual guidance is active. There is no single-tone mode.

| Tone | Frequency source | Pan source | Default waveform |
|------|------------------|------------|------------------|
| **Desired** | PID-commanded pitch (200–800 Hz over ±6°) | PID-commanded bank (±1.0 over ±5°) | Triangle |
| **Current** | Aircraft's *actual* pitch | Aircraft's *actual* bank | Sine |

The default pitch range is deliberately **tighter than `AudioToneGenerator`'s own default** (−10°/+20° pitch); the bank range is the native ±10° (it was ±5°, 0.20 pan/°, from May 2026 until the owner widened it on 2026-11-06: on the pattern's downwind at 230 kt the guidance asked 4 to 6° and any bank past 5° sounded the same as the target). The pitch narrowing gives **50 Hz of beat per ° of pitch error** (vs 30 Hz/° nose-down and 15 Hz/° nose-up at the native default), and the bank range gives **0.10 pan delta per ° of bank error**. At 0.1° pitch error a pilot hears a 5 Hz beat (slow wobble); at 0.5° they hear a 25 Hz beat (clear fluttering). The trade-off is earlier saturation: the PID can command up to 25° bank during intercept, which saturates the desired tone at full pan. That's fine because the spoken bank-guidance announcements ("3 left", "matched", etc.) already cover the large-error regime — the tones own the precise near-matched-state cue, spoken cues own the gross corrections.

The generator's own pitch default is **asymmetric** (−10° nose-down, +20° nose-up) and **piecewise**, anchored so 0° always sits at the centre frequency. Visual guidance is unaffected — it configures a symmetric range, and equal down/up ranges collapse to the single straight line the mapping always was. The asymmetry exists for **hand fly**, which is auto-activated at liftoff where an airliner is already 12–18° nose up: under the old symmetric ±10° default the tone was past saturation from its first update and stayed pinned at 800 Hz for the whole initial climb. Anchoring 0° at the centre is what leaves the nose-down half numerically identical, so widening the top cost nothing already learned by ear.

**Two** consumers inherit these defaults *and* drive pitch: `HandFlyManager`, and the Hand Fly settings panel's Test Tone preview (`HandFlyPanel.StartTestTone`). The preview still sweeps only to +10°, i.e. 650 Hz, so it does **not** demonstrate the top of the range — extending it needs `MaxPitchDeltaPerTick` re-tuned by ear, so it is documented rather than changed. Five further construction sites never call `Configure` either (`AudioPanel`, `TaxiGuidancePanel`, `TaxiSteeringTone`, `ProximityBeeper`, `TakeoffAssistManager`) and are unaffected **only** because none of them calls `UpdatePitch`; add a frequency cue to one of those and it silently inherits this mapping.

The widening is not free. Nose-up resolution goes from 30 Hz/° to **15 Hz/°**, and that applies to the whole flight — cruise and descent included, not just the climb-out. A 1° nose-up excursion in the cruise now moves the tone half as far, with no second tone to beat against. That is the deliberate trade for covering a rotation attitude at all, and it is pinned by `AudioTonePitchMappingTests.TheNoseUpHalfBelowTheOldSaturationEdgeWasDeliberatelyReTuned`.

### Independence from HandFly mode

Visual guidance **does not require HandFly mode to be active**. It monitors its own attitude (pitch + bank) via `VISUAL_GUIDANCE_PITCH` and `VISUAL_GUIDANCE_BANK` events emitted from `SimConnectManager`'s `VISUAL_GUIDANCE_DATA` callback, alongside the existing position / altitude / ground-track data. When the pilot activates visual guidance:

- If HandFly mode is *also* active, **HandFly's single tone is automatically muted** via `HandFlyManager.SuppressAudio()`. Reason: HandFly's tone uses the same Hz/pan mapping as VG's tones (it shares `AudioToneGenerator`'s native defaults), so all three tones playing simultaneously gives pilots no way to tell which tone they should be following. Muting HandFly while VG is active leaves a clean two-tone matching exercise. HandFly's announcements (if its feedback mode includes them) continue to fire — only the audio is suppressed.
- When VG deactivates, HandFly's tone is automatically resumed (`HandFlyManager.ResumeAudio()`) if HandFly is still active and its feedback mode wants tones.

### Quick-access hotkeys (H, V, Q, S, D, B, P, A, F)

The single-letter no-modifier hotkeys are **shared** between HandFly and visual guidance — VG is hand-flying with extra audio guidance, so the same in-flight readouts apply:

| Key | Action |
|---|---|
| H | Read heading (magnetic) |
| V | Read vertical speed |
| Q | Read altitude AGL |
| S | Read airspeed (indicated) |
| D | Read destination-runway distance |
| B | Read bank angle |
| P | Read pitch |
| A | Read altitude (MSL) |
| F | Visual guidance status: phase, sideways position, vertical (VG-22) |

Registration is reference-counted inside `HotkeyManager.AcquireQuickAccessHotkeys` / `ReleaseQuickAccessHotkeys`: the first mode (HandFly or VG) to activate registers all 9 keys; the second mode bumps the ref count without re-registering; whichever mode deactivates last releases the keys. Per-key partial-failure tracking means a key that was unavailable on first try (some other app holding it) will be retried on the next acquire. WM_HOTKEY dispatch is unified — same `case` for each key regardless of which mode is active. Pressing F in HandFly-only mode triggers `ReadTargetFPM`, whose handler self-gates on `visualGuidanceManager.IsActive` and announces *"Visual guidance not active"* if appropriate.

Both tones use the *same* pitch→Hz and bank→pan mappings, so the rules are:

- **Frequency match (zero-beat)** ⇒ actual pitch attitude = commanded pitch attitude ⇒ aircraft is flying the commanded vertical speed for the 3° glideslope.
- **Pan match** ⇒ actual bank = commanded bank ⇒ on the lateral track the PID wants.

When the two are out of agreement the two pure tones beat against each other audibly. The pilot pitches / banks until the beating stops and the pans align — no numeric callouts required for the routine vertical channel.

The two axes are **independent and continuous** — the pilot is not "first center the pan, then align the pitch." Roll to converge the pans and pitch to converge the frequencies in parallel, like flying two cross-pointers on an ILS.

**Why two different waveforms by default:** identical waveforms at the same frequency can phase-cancel or fuse into one tone exactly at the matched state, which is when the pilot most needs to perceive them as distinct. Triangle (desired) + sine (current) stays distinguishable even at zero-beat.

### Sign-convention gotcha (bank)

`AudioToneGenerator.UpdateBank` follows the standard convention **positive = right wing down → pan right**. The PID's `desiredBank` output already uses this convention, so the desired tone passes it straight through. SimConnect's `PLANE_BANK_DEGREES`, however, is left-positive — `VisualGuidanceManager.cachedBank` stores the raw SimConnect value. **Always** route `cachedBank` through `VisualGuidanceManager.StandardBank(double)` before passing it to any tone API or computing a bank error. The helper just returns `-simConnectBank` but its name documents the conversion at every call site. `HandFlyManager` does its own inline negation; `AnnounceBankGuidance` and the dual-tone update path both go through `StandardBank`. Forget the conversion and the current tone pans opposite the airplane's actual bank — matching the pans steers the pilot the wrong way.

### The flight recording

Every approach is recorded to `%APPDATA%\MSFSBlindAssist\logs\visual_guidance_flight.log` (VP-2): a start line at Ctrl+V, one line per update with the state guidance read and the commands it produced, a line for everything it spoke (all speech goes through the manager's `Say`), and a stop line. `VisualGuidanceFlightRecord` formats the lines; `tools/VirtualPilot/analyze_vg_flight.py` reads them back and prints the harness's measures for a real flight, plus how closely the pilot or autopilot followed the commands and the sensor noise. The line format and the script are in [virtual-pilot.md](virtual-pilot.md), "Flight recordings". Nothing is spoken or shown: it is a diagnostic file.

### F: the status readout

F (`HotkeyAction.ReadTargetFPM`, the name kept from when it read the target descent rate) speaks `GetStatusReadout()`, composed by the pure `VisualGuidanceStatusText.Compose` (VG-22): the phase, the sideways position, then the vertical, e.g. *"On final, 6.2 miles. On the centreline. 40 feet high, target 750 down."* Distance is straight-line to the landing threshold (past a displaced one, VG-30); cross-track is signed as the lateral law takes it (+ right of the centreline facing the runway), and on final, in the flare and at touchdown F says that side ("0.1 miles left of the centreline"); joining and vectoring it says where the extended centreline is from the cockpit, by the bearing of its nearest point from the track: "Centreline 6.1 miles to your left", "to your right", "ahead" within 30° of the nose (a base leg), "behind" within 30° of the tail (`VisualGuidanceStatusText.CentrelineFromYou`; the owner, 2026-11-07, heard "6.1 miles left of final" as 6.1 miles of final left to fly); the altitude error and target descent rate are the last update's; the flare reads the wheel height (`altMSL − threshold − FlareAltitudeBiasFt`). "On the centreline" is within 0.05 nm, "On path" within 30 ft; heights round to 10 ft and rates to 50 fpm, all culture-invariant. Before the first update, *"Visual guidance starting."* Whatever visual guidance speaks on its own in a phase belongs in that phase's readout. F stays a quick-access key only: Output mode's F is Fuel Quantity.

Before final (stage 5) the vertical half is the path at the join point: *"Path there 2,470 feet, you're 300 high."* The altitude is MSL (threshold elevation + `PathHeightFt(joinAlong)`), what the altimeter and ATC use; the height is the aircraft's MSL against it now ("you're on it" within 30 ft). Vectoring, every leg, adds BA's target (*"..., target 800 down."*), falling back on `MinimumFinalNm` for the join point as its plan does; joining has none, the altitude being the pilot's or ATC's, and says no height on a heading that does not cross final. A warning that holds now replaces the height: *"you'll be 1,700 feet high at final"* while joining, *"Too high, 600 feet above the path"* or *"Too low, 400 feet below the path"* on final (`VisualGuidanceVerticalWarning`).

### The path warnings and the mile calls

Each is queued (`Say(..., immediate: false, HOLD_...)`), holds the bank callouts for its measured time (VG-21), and is repeated by F. None at or under 60 ft of wheel height, where the correction stops. `HandleVerticalCallouts` runs right after the join's own lines, before the bank callouts.

| Line | When | Holds |
| --- | --- | --- |
| *"You'll be 1,700 feet high at final"* | Joining, in the level hold, before the turn onto final, once per approach: `TooHigh` from the join point (`PictureOfTheJoin(...).JoinAlongNm`) with the height there if held level | 3.6 s (2.97 measured) |
| *"Too high, 600 feet above the path"* | Final, once per approach: `TooHigh(along, error, ...)`, more than 2° steeper can remove by the gate (`StableGateFt`) plus half a degree at the gate; inside the gate, half a degree at the aircraft's own distance. A jet 600 ft high hears it at 4 nm, not at 7 | 4.2 s (3.48) |
| *"Too low, 400 feet below the path"* | Final: more than `TooLowMarginFt` below (half a degree, at least 60 ft: 319 ft at 6 nm, 160 at 3 nm); once more 10 s later if still that low; once as the datum comes down through `StableGateFt` if still low, unless said in the last 10 s; armed again within 80 % of the margin | 3.3 s (2.72) |
| *"Prepare to flare"* | 2026-11-06, the owner's request and wording: once per approach, on final, when the flare is `FlarePrepareLeadS` (4 s) away at the smoothed descent rate (`VisualGuidanceFlare.TimeToStartS`: about 100 ft at 740 fpm, 138 at 1,000), so a pilot has warning when 100 ft to the flare takes longer than the aircraft's own callouts suggest. Not said if "Flare" is already due. F on final then ends *"Prepare to flare."* | 1.3 s (1.07) |
| *"Flare"* | Stage 6 (VG-27), retimed 2026-11-06: once per approach, when the flare is `FlareCallLeadS` (1 s) away (about 65 ft at 740 fpm, 88 at 1,000, 35 ft at 300 fpm before the 30 ft flare height) or has started (Ctrl+V pressed low), so the pilot is pulling as the tone rises; until then it came 2.5 s before touchdown, about 2 s into the flare. Both calls are not said again for a balloon or a bounce; a climb of more than 200 fpm above `FlareEndWheelFt` (100 ft, a go-around) arms them again, so a call made above the band on the way down is never repeated. Through `Say`, not `AnnouncePhaseChange`, whose one-second gap could drop it. F in the flare: *"Flare, 20 feet. On the centreline. Target 350 down."* | 0.6 s (0.49) |
| *"3 miles out, 80 feet high"* | Final only (established): the mile call adds the height against the path, *"on path"* within 30 ft; vectoring keeps *"3 miles out"* | 3.4 / 3.2 / 3.0 s for 5 / 3 / 1 (2.77, 2.64, 2.50) |

The holds are measured at their longest likely form (a four-digit height), with `docs/superpowers/measure_speech.ps1` (System.Speech at Rate 0), × 1.2, rounded up to the next 0.1 s. In the harness (run `approach_stage5_t6`) "Too high" comes on the two VERT starts built for it and on every joining run that reaches final 400 to 3,700 ft high (INT, SHALLOW, TURN, NOCROSS, DESCHIGH, one SPEED); "Too low" on the two VERT starts built for it, 2 calls each; none on VEC, VECHIGH, FAR, CLOSE or DW.

### Bank callouts

`AnnounceBankGuidance` feeds every update to `VisualGuidanceBankCalloutGate` and speaks what it returns (VG-21): at once, the update the phrase changes, interrupting the last. The phrase is the error rounded to a degree (at least 1), or "matched", entered at 0.4° and left at 1.0°. Each callout is said once, never again on a timer (the owner, 2026-11-04). Since 2026-11-06 nothing waits: the owner, after the first sim flight, "before it said it instantly. we need to make it instant again", so stage 3's 0.25 s and 1.5 s holds, its 1 s gap and its wait for a steady bank past the tone range are gone. A bank callout interrupts, which drops queued speech, so each of visual guidance's own lines ("Flare", "5 miles", a phase change) still holds the bank callouts back for its measured speaking time (the `HOLD_` constants, measured as VG-1 measures); when the hold ends, the gate speaks the error as it is then.

### Manual-query grace window

When the pilot fires a readout hotkey (the H/V/Q/S/D/B/P/A/F set) while VG is active, `MainForm` calls `visualGuidanceManager.NotifyManualQuery()`, which holds back the bank callouts (`AnnounceImmediate`) for 3 s so the readout isn't cut off mid-sentence (VG-19). Only they are held back — phase changes and distance callouts (rare one-shots) still fire. Held-back callouts lose nothing: the gate keeps tracking, so the first one after the window speaks the error as it is then. `IsManualReadoutAction` in `MainForm` lists the qualifying hotkey actions.

The centreline deviation callouts ("0.4 nm left of center", "on runway center") were removed in stage 3 of the redesign (2026-11): F speaks the sideways position in every phase instead.

Also: the PID math, phase machine, and lateral arc-capture logic are untouched by the dual-tone work; the glidepath altitude calibration above is the one deliberate vertical-guidance change (a constant offset that shifts where "on glideslope" sits, not a gain/dynamics change). Failure mode of the tone is a missing audible reference, never a wrong steering command — keep it that way.

### Settings

All exposed in the Hand Fly Options dialog (`Forms/Settings/HandFlyPanel.cs`):

| Setting | Default | Purpose |
|---|---|---|
| `VisualGuidanceToneWaveform` | Triangle | Desired-tone waveform. |
| `VisualGuidanceToneVolume` | 5 % | Desired-tone volume. |
| `VisualGuidanceCurrentToneWaveform` | Sine | Current-tone waveform. Pick a different shape from the desired tone so the two stay distinguishable at zero-beat. |
| `VisualGuidanceCurrentToneVolume` | 5 % | Current-tone volume. Lower it to make the desired tone dominant; don't gate the current tone off. |
| `VisualGuidanceHardPanTone` | off | When ON, both tones snap to full left / full right once bank exceeds ~1°, instead of proportional pan. Use on stereo speakers where partial pan is hard to distinguish from centred. Headphones generally don't need this. |

Both tones always play when visual guidance is active; there is no single-tone mode.

### Lifecycle

- **Auto-deactivation on touchdown:** when `SIM_ON_GROUND` transitions from airborne to on-ground, visual guidance deactivates automatically (the SIM_ON_GROUND handler in `MainForm.Announcers.cs`). From that moment the landing-exit planner / taxi guidance take over — keeping the dual-tone running would compete audibly with the taxi steering tone and serve no useful purpose. `SIM_ON_GROUND` arrives on the 1 Hz batch, so this can come up to a second after the wheels touch: the landing assist's rollout centerline tone, which a destination runway arms, holds its tone and its "Rollout guidance" until then, so it starts after "Visual guidance off" and never plays over the dual tones ([ROL-33]). Manual activation on the ground (preflight test, etc.) is still allowed; the auto-deactivation fires only on the airborne→on-ground edge.
- **Deferred Start:** Initialize() instantiates both `AudioToneGenerator` instances but does NOT call `Start()`. The first ProcessUpdate computes real attitude commands and then starts both tones at the correct initial frequencies via `StartTonesIfNeeded`. The phase-continuous oscillator's portamento reaches the target frequency in well under a millisecond, far inside the 150 ms `WasapiOut` buffer that first note is written into (the tone is generated at the endpoint's own mix rate, whatever that endpoint mixes at), so the first *audible* note already reflects the airplane's state — no fused-tone glitch at session start. Concretely: without the deferral, both tones would start immediately at the centre 500 Hz (no real attitude command exists yet), producing an audible fused-tone glitch before the first real command arrives — do not re-add a `Start()` call inside `Initialize`.
- **Start the current (follower) tone only if the desired tone started.** Without a reference, the follower plays a constant centre-pan tone forever — annoying and meaningless. `StartTonesIfNeeded` enforces this; don't reorder its try blocks.
- **Idempotent Initialize:** if Initialize is called while existing tones are still running (defensive against future callers that bypass `Stop`), Initialize tears them down first before creating new instances. Today's Toggle flow always Stops first, but the guard prevents future leaks.

### Aircraft-specific tone frequency / attitude range

For aircraft with attitude envelopes very different from a transport jet (aerobatic, fighter, glider), the defaults below may saturate too easily or feel coarse near zero. `VisualGuidanceProfile` exposes four optional fields read at Initialize time, all passed to `AudioToneGenerator.Configure(...)` before the deferred `Start`:

| Field | Visual-guidance default | Notes |
|---|---|---|
| `ToneMinFrequencyHz` | 200 | Frequency at full nose-down. |
| `ToneMaxFrequencyHz` | 800 | Frequency at full nose-up. Centre = (min + max) / 2 = 500 Hz. |
| `TonePitchRangeDeg` | **6** | Pitch (degrees) at which frequency saturates, applied symmetrically. Tighter than `AudioToneGenerator`'s native nose-down default (−10°, 30 Hz/°) → **50 Hz/° matching slope** (+67% precision against that half; the native nose-up half is +20° at 15 Hz/°, so the gain there is larger). Covers the -3° glideslope + 6° flare command (saturating at the edge). |
| `ToneBankRangeDeg` | **10** | Bank (degrees) at which pan saturates, 0.10 pan/°, the same as `AudioToneGenerator`'s native default. It was 5 (0.20 pan/°) from May 2026 for finer matching on final; the owner widened it to 10 on 2026-11-06 after the first sim flight, where on the pattern's downwind at 230 kt the guidance asked 4 to 6° and any bank past 5° sounded the same as the target. Past 10° (the turns) the spoken bank callouts say how far. |

A320, Fenix A320, and PMDG 777 all keep these visual-guidance defaults. Override per airframe via `VisualGuidanceProfile`; behaviour is unchanged for aircraft that don't override. HandFly mode's own `AudioToneGenerator` instance never calls `Configure` and so keeps the native **−10°/+20° pitch and ±10° bank** defaults — VG's tighter, and deliberately symmetric, pitch precision is scoped to its two tones only (its bank range is the native ±10° again since 2026-11-06).

## Aircraft Profile (per-airframe tunables)

Approach AoA, Vref reference, and the pitch/bank rate caps are aircraft-shaped — A320 numbers do not fit a 777 or a 747. They live on the aircraft definition as `VisualGuidanceProfile`, with A320 numbers as the default. Override only the fields that change for your airframe.

| Field | A320 default | PMDG 777 override | Purpose |
|-------|-------------|-------------------|---------|
| `TypicalApproachAoaDeg` | 6.0° | 4.5° | The trim (pitch minus flight path) the vertical cue starts from until its first sample: the trim learner replaces it at the first update at 40 kt or more, so it stands for an update or two at most. |
| `ReferenceVrefKnots` | 140 | 145 | Denominator in lateral airspeed-compensation scaler `sqrt(GS / Vref)`. |
| `MaxPitchRateDegPerSec` | 2.5 | 2.0 | Cap on commanded-pitch change rate; heavier aircraft = slower authority. |
| `MaxBankRateDegPerSec` | 3.0 | 3.0 | Cap on commanded-bank change rate. |
| `GlideslopeAltitudeBiasFt` | 60 *(est.)* | 80 *(measured)* | Added to the ideal `distance·tan(3°)` glidepath so VG's "on glideslope" matches the real ILS. See below. |
| `FlareAltitudeBiasFt` | 12 *(est.)* | 30 *(measured)* | Subtracted from measured altitude before the flare/touchdown thresholds so they fire at true main-gear height. See below. |
| `FlareTriggerWheelHeightFt` | 30 (FCTM) | 40 (autoland) | True main-gear height by which the flare has started: the backstop of the time-based start (about 4.3 s before touchdown, VG-27), and of "Flare". A320 follows FCTM 30 ft initiation; PMDG 777 uses 40 to match its autopilot's autoland flare; a light aircraft rounds out at about 5 to 15 ft. The fixed flare pitch (`FlareTargetPitchDeg`, 6° and 4.5°) went in stage 6. |
| `TonePitchRangeDeg` | 6 | 10 | Pitch saturation. 777 widens to 10° (30 Hz/° matching slope) for gentler beat at small errors during a stabilized approach; A320 keeps the precision-oriented 6°. |
| `MaxApproachBankDeg` | 25.0° | 25.0° | Largest bank the capture law, vectoring and the pattern ask for; with `MaxBankRateDegPerSec` and the ground speed it sizes the turns BA counts (vectoring's cut, the pattern's base and downwind). |
| `MinimumFinalNm` | 6.0 | 6.0 | Shortest final BA joins (vectoring, the pattern), and the nearest point an ATC heading may meet final and still count as joining. The owner's starting value for airliners (2026-11-04), to be revisited after testing. |
| `StableGateFt` | 1,000 | 1,000 | The height above the threshold by which the aircraft should be on the path (the airline gate). Light aircraft take about 300 (the owner, 2026-11-06). |

### Where the path starts and how steep it is

Every distance, along-track position and path height is measured from the runway's **painted landing threshold** (`VisualGuidanceThreshold.Of`, VG-30), never from the runway end. The navdata's end position (`runway_end.lonx/laty`, `Runway.StartLat`/`StartLon`) is the start of the pavement; a displaced threshold lies `Runway.ThresholdOffset` feet down the runway from it (`runway_end.offset_threshold`), and `NavigationCalculator.LandingThreshold` places it there along the runway's true course. The landing flare assist uses the same function. It covers the glide path, F's distance and "past the threshold", the mile calls, the decision and the capture law's frame, and the recording's `start` line. Sideways nothing changes: the point moves along the centreline.

- About one hard-surface runway end in eight has a displaced threshold, in both navdata builds (5,049 of 42,470 in MSFS 2024, 4,754 of 36,116 in MSFS 2020; KATL 27R 512 ft in 2024 and 498 in 2020, KJFK 22R 3,471 ft). Until 2026-11-06 visual guidance ignored it and aimed short by the offset: 27 ft low at KATL 27R, about 180 ft low at KJFK 22R, and on a 3,471 ft displaced threshold the harness's jets landed 2,000 to 2,500 ft before the painted threshold.
- The offset data agrees with the sim's own ILS. On runways with an offset and a glideslope, the glideslope antenna sits a median 1,017 ft past the painted threshold (MSFS 2024, 722 ends; 1,018 ft in MSFS 2020), the same as the 1,021 ft past the end of runways without one. So the path's `GlideslopeAltitudeBiasFt`, calibrated on plain runways, fits displaced ones unchanged.
- An offset that reaches the runway's far end is a navdata error and is ignored (KMWH 18: 3,333 ft on a 3,321 ft runway, one end in each build). About 20 ILS ends in each build have the antenna before the painted threshold (RJAA 34L, KBIS 31), where the offset or the antenna is wrong; visual guidance trusts the offset there, which can only move the aim later on the runway.
- The navdata has one elevation per runway, the same at both ends, and none for a displaced threshold, so moving the threshold changes no height.

The glide path's angle (`glideslopeAngleDeg`, VG-31) is `VisualGuidanceVertical.PathAngleDeg`: the ILS glideslope's (`ils.gs_pitch`) when it is believable (2° to 8°), else the PAPI's or VASI's (`runway_end.left_vasi_pitch`, else `right_vasi_pitch`, read as `Runway.PapiAngleDeg`), else 3°. About 1,600 runway ends in each build have a PAPI angle other than 3° and no ILS angle, most at 3.5° or 4.0°; until 2026-11-06 visual guidance flew 3° there, 320 ft under a 4° PAPI at 3 nm. The ILS wins when both are there: it is what the sim's glideslope flies, and the MSFS 2024 navdata has PAPIs that disagree with their ILS (288 of 2,863 ends; London City's PAPIs read 3.0 beside its 5.5° ILS, LOWL 26's 9.0). The PAPI's position is not in the navdata, so the path still starts at the threshold plus the glidepath bias, not at the PAPI.

### Glidepath altitude calibration

VG measures altitude as the SimConnect **reference datum** (`PLANE ALTITUDE − threshold elevation`). Two facts make a raw `distance × tan(3°)` glidepath wrong:

1. **Threshold Crossing Height.** A bare `distance·tan(3°)` path crosses the runway threshold at **0 ft**. A real ILS glideslope crosses it at the **~50 ft reference datum height** (ICAO Annex 10 — the glideslope *antenna's* threshold crossing height, 50 ft nominal, Cat II/III 50–60 ft). So a perfectly-flown coupled autoland reads as *high* on an uncorrected VG — measured at **~80 ft** on the PMDG 777 — and VG's commanded vertical speed biases into commanding excess descent toward a path one TCH too low.
2. **Datum vs. wheels.** The SimConnect datum sits above the main gear. Uncorrected, the flare cue fires ~one bias *late* (on the 777, VG's 30 ft reading coincided with the aircraft's radio-altimeter "10" callout), and on a widebody the datum never gets within the 5 ft touchdown threshold of the runway, so the touchdown phase never triggered via altitude at all.

`GlideslopeAltitudeBiasFt` fixes (1): `idealAltitudeAGL = distance·tan(3°) + GlideslopeAltitudeBiasFt`. `FlareAltitudeBiasFt` fixes (2): `wheelHeightAGL = measuredAltitude − FlareAltitudeBiasFt`, and the flare (30 ft) and touchdown (5 ft) phase thresholds test `wheelHeightAGL`. The two are applied in **different code paths** and were measured separately (on the beam vs. near the ground, different attitudes) — they are deliberately **not** one shared constant. Set either to `0` to disable that correction.

The 777 numbers are measured against a coupled ILS autoland; the A320 numbers are estimates (≈50 ft TCH + a smaller datum offset) pending in-sim calibration.

Override pattern:

```csharp
public override VisualGuidanceProfile GetVisualGuidanceProfile() => new()
{
    TypicalApproachAoaDeg    = 4.5,
    ReferenceVrefKnots       = 145.0,
    MaxPitchRateDegPerSec    = 2.0,
    MaxBankRateDegPerSec     = 3.0,
    GlideslopeAltitudeBiasFt = 80.0,
    FlareAltitudeBiasFt      = 20.0
};
```

Every concrete aircraft declares its own `GetVisualGuidanceProfile()` override — `FlyByWireA320Definition`, `FenixA320Definition`, and `PMDG777Definition` each spell out a full profile, so the math is explicitly keyed per airframe and the FBW / Fenix glidepath biases can be calibrated independently. The `BaseAircraftDefinition` default is only a fallback for aircraft with no visual-guidance tuning. The Fenix A320's approach AoA / Vref / rate caps are the values validated in earlier flight testing — left unchanged; only its glidepath biases (60 / 12) are still estimates.

## How the guidance law works

There is no integral term anywhere. Each update computes a desired bank and a desired pitch from the
aircraft's position, track and vertical speed; the desired tone plays them.

**Joining final: the decision** (stage 4 of the redesign, `VisualGuidanceJoin.Decide`). After Ctrl+V,
BA waits until the bank is under 5° (both tones on the aircraft's own attitude), then decides once,
from the position, the ground track and the ground speed. Ctrl+V off and on decides again.

- **On final**: within 0.1 nm of the extended centreline and 5° of its course, on the approach side.
- **Joining** ("Joining final"): the track, extended ahead, crosses final at up to 100° and at least the
  profile's `MinimumFinalNm` from the threshold. The desired bank is 0, holding the heading flown
  (VG-23), until the capture law, clamped to the intercept being flown, first asks 5° of bank toward
  final (`TurnCueCrossNm`): "Turn onto final", with a countdown at 30 and 15 s before it
  (`SecondsToTurn`).
- **Vectoring** ("Vectoring to final"): on the approach side, the shallowest whole-degree cut from 30°
  that lines the aircraft up at least the minimum final out, counting the turn onto the cut at the
  profile's bank rate and largest approach bank (`TurnOntoCut`, `JoinAlongNm`); close to the line on
  short final and heading in, a 30° cut straight onto final; inside the minimum final and heading in,
  further off, the shallowest cut that still lines up before the threshold without the turn onto it
  crossing the line. Never the pattern there, which would lead the pilot away from a runway they are
  about to land on.
- **The simple pattern** (also "Vectoring to final"): anything else, such as the far side of the field
  or cutting in too close. A downwind on the aircraft's side, flown away from the runway, at least as
  far out as the aircraft, two turn radii, 1 nm, and the aircraft's own base turn plus the distance from
  final where the turn onto final comes (`PatternLeastDownwindNm`, 1.92 nm at 140 kt); a base the
  minimum final plus the turn onto final out (the wider of the capture law's curve and the aircraft's own
  turn, `PatternTurnOntoFinalNm`: 7.61 nm at 140 kt); then final. The next leg becomes active when the
  capture law onto it first asks 5° of bank toward it (`LegIsDue`): "Turn base", then the countdown and
  "Turn onto final". The turn onto the downwind never passes through the heading straight at the
  centreline, except within its first 20°, so an aircraft already pointing at the field turns the short
  way (`LegHeadingErrorDeg`, `ShortWayAtTheFieldDeg`). It mainly serves "cleared visual approach" handovers from the far
  side today; full VFR circuits come with the general aviation work.

**Lateral (bank)** (`CalculateDesiredBank`). Every turn, onto final, a downwind or a base, is one
capture law onto a straight line (`VisualGuidanceJoin.DesiredLegTrackDeg`), the shape of FlyByWire's
A320 localizer capture: a desired track toward the line of the capture gain ÷ the ground speed × the
offset (`DefaultCaptureGain` 10259 deg·kt/nm, FlyByWire's k1: 73° per nm at 140 kt, 158° at 65 kt),
less 12 × the closing rate, clamped to the intercept (30° on final, the chosen cut when vectoring, the
intercept flown when joining and at least 30° after the turn, 90° on the pattern). Far from the line
it holds the intercept and asks for no turn; it turns in as the line comes near. Above about 161 kt
of ground speed the gain is capped so the law never asks a faster turn than 90% of what the profile's
largest approach bank gives (`VisualGuidanceJoin.TurnLimitedCaptureGain`, VG-28): a faster aircraft
starts its turns further out. Until "Turn base" the pattern is resized every update from the speed now
and the capped gain (`ResizePattern`: the base moves in as the aircraft slows, the downwind never
narrows). Only the decision at the press takes the full gain, planning for a pilot who slows down for the
turns, as the pilot manages the speed.

- Bank is the track error to that desired track × the speed factor √(ground speed / Vref), rate-limited,
  at most the profile's `MaxApproachBankDeg`.
- Precision track, inside 0.05 nm of the centreline: bank = P on the cross-track (120° per nm) + D on
  its rate (12) + the track error to the course, each × the speed factor, at most 10°. Unchanged by
  the redesign.
- Established on final: within 0.1 nm and 5° for 3 s on the final leg, "Established on final".

The track error uses the GPS ground track all the way to touchdown, so a crab into the wind is not
corrected away (VG-29). Until 2026-11-06 it used the heading below 100 ft, to decrab, which banked the
aircraft downwind off the centreline; the decrab is the pilot's, with the rudder in the flare.

**Vertical (pitch)** (`CalculateDesiredPitch`, the vertical path, stage 5 of the redesign): the cue
steers a flight path angle, not a vertical speed, the shape of FlyByWire's A320 glideslope track.
Desired pitch = the learned trim − the target descent angle, rate limited at the profile's
`MaxPitchRateDegPerSec`, within ±12°.

- The learned trim (`VisualGuidanceTrimLearner`) is the aircraft's pitch minus its flight path angle
  (atan(vertical speed / ground speed), smoothed over 1 s), smoothed over 6 s, so a push or a pull
  barely moves it. The first update at 40 kt or more sets it; before that the profile's
  `TypicalApproachAoaDeg` stands in. The live angle of attack is not used, nor the vertical speed's
  rate of change (VG-25): the old law built on them moved the command away from a pilot who
  followed it.
- The mode says where the target descent angle comes from, in the order an approach meets them, and the
  flight recording has it per update (`vmode`, VP-2):
  - **Follow** (deciding, joining while climbing or descending): the aircraft's own pitch, since the
    altitude is ATC's or the pilot's there.
  - **Hold** (joining only, `VisualGuidanceLevelHold`): once the vertical speed has been under 200 fpm
    for 5 s, the altitude of that update is held: atan(height error / (27 s × ground speed)) either way,
    within ±2°, so the cue asks back down from above and back up from below (VG-24). A pitch more than
    1.5° from both the cue the pilot heard (the previous update's) and the learned level attitude ends
    the hold at once, so a descent ATC clears is never fought, while a pilot who follows the cue, or who
    keeps the level pitch while the air carries the aircraft off the altitude, is still held (the stage-5
    final review: the cue alone let go after a 167 ft drift at 140 kt); the next level-off is held the
    same way. Each decision starts without a
    hold. In the harness the scripted jets level by the altimeter about 30 ft low and climb back
    slowly, so the hold takes an altitude about 28 ft under the cleared one (DESC runs,
    `holdDriftMaxFt`); for about 15 s after a level-off from 1,500 fpm the learned trim reads up to
    0.6° high (the flight path lags the pitch), so the cue sits that much above the aircraft.
  - **Plan** (vectoring and the pattern, every leg until established): BA chose the route, so it plans
    the descent along it (`VisualGuidanceVertical.PlannedHeight`): a 3° line (the runway's angle) back
    from a level-off a quarter of a degree under the path at the join point, level for the last 20 s of
    flight before it, measured along the route still to fly (`VisualGuidanceJoin.RouteToJoinNm`: to the
    active leg's line and along it, corner to corner, then along final; one straight line when
    vectoring). Never below the floor, 150 ft under the path at the aircraft's straight-line distance:
    on the floor the feed-forward is the floor's own slope along the track (`ClosingFraction`,
    `SlopeDeg`), level when flying away. Past the join point on the final leg the plan never sits above
    the path (and still never under the floor), so an aircraft still capturing past the pattern's fixed
    join point follows the path down instead of flying level through it; before the join point the path
    at the along-course distance is the height of a point beside the aircraft, so there it is not used.
    Vectored on the path (within 30 ft above it) or under it, at the distance along the final course, at
    the decision (`vectoredUnderPathFt`): the plan is never lower than that far under the path along the
    course, with the path's own slope along the track as the feed-forward, so the aircraft keeps its
    place instead of going down to the floor first (the owner, 2026-11-06, the final review's finding 3).
    It only raises the plan, and it is the offset at the decision, not now: kept where it was each update,
    a light aircraft the plan had taken 125 ft under while cutting in close stayed there and joined 70 ft
    low. Vectored from above, the plan is unchanged and the aircraft still joins from below. Then the same correction as Path, from level up to the glide path angle +
    2° (VG-24, VG-26). A jet's plan is the floor all the way in (a 20 s level lead at 140 kt needs 248 ft
    under the path, the floor allows 150), so jets vectored from above join about 80 to 220 ft under the
    path, still descending, and the path law brings them onto it from below (a gentler descent until the
    path comes down to them); a light aircraft at 65 kt arrives level about 30 ft under it. A jet on the path but 0.2 nm off the centreline is vectoring too, and keeps to the
    path (below).
  - **Path** (established on final): the runway's glide path angle +
    atan(height error / (27 s × ground speed)), from level up to the glide path angle + 2° (VG-24,
    VG-26). The height error is against the path at the distance along the final course, not the
    straight line. Below the path the cue asks for level flight until the path comes down to the
    aircraft, then follows it. Under 60 ft of wheel height the correction stops and the cue holds the
    glide path angle. A steep runway uses its own angle throughout (London City 5.5°).
  - **Flare** (stage 6, VG-27): starts when the time to touchdown, the wheel height over the smoothed
    descent rate (the 1 s flight path angle times the ground speed), first reaches 4.3 s under 100 ft of
    wheel height, or at the profile's flare height (30 ft on the A320), whichever comes first. The target
    descent rate starts at the aircraft's own and eases to 150 fpm at touchdown
    (`LandingGuidanceLaws.FlareTargetVsFpm` with τ = h0 × 60 / (s0 − 150), set at the start; at 150 fpm or
    less, 150 throughout). The cue is the level attitude learned before the flare, held, minus the target's
    descent angle, plus how much steeper the aircraft still descends than the target, only that way (`FlareCorrectionGain`,
    1, as FlyByWire's flare corrects for the sink error), at the profile's pitch rate. So the tone rises as
    the wheels come down, a little faster at first while the aircraft's descent catches up, and the target
    never asks for a climb; the tone itself can briefly sit above the level attitude while the aircraft still sinks
    faster than the target. Floating or ballooning there is no correction: the cue is the target's own attitude,
    which is how a balloon or a bounce is asked gently back down (with the correction both ways, a balloon at
    30 ft climbing 200 fpm was asked 4.7° below the level attitude, a push near the ground: the stage 6 final
    review). A balloon or a bounce stays in the flare; above 100 ft of wheel height (a go-around) it is final
    again, the path's cue leaving from the flare's pitch. Ctrl+V pressed in the flare learns the level
    attitude from its first update, then holds it.

The vertical-speed law it replaced (a target rate of the natural descent plus 2 fpm per foot, locked to
the natural rate inside 1 nm, clamped at `min(−1500, 1.3 × natural)`, flown by a PD controller on the
vertical speed around the live angle of attack) went in stage 5, with VG-16 and VG-17.

Every limit, smoothing filter and derivative in both laws runs on the measured frame time (VG-20;
see [Bank-rate / pitch-rate caps](#bank-rate--pitch-rate-caps)).

## Guidance constants

Constants and instance fields in `VisualGuidanceManager.cs` and the pure classes beside it (defaults shown — instance fields tagged `[profile]` are overridable per aircraft via `VisualGuidanceProfile`).

### Lateral (Bank) Control

| Name | Value | Description |
|------|-------|-------------|
| `VisualGuidanceJoin.DefaultCaptureGain` | 10259 deg·kt/nm | The capture law's gain (FlyByWire's k1), divided by the ground speed (at least 40 kt); picked in the approach harness (`approach --capture-gain K`) |
| `ARC_MODE_EXIT_NM` | 0.05 | Inside this the precision track flies; outside it the capture law |
| `maxApproachBankDeg` `[profile]` | 25.0° | Largest bank the capture law, vectoring and the pattern ask for (`MaxApproachBankDeg`) |
| `minimumFinalNm` `[profile]` | 6.0 | Shortest final BA joins, or accepts ATC's heading for (`MinimumFinalNm`) |
| `LATERAL_GAIN_TRACKING` | 120.0 | Cross-track error (NM) → bank during precision tracking |
| `LATERAL_RATE_DAMPING` | 12.0 | Cross-track rate damping (derivative) |
| `LATERAL_HEADING_GAIN` | 1.0 | Track-error alignment gain |
| `airspeedReferenceKnots` `[profile]` | 140 | Reference Vref for `sqrt(GS / Vref)` scaling |
| `maxBankRateDegPerSec` `[profile]` | 3.0 | Cap on commanded bank change rate (deg/sec), applied over the measured frame time |
| `CROSS_TRACK_RATE_WINDOW_S` | 4.0 s | Time window the cross-track rate (the D term) is measured over, oldest to newest sample |
| `CROSS_TRACK_RATE_SMOOTHING_PER_SECOND` | 0.85 | Exponential smoothing of that rate, per second (α = 0.85^dt) |

The fixed 60° and 45° intercepts, the Extending phase and the 30°/nm arc capture went in stage 4 of the
redesign (2026-11-04); the capture law above replaced them.

### Vertical (Pitch) Control

The vertical path (stage 5) steers a flight path angle: desired pitch = learned trim − target descent angle (in the flare, stage 6, plus the correction for the sink error). The tuning numbers are angles, seconds and sink rates, so the aircraft's own speed scales them and they are the same for every aircraft (the owner, 2026-11-06); they live in `VisualGuidanceVerticalTuning`, and the harness sweeps them with `approach --vertical name=value,...`.

| Name | Value | Description |
|------|-------|-------------|
| `glideslopeAngleDeg` | per-runway (3.0° default) | `VisualGuidanceVertical.PathAngleDeg`: the ILS glideslope's (`ils.gs_pitch` via `Runway.GlideslopeAngleDeg`), else the PAPI's (`Runway.PapiAngleDeg`), else 3°; outside 2° to 8° an angle is not believed (see "Where the path starts and how steep it is"). London City = 5.5°, Aspen = 6.59°, Innsbruck = 3.8°. The path's height, the target's feed-forward angle and its 2° limit all use it. |
| `thresholdElevationMSL` | per-runway-end | Read from `runway_end.altitude` via `Runway.ThresholdElevation`; falls back to `airport.Altitude` when missing. It is the runway's own elevation, which can differ from the field's (KASE: 7,680 ft against 7,750). |
| `CorrectionTimeConstantS` | 27 s | The height error is removed over about this long: the correction is atan(error / (this × ground speed)). FlyByWire's glideslope uses 17 to 27 s; the spec said 20, and the harness picked 27, where a light aircraft's cue moves less with the pilot ([virtual-pilot.md](virtual-pilot.md), "Stage 5") |
| `PathLimitDeg` | 2.0° | The target stays at most this steeper than the glide path angle; on the shallow side it stops at level (VG-26) |
| `CorrectionFloorWheelFt` | 60 ft | Under this wheel height the correction stops and the target is the glide path angle (FlyByWire's) |
| `TrimTimeConstantS` | 6 s | The trim learner's smoothing of pitch minus flight path |
| `PathAngleTimeConstantS` | 1 s | The flight path angle's smoothing |
| `VisualGuidanceVertical.MinGroundSpeedKt` | 40 kt | Below it the angles use 40 kt, and the trim learner skips the sample (a slew, a pause) |
| `VisualGuidanceTrimLearner.MinValidDeg`, `MaxValidDeg` | −5°, 20° | A trim sample outside this band is a sim glitch and skipped |
| `LevelVsFpm`, `LevelHoldS` | 200 fpm, 5 s | While joining, under this vertical speed for this long counts as level, and the hold takes that update's altitude |
| `HoldBreakPitchDeg` | 1.5° | While holding, a pitch this far from the cue the pilot heard ends the hold: the pilot means it |
| `VisualGuidanceVertical.ArrivalBelowDeg` | 0.25° | The plan levels off this far under the path at the join point (159 ft at a jet's 6 nm, 27 ft at a light aircraft's 1 nm) |
| `VisualGuidanceVertical.FloorBelowPathFt` | 150 ft | Before established the plan is never lower than this under the path at the aircraft's straight-line distance (the owner, 2026-11-02); a jet's plan is this floor all the way in |
| `VisualGuidanceVertical.LevelOffLeadS` | 20 s | The plan is level for this long of flight before the join point |
| `StableGateFt` `[profile]` | 1,000 ft | Where the aircraft should be on the path: "Too high" judges whether 2° steeper can get there by it, "Too low" is said once more as the datum comes down through it. Light aircraft 300 ft |
| `VisualGuidanceVertical.TooLowDeg` | 0.5° | "Too low" below the path by this angle, about where the PAPI shows red; "Too high" adds the same at the gate |
| `VisualGuidanceVertical.WarningFloorFt` | 60 ft | The warnings never come within this of the path, twice the 30 ft "On path" (`OnPathFt`) |
| `VisualGuidanceVertical.TooLowRepeatS`, `TooLowRearmFraction` | 10 s, 0.8 | "Too low" is said once more after 10 s if still that low, and armed again back within 80 % of its margin |
| `typicalApproachAoaDeg` `[profile]` | 6.0° | The trim the learner starts from until its first sample. PMDG 777 overrides to 4.5°. |
| `maxPitchRateDegPerSec` `[profile]` | 2.5 | Cap on commanded pitch change rate (deg/sec), applied over the measured frame time |
| `glideslopeAltitudeBiasFt` `[profile]` | 60 *(est.)* | Added to ideal glidepath so VG matches the real ILS (TCH + datum). PMDG 777 = 80 (measured). |
| `flareAltitudeBiasFt` `[profile]` | 12 *(est.)* | Subtracted from measured altitude before flare/touchdown checks → true gear height. PMDG 777 = 30 (measured). |
| `flareTriggerWheelHeightFt` `[profile]` | 30 | True gear height by which the flare has started (the backstop of the 4.3 s start). PMDG 777 = 40 (matches autopilot autoland). |
| `TOUCHDOWN_ALTITUDE_FT` | 5.0 | Touchdown phase entry — true main-gear height (`wheelHeightAGL`) |
| `FlareStartS` | 4.3 s | The flare starts this long before touchdown, under `FlareEndWheelFt` (FlyByWire's flare law) |
| `FlarePrepareLeadS` | 4.0 s | "Prepare to flare" is due this long before the flare starts (about 100 ft at 740 fpm) |
| `FlareCallLeadS` | 1.0 s | "Flare" is due this long before the flare starts (about 65 ft at 740 fpm); until 2026-11-06 `FlareCallS`, 2.5 s before touchdown |
| `TouchdownSinkFpm` | 150 fpm | The flare's target sink at touchdown (FlyByWire's) |
| `FlareEndWheelFt` | 100 ft | The flare starts by time only under this wheel height and ends only above it (the landing assist's go-around height) |
| `FlareCorrectionGain` | 1 | For each degree the aircraft descends steeper than the flare's target, this many degrees more nose-up; nothing when floating or ballooning. Without it the harness's jets touched down at 518 fpm (the old fixed flare 382); at 1, 246; 1.5 and 2 floated and drifted ([virtual-pilot.md](virtual-pilot.md), "Stage 6"). The owner chose 1 (2026-11-06) |

The fixed flare pitch (`flareTargetPitchDeg`, 6° on the A320, 4.5° on the 777) and its 10°/s pitch-rate
exception went in stage 6: the flare's cue runs at the profile's pitch rate (VG-20, VG-27).

### Vertical sign

Above the reference (the path on final, BA's plan while vectoring and on the pattern, the held altitude while joining) means a larger target descent angle and a lower desired pitch (VG-24). Below the path or the plan the cue asks for level, never a climb; only the joining hold asks back up, within 2° (VG-26). A height error is + above the path, a descent angle + down, the glide path angle + (3.0): with autothrust holding the speed, less pitch is more descent. The old pitch PID once had this backwards (a leading minus, masked by autopilot tests that kept the error near zero) and would have steered a pilot away from the glideslope; the tests that pin the sign today are named in VG-24. If one fails, work out by hand which way the aircraft has to go; never flip the test.

## Tuning Guide

### Identifying problems

| Symptom | Likely cause | Solution |
|---------|--------------|----------|
| Slow to capture the centreline | Capture gain too low | Sweep `approach --capture-gain K` in the VirtualPilot harness before changing `DefaultCaptureGain` (VG-18) |
| Overshoots centerline | Tracking gain too high or damping too low | Decrease `LATERAL_GAIN_TRACKING` or increase `LATERAL_RATE_DAMPING` |
| Oscillates across centerline at short final | Bank rate cap too loose for the airframe | Lower `maxBankRateDegPerSec` in the airframe's `VisualGuidanceProfile` |
| Sits off the path and converges slowly | Correction too slow | Sweep `approach --vertical CorrectionTimeConstantS=...` in the harness; watch `selfFeedback` and `pathOvershootFt` |
| The pitch cue moves when the pilot pitches | The trim learner too quick | Sweep `TrimTimeConstantS`; the STEP runs' `selfFeedback` measures it |
| The joining hold lets go in a bump, or engages before the level-off settles | Hold break or level test | Sweep `HoldBreakPitchDeg`, `LevelVsFpm` and `LevelHoldS` on the DESC and DESCHIGH runs; watch `holdBreaks`, `holdEngageS` and `holdDriftMaxFt` (check J) |
| Joins final too high or too low while vectoring | The plan | `FloorBelowPathFt` (the owner's 150 ft), `ArrivalBelowDeg` and `LevelOffLeadS` in `VisualGuidanceVertical`; watch `joinPathErrFt` on VEC, VECHIGH and FAR (check K) |
| "Too high" or "Too low" too early or too late | The margins | `StableGateFt` in the profile, `TooLowDeg` and `WarningFloorFt`; the VERT runs' `firstTooHighAlongNm` and `firstTooLowAlongNm` (check I) |
| Aircraft-specific: 777/747 pitch lag feels jerky | Pitch rate cap too tight or too loose | Tune `maxPitchRateDegPerSec` in the airframe profile |

### Tuning workflow

1. Change one constant at a time. Build, fly a fixed approach, listen for the change.
2. Lateral tuning is felt as tone pan + spoken bank-error callouts. Vertical is felt as desired-tone frequency drift away from current-tone frequency.
3. Before touching `VisualGuidanceManager.cs` constants, check whether the right knob is on `VisualGuidanceProfile` (per-aircraft) instead. Anything that varies by airframe belongs there.

## Ground Track Monitoring

### What is Ground Track?

**Heading (what we used before):**
- Direction the aircraft **nose is pointing**
- Example: Aircraft heading 360° (pointing north)

**Ground Track (what we use now):**
- Direction the aircraft is **actually moving** over the ground
- Example: Heading 360° with right crosswind → Ground track 355° (moving northwest)
- **SimConnect Variable**: `GPS GROUND MAGNETIC TRACK`

### Why Ground Track Matters

**Without ground track (heading-only):**
- Aircraft can be pointed at runway but drifting off due to wind
- PID sees "heading aligned" and doesn't correct drift
- Can settle at equilibrium with steady-state error (off centerline)

**With ground track:**
- PID detects actual movement direction vs. runway heading
- Immediately sees drift even if nose is pointed correctly

**Example:**
```
Runway heading: 360°
Aircraft heading: 365° (crabbing into wind)
Ground track: 360° (actually tracking straight)

Old system: "Heading 5° off - turn left!" (Wrong!)
New system: "Ground track perfect - maintain!" (Correct!)
```

### Ground track in the code

Inside `CalculateDesiredBank` (see `VisualGuidanceManager.cs`, look for `cachedGroundTrack`). Pattern:

```csharp
double actualTrackAngle = cachedGroundTrack ?? heading;
```

**Behavior:**
- Primary: Uses GPS ground track (drift-aware)
- Fallback: Uses magnetic heading if ground track unavailable

## Bank-rate / pitch-rate caps

The lateral controller is rate-limited by `maxBankRateDegPerSec` (default 3°/s for A320; aircraft profile overrideable) and the vertical controller by `maxPitchRateDegPerSec` (default 2.5°/s; profile overrideable for heavier airframes). These caps prevent the desired tone from jumping suddenly when the PID computes a large step correction — both the audio and the airplane's response stay smooth.

Guidance updates on every sim frame (the visual guidance data arrives per `SIM_FRAME`), so every limit and filter runs on the measured frame time through `VisualGuidanceTiming` (VG-20): a cap moves the command at most rate × dt, a smoothing factor is per second, and a derivative is a rate over a time window. Until 2026-11-04 they were per-update constants written for one update a second, so at 30 frames a second the 3°/s cap allowed about 90°/s. Three edges:

- The first update after activation is not rate-limited: the tone starts where the guidance says, not ramping up from level.
- The frame time counts at most one second, so a pause or a stalled frame stream moves the command no more than one second's worth; two updates at the same instant move nothing.
- A clock that steps back counts as no time, never a reverse step.

The VirtualPilot approach harness measured the change ([virtual-pilot.md](virtual-pilot.md), "Stage 2: real-time limits"): final tracking held, and with assumed sensor noise the pitch command's rate of change fell from 27.6°/s to 0.11°/s at 30 frames a second. One design number did not survive real time: the flare's 1.5°/s pitch limit made the fixed flare cue arrive after touchdown, so from stage 2 to stage 5 the flare ran at 10°/s. Stage 6's flare starts from where the aircraft is and runs at the profile's pitch rate, with no exception.

There is no integral-windup machinery to worry about today: the vertical path is a path angle with a proportional height correction around a learned trim, and the lateral path uses rate damping rather than an explicit integrator at the precision-track stage. If a future contributor adds an integral term, document the windup strategy alongside it.

## Known gaps

What visual guidance does not do yet, known and left open on purpose, so nobody takes it for a bug or
fills it by guesswork.

- **No cue for the rudder in a crosswind (2026-11-06).** The bank tone holds the path on the centreline
  to touchdown, crab and all (VG-29); straightening the nose with the rudder in the flare is the pilot's
  alone, and nothing tells them how far the nose is off the runway's direction. A bank-based decrab was
  removed after it drove the first sim flight off the runway's side, and the owner chose not to bring
  it back at 30 ft either: it would still ask for bank, the wrong control, in the flare's busiest
  seconds, and the same tone would mean the rudder only there. The idea for later, if the owner asks:
  a separate cue that plainly means the rudder, such as a spoken "Nose 3 right" in the flare. Only the
  sim can test it: the approach harness's model aircraft has no rudder and cannot slip sideways.

## Related Documentation

- [Architecture](architecture.md) - Overall system design
- [Adding Features](adding-features.md) - Development workflows

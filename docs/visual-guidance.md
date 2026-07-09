# Visual Guidance PID Tuning

Reference guide for understanding and tuning the visual guidance PID controller for runway approaches.

## Overview

The visual guidance system uses a **PID controller** to generate pitch and bank commands that guide the aircraft onto the runway centerline and glideslope. It accounts for wind drift using GPS ground track monitoring. The PID outputs are rendered to the pilot as a **dual-tone audio cue** the pilot matches by ear (see [Dual-Tone Audio Cue](#dual-tone-audio-cue)).

**Key files:**
- `MSFSBlindAssist/Services/VisualGuidanceManager.cs` — PID, phase machine, tone modulation, `StandardBank` helper, on-ground auto-deactivation hook
- `MSFSBlindAssist/Services/AudioToneGenerator.cs` — default 200–800 Hz pitch→Hz / ±10° pitch / ±10° bank→pan; per-instance `Configure(minHz, maxHz, pitchRangeDeg, bankRangeDeg)` for aircraft-specific ranges
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

The default ranges are deliberately **tighter than `AudioToneGenerator`'s own defaults** (±10° / ±10°). The narrowing gives **50 Hz of beat per ° of pitch error** (vs 30 Hz/° at the native ±10° default — +67% precision) and **0.20 pan delta per ° of bank error** (vs 0.10 pan/° at ±10° — +100% precision), making sub-degree errors clearly audible. At 0.1° pitch error a pilot hears a 5 Hz beat (slow wobble); at 0.5° they hear a 25 Hz beat (clear fluttering). The trade-off is earlier saturation: the PID can command up to 25° bank during intercept, which saturates the desired tone at full pan. That's fine because the spoken bank-guidance announcements ("3 left", "matched", etc.) already cover the large-error regime — the tones own the precise near-matched-state cue, spoken cues own the gross corrections.

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
| F | Read target FPM (visual guidance) |

Registration is reference-counted inside `HotkeyManager.AcquireQuickAccessHotkeys` / `ReleaseQuickAccessHotkeys`: the first mode (HandFly or VG) to activate registers all 9 keys; the second mode bumps the ref count without re-registering; whichever mode deactivates last releases the keys. Per-key partial-failure tracking means a key that was unavailable on first try (some other app holding it) will be retried on the next acquire. WM_HOTKEY dispatch is unified — same `case` for each key regardless of which mode is active. Pressing F in HandFly-only mode triggers `ReadTargetFPM`, whose handler self-gates on `visualGuidanceManager.IsActive` and announces *"Visual guidance not active"* if appropriate.

Both tones use the *same* pitch→Hz and bank→pan mappings, so the rules are:

- **Frequency match (zero-beat)** ⇒ actual pitch attitude = commanded pitch attitude ⇒ aircraft is flying the commanded vertical speed for the 3° glideslope.
- **Pan match** ⇒ actual bank = commanded bank ⇒ on the lateral track the PID wants.

When the two are out of agreement the two pure tones beat against each other audibly. The pilot pitches / banks until the beating stops and the pans align — no numeric callouts required for the routine vertical channel.

The two axes are **independent and continuous** — the pilot is not "first center the pan, then align the pitch." Roll to converge the pans and pitch to converge the frequencies in parallel, like flying two cross-pointers on an ILS.

**Why two different waveforms by default:** identical waveforms at the same frequency can phase-cancel or fuse into one tone exactly at the matched state, which is when the pilot most needs to perceive them as distinct. Triangle (desired) + sine (current) stays distinguishable even at zero-beat.

### Sign-convention gotcha (bank)

`AudioToneGenerator.UpdateBank` follows the standard convention **positive = right wing down → pan right**. The PID's `desiredBank` output already uses this convention, so the desired tone passes it straight through. SimConnect's `PLANE_BANK_DEGREES`, however, is left-positive — `VisualGuidanceManager.cachedBank` stores the raw SimConnect value. **Always** route `cachedBank` through `VisualGuidanceManager.StandardBank(double)` before passing it to any tone API or computing a bank error. The helper just returns `-simConnectBank` but its name documents the conversion at every call site. `HandFlyManager` does its own inline negation; `AnnounceBankGuidance` and the dual-tone update path both go through `StandardBank`. Forget the conversion and the current tone pans opposite the airplane's actual bank — matching the pans steers the pilot the wrong way.

### Manual-query grace window

When the pilot fires a readout hotkey (the H/V/Q/S/D/B/P/A/F set) while VG is active, `MainForm` calls `visualGuidanceManager.NotifyManualQuery()`, which suppresses the per-second bank-guidance and centerline-deviation callouts (both `AnnounceImmediate`) for 3 s so the readout isn't cut off mid-sentence. Only those two chatty callouts are suppressed — phase changes and distance callouts (rare one-shots) still fire. Skipped callouts lose nothing: both always reflect current state, so the next one carries fresh data. `IsManualReadoutAction` in `MainForm` lists the qualifying hotkey actions.

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

- **Auto-deactivation on touchdown:** when `SIM_ON_GROUND` transitions from airborne to on-ground, visual guidance deactivates automatically (`MainForm.cs` SIM_ON_GROUND handler). From that moment the landing-exit planner / taxi guidance take over — keeping the dual-tone running would compete audibly with the taxi steering tone and serve no useful purpose. Manual activation on the ground (preflight test, etc.) is still allowed; the auto-deactivation fires only on the airborne→on-ground edge.
- **Deferred Start:** Initialize() instantiates both `AudioToneGenerator` instances but does NOT call `Start()`. The first ProcessUpdate computes real attitude commands and then starts both tones at the correct initial frequencies via `StartTonesIfNeeded`. The phase-continuous oscillator's portamento (~0.23 ms at 44.1 kHz) is well under WaveOut's 150 ms buffer, so the first *audible* note already reflects the airplane's state — no fused-tone glitch at session start. Concretely: without the deferral, both tones would start immediately at the centre 500 Hz (no real attitude command exists yet), producing an audible fused-tone glitch before the first real command arrives — do not re-add a `Start()` call inside `Initialize`.
- **Start the current (follower) tone only if the desired tone started.** Without a reference, the follower plays a constant centre-pan tone forever — annoying and meaningless. `StartTonesIfNeeded` enforces this; don't reorder its try blocks.
- **Idempotent Initialize:** if Initialize is called while existing tones are still running (defensive against future callers that bypass `Stop`), Initialize tears them down first before creating new instances. Today's Toggle flow always Stops first, but the guard prevents future leaks.

### Aircraft-specific tone frequency / attitude range

For aircraft with attitude envelopes very different from a transport jet (aerobatic, fighter, glider), the defaults below may saturate too easily or feel coarse near zero. `VisualGuidanceProfile` exposes four optional fields read at Initialize time, all passed to `AudioToneGenerator.Configure(...)` before the deferred `Start`:

| Field | Visual-guidance default | Notes |
|---|---|---|
| `ToneMinFrequencyHz` | 200 | Frequency at full nose-down. |
| `ToneMaxFrequencyHz` | 800 | Frequency at full nose-up. Centre = (min + max) / 2 = 500 Hz. |
| `TonePitchRangeDeg` | **6** | Pitch (degrees) at which frequency saturates. Tighter than `AudioToneGenerator`'s native 10° default → **50 Hz/° matching slope** (+67% precision). Covers the -3° glideslope + 6° flare command (saturating at the edge). |
| `ToneBankRangeDeg` | **5** | Bank (degrees) at which pan saturates. Tighter than the 10° native default → 0.20 pan/° (+100% precision). Saturates during ≥5° commanded bank (intercept) — spoken bank-guidance covers that regime. |

A320, Fenix A320, and PMDG 777 all keep these visual-guidance defaults. Override per airframe via `VisualGuidanceProfile`; behaviour is unchanged for aircraft that don't override. HandFly mode's own `AudioToneGenerator` instance never calls `Configure` and so keeps the native 10°/10° defaults — VG's tighter precision is scoped to its two tones only.

## Aircraft Profile (per-airframe tunables)

Approach AoA, Vref reference, and the pitch/bank rate caps are aircraft-shaped — A320 numbers do not fit a 777 or a 747. They live on the aircraft definition as `VisualGuidanceProfile`, with A320 numbers as the default. Override only the fields that change for your airframe.

| Field | A320 default | PMDG 777 override | Purpose |
|-------|-------------|-------------------|---------|
| `TypicalApproachAoaDeg` | 6.0° | 4.5° | **Fallback only.** VG uses the live SimConnect `INCIDENCE ALPHA` reading (smoothed, sanity-banded `[-5°, 20°]`) for the nominal pitch baseline. This profile value is only used when measured AoA is unavailable or out of band. With live AoA, weight/flap/speed-driven AoA variation is handled automatically — no per-airframe estimate needed. |
| `ReferenceVrefKnots` | 140 | 145 | Denominator in lateral airspeed-compensation scaler `sqrt(GS / Vref)`. |
| `MaxPitchRateDegPerSec` | 2.5 | 2.0 | Cap on commanded-pitch change rate; heavier aircraft = slower authority. |
| `MaxBankRateDegPerSec` | 3.0 | 3.0 | Cap on commanded-bank change rate. |
| `GlideslopeAltitudeBiasFt` | 60 *(est.)* | 80 *(measured)* | Added to the ideal `distance·tan(3°)` glidepath so VG's "on glideslope" matches the real ILS. See below. |
| `FlareAltitudeBiasFt` | 12 *(est.)* | 30 *(measured)* | Subtracted from measured altitude before the flare/touchdown thresholds so they fire at true main-gear height. See below. |
| `FlareTriggerWheelHeightFt` | 30 (FCTM) | 40 (autoland) | True main-gear height at which the flare cue announces. A320 follows FCTM 30 ft initiation; PMDG 777 uses 40 to match its autopilot's autoland flare. |
| `FlareTargetPitchDeg` | 6.0° | 4.5° | Pitch commanded during the Flare phase. A320 FCTM ~+5–6°; Boeing 777 FCTM "2–3° pitch increase from approach attitude" → ~+4–4.5° given the 777's +1.5° approach pitch. |
| `TonePitchRangeDeg` | 6 | 10 | Pitch saturation. 777 widens to 10° (30 Hz/° matching slope) for gentler beat at small errors during a stabilized approach; A320 keeps the precision-oriented 6°. |

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

## How PID Control Works

The controller combines three terms to calculate desired bank (lateral) and pitch (vertical) commands:

### Proportional (P)
- **What it does**: Provides correction proportional to the error
- **Example**: 1 NM off centerline → commands 5.0° bank
- **Effect**: Stronger values = more aggressive intercept, but can overshoot

### Integral (I)
- **What it does**: Accumulates error over time to eliminate steady-state drift
- **Example**: Aircraft drifting 0.1 NM left in crosswind → integral builds correction until drift stops
- **Effect**: Stronger values = faster drift elimination, but can cause oscillation

### Derivative (D)
- **What it does**: Resists rapid changes to prevent overshoot
- **Example**: Approaching centerline quickly → derivative dampens correction to prevent overshoot
- **Effect**: Stronger values = smoother but slower corrections

## Current PID Parameters

Constants and instance fields in `VisualGuidanceManager.cs` (defaults shown — instance fields tagged `[profile]` are overridable per aircraft via `VisualGuidanceProfile`).

### Lateral (Bank) Control

| Name | Value | Description |
|------|-------|-------------|
| `LATERAL_GAIN_INTERCEPT` | 0.5 | Heading-error → bank during intercept phase |
| `LATERAL_GAIN_TRACKING` | 120.0 | Cross-track error (NM) → bank during precision tracking |
| `LATERAL_RATE_DAMPING` | 12.0 | Cross-track rate damping (derivative) |
| `LATERAL_HEADING_GAIN` | 1.0 | Track-error alignment gain |
| `airspeedReferenceKnots` `[profile]` | 140 | Reference Vref for `sqrt(GS / Vref)` scaling |
| `maxBankRateDegPerSec` `[profile]` | 3.0 | Cap on commanded bank change rate (deg/sec) |
| `ARC_MODE_ENTRY_NM` | 1.5 | Distance at which arc-capture mode begins |
| `ARC_INTERCEPT_GAIN` | 30.0 | Intercept-angle gain inside arc capture |
| `ARC_RATE_DAMPING` | 12.0 | Damping inside arc capture |
| `ARC_BANK_LIMIT` | 15.0° | Bank limit inside arc capture |

### Vertical (Pitch) Control

The vertical path is **FPM-based** (target FPM derived from 3° glideslope + altitude-error correction, PD controller on FPM error). It is NOT a classic glideslope-deviation integral controller — the previous documentation in this section was stale.

| Name | Value | Description |
|------|-------|-------------|
| `glideslopeAngleDeg` | per-runway (3.0° default) | Read from `ils.gs_pitch` via `Runway.GlideslopeAngleDeg`; falls back to 3° when navdata lacks the angle. London City = 5.5°, Aspen = 6.59°, Innsbruck = 3.8°. Feeds the ideal glidepath, the natural-descent-rate FPM target, the phase-capture deviation, and the nominal-pitch baseline. |
| `thresholdElevationMSL` | per-runway-end | Read from `runway_end.altitude` via `Runway.ThresholdElevation`; falls back to `airport.Altitude` when missing. Matters at airports with sloped runways. |
| `GLIDESLOPE_GAIN` | 2.0 | Proportional: altitude error → FPM correction (FPM per ft) |
| `GLIDESLOPE_LOCK_DISTANCE_NM` | 1.0 | Inside this distance, lock to steady 3° descent — no altitude correction |
| `MAX_DESCENT_RATE_FPM` | -1500 | Safety clamp on commanded descent. Applied **dynamically** as `min(-1500, natural × 1.3)` so a legitimate steep-approach natural rate (Aspen 6.59° × 130 kt = ~-1521 fpm) is never clipped — the clamp always allows the natural rate plus correction headroom. |
| `typicalApproachAoaDeg` `[profile]` | 6.0° | Nominal pitch = -3° + AoA. PMDG 777 overrides to 4.5°. |
| `maxPitchRateDegPerSec` `[profile]` | 2.5 | Cap on commanded pitch change rate (deg/sec) |
| `glideslopeAltitudeBiasFt` `[profile]` | 60 *(est.)* | Added to ideal glidepath so VG matches the real ILS (TCH + datum). PMDG 777 = 80 (measured). |
| `flareAltitudeBiasFt` `[profile]` | 12 *(est.)* | Subtracted from measured altitude before flare/touchdown checks → true gear height. PMDG 777 = 30 (measured). |
| `flareTriggerWheelHeightFt` `[profile]` | 30 | True gear height at which the flare cue announces. PMDG 777 = 40 (matches autopilot autoland). |
| `TOUCHDOWN_ALTITUDE_FT` | 5.0 | Touchdown phase entry — true main-gear height (`wheelHeightAGL`) |
| `FPM_P_GAIN` | 0.005 | Pitch correction per FPM error (deg per FPM) |
| `FPM_D_GAIN` | 0.002 | Pitch damping per FPM/sec error rate |
| `FPM_SMOOTHING_FACTOR` | 0.7 | EMA on raw VS reading |
| `flareTargetPitchDeg` `[profile]` | 6.0° (A320) | Per-aircraft target pitch commanded in flare. PMDG 777 = 4.5°. |
| `MAX_FLARE_PITCH_RATE` | 1.5 | Cap on pitch rate during flare (deg/sec) |

### Pitch PID sign convention (bug history)

The pitch PID has **positive coefficients on `fpmError` / `fpmErrorRate`** — NOT the leading `-` that was historically there. Verified sign: with autothrust holding airspeed, MORE pitch ⇒ LESS descent. So negative `fpmError` (descending less than target ⇒ need more descent) calls for a NEGATIVE pitch correction ⇒ the correction has the SAME sign as `fpmError`. The old `-fpmError * gain` produced wrong-direction guidance that was masked by autopilot tests (tight tracking ⇒ `fpmError ≈ 0` ⇒ the wrong-sign correction was negligible) and would have steered a manually-flying pilot away from the glideslope. Do NOT reintroduce the leading minus.

## Tuning Guide

### Identifying problems

| Symptom | Likely cause | Solution |
|---------|--------------|----------|
| Slow to intercept centerline at long range | Intercept gain too low | Increase `LATERAL_GAIN_INTERCEPT`, or widen `ARC_MODE_ENTRY_NM` |
| Overshoots centerline | Tracking gain too high or damping too low | Decrease `LATERAL_GAIN_TRACKING` or increase `LATERAL_RATE_DAMPING` |
| Oscillates across centerline at short final | Bank rate cap too loose for the airframe | Lower `maxBankRateDegPerSec` in the airframe's `VisualGuidanceProfile` |
| Sits 50–100 ft above glideslope and never converges | Vertical gain too low | Increase `GLIDESLOPE_GAIN` |
| Tone frequency twitches around centre on still air | FPM smoothing too weak | Raise `FPM_SMOOTHING_FACTOR` toward 0.85 |
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
- Integral term eliminates drift automatically

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

There is no integral-windup machinery to worry about today: the vertical path is FPM-based PD, and the lateral path uses rate damping rather than an explicit integrator at the precision-track stage. If a future contributor adds an integral term, document the windup strategy alongside it.

## Related Documentation

- [Architecture](architecture.md) - Overall system design
- [Adding Features](adding-features.md) - Development workflows

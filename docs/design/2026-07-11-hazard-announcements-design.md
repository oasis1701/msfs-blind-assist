# Turbulence & Icing Announcements — Design

**Date:** 2026-07-11
**Branch:** `feat/hazard-announcements` (stacked on `feat/activesky-improvements` / PR #159)
**Status:** Approved by Robin (brainstorming session 2026-07-11); implementation plan to follow.

## 1. Background and motivation

The ambient auto-announce (`MainForm.Announcers.cs`, 30-second `weatherAnnouncementTimer`)
already speaks cloud entry/exit, precipitation changes, and visibility crossings. Robin asked
for the same treatment for **turbulence** and **icing** — conditions a blind pilot cannot see
coming and, in a sim, may not physically feel. Both data paths already exist:

- **Turbulence:** ActiveSky's `Conditions.AmbientTurbulence` (1–100 at the aircraft's
  position/altitude) is fetched by every 60-second `ActiveSkyWeatherMonitor` tick and today
  surfaces only in the Weather Radar form (bucketed by `CategorizeTurbulence`, ≤25 hidden).
- **Icing:** the stock `STRUCTURAL ICE PCT` SimVar is the sim's actual airframe ice accretion,
  engine-independent. The FBW A380 definition already has a tuned aircraft-specific accretion
  announcer (`FlyByWireA380Definition.cs` ~2868: FBW ice-stick indicator, rising 0.05 /
  falling 0.02 hysteresis, first-sample baseline silence) — the proven pattern this design
  generalizes.

Decisions fixed during brainstorming:

- **Scope:** turbulence category transitions + ice-accretion announcer. The heuristic
  "icing-conditions advisory" (in-cloud + 0…−20 °C, warns before ice forms) is **out of
  scope** — too chatty for cold-cloud transits; revisit only if real flying shows a gap.
- **Turbulence transitions:** ALL directions speak (worsening and easing, plus "Smooth air").
- **Icing:** binary with hysteresis — enter/clear only, no severity tiers, no reminders.
- **Settings:** two sub-toggles under the master auto-announce switch, both default ON;
  the turbulence toggle is hidden when ActiveSky is disabled.

## 2. Architecture (Approach A — each announcer rides the tick that already carries its data)

No new poll loops, no new HTTP traffic, no new forms. Two new pure tracker classes (the
`ActiveSkyModeTracker` pattern: `internal sealed`, `Observe(...)` → utterance string or null,
fully characterization-tested), each wired into an existing tick.

### 2.1 `Services/TurbulenceCategoryTracker.cs`

`internal sealed class TurbulenceCategoryTracker` with `public string? Observe(double turbulence)`.

**Categories** reuse the Weather Radar's exact `CategorizeTurbulence` boundaries:

| Value | Category |
|---|---|
| ≤ 25 | smooth (hidden per the documented raw-turbulence invariant — never named, never a number) |
| ≤ 50 | light |
| ≤ 75 | moderate |
| ≤ 90 | severe |
| > 90 | extreme |

**Hysteresis:** moving UP into a category happens at the boundary (value > 25 enters light,
> 50 enters moderate, …). Moving DOWN requires the value to fall **5 points below** the
boundary it re-crosses (light→smooth needs ≤ 20; moderate→light needs ≤ 45; severe→moderate
≤ 70; extreme→severe ≤ 85). A value oscillating exactly on a boundary therefore never flaps.

**Utterances** (words only, never the raw number):
- smooth → any category: `"Entering {category} turbulence"`
- worsening between categories: `"Turbulence now {category}"`
- easing between categories: `"Turbulence easing to {category}"`
- any category → smooth: `"Smooth air"`

**Baseline rules** (mirroring `ActiveSkyModeTracker`): the first successful read baselines
silently. The baseline survives AS-unreachable gaps — a genuine category change across a gap
announces on reconnect. A failed/absent conditions fetch never calls `Observe` and never
consumes the baseline. `Reset()` clears the baseline (aircraft switch).

**Wiring:** `ActiveSkyWeatherMonitor.OnTickAsync`, immediately after the conditions fetch
succeeds (`conditions != null`), before the weather-refresh logic:

```
if (Settings.SettingsManager.Current.AnnounceTurbulenceEnabled)
{
    string? turb = _turbulenceTracker.Observe(conditions.AmbientTurbulence);
    if (turb != null && !_disposed) _announcer.Announce(turb);
}
```

The monitor's existing gating (`ShouldRun` = `ActiveSkyEnabled` AND `WeatherAutoAnnounceEnabled`)
already scopes this correctly; the per-call settings read makes the toggle live without extra
wiring. The tick runs on the UI thread (WinForms timer), so `Announce` is safe.

### 2.2 `Services/IceAccretionTracker.cs`

`internal sealed class IceAccretionTracker` with `public string? Observe(double iceRatio)`.

- Input is the `STRUCTURAL ICE PCT` value as a 0…1 ratio; NaN/negative are clamped to 0 by
  the caller before `Observe`.
- Rising edge: ratio ≥ **0.05** while not in the icing state → `"Icing conditions, ice
  accumulating"`; latches the state.
- Falling edge: ratio ≤ **0.02** while in the icing state → `"Icing conditions cleared"`.
- Between 0.02 and 0.05: no change (hysteresis dead band).
- First sample is baseline-silenced (the A380 `_icingBaselineDone` behavior): if the app
  starts/connects with ice already on the airframe, the tracker adopts that state silently
  and only announces subsequent transitions.
- `Reset()` clears state + baseline (aircraft switch).

Thresholds 0.05/0.02 are copied from the A380's sim-verified constants
(`ICING_DETECT_RATIO`/`ICING_CLEAR_RATIO`).

### 2.3 SimConnect: one new field on the ambient struct

`SimConnectManager.AmbientWeatherData` gains `public double StructuralIcePct;` and the
ambient data definition registers `STRUCTURAL ICE PCT` (ratio units — "percent over 100";
the exact SimConnect unit string is verified at implementation against how the A380 def or
the SDK reads ratio-valued vars). It arrives on the existing `RequestWeatherInfo` call — the
same 30-second tick, the same 3-second timeout/null-skip protection against stalled-sim
false announces. One additional var in the definition is far inside the data-def budget.

### 2.4 Wiring + the A380 yield

`AnnounceAmbientChanges` (MainForm.Announcers.cs), next to the cloud announcer:

```
if (Settings.SettingsManager.Current.AnnounceIcingEnabled
    && currentAircraft?.HasOwnIcingAnnouncer != true)
{
    string? ice = _iceAccretionTracker.Observe(double.IsNaN(data.StructuralIcePct) || data.StructuralIcePct < 0
        ? 0 : data.StructuralIcePct);
    if (ice != null) announcer.Announce(ice);
}
```

**A380 yield:** `IAircraftDefinition` (via `BaseAircraftDefinition`) gains
`virtual bool HasOwnIcingAnnouncer => false`; `FlyByWireA380Definition` overrides `true`.
While the A380 is loaded the generic tracker is skipped entirely (not merely muted), so its
tuned FBW-stick announcer remains the single voice — the same one-condition-one-call-out rule
as the documented PB-light/ECAM-memo invariant. The A380's own announcer keeps its existing
wording and is NOT gated on the new `AnnounceIcingEnabled` setting in this design (it predates
it and is aircraft-curated); if Robin wants the toggle to govern it too, that is a one-line
follow-up in the A380 def.

**Resets:** both trackers reset on aircraft switch (the `SwitchAircraft` cleanup path that
already stops/starts the weather timers). Ice state and position discontinuities make stale
baselines meaningless; post-switch first samples re-baseline silently.

## 3. Settings & UI

Two new `UserSettings` bools, both **default `true`**:

- `AnnounceTurbulenceEnabled` — spoken gate for the turbulence tracker.
- `AnnounceIcingEnabled` — spoken gate for the generic ice tracker.

Weather settings tab, announcements group, directly under the master "Auto-announce weather
state changes" checkbox:

- `"Announce &turbulence changes"` — visible only while master AND ActiveSky are both checked
  (it cannot work without AS), via the existing `UpdateActiveSkyDependentVisibility` pattern.
- `"Announce &icing"` — visible only while the master is checked (works regardless of AS).

Hiding never resets the stored value (`ApplyTo` reads the checkboxes regardless — the
established interval-combo behavior). `LoadFrom`/`ApplyTo` round-trip both settings. Because
the announce sites read `SettingsManager.Current` per tick, toggling takes effect on the next
tick with no `ApplyRuntimeSettings` wiring.

Accessible names/descriptions follow the panel's existing style; both checkboxes state that
they only apply while auto-announce weather is enabled, and the turbulence one that it
requires ActiveSky.

## 4. Error handling

- Failed/absent AS conditions → turbulence tracker not called; baseline untouched.
- Sim stall → the ambient tick's existing timeout already skips the pass; no ice sample, no
  false announce.
- NaN/negative ice ratio → clamped to 0 before `Observe`.
- No announce storms by construction: baseline-first + categorical comparison + hysteresis
  on both trackers.
- Both announce sites use `announcer.Announce` (queued) — these are background state changes,
  never `AnnounceImmediate`.

## 5. Testing

Pure logic (CI, TDD):
- `TurbulenceCategoryTrackerTests`: first-read silence; every transition direction and its
  exact utterance; hysteresis edges (26 enters light; 25 stays smooth; from light, 21–25
  stays light, ≤20 → "Smooth air"; boundary oscillation never flaps); multi-category jumps
  (smooth→moderate announces "Entering moderate turbulence"); gap survival (no Observe calls,
  then changed category announces); `Reset()` re-baselines silently.
- `IceAccretionTrackerTests`: first-sample silence (including first sample already ≥0.05);
  rising edge at exactly 0.05; dead band 0.02–0.05 silent in both states; falling edge at
  ≤0.02; repeated cycles; `Reset()`.
- `WeatherPanelTests`: round-trip of both new settings; visibility rules for both checkboxes.

Sim-facing (in-sim test plan in the PR): STRUCTURAL ICE PCT registration reads plausibly;
flying into AS turbulence speaks the right category words at the right times; picking up ice
in a cold cloud announces once and clears once; the A380 announces icing exactly once (its own
voice); both checkboxes toggle the announcements live; everything silent with the master
auto-announce off.

## 6. Documentation

- `docs/weather.md`: new section for the two hazard announcers (data sources, thresholds,
  hysteresis values, baseline rules, the A380 yield).
- `CLAUDE.md` weather invariants: turbulence announcements are words-only with ≤25 hidden
  (extends the existing raw-number invariant to the spoken surface); the generic icing
  announcer must yield to `HasOwnIcingAnnouncer` aircraft; hysteresis/threshold values are
  tuned constants — don't "simplify" them away.

## 7. Delivery

Branch `feat/hazard-announcements`, stacked on `feat/activesky-improvements` (this feature
modifies the same monitor tick PR #159 touches). PR opens against the #159 branch and
retargets to main after #159 merges (or rebases onto main if #159 lands first). Commit order:
turbulence tracker → monitor wiring → ice tracker → SimConnect field + wiring + A380 yield →
settings UI → docs.

## 8. Out of scope (recorded)

- Icing-conditions advisory (in-cloud + temperature band) — revisit only on demonstrated need.
- Severity tiers or periodic reminders for icing.
- Profile-based look-ahead ("icing layer in your descent") — belongs with a future
  vertical-profile enhancement.
- Gating the A380's own icing announcer on `AnnounceIcingEnabled` (one-liner if wanted).

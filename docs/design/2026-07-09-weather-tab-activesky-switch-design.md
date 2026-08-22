# Weather Settings Tab + ActiveSky Master Switch — Design

**Date:** 2026-07-09
**Status:** Approved by Robin (this session)
**Branch:** `feature/weather-tab-activesky-switch`

## Problem

PR #139 (issue #129) made the output+I wind hotkey and the Alt+W precip auto-announce
consult ActiveSky. `ActiveSkyClient.IsRunningAsync()` has a ~1.2 s parallel-probe floor
when AS is absent, so every `I` press paid ~1.2 s for users without ActiveSky — the
majority. Separately, the `ActiveSkyWeatherMonitor` has always been started
unconditionally at app launch, paying that same probe on every poll for everyone.

The wind-source question is genuinely per-population: without AS, SimConnect IS the
weather engine; with AS, community consensus (and Robin's reading) is that AS's
injection leaves SimConnect ambient winds inaccurate, making the AS API the source of
truth — and gust data exists only in the AS API. No single source is right for both
groups, so the integration must be an explicit opt-in, not a liveness guess.

## Decision

One new setting, **`UserSettings.ActiveSkyEnabled`, default `false`**, surfaced on a
new **Weather** tab in the Settings dialog. When off, no ActiveSky code path runs —
zero probing anywhere. When on, the full #129 behavior applies. AS-specific features
are hidden (not stubbed) when off. Rejected alternatives: full revert of the #129 wind
change (un-fixes #129 for AS users, loses gusts); auto-detect with caching (source
non-determinism is unacceptable for a screen-reader app); weather-source enum (YAGNI —
AS augments the sim engine, it does not replace it).

## 1. Setting

- `UserSettings.ActiveSkyEnabled { get; set; } = false;` in the Weather Settings
  region, added to the settings clone/copy block like its siblings.
- No migration: absent-from-JSON reads `false`, the desired default. Existing AS users
  flip it once — release-note line required.

## 2. Weather settings tab

New `Forms/Settings/WeatherPanel.cs` implementing `ISettingsPanel`
(`TabTitle = "Weather"`), added to `SettingsForm`'s panel list directly after
`AnnouncementsPanel`. Layout, top to bottom:

- **GroupBox "ActiveSky (HiFi)"**: checkbox *"Enable &ActiveSky integration"*.
  AccessibleDescription: requires HiFi ActiveSky running on this PC; sources wind,
  gusts, precipitation and decoded station weather from ActiveSky instead of the sim's
  own weather; leave off if you don't use ActiveSky.
- **GroupBox "Announcements"**: the four weather controls moved verbatim from
  `AnnouncementsPanel.BuildWeatherGroup()` — auto-announce weather changes checkbox,
  interval combo, SIGMET/AIRMET alerts checkbox, PIREP alerts checkbox. Settings keys
  unchanged. `AnnouncementsPanel` keeps only its General group.

SIGMET/AIRMET/PIREP data comes from aviationweather.gov (web), independent of AS —
those features work regardless of the switch; only their UI home moves.

## 3. Gating map (defense in depth)

**Central gate:** `ActiveSkyClient.IsRunningAsync()` returns `false` immediately —
no HTTP, `LastStatus = "disabled in settings"` — when
`!SettingsManager.Current.ActiveSkyEnabled`. This alone guarantees no probe can ever
run, even from a future call site that forgets to check.

**Per-feature gates** (deterministic behavior + hide-when-off semantics):

| Feature | Switch OFF (default) | Switch ON |
|---|---|---|
| `RequestWindInfo` (output+I) | Pre-#139 SimConnect path; AS code not touched | AS wind + "gusting N" (#129 behavior) |
| `CheckAmbientWeatherChanges` (Alt+W precip) | SimConnect bitmask branch directly | Station/position METAR branch |
| `ActiveSkyWeatherMonitor` | Never started | Started; polls as today |
| `WeatherRadarForm` | Skip probe (`_activeSkyAvailable = false`); SimConnect-only rendering via existing fallback | As today |
| `METARReportForm` | Skip probe; AS METAR box hidden via existing no-AS layout | As today |

The precip fix, radar source precedence, null-timeout fixes and no-repeat compare from
#129 are all KEPT — they gate, they do not revert. Only pathway selection changes.

## 4. Live toggle

`MainForm.ApplyRuntimeSettings()` (the existing post-OK hook) starts/stops the monitor
on change: enabled → `Start()`, disabled → `Stop()`. All other sites read the setting
per-use, so the change takes effect on the next press/tick without restart.
`MainForm`'s unconditional monitor start at launch becomes conditional on the setting.

## 5. Docs, invariants, logging

- CLAUDE.md weather invariants updated: AS integration is strictly opt-in
  (`ActiveSkyEnabled`, default off); no AS probe may ever run on an unopted path (the
  probe has a ~1.2 s floor when AS is absent); wind truth is per-engine — SimConnect
  when off, AS API when on (AS wind smoothing makes the two legitimately differ;
  neither is a bug). The existing "source the output+I wind from ActiveSky" bullet is
  amended to be conditional on the switch.
- `ActiveSkyClient` gains `Log.Debug("ActiveSky", ...)` lines: disabled short-circuit,
  probe outcomes with durations, conditions-fetch failures — closing the observability
  gap (the client currently writes nothing to debug.log).

## 6. Testing & verification

- Unit (xUnit, `SettingsManagerGlobalState` collection where SettingsManager is read):
  `WeatherPanel` LoadFrom/ApplyTo round-trip; `IsRunningAsync` disabled short-circuit
  returns false without network I/O (verify via LastStatus and elapsed time).
- Existing suite stays green.
- In-sim PR test plan: fresh settings → `I` answers instantly, debug.log has zero
  ActiveSky lines; enable mid-session → monitor starts without restart, radar shows AS
  data; disable mid-session → monitor stops, radar reverts to SimConnect-only.

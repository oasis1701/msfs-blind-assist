---
paths:
  - "MSFSBlindAssist/Services/ActiveSky*.cs"
  - "MSFSBlindAssist/Services/WeatherService.cs"
  - "MSFSBlindAssist/Services/TurbulenceCategoryTracker.cs"
  - "MSFSBlindAssist/Services/IceAccretionTracker.cs"
  - "MSFSBlindAssist/Services/RouteAdvisory*.cs"
  - "MSFSBlindAssist/Services/TurnaroundLiftoffDetector.cs"
  - "MSFSBlindAssist/Forms/WeatherRadarForm.cs"
  - "tests/MSFSBlindAssist.Tests/**/*Weather*.cs"
---
# Weather and ActiveSky rules

Loaded when Claude reads matching code. Background: docs/weather.md. Full text of each rule: docs/invariants/weather.md.

- [WX-1] Never scan `%APPDATA%\HiFi\` for the ActiveSky settings-file port: recursive directory enumeration there blocks the UI thread for many seconds. Full: docs/invariants/weather.md#wx-1
- [WX-2] A METAR that says "no precipitation" must render as "None", never fall through to the next weather source; only a wholly missing METAR triggers fallback. Full: docs/invariants/weather.md#wx-2
- [WX-3] Turbulence ≤25 (the ActiveSky calm-weather baseline) must be hidden entirely, never shown as a raw alarming number. Full: docs/invariants/weather.md#wx-3
- [WX-4] The WMO/ICAO weather-token decoder in `WeatherRadarForm.ParsePrecipFromMetar` has a duplicate in the weather monitor; never move or change one without the other, keep both copies in sync. Full: docs/invariants/weather.md#wx-4
- [WX-5] The Weather Radar, the ActiveSky decoded-weather monitor and the Alt+W auto-announce MUST share one precip source precedence (closest-station METAR, then position METAR, then SimConnect bitmask) so they never contradict. Full: docs/invariants/weather.md#wx-5
- [WX-6] The ActiveSky precip auto-announce must not repeat an unchanged phrase: compare the decoded phrase trimmed and case-insensitive, and speak only on start, stop or a genuinely different phrase. Full: docs/invariants/weather.md#wx-6
- [WX-7] ActiveSky is strictly opt-in (`UserSettings.ActiveSkyEnabled`, default off): no AS probe may run on an unopted path, gated centrally in `ActiveSkyClient.IsRunningAsync`; `ActiveSkyWeatherMonitor` runs only when `ShouldRun` (also needs `WeatherAutoAnnounceEnabled`), never on the AS switch alone. Full: docs/invariants/weather.md#wx-7
- [WX-8] Wind truth is per-engine, keyed on the ActiveSky switch: AS on, precip auto-announce and output+I wind come from ActiveSky; AS off, from SimConnect; never hard-pick one for both. The AS `SurfaceGustSpeed` suffix is on-ground-only; never append it to an airborne wind readout. Full: docs/invariants/weather.md#wx-8
- [WX-9] Winds Aloft uses `/GetAtmosphere` when AS is enabled and reachable, else Open-Meteo, each with a `Source:` tag; both must call `ActiveSkyFormatting.WindsAloftAltitudes` for the ±5000 ft/1000-ft window, never an inline copy (more: see full). Full: docs/invariants/weather.md#wx-9
- [WX-10] Turbulence is words-only (raw 1-100 never spoken, ≤25 "smooth" never named) with tuned hysteresis, don't simplify it; the generic STRUCTURAL ICE PCT announcer must skip `HasOwnIcingAnnouncer` aircraft; both trackers are baseline-first, reset on aircraft switch and sim reconnect. Full: docs/invariants/weather.md#wx-10
- [WX-11] Route-advisory location context is additive-only (no geometry renders as before, never dropped); announcements are proximity events: Approach once inside `RouteAdvisoryProximityNm` (never folded into `SigmetProximityRangeNm`), behind-suppression Approach-only, Leave confirmed over 2 ticks, expiry silent (more: see full). Full: docs/invariants/weather.md#wx-11

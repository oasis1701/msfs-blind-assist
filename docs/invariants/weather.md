# Weather and ActiveSky — rules in full

Each section is the complete text of one rule. Its one-line form, under the same ID, is in `.claude/rules/weather.md`, which Claude Code loads when it reads matching code. Background: [weather.md](../weather.md).
The text is verbatim from CLAUDE.md as of `1f37801a`; a trailing "→ doc" pointer is the original's. Cross-references such as "the bullet below", "above" or "under Core" point at CLAUDE.md's old single list, whose rules now live in several files: search `docs/invariants/` for the rule's key name to find it.

## WX-1

- Never scan `%APPDATA%\HiFi\` for the ActiveSky settings-file port — recursive directory enumeration there blocks the UI thread for many seconds. → [weather.md](../weather.md)

## WX-2

- A "METAR says no precipitation" result must render as "None," never fall through to the next weather source — only a wholly-missing METAR should trigger fallback. → [weather.md](../weather.md)

## WX-3

- Turbulence ≤25 (the AS calm-weather baseline) must be hidden entirely, never shown as a raw alarming number. → [weather.md](../weather.md)

## WX-4

- Don't move the WMO/ICAO weather-token decoder out of `WeatherRadarForm.ParsePrecipFromMetar` without updating its duplicate in the weather monitor — keep both copies in sync. → [weather.md](../weather.md)

## WX-5

- All three precipitation readouts — the Weather Radar, the ActiveSky decoded-weather monitor, and the Alt+W auto-announce — MUST derive precip from the SAME source precedence (closest-station METAR first, position METAR second, SimConnect bitmask last) so they never contradict; the difference the user reported (radar rain vs decoded "none") was two features reading different METARs. → [weather.md](../weather.md)

## WX-6

- The ActiveSky precip auto-announce must NOT repeat an unchanged phrase: compare the decoded precip phrase trimmed + case-insensitive, and speak only on start / stop / a genuinely different phrase ("light rain" → "light rain" stays silent). → [weather.md](../weather.md)

## WX-7

- ActiveSky integration is strictly OPT-IN (`UserSettings.ActiveSkyEnabled`, Weather settings tab, default OFF) — no AS probe may ever run on an unopted path: the liveness probe has a ~1.2 s floor when AS is absent, which every non-AS user paid per output+I press before the switch existed. The gate is CENTRAL, inside `ActiveSkyClient.IsRunningAsync` (returns false instantly, `LastStatus = "disabled in settings"`); every call site degrades to its no-AS path through it, and the `ActiveSkyWeatherMonitor` additionally runs only when `ActiveSkyWeatherMonitor.ShouldRun` says so (`ActiveSkyEnabled` AND `WeatherAutoAnnounceEnabled`) — the AS switch alone must never start the spoken weather updates. → [weather.md](../weather.md)

## WX-8

- Wind truth is PER-ENGINE, keyed on the ActiveSky switch: with AS enabled, the Alt+W precip auto-announce AND the output+I wind come from ActiveSky (station/position METAR for precip; AS ambient wind for wind — the SimConnect `AMBIENT PRECIP STATE` bitmask sticks and the ambient wind can diverge under AS wind smoothing); with the switch off, SimConnect is authoritative and correct. Neither source is "the" truth — never hard-pick one for both populations. The AS `SurfaceGustSpeed` suffix is ON-GROUND-ONLY: `Ambient*` and `Surface*` are independent field groups (at-altitude vs ground below), so never re-append the surface gust to an airborne wind readout — destination gusts come from the destination METAR's `G##` group instead. → [weather.md](../weather.md)

## WX-9

- The Winds Aloft box follows the per-engine source rule: `/GetAtmosphere` when AS is enabled+reachable, Open-Meteo otherwise, each with a visible `Source:` tag — and the ±5000 ft/1000-ft altitude window must stay IDENTICAL between the two sources. That window is now enforced STRUCTURALLY, not by convention: both paths call the single `ActiveSkyFormatting.WindsAloftAltitudes` helper (`WeatherService.ParseWindsAloft` calls it for the Open-Meteo path too) — never reintroduce an inline copy of the ±5000/1000-ft-step math in either path. The AS mode monitor is baseline-first (silent at startup/connect; the baseline survives unreachable gaps so a reconnect in a different mode announces); AS-only UI sections are HIDDEN when the switch is off, never shown with disabled text. → [weather.md](../weather.md)

Split out on 2026-10-09: WX-12 (ActiveSky-only UI is hidden, never disabled) and WX-13 (the mode baseline survives unreachable gaps). This text keeps them as written; each has its own section below.

## WX-10

- Hazard announcements: turbulence is WORDS-ONLY (raw 1-100 never spoken; ≤25 "smooth" never named as a category) with rising-at-boundary/easing-5-below hysteresis — don't "simplify" the tuned thresholds; the generic STRUCTURAL ICE PCT announcer must SKIP aircraft with `HasOwnIcingAnnouncer` (A380 ice stick) so one icing episode never speaks twice; both trackers are baseline-first (silent first read, reset on aircraft switch AND sim reconnect). → [weather.md](../weather.md)

## WX-11

- Route-advisory location context is ADDITIVE-ONLY (no geometry → the advisory renders exactly as before, never dropped/blocked); only the FIRST advisory of a positional GetActiveSigmetsAt response is position-matched (bundling); tier-2 borrows aviationweather.gov geometry by EXACT identity match only — never attach geometry to an advisory whose identity didn't match. Route-advisory announcements (2026-07-14) are PROXIMITY EVENTS, not key-novelty: Approach fires once per approach within the configurable ring (`RouteAdvisoryProximityNm`, default 100 nm, clamped 10-500; re-arm = ring + 10 nm) — independent of `SigmetProximityRangeNm`, never fold them together — UNLESS the area is behind the aircraft — behind-suppression is Approach-ONLY, Enter and Leave always fire regardless of bearing; Leave needs 2 consecutive not-inside ticks to confirm (no single-tick boundary-graze flap); once a key has been Inside, Approach is latched off for it forever. Expiry (a key vanishing from the feed) is always SILENT by design — announcing it would resurrect the hourly-SIGMET-reissue double-announce the redesign exists to kill. A positional probe match may only STRENGTHEN an Inside verdict, never weaken one — never derive Leave from a probe match going away (one exception since 2026-10-15: WX-14's `BuildFact` far-outside guard). No-geometry advisories announce once and are NEVER dropped, but also never gain Far/Near/Inside distance zoning even if geometry later appears for that key (a recorded follow-up, not a bug). The tracker resets a third way too: a turnaround liftoff (touchdown + ≥5 min ground dwell + liftoff, via `Services/TurnaroundLiftoffDetector.cs`) — a surviving advisory key's Inside latch must not suppress flight 2's approach call; touch-and-goes/bounces and the session's first departure never fire it. → [weather.md](../weather.md)

Split out on 2026-10-09: WX-14 (the probe's first block, and a probe match only strengthens Inside) and WX-15 (the turnaround liftoff reset's wiring). This text keeps them as written; each has its own section below.

## WX-12

- ActiveSky-only UI is HIDDEN while the ActiveSky switch is off, never shown disabled or with "disabled" text: the weather radar's mode, station, profile and route-advisory sections (`WeatherRadarForm.RefreshAsync`, decided on every refresh), the METAR window's ActiveSky METAR and forecast controls (`METARReportForm`) and the Weather panel's ActiveSky settings (`WeatherPanel`). `WeatherPanelTests` pin the panel; both forms need a live client and are untested. → [weather.md](../weather.md)

Split from WX-9 on 2026-10-09: one mechanism per ID.

## WX-13

- The ActiveSky mode-change announcement is baseline-first (`ActiveSkyModeTracker`: silent at startup and on connect), and its baseline SURVIVES unreachable gaps, so ActiveSky coming back in a different mode is announced. `ActiveSkyWeatherMonitor` feeds the tracker only successful reads, and where an unreachable tick resets the WEATHER baseline it never re-seeds or resets the mode tracker: a "reset everything" in that branch silences the reconnect announcement and still passes every test (`ActiveSkyModeTrackerTests` pin the tracker, not the monitor's wiring). → [weather.md](../weather.md)

Split from WX-9 on 2026-10-09: one mechanism per ID.

## WX-14

- `RouteAdvisoryLocator.ComputeFactsAsync` position-matches ONLY the first block of the positional `GetActiveSigmetsAt` probe's answer: the later blocks are bundled advisories unrelated to the position. A probe match only STRENGTHENS an Inside verdict; never derive outside or Leave from a probe match going away (the tracker keeps the same rule for a no-geometry key, pinned by `RouteAdvisoryProximityTrackerTests`).
- One exception: `RouteAdvisoryLocator.BuildFact` ignores a probe match when the advisory's own shape is whole (`AdvisoryGeometry.ParseWiPolygon`'s `wholeShape`: no position, degrees-only included, follows the WI ring; or a tier-2 feature of a single polygon, `WeatherService.FindAdvisoryPolygonInGeoJson`'s `wholeShape`) and the aircraft is more than `ProbeTrustMarginNm` (150 nm) outside it. The fact then reads the edge distance. Live 2026-10-15: ActiveSky's probe answered CZVR SIGMET N2, a British Columbia mountain-wave SIGMET, for an MD-11 over Wisconsin about 1,600 nm away, and answered no-hit at 57.5N 131W, about 16 nm from the SIGMET's own line. Trusting it announced "Entering advisory area" and showed the row as Inside, while the identical CZEG SIGMET M2 read 1,800 nm ahead. The margin is wide on purpose: it only has to catch a gross misfire. A "WI nnNM WID LINE" body is measured as its corridor outline at the stated width (`AdvisoryGeometry.CorridorOutline`), never its line points, so a corridor of any width is its own whole shape.
- What never overrules the probe: a partial ring (a VA/TC forecast group, a TC center position, a second area), a tier-2 `MultiPolygon` of several polygons (only the first ring is returned), and a ring across 180° (`AdvisoryGeometry.CrossesAntimeridian`), which plain lat/lon `IsInside`/`NearestEdge` read as a 340° band, about 500 nm from an aircraft inside it. Tier 2 runs for a probe-matched key too, so its polygon can overrule a far-off match.
- Hysteresis: a key the announcer's tracker holds Inside (`RouteAdvisoryProximityTracker.IsInside`, passed as `ComputeFactsAsync`'s `wasInside`) keeps its match out to the margin plus `ProbeTrustBandNm` (10 nm), so a match hovering at the margin cannot alternate Enter and Leave. The radar box passes no tracker and reads each refresh as it is.
- `RouteAdvisoryLocatorTests` pin `BuildFact` (the live capture, the margin and band both ways, a ring across 180°, a partial ring, no geometry) and, through `ComputeFactsAsync`'s positional-probe seam, its wiring into `BuildFact` (the live CZVR N2 answer, a partial VA ring, the band). `AdvisoryGeometryTests` pin `wholeShape` and the corridor outline. The first-block-only match against a bundled probe answer is still unpinned. → [weather.md](../weather.md)

Split from WX-11 on 2026-10-09: one mechanism per ID. Amended 2026-10-15: the `BuildFact` far-outside exception, then its review fixes (corridor outline, 180°, tier 2, band).

## WX-15

- The route-advisory proximity tracker also resets on a turnaround liftoff: the `SIM_ON_GROUND` edge in `MainForm.Announcers.cs` calls `_routeAdvisoryProximity.Reset()` when `TurnaroundLiftoffDetector.ObserveEdge` fires (a touchdown, at least 5 minutes on the ground, then a liftoff); without it a surviving advisory key's Inside latch suppresses flight 2's Approach call. Touch-and-goes, bounces and the session's first departure never fire it (`TurnaroundLiftoffDetectorTests`); no test reaches the handler's wiring. → [weather.md](../weather.md)

Split from WX-11 on 2026-10-09: one mechanism per ID.

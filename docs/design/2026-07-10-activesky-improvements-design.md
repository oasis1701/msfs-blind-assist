# ActiveSky Read-Only Improvements — Design

**Date:** 2026-07-10
**Branch:** `feat/activesky-improvements`
**Status:** Approved by Robin (brainstorming session 2026-07-10); implementation plan to follow.

## 1. Background and motivation

A verified audit of HiFi ActiveSky's local HTTP API (port 19285; authoritative doc:
`C:\Program Files\HiFi\AS_FS\Documentation\Active_Sky_API.pdf`) against MSFSBA's current
consumption found the app uses only five endpoints (`GetMode` status-code-only,
`GetWeatherAreaJson`, `GetCurrentConditions`, `GetClosestStationWeather`, `GetMetarInfoAt`) and
identified 12 real gaps — data available from AS that never reaches a blind pilot in any
accessible form. Each gap was adversarially verified against both the codebase and a live ASFS
build.

This design covers the **read-only bundle**: seven items that expose weather *information*.
The write-path features (custom weather injection, training effects, SetMode control,
LoadFlightPlan + route SIGMETs) are explicitly **out of scope** — each changes sim/AS state and
gets its own design conversation and branch later.

## 2. Scope

In scope (all gated behind the existing `UserSettings.ActiveSkyEnabled` opt-in via the central
`ActiveSkyClient.IsRunningAsync` gate — no AS I/O ever happens on an unopted path):

1. **ActiveSky mode readout** — surface the `GetMode` body (mode + AS weather clock), plus a
   background announcement when the mode *changes* mid-session.
2. **Vertical weather profile** — `GetWeatherInfoXml` at the current position: cloud layers
   with bases/tops/coverage/icing/precip, winds/temps/turbulence aloft, as a curated narrative.
3. **Winds Aloft re-source** — the Weather Radar form's existing Winds Aloft box reads
   `GetAtmosphere` (sim-truth, adds temperatures) when AS is enabled; Open-Meteo unchanged as
   the non-AS path.
4. **On-demand closest-station decoded weather** — the ATIS-style utterance that today exists
   only as an auto-announce becomes readable in the Weather Radar form, with the station ICAO.
5. **Forecast METAR** — the Shift+M form gains a preset time-offset combo driving
   `GetMetarInfoAt`'s existing (already-plumbed, never-used) `timeoffset` parameter.
6. **Position dew point** — one decoded line from the already-fetched position METAR.
7. **LastStatus diagnostics** — `ActiveSkyClient.LastStatus` shown in the Weather settings
   panel, closing its doc-comment's unfulfilled "surfaced to the UI" promise.

Decisions fixed during brainstorming:

- **Form only.** No new Output Mode hotkeys; all new readouts live in existing forms.
- **Mode:** status line in the Weather Radar form + background change announcements
  (baseline-first, silent at startup/connect).
- **Forecast UX:** preset combo — Now / +1 h / +2 h / +4 h / +6 h.
- **Profile:** curated narrative (not a full layer dump), **current position only** (no ICAO
  input in v1).

## 3. Architecture (Approach A — extend existing classes, extract pure formatters)

No new forms, no new service facade, no hotkey changes. Three touched layers:

### 3.1 `Services/ActiveSkyClient.cs` — three new wrappers

Same shape as the existing five: opt-in gate check first, cached `LastSuccessfulPort`,
`CancellationTokenSource` timeout, `null` on any failure, never throws to callers.

- `GetModeAsync()` → raw mode string (e.g. `"Live Real time mode (Active) (2026/7/10 1935z)"`).
  Additionally `ProbePortAsync` — which already GETs `/GetMode` for liveness — now stores the
  response body in a new `LastModeText` property (with a timestamp), so the common case costs
  zero extra HTTP requests.
- `GetWeatherInfoXmlAsync(double lat, double lon)` → `VerticalProfile` model:
  `WindLayer[]` (altitude ft, direction °, speed kt, temperature °C, turbulence 0-100) and
  `CloudLayer[]` (base ft, top ft, coverage, icing, precip type, precip rate, turbulence).
  Endpoint: `GET /ActiveSky/API/GetWeatherInfoXml?lat=&lon=` (AS returns metres for cloud
  base/top — convert to feet at parse time). Parsing is permissive in the
  `ParseConditionsJson` style: missing/unparseable fields default, unknown elements ignored.
- `GetAtmosphereAsync(double lat, double lon, int[] altitudesFt)` → array of
  `(AltitudeFt, WindDirection, WindSpeed, TemperatureC)`.
  Endpoint: `GET /ActiveSky/API/GetAtmosphere?lat=&lon=&altitudes=a|b|c` (pipe-separated).

### 3.2 `Services/ActiveSkyFormatting.cs` — new static class, pure text builders

Every new string builder is an `internal static` pure function here, directly testable in CI
(the same pattern as the 2026-07 gust fix / `WindReadoutGustTests`):

- `ParseModeText(string raw)` → `(string ModeName, string? WeatherTimeZ)`. Recognizes the four
  AS mode families (Live Real time / Locked to sim time / Historic dynamic / Custom static);
  unknown strings pass through verbatim as the mode name (never crash, never hide).
- `BuildProfileNarrative(VerticalProfile p, double currentAltitudeFt)` → briefing text:
  - Cloud layers first, each as `"Broken, 4,500 to 9,200 feet, moderate icing, light rain"` —
    icing/precip/turbulence phrases included only when present; per-layer turbulence bucketed
    through the existing `CategorizeTurbulence` thresholds (≤ light-threshold hidden, per the
    documented raw-turbulence invariant).
  - Then `"Winds and temperatures aloft:"` at surface / 5,000 / 10,000 / 18,000 / 24,000 /
    34,000 ft **plus** the layer nearest the aircraft's current altitude, deduplicated, each as
    `"34,000 feet: 270 at 45, minus 42"`.
  - Empty layer set → `"No cloud layers reported below FL560."`
- `BuildWindsAloftLines((int,double,double,double)[] levels)` → the AS variant of the existing
  Open-Meteo Winds Aloft lines, now with temperature, ending with a source tag line
  (`"Source: ActiveSky"` / the Open-Meteo path appends `"Source: Open-Meteo"`).
- `FormatStationDecoded(...)` → **delegates** to the existing
  `ActiveSkyWeatherMonitor.DecodeMetar`/`BuildAnnouncement` statics — the form and the
  auto-announce must produce identical wording from one code path (no duplicated decoder;
  the METAR-token-decoder duplication invariant stays untouched).

### 3.3 `Services/ActiveSkyWeatherMonitor.cs` — mode tracking

The existing poll tick additionally reads the mode (from `LastModeText` when fresh, else
`GetModeAsync`). First successful read **baselines silently** (the EWD-monitor pattern —
nothing is announced at startup or on (re)connect). A subsequent read whose *parsed mode name*
differs announces once: `"ActiveSky weather mode changed to Custom static."` via the monitor's
existing UI-thread-marshalled announcer path.

Gating: runs only when the monitor already runs (`ShouldRun` = `ActiveSkyEnabled` AND
`WeatherAutoAnnounceEnabled`). This respects the documented invariant that the AS switch alone
must never start spoken weather updates. With auto-announce off, the mode is still visible on
demand in the Weather Radar form.

AS becoming unreachable is **not** a mode change: announcements require two successful reads
with different parsed modes. The unreachable case is owned by the existing "ActiveSky not
responding" surfaces. On regaining reachability the baseline is re-seeded silently only if the
mode is unchanged; if AS comes back in a *different* mode, that difference is announced (it is
a genuine change the pilot must hear).

## 4. Per-surface behavior

### 4.1 Weather Radar form (Shift+R)

All new content joins `RefreshAsync`, fetched **concurrently** with the existing fetches; each
section degrades independently to `"unavailable"` — one failed endpoint never blanks another
section. With the AS switch **off**, the new AS-only sections (mode line, closest-station box,
vertical profile box) show `"ActiveSky disabled in settings"` rather than `"unavailable"`, and
every pre-existing section behaves exactly as today. Top-to-bottom reading order: status →
current position → closest station → vertical profile → existing boxes (winds aloft,
advisories, …).

1. **Mode status line** (top): `"ActiveSky: Live Real time mode, weather time 1935Z"`. When AS
   is enabled but unreachable, this line carries the existing failure wording so the form and
   the output+I notice tell one story. When the AS switch is off, the line reads
   `"ActiveSky: disabled in settings"`.
2. **Current-position block** gains `"Temperature/dew point: 28 / 14"`, decoded from the
   position METAR the form already fetches (zero new requests). Dew point missing from the
   METAR → the line shows temperature only.
3. **Closest station box** (new): station ICAO + full decoded readout (identical wording to the
   auto-announce, same code path), with the raw METAR line underneath.
4. **Vertical profile box** (new, current position only): the `BuildProfileNarrative` output.
5. **Winds Aloft box**: with AS enabled, populated via `GetAtmosphereAsync` at the box's
   existing altitude set, now including temperature per level; with AS disabled, the
   Open-Meteo path is byte-for-byte unchanged. Both paths end with the source tag line.

### 4.2 METAR form (Shift+M)

A `"Forecast"` combo after the ICAO field: `Now, +1 hour, +2 hours, +4 hours, +6 hours`
(values 0/3600/7200/14400/21600 s passed as `timeoffset`). Applies **only** to the AS METAR
box; the VATSIM box always shows current weather. The AS box caption states the offset in
effect (e.g. `"ActiveSky METAR (+2 hours):"`). The combo resets to `Now` on form open. Combo
selection follows the global no-announce rule for combo changes (`MarkUiSet` not needed —
this combo drives no SimVar; it only parameterizes the next fetch).

### 4.3 Weather settings panel

Read-only `"ActiveSky status:"` line under the enable checkbox, showing
`ActiveSkyClient.LastStatus` verbatim (`"connected on port 19285"`, `"port 19285: timeout"`,
`"disabled in settings"`, …), refreshed when the panel is shown.

## 5. Error handling and performance

- Every wrapper: `null` on timeout/non-200/parse failure; forms render `"unavailable"` per
  section. Parsers permissive; a shape change in AS output degrades, never crashes.
- No announce storms: mode announcements need two successful reads with different parsed
  modes; unreachability is not a change.
- All fetches are on-demand (form refresh) or ride the existing monitor tick — **zero new
  background poll loops**, no per-frame work, nothing on the UI thread.
- No `%APPDATA%\HiFi` file access anywhere (documented UI-hang hazard); HTTP only, existing
  1.2 s probe / 5 s fetch timeouts.
- The `GetWeatherInfoXml`/`GetAtmosphere` position comes from `LastKnownPosition`, refreshed
  the same way the form's existing fetches do.

## 6. Testing

TDD throughout (failing test first), matching `WindReadoutGustTests`:

- `ParseModeText`: all four mode families, the live-observed string, garbage input, empty.
- `BuildProfileNarrative`: multi-layer sky with icing + precip; turbulence at/below/above the
  light threshold; level curation (dedup, nearest-to-current-altitude inclusion); empty sky;
  missing fields (no top, no temperature).
- `BuildWindsAloftLines`: normal set, single level, temperature sign rendering, source tag.
- XML/atmosphere parsers: golden AS response fixtures (captured live), malformed variants.
- Dew-point line builder: present, missing dew point, missing both.
- Mode-change decision logic (extracted pure): first-sight silence, change announce, unchanged
  silence, unreachable-gap handling, reconnect-in-different-mode announce.

Sim-facing paths (form rendering, live fetches, monitor announce timing, winds-aloft source
switch) get an **in-sim test plan in the PR**: sections populate with AS running; degrade with
AS closed; flipping AS Live→Custom announces exactly once; toggling the AS setting switches
the Winds Aloft source; forecast combo returns distinct METARs for distinct offsets.

## 7. Documentation updates

- `docs/weather.md`: new sections for the mode readout + monitor rule, the vertical profile,
  and the forecast combo; the per-engine wind-truth section (§3) extended to cover the Winds
  Aloft box source rule.
- `CLAUDE.md` weather invariants: one new line — the Winds Aloft display must follow the
  per-engine source rule (AS when enabled, Open-Meteo otherwise), and the mode monitor must
  stay baseline-first/silent-on-connect.

## 8. Delivery

One commit per feature on `feat/activesky-improvements` (already carrying the output+I gust
fix), one PR titled as the ActiveSky improvements bundle, with the combined in-sim test plan.
Suggested commit order (dependency-driven, cheapest first):

1. `GetMode` capture + `ParseModeText` + Weather Radar status line
2. Mode-change tracking in `ActiveSkyWeatherMonitor`
3. `LastStatus` line in Weather settings
4. Position dew-point line
5. Closest-station decoded box (reuses monitor statics)
6. Forecast METAR combo (Shift+M)
7. `GetAtmosphereAsync` + Winds Aloft re-source
8. `GetWeatherInfoXmlAsync` + vertical profile box
9. Docs updates

## 9. Out of scope (recorded for later branches)

- Weather injection (`ActiveSkyRemoteWeatherControl`) — switches AS to Custom mode; needs its
  own safety/UX design. Pairs with the mode readout shipped here.
- Training effects (`AddUserEffect`/`RemoveUserEffect`).
- `SetMode` control and historic-date UI.
- `LoadFlightPlan` push + route-scoped SIGMETs (`GetActiveSigmetsAt`) — needs a `.pln` export
  step that does not exist yet.
- Vertical profile for an arbitrary ICAO (deliberately deferred from v1).
- Dead-code cleanup adjacent to this work: `GetWeatherAreaAsync` (zero call sites) — item 5
  revives its sibling `timeoffset` plumbing; remove or revive `GetWeatherAreaAsync` in a
  separate housekeeping change if still unused after this bundle.

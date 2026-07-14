# Route-advisory location context — design

**Date:** 2026-07-13
**Status:** Approved (Robin, 2026-07-13)
**Prerequisites:** the en-route advisories feature (`docs/design/2026-07-12-route-advisories-design.md`,
weather.md §12) and the 2026-07-13 live-probe findings (weather.md §13).

## 1. Problem

The Route Advisories box lists SIGMETs/AIRMETs on the flight plan loaded in ActiveSky, but gives
no sense of WHERE each advisory sits relative to the aircraft. A blind pilot reading
"CONVECTIVE SIGMET 54E, thunderstorms, tops above FL450" cannot tell whether that's a cell to
punch through right after departure, something mid-cruise, or weather waiting at the descent —
Robin's 2026-07-13 ask.

## 2. Decision history (rejected richer design)

A route-aware design (SimBrief route fetch, positional-endpoint probe sweep along the route,
TOC/TOD-based phase words, nearest-waypoint anchoring) was designed first and REJECTED by Robin
as too many moving parts. Recorded here so it isn't re-proposed wholesale; its probe-sweep
mechanics remain viable if route-relative context is ever wanted (the 2026-07-13 probe facts in
weather.md §13 de-risk it). The approved replacement is position-relative only:

> Leave the advisory list as-is; add the distance between the aircraft's current position
> (SimConnect) and the advisory area, plus whether the aircraft is inside the area or the
> advisory is behind.

## 3. Output contract

Each route advisory gains ONE derived, clearly-labeled trailing line in the Route Advisories
box, in BOTH raw and decoded render modes (always a separate line — raw advisory text stays
verbatim above it):

- `Location: 123 nm ahead`
- `Location: 95 nm behind you`
- `Location: less than one nautical mile ahead` (distance rounds to whole nm; 0 → this wording)
- `Location: at your position (inside the area)`
- No line at all when no geometry could be resolved (§5) — the advisory renders exactly as
  today. The feature is additive-only.

The auto-announcement appends the same fact in spoken form, after the existing fields:
`"Route advisory: CONVECTIVE SIGMET 54E, thunderstorms, tops above FL450, 123 nautical miles
ahead."` / `", at your position."` / `", 95 nautical miles behind you."` — nothing appended
when unresolved. Speaking a distance is consistent with the existing SIGMET/PIREP proximity
alerts ("…, bearing 240 degrees, 85 nautical miles"); bearing is deliberately NOT spoken here
(Robin asked for distance + inside/behind only).

## 4. Geometry sources (two tiers + one authoritative probe)

**Tier 1 — the advisory's own `WI` polygon (ICAO-style bodies).** A new pure parser extracts
the `WI N1121 W10027 - N1258 W09506 - …` vertex list: tokens `[NS]ddmm` (degrees+minutes
latitude) and `[EW]dddmm` (degrees+minutes longitude), pairs separated by `-`. The polygon may
or may not repeat its first vertex; the parser treats it as closed either way. Fewer than 3
parsed vertices → no geometry from this tier. Circle shapes (`WI 60NM OF CENTRE …`) and
`ENTIRE FIR` bodies are out of scope for v1 — they fall through to tier 2 / no line.

**Tier 2 — aviationweather.gov cross-match (US convective bodies).** AS strips the location
line from US convective advisories (live-verified 2026-07-13, weather.md §13), so their text
carries no geometry. But their identity — `CONVECTIVE SIGMET 54E` — appears verbatim in the
aviationweather.gov `airsigmet` feed we ALREADY fetch and TTL-cache for the Nearby Advisories
box. A new internal `WeatherService` lookup finds the cached feature whose raw text contains
the advisory's decoded Identity phrase and returns its polygon ring. The lookup refreshes
through the existing TTL cache helper (no new HTTP within the TTL window; same lock
discipline). Both feeds are searched (`airsigmet` first, then `isigmet`), but ICAO advisories
rarely match (AS renders identities differently — "SIGA SIGMET E5" vs "SIGMET ECHO 5"), which
is fine: they carry tier-1 polygons.

Per-engine truth note: tier 2 borrows REAL-WORLD geometry for an AS-sourced advisory. In Live
mode the two sources carry identical advisories (live-verified 2026-07-13). In historic/custom
AS modes the real-world feed's IDs won't correspond to AS's replayed advisories, so the match
simply fails and the line is omitted — wrong geometry is never attached to the wrong advisory
by construction (exact-identity match only).

**Authoritative inside-check — one positional probe.** One `GetActiveSigmetsAt?lat=&lon=` GET
at the aircraft's own position per computation pass. Any route-advisory key that matches the
probe's position-matched advisory is "at your position" regardless of whether its geometry
resolved — this covers convective advisories in non-Live modes and is AS's own containment
truth. Per the 2026-07-13 bundling finding, ONLY THE FIRST parsed advisory of a positional
response is treated as position-matched (bundled extras after `---------------------- Hazard:`
separators are unrelated advisories). Known limitation until a genuine multi-advisory-at-point
response is captured: if the aircraft sits inside two overlapping advisories, only one is
confirmed "at your position" by the probe; the other can still resolve via its polygon
(point-in-polygon also yields inside).

## 5. Computation (pure)

Inputs: polygon vertices (tier 1 or 2), aircraft `Latitude`/`Longitude`/`HeadingMagnetic` +
`MagneticVariation` (SimConnect `AircraftPosition` — the existing `LastKnownPosition`), probe
result. Rules:

- **Inside:** ray-cast point-in-polygon on plain lat/lon (adequate at SIGMET scales; polygons
  spanning the antimeridian are a documented non-goal). Inside — by polygon OR by probe —
  renders "at your position (inside the area)" and wins over any distance.
- **Distance:** great-circle distance (`NavigationCalculator.CalculateDistance`) to the NEAREST
  VERTEX — deliberately the same approximation the Nearby Advisories box already uses
  (`WeatherService.ClosestPoint` is vertex-based), so the two boxes can never disagree about
  the same advisory's distance. Rounded to whole nm; 0 nm while outside → "less than one
  nautical mile".
- **Ahead/behind:** bearing to the nearest vertex (`NavigationCalculator.CalculateBearing`,
  true) vs the aircraft's TRUE heading (magnetic heading + magnetic variation, the codebase's
  existing convention); absolute relative bearing > 90° → "behind you", else "ahead". Binary by
  design (Robin asked ahead/behind, not clock positions). True HEADING, not ground track, is a
  documented approximation: in a strong crab the two differ by ~10-20°, which flips the word
  only when the advisory is nearly abeam — acceptable for advisory-level awareness, and heading
  is already in `AircraftPosition` while track is not.

## 6. Component shape

- `Services/AdvisoryGeometry.cs` (new, pure, internal): `ParseWiPolygon(string body)`,
  `IsInside(vertices, lat, lon)`, `NearestVertex(vertices, lat, lon)` (distance nm + true
  bearing), `IsBehind(bearingToDeg, trueHeadingDeg)`. No I/O, fully characterization-tested.
- `ActiveSkyFormatting.BuildLocationPhrase(double? distanceNm, bool inside, bool behind,
  bool spoken)` (pure): the §3 wording, null → no line. The `spoken` flag selects the
  surface — §3 mandates two renderings (box abbreviates "nm" and keeps the parenthetical;
  the spoken form spells "nautical miles" and drops it), so the builder must know which
  it is producing.
- `WeatherService.TryGetAdvisoryPolygonAsync(string identityPhrase)` (internal): the tier-2
  cached-feed lookup.
- `ActiveSkyClient.GetPositionalAdvisoriesTextAsync(double lat, double lon)` (new): the
  positional variant of the existing route call — same gate, port, timeout, null-on-error
  contract; invariant-culture lat/lon (API doc requirement).
- A small orchestrator (static helper) used by BOTH call sites — `WeatherRadarForm.
  FetchRouteAdvisoriesAsync` (box) and `MainForm.CheckRouteAdvisoriesAsync` (announce) — that
  takes the parsed advisories + aircraft position and returns `key → location phrase`. The box
  renders phrases via a new optional parameter on `BuildRouteAdvisoriesText`; the announcer
  appends the phrase to `BuildRouteAdvisoryAnnouncement`'s output for new keys only.

## 7. Cost & lifecycle

Per computation pass (radar refresh ≤ every 30 s while the form is open; announce check only
when a NEW key appears): one localhost probe GET + zero-to-one TTL-cached feed refresh + pure
math. No caching of computed phrases — the aircraft moves, distances are recomputed per pass
(the DisplayListBox reconcile keeps an updated Location row from stealing the reading cursor).
No new settings: the feature rides the existing route-advisories visibility (AS switch) and
announce toggle.

## 8. Error handling / degradation ladder

1. Geometry from tier 1; else tier 2; else none.
2. Probe fails / AS unreachable mid-pass → probe contributes nothing (geometry may still
   resolve). The location pass runs inside the same background tick that fires the
   announcement, so a hung probe adds at most its bounded 5 s timeout before the
   announcement goes out (against a 30 s tick cadence, imperceptible); on any failure the
   announcement fires without the suffix — it is never dropped, and the UI is never blocked.
3. No geometry AND no probe hit → no Location line, no announce suffix — today's behavior.
4. `LastKnownPosition` null or (0,0) → skip the whole pass (no lines).
5. Parser rejects malformed `WI` tokens permissively (skip advisory, never throw).

## 9. Testing

Pure (CI, characterization): `ParseWiPolygon` against the live MHTG and YMMM bodies (2026-07-12
capture — known vertex counts and first/last vertices) + malformed/short inputs;
point-in-polygon and nearest-vertex/bearing cases with hand-computed fixtures; `IsBehind`
boundary cases (90°, wrap-around at 000/360); `BuildLocationPhrase` all four wordings + null;
tier-2 lookup against a trimmed real `airsigmet` GeoJSON fixture (the live 54E feature,
2026-07-13); `BuildRouteAdvisoriesText` with a location map (line placement in raw AND decoded
modes); announcement suffix composition. Sim-facing (in-sim test plan, Robin runs): box shows
plausible distance/ahead-behind for a real route advisory; "at your position" when inside;
line absent for an `ENTIRE FIR`-style advisory; announce carries the suffix for a new key.

## 10. Out of scope (recorded)

- Route-relative context (phases, waypoints, along-route distances) — the rejected §2 design.
- `WI … NM OF CENTRE` circles and `ENTIRE FIR` geometry.
- Bearing/clock-position wording in the output.
- Ground-track-based ahead/behind (heading approximation documented in §5).
- Antimeridian-spanning polygons.
- Nearby Advisories box changes — it already has distance/bearing.

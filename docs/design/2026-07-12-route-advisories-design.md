# En-Route Advisories (ActiveSky Route SIGMETs/AIRMETs) — Design

**Date:** 2026-07-12
**Branch:** `feat/activesky-improvements` (part of PR #159)
**Status:** Approved by Robin (brainstorming session 2026-07-12); implementation plan to follow.

## 1. Background and motivation

The existing SIGMET/AIRMET feature is position+range only (aviationweather.gov, the Weather
Radar's "Nearby Advisories" box + the proximity auto-announce): a convective SIGMET 800 nm
down-route is invisible until the aircraft is inside `SigmetProximityRangeNm`. ActiveSky's
`GET /ActiveSky/API/GetActiveSigmetsAt` — called **parameterless** — answers for the aircraft's
**loaded flight plan route** instead.

The 2026-07-10 audit assumed this required MSFSBA to export a `.pln` and push it via
`LoadFlightPlan`. Live verification (2026-07-12, OMDB→LTFM enroute at FL358) proved that wrong
for SimBrief-linked setups: ActiveSky's own SimBrief downloader satisfies the "loaded flight
plan" requirement, and the parameterless call answered authoritatively for the current route
("No airmet/sigmet affecting currently loaded flight plan route"). No plan push, no file
reads — **API only**, per Robin's explicit constraints:

- **No `.pln` export / `LoadFlightPlan` push** — AS's SimBrief link (or the user loading a plan
  in AS themselves) is the route source. `LoadFlightPlan` remains a possible future fallback
  for non-SimBrief users; out of scope here.
- **No file reads** — `activeflightplanwx.txt` and friends are excluded (custom AS paths would
  break them). Everything comes from the HTTP API.
- **No per-waypoint route winds** — explicitly rejected as too verbose.

Decisions fixed during brainstorming:

- **Surface: Weather Radar box + new-advisory announce** (option A) — a "Route Advisories
  (ActiveSky)" list riding the 30 s live refresh, plus a baseline-first background
  announcement when a NEW advisory appears on the route mid-flight.
- **Announce gate: a NEW sub-toggle** (`AnnounceRouteAdvisoriesEnabled`), not the existing
  SIGMET-proximity toggle.
- Merging into the existing Nearby Advisories box was rejected (mixes two sources with
  different semantics — position+range vs along-plan — against the doc's source-clarity rules).

## 2. Data layer

### 2.1 `ActiveSkyClient.GetRouteAdvisoriesTextAsync()`

Standard wrapper shape (the eighth): opt-in gate (`ActiveSkyEnabled`), cached
`LastSuccessfulPort`, 5 s CTS, `GET {BaseUrl}/GetActiveSigmetsAt` (no query string), returns
the trimmed raw response text, null on any failure. Never throws.

### 2.2 `ActiveSkyFormatting.ParseRouteAdvisories(string?)` (pure, CI-tested)

Returns `List<RouteAdvisory>` where `RouteAdvisory` is a small model:
`{ string Key; IReadOnlyList<string> Lines; }`.

- Null/whitespace input → empty list (caller renders "unavailable" — fetch failed).
- The known no-hit sentence — any response whose trimmed text starts with
  `"No airmet/sigmet"` (case-insensitive) → empty list with a distinct "no advisories" outcome
  (see 2.3; parser returns empty + an `IsNoAdvisories` flag or the caller re-checks the
  sentinel — plan decides the exact shape, behavior is what's specified here).
- Otherwise: split into blocks on blank lines; each block's rows are its non-empty lines;
  the block's **first trimmed line is its dedup `Key`** (e.g. `"CONVECTIVE SIGMET 18E"`).
- DELIBERATELY DEFENSIVE: the route variant's hit format is only partially known (the
  positional variant returned `"CONVECTIVE SIGMET 18E / Valid until: 2000z / AREA TS MOV FROM
  26015KT. TOPS TO FL420..."` during the audit; the route variant's first real hit will be in
  the wild). Anything unrecognized renders verbatim as its own block rather than being
  dropped — an unanticipated "no flight plan loaded"-style sentence becomes one readable,
  self-explanatory row whose first line is its key.

## 3. Weather Radar section

New label + `DisplayListBox` **between Vertical Profile and Nearby Advisories**, following
every established convention: AS-only (hidden entirely when the switch is off, per-refresh
visibility toggle), Consolas 9, AccessibleName `"Route Advisories"`, label text
`"Route Advisories (ActiveSky):"`, fetched by a new `FetchRouteAdvisoriesAsync()` in
`RefreshAsync`'s parallel batch, riding the 30 s live refresh with in-place row reconcile.

Box content:
- Advisories present → each block's lines as rows, blank row between blocks.
- No-hit → one row: `"No advisories on route."`
- AS enabled but fetch failed → `"unavailable"`.

The existing "Decode advisories into plain English" toggle does **not** apply to this box in
v1 — AS's text is already terse plain language and the decoder is tuned to aviationweather.gov
formats (recorded out of scope).

**Form height:** the new section adds ~146 px. To stop compounding the fixed-height screen-fit
problem (flagged at 976 px by an earlier review), the form gains `AutoScroll = true` and
modest trims to existing boxes chosen at plan time (target: total height stays ≤ ~1040 px
while every box keeps ≥ ~100 px; blind users are unaffected by visual height — boxes scroll
internally — and sighted helpers on small screens get a scrollbar instead of clipped buttons).

## 4. Announce path

Rides the existing 30 s `weatherAnnouncementTimer` tick (`WeatherAnnouncementTimer_Tick`),
next to the proximity check, with its own reentrancy flag (the proximity `_proximityCheckRunning`
pattern). Each pass:

1. Gate: `AnnounceRouteAdvisoriesEnabled` setting, then `IsRunningAsync()` (instant false when
   the AS switch is off — no probe cost for non-AS users).
2. Fetch + parse (2.1/2.2). A failed fetch touches nothing.
3. Keys go to a pure **`RouteAdvisoryTracker`** (the `ActiveSkyModeTracker` pattern —
   `internal sealed`, CI-tested): `Observe(IReadOnlyList<string> keys)` returns the keys not
   seen before, or nothing on the FIRST successful read (**baseline-first**: preflight
   discovery belongs to the radar box, not a startup announcement burst).
4. One queued `announcer.Announce` per new key: `"New advisory on route: {key}."`
   (`Announce`, never `AnnounceImmediate` — background change.)

Key-set lifecycle:
- Persists across AS-unreachable gaps (no re-announce storm on reconnect; a NEW flight plan
  naturally produces new keys, which announce).
- Clears on the same housekeeping cadence as `_announcedSigmetKeys`/`_sigmetKeysClearedAt`
  (expired advisories may legitimately reissue) and in the same connect-time reset block.
- `Reset()` on aircraft switch, alongside the other hazard trackers.

## 5. Settings & UI

`UserSettings.AnnounceRouteAdvisoriesEnabled` — bool, **default true**, added in BOTH edit
sites (property block + `Clone()`).

Weather panel, announcements group: checkbox
`"Auto-announce new &route advisories (ActiveSky)"`, placed with the SIGMET/PIREP proximity
rows. Like those siblings it is **independent of the auto-announce weather master**; unlike
them it **hides when ActiveSky is disabled** (it cannot function without AS), via the existing
`UpdateActiveSkyDependentVisibility`. Hiding never resets the stored value. `LoadFrom`/`ApplyTo`
round-trip it. AccessibleDescription states both facts (announces new advisories affecting the
flight-plan route loaded in ActiveSky; requires ActiveSky).

## 6. Error handling

- Wrapper null on failure; parser never throws; box shows "unavailable"; announce path is a
  no-op on failure (tracker untouched, baseline preserved).
- Unknown response wording degrades to a verbatim readable row + a stable key — never silence,
  never spam.
- No new poll loops (rides two existing timers); no announcements beyond §4's single form.

## 7. Testing

- `ParseRouteAdvisories`: no-hit sentence (exact + case variants), single block, multi block
  with blank-line separators, unknown free text (verbatim block), null/empty/whitespace.
- `RouteAdvisoryTracker`: first-read baseline silence (even with advisories present), new-key
  detection, unchanged-set silence, persistence across gaps (no Observe calls), clear/reset
  re-baselines silently.
- Weather panel: settings round-trip, default-true, visibility (hidden without AS, independent
  of the auto-announce master), hidden-preserves-value.
- **In-sim (PR checklist, with the honest caveat):** the no-hit path, box rendering, 30 s
  refresh, and toggle are verifiable immediately; the HIT path (a real SIGMET intersecting the
  route) gets its first true verification when weather actually crosses a planned flight —
  stated explicitly so a "worked in testing" claim is never over-read.

## 8. Documentation

`docs/weather.md`: new subsection under the read-only surfaces (route source = AS's loaded
plan via its SimBrief link; no plan push; API-only per Robin's constraint; parser defensiveness
rationale; announce lifecycle). CLAUDE.md: no new invariant line needed beyond weather.md
unless the plan surfaces one (the AS-only-UI-hidden and words-only invariants already cover
the patterns used here).

## 9. Delivery

Same branch/PR (#159). Three implementation commits — (1) client wrapper + parser + tracker
(pure layer), (2) radar section + AutoScroll/height rework, (3) announce path + settings —
plus docs. In-sim items appended to the PR checklist.

## 10. Out of scope (recorded)

- `LoadFlightPlan` push / `.pln` export (fallback for non-SimBrief users — future, if ever).
- Per-waypoint route winds (`activeflightplanwx.txt`) — rejected as too verbose AND file-based.
- Any file read under `%APPDATA%\HiFi` (custom-path fragility; API only).
- Applying the plain-English decode toggle to AS route text (format mismatch; revisit only if
  the raw text proves hard to read in practice).
- Positional `GetActiveSigmetsAt?lat=&lon=` as an aviationweather.gov replacement for the
  Nearby Advisories box (the audit's consistency argument for AS historic/custom modes —
  separate feature, separate conversation).
- Revisit the block/key strategy (per-line advisory detection vs. the current first-line-as-key
  approach) once the first real route hit is observed in the wild and the genuine hit format is
  actually known. Also: the "AS running, no flight plan loaded" response wording needs to be
  captured in-sim and, if it doesn't already start with `"No airmet/sigmet"`, added to the
  parser's no-hit prefixes — otherwise it would parse as an unrecognized hit block and both
  announce once and 15-minute-reminder-re-announce as a phantom advisory.

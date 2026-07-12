# Route Advisory Decoding — Design

**Date:** 2026-07-12
**Status:** Approved by Robin (conversation, 2026-07-12)
**Branch:** `feat/activesky-improvements` (fixes + extends the Route Advisories feature of the
still-open PR #159; see `2026-07-12-route-advisories-design.md` for the base feature)

## 1. Problem

The first-ever live capture of the `/GetActiveSigmetsAt` hit path (2026-07-12, route with
en-route SIGMETs loaded in ActiveSky) revealed that the real response format differs from
what `ActiveSkyFormatting.ParseRouteAdvisories` was defensively written to expect, and that
the raw SIGMET text is unreadable for a screen-reader user:

1. **Advisories are separated by single CRLF, not blank lines.** The parser splits on
   `\r\n\r\n` / `\n\n`, which never fires — the entire response parses as ONE
   `RouteAdvisory` whose key is the first line. Consequences: the Weather Radar box shows
   one undifferentiated wall of text; only the first advisory ever has an announce key, so
   a second advisory in the response never announces separately, and a NEW advisory
   appearing later never announces at all (the first line — the key — doesn't change).
   This is a functional bug in the hit path, not just presentation.
2. **ActiveSky repeats the same advisory** — apparently once per route-segment
   intersection. The live capture contains the identical MHTG SIGMET J5 block SEVEN times,
   followed by one YMMM SIGMET T07 block.
3. **The body is raw ICAO SIGMET-ese** (`EMBD TS OBS AT 1830Z WI N1121 W10027 - … TOP
   FL520 MOV W 05KT NC=`). Read by NVDA this is noise, and the 7-point lat/lon polygon is
   pure noise.

### Live capture (golden reference, 2026-07-12, port 19285)

Each advisory is exactly THREE lines — header, `Valid until:`, body — with single-CRLF
separators and no blank lines (shown here deduplicated; the raw capture repeats block 1
seven times):

```
MHTG SIGMET J5 EMBD TS
Valid until: 2200z
MHCC CENTRAL AMERICAN FIR EMBD TS OBS AT 1830Z WI N1121 W10027 - N1258 W09506 - N1403 W09304- N1127 W09031  - N0950 W09306 - N0923 W09619 - N0904 W09940 TOP FL520 MOV W 05KT NC=
YMMM SIGMET T07 TURB
Valid until: 2300z
YMMM MELBOURNE FIR SEV TURB FCST WI S3640 E14800 - S3340 E15000 - S3410 E15100 - S3740 E14940 - S3820 E14550 - S3730 E14520 SFC/8000FT STNR NC=
```

The full raw capture (with the duplicates) is preserved as the test fixture.

## 2. Decisions (Robin, 2026-07-12)

- **The Weather Radar box follows the existing "Decode advisories into plain English"
  checkbox** (`UserSettings.DecodeWeatherAdvisories`) — the same toggle the Nearby
  Advisories box already honors. Checkbox OFF → original raw text (now properly split into
  blocks and deduplicated). Checkbox ON → a REBUILT plain-English summary per advisory.
- **Decoded form is a rebuilt summary, not in-place expansion.** The coordinate polygon is
  dropped from the decoded view (worthless when read aloud); flipping the checkbox off
  shows it again in the raw text.
- **The auto-announcement is ALWAYS decoded, independent of the checkbox.** The checkbox
  is a Weather-Radar display preference; a spoken announcement must never contain raw
  abbreviations a screen reader mangles ("EMBD TS").
- Rejected alternatives: dictionary word-by-word in-place expansion (can't drop the
  coordinate wall or restructure into a sentence); cross-matching aviationweather.gov's
  structured feed by SIGMET ID (network-dependent and US-centric — the live capture has
  MHCC and YMMM FIRs).

## 3. Design

All new logic is pure and lives in `Services/ActiveSkyFormatting.cs`, following the
existing formatter/tracker pattern; forms and MainForm only pass flags.

### 3.1 Parser fix — `ParseRouteAdvisories`

- A new block starts at any line matching the header shape
  `^\S{3,4}\s+(SIGMET|AIRMET)\s+\S+` (case-insensitive on the type word). Blank lines
  STILL split blocks too — compatibility with the previously assumed format costs nothing.
- After splitting, blocks are **deduplicated by key** (first trimmed line,
  `OrdinalIgnoreCase`) preserving first-seen order — collapses the 7× MHTG J5 to one.
- Defensive fallbacks unchanged: text before the first header line, or a response with no
  header lines at all, stays one verbatim block; the known no-hit sentence (any response
  starting `No airmet/sigmet`, case-insensitive) still parses to an empty list; nothing is
  ever dropped or thrown.
- `RouteAdvisory` keeps `Key` + `Lines` and gains the parsed fields of §3.2 (null/empty
  when not recognized). The **tracker key stays the raw header line** — announce/dedup
  identity is untouched, so `RouteAdvisoryTracker` and its tests do not change.

### 3.2 Body decoder — pure field extraction

From the header and body, extract (each independently optional):

| Field | Source tokens | Decoded |
|---|---|---|
| Identity | header `MHTG SIGMET J5` | "MHTG SIGMET J5" |
| Hazard | qualifier + hazard tokens, extracted from the BODY first (falls back to the header's trailing tokens when the body has none): `EMBD/OCNL/FRQ/SQL/ISOL` × `TS/TSGR`; `SEV/MOD` × `TURB/ICE/ICE (FZRA)/MTW`; `HVY/SEV` × `DS/SS`; `VA` (+`CLD`); `RDOACT CLD` | "embedded thunderstorms", "severe turbulence", … |
| Observed/forecast | `OBS( AT hhmmZ)?`, `FCST( AT hhmmZ)?` | "observed at 1830Z", "forecast" |
| Vertical extent | `TOP FLnnn`, `TOPS?( ABV/BLW)? FLnnn`, `FLnnn/nnn`, `SFC/FLnnn`, `SFC/nnnnFT`, `nnnnFT` | "tops FL520", "surface to 8,000 feet" |
| Movement | `MOV <dir> nnKT` (16-wind compass), `STNR` | "moving west at 5 knots", "stationary" |
| Trend | `NC`, `INTSF`, `WKN` | "no change expected", "intensifying", "weakening" |
| Validity | the `Valid until: hhmmz` line | "Valid until 2200Z." |
| Area polygon | `WI <lat/lon run>` (`[NS]dddd [EW]ddddd` pairs, dash-separated, tolerant of the capture's irregular spacing) | recognized and DROPPED from the summary |

Unknown tokens are ignored for the summary (never spoken); a body yielding NO recognized
fields renders verbatim even when decoding is on — never a blank entry.

Decoded box form per advisory (fields joined in the fixed order above, only those
present):

```
MHTG SIGMET J5: embedded thunderstorms, observed at 1830Z, tops FL520, moving west at 5 knots, no change expected.
Valid until 2200Z.
```

### 3.3 Box rendering — `BuildRouteAdvisoriesText(advisories, decode)`

`decode` comes from `SettingsManager.Current.DecodeWeatherAdvisories`, read by
`WeatherRadarForm.FetchRouteAdvisoriesAsync` at fetch time (the checkbox's
`CheckedChanged` already saves the setting; the next refresh — ≤30 s auto or F5 — picks it
up, same as the Nearby Advisories box). OFF → raw blocks exactly as today, but properly
split with one blank separator row between advisories and deduplicated. ON → the decoded
form, same blank-row separation. Empty list still reads "No advisories on route."

### 3.4 Announcement — always decoded

`BuildRouteAdvisoryAnnouncement(advisory)` returns
`"Route advisory: MHTG SIGMET J5, embedded thunderstorms, tops FL520."` — identity +
hazard + vertical extent only (movement/trend/validity stay box-only; announcements are
interruptions, the box is the briefing). Falls back to the raw key when nothing decodes.
`MainForm.CheckRouteAdvisoriesAsync` announces per fresh KEY as today, but looks up the
advisory by key to build the phrase. Neutral phrasing (no "New") is retained — the same
announce fires as the 15-minute reminder (see `docs/weather.md` §12).

### 3.5 Testing

The live capture (WITH its duplicates) becomes a fixture in `RouteAdvisoriesTests`:

- Parses to exactly 2 advisories (8 raw blocks → dedup), keys `MHTG SIGMET J5 EMBD TS`
  and `YMMM SIGMET T07 TURB`, in that order.
- Decoded summaries pinned for both (YMMM exercises `SEV TURB FCST`, `SFC/8000FT`,
  `STNR`).
- Raw rendering (decode off) round-trips the deduplicated blocks with blank separators.
- Announcement phrases pinned for both; fallback-to-raw-key pinned for an undecodable
  block.
- All existing defensive-fallback and tracker tests keep passing unchanged (blank-line
  splitting still works; unknown free text still renders verbatim; no-hit still empty).

### 3.6 Docs

`docs/weather.md` §12: the hit-path format is now KNOWN (single-CRLF separators,
3-line blocks, per-segment duplicates) — replace the "only partially known" caveats with
the observed format, note the capture date, and document the decode-checkbox gating and
the always-decoded announcement. The §12(f) "hit path never observed" caveat is updated:
the RESPONSE format is now live-verified; the end-to-end announce timing still awaits a
live mid-flight appearance.

## 4. Error handling summary

Every layer degrades to showing raw text rather than hiding data: unparseable response →
one verbatim block (existing rule); unrecognized body with decode on → verbatim block;
undecodable announce → raw key. No exception paths are added; the decoder is pure string
work inside the existing try/catch call sites.

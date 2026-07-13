# Weather (ActiveSky integration, SimConnect fallbacks, METAR)

Weather in MSFS Blind Assist comes from three sources, never blended per-field: **SimConnect**
(`AMBIENT_*` SimVars, always available, source of truth when ActiveSky is off), **HiFi
ActiveSky's local HTTP API** (opt-in, source of truth for wind/precip/turbulence when it's
running), and **VATSIM's public METAR feed** (always-on, independent of both — the METAR
report window's primary source). This doc covers the ActiveSky opt-in gate, the four places
that must keep working with ActiveSky off, wind and precipitation source precedence, the
decoded-weather monitor's lifecycle, and two standing implementation gotchas.

**Key files:**
- `Services/ActiveSkyClient.cs` — HTTP client for the AS local API; owns the central opt-in gate
- `Services/ActiveSkyWeatherMonitor.cs` — background poller that speaks "Weather update…"
- `Services/ActiveSkyFormatting.cs` — pure text builders for the ActiveSky read-only surfaces
  (mode line, temp/dew line, forecast presets, Winds Aloft altitude window, vertical-profile
  narrative) — no I/O, fully characterization-tested
- `Services/ActiveSkyModeTracker.cs` — pure baseline-first decision logic for the mode-change
  announcement
- `Services/RouteAdvisoryTracker.cs` — pure baseline-first decision logic for the en-route
  advisory announce (§12)
- `MainForm.Announcers.cs` — output+I wind (`RequestWindInfo`), the ambient auto-announce
  (`CheckAmbientWeatherChanges` / `AnnounceAmbientChanges`)
- `Forms/WeatherRadarForm.cs` — the on-demand weather radar/briefing window
- `Forms/METARReportForm.cs` — the ICAO METAR lookup window (VATSIM + optional AS section)
- `Settings/UserSettings.cs` — `ActiveSkyEnabled`, `WeatherAutoAnnounceEnabled`,
  `WeatherAutoAnnounceIntervalMinutes`
- `Forms/Settings/WeatherPanel.cs` — the Weather settings tab

## 1. ActiveSky integration is opt-in

`UserSettings.ActiveSkyEnabled` (Weather settings tab, default `false`) is the master switch
for all ActiveSky code. The gate is centralized at the top of
`ActiveSkyClient.IsRunningAsync()`:

```csharp
if (!Settings.SettingsManager.Current.ActiveSkyEnabled)
{
    LastSuccessfulPort = null;
    LastStatus = "disabled in settings";
    return false;
}
```

Nulling `LastSuccessfulPort` here closes an enable → discover → disable leak: without it, a
cached port from a previous enabled session would silently outlive the user turning the
switch off. The five data methods (`GetCurrentConditionsAsync`, `GetWeatherAreaAsync`,
`GetPositionMetarAsync`, `GetClosestStationMetarAsync`, `GetMetarAsync`) each carry their own
`if (!Settings.SettingsManager.Current.ActiveSkyEnabled) return null;` guard as defense in
depth, in case a future call site skips the `IsRunningAsync()` liveness check.

Why the switch exists at all: `IsRunningAsync()`'s parallel port probe
(`CandidatePortList = { 19285, 19286, 19287 }`) has a `ProbeTimeout` of 1.2 seconds. When AS
isn't installed, every candidate probe times out — so before the switch existed, every
non-AS user paid up to ~1.2 s on every output+I press, every radar refresh, and every 60 s
monitor poll. With the switch off, `IsRunningAsync()` returns `false` **instantly** — no probe
is ever attempted.

## 2. SimConnect remains the weather source when the switch is off

This is the invariant the whole feature rests on. Three of the four weather-reading paths
fall back to SimConnect when `ActiveSkyEnabled == false`; the fourth (the METAR report form)
never used SimConnect at all — its always-on primary source is VATSIM, not SimConnect (the
code contradicts a looser "SimConnect on all four paths" framing; see the note after the
table).

| Path | File / method | With the switch off |
|---|---|---|
| output+I wind | `MainForm.Announcers.cs` `RequestWindInfo` → `TryGetActiveSkyConditionsAsync` | `TryGetActiveSkyConditionsAsync` awaits `weatherActiveSky.IsRunningAsync()`, which returns `false` at the gate, so it returns `null`. The `else` branch calls `simConnectManager.RequestWindInfo(...)` → `FormatWindData`. `sourceNotice` ("ActiveSky not responding…") stays empty because it's only set when `ActiveSkyEnabled` is true. |
| Cloud / precipitation / visibility auto-announce | `MainForm.Announcers.cs` `CheckAmbientWeatherChanges` → `AnnounceAmbientChanges` | SimConnect ambient (`simConnectManager.RequestWeatherInfo`) is fetched **unconditionally, first** — every call, AS on or off. `asPrecip` stays `null` (the AS block is skipped when `IsRunningAsync()` returns false), so `AnnounceAmbientChanges` takes the SimConnect `PrecipState`/`PrecipRate` branch. Cloud entry/exit and visibility crossings are **always** SimConnect-sourced in either branch — AS's API doesn't expose in-cloud or a precip bitmask at all. |
| Weather Radar | `Forms/WeatherRadarForm.cs` `FetchAmbientAsync` | SimConnect ambient (`_simConnect.RequestWeatherInfo`) is fetched unconditionally, same as above. `_activeSkyAvailable == false` (set once per `RefreshAsync` from `IsRunningAsync()`) skips the whole AS block, and the method returns `WeatherService.FormatAmbientWeather(simData)` — fully SimConnect. |
| METAR report form (Shift+M-style lookup) | `Forms/METARReportForm.cs` `FetchMETAR` | `asAvailable` (checked once on `Load`) is `false`, so the AS section (`asMetarLabel`/`asMetarTextBox`) never becomes visible and the form stays at its compact 400 px size. The **only** METAR fetched is `VATSIMService.GetMETARAsync(icao)` — this path was never SimConnect, on or off. |

## 3. Wind truth is per-engine

With the switch **on**, output+I reads ActiveSky's ambient wind (`FormatActiveSkyWind` in
`MainForm.Announcers.cs`, reading `Conditions.AmbientWindDirection` / `AmbientWindSpeed`).
The `SurfaceGustSpeed` suffix ("… gusting N") is appended **only when the aircraft is on the
ground** (`_lastOnGround`): AS's `Ambient*` and `Surface*` field groups are independent
quantities — at-altitude vs ground level below the aircraft — so airborne the surface gust
does not belong to the ambient wind being read out (at FL360 it produced "061 at 11 gusting
21", the cruise wind glued to the ground gust six miles below — the 2026-07 fix). The
destination half of the same announcement speaks the gust from the destination METAR's wind
group (`VATSIMService.ParseMETARWind` captures `G##`), which is where an approach-relevant
gust actually comes from. With the switch **off**, SimConnect is authoritative and correct —
full stop.

The two sources can legitimately diverge: AS applies its own wind smoothing/interpolation, so
its ambient wind is not guaranteed to match SimConnect's `AMBIENT WIND DIRECTION`/`VELOCITY`
at the same instant. Neither is "the" truth for both populations — never hard-pick one source
for users who haven't opted into ActiveSky, and never silently fall back to SimConnect for
users who have (that's exactly the "ActiveSky not responding" notice's job — Robin's ruling,
recorded in `docs/design/2026-07-09-activesky-switch-review-fixes-design.md` §D5: the notice
fires on every press when AS is enabled but unreachable, deliberately, because a blind pilot
must never miss a degraded wind readout).

The same per-engine rule extends to the Weather Radar form's Winds Aloft box
(`Forms/WeatherRadarForm.cs` `FetchWindsAloftAsync`). With the switch **on** and AS reachable
(`_activeSkyAvailable == true`), the box calls `ActiveSkyClient.GetAtmosphereAsync` for
sim-truth wind and temperature at each level; if that call comes back empty (AS answered the
earlier liveness probe but this particular endpoint call didn't), the method falls through to
the unchanged Open-Meteo path (`WeatherService.GetWindsAloftAsync`) rather than failing the box
outright. With the switch **off**, Open-Meteo is used directly, exactly as before. Both paths
end with a visible source tag line — `"Source: ActiveSky"` (`ActiveSkyFormatting.
BuildWindsAloftText`) or `"Source: Open-Meteo"` — so the reader always knows which engine
answered. The altitude window itself — ±5,000 ft of the aircraft in 1,000-ft steps, clamped at
0 — must stay identical between the two sources: `ActiveSkyFormatting.WindsAloftAltitudes`
(`Services/ActiveSkyFormatting.cs`) mirrors the window `WeatherService.ParseWindsAloft` builds
for the Open-Meteo path, so switching source never changes which levels the pilot hears — only
who answered for them.

## 4. Precipitation source precedence

Three separate readouts decode precipitation from a METAR: the Weather Radar
(`WeatherRadarForm.ParsePrecipFromMetar`), the ActiveSky decoded-weather monitor
(`ActiveSkyWeatherMonitor` via the `WeatherRadarFormPrecipShim` copy — see §7), and the
ambient auto-announce (`MainForm.Announcers.cs` `CheckAmbientWeatherChanges`). They do **not**
all share one tier count — each has a different number of fallback steps — but they all obey
the same underlying rule: **prefer the closest-station METAR before the position METAR**, so
that when both a closest-station report and a position report exist, all three readouts pick
the same one and never contradict each other.

**Weather Radar** (`WeatherRadarForm.FormatAmbientFromActiveSky`, `Forms/WeatherRadarForm.cs`)
has four tiers:

1. Closest-station METAR (`ActiveSkyClient.GetClosestStationMetarAsync`) — the real nearest
   reporting station.
2. The conditions-JSON `ClosestMetar` field — a second, belt-and-braces attempt at the same
   closest-station report when the dedicated endpoint call in tier 1 came back empty, before
   the radar gives up on "closest station" entirely.
3. Position METAR (`GetPositionMetarAsync`, AS's `@POS` interpolated point weather) —
   fallback when both closest-station attempts fail.
4. SimConnect `AMBIENT_PRECIP_STATE`/`AMBIENT_PRECIP_RATE` bitmask — last resort, only reached
   when ActiveSky isn't running at all (see §2's table; under ActiveSky the bitmask is known
   to stick, e.g. reported stuck on "extreme snow").

**Alt+W ambient auto-announce** (`MainForm.Announcers.cs`, `CheckAmbientWeatherChanges` /
`AnnounceAmbientChanges`) has three tiers: closest-station METAR, then (if that's empty)
position METAR, then — only if AS isn't running or both METAR fetches came back empty, leaving
`asPrecip == null` — the same SimConnect bitmask branch.

**The decoded-weather monitor** (`ActiveSkyWeatherMonitor.BuildAnnouncement`) has only **two**
tiers and no SimConnect branch at all: closest-station METAR, else position METAR. There is no
third tier here and there cannot be one — `OnTickAsync` early-returns before `BuildAnnouncement`
is ever called, both when ActiveSky isn't running (§5) and when the position METAR fetch itself
came back empty, so the monitor never reaches a state where it would need to fall back past
"position METAR." When ActiveSky isn't running, the monitor doesn't announce a SimConnect-
sourced precip line — it stays silent and resets its baseline (§5). This matches §5 below: there
is no SimConnect fallback for decoded station weather.

CLAUDE.md's weather invariant states this as three tiers (closest-station METAR → position
METAR → SimConnect bitmask). That's a simplification aimed at the general "prefer closest
station" rule, not a literal count for every readout: it's exact for the ambient auto-announce;
for the Weather Radar it collapses tiers 1 and 2 into one "closest-station METAR" step; and for
the decoded-weather monitor the third (SimConnect) tier doesn't exist — the monitor's fallback
chain stops at position METAR.

A METAR that parses with **no** weather-phenomenon token means "no precipitation" and must
render as `"None"` — it must never fall through to the next source in the list. Only a
wholly-missing METAR (the fetch itself failed or returned empty/whitespace) triggers
fallback to the next source. `WeatherRadarForm.FormatAmbientFromActiveSky` implements this
exactly: each of its three METAR-based branches does
`string.IsNullOrEmpty(parsed) ? "None" : parsed` — never `continue`/fall-through on an empty
parse.

## 5. The decoded-weather monitor lifecycle

`ActiveSkyWeatherMonitor` (the background poller that speaks *"Active sky weather updated…"*)
is constructed unconditionally in `MainForm.InitializeManagers`, so the settings dialog can
start or stop it live without reconstructing it. Whether it actually polls is governed by one
static predicate, `ActiveSkyWeatherMonitor.ShouldRun`:

```csharp
public static bool ShouldRun(Settings.UserSettings settings)
    => settings.ActiveSkyEnabled && settings.WeatherAutoAnnounceEnabled;
```

Both flags are required because the monitor is simultaneously an ActiveSky feature (it reads
only the AS HTTP API — there is **no** SimConnect fallback for decoded station weather; if AS
isn't running, the tick just resets its baseline and returns) and an announcement feature (it
speaks unprompted). `ShouldRun` has three call sites, not one. Two gate the monitor's own
`Enabled` state: `MainForm.InitializeManagers` at launch
(`activeSkyWeatherMonitor.Enabled = ActiveSkyWeatherMonitor.ShouldRun(...)`, `MainForm.cs:602`)
and `MainForm.MenuHandlers.ApplyRuntimeSettings` on live settings save
(`MainForm.MenuHandlers.cs:113`). The third, `WeatherPanel.UpdateActiveSkyDependentVisibility`
(`Forms/Settings/WeatherPanel.cs:61`), gates something different — not the monitor's `Enabled`
flag, but whether the announcement-interval combo is visible and in the NVDA tab order. It
builds a throwaway `UserSettings` from the two live checkbox states (the panel has no
committed `UserSettings` at `CheckedChanged` time) and calls `ShouldRun` on it, because a
setting that governs nothing shouldn't be reachable by a blind user tabbing the panel. All
three call `ShouldRun` rather than restating `enabled && autoAnnounce` inline, so the
both-flags rule has exactly one definition and none of the three sites can drift from it.

`CheckAmbientWeatherChanges` (§2's cloud/precip/visibility auto-announce) is a **different**
feature on a **different** timer (`weatherAnnouncementTimer`, ticked from
`WeatherAnnouncementTimer_Tick`), gated on `WeatherAutoAnnounceEnabled` alone — it doesn't
consult `ActiveSkyEnabled` at all, because it always has a SimConnect fallback path (§2).
Don't confuse the two: a user can have ambient cloud/precip/visibility announcements without
ever touching ActiveSky, but the decoded-AS-weather monitor needs both switches.

**Known behavior consequence:** an existing user who had spoken weather updates before this
gate change but never ticked "Auto-announce weather state changes" will go silent after
upgrading. That's the intended decoupling (the AS switch alone must never start the spoken
weather updates), not a regression.

That invariant is scoped to the `ActiveSkyWeatherMonitor`'s decoded-weather updates
(`ShouldRun`, above) — it does not extend to every AS-gated announcer. The route-advisory
announce (§12d) is DELIBERATELY independent of the "Auto-announce weather state changes" master
(spec 2026-07-12 §5) and is gated on its own default-on sub-toggle
(`AnnounceRouteAdvisoriesEnabled`) plus the AS switch alone, the same shape as the SIGMET/PIREP
proximity toggles it sits beside. Do not "fix" it onto the master gate — that would be reverting
a deliberate design choice, not correcting a bug.

## 6. The no-repeat rule for the precip auto-announce

`AnnounceAmbientChanges`'s ActiveSky branch must not repeat an unchanged phrase. It compares
the decoded precip phrase **trimmed and case-insensitive**:

```csharp
string cur = asPrecip.Trim();
if (_prevAsPrecip != null && !string.Equals(cur, _prevAsPrecip, StringComparison.OrdinalIgnoreCase))
{
    ...
}
```

It speaks only on a genuine start (`""` → non-empty: `"Precipitation started: {cur}"`), a
genuine stop (non-empty → `""`: `"Precipitation stopped"`), or a genuinely different phrase
(`"Precipitation now {cur}"`). An unchanged phrase — e.g. `"light rain"` → `"light rain"` —
stays silent even though the poll fired. The SimConnect (non-AS) branch uses a parallel but
distinct mechanism: `IntensityTier(rate)` buckets the continuous `PrecipRate` into
light/moderate/heavy/extreme and only announces on a tier change, since the raw SimConnect
rate never repeats byte-for-byte the way a decoded METAR phrase can.

## 7. The decoder duplication

`WeatherRadarForm.ParsePrecipFromMetar` (internal static, `Forms/WeatherRadarForm.cs`) is the
canonical WMO/ICAO weather-token decoder — it walks METAR tokens for intensity prefix
(`-`/`+`/`VC`), descriptor (`TS`/`SH`/`FZ`/`BL`/`DR`/`MI`/`BC`/`PR`), and phenomenon
(`RA`/`SN`/`GR`/`GS`/`PL`/`IC`/`UP`/`DZ`/`SG`), and renders e.g. `"light rain"` or
`"thunderstorm with heavy rain"`.

`ActiveSkyWeatherMonitor` (namespace `MSFSBlindAssist.Services`) doesn't call it directly.
The obstacle isn't access level — `ParsePrecipFromMetar` is `internal static`
(`Forms/WeatherRadarForm.cs:444`), which is visible from anywhere in the same assembly,
`Services` included. The obstacle is layering: `Forms/WeatherRadarForm.cs` already
`using`s `MSFSBlindAssist.Services`, so a `Services` class calling back into a `Forms` class
would create a `Services → Forms` dependency the codebase doesn't want alongside the existing
`Forms → Services` one. So `Services/ActiveSkyWeatherMonitor.cs` hosts a byte-for-byte
duplicate as `WeatherRadarFormPrecipShim.ParsePrecipFromMetar` — same token classification,
same output strings. **Do not move the decoder out of `WeatherRadarForm` without updating the
shim**, and do not change one copy's phenomenon/descriptor table without mirroring the change
in the other — a drift here would make the radar, the decoded-weather monitor, and the ambient
auto-announce (which also calls the shim, via `MSFSBlindAssist.Services.WeatherRadarFormPrecipShim`
in `CheckAmbientWeatherChanges`) disagree about the same METAR.

## 8. Two standing gotchas

- **Never scan `%APPDATA%\HiFi\` for the ActiveSky settings-file port.** `ActiveSkyClient`
  hard-codes `CandidatePortList = { 19285, 19286, 19287 }` and probes them in parallel instead
  of reading AS's settings file for its configured port. Recursive directory enumeration under
  `%APPDATA%\HiFi\` was tried first and caused multi-second UI-thread hangs — AS keeps
  gigabytes of weather logs/history in subdirectories there.
- **Turbulence ≤ 25 is the AS calm-weather baseline and must be hidden entirely, never shown
  as a raw number.** `WeatherRadarForm.CategorizeTurbulence` maps AS's 1–100 turbulence value
  to `null` (suppress the line) at ≤ 25, then `"light"` (26–50), `"moderate"` (51–75),
  `"severe"` (76–90), `"extreme"` (91+) — following FAA AIM 7-1-23 phraseology. AS sits at
  ~25 in genuinely calm conditions as numerical/atmospheric baseline noise; showing "25/100"
  reads as alarming when nothing is actually happening.

## 9. ActiveSky read-only surfaces (2026-07)

Design doc: `docs/design/2026-07-10-activesky-improvements-design.md`. Four read-only additions
on top of the surfaces above — a mode readout, a vertical weather profile, an on-demand
closest-station readout, and a forecast METAR combo — all gated
behind the same `ActiveSkyEnabled` opt-in and, on the Weather Radar form, hidden entirely
(`Visible = false`, out of the NVDA tab order) rather than shown with disabled/placeholder text
when the switch is off.

**(a) Mode status line + mode-change announce.** `ActiveSkyClient.ProbePortAsync` (`Services/
ActiveSkyClient.cs`) — the same GET that already runs for every liveness probe — captures the
`/GetMode` response body into `LastModeText` for free (zero extra requests). `ActiveSkyFormatting
.ParseModeText` (`Services/ActiveSkyFormatting.cs`) strips the trailing `"(Active)"`/
`"(Inactive)"` marker and the `"(...z)"` weather-clock group out of that raw text; `FormatModeLine`
renders it as `"ActiveSky: Live Real time mode, weather time 1935Z"`. `WeatherRadarForm.
RefreshAsync` (`Forms/WeatherRadarForm.cs`) shows this in `_asModeBox` at the top of the form — a
**read-only TextBox, deliberately not a Label**: labels have no tab stop, so a screen-reader user
could never reach the mode line by keyboard (Robin's 2026-07-11 review finding). It substitutes
`"ActiveSky: {LastStatus}"` when AS is enabled but unreachable, and hides the box entirely when
the switch is off.

The *spoken* side is separate: `ActiveSkyModeTracker.Observe` (`Services/
ActiveSkyModeTracker.cs`) is pure decision logic wired into `ActiveSkyWeatherMonitor.OnTickAsync`
(`Services/ActiveSkyWeatherMonitor.cs`) and follows four rules. **Baseline-first** — the first
successful `Observe` call only records `_baselineMode` and returns null; nothing is ever spoken
about the mode the app happened to find AS in. **Silent on connect** — because of the
baseline-first rule, launching the app (or AS) never produces a mode announcement, only a later
genuine change does. **The baseline survives unreachable gaps** — when `IsRunningAsync()` fails
mid-tick, `OnTickAsync` resets its OWN change-detection state (`_lastTimeStamp`,
`_hasBaseline`, `_lastAnnouncedAt`) but deliberately does **not** touch `_modeTracker`, so if AS
comes back in a *different* mode than the one last observed, that difference still announces —
a pilot needs to hear that AS restarted into, say, Custom static mode, even though the monitor's
own weather-refresh baseline was wiped by the gap. **Gated by `ShouldRun`** — the mode-change
announce only fires while the monitor is polling at all, i.e. `ActiveSkyEnabled &&
WeatherAutoAnnounceEnabled` (§5); with auto-announce off, the mode is still readable on demand
via the Weather Radar form's status line, just never spoken unprompted.

**(b) Closest-station box shares one code path with the auto-announce.** `WeatherRadarForm.
FetchAmbientAsync` (`Forms/WeatherRadarForm.cs`) populates `_stationBox` by calling
`ActiveSkyWeatherMonitor.BuildDecodedWeatherText` (`Services/ActiveSkyWeatherMonitor.cs`) —
the exact same internal static that `BuildAnnouncement` calls to build the *"Active sky weather
updated…"* auto-announce — with the raw METAR appended underneath. There is deliberately no
second decoder: on-demand and unprompted readouts of the same station must say the same thing
in the same words, and any wording change to `BuildDecodedWeatherText` reaches both surfaces at
once rather than needing to be kept in sync by hand (unlike the precipitation-token decoder in
§7, which genuinely does have to be duplicated for layering reasons — this one doesn't, because
both callers already live in `Services`).

**Current-position temperature/dew point line (a related read-only addition, sharing the
decoder used in (b)).** `ActiveSkyFormatting.BuildTempDewLine` (`Services/
ActiveSkyFormatting.cs`) decodes the already-fetched position METAR into `"Temperature/dew
point: 36 / 12°C"`, appended to `WeatherRadarForm.FormatAmbientFromActiveSky`'s current-position
block (`Forms/WeatherRadarForm.cs`) — the dew point has no other source anywhere in the app (no
SimConnect dew-point SimVar, no dew field in the AS JSON conditions block). `ActiveSkyWeatherMonitor
.DecodeMetar` (`Services/ActiveSkyWeatherMonitor.cs`) yields temperature and dew point strictly
together — both-or-neither, since a METAR reports them as one `TT/DD` group token — so
`BuildTempDewLine` renders the line only when **both** `TemperatureC` and `DewPointC` are
present, and returns null (no line at all) otherwise. The original design doc's
"dew point missing from the METAR → the line shows temperature only" fallback was proven
unreachable during implementation — the decoder never produces temperature without dew point —
and was deliberately not built (see the implementation note appended to `docs/design/
2026-07-10-activesky-improvements-design.md` §4.1 item 2).

**(c) Vertical profile: enum conventions and curation.** `ActiveSkyClient.GetWeatherInfoXmlAsync`
/ `ParseWeatherInfoXml` (`Services/ActiveSkyClient.cs`) parse `/GetWeatherInfoXml` into a
`VerticalProfile` of `ProfileWindLayer`/`ProfileCloudLayer` records. Three enum conventions to
remember when touching this code: **severity is 0-4** (FSX-style: 1 light, 2 moderate, 3 heavy,
4 severe; 0/unknown = no phrase) for `TurbulenceEnum` and `IcingEnum` on both layer types — this
is a *different* scale from the JSON `Conditions.AmbientTurbulence` 0-100 value used elsewhere
in the file, so `ActiveSkyFormatting.SeverityWord` must never be fed a 0-100 number or vice
versa; **cloud coverage is oktas** (1-2 few, 3-4 scattered, 5-7 broken, 8 overcast; anything
else is not a reportable layer), decoded by `CoverageWord`; **metres→feet conversion applies
only to cloud base/top** (`ParseWeatherInfoXml` converts `CloudBaseMeters`/`CloudTopMeters` ×
3.28084 at parse time) — wind-layer altitudes arrive from the XML's `AltFeet` attribute already
in feet and must not be converted again. `PrecipWord` maps `PrecipType` 0/1/2 to none/rain/snow.

`ActiveSkyFormatting.BuildProfileNarrative` assembles the spoken text: every cloud layer with
base/top/coverage, plus icing/precip/turbulence phrases only when present, ordered by base
altitude ascending; then winds/temps at the layer nearest each of six standard levels (surface,
5,000, 10,000, 18,000, 24,000, 34,000 ft) **plus** the layer nearest the aircraft's current
altitude, deduplicated and re-sorted ascending by `SelectCuratedLevels` — a full 13-layer dump
would bury the levels that actually matter to the pilot in ones that don't. `WeatherRadarForm.
FetchProfileAsync` (`Forms/WeatherRadarForm.cs`) is AS-only by construction: it returns
`"unavailable"` immediately when `_activeSkyAvailable != true`, with no SimConnect fallback —
there is no vertical-profile data source outside ActiveSky.

**(d) Forecast combo.** `ActiveSkyFormatting.ForecastPresets` (`Services/
ActiveSkyFormatting.cs`) is the fixed offset table — a full hourly ladder, Now through
+6 hours, i.e. 0/3600/7200/10800/14400/18000/21600 seconds — consumed both to populate `METARReportForm`'s
`forecastCombo` (`Forms/METARReportForm.cs`) and, in `FetchMETAR`, as the `timeoffset` argument
to `ActiveSkyClient.GetMetarAsync`. The combo sits BELOW the AS METAR box, last in the tab order
before Close (Robin's 2026-07-12 review), so Tab/Shift+Tab hops directly between the AS text
and the offset selector. `BuildAsMetarCaption` renders the AS METAR box's label so it
always states which offset is showing — `"ActiveSky METAR:"` at the `Now` preset,
`"ActiveSky METAR (+4 hours):"` otherwise — so a screen-reader user tabbing between the label
and the box never has to guess which forecast they're reading. The **VATSIM box is always
current**: `FetchMETAR`'s `vatsimTask` calls `VATSIMService.GetMETARAsync(icao)` with no offset
parameter at all, regardless of the combo's selection — VATSIM has no forecast concept, and the
combo only ever parameterizes the AS fetch. Both the forecast combo and the AS METAR section
share one visibility flag (`asMetarTextBox.Visible`, set once on `Load` from
`_activeSky.IsRunningAsync()`), so when AS is off or unreachable at open time the form reverts
to its original compact VATSIM-only layout.

A fifth surface — an `ActiveSkyClient.LastStatus` line in the Weather settings panel — shipped
on this branch and was then REMOVED on Robin's 2026-07-13 review: a connection-status readout
doesn't belong in the settings panel at all. `LastStatus` remains surfaced where it is useful:
the Weather Radar form's mode box shows `"ActiveSky: {LastStatus}"` whenever AS is enabled but
unreachable (§9a). Don't re-add a status line to `WeatherPanel` — the panel is deliberately
pure over its `LoadFrom`/`ApplyTo` arguments (no `SettingsManager.Current` reads, no probes),
which also keeps `WeatherPanelTests` hermetic with no shared-state collection.

## 10. Hazard announcements: turbulence and icing (2026-07)

Design doc: `docs/design/2026-07-11-hazard-announcements-design.md`. Two new unprompted
announcers ride the existing 60-second `ActiveSkyWeatherMonitor` tick and the existing
30-second ambient tick respectively — no new poll loops, no new HTTP traffic. Both follow the
`ActiveSkyModeTracker` pattern (§9a): an `internal sealed` pure tracker class with a
`string? Observe(...)` method, fully characterization-tested
(`tests/MSFSBlindAssist.Tests/TurbulenceCategoryTrackerTests.cs`,
`IceAccretionTrackerTests.cs`).

**(a) Turbulence — `Services/TurbulenceCategoryTracker.cs`.** Data source is
`Conditions.AmbientTurbulence` (AS's 1–100 value), read once per `ActiveSkyWeatherMonitor
.OnTickAsync` (`Services/ActiveSkyWeatherMonitor.cs`) right after the conditions fetch
succeeds — before the weather-refresh/throttle logic, so a category change is never absorbed
by the "unchanged weather" or interval-throttle branches. Category boundaries are copied
**verbatim** from the Weather Radar's `CategorizeTurbulence` (§8): ≤25 smooth, ≤50 light, ≤75
moderate, ≤90 severe, >90 extreme — the same FAA AIM 7-1-23 wording, so the spoken category
always matches what the radar form's Turbulence line would show for the same value.

Rising transitions happen **at the boundary** (>25 enters light, >50 enters moderate, …).
Easing requires the value to clear the boundary it's re-crossing by a **5-point** hysteresis
margin, so a value oscillating on a boundary never flaps: light→smooth needs ≤20, moderate→
light needs ≤45, severe→moderate needs ≤70, extreme→severe needs ≤85. All four are concrete
literals in `CategoryWithHysteresis`/`LowerBoundaries`, not a computed percentage — don't
"simplify" them into a single formula.

Four utterance forms, all **words only** — the raw 1–100 number is never spoken, and smooth
(≤25) is never named as a category, matching §8's "hidden entirely" rule for the raw value:

| Transition | Utterance |
|---|---|
| smooth → any category | `"Entering {category} turbulence"` |
| worsening between categories | `"Turbulence now {category}"` |
| easing between categories (not to smooth) | `"Turbulence easing to {category}"` |
| any category → smooth | `"Smooth air"` |

Baseline-first: the first successful `Observe` call only records the starting category and
returns null — nothing is spoken about the turbulence the app happened to find on connect. The
baseline **survives AS-unreachable gaps**: `OnTickAsync`'s early-return path (AS not detected)
resets the monitor's own refresh-detection state but never calls `Reset()` on the turbulence
tracker, so a genuine category change that happened while AS was unreachable still announces
once AS comes back. `Reset()` is called only on aircraft switch
(`MainForm.AircraftSwitch.cs SwitchAircraft`, via `activeSkyWeatherMonitor
?.ResetTurbulenceTracker()`) — a stale category from the previous airframe must not read as a
"change" on the new one. Gated on `AnnounceTurbulenceEnabled`, checked per tick (no
`ApplyRuntimeSettings` wiring needed) and — because the data source is AS-only — implicitly on
`ActiveSkyWeatherMonitor.ShouldRun` (§5): with AS off or auto-announce off, the tick that would
call `Observe` never runs at all.

**(b) Icing — `Services/IceAccretionTracker.cs`.** Data source is the stock `STRUCTURAL ICE
PCT` SimVar, engine-independent airframe ice accretion as a 0..1 ratio (not a percent — the
SimConnect unit string is `"percent over 100"`). It's registered as datum 7 on the existing
ambient weather struct (`SimConnectManager.Setup.cs`):

```csharp
sc.AddToDataDefinition(DATA_DEFINITIONS.WEATHER_DATA, "STRUCTURAL ICE PCT", "percent over 100",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)7);
```

landing in `AmbientWeatherData.StructuralIcePct` (`SimConnect/SimConnectManager.cs`) and
arriving on the same 30-second `RequestWeatherInfo` ambient tick §2's table describes — the
same 3-second timeout/null-skip protection against a stalled sim applies, so no separate
polling was added. `AnnounceAmbientChanges` (`MainForm.Announcers.cs`) clamps a `NaN` or
negative sample to 0 before calling `Observe`, so a bad read can't corrupt the tracker's
hysteresis state.

Thresholds are **copied verbatim from the FBW A380's sim-verified constants**
(`FlyByWireA380Definition.cs`, `ICING_DETECT_RATIO`/`ICING_CLEAR_RATIO`) — 0.05 rising, 0.02
falling, same as the A380's own ice-stick debounce logic
(`FlyByWireA380Definition.SimVarUpdate.cs`). Rising edge (ratio ≥ 0.05 while not already
icing) speaks `"Icing conditions, ice accumulating"`; falling edge (ratio ≤ 0.02 while icing)
speaks `"Icing conditions cleared"`; the 0.02–0.05 band is a dead zone, deliberately not a
single crossing value, so a ratio hovering near the threshold doesn't flap. First sample is
baseline-silenced — an app connecting with ice already on the airframe adopts that state
without announcing it as a "change." `Reset()` fires on both SimConnect connect
(`MainForm.AircraftSwitch.cs OnConnectionStatusChanged`) and aircraft switch (`SwitchAircraft`)
— a reconnect or airframe swap invalidates any accumulated ice state, so the next sample
re-baselines silently rather than announcing a spurious edge.

**(c) The `HasOwnIcingAnnouncer` yield.** The FBW A380 already had a tuned, sim-verified
ice-stick announcer (`FlyByWireA380Definition.SimVarUpdate.cs`, reading
`A32NX_ICING_STATE_ICING_STICK_INDICATOR` with the same 0.05/0.02 hysteresis, speaking
`"Icing conditions"` / `"Icing conditions cleared"` — note the wording differs slightly from
the generic tracker's onset phrase). `IAircraftDefinition.HasOwnIcingAnnouncer` (default
`false` on `BaseAircraftDefinition`, overridden `true` on `FlyByWireA380Definition`) lets the
generic announcer detect this and skip itself **entirely** for that aircraft —
`AnnounceAmbientChanges` gates the whole `STRUCTURAL ICE PCT` branch on
`currentAircraft?.HasOwnIcingAnnouncer != true`, not merely muted — so one icing episode on the
A380 is never spoken by two voices, the same one-condition-one-call-out rule as the documented
PB-light/ECAM-memo invariant (see CLAUDE.md's A380X section). The A380's own announcer is
deliberately **not** gated on the new `AnnounceIcingEnabled` setting — it predates the setting
and is aircraft-curated; turning the new toggle off silences the generic tracker for every
other aircraft but leaves the A380's own voice untouched. (Making the A380 announcer also
respect the toggle is a recorded one-line follow-up, not done here.)

**(d) Settings.** Two new `UserSettings` bools (`Settings/UserSettings.cs`), both default
`true`: `AnnounceTurbulenceEnabled` and `AnnounceIcingEnabled`. Both live in the Weather
settings tab's Announcements group (`Forms/Settings/WeatherPanel.cs`), directly under the
master "Auto-announce weather state changes" checkbox, and both round-trip via
`LoadFrom`/`ApplyTo` unconditionally — hiding a checkbox never resets its stored value, the
same rule the announcement-interval combo already follows (§5).

Visibility is computed in `UpdateActiveSkyDependentVisibility`:

```csharp
bool master = _weatherAutoAnnounce.Checked;
_announceTurbulence.Visible = master && _activeSkyEnabled.Checked;
_announceIcing.Visible = master;
```

"Announce turbulence changes" needs **both** the master auto-announce switch and ActiveSky
enabled — the data source is AS-only, so the checkbox is meaningless (and hidden, out of the
NVDA tab order) without AS. "Announce icing" needs only the master switch — `STRUCTURAL ICE
PCT` is a stock SimVar with no ActiveSky dependency, so it stays reachable with AS off. Neither
checkbox appears at all with the master auto-announce switch off, matching every other
sub-toggle on this tab.

## 11. Weather Radar: ListBox conversion + live refresh (2026-07)

Design doc: `docs/design/2026-07-12-weather-radar-listbox-design.md`. This completes, for the
Weather Radar window (Shift+R, `Forms/WeatherRadarForm.cs`), the 2026-07-02 "Live-Display
Consistency Pass" that had already converted every other live text display in the app (E/WD,
OANS, RMP, HS787 display + EICAS, GSX menu, ECL, all MCDU/CDU/DCDU forms, MainForm's status
display — see `docs/a32nx.md`'s "single reconcile home" list). The radar's five multi-line
readouts — `_currentWeatherBox`, `_stationBox`, `_profileBox`, `_advisoriesBox`,
`_windsAloftBox` — are `DisplayListBox` rows reconciled through `DisplayList.UpdateInPlace`
(rewrites only the rows whose text changed, grows/shrinks the tail in place, never
`Items.Clear()`, restores the reading cursor by ROW CONTENT nearest the old index, no-ops on
unchanged content). Every write site is `_box.SetText(value)`, splitting on `\r\n`/`\n` with
`StringSplitOptions.None` so blank separator rows and `─`-rule rows survive as their own items —
one fact/advisory/wind level per row, exactly as `UpdateInPlace`'s duplicate-row matching
already assumes elsewhere. The text-building code itself (`FormatAmbientFromActiveSky`,
`BuildProfileNarrative`, `BuildWindsAloftText`, the advisories builder) is byte-identical; only
the sink changed.

The window now auto-refreshes every **30 seconds** via a `System.Windows.Forms.Timer`
(`_autoRefreshTimer`), calling `RefreshAsync(forceRefresh: false)` — the same call the pass's
other pop-outs use. `forceRefresh: false` lets the internet-backed fetches (SIGMET/PIREP
advisories, Open-Meteo winds aloft) keep serving from their existing TTL caches; the cheap
AS/SimConnect fetches (ambient, station, profile, AS winds, mode line, position) re-run every
tick, so the boxes track the flight without the pilot ever touching F5. This is safe specifically
*because* the readouts are `DisplayListBox` rows: an unchanged tick reconciles to a no-op, and a
changed one updates in place without moving the reading cursor — the TextBox form of this window
could never have auto-refreshed without resetting the NVDA position on every tick. F5 and the
Refresh button still pass `forceRefresh: true` for an immediate, uncached pull.

`_autoRefreshTimer` carries the same `IsDisposed` guards as `FlyByWireDcduForm`'s poll timer
(the precedent recorded in `docs/a32nx.md` §"2026-06-12 bug-pass hardening"): the timer is
created and started only `if (!IsDisposed)` after the initial `RefreshAsync(forceRefresh: true)`
await returns (the form can be closed while that first fetch is in flight), and its `Tick`
handler re-checks `IsDisposed` before firing another refresh. Skipping the creation/`Start()`
guard reproduces the DCDU's zombie-timer bug — starting a disposed WinForms timer silently
re-creates its native timer with no cleanup path ever stopping it; the Tick-body guard is
belt-and-braces against one late queued tick. `OnFormClosed` and `Dispose(bool)`
both stop and null the timer, belt-and-braces (the radar closes fully rather than hiding — the
hide-on-close pattern belongs to the A380 RMP, not here — and the guard shape is identical to
the DCDU's).

The single-line `_asModeBox` (ActiveSky mode status) is deliberately **not** part of this
conversion — it stays the read-only TextBox from the 2026-07-11 keyboard-reachability fix (§9a):
a one-line list adds nothing, and that box already has its own carve-out from the original
consistency pass.

**The Refresh button is deliberately never disabled**, even mid-fetch: `RefreshAsync` used to
set `_refreshButton.Enabled = false` for the duration of a fetch, but WinForms moves focus off a
disabled control automatically — with a 30 s timer now running unconditionally, that would steal
focus from a user resting on the Refresh button every single tick. The `Enabled` toggle (both
the disable and the `finally` re-enable) was removed entirely; the pre-existing `_isFetching`
guard already makes a mid-fetch click or tick a harmless no-op.

## 12. En-route advisories (ActiveSky route SIGMETs/AIRMETs) (2026-07)

Design doc: `docs/design/2026-07-12-route-advisories-design.md`. A read-only Weather Radar
addition plus a background announcer, both riding `ActiveSkyClient.GetRouteAdvisoriesTextAsync()`
— a parameterless `GET /GetActiveSigmetsAt` that answers for whatever flight plan is currently
**loaded in ActiveSky itself**, not for the aircraft's proximity like the existing SIGMET/AIRMET
box (the Nearby Advisories box, which stays position+range and aviationweather.gov-sourced —
§13 records that decision and its live-probe evidence).

**(a) Route source and the API-only constraint.** The 2026-07-10 audit had assumed this needed
MSFSBA to export a `.pln` and push it via `LoadFlightPlan`. Live verification on 2026-07-12
(OMDB→LTFM en route at FL358) proved that unnecessary for SimBrief-linked setups: ActiveSky's
own SimBrief downloader already satisfies AS's "loaded flight plan" requirement, so the bare
parameterless call answered authoritatively for the current route (`"No airmet/sigmet affecting
currently loaded flight plan route"`). MSFSBA pushes no plan and reads no file — the route is
whatever AS itself has loaded, kept current by AS's own SimBrief link (or by the user loading a
plan in AS directly). This is Robin's explicit constraint, not just a simplification: no `.pln`
export, and no reading `activeflightplanwx.txt` or any other file under `%APPDATA%\HiFi\` (a
custom AS install path would silently break a file-based read, and §8 already forbids scanning
that tree for other reasons) — everything comes from the HTTP API, same as every other AS
surface in this doc. `LoadFlightPlan` push remains a possible future fallback for non-SimBrief
users; it is out of scope here (design doc §10).

**(b) The parser is deliberately defensive.** `ActiveSkyFormatting.ParseRouteAdvisories`
(`Services/ActiveSkyFormatting.cs`) is pure and CI-tested. A response beginning with
`"No airmet/sigmet"` (case-insensitive) — the only no-hit shape actually observed during the
2026-07-12 audit — parses to an empty list.

The hit format is now KNOWN (live capture 2026-07-12): each advisory is exactly three
lines — header (`MHTG SIGMET J5 EMBD TS`), `Valid until: 2200z`, and the raw SIGMET
body — separated by single CRLF with NO blank lines, and ActiveSky repeats the same
advisory once per route-segment intersection (the capture carried the identical MHTG
J5 block seven times). `ParseRouteAdvisories` therefore splits on header lines
(`^\S{3,4}\s+(SIGMET|AIRMET)\s+\S+`, case-insensitive) and DEDUPLICATES by key
(first line, case-insensitive, first-seen order) into one `RouteAdvisory { Key, Lines }` per
distinct advisory. The defensive fallbacks remain: truly-blank lines still split,
unrecognized text still renders verbatim as its own block (with its own first line standing
in as the key), and the "No airmet/sigmet" sentence still parses to an empty list — so the
parser never drops or throws on anything it doesn't recognize, one readable, self-explanatory
row rather than silence or a crash.

Each parsed `RouteAdvisory` also carries decoded fields (`Identity`, `Hazard`, `ObsFcst`,
`VerticalExtent`, `Movement`, `Trend`, `ValidUntil`), extracted from `Lines` by pure regexes;
the `WI` lat/lon polygon is deliberately never extracted (see (c)). `ActiveSkyFormatting.BuildRouteAdvisoriesText`
is the matching renderer for the radar box (see (c)); the tracker in (d) works from the same
`Key` list.

**(c) Weather Radar box.** A new `"Route Advisories (ActiveSky):"` label + `_routeAdvisoriesBox`
(`DisplayListBox`) sits between Vertical Profile and Nearby Advisories in `WeatherRadarForm`
(`Forms/WeatherRadarForm.cs`), fetched by `FetchRouteAdvisoriesAsync` inside `RefreshAsync`'s
parallel batch — four-way before this feature, five-way with this addition — so it rides the
form's 30 s auto-refresh (§11) with no new poll loop. Three fetch outcomes (plus the visibility
rule), matching every other AS-only surface in this doc:

- Advisories present, decode checkbox off → each deduplicated block's lines as rows, one blank
  row between blocks (raw AS text, unmodified).
- Advisories present, decode checkbox on → each advisory renders instead as its rebuilt
  plain-English summary (see **Decode gating** below).
- No hit → `BuildRouteAdvisoriesText` renders the single sentence `"No advisories on route."`
  (never the raw AS wording, and never silence — a clear route is itself a fact worth reading).
- AS enabled but the fetch failed (`GetRouteAdvisoriesTextAsync` returned `null`) →
  `FetchRouteAdvisoriesAsync` returns `"unavailable"`.
- AS switch off → the label and box are hidden entirely (`_routeAdvisoriesLabel.Visible =
  _routeAdvisoriesBox.Visible = asEnabled`, evaluated every refresh), out of the NVDA tab order,
  matching the mode line/station/profile boxes rather than showing disabled placeholder text.

**Decode gating.** The box follows the same "Decode advisories into plain English"
checkbox (`UserSettings.DecodeWeatherAdvisories`) as the Nearby Advisories box:
unchecked shows the raw blocks (split + deduplicated); checked shows a rebuilt
plain-English summary per advisory ("MHTG SIGMET J5: Central American FIR, embedded
thunderstorms, observed at 1830Z, tops FL520, moving west at 5 knots, no change
expected." / "Valid until 2200Z.") built from the decoded fields — FIR name (the body's
leading "<code> <NAME> FIR" declaration, title-cased: the geographic name is what a
blind pilot can use, the MHCC/NZZO code is not — Robin's 2026-07-13 feedback), hazard,
observed/forecast time, vertical extent, movement, trend. The `WI` lat/lon polygon is
deliberately dropped from the
decoded view (noise when read aloud; flip the checkbox off to see it). The summary path
requires a decoded HAZARD (`HasDecodedContent`): a block whose phenomenon is out of
vocabulary renders verbatim even with decoding on, no matter how many other fields decoded
— a summary must never drop what the SIGMET is FOR. Decoding never hides data.

**(d) Announce lifecycle.** `MainForm.WeatherAnnouncementTimer_Tick` calls
`CheckRouteAdvisoriesAsync` (`MainForm.Announcers.cs`) on the same 30 s tick as the SIGMET/PIREP
proximity check, guarded by its own reentrancy flag (`_routeAdvisoryCheckRunning`, the
`_proximityCheckRunning` pattern) and gated on `AnnounceRouteAdvisoriesEnabled`. The central AS
gate inside `IsRunningAsync()` (§1) makes the check free for non-AS users despite riding the
tick unconditionally. A failed fetch (`raw == null`) touches nothing — the tracker's state is
left exactly as it was.

Key-set decisions are pure, in `Services/RouteAdvisoryTracker.cs`
(`RouteAdvisoryTracker.Observe`), following the same `ActiveSkyModeTracker` shape as every other
tracker in this doc:

- **Baseline-first.** The first successful `Observe` call records every key it sees and returns
  nothing — a route that already has advisories on it when the app connects (or when AS itself
  is found already running) is preflight discovery for the radar box, not a startup announcement
  burst.
- **New keys only, after baseline.** Subsequent calls return only keys never seen before; each
  one produces exactly one queued announce. The spoken announcement is ALWAYS decoded when
  possible, independent of the box's checkbox — plain English is preferred over raw SIGMET
  abbreviations: `announcer.Announce($"Route advisory: {BuildRouteAdvisoryAnnouncement(adv)}.")`,
  e.g. `"Route advisory: MHTG SIGMET J5, Central American FIR, embedded thunderstorms, tops FL520."`
  (`BuildRouteAdvisoryAnnouncement`: identity + FIR name + hazard + vertical extent;
  movement/trend/validity stay box-only). When identity/hazard don't decode it deliberately
  falls back to the raw key, abbreviations and all — an out-of-vocabulary advisory must still
  be heard, never dropped (never `AnnounceImmediate` —
  this is a background change, not a user action). The phrasing is deliberately neutral — no
  "New" — because the identical announce also fires as the 15-minute reminder below, where a
  newness claim would mislead (the sibling SIGMET/PIREP proximity alerts use the same neutral
  "category: content" pattern).
- **15-minute reminder clear**, matching `_announcedSigmetKeys`/`_sigmetKeysClearedAt`
  byte-for-byte in shape: `CheckRouteAdvisoriesAsync` tracks its own
  `_routeAdvisoryKeysClearedAt` and calls `_routeAdvisoryTracker.ClearAnnouncedKeys()` once more
  than 15 minutes have passed, so a still-active advisory legitimately re-announces as a
  reminder rather than going silent for the rest of the flight. `ClearAnnouncedKeys()` drops only
  the seen-key set — the baseline latch (`_baselineDone`) is untouched, so the next tick after a
  clear does NOT re-baseline silently; it treats every currently-present key as "new" again,
  which is the intended reminder behavior.
- **Persists across AS-unreachable gaps.** A tick where `IsRunningAsync()` returns false, or
  where the fetch fails, returns early without touching the tracker at all — no re-announce
  storm on reconnect, and no baseline reset.
- **Resets on both SimConnect connect and aircraft switch** — `RouteAdvisoryTracker.Reset()` is
  called from `MainForm.AircraftSwitch.cs`'s `OnConnectionStatusChanged` "Connected" branch
  (alongside `_announcedSigmetKeys.Clear()`) and from `SwitchAircraft` (alongside the other
  hazard trackers' resets — turbulence, icing). Only the `OnConnectionStatusChanged` Connected
  branch also resets `_routeAdvisoryKeysClearedAt = DateTime.UtcNow`; `SwitchAircraft` calls only
  `_routeAdvisoryTracker.Reset()`. This is functionally harmless — the 15-minute clear is a no-op
  immediately after a `Reset()` regardless, since there are no announced keys left to clear.

**(e) Settings.** `UserSettings.AnnounceRouteAdvisoriesEnabled` (bool, default `true`, both the
property and `Clone()`) backs a new checkbox in the Weather panel's Announcements group,
`"Auto-announce new route advisories (ActiveSky)"`, placed with the SIGMET/PIREP proximity rows
(`Forms/Settings/WeatherPanel.cs`). Like those two siblings it is a proximity-alert-style
sub-toggle **independent of the "Auto-announce weather state changes" master** — unlike them
(and unlike the turbulence/icing toggles in §10d, which need the master), its visibility is
gated on ActiveSky alone: `_routeAdvisoryAlerts.Visible = _activeSkyEnabled.Checked` in
`UpdateActiveSkyDependentVisibility`, because the data source is AS-only and the checkbox is
meaningless without it. Hiding never resets the stored value; `LoadFrom`/`ApplyTo` round-trip it
unconditionally either way.

**(f) Honest verification caveat.** The no-hit path, the box's three-way rendering, the 30 s
refresh, and the settings toggle are all verifiable on demand and were exercised during
development. The HIT path — a real SIGMET/AIRMET response, its parsing, and its decoding — is
now live-verified: the 2026-07-12 live capture (live MHTG SIGMET J5 and YMMM T07 advisories,
the former repeated seven times across route-segment intersections) is pinned as the test
fixture in `RouteAdvisoriesTests.cs`, and the full suite is green against it — dedup,
decoded-field extraction, box rendering with decoding on and off, and the announcement builder
all exercise the real captured text, not a hand-written approximation. The capture came from a
KMIA→YSSY (Miami→Sydney) plan Robin dispatched deliberately to cross many FIRs, which is why the
response carries both a Central-American-FIR SIGMET (MHTG) and a Melbourne-FIR SIGMET (YMMM) —
the route-filtered framing is confirmed, since both FIRs genuinely lie along that great-circle
route. A second live capture (2026-07-13, KMIA→KJFK) confirmed the ROUTE endpoint also serves
US convective advisories in the same shape — CONVECTIVE SIGMET 54E repeated 5× across
route-segment intersections, deduped to one — and added the wind-style movement group
("MOV FROM 27010KT" → "moving from 270 degrees at 10 knots") to the decoder's vocabulary;
that capture is pinned in `RouteAdvisoriesTests.cs` too.

What remains unverified is narrower: a genuinely NEW advisory appearing mid-flight — an advisory
absent from the tracker's baseline that then shows up on a later 30 s tick and fires
`"Route advisory: …"` in real time — has not yet been observed, because both advisories in the
2026-07-12 session were already present at baseline (preflight discovery, not a live "new key"
event). It gets its first true end-to-end verification only when a flight's route genuinely
picks up new weather mid-route. Until then, the announce firing correctly, exactly once per
advisory, with no startup burst, rests on the tracker's characterization tests
(`tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs`) and the now-live-verified parser/decoder
rather than an observed live "new advisory" event — a narrower but real distinction worth
remembering before calling this feature fully tested end to end.

## 13. Nearby advisories stay aviationweather.gov-sourced (2026-07-13 decision)

The Weather Radar's **Nearby Advisories** box and the SIGMET/PIREP **proximity auto-announce**
(`WeatherService.GetNearbyAdvisoriesAsync` / `GetNearbyPirepsAsync` — aviationweather.gov
`isigmet`/`airsigmet`/`pirep` GeoJSON, position+range filtered) deliberately do NOT follow the
per-engine source rule (§3). This was investigated and decided against on 2026-07-13, with live
probes against a running ASFS (aircraft at KMIA, three real SIGMETs within 300 nm):

- **In Live mode the two sources carry identical content.** All three aviationweather.gov
  advisories (one KZWY/KZMA oceanic-FIR `FRQ TS` SIGMET + two US convective SIGMETs) hit on
  ActiveSky's positional `GetActiveSigmetsAt?lat=&lon=` when probed inside their polygons; the
  oceanic body text was verbatim-identical. So switching sources gains nothing for the Live-mode
  majority while LOSING precision: aviationweather.gov's structured geometry gives the announcer
  its exact "bearing 240 degrees, 85 nautical miles" phrasing, which AS cannot provide (below).
- **The divergence window is non-Live modes only** (historic/custom/SimTime) — handled by a
  cheap caveat instead of a source switch: `ActiveSkyFormatting.BuildNearbyAdvisoriesModeCaveat`
  (pure, CI-tested) appends "Note: nearby advisories are live real-world data; ActiveSky is in
  {mode}." under the box header whenever AS is enabled+reachable and its parsed mode doesn't
  start with "Live". Zero extra I/O — the mode text rides the liveness probe already. Unknown
  /unreadable mode → no caveat; unrecognized mode text passes through verbatim (FormatModeLine's
  never-hide philosophy).

**Live-probe facts recorded for any future AS-sourced-proximity design** (also in the
2026-07-12 route-advisories design doc §10; these answer that doc's open questions):

- `GetActiveSigmetsAt?lat=&lon=` is strict point-in-polygon **containment** — a probe 3 nm
  outside a convective SIGMET's boundary already returns the no-hit sentence. No tolerance, no
  radius parameter, no "list all within N nm" form. A proximity feature must probe PROJECTED
  points (ring/track sampling); sampling density fully determines detection resolution.
- The positional no-hit sentence is **"No airmet/sigmet active at the requested position"** —
  different wording from the route variant's, but the parser's `StartsWith("No airmet/sigmet")`
  check covers both.
- **US convective bodies come back with their geometry stripped** — AS consumes the
  "FROM 160SE CHS-…" location line, returning "AREA TS MOV LTL. TOPS ABV FL450. …" only. There
  is no position data to parse out of AS text for US advisories; polygon-parsing designs are
  dead for that class. ICAO-style bodies keep their `WI` polygons.
- **A hit can bundle unrelated advisories** after `---------------------- Hazard: XX` separators
  on one body line — the ECHO 5 probe returned the matched advisory plus a mid-Atlantic SIGMET
  and an Indonesian volcanic-ash SIGMET, neither containing the probe point. Any consumer must
  treat only the FIRST advisory of a hit as position-matched (our block parser keeps the bundle
  as one block whose leading text is the matched advisory, so hazard/extent decode correctly).
- **AS's "Valid until:" line is suspect** — all three hits said `1600z` against real validities
  of 1605Z/1655Z; it may be AS's own data-window time, not the advisory's validity.
- **AS renders identities differently** — "SIGA SIGMET E5 TS" for aviationweather.gov's
  "SIGMET ECHO 5" (KZWY/KZMA). `IdentityPattern` matches both shapes.

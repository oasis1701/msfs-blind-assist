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

Design doc: `docs/design/2026-07-10-activesky-improvements-design.md`. Five read-only additions
on top of the surfaces above — a mode readout, a vertical weather profile, an on-demand
closest-station readout, a forecast METAR combo, and a settings-panel status line — all gated
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

**Current-position temperature/dew point line (a related sixth read-only addition, sharing the
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
ActiveSkyFormatting.cs`) is the fixed offset table — Now, then hourly through +4 hours, then +6
hours, i.e. 0/3600/7200/10800/14400/21600 seconds — consumed both to populate `METARReportForm`'s
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

**(e) Settings-panel status line.** `WeatherPanel.RefreshActiveSkyStatusAsync` (`Forms/
Settings/WeatherPanel.cs`) probes AS via a dedicated `ActiveSkyClient` instance (separate from
the one any open form/monitor uses, so opening Settings never disturbs another surface's cached
port) and writes `ActiveSkyClient.LastStatus` into `_asStatusLabel` — e.g. `"ActiveSky status:
detected on port 19285"` or `"ActiveSky status: not detected (port 19285: no listener)"`. It
runs once when the panel loads (`LoadFrom`) and reflects the **saved** setting (the central
`IsRunningAsync` gate reads `SettingsManager.Current`, not the panel's unapplied checkbox
state) — so ticking the checkbox and reading the status line in the same visit shows the
*previous* session's result until Apply/OK commits the change and the panel is reopened. This
finally fulfills `ActiveSkyClient.LastStatus`'s doc comment, which had promised "surfaced to the
UI" since the property was first added.

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

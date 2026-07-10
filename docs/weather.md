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

With the switch **on**, output+I reads ActiveSky's ambient wind and surface gust
(`FormatActiveSkyWind` in `MainForm.Announcers.cs`, reading `Conditions.AmbientWindDirection` /
`AmbientWindSpeed` / `SurfaceGustSpeed`). With the switch **off**, SimConnect is authoritative
and correct — full stop.

The two sources can legitimately diverge: AS applies its own wind smoothing/interpolation, so
its ambient wind is not guaranteed to match SimConnect's `AMBIENT WIND DIRECTION`/`VELOCITY`
at the same instant. Neither is "the" truth for both populations — never hard-pick one source
for users who haven't opted into ActiveSky, and never silently fall back to SimConnect for
users who have (that's exactly the "ActiveSky not responding" notice's job — see §1's
Robin's-ruling note in `RequestWindInfo`: the notice fires on every press when AS is enabled
but unreachable, deliberately, because a blind pilot must never miss a degraded wind readout).

## 4. Precipitation source precedence

Three separate readouts decode precipitation from a METAR: the Weather Radar
(`WeatherRadarForm.ParsePrecipFromMetar`), the ActiveSky decoded-weather monitor
(`ActiveSkyWeatherMonitor` via the `WeatherRadarFormPrecipShim` copy — see §7), and the
ambient auto-announce (`MainForm.Announcers.cs` `CheckAmbientWeatherChanges`). All three use
the **same** source precedence so they never contradict each other:

1. Closest-station METAR (`ActiveSkyClient.GetClosestStationMetarAsync`) — the real nearest
   reporting station.
2. Position METAR (`GetPositionMetarAsync`, AS's `@POS` interpolated point weather) —
   fallback when the closest-station call fails.
3. SimConnect `AMBIENT_PRECIP_STATE`/`AMBIENT_PRECIP_RATE` bitmask — last resort, only used
   when ActiveSky isn't running at all (see §2's table; under ActiveSky the bitmask is known
   to stick, e.g. reported stuck on "extreme snow").

A METAR that parses with **no** weather-phenomenon token means "no precipitation" and must
render as `"None"` — it must never fall through to the next source in the list. Only a
wholly-missing METAR (the fetch itself failed or returned empty/whitespace) triggers
fallback to the next source. `WeatherRadarForm.FormatAmbientFromActiveSky` implements this
exactly: each of the three METAR-based branches does
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
speaks unprompted). `ShouldRun` is called from exactly two sites — `MainForm.InitializeManagers`
at launch (`activeSkyWeatherMonitor.Enabled = ActiveSkyWeatherMonitor.ShouldRun(...)`) and
`MainForm.MenuHandlers.ApplyRuntimeSettings` on live settings save — deliberately never
inlined elsewhere, so the two call sites can't drift apart.

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

`ActiveSkyWeatherMonitor` cannot call it directly (the form doesn't expose it publicly), so
`Services/ActiveSkyWeatherMonitor.cs` hosts a byte-for-byte duplicate as
`WeatherRadarFormPrecipShim.ParsePrecipFromMetar` — same token classification, same output
strings. **Do not move the decoder out of `WeatherRadarForm` without updating the shim**, and
do not change one copy's phenomenon/descriptor table without mirroring the change in the
other — a drift here would make the radar, the decoded-weather monitor, and the ambient
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

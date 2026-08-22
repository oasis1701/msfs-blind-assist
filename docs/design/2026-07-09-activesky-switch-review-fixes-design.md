# ActiveSky Master Switch — Review Fixes (PR #156)

**Date:** 2026-07-09
**Status:** Approved by Robin (this session)
**Branch:** `feature/weather-tab-activesky-switch`
**Reviews:** follow-up to the PR #156 code review

## Problem

PR #156 added `UserSettings.ActiveSkyEnabled` (default off) and gated all ActiveSky
probing behind it. The gating architecture is sound — a central short-circuit in
`ActiveSkyClient.IsRunningAsync()` plus per-method guards — and every call site was
verified to degrade to its SimConnect/VATSIM path. Review surfaced five defects.

### D1 — The AS switch silently doubles as an announcement switch

`ActiveSkyWeatherMonitor` (the thing that speaks *"Weather update. Surface wind X at
Y…"*) is started on `ActiveSkyEnabled` alone, in both `MainForm.InitializeManagers`
and `ApplyRuntimeSettings`. `WeatherAutoAnnounceEnabled` is read in exactly one place
(`MainForm.Announcers.cs` `WeatherAnnouncementTimer_Tick`) and never reaches the
monitor.

Consequence: a user who enables ActiveSky purely for accurate output+I wind, gust
data, and radar cannot stop the periodic spoken weather updates. The new Weather tab
makes this actively misleading — the interval combo that throttles the monitor sits
inside an "Announcements" group headed by a checkbox that does not control it, and the
combo's visibility is keyed to the AS switch rather than to the checkbox above it.

### D2 — Two tests are vacuous

`ActiveSkyGateTests.GetCurrentConditions_WhenDisabled_ReturnsNull` constructs a fresh
`ActiveSkyClient`, whose `LastSuccessfulPort` is already null. The pre-existing
`if (LastSuccessfulPort is not int port) return null;` line satisfies the assertion, so
the test passes with the new master-switch guard deleted. It exercises nothing.

Worse, nothing pins `LastSuccessfulPort = null` in the disabled branch of
`IsRunningAsync()` — the single line that closes the enable → discover → disable cached-
port leak the PR body claims to fix. `LastSuccessfulPort` has a private setter, so no
test can seed a cached port to observe the reset.

### D3 — Logging parity

The PR states its goal is closing the observability gap that made the original
hotkey-delay report undiagnosable. `GetCurrentConditionsAsync` gained a
`Log.Debug("ActiveSky", …)` line in its catch. The four sibling data methods —
`GetWeatherAreaAsync`, `GetPositionMetarAsync`, `GetClosestStationMetarAsync`,
`GetMetarAsync` — still swallow their exceptions silently.

### D4 — CLAUDE.md weather invariants point at a doc with no weather content

All eight weather/ActiveSky invariants (CLAUDE.md:248–255), including the two this PR
rewrote, end with `→ [taxi-guidance.md](docs/taxi-guidance.md)`. That file is 1,483
lines, has zero weather sections, and uses the word "weather" three times, all
incidental. There is no weather doc anywhere in `docs/`. The mislink predates this PR;
this PR is the first to touch those bullets.

### D5 (rejected) — "not responding" notice cadence

The notice fires on every output+I press when the AS fetch returns null. Robin's
ruling: **keep as-is.** A blind pilot must never miss that their wind readout is
degraded, and a per-episode latch could swallow the notice on a press made an hour
after the last one. Wording also stays — "not responding" fairly describes both a
failed probe and a probe that succeeded before a failed data fetch.

## Non-goal: SimConnect must keep working with the switch off

Verified against the code on this branch, and reaffirmed here as an invariant the fixes
must not break:

| Path | With `ActiveSkyEnabled == false` |
|---|---|
| output+I wind (`MainForm.Announcers.cs`) | `TryGetActiveSkyConditionsAsync` returns null at the gate; `else` branch calls `simConnectManager.RequestWindInfo` → `FormatWindData`. `sourceNotice` stays empty (guarded on the setting). |
| Alt+W cloud/precip/visibility (`CheckAmbientWeatherChanges`) | SimConnect ambient is fetched **unconditionally, first**. `asPrecip` stays null → `AnnounceAmbientChanges` takes the SimConnect `PrecipState`/`PrecipRate` branch. Cloud and visibility never consult AS in either branch. |
| Weather Radar (`WeatherRadarForm`) | SimConnect ambient fetched unconditionally; `_activeSkyAvailable == false` skips the AS block; in-cloud renders from `simAmbient.InCloud`. |
| METAR form (`METARReportForm`) | `asAvailable == false` → compact 400 px VATSIM-only layout. |

D1's fix cannot regress any of these: `ActiveSkyWeatherMonitor` reads only the AS HTTP
API and has no SimConnect fallback, and `CheckAmbientWeatherChanges` runs on a
different timer gated on `WeatherAutoAnnounceEnabled` alone.

## Decision

Five changes. D5 is closed as won't-fix per the ruling above.

## 1. Decouple the monitor from the AS switch (D1)

The decoded-weather monitor runs iff **both** `ActiveSkyEnabled` and
`WeatherAutoAnnounceEnabled`. Rationale: it is an ActiveSky feature (needs AS) *and* an
announcement feature (the user must have asked to be spoken at).

- New private helper on `MainForm`:
  `private static bool ShouldRunWeatherMonitor(UserSettings s) => s.ActiveSkyEnabled && s.WeatherAutoAnnounceEnabled;`
  so the launch site and the live-toggle site cannot drift.
- `MainForm.InitializeManagers`: replace `if (…ActiveSkyEnabled) activeSkyWeatherMonitor.Start();`
  with `activeSkyWeatherMonitor.Enabled = ShouldRunWeatherMonitor(SettingsManager.Current);`
- `MainForm.MenuHandlers.ApplyRuntimeSettings`: replace the `if/else Start()/Stop()`
  pair with `activeSkyWeatherMonitor.Enabled = ShouldRunWeatherMonitor(settings);`
  The existing `ActiveSkyWeatherMonitor.Enabled` setter already does
  `if (value) Start(); else Stop();`, and `Timer.Start/Stop` are idempotent.

**Release note (required, second line):** an existing ActiveSky user who has spoken
weather updates today but has never ticked "Auto-announce weather state changes" will
go silent after this change. This is the intended decoupling, not a bug.

## 2. Interval-combo visibility follows the monitor predicate (D1)

`WeatherPanel.UpdateActiveSkyDependentVisibility` currently hides
`_weatherIntervalLabel`/`_weatherIntervalCombo` on `!_activeSkyEnabled.Checked`. It
becomes: visible iff `_activeSkyEnabled.Checked && _weatherAutoAnnounce.Checked`,
subscribed to `CheckedChanged` on **both** boxes.

`WeatherAutoAnnounceIntervalMinutes` is read in exactly one place — it becomes
`ActiveSkyWeatherMonitor.IntervalMinutes` — so the combo is meaningful precisely when
the monitor runs. This preserves the existing invariant (a blind user tabbing the panel
must not meet a setting that governs nothing) against the *actual* governing condition.

`ApplyTo` keeps reading the combo regardless of visibility, so a hidden interval still
round-trips. The existing `HiddenInterval_StillRoundTripsItsValue` test pins this and
stays green.

## 3. Make the vacuous tests real (D2)

- `ActiveSkyClient.LastSuccessfulPort` setter widens from `private` to `internal`.
  `MSFSBlindAssist/Properties/InternalsVisibleTo.cs` already exposes internals to
  `MSFSBlindAssist.Tests`, so this costs nothing and leaks nothing to app code.
- New `IsRunningAsync_WhenDisabled_ClearsCachedPort`: seed `LastSuccessfulPort = 19285`,
  disable, call `IsRunningAsync()`, assert the port is null. Fails if the
  `LastSuccessfulPort = null` line is deleted. This is the leak fix, pinned.
- Rewrite `GetCurrentConditions_WhenDisabled_ReturnsNull` to seed a port first, so it
  exercises the per-method master-switch guard rather than the null-port fallthrough.
- Two new `WeatherPanel` tests for the both-flags visibility predicate (AS on +
  announce off → hidden; both on → visible), located by `AccessibleName` like the
  existing visibility tests.

## 4. Logging parity (D3)

`GetWeatherAreaAsync`, `GetPositionMetarAsync`, `GetClosestStationMetarAsync`, and
`GetMetarAsync` each get a `Log.Debug("ActiveSky", "<MethodName> failed (timeout or
connection error)")` in their catch, matching `GetCurrentConditionsAsync`.

The disabled short-circuit in `IsRunningAsync` stays **unlogged**, deliberately — the
weather timer would spam debug.log every 30 s. The original implementation plan already
called this out and it remains correct.

## 5. `docs/weather.md` + repoint the invariants (D4)

New `docs/weather.md` covering, with the code as the source of truth:

- ActiveSky integration architecture: the opt-in setting, the central gate in
  `IsRunningAsync`, the per-method defense-in-depth guards, why the probe has a ~1.2 s
  floor.
- The per-engine wind-truth rule (SimConnect when off, AS API when on) and why neither
  source is "the" truth.
- Precip source precedence shared by the three readouts (closest-station METAR →
  position METAR → SimConnect bitmask).
- The SimConnect fallback table from the Non-goal section above.
- Monitor lifecycle: constructed always, `Enabled` gated on both flags, live-toggled
  from `ApplyRuntimeSettings`.
- The `WeatherRadarForm.ParsePrecipFromMetar` / weather-monitor decoder duplication.

Then repoint CLAUDE.md:248–255 (eight bullets) from `taxi-guidance.md` to
`weather.md`, and add `docs/weather.md` to CLAUDE.md's "Detailed Documentation" list
and its "when to read" table.

**Known remaining:** CLAUDE.md:256 (Cold Temperature Correction) is also mislinked to
`taxi-guidance.md`. It is an EFB altitude-correction feature, documented nowhere, and is
out of scope here. Left as-is.

## Testing & verification

- `dotnet build MSFSBlindAssist.sln -c Debug` — 0 warnings.
- `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
  — full suite green; 1031 existing + 3 new (1 port-reset, 2 panel-visibility), with
  `GetCurrentConditions_WhenDisabled_ReturnsNull` rewritten in place.
- Each new test must be watched to FAIL before its fix lands (TDD), specifically:
  delete `LastSuccessfulPort = null` from the disabled branch and confirm
  `IsRunningAsync_WhenDisabled_ClearsCachedPort` goes red.

### In-sim test plan (sim-facing; repo owner runs)

1. **Switch off, "Auto-announce" ON, no ActiveSky installed.** Fly into cloud and into
   rain. Cloud entry/exit, precip start/stop, and visibility crossings all announce from
   SimConnect. `debug.log` contains zero `[ActiveSky]` lines. output+I answers instantly
   with SimConnect wind and no "not responding" prefix. *(This is the regression the
   decoupling must not cause.)*
2. **Switch ON, "Auto-announce" OFF, AS running.** No spoken "Weather update…" ever.
   output+I still speaks AS wind + gust. Radar still shows AS data. The interval combo
   is hidden from the Weather tab and absent from the tab order.
3. **Switch ON, "Auto-announce" ON, AS running.** Interval combo visible. Monitor
   announces AS weather updates. Untick "Auto-announce" and save → announcements stop
   without restart, combo disappears.
4. **Switch ON, AS not running.** output+I prefixes "ActiveSky not responding, using
   simulator wind." on every press, then the SimConnect wind. (Deliberate — see D5.)
5. **Enable → let a probe succeed → disable → save.** `debug.log` shows no further
   `[ActiveSky] probe` lines; radar reverts to SimConnect-only on next refresh.

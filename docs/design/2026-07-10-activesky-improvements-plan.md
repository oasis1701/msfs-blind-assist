# ActiveSky Read-Only Improvements — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose seven read-only ActiveSky API data gaps accessibly (mode readout + change announce, position dew point, on-demand closest-station decoded weather, forecast METAR, AS-truth winds/temps aloft, vertical weather profile, LastStatus diagnostics), per the approved spec `docs/design/2026-07-10-activesky-improvements-design.md`.

**Architecture:** Extend `ActiveSkyClient` with new endpoint wrappers (same null-on-failure shape as the existing five); put every new text builder as an `internal static` pure function in a new `Services/ActiveSkyFormatting.cs` (CI-tested); piggyback mode-change tracking on the existing `ActiveSkyWeatherMonitor` tick; grow the three existing forms in place. No new forms, no new poll loops, no hotkey changes.

**Tech Stack:** .NET 10 / C# 13, Windows Forms (manual layout, no designer files), xUnit (x64), `System.Text.Json`, `System.Xml.Linq`.

## Global Constraints

- Build ONLY via `dotnet build MSFSBlindAssist.sln -c Debug` — never the bare `.csproj` (writes to the wrong output folder; see CLAUDE.md).
- Test via `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`. The FULL suite must pass after every task (baseline: 1074 tests green).
- Every AS I/O call goes through wrappers that early-return `null` when `Settings.SettingsManager.Current.ActiveSkyEnabled` is false or `LastSuccessfulPort` is null. Never bypass this gate.
- All URL numbers and all response parsing use invariant culture. AS returns JSON numbers as strings.
- Never announce combo/dropdown/button interactions (screen readers already do). The ONLY new announcement in this plan is the background mode-change announce (Task 2).
- No `%APPDATA%\HiFi` file access. HTTP only. All logging via `Log.Debug("ActiveSky"|"Services"|"Forms", msg)`.
- Hidden WinForms controls leave the tab order automatically (`TabStop` honours `Visible`) — never manually re-index on show/hide.
- TDD: write the failing test, watch it fail, implement, watch it pass. Compile errors for missing APIs count as the RED step in C#.
- Commit after every task. End commit messages with: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
- New test files live in `tests/MSFSBlindAssist.Tests/`, namespace `MSFSBlindAssist.Tests`, xUnit with implicit `using Xunit;` (project-wide `<Using Include="Xunit" />`).

**Key existing facts (verified 2026-07-10):**
- `ActiveSkyClient` (`MSFSBlindAssist/Services/ActiveSkyClient.cs`): `public class`, namespace `MSFSBlindAssist.Services`. Shared `private static readonly HttpClient _http` (no Timeout; per-call CTS). `private string BaseUrl(int port) => $"http://localhost:{port}/ActiveSky/API";` (line ~111). `public int? LastSuccessfulPort { get; internal set; }` (~49), `public string LastStatus { get; private set; }` (~52). `ProbePortAsync(int)` (~181-203) GETs `{BaseUrl}/GetMode`, returns `(int port, bool success, string err)`. `IsRunningAsync()` (~120) is the central gate. Existing helpers `ReadDouble(JsonElement, string)` / `ReadLong(JsonElement, string)` are `private static`, tolerate numbers-as-strings, default 0. `InternalsVisibleTo("MSFSBlindAssist.Tests")` is in place (`MSFSBlindAssist/Properties/InternalsVisibleTo.cs`).
- `ActiveSkyWeatherMonitor` (`MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs`): `System.Windows.Forms.Timer` at 60 s, `OnTickAsync` is `private async void` on the UI thread, re-entry-guarded by `_polling`. Tick starts with `if (!await _activeSky.IsRunningAsync()) { ...weather-baseline reset...; return; }`. `internal static DecodeMetar(string)` returns nested `internal sealed class DecodedMetar` (public FIELDS: `Station`, `IsPositionMetar`, `WindText`, `VisibilityText`, `CloudsText`, `TemperatureC`, `DewPointC`, `QnhMb`). `private static string BuildAnnouncement(string? closestStationMetar, string positionMetar, ActiveSkyClient.Conditions conditions)` (~275). Announces via `_announcer.Announce(spoken)` (UI thread — WinForms timer).
- `WeatherRadarForm` (`MSFSBlindAssist/Forms/WeatherRadarForm.cs`, 672 lines): manual layout in `InitializeComponent()` (~50-175); form shell `Size = new Size(600, 710)`, FixedDialog, KeyPreview. Controls all x=12, boxes width 566, labels width 570, `Font("Consolas", 9)`. Current stack: current-weather label y=12/box y=36 h100 → advisories label y=148/box y=172 h210 → winds label y=394/box y=418 h170 → decode checkbox y=598 → status y=632, buttons y=626. Tab order in `SetupAccessibility()` (~177): currentBox 0, advisories 1, winds 2, decode 3, refresh 4, close 5. `RefreshAsync(bool forceRefresh)` (~199) sets `_activeSkyAvailable = await _activeSky.IsRunningAsync()`, gets `(lat, lon, altFt)` from `GetPositionAsync()`, runs `FetchAmbientAsync()` + `FetchAdvisoriesAsync(lat,lon,force)` + `FetchWindsAloftAsync(lat,lon,altFt,force)` via `Task.WhenAll`, assigns box texts. `private static string FormatAmbientFromActiveSky(ActiveSkyClient.Conditions c, SimConnectManager.AmbientWeatherData simAmbient, bool simConnected, string? positionMetar, string? closestStationMetar = null)` (~345). `FetchAmbientAsync` already fetches conditions + position METAR + closest-station METAR in parallel (~283-334).
- `WeatherService` (`MSFSBlindAssist/Services/WeatherService.cs`): `public static async Task<List<WindAtAltitude>> GetWindsAloftAsync(double lat, double lon, int aircraftAltFt, bool forceRefresh = false)` (~212) — Open-Meteo; `public record WindAtAltitude(int AltitudeFt, double DirectionDeg, double SpeedKts)` (~101). The ±5000 ft / 1000-ft-step window lives in `ParseWindsAloft` (~424-431).
- `METARReportForm` (`MSFSBlindAssist/Forms/METARReportForm.cs`): compact `Size(500, 400)`; AS section (`asMetarLabel` y=285, `asMetarTextBox` y=310 h160) starts `Visible = false`; revealed in async `Load` when `await _activeSky.IsRunningAsync()` — Close button moves to `(385, 490)`, form grows to `500x580`. Tab order: icao 0, metar 1, asMetar 2, close 3. `FetchMETAR()` (~220-310) calls `_activeSky.GetMetarAsync(icao)` (already has unused `int timeOffsetSec = 0` param, lowercase `timeoffset` in URL).
- `WeatherPanel` (`MSFSBlindAssist/Forms/Settings/WeatherPanel.cs`, 227 lines): `UserControl, ISettingsPanel`. `BuildActiveSkyGroup()` (~70-97) returns a GroupBox `Size(460, 96)` containing only `_activeSkyEnabled` checkbox at (12,24) size (430,48). Announcements group below auto-positions at `activeSkyGroup.Bottom + 12`. `LoadFrom(UserSettings)` / `ApplyTo(UserSettings)` contract; existing tests in `tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs` use `FindByAccessibleName` and the `SettingsManagerGlobalStateCollection`.
- Live API fixtures for tests are embedded in the task steps below (captured 2026-07-10 from the running ASFS).

---

### Task 1: GetMode capture + `ActiveSkyFormatting.ParseModeText`/`FormatModeLine` + Weather Radar mode line

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyClient.cs` (LastModeText property; capture in ProbePortAsync)
- Create: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs`
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs` (mode label + y-cursor layout refactor)
- Test: `tests/MSFSBlindAssist.Tests/ActiveSkyFormattingTests.cs`

**Interfaces:**
- Consumes: `ActiveSkyClient.ProbePortAsync`, `LastStatus`, `SettingsManager.Current.ActiveSkyEnabled`.
- Produces: `public string? ActiveSkyClient.LastModeText { get; private set; }` (set on every successful probe); `internal static (string ModeName, string? WeatherTimeZ) ActiveSkyFormatting.ParseModeText(string? raw)`; `internal static string ActiveSkyFormatting.FormatModeLine(string? raw)`; `WeatherRadarForm._asModeLabel` (AccessibleName "ActiveSky status"). Later tasks (2) rely on `LastModeText`; later form tasks (5, 8) rely on the y-cursor layout introduced here.

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/ActiveSkyFormattingTests.cs`:

```csharp
// Characterization tests for the pure ActiveSky text builders (Services/ActiveSkyFormatting.cs).
// Golden inputs are live captures from a running ASFS build (2026-07-10).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyFormattingTests
{
    // --- ParseModeText -----------------------------------------------------------------

    [Fact]
    public void ParseModeText_parses_live_mode_string()
    {
        var (mode, time) = ActiveSkyFormatting.ParseModeText(
            "Live Real time mode (Active) (2026/7/10 1935z)");
        Assert.Equal("Live Real time mode", mode);
        Assert.Equal("2026/7/10 1935z", time);
    }

    [Theory]
    [InlineData("Historic dynamic mode (Active) (2019/3/4 0600z)", "Historic dynamic mode", "2019/3/4 0600z")]
    [InlineData("Custom static mode (Active) (2026/7/10 2100z)", "Custom static mode", "2026/7/10 2100z")]
    public void ParseModeText_parses_other_mode_families(string raw, string mode, string time)
    {
        var parsed = ActiveSkyFormatting.ParseModeText(raw);
        Assert.Equal(mode, parsed.ModeName);
        Assert.Equal(time, parsed.WeatherTimeZ);
    }

    [Fact]
    public void ParseModeText_passes_unknown_strings_through_without_time()
    {
        var (mode, time) = ActiveSkyFormatting.ParseModeText("wibble");
        Assert.Equal("wibble", mode);
        Assert.Null(time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseModeText_maps_empty_to_unknown(string? raw)
    {
        var (mode, time) = ActiveSkyFormatting.ParseModeText(raw);
        Assert.Equal("unknown", mode);
        Assert.Null(time);
    }

    // --- FormatModeLine ----------------------------------------------------------------

    [Fact]
    public void FormatModeLine_shows_mode_and_clock()
        => Assert.Equal("ActiveSky: Live Real time mode, weather time 1935Z",
            ActiveSkyFormatting.FormatModeLine("Live Real time mode (Active) (2026/7/10 1935z)"));

    [Fact]
    public void FormatModeLine_without_time_shows_mode_only()
        => Assert.Equal("ActiveSky: wibble", ActiveSkyFormatting.FormatModeLine("wibble"));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet build tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: FAIL with CS0103/CS0117 "ActiveSkyFormatting" does not exist. Any other error kind means a typo — fix before proceeding.

- [ ] **Step 3: Create `MSFSBlindAssist/Services/ActiveSkyFormatting.cs`**

```csharp
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure text builders for the ActiveSky readouts. Everything here is a static
/// function of its inputs — no I/O, no settings reads — so the whole class is
/// directly characterization-tested in CI (ActiveSkyFormattingTests).
/// </summary>
public static class ActiveSkyFormatting
{
    /// <summary>
    /// Parses the /GetMode body, e.g. "Live Real time mode (Active) (2026/7/10 1935z)"
    /// → ("Live Real time mode", "2026/7/10 1935z"). Unknown strings pass through
    /// verbatim as the mode name (never crash, never hide); empty → "unknown".
    /// </summary>
    internal static (string ModeName, string? WeatherTimeZ) ParseModeText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ("unknown", null);
        string s = raw.Trim();

        // The trailing "(...z)" group is the AS weather clock.
        string? time = null;
        var m = Regex.Match(s, @"\(([^()]*z)\)\s*$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            time = m.Groups[1].Value.Trim();
            s = s[..m.Index].Trim();
        }

        // Drop a trailing "(Active)" / "(Inactive)" marker.
        s = Regex.Replace(s, @"\s*\((?:In)?active\)\s*$", "", RegexOptions.IgnoreCase).Trim();
        return (s.Length > 0 ? s : "unknown", time);
    }

    /// <summary>Weather Radar status line: "ActiveSky: Live Real time mode, weather time 1935Z".</summary>
    internal static string FormatModeLine(string? raw)
    {
        var (mode, time) = ParseModeText(raw);
        if (time == null) return $"ActiveSky: {mode}";
        string clock = time.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1].ToUpperInvariant();
        return $"ActiveSky: {mode}, weather time {clock}";
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter ActiveSkyFormattingTests`
Expected: PASS (7 tests). Then run the FULL suite — all green.

- [ ] **Step 5: Capture the GetMode body in `ActiveSkyClient`**

In `MSFSBlindAssist/Services/ActiveSkyClient.cs`, add below the `LastStatus` property (~line 52):

```csharp
    /// <summary>Body of the last successful /GetMode probe (e.g. "Live Real time mode
    /// (Active) (2026/7/10 1935z)"). Refreshed by every successful liveness probe —
    /// callers that just ran IsRunningAsync() can read it without another request.
    /// Null until AS has been reached once this session.</summary>
    public string? LastModeText { get; private set; }
```

In `ProbePortAsync`, replace:

```csharp
            using var resp = await _http.GetAsync($"{BaseUrl(port)}/GetMode", cts.Token);
            if (resp.IsSuccessStatusCode)
                return (port, true, "");
```

with:

```csharp
            using var resp = await _http.GetAsync($"{BaseUrl(port)}/GetMode", cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                // Free mode capture — the probe already fetched the body's request.
                try { LastModeText = (await resp.Content.ReadAsStringAsync(cts.Token)).Trim(); }
                catch { /* liveness result stands even if the body read fails */ }
                return (port, true, "");
            }
```

(Design note: the spec listed a `GetModeAsync()` wrapper; it is deliberately dropped — every consumer path calls `IsRunningAsync()` immediately before reading the mode, which refreshes `LastModeText` via the probe. YAGNI.)

- [ ] **Step 6: Add the mode line to `WeatherRadarForm` with a y-cursor layout**

In `MSFSBlindAssist/Forms/WeatherRadarForm.cs`:

6a. Add a field next to the other control fields (~line 26):

```csharp
    private Label _asModeLabel = null!;
```

6b. In `InitializeComponent()`, change the form shell's `Size = new Size(600, 710)` to `Size = new Size(600, 734)`.

6c. Replace the whole control-construction section — from the `// ── Current position weather` comment down to and including the `CancelButton = _closeButton;` line — with the following (this introduces a running `y` cursor so Tasks 5 and 8 can insert sections without renumbering everything; all values reproduce the current layout shifted down 24 px for the new mode line):

```csharp
        int y = 12;

        // ── ActiveSky mode status (hidden when the AS switch is off) ──────
        _asModeLabel = new Label
        {
            Text = "",
            Location = new Point(12, y),
            Size = new Size(570, 20),
            AccessibleName = "ActiveSky status",
            Visible = false
        };
        y += 24;

        // ── Current position weather ──────────────────────────────────────
        _currentWeatherLabel = new Label
        {
            Text = "Weather at Current Position:",
            Location = new Point(12, y),
            Size = new Size(570, 20),
            AccessibleName = "Weather at Current Position label"
        };
        y += 24;

        _currentWeatherBox = new TextBox
        {
            Location = new Point(12, y),
            Size = new Size(566, 100),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = "Press F5 or Refresh to fetch weather data.",
            AccessibleName = "Weather at Current Position",
            AccessibleDescription = "Ambient weather conditions at aircraft position from simulator"
        };
        y += 100 + 12;

        // ── Advisories (SIGMETs, AIRMETs, PIREPs) ────────────────────────
        _advisoriesLabel = new Label
        {
            Text = "Nearby Advisories (SIGMETs / AIRMETs / PIREPs):",
            Location = new Point(12, y),
            Size = new Size(570, 20),
            AccessibleName = "Nearby Advisories label"
        };
        y += 24;

        _advisoriesBox = new TextBox
        {
            Location = new Point(12, y),
            Size = new Size(566, 210),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = "Press F5 or Refresh to fetch advisories.",
            AccessibleName = "Nearby Advisories",
            AccessibleDescription = "Active SIGMETs, AIRMETs, and pilot reports near the aircraft"
        };
        y += 210 + 12;

        // ── Winds Aloft ───────────────────────────────────────────────────
        _windsAloftLabel = new Label
        {
            Text = "Winds Aloft (±5000 ft):",
            Location = new Point(12, y),
            Size = new Size(570, 20),
            AccessibleName = "Winds Aloft label"
        };
        y += 24;

        _windsAloftBox = new TextBox
        {
            Location = new Point(12, y),
            Size = new Size(566, 170),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = "Press F5 or Refresh to fetch winds aloft.",
            AccessibleName = "Winds Aloft",
            AccessibleDescription = "Forecast wind direction and speed at each 1000 ft from 5000 ft below to 5000 ft above aircraft altitude"
        };
        y += 170 + 10;

        // ── Status + buttons ──────────────────────────────────────────────
        _decodeCheckBox = new CheckBox
        {
            Text = "&Decode advisories into plain English",
            Location = new Point(12, y),
            Size = new Size(370, 24),
            Checked = SettingsManager.Current.DecodeWeatherAdvisories,
            AccessibleName = "Decode advisories into plain English",
            AccessibleDescription = "Expand aviation abbreviations in SIGMETs and PIREPs into plain language"
        };
        _decodeCheckBox.CheckedChanged += (_, _) =>
        {
            SettingsManager.Current.DecodeWeatherAdvisories = _decodeCheckBox.Checked;
            SettingsManager.Save();
        };
        y += 28;

        _refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(390, y),
            Size = new Size(100, 28),
            AccessibleName = "Refresh",
            AccessibleDescription = "Fetch current weather, advisories, and winds aloft"
        };
        _refreshButton.Click += (s, e) => _ = RefreshAsync(forceRefresh: true);

        _closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(500, y),
            Size = new Size(78, 28),
            DialogResult = DialogResult.OK,
            AccessibleName = "Close",
            AccessibleDescription = "Close weather radar window"
        };
        _closeButton.Click += CloseButton_Click;

        _statusLabel = new Label
        {
            Location = new Point(12, y + 6),
            Size = new Size(370, 20),
            Text = "",
            AccessibleName = "Status"
        };

        Controls.AddRange(new Control[]
        {
            _asModeLabel,
            _currentWeatherLabel, _currentWeatherBox,
            _advisoriesLabel, _advisoriesBox,
            _windsAloftLabel, _windsAloftBox,
            _decodeCheckBox, _statusLabel, _refreshButton, _closeButton
        });

        CancelButton = _closeButton;
```

6d. In `RefreshAsync`, immediately after the line `_activeSkyAvailable = await _activeSky.IsRunningAsync();`, insert:

```csharp
            // Mode status line — hidden entirely when the AS switch is off (spec:
            // AS-only UI disappears, not "disabled" text). Re-evaluated per refresh
            // so toggling the setting takes effect without an app restart.
            bool asEnabled = SettingsManager.Current.ActiveSkyEnabled;
            _asModeLabel.Visible = asEnabled;
            if (asEnabled)
                _asModeLabel.Text = _activeSkyAvailable == true
                    ? MSFSBlindAssist.Services.ActiveSkyFormatting.FormatModeLine(_activeSky.LastModeText)
                    : $"ActiveSky: {_activeSky.LastStatus}";
```

- [ ] **Step 7: Build the full solution, run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` — Expected: Build succeeded, 0 warnings.
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` — Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(weather): ActiveSky mode readout in Weather Radar form

Captures the /GetMode body the liveness probe already fetches
(ActiveSkyClient.LastModeText) and shows it as a status line at the top
of the Weather Radar form; hidden entirely when the AS switch is off.
Introduces ActiveSkyFormatting (pure, CI-tested text builders) and a
y-cursor layout in WeatherRadarForm.InitializeComponent so later
sections insert without coordinate churn.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Mode-change announcement in `ActiveSkyWeatherMonitor`

**Files:**
- Create: `MSFSBlindAssist/Services/ActiveSkyModeTracker.cs`
- Modify: `MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs`
- Test: `tests/MSFSBlindAssist.Tests/ActiveSkyModeTrackerTests.cs`

**Interfaces:**
- Consumes: `ActiveSkyFormatting.ParseModeText` (Task 1), `ActiveSkyClient.LastModeText` (Task 1), monitor's `_announcer.Announce`.
- Produces: `internal sealed class ActiveSkyModeTracker` with `public string? Observe(string? rawModeText)` — returns announcement text or null for silence. Baseline survives unreachable gaps by design (spec §3.3: reconnect in a different mode announces).

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/ActiveSkyModeTrackerTests.cs`:

```csharp
// Pins the baseline-first mode-change announcement rules (spec 2026-07-10 §3.3):
// silent on first sight, silent on unchanged, announce once per genuine change,
// failed reads are never a change, baseline SURVIVES unreachable gaps so a
// reconnect in a different mode announces.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyModeTrackerTests
{
    private const string Live = "Live Real time mode (Active) (2026/7/10 1935z)";
    private const string LiveLater = "Live Real time mode (Active) (2026/7/10 2050z)";
    private const string Custom = "Custom static mode (Active) (2026/7/10 2100z)";

    [Fact]
    public void First_successful_read_baselines_silently()
        => Assert.Null(new ActiveSkyModeTracker().Observe(Live));

    [Fact]
    public void Unchanged_mode_stays_silent_even_as_weather_clock_advances()
    {
        var t = new ActiveSkyModeTracker();
        t.Observe(Live);
        Assert.Null(t.Observe(LiveLater));
    }

    [Fact]
    public void Mode_change_announces_once_then_goes_silent()
    {
        var t = new ActiveSkyModeTracker();
        t.Observe(Live);
        Assert.Equal("ActiveSky weather mode changed to Custom static mode.", t.Observe(Custom));
        Assert.Null(t.Observe(Custom));
    }

    [Fact]
    public void Change_back_announces_again()
    {
        var t = new ActiveSkyModeTracker();
        t.Observe(Live);
        t.Observe(Custom);
        Assert.Equal("ActiveSky weather mode changed to Live Real time mode.", t.Observe(Live));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Failed_or_empty_reads_are_never_a_change_and_never_baseline(string? bad)
    {
        var t = new ActiveSkyModeTracker();
        Assert.Null(t.Observe(bad));       // no baseline consumed
        Assert.Null(t.Observe(Live));      // first REAL read still baselines silently
        Assert.Null(t.Observe(bad));       // mid-session bad read: silent
        Assert.Equal("ActiveSky weather mode changed to Custom static mode.", t.Observe(Custom));
    }

    [Fact]
    public void Reconnect_in_a_different_mode_announces()
    {
        // Simulates: baseline Live → AS closed (no Observe calls happen while
        // unreachable; the monitor only calls Observe after IsRunningAsync()==true)
        // → AS reopened in Custom. The baseline survived, so this is a change.
        var t = new ActiveSkyModeTracker();
        t.Observe(Live);
        Assert.Equal("ActiveSky weather mode changed to Custom static mode.", t.Observe(Custom));
    }
}
```

- [ ] **Step 2: Run to verify RED** — build fails: `ActiveSkyModeTracker` missing.

- [ ] **Step 3: Create `MSFSBlindAssist/Services/ActiveSkyModeTracker.cs`**

```csharp
namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure decision logic for the ActiveSky mode-change announcement, extracted
/// from the weather monitor so the silence rules are pinned in CI
/// (ActiveSkyModeTrackerTests). Baseline-first: the first successful read is
/// silent; only a genuine parsed-mode change announces. The baseline
/// deliberately SURVIVES unreachable gaps — AS coming back in a different mode
/// is a change the pilot must hear (design doc 2026-07-10 §3.3).
/// </summary>
internal sealed class ActiveSkyModeTracker
{
    private string? _baselineMode;

    /// <summary>Call only after a successful liveness probe, with the fresh
    /// LastModeText. Returns the announcement, or null for silence.</summary>
    public string? Observe(string? rawModeText)
    {
        if (string.IsNullOrWhiteSpace(rawModeText)) return null;   // failed body read ≠ change
        string mode = ActiveSkyFormatting.ParseModeText(rawModeText).ModeName;
        if (mode == "unknown") return null;
        if (_baselineMode == null) { _baselineMode = mode; return null; }
        if (mode == _baselineMode) return null;
        _baselineMode = mode;
        return $"ActiveSky weather mode changed to {mode}.";
    }
}
```

- [ ] **Step 4: Run tests to verify GREEN** (filter `ActiveSkyModeTrackerTests`), then the full suite.

- [ ] **Step 5: Wire into the monitor tick**

In `MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs`:

5a. Add a field next to `_lastAnnouncedAt`:

```csharp
    private readonly ActiveSkyModeTracker _modeTracker = new();
```

5b. In `OnTickAsync`, immediately AFTER the `if (!await _activeSky.IsRunningAsync()) { ... return; }` block (do NOT touch that block — the mode baseline intentionally does not reset there), insert:

```csharp
            // Mode-change announce (baseline-first, silent on first sight). The
            // probe inside IsRunningAsync just refreshed LastModeText, so this
            // costs no extra request. Announced even when the weather fetch
            // below fails — mode is independent of weather data.
            string? modeChange = _modeTracker.Observe(_activeSky.LastModeText);
            if (modeChange != null && !_disposed)
            {
                Log.Debug("Services", $"mode change: \"{modeChange}\"");
                _announcer.Announce(modeChange);
            }
```

- [ ] **Step 6: Build solution + full suite** — all green, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(weather): announce ActiveSky mode changes (baseline-first)

Piggybacks on the existing 60s monitor tick and the LastModeText the
liveness probe already refreshes. Silent at startup and across
unreachable gaps; a reconnect in a different mode announces. Decision
logic extracted pure (ActiveSkyModeTracker) and pinned in CI.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: `LastStatus` diagnostics line in the Weather settings panel

**Files:**
- Modify: `MSFSBlindAssist/Forms/Settings/WeatherPanel.cs`
- Test: `tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs` (extend)

**Interfaces:**
- Consumes: `ActiveSkyClient.IsRunningAsync()`, `LastStatus`.
- Produces: `internal Task WeatherPanel.RefreshActiveSkyStatusAsync()` (exposed for the test); a Label with AccessibleName `"ActiveSky status"` inside the ActiveSky group.

- [ ] **Step 1: Write the failing test**

Append to `tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs` (inside the existing class, following its existing `FindByAccessibleName` helper and settings-collection pattern — read the file first and match its fixture/collection attributes exactly):

```csharp
    [Fact]
    public async Task Status_line_shows_disabled_when_activesky_is_off()
    {
        var saved = SettingsManager.Current.ActiveSkyEnabled;
        try
        {
            SettingsManager.Current.ActiveSkyEnabled = false;   // central gate → instant, no probe
            using var panel = new WeatherPanel();
            panel.LoadFrom(new UserSettings { ActiveSkyEnabled = false });
            await panel.RefreshActiveSkyStatusAsync();

            var label = FindByAccessibleName(panel, "ActiveSky status");
            Assert.NotNull(label);
            Assert.Equal("ActiveSky status: disabled in settings", label!.Text);
        }
        finally
        {
            SettingsManager.Current.ActiveSkyEnabled = saved;
        }
    }
```

(If the existing file's helper takes different parameters, adapt the call — the assertion contract is what matters: label exists, text is exactly `"ActiveSky status: disabled in settings"`.)

- [ ] **Step 2: Verify RED** — build fails: `RefreshActiveSkyStatusAsync` missing.

- [ ] **Step 3: Implement in `WeatherPanel.cs`**

3a. Add fields:

```csharp
    private Label _asStatusLabel = null!;
    private readonly Services.ActiveSkyClient _statusClient = new();
```

3b. In `BuildActiveSkyGroup()`, change the GroupBox `Size = new System.Drawing.Size(460, 96)` to `Size = new System.Drawing.Size(460, 120)` and, after `group.Controls.Add(_activeSkyEnabled);`, add:

```csharp
    _asStatusLabel = new Label
    {
        Text = "ActiveSky status: not yet checked",
        Location = new System.Drawing.Point(12, 76),
        Size = new System.Drawing.Size(430, 20),
        AccessibleName = "ActiveSky status",
        AccessibleDescription = "Result of the last ActiveSky connection check"
    };
    group.Controls.Add(_asStatusLabel);
```

(The announcements group below repositions automatically — it is placed at `activeSkyGroup.Bottom + 12`.)

3c. Add the refresh method and call it from `LoadFrom` (fire-and-forget — the panel shows "checking…" until the probe lands):

```csharp
    /// <summary>Probes AS and shows ActiveSkyClient.LastStatus. Reflects the SAVED
    /// setting (the central gate reads SettingsManager.Current), not the unapplied
    /// checkbox state. Internal so the test can await it deterministically.</summary>
    internal async Task RefreshActiveSkyStatusAsync()
    {
        _asStatusLabel.Text = "ActiveSky status: checking…";
        await _statusClient.IsRunningAsync();
        if (!IsDisposed)
            _asStatusLabel.Text = $"ActiveSky status: {_statusClient.LastStatus}";
    }
```

At the end of `LoadFrom(UserSettings settings)` add:

```csharp
    _ = RefreshActiveSkyStatusAsync();
```

- [ ] **Step 4: Verify GREEN** (filter `WeatherPanelTests`), then full suite + solution build.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(settings): show ActiveSky connection status in Weather panel

Surfaces ActiveSkyClient.LastStatus (\"detected on port 19285\" /
\"port 19285: timeout\" / \"disabled in settings\") as a read-only line
under the enable checkbox — closing the doc-comment's unfulfilled
'surfaced to the UI' promise.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Position temperature/dew-point line in the Weather Radar form

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs` (add `BuildTempDewLine`)
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs` (one call in `FormatAmbientFromActiveSky`)
- Test: `tests/MSFSBlindAssist.Tests/ActiveSkyFormattingTests.cs` (extend)

**Interfaces:**
- Consumes: `ActiveSkyWeatherMonitor.DecodeMetar` (already `internal static`; returns `DecodedMetar` with `int? TemperatureC` / `int? DewPointC` fields).
- Produces: `internal static string? ActiveSkyFormatting.BuildTempDewLine(string? positionMetar)` — null when no line should render.

- [ ] **Step 1: Write the failing tests** (append to `ActiveSkyFormattingTests`)

```csharp
    // --- BuildTempDewLine ---------------------------------------------------------------

    [Fact]
    public void TempDew_line_from_position_metar()
        => Assert.Equal("Temperature/dew point: 36 / 12°C",
            ActiveSkyFormatting.BuildTempDewLine(
                "@POS 101905Z 22009KT 10SM 36/12 A3001 RMK ADVANCED INTERPOLATION"));

    [Fact]
    public void TempDew_line_handles_negative_values()
        => Assert.Equal("Temperature/dew point: -5 / -8°C",
            ActiveSkyFormatting.BuildTempDewLine(
                "PANC 071751Z 30012KT 1SM SHSN OVC008 M05/M08 A2990"));

    [Fact]
    public void TempDew_line_with_missing_dew_point_shows_temperature_only()
        => Assert.Equal("Temperature/dew point: 28°C",
            ActiveSkyFormatting.BuildTempDewLine(
                "@POS 101905Z 22009KT 10SM 28/ A3001 RMK ADVANCED INTERPOLATION"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@POS 101905Z 22009KT 10SM A3001")]   // no temp group at all
    public void TempDew_line_is_omitted_when_unavailable(string? metar)
        => Assert.Null(ActiveSkyFormatting.BuildTempDewLine(metar));
```

NOTE: the `28/` (missing dew) and no-temp-group vectors are characterization — if `DecodeMetar`'s actual behavior on those inputs differs (e.g. it rejects `28/` entirely), adjust the EXPECTED value to what `DecodeMetar` really produces and note it in the test comment. Never change `DecodeMetar` itself in this task.

- [ ] **Step 2: Verify RED** — `BuildTempDewLine` missing.

- [ ] **Step 3: Implement** (append to `ActiveSkyFormatting`)

```csharp
    /// <summary>
    /// "Temperature/dew point: 36 / 12°C" from the AS position METAR — the dew
    /// point exists nowhere else (no SimConnect dew SimVar; the AS JSON ambient
    /// block has no dew field). Null when the METAR has no temperature group.
    /// </summary>
    internal static string? BuildTempDewLine(string? positionMetar)
    {
        if (string.IsNullOrWhiteSpace(positionMetar)) return null;
        var d = ActiveSkyWeatherMonitor.DecodeMetar(positionMetar);
        if (d.TemperatureC.HasValue && d.DewPointC.HasValue)
            return $"Temperature/dew point: {d.TemperatureC} / {d.DewPointC}°C";
        if (d.TemperatureC.HasValue)
            return $"Temperature/dew point: {d.TemperatureC}°C";
        return null;
    }
```

- [ ] **Step 4: Verify GREEN**, adjusting characterization vectors per the Step 1 note if needed.

- [ ] **Step 5: Wire into the radar form**

In `WeatherRadarForm.FormatAmbientFromActiveSky`, after the `sb.AppendLine($"Surface temperature: {c.SurfaceTemperature:F0}°C");` line, insert:

```csharp
        string? tempDew = MSFSBlindAssist.Services.ActiveSkyFormatting.BuildTempDewLine(positionMetar);
        if (tempDew != null)
            sb.AppendLine(tempDew);
```

- [ ] **Step 6: Full suite + solution build** — green, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(weather): position dew point line in Weather Radar form

Decoded from the position METAR the form already fetches — zero new
requests. Dew point is otherwise unavailable anywhere in the app.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: On-demand closest-station decoded weather box

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs` (extract `BuildDecodedWeatherText`, promote to internal)
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs` (new section + tuple fetch)
- Test: `tests/MSFSBlindAssist.Tests/ActiveSkyStationDecodedTests.cs`

**Interfaces:**
- Consumes: monitor's `BuildAnnouncement` internals, `DecodeMetar`, `ActiveSkyClient.Conditions`, the Task 1 y-cursor layout.
- Produces: `internal static string ActiveSkyWeatherMonitor.BuildDecodedWeatherText(string metar, ActiveSkyClient.Conditions conditions)` — the "Decoded weather at {station}. …" portion WITHOUT the auto-announce preamble; `WeatherRadarForm._stationLabel`/`_stationBox` (AccessibleName "Closest Station Weather"); `FetchAmbientAsync` returns `(string ambient, string station)`.

- [ ] **Step 1: Extract `BuildDecodedWeatherText` from `BuildAnnouncement` (mechanical refactor, no wording changes)**

Read `BuildAnnouncement` (~line 275). Its shape is: (a) choose `metarToUse` = closest-station METAR if non-blank else position METAR; (b) decode + derive the station label; (c) assemble a `List<string> parts` ("Decoded weather at X." + wind/visibility/clouds/precip/temperature/dew/altimeter sentences) joined with `" "`; (d) any leading "weather updated"-style preamble sentence(s).

Refactor so that steps (b)+(c) — everything from the decode down to the final join, EXCLUDING any preamble sentence(s) that come before "Decoded weather at" — move verbatim into:

```csharp
    /// <summary>The "Decoded weather at {station}. Wind… Altimeter…" text, shared
    /// verbatim between the auto-announce and the Weather Radar form's on-demand
    /// closest-station box (spec 2026-07-10 §4.1: identical wording, one code path).
    /// Internal for the form + tests.</summary>
    internal static string BuildDecodedWeatherText(string metar, ActiveSkyClient.Conditions conditions)
```

`BuildAnnouncement` keeps steps (a)+(d) and ends with `... + BuildDecodedWeatherText(metarToUse, conditions)`. DO NOT reword, reorder, add, or drop any string literal — this is a pure extraction. If a literal must move, move it verbatim.

- [ ] **Step 2: Write the pin + new-surface tests**

Create `tests/MSFSBlindAssist.Tests/ActiveSkyStationDecodedTests.cs`:

```csharp
// Pins the extracted BuildDecodedWeatherText (shared by the auto-announce and the
// Weather Radar form's closest-station box) against a live-captured METAR.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyStationDecodedTests
{
    private const string StationMetar = "KDUX 101915Z AUTO 22009KT 10SM CLR 36/12 A3001 RMK AO2";

    private static ActiveSkyClient.Conditions Conditions() => new()
    {
        SurfaceWindDirection = 220, SurfaceWindSpeed = 9, QnhMb = 1016,
    };

    [Fact]
    public void Decoded_text_names_the_station_and_core_elements()
    {
        string text = ActiveSkyWeatherMonitor.BuildDecodedWeatherText(StationMetar, Conditions());
        Assert.Contains("KDUX", text);
        Assert.Contains("220 at 9", text);          // wind
        Assert.Contains("36", text);                // temperature
        Assert.Contains("12", text);                // dew point
        Assert.StartsWith("Decoded weather at", text);
        Assert.DoesNotContain("updated", text, StringComparison.OrdinalIgnoreCase);  // preamble stays in BuildAnnouncement
    }

    [Fact]
    public void Position_metar_is_labeled_your_position()
    {
        string text = ActiveSkyWeatherMonitor.BuildDecodedWeatherText(
            "@POS 101905Z 22009KT 10SM 36/12 A3001 RMK ADVANCED INTERPOLATION", Conditions());
        Assert.Contains("your position", text);
    }
}
```

NOTE: these are structural pins (Contains), not exact-string goldens, because the pre-existing wording is authoritative — if an assertion fails, check whether the extraction changed wording (fix the extraction) rather than editing the expected value. The existing `ActiveSkyWeatherMonitorGateTests` must also stay green.

- [ ] **Step 3: Run** — RED first (method missing) if tests were written before the refactor compiles; then GREEN after Steps 1+2 are both in. Run the FULL suite — the pre-existing monitor tests are the real safety net for the extraction.

- [ ] **Step 4: Add the station section to the Weather Radar form**

4a. Fields:

```csharp
    private Label _stationLabel = null!;
    private TextBox _stationBox = null!;
```

4b. In `InitializeComponent()`, insert between the current-weather block and the advisories block (after the `y += 100 + 12;` line):

```csharp
        // ── Closest station (ActiveSky only; hidden when AS is off) ───────
        _stationLabel = new Label
        {
            Text = "Closest Station Weather (ActiveSky):",
            Location = new Point(12, y),
            Size = new Size(570, 20),
            AccessibleName = "Closest Station Weather label",
            Visible = false
        };
        y += 24;

        _stationBox = new TextBox
        {
            Location = new Point(12, y),
            Size = new Size(566, 110),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = "",
            AccessibleName = "Closest Station Weather",
            AccessibleDescription = "Decoded weather and raw METAR at the nearest reporting station, from ActiveSky",
            Visible = false
        };
        y += 110 + 12;
```

Add `_stationLabel, _stationBox,` to `Controls.AddRange` (after `_currentWeatherBox`). Grow the form shell `Size` height by 146 (new total: `new Size(600, 880)`).

4c. Tab order in `SetupAccessibility()` — insert after `_currentWeatherBox.TabIndex = 0;`:

```csharp
        _stationBox.TabIndex = 1;
        _advisoriesBox.TabIndex = 2;
        _windsAloftBox.TabIndex = 3;
        _decodeCheckBox.TabIndex = 4;
        _refreshButton.TabIndex = 5;
        _closeButton.TabIndex = 6;
```

(Replace the old indices 1–5 lines.)

4d. Change `FetchAmbientAsync()`'s signature to `private async Task<(string ambient, string station)> FetchAmbientAsync()`. In its AS branch (where `asConditions`, `posMetar`, `stationMetar` are already in scope), replace the `return FormatAmbientFromActiveSky(...)` line with:

```csharp
            if (asConditions != null)
            {
                string ambient = FormatAmbientFromActiveSky(asConditions, simData, simConnected, posMetar, stationMetar);
                string station = string.IsNullOrWhiteSpace(stationMetar)
                    ? "unavailable"
                    : MSFSBlindAssist.Services.ActiveSkyWeatherMonitor.BuildDecodedWeatherText(stationMetar, asConditions)
                      + Environment.NewLine + Environment.NewLine
                      + "Raw METAR:" + Environment.NewLine + stationMetar.Trim();
                return (ambient, station);
            }
```

The two non-AS returns become `return ("Not connected to simulator.", "unavailable");` and `return (WeatherService.FormatAmbientWeather(simData), "unavailable");` — "unavailable" covers the AS-enabled-but-conditions-fetch-failed fallthrough (spec: per-section degradation); when the AS switch is off the station box is hidden, so the text is never shown.

4e. In `RefreshAsync`: extend the visibility block from Task 1 Step 6d with:

```csharp
            _stationLabel.Visible = asEnabled;
            _stationBox.Visible = asEnabled;
```

and change the box assignment to unpack the tuple:

```csharp
            (string ambientText, string stationText) = await ambientTask;
            _currentWeatherBox.Text = ambientText;
            _stationBox.Text = stationText;
```

- [ ] **Step 5: Full suite + solution build** — green, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(weather): on-demand closest-station decoded weather box

The ATIS-style decoded readout that previously existed only as the
auto-announce is now readable in the Weather Radar form, with the
station ICAO and raw METAR — same code path (BuildDecodedWeatherText
extracted from BuildAnnouncement, wording unchanged), so the form and
the announce can never diverge.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Forecast METAR combo in the METAR form (Shift+M)

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs` (presets + caption)
- Modify: `MSFSBlindAssist/Forms/METARReportForm.cs`
- Test: `tests/MSFSBlindAssist.Tests/ActiveSkyFormattingTests.cs` (extend)

**Interfaces:**
- Consumes: `ActiveSkyClient.GetMetarAsync(string icao, int timeOffsetSec = 0)` (existing, offset already plumbed).
- Produces: `internal static readonly (string Label, int OffsetSeconds)[] ActiveSkyFormatting.ForecastPresets`; `internal static string ActiveSkyFormatting.BuildAsMetarCaption(int presetIndex)`.

- [ ] **Step 1: Write the failing tests** (append to `ActiveSkyFormattingTests`)

```csharp
    // --- Forecast presets ----------------------------------------------------------------

    [Fact]
    public void Forecast_presets_cover_now_through_six_hours()
    {
        Assert.Equal(new[] { 0, 3600, 7200, 14400, 21600 },
            ActiveSkyFormatting.ForecastPresets.Select(p => p.OffsetSeconds).ToArray());
        Assert.Equal("Now", ActiveSkyFormatting.ForecastPresets[0].Label);
        Assert.Equal("+6 hours", ActiveSkyFormatting.ForecastPresets[^1].Label);
    }

    [Theory]
    [InlineData(0, "ActiveSky METAR:")]
    [InlineData(2, "ActiveSky METAR (+2 hours):")]
    [InlineData(99, "ActiveSky METAR (+6 hours):")]   // clamped
    [InlineData(-1, "ActiveSky METAR:")]              // clamped
    public void As_metar_caption_states_the_offset(int index, string expected)
        => Assert.Equal(expected, ActiveSkyFormatting.BuildAsMetarCaption(index));
```

- [ ] **Step 2: Verify RED**, then implement (append to `ActiveSkyFormatting`):

```csharp
    /// <summary>Forecast offsets for the METAR form's combo. AS synthesizes a
    /// forecast METAR from current METAR + TAF at the given offset (live-verified:
    /// distinct output at +4h/+12h/+24h).</summary>
    internal static readonly (string Label, int OffsetSeconds)[] ForecastPresets =
    {
        ("Now", 0),
        ("+1 hour", 3600),
        ("+2 hours", 7200),
        ("+4 hours", 14400),
        ("+6 hours", 21600),
    };

    /// <summary>Caption for the AS METAR box stating which offset it shows.</summary>
    internal static string BuildAsMetarCaption(int presetIndex)
    {
        var p = ForecastPresets[Math.Clamp(presetIndex, 0, ForecastPresets.Length - 1)];
        return p.OffsetSeconds == 0 ? "ActiveSky METAR:" : $"ActiveSky METAR ({p.Label}):";
    }
```

- [ ] **Step 3: Verify GREEN**, full suite.

- [ ] **Step 4: Add the combo to `METARReportForm`**

4a. Fields (next to the other controls):

```csharp
    private Label forecastLabel = null!;
    private ComboBox forecastCombo = null!;
```

4b. In `InitializeComponent()`, after the `statusLabel` construction, add (both start hidden — they share the AS section's visibility; statusLabel moves right to make room):

```csharp
        // Forecast offset — AS-only feature, revealed with the AS METAR section.
        forecastLabel = new Label
        {
            Text = "Forecast:",
            Location = new Point(140, 20),
            Size = new Size(80, 20),
            AccessibleName = "Forecast label",
            Visible = false
        };

        forecastCombo = new ComboBox
        {
            Location = new Point(140, 45),
            Size = new Size(110, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Forecast",
            AccessibleDescription = "Show the ActiveSky METAR as of now or a forecast offset up to six hours ahead",
            Visible = false
        };
        foreach (var (label, _) in ActiveSkyFormatting.ForecastPresets)
            forecastCombo.Items.Add(label);
        forecastCombo.SelectedIndex = 0;
        forecastCombo.SelectedIndexChanged += async (_, _) =>
        {
            // Re-fetch with the new offset when a lookup is already on screen.
            // No announcement — the screen reader already speaks the selection.
            if (icaoTextBox.Text.Trim().Length == 4 && icaoTextBox.Enabled)
                await FetchMETAR();
        };
```

Move `statusLabel`'s `Location` from `new Point(130, 45)` to `new Point(260, 45)` and shrink its `Size` to `new Size(200, 25)`.

Add `forecastLabel, forecastCombo,` to the `Controls.AddRange` array (after `statusLabel`).

4c. Tab order in `SetupAccessibility()` — replace the four assignments with:

```csharp
        icaoTextBox.TabIndex = 0;
        forecastCombo.TabIndex = 1;
        metarTextBox.TabIndex = 2;
        asMetarTextBox.TabIndex = 3;
        closeButton.TabIndex = 4;
```

4d. In the `Load` handler's AS-reveal block (`if (asAvailable && IsHandleCreated && !IsDisposed)`), add:

```csharp
            forecastLabel.Visible = true;
            forecastCombo.Visible = true;
```

4e. In `FetchMETAR()`:
- Where the AS task is started (`asMetarTextBox.Visible ? _activeSky.GetMetarAsync(icao) : ...`), replace with:

```csharp
        int presetIndex = forecastCombo.Visible ? Math.Max(0, forecastCombo.SelectedIndex) : 0;
        Task<string?> asTask = asMetarTextBox.Visible
            ? _activeSky.GetMetarAsync(icao, ActiveSkyFormatting.ForecastPresets[
                Math.Clamp(presetIndex, 0, ActiveSkyFormatting.ForecastPresets.Length - 1)].OffsetSeconds)
            : Task.FromResult<string?>(null);
```

- In the AS result block (`if (asMetarTextBox.Visible)`), before assigning the box text, add:

```csharp
            asMetarLabel.Text = ActiveSkyFormatting.BuildAsMetarCaption(presetIndex);
```

Also add `using MSFSBlindAssist.Services;` if not already present (it is — `_activeSky` uses it).

- [ ] **Step 5: Full suite + solution build** — green, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(weather): forecast METAR offsets in the Shift+M form

A Now/+1h/+2h/+4h/+6h combo (AS-only, revealed with the AS section)
drives GetMetarInfoAt's already-plumbed timeoffset parameter; the AS
box caption states the offset in effect. VATSIM box always shows now.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: `GetAtmosphereAsync` + Winds Aloft re-source (AS-truth with temperatures)

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyClient.cs` (AtmosphereLevel, parser, wrapper)
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs` (altitude window + text builder)
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs` (`FetchWindsAloftAsync` re-source + source tags)
- Test: `tests/MSFSBlindAssist.Tests/ActiveSkyAtmosphereTests.cs`

**Interfaces:**
- Consumes: existing `ReadDouble(JsonElement, string)` helper; `_activeSkyAvailable` in the form.
- Produces: `public sealed class ActiveSkyClient.AtmosphereLevel { public int AltitudeFt; public double WindDirection; public double WindSpeed; public double TemperatureC; }` (public fields); `internal static List<AtmosphereLevel>? ActiveSkyClient.ParseAtmosphereJson(string json)`; `public Task<List<AtmosphereLevel>?> ActiveSkyClient.GetAtmosphereAsync(double lat, double lon, IEnumerable<int> altitudesFt)`; `internal static int[] ActiveSkyFormatting.WindsAloftAltitudes(int aircraftAltFt)`; `internal static string ActiveSkyFormatting.BuildWindsAloftText(int aircraftAltFt, IReadOnlyList<ActiveSkyClient.AtmosphereLevel> levels)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/ActiveSkyAtmosphereTests.cs`:

```csharp
// GetAtmosphere JSON parsing + the AS-sourced Winds Aloft text. The JSON golden is a
// live capture (2026-07-10, ?altitudes=0|5000|10000|18000|24000|34000).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyAtmosphereTests
{
    private const string Fixture = """
{
  "WeatherData": [
    { "Altitude": "0", "WindDirection": "208.0", "WindSpeed": "4.0", "Temperature": "37.3", "Pressure": "1014.7" },
    { "Altitude": "5000", "WindDirection": "190.0", "WindSpeed": "10.0", "Temperature": "31.3", "Pressure": "844.5" },
    { "Altitude": "10000", "WindDirection": "203.0", "WindSpeed": "9.0", "Temperature": "21.5", "Pressure": "698.2" },
    { "Altitude": "18000", "WindDirection": "316.0", "WindSpeed": "19.0", "Temperature": "-7.9", "Pressure": "507.2" },
    { "Altitude": "24000", "WindDirection": "287.0", "WindSpeed": "26.0", "Temperature": "-17.8", "Pressure": "393.8" },
    { "Altitude": "34000", "WindDirection": "286.0", "WindSpeed": "20.0", "Temperature": "-37.1", "Pressure": "250.9" }
  ]
}
""";

    [Fact]
    public void ParseAtmosphereJson_reads_all_levels_with_invariant_culture()
    {
        var levels = ActiveSkyClient.ParseAtmosphereJson(Fixture);
        Assert.NotNull(levels);
        Assert.Equal(6, levels!.Count);
        Assert.Equal(0, levels[0].AltitudeFt);
        Assert.Equal(208.0, levels[0].WindDirection);
        Assert.Equal(34000, levels[5].AltitudeFt);
        Assert.Equal(-37.1, levels[5].TemperatureC, 3);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"WeatherData\": \"nope\"}")]
    public void ParseAtmosphereJson_degrades_to_null_or_empty_on_bad_input(string bad)
    {
        var levels = ActiveSkyClient.ParseAtmosphereJson(bad);
        Assert.True(levels == null || levels.Count == 0);
    }

    [Fact]
    public void WindsAloftAltitudes_mirrors_the_open_meteo_window()
        => Assert.Equal(new[] { 31000, 32000, 33000, 34000, 35000, 36000, 37000, 38000, 39000, 40000, 41000 },
            ActiveSkyFormatting.WindsAloftAltitudes(36000));

    [Fact]
    public void WindsAloftAltitudes_clamps_at_ground()
        => Assert.Equal(0, ActiveSkyFormatting.WindsAloftAltitudes(2000)[0]);

    [Fact]
    public void BuildWindsAloftText_formats_levels_with_temperature_marker_and_source()
    {
        var levels = ActiveSkyClient.ParseAtmosphereJson(Fixture)!;
        string text = ActiveSkyFormatting.BuildWindsAloftText(34200, levels);
        Assert.Contains("34,000 ft:  286° / 20 kts, -37°C (nearest)", text);
        Assert.Contains("0 ft:  208° / 4 kts, 37°C", text);
        Assert.EndsWith("Source: ActiveSky", text);
    }
}
```

- [ ] **Step 2: Verify RED** — missing types/members.

- [ ] **Step 3: Implement the client side**

In `ActiveSkyClient.cs`, add after the `Conditions` class:

```csharp
    /// <summary>One level from /GetAtmosphere — sim-truth wind + temperature.</summary>
    public sealed class AtmosphereLevel
    {
        public int AltitudeFt;
        public double WindDirection;
        public double WindSpeed;
        public double TemperatureC;
    }
```

Add the parser (near `ParseConditionsJson`; internal for tests):

```csharp
    /// <summary>Parses /GetAtmosphere JSON ({"WeatherData":[{Altitude,WindDirection,
    /// WindSpeed,Temperature,Pressure}]} — all values strings, invariant culture).
    /// Null on malformed input; permissive per-field like ParseConditionsJson.</summary>
    internal static List<AtmosphereLevel>? ParseAtmosphereJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("WeatherData", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                return null;
            var list = new List<AtmosphereLevel>();
            foreach (var el in arr.EnumerateArray())
            {
                list.Add(new AtmosphereLevel
                {
                    AltitudeFt = (int)Math.Round(ReadDouble(el, "Altitude")),
                    WindDirection = ReadDouble(el, "WindDirection"),
                    WindSpeed = ReadDouble(el, "WindSpeed"),
                    TemperatureC = ReadDouble(el, "Temperature"),
                });
            }
            return list;
        }
        catch
        {
            return null;
        }
    }
```

Add the wrapper (same shape as the existing five):

```csharp
    /// <summary>
    /// /GetAtmosphere?lat=&lon=&altitudes=a|b|c — sim-truth winds/temps at the
    /// requested altitudes (feet, pipe-separated; invariant decimal separators
    /// per the AS API doc). Null on error or when AS is off/unreachable.
    /// </summary>
    public async Task<List<AtmosphereLevel>?> GetAtmosphereAsync(double lat, double lon, IEnumerable<int> altitudesFt)
    {
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled) return null;   // master switch — no AS I/O when off
        if (LastSuccessfulPort is not int port) return null;
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            string url = $"{BaseUrl(port)}/GetAtmosphere"
                + $"?lat={lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"
                + $"&lon={lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"
                + $"&altitudes={string.Join("|", altitudesFt)}&timeoffset=0";
            using var resp = await _http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            return ParseAtmosphereJson(await resp.Content.ReadAsStringAsync(cts.Token));
        }
        catch
        {
            Log.Debug("ActiveSky", "GetAtmosphere failed (timeout or connection error)");
            return null;
        }
    }
```

- [ ] **Step 4: Implement the formatting side** (append to `ActiveSkyFormatting`)

```csharp
    /// <summary>The Winds Aloft box's altitude set: ±5000 ft of the aircraft in
    /// 1000-ft steps, clamped at 0 — kept identical to the Open-Meteo path's
    /// window (WeatherService.ParseWindsAloft) so switching source never
    /// changes which levels the pilot hears.</summary>
    internal static int[] WindsAloftAltitudes(int aircraftAltFt)
    {
        int lowAlt = (int)Math.Max(0, Math.Round((aircraftAltFt - 5000) / 1000.0) * 1000);
        int highAlt = (int)Math.Round((aircraftAltFt + 5000) / 1000.0) * 1000;
        var list = new List<int>();
        for (int a = lowAlt; a <= highAlt; a += 1000) list.Add(a);
        return list.ToArray();
    }

    /// <summary>AS-sourced Winds Aloft text — same layout as the Open-Meteo path
    /// plus per-level temperature and the source tag line.</summary>
    internal static string BuildWindsAloftText(int aircraftAltFt, IReadOnlyList<ActiveSkyClient.AtmosphereLevel> levels)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Aircraft: {aircraftAltFt:N0} ft  |  forecast winds:");
        sb.AppendLine(new string('─', 36));
        foreach (var w in levels)
        {
            string marker = Math.Abs(w.AltitudeFt - aircraftAltFt) < 500 ? " (nearest)" : "";
            sb.AppendLine($"{w.AltitudeFt:N0} ft:  {w.WindDirection:F0}° / {w.WindSpeed:F0} kts, {w.TemperatureC:F0}°C{marker}");
        }
        sb.AppendLine("Source: ActiveSky");
        return sb.ToString().TrimEnd();
    }
```

- [ ] **Step 5: Verify GREEN** (filter `ActiveSkyAtmosphereTests`), then full suite.

- [ ] **Step 6: Re-source the form's winds box**

In `WeatherRadarForm.FetchWindsAloftAsync`, after the position guard (`if (lat == 0 && lon == 0) ...`), insert the AS branch BEFORE the existing Open-Meteo call:

```csharp
        // Per-engine wind truth (docs/weather.md §3): with AS enabled and
        // reachable, the box shows what the sim is actually flying through —
        // AS historic/custom weather can contradict live internet data. The
        // Open-Meteo path below is the unchanged non-AS fallback.
        if (_activeSkyAvailable == true)
        {
            var asLevels = await _activeSky.GetAtmosphereAsync(
                lat, lon, MSFSBlindAssist.Services.ActiveSkyFormatting.WindsAloftAltitudes(altFt));
            if (asLevels is { Count: > 0 })
                return MSFSBlindAssist.Services.ActiveSkyFormatting.BuildWindsAloftText(altFt, asLevels);
            // AS answered the probe but not this call — fall through to Open-Meteo.
        }
```

And in the existing Open-Meteo formatting block of the same method, add the source tag: change the final `return sb.ToString().TrimEnd();` to:

```csharp
        sb.AppendLine("Source: Open-Meteo");
        return sb.ToString().TrimEnd();
```

- [ ] **Step 7: Full suite + solution build** — green, 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(weather): AS-truth winds and temperatures aloft

With ActiveSky enabled the Winds Aloft box reads /GetAtmosphere (what
the sim actually flies through — historic/custom AS weather diverges
from live internet data), adding per-level temperatures; Open-Meteo is
the unchanged fallback. Both paths carry a source tag line.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: `GetWeatherInfoXmlAsync` + vertical profile box

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyClient.cs` (profile models, XML parser, wrapper)
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs` (narrative builder + enum maps)
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs` (profile section + fetch)
- Test: `tests/MSFSBlindAssist.Tests/ActiveSkyProfileTests.cs`

**Interfaces:**
- Consumes: Task 1's y-cursor layout, Task 5's tab-order block, `_activeSkyAvailable`.
- Produces:
  - `public sealed class ActiveSkyClient.VerticalProfile { public List<ProfileWindLayer> WindLayers; public List<ProfileCloudLayer> CloudLayers; }`
  - `public sealed class ActiveSkyClient.ProfileWindLayer { public bool IsSurface; public int AltitudeFt; public double DirectionDeg; public double SpeedKts; public double TemperatureC; public int TurbulenceEnum; public double GustKts; }`
  - `public sealed class ActiveSkyClient.ProfileCloudLayer { public int BaseFt; public int TopFt; public int CoverageOktas; public int IcingEnum; public int PrecipType; public int TurbulenceEnum; }`
  - `internal static VerticalProfile? ActiveSkyClient.ParseWeatherInfoXml(string xml)`
  - `public Task<VerticalProfile?> ActiveSkyClient.GetWeatherInfoXmlAsync(double lat, double lon)`
  - `internal static string ActiveSkyFormatting.BuildProfileNarrative(ActiveSkyClient.VerticalProfile p, int aircraftAltFt)` plus enum-word helpers `CoverageWord(int)`, `SeverityWord(int)`, `PrecipWord(int)`, `TempWord(double)`, `SelectCuratedLevels(...)` (all `internal static`).

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/ActiveSkyProfileTests.cs`:

```csharp
// GetWeatherInfoXml parsing + the curated vertical-profile narrative. The XML golden
// is a live capture (2026-07-10, FL360 over the Texas panhandle). Enum conventions
// (FSX lineage, live-verified): wind-layer altitudes are AltFeet (FEET), cloud
// base/top are METRES; Turbulence/CloudTurbulence/Icing are severity enums 0-4;
// Coverage is oktas; PrecipType 0 none / 1 rain / 2 snow.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyProfileTests
{
    private const string LiveFixture =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?><Weather ElevationMeters=\"\"><WindLayers>"
        + "<SurfaceLayer WindDirection=\"208\" WindSpeed=\"4\" Turbulence=\"1\" Temp=\"37.29\" Gust=\"14\" Variance=\"327.00\" />"
        + "<Layer AltFeet=\"3000\" WindDirection=\"187\" WindSpeed=\"9\" Turbulence=\"1\" Temp=\"38.54\" />"
        + "<Layer AltFeet=\"6000\" WindDirection=\"192\" WindSpeed=\"10\" Turbulence=\"1\" Temp=\"29.32\" />"
        + "<Layer AltFeet=\"9000\" WindDirection=\"195\" WindSpeed=\"10\" Turbulence=\"0\" Temp=\"23.50\" />"
        + "<Layer AltFeet=\"12000\" WindDirection=\"226\" WindSpeed=\"7\" Turbulence=\"0\" Temp=\"7.86\" />"
        + "<Layer AltFeet=\"18000\" WindDirection=\"316\" WindSpeed=\"19\" Turbulence=\"0\" Temp=\"-7.92\" />"
        + "<Layer AltFeet=\"24000\" WindDirection=\"287\" WindSpeed=\"26\" Turbulence=\"0\" Temp=\"-17.83\" />"
        + "<Layer AltFeet=\"30000\" WindDirection=\"262\" WindSpeed=\"27\" Turbulence=\"0\" Temp=\"-29.43\" />"
        + "<Layer AltFeet=\"34000\" WindDirection=\"286\" WindSpeed=\"20\" Turbulence=\"0\" Temp=\"-37.08\" />"
        + "<Layer AltFeet=\"39000\" WindDirection=\"267\" WindSpeed=\"16\" Turbulence=\"0\" Temp=\"-49.91\" />"
        + "<Layer AltFeet=\"44000\" WindDirection=\"298\" WindSpeed=\"13\" Turbulence=\"0\" Temp=\"-62.03\" />"
        + "<Layer AltFeet=\"49000\" WindDirection=\"197\" WindSpeed=\"11\" Turbulence=\"0\" Temp=\"-72.96\" />"
        + "<Layer AltFeet=\"56000\" WindDirection=\"114\" WindSpeed=\"14\" Turbulence=\"0\" Temp=\"-60.08\" />"
        + "</WindLayers><SurfaceVisibility VisMeters=\"83271\" VisBaseMeters=\"-1999\" VisTopMeters=\"1218\" />"
        + "<Clouds><Cloud CloudType=\"9\" CloudBaseMeters=\"3974\" CloudTopMeters=\"7530\" Coverage=\"4\" CloudTurbulence=\"1\" PrecipType=\"0\" PrecipRate=\"0\" Icing=\"0\" /></Clouds>"
        + "<QNH ValueHectoPascal=\"1014.63\" ReportsAsQNH=\"0\" /></Weather>";

    // API-doc example variant: richer cloud (broken, moderate cloud turbulence,
    // rain, light icing) and explicit </Layer> closing tags.
    private const string DocFixture =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?><Weather ElevationMeters=\"\"><WindLayers>"
        + "<SurfaceLayer WindDirection=\"205\" WindSpeed=\"15\" Turbulence=\"0\" Temp=\"15\" Gust=\"25\" Variance=\"0\" DewPoint=\"5\" />"
        + "<Layer AltFeet=\"3000\" WindDirection=\"205\" WindSpeed=\"15\" Turbulence=\"0\" Temp=\"15\"></Layer>"
        + "<Layer AltFeet=\"6000\" WindDirection=\"205\" WindSpeed=\"15\" Turbulence=\"0\" Temp=\"15\" />"
        + "</WindLayers>"
        + "<Clouds><Cloud CloudType=\"9\" CloudBaseMeters=\"1000\" CloudTopMeters=\"2000\" Coverage=\"5\" CloudTurbulence=\"2\" PrecipType=\"1\" PrecipRate=\"2\" Icing=\"1\" /></Clouds>"
        + "<QNH ValueHectoPascal=\"1015\" ReportsAsQNH=\"1\" /></Weather>";

    [Fact]
    public void Parse_reads_wind_layers_with_surface_first()
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(LiveFixture);
        Assert.NotNull(p);
        Assert.Equal(13, p!.WindLayers.Count);
        Assert.True(p.WindLayers[0].IsSurface);
        Assert.Equal(0, p.WindLayers[0].AltitudeFt);
        Assert.Equal(14, p.WindLayers[0].GustKts);
        Assert.Equal(34000, p.WindLayers[8].AltitudeFt);
        Assert.Equal(-37.08, p.WindLayers[8].TemperatureC, 2);
    }

    [Fact]
    public void Parse_converts_cloud_metres_to_feet()
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(LiveFixture);
        var c = Assert.Single(p!.CloudLayers);
        Assert.Equal(13038, c.BaseFt);   // 3974 m
        Assert.Equal(24705, c.TopFt);    // 7530 m
        Assert.Equal(4, c.CoverageOktas);
        Assert.Equal(0, c.IcingEnum);
    }

    [Fact]
    public void Parse_handles_explicit_closing_tags_and_missing_attributes()
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(DocFixture);
        Assert.NotNull(p);
        Assert.Equal(3, p!.WindLayers.Count);
        var c = Assert.Single(p.CloudLayers);
        Assert.Equal(5, c.CoverageOktas);
        Assert.Equal(1, c.IcingEnum);
        Assert.Equal(1, c.PrecipType);
        Assert.Equal(2, c.TurbulenceEnum);
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<Weather></Weather>")]
    public void Parse_degrades_gracefully(string bad)
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(bad);
        Assert.True(p == null || (p.WindLayers.Count == 0 && p.CloudLayers.Count == 0));
    }

    // --- Narrative -----------------------------------------------------------------------

    [Fact]
    public void Narrative_curates_levels_and_describes_the_cloud_layer()
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(LiveFixture)!;
        string text = ActiveSkyFormatting.BuildProfileNarrative(p, 36000);

        Assert.Contains("Scattered, 13,038 to 24,705 feet", text);
        Assert.DoesNotContain("icing", text);                       // Icing=0 → omitted
        Assert.Contains("Winds and temperatures aloft:", text);
        Assert.Contains("Surface: 208 at 4, gusting 14, 37, light turbulence", text);
        Assert.Contains("34,000 feet: 286 at 20, minus 37", text);  // nearest to FL360
        Assert.DoesNotContain("44,000 feet", text);                 // not a curated target
        Assert.DoesNotContain("3,000 feet:", text);                 // 6,000 wins the 5,000 target
    }

    [Fact]
    public void Narrative_includes_icing_precip_and_cloud_turbulence_when_present()
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(DocFixture)!;
        string text = ActiveSkyFormatting.BuildProfileNarrative(p, 2000);
        Assert.Contains("Broken, 3,281 to 6,562 feet, light icing, rain, moderate turbulence", text);
    }

    [Fact]
    public void Narrative_reports_empty_sky()
    {
        var p = new ActiveSkyClient.VerticalProfile();
        p.WindLayers.Add(new ActiveSkyClient.ProfileWindLayer
        {
            IsSurface = true, AltitudeFt = 0, DirectionDeg = 100, SpeedKts = 5, TemperatureC = 15,
        });
        string text = ActiveSkyFormatting.BuildProfileNarrative(p, 1000);
        Assert.StartsWith("No cloud layers reported below FL560.", text);
        Assert.Contains("Surface: 100 at 5, 15", text);
    }

    // --- Enum word maps -------------------------------------------------------------------

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "Few")]
    [InlineData(2, "Few")]
    [InlineData(3, "Scattered")]
    [InlineData(4, "Scattered")]
    [InlineData(5, "Broken")]
    [InlineData(7, "Broken")]
    [InlineData(8, "Overcast")]
    [InlineData(99, null)]
    public void Coverage_words(int oktas, string? expected)
        => Assert.Equal(expected, ActiveSkyFormatting.CoverageWord(oktas));

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "light")]
    [InlineData(2, "moderate")]
    [InlineData(3, "heavy")]
    [InlineData(4, "severe")]
    [InlineData(9, null)]
    public void Severity_words(int e, string? expected)
        => Assert.Equal(expected, ActiveSkyFormatting.SeverityWord(e));
}
```

- [ ] **Step 2: Verify RED** — missing types/members.

- [ ] **Step 3: Implement the client side** (in `ActiveSkyClient.cs`, after `AtmosphereLevel`)

```csharp
    /// <summary>Parsed /GetWeatherInfoXml — the vertical weather profile.</summary>
    public sealed class VerticalProfile
    {
        public List<ProfileWindLayer> WindLayers { get; } = new();
        public List<ProfileCloudLayer> CloudLayers { get; } = new();
    }

    /// <summary>One wind layer. AltFeet arrives in FEET (surface layer = 0).
    /// TurbulenceEnum is the FSX-style severity 0-4, NOT the 0-100 JSON scale.</summary>
    public sealed class ProfileWindLayer
    {
        public bool IsSurface;
        public int AltitudeFt;
        public double DirectionDeg;
        public double SpeedKts;
        public double TemperatureC;
        public int TurbulenceEnum;
        public double GustKts;          // surface layer only; 0 elsewhere
    }

    /// <summary>One cloud layer. Base/top arrive in METRES and are converted to
    /// feet at parse time. Coverage is oktas; Icing/Turbulence are severity 0-4;
    /// PrecipType 0 none / 1 rain / 2 snow.</summary>
    public sealed class ProfileCloudLayer
    {
        public int BaseFt;
        public int TopFt;
        public int CoverageOktas;
        public int IcingEnum;
        public int PrecipType;
        public int TurbulenceEnum;
    }
```

Parser + attribute helper (near `ParseAtmosphereJson`):

```csharp
    /// <summary>Parses the /GetWeatherInfoXml body. The response declares
    /// encoding="utf-16", which matches .NET strings — XDocument.Parse handles it.
    /// Permissive: missing attributes default to 0, unknown elements are ignored,
    /// malformed XML → null.</summary>
    internal static VerticalProfile? ParseWeatherInfoXml(string xml)
    {
        try
        {
            var root = System.Xml.Linq.XDocument.Parse(xml).Root;
            if (root == null) return null;
            var p = new VerticalProfile();

            var windLayers = root.Element("WindLayers");
            if (windLayers != null)
            {
                foreach (var el in windLayers.Elements())
                {
                    bool isSurface = el.Name.LocalName == "SurfaceLayer";
                    if (!isSurface && el.Name.LocalName != "Layer") continue;
                    p.WindLayers.Add(new ProfileWindLayer
                    {
                        IsSurface = isSurface,
                        AltitudeFt = isSurface ? 0 : (int)Math.Round(Attr(el, "AltFeet")),
                        DirectionDeg = Attr(el, "WindDirection"),
                        SpeedKts = Attr(el, "WindSpeed"),
                        TemperatureC = Attr(el, "Temp"),
                        TurbulenceEnum = (int)Math.Round(Attr(el, "Turbulence")),
                        GustKts = Attr(el, "Gust"),
                    });
                }
            }

            var clouds = root.Element("Clouds");
            if (clouds != null)
            {
                foreach (var el in clouds.Elements("Cloud"))
                {
                    p.CloudLayers.Add(new ProfileCloudLayer
                    {
                        BaseFt = (int)Math.Round(Attr(el, "CloudBaseMeters") * 3.28084),
                        TopFt = (int)Math.Round(Attr(el, "CloudTopMeters") * 3.28084),
                        CoverageOktas = (int)Math.Round(Attr(el, "Coverage")),
                        IcingEnum = (int)Math.Round(Attr(el, "Icing")),
                        PrecipType = (int)Math.Round(Attr(el, "PrecipType")),
                        TurbulenceEnum = (int)Math.Round(Attr(el, "CloudTurbulence")),
                    });
                }
            }

            return p;
        }
        catch
        {
            return null;
        }
    }

    private static double Attr(System.Xml.Linq.XElement el, string name)
    {
        var a = el.Attribute(name);
        if (a == null) return 0;
        return double.TryParse(a.Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
    }
```

Wrapper:

```csharp
    /// <summary>/GetWeatherInfoXml?lat=&lon= — the comprehensive vertical profile
    /// at a position (13 wind/temp/turbulence layers to FL560, cloud layers with
    /// tops and icing). Null on error or when AS is off/unreachable.</summary>
    public async Task<VerticalProfile?> GetWeatherInfoXmlAsync(double lat, double lon)
    {
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled) return null;   // master switch — no AS I/O when off
        if (LastSuccessfulPort is not int port) return null;
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            string url = $"{BaseUrl(port)}/GetWeatherInfoXml"
                + $"?lat={lat.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}"
                + $"&lon={lon.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}&timeoffset=0";
            using var resp = await _http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            return ParseWeatherInfoXml(await resp.Content.ReadAsStringAsync(cts.Token));
        }
        catch
        {
            Log.Debug("ActiveSky", "GetWeatherInfoXml failed (timeout or connection error)");
            return null;
        }
    }
```

- [ ] **Step 4: Implement the narrative builder** (append to `ActiveSkyFormatting`)

```csharp
    /// <summary>
    /// Curated vertical-profile briefing (design doc 2026-07-10 §3.2): every cloud
    /// layer with base/top/coverage plus icing/precip/turbulence phrases only when
    /// present; winds/temps at the layers nearest the standard levels (surface,
    /// 5,000, 10,000, 18,000, 24,000, 34,000 ft) plus the layer nearest the
    /// aircraft, deduplicated, ascending. Unknown enum values render as no phrase.
    /// </summary>
    internal static string BuildProfileNarrative(ActiveSkyClient.VerticalProfile p, int aircraftAltFt)
    {
        var sb = new System.Text.StringBuilder();

        var realClouds = p.CloudLayers.Where(c => CoverageWord(c.CoverageOktas) != null)
                                      .OrderBy(c => c.BaseFt).ToList();
        if (realClouds.Count == 0)
        {
            sb.AppendLine("No cloud layers reported below FL560.");
        }
        else
        {
            sb.AppendLine("Cloud layers:");
            foreach (var c in realClouds)
            {
                var line = new System.Text.StringBuilder(
                    $"{CoverageWord(c.CoverageOktas)}, {c.BaseFt:N0} to {c.TopFt:N0} feet");
                if (SeverityWord(c.IcingEnum) is { } icing) line.Append($", {icing} icing");
                if (PrecipWord(c.PrecipType) is { } precip) line.Append($", {precip}");
                if (SeverityWord(c.TurbulenceEnum) is { } turb) line.Append($", {turb} turbulence");
                sb.AppendLine(line.ToString());
            }
        }

        var curated = SelectCuratedLevels(p.WindLayers, aircraftAltFt);
        if (curated.Count > 0)
        {
            sb.AppendLine("Winds and temperatures aloft:");
            foreach (var w in curated)
            {
                string label = w.IsSurface ? "Surface" : $"{w.AltitudeFt:N0} feet";
                var line = new System.Text.StringBuilder(
                    $"{label}: {(int)Math.Round(w.DirectionDeg):000} at {w.SpeedKts:F0}");
                if (w.IsSurface && w.GustKts > 0) line.Append($", gusting {w.GustKts:F0}");
                line.Append($", {TempWord(w.TemperatureC)}");
                if (SeverityWord(w.TurbulenceEnum) is { } turb) line.Append($", {turb} turbulence");
                sb.AppendLine(line.ToString());
            }
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>Oktas → METAR coverage word; null = not a reportable layer.</summary>
    internal static string? CoverageWord(int oktas) => oktas switch
    {
        1 or 2 => "Few",
        3 or 4 => "Scattered",
        >= 5 and <= 7 => "Broken",
        8 => "Overcast",
        _ => null,
    };

    /// <summary>FSX-style severity enum (0-4) → word; null = none/unknown (omit).</summary>
    internal static string? SeverityWord(int e) => e switch
    {
        1 => "light",
        2 => "moderate",
        3 => "heavy",
        4 => "severe",
        _ => null,
    };

    /// <summary>PrecipType enum → word; null = none/unknown (omit).</summary>
    internal static string? PrecipWord(int t) => t switch
    {
        1 => "rain",
        2 => "snow",
        _ => null,
    };

    /// <summary>Spoken-friendly whole-degree temperature ("minus 37" / "15").</summary>
    internal static string TempWord(double c)
    {
        int r = (int)Math.Round(c);
        return r < 0 ? $"minus {-r}" : $"{r}";
    }

    /// <summary>Nearest layer to each standard level + the aircraft's level, deduped, ascending.</summary>
    internal static List<ActiveSkyClient.ProfileWindLayer> SelectCuratedLevels(
        IReadOnlyList<ActiveSkyClient.ProfileWindLayer> layers, int aircraftAltFt)
    {
        var result = new List<ActiveSkyClient.ProfileWindLayer>();
        if (layers.Count == 0) return result;
        int[] targets = { 0, 5000, 10000, 18000, 24000, 34000 };
        foreach (int t in targets)
        {
            var nearest = layers.OrderBy(l => Math.Abs(l.AltitudeFt - t)).First();
            if (!result.Contains(nearest)) result.Add(nearest);
        }
        var nearCurrent = layers.OrderBy(l => Math.Abs(l.AltitudeFt - aircraftAltFt)).First();
        if (!result.Contains(nearCurrent)) result.Add(nearCurrent);
        return result.OrderBy(l => l.AltitudeFt).ToList();
    }
```

- [ ] **Step 5: Verify GREEN** (filter `ActiveSkyProfileTests`). The metre→feet expectations (13,038 / 24,705 / 3,281 / 6,562) come from ×3.28084 rounded — if an off-by-one from rounding shows up, trust the code's `Math.Round` result and correct the test constant.

- [ ] **Step 6: Add the profile section to the form**

6a. Fields:

```csharp
    private Label _profileLabel = null!;
    private TextBox _profileBox = null!;
```

6b. In `InitializeComponent()`, insert after the station block (after its `y += 110 + 12;`):

```csharp
        // ── Vertical profile (ActiveSky only; hidden when AS is off) ──────
        _profileLabel = new Label
        {
            Text = "Vertical Profile (ActiveSky):",
            Location = new Point(12, y),
            Size = new Size(570, 20),
            AccessibleName = "Vertical Profile label",
            Visible = false
        };
        y += 24;

        _profileBox = new TextBox
        {
            Location = new Point(12, y),
            Size = new Size(566, 140),
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9),
            Text = "",
            AccessibleName = "Vertical Profile",
            AccessibleDescription = "Cloud layers with tops and icing, and winds and temperatures aloft, at the current position from ActiveSky",
            Visible = false
        };
        y += 140 + 12;
```

Add `_profileLabel, _profileBox,` to `Controls.AddRange` (after `_stationBox`). To keep the taller form on screen, in the same edit shrink `_advisoriesBox`'s height from 210 to 160 (`y += 160 + 12;`) and `_windsAloftBox`'s from 170 to 140 (`y += 140 + 10;`), and set the form shell `Size` to `new Size(600, 976)` (880 + 176 new profile section − 80 reclaimed from the two shrunk boxes).

6c. Tab order — final block in `SetupAccessibility()`:

```csharp
        _currentWeatherBox.TabIndex = 0;
        _stationBox.TabIndex = 1;
        _profileBox.TabIndex = 2;
        _advisoriesBox.TabIndex = 3;
        _windsAloftBox.TabIndex = 4;
        _decodeCheckBox.TabIndex = 5;
        _refreshButton.TabIndex = 6;
        _closeButton.TabIndex = 7;
```

6d. Fetch: add to `WeatherRadarForm`:

```csharp
    private async Task<string> FetchProfileAsync(double lat, double lon, int altFt)
    {
        if (_activeSkyAvailable != true) return "unavailable";
        if (lat == 0 && lon == 0) return "Aircraft position unavailable — connect to simulator first.";
        var profile = await _activeSky.GetWeatherInfoXmlAsync(lat, lon);
        if (profile == null) return "unavailable";
        return MSFSBlindAssist.Services.ActiveSkyFormatting.BuildProfileNarrative(profile, altFt);
    }
```

In `RefreshAsync`, add the task to the parallel batch:

```csharp
            var profileTask    = FetchProfileAsync(lat, lon, altFt);
```

include it in `Task.WhenAll(ambientTask, advisoriesTask, windsTask, profileTask);`, assign `_profileBox.Text = await profileTask;`, and extend the visibility block:

```csharp
            _profileLabel.Visible = asEnabled;
            _profileBox.Visible = asEnabled;
```

- [ ] **Step 7: Full suite + solution build** — green, 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(weather): vertical weather profile in the Weather Radar form

GetWeatherInfoXml at the current position: every cloud layer with base,
top, coverage, icing, precipitation and turbulence, plus curated winds
and temperatures aloft (standard levels + the aircraft's level). Cloud
tops, icing bands and temps aloft were previously exposed nowhere.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: Documentation updates

**Files:**
- Modify: `docs/weather.md`
- Modify: `CLAUDE.md`

**Interfaces:** none (docs only). Content requirements (write full prose, not stubs):

- [ ] **Step 1: `docs/weather.md`** — add a new section "ActiveSky read-only surfaces (2026-07)" documenting, with file/method names: (a) the mode status line + the monitor's mode-change announce rules (baseline-first, silent on connect, baseline survives unreachable gaps, gated by `ShouldRun`); (b) the closest-station box sharing `BuildDecodedWeatherText` with the auto-announce (one code path, never diverge); (c) the vertical profile (enum conventions: severity 0-4, oktas, metres→feet for clouds only) and its curation rules; (d) the forecast combo (offsets, AS box caption, VATSIM box always current); (e) the settings-panel status line *(removed on Robin's 2026-07-13 review — see the self-review checklist's POST-EXECUTION note)*. Extend §3 (per-engine wind truth) with: the Winds Aloft box reads `/GetAtmosphere` when AS is enabled and reachable (falling through to Open-Meteo only when the AS call fails), Open-Meteo otherwise; both paths carry a `Source:` tag line; the altitude window must stay identical between the two sources (`ActiveSkyFormatting.WindsAloftAltitudes` mirrors `WeatherService.ParseWindsAloft`).

- [ ] **Step 2: `CLAUDE.md`** — in the weather invariants block (near the existing "Wind truth is PER-ENGINE" bullet), add one bullet:

```markdown
- The Winds Aloft box follows the per-engine source rule: `/GetAtmosphere` when AS is enabled+reachable, Open-Meteo otherwise, each with a visible `Source:` tag — and the ±5000 ft/1000-ft altitude window must stay IDENTICAL between the two sources. The AS mode monitor is baseline-first (silent at startup/connect; the baseline survives unreachable gaps so a reconnect in a different mode announces); AS-only UI sections are HIDDEN when the switch is off, never shown with disabled text. → [weather.md](docs/weather.md)
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "docs(weather): document the ActiveSky read-only surfaces

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## In-sim test plan (goes in the PR description; Robin runs it)

1. **AS running, switch on (Live mode):** Shift+R → mode line reads "ActiveSky: Live Real time mode, weather time HHMMZ"; closest-station box shows "Decoded weather at <ICAO>…" + raw METAR; vertical profile lists cloud layers and winds/temps including the aircraft's level; Winds Aloft ends "Source: ActiveSky" with temperatures; current-position box has a "Temperature/dew point" line.
2. **Switch AS Live→Custom mid-session (auto-announce on):** exactly one "ActiveSky weather mode changed to Custom static mode." within ~60 s; no announce at app start or when AS merely restarts in the same mode.
3. **Switch off in settings:** re-open Shift+R — mode line, station box, profile box all gone from the tab order; Winds Aloft ends "Source: Open-Meteo"; Shift+M shows no forecast combo. Toggle back on → everything returns without an app restart.
4. **AS enabled but closed:** Shift+R sections show "unavailable"/failure wording (not hidden). *(The "settings panel status line shows the failure reason" sub-check was dropped — the status line was removed on Robin's 2026-07-13 review; the Shift+R mode box carries the failure reason instead.)*
5. **Forecast:** Shift+M, ICAO with an active TAF (e.g. KJFK), switch Now → +4 hours → AS box re-fetches, caption reads "ActiveSky METAR (+4 hours):", text differs from Now; VATSIM box unchanged.

## Self-review checklist (run after writing, before execution)

- Spec coverage: §2 items 1-7 → Tasks 1/2, 8, 7, 5, 6, 4, 3; §4.1 hidden-when-off → Tasks 1/5/8 visibility blocks; §7 docs → Task 9. Deviations from spec, both deliberate: no `GetModeAsync` wrapper (LastModeText suffices — YAGNI); profile box heights/trims chosen for screen fit. POST-EXECUTION: Task 3's settings-panel status line was built as planned and then REMOVED on Robin's 2026-07-13 PR review — a connection-status readout doesn't belong in the settings panel; `LastStatus` stays surfaced via the Weather Radar mode box instead (see the design doc §2 item 7 note).
- Type consistency: `AtmosphereLevel`/`VerticalProfile`/`ProfileWindLayer`/`ProfileCloudLayer` are nested in `ActiveSkyClient`; all formatting statics on `ActiveSkyFormatting`; `BuildDecodedWeatherText` on `ActiveSkyWeatherMonitor`.
- Every code step contains complete code; no TBDs.

# Weather Settings Tab + ActiveSky Master Switch — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in `ActiveSkyEnabled` setting (default off) on a new Weather settings tab so no ActiveSky probing ever runs for users who don't use ActiveSky.

**Architecture:** One central gate inside `ActiveSkyClient.IsRunningAsync()` (returns false instantly when the setting is off) makes every existing call site — output+I wind, Alt+W precip, Weather Radar, METAR form, decoded-weather monitor — degrade to its existing no-AS path with no per-site changes. The monitor additionally gets explicit start/stop lifecycle gating. A new `WeatherPanel` settings tab hosts the switch plus the weather-announcement toggles moved from `AnnouncementsPanel`.

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit (tests/MSFSBlindAssist.Tests).

**Spec:** `docs/design/2026-07-09-weather-tab-activesky-switch-design.md`

## Global Constraints

- Branch: `feature/weather-tab-activesky-switch` (main is protected — never commit to main).
- Build via `dotnet build MSFSBlindAssist.sln -c Debug` (NEVER the bare csproj — wrong output dir).
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
- All log writes via `MSFSBlindAssist.Utils.Logging.Log` (`Log.Debug(category, msg)`), never raw file I/O.
- Tests that read/write `SettingsManager.Current` MUST join `[Collection("SettingsManagerGlobalState")]` and restore values in Dispose.
- TDD: write the failing test first, watch it fail, then implement.
- Commit messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- The setting's default MUST be `false` — absent-from-JSON must read as disabled.

---

### Task 1: `ActiveSkyEnabled` setting + central gate + client logging

**Files:**
- Modify: `MSFSBlindAssist/Settings/UserSettings.cs` (Weather Settings region ~line 336; clone block ~line 467)
- Modify: `MSFSBlindAssist/Services/ActiveSkyClient.cs` (`IsRunningAsync` ~line 115, `GetCurrentConditionsAsync` ~line 208)
- Test: `tests/MSFSBlindAssist.Tests/ActiveSkyGateTests.cs` (new)

**Interfaces:**
- Consumes: `SettingsManager.Current` (process-global `UserSettings`), `Log.Debug(string, string)` from `MSFSBlindAssist.Utils.Logging`.
- Produces: `UserSettings.ActiveSkyEnabled` (bool, default false) — Tasks 2 and 3 read/write this exact name. `ActiveSkyClient.IsRunningAsync()` returning `false` with `LastStatus == "disabled in settings"` when the setting is off.

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/ActiveSkyGateTests.cs`:

```csharp
using System.Diagnostics;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The ActiveSky master switch (UserSettings.ActiveSkyEnabled, default OFF) must make
/// ActiveSkyClient.IsRunningAsync() short-circuit false with NO network probe — the
/// probe has a ~1.2 s floor when AS is absent, which every non-AS user paid on the
/// output+I hotkey before the switch existed. Reads process-global SettingsManager →
/// shared no-parallelism collection + restore in Dispose.
/// </summary>
[Collection("SettingsManagerGlobalState")]
public class ActiveSkyGateTests : IDisposable
{
    private readonly bool savedEnabled;

    public ActiveSkyGateTests()
    {
        savedEnabled = SettingsManager.Current.ActiveSkyEnabled;
    }

    public void Dispose() => SettingsManager.Current.ActiveSkyEnabled = savedEnabled;

    [Fact]
    public void ActiveSkyEnabled_DefaultsToFalse()
    {
        Assert.False(new UserSettings().ActiveSkyEnabled);
    }

    [Fact]
    public async Task IsRunningAsync_WhenDisabled_ShortCircuitsFalse_WithoutProbing()
    {
        SettingsManager.Current.ActiveSkyEnabled = false;
        var client = new ActiveSkyClient();
        var sw = Stopwatch.StartNew();
        bool running = await client.IsRunningAsync();
        sw.Stop();
        Assert.False(running);
        Assert.Equal("disabled in settings", client.LastStatus);
        // A real probe has a 1.2 s timeout floor; the short-circuit must be instant.
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"expected instant short-circuit, took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task GetCurrentConditions_WhenDisabled_ReturnsNull()
    {
        SettingsManager.Current.ActiveSkyEnabled = false;
        var client = new ActiveSkyClient();
        // No port was ever discovered (and none may be while disabled).
        Assert.Null(await client.GetCurrentConditionsAsync());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ActiveSkyGateTests"`
Expected: compile FAILURE — `'UserSettings' does not contain a definition for 'ActiveSkyEnabled'`. That is the correct RED for this task.

- [ ] **Step 3: Add the setting**

In `MSFSBlindAssist/Settings/UserSettings.cs`, at the TOP of the `// Weather Settings` region (directly above `public bool WeatherAutoAnnounceEnabled`):

```csharp
        // Weather Settings
        /// <summary>
        /// Master switch for the HiFi ActiveSky integration (Weather settings tab).
        /// Default OFF: most users run the sim's own weather engine, and the AS
        /// liveness probe has a ~1.2 s floor when AS is absent — so NO ActiveSky
        /// code path may run unless the user opts in. Gated centrally in
        /// ActiveSkyClient.IsRunningAsync (returns false instantly when off).
        /// </summary>
        public bool ActiveSkyEnabled { get; set; } = false;
```

Then find the settings-copy block (the object initializer around line 467 that lists `WeatherAutoAnnounceEnabled = WeatherAutoAnnounceEnabled,`) and add, directly above that line:

```csharp
            ActiveSkyEnabled = ActiveSkyEnabled,
```

- [ ] **Step 4: Add the central gate + logging to ActiveSkyClient**

In `MSFSBlindAssist/Services/ActiveSkyClient.cs`:

Add usings (top of file, if not present):

```csharp
using System.Diagnostics;
using MSFSBlindAssist.Utils.Logging;
```

In `IsRunningAsync()`, insert at the very top of the method body (before the cached-port fast path):

```csharp
        // MASTER SWITCH (Weather settings tab): when the user has not opted into
        // ActiveSky, NO probe may run — the parallel probe has a ~1.2 s floor when
        // AS is absent, which every non-AS user would otherwise pay on each call
        // (output+I hotkey, radar open, monitor poll). This central gate covers
        // every call site, present and future.
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled)
        {
            LastStatus = "disabled in settings";
            return false;
        }
```

Still in `IsRunningAsync()`, add debug logging (closing the observability gap — the client previously wrote nothing to debug.log):
- Wrap the whole post-gate body's outcome: at each `return true`, before returning, add
  `Log.Debug("ActiveSky", $"probe ok: {LastStatus} in {sw.ElapsedMilliseconds} ms");`
- At the final `return false`, add
  `Log.Debug("ActiveSky", $"probe failed: {LastStatus} in {sw.ElapsedMilliseconds} ms");`
- Declare `var sw = Stopwatch.StartNew();` immediately AFTER the master-switch gate (the disabled short-circuit is not worth a log line per call — it would spam debug.log every 30 s from the weather timer).

In `GetCurrentConditionsAsync()`, inside the existing `catch`, replace the bare `return null;` with:

```csharp
            Log.Debug("ActiveSky", "GetCurrentConditions failed (timeout or connection error)");
            return null;
```

(The method already returns null when `LastSuccessfulPort` is null, which can never be set while disabled — no extra gate needed, but the test in Step 1 pins that behavior.)

- [ ] **Step 5: Run the new tests — verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ActiveSkyGateTests"`
Expected: PASS (3/3).

- [ ] **Step 6: Run the FULL suite — verify no regressions**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass (1025 existing + 3 new).

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Settings/UserSettings.cs MSFSBlindAssist/Services/ActiveSkyClient.cs tests/MSFSBlindAssist.Tests/ActiveSkyGateTests.cs
git commit -m "feat(weather): ActiveSkyEnabled master switch — central gate in ActiveSkyClient

Default OFF. IsRunningAsync short-circuits false ('disabled in settings')
with zero network I/O when the user has not opted in, so every call site
(output+I wind, Alt+W precip, radar, METAR form, decoded-weather monitor)
degrades to its existing no-AS path with no per-site changes. Adds
Log.Debug probe-outcome lines (the client previously wrote nothing to
debug.log, which made the #139 hotkey-delay report undiagnosable).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Weather settings tab (`WeatherPanel`) + move weather toggles out of Announcements

**Files:**
- Create: `MSFSBlindAssist/Forms/Settings/WeatherPanel.cs`
- Modify: `MSFSBlindAssist/Forms/Settings/AnnouncementsPanel.cs` (remove the Weather group: fields lines 19-24, `BuildWeatherGroup()` lines 154-239, its call sites in `InitializeComponent`/`LoadFrom`/`ApplyTo`, and the now-unused `IntervalChoicesMinutes`/`IntervalChoiceLabel`)
- Modify: `MSFSBlindAssist/Forms/Settings/SettingsForm.cs` (line 32-37 panel list)
- Test: `tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs` (new)

**Interfaces:**
- Consumes: `UserSettings.ActiveSkyEnabled` (Task 1), `ISettingsPanel` (`TabTitle`, `LoadFrom`, `Validate`, `ApplyTo`, `OnLeaving` — see `Forms/Settings/ISettingsPanel.cs`), existing setting keys `WeatherAutoAnnounceEnabled`, `WeatherAutoAnnounceIntervalMinutes`, `SigmetProximityAlertsEnabled`, `PirepProximityAlertsEnabled`, `SigmetProximityRangeNm`.
- Produces: `WeatherPanel` class registered as the tab after Announcements. Setting keys are UNCHANGED — no persistence migration.

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs`:

```csharp
using MSFSBlindAssist.Forms.Settings;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// LoadFrom/ApplyTo round-trip for the Weather settings tab. The panel is a plain
/// UserControl whose controls are readable/writable without a message pump, so it
/// can be exercised directly. No SettingsManager access — a local UserSettings is
/// passed in — so no collection needed.
/// </summary>
public class WeatherPanelTests
{
    [Fact]
    public void RoundTrip_PreservesAllWeatherSettings()
    {
        var source = new UserSettings
        {
            ActiveSkyEnabled = true,
            WeatherAutoAnnounceEnabled = true,
            WeatherAutoAnnounceIntervalMinutes = 15,
            SigmetProximityAlertsEnabled = true,
            PirepProximityAlertsEnabled = true,
            SigmetProximityRangeNm = 250
        };

        using var panel = new WeatherPanel();
        panel.LoadFrom(source);
        var target = new UserSettings();
        panel.ApplyTo(target);

        Assert.True(target.ActiveSkyEnabled);
        Assert.True(target.WeatherAutoAnnounceEnabled);
        Assert.Equal(15, target.WeatherAutoAnnounceIntervalMinutes);
        Assert.True(target.SigmetProximityAlertsEnabled);
        Assert.True(target.PirepProximityAlertsEnabled);
        Assert.Equal(250, target.SigmetProximityRangeNm);
    }

    [Fact]
    public void Defaults_RoundTripToDefaults()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());
        var target = new UserSettings { ActiveSkyEnabled = true }; // must be overwritten to false
        panel.ApplyTo(target);

        Assert.False(target.ActiveSkyEnabled);
        Assert.False(target.WeatherAutoAnnounceEnabled);
        Assert.Equal(0, target.WeatherAutoAnnounceIntervalMinutes);
    }

    [Fact]
    public void TabTitle_IsWeather()
    {
        using var panel = new WeatherPanel();
        Assert.Equal("Weather", panel.TabTitle);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~WeatherPanelTests"`
Expected: compile FAILURE — `The type or namespace name 'WeatherPanel' could not be found`.

- [ ] **Step 3: Create WeatherPanel**

Create `MSFSBlindAssist/Forms/Settings/WeatherPanel.cs`. The Announcements GroupBox content is moved VERBATIM from `AnnouncementsPanel.BuildWeatherGroup()` (same control text, AccessibleNames, choices):

```csharp
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>Weather section of the unified Settings dialog: the ActiveSky master switch
/// plus the weather auto-announcement toggles (moved here from AnnouncementsPanel so all
/// weather behavior lives on one tab). The ActiveSky switch is the ONLY control that
/// enables AS integration — default off; when off, ActiveSkyClient.IsRunningAsync
/// short-circuits and no AS probing runs anywhere (see the CLAUDE.md weather invariant).</summary>
public class WeatherPanel : UserControl, ISettingsPanel
{
    private CheckBox _activeSkyEnabled = null!;

    private CheckBox _weatherAutoAnnounce = null!;
    private ComboBox _weatherIntervalCombo = null!;
    private CheckBox _sigmetAlerts = null!;
    private CheckBox _pirepAlerts = null!;
    private NumericUpDown _proximityRange = null!;

    /// <summary>Combo entries: minutes (0 = AS download interval, no extra throttle).</summary>
    private static readonly int[] IntervalChoicesMinutes = { 0, 5, 10, 15, 20, 30, 45, 60 };

    public string TabTitle => "Weather";

    public WeatherPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AutoScroll = true;

        var activeSkyGroup = BuildActiveSkyGroup();
        var announceGroup = BuildAnnouncementsGroup();
        announceGroup.Location = new System.Drawing.Point(12, activeSkyGroup.Bottom + 12);

        Controls.Add(activeSkyGroup);
        Controls.Add(announceGroup);
    }

    private GroupBox BuildActiveSkyGroup()
    {
        var group = new GroupBox
        {
            Text = "ActiveSky (HiFi)",
            Location = new System.Drawing.Point(12, 12),
            Size = new System.Drawing.Size(460, 96),
            AccessibleName = "ActiveSky",
            AccessibleDescription = "HiFi ActiveSky weather engine integration",
        };

        _activeSkyEnabled = new CheckBox
        {
            Text = "Enable &ActiveSky integration",
            Location = new System.Drawing.Point(12, 24),
            Size = new System.Drawing.Size(430, 48),
            AutoSize = false,
            CheckAlign = System.Drawing.ContentAlignment.TopLeft,
            TextAlign = System.Drawing.ContentAlignment.TopLeft,
            AccessibleName = "Enable ActiveSky integration",
            AccessibleDescription = "Requires HiFi ActiveSky running on this PC. When enabled, wind, gusts, "
                + "precipitation and decoded station weather come from ActiveSky instead of the sim's own "
                + "weather engine. Leave off if you don't use ActiveSky."
        };

        group.Controls.Add(_activeSkyEnabled);
        return group;
    }

    private GroupBox BuildAnnouncementsGroup()
    {
        var group = new GroupBox
        {
            Text = "Announcements",
            Location = new System.Drawing.Point(12, 0),
            Size = new System.Drawing.Size(460, 230),
            AccessibleName = "Weather announcements",
            AccessibleDescription = "Weather and advisory auto-announcement settings",
        };

        _weatherAutoAnnounce = new CheckBox
        {
            Text = "Auto-announce &weather state changes",
            Location = new System.Drawing.Point(12, 24),
            Size = new System.Drawing.Size(420, 24),
            AccessibleName = "Auto-announce weather state changes",
            AccessibleDescription = "Automatically announce when entering or leaving clouds and when precipitation starts or stops"
        };

        _sigmetAlerts = new CheckBox
        {
            Text = "Auto-announce approaching &SIGMETs and AIRMETs",
            Location = new System.Drawing.Point(12, 60),
            Size = new System.Drawing.Size(420, 24),
            AccessibleName = "Auto-announce approaching SIGMETs and AIRMETs",
            AccessibleDescription = "Announce when the aircraft enters the proximity range of an active SIGMET or AIRMET"
        };

        _pirepAlerts = new CheckBox
        {
            Text = "Auto-announce approaching pilot reports (&PIREPs)",
            Location = new System.Drawing.Point(12, 96),
            Size = new System.Drawing.Size(420, 24),
            AccessibleName = "Auto-announce approaching PIREPs",
            AccessibleDescription = "Announce when the aircraft enters the proximity range of a significant pilot report of turbulence or icing"
        };

        var rangeLabel = new Label
        {
            Text = "&Proximity range (nautical miles):",
            Location = new System.Drawing.Point(12, 138),
            Size = new System.Drawing.Size(250, 20),
            AccessibleName = "Proximity range label"
        };

        _proximityRange = new NumericUpDown
        {
            Location = new System.Drawing.Point(270, 134),
            Size = new System.Drawing.Size(80, 24),
            Minimum = 10,
            Maximum = 500,
            AccessibleName = "Proximity range in nautical miles",
            AccessibleDescription = "Distance at which to announce approaching SIGMETs, AIRMETs, and PIREPs"
        };

        var intervalLabel = new Label
        {
            Text = "Weather announcement &interval:",
            Location = new System.Drawing.Point(12, 178),
            Size = new System.Drawing.Size(250, 20),
            AccessibleName = "Weather announcement interval label"
        };

        _weatherIntervalCombo = new ComboBox
        {
            Location = new System.Drawing.Point(12, 202),
            Size = new System.Drawing.Size(338, 24),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Weather announcement interval",
            AccessibleDescription = "Minimum minutes between auto-announced ActiveSky weather updates. Active Sky download interval means no extra throttle; the announcer follows ActiveSky's own refresh cadence."
        };
        foreach (int minutes in IntervalChoicesMinutes)
        {
            _weatherIntervalCombo.Items.Add(IntervalChoiceLabel(minutes));
        }

        group.Controls.AddRange(new Control[]
        {
            _weatherAutoAnnounce, _sigmetAlerts, _pirepAlerts,
            rangeLabel, _proximityRange,
            intervalLabel, _weatherIntervalCombo
        });

        return group;
    }

    private static string IntervalChoiceLabel(int minutes)
        => minutes == 0
            ? "Active Sky download interval"
            : minutes == 60
                ? "1 hour"
                : $"{minutes} minutes";

    public void LoadFrom(UserSettings settings)
    {
        _activeSkyEnabled.Checked = settings.ActiveSkyEnabled;

        _weatherAutoAnnounce.Checked = settings.WeatherAutoAnnounceEnabled;
        _sigmetAlerts.Checked = settings.SigmetProximityAlertsEnabled;
        _pirepAlerts.Checked = settings.PirepProximityAlertsEnabled;
        _proximityRange.Value = Math.Clamp(settings.SigmetProximityRangeNm, 10, 500);
        _weatherIntervalCombo.SelectedIndex = Math.Max(0, Array.IndexOf(IntervalChoicesMinutes, settings.WeatherAutoAnnounceIntervalMinutes));
    }

    public bool Validate(out string error, out Control? focus)
    {
        error = "";
        focus = null;
        return true;
    }

    public void ApplyTo(UserSettings settings)
    {
        settings.ActiveSkyEnabled = _activeSkyEnabled.Checked;

        settings.WeatherAutoAnnounceEnabled = _weatherAutoAnnounce.Checked;
        settings.WeatherAutoAnnounceIntervalMinutes = IntervalChoicesMinutes[
            Math.Clamp(_weatherIntervalCombo.SelectedIndex, 0, IntervalChoicesMinutes.Length - 1)];
        settings.SigmetProximityAlertsEnabled = _sigmetAlerts.Checked;
        settings.PirepProximityAlertsEnabled = _pirepAlerts.Checked;
        settings.SigmetProximityRangeNm = (int)_proximityRange.Value;
    }

    public void OnLeaving()
    {
    }
}
```

- [ ] **Step 4: Register the tab and strip the Weather group from AnnouncementsPanel**

In `MSFSBlindAssist/Forms/Settings/SettingsForm.cs`, the panel list (lines 32-37) becomes:

```csharp
        AddPanel(new AnnouncementsPanel());
        AddPanel(new WeatherPanel());
        AddPanel(new GeoNamesPanel());
        AddPanel(new SimBriefPanel());
        AddPanel(new GeminiPanel());
        AddPanel(new HandFlyPanel());
        AddPanel(new TaxiGuidancePanel(refreshTaxiwayNames));
```

In `MSFSBlindAssist/Forms/Settings/AnnouncementsPanel.cs`, remove:
- The five `// ── Weather group ──` fields (`_weatherAutoAnnounce`, `_weatherIntervalCombo`, `_sigmetAlerts`, `_pirepAlerts`, `_proximityRange`) and the `IntervalChoicesMinutes` array + `IntervalChoiceLabel` helper (now owned by WeatherPanel).
- In `InitializeComponent()`: the `BuildWeatherGroup()` call, its `weatherGroup` local, and `Controls.Add(weatherGroup);` (keep only the General group).
- The whole `BuildWeatherGroup()` method.
- In `LoadFrom()`: the five `settings.Weather*/Sigmet*/Pirep*` lines (316-320).
- In `ApplyTo()`: the five weather lines (339-344).

Update the class doc comment's mention of "two stacked GroupBoxes" to reflect that the Weather group moved to the Weather tab (`WeatherPanel`).

- [ ] **Step 5: Run the new tests — verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~WeatherPanelTests"`
Expected: PASS (3/3).

- [ ] **Step 6: Full build + full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` — expect 0 warnings / 0 errors (an unused-field warning in AnnouncementsPanel means Step 4's removal was incomplete).
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` — expect all pass.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Forms/Settings/WeatherPanel.cs MSFSBlindAssist/Forms/Settings/AnnouncementsPanel.cs MSFSBlindAssist/Forms/Settings/SettingsForm.cs tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs
git commit -m "feat(settings): Weather tab — ActiveSky master switch + weather announce toggles

New WeatherPanel (second tab): the ActiveSkyEnabled switch plus the four
weather auto-announcement controls moved verbatim from AnnouncementsPanel
(same setting keys, no persistence migration). AnnouncementsPanel keeps
only its General group.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Monitor lifecycle gating, live toggle, docs/invariants

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs` (~line 590: unconditional `activeSkyWeatherMonitor.Start()`)
- Modify: `MSFSBlindAssist/MainForm.MenuHandlers.cs` (`ApplyRuntimeSettings`, ~line 94)
- Modify: `MSFSBlindAssist/MainForm.Announcers.cs` (comment on `RequestWindInfo`'s AS branch, ~line 1723)
- Modify: `CLAUDE.md` (weather invariant bullets, ~line 250-253 region)

No new unit tests — this is UI/lifecycle wiring (sim-facing by this repo's testing policy); the full suite must stay green and the PR carries the in-sim test plan.

**Interfaces:**
- Consumes: `UserSettings.ActiveSkyEnabled` (Task 1); `ActiveSkyWeatherMonitor.Start()` / `.Stop()` (both are plain timer start/stop, idempotent).
- Produces: nothing consumed by other tasks (final task).

- [ ] **Step 1: Gate the monitor start at launch**

In `MSFSBlindAssist/MainForm.cs` (~line 590), replace the unconditional start block:

```csharp
        // ActiveSky weather-update announcer. Constructed always (so the settings
        // dialog can start it live via ApplyRuntimeSettings), but STARTED only when
        // the user has opted into ActiveSky on the Weather settings tab — when the
        // switch is off no AS code may run at all, and even when it's on but AS
        // isn't running, each poll would be a ~1.2 s parallel-probe timeout.
        activeSkyWeatherMonitor = new MSFSBlindAssist.Services.ActiveSkyWeatherMonitor(
            new MSFSBlindAssist.Services.ActiveSkyClient(), announcer);
        activeSkyWeatherMonitor.IntervalMinutes =
            MSFSBlindAssist.Settings.SettingsManager.Current.WeatherAutoAnnounceIntervalMinutes;
        if (MSFSBlindAssist.Settings.SettingsManager.Current.ActiveSkyEnabled)
            activeSkyWeatherMonitor.Start();
```

- [ ] **Step 2: Live toggle in ApplyRuntimeSettings**

In `MSFSBlindAssist/MainForm.MenuHandlers.cs`, `ApplyRuntimeSettings()`, extend the existing monitor block:

```csharp
        if (activeSkyWeatherMonitor != null)
        {
            activeSkyWeatherMonitor.IntervalMinutes = settings.WeatherAutoAnnounceIntervalMinutes;
            // ActiveSky master switch (Weather tab) takes effect without restart:
            // Start/Stop are idempotent timer calls.
            if (settings.ActiveSkyEnabled) activeSkyWeatherMonitor.Start();
            else activeSkyWeatherMonitor.Stop();
        }
```

(Replace the existing two-line `if (activeSkyWeatherMonitor != null) activeSkyWeatherMonitor.IntervalMinutes = ...;` statement.)

- [ ] **Step 3: Update the RequestWindInfo comment**

In `MSFSBlindAssist/MainForm.Announcers.cs` (~line 1723), replace the four comment lines above `string currentWind = "unavailable";`:

```csharp
            // Current wind. #129: when the user has OPTED INTO ActiveSky (Weather
            // settings tab) read the AS ambient wind + gust — under AS the SimConnect
            // ambient wind can diverge (AS wind smoothing), and the radar's "wind at
            // altitude" reads AS, so the two must match. When the switch is off,
            // TryGetActiveSkyConditionsAsync returns null INSTANTLY (the central gate
            // in ActiveSkyClient.IsRunningAsync — no probe, no ~1.2 s floor) and the
            // SimConnect path below is authoritative.
```

- [ ] **Step 4: Update CLAUDE.md weather invariants**

In `CLAUDE.md`, find the bullet beginning `- Under ActiveSky the SimConnect ambient feed is unreliable: source the Alt+W precip auto-announce AND the output+I wind from ActiveSky` and replace it with:

```markdown
- ActiveSky integration is strictly OPT-IN (`UserSettings.ActiveSkyEnabled`, Weather settings tab, default OFF) — no AS probe may ever run on an unopted path: the liveness probe has a ~1.2 s floor when AS is absent, which every non-AS user paid per output+I press before the switch existed. The gate is CENTRAL, inside `ActiveSkyClient.IsRunningAsync` (returns false instantly, `LastStatus = "disabled in settings"`); every call site degrades to its no-AS path through it, and the `ActiveSkyWeatherMonitor` is additionally only started when enabled. → [taxi-guidance.md](docs/taxi-guidance.md)
- Wind truth is PER-ENGINE, keyed on the ActiveSky switch: with AS enabled, the Alt+W precip auto-announce AND the output+I wind come from ActiveSky (station/position METAR for precip; AS ambient wind + surface gust for wind — the SimConnect `AMBIENT PRECIP STATE` bitmask sticks and the ambient wind can diverge under AS wind smoothing); with the switch off, SimConnect is authoritative and correct. Neither source is "the" truth — never hard-pick one for both populations. → [taxi-guidance.md](docs/taxi-guidance.md)
```

- [ ] **Step 5: Full build + full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug` — expect 0 warnings / 0 errors.
Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64` — expect all pass.

- [ ] **Step 6: Commit**

```bash
git add MSFSBlindAssist/MainForm.cs MSFSBlindAssist/MainForm.MenuHandlers.cs MSFSBlindAssist/MainForm.Announcers.cs CLAUDE.md
git commit -m "feat(weather): gate ActiveSky monitor on the master switch; live toggle; invariants

The decoded-weather monitor was started unconditionally at launch, paying
the ~1.2 s probe on every poll for every user. Now started only when
ActiveSkyEnabled, and started/stopped live from ApplyRuntimeSettings when
the setting changes. CLAUDE.md weather invariants rewritten: AS is opt-in,
the gate is central, and wind truth is per-engine (SimConnect when off,
AS API when on — AS wind smoothing makes them legitimately differ).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## In-sim test plan (goes in the PR body — the repo owner runs it)

1. Fresh/default settings (switch off), no ActiveSky installed: press output+I → wind announces immediately (no ~1.2 s delay); `%APPDATA%\MSFSBlindAssist\logs\debug.log` contains zero `[ActiveSky]` lines.
2. Settings → Weather tab: verify tab order (after Announcements), NVDA reads the switch's AccessibleDescription; the Announcements tab no longer has a Weather group.
3. Enable the switch mid-session (with AS running): decoded-weather announcements begin without restart; radar shows AS-sourced data; output+I speaks AS wind incl. gust; debug.log shows `[ActiveSky] probe ok` lines.
4. Disable mid-session: monitor announcements stop; radar reverts to SimConnect-only; output+I instant again.

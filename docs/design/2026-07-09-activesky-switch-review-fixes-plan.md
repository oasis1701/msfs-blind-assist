# ActiveSky Master Switch — Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the five defects the PR #156 code review found: the ActiveSky master switch silently doubles as an announcement switch, two `ActiveSkyGateTests` are vacuous, four `ActiveSkyClient` catches log nothing, and eight CLAUDE.md weather invariants point at a doc with no weather content.

**Architecture:** The decoded-weather monitor's run condition becomes a single pure static predicate, `ActiveSkyWeatherMonitor.ShouldRun(UserSettings)`, called from both the launch site and the live-toggle site so they cannot drift. The Weather settings panel's interval-combo visibility follows the same predicate. `ActiveSkyClient.LastSuccessfulPort`'s setter widens to `internal` so tests can seed a cached port and pin the disabled-branch reset that closes the enable→discover→disable leak. Then logging parity, then a new `docs/weather.md` and the CLAUDE.md repoint.

**Tech Stack:** C# 13 / .NET 10, WinForms, xUnit (`tests/MSFSBlindAssist.Tests`).

**Spec:** `docs/design/2026-07-09-activesky-switch-review-fixes-design.md`

## Global Constraints

- Branch: `feature/weather-tab-activesky-switch` (already checked out). `main` is protected — never commit to main.
- Build via `dotnet build MSFSBlindAssist.sln -c Debug`. **NEVER** `dotnet build MSFSBlindAssist/MSFSBlindAssist.csproj` alone — a bare csproj build silently defaults to `Platform=AnyCPU` and writes to `bin\Debug\`, not the `bin\x64\Debug\` run path.
- Tests: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`.
- Close MSFSBlindAssist before building — the exe is file-locked while it runs (MSB3021).
- All log writes go through `MSFSBlindAssist.Utils.Logging.Log` (`Log.Debug(category, msg)`). Never raw file I/O.
- Tests that read or write `SettingsManager.Current` MUST carry `[Collection("SettingsManagerGlobalState")]` and restore the values they touched in `Dispose`.
- TDD: write the failing test, run it, watch it fail for the *right reason*, then implement.
- Commit messages end with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.
- **Invariant that must not break:** with `ActiveSkyEnabled == false`, SimConnect remains the weather source on all four paths (output+I wind, Alt+W cloud/precip/visibility, Weather Radar, METAR form). See the spec's "Non-goal" table.

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs` | Gains `ShouldRun(UserSettings)` — the single source of truth for whether the monitor runs | 1 |
| `MSFSBlindAssist/MainForm.cs` | Launch-time monitor start, via `ShouldRun` | 1 |
| `MSFSBlindAssist/MainForm.MenuHandlers.cs` | Live toggle on settings save, via `ShouldRun` | 1 |
| `MSFSBlindAssist/Forms/Settings/WeatherPanel.cs` | Interval-combo visibility follows the same both-flags predicate | 2 |
| `MSFSBlindAssist/Services/ActiveSkyClient.cs` | `LastSuccessfulPort` setter → `internal`; logging parity in four catches | 3, 4 |
| `tests/MSFSBlindAssist.Tests/ActiveSkyWeatherMonitorGateTests.cs` (new) | Pins `ShouldRun`'s truth table | 1 |
| `tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs` | Two new both-flags visibility tests | 2 |
| `tests/MSFSBlindAssist.Tests/ActiveSkyGateTests.cs` | New port-reset test; rewrite the vacuous one | 3 |
| `docs/weather.md` (new) | The weather/ActiveSky doc the invariants should point at | 5 |
| `CLAUDE.md` | Repoint eight weather invariants; register the new doc | 5 |

**Deviation from the spec, deliberate:** the spec put the predicate as
`private static bool ShouldRunWeatherMonitor(UserSettings)` on `MainForm`. `MainForm` is
not unit-testable (WinForms, SimConnect). Moving it to a public static on
`ActiveSkyWeatherMonitor` makes it TDD-able and puts the rule next to the thing it gates.
Same behavior, same two call sites.

---

### Task 1: Decouple the monitor from the ActiveSky master switch

The decoded-weather monitor (which speaks *"Weather update. Surface wind X at Y…"*) must
run only when the user has **both** opted into ActiveSky and asked to be spoken at. Today
it runs on `ActiveSkyEnabled` alone, so an AS user cannot get accurate output+I wind and
radar data without also being spoken at.

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs` (add `ShouldRun` next to the `Enabled` property, ~line 113)
- Modify: `MSFSBlindAssist/MainForm.cs:600-601`
- Modify: `MSFSBlindAssist/MainForm.MenuHandlers.cs:106-113`
- Test: `tests/MSFSBlindAssist.Tests/ActiveSkyWeatherMonitorGateTests.cs` (new)

**Interfaces:**
- Consumes: `MSFSBlindAssist.Settings.UserSettings` (`ActiveSkyEnabled`, `WeatherAutoAnnounceEnabled` — both `bool`), and the existing `ActiveSkyWeatherMonitor.Enabled { get; set; }` whose setter already does `if (value) Start(); else Stop();`.
- Produces: `public static bool ActiveSkyWeatherMonitor.ShouldRun(UserSettings settings)`. Task 2 calls it from `WeatherPanel` with a throwaway `UserSettings` built from the live checkbox state — so it must be `public`, not `internal`.

- [ ] **Step 1: Write the failing test**

Create `tests/MSFSBlindAssist.Tests/ActiveSkyWeatherMonitorGateTests.cs`:

```csharp
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The decoded-weather monitor is BOTH an ActiveSky feature (it reads only the AS HTTP
/// API — no SimConnect fallback) and an announcement feature (it speaks). It may run
/// only when the user has opted into both. Before this gate, enabling ActiveSky purely
/// for accurate output+I wind and radar data also, unavoidably, started the speech.
///
/// Pure static over a local UserSettings — no SettingsManager, no WinForms pump, so no
/// [Collection] needed.
/// </summary>
public class ActiveSkyWeatherMonitorGateTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true,  false, false)]  // AS on, announce off: wind/radar work, speech silent
    [InlineData(false, true,  false)]  // announce on, AS off: monitor has no data source
    [InlineData(true,  true,  true)]
    public void ShouldRun_RequiresBothFlags(bool activeSky, bool autoAnnounce, bool expected)
    {
        var settings = new UserSettings
        {
            ActiveSkyEnabled = activeSky,
            WeatherAutoAnnounceEnabled = autoAnnounce
        };

        Assert.Equal(expected, ActiveSkyWeatherMonitor.ShouldRun(settings));
    }

    [Fact]
    public void ShouldRun_DefaultSettings_IsFalse()
    {
        Assert.False(ActiveSkyWeatherMonitor.ShouldRun(new UserSettings()));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ActiveSkyWeatherMonitorGateTests"`

Expected: compile FAILURE — `'ActiveSkyWeatherMonitor' does not contain a definition for 'ShouldRun'`. That is the correct RED.

- [ ] **Step 3: Add `ShouldRun` to the monitor**

In `MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs`, find the existing `Enabled` property:

```csharp
    /// <summary>
    /// User-facing toggle: enable/disable the announcement feature without
    /// disposing the monitor. When disabled we don't even poll.
    /// </summary>
    public bool Enabled
    {
        get => _timer.Enabled;
        set { if (value) Start(); else Stop(); }
    }
```

Insert directly ABOVE it:

```csharp
    /// <summary>
    /// The single source of truth for whether this monitor may run. It is BOTH an
    /// ActiveSky feature (it reads only the AS HTTP API — there is no SimConnect
    /// fallback for decoded station weather) and an announcement feature (it speaks),
    /// so it requires both opt-ins. Gating on <c>ActiveSkyEnabled</c> alone forced AS
    /// users who only wanted accurate output+I wind and radar data to also be spoken at.
    ///
    /// Called from MainForm.InitializeManagers (launch) and ApplyRuntimeSettings (live
    /// toggle) — never inline the condition at either site, or the two will drift.
    /// </summary>
    public static bool ShouldRun(Settings.UserSettings settings)
        => settings.ActiveSkyEnabled && settings.WeatherAutoAnnounceEnabled;

```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ActiveSkyWeatherMonitorGateTests"`

Expected: PASS (5/5 — four `[InlineData]` cases plus the default-settings fact).

- [ ] **Step 5: Wire the launch site**

In `MSFSBlindAssist/MainForm.cs`, replace this block (currently at ~line 591-601):

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

with:

```csharp
        // ActiveSky weather-update announcer. Constructed always (so the settings
        // dialog can start it live via ApplyRuntimeSettings), but STARTED only when
        // ActiveSkyWeatherMonitor.ShouldRun says so — the user must have opted into
        // ActiveSky AND asked for weather announcements. When the AS switch is off no
        // AS code may run at all, and even when it's on but AS isn't running, each
        // poll would be a ~1.2 s parallel-probe timeout.
        activeSkyWeatherMonitor = new MSFSBlindAssist.Services.ActiveSkyWeatherMonitor(
            new MSFSBlindAssist.Services.ActiveSkyClient(), announcer);
        activeSkyWeatherMonitor.IntervalMinutes =
            MSFSBlindAssist.Settings.SettingsManager.Current.WeatherAutoAnnounceIntervalMinutes;
        activeSkyWeatherMonitor.Enabled = MSFSBlindAssist.Services.ActiveSkyWeatherMonitor
            .ShouldRun(MSFSBlindAssist.Settings.SettingsManager.Current);
```

- [ ] **Step 6: Wire the live-toggle site**

In `MSFSBlindAssist/MainForm.MenuHandlers.cs`, replace this block (currently at ~line 106-113):

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

with:

```csharp
        if (activeSkyWeatherMonitor != null)
        {
            activeSkyWeatherMonitor.IntervalMinutes = settings.WeatherAutoAnnounceIntervalMinutes;
            // Both Weather-tab switches take effect without restart. The Enabled setter
            // maps to Start()/Stop(), and System.Windows.Forms.Timer.Start/Stop are
            // idempotent, so an unchanged setting is a no-op.
            activeSkyWeatherMonitor.Enabled =
                MSFSBlindAssist.Services.ActiveSkyWeatherMonitor.ShouldRun(settings);
        }
```

- [ ] **Step 7: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 warnings.

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass (1035 existing + 5 new = 1040). NOTE: the PR body's "1031" predates commit `bce0c6c8`, which added 4 interval-visibility tests; the true baseline on this branch is 1035.

- [ ] **Step 8: Commit**

```bash
git add MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs MSFSBlindAssist/MainForm.cs MSFSBlindAssist/MainForm.MenuHandlers.cs tests/MSFSBlindAssist.Tests/ActiveSkyWeatherMonitorGateTests.cs
git commit -m "$(cat <<'EOF'
fix(weather): decouple the decoded-weather monitor from the ActiveSky switch

ActiveSkyWeatherMonitor was started on ActiveSkyEnabled alone, so a user
who enabled ActiveSky purely for accurate output+I wind, gusts and radar
data could not stop the periodic spoken "Weather update..." announcements.
WeatherAutoAnnounceEnabled never reached the monitor at all.

It now runs iff ShouldRun(settings) -- ActiveSkyEnabled AND
WeatherAutoAnnounceEnabled -- a pure static on the monitor itself, called
from both the launch site and the live-toggle site so they cannot drift.

No SimConnect path is affected: the monitor reads only the AS HTTP API and
has no SimConnect fallback, and CheckAmbientWeatherChanges (cloud/precip/
visibility) runs on a different timer gated on WeatherAutoAnnounceEnabled
alone.

RELEASE NOTE: an existing ActiveSky user who has weather updates today but
has never ticked "Auto-announce weather state changes" will go silent
after this change. That is the intended decoupling.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: Interval-combo visibility follows the monitor predicate

`WeatherAutoAnnounceIntervalMinutes` is read in exactly one place in the whole codebase —
it becomes `ActiveSkyWeatherMonitor.IntervalMinutes`. After Task 1 the monitor needs both
flags, so the combo governs nothing unless both boxes are ticked. The existing invariant
("a blind user tabbing the panel must not meet a setting they can't use") should track the
real governing condition.

**Files:**
- Modify: `MSFSBlindAssist/Forms/Settings/WeatherPanel.cs:40-54`
- Test: `tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs` (append two tests)

**Interfaces:**
- Consumes: `UserSettings.ActiveSkyEnabled`, `UserSettings.WeatherAutoAnnounceEnabled`. The existing private fields `_activeSkyEnabled`, `_weatherAutoAnnounce`, `_weatherIntervalLabel`, `_weatherIntervalCombo` (all already declared).
- Consumes (cont.): `ActiveSkyWeatherMonitor.ShouldRun(UserSettings)` from Task 1.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the failing tests**

Append to `tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs`, immediately before the final closing `}` of the class:

```csharp
    // ---- After the monitor decoupling, the interval throttles the decoded-weather
    // monitor, which runs only when BOTH the ActiveSky switch and the auto-announce
    // checkbox are on. So the combo must be visible under exactly that condition --
    // it governs nothing otherwise.

    [Fact]
    public void IntervalSetting_HiddenWhenActiveSkyOnButAutoAnnounceOff()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = true, WeatherAutoAnnounceEnabled = false });

        Assert.False(FindByAccessibleName(panel, "Weather announcement interval").Visible);
        Assert.False(FindByAccessibleName(panel, "Weather announcement interval label").Visible);
    }

    [Fact]
    public void IntervalSetting_HiddenWhenAutoAnnounceOnButActiveSkyOff()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = false, WeatherAutoAnnounceEnabled = true });

        Assert.False(FindByAccessibleName(panel, "Weather announcement interval").Visible);
    }

    [Fact]
    public void IntervalSetting_VisibilityFollowsEitherCheckbox_Live()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = true, WeatherAutoAnnounceEnabled = true });
        var activeSky = (CheckBox)FindByAccessibleName(panel, "Enable ActiveSky integration");
        var autoAnnounce = (CheckBox)FindByAccessibleName(panel, "Auto-announce weather state changes");
        var combo = FindByAccessibleName(panel, "Weather announcement interval");

        Assert.True(combo.Visible);
        autoAnnounce.Checked = false;
        Assert.False(combo.Visible);
        autoAnnounce.Checked = true;
        Assert.True(combo.Visible);
        activeSky.Checked = false;
        Assert.False(combo.Visible);
    }
```

Two existing tests in this file assume the OLD single-flag rule and must be updated in the
same edit, because `new UserSettings()` has `WeatherAutoAnnounceEnabled = false`:

Replace the existing `IntervalSetting_VisibleWhileActiveSkyEnabled`:

```csharp
    [Fact]
    public void IntervalSetting_VisibleWhileActiveSkyEnabled()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = true });

        Assert.True(FindByAccessibleName(panel, "Weather announcement interval").Visible);
        Assert.True(FindByAccessibleName(panel, "Weather announcement interval label").Visible);
    }
```

with:

```csharp
    [Fact]
    public void IntervalSetting_VisibleWhenBothActiveSkyAndAutoAnnounceEnabled()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = true, WeatherAutoAnnounceEnabled = true });

        Assert.True(FindByAccessibleName(panel, "Weather announcement interval").Visible);
        Assert.True(FindByAccessibleName(panel, "Weather announcement interval label").Visible);
    }
```

And replace the existing `IntervalSetting_FollowsCheckboxToggle_Live` (its toggle of the AS
box alone no longer reveals the combo) with:

```csharp
    [Fact]
    public void IntervalSetting_FollowsCheckboxToggle_Live()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { WeatherAutoAnnounceEnabled = true });
        var checkbox = (CheckBox)FindByAccessibleName(panel, "Enable ActiveSky integration");
        var combo = FindByAccessibleName(panel, "Weather announcement interval");

        checkbox.Checked = true;
        Assert.True(combo.Visible);
        checkbox.Checked = false;
        Assert.False(combo.Visible);
    }
```

`IntervalSetting_HiddenWhileActiveSkyDisabled` and `HiddenInterval_StillRoundTripsItsValue`
need no change — both already pass under the stricter rule.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~WeatherPanelTests"`

Expected: FAIL. `IntervalSetting_HiddenWhenActiveSkyOnButAutoAnnounceOff` fails with
`Assert.False() Failure — Expected: False, Actual: True` (the combo is still visible on the
AS flag alone), and `IntervalSetting_VisibleWhenBothActiveSkyAndAutoAnnounceEnabled` passes
already. That mix is the correct RED.

- [ ] **Step 3: Implement the both-flags predicate**

In `MSFSBlindAssist/Forms/Settings/WeatherPanel.cs`, replace this block:

```csharp
        // The announcement interval throttles ACTIVESKY decoded-weather announcements
        // only, so it's hidden (and out of the tab order) while the switch is off — a
        // blind user tabbing the panel shouldn't meet an AS-specific setting they can't
        // use. Hiding never resets the stored value (ApplyTo reads the combo regardless).
        _activeSkyEnabled.CheckedChanged += (_, _) => UpdateActiveSkyDependentVisibility();
        UpdateActiveSkyDependentVisibility();
    }

    private void UpdateActiveSkyDependentVisibility()
    {
        bool on = _activeSkyEnabled.Checked;
        _weatherIntervalLabel.Visible = on;
        _weatherIntervalCombo.Visible = on;
    }
```

with:

```csharp
        // The announcement interval throttles the ActiveSky decoded-weather monitor,
        // and nothing else — WeatherAutoAnnounceIntervalMinutes is read in exactly one
        // place in the codebase, where it becomes ActiveSkyWeatherMonitor.IntervalMinutes.
        // That monitor runs only when BOTH switches are on (ActiveSkyWeatherMonitor.
        // ShouldRun), so the combo is hidden — and out of the tab order — otherwise: a
        // blind user tabbing the panel shouldn't meet a setting that governs nothing.
        // Hiding never resets the stored value (ApplyTo reads the combo regardless).
        _activeSkyEnabled.CheckedChanged += (_, _) => UpdateActiveSkyDependentVisibility();
        _weatherAutoAnnounce.CheckedChanged += (_, _) => UpdateActiveSkyDependentVisibility();
        UpdateActiveSkyDependentVisibility();
    }

    /// <summary>Defers to ActiveSkyWeatherMonitor.ShouldRun — the single source of truth
    /// for whether the monitor (and therefore its interval) is live. The panel has no
    /// UserSettings at CheckedChanged time, so it builds a throwaway one from the live
    /// checkbox state; one allocation per toggle is free at human interaction rates, and
    /// it means the rule can never drift between the settings UI and the monitor.</summary>
    private void UpdateActiveSkyDependentVisibility()
    {
        bool on = Services.ActiveSkyWeatherMonitor.ShouldRun(new UserSettings
        {
            ActiveSkyEnabled = _activeSkyEnabled.Checked,
            WeatherAutoAnnounceEnabled = _weatherAutoAnnounce.Checked
        });
        _weatherIntervalLabel.Visible = on;
        _weatherIntervalCombo.Visible = on;
    }
```

`WeatherPanel.cs` is in namespace `MSFSBlindAssist.Forms.Settings` and already has
`using MSFSBlindAssist.Settings;` at the top, so `UserSettings` resolves unqualified.
`Services.ActiveSkyWeatherMonitor` resolves via the root namespace — if the compiler
disagrees, fully qualify as `MSFSBlindAssist.Services.ActiveSkyWeatherMonitor`. Do not
add a `using` that shadows `Forms.Settings`.

**Do NOT restate the predicate inline as `_activeSkyEnabled.Checked &&
_weatherAutoAnnounce.Checked`.** That was the original draft; it was rejected because it
encodes one rule in two places.

**Ordering hazard:** `InitializeComponent` builds `activeSkyGroup` before `announceGroup`,
so `_weatherAutoAnnounce` is null until `BuildAnnouncementsGroup()` has run. Both
`CheckedChanged` subscriptions above sit AFTER both `Build*Group()` calls, so this is
already safe — do not move them earlier.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~WeatherPanelTests"`

Expected: PASS (10/10 — 7 existing after the two rewrites, plus 3 new).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/Settings/WeatherPanel.cs tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs
git commit -m "$(cat <<'EOF'
fix(settings): hide the weather interval unless both Weather switches are on

WeatherAutoAnnounceIntervalMinutes throttles the ActiveSky decoded-weather
monitor and nothing else. After the monitor decoupling that monitor needs
both ActiveSkyEnabled and WeatherAutoAnnounceEnabled, so the combo governed
nothing when only the AS switch was ticked -- yet it sat in the tab order.

Visibility now mirrors ActiveSkyWeatherMonitor.ShouldRun, subscribed to
CheckedChanged on both boxes. ApplyTo still reads the combo regardless of
visibility, so a hidden interval round-trips (pinned by
HiddenInterval_StillRoundTripsItsValue).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Make the vacuous ActiveSky gate tests real

`GetCurrentConditions_WhenDisabled_ReturnsNull` constructs a fresh `ActiveSkyClient`, whose
`LastSuccessfulPort` is already null — the pre-existing `if (LastSuccessfulPort is not int
port) return null;` satisfies the assertion, so the test passes with the master-switch guard
deleted. And nothing pins `LastSuccessfulPort = null` in the disabled branch of
`IsRunningAsync()`, the single line that closes the enable→discover→disable cached-port leak.

Both are untestable because the setter is private. `MSFSBlindAssist/Properties/InternalsVisibleTo.cs`
already contains `[assembly: InternalsVisibleTo("MSFSBlindAssist.Tests")]`, so widening it to
`internal` costs nothing and leaks nothing to app code.

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyClient.cs:46`
- Modify: `tests/MSFSBlindAssist.Tests/ActiveSkyGateTests.cs`

**Interfaces:**
- Consumes: `ActiveSkyClient.IsRunningAsync()`, `ActiveSkyClient.GetCurrentConditionsAsync()`, `ActiveSkyClient.LastStatus` (`string`, public getter), `SettingsManager.Current.ActiveSkyEnabled`.
- Produces: `ActiveSkyClient.LastSuccessfulPort { get; internal set; }` (`int?`). No later task consumes this.

- [ ] **Step 1: Widen the setter**

In `MSFSBlindAssist/Services/ActiveSkyClient.cs`, replace:

```csharp
    /// <summary>The port we successfully reached AS on, or null if we haven't.</summary>
    public int? LastSuccessfulPort { get; private set; }
```

with:

```csharp
    /// <summary>The port we successfully reached AS on, or null if we haven't.
    /// Setter is `internal` solely so the test assembly can seed a cached port and pin
    /// the disabled-branch reset in <see cref="IsRunningAsync"/> (the enable → discover →
    /// disable cached-port leak). App code must never assign it.</summary>
    public int? LastSuccessfulPort { get; internal set; }
```

This is not a behavior change, so it needs no test of its own. It is a precondition for
Steps 2-4 to compile.

- [ ] **Step 2: Write the failing tests**

In `tests/MSFSBlindAssist.Tests/ActiveSkyGateTests.cs`, replace the existing
`GetCurrentConditions_WhenDisabled_ReturnsNull` test:

```csharp
    [Fact]
    public async Task GetCurrentConditions_WhenDisabled_ReturnsNull()
    {
        SettingsManager.Current.ActiveSkyEnabled = false;
        var client = new ActiveSkyClient();
        // No port was ever discovered (and none may be while disabled).
        Assert.Null(await client.GetCurrentConditionsAsync());
    }
```

with these two:

```csharp
    [Fact]
    public async Task IsRunningAsync_WhenDisabled_ClearsCachedPort()
    {
        // The enable -> discover -> disable leak: a port discovered while the switch was
        // on must not survive turning it off, or the per-method guards would be the only
        // thing standing between a disabled integration and live HTTP traffic.
        var client = new ActiveSkyClient { LastSuccessfulPort = 19285 };
        SettingsManager.Current.ActiveSkyEnabled = false;

        Assert.False(await client.IsRunningAsync());
        Assert.Null(client.LastSuccessfulPort);
        Assert.Equal("disabled in settings", client.LastStatus);
    }

    [Fact]
    public async Task GetCurrentConditions_WhenDisabled_ReturnsNull_EvenWithCachedPort()
    {
        // Seed a port FIRST: without it this asserts only the pre-existing
        // `if (LastSuccessfulPort is not int port) return null;` fallthrough and says
        // nothing about the disabled state.
        //
        // HONEST LIMIT: this pins the observable CONTRACT (disabled + cached port =>
        // null), not the mechanism. It cannot distinguish the master-switch guard from
        // an attempted HTTP call that failed -- `_http` is a private static HttpClient
        // with no injection seam, so there is no way to observe "no I/O happened" from a
        // unit test. The airtight guarantee is IsRunningAsync_WhenDisabled_ClearsCachedPort
        // below: with no cached port, the per-method guards have nothing to guard against.
        // The guards remain worth keeping as defense in depth for a future call site.
        var client = new ActiveSkyClient { LastSuccessfulPort = 19285 };
        SettingsManager.Current.ActiveSkyEnabled = false;

        Assert.Null(await client.GetCurrentConditionsAsync());
    }
```

- [ ] **Step 3: Verify the port-reset test actually catches its regression**

Temporarily delete the `LastSuccessfulPort = null;` line from the disabled branch of
`IsRunningAsync()` in `MSFSBlindAssist/Services/ActiveSkyClient.cs` (~line 126).

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ActiveSkyGateTests"`

Expected: `IsRunningAsync_WhenDisabled_ClearsCachedPort` FAILS with
`Assert.Null() Failure — Value: 19285`. This is the point of the task: confirm the test pins
the line that closes the leak. **Restore the deleted line before continuing.**

Do **not** attempt the equivalent mutation on `GetCurrentConditionsAsync`'s guard. Deleting
it makes the method attempt an HTTP call to 19285, which on localhost is refused near-
instantly and returns null from the catch — so the test still passes. That guard is not
independently unit-testable, for the reason recorded in the test's comment. Its correctness
rests on the port reset, which *is* pinned.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter "FullyQualifiedName~ActiveSkyGateTests"`

Expected: PASS (4/4 — `ActiveSkyEnabled_DefaultsToFalse`,
`IsRunningAsync_WhenDisabled_ShortCircuitsFalse_WithoutProbing`,
`IsRunningAsync_WhenDisabled_ClearsCachedPort`,
`GetCurrentConditions_WhenDisabled_ReturnsNull_EvenWithCachedPort`).

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Services/ActiveSkyClient.cs tests/MSFSBlindAssist.Tests/ActiveSkyGateTests.cs
git commit -m "$(cat <<'EOF'
test(weather): pin the ActiveSky cached-port reset and the per-method guard

GetCurrentConditions_WhenDisabled_ReturnsNull was vacuous: a fresh client
already has a null LastSuccessfulPort, so the pre-existing null-port
fallthrough satisfied it and the test passed with the master-switch guard
deleted. And nothing pinned `LastSuccessfulPort = null` in the disabled
branch of IsRunningAsync -- the line that closes the enable -> discover ->
disable cached-port leak.

Widen LastSuccessfulPort's setter to internal (InternalsVisibleTo already
grants the test assembly access) so both tests can seed a cached port.
Verified each new test goes red when its line is removed.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Logging parity across the ActiveSkyClient data methods

PR #156's stated goal was closing the observability gap that made the original hotkey-delay
report undiagnosable. `GetCurrentConditionsAsync` got a `Log.Debug` line in its catch; its
four siblings still swallow silently.

No unit test: this is a logging side effect with no return-value contract, and the codebase
has no log-capture harness. Verified by build plus in-sim step 5 of the test plan.

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyClient.cs` — the catches in `GetWeatherAreaAsync` (~line 271), `GetPositionMetarAsync` (~line 300), `GetClosestStationMetarAsync` (~line 327), `GetMetarAsync` (~line 349)

**Interfaces:**
- Consumes: `MSFSBlindAssist.Utils.Logging.Log.Debug(string category, string message)` — already imported at the top of this file by PR #156.
- Produces: nothing.

**Edit hazard:** all four catch blocks are the literal text `catch\n        {\n            return null;\n        }`. Two of them (`GetPositionMetarAsync`, `GetClosestStationMetarAsync`) are preceded by the *identical* line `return string.IsNullOrWhiteSpace(body) ? null : body;`. Each edit below therefore includes the method's unique `_http.GetAsync` line as an anchor. Do not try to match on the catch alone.

- [ ] **Step 1: `GetWeatherAreaAsync`**

Replace:

```csharp
            using var resp = await _http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = await resp.Content.ReadAsStringAsync(cts.Token);
            return ParseConditionsJson(body);
        }
        catch
        {
            return null;
        }
    }
```

with:

```csharp
            using var resp = await _http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = await resp.Content.ReadAsStringAsync(cts.Token);
            return ParseConditionsJson(body);
        }
        catch
        {
            Log.Debug("ActiveSky", "GetWeatherArea failed (timeout or connection error)");
            return null;
        }
    }
```

- [ ] **Step 2: `GetPositionMetarAsync`**

Replace:

```csharp
            using var resp = await _http.GetAsync($"{BaseUrl(port)}/GetCurrentConditions", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            return null;
        }
    }
```

with:

```csharp
            using var resp = await _http.GetAsync($"{BaseUrl(port)}/GetCurrentConditions", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            Log.Debug("ActiveSky", "GetPositionMetar failed (timeout or connection error)");
            return null;
        }
    }
```

- [ ] **Step 3: `GetClosestStationMetarAsync`**

Replace:

```csharp
            using var resp = await _http.GetAsync($"{BaseUrl(port)}/GetClosestStationWeather", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            return null;
        }
    }
```

with:

```csharp
            using var resp = await _http.GetAsync($"{BaseUrl(port)}/GetClosestStationWeather", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            Log.Debug("ActiveSky", "GetClosestStationMetar failed (timeout or connection error)");
            return null;
        }
    }
```

- [ ] **Step 4: `GetMetarAsync`**

Replace:

```csharp
            using var resp = await _http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            return (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
        }
        catch
        {
            return null;
        }
    }
```

with:

```csharp
            using var resp = await _http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            return (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
        }
        catch
        {
            Log.Debug("ActiveSky", $"GetMetar({icao}) failed (timeout or connection error)");
            return null;
        }
    }
```

- [ ] **Step 5: Confirm the disabled short-circuit stays unlogged**

Read the master-switch branch at the top of `IsRunningAsync()`. It must contain **no**
`Log.Debug` call. The weather timer would spam `debug.log` every 30 s. This is deliberate;
the original implementation plan called it out and it remains correct. Make no change here.

- [ ] **Step 6: Build and run the full suite**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded`, 0 warnings.

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: all pass (1035 baseline + 5 from Task 1 + 3 net-new from Task 2 + 1 net-new from Task 3 = 1044).

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Services/ActiveSkyClient.cs
git commit -m "$(cat <<'EOF'
feat(weather): log ActiveSky data-fetch failures from all five methods

GetCurrentConditionsAsync logged its catch; GetWeatherAreaAsync,
GetPositionMetarAsync, GetClosestStationMetarAsync and GetMetarAsync
swallowed theirs silently. Closing that gap is the stated point of the
client's new logging -- a silent timeout in any of these is exactly the
condition that made the original hotkey-delay report undiagnosable.

The disabled short-circuit in IsRunningAsync stays unlogged on purpose:
the weather timer would spam debug.log every 30 s.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: `docs/weather.md` + repoint the CLAUDE.md invariants

All eight weather/ActiveSky invariants at `CLAUDE.md:248-255` end with
`→ [taxi-guidance.md](docs/taxi-guidance.md)`. That file is 1,483 lines, has zero weather
sections, and uses the word "weather" three times, all incidental. There is no weather doc
anywhere in `docs/`. The mislink predates PR #156; PR #156 is the first change to touch
those bullets.

**Files:**
- Create: `docs/weather.md`
- Modify: `CLAUDE.md:248-255` (the eight bullets), plus the "Detailed Documentation" list and the "when to read" table

**Interfaces:**
- Consumes: nothing. Documentation only.
- Produces: nothing.

- [ ] **Step 1: Write `docs/weather.md`**

Create `docs/weather.md`. Source every claim from the code, not from this plan. Required
sections, in order:

1. **ActiveSky integration is opt-in.** `UserSettings.ActiveSkyEnabled`, default `false`,
   Weather settings tab. The central gate is at the top of `ActiveSkyClient.IsRunningAsync()`:
   it returns `false` instantly, sets `LastStatus = "disabled in settings"`, and nulls
   `LastSuccessfulPort` (closing the enable → discover → disable leak). Per-method guards on
   all five data methods are defense in depth. The liveness probe has a ~1.2 s floor when AS
   is absent — that cost is why the switch exists.
2. **SimConnect remains the weather source when the switch is off.** Reproduce the four-row
   table from the spec's "Non-goal" section (output+I wind, Alt+W cloud/precip/visibility,
   Weather Radar, METAR form), with the file and method name for each fallback.
3. **Wind truth is per-engine.** With AS on, output+I reads AS ambient wind + surface gust.
   With it off, SimConnect is authoritative and correct. AS wind smoothing makes the two
   legitimately differ. Neither is "the" truth — never hard-pick one for both populations.
4. **Precip source precedence**, shared by all three readouts (Weather Radar, decoded-weather
   monitor, Alt+W auto-announce): closest-station METAR → position METAR → SimConnect
   bitmask. A METAR that says "no precipitation" renders as "None" and must never fall
   through; only a wholly-missing METAR triggers fallback.
5. **The decoded-weather monitor lifecycle.** Constructed always in
   `MainForm.InitializeManagers`; `Enabled` gated on `ActiveSkyWeatherMonitor.ShouldRun` —
   `ActiveSkyEnabled && WeatherAutoAnnounceEnabled` — at launch and live-toggled from
   `ApplyRuntimeSettings`. It reads only the AS HTTP API; there is no SimConnect fallback for
   decoded station weather. `CheckAmbientWeatherChanges` is a *different* feature on a
   different timer, gated on `WeatherAutoAnnounceEnabled` alone.
6. **The no-repeat rule** for the precip auto-announce: compare the decoded phrase trimmed +
   case-insensitive; speak only on start / stop / a genuinely different phrase.
7. **The decoder duplication.** `WeatherRadarForm.ParsePrecipFromMetar` has a duplicate in
   the weather monitor. Do not move one without updating the other.
8. **Two standing gotchas:** never scan `%APPDATA%\HiFi\` for the AS port (recursive
   enumeration there blocks the UI thread for many seconds); turbulence ≤ 25 is the AS
   calm-weather baseline and must be hidden entirely, never shown as a raw alarming number.

- [ ] **Step 2: Repoint the eight invariants**

In `CLAUDE.md`, on each of lines 248-255, replace the trailing
`→ [taxi-guidance.md](docs/taxi-guidance.md)` with `→ [weather.md](docs/weather.md)`.

Those eight bullets begin, in order:
`- Never scan \`%APPDATA%\HiFi\\\` for the ActiveSky settings-file port…`,
`- A "METAR says no precipitation" result…`,
`- Turbulence ≤25…`,
`- Don't move the WMO/ICAO weather-token decoder…`,
`- All three precipitation readouts…`,
`- The ActiveSky precip auto-announce must NOT repeat an unchanged phrase…`,
`- ActiveSky integration is strictly OPT-IN…`,
`- Wind truth is PER-ENGINE…`.

**Do NOT touch line 256** (`- The Cold Temperature Correction math must be transcribed
VERBATIM…`). It is also mislinked to `taxi-guidance.md`, but it is an EFB altitude-correction
feature documented nowhere and is explicitly out of scope. Leave it exactly as it is.

- [ ] **Step 3: Amend the ActiveSky opt-in invariant for the decoupling**

Task 1 changed the monitor's gate, so the seventh bullet's tail is now wrong. In `CLAUDE.md`,
find the fragment:

```
and the `ActiveSkyWeatherMonitor` is additionally only started when enabled.
```

and replace it with:

```
and the `ActiveSkyWeatherMonitor` additionally runs only when `ActiveSkyWeatherMonitor.ShouldRun` says so (`ActiveSkyEnabled` AND `WeatherAutoAnnounceEnabled`) — the AS switch alone must never start the spoken weather updates.
```

- [ ] **Step 4: Register the new doc in both CLAUDE.md indexes**

In the **"When to read detailed docs"** list, add, after the GSX line:

```markdown
- **Working on ActiveSky integration, the weather radar, METAR readouts, or weather auto-announcements** → [Weather](docs/weather.md)
```

In the **"Available documentation"** list, add, after the GSX Integration entry:

```markdown
- **[Weather](docs/weather.md)** - ActiveSky opt-in gate, SimConnect fallbacks, per-engine wind truth, precip source precedence, decoded-weather monitor lifecycle
```

- [ ] **Step 5: Verify every repointed link resolves**

Run: `grep -c "weather.md" CLAUDE.md`
Expected: `10` (eight repointed invariants + the two index entries).

Run: `grep -n "taxi-guidance.md" CLAUDE.md | grep -ci "activesky\|precip\|metar\|turbulence\|wind truth"`
Expected: `0` — no weather bullet still points at the taxi doc.

Run: `test -f docs/weather.md && echo OK`
Expected: `OK`

- [ ] **Step 6: Commit**

```bash
git add docs/weather.md CLAUDE.md
git commit -m "$(cat <<'EOF'
docs(weather): add docs/weather.md; repoint the mislinked weather invariants

All eight weather/ActiveSky invariants in CLAUDE.md pointed at
docs/taxi-guidance.md, which has 1,483 lines, zero weather sections, and
three incidental uses of the word "weather". There was no weather doc at
all. The mislink predates PR #156, but #156 is the first change to touch
those bullets.

Also amends the opt-in invariant to record the monitor decoupling: the AS
switch alone must never start the spoken weather updates.

CLAUDE.md:256 (Cold Temperature Correction) is mislinked the same way but
is an EFB altitude feature documented nowhere -- left out of scope.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification

- [ ] **Full build, zero warnings**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Full suite green**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: 1044 passed, 0 failed.

- [ ] **Update the PR body**

Add to PR #156's "Known behavior notes (deliberate)" section:

```markdown
- **Second release-note line required.** An existing ActiveSky user who has spoken weather
  updates today but has never ticked "Auto-announce weather state changes" will go silent
  after this change. That is the intended decoupling of the AS integration from the
  announcement feature, not a regression.
```

## In-sim test plan (sim-facing; repo owner runs)

Sim-facing behavior cannot be unit-tested. Run these against a live sim:

1. **Switch off, "Auto-announce" ON, no ActiveSky installed.** Fly into cloud and into rain.
   Cloud entry/exit, precip start/stop, and visibility crossings all announce from SimConnect.
   `%APPDATA%\MSFSBlindAssist\logs\debug.log` contains zero `[ActiveSky]` lines. output+I
   answers instantly with SimConnect wind and no "not responding" prefix.
   *(This is the regression the decoupling must not cause — the most important step.)*
2. **Switch ON, "Auto-announce" OFF, AS running.** No spoken "Weather update…" ever. output+I
   still speaks AS wind + gust. Radar still shows AS data. The interval combo is hidden from
   the Weather tab and absent from the tab order (verify by tabbing with NVDA).
3. **Switch ON, "Auto-announce" ON, AS running.** Interval combo visible. Monitor announces AS
   weather updates. Untick "Auto-announce" and save → announcements stop without restart, and
   the combo disappears.
4. **Switch ON, AS not running.** output+I prefixes "ActiveSky not responding, using simulator
   wind." on every press, then the SimConnect wind. Deliberate — a blind pilot must never miss
   a degraded readout.
5. **Enable → let a probe succeed → disable → save.** `debug.log` shows no further
   `[ActiveSky] probe` lines. Radar reverts to SimConnect-only on next refresh. Provoke an AS
   timeout mid-fetch and confirm a `[ActiveSky] Get… failed` line appears (Task 4).

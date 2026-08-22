# Turbulence & Icing Announcements — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Announce turbulence category transitions (ActiveSky-sourced) and airframe ice accretion (sim-truth, all aircraft) with per-announcement settings toggles, per the approved spec `docs/design/2026-07-11-hazard-announcements-design.md`.

**Architecture:** Two pure `internal sealed` tracker classes (the `ActiveSkyModeTracker` pattern — `Observe(...)` → utterance or null, fully CI-tested), each wired into the existing tick that already carries its data: turbulence into `ActiveSkyWeatherMonitor.OnTickAsync` (60 s, AS conditions already fetched), icing into `AnnounceAmbientChanges` (30 s ambient tick) fed by one new field on the `AmbientWeatherData` SimConnect struct. The FBW A380 yields to its own tuned announcer via a new aircraft-definition capability flag.

**Tech Stack:** .NET 10 / C# 13, WinForms, SimConnect, xUnit (x64).

## Global Constraints

- Build ONLY via `dotnet build MSFSBlindAssist.sln -c Debug` — never the bare `.csproj`. 0 warnings.
- Test via `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`. Full suite green after every task (baseline on this branch: **1136**).
- TDD: failing test first (compile error counts as RED in C#).
- Turbulence is words-only — the raw 1–100 value is NEVER spoken, and ≤25 is never named as a category (extends the documented raw-turbulence invariant to the spoken surface).
- All announcements via `announcer.Announce` / `_announcer.Announce` (queued — background changes), never `AnnounceImmediate`. No other new announcements.
- Every new `UserSettings` property is added in TWO places: the property block AND the `Clone()` object initializer (~line 475) — a property missing from `Clone()` silently doesn't survive cloning.
- Commit after every task, message ending with: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
- New test files: `tests/MSFSBlindAssist.Tests/`, namespace `MSFSBlindAssist.Tests`, implicit `using Xunit;`.

**Key verified facts (2026-07-11):**
- `ActiveSkyWeatherMonitor.OnTickAsync` (`MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs:145+`): after the `IsRunningAsync()` early-return and the mode-tracker block, it fetches conditions + position METAR and guards `if (conditions == null || string.IsNullOrWhiteSpace(positionMetar)) { ...; return; }` (~line 188-192). After that guard `conditions` (type `ActiveSkyClient.Conditions`) is non-null and carries `AmbientTurbulence` (double, 1–100, 0 when omitted). Tracker-field precedent: `private readonly ActiveSkyModeTracker _modeTracker = new();` (line ~79). Announce pattern to mirror: `string? x = _tracker.Observe(...); if (x != null && !_disposed) { Log.Debug("Services", $"..."); _announcer.Announce(x); }`. The tick runs on the UI thread (WinForms timer).
- `CategorizeTurbulence` boundaries (WeatherRadarForm.cs:518-525): ≤25 → null(smooth), ≤50 light, ≤75 moderate, ≤90 severe, else extreme.
- `AmbientWeatherData` struct (`MSFSBlindAssist/SimConnect/SimConnectManager.cs:573-583`): 7 sequential `double` fields, `Pack = 1`. Registration block (`MSFSBlindAssist/SimConnect/SimConnectManager.Setup.cs:198-213`): seven `sc.AddToDataDefinition(DATA_DEFINITIONS.WEATHER_DATA, "<VAR>", "<units>", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)N)` lines, N = 0..6, then `sc.RegisterDataDefineStruct<AmbientWeatherData>(DATA_DEFINITIONS.WEATHER_DATA);`. A new struct field appends at the END of the struct and registers as `(uint)7` BEFORE the RegisterDataDefineStruct call. The receive path casts the whole struct — no per-field extraction to touch.
- A380 icing announcer (`MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs:2868-2875` + `FlyByWireA380Definition.SimVarUpdate.cs:84-104`): `ICING_DETECT_RATIO = 0.05`, `ICING_CLEAR_RATIO = 0.02`, hysteresis ternary `bool nowIcing = _icingActive ? value > ICING_CLEAR_RATIO : value >= ICING_DETECT_RATIO;`, first-sample baseline via `_icingBaselineDone`. Its fields live on the def instance (a fresh instance is constructed per switch), so it needs no reset wiring.
- `IAircraftDefinition` (`MSFSBlindAssist/Aircraft/IAircraftDefinition.cs`): last member is `double TaxiTurnLeadSeconds { get; }` (~line 324, closing brace 325). `BaseAircraftDefinition` (`MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs`): virtual-default precedent `public virtual double TaxiTurnLeadSeconds => 1.2;` (~line 672). `FlyByWireA380Definition` overrides go near line 50 (`public override double TaxiTurnLeadSeconds => 1.8;`).
- MainForm: `private IAircraftDefinition currentAircraft;` (MainForm.cs:347). `AnnounceAmbientChanges` (MainForm.Announcers.cs:1976+) — the cloud announcer block is lines ~1979-1983. Ambient `_prev*` baselines reset ONLY in the connect block (`MainForm.AircraftSwitch.cs:197-207`, starting `_prevPrecipState = -1;` and ending `weatherAnnouncementTimer?.Start();`). `SwitchAircraft` reset anchor: `simVarMonitor.Reset();` (`MainForm.AircraftSwitch.cs:601`). Monitor field: `activeSkyWeatherMonitor` (nullable, MainForm.cs:163).
- `UserSettings` (`MSFSBlindAssist/Settings/UserSettings.cs`): Weather block ends `public bool DecodeWeatherAdvisories { get; set; } = false;` (~line 360); default-true precedent `public bool AltitudeCalloutsEnabled { get; set; } = true;`; `Clone()` weather lines at ~475-481.
- `WeatherPanel` (`MSFSBlindAssist/Forms/Settings/WeatherPanel.cs`): `BuildAnnouncementsGroup()` (lines 113-198), group `Size (460, 230)`; rows: `_weatherAutoAnnounce` y=24, `_sigmetAlerts` y=60, `_pirepAlerts` y=96, `rangeLabel` y=138, `_proximityRange` y=134(x=270), `_weatherIntervalLabel` y=178, `_weatherIntervalCombo` y=202. The Announcements group's panel position auto-reflows (`activeSkyGroup.Bottom + 12`), and `AutoScroll = true`. `UpdateActiveSkyDependentVisibility()` (lines 57-71) is wired to both `CheckedChanged` events + called once at init. `LoadFrom`/`ApplyTo` at 207-237.
- `WeatherPanelTests` is hermetic (`[Collection("SettingsManagerGlobalState")]`, ctor forces the AS gate off, `Dispose` restores) with a `FindByAccessibleName(Control, string)` helper.

---

### Task 1: `TurbulenceCategoryTracker` (pure) + tests

**Files:**
- Create: `MSFSBlindAssist/Services/TurbulenceCategoryTracker.cs`
- Test: `tests/MSFSBlindAssist.Tests/TurbulenceCategoryTrackerTests.cs`

**Interfaces:**
- Produces: `internal sealed class TurbulenceCategoryTracker` with `public string? Observe(double turbulence)` and `public void Reset()`. Task 2 wires it into the monitor; the exact utterance strings below are contractual.

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/TurbulenceCategoryTrackerTests.cs`:

```csharp
// Pins the turbulence announcement rules (spec 2026-07-11 §2.1): baseline-first
// silence, category boundaries identical to the radar form's CategorizeTurbulence
// (≤25 smooth — never named — / ≤50 light / ≤75 moderate / ≤90 severe / else
// extreme), rising at the boundary, easing only 5 points below each re-crossed
// boundary, words only (never the raw number).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class TurbulenceCategoryTrackerTests
{
    [Fact]
    public void First_read_baselines_silently_even_inside_turbulence()
    {
        var t = new TurbulenceCategoryTracker();
        Assert.Null(t.Observe(60));                    // moderate at startup: silent
        Assert.Null(t.Observe(60));                    // unchanged: silent
    }

    [Fact]
    public void Entering_from_smooth_names_the_category()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(10);                                 // baseline smooth
        Assert.Equal("Entering light turbulence", t.Observe(30));
    }

    [Fact]
    public void Jump_from_smooth_straight_to_moderate_announces_moderate()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(10);
        Assert.Equal("Entering moderate turbulence", t.Observe(60));
    }

    [Fact]
    public void Worsening_between_categories_says_now()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(30);                                 // baseline light
        Assert.Equal("Turbulence now moderate", t.Observe(60));
        Assert.Equal("Turbulence now extreme", t.Observe(95));
    }

    [Fact]
    public void Easing_between_categories_says_easing()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(95);                                 // baseline extreme
        Assert.Equal("Turbulence easing to moderate", t.Observe(60));
    }

    [Fact]
    public void Easing_to_smooth_says_smooth_air()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(30);                                 // baseline light
        Assert.Equal("Smooth air", t.Observe(10));
    }

    [Fact]
    public void Rising_edge_is_exactly_the_boundary()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(10);
        Assert.Null(t.Observe(25));                    // 25 is still smooth
        Assert.Equal("Entering light turbulence", t.Observe(26));
    }

    [Fact]
    public void Easing_needs_five_points_below_the_boundary()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(30);                                 // light
        Assert.Null(t.Observe(25));                    // raw smooth, but > 20: stays light
        Assert.Null(t.Observe(21));                    // still inside the margin
        Assert.Equal("Smooth air", t.Observe(20));     // cleared 25 − 5
    }

    [Fact]
    public void Boundary_oscillation_never_flaps()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(10);
        Assert.Equal("Entering light turbulence", t.Observe(26));
        Assert.Null(t.Observe(25));
        Assert.Null(t.Observe(26));
        Assert.Null(t.Observe(24));
        Assert.Null(t.Observe(27));                    // never re-announced
    }

    [Fact]
    public void Deep_drop_steps_only_through_cleared_boundaries()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(60);                                 // moderate
        // 22 clears the 50-boundary (≤45) but NOT the 25-boundary (needs ≤20):
        Assert.Equal("Turbulence easing to light", t.Observe(22));
        Assert.Equal("Smooth air", t.Observe(19));
    }

    [Fact]
    public void Gap_survival_a_change_across_a_gap_announces()
    {
        // The monitor never calls Observe while AS is unreachable; the baseline
        // survives, so a genuine category change across the gap announces.
        var t = new TurbulenceCategoryTracker();
        t.Observe(30);                                 // light, then gap
        Assert.Equal("Turbulence now severe", t.Observe(80));
    }

    [Fact]
    public void Reset_rebaselines_silently()
    {
        var t = new TurbulenceCategoryTracker();
        t.Observe(30);
        t.Reset();
        Assert.Null(t.Observe(80));                    // first read after reset: silent
        Assert.Equal("Turbulence now extreme", t.Observe(95));
    }
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet build tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64`
Expected: FAIL, CS0246/CS0103 — `TurbulenceCategoryTracker` missing. Any other error is a typo.

- [ ] **Step 3: Create `MSFSBlindAssist/Services/TurbulenceCategoryTracker.cs`**

```csharp
namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure decision logic for the turbulence auto-announcement (spec 2026-07-11 §2.1).
/// Categories reuse the Weather Radar's CategorizeTurbulence boundaries verbatim
/// (≤25 smooth / ≤50 light / ≤75 moderate / ≤90 severe / else extreme). Rising
/// transitions happen at the boundary; easing requires the value to clear each
/// re-crossed boundary by HYSTERESIS points, so a value oscillating on a boundary
/// never flaps. Baseline-first: the first read is silent, and the baseline
/// deliberately survives AS-unreachable gaps (a change across a gap announces).
/// Words only — the raw 1–100 number is never part of an utterance, and smooth
/// (≤25) is never named as a category, per the documented turbulence invariant.
/// </summary>
internal sealed class TurbulenceCategoryTracker
{
    private const double HYSTERESIS = 5.0;

    // Index 0 = smooth (never named), 1..4 = spoken category words.
    private static readonly string[] CategoryWords = { "", "light", "moderate", "severe", "extreme" };

    // Lower boundary of each category: category N is entered when the value
    // exceeds Boundaries[N-1] (light > 25, moderate > 50, severe > 75, extreme > 90).
    private static readonly double[] LowerBoundaries = { 25, 50, 75, 90 };

    private int _category = -1;   // -1 = no baseline yet

    public string? Observe(double turbulence)
    {
        if (double.IsNaN(turbulence)) return null;

        if (_category < 0)
        {
            _category = RawCategory(turbulence);
            return null;                               // baseline-first: silent
        }

        int target = CategoryWithHysteresis(turbulence, _category);
        if (target == _category) return null;

        int previous = _category;
        _category = target;

        if (target == 0) return "Smooth air";
        if (previous == 0) return $"Entering {CategoryWords[target]} turbulence";
        return target > previous
            ? $"Turbulence now {CategoryWords[target]}"
            : $"Turbulence easing to {CategoryWords[target]}";
    }

    public void Reset() => _category = -1;

    private static int RawCategory(double v)
    {
        if (v <= 25) return 0;
        if (v <= 50) return 1;
        if (v <= 75) return 2;
        if (v <= 90) return 3;
        return 4;
    }

    /// <summary>Rising uses raw boundaries; falling steps down only through
    /// boundaries the value clears by the hysteresis margin.</summary>
    private static int CategoryWithHysteresis(double v, int current)
    {
        int raw = RawCategory(v);
        if (raw >= current) return raw;
        int cat = current;
        while (cat > raw && v <= LowerBoundaries[cat - 1] - HYSTERESIS)
            cat--;
        return cat;
    }
}
```

- [ ] **Step 4: Verify GREEN**

Run: `dotnet test tests/MSFSBlindAssist.Tests/MSFSBlindAssist.Tests.csproj -c Debug -p:Platform=x64 --filter TurbulenceCategoryTrackerTests`
Expected: PASS (12 tests). Then the FULL suite — 1148 green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(weather): turbulence category tracker (pure, baseline-first)

Reuses the radar form's category boundaries; rising at the boundary,
easing only 5 points below each re-crossed boundary; words only, smooth
never named; baseline survives AS-unreachable gaps.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Wire turbulence announcements into the monitor tick

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs`
- Modify: `MSFSBlindAssist/Settings/UserSettings.cs` (the `AnnounceTurbulenceEnabled` setting — needed by the wiring)
- Modify: `MSFSBlindAssist/MainForm.AircraftSwitch.cs` (aircraft-switch reset)

**Interfaces:**
- Consumes: `TurbulenceCategoryTracker` (Task 1), `Conditions.AmbientTurbulence`.
- Produces: `public void ActiveSkyWeatherMonitor.ResetTurbulenceTracker()`; `UserSettings.AnnounceTurbulenceEnabled` (bool, default true) — Task 5's panel binds it.

- [ ] **Step 1: Add the setting (both edit sites)**

In `MSFSBlindAssist/Settings/UserSettings.cs`, after `public bool DecodeWeatherAdvisories { get; set; } = false;` (~line 360), add:

```csharp
        /// <summary>
        /// Speak turbulence category transitions (ActiveSky-sourced; rides the
        /// decoded-weather monitor, so it needs ActiveSkyEnabled AND
        /// WeatherAutoAnnounceEnabled to be live). Default on.
        /// </summary>
        public bool AnnounceTurbulenceEnabled { get; set; } = true;

        /// <summary>
        /// Speak airframe ice-accretion start/clear (sim-truth STRUCTURAL ICE PCT,
        /// any weather engine; rides the ambient auto-announce tick, so it needs
        /// WeatherAutoAnnounceEnabled to be live). Default on.
        /// </summary>
        public bool AnnounceIcingEnabled { get; set; } = true;
```

And in `Clone()`'s object initializer, after `DecodeWeatherAdvisories = DecodeWeatherAdvisories,` (~line 481), add:

```csharp
            AnnounceTurbulenceEnabled = AnnounceTurbulenceEnabled,
            AnnounceIcingEnabled = AnnounceIcingEnabled,
```

(Both settings land in this task so Task 4 can reference `AnnounceIcingEnabled` without ordering pain; Task 5 adds the UI.)

- [ ] **Step 2: Wire the tracker into `OnTickAsync`**

In `MSFSBlindAssist/Services/ActiveSkyWeatherMonitor.cs`:

2a. Field, directly under `private readonly ActiveSkyModeTracker _modeTracker = new();`:

```csharp
    private readonly TurbulenceCategoryTracker _turbulenceTracker = new();
```

2b. In `OnTickAsync`, immediately AFTER the `if (conditions == null || string.IsNullOrWhiteSpace(positionMetar)) { ...; return; }` guard (the tracker needs the fetched conditions), insert:

```csharp
            // Turbulence category announce (baseline-first; TurbulenceCategoryTracker).
            // Per-call settings read keeps the Weather-tab toggle live with no rewiring.
            // Placed before the weather-refresh logic so a category change announces
            // even on ticks the refresh detector considers "unchanged weather".
            if (Settings.SettingsManager.Current.AnnounceTurbulenceEnabled)
            {
                string? turb = _turbulenceTracker.Observe(conditions.AmbientTurbulence);
                if (turb != null && !_disposed)
                {
                    Log.Debug("Services", $"turbulence: \"{turb}\"");
                    _announcer.Announce(turb);
                }
            }
```

2c. Public reset for aircraft switches, next to `Start()`/`Stop()`:

```csharp
    /// <summary>Re-baselines the turbulence announcer (aircraft switch — position
    /// and airframe discontinuities make the old category baseline meaningless).
    /// The next successful poll re-baselines silently.</summary>
    public void ResetTurbulenceTracker() => _turbulenceTracker.Reset();
```

- [ ] **Step 3: Reset on aircraft switch**

In `MSFSBlindAssist/MainForm.AircraftSwitch.cs`, in `SwitchAircraft`, directly after the existing `simVarMonitor.Reset();` line (~601), add:

```csharp
        // Hazard announcers re-baseline on switch — stale category/ice baselines
        // from the previous airframe must not announce as "changes".
        activeSkyWeatherMonitor?.ResetTurbulenceTracker();
```

- [ ] **Step 4: Build solution + full suite**

`dotnet build MSFSBlindAssist.sln -c Debug` → 0 warnings. Full suite → 1148 green (no new tests this task; the tracker's behavior is pinned by Task 1, the wiring is sim-facing).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(weather): announce turbulence transitions from the AS monitor tick

Rides the existing 60s conditions fetch; gated per-call on the new
AnnounceTurbulenceEnabled setting (default on) under the monitor's
existing ShouldRun gate; re-baselines on aircraft switch. Also adds
AnnounceIcingEnabled (setting only — wired in the icing task).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: `IceAccretionTracker` (pure) + tests

**Files:**
- Create: `MSFSBlindAssist/Services/IceAccretionTracker.cs`
- Test: `tests/MSFSBlindAssist.Tests/IceAccretionTrackerTests.cs`

**Interfaces:**
- Produces: `internal sealed class IceAccretionTracker` with `public string? Observe(double iceRatio)` and `public void Reset()`; `internal const double DETECT_RATIO = 0.05` / `CLEAR_RATIO = 0.02`. Task 4 wires it.

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/IceAccretionTrackerTests.cs`:

```csharp
// Pins the ice-accretion announcement rules (spec 2026-07-11 §2.2): binary with
// hysteresis using the A380's sim-verified thresholds (rise ≥ 0.05, clear ≤ 0.02,
// mirrored ternary semantics), first-sample baseline silence.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class IceAccretionTrackerTests
{
    [Fact]
    public void First_sample_is_silent_even_when_already_icing()
    {
        var t = new IceAccretionTracker();
        Assert.Null(t.Observe(0.30));                  // loads mid-icing: adopt silently
        Assert.Null(t.Observe(0.35));                  // still icing: silent
        Assert.Equal("Icing conditions cleared", t.Observe(0.01));
    }

    [Fact]
    public void Rising_edge_at_exactly_the_detect_threshold()
    {
        var t = new IceAccretionTracker();
        t.Observe(0.0);                                // baseline: not icing
        Assert.Null(t.Observe(0.049));
        Assert.Equal("Icing conditions, ice accumulating", t.Observe(0.05));
    }

    [Fact]
    public void Dead_band_is_silent_in_both_states()
    {
        var t = new IceAccretionTracker();
        t.Observe(0.0);
        Assert.Null(t.Observe(0.03));                  // not icing: below detect → silent
        t.Observe(0.06);                               // now icing (announced)
        Assert.Null(t.Observe(0.03));                  // icing: above clear → still icing
    }

    [Fact]
    public void Falling_edge_at_or_below_the_clear_threshold()
    {
        var t = new IceAccretionTracker();
        t.Observe(0.0);
        t.Observe(0.10);                               // icing
        Assert.Null(t.Observe(0.021));                 // just above clear
        Assert.Equal("Icing conditions cleared", t.Observe(0.02));
    }

    [Fact]
    public void Repeated_cycles_announce_each_edge_once()
    {
        var t = new IceAccretionTracker();
        t.Observe(0.0);
        Assert.Equal("Icing conditions, ice accumulating", t.Observe(0.08));
        Assert.Null(t.Observe(0.09));
        Assert.Equal("Icing conditions cleared", t.Observe(0.0));
        Assert.Equal("Icing conditions, ice accumulating", t.Observe(0.06));
    }

    [Fact]
    public void Reset_rebaselines_silently()
    {
        var t = new IceAccretionTracker();
        t.Observe(0.0);
        t.Observe(0.10);                               // icing
        t.Reset();
        Assert.Null(t.Observe(0.10));                  // first sample after reset: silent
        Assert.Equal("Icing conditions cleared", t.Observe(0.0));
    }
}
```

- [ ] **Step 2: Verify RED** — `IceAccretionTracker` missing.

- [ ] **Step 3: Create `MSFSBlindAssist/Services/IceAccretionTracker.cs`**

```csharp
namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure decision logic for the generic ice-accretion announcement (spec
/// 2026-07-11 §2.2). Binary with hysteresis, using the FBW A380 announcer's
/// sim-verified thresholds and mirrored-ternary semantics
/// (FlyByWireA380Definition: ICING_DETECT_RATIO / ICING_CLEAR_RATIO): rising
/// edge at ratio ≥ 0.05, falling edge at ratio ≤ 0.02, dead band silent.
/// First sample baseline-silenced — an app starting with ice already on the
/// airframe adopts the state without announcing.
/// </summary>
internal sealed class IceAccretionTracker
{
    internal const double DETECT_RATIO = 0.05;   // rising edge → "ice accumulating"
    internal const double CLEAR_RATIO = 0.02;    // falling edge → "cleared"

    private bool _icing;
    private bool _baselineDone;

    public string? Observe(double iceRatio)
    {
        bool now = _icing ? iceRatio > CLEAR_RATIO : iceRatio >= DETECT_RATIO;
        if (!_baselineDone)
        {
            _icing = now;
            _baselineDone = true;
            return null;
        }
        if (now == _icing) return null;
        _icing = now;
        return now ? "Icing conditions, ice accumulating" : "Icing conditions cleared";
    }

    public void Reset()
    {
        _icing = false;
        _baselineDone = false;
    }
}
```

- [ ] **Step 4: Verify GREEN** (filter `IceAccretionTrackerTests`, 6 tests), then full suite → 1154 green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(weather): ice accretion tracker (pure, A380-threshold hysteresis)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: SimConnect field + capability flag + icing wiring

**Files:**
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.cs` (struct field)
- Modify: `MSFSBlindAssist/SimConnect/SimConnectManager.Setup.cs` (registration)
- Modify: `MSFSBlindAssist/Aircraft/IAircraftDefinition.cs` + `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs` + `MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs` (capability flag)
- Modify: `MSFSBlindAssist/MainForm.cs` (tracker field), `MSFSBlindAssist/MainForm.Announcers.cs` (wiring), `MSFSBlindAssist/MainForm.AircraftSwitch.cs` (resets)

**Interfaces:**
- Consumes: `IceAccretionTracker` (Task 3), `UserSettings.AnnounceIcingEnabled` (Task 2).
- Produces: `AmbientWeatherData.StructuralIcePct` (double, ratio 0..1); `bool IAircraftDefinition.HasOwnIcingAnnouncer { get; }` with base default `false`, A380 override `true`.

- [ ] **Step 1: Struct field + registration**

In `MSFSBlindAssist/SimConnect/SimConnectManager.cs`, append to `AmbientWeatherData` after `public double WindSpeed;`:

```csharp
        public double StructuralIcePct; // STRUCTURAL ICE PCT, ratio 0..1 ("percent over 100")
```

In `MSFSBlindAssist/SimConnect/SimConnectManager.Setup.cs`, after the `"AMBIENT WIND VELOCITY"` AddToDataDefinition line and BEFORE `sc.RegisterDataDefineStruct<AmbientWeatherData>(...)`, add:

```csharp
sc.AddToDataDefinition(DATA_DEFINITIONS.WEATHER_DATA, "STRUCTURAL ICE PCT", "percent over 100",
    SIMCONNECT_DATATYPE.FLOAT64, 0.0f, (uint)7);
```

(Field order in the struct and datum index order MUST match — the struct is cast whole on receive. `"percent over 100"` is the SDK's ratio unit for this SimVar; the value arrives 0..1, matching the A380-derived thresholds.)

- [ ] **Step 2: Capability flag**

In `MSFSBlindAssist/Aircraft/IAircraftDefinition.cs`, before the interface's closing brace (after `TaxiTurnLeadSeconds`):

```csharp
    /// <summary>
    /// True when this aircraft announces its own ice accretion (e.g. the FBW A380's
    /// ice-stick announcer). MainForm's generic STRUCTURAL ICE PCT announcer is
    /// skipped entirely for such aircraft so an icing episode never speaks twice —
    /// the same one-condition-one-call-out rule as the PB-light/ECAM-memo invariant.
    /// </summary>
    bool HasOwnIcingAnnouncer { get; }
```

In `MSFSBlindAssist/Aircraft/BaseAircraftDefinition.cs`, after `public virtual double TaxiTurnLeadSeconds => 1.2;`:

```csharp
    public virtual bool HasOwnIcingAnnouncer => false;
```

In `MSFSBlindAssist/Aircraft/FlyByWireA380Definition.cs`, next to `public override double TaxiTurnLeadSeconds => 1.8;` (~line 50):

```csharp
    // The FBW ice-stick announcer (see the A32NX_ICING_STATE_ICING_STICK_INDICATOR
    // block) is this airframe's single icing voice; the generic announcer yields.
    public override bool HasOwnIcingAnnouncer => true;
```

- [ ] **Step 3: MainForm wiring**

3a. Field in `MSFSBlindAssist/MainForm.cs`, next to the `_prev*` ambient baseline fields (~line 295):

```csharp
    private readonly MSFSBlindAssist.Services.IceAccretionTracker _iceAccretionTracker = new();
```

3b. In `MSFSBlindAssist/MainForm.Announcers.cs`, in `AnnounceAmbientChanges`, directly AFTER the cloud entry/exit block (`_prevInCloud = inCloud;`), insert:

```csharp
        // Ice accretion (generic, sim-truth). Aircraft with their own tuned icing
        // announcer (HasOwnIcingAnnouncer, e.g. the FBW A380's ice stick) are skipped
        // entirely so one icing episode never speaks twice. NaN/negative clamp keeps
        // a bad sample from corrupting the tracker's hysteresis state.
        if (MSFSBlindAssist.Settings.SettingsManager.Current.AnnounceIcingEnabled
            && currentAircraft?.HasOwnIcingAnnouncer != true)
        {
            double ice = double.IsNaN(data.StructuralIcePct) || data.StructuralIcePct < 0
                ? 0 : data.StructuralIcePct;
            string? icing = _iceAccretionTracker.Observe(ice);
            if (icing != null)
                announcer.Announce(icing);
        }
```

3c. Resets in `MSFSBlindAssist/MainForm.AircraftSwitch.cs`:
- In the connect-time baseline block (the one starting `_prevPrecipState = -1;` and ending `weatherAnnouncementTimer?.Start();`), add alongside the other resets:

```csharp
        _iceAccretionTracker.Reset();
```

- In `SwitchAircraft`, extend the Task 2 insertion (after `activeSkyWeatherMonitor?.ResetTurbulenceTracker();`):

```csharp
        _iceAccretionTracker.Reset();
```

- [ ] **Step 4: Build solution + full suite**

`dotnet build MSFSBlindAssist.sln -c Debug` → 0 warnings (the interface addition compiles against every aircraft def via the base default). Full suite → 1154 green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(weather): generic ice-accretion announcements (STRUCTURAL ICE PCT)

New field on the ambient SimConnect struct (datum 7, ratio units) feeds
IceAccretionTracker on the existing 30s ambient tick, gated on
AnnounceIcingEnabled. Aircraft with their own tuned icing announcer
(HasOwnIcingAnnouncer — FBW A380 ice stick) are skipped entirely so an
icing episode never speaks twice. Resets on connect and aircraft switch.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Settings UI (two checkboxes) + tests

**Files:**
- Modify: `MSFSBlindAssist/Forms/Settings/WeatherPanel.cs`
- Test: `tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs` (extend)

**Interfaces:**
- Consumes: `UserSettings.AnnounceTurbulenceEnabled` / `AnnounceIcingEnabled` (Task 2).
- Produces: checkboxes with AccessibleNames `"Announce turbulence changes"` and `"Announce icing"`.

- [ ] **Step 1: Write the failing tests** (append inside the existing hermetic `WeatherPanelTests` class, matching its style)

```csharp
    [Fact]
    public void RoundTrip_PreservesHazardAnnouncementSettings()
    {
        var source = new UserSettings
        {
            AnnounceTurbulenceEnabled = false,
            AnnounceIcingEnabled = false,
        };

        using var panel = new WeatherPanel();
        panel.LoadFrom(source);
        var target = new UserSettings();
        panel.ApplyTo(target);

        Assert.False(target.AnnounceTurbulenceEnabled);
        Assert.False(target.AnnounceIcingEnabled);
    }

    [Fact]
    public void HazardDefaults_AreOn()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());
        var target = new UserSettings { AnnounceTurbulenceEnabled = false, AnnounceIcingEnabled = false };
        panel.ApplyTo(target);

        Assert.True(target.AnnounceTurbulenceEnabled);
        Assert.True(target.AnnounceIcingEnabled);
    }

    [Fact]
    public void Turbulence_checkbox_needs_master_and_activesky_icing_needs_master_only()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());            // both master switches off

        var turb = FindByAccessibleName(panel, "Announce turbulence changes");
        var icing = FindByAccessibleName(panel, "Announce icing");
        var master = (CheckBox)FindByAccessibleName(panel, "Auto-announce weather state changes");
        var asSwitch = (CheckBox)FindByAccessibleName(panel, "Enable ActiveSky integration");

        Assert.False(turb.Visible);
        Assert.False(icing.Visible);

        master.Checked = true;                          // master only
        Assert.False(turb.Visible);                     // turbulence still needs AS
        Assert.True(icing.Visible);

        asSwitch.Checked = true;                        // master + AS
        Assert.True(turb.Visible);
        Assert.True(icing.Visible);

        master.Checked = false;                         // master off hides both
        Assert.False(turb.Visible);
        Assert.False(icing.Visible);
    }

    [Fact]
    public void Hiding_hazard_checkboxes_never_resets_their_values()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());             // defaults: both true, both hidden

        var target = new UserSettings { AnnounceTurbulenceEnabled = false, AnnounceIcingEnabled = false };
        panel.ApplyTo(target);                          // hidden ≠ unchecked

        Assert.True(target.AnnounceTurbulenceEnabled);
        Assert.True(target.AnnounceIcingEnabled);
    }
```

(NOTE on `Visible` in tests: an unparented/never-shown WinForms control can report `Visible == false` regardless of the set value. The `WeatherPanel` used in the existing visibility tests works because the check reads the control's own `Visible` property set by `UpdateActiveSkyDependentVisibility` — follow whatever the EXISTING visibility test in this file does; if it asserts on the interval combo's `Visible` directly, this pattern is proven. If the new test fails for parenting reasons rather than logic, mirror the existing test's mechanism exactly and note it in the report.)

- [ ] **Step 2: Verify RED** — AccessibleNames not found / properties unbound (assertion failures or null control), for the expected reasons.

- [ ] **Step 3: Implement in `WeatherPanel.cs`**

3a. Fields, next to `_weatherAutoAnnounce`:

```csharp
    private CheckBox _announceTurbulence = null!;
    private CheckBox _announceIcing = null!;
```

3b. In `BuildAnnouncementsGroup()`:
- Grow the group: `Size = new System.Drawing.Size(460, 302)` (was 230; two new 24-px rows at 36-px pitch = +72).
- Insert after the `_weatherAutoAnnounce` construction:

```csharp
        _announceTurbulence = new CheckBox
        {
            Text = "Announce &turbulence changes",
            Location = new System.Drawing.Point(12, 60),
            Size = new System.Drawing.Size(420, 24),
            AccessibleName = "Announce turbulence changes",
            AccessibleDescription = "Announce entering, worsening, easing and smooth-air turbulence "
                + "transitions from ActiveSky. Applies only while auto-announce weather is enabled; "
                + "requires ActiveSky."
        };

        _announceIcing = new CheckBox
        {
            Text = "Announce &icing",
            Location = new System.Drawing.Point(12, 96),
            Size = new System.Drawing.Size(420, 24),
            AccessibleName = "Announce icing",
            AccessibleDescription = "Announce when ice starts accumulating on the airframe and when "
                + "it clears. Applies only while auto-announce weather is enabled."
        };
```

- Shift every control below by +72: `_sigmetAlerts` y 60→132, `_pirepAlerts` y 96→168, `rangeLabel` y 138→210, `_proximityRange` y 134→206, `_weatherIntervalLabel` y 178→250, `_weatherIntervalCombo` y 202→274.
- Add `_announceTurbulence, _announceIcing,` to `group.Controls.AddRange` right after `_weatherAutoAnnounce`.

3c. Extend `UpdateActiveSkyDependentVisibility()` (keep the existing `on` computation and interval lines untouched) by appending:

```csharp
        // Hazard announcers: both ride the master auto-announce; turbulence is
        // AS-sourced so it additionally needs the AS switch. Hiding never resets
        // the stored value (ApplyTo reads the checkboxes regardless).
        bool master = _weatherAutoAnnounce.Checked;
        _announceTurbulence.Visible = master && _activeSkyEnabled.Checked;
        _announceIcing.Visible = master;
```

3d. `LoadFrom` additions (with the other checkbox loads):

```csharp
        _announceTurbulence.Checked = settings.AnnounceTurbulenceEnabled;
        _announceIcing.Checked = settings.AnnounceIcingEnabled;
```

`ApplyTo` additions:

```csharp
        settings.AnnounceTurbulenceEnabled = _announceTurbulence.Checked;
        settings.AnnounceIcingEnabled = _announceIcing.Checked;
```

- [ ] **Step 4: Verify GREEN** (filter `WeatherPanelTests`), then full suite → 1158 green, solution build 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(settings): toggles for turbulence and icing announcements

Two default-on checkboxes under the master auto-announce switch;
turbulence hides without ActiveSky (it cannot work without it), icing
follows the master only. Hiding never resets stored values.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Documentation

**Files:**
- Modify: `docs/weather.md`
- Modify: `CLAUDE.md`

- [ ] **Step 1: `docs/weather.md`** — add a section "Hazard announcements: turbulence and icing (2026-07)" documenting, with file/method citations and matching the doc's voice: the turbulence tracker (data source `Conditions.AmbientTurbulence` on the monitor tick; category boundaries shared verbatim with `CategorizeTurbulence`; rising-at-boundary / easing-5-below hysteresis with the four concrete easing thresholds 20/45/70/85; all four utterance forms; baseline-first, gap survival, aircraft-switch reset; words-only, smooth never named); the icing tracker (STRUCTURAL ICE PCT datum 7 on the ambient struct, 0..1 ratio; 0.05/0.02 thresholds copied from the A380's sim-verified constants; first-sample silence; resets on connect and switch); the `HasOwnIcingAnnouncer` yield (A380's ice-stick announcer stays the single voice, and it is deliberately NOT gated on `AnnounceIcingEnabled`); the two settings and their visibility rules.

- [ ] **Step 2: `CLAUDE.md`** — one bullet in the weather invariants block:

```markdown
- Hazard announcements: turbulence is WORDS-ONLY (raw 1-100 never spoken; ≤25 "smooth" never named as a category) with rising-at-boundary/easing-5-below hysteresis — don't "simplify" the tuned thresholds; the generic STRUCTURAL ICE PCT announcer must SKIP aircraft with `HasOwnIcingAnnouncer` (A380 ice stick) so one icing episode never speaks twice; both trackers are baseline-first (silent first read, reset on aircraft switch). → [weather.md](docs/weather.md)
```

- [ ] **Step 3: Full build + suite once (docs-only sanity), commit**

```bash
git add -A
git commit -m "docs(weather): document the hazard announcements

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## In-sim test plan (goes in the PR; Robin runs it)

1. **Turbulence (AS on, auto-announce on):** fly into an AS turbulence area → "Entering light/moderate… turbulence" within ~60 s of the radar form's Turbulence line showing the same category; category words match the radar form exactly; leaving the area → "Turbulence easing to …" then "Smooth air". No raw numbers ever.
2. **Icing (any aircraft except the A380):** fly into a cold cloud until ice forms → exactly one "Icing conditions, ice accumulating"; after leaving/de-icing → exactly one "Icing conditions cleared".
3. **A380 yield:** same scenario in the FBW A380 → exactly ONE announcement per edge, in the A380's own wording ("Icing conditions") — never two voices.
4. **Toggles:** turn "Announce turbulence changes" / "Announce icing" off in settings mid-flight → next transitions silent; back on → next genuine transition announces (a transition that happened while off announces once on the next tick — baseline survival).
5. **Settings visibility:** with auto-announce off both checkboxes are absent from the tab order; with auto-announce on but AS off, only "Announce icing" appears.
6. **Master off:** with auto-announce weather off entirely, no hazard announcements regardless of the sub-toggles.

## Self-review checklist

- Spec coverage: §2.1 → Tasks 1-2; §2.2/§2.3/§2.4 → Tasks 3-4; §3 → Tasks 2 (settings) + 5 (UI); §5 test matrix → Tasks 1/3/5; §6 → Task 6. Both settings created in Task 2 (one commit) so Task 4 never dangles.
- Type consistency: `TurbulenceCategoryTracker`/`IceAccretionTracker` in `MSFSBlindAssist.Services`; `HasOwnIcingAnnouncer` on interface + base + A380; struct field name `StructuralIcePct` used identically in registration comment and wiring.
- No placeholders; every code step carries complete code.

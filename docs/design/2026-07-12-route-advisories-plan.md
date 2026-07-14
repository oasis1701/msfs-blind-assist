# En-Route Advisories — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route-scoped SIGMET/AIRMET support via ActiveSky's parameterless `GetActiveSigmetsAt` (AS's SimBrief link provides the route — no plan push, no file reads), per the approved spec `docs/design/2026-07-12-route-advisories-design.md`.

**Architecture:** One new `ActiveSkyClient` wrapper (the eighth, standard shape); a defensive pure parser + text builder + baseline-first `RouteAdvisoryTracker` (all CI-tested); a "Route Advisories (ActiveSky)" `DisplayListBox` in the Weather Radar form riding the 30 s refresh (with `AutoScroll` + height rework); an announce check riding the existing 30 s weather-announcement tick behind a new independent sub-toggle.

**Tech Stack:** .NET 10 / C# 13, WinForms, xUnit (x64).

## Global Constraints

- Build ONLY via `dotnet build MSFSBlindAssist.sln -c Debug`; 0 warnings. Full suite green after every task (baseline: **1181**).
- API only — no `.pln` export, no `LoadFlightPlan`, no reads under `%APPDATA%\HiFi`.
- The ONLY new announcement is `"New advisory on route: {key}."` via queued `announcer.Announce`; baseline-first (first successful read silent).
- New `UserSettings` property in BOTH edit sites (property block + `Clone()`).
- TDD: failing test first (compile error counts as RED).
- Commit after every task, ending: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`

**Key verified facts (2026-07-12):**
- Live endpoint: `GET {BaseUrl}/GetActiveSigmetsAt` (no query) → no-hit text `"No airmet/sigmet affecting currently loaded flight plan route"`; hit format (from the positional variant) `"CONVECTIVE SIGMET 18E / Valid until: 2000z / AREA TS MOV FROM 26015KT. TOPS TO FL420..."` — route-variant hit format assumed similar, parser is defensive by spec.
- `ActiveSkyClient.cs`: seven wrappers share the shape gate→port→CTS(5s)→GET→null-on-fail (e.g. `GetRouteAdvisoriesTextAsync` mirrors `GetPositionMetarAsync` exactly but for the endpoint and trim).
- `MainForm.cs:347-355`: `weatherActiveSky` (MainForm's own `ActiveSkyClient`), `_announcedSigmetKeys`/`_announcedPirepKeys` HashSets, `_sigmetKeysClearedAt`, and the `_proximityCheckRunning` latch pattern. Clearing cadence: **15 minutes** (`MainForm.Announcers.cs:2234-2240` — "Clear stale announced keys every 15 minutes"; reminder semantics: still-active advisories re-announce after a clear).
- `WeatherAnnouncementTimer_Tick` (MainForm.Announcers.cs ~2076-2085): reads `settings`, guards `IsConnected`, dispatches ambient + proximity checks. 30 s timer.
- Connect-time reset block: `MainForm.AircraftSwitch.cs:197-208` (ends `_sigmetKeysClearedAt = DateTime.UtcNow; weatherAnnouncementTimer?.Start();`). Aircraft-switch hazard resets: `MainForm.AircraftSwitch.cs` ~605-608 (`activeSkyWeatherMonitor?.ResetTurbulenceTracker(); _iceAccretionTracker.Reset();`).
- `WeatherRadarForm.cs` (post-ListBox-conversion): y-cursor layout; box heights currently station 110 / profile 140 / advisories 160 / winds 140; form `Size(600, 988)`; tab order `_asModeBox 0, _currentWeatherBox 1, _stationBox 2, _profileBox 3, _advisoriesBox 4, _windsAloftBox 5, _decodeCheckBox 6, _refreshButton 7, _closeButton 8`; visibility block in `RefreshAsync` toggles the AS-only sections on `asEnabled`; fetches run in a `Task.WhenAll` batch; 30 s `_autoRefreshTimer` with IsDisposed guards.
- `WeatherPanel.cs` announcements group (post-hazard-toggles): `_weatherAutoAnnounce` y=24, `_announceTurbulence` 60, `_announceIcing` 96, `_sigmetAlerts` 132, `_pirepAlerts` 168, `rangeLabel` 210, `_proximityRange` 206(x=270), `_weatherIntervalLabel` 250, `_weatherIntervalCombo` 274; group `Size(460, 302)`; `UpdateActiveSkyDependentVisibility` owns conditional visibility; panel tests are hermetic (`WeatherPanelTests`, `FindByAccessibleName`).

---

### Task 1: Pure layer — wrapper, parser, builder, tracker + tests

**Files:**
- Modify: `MSFSBlindAssist/Services/ActiveSkyClient.cs` (wrapper)
- Modify: `MSFSBlindAssist/Services/ActiveSkyFormatting.cs` (RouteAdvisory + parser + builder)
- Create: `MSFSBlindAssist/Services/RouteAdvisoryTracker.cs`
- Test: `tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs`

**Interfaces:**
- Produces: `public Task<string?> ActiveSkyClient.GetRouteAdvisoriesTextAsync()`; `internal sealed class ActiveSkyFormatting.RouteAdvisory { public string Key; public List<string> Lines; }` (nested in ActiveSkyFormatting); `internal static List<RouteAdvisory> ParseRouteAdvisories(string raw)`; `internal static string BuildRouteAdvisoriesText(IReadOnlyList<RouteAdvisory> advisories)`; `internal sealed class RouteAdvisoryTracker` with `IReadOnlyList<string> Observe(IReadOnlyList<string> keys)`, `void ClearAnnouncedKeys()`, `void Reset()`.

- [ ] **Step 1: Write the failing tests**

Create `tests/MSFSBlindAssist.Tests/RouteAdvisoriesTests.cs`:

```csharp
// Route-advisory parsing + announce-decision rules (spec 2026-07-12): the parser is
// DELIBERATELY defensive — the route variant's hit format is only partially known, so
// unknown text renders verbatim as its own block rather than being dropped. The
// tracker is baseline-first; ClearAnnouncedKeys() keeps the baseline (15-minute
// reminder semantics, matching _announcedSigmetKeys); Reset() re-baselines fully.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteAdvisoriesTests
{
    private const string NoHit = "No airmet/sigmet affecting currently loaded flight plan route";
    private const string OneBlock =
        "CONVECTIVE SIGMET 18E\r\nValid until: 2000z\r\nAREA TS MOV FROM 26015KT. TOPS TO FL420.";
    private const string TwoBlocks =
        "CONVECTIVE SIGMET 18E\r\nValid until: 2000z\r\n\r\nSIGMET B4\r\nSEV TURB FL250-FL380";

    // --- ParseRouteAdvisories -----------------------------------------------------------

    [Theory]
    [InlineData(NoHit)]
    [InlineData("no airmet/sigmet affecting currently loaded flight plan route")]  // case-insensitive
    [InlineData("  No airmet/sigmet affecting anything at all  ")]                 // prefix match
    public void No_hit_sentence_parses_to_empty(string raw)
        => Assert.Empty(ActiveSkyFormatting.ParseRouteAdvisories(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_parses_to_empty(string raw)
        => Assert.Empty(ActiveSkyFormatting.ParseRouteAdvisories(raw));

    [Fact]
    public void Single_block_keeps_lines_and_keys_on_first_line()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(OneBlock);
        var a = Assert.Single(advisories);
        Assert.Equal("CONVECTIVE SIGMET 18E", a.Key);
        Assert.Equal(3, a.Lines.Count);
        Assert.Equal("AREA TS MOV FROM 26015KT. TOPS TO FL420.", a.Lines[2]);
    }

    [Fact]
    public void Blank_lines_split_blocks()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(TwoBlocks);
        Assert.Equal(2, advisories.Count);
        Assert.Equal("CONVECTIVE SIGMET 18E", advisories[0].Key);
        Assert.Equal("SIGMET B4", advisories[1].Key);
    }

    [Fact]
    public void Unknown_free_text_renders_verbatim_as_one_block()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories("No flight plan is currently loaded");
        var a = Assert.Single(advisories);
        Assert.Equal("No flight plan is currently loaded", a.Key);
    }

    [Fact]
    public void Lf_only_newlines_parse_identically()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "SIGMET B4\nSEV TURB FL250-FL380\n\nAIRMET T1\nMOD TURB");
        Assert.Equal(2, advisories.Count);
        Assert.Equal("AIRMET T1", advisories[1].Key);
    }

    // --- BuildRouteAdvisoriesText ---------------------------------------------------------

    [Fact]
    public void Empty_list_reads_no_advisories()
        => Assert.Equal("No advisories on route.",
            ActiveSkyFormatting.BuildRouteAdvisoriesText(
                ActiveSkyFormatting.ParseRouteAdvisories(NoHit)));

    [Fact]
    public void Blocks_render_with_blank_separator_rows()
    {
        string text = ActiveSkyFormatting.BuildRouteAdvisoriesText(
            ActiveSkyFormatting.ParseRouteAdvisories(TwoBlocks));
        string[] rows = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Assert.Equal(new[]
        {
            "CONVECTIVE SIGMET 18E",
            "Valid until: 2000z",
            "",
            "SIGMET B4",
            "SEV TURB FL250-FL380",
        }, rows);
    }

    // --- RouteAdvisoryTracker ---------------------------------------------------------------

    [Fact]
    public void First_read_baselines_silently_even_with_advisories_present()
    {
        var t = new RouteAdvisoryTracker();
        Assert.Empty(t.Observe(new[] { "SIGMET B4" }));
        Assert.Empty(t.Observe(new[] { "SIGMET B4" }));          // unchanged: silent
    }

    [Fact]
    public void New_key_is_reported_once()
    {
        var t = new RouteAdvisoryTracker();
        t.Observe(new string[0]);                                 // baseline: clear route
        Assert.Equal(new[] { "SIGMET B4" }, t.Observe(new[] { "SIGMET B4" }));
        Assert.Empty(t.Observe(new[] { "SIGMET B4" }));
    }

    [Fact]
    public void Keys_persist_across_gaps()
    {
        // The caller never Observes on failed fetches — keys simply persist, so a
        // reconnect with the same advisories stays silent.
        var t = new RouteAdvisoryTracker();
        t.Observe(new string[0]);
        t.Observe(new[] { "SIGMET B4" });                         // announced
        Assert.Empty(t.Observe(new[] { "SIGMET B4" }));           // after a gap: silent
    }

    [Fact]
    public void ClearAnnouncedKeys_keeps_baseline_and_reannounces_active_advisories()
    {
        // 15-minute reminder semantics, matching _announcedSigmetKeys: after the
        // periodic clear, a still-active advisory announces again.
        var t = new RouteAdvisoryTracker();
        t.Observe(new string[0]);
        t.Observe(new[] { "SIGMET B4" });
        t.ClearAnnouncedKeys();
        Assert.Equal(new[] { "SIGMET B4" }, t.Observe(new[] { "SIGMET B4" }));
    }

    [Fact]
    public void Reset_rebaselines_silently()
    {
        var t = new RouteAdvisoryTracker();
        t.Observe(new string[0]);
        t.Observe(new[] { "SIGMET B4" });
        t.Reset();
        Assert.Empty(t.Observe(new[] { "SIGMET B4", "AIRMET T1" }));   // first read after reset
        Assert.Equal(new[] { "SIGMET Z9" }, t.Observe(new[] { "SIGMET B4", "SIGMET Z9" }));
    }
}
```

- [ ] **Step 2: Verify RED** — `dotnet build tests/... -p:Platform=x64` fails with missing `ParseRouteAdvisories`/`RouteAdvisoryTracker` members only.

- [ ] **Step 3: Implement the client wrapper** (in `ActiveSkyClient.cs`, after `GetWeatherInfoXmlAsync`):

```csharp
    /// <summary>
    /// /GetActiveSigmetsAt with NO parameters — SIGMETs/AIRMETs affecting the flight
    /// plan currently loaded in ActiveSky (AS's own SimBrief link keeps that current;
    /// MSFSBA never pushes a plan). Returns the raw response text — either advisory
    /// blocks or the "No airmet/sigmet affecting currently loaded flight plan route"
    /// sentence — or null on error / AS off / unreachable.
    /// </summary>
    public async Task<string?> GetRouteAdvisoriesTextAsync()
    {
        if (!Settings.SettingsManager.Current.ActiveSkyEnabled) return null;   // master switch — no AS I/O when off
        if (LastSuccessfulPort is not int port) return null;
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using var resp = await _http.GetAsync($"{BaseUrl(port)}/GetActiveSigmetsAt", cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            string body = (await resp.Content.ReadAsStringAsync(cts.Token)).Trim();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            Log.Debug("ActiveSky", "GetRouteAdvisories failed (timeout or connection error)");
            return null;
        }
    }
```

- [ ] **Step 4: Implement parser + builder** (append to `ActiveSkyFormatting`):

```csharp
    /// <summary>One advisory block from /GetActiveSigmetsAt. Key = first trimmed line
    /// (dedup identity for the announce tracker).</summary>
    internal sealed class RouteAdvisory
    {
        public string Key = "";
        public List<string> Lines = new();
    }

    /// <summary>
    /// Parses the route-advisories response. DELIBERATELY defensive (spec 2026-07-12
    /// §2.2): the route variant's hit format is only partially known, so blocks split
    /// on blank lines and ANY unrecognized text renders verbatim as its own block —
    /// never dropped, never thrown. The known no-hit sentence (any response starting
    /// "No airmet/sigmet", case-insensitive) parses to an empty list.
    /// </summary>
    internal static List<RouteAdvisory> ParseRouteAdvisories(string raw)
    {
        var result = new List<RouteAdvisory>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        string trimmed = raw.Trim();
        if (trimmed.StartsWith("No airmet/sigmet", StringComparison.OrdinalIgnoreCase))
            return result;

        var blocks = trimmed.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                             .Select(l => l.TrimEnd())
                             .Where(l => l.Length > 0)
                             .ToList();
            if (lines.Count == 0) continue;
            result.Add(new RouteAdvisory { Key = lines[0].Trim(), Lines = lines });
        }
        return result;
    }

    /// <summary>Radar-box text: blocks separated by one blank row; empty list reads
    /// "No advisories on route."</summary>
    internal static string BuildRouteAdvisoriesText(IReadOnlyList<RouteAdvisory> advisories)
    {
        if (advisories.Count == 0) return "No advisories on route.";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < advisories.Count; i++)
        {
            if (i > 0) sb.AppendLine();
            foreach (var line in advisories[i].Lines)
                sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd();
    }
```

- [ ] **Step 5: Implement the tracker** — create `MSFSBlindAssist/Services/RouteAdvisoryTracker.cs`:

```csharp
namespace MSFSBlindAssist.Services;

/// <summary>
/// Pure announce-decision logic for route advisories (spec 2026-07-12 §4).
/// Baseline-first: the first successful Observe seeds silently (preflight discovery
/// belongs to the Weather Radar box, not a startup announcement burst). Afterwards,
/// Observe returns only keys never seen before. ClearAnnouncedKeys() drops the seen
/// set but KEEPS the baseline latch — the 15-minute reminder semantics shared with
/// MainForm's _announcedSigmetKeys, so a still-active advisory re-announces after
/// the periodic clear. Reset() (connect / aircraft switch) re-baselines fully.
/// </summary>
internal sealed class RouteAdvisoryTracker
{
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private bool _baselineDone;

    public IReadOnlyList<string> Observe(IReadOnlyList<string> keys)
    {
        var fresh = new List<string>();
        if (!_baselineDone)
        {
            foreach (var k in keys) _seen.Add(k);
            _baselineDone = true;
            return fresh;
        }
        foreach (var k in keys)
            if (_seen.Add(k)) fresh.Add(k);
        return fresh;
    }

    public void ClearAnnouncedKeys() => _seen.Clear();

    public void Reset()
    {
        _seen.Clear();
        _baselineDone = false;
    }
}
```

- [ ] **Step 6: Verify GREEN** (filter `RouteAdvisoriesTests`, 16 test cases), then full suite (expect 1197) + solution build 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(weather): route-advisories pure layer (AS GetActiveSigmetsAt)

Eighth ActiveSkyClient wrapper (parameterless — AS's SimBrief link owns
the route; no plan push, no file reads), a deliberately-defensive block
parser, the radar-box text builder, and the baseline-first
RouteAdvisoryTracker with 15-minute reminder-clear semantics.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Weather Radar section + height rework + AutoScroll

**Files:**
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs`

**Interfaces:**
- Consumes: Task 1's wrapper/parser/builder; the y-cursor layout; the 30 s refresh batch.
- Produces: `_routeAdvisoriesLabel`/`_routeAdvisoriesBox` (AccessibleName "Route Advisories"); `FetchRouteAdvisoriesAsync()`.

- [ ] **Step 1: Fields** (with the other label/box pairs):

```csharp
    private Label _routeAdvisoriesLabel = null!;
    private DisplayListBox _routeAdvisoriesBox = null!;
```

- [ ] **Step 2: Layout.** In `InitializeComponent`, FIRST apply the height trims (reclaiming room per spec §3): `_stationBox` height 110→100 (`y += 100 + 12;`), `_profileBox` 140→120 (`y += 120 + 12;`), `_advisoriesBox` 160→120 (`y += 120 + 12;`), `_windsAloftBox` 140→120 (`y += 120 + 10;`). THEN insert the new section between the profile block and the advisories block:

```csharp
        // ── Route advisories (ActiveSky only; hidden when AS is off) ──────
        _routeAdvisoriesLabel = new Label
        {
            Text = "Route Advisories (ActiveSky):",
            Location = new Point(12, y),
            Size = new Size(570, 20),
            AccessibleName = "Route Advisories label",
            Visible = false
        };
        y += 24;

        _routeAdvisoriesBox = new DisplayListBox
        {
            Location = new Point(12, y),
            Size = new Size(566, 100),
            Font = new Font("Consolas", 9),
            AccessibleName = "Route Advisories",
            AccessibleDescription = "SIGMETs and AIRMETs affecting the flight plan loaded in ActiveSky",
            Visible = false
        };
        y += 100 + 12;
```

Add `_routeAdvisoriesLabel, _routeAdvisoriesBox,` to `Controls.AddRange` between the profile pair and the advisories pair. Update the form shell: `Size = new Size(600, 1034)` (988 − 90 trims + 136 new section) and add `AutoScroll = true;` next to it.

- [ ] **Step 3: Small-screen clamp.** In `SetupAccessibility`'s `Load` handler, BEFORE the initial refresh:

```csharp
            // Fixed 1034 px exceeds small/scaled working areas; with AutoScroll on,
            // clamping the window height turns clipped content into a scrollbar
            // (design 2026-07-12 §3 — blind users are unaffected either way).
            var workingArea = Screen.FromControl(this).WorkingArea;
            if (Height > workingArea.Height)
                Height = workingArea.Height;
```

- [ ] **Step 4: Tab order** — final block: `_asModeBox 0, _currentWeatherBox 1, _stationBox 2, _profileBox 3, _routeAdvisoriesBox 4, _advisoriesBox 5, _windsAloftBox 6, _decodeCheckBox 7, _refreshButton 8, _closeButton 9`.

- [ ] **Step 5: Fetch + wiring.** Add:

```csharp
    private async Task<string> FetchRouteAdvisoriesAsync()
    {
        if (_activeSkyAvailable != true) return "unavailable";
        string? raw = await _activeSky.GetRouteAdvisoriesTextAsync();
        if (raw == null) return "unavailable";
        return MSFSBlindAssist.Services.ActiveSkyFormatting.BuildRouteAdvisoriesText(
            MSFSBlindAssist.Services.ActiveSkyFormatting.ParseRouteAdvisories(raw));
    }
```

In `RefreshAsync`: add `var routeAdvisoriesTask = FetchRouteAdvisoriesAsync();` to the parallel batch, include it in the `Task.WhenAll`, assign `_routeAdvisoriesBox.SetText(await routeAdvisoriesTask);`, and extend the `asEnabled` visibility block with `_routeAdvisoriesLabel.Visible = asEnabled; _routeAdvisoriesBox.Visible = asEnabled;`.

- [ ] **Step 6: Build + full suite** (1197 green, 0 warnings). **Step 7: Commit**

```bash
git add -A
git commit -m "feat(weather): Route Advisories section in the Weather Radar window

AS-only DisplayListBox riding the 30s live refresh, fed by the
parameterless GetActiveSigmetsAt. Box heights trimmed and AutoScroll +
a working-area height clamp added so the taller form scrolls on small
screens instead of clipping (resolves the standing screen-fit concern).

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Announce path + setting + panel + tests

**Files:**
- Modify: `MSFSBlindAssist/Settings/UserSettings.cs`, `MSFSBlindAssist/MainForm.cs`, `MSFSBlindAssist/MainForm.Announcers.cs`, `MSFSBlindAssist/MainForm.AircraftSwitch.cs`, `MSFSBlindAssist/Forms/Settings/WeatherPanel.cs`
- Test: `tests/MSFSBlindAssist.Tests/WeatherPanelTests.cs` (extend)

**Interfaces:**
- Consumes: Task 1's tracker/parser; `weatherActiveSky`; the 30 s `WeatherAnnouncementTimer_Tick`.
- Produces: `UserSettings.AnnounceRouteAdvisoriesEnabled` (default true); panel checkbox AccessibleName `"Auto-announce new route advisories"`.

- [ ] **Step 1: Failing panel tests** (append to the hermetic `WeatherPanelTests`, matching its style):

```csharp
    [Fact]
    public void RouteAdvisories_setting_roundtrips_and_defaults_on()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { AnnounceRouteAdvisoriesEnabled = false });
        var target = new UserSettings();
        panel.ApplyTo(target);
        Assert.False(target.AnnounceRouteAdvisoriesEnabled);

        panel.LoadFrom(new UserSettings());
        panel.ApplyTo(target);
        Assert.True(target.AnnounceRouteAdvisoriesEnabled);      // default on
    }

    [Fact]
    public void RouteAdvisories_checkbox_needs_activesky_only()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());                       // AS off, master off

        var route = FindByAccessibleName(panel, "Auto-announce new route advisories");
        var asSwitch = (CheckBox)FindByAccessibleName(panel, "Enable ActiveSky integration");
        var master = (CheckBox)FindByAccessibleName(panel, "Auto-announce weather state changes");

        Assert.False(route.Visible);
        asSwitch.Checked = true;                                  // AS alone suffices
        Assert.True(route.Visible);
        master.Checked = true;                                    // master irrelevant
        Assert.True(route.Visible);
        asSwitch.Checked = false;
        Assert.False(route.Visible);
    }
```

- [ ] **Step 2: Verify RED**, then implement the setting (BOTH sites in `UserSettings.cs`):

```csharp
        /// <summary>
        /// Announce a NEW SIGMET/AIRMET appearing on the flight-plan route loaded in
        /// ActiveSky (parameterless GetActiveSigmetsAt; AS's SimBrief link keeps the
        /// route current). Independent of WeatherAutoAnnounceEnabled — a sibling of
        /// the SIGMET/PIREP proximity alerts — but requires ActiveSkyEnabled.
        /// Default on.
        /// </summary>
        public bool AnnounceRouteAdvisoriesEnabled { get; set; } = true;
```

plus `AnnounceRouteAdvisoriesEnabled = AnnounceRouteAdvisoriesEnabled,` in `Clone()`.

- [ ] **Step 3: Panel checkbox.** Field `private CheckBox _routeAdvisoryAlerts = null!;`. In `BuildAnnouncementsGroup()`: insert after `_pirepAlerts` at `(12, 204)` size `(420, 24)`, `Text = "Auto-announce new r&oute advisories (ActiveSky)"`, `AccessibleName = "Auto-announce new route advisories"`, `AccessibleDescription = "Announce when a new SIGMET or AIRMET appears on the flight-plan route loaded in ActiveSky. Requires ActiveSky."`; shift `rangeLabel` 210→246, `_proximityRange` 206→242, `_weatherIntervalLabel` 250→286, `_weatherIntervalCombo` 274→310; group `Size` 302→338; add to `Controls.AddRange` after `_pirepAlerts`. In `UpdateActiveSkyDependentVisibility()` append `_routeAdvisoryAlerts.Visible = _activeSkyEnabled.Checked;` (deliberately NOT gated on the master — proximity-sibling semantics). `LoadFrom`/`ApplyTo` bind it.

- [ ] **Step 4: Announce wiring.** In `MainForm.cs`, next to `_announcedSigmetKeys`:

```csharp
    private readonly MSFSBlindAssist.Services.RouteAdvisoryTracker _routeAdvisoryTracker = new();
    private DateTime _routeAdvisoryKeysClearedAt = DateTime.MinValue;
    private bool _routeAdvisoryCheckRunning;
```

In `WeatherAnnouncementTimer_Tick`, after the proximity dispatch:

```csharp
        if (settings.AnnounceRouteAdvisoriesEnabled && !_routeAdvisoryCheckRunning)
            _ = CheckRouteAdvisoriesAsync();
```

New method in `MainForm.Announcers.cs` (near `CheckWeatherProximityAsync`):

```csharp
    private async Task CheckRouteAdvisoriesAsync()
    {
        _routeAdvisoryCheckRunning = true;
        try
        {
            // Central AS gate: instant false when the switch is off — this check
            // costs nothing for non-AS users despite riding every 30 s tick.
            if (!await weatherActiveSky.IsRunningAsync()) return;

            string? raw = await weatherActiveSky.GetRouteAdvisoriesTextAsync();
            if (raw == null) return;                             // failed fetch: tracker untouched

            // Same 15-minute reminder cadence as _announcedSigmetKeys: a still-active
            // route advisory re-announces after the clear.
            if ((DateTime.UtcNow - _routeAdvisoryKeysClearedAt).TotalMinutes > 15)
            {
                _routeAdvisoryTracker.ClearAnnouncedKeys();
                _routeAdvisoryKeysClearedAt = DateTime.UtcNow;
            }

            var advisories = MSFSBlindAssist.Services.ActiveSkyFormatting.ParseRouteAdvisories(raw);
            var newKeys = _routeAdvisoryTracker.Observe(advisories.Select(a => a.Key).ToList());

            if (!IsHandleCreated || IsDisposed) return;
            foreach (string key in newKeys)
            {
                Log.Debug("MainForm", $"route advisory: \"{key}\"");
                announcer.Announce($"New advisory on route: {key}.");
            }
        }
        catch (Exception ex)
        {
            Log.Debug("MainForm", $"Route advisory check error: {ex.Message}");
        }
        finally
        {
            _routeAdvisoryCheckRunning = false;
        }
    }
```

Resets: in the connect-time baseline block (`MainForm.AircraftSwitch.cs`, alongside `_sigmetKeysClearedAt = DateTime.UtcNow;`) add `_routeAdvisoryTracker.Reset(); _routeAdvisoryKeysClearedAt = DateTime.UtcNow;`. In `SwitchAircraft`'s hazard resets (after `_iceAccretionTracker.Reset();`) add `_routeAdvisoryTracker.Reset();`.

- [ ] **Step 5: Verify GREEN** (panel tests filter), full suite (expect 1199), solution build 0 warnings. **Step 6: Commit**

```bash
git add -A
git commit -m "feat(weather): announce new route advisories (baseline-first)

Rides the 30s weather-announcement tick behind a new independent
sub-toggle (default on, hidden without ActiveSky). First successful
read baselines silently; 15-minute reminder-clear matches the
proximity-alert keys; resets on connect and aircraft switch.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Documentation

**Files:** Modify `docs/weather.md`.

- [ ] **Step 1:** New subsection under the read-only surfaces: route source is the plan loaded in ActiveSky (its SimBrief link keeps it current — live-verified 2026-07-12; MSFSBA pushes nothing and reads no files, per Robin's API-only constraint); the defensive parser rationale (route-variant hit format partially unknown → verbatim fallback, first line = key); the radar box behavior (no-hit sentence → "No advisories on route.", unavailable, hidden without AS); the announce lifecycle (baseline-first, 15-minute reminder clear, resets, independent-of-master sub-toggle); the honest verification caveat (hit path first truly verified when real weather crosses a planned route).

- [ ] **Step 2:** Build + suite once, commit:

```bash
git add -A
git commit -m "docs(weather): document the en-route advisories feature

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## In-sim test items (append to the PR checklist)

15. With a SimBrief plan loaded in AS: Shift+R shows "Route Advisories (ActiveSky)" — "No advisories on route." on a clear route; box hidden entirely with the AS switch off; "unavailable" with AS enabled but closed.
16. Settings: "Auto-announce new route advisories (ActiveSky)" visible whenever AS is enabled (independent of the auto-announce master), round-trips, default on.
17. HIT-PATH CAVEAT (recorded, not fully verifiable on demand): when a real SIGMET intersects a planned route mid-flight, expect exactly one "New advisory on route: …" per advisory, no announcement at startup for advisories already present (baseline-first), and a reminder re-announce no sooner than ~15 minutes.
18. Small-screen check: on a display shorter than the form, the radar window clamps to the working area and scrolls instead of clipping the buttons.

## Self-review checklist

- Spec coverage: §2 → Task 1; §3 → Task 2; §4/§5 → Task 3; §8 → Task 4; §7 test matrix → Tasks 1/3.
- Type consistency: `RouteAdvisory` nested in `ActiveSkyFormatting` (referenced as `ActiveSkyFormatting.RouteAdvisory` — tests use the outer-class statics only, so no qualification issues); `RouteAdvisoryTracker` top-level in `Services` like the other trackers; tab-order and y-cursor arithmetic double-checked (988 − 90 + 136 = 1034).
- Suite arithmetic: 1181 + 16 (Task 1) + 2 (Task 3) = 1199.
- No placeholders; every code step carries complete code.

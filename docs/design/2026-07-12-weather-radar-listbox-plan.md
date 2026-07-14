# Weather Radar ListBox Conversion + Live Refresh — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the Weather Radar window's five multi-line readouts to `DisplayListBox` rows (cursor-stable, row-per-fact NVDA navigation) and add a 30-second live refresh, per the approved spec `docs/design/2026-07-12-weather-radar-listbox-design.md`.

**Architecture:** Pure sink-swap + one timer. The five `TextBox` fields become `Forms.DisplayListBox` (the shared control from the 2026-07-02 consistency pass; reconciles via `DisplayList.UpdateInPlace`); every `.Text =` write becomes `.SetText(...)`; a WinForms timer mirrors `FbwEwdWindow`'s lifecycle driving the existing `RefreshAsync(forceRefresh: false)`. No text-builder, announcement, or layout changes.

**Tech Stack:** .NET 10 / C# 13, WinForms, xUnit (x64).

## Global Constraints

- Build ONLY via `dotnet build MSFSBlindAssist.sln -c Debug`; 0 warnings. Full suite green after every task (baseline on this branch: **1181**; no new tests expected — the reconcile contract is already pinned by `DisplayListUpdateInPlaceTests`).
- All text-building output stays byte-identical — only the sink control changes. No new announcements; the timer must be inaudible except through changed row content.
- The mode box `_asModeBox` (single-line TextBox) is NOT converted (consistency-pass carve-out).
- Commit after every task, message ending with: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`

**Key verified facts (2026-07-12):**
- `Forms.DisplayListBox` (`MSFSBlindAssist/Forms/DisplayListBox.cs`): subclasses ListBox; ctor sets `SelectionMode.One`, `IntegralHeight = false`, `HorizontalScrollbar = true`, `TabStop = true`, `Font = Consolas 10` (caller-overridable). API: `SetLines(IReadOnlyList<string>)`, `SetText(string)` (splits `\r\n`/`\n`, `StringSplitOptions.None` — blank rows survive), `SuppressTypeAhead` (leave default `false` here). Same namespace as WeatherRadarForm (`MSFSBlindAssist.Forms`) — no qualification needed.
- Timer precedent (`MSFSBlindAssist/Forms/FbwEwdWindow.cs` ~61-118): `System.Windows.Forms.Timer { Interval = ... }`, `Tick += async → RefreshAsync(false)`, `Start()` in Load; `Stop()`+`Dispose()` in BOTH `OnFormClosed` and `Dispose(bool)`.
- `WeatherRadarForm.cs` current state: five multiline TextBoxes (`_currentWeatherBox` ~:94, `_stationBox` ~:117, `_profileBox` ~:141, `_advisoriesBox` ~:164, `_windsAloftBox` ~:186), each `Multiline/ReadOnly/ScrollBars.Vertical`, `Font("Consolas", 9)`, constructed in the y-cursor `InitializeComponent`. Write sites in `RefreshAsync` (~:301-329): tuple unpack assigns `_currentWeatherBox.Text`/`_stationBox.Text`, then `_profileBox.Text = await profileTask;`, `_advisoriesBox.Text = await advisoriesTask;`, `_windsAloftBox.Text = await windsTask;`. `RefreshAsync` sets `_refreshButton.Enabled = false` near its top (after `SetStatus("Fetching weather data...")`) and `_refreshButton.Enabled = true` in its `finally`. `SetupAccessibility`'s `Load` handler ends with `await RefreshAsync(forceRefresh: true);`. `OnFormClosed` currently only restores the foreground window. The form has NO `Dispose` override and NO timer today.

---

### Task 1: Convert the five readouts to `DisplayListBox`

**Files:**
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs`

**Interfaces:**
- Consumes: `Forms.DisplayListBox` (existing shared control).
- Produces: the five fields typed `DisplayListBox`; all writes via `SetText`. Task 2 relies on the conversion being complete (cursor-stable refresh is what makes the timer safe).

- [ ] **Step 1: Change the five field declarations**

`private TextBox _currentWeatherBox = null!;` → `private DisplayListBox _currentWeatherBox = null!;` — and likewise for `_stationBox`, `_profileBox`, `_advisoriesBox`, `_windsAloftBox`. Do NOT touch `_asModeBox`.

- [ ] **Step 2: Convert the five constructions in `InitializeComponent`**

Transformation rule, applied identically to each box — worked example for `_currentWeatherBox`:

BEFORE:
```csharp
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
```

AFTER:
```csharp
        _currentWeatherBox = new DisplayListBox
        {
            Location = new Point(12, y),
            Size = new Size(566, 100),
            Font = new Font("Consolas", 9),
            AccessibleName = "Weather at Current Position",
            AccessibleDescription = "Ambient weather conditions at aircraft position from simulator"
        };
        _currentWeatherBox.SetText("Press F5 or Refresh to fetch weather data.");
```

Rule: keep `Location`, `Size`, `Font`, `AccessibleName`, `AccessibleDescription`, and (where present) `Visible = false` VERBATIM; drop `Multiline`, `ReadOnly`, `ScrollBars`; move the `Text` initializer (when non-empty) to a `SetText(...)` call immediately after the initializer. Per-box parameters:

| Box | Placeholder → SetText | Has `Visible = false` |
|---|---|---|
| `_currentWeatherBox` | "Press F5 or Refresh to fetch weather data." | no |
| `_stationBox` | (empty `Text = ""` — no SetText call needed) | yes |
| `_profileBox` | (empty — no SetText call) | yes |
| `_advisoriesBox` | "Press F5 or Refresh to fetch advisories." | no |
| `_windsAloftBox` | "Press F5 or Refresh to fetch winds aloft." | no |

The y-cursor arithmetic, all labels, and the `Controls.AddRange` list are untouched (field names unchanged). `SetupAccessibility`'s TabIndex block is untouched (a `DisplayListBox` IS a ListBox — TabIndex applies unchanged).

- [ ] **Step 3: Convert the write sites in `RefreshAsync`**

```csharp
            _currentWeatherBox.SetText(ambientText);
            _stationBox.SetText(stationText);
            _profileBox.SetText(await profileTask);
            _advisoriesBox.SetText(await advisoriesTask);
            _windsAloftBox.SetText(await windsTask);
```

(replacing the five `.Text = ...` assignments; the tuple unpack line above them stays). Search the whole file for any remaining `.Text =` on these five fields (e.g. error paths) and convert those too — `grep -n "_currentWeatherBox.Text\|_stationBox.Text\|_profileBox.Text\|_advisoriesBox.Text\|_windsAloftBox.Text"` must come back empty after this step.

- [ ] **Step 4: Build + full suite**

`dotnet build MSFSBlindAssist.sln -c Debug` → 0 warnings; full suite → 1181 green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(weather): Weather Radar readouts become DisplayListBox rows

Completes the 2026-07-02 live-display consistency pass for the radar
window: the five multi-line readouts are now screen-reader-navigable
row lists reconciled via DisplayList.UpdateInPlace (only changed rows
rewritten, selection follows content, unchanged refresh is a no-op).
The single-line AS mode box stays a TextBox per the pass's carve-out.
Text output byte-identical; only the sink control changed.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: 30-second live refresh + Refresh-button focus fix

**Files:**
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs`

**Interfaces:**
- Consumes: existing `RefreshAsync(bool forceRefresh)` with its `_isFetching` guard; Task 1's cursor-stable boxes.
- Produces: `_autoRefreshTimer` with the E/WD lifecycle.

- [ ] **Step 1: Add the timer field** (next to `_isFetching`):

```csharp
    // 30 s live refresh (spec 2026-07-12 §3). Safe only because the readouts are
    // DisplayListBox rows reconciled in place — an unchanged tick is a no-op and a
    // changed one never moves the reading cursor. forceRefresh:false lets the
    // internet-backed fetches (advisories, Open-Meteo winds) serve from their TTL
    // caches; the cheap local AS/SimConnect fetches re-run each tick.
    private System.Windows.Forms.Timer? _autoRefreshTimer;
```

- [ ] **Step 2: Start it in the `Load` handler**

In `SetupAccessibility`'s `Load` handler, after `await RefreshAsync(forceRefresh: true);`, append:

```csharp
            _autoRefreshTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
            _autoRefreshTimer.Tick += (_, _) => _ = RefreshAsync(forceRefresh: false);
            _autoRefreshTimer.Start();
```

- [ ] **Step 3: Remove the Refresh button's Enabled toggling**

Delete `_refreshButton.Enabled = false;` (near the top of `RefreshAsync`) and `_refreshButton.Enabled = true;` (in its `finally`; keep `_isFetching = false;` there). Add one comment where the disable used to be:

```csharp
            // The Refresh button is deliberately NEVER disabled: WinForms moves focus
            // off a focused control when it's disabled, and the 30 s auto-refresh would
            // steal focus from a user resting on the button every tick. The _isFetching
            // guard above already makes mid-fetch clicks harmless no-ops.
```

- [ ] **Step 4: Stop + dispose in both teardown paths**

In `OnFormClosed` (before/alongside the foreground-window restore):

```csharp
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer?.Dispose();
        _autoRefreshTimer = null;
```

And add a `Dispose` override (the form has none today; the timer is not in a components container):

```csharp
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoRefreshTimer?.Stop();
            _autoRefreshTimer?.Dispose();
            _autoRefreshTimer = null;
        }
        base.Dispose(disposing);
    }
```

- [ ] **Step 5: Build + full suite** — 0 warnings, 1181 green.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(weather): 30s live refresh for the Weather Radar window

E/WD-pattern timer driving RefreshAsync(forceRefresh: false) — local AS
fetches track the flight, internet sources keep their TTL caches. The
Refresh button's Enabled toggling is removed: disabling a focused
control steals focus, which the auto-refresh would do every 30 s.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Documentation

**Files:**
- Modify: `docs/weather.md`
- Modify: `docs/a32nx.md`

- [ ] **Step 1: `docs/weather.md`** — in the Weather Radar / read-only-surfaces material, add a short paragraph: the radar's five multi-line readouts are `DisplayListBox` rows reconciled via `DisplayList.UpdateInPlace` (cursor never moves on refresh; one fact/advisory/level per row); the window auto-refreshes every 30 s via `RefreshAsync(forceRefresh: false)` (internet caches honored, F5/button force); the AS mode box stays a single-line TextBox (carve-out, and the 2026-07-11 keyboard-reachability fix); the Refresh button is deliberately never disabled (focus-steal under auto-refresh).

- [ ] **Step 2: `docs/a32nx.md`** — the "single reconcile home" consumer list (~line 29, the status-display/DisplayList paragraph) gains the Weather Radar window alongside E/WD, OANS, RMP, HS787, GSX, ECL, and the CDU forms.

- [ ] **Step 3: Build + suite once (docs sanity), commit**

```bash
git add -A
git commit -m "docs(weather): document the radar ListBox conversion and live refresh

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## In-sim test items (append to the PR checklist)

12. Shift+R: arrow into the middle of each readout, wait through 2-3 auto-refresh cycles — the cursor stays on its row; changed rows (winds during climb) update in place; "Last updated" advances ~every 30 s without touching F5.
13. Rest focus ON the Refresh button through a tick — focus stays put.
14. NVDA arrows read one fact/advisory/wind level per row, including blank separators and rule lines; F5/Escape/tab order unchanged; AS-only boxes still vanish with the switch off.

## Self-review checklist

- Spec coverage: §2 → Task 1; §3 (timer + button fix) → Task 2; §6 → Task 3. §4/§5 impose no code beyond the above (error paths ride the same SetText sinks; no new tests by design).
- Type consistency: `DisplayListBox` unqualified (same namespace); field names unchanged so `Controls.AddRange`/TabIndex/visibility code needs no edits beyond the listed ones.
- No placeholders; each code step carries complete code or an exact, verifiable transformation rule with a worked example and a per-box parameter table.

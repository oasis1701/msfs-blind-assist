# Unified Settings Dialog — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Consolidate the six File-menu preference dialogs into one tabbed `SettingsForm`, keep the database dialog separate and rename it to "Nav Database…".

**Architecture:** A modal `SettingsForm` hosts the app's NVDA-tuned `AccessibleTabControl`; each old dialog's content is extracted into a `UserControl` panel implementing a small `ISettingsPanel` contract (LoadFrom / Validate / ApplyTo / OnLeaving). One OK/Cancel: OK validates every panel in tab order, applies all into `SettingsManager.Current`, `Save()`s once, then `MainForm` re-applies runtime effects. Panels are added incrementally, one per task. Companion spec: `docs/design/2026-07-07-unified-settings-dialog-design.md`.

**Tech Stack:** .NET 10 (C# 13), WinForms, xUnit. Builds x64.

## Global Constraints

- **This is a re-housing, not a settings redesign** — move existing controls (with their `AccessibleName`/`AccessibleDescription`/`TabIndex` verbatim) into the panels; do not recreate layouts or change what settings exist.
- **Persistence goes through the dialog only:** panels never call `SettingsManager.Save()`; the dialog's OK does it once. `SettingsManager.Current` is the shared `UserSettings`; `SettingsManager.Save()` persists it.
- **Live effects must be preserved:** every setting that took effect immediately via an old handler must be re-applied after OK by `MainForm.ApplyRuntimeSettings()`.
- **Audio test tones** (Hand Fly, Taxi Guidance) must be stopped in `OnLeaving()` (tab-switch, OK, Cancel, close) and disposed in the panel's `Dispose`.
- **Build the SOLUTION with `-p:Platform=x64`:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` → **0 Warning(s), 0 Error(s)**. Tests: `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` → 235 green (this is UI; tests won't cover it — build + the manual checks per task are the net).
- **Accessibility:** panels are `UserControl`s of standard WinForms controls with accessible names; the tab strip uses `AccessibleTabControl`. Errors are shown via `MessageBox` (matches today's GeoNames validation) so NVDA reads them.
- **Commit trailer:** each commit ends with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`. Never push (controller opens the PR at the end).

## File structure

Create under `MSFSBlindAssist/Forms/Settings/`: `ISettingsPanel.cs`, `SettingsForm.cs`, `AnnouncementsPanel.cs`, `GeoNamesPanel.cs`, `SimBriefPanel.cs`, `GeminiPanel.cs`, `HandFlyPanel.cs`, `TaxiGuidancePanel.cs`.
Delete (after their content is moved): `Forms/AnnouncementSettingsForm.cs`, `Forms/GeoNamesApiKeyForm.cs`, `Forms/SimBriefSettingsForm.cs`, `Forms/GeminiSettingsForm.cs`, `Forms/HandFlyOptionsForm.cs`, `Forms/TaxiGuidanceOptionsForm.cs` (+ any `.Designer.cs`).
Modify: `MainForm.Designer.cs` (menu items), `MainForm.MenuHandlers.cs` (handlers + `ApplyRuntimeSettings`), `Forms/DatabaseSettingsForm.cs` (title text).
Final tab order in `SettingsForm`: **Announcements, GeoNames, SimBrief, Gemini, Hand Fly, Taxi Guidance**.

---

### Task 1: `ISettingsPanel` + `SettingsForm` shell + SimBrief vertical slice

**Files:**
- Create: `Forms/Settings/ISettingsPanel.cs`, `Forms/Settings/SettingsForm.cs`, `Forms/Settings/SimBriefPanel.cs`
- Modify: `MainForm.Designer.cs`, `MainForm.MenuHandlers.cs`
- Delete: `Forms/SimBriefSettingsForm.cs` (+ `.Designer.cs` if present)

**Interfaces produced:**
- `interface ISettingsPanel { string TabTitle {get;} void LoadFrom(UserSettings s); bool Validate(out string error, out Control? focus); void ApplyTo(UserSettings s); void OnLeaving(); }`
- `class SettingsForm : Form { SettingsForm(Func<Task>? refreshTaxiwayNames = null); }`
- `void MainForm.ApplyRuntimeSettings()` (grows per later task).

- [ ] **Step 1: `ISettingsPanel.cs`:**
```csharp
using System.Windows.Forms;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>One settings section, hosted as a tab in SettingsForm. Panels never persist —
/// the dialog owns Save. LoadFrom populates on open; Validate gates OK; ApplyTo writes into
/// the shared UserSettings; OnLeaving stops transient resources (e.g. test tones).</summary>
public interface ISettingsPanel
{
    string TabTitle { get; }
    void LoadFrom(UserSettings settings);
    bool Validate(out string error, out Control? focus);
    void ApplyTo(UserSettings settings);
    void OnLeaving();
}
```

- [ ] **Step 2: `SettingsForm.cs`** — the host (this is the crux; use verbatim, panels are added by later tasks):
```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

public class SettingsForm : Form
{
    private readonly AccessibleTabControl _tabs;
    private readonly List<ISettingsPanel> _panels = new();
    private ISettingsPanel? _currentPanel;

    public SettingsForm(Func<Task>? refreshTaxiwayNames = null)
    {
        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        ClientSize = new System.Drawing.Size(660, 560);

        _tabs = new AccessibleTabControl { Dock = DockStyle.Fill, AccessibleName = "Settings sections" };
        // Stop the outgoing panel's tone BEFORE the tab actually switches.
        _tabs.Selecting += (_, _) => _currentPanel?.OnLeaving();
        _tabs.SelectedIndexChanged += (_, _) =>
        { if (_tabs.SelectedIndex >= 0 && _tabs.SelectedIndex < _panels.Count) _currentPanel = _panels[_tabs.SelectedIndex]; };

        // Panels are added here in FINAL TAB ORDER by later tasks. Task 1 adds only SimBrief;
        // subsequent tasks INSERT their AddPanel(...) calls so the final order is:
        // Announcements, GeoNames, SimBrief, Gemini, HandFly, TaxiGuidance.
        AddPanel(new SimBriefPanel());

        var ok = new Button { Text = "OK", AccessibleName = "OK", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AccessibleName = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += OnOk;
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 44, Padding = new Padding(8) };
        buttonRow.Controls.Add(cancel);
        buttonRow.Controls.Add(ok);

        Controls.Add(_tabs);
        Controls.Add(buttonRow);
        AcceptButton = ok; CancelButton = cancel;
    }

    private void AddPanel(ISettingsPanel panel)
    {
        var uc = (UserControl)panel;
        uc.Dock = DockStyle.Fill;
        var page = new TabPage(panel.TabTitle) { AccessibleName = panel.TabTitle };
        page.Controls.Add(uc);
        _tabs.TabPages.Add(page);
        _panels.Add(panel);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var current = SettingsManager.Current;
        foreach (var p in _panels) p.LoadFrom(current);
        if (_panels.Count > 0) { _currentPanel = _panels[0]; _tabs.SelectedIndex = 0; }
    }

    private void OnOk(object? sender, EventArgs e)
    {
        for (int i = 0; i < _panels.Count; i++)
        {
            if (!_panels[i].Validate(out string error, out Control? focus))
            {
                _tabs.SelectedIndex = i;
                MessageBox.Show(this, error, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                focus?.Focus();
                return; // do not save
            }
        }
        var current = SettingsManager.Current;
        foreach (var p in _panels) p.ApplyTo(current);
        SettingsManager.Save();
        foreach (var p in _panels) p.OnLeaving();
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        foreach (var p in _panels) p.OnLeaving(); // stop tones on every close path
        base.OnFormClosing(e);
    }
}
```

- [ ] **Step 3: `SimBriefPanel.cs`** — extract from `Forms/SimBriefSettingsForm.cs` (read it first). It has a single SimBrief username `TextBox` + instruction label. Create a `UserControl` implementing `ISettingsPanel`:
  - Move the label + textbox (with their `AccessibleName`s) into the UserControl.
  - `TabTitle => "SimBrief"`.
  - `LoadFrom(s)` → set the textbox from `s.SimBriefUsername` (use the exact `UserSettings` field the old form read/wrote — confirm its name in `UserSettings.cs`).
  - `ApplyTo(s)` → `s.SimBriefUsername = textbox.Text.Trim();`
  - `Validate(...)` → `error=""; focus=null; return true;` (no validation).
  - `OnLeaving()` → `{ }`.

- [ ] **Step 4: Menu wiring** in `MainForm.Designer.cs` + `MainForm.MenuHandlers.cs`:
  - Add a `settingsMenuItem` ToolStripMenuItem, Text `"Settings…"`, to the File menu (place it where the old settings items were). Remove `simbriefSettingsMenuItem` from the designer.
  - In `MainForm.MenuHandlers.cs`, remove `SimBriefSettingsMenuItem_Click`; add:
```csharp
private void SettingsMenuItem_Click(object? sender, EventArgs e)
{
    using var dlg = new Forms.Settings.SettingsForm(refreshTaxiwayNames: RefreshTaxiwayNamesAsync);
    if (dlg.ShowDialog(this) == DialogResult.OK)
    {
        ApplyRuntimeSettings();
        statusLabel.Text = "Settings saved";
        announcer.Announce("Settings saved");
    }
}

/// <summary>Re-applies saved UserSettings to the live runtime managers after the Settings
/// dialog is accepted, so changes take effect without restarting. Each settings section that
/// has a live effect adds its re-apply here (populated as panels are migrated).</summary>
private void ApplyRuntimeSettings()
{
    // (SimBrief has no live effect. Later tasks add announcement/handfly/taxi re-apply here.)
}
```
  Use the app's actual taxiway-refresh method name for `RefreshTaxiwayNamesAsync` — find what `TaxiGuidanceOptionsForm` was passed today (`MainForm.MenuHandlers.cs` `TaxiGuidanceOptionsMenuItem_Click`); if it's an inline lambda, pass that same lambda instead.
  - Delete `Forms/SimBriefSettingsForm.cs` (+ `.Designer.cs`).

- [ ] **Step 5: Build + verify.** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64` → 0 warn/0 err. `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` → 235 green. Grep confirms no remaining `SimBriefSettingsForm` reference.
- [ ] **Step 6: Manual smoke (note in report for the human):** File ▸ Settings… opens a dialog titled "Settings" with a SimBrief tab; the username loads, editing + OK persists to settings.json, Cancel discards.
- [ ] **Step 7: Commit** `git add -A && git commit -m "feat(settings): SettingsForm shell + ISettingsPanel + SimBrief tab; add Settings menu item"`

---

### Tasks 2–6: one settings panel each (same procedure)

For each, follow this **procedure** (the panels differ only in their content and the specifics called out):
1. **Read the source form** and move its controls verbatim (labels, inputs, group boxes, buttons, with their `AccessibleName`/`AccessibleDescription`/`TabIndex`) into a new `UserControl` named `<Section>Panel` implementing `ISettingsPanel` in `Forms/Settings/`.
2. Implement the four members by lifting the form's existing code:
   - `TabTitle` → the section name.
   - `LoadFrom(s)` ← the form's constructor/`OnLoad` population from `UserSettings` (or its constructor parameters, which the old MainForm handler sourced from `SettingsManager.Current`).
   - `ApplyTo(s)` ← the form's save/`SelectedX`-property logic, writing into `UserSettings` (NO `SettingsManager.Save()` — the dialog saves).
   - `Validate(out error, out focus)` ← the form's validation (see GeoNames); `return true` if the form had none.
   - `OnLeaving()` ← stop/dispose any test-tone generator/timer (Hand Fly, Taxi); no-op otherwise. Make it idempotent and non-throwing.
3. Insert `AddPanel(new <Section>Panel(...))` into `SettingsForm`'s constructor **at the correct position for the final tab order** (Announcements, GeoNames, SimBrief, Gemini, HandFly, TaxiGuidance).
4. Move the section's **live-apply** code from the old MainForm handler into `MainForm.ApplyRuntimeSettings()` (reading from `SettingsManager.Current`).
5. Remove the old menu item from `MainForm.Designer.cs` and its `*_Click` handler from `MainForm.MenuHandlers.cs`; delete the old form file(s).
6. Build (0 warn/0 err) + tests (235) + grep for no dangling reference to the old form. Note the manual check. Commit.

- [ ] **Task 2 — AnnouncementsPanel** (from `AnnouncementSettingsForm`). Combine its General and Weather **sub-tabs into two `GroupBox`es** ("General", "Weather") stacked in the one panel (no inner `TabControl`). `LoadFrom`/`ApplyTo` cover: announcement-mode radios (SR/SAPI), `NearestCityAnnouncementInterval`, `AnnounceTimeWithSeconds`, `GsxBackgroundMonitoring`, `WeatherAutoAnnounceEnabled`, `WeatherAutoAnnounceIntervalMinutes`, `SigmetProximityAlertsEnabled`, `PirepProximityAlertsEnabled`, `SigmetProximityRangeNm`. **Live-apply** (move from `AnnouncementSettingsMenuItem_Click` into `ApplyRuntimeSettings`): `announcer.SetAnnouncementMode(...)`, `RestartNearestCityAnnouncementTimer()`, `if (activeSkyWeatherMonitor != null) activeSkyWeatherMonitor.IntervalMinutes = settings.WeatherAutoAnnounceIntervalMinutes`, and the GSX toggle (`if (_gsxService != null && (_accessGsxForm == null || !_accessGsxForm.Visible)) _gsxService.AnnounceWhenFormHidden = settings.GsxBackgroundMonitoring`). Commit `feat(settings): Announcements tab; migrate announcement live-apply`.
- [ ] **Task 3 — GeoNamesPanel** (from `GeoNamesApiKeyForm`). Username + 11 numeric fields + miles/km radios + a **"Reset Defaults"** button (kept, resets only this panel's fields). Its per-field numeric validation + radius-limit warning move into `Validate`: return `false` with the message and the offending `Control` on the first bad field (the dialog switches to this tab and focuses it). This dialog self-persisted before and had no live-apply, so add nothing to `ApplyRuntimeSettings`. Commit `feat(settings): GeoNames tab with per-field validation`.
- [ ] **Task 4 — GeminiPanel** (from `GeminiSettingsForm`). API-key textbox, model `ComboBox` + async **"Refresh models"** button (keep the existing async fetch via the Gemini service + offline fallback + status label) + search-grounding checkbox + `LinkLabel`. `ApplyTo` writes key/model/grounding into `UserSettings`. No live-apply. Commit `feat(settings): Gemini tab with async model refresh`.
- [ ] **Task 5 — HandFlyPanel** (from `HandFlyOptionsForm`). Move the dense control set (feedback-mode radios, wave-type combos + volume `TrackBar`s, Test/Stop-Tone button, checkboxes, heading-threshold combo). `OnLeaving()` stops the `AudioToneGenerator`; the panel disposes it in `Dispose(bool)`. **Live-apply** (move from `HandFlyOptionsMenuItem_Click`): push the hand-fly settings into the live hand-fly manager exactly as that handler did. Commit `feat(settings): Hand Fly tab; migrate hand-fly live-apply + tone lifecycle`.
- [ ] **Task 6 — TaxiGuidancePanel** (from `TaxiGuidanceOptionsForm`). Tone-type combo + volume + test, checkboxes, two ground-speed interval combos, the **docking sub-group** (`GroupBox`: enabled, beep-type, beep volume, beep test), and the **"Refresh taxiway names"** async button wired to the `Func<Task>` passed into the panel's constructor. `OnLeaving()` stops both the taxi test tone and the docking beep. **Live-apply** (move from `TaxiGuidanceOptionsMenuItem_Click`): push taxi/docking settings into `taxiGuidanceManager`/`dockingGuidanceManager` as that handler did. Constructor: `TaxiGuidancePanel(Func<Task>? refreshTaxiwayNames)`. Commit `feat(settings): Taxi Guidance tab; migrate taxi live-apply + tone lifecycle`.

---

### Task 7: Rename Database Settings → "Nav Database…"

**Files:** Modify `MainForm.Designer.cs` (menu item text), `Forms/DatabaseSettingsForm.cs` (title/heading).

- [ ] **Step 1:** Change `databaseSettingsMenuItem.Text` to `"Nav Database…"`.
- [ ] **Step 2:** In `DatabaseSettingsForm`, change the form `Text` (title bar) and any top heading label to "Nav Database" (keep all button/action behavior unchanged). Update its `AccessibleName` if it names itself "Database Settings".
- [ ] **Step 3:** Build (0 warn/0 err) + tests (235). Commit `refactor(settings): rename Database Settings to "Nav Database"`.

---

### Task 8: Final verification & PR

- [ ] **Step 1:** `dotnet build MSFSBlindAssist.sln -c Debug -p:Platform=x64 --no-incremental` → 0 Warning(s)/0 Error(s).
- [ ] **Step 2:** `dotnet test tests/MSFSBlindAssist.Tests -p:Platform=x64` → 235 green.
- [ ] **Step 3:** Confirm cleanup: the six old form files are gone (`ls Forms/*SettingsForm.cs Forms/*OptionsForm.cs Forms/GeoNamesApiKeyForm.cs`), no source references any of them, and the File menu has exactly `Settings…` + `Nav Database…` for these (no leftover items/handlers).
- [ ] **Step 4:** Write the in-app manual-test checklist into the PR body: all six tabs load current values; edits persist on OK; Cancel discards; GeoNames validation switches to its tab + focuses the bad field; test tones stop on tab-switch/OK/Cancel/close; live effects (announcement mode, a taxi/hand-fly tone volume) apply immediately without restart; NVDA announces only the selected tab.
- [ ] **Step 5:** Controller pushes `feat/unified-settings-dialog` and opens a PR to `main` on `origin`.

---

## Self-review notes (author)

- **Spec coverage:** tabbed dialog + AccessibleTabControl (T1), the 6 tabs incl. Announcements-as-two-groups (T1–T6), ISettingsPanel + single OK/Cancel + one Save (T1 host), per-panel validation/Gemini-async/tone-lifecycle/GeoNames-reset (T3–T6), live-apply consolidation (T1 stub + T2/T5/T6), delete old forms (each task), rename Nav Database (T7), verification (T8). All spec sections mapped.
- **No placeholders:** the two crux files (`ISettingsPanel`, `SettingsForm`) are complete code; the panel tasks are extraction procedures against named existing forms with the exact settings fields + live-apply calls to move (the control layouts are moved, not re-authored, so pasting them would be inaccurate).
- **Type consistency:** `ISettingsPanel` members, `SettingsForm(Func<Task>?)`, `AddPanel`, `ApplyRuntimeSettings`, `SettingsMenuItem_Click` are consistent across tasks. Verify each panel's `UserSettings` field names against `Settings/UserSettings.cs` during implementation.

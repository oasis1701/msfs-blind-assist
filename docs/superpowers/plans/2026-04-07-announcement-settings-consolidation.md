# Announcement Settings Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consolidate announcement settings from WeatherSettingsForm and GeoNamesApiKeyForm into a single tabbed AnnouncementSettingsForm, and move the decode-advisories display setting into WeatherRadarForm.

**Architecture:** Replace the existing flat AnnouncementSettingsForm with a tabbed version using AccessibleTabControl. General tab holds announcement mode + nearest city interval. Weather tab holds weather/SIGMET/PIREP auto-announce toggles + proximity range. Delete WeatherSettingsForm entirely. Add decode-advisories checkbox to WeatherRadarForm.

**Tech Stack:** C# / .NET 9, Windows Forms, AccessibleTabControl (custom control in Controls/)

**Spec:** `docs/superpowers/specs/2026-04-07-announcement-settings-consolidation-design.md`

---

### Task 1: Rewrite AnnouncementSettingsForm as tabbed form

**Files:**
- Modify: `MSFSBlindAssist/Forms/AnnouncementSettingsForm.cs`

- [ ] **Step 1: Replace the entire AnnouncementSettingsForm with the tabbed version**

Replace the contents of `MSFSBlindAssist/Forms/AnnouncementSettingsForm.cs` with:

```csharp
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Controls;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

public partial class AnnouncementSettingsForm : Form
{
    // ── General tab controls ────────────────────────────────────────────────
    private RadioButton _screenReaderRadio = null!;
    private RadioButton _sapiRadio = null!;
    private Label _statusLabel = null!;
    private ComboBox _nearestCityIntervalCombo = null!;

    // ── Weather tab controls ────────────────────────────────────────────────
    private CheckBox _weatherAutoAnnounce = null!;
    private CheckBox _sigmetAlerts = null!;
    private CheckBox _pirepAlerts = null!;
    private NumericUpDown _proximityRange = null!;

    // ── Buttons ─────────────────────────────────────────────────────────────
    private Button _okButton = null!;
    private Button _cancelButton = null!;

    // ── Public results ──────────────────────────────────────────────────────
    public AnnouncementMode SelectedMode { get; private set; }
    public int NearestCityAnnouncementInterval { get; private set; }
    public bool WeatherAutoAnnounceEnabled { get; private set; }
    public bool SigmetProximityAlertsEnabled { get; private set; }
    public bool PirepProximityAlertsEnabled { get; private set; }
    public int SigmetProximityRangeNm { get; private set; }

    public AnnouncementSettingsForm(
        AnnouncementMode currentMode,
        int nearestCityInterval,
        bool weatherAutoAnnounce,
        bool sigmetAlerts,
        bool pirepAlerts,
        int proximityRangeNm)
    {
        SelectedMode = currentMode;
        NearestCityAnnouncementInterval = nearestCityInterval;
        WeatherAutoAnnounceEnabled = weatherAutoAnnounce;
        SigmetProximityAlertsEnabled = sigmetAlerts;
        PirepProximityAlertsEnabled = pirepAlerts;
        SigmetProximityRangeNm = proximityRangeNm;
        InitializeComponent();
        SetupAccessibility();
        UpdateScreenReaderStatus();
    }

    private void InitializeComponent()
    {
        Text = "Announcement Settings";
        Size = new Size(480, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var tabs = new AccessibleTabControl
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Announcement settings tabs"
        };

        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildWeatherTab());

        // Button panel at bottom
        var buttonPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 45,
        };

        _okButton = new Button
        {
            Text = "OK",
            Location = new Point(300, 8),
            Size = new Size(75, 28),
            DialogResult = DialogResult.OK,
            AccessibleName = "OK",
            AccessibleDescription = "Save announcement settings"
        };
        _okButton.Click += OkButton_Click;

        _cancelButton = new Button
        {
            Text = "Cancel",
            Location = new Point(385, 8),
            Size = new Size(75, 28),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel without saving"
        };

        buttonPanel.Controls.AddRange(new Control[] { _okButton, _cancelButton });

        Controls.Add(tabs);
        Controls.Add(buttonPanel);

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    // ── General tab ─────────────────────────────────────────────────────────

    private TabPage BuildGeneralTab()
    {
        var tab = new TabPage("General")
        {
            AccessibleName = "General",
            AccessibleDescription = "Announcement mode and location announcement settings",
            Padding = new Padding(12),
        };

        var modeLabel = new Label
        {
            Text = "Choose how announcements are delivered:",
            Location = new Point(12, 12),
            Size = new Size(420, 20),
            AccessibleName = "Announcement mode"
        };

        _screenReaderRadio = new RadioButton
        {
            Text = "Screen Reader (NVDA, JAWS, etc.) - Recommended",
            Location = new Point(12, 40),
            Size = new Size(420, 25),
            AccessibleName = "Screen Reader Mode",
            AccessibleDescription = "Send announcements through your screen reader for natural speech integration",
            Checked = SelectedMode == AnnouncementMode.ScreenReader
        };

        _sapiRadio = new RadioButton
        {
            Text = "SAPI (Windows Speech) - Fallback",
            Location = new Point(12, 68),
            Size = new Size(420, 25),
            AccessibleName = "SAPI Mode",
            AccessibleDescription = "Use Windows built-in speech synthesis for announcements",
            Checked = SelectedMode == AnnouncementMode.SAPI
        };

        _statusLabel = new Label
        {
            Location = new Point(12, 100),
            Size = new Size(420, 40),
            AccessibleName = "Screen Reader Status",
            Text = "Checking screen reader status..."
        };

        var intervalLabel = new Label
        {
            Text = "Announce nearest city automatically:",
            Location = new Point(12, 155),
            Size = new Size(250, 20),
            AccessibleName = "Announce nearest city automatically label"
        };

        _nearestCityIntervalCombo = new ComboBox
        {
            Location = new Point(270, 152),
            Size = new Size(170, 25),
            DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Nearest city announcement interval",
            AccessibleDescription = "Choose how often to automatically announce the nearest city"
        };
        _nearestCityIntervalCombo.Items.AddRange(new object[]
        {
            "Off",
            "Every 1 minute",
            "Every 2 minutes",
            "Every 5 minutes",
            "Every 10 minutes",
            "Every 15 minutes",
            "Every 20 minutes"
        });
        _nearestCityIntervalCombo.SelectedIndex = IntervalToIndex(NearestCityAnnouncementInterval);

        tab.Controls.AddRange(new Control[]
        {
            modeLabel, _screenReaderRadio, _sapiRadio, _statusLabel,
            intervalLabel, _nearestCityIntervalCombo
        });

        return tab;
    }

    // ── Weather tab ─────────────────────────────────────────────────────────

    private TabPage BuildWeatherTab()
    {
        var tab = new TabPage("Weather")
        {
            AccessibleName = "Weather",
            AccessibleDescription = "Weather and advisory auto-announcement settings",
            Padding = new Padding(12),
        };

        _weatherAutoAnnounce = new CheckBox
        {
            Text = "Auto-announce &weather state changes",
            Location = new Point(12, 12),
            Size = new Size(420, 24),
            Checked = WeatherAutoAnnounceEnabled,
            AccessibleName = "Auto-announce weather state changes",
            AccessibleDescription = "Automatically announce when entering or leaving clouds and when precipitation starts or stops"
        };

        _sigmetAlerts = new CheckBox
        {
            Text = "Auto-announce approaching &SIGMETs and AIRMETs",
            Location = new Point(12, 48),
            Size = new Size(420, 24),
            Checked = SigmetProximityAlertsEnabled,
            AccessibleName = "Auto-announce approaching SIGMETs and AIRMETs",
            AccessibleDescription = "Announce when the aircraft enters the proximity range of an active SIGMET or AIRMET"
        };

        _pirepAlerts = new CheckBox
        {
            Text = "Auto-announce approaching pilot reports (&PIREPs)",
            Location = new Point(12, 84),
            Size = new Size(420, 24),
            Checked = PirepProximityAlertsEnabled,
            AccessibleName = "Auto-announce approaching PIREPs",
            AccessibleDescription = "Announce when the aircraft enters the proximity range of a significant pilot report of turbulence or icing"
        };

        var rangeLabel = new Label
        {
            Text = "&Proximity range (nautical miles):",
            Location = new Point(12, 126),
            Size = new Size(250, 20),
            AccessibleName = "Proximity range label"
        };

        _proximityRange = new NumericUpDown
        {
            Location = new Point(270, 122),
            Size = new Size(80, 24),
            Minimum = 10,
            Maximum = 500,
            Value = Math.Clamp(SigmetProximityRangeNm, 10, 500),
            AccessibleName = "Proximity range in nautical miles",
            AccessibleDescription = "Distance at which to announce approaching SIGMETs, AIRMETs, and PIREPs"
        };

        tab.Controls.AddRange(new Control[]
        {
            _weatherAutoAnnounce, _sigmetAlerts, _pirepAlerts,
            rangeLabel, _proximityRange
        });

        return tab;
    }

    // ── Screen reader detection ─────────────────────────────────────────────

    private void UpdateScreenReaderStatus()
    {
        try
        {
            using (var tolkTest = new TolkWrapper())
            {
                if (tolkTest.Initialize())
                {
                    if (tolkTest.IsScreenReaderRunning())
                    {
                        string detected = tolkTest.DetectedScreenReader;
                        _statusLabel.Text = $"Screen reader detected: {detected}\nChoose 'Screen Reader' for best experience.";
                        _statusLabel.ForeColor = Color.DarkGreen;
                    }
                    else
                    {
                        _statusLabel.Text = "No screen reader detected.\nSAPI mode recommended for speech feedback.";
                        _statusLabel.ForeColor = Color.DarkOrange;
                    }
                }
                else
                {
                    _statusLabel.Text = "Unable to initialize screen reader detection.\nSAPI mode will be used as fallback.";
                    _statusLabel.ForeColor = Color.Red;
                }
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Error checking screen reader: {ex.Message}\nSAPI mode recommended.";
            _statusLabel.ForeColor = Color.Red;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int IntervalToIndex(int seconds) => seconds switch
    {
        60   => 1,
        120  => 2,
        300  => 3,
        600  => 4,
        900  => 5,
        1200 => 6,
        _    => 0
    };

    private static int IndexToInterval(int index) => index switch
    {
        1 => 60,
        2 => 120,
        3 => 300,
        4 => 600,
        5 => 900,
        6 => 1200,
        _ => 0
    };

    private void SetupAccessibility()
    {
        Load += (_, _) =>
        {
            BringToFront();
            Activate();
            _screenReaderRadio.Focus();
        };
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        SelectedMode = _screenReaderRadio.Checked
            ? AnnouncementMode.ScreenReader
            : AnnouncementMode.SAPI;
        NearestCityAnnouncementInterval = IndexToInterval(_nearestCityIntervalCombo.SelectedIndex);
        WeatherAutoAnnounceEnabled = _weatherAutoAnnounce.Checked;
        SigmetProximityAlertsEnabled = _sigmetAlerts.Checked;
        PirepProximityAlertsEnabled = _pirepAlerts.Checked;
        SigmetProximityRangeNm = (int)_proximityRange.Value;
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }
}
```

- [ ] **Step 2: Build and verify compilation**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build errors in MainForm.cs because the constructor signature changed. That's expected — we fix it in Task 3.

- [ ] **Step 3: Commit**

```bash
git add MSFSBlindAssist/Forms/AnnouncementSettingsForm.cs
git commit -m "refactor: rewrite AnnouncementSettingsForm as tabbed form

General tab: announcement mode + nearest city interval
Weather tab: weather/SIGMET/PIREP auto-announce + proximity range"
```

---

### Task 2: Add decode-advisories checkbox to WeatherRadarForm

**Files:**
- Modify: `MSFSBlindAssist/Forms/WeatherRadarForm.cs`

- [ ] **Step 1: Add the checkbox field declaration**

In `WeatherRadarForm.cs`, after the `_closeButton` field declaration (line 27), add:

```csharp
    private CheckBox _decodeCheckBox = null!;
```

- [ ] **Step 2: Add the checkbox to InitializeComponent**

In `InitializeComponent()`, after the `_closeButton.Click += CloseButton_Click;` line and before the `Controls.AddRange` call, add:

```csharp
        _decodeCheckBox = new CheckBox
        {
            Text = "&Decode advisories into plain English",
            Location = new Point(12, 598),
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
```

- [ ] **Step 3: Move buttons down and add checkbox to Controls**

Shift the refresh and close buttons down to make room. Change their Y locations and the status label:

Replace:
```csharp
        _statusLabel = new Label
        {
            Location = new Point(12, 604),
            Size = new Size(370, 20),
```

With:
```csharp
        _statusLabel = new Label
        {
            Location = new Point(12, 632),
            Size = new Size(370, 20),
```

Replace:
```csharp
        _refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(390, 598),
```

With:
```csharp
        _refreshButton = new Button
        {
            Text = "&Refresh (F5)",
            Location = new Point(390, 626),
```

Replace:
```csharp
        _closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(500, 598),
```

With:
```csharp
        _closeButton = new Button
        {
            Text = "&Close",
            Location = new Point(500, 626),
```

Update the form size to accommodate the extra row. Replace:
```csharp
        Size = new Size(600, 680);
```

With:
```csharp
        Size = new Size(600, 710);
```

Add `_decodeCheckBox` to the `Controls.AddRange` array, before `_statusLabel`:

Replace:
```csharp
            _statusLabel, _refreshButton, _closeButton
```

With:
```csharp
            _decodeCheckBox, _statusLabel, _refreshButton, _closeButton
```

- [ ] **Step 4: Update tab order in SetupAccessibility**

Replace:
```csharp
        _refreshButton.TabIndex = 3;
        _closeButton.TabIndex = 4;
```

With:
```csharp
        _decodeCheckBox.TabIndex = 3;
        _refreshButton.TabIndex = 4;
        _closeButton.TabIndex = 5;
```

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Forms/WeatherRadarForm.cs
git commit -m "feat: move decode-advisories checkbox into WeatherRadarForm

Saves immediately on change since it's a display preference within
the already-open weather radar window."
```

---

### Task 3: Update MainForm handlers and remove weather settings menu item

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs`
- Modify: `MSFSBlindAssist/MainForm.Designer.cs`

- [ ] **Step 1: Update AnnouncementSettingsMenuItem_Click in MainForm.cs**

Replace the entire `AnnouncementSettingsMenuItem_Click` method (lines 2170-2190) with:

```csharp
    private void AnnouncementSettingsMenuItem_Click(object? sender, EventArgs e)
    {
        var settings = MSFSBlindAssist.Settings.SettingsManager.Current;
        var currentMode = announcer.GetAnnouncementMode();
        using (var settingsForm = new AnnouncementSettingsForm(
            currentMode,
            settings.NearestCityAnnouncementInterval,
            settings.WeatherAutoAnnounceEnabled,
            settings.SigmetProximityAlertsEnabled,
            settings.PirepProximityAlertsEnabled,
            settings.SigmetProximityRangeNm))
        {
            if (settingsForm.ShowDialog(this) == DialogResult.OK)
            {
                // Announcement mode
                var newMode = settingsForm.SelectedMode;
                announcer.SetAnnouncementMode(newMode);

                // Nearest city interval
                settings.NearestCityAnnouncementInterval = settingsForm.NearestCityAnnouncementInterval;
                RestartNearestCityAnnouncementTimer();

                // Weather announcements
                settings.WeatherAutoAnnounceEnabled = settingsForm.WeatherAutoAnnounceEnabled;
                settings.SigmetProximityAlertsEnabled = settingsForm.SigmetProximityAlertsEnabled;
                settings.PirepProximityAlertsEnabled = settingsForm.PirepProximityAlertsEnabled;
                settings.SigmetProximityRangeNm = settingsForm.SigmetProximityRangeNm;

                MSFSBlindAssist.Settings.SettingsManager.Save();

                string modeText = newMode == AnnouncementMode.ScreenReader ? "screen reader" : "SAPI";
                statusLabel.Text = $"Announcement settings saved (mode: {modeText})";
                announcer.Announce("Announcement settings saved");
            }
        }
    }
```

- [ ] **Step 2: Remove WeatherSettingsMenuItem_Click and its menu item click handler reference**

Delete the entire `WeatherSettingsMenuItem_Click` method (lines 1333-1353) from MainForm.cs.

Also delete the `WeatherRadarMenuItem_Click` line if it references weather settings (check — it may be the weather *radar* opener, which stays). Keep `WeatherRadarMenuItem_Click`.

- [ ] **Step 3: Remove weatherSettingsMenuItem from MainForm.Designer.cs**

In `MainForm.Designer.cs`:

Remove the field declaration:
```csharp
        private System.Windows.Forms.ToolStripMenuItem weatherSettingsMenuItem = null!;
```

Remove the instantiation:
```csharp
            this.weatherSettingsMenuItem = new System.Windows.Forms.ToolStripMenuItem();
```

Remove it from the `fileMenuItem.DropDownItems.AddRange` array:
```csharp
            this.weatherSettingsMenuItem,
```

Remove the entire configuration block:
```csharp
            // weatherSettingsMenuItem
            //
            this.weatherSettingsMenuItem.AccessibleName = "Weather Settings";
            this.weatherSettingsMenuItem.AccessibleDescription = "Configure weather auto-announcements and SIGMET proximity alerts";
            this.weatherSettingsMenuItem.Name = "weatherSettingsMenuItem";
            this.weatherSettingsMenuItem.Size = new System.Drawing.Size(220, 26);
            this.weatherSettingsMenuItem.Text = "Weather &Settings";
            this.weatherSettingsMenuItem.Click += new System.EventHandler(this.WeatherSettingsMenuItem_Click);
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build errors because WeatherSettingsForm is still referenced. We delete it in Task 4.

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/MainForm.cs MSFSBlindAssist/MainForm.Designer.cs
git commit -m "refactor: wire consolidated AnnouncementSettingsForm and remove weather settings menu item"
```

---

### Task 4: Delete WeatherSettingsForm and remove nearest city interval from GeoNamesApiKeyForm

**Files:**
- Delete: `MSFSBlindAssist/Forms/WeatherSettingsForm.cs`
- Modify: `MSFSBlindAssist/Forms/GeoNamesApiKeyForm.cs`

- [ ] **Step 1: Delete WeatherSettingsForm.cs**

```bash
git rm MSFSBlindAssist/Forms/WeatherSettingsForm.cs
```

- [ ] **Step 2: Remove nearest city announcement controls from GeoNamesApiKeyForm.cs**

In `GeoNamesApiKeyForm.cs`:

Remove the field declaration (line 20):
```csharp
    private ComboBox nearestCityAnnouncementComboBox = null!;
```

Remove the nearest city announcement section from `InitializeComponent()` — the label, combo box creation, and `this.Controls.Add()` calls for both (approximately lines 232-260).

Remove the interval loading logic from the settings loader method (approximately lines 451-484 — the entire `if/else if` chain for `NearestCityAnnouncementInterval`).

Remove the reset line from `ResetDefaultsButton_Click` (line 504):
```csharp
        nearestCityAnnouncementComboBox.SelectedIndex = 0; // Off
```

Remove the interval save logic from `SaveButton_Click` (approximately lines 693-704):
```csharp
            int selectedInterval = nearestCityAnnouncementComboBox.SelectedIndex switch
            {
                0 => 0,      // Off
                1 => 60,     // Every 1 minute
                2 => 120,    // Every 2 minutes
                3 => 300,    // Every 5 minutes
                4 => 600,    // Every 10 minutes
                5 => 900,    // Every 15 minutes
                6 => 1200,   // Every 20 minutes
                _ => 0       // Default to Off
            };
            SettingsManager.Current.NearestCityAnnouncementInterval = selectedInterval;
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 errors. All references to WeatherSettingsForm removed, GeoNamesApiKeyForm compiles without the combo box.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor: delete WeatherSettingsForm, remove nearest city interval from GeoNames form

WeatherSettingsForm settings now live in AnnouncementSettingsForm.
Nearest city interval now in AnnouncementSettingsForm General tab.
GeoNamesApiKeyForm retains API key, thresholds, and distance units."
```

---

### Task 5: Remove DecodeWeatherAdvisories from AnnouncementSettingsMenuItem save logic

**Files:**
- Modify: `MSFSBlindAssist/MainForm.cs` (verify only — should already be clean)

- [ ] **Step 1: Verify no remaining references to WeatherSettingsForm or DecodeWeatherAdvisories in MainForm.cs**

Run: `grep -n "WeatherSettingsForm\|DecodeWeatherAdvisories" MSFSBlindAssist/MainForm.cs`
Expected: No matches. If any remain, remove them.

- [ ] **Step 2: Verify the GeoNames handler no longer saves announcement interval**

Check that `GeoNamesSettingsMenuItem_Click` still calls `RestartNearestCityAnnouncementTimer()`. This is still needed because the GeoNames form saves other settings that affect the timer indirectly. However, since the interval is no longer changed in that form, this call is now a no-op (the interval hasn't changed). It's harmless to keep, but can be removed for clarity.

Remove `RestartNearestCityAnnouncementTimer();` from `GeoNamesSettingsMenuItem_Click` since the interval is now managed by the announcement settings form.

- [ ] **Step 3: Final build and verify**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: 0 errors, same warning count as before.

- [ ] **Step 4: Commit**

```bash
git add MSFSBlindAssist/MainForm.cs
git commit -m "cleanup: remove stale RestartNearestCityAnnouncementTimer call from GeoNames handler"
```

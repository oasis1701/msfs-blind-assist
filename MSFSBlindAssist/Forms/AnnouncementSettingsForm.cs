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

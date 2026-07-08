using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>Announcements section of the unified Settings dialog. Extracted from the retired
/// standalone announcement-settings dialog — same controls, same AccessibleNames, but the old
/// General/Weather inner TabControl is replaced with two stacked GroupBoxes.</summary>
public class AnnouncementsPanel : UserControl, ISettingsPanel
{
    // ── General group ────────────────────────────────────────────────────────
    private RadioButton _screenReaderRadio = null!;
    private RadioButton _sapiRadio = null!;
    private Label _statusLabel = null!;
    private ComboBox _nearestCityIntervalCombo = null!;
    private CheckBox _timeWithSecondsCheck = null!;
    private CheckBox _gsxBackgroundMonitoring = null!;

    // ── Weather group ────────────────────────────────────────────────────────
    private CheckBox _weatherAutoAnnounce = null!;
    private ComboBox _weatherIntervalCombo = null!;
    private CheckBox _sigmetAlerts = null!;
    private CheckBox _pirepAlerts = null!;
    private NumericUpDown _proximityRange = null!;

    /// <summary>Combo entries: minutes (0 = AS download interval, no extra throttle).</summary>
    private static readonly int[] IntervalChoicesMinutes = { 0, 5, 10, 15, 20, 30, 45, 60 };

    public string TabTitle => "Announcements";

    public AnnouncementsPanel()
    {
        InitializeComponent();
        UpdateScreenReaderStatus();
    }

    private void InitializeComponent()
    {
        AutoScroll = true;

        var generalGroup = BuildGeneralGroup();
        var weatherGroup = BuildWeatherGroup();
        weatherGroup.Location = new System.Drawing.Point(12, generalGroup.Bottom + 12);

        Controls.Add(generalGroup);
        Controls.Add(weatherGroup);
    }

    private GroupBox BuildGeneralGroup()
    {
        var group = new GroupBox
        {
            Text = "General",
            Location = new System.Drawing.Point(12, 12),
            Size = new System.Drawing.Size(460, 270),
            AccessibleName = "General",
            AccessibleDescription = "Announcement mode and location announcement settings",
        };

        var modeLabel = new Label
        {
            Text = "Choose how announcements are delivered:",
            Location = new System.Drawing.Point(12, 24),
            Size = new System.Drawing.Size(420, 20),
            AccessibleName = "Announcement mode"
        };

        _screenReaderRadio = new RadioButton
        {
            Text = "Screen Reader (NVDA, JAWS, etc.) - Recommended",
            Location = new System.Drawing.Point(12, 52),
            Size = new System.Drawing.Size(420, 25),
            AccessibleName = "Screen Reader Mode",
            AccessibleDescription = "Send announcements through your screen reader for natural speech integration",
        };

        _sapiRadio = new RadioButton
        {
            Text = "SAPI (Windows Speech) - Fallback",
            Location = new System.Drawing.Point(12, 80),
            Size = new System.Drawing.Size(420, 25),
            AccessibleName = "SAPI Mode",
            AccessibleDescription = "Use Windows built-in speech synthesis for announcements",
        };

        _statusLabel = new Label
        {
            Location = new System.Drawing.Point(12, 112),
            Size = new System.Drawing.Size(420, 40),
            AccessibleName = "Screen Reader Status",
            Text = "Checking screen reader status..."
        };

        var intervalLabel = new Label
        {
            Text = "Announce nearest city automatically:",
            Location = new System.Drawing.Point(12, 167),
            Size = new System.Drawing.Size(250, 20),
            AccessibleName = "Announce nearest city automatically label"
        };

        _nearestCityIntervalCombo = new ComboBox
        {
            Location = new System.Drawing.Point(270, 164),
            Size = new System.Drawing.Size(170, 25),
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

        // Time-of-day seconds toggle (controls Output Z / Output Shift+Z format).
        _timeWithSecondsCheck = new CheckBox
        {
            Text = "Include seconds in time announcements (Output Z / Shift+Z)",
            Location = new System.Drawing.Point(12, 207),
            Size = new System.Drawing.Size(420, 25),
            AccessibleName = "Include seconds in time announcements",
            AccessibleDescription = "When checked, the local-time and Zulu-time hotkeys speak hours, minutes, and seconds. Default is hours and minutes only."
        };

        // GSX background-monitoring toggle. When checked, GSX tooltip
        // updates are spoken even when the Access GSX window is closed.
        _gsxBackgroundMonitoring = new CheckBox
        {
            Text = "Announce GSX tooltips when Access GSX window is closed",
            Location = new System.Drawing.Point(12, 235),
            Size = new System.Drawing.Size(440, 30),
            AutoSize = false,
            AccessibleName = "Announce GSX tooltips in background",
            AccessibleDescription = "When checked, GSX tooltip updates (boarding, fuel, pushback) are read aloud by the screen reader even when the Access GSX window is hidden."
        };

        group.Controls.AddRange(new Control[]
        {
            modeLabel, _screenReaderRadio, _sapiRadio, _statusLabel,
            intervalLabel, _nearestCityIntervalCombo,
            _timeWithSecondsCheck,
            _gsxBackgroundMonitoring
        });

        return group;
    }

    private GroupBox BuildWeatherGroup()
    {
        var group = new GroupBox
        {
            Text = "Weather",
            Location = new System.Drawing.Point(12, 0),
            Size = new System.Drawing.Size(460, 230),
            AccessibleName = "Weather",
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
        60 => 1,
        120 => 2,
        300 => 3,
        600 => 4,
        900 => 5,
        1200 => 6,
        _ => 0
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

    public void LoadFrom(UserSettings settings)
    {
        var mode = Enum.TryParse(settings.AnnouncementMode, out AnnouncementMode parsed)
            ? parsed
            : AnnouncementMode.ScreenReader;
        _screenReaderRadio.Checked = mode == AnnouncementMode.ScreenReader;
        _sapiRadio.Checked = mode == AnnouncementMode.SAPI;

        _nearestCityIntervalCombo.SelectedIndex = IntervalToIndex(settings.NearestCityAnnouncementInterval);
        _timeWithSecondsCheck.Checked = settings.AnnounceTimeWithSeconds;
        _gsxBackgroundMonitoring.Checked = settings.GsxBackgroundMonitoring;

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
        settings.AnnouncementMode = (_screenReaderRadio.Checked
            ? AnnouncementMode.ScreenReader
            : AnnouncementMode.SAPI).ToString();
        settings.NearestCityAnnouncementInterval = IndexToInterval(_nearestCityIntervalCombo.SelectedIndex);
        settings.AnnounceTimeWithSeconds = _timeWithSecondsCheck.Checked;
        settings.GsxBackgroundMonitoring = _gsxBackgroundMonitoring.Checked;

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

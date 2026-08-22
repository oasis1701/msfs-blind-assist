using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>Announcements section of the unified Settings dialog. Extracted from the retired
/// standalone announcement-settings dialog — same controls, same AccessibleNames. The old
/// General/Weather inner TabControl was first replaced with two stacked GroupBoxes; the
/// Weather group has since moved to its own top-level Settings tab (WeatherPanel), leaving
/// this panel with only the General group.</summary>
public class AnnouncementsPanel : UserControl, ISettingsPanel
{
    // ── General group ────────────────────────────────────────────────────────
    private RadioButton _screenReaderRadio = null!;
    private RadioButton _sapiRadio = null!;
    private Label _statusLabel = null!;
    private ComboBox _nearestCityIntervalCombo = null!;
    private CheckBox _timeWithSecondsCheck = null!;
    private CheckBox _gsxBackgroundMonitoring = null!;

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

        Controls.Add(generalGroup);
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
    }

    public void OnLeaving()
    {
    }
}

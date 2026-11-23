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
    private TextBox _statusBox = null!;
    private ComboBox _nearestCityIntervalCombo = null!;
    private CheckBox _timeWithSecondsCheck = null!;
    private CheckBox _gsxBackgroundMonitoring = null!;
    private CheckBox _altitudeCalloutsCheck = null!;

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
            Size = new System.Drawing.Size(460, 316),
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

        // A read-only TextBox, deliberately NOT a Label: a Label is not in the tab order,
        // so with a screen reader it has to be hunted for with the review cursor.
        _statusBox = new TextBox
        {
            Location = new System.Drawing.Point(12, 112),
            // Three lines: the "ZDSR is running, but ..." status wraps onto a third.
            Size = new System.Drawing.Size(420, 56),
            Multiline = true,
            ReadOnly = true,
            // Explicit on purpose: a read-only TextBox ignores ForeColor until BackColor is set (WinForms
            // leaves WM_CTLCOLORSTATIC to the edit control otherwise), and the status colour below is lost.
            BackColor = SystemColors.Control,
            AccessibleName = "Screen Reader Status",
            Text = "Checking screen reader status..."
        };

        var intervalLabel = new Label
        {
            Text = "Announce nearest city automatically:",
            Location = new System.Drawing.Point(12, 183),
            Size = new System.Drawing.Size(250, 20),
            AccessibleName = "Announce nearest city automatically label"
        };

        _nearestCityIntervalCombo = new ComboBox
        {
            Location = new System.Drawing.Point(270, 180),
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
            Location = new System.Drawing.Point(12, 223),
            Size = new System.Drawing.Size(420, 25),
            AccessibleName = "Include seconds in time announcements",
            AccessibleDescription = "When checked, the local-time and Zulu-time hotkeys speak hours, minutes, and seconds. Default is hours and minutes only."
        };

        // GSX background-monitoring toggle. When checked, GSX tooltip
        // updates are spoken even when the Access GSX window is closed.
        _gsxBackgroundMonitoring = new CheckBox
        {
            Text = "Announce GSX tooltips when Access GSX window is closed",
            Location = new System.Drawing.Point(12, 251),
            Size = new System.Drawing.Size(440, 30),
            AutoSize = false,
            AccessibleName = "Announce GSX tooltips in background",
            AccessibleDescription = "When checked, GSX tooltip updates (boarding, fuel, pushback) are read aloud by the screen reader even when the Access GSX window is hidden."
        };

        // 1,000-foot altitude crossing call-outs ("5000", "6000", ...). This is their only
        // switch: the Ctrl+M monitor deliberately lists no row for INDICATED_ALTITUDE, because
        // that row could never reach the announcer (BaseAircraftDefinition explains why).
        _altitudeCalloutsCheck = new CheckBox
        {
            Text = "Announce 1,000-foot altitude crossings",
            Location = new System.Drawing.Point(12, 283),
            Size = new System.Drawing.Size(440, 25),
            AccessibleName = "Announce 1,000-foot altitude crossings",
            AccessibleDescription = "When checked, each thousand feet crossed in a climb or descent is spoken. Default is on."
        };

        group.Controls.AddRange(new Control[]
        {
            modeLabel, _screenReaderRadio, _sapiRadio, _statusBox,
            intervalLabel, _nearestCityIntervalCombo,
            _timeWithSecondsCheck,
            _gsxBackgroundMonitoring,
            _altitudeCalloutsCheck
        });

        return group;
    }

    private void UpdateScreenReaderStatus()
    {
        try
        {
            // The order the announcer tries them: NVDA, ZDSR, then what Tolk detects (JAWS and the rest).
            bool nvdaRunning;
            using (var nvdaTest = new NvdaControllerWrapper())
            {
                nvdaRunning = nvdaTest.IsRunning;
            }
            var zdsr = new ZdsrWrapper();
            bool zdsrRunning = !nvdaRunning && zdsr.IsRunning;

            // A wrapper of its own is safe: TolkWrapper counts its users, so disposing this one does not
            // unload the Tolk session the announcer speaks through.
            using (var tolkTest = new TolkWrapper())
            {
                bool tolkLoaded = tolkTest.Initialize();
                string? tolkReader = tolkLoaded && tolkTest.IsScreenReaderRunning() ? tolkTest.DetectedScreenReader : null;
                string? detected = ScreenReaderAnnouncer.ActiveScreenReaderName(nvdaRunning, zdsrRunning, tolkReader);
                // A ZDSR that is running but cannot be spoken through (not licensed, too old, its API refused)
                // is named with its reason, never reported as "No screen reader detected".
                string? zdsrProblem = detected == null && !nvdaRunning ? zdsr.UnavailableReason : null;

                _statusBox.Text = ScreenReaderAnnouncer.DetectionStatusText(detected, zdsrProblem, tolkLoaded);
                _statusBox.ForeColor = detected != null ? Color.DarkGreen
                    : zdsrProblem != null || tolkLoaded ? Color.DarkOrange
                    : Color.Red;
            }
        }
        catch (Exception ex)
        {
            _statusBox.Text = $"Error checking screen reader: {ex.Message}{Environment.NewLine}SAPI mode recommended.";
            _statusBox.ForeColor = Color.Red;
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
        _altitudeCalloutsCheck.Checked = settings.AltitudeCalloutsEnabled;
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
        settings.AltitudeCalloutsEnabled = _altitudeCalloutsCheck.Checked;
    }

    public void OnLeaving()
    {
    }
}

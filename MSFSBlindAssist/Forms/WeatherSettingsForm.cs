using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms;

/// <summary>
/// Settings form for weather auto-announcement options.
/// </summary>
public class WeatherSettingsForm : Form
{
    private CheckBox _autoAnnounceCheckBox = null!;
    private CheckBox _sigmetProximityCheckBox = null!;
    private Label _rangeLabel = null!;
    private NumericUpDown _rangeUpDown = null!;
    private Button _okButton = null!;
    private Button _cancelButton = null!;

    public bool WeatherAutoAnnounceEnabled { get; private set; }
    public bool SigmetProximityAlertsEnabled { get; private set; }
    public int SigmetProximityRangeNm { get; private set; }

    public WeatherSettingsForm(bool autoAnnounce, bool sigmetAlerts, int sigmetRangeNm)
    {
        WeatherAutoAnnounceEnabled = autoAnnounce;
        SigmetProximityAlertsEnabled = sigmetAlerts;
        SigmetProximityRangeNm = sigmetRangeNm;
        InitializeComponent();
        SetupAccessibility();
    }

    private void InitializeComponent()
    {
        Text = "Weather Settings";
        Size = new Size(420, 250);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        // Auto-announce ambient weather
        _autoAnnounceCheckBox = new CheckBox
        {
            Text = "Auto-announce &weather state changes",
            Location = new Point(16, 20),
            Size = new Size(380, 24),
            Checked = WeatherAutoAnnounceEnabled,
            AccessibleName = "Auto-announce weather state changes",
            AccessibleDescription = "Automatically announce when entering or leaving clouds and when precipitation starts or stops"
        };

        // SIGMET proximity alerts
        _sigmetProximityCheckBox = new CheckBox
        {
            Text = "Auto-announce approaching &SIGMETs and AIRMETs",
            Location = new Point(16, 56),
            Size = new Size(380, 24),
            Checked = SigmetProximityAlertsEnabled,
            AccessibleName = "Auto-announce approaching SIGMETs and AIRMETs",
            AccessibleDescription = "Announce when the aircraft enters the proximity range of an active SIGMET or AIRMET"
        };
        _sigmetProximityCheckBox.CheckedChanged += (s, e) => _rangeUpDown.Enabled = _sigmetProximityCheckBox.Checked;

        // Proximity range
        _rangeLabel = new Label
        {
            Text = "&Proximity range (nautical miles):",
            Location = new Point(16, 96),
            Size = new Size(250, 20),
            AccessibleName = "Proximity range label"
        };

        _rangeUpDown = new NumericUpDown
        {
            Location = new Point(270, 92),
            Size = new Size(80, 24),
            Minimum = 10,
            Maximum = 500,
            Value = Math.Clamp(SigmetProximityRangeNm, 10, 500),
            Enabled = SigmetProximityAlertsEnabled,
            AccessibleName = "Proximity range in nautical miles",
            AccessibleDescription = "Distance at which to announce approaching SIGMETs and AIRMETs"
        };

        // Buttons
        _okButton = new Button
        {
            Text = "&OK",
            Location = new Point(220, 170),
            Size = new Size(80, 28),
            DialogResult = DialogResult.OK,
            AccessibleName = "OK",
            AccessibleDescription = "Save weather settings"
        };
        _okButton.Click += OkButton_Click;

        _cancelButton = new Button
        {
            Text = "&Cancel",
            Location = new Point(312, 170),
            Size = new Size(80, 28),
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
            AccessibleDescription = "Cancel without saving"
        };

        Controls.AddRange(new Control[]
        {
            _autoAnnounceCheckBox, _sigmetProximityCheckBox,
            _rangeLabel, _rangeUpDown,
            _okButton, _cancelButton
        });

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    private void SetupAccessibility()
    {
        _autoAnnounceCheckBox.TabIndex = 0;
        _sigmetProximityCheckBox.TabIndex = 1;
        _rangeUpDown.TabIndex = 2;
        _okButton.TabIndex = 3;
        _cancelButton.TabIndex = 4;

        Load += (s, e) => _autoAnnounceCheckBox.Focus();
    }

    private void OkButton_Click(object? sender, EventArgs e)
    {
        WeatherAutoAnnounceEnabled = _autoAnnounceCheckBox.Checked;
        SigmetProximityAlertsEnabled = _sigmetProximityCheckBox.Checked;
        SigmetProximityRangeNm = (int)_rangeUpDown.Value;
    }
}

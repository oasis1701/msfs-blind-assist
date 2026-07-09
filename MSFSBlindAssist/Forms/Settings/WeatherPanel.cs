using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Forms.Settings;

/// <summary>Weather section of the unified Settings dialog: the ActiveSky master switch
/// plus the weather auto-announcement toggles (moved here from AnnouncementsPanel so all
/// weather behavior lives on one tab). The ActiveSky switch is the ONLY control that
/// enables AS integration — default off; when off, ActiveSkyClient.IsRunningAsync
/// short-circuits and no AS probing runs anywhere (see the CLAUDE.md weather invariant).</summary>
public class WeatherPanel : UserControl, ISettingsPanel
{
    private CheckBox _activeSkyEnabled = null!;

    private CheckBox _weatherAutoAnnounce = null!;
    private ComboBox _weatherIntervalCombo = null!;
    private CheckBox _sigmetAlerts = null!;
    private CheckBox _pirepAlerts = null!;
    private NumericUpDown _proximityRange = null!;

    /// <summary>Combo entries: minutes (0 = AS download interval, no extra throttle).</summary>
    private static readonly int[] IntervalChoicesMinutes = { 0, 5, 10, 15, 20, 30, 45, 60 };

    public string TabTitle => "Weather";

    public WeatherPanel()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AutoScroll = true;

        var activeSkyGroup = BuildActiveSkyGroup();
        var announceGroup = BuildAnnouncementsGroup();
        announceGroup.Location = new System.Drawing.Point(12, activeSkyGroup.Bottom + 12);

        Controls.Add(activeSkyGroup);
        Controls.Add(announceGroup);
    }

    private GroupBox BuildActiveSkyGroup()
    {
        var group = new GroupBox
        {
            Text = "ActiveSky (HiFi)",
            Location = new System.Drawing.Point(12, 12),
            Size = new System.Drawing.Size(460, 96),
            AccessibleName = "ActiveSky",
            AccessibleDescription = "HiFi ActiveSky weather engine integration",
        };

        _activeSkyEnabled = new CheckBox
        {
            Text = "Enable &ActiveSky integration",
            Location = new System.Drawing.Point(12, 24),
            Size = new System.Drawing.Size(430, 48),
            AutoSize = false,
            CheckAlign = System.Drawing.ContentAlignment.TopLeft,
            TextAlign = System.Drawing.ContentAlignment.TopLeft,
            AccessibleName = "Enable ActiveSky integration",
            AccessibleDescription = "Requires HiFi ActiveSky running on this PC. When enabled, wind, gusts, "
                + "precipitation and decoded station weather come from ActiveSky instead of the sim's own "
                + "weather engine. Leave off if you don't use ActiveSky."
        };

        group.Controls.Add(_activeSkyEnabled);
        return group;
    }

    private GroupBox BuildAnnouncementsGroup()
    {
        var group = new GroupBox
        {
            Text = "Announcements",
            Location = new System.Drawing.Point(12, 0),
            Size = new System.Drawing.Size(460, 230),
            AccessibleName = "Weather announcements",
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

    public void LoadFrom(UserSettings settings)
    {
        _activeSkyEnabled.Checked = settings.ActiveSkyEnabled;

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
        settings.ActiveSkyEnabled = _activeSkyEnabled.Checked;

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

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
    private CheckBox _announceTurbulence = null!;
    private CheckBox _announceIcing = null!;
    private ComboBox _weatherIntervalCombo = null!;
    private Label _weatherIntervalLabel = null!;
    private CheckBox _sigmetAlerts = null!;
    private CheckBox _pirepAlerts = null!;
    private CheckBox _routeAdvisoryAlerts = null!;
    private NumericUpDown _proximityRange = null!;
    private Label _routeAdvisoryDistanceLabel = null!;
    private NumericUpDown _routeAdvisoryDistance = null!;

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

        // The announcement interval throttles the ActiveSky decoded-weather monitor,
        // and nothing else — WeatherAutoAnnounceIntervalMinutes is read in exactly one
        // place in the codebase, where it becomes ActiveSkyWeatherMonitor.IntervalMinutes.
        // That monitor runs only when BOTH switches are on (ActiveSkyWeatherMonitor.
        // ShouldRun), so the combo is hidden — and out of the tab order — otherwise: a
        // blind user tabbing the panel shouldn't meet a setting that governs nothing.
        // Hiding never resets the stored value (ApplyTo reads the combo regardless).
        _activeSkyEnabled.CheckedChanged += (_, _) => UpdateActiveSkyDependentVisibility();
        _weatherAutoAnnounce.CheckedChanged += (_, _) => UpdateActiveSkyDependentVisibility();
        UpdateActiveSkyDependentVisibility();
    }

    /// <summary>Defers to ActiveSkyWeatherMonitor.ShouldRun — the single source of truth
    /// for whether the monitor (and therefore its interval) is live. The panel has no
    /// UserSettings at CheckedChanged time, so it builds a throwaway one from the live
    /// checkbox state; one allocation per toggle is free at human interaction rates, and
    /// it means the rule can never drift between the settings UI and the monitor.</summary>
    private void UpdateActiveSkyDependentVisibility()
    {
        bool on = Services.ActiveSkyWeatherMonitor.ShouldRun(new UserSettings
        {
            ActiveSkyEnabled = _activeSkyEnabled.Checked,
            WeatherAutoAnnounceEnabled = _weatherAutoAnnounce.Checked
        });
        _weatherIntervalLabel.Visible = on;
        _weatherIntervalCombo.Visible = on;

        // Hazard announcers: both ride the master auto-announce; turbulence is
        // AS-sourced so it additionally needs the AS switch. Hiding never resets
        // the stored value (ApplyTo reads the checkboxes regardless).
        bool master = _weatherAutoAnnounce.Checked;
        _announceTurbulence.Visible = master && _activeSkyEnabled.Checked;
        _announceIcing.Visible = master;

        // Route advisories are a proximity-alert sibling (SIGMET/PIREP), independent
        // of the auto-announce master — only ActiveSky itself gates visibility.
        _routeAdvisoryAlerts.Visible = _activeSkyEnabled.Checked;

        // The distance row is route-advisory-only (it configures nothing else), so it is
        // gated exactly like the route-advisory checkbox itself. Hiding never resets the
        // stored value — LoadFrom/ApplyTo read the control regardless of visibility.
        _routeAdvisoryDistanceLabel.Visible = _activeSkyEnabled.Checked;
        _routeAdvisoryDistance.Visible = _activeSkyEnabled.Checked;
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
            Size = new System.Drawing.Size(460, 378),
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

        _announceTurbulence = new CheckBox
        {
            Text = "Announce &turbulence changes",
            Location = new System.Drawing.Point(12, 60),
            Size = new System.Drawing.Size(420, 24),
            AccessibleName = "Announce turbulence changes",
            AccessibleDescription = "Announce entering, worsening, easing and smooth-air turbulence "
                + "transitions from ActiveSky. Applies only while auto-announce weather is enabled; "
                + "requires ActiveSky."
        };

        _announceIcing = new CheckBox
        {
            Text = "Announce icin&g",
            Location = new System.Drawing.Point(12, 96),
            Size = new System.Drawing.Size(420, 24),
            AccessibleName = "Announce icing",
            AccessibleDescription = "Announce when ice starts accumulating on the airframe and when "
                + "it clears. Applies only while auto-announce weather is enabled."
        };

        _sigmetAlerts = new CheckBox
        {
            Text = "Auto-announce approaching &SIGMETs and AIRMETs",
            Location = new System.Drawing.Point(12, 132),
            Size = new System.Drawing.Size(420, 24),
            AccessibleName = "Auto-announce approaching SIGMETs and AIRMETs",
            AccessibleDescription = "Announce when the aircraft enters the proximity range of an active SIGMET or AIRMET"
        };

        _pirepAlerts = new CheckBox
        {
            Text = "Auto-announce approaching pilot repo&rts (PIREPs)",
            Location = new System.Drawing.Point(12, 168),
            Size = new System.Drawing.Size(420, 24),
            AccessibleName = "Auto-announce approaching PIREPs",
            AccessibleDescription = "Announce when the aircraft enters the proximity range of a significant pilot report of turbulence or icing"
        };

        _routeAdvisoryAlerts = new CheckBox
        {
            Text = "Announce r&oute advisories by proximity (ActiveSky)",
            Location = new System.Drawing.Point(12, 204),
            Size = new System.Drawing.Size(420, 24),
            AccessibleName = "Announce route advisories by proximity",
            AccessibleDescription = "Announce SIGMETs and AIRMETs on the ActiveSky flight-plan route when approaching within the distance set below, when entering the area, and when leaving it. Requires ActiveSky."
        };

        var rangeLabel = new Label
        {
            Text = "&Proximity range (nautical miles):",
            Location = new System.Drawing.Point(12, 246),
            Size = new System.Drawing.Size(250, 20),
            AccessibleName = "Proximity range label"
        };

        _proximityRange = new NumericUpDown
        {
            Location = new System.Drawing.Point(270, 242),
            Size = new System.Drawing.Size(80, 24),
            Minimum = 10,
            Maximum = 500,
            AccessibleName = "Proximity range in nautical miles",
            AccessibleDescription = "Distance at which to announce approaching SIGMETs, AIRMETs, and PIREPs"
        };

        // Route-advisory approach ring: a sibling of the SIGMET/PIREP proximity range above,
        // but a SEPARATE setting (RouteAdvisoryProximityNm) — deliberately independent so
        // tuning one never silently moves the other. Route-advisory-only, so it is gated the
        // same way as _routeAdvisoryAlerts (see UpdateActiveSkyDependentVisibility).
        _routeAdvisoryDistanceLabel = new Label
        {
            Text = "&En-route advisory distance (nautical miles):",
            Location = new System.Drawing.Point(12, 286),
            Size = new System.Drawing.Size(300, 20),
            AccessibleName = "En-route advisory distance label"
        };

        _routeAdvisoryDistance = new NumericUpDown
        {
            Location = new System.Drawing.Point(310, 282),
            Size = new System.Drawing.Size(80, 24),
            Minimum = 10,
            Maximum = 500,
            AccessibleName = "En-route advisory distance in nautical miles",
            AccessibleDescription = "Distance at which a SIGMET or AIRMET on the ActiveSky flight-plan route announces its approach. Entering and leaving the area always announce regardless of this distance."
        };

        _weatherIntervalLabel = new Label
        {
            Text = "Weather announcement &interval:",
            Location = new System.Drawing.Point(12, 326),
            Size = new System.Drawing.Size(250, 20),
            AccessibleName = "Weather announcement interval label"
        };

        _weatherIntervalCombo = new ComboBox
        {
            Location = new System.Drawing.Point(12, 350),
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
            _weatherAutoAnnounce, _announceTurbulence, _announceIcing, _sigmetAlerts, _pirepAlerts,
            _routeAdvisoryAlerts,
            rangeLabel, _proximityRange,
            _routeAdvisoryDistanceLabel, _routeAdvisoryDistance,
            _weatherIntervalLabel, _weatherIntervalCombo
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
        _announceTurbulence.Checked = settings.AnnounceTurbulenceEnabled;
        _announceIcing.Checked = settings.AnnounceIcingEnabled;
        _sigmetAlerts.Checked = settings.SigmetProximityAlertsEnabled;
        _pirepAlerts.Checked = settings.PirepProximityAlertsEnabled;
        _routeAdvisoryAlerts.Checked = settings.AnnounceRouteAdvisoriesEnabled;
        _proximityRange.Value = Math.Clamp(settings.SigmetProximityRangeNm, 10, 500);
        _routeAdvisoryDistance.Value = Math.Clamp(settings.RouteAdvisoryProximityNm, 10, 500);
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
        settings.AnnounceTurbulenceEnabled = _announceTurbulence.Checked;
        settings.AnnounceIcingEnabled = _announceIcing.Checked;
        settings.WeatherAutoAnnounceIntervalMinutes = IntervalChoicesMinutes[
            Math.Clamp(_weatherIntervalCombo.SelectedIndex, 0, IntervalChoicesMinutes.Length - 1)];
        settings.SigmetProximityAlertsEnabled = _sigmetAlerts.Checked;
        settings.PirepProximityAlertsEnabled = _pirepAlerts.Checked;
        settings.AnnounceRouteAdvisoriesEnabled = _routeAdvisoryAlerts.Checked;
        settings.SigmetProximityRangeNm = (int)_proximityRange.Value;
        settings.RouteAdvisoryProximityNm = (int)_routeAdvisoryDistance.Value;
    }

    public void OnLeaving()
    {
    }
}

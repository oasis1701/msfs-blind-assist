using System.Windows.Forms;
using MSFSBlindAssist.Forms.Settings;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// LoadFrom/ApplyTo round-trip for the Weather settings tab. The panel is a plain
/// UserControl whose controls are readable/writable without a message pump, so it
/// can be exercised directly. The panel is pure over its LoadFrom/ApplyTo arguments —
/// it never reads the process-global SettingsManager.Current and never probes
/// ActiveSky (the 2026-07 status line that did was removed on review), so these
/// tests are hermetic without any shared-state collection.
/// </summary>
public class WeatherPanelTests
{
    [Fact]
    public void RoundTrip_PreservesAllWeatherSettings()
    {
        var source = new UserSettings
        {
            ActiveSkyEnabled = true,
            WeatherAutoAnnounceEnabled = true,
            WeatherAutoAnnounceIntervalMinutes = 15,
            SigmetProximityAlertsEnabled = true,
            PirepProximityAlertsEnabled = true,
            SigmetProximityRangeNm = 250
        };

        using var panel = new WeatherPanel();
        panel.LoadFrom(source);
        var target = new UserSettings();
        panel.ApplyTo(target);

        Assert.True(target.ActiveSkyEnabled);
        Assert.True(target.WeatherAutoAnnounceEnabled);
        Assert.Equal(15, target.WeatherAutoAnnounceIntervalMinutes);
        Assert.True(target.SigmetProximityAlertsEnabled);
        Assert.True(target.PirepProximityAlertsEnabled);
        Assert.Equal(250, target.SigmetProximityRangeNm);
    }

    [Fact]
    public void Defaults_RoundTripToDefaults()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());
        var target = new UserSettings { ActiveSkyEnabled = true }; // must be overwritten to false
        panel.ApplyTo(target);

        Assert.False(target.ActiveSkyEnabled);
        Assert.False(target.WeatherAutoAnnounceEnabled);
        Assert.Equal(0, target.WeatherAutoAnnounceIntervalMinutes);
    }

    [Fact]
    public void TabTitle_IsWeather()
    {
        using var panel = new WeatherPanel();
        Assert.Equal("Weather", panel.TabTitle);
    }

    // ---- Interval visibility: the announcement interval throttles ACTIVESKY decoded-weather
    // announcements only, so it (and its label) must be hidden while the AS switch is off —
    // a blind user tabbing the panel should not meet an AS-specific setting they can't use.
    // Controls are located by AccessibleName (their NVDA identity), not private fields.

    private static Control FindByAccessibleName(Control root, string accessibleName)
    {
        foreach (Control child in root.Controls)
        {
            if (child.AccessibleName == accessibleName) return child;
            if (FindByAccessibleName(child, accessibleName) is { } nested) return nested;
        }
        return null!;
    }

    [Fact]
    public void IntervalSetting_HiddenWhileActiveSkyDisabled()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());   // ActiveSkyEnabled defaults false

        Assert.False(FindByAccessibleName(panel, "Weather announcement interval").Visible);
        Assert.False(FindByAccessibleName(panel, "Weather announcement interval label").Visible);
    }

    [Fact]
    public void IntervalSetting_VisibleWhenBothActiveSkyAndAutoAnnounceEnabled()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = true, WeatherAutoAnnounceEnabled = true });

        Assert.True(FindByAccessibleName(panel, "Weather announcement interval").Visible);
        Assert.True(FindByAccessibleName(panel, "Weather announcement interval label").Visible);
    }

    [Fact]
    public void IntervalSetting_FollowsCheckboxToggle_Live()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { WeatherAutoAnnounceEnabled = true });
        var checkbox = (CheckBox)FindByAccessibleName(panel, "Enable ActiveSky integration");
        var combo = FindByAccessibleName(panel, "Weather announcement interval");

        checkbox.Checked = true;
        Assert.True(combo.Visible);
        checkbox.Checked = false;
        Assert.False(combo.Visible);
    }

    [Fact]
    public void HiddenInterval_StillRoundTripsItsValue()
    {
        // Hiding must never reset the stored value: a user who set 15 min, then turns
        // AS off and back on, keeps their interval.
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = false, WeatherAutoAnnounceIntervalMinutes = 15 });
        var target = new UserSettings();
        panel.ApplyTo(target);

        Assert.Equal(15, target.WeatherAutoAnnounceIntervalMinutes);
    }

    // ---- After the monitor decoupling, the interval throttles the decoded-weather
    // monitor, which runs only when BOTH the ActiveSky switch and the auto-announce
    // checkbox are on. So the combo must be visible under exactly that condition --
    // it governs nothing otherwise.

    [Fact]
    public void IntervalSetting_HiddenWhenActiveSkyOnButAutoAnnounceOff()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = true, WeatherAutoAnnounceEnabled = false });

        Assert.False(FindByAccessibleName(panel, "Weather announcement interval").Visible);
        Assert.False(FindByAccessibleName(panel, "Weather announcement interval label").Visible);
    }

    [Fact]
    public void IntervalSetting_HiddenWhenAutoAnnounceOnButActiveSkyOff()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = false, WeatherAutoAnnounceEnabled = true });

        Assert.False(FindByAccessibleName(panel, "Weather announcement interval").Visible);
    }

    [Fact]
    public void IntervalSetting_VisibilityFollowsEitherCheckbox_Live()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = true, WeatherAutoAnnounceEnabled = true });
        var activeSky = (CheckBox)FindByAccessibleName(panel, "Enable ActiveSky integration");
        var autoAnnounce = (CheckBox)FindByAccessibleName(panel, "Auto-announce weather state changes");
        var combo = FindByAccessibleName(panel, "Weather announcement interval");

        Assert.True(combo.Visible);
        autoAnnounce.Checked = false;
        Assert.False(combo.Visible);
        autoAnnounce.Checked = true;
        Assert.True(combo.Visible);
        activeSky.Checked = false;
        Assert.False(combo.Visible);
    }

    [Fact]
    public void RoundTrip_PreservesHazardAnnouncementSettings()
    {
        var source = new UserSettings
        {
            AnnounceTurbulenceEnabled = false,
            AnnounceIcingEnabled = false,
        };

        using var panel = new WeatherPanel();
        panel.LoadFrom(source);
        var target = new UserSettings();
        panel.ApplyTo(target);

        Assert.False(target.AnnounceTurbulenceEnabled);
        Assert.False(target.AnnounceIcingEnabled);
    }

    [Fact]
    public void HazardDefaults_AreOn()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());
        var target = new UserSettings { AnnounceTurbulenceEnabled = false, AnnounceIcingEnabled = false };
        panel.ApplyTo(target);

        Assert.True(target.AnnounceTurbulenceEnabled);
        Assert.True(target.AnnounceIcingEnabled);
    }

    [Fact]
    public void Turbulence_checkbox_needs_master_and_activesky_icing_needs_master_only()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());            // both master switches off

        var turb = FindByAccessibleName(panel, "Announce turbulence changes");
        var icing = FindByAccessibleName(panel, "Announce icing");
        var master = (CheckBox)FindByAccessibleName(panel, "Auto-announce weather state changes");
        var asSwitch = (CheckBox)FindByAccessibleName(panel, "Enable ActiveSky integration");

        Assert.False(turb.Visible);
        Assert.False(icing.Visible);

        master.Checked = true;                          // master only
        Assert.False(turb.Visible);                     // turbulence still needs AS
        Assert.True(icing.Visible);

        asSwitch.Checked = true;                        // master + AS
        Assert.True(turb.Visible);
        Assert.True(icing.Visible);

        master.Checked = false;                         // master off hides both
        Assert.False(turb.Visible);
        Assert.False(icing.Visible);
    }

    [Fact]
    public void Hiding_hazard_checkboxes_never_resets_their_values()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());             // defaults: both true, both hidden

        var target = new UserSettings { AnnounceTurbulenceEnabled = false, AnnounceIcingEnabled = false };
        panel.ApplyTo(target);                          // hidden ≠ unchecked

        Assert.True(target.AnnounceTurbulenceEnabled);
        Assert.True(target.AnnounceIcingEnabled);
    }

    [Fact]
    public void RouteAdvisories_setting_roundtrips_and_defaults_on()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { AnnounceRouteAdvisoriesEnabled = false });
        var target = new UserSettings();
        panel.ApplyTo(target);
        Assert.False(target.AnnounceRouteAdvisoriesEnabled);

        panel.LoadFrom(new UserSettings());
        panel.ApplyTo(target);
        Assert.True(target.AnnounceRouteAdvisoriesEnabled);      // default on
    }

    [Fact]
    public void RouteAdvisories_checkbox_needs_activesky_only()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());                       // AS off, master off

        var route = FindByAccessibleName(panel, "Announce route advisories by proximity");
        var asSwitch = (CheckBox)FindByAccessibleName(panel, "Enable ActiveSky integration");
        var master = (CheckBox)FindByAccessibleName(panel, "Auto-announce weather state changes");

        Assert.False(route.Visible);
        asSwitch.Checked = true;                                  // AS alone suffices
        Assert.True(route.Visible);
        master.Checked = true;                                    // master irrelevant
        Assert.True(route.Visible);
        asSwitch.Checked = false;
        Assert.False(route.Visible);
    }

    // ---- RouteAdvisoryProximityNm: a SEPARATE setting from SigmetProximityRangeNm (never
    // fold them together), defaulting to 100, round-tripped like its sibling _proximityRange,
    // and gated on ActiveSky exactly like the route-advisory checkbox it sits under (it
    // configures nothing else).

    [Fact]
    public void RouteAdvisoryDistance_roundtrips_independently_of_sigmet_range()
    {
        var source = new UserSettings
        {
            SigmetProximityRangeNm = 250,
            RouteAdvisoryProximityNm = 75
        };

        using var panel = new WeatherPanel();
        panel.LoadFrom(source);
        var target = new UserSettings();
        panel.ApplyTo(target);

        Assert.Equal(250, target.SigmetProximityRangeNm);
        Assert.Equal(75, target.RouteAdvisoryProximityNm);       // independent value, not clobbered by the sibling
    }

    [Fact]
    public void RouteAdvisoryDistance_defaults_to_100()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());
        var target = new UserSettings { RouteAdvisoryProximityNm = 999 };
        panel.ApplyTo(target);

        Assert.Equal(100, target.RouteAdvisoryProximityNm);
    }

    [Fact]
    public void RouteAdvisoryDistance_needs_activesky_only_same_as_its_checkbox()
    {
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings());                       // AS off, master off

        var label = FindByAccessibleName(panel, "En-route advisory distance label");
        var numeric = FindByAccessibleName(panel, "En-route advisory distance in nautical miles");
        var asSwitch = (CheckBox)FindByAccessibleName(panel, "Enable ActiveSky integration");
        var master = (CheckBox)FindByAccessibleName(panel, "Auto-announce weather state changes");

        Assert.False(label.Visible);
        Assert.False(numeric.Visible);
        asSwitch.Checked = true;                                  // AS alone suffices
        Assert.True(label.Visible);
        Assert.True(numeric.Visible);
        master.Checked = true;                                    // master irrelevant
        Assert.True(numeric.Visible);
        asSwitch.Checked = false;
        Assert.False(label.Visible);
        Assert.False(numeric.Visible);
    }

    [Fact]
    public void HiddenRouteAdvisoryDistance_StillRoundTripsItsValue()
    {
        // Hiding must never reset the stored value (same pattern as the interval combo above).
        using var panel = new WeatherPanel();
        panel.LoadFrom(new UserSettings { ActiveSkyEnabled = false, RouteAdvisoryProximityNm = 65 });
        var target = new UserSettings();
        panel.ApplyTo(target);

        Assert.Equal(65, target.RouteAdvisoryProximityNm);
    }
}

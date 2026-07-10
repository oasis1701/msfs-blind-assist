using System.Windows.Forms;
using MSFSBlindAssist.Forms.Settings;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// LoadFrom/ApplyTo round-trip for the Weather settings tab. The panel is a plain
/// UserControl whose controls are readable/writable without a message pump, so it
/// can be exercised directly. No SettingsManager access — a local UserSettings is
/// passed in — so no collection needed.
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
}

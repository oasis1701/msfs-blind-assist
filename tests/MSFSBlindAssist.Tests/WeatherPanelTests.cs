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
}

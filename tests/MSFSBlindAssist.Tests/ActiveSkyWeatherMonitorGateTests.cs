using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The decoded-weather monitor is BOTH an ActiveSky feature (it reads only the AS HTTP
/// API — no SimConnect fallback) and an announcement feature (it speaks). It may run
/// only when the user has opted into both. Before this gate, enabling ActiveSky purely
/// for accurate output+I wind and radar data also, unavoidably, started the speech.
///
/// Pure static over a local UserSettings — no SettingsManager, no WinForms pump, so no
/// [Collection] needed.
/// </summary>
public class ActiveSkyWeatherMonitorGateTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true,  false, false)]  // AS on, announce off: wind/radar work, speech silent
    [InlineData(false, true,  false)]  // announce on, AS off: monitor has no data source
    [InlineData(true,  true,  true)]
    public void ShouldRun_RequiresBothFlags(bool activeSky, bool autoAnnounce, bool expected)
    {
        var settings = new UserSettings
        {
            ActiveSkyEnabled = activeSky,
            WeatherAutoAnnounceEnabled = autoAnnounce
        };

        Assert.Equal(expected, ActiveSkyWeatherMonitor.ShouldRun(settings));
    }

    [Fact]
    public void ShouldRun_DefaultSettings_IsFalse()
    {
        Assert.False(ActiveSkyWeatherMonitor.ShouldRun(new UserSettings()));
    }
}

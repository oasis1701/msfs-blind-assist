using System.Diagnostics;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The ActiveSky master switch (UserSettings.ActiveSkyEnabled, default OFF) must make
/// ActiveSkyClient.IsRunningAsync() short-circuit false with NO network probe — the
/// probe has a ~1.2 s floor when AS is absent, which every non-AS user paid on the
/// output+I hotkey before the switch existed. Reads process-global SettingsManager →
/// shared no-parallelism collection + restore in Dispose.
/// </summary>
[Collection("SettingsManagerGlobalState")]
public class ActiveSkyGateTests : IDisposable
{
    private readonly bool savedEnabled;

    public ActiveSkyGateTests()
    {
        savedEnabled = SettingsManager.Current.ActiveSkyEnabled;
    }

    public void Dispose() => SettingsManager.Current.ActiveSkyEnabled = savedEnabled;

    [Fact]
    public void ActiveSkyEnabled_DefaultsToFalse()
    {
        Assert.False(new UserSettings().ActiveSkyEnabled);
    }

    [Fact]
    public async Task IsRunningAsync_WhenDisabled_ShortCircuitsFalse_WithoutProbing()
    {
        SettingsManager.Current.ActiveSkyEnabled = false;
        var client = new ActiveSkyClient();
        var sw = Stopwatch.StartNew();
        bool running = await client.IsRunningAsync();
        sw.Stop();
        Assert.False(running);
        Assert.Equal("disabled in settings", client.LastStatus);
        // A real probe has a 1.2 s timeout floor; the short-circuit must be instant.
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"expected instant short-circuit, took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task IsRunningAsync_WhenDisabled_ClearsCachedPort()
    {
        // The enable -> discover -> disable leak: a port discovered while the switch was
        // on must not survive turning it off, or the per-method guards would be the only
        // thing standing between a disabled integration and live HTTP traffic.
        var client = new ActiveSkyClient { LastSuccessfulPort = 19285 };
        SettingsManager.Current.ActiveSkyEnabled = false;

        Assert.False(await client.IsRunningAsync());
        Assert.Null(client.LastSuccessfulPort);
        Assert.Equal("disabled in settings", client.LastStatus);
    }

    [Fact]
    public async Task GetCurrentConditions_WhenDisabled_ReturnsNull_EvenWithCachedPort()
    {
        // Seed a port FIRST: without it this asserts only the pre-existing
        // `if (LastSuccessfulPort is not int port) return null;` fallthrough and says
        // nothing about the disabled state.
        //
        // HONEST LIMIT: this pins the observable CONTRACT (disabled + cached port =>
        // null), not the mechanism. It cannot distinguish the master-switch guard from
        // an attempted HTTP call that failed -- `_http` is a private static HttpClient
        // with no injection seam, so there is no way to observe "no I/O happened" from a
        // unit test. The airtight guarantee is IsRunningAsync_WhenDisabled_ClearsCachedPort
        // below: with no cached port, the per-method guards have nothing to guard against.
        // The guards remain worth keeping as defense in depth for a future call site.
        var client = new ActiveSkyClient { LastSuccessfulPort = 19285 };
        SettingsManager.Current.ActiveSkyEnabled = false;

        Assert.Null(await client.GetCurrentConditionsAsync());
    }

    // Defense-in-depth pins for the four newer AS endpoints — same contract-only limit
    // documented above applies (can't observe "no I/O happened" without an injection seam).

    [Fact]
    public async Task GetAtmosphere_WhenDisabled_ReturnsNull_EvenWithCachedPort()
    {
        var client = new ActiveSkyClient { LastSuccessfulPort = 19285 };
        SettingsManager.Current.ActiveSkyEnabled = false;

        Assert.Null(await client.GetAtmosphereAsync(1, 2, new[] { 1000 }));
    }

    [Fact]
    public async Task GetWeatherInfoXml_WhenDisabled_ReturnsNull_EvenWithCachedPort()
    {
        var client = new ActiveSkyClient { LastSuccessfulPort = 19285 };
        SettingsManager.Current.ActiveSkyEnabled = false;

        Assert.Null(await client.GetWeatherInfoXmlAsync(1, 2));
    }

    [Fact]
    public async Task GetRouteAdvisoriesText_WhenDisabled_ReturnsNull_EvenWithCachedPort()
    {
        var client = new ActiveSkyClient { LastSuccessfulPort = 19285 };
        SettingsManager.Current.ActiveSkyEnabled = false;

        Assert.Null(await client.GetRouteAdvisoriesTextAsync());
    }

    [Fact]
    public async Task GetPositionalAdvisoriesText_WhenDisabled_ReturnsNull_EvenWithCachedPort()
    {
        var client = new ActiveSkyClient { LastSuccessfulPort = 19285 };
        SettingsManager.Current.ActiveSkyEnabled = false;

        Assert.Null(await client.GetPositionalAdvisoriesTextAsync(1, 2));
    }

    [Fact]
    public async Task IsRunningAsync_WhenDisabled_ClearsModeText()
    {
        // The same enable -> discover -> disable leak as LastSuccessfulPort, but for the
        // /GetMode body text: a mode string captured while the switch was on must not
        // survive turning it off, or the UI could keep showing a stale "Live Real time
        // mode (Active)" readout after AS integration has been disabled.
        var client = new ActiveSkyClient
        {
            LastSuccessfulPort = 19285,
            LastModeText = "Live Real time mode (Active) (2026/7/13 2013z)",
        };
        SettingsManager.Current.ActiveSkyEnabled = false;

        Assert.False(await client.IsRunningAsync());
        Assert.Null(client.LastModeText);
    }
}

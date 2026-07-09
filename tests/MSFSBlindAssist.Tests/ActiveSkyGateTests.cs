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
    public async Task GetCurrentConditions_WhenDisabled_ReturnsNull()
    {
        SettingsManager.Current.ActiveSkyEnabled = false;
        var client = new ActiveSkyClient();
        // No port was ever discovered (and none may be while disabled).
        Assert.Null(await client.GetCurrentConditionsAsync());
    }
}

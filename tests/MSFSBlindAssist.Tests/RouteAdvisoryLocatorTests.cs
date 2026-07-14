using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteAdvisoryLocatorTests
{
    [Fact]
    public void ComposePhrase_inside_wins_over_distance()
    {
        var fact = new LocationFact(HasGeometry: true, Inside: true, DistanceNm: 0, Behind: false);
        Assert.Equal("at your position", RouteAdvisoryLocator.ComposePhrase(fact, spoken: true));
        Assert.Equal("Inside", RouteAdvisoryLocator.ComposePhrase(fact, spoken: false));
    }

    [Fact]
    public void ComposePhrase_no_geometry_yields_no_line()
        => Assert.Null(RouteAdvisoryLocator.ComposePhrase(
            new LocationFact(false, false, null, false), spoken: false));

    [Fact]
    public void ComposePhrase_no_geometry_probe_inside_still_reads_inside()
    {
        // The probe-confirmed no-geometry inside case: HasGeometry is false but the
        // positional probe matched (Inside true). The Inside check MUST stay ordered
        // BEFORE the HasGeometry guard, or this drops to "no line" and the box/speech
        // silently lose the one location a blind pilot most needs.
        var fact = new LocationFact(HasGeometry: false, Inside: true, DistanceNm: null, Behind: false);
        Assert.Equal("Inside", RouteAdvisoryLocator.ComposePhrase(fact, spoken: false));
        Assert.Equal("at your position", RouteAdvisoryLocator.ComposePhrase(fact, spoken: true));
    }

    [Fact]
    public void ComposePhrase_outside_uses_distance_and_behindness()
        => Assert.Equal("95 nm behind", RouteAdvisoryLocator.ComposePhrase(
            new LocationFact(true, false, 95.0, true), spoken: false));

    [Fact]
    public async Task ComputeFactsAsync_zero_position_yields_empty()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "MHTG SIGMET J5 EMBD TS\r\nValid until: 2200z\r\nMHCC CENTRAL AMERICAN FIR EMBD TS OBS TOPS FL520 STNR NC");
        var facts = await RouteAdvisoryLocator.ComputeFactsAsync(
            new ActiveSkyClient(), advisories, default);
        Assert.Empty(facts);
    }

    [Fact]
    public async Task ComputeLocationsAsync_zero_position_yields_no_phrases()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            "MHTG SIGMET J5 EMBD TS\r\nValid until: 2200z\r\nMHCC CENTRAL AMERICAN FIR EMBD TS OBS TOPS FL520 STNR NC");
        var pos = default(MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition); // Lat/Lon 0 — returns before any I/O (spec §8.4)
        var result = await RouteAdvisoryLocator.ComputeLocationsAsync(
            new ActiveSkyClient(), advisories, pos, spoken: true);
        Assert.Empty(result);
    }
}

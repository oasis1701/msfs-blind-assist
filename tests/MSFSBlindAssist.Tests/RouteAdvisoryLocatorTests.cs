using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class RouteAdvisoryLocatorTests
{
    private static readonly (double Lat, double Lon)[] Square =
        { (10, 20), (10, 21), (11, 21), (11, 20) };

    [Fact]
    public void Probe_match_wins_even_without_geometry()
        => Assert.Equal("at your position",
            RouteAdvisoryLocator.Compose(null, probeMatched: true, 0, 0, 0, spoken: true));

    [Fact]
    public void Polygon_containment_reads_inside()
        => Assert.Equal("Inside",
            RouteAdvisoryLocator.Compose(Square, probeMatched: false, 10.5, 20.5, 0, spoken: false));

    [Fact]
    public void Outside_polygon_reads_distance_and_direction()
    {
        // 1° south of the square, heading north: nearest corner ~60 nm dead ahead.
        Assert.Equal("60 nm ahead",
            RouteAdvisoryLocator.Compose(Square, false, 9.0, 20.0, 0, spoken: false));
        // Same geometry, heading south: it's behind.
        Assert.Equal("60 nautical miles behind you",
            RouteAdvisoryLocator.Compose(Square, false, 9.0, 20.0, 180, spoken: true));
    }

    [Fact]
    public void No_geometry_and_no_probe_is_null()
        => Assert.Null(RouteAdvisoryLocator.Compose(null, false, 9.0, 20.0, 0, spoken: false));

    [Fact]
    public void Compose_probe_match_overrides_geometry_that_says_outside()
    {
        var farPoly = new List<(double Lat, double Lon)> { (50, 10), (51, 10), (51, 11) };
        string? p = RouteAdvisoryLocator.Compose(farPoly, probeMatched: true,
            lat: 0.5, lon: 0.5, trueHeadingDeg: 0, spoken: true);
        Assert.Equal("at your position", p);   // probe is authoritative (locator rule, line ~19)
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

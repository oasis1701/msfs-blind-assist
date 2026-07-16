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

    // 1°x1° WI square N1000/E02000 - N1000/E02100 - N1100/E02100 - N1100/E02000 — the same square
    // AdvisoryGeometryTests uses (there as plain (lat,lon) tuples), spelled out in ICAO WI form so
    // AdvisoryGeometry.ParseWiPolygon can extract it from the advisory body below.
    private const string SquareWiBody =
        "TEST FIR EMBD TS OBS AT 1100Z WI N1000 E02000 - N1000 E02100 - N1100 E02100 - N1100 E02000 TOP FL450 NC=";

    // Hermeticity note (mirrors ComputeFactsAsync_zero_position_yields_empty above): these two
    // tests use a NON-zero position, so ComputeFactsAsync proceeds past the §8.4 early return and
    // reaches the positional probe. That probe still makes no real HTTP call: a freshly
    // constructed ActiveSkyClient has LastSuccessfulPort == null (never discovered), so
    // GetPositionalAdvisoriesTextAsync's `if (LastSuccessfulPort is not int port) return null;`
    // short-circuits instantly — doubly guarded by UserSettings.ActiveSkyEnabled defaulting false
    // (GetPositionalAdvisoriesTextAsync's very first line). Tier-2 HTTP is likewise never reached:
    // the advisory's own WI polygon parses on tier 1, so `vertices == null` is false and the
    // tier-2 branch is skipped entirely regardless of Identity.

    [Fact]
    public async Task ComputeFactsAsync_tier1_polygon_containing_the_position_yields_inside_fact()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            $"TEST SIGMET P1 EMBD TS\r\nValid until: 1200z\r\n{SquareWiBody}");
        Assert.Single(advisories);   // sanity: one WI polygon parsed into one advisory block

        var pos = new MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition
        {
            Latitude = 10.5, Longitude = 20.5,   // centre of the square — inside
        };
        var facts = await RouteAdvisoryLocator.ComputeFactsAsync(new ActiveSkyClient(), advisories, pos);

        var fact = Assert.Single(facts).Value;
        Assert.True(fact.HasGeometry);
        Assert.True(fact.Inside);
        Assert.Equal(0, fact.DistanceNm);
    }

    [Fact]
    public async Task ComputeFactsAsync_tier1_polygon_not_containing_the_position_yields_outside_fact()
    {
        var advisories = ActiveSkyFormatting.ParseRouteAdvisories(
            $"TEST SIGMET P2 EMBD TS\r\nValid until: 1200z\r\n{SquareWiBody}");
        Assert.Single(advisories);

        var pos = new MSFSBlindAssist.SimConnect.SimConnectManager.AircraftPosition
        {
            Latitude = 5.0, Longitude = 20.5,    // well south of the square — outside
        };
        var facts = await RouteAdvisoryLocator.ComputeFactsAsync(new ActiveSkyClient(), advisories, pos);

        var fact = Assert.Single(facts).Value;
        Assert.True(fact.HasGeometry);
        Assert.False(fact.Inside);
        Assert.True(fact.DistanceNm > 0, $"expected a positive distance, got {fact.DistanceNm}");
    }
}

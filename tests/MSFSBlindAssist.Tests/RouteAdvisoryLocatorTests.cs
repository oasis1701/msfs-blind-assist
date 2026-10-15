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

    // Live capture 2026-10-15, MD-11F at FL360 over Wisconsin: ActiveSky's positional probe at
    // 43.843412, -88.738517 answered with this British Columbia mountain-wave SIGMET, about
    // 1,600 nm away, while its probe at 57.5N 131W, on the SIGMET's own line, answered no-hit.
    // The app announced "Entering advisory area" and showed the row as Inside (WX-14).
    private const string CzvrN2Body =
        "CZVR VANCOUVER FIR SEV MTW FCST WI 120NM WID LINE BTN N5443 W12731 - N6029 W13340 - N6050 W13610 SFC/FL140 STNR NC=";

    [Fact]
    public void BuildFact_ignores_a_probe_match_far_outside_the_advisorys_own_shape()
    {
        var vertices = AdvisoryGeometry.ParseWiPolygon(CzvrN2Body, out bool wholeShape);
        var fact = RouteAdvisoryLocator.BuildFact(vertices, wholeShape, probeMatched: true,
            43.843412, -88.738517, trueHeadingDeg: 249.4);

        Assert.True(fact.HasGeometry);
        Assert.False(fact.Inside);
        Assert.False(fact.Behind);
        Assert.Equal("1,800 nm ahead", RouteAdvisoryLocator.ComposePhrase(fact, spoken: false));
    }

    // Aircraft due south of the square's south edge (N1000, E02000-E02100): in the locator's flat
    // frame the nearest boundary point is straight north, nmSouth away.
    [Theory]
    [InlineData(RouteAdvisoryLocator.ProbeTrustMarginNm - 1, true)]
    [InlineData(RouteAdvisoryLocator.ProbeTrustMarginNm + 1, false)]
    public void BuildFact_trusts_a_probe_match_only_within_the_margin(double nmSouth, bool inside)
    {
        var vertices = AdvisoryGeometry.ParseWiPolygon(SquareWiBody, out bool wholeShape);
        var fact = RouteAdvisoryLocator.BuildFact(vertices, wholeShape, probeMatched: true,
            10.0 - nmSouth / 60.0, 20.5, trueHeadingDeg: 0);

        Assert.True(fact.HasGeometry);
        Assert.Equal(inside, fact.Inside);
        if (!inside) Assert.Equal(nmSouth, fact.DistanceNm!.Value, 3);
    }

    [Fact]
    public void BuildFact_trusts_a_far_probe_match_when_the_ring_is_only_part_of_the_shape()
    {
        // The aircraft could be inside the part the parsed ring leaves out.
        var vertices = AdvisoryGeometry.ParseWiPolygon(CzvrN2Body, out _);
        var fact = RouteAdvisoryLocator.BuildFact(vertices, wholeShape: false, probeMatched: true,
            43.843412, -88.738517, trueHeadingDeg: 249.4);
        Assert.Equal(new LocationFact(true, true, 0, false), fact);
    }

    [Fact]
    public void BuildFact_without_geometry_takes_the_probe_alone()
    {
        Assert.Equal(new LocationFact(false, true, null, false), RouteAdvisoryLocator.BuildFact(
            null, wholeShape: false, probeMatched: true, 43.8, -88.7, trueHeadingDeg: 249));
        Assert.Equal(new LocationFact(false, false, null, false), RouteAdvisoryLocator.BuildFact(
            null, wholeShape: false, probeMatched: false, 43.8, -88.7, trueHeadingDeg: 249));
    }

    [Fact]
    public void BuildFact_inside_the_shape_reads_inside_without_a_probe_match()
    {
        var vertices = AdvisoryGeometry.ParseWiPolygon(SquareWiBody, out bool wholeShape);
        Assert.Equal(new LocationFact(true, true, 0, false), RouteAdvisoryLocator.BuildFact(
            vertices, wholeShape, probeMatched: false, 10.5, 20.5, trueHeadingDeg: 0));
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

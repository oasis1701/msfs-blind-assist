using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class AdvisoryGeometryTests
{
    // Live captures 2026-07-12 (weather.md §12) — spacing quirks are verbatim. MhtgBody is
    // byte-identical to RouteAdvisoriesTests.LiveMhtgBody (7 coordinate pairs); keep them in sync.
    private const string MhtgBody =
        "MHCC CENTRAL AMERICAN FIR EMBD TS OBS AT 1830Z WI N1121 W10027 - N1258 W09506 - N1403 W09304- N1127 W09031  - N0950 W09306 - N0923 W09619 - N0904 W09940 TOP FL520 MOV W 05KT NC=";
    private const string YmmmBody =
        "YMMM MELBOURNE FIR SEV TURB FCST WI S3640 E14800 - S3340 E15000 - S3410 E15100 - S3740 E14940 - S3820 E14550 - S3730 E14520 SFC/8000FT STNR NC=";

    [Fact]
    public void ParseWiPolygon_parses_the_live_mhtg_body()
    {
        var v = AdvisoryGeometry.ParseWiPolygon(MhtgBody)!;
        Assert.Equal(7, v.Count);
        Assert.Equal(11 + 21 / 60.0, v[0].Lat, 6);          // N1121
        Assert.Equal(-(100 + 27 / 60.0), v[0].Lon, 6);      // W10027
        Assert.Equal(9 + 4 / 60.0, v[6].Lat, 6);            // N0904
        Assert.Equal(-(99 + 40 / 60.0), v[6].Lon, 6);       // W09940
    }

    [Fact]
    public void ParseWiPolygon_parses_southern_and_eastern_hemispheres()
    {
        var v = AdvisoryGeometry.ParseWiPolygon(YmmmBody)!;
        Assert.Equal(6, v.Count);
        Assert.Equal(-(36 + 40 / 60.0), v[0].Lat, 6);       // S3640
        Assert.Equal(148.0, v[0].Lon, 6);                   // E14800
    }

    [Fact]
    public void ParseWiPolygon_drops_a_duplicated_closing_vertex()
    {
        // The 2026-07-13 oceanic ECHO 5 capture repeats its first vertex to close.
        var v = AdvisoryGeometry.ParseWiPolygon(
            "FRQ TS OBS WI N3454 W07549 - N3231 W06558 - N2956 W06558 - N3454 W07549 TOP FL490")!;
        Assert.Equal(3, v.Count);
    }

    [Fact]
    public void ParseWiPolygon_ignores_coordinates_before_the_wi_token()
    {
        // VA SIGMETs carry a PSN pair before WI — only the WI polygon counts.
        var v = AdvisoryGeometry.ParseWiPolygon(
            "VA ERUPTION MT LEWOTOLOK PSN S0816 E12330 VA CLD OBS AT 1150Z WI S0820 E12333 - S0820 E12313 - S0804 E12318")!;
        Assert.Equal(3, v.Count);
        Assert.Equal(-(8 + 20 / 60.0), v[0].Lat, 6);
    }

    [Theory]
    [InlineData("EMBD TS OBS AT 1830Z TOP FL520 MOV W 05KT NC=")]          // no WI
    [InlineData("SEV TURB FCST WI S3640 E14800 - S3340 E15000 SFC/8000FT")] // only 2 vertices
    [InlineData("")]
    public void ParseWiPolygon_returns_null_when_unusable(string body)
        => Assert.Null(AdvisoryGeometry.ParseWiPolygon(body));
}

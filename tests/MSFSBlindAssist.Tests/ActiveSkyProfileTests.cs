// GetWeatherInfoXml parsing + the curated vertical-profile narrative. The XML golden
// is a live capture (2026-07-10, FL360 over the Texas panhandle). Enum conventions
// (FSX lineage, live-verified): wind-layer altitudes are AltFeet (FEET), cloud
// base/top are METRES; Turbulence/CloudTurbulence/Icing are severity enums 0-4;
// Coverage is oktas; PrecipType 0 none / 1 rain / 2 snow.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyProfileTests
{
    private const string LiveFixture =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?><Weather ElevationMeters=\"\"><WindLayers>"
        + "<SurfaceLayer WindDirection=\"208\" WindSpeed=\"4\" Turbulence=\"1\" Temp=\"37.29\" Gust=\"14\" Variance=\"327.00\" />"
        + "<Layer AltFeet=\"3000\" WindDirection=\"187\" WindSpeed=\"9\" Turbulence=\"1\" Temp=\"38.54\" />"
        + "<Layer AltFeet=\"6000\" WindDirection=\"192\" WindSpeed=\"10\" Turbulence=\"1\" Temp=\"29.32\" />"
        + "<Layer AltFeet=\"9000\" WindDirection=\"195\" WindSpeed=\"10\" Turbulence=\"0\" Temp=\"23.50\" />"
        + "<Layer AltFeet=\"12000\" WindDirection=\"226\" WindSpeed=\"7\" Turbulence=\"0\" Temp=\"7.86\" />"
        + "<Layer AltFeet=\"18000\" WindDirection=\"316\" WindSpeed=\"19\" Turbulence=\"0\" Temp=\"-7.92\" />"
        + "<Layer AltFeet=\"24000\" WindDirection=\"287\" WindSpeed=\"26\" Turbulence=\"0\" Temp=\"-17.83\" />"
        + "<Layer AltFeet=\"30000\" WindDirection=\"262\" WindSpeed=\"27\" Turbulence=\"0\" Temp=\"-29.43\" />"
        + "<Layer AltFeet=\"34000\" WindDirection=\"286\" WindSpeed=\"20\" Turbulence=\"0\" Temp=\"-37.08\" />"
        + "<Layer AltFeet=\"39000\" WindDirection=\"267\" WindSpeed=\"16\" Turbulence=\"0\" Temp=\"-49.91\" />"
        + "<Layer AltFeet=\"44000\" WindDirection=\"298\" WindSpeed=\"13\" Turbulence=\"0\" Temp=\"-62.03\" />"
        + "<Layer AltFeet=\"49000\" WindDirection=\"197\" WindSpeed=\"11\" Turbulence=\"0\" Temp=\"-72.96\" />"
        + "<Layer AltFeet=\"56000\" WindDirection=\"114\" WindSpeed=\"14\" Turbulence=\"0\" Temp=\"-60.08\" />"
        + "</WindLayers><SurfaceVisibility VisMeters=\"83271\" VisBaseMeters=\"-1999\" VisTopMeters=\"1218\" />"
        + "<Clouds><Cloud CloudType=\"9\" CloudBaseMeters=\"3974\" CloudTopMeters=\"7530\" Coverage=\"4\" CloudTurbulence=\"1\" PrecipType=\"0\" PrecipRate=\"0\" Icing=\"0\" /></Clouds>"
        + "<QNH ValueHectoPascal=\"1014.63\" ReportsAsQNH=\"0\" /></Weather>";

    // API-doc example variant: richer cloud (broken, moderate cloud turbulence,
    // rain, light icing) and explicit </Layer> closing tags.
    private const string DocFixture =
        "<?xml version=\"1.0\" encoding=\"utf-16\"?><Weather ElevationMeters=\"\"><WindLayers>"
        + "<SurfaceLayer WindDirection=\"205\" WindSpeed=\"15\" Turbulence=\"0\" Temp=\"15\" Gust=\"25\" Variance=\"0\" DewPoint=\"5\" />"
        + "<Layer AltFeet=\"3000\" WindDirection=\"205\" WindSpeed=\"15\" Turbulence=\"0\" Temp=\"15\"></Layer>"
        + "<Layer AltFeet=\"6000\" WindDirection=\"205\" WindSpeed=\"15\" Turbulence=\"0\" Temp=\"15\" />"
        + "</WindLayers>"
        + "<Clouds><Cloud CloudType=\"9\" CloudBaseMeters=\"1000\" CloudTopMeters=\"2000\" Coverage=\"5\" CloudTurbulence=\"2\" PrecipType=\"1\" PrecipRate=\"2\" Icing=\"1\" /></Clouds>"
        + "<QNH ValueHectoPascal=\"1015\" ReportsAsQNH=\"1\" /></Weather>";

    [Fact]
    public void Parse_reads_wind_layers_with_surface_first()
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(LiveFixture);
        Assert.NotNull(p);
        Assert.Equal(13, p!.WindLayers.Count);
        Assert.True(p.WindLayers[0].IsSurface);
        Assert.Equal(0, p.WindLayers[0].AltitudeFt);
        Assert.Equal(14, p.WindLayers[0].GustKts);
        Assert.Equal(34000, p.WindLayers[8].AltitudeFt);
        Assert.Equal(-37.08, p.WindLayers[8].TemperatureC, 2);
    }

    [Fact]
    public void Parse_converts_cloud_metres_to_feet()
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(LiveFixture);
        var c = Assert.Single(p!.CloudLayers);
        Assert.Equal(13038, c.BaseFt);   // 3974 m
        Assert.Equal(24705, c.TopFt);    // 7530 m
        Assert.Equal(4, c.CoverageOktas);
        Assert.Equal(0, c.IcingEnum);
    }

    [Fact]
    public void Parse_handles_explicit_closing_tags_and_missing_attributes()
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(DocFixture);
        Assert.NotNull(p);
        Assert.Equal(3, p!.WindLayers.Count);
        var c = Assert.Single(p.CloudLayers);
        Assert.Equal(5, c.CoverageOktas);
        Assert.Equal(1, c.IcingEnum);
        Assert.Equal(1, c.PrecipType);
        Assert.Equal(2, c.TurbulenceEnum);
    }

    [Theory]
    [InlineData("not xml at all")]
    [InlineData("<Weather></Weather>")]
    public void Parse_degrades_gracefully(string bad)
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(bad);
        Assert.True(p == null || (p.WindLayers.Count == 0 && p.CloudLayers.Count == 0));
    }

    // --- Narrative -----------------------------------------------------------------------

    [Fact]
    public void Narrative_curates_levels_and_describes_the_cloud_layer()
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(LiveFixture)!;
        string text = ActiveSkyFormatting.BuildProfileNarrative(p, 36000);

        Assert.Contains("Scattered, 13,038 to 24,705 feet", text);
        Assert.DoesNotContain("icing", text);                       // Icing=0 → omitted
        Assert.Contains("Winds and temperatures aloft:", text);
        Assert.Contains("Surface: 208 at 4, gusting 14, 37, light turbulence", text);
        Assert.Contains("34,000 feet: 286 at 20, minus 37", text);  // nearest to FL360
        Assert.DoesNotContain("44,000 feet", text);                 // not a curated target
        Assert.DoesNotContain("3,000 feet:", text);                 // 6,000 wins the 5,000 target
    }

    [Fact]
    public void Narrative_includes_icing_precip_and_cloud_turbulence_when_present()
    {
        var p = ActiveSkyClient.ParseWeatherInfoXml(DocFixture)!;
        string text = ActiveSkyFormatting.BuildProfileNarrative(p, 2000);
        Assert.Contains("Broken, 3,281 to 6,562 feet, light icing, rain, moderate turbulence", text);
    }

    [Fact]
    public void Narrative_reports_empty_sky()
    {
        var p = new ActiveSkyClient.VerticalProfile();
        p.WindLayers.Add(new ActiveSkyClient.ProfileWindLayer
        {
            IsSurface = true, AltitudeFt = 0, DirectionDeg = 100, SpeedKts = 5, TemperatureC = 15,
        });
        string text = ActiveSkyFormatting.BuildProfileNarrative(p, 1000);
        Assert.StartsWith("No cloud layers reported below FL560.", text);
        Assert.Contains("Surface: 100 at 5, 15", text);
    }

    // --- Enum word maps -------------------------------------------------------------------

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "Few")]
    [InlineData(2, "Few")]
    [InlineData(3, "Scattered")]
    [InlineData(4, "Scattered")]
    [InlineData(5, "Broken")]
    [InlineData(7, "Broken")]
    [InlineData(8, "Overcast")]
    [InlineData(99, null)]
    public void Coverage_words(int oktas, string? expected)
        => Assert.Equal(expected, ActiveSkyFormatting.CoverageWord(oktas));

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "light")]
    [InlineData(2, "moderate")]
    [InlineData(3, "heavy")]
    [InlineData(4, "severe")]
    [InlineData(9, null)]
    public void Severity_words(int e, string? expected)
        => Assert.Equal(expected, ActiveSkyFormatting.SeverityWord(e));
}

// ActiveSky number strings: some GetCurrentConditions fields come in the OS culture.
// Live capture 2026-10-12 at ESGG under Swedish Windows: QNH "1014,999" and
// AmbientTemperature "7,992481" rendered as 1014999 hPa and 7992481°C.
// The parser itself is covered in NumberParsingTests.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyNumberParsingTests
{
    // Spelled as an escape: a screen reader cannot tell it from "-".
    private const string Minus = "\U00002212";   // U+2212 MINUS SIGN, written by .NET 5+ for sv-SE

    [Fact]
    public void ParseConditionsJson_reads_comma_decimal_fields_under_any_culture()
    {
        InSwedish(() =>
        {
            const string json = """
{ "AmbientTemperature": "7,992481", "SurfaceTemerature": "8", "QNH": "1014,999",
  "CloudCeiling": "6449", "SurfaceVisibility": "10.0", "AmbientWindSpeed": "12" }
""";
            var c = ActiveSkyClient.ParseConditionsJson(json);
            Assert.NotNull(c);
            Assert.Equal(7.992481, c!.AmbientTemperature, 6);
            Assert.Equal(8.0, c.SurfaceTemperature, 6);
            Assert.Equal(1014.999, c.QnhMb, 6);
            Assert.Equal(6449.0, c.CloudCeilingFtAgl, 6);
            Assert.Equal(10.0, c.SurfaceVisibility, 6);
        });
    }

    [Theory]
    [InlineData("1760300000")]
    [InlineData("1760300000.0")]
    [InlineData("1760300000,5")]
    public void ParseConditionsJson_reads_a_timestamp_with_a_fraction(string raw)
    {
        var c = ActiveSkyClient.ParseConditionsJson("{ \"TimeStamp\": \"" + raw + "\" }");
        Assert.Equal(1760300000L, c!.TimeStamp);
    }

    [Fact]
    public void ParseAtmosphereJson_reads_comma_decimal_levels()
    {
        InSwedish(() =>
        {
            string json = $$"""
{ "WeatherData": [
  { "Altitude": "34000", "WindDirection": "286,0", "WindSpeed": "20,0", "Temperature": "{{Minus}}37,1" },
  { "Altitude": "18000", "WindDirection": "316,0", "WindSpeed": "19,0", "Temperature": "-7,9" } ] }
""";
            var levels = ActiveSkyClient.ParseAtmosphereJson(json);
            Assert.NotNull(levels);
            Assert.Equal(2, levels!.Count);
            Assert.Equal(34000, levels[0].AltitudeFt);
            Assert.Equal(286.0, levels[0].WindDirection, 6);
            Assert.Equal(-37.1, levels[0].TemperatureC, 6);
            Assert.Equal(-7.9, levels[1].TemperatureC, 6);
        });
    }

    [Fact]
    public void ParseWeatherInfoXml_reads_comma_decimal_attributes()
    {
        InSwedish(() =>
        {
            string xml = "<Weather><WindLayers>"
                + "<SurfaceLayer WindDirection=\"208\" WindSpeed=\"4\" Turbulence=\"1\" Temp=\"7,5\" Gust=\"14\" />"
                + "<Layer AltFeet=\"34000\" WindDirection=\"286\" WindSpeed=\"20\" Turbulence=\"0\" Temp=\"" + Minus + "37,08\" />"
                + "</WindLayers><Clouds><Cloud CloudBaseMeters=\"3974,5\" CloudTopMeters=\"7530\" Coverage=\"4\" /></Clouds></Weather>";
            var p = ActiveSkyClient.ParseWeatherInfoXml(xml);
            Assert.NotNull(p);
            Assert.Equal(7.5, p!.WindLayers[0].TemperatureC, 6);
            Assert.Equal(-37.08, p.WindLayers[1].TemperatureC, 6);
            Assert.Equal(13040, p.CloudLayers[0].BaseFt);   // 3974.5 m
        });
    }

    // CI runs under en-US; these parses must not depend on the PC's culture.
    private static void InSwedish(Action body)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

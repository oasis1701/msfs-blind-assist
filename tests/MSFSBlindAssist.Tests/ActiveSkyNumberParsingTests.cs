// ActiveSky number strings: some GetCurrentConditions fields come in the OS culture.
// Live capture 2026-10-12 at ESGG under Swedish Windows: QNH "1014,999" and
// AmbientTemperature "7,992481" rendered as 1014999 hPa and 7992481°C.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyNumberParsingTests
{
    [Theory]
    [InlineData("1014.999", 1014.999)]
    [InlineData("1014,999", 1014.999)]
    [InlineData("7,992481", 7.992481)]
    [InlineData("-12,5", -12.5)]
    [InlineData("8", 8.0)]
    [InlineData("6449", 6449.0)]
    [InlineData("1.014,999", 1014.999)]
    [InlineData("1,014.999", 1014.999)]
    [InlineData("1 014,999", 1014.999)]
    public void TryParseNumber_reads_invariant_and_comma_decimal_strings(string raw, double expected)
    {
        Assert.True(ActiveSkyClient.TryParseNumber(raw, out double d));
        Assert.Equal(expected, d, 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("n/a")]
    public void TryParseNumber_rejects_non_numbers(string? raw)
        => Assert.False(ActiveSkyClient.TryParseNumber(raw, out _));

    [Fact]
    public void ParseConditionsJson_reads_comma_decimal_fields_under_any_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
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
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

// ActiveSky number strings: some GetCurrentConditions fields come in the OS culture.
// Live capture 2026-10-12 at ESGG under Swedish Windows: QNH "1014,999" and
// AmbientTemperature "7,992481" rendered as 1014999 hPa and 7992481°C.
// The parser itself is covered in NumberParsingTests.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyNumberParsingTests
{
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

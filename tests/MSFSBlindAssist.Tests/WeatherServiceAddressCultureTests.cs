// The weather radar's web addresses carry coordinates with a decimal point whatever the PC's
// regional settings. Under a comma-decimal culture the bounding box was eight numbers
// ("11,25,48,10,…") and the winds address "latitude=48,3538", which the services refuse or misread,
// so a German or Swedish Windows got no SIGMETs, PIREPs or winds aloft.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class WeatherServiceAddressCultureTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_bounding_box_is_four_numbers_with_decimal_points(string culture)
    {
        string box = TestCulture.Run(culture, () => WeatherService.BuildBbox(48.0, 11.5, 60.0));
        string[] parts = box.Split(',');
        Assert.Equal(4, parts.Length);
        Assert.Equal("47.00", parts[1]);   // south: one degree of latitude below 48
        Assert.Equal("49.00", parts[3]);   // north
        Assert.All(parts, p => Assert.Matches(@"^-?\d+\.\d{2}$", p));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_winds_address_carries_the_position_with_decimal_points(string culture) =>
        Assert.Contains("latitude=48.3538&longitude=11.7861&",
            TestCulture.Run(culture, () => WeatherService.BuildWindsUrl(48.3538, 11.7861)));

    [Fact]
    public void A_negative_position_keeps_its_ascii_minus_under_a_swedish_culture() =>
        Assert.Contains("latitude=-33.9461&longitude=-70.7858&",
            TestCulture.Run("sv-SE", () => WeatherService.BuildWindsUrl(-33.9461, -70.7858)));
}

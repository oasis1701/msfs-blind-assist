// The weather radar's web addresses carry coordinates with a decimal point whatever the PC's
// regional settings. Under a comma-decimal culture the bounding box was eight numbers
// ("11,25,48,10,…") and the winds address "latitude=48,3538", which the services refuse or misread,
// so a German or Swedish Windows got no PIREPs or winds aloft. The PIREP box is latitude first, as
// the aviationweather.gov Data API defines it ("lat0, lon0, lat1, lon1"), and stays in range.

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
        Assert.Equal("47.00", parts[0]);   // south: one degree of latitude below 48
        Assert.Equal("49.00", parts[2]);   // north
        Assert.All(parts, p => Assert.Matches(@"^-?\d+\.\d{2}$", p));
    }

    [Fact]
    public void The_bounding_box_is_latitude_first() =>
        // 60 nm around 40 N 90 W: latitude 39 to 41, longitude about 91.31 W to 88.69 W.
        Assert.Equal("39.00,-91.31,41.00,-88.69", WeatherService.BuildBbox(40.0, -90.0, 60.0));

    [Fact]
    public void A_box_past_the_date_line_is_clamped_to_it() =>
        Assert.EndsWith(",180.00", WeatherService.BuildBbox(-17.75, 179.5, 300.0));

    [Fact]
    public void A_box_near_a_pole_takes_every_longitude_and_stops_at_the_pole() =>
        Assert.Equal("88.50,-180.00,90.00,180.00", WeatherService.BuildBbox(89.5, 10.0, 60.0));

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

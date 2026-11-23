// Pure-logic formatters that speak or show a decimal or a grouped number keep a decimal point and an
// English group separator whatever the PC's regional settings (CORE-21). CI runs under en-US, so each
// case runs under de-DE and sv-SE, where a plain interpolated string writes "2,3" and "2.000".

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Models;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class FormatterDecimalCultureTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_tcas_line_has_a_decimal_point_and_an_english_group(string culture) =>
        Assert.Equal("2.3nm, 2,000ft above, 1 o'clock", TestCulture.Run(culture,
            () => new TcasTraffic { DistanceNm = 2.3, AltitudeDiffFt = 2000, RelativeBearing = 30 }.RelativePositionSummary));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_waypoint_reads_its_distance_with_a_decimal_point(string culture)
    {
        var fix = new WaypointFix { Ident = "ROKIL", DistanceFromAircraft = 12.34, BearingFromAircraft = 270 };
        Assert.Equal("ROKIL - 12.3 NM, 270°", TestCulture.Run(culture, fix.ToString));
        Assert.StartsWith("ROKIL, Distance 12.3 nautical miles, Bearing 270 degrees",
            TestCulture.Run(culture, fix.GetDetailedDescription));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void An_altitude_range_groups_thousands_with_a_comma(string culture) =>
        Assert.Equal("5,000 ft to 18,000 ft", TestCulture.Run(culture, () => WeatherService.DecodeAltitudeRange(5000, 18000)));

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_activesky_altimeter_reads_its_inches_with_a_decimal_point(string culture)
    {
        string text = TestCulture.Run(culture, () => ActiveSkyWeatherMonitor.BuildDecodedWeatherText(
            "KDUX 101915Z AUTO 22009KT 10SM CLR 36/12 A3001 RMK AO2",
            new ActiveSkyClient.Conditions { SurfaceWindDirection = 220, SurfaceWindSpeed = 9, QnhMb = 1016 }));
        Assert.Matches(@"Altimeter: \d+ \(\d+\.\d{2} inches\)\.", text);
    }
}

// DistanceFormatter's unit is process-wide, so this runs in the collection that serialises it.
[Collection("DistanceUnitGlobalState")]
public class TaxiDistanceDecimalCultureTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_long_taxi_distance_reads_with_a_decimal_point(string culture)
    {
        var saved = DistanceFormatter.UnitProvider;
        try
        {
            DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
            Assert.Equal("2.7 kilometres", TestCulture.Run(culture, () => TaxiGuidanceManager.FormatDistance(2700)));
            DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
            Assert.Equal("1.5 nautical miles", TestCulture.Run(culture, () => TaxiGuidanceManager.FormatDistance(2700)));
        }
        finally
        {
            DistanceFormatter.UnitProvider = saved;
        }
    }
}

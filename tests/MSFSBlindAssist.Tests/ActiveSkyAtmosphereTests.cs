// GetAtmosphere JSON parsing + the AS-sourced Winds Aloft text. The JSON golden is a
// live capture (2026-07-10, ?altitudes=0|5000|10000|18000|24000|34000).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyAtmosphereTests
{
    private const string Fixture = """
{
  "WeatherData": [
    { "Altitude": "0", "WindDirection": "208.0", "WindSpeed": "4.0", "Temperature": "37.3", "Pressure": "1014.7" },
    { "Altitude": "5000", "WindDirection": "190.0", "WindSpeed": "10.0", "Temperature": "31.3", "Pressure": "844.5" },
    { "Altitude": "10000", "WindDirection": "203.0", "WindSpeed": "9.0", "Temperature": "21.5", "Pressure": "698.2" },
    { "Altitude": "18000", "WindDirection": "316.0", "WindSpeed": "19.0", "Temperature": "-7.9", "Pressure": "507.2" },
    { "Altitude": "24000", "WindDirection": "287.0", "WindSpeed": "26.0", "Temperature": "-17.8", "Pressure": "393.8" },
    { "Altitude": "34000", "WindDirection": "286.0", "WindSpeed": "20.0", "Temperature": "-37.1", "Pressure": "250.9" }
  ]
}
""";

    [Fact]
    public void ParseAtmosphereJson_reads_all_levels_with_invariant_culture()
    {
        var levels = ActiveSkyClient.ParseAtmosphereJson(Fixture);
        Assert.NotNull(levels);
        Assert.Equal(6, levels!.Count);
        Assert.Equal(0, levels[0].AltitudeFt);
        Assert.Equal(208.0, levels[0].WindDirection);
        Assert.Equal(34000, levels[5].AltitudeFt);
        Assert.Equal(-37.1, levels[5].TemperatureC, 3);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"WeatherData\": \"nope\"}")]
    public void ParseAtmosphereJson_degrades_to_null_or_empty_on_bad_input(string bad)
    {
        var levels = ActiveSkyClient.ParseAtmosphereJson(bad);
        Assert.True(levels == null || levels.Count == 0);
    }

    [Fact]
    public void WindsAloftAltitudes_mirrors_the_open_meteo_window()
        => Assert.Equal(new[] { 31000, 32000, 33000, 34000, 35000, 36000, 37000, 38000, 39000, 40000, 41000 },
            ActiveSkyFormatting.WindsAloftAltitudes(36000));

    [Fact]
    public void WindsAloftAltitudes_clamps_at_ground()
        => Assert.Equal(0, ActiveSkyFormatting.WindsAloftAltitudes(2000)[0]);

    [Fact]
    public void BuildWindsAloftText_formats_levels_with_temperature_marker_and_source()
    {
        var levels = ActiveSkyClient.ParseAtmosphereJson(Fixture)!;
        string text = ActiveSkyFormatting.BuildWindsAloftText(34200, levels);
        Assert.Contains("34,000 ft:  286° / 20 kts, -37°C (nearest)", text);
        Assert.Contains("0 ft:  208° / 4 kts, 37°C", text);
        Assert.EndsWith("Source: ActiveSky", text);
    }

    [Fact]
    public void ParseWindsAloft_levels_equal_the_shared_altitude_window()
    {
        string hour = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:00");
        // Two-level fixture bracketing the window; 1000 hPa ≈ 364 ft, 250 hPa ≈ 34000 ft.
        // Values are arbitrary (will be interpolated to the window altitudes).
        string json = "{\"hourly\":{\"time\":[\"" + hour + "\"]," +
            "\"wind_speed_1000hPa\":[10],\"wind_direction_1000hPa\":[270]," +
            "\"wind_speed_250hPa\":[50],\"wind_direction_250hPa\":[300]}}";
        var winds = WeatherService.ParseWindsAloft(json, 17_300);
        Assert.Equal(ActiveSkyFormatting.WindsAloftAltitudes(17_300),
            winds.Select(w => w.AltitudeFt).ToArray());
    }
}

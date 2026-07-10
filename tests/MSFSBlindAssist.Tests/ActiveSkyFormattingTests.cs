// Characterization tests for the pure ActiveSky text builders (Services/ActiveSkyFormatting.cs).
// Golden inputs are live captures from a running ASFS build (2026-07-10).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyFormattingTests
{
    // --- ParseModeText -----------------------------------------------------------------

    [Fact]
    public void ParseModeText_parses_live_mode_string()
    {
        var (mode, time) = ActiveSkyFormatting.ParseModeText(
            "Live Real time mode (Active) (2026/7/10 1935z)");
        Assert.Equal("Live Real time mode", mode);
        Assert.Equal("2026/7/10 1935z", time);
    }

    [Theory]
    [InlineData("Historic dynamic mode (Active) (2019/3/4 0600z)", "Historic dynamic mode", "2019/3/4 0600z")]
    [InlineData("Custom static mode (Active) (2026/7/10 2100z)", "Custom static mode", "2026/7/10 2100z")]
    public void ParseModeText_parses_other_mode_families(string raw, string mode, string time)
    {
        var parsed = ActiveSkyFormatting.ParseModeText(raw);
        Assert.Equal(mode, parsed.ModeName);
        Assert.Equal(time, parsed.WeatherTimeZ);
    }

    [Fact]
    public void ParseModeText_passes_unknown_strings_through_without_time()
    {
        var (mode, time) = ActiveSkyFormatting.ParseModeText("wibble");
        Assert.Equal("wibble", mode);
        Assert.Null(time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseModeText_maps_empty_to_unknown(string? raw)
    {
        var (mode, time) = ActiveSkyFormatting.ParseModeText(raw);
        Assert.Equal("unknown", mode);
        Assert.Null(time);
    }

    // --- FormatModeLine ----------------------------------------------------------------

    [Fact]
    public void FormatModeLine_shows_mode_and_clock()
        => Assert.Equal("ActiveSky: Live Real time mode, weather time 1935Z",
            ActiveSkyFormatting.FormatModeLine("Live Real time mode (Active) (2026/7/10 1935z)"));

    [Fact]
    public void FormatModeLine_without_time_shows_mode_only()
        => Assert.Equal("ActiveSky: wibble", ActiveSkyFormatting.FormatModeLine("wibble"));

    // --- BuildTempDewLine ---------------------------------------------------------------

    [Fact]
    public void TempDew_line_from_position_metar()
        => Assert.Equal("Temperature/dew point: 36 / 12°C",
            ActiveSkyFormatting.BuildTempDewLine(
                "@POS 101905Z 22009KT 10SM 36/12 A3001 RMK ADVANCED INTERPOLATION"));

    [Fact]
    public void TempDew_line_handles_negative_values()
        => Assert.Equal("Temperature/dew point: -5 / -8°C",
            ActiveSkyFormatting.BuildTempDewLine(
                "PANC 071751Z 30012KT 1SM SHSN OVC008 M05/M08 A2990"));

    [Fact]
    public void TempDew_line_with_incomplete_temp_dew_group_returns_null()
        // CHARACTERIZATION: DecodeMetar requires digits on both sides of the slash
        // in a temperature/dew-point group (IsTempDewToken enforces this). Any
        // incomplete temp/dew group like "28/" or "28//" yields no line from BuildTempDewLine.
        => Assert.Null(
            ActiveSkyFormatting.BuildTempDewLine(
                "@POS 101905Z 22009KT 10SM 28/ A3001 RMK ADVANCED INTERPOLATION"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@POS 101905Z 22009KT 10SM A3001")]   // no temp group at all
    public void TempDew_line_is_omitted_when_unavailable(string? metar)
        => Assert.Null(ActiveSkyFormatting.BuildTempDewLine(metar));

    // --- Forecast presets ----------------------------------------------------------------

    [Fact]
    public void Forecast_presets_cover_now_through_six_hours()
    {
        Assert.Equal(new[] { 0, 3600, 7200, 14400, 21600 },
            ActiveSkyFormatting.ForecastPresets.Select(p => p.OffsetSeconds).ToArray());
        Assert.Equal("Now", ActiveSkyFormatting.ForecastPresets[0].Label);
        Assert.Equal("+6 hours", ActiveSkyFormatting.ForecastPresets[^1].Label);
    }

    [Theory]
    [InlineData(0, "ActiveSky METAR:")]
    [InlineData(2, "ActiveSky METAR (+2 hours):")]
    [InlineData(99, "ActiveSky METAR (+6 hours):")]   // clamped
    [InlineData(-1, "ActiveSky METAR:")]              // clamped
    public void As_metar_caption_states_the_offset(int index, string expected)
        => Assert.Equal(expected, ActiveSkyFormatting.BuildAsMetarCaption(index));
}

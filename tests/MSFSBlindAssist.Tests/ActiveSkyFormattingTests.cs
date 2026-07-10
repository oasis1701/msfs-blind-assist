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
}

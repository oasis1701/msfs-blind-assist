// Pins the extracted BuildDecodedWeatherText (shared by the auto-announce and the
// Weather Radar form's closest-station box) against a live-captured METAR.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class ActiveSkyStationDecodedTests
{
    private const string StationMetar = "KDUX 101915Z AUTO 22009KT 10SM CLR 36/12 A3001 RMK AO2";

    private static ActiveSkyClient.Conditions Conditions() => new()
    {
        SurfaceWindDirection = 220, SurfaceWindSpeed = 9, QnhMb = 1016,
    };

    [Fact]
    public void Decoded_text_names_the_station_and_core_elements()
    {
        string text = ActiveSkyWeatherMonitor.BuildDecodedWeatherText(StationMetar, Conditions());
        Assert.Contains("KDUX", text);
        Assert.Contains("220 at 9", text);          // wind
        Assert.Contains("36", text);                // temperature
        Assert.Contains("12", text);                // dew point
        Assert.StartsWith("Decoded weather at", text);
        Assert.DoesNotContain("updated", text, StringComparison.OrdinalIgnoreCase);  // preamble stays in BuildAnnouncement
    }

    [Fact]
    public void Position_metar_is_labeled_your_position()
    {
        string text = ActiveSkyWeatherMonitor.BuildDecodedWeatherText(
            "@POS 101905Z 22009KT 10SM 36/12 A3001 RMK ADVANCED INTERPOLATION", Conditions());
        Assert.Contains("your position", text);
    }
}

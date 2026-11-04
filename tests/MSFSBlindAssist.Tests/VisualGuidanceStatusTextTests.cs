using System.Globalization;
using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What F says while visual guidance is on (VG-22): the phase, the sideways position, then the
/// vertical, with every number culture-invariant.
/// </summary>
public class VisualGuidanceStatusTextTests
{
    // phase, distance nm, cross-track nm (+ right), altitude error ft (+ high), target fpm, wheel height ft, extension heading.
    [Theory]
    [InlineData("OnFinal", 6.2, 0.0, 40, -750, 0, 0, "On final, 6.2 miles. On the centreline. 40 feet high, target 750 down.")]
    [InlineData("OnFinal", 2.1, -0.1, 10, -700, 0, 0, "On final, 2.1 miles. 0.1 miles left of the centreline. On path, target 700 down.")]
    [InlineData("Flare", 0, 0.0, 0, 0, 20.4, 0, "Flare, 20 feet. On the centreline.")]
    [InlineData("Intercepting", 8.2, 1.2, -200, 0, 0, 0, "Intercepting final, 8.2 miles out. 1.2 miles right of the centreline. 200 feet low, target level.")]
    [InlineData("Extending", 1.5, -3.0, 2258, -1500, 0, 93, "Extending, heading 093, 1.5 miles behind the threshold. 3.0 miles left of the centreline. 2,260 feet high, target 1,500 down.")]
    [InlineData("Starting", 0, 0, 0, 0, 0, 0, "Visual guidance starting.")]
    [InlineData("Touchdown", 0, 0.06, 0, 0, 0, 0, "Touchdown. 0.1 miles right of the centreline.")]
    [InlineData("OnFinal", 3.0, 0.0, 29.9, -724, 0, 0, "On final, 3.0 miles. On the centreline. On path, target 700 down.")]
    [InlineData("Flare", 0, 0.0, 0, 0, -3, 0, "Flare, 0 feet. On the centreline.")]
    public void Each_phase_reads_its_sentence(string phase, double distanceNm, double crossTrackNm, double altitudeErrorFt,
        double targetFpm, double wheelHeightFt, double extensionHeadingMag, string expected)
    {
        var status = new VisualGuidanceStatus(Enum.Parse<VisualGuidanceReadoutPhase>(phase), distanceNm, crossTrackNm,
            altitudeErrorFt, targetFpm, wheelHeightFt, extensionHeadingMag);
        Assert.Equal(expected, VisualGuidanceStatusText.Compose(status));
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void Readout_numbers_are_culture_invariant(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var status = new VisualGuidanceStatus(VisualGuidanceReadoutPhase.OnFinal, 6.2, -0.26, 1234, -1234, 0, 0);
            Assert.Equal("On final, 6.2 miles. 0.3 miles left of the centreline. 1,230 feet high, target 1,250 down.",
                VisualGuidanceStatusText.Compose(status));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

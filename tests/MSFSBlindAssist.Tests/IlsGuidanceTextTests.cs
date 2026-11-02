// IlsGuidanceText: the wording of output Ctrl+I. A localizer is named and gets no
// glideslope; the far side of the threshold gives distance, range and fitting advice; every
// number reads with a dot under a comma-decimal Windows format.

using System.Globalization;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class IlsGuidanceTextTests
{
    private static void UnderCulture(string cultureName, Action assertions)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            assertions();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ESOH 18 "IHF": an ILS, true course 177, 18 NM range.
    private static ILSData Ils() => new()
    {
        Ident = "IHF", Frequency = 110.10, Range = 18, LocalizerHeading = 177.0,
        GlideslopePitch = 3.0, HasGlideslope = true, Type = "1", MagVar = 6.0
    };

    // ESST 16 "NST": localizer only; ReadILSFromReader still defaults its pitch to 3.0.
    private static ILSData Localizer() => new()
    {
        Ident = "NST", Frequency = 108.70, Range = 18, LocalizerHeading = 157.0,
        GlideslopePitch = 3.0, HasGlideslope = false, Type = "0", MagVar = 5.0
    };

    [Fact]
    public void A_localizer_is_named_and_has_no_glideslope_line()
    {
        string text = IlsGuidanceText.OnCenterline(Localizer(), 0, 6.2, glideslopeDeviationFeet: 140, 152);

        Assert.StartsWith("Localizer 108.70, course 152. ", text);
        Assert.DoesNotContain("glideslope", text);
    }

    [Fact]
    public void An_ils_opens_with_the_distance_and_keeps_its_glideslope()
    {
        string text = IlsGuidanceText.OnCenterline(Ils(), 0, 6.2, glideslopeDeviationFeet: -120, 171);

        Assert.Equal("6.2 nautical miles from threshold, on centerline. 120 feet below glideslope. Runway heading 171.", text);
    }

    [Fact]
    public void The_range_warning_names_the_aid()
    {
        string text = IlsGuidanceText.OffCenterline(Localizer(), 0, 24.0, 1.5, 4, left: true, null, 100, 115, 130);

        Assert.Contains("Warning: Localizer signal range is 18 nautical miles. ", text);
    }

    [Fact]
    public void Behind_the_runway_beyond_range_gives_distance_and_threshold_bearing()
    {
        string text = IlsGuidanceText.Behind(Ils(), 0, "18", 31.4, extensionHeadingMag: 352, thresholdBearingMag: 8);

        Assert.Equal("Warning: ILS signal range is 18 nautical miles. " +
                     "Opposite side of runway 18, 31.4 nautical miles from threshold. Threshold bearing 008.", text);
    }

    [Fact]
    public void Behind_the_runway_within_range_says_to_extend_outbound()
    {
        string text = IlsGuidanceText.Behind(Ils(), 0, "18", 3.2, extensionHeadingMag: 352, thresholdBearingMag: 8);

        Assert.Equal("Opposite side of runway 18, 3.2 nautical miles from threshold. Fly heading 352 to extend outbound.", text);
    }

    [Fact]
    public void Numbers_read_with_a_dot_under_a_comma_decimal_format()
    {
        UnderCulture("sv-SE", () =>
        {
            string off = IlsGuidanceText.OffCenterline(Ils(), 0, 12.3, 0.4, 2, left: false, -35.6, 140, 155, 160);
            string behind = IlsGuidanceText.Behind(Localizer(), 0, "16", 3.2, 332, 152);

            Assert.DoesNotMatch(@"\d,\d", off);
            Assert.DoesNotMatch(@"\d,\d", behind);
            Assert.Contains("12.3 nautical miles from threshold, 0.4 nautical miles right of centerline", off);
            Assert.StartsWith("Localizer 108.70, course 152. ", behind);
        });
    }
}

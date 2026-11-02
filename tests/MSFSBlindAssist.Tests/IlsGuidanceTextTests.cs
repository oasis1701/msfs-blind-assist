// IlsGuidanceText: the wording of output Ctrl+I. One course per report, the chart's (the
// runway's ILSCourseMag); a localizer, LDA or IGS is named and ends with its course, not
// "Runway heading"; a localizer gets no glideslope; the far side of the threshold gives
// distance, range and fitting advice; every number reads with a dot under a comma-decimal
// Windows format.

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

    // ESOH 18 "IHF": an ILS, 18 NM range.
    private static ILSData Ils(string type = "1") => new()
    {
        Ident = "IHF", Frequency = 110.10, Range = 18, LocalizerHeading = 177.0,
        GlideslopePitch = 3.0, HasGlideslope = true, Type = type, MagVar = 6.0
    };

    // ESST 16 "NST": localizer only; ReadILSFromReader still defaults its pitch to 3.0.
    private static ILSData Localizer() => new()
    {
        Ident = "NST", Frequency = 108.70, Range = 18, LocalizerHeading = 157.0,
        GlideslopePitch = 3.0, HasGlideslope = false, Type = "0", MagVar = 5.0
    };

    // KDCA 19's LDA (type "A", no glideslope, course 147 on a runway of 186) and VHHX 13's
    // IGS (type "I", with a glideslope, course 89 on a runway of 137).
    private static ILSData Lda() => new()
    {
        Ident = "IDCA", Frequency = 111.35, Range = 18, LocalizerHeading = 136.0,
        GlideslopePitch = 3.0, HasGlideslope = false, Type = "A", MagVar = -11.0
    };

    private static ILSData Igs() => new()
    {
        Ident = "IHX", Frequency = 111.90, Range = 18, LocalizerHeading = 86.0,
        GlideslopePitch = 3.0, HasGlideslope = true, Type = "I", MagVar = -3.0
    };

    // ---- The course -------------------------------------------------------------------

    [Fact]
    public void The_course_is_the_runways_chart_course_when_the_runway_has_this_localizer()
    {
        var runway = new Runway { ILSFreq = 110.10, ILSCourseMag = 172 };

        // MagneticCourse would say 171 (177 - 6); the chart says 172.
        Assert.Equal(172, IlsGuidanceText.CourseFor(Ils(), runway, fallbackMagVar: 0));
    }

    [Fact]
    public void The_course_falls_back_to_the_localizers_own_variation_for_another_localizer()
    {
        var runway = new Runway { ILSFreq = 109.50, ILSCourseMag = 172 };

        Assert.Equal(171, IlsGuidanceText.CourseFor(Ils(), runway, fallbackMagVar: 0));
        Assert.Equal(171, IlsGuidanceText.CourseFor(Ils(), null, fallbackMagVar: 0));
    }

    [Fact]
    public void An_ils_report_says_one_course_as_its_runway_heading()
    {
        string text = IlsGuidanceText.OnCenterline(Ils(), 172, 6.2, glideslopeDeviationFeet: -120);

        Assert.Equal("6.2 nautical miles from threshold, on centerline. 120 feet below glideslope. Runway heading 172.", text);
    }

    // ---- Localizer, LDA, IGS ------------------------------------------------------------

    [Fact]
    public void A_localizer_is_named_ends_with_its_course_and_has_no_glideslope_line()
    {
        string text = IlsGuidanceText.OnCenterline(Localizer(), 152, 6.2, glideslopeDeviationFeet: 140);

        Assert.Equal("Localizer 108.70, course 152. 6.2 nautical miles from threshold, on centerline. Localizer course 152.", text);
    }

    [Fact]
    public void An_lda_never_calls_its_course_the_runway_heading()
    {
        string text = IlsGuidanceText.OnCenterline(Lda(), 147, 5.0, glideslopeDeviationFeet: null);

        Assert.Equal("LDA 111.35, course 147. 5.0 nautical miles from threshold, on centerline. LDA course 147.", text);
        Assert.DoesNotContain("Runway heading", text);
    }

    [Fact]
    public void An_igs_with_a_glideslope_keeps_its_glideslope_line()
    {
        string text = IlsGuidanceText.OnCenterline(Igs(), 89, 5.0, glideslopeDeviationFeet: 60);

        Assert.Equal("IGS 111.90, course 89. 5.0 nautical miles from threshold, on centerline. 60 feet above glideslope. IGS course 89.", text);
    }

    // ---- Range warning ----------------------------------------------------------------

    [Fact]
    public void The_range_warning_names_the_aid()
    {
        string text = IlsGuidanceText.OffCenterline(Localizer(), 152, 24.0, 1.5, 4, left: true, null, 100, 115, 130);

        Assert.Contains("Warning: Localizer signal range is 18 nautical miles. ", text);
    }

    [Fact]
    public void The_range_warning_leaves_out_the_category()
    {
        string text = IlsGuidanceText.OffCenterline(Ils(type: "3"), 172, 24.0, 1.5, 4, left: true, null, 100, 115, 130);

        Assert.StartsWith("Warning: ILS signal range is 18 nautical miles. ", text);
    }

    // ---- Far side of the threshold ------------------------------------------------------

    [Fact]
    public void Behind_the_runway_beyond_range_gives_distance_and_threshold_bearing()
    {
        string text = IlsGuidanceText.Behind(Ils(), 172, "18", 31.4, thresholdBearingMag: 8);

        Assert.Equal("Warning: ILS signal range is 18 nautical miles. " +
                     "Opposite side of runway 18, 31.4 nautical miles from threshold. Threshold bearing 008.", text);
    }

    [Fact]
    public void Behind_the_runway_within_range_says_to_extend_outbound_on_the_course_reversed()
    {
        string text = IlsGuidanceText.Behind(Ils(), 172, "18", 3.2, thresholdBearingMag: 8);

        Assert.Equal("Opposite side of runway 18, 3.2 nautical miles from threshold. Fly heading 352 to extend outbound.", text);
    }

    // ---- Numbers ----------------------------------------------------------------------

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void Every_sentence_reads_numbers_with_a_dot_under_a_comma_decimal_format(string culture)
    {
        UnderCulture(culture, () =>
        {
            string[] texts =
            {
                IlsGuidanceText.OnCenterline(Ils(), 172, 6.2, -35.6),
                IlsGuidanceText.OnCenterline(Localizer(), 152, 24.5, null),
                IlsGuidanceText.OffCenterline(Ils(), 172, 12.3, 0.4, 2, left: false, -35.6, 140, 155, 160),
                IlsGuidanceText.Behind(Localizer(), 152, "16", 3.2, 152),
                IlsGuidanceText.Behind(Ils(), 172, "18", 31.4, 8),
            };

            foreach (string text in texts)
                Assert.DoesNotMatch(@"\d,\d", text);
            Assert.Contains("12.3 nautical miles from threshold, 0.4 nautical miles right of centerline", texts[2]);
            Assert.StartsWith("Localizer 108.70, course 152. Warning: Localizer signal range", texts[1]);
            Assert.Contains("31.4 nautical miles from threshold. Threshold bearing 008.", texts[4]);
        });
    }
}

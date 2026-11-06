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
    // phase, distance nm, cross-track nm (+ right), altitude error ft (+ high), target fpm, wheel height ft.
    [Theory]
    [InlineData("OnFinal", 6.2, 0.0, 40, -750, 0, "On final, 6.2 miles. On the centreline. 40 feet high, target 750 down.")]
    [InlineData("OnFinal", 2.1, -0.1, 10, -700, 0, "On final, 2.1 miles. 0.1 miles left of the centreline. On path, target 700 down.")]
    [InlineData("Flare", 0, 0.0, 0, 0, 20.4, "Flare, 20 feet. On the centreline.")]
    [InlineData("Starting", 0, 0, 0, 0, 0, "Visual guidance starting.")]
    [InlineData("Deciding", 12.4, 3.0, 0, 0, 0, "Visual guidance starting, waiting for wings level.")]
    [InlineData("Touchdown", 0, 0.06, 0, 0, 0, "Touchdown. 0.1 miles right of the centreline.")]
    [InlineData("OnFinal", 3.0, 0.0, 29.9, -724, 0, "On final, 3.0 miles. On the centreline. On path, target 700 down.")]
    [InlineData("Flare", 0, 0.0, 0, 0, -3, "Flare, 0 feet. On the centreline.")]
    public void Each_phase_reads_its_sentence(string phase, double distanceNm, double crossTrackNm, double altitudeErrorFt,
        double targetFpm, double wheelHeightFt, string expected)
    {
        var status = new VisualGuidanceStatus(Enum.Parse<VisualGuidanceReadoutPhase>(phase), distanceNm, crossTrackNm,
            altitudeErrorFt, targetFpm, wheelHeightFt);
        Assert.Equal(expected, VisualGuidanceStatusText.Compose(status));
    }

    // Joining: where the aircraft will be lined up on final, the intercept (to 5°), the time to the turn (to 5 s).
    // No vertical half: in joining the altitude is ATC's or the pilot's.
    [Fact]
    public void Joining_says_where_it_joins_the_intercept_and_the_turn()
        => Assert.Equal("Joining final 6.4 miles out. 30 degree intercept, turn in about 85 seconds.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 3.0, join: 6.44, intercept: 31, seconds: 85.3, altErr: 600, fpm: -700));

    [Fact]
    public void Joining_with_no_time_to_the_turn_leaves_it_out()
        => Assert.Equal("Joining final 8.8 miles out. 15 degree intercept.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 3.0, join: 8.8, intercept: 15, seconds: null));

    [Fact]
    public void Joining_on_a_heading_that_does_not_cross_says_where_final_is()
        => Assert.Equal("Joining final. Heading does not cross final. Final 2.6 miles to your left.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 2.6, crosses: false, finalToTheRight: false));

    [Fact]
    public void Joining_in_the_turn_says_the_sideways_position()
        => Assert.Equal("Joining final, turning onto final. 0.2 miles right of final.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 0.24, turning: true));

    [Fact]
    public void Vectoring_says_where_it_joins_the_side_the_turn_and_the_vertical()
        => Assert.Equal("Vectoring to final, joining 6.2 miles out. 2.0 miles right of final, turn in about 85 seconds. 2,260 feet high, target 1,500 down.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 2.0, join: 6.18, intercept: 30, seconds: 85, altErr: 2258, fpm: -1500));

    [Fact]
    public void Vectoring_with_no_time_to_the_turn_leaves_it_out()
        => Assert.Equal("Vectoring to final, joining 6.0 miles out. 2.0 miles left of final. 100 feet low, target level.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -2.0, join: 6.0, seconds: null, altErr: -100, fpm: 0));

    [Fact]
    public void Vectoring_in_the_turn_says_the_side_and_the_vertical()
        => Assert.Equal("Vectoring to final, turning onto final. On the centreline. On path, target 750 down.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 0.04, turning: true, altErr: 10, fpm: -750));

    // The pattern's downwind and base: the leg, where the aircraft will be lined up, the side, and on the
    // base the time to the turn onto final. No vertical half until the vertical path (stage 5): there the
    // pitch tone sits on the aircraft's own pitch, so the old law's target is not what the tone shows.
    [Fact]
    public void Pattern_downwind_says_the_leg_where_it_joins_and_the_side()
        => Assert.Equal("Vectoring to final, downwind, joining 6.0 miles out. 3.0 miles right of final.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 3.0, join: 6.0, leg: VisualGuidanceLegKind.Downwind,
                altErr: 2258, fpm: -1500));

    [Fact]
    public void Pattern_base_says_the_turn()
        => Assert.Equal("Vectoring to final, base, joining 6.0 miles out. 1.5 miles right of final, turn in about 40 seconds.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 1.5, join: 6.0, seconds: 40, leg: VisualGuidanceLegKind.Base,
                altErr: 2258, fpm: -1500));

    [Fact]
    public void Pattern_base_with_no_time_to_the_turn_leaves_it_out()
        => Assert.Equal("Vectoring to final, base, joining 6.0 miles out. 2.0 miles left of final.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -2.0, join: 6.0, seconds: null, leg: VisualGuidanceLegKind.Base));

    // A tiny intercept or time still reads as at least 5.
    [Fact]
    public void Intercept_and_time_read_at_least_five()
        => Assert.Equal("Joining final 7.0 miles out. 5 degree intercept, turn in about 5 seconds.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 0.5, join: 7.0, intercept: 2, seconds: 1));

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void Readout_numbers_are_culture_invariant(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var status = new VisualGuidanceStatus(VisualGuidanceReadoutPhase.OnFinal, 6.2, -0.26, 1234, -1234, 0);
            Assert.Equal("On final, 6.2 miles. 0.3 miles left of the centreline. 1,230 feet high, target 1,250 down.",
                VisualGuidanceStatusText.Compose(status));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void New_readouts_are_culture_invariant(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("Joining final 12.3 miles out. 45 degree intercept, turn in about 1,235 seconds.",
                Compose(VisualGuidanceReadoutPhase.Joining, cross: 1.5, join: 12.34, intercept: 45, seconds: 1234));
            Assert.Equal("Vectoring to final, joining 6.5 miles out. 1.5 miles left of final, turn in about 40 seconds. 1,230 feet high, target 1,250 down.",
                Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -1.46, join: 6.46, seconds: 41, altErr: 1234, fpm: -1234));
            Assert.Equal("Vectoring to final, base, joining 12.3 miles out. 2.5 miles left of final, turn in about 1,235 seconds.",
                Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -2.46, join: 12.34, seconds: 1234, leg: VisualGuidanceLegKind.Base));
            Assert.Equal("Vectoring to final, downwind, joining 6.5 miles out. 1.5 miles right of final.",
                Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 1.46, join: 6.46, leg: VisualGuidanceLegKind.Downwind));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static string Compose(VisualGuidanceReadoutPhase phase, double cross, double join = 0, double intercept = 30,
        double? seconds = null, bool crosses = true, bool turning = false, bool finalToTheRight = false,
        double altErr = 0, double fpm = 0, VisualGuidanceLegKind leg = VisualGuidanceLegKind.Final)
        => VisualGuidanceStatusText.Compose(new VisualGuidanceStatus(phase, 10.0, cross, altErr, fpm, 0, Leg: leg,
            InterceptDeg: intercept, Turning: turning, CrossesFinal: crosses, JoinAlongNm: join, SecondsToTurn: seconds,
            FinalToTheRight: finalToTheRight));
}

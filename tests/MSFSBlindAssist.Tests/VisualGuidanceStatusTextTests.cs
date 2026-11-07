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
    [InlineData("Flare", 0, 0.0, 0, -373, 20.4, "Flare, 20 feet. On the centreline. Target 350 down.")]
    [InlineData("Starting", 0, 0, 0, 0, 0, "Visual guidance starting.")]
    [InlineData("Deciding", 12.4, 3.0, 0, 0, 0, "Visual guidance starting, waiting for wings level.")]
    [InlineData("Touchdown", 0, 0.06, 0, 0, 0, "Touchdown. 0.1 miles right of the centreline.")]
    [InlineData("OnFinal", 3.0, 0.0, 29.9, -724, 0, "On final, 3.0 miles. On the centreline. On path, target 700 down.")]
    [InlineData("Flare", 0, 0.0, 0, -150, -3, "Flare, 0 feet. On the centreline. Target 150 down.")]
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
        => Assert.Equal("Joining a 6.4 mile final. 30 degree intercept, turn in about 85 seconds.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 3.0, join: 6.44, intercept: 31, seconds: 85.3, altErr: 600, fpm: -700));

    // Joining (stage 5): the path's altitude at the join point and how far above or below it the aircraft
    // is; no target, since the altitude is ATC's or the pilot's (plan decision 8).
    [Theory]
    [InlineData(297, "None", "Path there 2,470 feet, you're 300 high.")]
    [InlineData(-204, "None", "Path there 2,470 feet, you're 200 low.")]
    [InlineData(29.9, "None", "Path there 2,470 feet, you're on it.")]
    [InlineData(1704, "HighAtFinal", "Path there 2,470 feet, you'll be 1,700 feet high at final.")]
    public void Joining_says_the_path_there(double joinErr, string warning, string vertical)
        => Assert.Equal("Joining a 6.4 mile final. 30 degree intercept, turn in about 85 seconds. " + vertical,
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 3.0, join: 6.44, intercept: 31, seconds: 85.3, joinPath: 2468,
                joinErr: joinErr, warning: Enum.Parse<VisualGuidanceVerticalWarning>(warning)));

    [Fact]
    public void Joining_in_the_turn_says_the_path_there()
        => Assert.Equal("Joining final, turning onto final. Centreline 0.3 miles to your left. Path there 2,470 feet, you're 300 high.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 0.26, turning: true, bearing: -60, joinPath: 2468, joinErr: 297));

    [Fact]
    public void Joining_on_a_heading_that_does_not_cross_says_no_height()
        => Assert.Equal("Joining final. Heading does not cross final. Centreline 2.6 miles to your left.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 2.6, crosses: false, bearing: -90, joinPath: 2468, joinErr: 297));

    // Where the centreline is from the cockpit, joining and vectoring (the owner, 2026-11-07: "6.1 miles left of
    // final" was heard as 6.1 miles of final left to fly): its bearing from the track, + right; within 30° of the
    // nose "ahead", within 30° of the tail "behind". The owner's downwind, flown away from the runway on its left.
    [Theory]
    [InlineData(-90, "to your left")]
    [InlineData(90, "to your right")]
    [InlineData(0, "ahead")]
    [InlineData(30, "ahead")]
    [InlineData(-30, "ahead")]
    [InlineData(31, "to your right")]
    [InlineData(-149, "to your left")]
    [InlineData(150, "behind")]
    [InlineData(-150, "behind")]
    [InlineData(180, "behind")]
    public void The_centreline_is_placed_from_the_cockpit(double bearing, string where)
        => Assert.Equal($"Vectoring to a 6.0 mile final, downwind. Centreline 6.1 miles {where}.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -6.1, join: 6.0, leg: VisualGuidanceLegKind.Downwind, bearing: bearing));

    [Fact]
    public void Without_a_bearing_the_centreline_has_no_side()
        => Assert.Equal("Vectoring to a 6.0 mile final, downwind. 6.1 miles from the centreline.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -6.1, join: 6.0, leg: VisualGuidanceLegKind.Downwind));

    // On final the warnings replace the height: "Too high" and "Too low" with the height against the path.
    [Fact]
    public void On_final_too_high_says_so()
        => Assert.Equal("On final, 3.0 miles. On the centreline. Too high, 600 feet above the path, target 1,250 down.",
            VisualGuidanceStatusText.Compose(new VisualGuidanceStatus(VisualGuidanceReadoutPhase.OnFinal, 3.0, 0.0, 604, -1247, 0,
                Warning: VisualGuidanceVerticalWarning.TooHigh)));

    [Fact]
    public void On_final_too_low_says_so()
        => Assert.Equal("On final, 3.0 miles. On the centreline. Too low, 400 feet below the path, target level.",
            VisualGuidanceStatusText.Compose(new VisualGuidanceStatus(VisualGuidanceReadoutPhase.OnFinal, 3.0, 0.0, -404, 0, 0,
                Warning: VisualGuidanceVerticalWarning.TooLow)));

    // What BA says on its own about the path: the three warnings and, once established, the mile calls.
    [Fact]
    public void Spoken_path_lines()
    {
        Assert.Equal("You'll be 1,700 feet high at final", VisualGuidanceStatusText.HighAtFinalLine(1704));
        Assert.Equal("Too high, 600 feet above the path", VisualGuidanceStatusText.TooHighLine(604));
        Assert.Equal("Too low, 400 feet below the path", VisualGuidanceStatusText.TooLowLine(-404));
        Assert.Equal("5 miles out, on path", VisualGuidanceStatusText.MileLine(5, 12));
        Assert.Equal("3 miles out, 80 feet high", VisualGuidanceStatusText.MileLine(3, 83));
        Assert.Equal("1 mile out, 40 feet low", VisualGuidanceStatusText.MileLine(1, -38));
        Assert.Equal("3 miles out", VisualGuidanceStatusText.MileLine(3, null));
    }

    // The flare's target (stage 6) has a thousands separator on a fast descent: a comma, whatever the PC's culture.
    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void Flare_readout_is_culture_invariant(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("Flare, 20 feet. On the centreline. Target 1,250 down.",
                VisualGuidanceStatusText.Compose(new VisualGuidanceStatus(VisualGuidanceReadoutPhase.Flare, 0, 0, 0, -1234, 20.4)));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void Vertical_readouts_are_culture_invariant(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Assert.Equal("Joining a 12.3 mile final. 45 degree intercept. Path there 12,470 feet, you'll be 1,230 feet high at final.",
                Compose(VisualGuidanceReadoutPhase.Joining, cross: 1.5, join: 12.34, intercept: 45, joinPath: 12468, joinErr: 1234,
                    warning: VisualGuidanceVerticalWarning.HighAtFinal));
            Assert.Equal("Vectoring to a 12.3 mile final, base. Centreline 2.5 miles ahead. Path there 12,470 feet, you're 1,230 low, target 1,250 down.",
                Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -2.46, join: 12.34, leg: VisualGuidanceLegKind.Base, fpm: -1234,
                    bearing: 0, joinPath: 12468, joinErr: -1234));
            Assert.Equal("On final, 6.2 miles. On the centreline. Too high, 1,230 feet above the path, target 1,250 down.",
                VisualGuidanceStatusText.Compose(new VisualGuidanceStatus(VisualGuidanceReadoutPhase.OnFinal, 6.2, 0, 1234, -1234, 0,
                    Warning: VisualGuidanceVerticalWarning.TooHigh)));
            Assert.Equal("You'll be 1,230 feet high at final", VisualGuidanceStatusText.HighAtFinalLine(1234));
            Assert.Equal("Too high, 1,230 feet above the path", VisualGuidanceStatusText.TooHighLine(1234));
            Assert.Equal("Too low, 1,230 feet below the path", VisualGuidanceStatusText.TooLowLine(-1234));
            Assert.Equal("5 miles out, 1,230 feet high", VisualGuidanceStatusText.MileLine(5, 1234));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Joining_with_no_time_to_the_turn_leaves_it_out()
        => Assert.Equal("Joining an 8.8 mile final. 15 degree intercept.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 3.0, join: 8.8, intercept: 15, seconds: null));

    // "a 6.4 mile final" in the pilot's words (the owner, 2026-11-06), with "an" before eight, eleven and eighteen.
    [Theory]
    [InlineData(6.44, "Joining a 6.4 mile final.")]
    [InlineData(8.0, "Joining an 8.0 mile final.")]
    [InlineData(11.2, "Joining an 11.2 mile final.")]
    [InlineData(18.04, "Joining an 18.0 mile final.")]
    [InlineData(10.8, "Joining a 10.8 mile final.")]
    [InlineData(1.1, "Joining a 1.1 mile final.")]
    public void The_length_of_final_takes_the_right_article(double join, string start)
        => Assert.StartsWith(start, Compose(VisualGuidanceReadoutPhase.Joining, cross: 3.0, join: join, intercept: 30, seconds: null));

    [Fact]
    public void Joining_on_a_heading_that_does_not_cross_says_where_final_is()
        => Assert.Equal("Joining final. Heading does not cross final. Centreline 2.6 miles to your left.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 2.6, crosses: false, bearing: -90));

    [Fact]
    public void Joining_in_the_turn_says_the_sideways_position()
        => Assert.Equal("Joining final, turning onto final. Centreline 0.2 miles to your left.",
            Compose(VisualGuidanceReadoutPhase.Joining, cross: 0.24, turning: true, bearing: -60));

    // Vectoring, every leg (stage 5): the path's altitude at the join point (MSL, to 10 ft), how far above or
    // below it the aircraft is now, and BA's target (plan decision 8).
    [Fact]
    public void Vectoring_says_where_it_joins_the_side_the_turn_and_the_path_there()
        => Assert.Equal("Vectoring to a 6.2 mile final. Centreline 2.0 miles to your left, turn in about 85 seconds. Path there 2,030 feet, you're 400 high, target 800 down.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 2.0, join: 6.18, intercept: 30, seconds: 85, bearing: -60, altErr: 2258, fpm: -812,
                joinPath: 2034, joinErr: 404));

    [Fact]
    public void Vectoring_with_no_time_to_the_turn_leaves_it_out()
        => Assert.Equal("Vectoring to a 6.0 mile final. Centreline 2.0 miles to your right. Path there 2,030 feet, you're 100 low, target level.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -2.0, join: 6.0, seconds: null, bearing: 90, altErr: -100, fpm: 0,
                joinPath: 2034, joinErr: -96));

    [Fact]
    public void Vectoring_in_the_turn_says_the_side_and_the_path_there()
        => Assert.Equal("Vectoring to final, turning onto final. On the centreline. Path there 2,030 feet, you're on it, target 750 down.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 0.04, turning: true, altErr: 10, fpm: -750, joinPath: 2034, joinErr: 29));

    // The pattern's downwind and base: the leg, where the aircraft will be lined up, the side, on the base the
    // time to the turn onto final, then the path there.
    [Fact]
    public void Pattern_downwind_says_the_leg_where_it_joins_the_side_and_the_path_there()
        => Assert.Equal("Vectoring to a 6.0 mile final, downwind. Centreline 3.0 miles to your right. Path there 2,030 feet, you're 2,260 high, target level.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 3.0, join: 6.0, leg: VisualGuidanceLegKind.Downwind, bearing: 90,
                altErr: 2258, fpm: 0, joinPath: 2034, joinErr: 2258));

    // Without a join point there is no vertical half.
    [Fact]
    public void Pattern_downwind_without_a_join_point_says_no_height()
        => Assert.Equal("Vectoring to a 6.0 mile final, downwind. Centreline 3.0 miles to your right.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 3.0, join: 6.0, leg: VisualGuidanceLegKind.Downwind, bearing: 90,
                altErr: 2258, fpm: -1500));

    // On the base the aircraft flies at the centreline: it is ahead.
    [Fact]
    public void Pattern_base_says_the_turn()
        => Assert.Equal("Vectoring to a 6.0 mile final, base. Centreline 1.5 miles ahead, turn in about 40 seconds.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 1.5, join: 6.0, seconds: 40, leg: VisualGuidanceLegKind.Base, bearing: 0,
                altErr: 2258, fpm: -1500));

    [Fact]
    public void Pattern_base_with_no_time_to_the_turn_leaves_it_out()
        => Assert.Equal("Vectoring to a 6.0 mile final, base. Centreline 2.0 miles to your right.",
            Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -2.0, join: 6.0, seconds: null, leg: VisualGuidanceLegKind.Base, bearing: 45));

    // A tiny intercept or time still reads as at least 5.
    [Fact]
    public void Intercept_and_time_read_at_least_five()
        => Assert.Equal("Joining a 7.0 mile final. 5 degree intercept, turn in about 5 seconds.",
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
            Assert.Equal("Joining a 12.3 mile final. 45 degree intercept, turn in about 1,235 seconds.",
                Compose(VisualGuidanceReadoutPhase.Joining, cross: 1.5, join: 12.34, intercept: 45, seconds: 1234));
            Assert.Equal("Vectoring to a 6.5 mile final. Centreline 1.5 miles to your right, turn in about 40 seconds. Path there 2,030 feet, you're 1,230 high, target 1,250 down.",
                Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -1.46, join: 6.46, seconds: 41, bearing: 60, altErr: 1234, fpm: -1234,
                    joinPath: 2034, joinErr: 1234));
            Assert.Equal("Vectoring to a 12.3 mile final, base. Centreline 2.5 miles ahead, turn in about 1,235 seconds.",
                Compose(VisualGuidanceReadoutPhase.Vectoring, cross: -2.46, join: 12.34, seconds: 1234, leg: VisualGuidanceLegKind.Base, bearing: 0));
            Assert.Equal("Vectoring to a 6.5 mile final, downwind. Centreline 1.5 miles to your right.",
                Compose(VisualGuidanceReadoutPhase.Vectoring, cross: 1.46, join: 6.46, leg: VisualGuidanceLegKind.Downwind, bearing: 90));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static string Compose(VisualGuidanceReadoutPhase phase, double cross, double join = 0, double intercept = 30,
        double? seconds = null, bool crosses = true, bool turning = false, double bearing = double.NaN,
        double altErr = 0, double fpm = 0, VisualGuidanceLegKind leg = VisualGuidanceLegKind.Final,
        double? joinPath = null, double joinErr = 0, VisualGuidanceVerticalWarning warning = VisualGuidanceVerticalWarning.None)
        => VisualGuidanceStatusText.Compose(new VisualGuidanceStatus(phase, 10.0, cross, altErr, fpm, 0, Leg: leg,
            InterceptDeg: intercept, Turning: turning, CrossesFinal: crosses, JoinAlongNm: join, SecondsToTurn: seconds,
            CentrelineBearingDeg: bearing, JoinPathMslFt: joinPath, JoinHeightErrorFt: joinErr, Warning: warning));
}

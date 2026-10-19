// The C172's speeds (output Shift+1 to 6 and the take-off roll's "Rotate") and its landing numbers. Each
// speed is the flight model's own (Simplane.getDesignSpeeds, read live 2026-10-18) or, where the flight
// model sets none, the 172S POH's; the landing profile's datum height was measured parked.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class CessnaC172SpeedsTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_speed_keys_read_the_same_in_every_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);

            Assert.Equal("Best glide 68 knots.", CessnaC172Speeds.DescribeBestGlide());
            // Every number with its unit, every flap setting in words (the owner's review, 2026-10-18).
            Assert.Equal("Rotate 55 knots. Best angle of climb 62 knots. Best rate of climb 74 knots.",
                CessnaC172Speeds.DescribeTakeoffSpeeds());
            Assert.Equal("Approach 60 to 70 knots with full flaps. Short field approach 61 knots with full flaps. " +
                "Landing without flaps, 65 to 75 knots. Best glide 68 knots.",
                CessnaC172Speeds.DescribeLandingSpeeds());
            Assert.Equal("Flap limits: 110 knots with 10 degrees, 85 knots with 20 degrees or full.",
                CessnaC172Speeds.DescribeFlapLimits());
            Assert.Equal("Stall 48 knots with flaps up, 40 knots with full flaps. Manoeuvring speed 90 knots at this weight. " +
                "Maximum structural cruise 129 knots. Never exceed 163 knots.", CessnaC172Speeds.DescribeLimits(2079.8));
            Assert.Equal("Stall 48 knots with flaps up, 40 knots with full flaps. Manoeuvring speed 105 knots at maximum weight. " +
                "Maximum structural cruise 129 knots. Never exceed 163 knots.", CessnaC172Speeds.DescribeLimits(null));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>
    /// The POH's table (105 kt at 2550 lb, 98 at 2200, 90 at 1900), never a figure for a heavier weight
    /// than the aircraft's: the maximum-weight 105 overstates it on a lighter aircraft.
    /// </summary>
    [Theory]
    [InlineData(2558, 105)]
    [InlineData(2550, 105)]
    [InlineData(2549, 98)]
    [InlineData(2200, 98)]
    [InlineData(2079.8, 90)]
    [InlineData(1900, 90)]
    [InlineData(1700, 90)]
    public void The_manoeuvring_speed_is_the_POH_figure_at_or_below_the_weight(double pounds, double knots)
    {
        Assert.Equal(knots, CessnaC172Speeds.ManoeuvringSpeed(pounds));
    }

    [Theory]
    [InlineData(0, "Flaps up. Limit with 10 degrees of flaps, 110 knots.")]
    [InlineData(1, "Flaps 10 degrees. Limit 110 knots.")]
    [InlineData(2, "Flaps 20 degrees. Limit 85 knots.")]
    [InlineData(3, "Flaps full. Limit 85 knots.")]
    public void Shift_6_names_the_limit_for_the_flaps_set_now(double handleIndex, string expected)
    {
        Assert.Equal(expected, CessnaC172Speeds.DescribeFlapLimitNow(handleIndex));
    }

    [Fact]
    public void The_take_off_roll_says_Rotate_once_at_the_flight_models_Vr()
    {
        var p = TakeoffRollCallouts.For(new CessnaC172G1000Definition());

        Assert.Equal(55.0, p.LowKts);
        Assert.Equal("Rotate", p.LowPhrase);
        Assert.Null(p.HighKts);
    }

    [Fact]
    public void The_landing_profile_is_a_light_singles_not_the_A320s()
    {
        var p = new CessnaC172G1000Definition().GetVisualGuidanceProfile();

        // PLANE ALT ABOVE GROUND parked (2026-10-18): 3.92 ft on wheels, 3.83 on skis.
        Assert.Equal(3.9, p.FlareAltitudeBiasFt);
        // FAA Airplane Flying Handbook: a light single rounds out 10-20 ft above the runway.
        Assert.InRange(p.FlareTriggerWheelHeightFt, 10.0, 20.0);
        // Above the 2.25 degree nose-up ground stance, so the mains touch first; under the A320's 6.
        Assert.InRange(p.FlareTargetPitchDeg, 3.0, 6.0);
        Assert.True(p.FlareHoldsEntrySink);
        // Touchdown is near 45-50 kt (VS0 40): the jets' 40 kt end would cut the rollout tone short.
        Assert.True(p.RolloutGuidanceEndKnots < CessnaC172Speeds.Vs0);
        Assert.InRange(p.ReferenceVrefKnots, CessnaC172Speeds.ApproachFullFlapLow, CessnaC172Speeds.ApproachFullFlapHigh);
    }

    [Theory]
    // The POH's full-flap approach (60-70 KIAS) on a 3 degree path: about 320-370 fpm.
    [InlineData(320.0)]
    [InlineData(370.0)]
    public void A_normal_approach_is_on_profile_until_the_round_out(double approachSinkFpm)
    {
        var p = new CessnaC172G1000Definition().GetVisualGuidanceProfile();
        // From the 50 ft engage down to the flare height the target is the sink the aircraft arrived
        // with, so the vertical tone stays silent through a normal approach.
        foreach (double h in new[] { 50.0, 30.0, 20.0 })
            Assert.Equal(-approachSinkFpm,
                LandingGuidanceLaws.FlareTargetVsFpm(h, p.FlareSinkTauSec, p.FlareTouchdownSinkFpm, approachSinkFpm), 6);
        Assert.Equal(-p.FlareTouchdownSinkFpm,
            LandingGuidanceLaws.FlareTargetVsFpm(0.0, p.FlareSinkTauSec, p.FlareTouchdownSinkFpm, approachSinkFpm), 6);
    }
}

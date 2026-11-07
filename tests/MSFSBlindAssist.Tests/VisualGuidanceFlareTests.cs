using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The flare's pure pieces (<see cref="VisualGuidanceFlare"/>), stage 6 of the visual guidance redesign:
/// 140 kt and the A320 default profile's 30 ft flare height (the backstop) unless a test says otherwise.
/// Heights are wheel heights; a sink rate is fpm + down; a descent angle is + down.
/// </summary>
public class VisualGuidanceFlareTests
{
    private const double Gs = 140.0, Backstop = 30.0, Touchdown = 150.0;
    private static readonly VisualGuidanceVerticalTuning T = VisualGuidanceVerticalTuning.Default;

    [Fact]
    public void Time_to_touchdown()
    {
        Assert.Equal(4.297, VisualGuidanceFlare.TimeToTouchdownS(53, 740), 0.01);
        Assert.Equal(double.PositiveInfinity, VisualGuidanceFlare.TimeToTouchdownS(30, 0));
        Assert.Equal(double.PositiveInfinity, VisualGuidanceFlare.TimeToTouchdownS(30, -100));
        Assert.Equal(0.0, VisualGuidanceFlare.TimeToTouchdownS(0, 740));
    }

    // 4.3 s out (FlyByWire's flare law starts there), and only under 100 ft.
    [Fact]
    public void The_flare_starts_four_seconds_out()
    {
        Assert.False(VisualGuidanceFlare.Starts(53.5, 740, Backstop, T));   // 4.34 s
        Assert.True(VisualGuidanceFlare.Starts(53.0, 740, Backstop, T));    // 4.30 s
        Assert.False(VisualGuidanceFlare.Starts(110, 2000, Backstop, T));   // 3.3 s, but over 100 ft
    }

    [Fact]
    public void Or_at_the_backstop_when_slow()
    {
        Assert.True(VisualGuidanceFlare.Starts(30.0, 300, Backstop, T));    // 6 s, at the flare height
        Assert.False(VisualGuidanceFlare.Starts(30.5, 300, Backstop, T));
    }

    // "Flare" 1 s before the flare starts and "Prepare to flare" 4 s before (the owner, 2026-11-06): at 742 fpm
    // the flare starts at 53 ft, "Flare" at 65.5 ft, "Prepare to flare" at 102.6 ft.
    [Fact]
    public void The_calls_come_one_and_four_seconds_before_the_flare()
    {
        Assert.Equal(1.0, VisualGuidanceFlare.TimeToStartS(65.54, 742, Backstop, T), 0.01);
        Assert.True(VisualGuidanceFlare.CallDue(65.4, 742, Backstop, T));
        Assert.False(VisualGuidanceFlare.CallDue(65.8, 742, Backstop, T));
        Assert.True(VisualGuidanceFlare.PrepareDue(102.5, 742, Backstop, T));
        Assert.False(VisualGuidanceFlare.PrepareDue(103.0, 742, Backstop, T));
    }

    // Slow, the flare starts at the flare height (30 ft): at 300 fpm "Flare" at 35 ft, "Prepare to flare" at 50.
    [Fact]
    public void Slow_the_calls_come_before_the_flare_height()
    {
        Assert.True(VisualGuidanceFlare.CallDue(35.0, 300, Backstop, T));
        Assert.False(VisualGuidanceFlare.CallDue(35.2, 300, Backstop, T));
        Assert.True(VisualGuidanceFlare.PrepareDue(50.0, 300, Backstop, T));
        Assert.False(VisualGuidanceFlare.PrepareDue(50.2, 300, Backstop, T));
    }

    // Fast, the flare starts at the band's top (100 ft) once touchdown is 4.3 s away: at 1,500 fpm "Flare" at 125 ft.
    [Fact]
    public void Fast_the_flare_starts_at_the_band_and_the_calls_come_before_it()
    {
        Assert.True(VisualGuidanceFlare.CallDue(125, 1500, Backstop, T));
        Assert.False(VisualGuidanceFlare.CallDue(126, 1500, Backstop, T));
        Assert.True(VisualGuidanceFlare.PrepareDue(200, 1500, Backstop, T));
        Assert.False(VisualGuidanceFlare.PrepareDue(201, 1500, Backstop, T));
    }

    [Fact]
    public void Climbing_never_starts_the_flare_and_a_started_one_is_now()
    {
        Assert.Equal(double.PositiveInfinity, VisualGuidanceFlare.TimeToStartS(80, -300, Backstop, T));
        Assert.Equal(double.PositiveInfinity, VisualGuidanceFlare.TimeToStartS(80, 0, Backstop, T));
        Assert.Equal(0.0, VisualGuidanceFlare.TimeToStartS(40, 740, Backstop, T));
    }

    // τ is chosen so the profile starts at the aircraft's own sink and eases to 150 fpm at touchdown.
    [Fact]
    public void The_profile_starts_at_the_current_descent()
    {
        double tau = VisualGuidanceFlare.TauS(53, 740, Touchdown);
        Assert.Equal(5.390, tau, 0.005);
        Assert.Equal(740.0, VisualGuidanceFlare.TargetSinkFpm(53, tau, Touchdown), 0.5);
        Assert.Equal(372.6, VisualGuidanceFlare.TargetSinkFpm(20, tau, Touchdown), 0.5);
        Assert.Equal(150.0, VisualGuidanceFlare.TargetSinkFpm(0, tau, Touchdown), 9);
    }

    // Already sinking 150 fpm or less: asked gently down at 150, never up.
    [Fact]
    public void A_floating_aircraft_is_asked_gently_down()
    {
        double tau = VisualGuidanceFlare.TauS(30, 120, Touchdown);
        Assert.Equal(double.PositiveInfinity, tau);
        Assert.Equal(150.0, VisualGuidanceFlare.TargetSinkFpm(30, tau, Touchdown), 9);
    }

    [Fact]
    public void Descent_angle()
    {
        Assert.Equal(2.988, VisualGuidanceFlare.DescentDeg(740, Gs), 0.005);
        Assert.Equal(0.606, VisualGuidanceFlare.DescentDeg(150, Gs), 0.005);
    }

    // A steep runway or a tailwind: the profile is a sink rate, so a faster descent starts it higher
    // and says the word earlier.
    [Fact]
    public void A_faster_descent_starts_higher()
    {
        Assert.True(VisualGuidanceFlare.Starts(71.0, 1000, Backstop, T));   // 4.26 s
        Assert.False(VisualGuidanceFlare.Starts(72.5, 1000, Backstop, T));  // 4.35 s
        Assert.True(VisualGuidanceFlare.CallDue(41.6, 1000, Backstop, T));
    }

    // A slew or a pause: finite numbers, an infinite time to touchdown, the backstop still starting it.
    [Fact]
    public void A_stopped_or_level_aircraft_gets_finite_flare_numbers()
    {
        double sink = VisualGuidanceFlare.SinkFpm(-3, 0);
        Assert.True(double.IsFinite(sink));
        Assert.Equal(-Math.Tan(-3 * Math.PI / 180) * 40 * 101.269, sink, 0.01);   // at 40 kt
        Assert.True(double.IsFinite(VisualGuidanceFlare.DescentDeg(150, 0)));
        Assert.True(VisualGuidanceFlare.Starts(29, 0, Backstop, T));
        Assert.False(VisualGuidanceFlare.Starts(40, 0, Backstop, T));
        Assert.Equal(double.PositiveInfinity, VisualGuidanceFlare.TauS(0, 740, Touchdown));
    }

    // FlyByWire's flare also corrects for the sink error: descending 1.5° steeper than the target asks
    // 1.5° more nose-up at gain 1. Only that way: shallower (floating) or climbing (a balloon), the cue is
    // the target's attitude, asking gently back down, never a push (the stage 6 final review). On the
    // target, or at gain 0, the cue is the level attitude minus the target's angle.
    [Fact]
    public void The_cue_corrects_for_the_descent_against_the_target()
    {
        Assert.Equal(5.0, VisualGuidanceFlare.CueDeg(5.0, 1.5, 3.0, 1.0), 9);
        Assert.Equal(3.5, VisualGuidanceFlare.CueDeg(5.0, 1.5, 1.5, 1.0), 9);
        Assert.Equal(3.5, VisualGuidanceFlare.CueDeg(5.0, 1.5, 0.5, 1.0), 9);    // floating
        Assert.Equal(3.5, VisualGuidanceFlare.CueDeg(5.0, 1.5, -0.8, 1.0), 9);   // climbing
        Assert.Equal(3.5, VisualGuidanceFlare.CueDeg(5.0, 1.5, 3.0, 0.0), 9);
        Assert.Equal(3.5, VisualGuidanceFlare.CueDeg(5.0, 1.5, double.NaN, 1.0), 9);   // no reading: no correction
    }

    [Fact]
    public void Sink_rate_from_the_flight_path()
    {
        Assert.Equal(742.0, VisualGuidanceFlare.SinkFpm(-2.997, Gs), 1.0);
        Assert.True(VisualGuidanceFlare.SinkFpm(1.0, Gs) < 0);   // climbing: a negative sink
    }
}

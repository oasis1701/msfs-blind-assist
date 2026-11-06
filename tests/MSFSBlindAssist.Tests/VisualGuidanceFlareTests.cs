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

    // "Flare" at 2.5 s (30 ft at 740 fpm), or at the flare height, whichever first.
    [Fact]
    public void The_word_comes_two_and_a_half_seconds_out()
    {
        Assert.False(VisualGuidanceFlare.CallDue(31.0, 740, Backstop, T));  // 2.51 s
        Assert.True(VisualGuidanceFlare.CallDue(30.8, 740, Backstop, T));
        Assert.True(VisualGuidanceFlare.CallDue(41.6, 1000, Backstop, T));  // 2.50 s, above the flare height
        Assert.False(VisualGuidanceFlare.CallDue(31, 300, Backstop, T));
        Assert.True(VisualGuidanceFlare.CallDue(30, 300, Backstop, T));
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

    [Fact]
    public void Sink_rate_from_the_flight_path()
    {
        Assert.Equal(742.0, VisualGuidanceFlare.SinkFpm(-2.997, Gs), 1.0);
        Assert.True(VisualGuidanceFlare.SinkFpm(1.0, Gs) < 0);   // climbing: a negative sink
    }
}

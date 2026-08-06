// Characterization tests for the 1-knot docking ground-speed callout cadence.
// Density matters here: the callout fires while the steering tone and proximity
// beeper are already sounding, so it must speak on genuine 1-knot changes and
// stay silent otherwise — flutter would be worse than no feature.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class DockingSpeedCalloutTests
{
    [Fact]
    public void First_sample_always_speaks_so_the_pilot_starts_with_a_number()
    {
        var c = new DockingSpeedCallout();
        Assert.Equal("3 knots.", c.Update(3.0));
    }

    [Fact]
    public void Unchanged_speed_stays_silent()
    {
        var c = new DockingSpeedCallout();
        c.Update(3.0);
        Assert.Null(c.Update(3.0));
        Assert.Null(c.Update(3.1));
        Assert.Null(c.Update(2.9));
    }

    [Fact]
    public void Each_whole_knot_is_announced_once_as_speed_builds()
    {
        var c = new DockingSpeedCallout();
        Assert.Equal("1 knot.", c.Update(1.0));
        Assert.Equal("2 knots.", c.Update(2.0));
        Assert.Equal("3 knots.", c.Update(3.0));
        Assert.Equal("4 knots.", c.Update(4.0));
    }

    [Fact]
    public void Sitting_on_a_boundary_cannot_flutter()
    {
        // The live failure mode: speed hovering at x.5 announcing "1 / 2 / 1 / 2".
        var c = new DockingSpeedCallout();
        c.Update(1.0);
        Assert.Null(c.Update(1.5));   // rounds to 2 but is only 0.5 from the last value
        Assert.Null(c.Update(1.4));
        Assert.Null(c.Update(1.55));
        Assert.Equal("2 knots.", c.Update(1.7));   // genuinely past the deadband
    }

    [Fact]
    public void Slowing_down_is_announced_too_not_just_speeding_up()
    {
        var c = new DockingSpeedCallout();
        c.Update(4.0);
        Assert.Equal("3 knots.", c.Update(3.0));
        Assert.Equal("2 knots.", c.Update(2.0));
    }

    [Fact]
    public void Sitting_on_a_boundary_cannot_flutter_coming_down_either()
    {
        // Mirror of the test above. The deadband is measured from the last ANNOUNCED
        // integer, so it has to hold in both directions — only the ascending half was
        // pinned, and a one-sided guard is exactly the kind of thing a later "simplify"
        // pass breaks without failing a test.
        var c = new DockingSpeedCallout();
        c.Update(1.0);
        Assert.Equal("2 knots.", c.Update(1.7));
        Assert.Null(c.Update(1.55));
        Assert.Null(c.Update(1.45));   // rounds to 1 but is only 0.55 from the last value
        Assert.Equal("1 knot.", c.Update(1.3));
    }

    [Fact]
    public void The_deadband_is_measured_from_the_last_announced_value_not_the_boundary()
    {
        // Pinned against HysteresisKts rather than a literal, and with a margin either
        // side rather than sitting exactly on it — "1.0 + 0.6" is not representable, so
        // an exact-boundary assertion would be pinning a floating-point coincidence.
        const double h = DockingSpeedCallout.HysteresisKts;
        var below = new DockingSpeedCallout();
        below.Update(1.0);
        Assert.Null(below.Update(1.0 + h - 0.01));

        var above = new DockingSpeedCallout();
        above.Update(1.0);
        Assert.Equal("2 knots.", above.Update(1.0 + h + 0.01));
    }

    [Fact]
    public void Zero_says_stopped_rather_than_zero_knots()
    {
        var c = new DockingSpeedCallout();
        c.Update(2.0);
        Assert.Equal("Stopped.", c.Update(0.0));
    }

    [Fact]
    public void Moving_off_again_after_a_stop_is_announced()
    {
        // The pilot stops mid-approach, is told "Stopped.", then creeps forward: the next
        // knot has to arrive, or a stationary aircraft that starts rolling gets no number.
        var c = new DockingSpeedCallout();
        c.Update(2.0);
        Assert.Equal("Stopped.", c.Update(0.0));
        Assert.Equal("1 knot.", c.Update(1.0));
    }

    [Fact]
    public void Reset_rearms_for_a_retry_after_backing_up()
    {
        var c = new DockingSpeedCallout();
        c.Update(2.0);
        Assert.Null(c.Update(2.0));
        c.Reset();
        Assert.Equal("2 knots.", c.Update(2.0));   // fresh approach speaks again
    }

    [Fact]
    public void Arm_primes_silently_so_engage_does_not_speak_over_its_own_callout()
    {
        // The engage frame is the one frame docking is already talking (VDGS type,
        // distance to stop, steering demand, jetway side — seconds of speech), and the
        // next position sample lands 16-33 ms later. Arm must NOT leave the callout in
        // the "speak whatever comes next" state Reset does.
        var c = new DockingSpeedCallout();
        c.Arm(4.0);
        Assert.Null(c.Update(4.0));
        Assert.Null(c.Update(3.8));                // same knot — still nothing
        Assert.Equal("3 knots.", c.Update(3.2));   // first genuine change speaks
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Invalid_samples_are_ignored_not_announced(double gs)
        => Assert.Null(new DockingSpeedCallout().Update(gs));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    [InlineData(double.PositiveInfinity)]
    public void Arm_with_an_unusable_sample_falls_back_to_speaking_the_next_one(double gs)
    {
        // Better to speak one extra number than to prime the state from garbage and go
        // silent for the rest of the approach.
        var c = new DockingSpeedCallout();
        c.Arm(gs);
        Assert.Equal("2 knots.", c.Update(2.0));
    }
}

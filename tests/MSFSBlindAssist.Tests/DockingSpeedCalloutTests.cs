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
    public void Zero_says_stopped_rather_than_zero_knots()
    {
        var c = new DockingSpeedCallout();
        c.Update(2.0);
        Assert.Equal("Stopped.", c.Update(0.0));
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

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    public void Invalid_samples_are_ignored_not_announced(double gs)
        => Assert.Null(new DockingSpeedCallout().Update(gs));
}

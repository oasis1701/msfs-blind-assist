// Turnaround liftoff detector (spec 2026-07-14 post-note to the route-advisory proximity
// design). Detects the liftoff edge of a NEW flight in a same-session turnaround: a
// touchdown edge followed by >= 5 minutes on the ground, then a liftoff edge. Every test
// asserts the return of EVERY ObserveEdge call so a spurious extra "true" or a missed one
// both fail.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class TurnaroundLiftoffDetectorTests
{
    private static readonly DateTime T = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Turnaround_liftoff_after_six_minutes_on_ground_fires()
    {
        var d = new TurnaroundLiftoffDetector();
        Assert.False(d.ObserveEdge(justTouchedDown: true, justLiftedOff: false, T));
        Assert.True(d.ObserveEdge(justTouchedDown: false, justLiftedOff: true, T + TimeSpan.FromMinutes(6)));
    }

    [Fact]
    public void Touch_and_go_never_fires()
    {
        var d = new TurnaroundLiftoffDetector();
        Assert.False(d.ObserveEdge(justTouchedDown: true, justLiftedOff: false, T));
        Assert.False(d.ObserveEdge(justTouchedDown: false, justLiftedOff: true, T + TimeSpan.FromSeconds(40)));

        // Next full stop still works.
        Assert.False(d.ObserveEdge(justTouchedDown: true, justLiftedOff: false, T + TimeSpan.FromMinutes(2)));
        Assert.True(d.ObserveEdge(justTouchedDown: false, justLiftedOff: true, T + TimeSpan.FromMinutes(8)));
    }

    [Fact]
    public void First_departure_of_the_session_never_fires()
    {
        var d = new TurnaroundLiftoffDetector();
        Assert.False(d.ObserveEdge(justTouchedDown: false, justLiftedOff: true, T));
    }

    [Fact]
    public void Bounce_flicker_after_turnaround_liftoff_does_not_double_fire()
    {
        var d = new TurnaroundLiftoffDetector();
        Assert.False(d.ObserveEdge(justTouchedDown: true, justLiftedOff: false, T));
        Assert.True(d.ObserveEdge(justTouchedDown: false, justLiftedOff: true, T + TimeSpan.FromMinutes(10)));

        // Bounce: touchdown then liftoff 3s later.
        var bounceDown = T + TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(2);
        var bounceUp = T + TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(5);
        Assert.False(d.ObserveEdge(justTouchedDown: true, justLiftedOff: false, bounceDown));
        Assert.False(d.ObserveEdge(justTouchedDown: false, justLiftedOff: true, bounceUp));
    }

    [Fact]
    public void Liftoff_consumes_the_touchdown()
    {
        var d = new TurnaroundLiftoffDetector();
        Assert.False(d.ObserveEdge(justTouchedDown: true, justLiftedOff: false, T));
        Assert.True(d.ObserveEdge(justTouchedDown: false, justLiftedOff: true, T + TimeSpan.FromMinutes(6)));

        // Second liftoff sample with no touchdown between: must not fire again.
        Assert.False(d.ObserveEdge(justTouchedDown: false, justLiftedOff: true, T + TimeSpan.FromMinutes(6) + TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void Reset_clears_a_pending_touchdown()
    {
        var d = new TurnaroundLiftoffDetector();
        Assert.False(d.ObserveEdge(justTouchedDown: true, justLiftedOff: false, T));
        d.Reset();
        Assert.False(d.ObserveEdge(justTouchedDown: false, justLiftedOff: true, T + TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Non_edge_samples_are_ignored()
    {
        var d = new TurnaroundLiftoffDetector();

        // No pending touchdown: non-edge samples stay silent.
        Assert.False(d.ObserveEdge(justTouchedDown: false, justLiftedOff: false, T));
        Assert.False(d.ObserveEdge(justTouchedDown: false, justLiftedOff: false, T + TimeSpan.FromMinutes(1)));

        // Pending touchdown survives non-edge samples in between.
        Assert.False(d.ObserveEdge(justTouchedDown: true, justLiftedOff: false, T + TimeSpan.FromMinutes(2)));
        Assert.False(d.ObserveEdge(justTouchedDown: false, justLiftedOff: false, T + TimeSpan.FromMinutes(3)));
        Assert.False(d.ObserveEdge(justTouchedDown: false, justLiftedOff: false, T + TimeSpan.FromMinutes(4)));
        Assert.False(d.ObserveEdge(justTouchedDown: false, justLiftedOff: false, T + TimeSpan.FromMinutes(5)));
        Assert.True(d.ObserveEdge(justTouchedDown: false, justLiftedOff: true, T + TimeSpan.FromMinutes(2) + TimeSpan.FromMinutes(6)));
    }
}

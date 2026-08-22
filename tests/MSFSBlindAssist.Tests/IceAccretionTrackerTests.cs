// Pins the ice-accretion announcement rules (spec 2026-07-11 §2.2): binary with
// hysteresis using the A380's sim-verified thresholds (rise ≥ 0.05, clear ≤ 0.02,
// mirrored ternary semantics), first-sample baseline silence.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class IceAccretionTrackerTests
{
    [Fact]
    public void First_sample_is_silent_even_when_already_icing()
    {
        var t = new IceAccretionTracker();
        Assert.Null(t.Observe(0.30));                  // loads mid-icing: adopt silently
        Assert.Null(t.Observe(0.35));                  // still icing: silent
        Assert.Equal("Icing conditions cleared", t.Observe(0.01));
    }

    [Fact]
    public void Rising_edge_at_exactly_the_detect_threshold()
    {
        var t = new IceAccretionTracker();
        t.Observe(0.0);                                // baseline: not icing
        Assert.Null(t.Observe(0.049));
        Assert.Equal("Icing conditions, ice accumulating", t.Observe(0.05));
    }

    [Fact]
    public void Dead_band_is_silent_in_both_states()
    {
        var t = new IceAccretionTracker();
        t.Observe(0.0);
        Assert.Null(t.Observe(0.03));                  // not icing: below detect → silent
        t.Observe(0.06);                               // now icing (announced)
        Assert.Null(t.Observe(0.03));                  // icing: above clear → still icing
    }

    [Fact]
    public void Falling_edge_at_or_below_the_clear_threshold()
    {
        var t = new IceAccretionTracker();
        t.Observe(0.0);
        t.Observe(0.10);                               // icing
        Assert.Null(t.Observe(0.021));                 // just above clear
        Assert.Equal("Icing conditions cleared", t.Observe(0.02));
    }

    [Fact]
    public void Repeated_cycles_announce_each_edge_once()
    {
        var t = new IceAccretionTracker();
        t.Observe(0.0);
        Assert.Equal("Icing conditions, ice accumulating", t.Observe(0.08));
        Assert.Null(t.Observe(0.09));
        Assert.Equal("Icing conditions cleared", t.Observe(0.0));
        Assert.Equal("Icing conditions, ice accumulating", t.Observe(0.06));
    }

    [Fact]
    public void Reset_rebaselines_silently()
    {
        var t = new IceAccretionTracker();
        t.Observe(0.0);
        t.Observe(0.10);                               // icing
        t.Reset();
        Assert.Null(t.Observe(0.10));                  // first sample after reset: silent
        Assert.Equal("Icing conditions cleared", t.Observe(0.0));
    }
}

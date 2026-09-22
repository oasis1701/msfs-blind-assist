// tests/MSFSBlindAssist.Tests/Cessna172WarningsTests.cs
// Every C172 warning is edge-triggered, one-shot per excursion, baseline-first (the first sample
// seeds silently so loading into a running aircraft narrates nothing), and re-arms with hysteresis
// so a value hovering on the limit does not chatter.
using MSFSBlindAssist.Aircraft.C172;

namespace MSFSBlindAssist.Tests;

public class Cessna172WarningsTests
{
    // ---------- RisingEdge (stall, overspeed) ----------

    [Fact]
    public void A_rising_edge_fires_once_and_rearms_when_the_flag_falls()
    {
        var e = new RisingEdge();
        Assert.False(e.Feed(false));   // baseline
        Assert.True(e.Feed(true));
        Assert.False(e.Feed(true));    // still up: one-shot
        Assert.False(e.Feed(false));   // falls: re-arms, silently
        Assert.True(e.Feed(true));
    }

    [Fact]
    public void A_flag_already_up_at_the_first_sample_is_a_baseline_not_an_event()
    {
        var e = new RisingEdge();
        Assert.False(e.Feed(true));
        Assert.False(e.Feed(true));
        Assert.False(e.Feed(false));
        Assert.True(e.Feed(true));
    }

    [Fact]
    public void Reset_makes_the_next_sample_a_baseline_again()
    {
        var e = new RisingEdge();
        e.Feed(false);
        e.Reset();
        Assert.False(e.Feed(true));
    }

    // ---------- ThresholdWarning (low fuel, low volts, oil) ----------

    [Fact]
    public void Low_fuel_fires_below_five_and_rearms_above_six()
    {
        var w = new ThresholdWarning(Cessna172Limits.FuelLowGallons, Cessna172Limits.FuelRearmGallons, triggerBelow: true);
        Assert.False(w.Feed(20.0, enabled: true));   // baseline
        Assert.False(w.Feed(5.5, true));
        Assert.True(w.Feed(4.9, true));
        Assert.True(w.IsTripped);
        Assert.False(w.Feed(4.0, true));              // one-shot
        Assert.False(w.Feed(5.5, true));              // inside the hysteresis band: still tripped
        Assert.True(w.IsTripped);
        Assert.False(w.Feed(6.1, true));              // re-arms silently
        Assert.False(w.IsTripped);
        Assert.True(w.Feed(4.9, true));
    }

    [Fact]
    public void High_oil_temperature_fires_above_245_and_rearms_below_235()
    {
        var w = new ThresholdWarning(Cessna172Limits.OilTempHighF, Cessna172Limits.OilTempRearmF, triggerBelow: false);
        Assert.False(w.Feed(180, true));
        Assert.True(w.Feed(246, true));
        Assert.False(w.Feed(240, true));
        Assert.False(w.Feed(234, true));
        Assert.True(w.Feed(250, true));
    }

    [Fact]
    public void A_value_already_out_of_limits_at_the_first_sample_is_a_baseline()
    {
        var w = new ThresholdWarning(Cessna172Limits.FuelLowGallons, Cessna172Limits.FuelRearmGallons, triggerBelow: true);
        Assert.False(w.Feed(2.0, true));
        Assert.True(w.IsTripped);                     // tripped, so it cannot fire until re-armed
        Assert.False(w.Feed(1.0, true));
        Assert.False(w.Feed(7.0, true));
        Assert.True(w.Feed(4.0, true));
    }

    [Fact]
    public void A_disabled_gate_neither_fires_nor_trips()
    {
        // Low voltage and oil pressure are gated on the engine running: a cold aircraft on the
        // battery is not a warning.
        var w = new ThresholdWarning(Cessna172Limits.VoltsLow, Cessna172Limits.VoltsRearm, triggerBelow: true);
        Assert.False(w.Feed(28.0, enabled: false));
        Assert.False(w.Feed(23.0, enabled: false));
        Assert.False(w.IsTripped);
        Assert.False(w.Feed(28.0, enabled: true));    // first ENABLED sample is the baseline
        Assert.True(w.Feed(23.0, enabled: true));
    }

    [Fact]
    public void Reset_forgets_the_baseline_and_the_trip()
    {
        var w = new ThresholdWarning(Cessna172Limits.OilPressureLowPsi, Cessna172Limits.OilPressureRearmPsi, triggerBelow: true);
        w.Feed(60, true);
        w.Feed(10, true);
        w.Reset();
        Assert.False(w.IsTripped);
        Assert.False(w.Feed(10, true));               // baseline again
    }

    [Fact]
    public void The_limits_are_the_c172_ones()
    {
        Assert.Equal(5.0, Cessna172Limits.FuelLowGallons);
        Assert.Equal(6.0, Cessna172Limits.FuelRearmGallons);
        Assert.Equal(24.0, Cessna172Limits.VoltsLow);
        Assert.Equal(25.0, Cessna172Limits.VoltsRearm);
        Assert.Equal(20.0, Cessna172Limits.OilPressureLowPsi);
        Assert.Equal(25.0, Cessna172Limits.OilPressureRearmPsi);
        Assert.Equal(245.0, Cessna172Limits.OilTempHighF);
        Assert.Equal(235.0, Cessna172Limits.OilTempRearmF);
        Assert.Equal(20.0, Cessna172Limits.EngineStoppedMinIasKnots);
    }
}

// The Engines window speaks the Fenix's own running flag edges ("Engine 2 running") as background
// changes (CORE-7). Baseline first, so opening the window says nothing, and a data dropout (values
// turning unknown, then back) never re-speaks a state that did not change.
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixEngineRunningWatchTests
{
    private static FenixEngineReading R(bool? e1, bool? e2) => FenixEngineReading.None with
    {
        Engine1 = FenixEngineValues.None with { Running = e1 },
        Engine2 = FenixEngineValues.None with { Running = e2 }
    };

    [Fact]
    public void First_values_are_silent()
        => Assert.Empty(new FenixEngineRunningWatch().Observe(R(true, false)));

    [Fact]
    public void A_start_and_a_shutdown_are_spoken()
    {
        var w = new FenixEngineRunningWatch();
        Assert.Empty(w.Observe(R(true, false)));
        Assert.Equal(new[] { "Engine 2 running" }, w.Observe(R(true, true)));
        Assert.Equal(new[] { "Engine 1 stopped" }, w.Observe(R(false, true)));
    }

    [Fact]
    public void Both_in_one_tick_engine_1_first()
    {
        var w = new FenixEngineRunningWatch();
        Assert.Empty(w.Observe(R(false, false)));
        Assert.Equal(new[] { "Engine 1 running", "Engine 2 running" }, w.Observe(R(true, true)));
    }

    [Fact]
    public void A_dropout_does_not_respeak()
    {
        var w = new FenixEngineRunningWatch();
        Assert.Empty(w.Observe(R(true, true)));
        Assert.Empty(w.Observe(R(null, null)));
        Assert.Empty(w.Observe(R(true, true)));
    }

    [Fact]
    public void Unknown_first_then_known_is_a_baseline()
    {
        var w = new FenixEngineRunningWatch();
        Assert.Empty(w.Observe(R(null, null)));
        Assert.Empty(w.Observe(R(true, false)));
    }
}

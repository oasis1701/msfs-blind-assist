// The display hold keeps an Engines row still while the Fenix's value jitters across a rounding edge
// (oil temperature 116.73, 116.62, 116.55 in 0.8 s), so a focused row is not re-read every second.
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixEngineDisplayHoldTests
{
    [Fact]
    public void First_value_rounds_normally()
    {
        var h = new FenixEngineDisplayHold();
        Assert.Equal(116, h.Hold("k", 116.4, 0)!.Value, 6);
        Assert.Equal(22.4, h.Hold("t", 22.448, 1)!.Value, 6);
    }

    [Fact]
    public void Jitter_inside_the_band_keeps_the_shown_value()
    {
        var h = new FenixEngineDisplayHold();
        Assert.Equal(116, h.Hold("k", 116.4, 0)!.Value, 6);
        Assert.Equal(116, h.Hold("k", 116.55, 0)!.Value, 6);
        Assert.Equal(116, h.Hold("k", 116.45, 0)!.Value, 6);
        Assert.Equal(116, h.Hold("k", 116.59, 0)!.Value, 6);
    }

    [Fact]
    public void A_real_move_updates_and_then_holds()
    {
        var h = new FenixEngineDisplayHold();
        Assert.Equal(116, h.Hold("k", 116.4, 0)!.Value, 6);
        Assert.Equal(117, h.Hold("k", 116.7, 0)!.Value, 6);
        Assert.Equal(117, h.Hold("k", 116.45, 0)!.Value, 6);
        Assert.Equal(116, h.Hold("k", 116.3, 0)!.Value, 6);
    }

    [Fact]
    public void Tenths_use_a_tenth_of_the_band()
    {
        var h = new FenixEngineDisplayHold();
        Assert.Equal(22.4, h.Hold("k", 22.448, 1)!.Value, 6);
        Assert.Equal(22.4, h.Hold("k", 22.45, 1)!.Value, 6);
        Assert.Equal(22.5, h.Hold("k", 22.47, 1)!.Value, 6);
    }

    [Fact]
    public void Unknown_forgets_the_key()
    {
        var h = new FenixEngineDisplayHold();
        Assert.Equal(116, h.Hold("k", 116.4, 0)!.Value, 6);
        Assert.Null(h.Hold("k", null, 0));
        Assert.Equal(117, h.Hold("k", 116.55, 0)!.Value, 6);
    }

    [Fact]
    public void Keys_are_independent()
    {
        var h = new FenixEngineDisplayHold();
        Assert.Equal(116, h.Hold("a", 116.4, 0)!.Value, 6);
        Assert.Equal(117, h.Hold("b", 116.7, 0)!.Value, 6);
    }
}

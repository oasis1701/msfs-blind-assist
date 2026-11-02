// The engine gauge colours are PMDG's own tests from its draw code, applied to the limits read from
// memory: N1 and N2 red above their limit; EGT red above its red line, else amber above its maximum
// continuous; oil pressure (engine running) amber below its engine's amber limit, red below the red one;
// oil temperature amber above its amber limit, red above the red one; vibration above its limit drawn
// in reverse video ("high"). Each boundary is PMDG's strict comparison.

using MSFSBlindAssist.Services.PMDG;
using Xunit;
using static MSFSBlindAssist.Services.PMDG.Pmdg737EngineDisplayColors;

namespace MSFSBlindAssist.Tests;

public class Pmdg737EngineDisplayColorsTests
{
    private static EngineDisplaySample With(EngineValues engine1) =>
        EngineDisplaySampleFactory.Idle() with { Engines = new[] { engine1, EngineDisplaySampleFactory.IdleEngine(2) } };

    private static EngineValues Idle => EngineDisplaySampleFactory.IdleEngine(1);

    [Fact]
    public void The_live_idle_reading_is_normal_everywhere()
    {
        var s = EngineDisplaySampleFactory.Idle();
        foreach (var gauge in Enum.GetValues<Gauge>())
            for (int engine = 0; engine < 2; engine++)
                Assert.Equal(GaugeColor.Normal, ColorOf(gauge, s, engine));
    }

    [Theory]
    [InlineData(104.0, GaugeColor.Normal)]
    [InlineData(104.1, GaugeColor.Red)]
    public void N1_above_its_red_line_is_red(double n1, GaugeColor expected) =>
        Assert.Equal(expected, ColorOf(Gauge.N1, With(Idle with { N1 = n1 }), 0));

    [Theory]
    [InlineData(925.0, GaugeColor.Normal)]
    [InlineData(925.1, GaugeColor.Amber)]
    [InlineData(950.0, GaugeColor.Amber)]
    [InlineData(950.1, GaugeColor.Red)]
    public void Egt_at_the_red_line_is_amber_not_red(double egt, GaugeColor expected) =>
        Assert.Equal(expected, ColorOf(Gauge.Egt, With(Idle with { Egt = egt }), 0));

    [Theory]
    [InlineData(105.0, GaugeColor.Normal)]
    [InlineData(105.1, GaugeColor.Red)]
    public void N2_above_its_maximum_is_red(double n2, GaugeColor expected) =>
        Assert.Equal(expected, ColorOf(Gauge.N2, With(Idle with { N2 = n2 }), 0));

    [Theory]
    [InlineData(20.0, GaugeColor.Normal)]
    [InlineData(19.9, GaugeColor.Amber)]
    [InlineData(13.0, GaugeColor.Amber)]
    [InlineData(12.9, GaugeColor.Red)]
    public void Oil_pressure_amber_below_its_engines_limit_red_below_the_red_line(double pressure, GaugeColor expected) =>
        Assert.Equal(expected, ColorOf(Gauge.OilPressure, With(Idle with { OilPressure = pressure }), 0));

    [Fact]
    public void Oil_pressure_ignored_while_not_running() =>
        Assert.Equal(GaugeColor.Normal, ColorOf(Gauge.OilPressure, With(Idle with { OilPressure = 0, Running = false }), 0));

    [Theory]
    [InlineData(140.0, GaugeColor.Normal)]
    [InlineData(140.1, GaugeColor.Amber)]
    [InlineData(160.0, GaugeColor.Amber)]
    [InlineData(160.1, GaugeColor.Red)]
    public void Oil_temperature_amber_above_its_limit_red_above_the_red_line(double temperature, GaugeColor expected) =>
        Assert.Equal(expected, ColorOf(Gauge.OilTemperature, With(Idle with { OilTemperature = temperature }), 0));

    [Theory]
    [InlineData(4.0, GaugeColor.Normal)]
    [InlineData(4.1, GaugeColor.High)]
    public void Vibration_above_its_limit_is_high(double vibration, GaugeColor expected) =>
        Assert.Equal(expected, ColorOf(Gauge.Vibration, With(Idle with { Vibration = vibration }), 0));

    [Fact]
    public void A_latched_exceedance_is_red_below_the_limit()
    {
        // PMDG's engine model latches an exceedance (a hot start, an overtemperature): the readout stays red
        // after the value is back in range, until the latch clears.
        Assert.Equal(GaugeColor.Red, ColorOf(Gauge.Egt, With(Idle with { EgtRedLatch = true }), 0));
        Assert.Equal(GaugeColor.Red, ColorOf(Gauge.N1, With(Idle with { N1RedLatch = true }), 0));
        Assert.Equal(GaugeColor.Red, ColorOf(Gauge.N2, With(Idle with { N2RedLatch = true }), 0));
        Assert.Equal(GaugeColor.Normal, ColorOf(Gauge.Egt, With(Idle with { EgtRedLatch = true }), 1));
    }

    [Fact]
    public void Each_engine_has_its_own_colour()
    {
        var s = With(Idle with { Egt = 960 });
        Assert.Equal(GaugeColor.Red, ColorOf(Gauge.Egt, s, 0));
        Assert.Equal(GaugeColor.Normal, ColorOf(Gauge.Egt, s, 1));
    }

    [Fact]
    public void Colour_words()
    {
        Assert.Equal("normal", Word(GaugeColor.Normal));
        Assert.Equal("amber", Word(GaugeColor.Amber));
        Assert.Equal("red", Word(GaugeColor.Red));
        Assert.Equal("high", Word(GaugeColor.High));
    }
}

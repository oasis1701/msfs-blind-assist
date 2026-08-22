using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the shared wiper OFF/SLOW/FAST decode (A32NX circuits
/// 77/80, A380 141/143). The live position is a TWO-var state: circuit switch bool +
/// circuit power setting. The power setting PERSISTS at its default (100%) while the
/// switch is off, so a cold-start read of switch=off + power=100 must classify OFF,
/// not FAST — the switch always wins. Power is tolerated as ratio (0.75/1.0) or
/// percent (75/100) so a unit surprise can't collapse the two speeds.
/// </summary>
public class WiperPositionTests
{
    // ---- switch off => OFF regardless of the persisted power setting ----

    [Theory]
    [InlineData(0.0, 100.0)]   // the cold-start trap: power rests at 100% while off
    [InlineData(0.0, 75.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(null, 100.0)]  // no switch read yet
    public void SwitchOff_IsOff(double? sw, double? pwr)
    {
        Assert.Equal(0, WiperPosition.FromCircuit(sw, pwr));
    }

    // ---- switch on: SLOW (~75%) vs FAST (~100%), percent or ratio units ----

    [Theory]
    [InlineData(1.0, 75.0)]    // percent
    [InlineData(1.0, 0.75)]    // ratio
    [InlineData(1.0, 80.0)]    // below the 87.5 midpoint => slow
    public void SwitchOn_SlowPower_IsSlow(double? sw, double? pwr)
    {
        Assert.Equal(1, WiperPosition.FromCircuit(sw, pwr));
    }

    [Theory]
    [InlineData(1.0, 100.0)]   // percent
    [InlineData(1.0, 1.0)]     // ratio
    [InlineData(1.0, 90.0)]    // above the midpoint => fast
    [InlineData(1.0, null)]    // switch read arrives before power => on classifies Fast until proven Slow
    public void SwitchOn_FastPower_IsFast(double? sw, double? pwr)
    {
        Assert.Equal(2, WiperPosition.FromCircuit(sw, pwr));
    }

    // ---- spoken text ----

    [Theory]
    [InlineData(0, "Off")]
    [InlineData(1, "Slow")]
    [InlineData(2, "Fast")]
    [InlineData(-1, "Off")]    // defensive: anything non-positive reads Off
    public void Text_MapsStates(int state, string expected)
    {
        Assert.Equal(expected, WiperPosition.Text(state));
    }
}

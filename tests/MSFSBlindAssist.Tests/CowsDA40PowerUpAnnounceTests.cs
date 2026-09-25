using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ Turning the electric master on used to announce "Electric Master: On" and nothing else.
///
/// Every consequence of that switch is a NUMBER, and numbers are silent by the house rule - so
/// the switch POSITION was the whole of what a blind pilot got, and the switch position is the
/// one thing that says nothing about whether power actually reached anything. On this aeroplane
/// the master can be on over a dead bus (pulled breaker, flat battery, the essential-bus switch
/// isolating the main bus), and this project has already lost an afternoon to that state.
///
/// The read-back reports what the buses came up at. It never says what a reading MEANS.
/// </summary>
public class CowsDA40PowerUpAnnounceTests
{
    [Fact]
    public void PoweredUpNamesWhatTheBusesCameUpAt()
    {
        Assert.Equal("main bus 24.1 volts, essential 24.1, battery 24.0.",
            CowsDA40Definition.ComposeBusState(true, 24.1, 24.1, 24.0));
    }

    [Fact]
    public void ADeadBusIsNamedAsZeroAndNeverSoftened()
    {
        // ⚠️ THE READING THAT MATTERS MOST. An "electrical normal" summary would hide exactly
        // this, and a pilot would be left with a master reading On over a bus with nothing on
        // it - the state the whole read-back exists to expose.
        string text = CowsDA40Definition.ComposeBusState(true, 0.0, 24.1, 24.0);
        Assert.Contains("main bus 0.0 volts", text);
    }

    [Fact]
    public void ItNeverExplainsWhatAReadingMeans()
    {
        // The pilot's ruling: an announcement reports the aeroplane, it does not coach. A zero
        // bus must not gain "avionics will not power" or any other consequence clause.
        string text = CowsDA40Definition.ComposeBusState(true, 0.0, 0.0, 24.0);
        foreach (string coaching in new[] { "will not", "cannot", "check", "should", "unable" })
            Assert.DoesNotContain(coaching, text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SwitchingOffSaysNothingBecauseTheSwitchAlreadyDid()
    {
        // The buses going dead is the expected consequence of switching off; repeating it is
        // noise, and the switch's own announcement already carried the news.
        Assert.Equal("", CowsDA40Definition.ComposeBusState(false, 0.0, 0.0, 24.0));
    }

    [Fact]
    public void NothingCachedYetIsSilentRatherThanInvented()
    {
        // The first master-on of a session can land before the batch has delivered a single
        // voltage. Silence beats a made-up reading.
        Assert.Equal("", CowsDA40Definition.ComposeBusState(true, null, null, null));
    }

    [Fact]
    public void APartialReadNamesOnlyWhatItHas()
    {
        Assert.Equal("main bus 24.1 volts.",
            CowsDA40Definition.ComposeBusState(true, 24.1, null, null));
    }

    [Fact]
    public void TheThreeBusVoltagesAreBatchEligibleOrTheReadBackHasNothingToRead()
    {
        // ⚠️ Batch membership is Continuous AND IsAnnounced AND not ExcludeFromBatch. Without
        // all three the cache is empty, ComposeBusState gets three nulls, and the whole
        // announcement silently degrades to nothing - the exact shape of "COM 2 does not
        // remember what the standby is".
        var def = new CowsDA40Definition(DA40Variant.NG);
        var vars = def.GetVariables();

        foreach (string key in new[]
        {
            "DA40_ELEC_BUS_MAIN_VOLT", "DA40_ELEC_BUS_ESS_VOLT", "DA40_ELEC_BUS_BATT_VOLT"
        })
        {
            Assert.True(vars.ContainsKey(key), $"{key} is not defined");
            var d = vars[key];
            Assert.Equal(MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous, d.UpdateFrequency);
            Assert.True(d.IsAnnounced, $"{key} must be IsAnnounced to reach the batch cache");
            Assert.False(d.ExcludeFromBatch, $"{key} must not be excluded from the batch");
        }
    }
}

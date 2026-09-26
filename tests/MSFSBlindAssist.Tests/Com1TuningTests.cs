using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Enter / Shift+Enter on the surroundings window's Frequencies list tunes COM 1 and speaks what the
/// radio then holds, read back from the sim: the pilot's only confirmation, since a list gives none of
/// its own when Enter is pressed on it.
/// </summary>
public class Com1TuningTests
{
    [Fact]
    public void A_frequency_the_radio_holds_is_confirmed_with_its_slot()
    {
        Assert.Equal("COM 1 standby 121.9", Com1Tuning.Describe(active: false, 121900000, 121900000.0));
        Assert.Equal("COM 1 active 128.425", Com1Tuning.Describe(active: true, 128425000, 128425000.0));
    }

    [Fact]
    public void A_radio_that_did_not_take_it_says_so_and_what_it_holds_instead()
    {
        // An 8.33 kHz channel on a radio that rounded it to its 25 kHz neighbour is NOT the frequency sent.
        Assert.Equal("Could not tune COM 1 standby to 121.705. It reads 121.7.",
            Com1Tuning.Describe(active: false, 121705000, 121700000.0));
        Assert.Equal("Could not tune COM 1 active to 119.0. It reads 121.95.",
            Com1Tuning.Describe(active: true, 119000000, 121950000.0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    public void A_radio_that_never_answered_is_not_called_a_failure_or_a_success(double? read)
        => Assert.Equal("COM 1 did not report back after tuning 118.3.", Com1Tuning.Describe(active: true, 118300000, read));

    [Fact]
    public void Holding_a_frequency_forgives_float_rounding_but_never_a_neighbouring_channel()
    {
        Assert.True(Com1Tuning.Holds(121900000.4, 121900000));
        Assert.False(Com1Tuning.Holds(121905000.0, 121900000));
        Assert.False(Com1Tuning.Holds(null, 121900000));
    }

    [Fact]
    public void It_sends_the_same_stock_events_as_the_generic_COM_fields()
    {
        Assert.Equal("COM_STBY_RADIO_SET_HZ", Com1Tuning.StandbySetEvent);
        Assert.Equal("COM1_RADIO_SWAP", Com1Tuning.SwapEvent);
    }

    [Fact]
    public void The_A380_refuses_out_loud_and_an_aircraft_the_stock_events_reach_does_not()
    {
        // The FBW A380 ignores the stock COM events (live-verified); pretending to tune would be a
        // silent no-op, so it names the RMP window instead.
        Assert.Contains("RMP", new FlyByWireA380Definition().StockComTuningRefusal);
        Assert.Null(new FlyByWireA320Definition().StockComTuningRefusal);
        Assert.Null(new PMDG737Definition().StockComTuningRefusal);
    }
}

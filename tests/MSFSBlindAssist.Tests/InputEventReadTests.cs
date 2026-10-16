// Reading a B: input event (SIM-23). A SimVarType.InputEvent variable is subscribed by hash and read
// once with GetInputEvent; it never gets a data definition or a batch slot, because a B: name in a
// data definition reads 0 (measured on the MSFS 2024 C172 G1000, 2026-10-16). These pin the pure
// parts: which variables are read that way, that the batch leaves them out, the reserved request-id
// range and its wrap, and how a delivery's value is taken.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class InputEventReadTests
{
    private static SimVarDefinition Def(SimVarType type, UpdateFrequency frequency, bool announced = false) => new()
    {
        Name = "ELECTRICAL_BATTERY_STBY_2",
        DisplayName = "Standby Battery",
        Type = type,
        UpdateFrequency = frequency,
        IsAnnounced = announced
    };

    [Theory]
    [InlineData(UpdateFrequency.OnRequest, true)]
    [InlineData(UpdateFrequency.Continuous, true)]
    [InlineData(UpdateFrequency.Never, false)]   // a pure key press: written, never read
    public void An_input_event_variable_is_read_unless_write_only(UpdateFrequency frequency, bool read)
    {
        Assert.Equal(read, SimConnectManager.IsInputEventRead(Def(SimVarType.InputEvent, frequency)));
    }

    [Theory]
    [InlineData(SimVarType.SimVar)]
    [InlineData(SimVarType.LVar)]
    [InlineData(SimVarType.Event)]
    public void Other_types_are_not_input_event_reads(SimVarType type)
    {
        Assert.False(SimConnectManager.IsInputEventRead(Def(type, UpdateFrequency.OnRequest)));
    }

    [Fact]
    public void An_announced_continuous_input_event_never_rides_the_batch()
    {
        Assert.False(ContinuousBatchLayout.RidesBatch(Def(SimVarType.InputEvent, UpdateFrequency.Continuous, announced: true)));
        Assert.True(ContinuousBatchLayout.RidesBatch(Def(SimVarType.SimVar, UpdateFrequency.Continuous, announced: true)));
    }

    [Fact]
    public void The_read_request_ids_are_710_to_799_and_wrap()
    {
        Assert.Equal(710u, SimConnectManager.InputEventReadRequestFirst);
        Assert.Equal(799u, SimConnectManager.InputEventReadRequestFirst + SimConnectManager.InputEventReadRequestCount - 1);
        Assert.Equal(711u, SimConnectManager.NextInputEventReadRequest(710));
        Assert.Equal(799u, SimConnectManager.NextInputEventReadRequest(798));
        Assert.Equal(710u, SimConnectManager.NextInputEventReadRequest(799));
    }

    [Fact]
    public void The_read_range_stays_clear_of_the_other_reserved_ids()
    {
        // SIM-6's ranges (camera 341-348, guidance 505-508, ground traffic 600-607), the catalog
        // enumeration (700) and the individual-variable ids from 1000.
        uint first = SimConnectManager.InputEventReadRequestFirst;
        uint last = first + SimConnectManager.InputEventReadRequestCount - 1;
        Assert.True(first > (uint)SimConnectManager.DATA_REQUESTS.REQUEST_ENUMERATE_INPUT_EVENTS);
        Assert.True(last < (uint)SimConnectManager.DATA_REQUESTS.INDIVIDUAL_VARIABLE_BASE);
        Assert.True(first > (uint)SimConnectManager.DATA_REQUESTS.REQUEST_GROUND_TRAFFIC + SimConnectManager.GroundTrafficRequestIdCount - 1);
    }

    [Fact]
    public void A_delivery_value_is_its_first_element_as_a_number()
    {
        Assert.True(SimConnectManager.TryInputEventNumber(new object[] { 2.0 }, out double magneto));
        Assert.Equal(2.0, magneto);
        Assert.True(SimConnectManager.TryInputEventNumber(new object[] { 1.0f }, out double asFloat));
        Assert.Equal(1.0, asFloat);
    }

    [Fact]
    public void An_empty_or_non_numeric_delivery_has_no_value()
    {
        Assert.False(SimConnectManager.TryInputEventNumber(null, out _));
        Assert.False(SimConnectManager.TryInputEventNumber(Array.Empty<object>(), out _));
        Assert.False(SimConnectManager.TryInputEventNumber(new object[] { "ARM" }, out _));
    }
}

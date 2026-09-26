// FlyByWireMCDUService resends a Coherent key press over SimBridge only when the result
// PROVES the key never reached the instrument. These pin which results count: a certain
// miss is resent, while a timeout or a dispatch that threw part-way may have landed, and
// resending it would press the key twice.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CoherentA32nxMcduKeyResultTests
{
    [Theory]
    [InlineData("no-socket")]
    [InlineData("no-agent")]
    [InlineData("no-instrument")]
    [InlineData("no-dispatch-path")]
    public void A_certain_miss_is_undelivered(string result)
    {
        Assert.True(CoherentA32nxMcduClient.IsUndeliveredKey(result));
        Assert.False(CoherentA32nxMcduClient.IsDeliveredKey(result));
    }

    [Theory]
    [InlineData("")]                        // the eval timed out — it may still have run
    [InlineData("error: bus is undefined")] // the dispatch threw part-way
    [InlineData("invalid-key")]             // the relay would refuse the same name
    public void An_ambiguous_or_invalid_result_is_never_resent(string result)
    {
        Assert.False(CoherentA32nxMcduClient.IsUndeliveredKey(result));
        Assert.False(CoherentA32nxMcduClient.IsDeliveredKey(result));
    }

    [Theory]
    [InlineData("dispatchHEvent")]
    [InlineData("bus.pub")]
    [InlineData("onEvent")]
    public void Every_agent_dispatch_path_counts_as_delivered(string result)
    {
        Assert.True(CoherentA32nxMcduClient.IsDeliveredKey(result));
        Assert.False(CoherentA32nxMcduClient.IsUndeliveredKey(result));
    }
}

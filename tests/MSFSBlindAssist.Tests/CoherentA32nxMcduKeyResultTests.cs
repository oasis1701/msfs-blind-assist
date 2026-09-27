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

    [Fact]
    public async Task A_key_that_never_left_is_a_certain_miss()
    {
        // No socket, so the press expression is never sent: that must come back as "no-socket"
        // (resendable), never as the ambiguous "" of an answer that did not arrive.
        using var client = new CoherentA32nxMcduClient("A32NX_MCDU_TEST_UNSTARTED");
        string result = await client.SendKeyAsync("INIT");
        Assert.Equal("no-socket", result);
        Assert.True(CoherentA32nxMcduClient.IsUndeliveredKey(result));
    }

    [Fact]
    public async Task A_name_that_is_not_a_key_is_never_sent()
    {
        using var client = new CoherentA32nxMcduClient("A32NX_MCDU_TEST_UNSTARTED");
        Assert.Equal("invalid-key", await client.SendKeyAsync("init\"); x("));
    }
}

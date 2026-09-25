// FbwMcduTransportArbiter chooses between the FlyByWire MCDU's two transports — the
// Coherent debugger socket (primary) and SimBridge's relay (fallback) — so the MCDU
// window sees one MCDU. These pin the precedence, the silence of the non-live transport,
// the catch-up re-publish on a switch, and the once-per-change connection report.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FbwMcduTransportArbiterTests
{
    private static MCDUDisplayData Frame(string title) => new() { Title = title };

    [Fact]
    public void Nothing_is_live_until_a_transport_connects()
    {
        var a = new FbwMcduTransportArbiter();
        Assert.Equal(FbwMcduSource.None, a.Live);
        Assert.False(a.AnyConnected);
        Assert.Null(a.Offer(FbwMcduSource.Coherent, Frame("INIT")).Publish);
    }

    [Fact]
    public void Coherent_outranks_simbridge_whenever_both_are_up()
    {
        var a = new FbwMcduTransportArbiter();
        a.SetConnected(FbwMcduSource.SimBridge, true);
        Assert.Equal(FbwMcduSource.SimBridge, a.Live);
        a.SetConnected(FbwMcduSource.Coherent, true);
        Assert.Equal(FbwMcduSource.Coherent, a.Live);
    }

    [Fact]
    public void Frames_from_the_transport_that_is_not_live_are_never_published()
    {
        var a = new FbwMcduTransportArbiter();
        a.SetConnected(FbwMcduSource.Coherent, true);
        a.SetConnected(FbwMcduSource.SimBridge, true);

        Assert.Null(a.Offer(FbwMcduSource.SimBridge, Frame("PERF")).Publish);
        Assert.Equal("INIT", a.Offer(FbwMcduSource.Coherent, Frame("INIT")).Publish?.Title);
    }

    [Fact]
    public void Losing_coherent_hands_the_window_to_simbridge_with_its_last_frame()
    {
        var a = new FbwMcduTransportArbiter();
        a.SetConnected(FbwMcduSource.SimBridge, true);
        a.SetConnected(FbwMcduSource.Coherent, true);
        a.Offer(FbwMcduSource.SimBridge, Frame("PERF"));   // remembered, not published

        var d = a.SetConnected(FbwMcduSource.Coherent, false);

        Assert.Equal(FbwMcduSource.SimBridge, a.Live);
        Assert.Equal("PERF", d.Publish?.Title);      // catch-up without waiting for a change
        Assert.Null(d.ConnectionChangedTo);          // still connected overall — no announcement
    }

    [Fact]
    public void Coherent_coming_up_takes_over_and_republishes_its_last_frame()
    {
        var a = new FbwMcduTransportArbiter();
        a.SetConnected(FbwMcduSource.SimBridge, true);
        a.Offer(FbwMcduSource.Coherent, Frame("F-PLN"));   // arrived before it was declared readable

        var d = a.SetConnected(FbwMcduSource.Coherent, true);

        Assert.Equal(FbwMcduSource.Coherent, a.Live);
        Assert.Equal("F-PLN", d.Publish?.Title);
    }

    [Fact]
    public void A_switch_with_no_frame_yet_publishes_nothing()
    {
        var a = new FbwMcduTransportArbiter();
        var d = a.SetConnected(FbwMcduSource.Coherent, true);
        Assert.Null(d.Publish);
    }

    [Fact]
    public void Connection_state_is_reported_only_when_the_overall_state_changes()
    {
        var a = new FbwMcduTransportArbiter();
        Assert.True(a.SetConnected(FbwMcduSource.SimBridge, true).ConnectionChangedTo);
        Assert.Null(a.SetConnected(FbwMcduSource.Coherent, true).ConnectionChangedTo);   // already connected
        Assert.Null(a.SetConnected(FbwMcduSource.SimBridge, false).ConnectionChangedTo); // Coherent still up
        Assert.False(a.SetConnected(FbwMcduSource.Coherent, false).ConnectionChangedTo); // now nothing
        Assert.Equal(FbwMcduSource.None, a.Live);
    }

    [Fact]
    public void Repeating_the_same_connection_state_is_a_no_op()
    {
        var a = new FbwMcduTransportArbiter();
        a.SetConnected(FbwMcduSource.Coherent, true);
        var d = a.SetConnected(FbwMcduSource.Coherent, true);
        Assert.Null(d.Publish);
        Assert.Null(d.ConnectionChangedTo);
    }
}

// FbwMcduTransportArbiter chooses between the FlyByWire MCDU's two transports — the
// Coherent debugger socket (primary) and SimBridge's relay (fallback) — so the MCDU
// window sees one MCDU. These pin the precedence, the silence of the non-live transport,
// the fresh-frame request on a switch, and the once-per-change connection report.
//
// A switch never replays a frame the arbiter remembered (PR #253 review): a hung SimBridge
// keeps its socket open, and FBW's MCDU sends SimBridge a blank screen when it detaches,
// so the relay's last frame could be hours old or empty — and it was put back on screen,
// title spoken, whenever Coherent dropped. The newcomer is asked for a fresh frame instead.

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
    public void Losing_coherent_asks_simbridge_for_a_fresh_frame_instead_of_replaying_its_last_one()
    {
        var a = new FbwMcduTransportArbiter();
        a.SetConnected(FbwMcduSource.SimBridge, true);
        a.SetConnected(FbwMcduSource.Coherent, true);
        a.Offer(FbwMcduSource.SimBridge, Frame("INIT"));   // the relay's last frame — possibly stale

        var d = a.SetConnected(FbwMcduSource.Coherent, false);

        Assert.Equal(FbwMcduSource.SimBridge, a.Live);
        Assert.Null(d.Publish);                              // the window keeps what it showed
        Assert.Equal(FbwMcduSource.SimBridge, d.RequestFreshFrom);
        Assert.Null(d.ConnectionChangedTo);                  // still connected overall — no announcement
    }

    [Fact]
    public void The_fresh_frame_from_the_new_live_transport_is_published()
    {
        var a = new FbwMcduTransportArbiter();
        a.SetConnected(FbwMcduSource.SimBridge, true);
        a.SetConnected(FbwMcduSource.Coherent, true);
        a.SetConnected(FbwMcduSource.Coherent, false);

        Assert.Equal("PERF", a.Offer(FbwMcduSource.SimBridge, Frame("PERF")).Publish?.Title);
    }

    [Fact]
    public void Coherent_coming_up_takes_over_and_asks_for_a_fresh_frame()
    {
        var a = new FbwMcduTransportArbiter();
        a.SetConnected(FbwMcduSource.SimBridge, true);
        a.Offer(FbwMcduSource.Coherent, Frame("F-PLN"));   // arrived before it was declared readable

        var d = a.SetConnected(FbwMcduSource.Coherent, true);

        Assert.Equal(FbwMcduSource.Coherent, a.Live);
        Assert.Null(d.Publish);                              // never a frame from before it was live
        Assert.Equal(FbwMcduSource.Coherent, d.RequestFreshFrom);
    }

    [Fact]
    public void The_first_connect_asks_that_transport_for_a_frame()
    {
        var a = new FbwMcduTransportArbiter();
        var d = a.SetConnected(FbwMcduSource.Coherent, true);

        Assert.Null(d.Publish);
        Assert.True(d.ConnectionChangedTo);
        Assert.Equal(FbwMcduSource.Coherent, d.RequestFreshFrom);
    }

    [Fact]
    public void Losing_the_last_transport_asks_nobody_for_a_frame()
    {
        var a = new FbwMcduTransportArbiter();
        a.SetConnected(FbwMcduSource.Coherent, true);

        var d = a.SetConnected(FbwMcduSource.Coherent, false);

        Assert.Equal(FbwMcduSource.None, a.Live);
        Assert.Equal(FbwMcduSource.None, d.RequestFreshFrom);
        Assert.False(d.ConnectionChangedTo);
    }

    [Fact]
    public void A_connection_change_of_the_transport_that_is_not_live_asks_for_nothing()
    {
        var a = new FbwMcduTransportArbiter();
        a.SetConnected(FbwMcduSource.Coherent, true);

        var up = a.SetConnected(FbwMcduSource.SimBridge, true);
        var down = a.SetConnected(FbwMcduSource.SimBridge, false);

        Assert.Equal(FbwMcduSource.None, up.RequestFreshFrom);
        Assert.Equal(FbwMcduSource.None, down.RequestFreshFrom);
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
        Assert.Equal(FbwMcduSource.None, d.RequestFreshFrom);
    }
}

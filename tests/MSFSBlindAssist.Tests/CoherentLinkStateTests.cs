// CoherentLinkState owns the A32NX MCDU Coherent client's "readable" state — the state that
// makes Coherent the live transport and the window say "MCDU: Connected". PR #253 review: it
// was a plain flag written from two threads, and three teardown paths never reported it, so
// it could stay "readable" with no socket (SimBridge frames suppressed, window frozen) or, the
// posts landing out of order, stay unreadable over a working socket with keys dropped.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CoherentLinkStateTests
{
    private static (CoherentLinkState Link, List<bool> Reports) Build()
    {
        var reports = new List<bool>();
        var link = new CoherentLinkState(readable => { lock (reports) { reports.Add(readable); } });
        return (link, reports);
    }

    /// <summary>Open a socket, install the agent and answer a read: the link is readable.</summary>
    private static int MakeReadable(CoherentLinkState link)
    {
        int generation = link.OnSocketOpened();
        link.OnAgentInstalled(generation);
        link.OnReadAnswered(generation, ok: true);
        return generation;
    }

    [Fact]
    public void Readable_needs_an_open_socket_an_installed_agent_and_a_read_answered_ok()
    {
        var (link, reports) = Build();

        int generation = link.OnSocketOpened();
        Assert.True(link.SocketOpen);
        Assert.False(link.Readable);
        link.OnAgentInstalled(generation);
        Assert.True(link.IsAgentInstalled(generation));
        Assert.False(link.Readable);
        link.OnReadAnswered(generation, ok: true);

        Assert.True(link.Readable);
        Assert.Equal(new[] { true }, reports);
    }

    [Fact]
    public void A_read_answered_after_its_socket_closed_does_not_make_the_link_readable()
    {
        // The receive loop handles a Close frame right after an eval answer, and the poll that
        // sent that eval reports its "ok" late. That must not bring the link back to readable.
        var (link, reports) = Build();
        int generation = MakeReadable(link);
        link.OnReadAnswered(generation, ok: false);

        link.OnSocketClosed(generation);
        link.OnReadAnswered(generation, ok: true);

        Assert.False(link.Readable);
        Assert.Equal(new[] { true, false }, reports);
    }

    [Fact]
    public void A_close_from_a_replaced_socket_changes_nothing()
    {
        var (link, _) = Build();
        int old = link.OnSocketOpened();
        int current = MakeReadable(link);

        link.OnSocketClosed(old);   // the replaced socket's receive loop ends late

        Assert.NotEqual(old, current);
        Assert.True(link.SocketOpen);
        Assert.True(link.Readable);
    }

    [Fact]
    public void Tearing_the_socket_down_is_reported()
    {
        // Every teardown reports: the reconnect path used to close the socket silently.
        var (link, reports) = Build();
        int generation = MakeReadable(link);

        link.OnSocketClosed(generation);

        Assert.False(link.SocketOpen);
        Assert.False(link.Readable);
        Assert.Equal(new[] { true, false }, reports);
    }

    [Fact]
    public void Losing_the_agent_is_reported_and_a_reinstall_on_the_same_socket_restores_it()
    {
        // A key press answered "no-agent" used to clear only the agent flag, leaving the link
        // readable; a failed re-install then tore the socket down with nothing reported.
        var (link, reports) = Build();
        int generation = MakeReadable(link);

        link.OnAgentLost(generation);
        Assert.False(link.Readable);
        Assert.True(link.SocketOpen);             // the view is still held
        Assert.False(link.IsAgentInstalled(generation));

        link.OnAgentInstalled(generation);
        link.OnReadAnswered(generation, ok: true);

        Assert.True(link.Readable);
        Assert.Equal(new[] { true, false, true }, reports);
    }

    [Fact]
    public void An_instrument_that_is_not_ready_is_unreadable_until_it_answers_again()
    {
        var (link, reports) = Build();
        int generation = MakeReadable(link);

        link.OnReadAnswered(generation, ok: false);
        Assert.False(link.Readable);
        link.OnReadAnswered(generation, ok: true);

        Assert.True(link.Readable);
        Assert.Equal(new[] { true, false, true }, reports);
    }

    [Fact]
    public void Each_change_is_reported_once()
    {
        var (link, reports) = Build();
        int generation = MakeReadable(link);

        link.OnReadAnswered(generation, ok: true);
        link.OnSocketClosed(generation);
        link.OnSocketClosed(generation);

        Assert.Equal(new[] { true, false }, reports);
    }

    [Fact]
    public void After_stop_nothing_more_is_reported()
    {
        var (link, reports) = Build();
        int generation = MakeReadable(link);

        link.Stop();
        link.OnReadAnswered(generation, ok: true);
        int next = link.OnSocketOpened();
        link.OnAgentInstalled(next);
        link.OnReadAnswered(next, ok: true);

        Assert.False(link.Readable);
        Assert.False(link.SocketOpen);
        Assert.Equal(new[] { true, false }, reports);
    }

    [Fact]
    public void The_last_change_reported_is_always_the_current_state()
    {
        // Reads and closes report from different threads. Checked and posted outside one lock,
        // a "true" could be posted after the "false" that superseded it; the next real change
        // was then never posted at all, because the flag already held it.
        var (link, reports) = Build();
        int generation = link.OnSocketOpened();
        link.OnAgentInstalled(generation);

        Parallel.For(0, 20000, i => link.OnReadAnswered(generation, ok: i % 3 != 0));

        lock (reports)
        {
            Assert.NotEmpty(reports);
            Assert.Equal(link.Readable, reports[^1]);
            for (int i = 1; i < reports.Count; i++) { Assert.NotEqual(reports[i - 1], reports[i]); }
        }
    }
}

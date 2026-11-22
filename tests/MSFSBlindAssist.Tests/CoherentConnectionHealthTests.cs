using System.Net.WebSockets;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins how a tablet client tells its own shutdown from a dropped connection, and when it reports that
/// the simulator keeps refusing it (CoherentConnectionHealth, used by CoherentPmdgEfbClient). Measured
/// 2026-11-22: with Cockpit2Ground holding the sim tablet's view, the sim closed every BA connection, a
/// send on the aborted socket threw OperationCanceledException, and the client's loop took it for
/// shutdown and stopped for the session.
/// </summary>
public class CoherentConnectionHealthTests
{
    [Fact]
    public void A_cancellation_with_the_token_cancelled_is_shutdown()
    {
        Assert.True(CoherentConnectionHealth.IsShutdown(new OperationCanceledException(), cancellationRequested: true));
    }

    [Fact]
    public void A_cancellation_from_a_dropped_socket_is_not_shutdown()
    {
        Assert.False(CoherentConnectionHealth.IsShutdown(new OperationCanceledException(), cancellationRequested: false));
    }

    [Fact]
    public void Another_failure_is_never_shutdown()
    {
        Assert.False(CoherentConnectionHealth.IsShutdown(new WebSocketException(), cancellationRequested: true));
        Assert.False(CoherentConnectionHealth.IsShutdown(new WebSocketException(), cancellationRequested: false));
    }

    [Fact]
    public void One_closed_connection_is_not_reported()
    {
        var health = new CoherentConnectionHealth();
        health.ClosedBeforeInstall();
        Assert.False(health.SimKeepsRefusing);
    }

    [Fact]
    public void Two_closed_connections_in_a_row_are_reported()
    {
        var health = new CoherentConnectionHealth();
        health.ClosedBeforeInstall();
        health.ClosedBeforeInstall();
        Assert.True(health.SimKeepsRefusing);
        health.ClosedBeforeInstall();
        Assert.True(health.SimKeepsRefusing);
    }

    [Fact]
    public void An_install_clears_the_report()
    {
        var health = new CoherentConnectionHealth();
        health.ClosedBeforeInstall();
        health.ClosedBeforeInstall();
        health.Installed();
        Assert.False(health.SimKeepsRefusing);
        health.ClosedBeforeInstall();
        Assert.False(health.SimKeepsRefusing);
    }

    [Fact]
    public void A_view_not_listed_clears_the_report()
    {
        var health = new CoherentConnectionHealth();
        health.ClosedBeforeInstall();
        health.ClosedBeforeInstall();
        health.ViewNotListed();
        Assert.False(health.SimKeepsRefusing);
    }

    [Fact]
    public void A_reinstall_on_an_open_socket_clears_a_close()
    {
        // Review of the branch: an aircraft load closed one connection, the next reached the rebuilt page
        // too early (no marker, socket open), and the third reinstalled on that socket. The count must
        // reset there, or the next load's single drop is reported as a refusal.
        var health = new CoherentConnectionHealth();
        health.ClosedBeforeInstall();
        health.RecordInstall(installed: false, sendFailed: false, socketOpen: true);
        health.RecordInstall(installed: true, sendFailed: false, socketOpen: true);
        health.ClosedBeforeInstall();
        Assert.False(health.SimKeepsRefusing);
    }

    [Fact]
    public void An_install_that_failed_on_a_closed_or_failed_socket_is_a_refusal()
    {
        var closed = new CoherentConnectionHealth();
        closed.RecordInstall(installed: false, sendFailed: false, socketOpen: false);
        closed.RecordInstall(installed: false, sendFailed: false, socketOpen: false);
        Assert.True(closed.SimKeepsRefusing);

        var failedSend = new CoherentConnectionHealth();
        failedSend.RecordInstall(installed: false, sendFailed: true, socketOpen: true);
        failedSend.RecordInstall(installed: false, sendFailed: true, socketOpen: true);
        Assert.True(failedSend.SimKeepsRefusing);
    }

    [Fact]
    public void An_install_without_its_marker_on_an_open_socket_is_not_a_refusal()
    {
        // The page answered but its script was not ready (or the eval timed out): not the sim refusing.
        var health = new CoherentConnectionHealth();
        health.ClosedBeforeInstall();
        health.RecordInstall(installed: false, sendFailed: false, socketOpen: true);
        Assert.False(health.SimKeepsRefusing);
    }

    [Fact]
    public void A_drop_then_an_install_never_reports()
    {
        // An aircraft load: the sim rebuilds the view and drops the connection once, then BA reconnects.
        var health = new CoherentConnectionHealth();
        health.ClosedBeforeInstall();
        Assert.False(health.SimKeepsRefusing);
        health.Installed();
        Assert.False(health.SimKeepsRefusing);
        health.ClosedBeforeInstall();
        Assert.False(health.SimKeepsRefusing);
        health.Installed();
        Assert.False(health.SimKeepsRefusing);
    }
}

using System;
using System.IO;
using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ ONE INSPECTOR SOCKET PER VIEW. While the PFD display window holds AS1000_PFD the CAS
/// monitor's own client is suspended - and the waypoint reader's one-shot expression used to go
/// through that suspended client anyway. <c>InvokeAsync</c> reconnects regardless of the active
/// flag, so every leg poll pulled the socket back from the window the pilot was reading.
/// While suspended, the expression rides the window's own socket, or is not sent at all.
/// </summary>
public class CowsDA40CasSocketHandoverTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException("MSFSBlindAssist.sln not found");
    }

    [Fact]
    public void ActiveMonitorUsesItsOwnClient()
    {
        Assert.Equal(CowsDA40Definition.CasInvokeRoute.Own,
            CowsDA40Definition.RouteCasInvoke(suspended: false, hasOwnClient: true, hasLender: false));
    }

    [Fact]
    public void SuspendedMonitorNeverTouchesItsOwnClient()
    {
        Assert.Equal(CowsDA40Definition.CasInvokeRoute.Lender,
            CowsDA40Definition.RouteCasInvoke(suspended: true, hasOwnClient: true, hasLender: true));
        Assert.Equal(CowsDA40Definition.CasInvokeRoute.None,
            CowsDA40Definition.RouteCasInvoke(suspended: true, hasOwnClient: true, hasLender: false));
    }

    [Fact]
    public void NoClientMeansNothingIsSent()
    {
        Assert.Equal(CowsDA40Definition.CasInvokeRoute.None,
            CowsDA40Definition.RouteCasInvoke(suspended: false, hasOwnClient: false, hasLender: true));
    }

    [Fact]
    public void ThePfdWindowLendsItsSocketWhenItTakesIt()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(),
            "MSFSBlindAssist", "Forms", "DA40", "CowsDA40DisplayForm.cs"));
        Assert.Contains("SuspendCasMonitor(true, _client.InvokeAsync)", src);
    }
}

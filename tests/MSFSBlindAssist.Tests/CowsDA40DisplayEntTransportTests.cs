using MSFSBlindAssist.Forms.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The G1000's database-acknowledge screen ("Press ENT or rightmost softkey to continue")
/// takes ENT only as the SimConnect H-event, not through the instrument's own
/// onInteractionEvent - measured on the XLS after a reload: the socket press left the
/// screen up, <c>1 (&gt;H:AS1000_MFD_ENT_Push)</c> cleared it. The same split the softkeys
/// have. So the window's ENT goes over SimConnect while that screen is showing and over the
/// socket otherwise, decided from the rows the agent last returned.
/// </summary>
public class CowsDA40DisplayEntTransportTests
{
    [Fact]
    public void OnTheStartupScreenEntGoesOverSimConnect()
        => Assert.True(CowsDA40DisplayForm.EntGoesOverSimConnect(new[] { "Display starting up:", "  G1000 NXi System WT1.4.1 ... Press ENT or rightmost softkey to continue" }));

    [Fact]
    public void OnAPageEntGoesOverTheSocket()
    {
        Assert.False(CowsDA40DisplayForm.EntGoesOverSimConnect(new[] { "EIS - Engine", "MAN IN HG: 22.7" }));
        Assert.False(CowsDA40DisplayForm.EntGoesOverSimConnect(System.Array.Empty<string>()));
    }
}

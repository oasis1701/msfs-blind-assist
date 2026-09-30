// SimVarDefinition.RefreshControlWhenDefHandled: which delivered values of a self-announcing
// control MainForm's def-handled branch refreshes an open panel control with.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class SimVarDefinitionRefreshTests
{
    [Fact]
    public void A_control_with_no_predicate_is_never_refreshed()
    {
        Assert.False(new SimVarDefinition().RefreshesControlWhenDefHandled(1));
    }

    [Fact]
    public void The_predicate_decides_per_value()
    {
        var def = new SimVarDefinition { RefreshControlWhenDefHandled = v => v >= 10 };
        Assert.True(def.RefreshesControlWhenDefHandled(10));
        Assert.False(def.RefreshesControlWhenDefHandled(9));
    }
}

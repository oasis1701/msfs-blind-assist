// Characterization tests for Md11Guard.Decide — the state-aware decision for whether a guarded
// MD-11 control's cover must be lifted before actuating (29 guarded controls: engine fire handles,
// cargo smoke agents, fuel dump, battery, generator drives, oxygen masks, ditching…).
//
// The load-bearing rule is "never worse than today": an unreadable guard must resolve to LeaveAlone
// so the code never toggles a cover it cannot see (which could close an already-open one and break a
// control that would otherwise have worked).

using MSFSBlindAssist.Aircraft.MD11;

namespace MSFSBlindAssist.Tests;

public class Md11GuardTests
{
    [Fact]
    public void Unreadable_state_is_left_alone()
    {
        // The safety property: no read → no toggle → no regression vs. the old ungated behaviour.
        Assert.Equal(Md11Guard.Action.LeaveAlone, Md11Guard.Decide(null));
    }

    [Fact]
    public void Closed_cover_is_opened()
    {
        Assert.Equal(Md11Guard.Action.Open, Md11Guard.Decide(0.0));
    }

    [Fact]
    public void Open_cover_is_not_toggled()
    {
        // Already open ⇒ AlreadyOpen (not Open), so a repeat press can never re-close it.
        Assert.Equal(Md11Guard.Action.AlreadyOpen, Md11Guard.Decide(1.0));
    }

    [Theory]
    [InlineData(0.0, Md11Guard.Action.Open)]
    [InlineData(0.49, Md11Guard.Action.Open)]
    [InlineData(0.5, Md11Guard.Action.AlreadyOpen)]   // threshold is inclusive of "open"
    [InlineData(0.51, Md11Guard.Action.AlreadyOpen)]
    [InlineData(1.0, Md11Guard.Action.AlreadyOpen)]
    public void Threshold_splits_closed_from_open(double state, Md11Guard.Action expected)
    {
        Assert.Equal(expected, Md11Guard.Decide(state));
    }
}

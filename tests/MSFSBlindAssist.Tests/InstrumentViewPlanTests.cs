using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins what an AI display read does with the simulator camera before it captures. The numbers
/// come from a live MSFS 2024 session (2026-09-08): CAMERA STATE 2 is a cockpit camera, view
/// type 2 is an instrument view, the index is 0-based (the sim's "instrument view 1" is index 0).
/// The plan carries no way back by design (2026-09-09): a user-saved custom camera reads as a
/// pilot-view index the sim refuses on the way back, so nothing here remembers a previous view.
/// </summary>
public class InstrumentViewPlanTests
{
    [Fact]
    public void AnExternalCamera_IsRefused_WithNothingWritten()
    {
        var plan = InstrumentViewPlan.For(new CameraViewReading(State: 3, ViewType: 0, ViewIndex: 0), wantedIndex: 2);

        Assert.Equal(InstrumentViewOutcome.NotInCockpit, plan.Outcome);
        Assert.Null(plan.Writes);
    }

    [Fact]
    public void TheWantedInstrumentView_NeedsNoWrite()
    {
        var plan = InstrumentViewPlan.For(new CameraViewReading(2, 2, 2), wantedIndex: 2);

        Assert.Equal(InstrumentViewOutcome.AlreadyThere, plan.Outcome);
        Assert.Null(plan.Writes);
    }

    [Fact]
    public void APilotView_IsSwitched()
    {
        var plan = InstrumentViewPlan.For(new CameraViewReading(2, 1, 0), wantedIndex: 0);

        Assert.Equal(InstrumentViewOutcome.Switch, plan.Outcome);
        Assert.Equal((2, 0), plan.Writes);
    }

    [Fact]
    public void AnotherInstrumentView_IsSwitched()
    {
        var plan = InstrumentViewPlan.For(new CameraViewReading(2, 2, 0), wantedIndex: 2);

        Assert.Equal(InstrumentViewOutcome.Switch, plan.Outcome);
        Assert.Equal((2, 2), plan.Writes);
    }

    [Fact]
    public void ACustomCockpitCamera_ReadAsAPilotViewIndexPastTheList_IsSwitchedLikeAnyOther()
    {
        // Measured 2026-09-09: a saved cabin view read as type 1 index 7 with six pilot views advertised.
        var plan = InstrumentViewPlan.For(new CameraViewReading(2, 1, 7), wantedIndex: 0);

        Assert.Equal(InstrumentViewOutcome.Switch, plan.Outcome);
        Assert.Equal((2, 0), plan.Writes);
    }

    [Fact]
    public void AnUnreadableCamera_StillWrites()
    {
        var plan = InstrumentViewPlan.For(null, wantedIndex: 3);

        Assert.Equal(InstrumentViewOutcome.Unknown, plan.Outcome);
        Assert.Equal((2, 3), plan.Writes);
    }

    [Fact]
    public void IsOn_MatchesTypeAndIndexOnly()
    {
        Assert.True(InstrumentViewPlan.IsOn(new CameraViewReading(2, 2, 4), 4));
        Assert.False(InstrumentViewPlan.IsOn(new CameraViewReading(2, 1, 4), 4));
        Assert.False(InstrumentViewPlan.IsOn(new CameraViewReading(2, 2, 3), 4));
    }

    [Fact]
    public void TheConstants_AreTheSimulatorsNumbers()
    {
        Assert.Equal(2, InstrumentViewPlan.CockpitState);
        Assert.Equal(2, InstrumentViewPlan.InstrumentViewType);
    }
}

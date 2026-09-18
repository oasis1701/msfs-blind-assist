using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins what an AI display read does with the simulator camera before it captures. The numbers
/// come from a live MSFS 2024 session (2026-09-08): CAMERA STATE 2 is a cockpit camera, view
/// type 2 is an instrument view, the index is 0-based (the sim's "instrument view 1" is index 0).
/// The plan also says what to write on the way BACK (RestoreWrites): the restore was removed on
/// 2026-09-09 and reinstated on 2026-09-18, when a custom pilot view and a cabin quickview both
/// round-tripped on the live iFly 737 MAX8.
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

    [Fact]
    public void AfterASwitch_TheCameraGoesBackToWhereItWas()
    {
        var before = new CameraViewReading(2, 1, 7);

        Assert.Equal((1, 7), InstrumentViewPlan.RestoreWrites(InstrumentViewOutcome.Switch, before));
    }

    [Fact]
    public void AnUnverifiedSwitch_StillGoesBack()
    {
        // Switch always ATTEMPTED the write, so the camera may have moved whether or not the
        // read-back confirmed it. The entry's verification is deliberately not consulted here.
        var before = new CameraViewReading(2, 3, 2);

        Assert.Equal((3, 2), InstrumentViewPlan.RestoreWrites(InstrumentViewOutcome.Switch, before));
    }

    [Fact]
    public void AlreadyOnTheView_HasNothingToGoBackTo()
    {
        Assert.Null(InstrumentViewPlan.RestoreWrites(
            InstrumentViewOutcome.AlreadyThere, new CameraViewReading(2, 2, 0)));
    }

    [Fact]
    public void AnExternalCamera_WroteNothing_SoNothingGoesBack()
    {
        Assert.Null(InstrumentViewPlan.RestoreWrites(
            InstrumentViewOutcome.NotInCockpit, new CameraViewReading(3, 0, 0)));
    }

    [Fact]
    public void AnUnreadableCamera_LeftNoReadingToGoBackTo()
    {
        Assert.Null(InstrumentViewPlan.RestoreWrites(InstrumentViewOutcome.Unknown, null));
    }
}

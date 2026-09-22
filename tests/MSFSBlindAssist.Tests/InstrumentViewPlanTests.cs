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

    // ---- The way back ----
    //
    // The restore aims at a TARGET rather than at the reading this read happened to take, so a
    // home owed by an earlier failed restore can outlive the read that recorded it
    // (CameraHomePlan). And whether the app must confess to having moved the camera with no way
    // back is decided by whether a move-write was DISPATCHED, never by whether the entry
    // verified: a verified entry can be a camera that was already there, and an unverified one
    // can be a write that landed while every read timed out.

    private static readonly CameraViewReading CabinView = new(2, 1, 7);

    [Fact]
    public void AfterASwitch_TheCameraGoesBackToWhereItWas()
    {
        Assert.Equal((1, 7), InstrumentViewPlan.RestoreWrites(CabinView, wantedIndex: 0, InstrumentViewOutcome.Switch));
    }

    [Fact]
    public void AQuickviewGoesBackToo()
    {
        Assert.Equal((3, 2), InstrumentViewPlan.RestoreWrites(
            new CameraViewReading(2, 3, 2), wantedIndex: 1, InstrumentViewOutcome.Switch));
    }

    [Fact]
    public void AnExternalCamera_WroteNothing_SoNothingGoesBack()
    {
        Assert.Null(InstrumentViewPlan.RestoreWrites(
            new CameraViewReading(3, 0, 0), wantedIndex: 2, InstrumentViewOutcome.NotInCockpit));
    }

    [Fact]
    public void NoTarget_MeansNothingToGoBackTo()
    {
        Assert.Null(InstrumentViewPlan.RestoreWrites(null, wantedIndex: 2, InstrumentViewOutcome.Unknown));
    }

    [Fact]
    public void ATargetThatIsTheViewWeRead_NeedsNoWriteBack()
    {
        // AlreadyThere with nothing owed: the pilot was sitting on the very view the read wanted,
        // so there is nothing to put back and a write would be a no-op the restore then has to
        // verify.
        Assert.Null(InstrumentViewPlan.RestoreWrites(
            new CameraViewReading(2, 2, 2), wantedIndex: 2, InstrumentViewOutcome.AlreadyThere));
    }

    [Fact]
    public void AnOwedHome_GoesBackEvenWhenThisReadMovedNothing()
    {
        // The strand, one read later: the camera already sat on the wanted instrument view, so
        // this read wrote nothing — but a previous restore still owes the pilot their cabin view.
        Assert.Equal((1, 7), InstrumentViewPlan.RestoreWrites(CabinView, wantedIndex: 2, InstrumentViewOutcome.AlreadyThere));
    }

    [Fact]
    public void AnOwedHome_GoesBackEvenWhenTheCameraCouldNotBeRead()
    {
        Assert.Equal((1, 7), InstrumentViewPlan.RestoreWrites(CabinView, wantedIndex: 0, InstrumentViewOutcome.Unknown));
    }

    // MovedWithNoWayBack is the app's confession that it moved the camera off wherever the pilot
    // had it and cannot put it back. It needs BOTH halves: no target to aim at, and a write that
    // actually went out. A write that was never dispatched (SimConnect down) moved nothing, so
    // warning about it would be a false alarm over nothing.
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void WithNoTarget_ItDependsOnWhetherAWriteWasDispatched(bool moved, bool expected)
    {
        Assert.Equal(expected, InstrumentViewPlan.MovedWithNoWayBack(target: null, moved));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithATargetToGoBackTo_ThereIsAlwaysAWayBack(bool moved)
    {
        Assert.False(InstrumentViewPlan.MovedWithNoWayBack(CabinView, moved));
    }
}

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Which view a display read's restore should aim at, once a previous restore has FAILED.
///
/// Without this, a failed restore is silent and permanent: the camera is left on the instrument
/// view, so the next read reads THAT as "where the pilot was", faithfully puts them back on it,
/// and reports success. The pilot is never told again and the app can never get them home.
///
/// The owed home is the reading the failed restore could not reach. It survives until either a
/// restore reaches it or the pilot moves the camera somewhere of their own.
/// </summary>
public class CameraHomePlanTests
{
    private static readonly CameraViewReading CabinView = new(2, 1, 7);
    private static readonly CameraViewReading InstrumentView = new(2, 2, 1);
    private static readonly CameraViewReading Quickview = new(2, 3, 2);

    [Fact]
    public void WithNothingOwed_TheTargetIsTheReadingJustTaken()
    {
        var decision = CameraHomePlan.For(owedHome: null, before: CabinView);

        Assert.Equal(CabinView, decision.RestoreTarget);
        Assert.False(decision.ClearOwedHome);
    }

    [Fact]
    public void WithNothingOwed_AnUnreadableCameraLeavesNoTarget()
    {
        var decision = CameraHomePlan.For(owedHome: null, before: null);

        Assert.Null(decision.RestoreTarget);
        Assert.False(decision.ClearOwedHome);
    }

    [Fact]
    public void StillOnTheInstrumentViewWeLeftThemOn_TheOwedHomeIsTheTarget()
    {
        // The strand: the previous restore failed, so the camera reads an instrument view. The
        // pilot never got home, and `before` is the view we stranded them on — restoring to it
        // would strand them again and report success.
        var decision = CameraHomePlan.For(owedHome: CabinView, before: InstrumentView);

        Assert.Equal(CabinView, decision.RestoreTarget);
        Assert.False(decision.ClearOwedHome);
    }

    [Fact]
    public void AnUnreadableCamera_KeepsTheOwedHomeAsTheTarget()
    {
        var decision = CameraHomePlan.For(owedHome: CabinView, before: null);

        Assert.Equal(CabinView, decision.RestoreTarget);
        Assert.False(decision.ClearOwedHome);
    }

    [Fact]
    public void ThePilotMovedThemselves_TheOwedHomeIsDroppedForWhereTheyAreNow()
    {
        // Any non-instrument cockpit view is the pilot's own choice — they got themselves out, so
        // the debt is settled and the view they chose is what the next restore owes them.
        var decision = CameraHomePlan.For(owedHome: CabinView, before: Quickview);

        Assert.Equal(Quickview, decision.RestoreTarget);
        Assert.True(decision.ClearOwedHome);
    }

    [Fact]
    public void ThePilotMovedToAnotherPilotView_AlsoDropsTheOwedHome()
    {
        var theirNewView = new CameraViewReading(2, 1, 3);

        var decision = CameraHomePlan.For(owedHome: CabinView, before: theirNewView);

        Assert.Equal(theirNewView, decision.RestoreTarget);
        Assert.True(decision.ClearOwedHome);
    }

    [Fact]
    public void AnExternalCamera_SettlesTheDebtToo()
    {
        // NotInCockpit refuses the read outright, but the reading still says the pilot left the
        // instrument view under their own power, so nothing is owed any more.
        var external = new CameraViewReading(3, 0, 0);

        var decision = CameraHomePlan.For(owedHome: CabinView, before: external);

        Assert.Equal(external, decision.RestoreTarget);
        Assert.True(decision.ClearOwedHome);
    }
}

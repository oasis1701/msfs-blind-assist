using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// ⚠️ THE XLS CYLINDER ANNOUNCERS TOOK THEIR BASELINE FROM THE FIRST VARIABLE OF THE BATCH.
/// The batch delivers its variables one at a time, so the first one seeded the baseline while
/// every other cylinder still read zero — and the next cylinder's PERSISTED damage (damage
/// survives a reload) was then announced as new on every connect. Each reading is now its own
/// cylinder's or plug's baseline.
/// </summary>
public class DA40CylinderBaselineTests
{
    [Fact]
    public void PersistedDamageOnALaterCylinderIsNotAnnouncedAtConnect()
    {
        var spoken = new double?[4];
        Assert.Null(DA40CylinderState.NextDamageCallout(spoken, 0, 0));
        Assert.Null(DA40CylinderState.NextDamageCallout(spoken, 1, 40));   // persisted, first reading
        Assert.Null(DA40CylinderState.NextDamageCallout(spoken, 2, 0));
    }

    [Fact]
    public void ARealFallIsStillAnnounced()
    {
        var spoken = new double?[4];
        DA40CylinderState.NextDamageCallout(spoken, 1, 40);
        Assert.Null(DA40CylinderState.NextDamageCallout(spoken, 1, 43));
        Assert.Equal("Cylinder 2 damaged, 55 percent", DA40CylinderState.NextDamageCallout(spoken, 1, 45));
        Assert.Equal("Cylinder 2 dead", DA40CylinderState.NextDamageCallout(spoken, 1, 100));
    }

    [Fact]
    public void ARepairLowersTheBaselineSoNewDamageIsHeard()
    {
        var spoken = new double?[4];
        DA40CylinderState.NextDamageCallout(spoken, 0, 60);
        Assert.Null(DA40CylinderState.NextDamageCallout(spoken, 0, 0));   // Reset: Damage
        Assert.Equal("Cylinder 1 damaged, 94 percent", DA40CylinderState.NextDamageCallout(spoken, 0, 6));
    }

    [Fact]
    public void PersistedFoulingOnALaterPlugIsNotAnnouncedAtConnect()
    {
        var plugs = new double[8];
        var seen = new bool[8];
        double worst = -1;
        plugs[0] = 0;
        Assert.Null(DA40CylinderState.NextFoulingCallout(seen, ref worst, plugs, 0));
        plugs[3] = 60;
        Assert.Null(DA40CylinderState.NextFoulingCallout(seen, ref worst, plugs, 3));
        Assert.Equal(60, worst);
    }

    [Fact]
    public void FoulingWorseningAfterTheBaselineIsStillAnnounced()
    {
        var plugs = new double[8];
        var seen = new bool[8];
        double worst = -1;
        for (int i = 0; i < 8; i++) DA40CylinderState.NextFoulingCallout(seen, ref worst, plugs, i);
        plugs[5] = 30;
        Assert.Equal("Plug fouling, cylinder 3 left at 30 percent",
            DA40CylinderState.NextFoulingCallout(seen, ref worst, plugs, 5));
    }

    [Fact]
    public void ASeenMaskCompletesOnlyWhenEveryMemberHasReported()
    {
        var mask = new DA40SeenMask(3);
        Assert.False(mask.Mark(0));
        Assert.False(mask.Mark(0));
        Assert.False(mask.Mark(2));
        Assert.True(mask.Mark(1));
        Assert.True(mask.Mark(0));
        mask.Reset();
        Assert.False(mask.Mark(1));
    }
}

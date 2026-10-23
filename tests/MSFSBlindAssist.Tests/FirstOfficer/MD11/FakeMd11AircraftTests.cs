using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>The fake reproduces the decoded semantics the executor tests rely on.</summary>
public class FakeMd11AircraftTests
{
    [Fact]
    public void EvacIncrement_IsRefusedAboveArmedWithTheCoverClosed()
    {
        var ac = new FakeMd11Aircraft();
        ac.Fire(73774);                                     // Off -> Armed: allowed cover-closed
        Assert.Equal(1, ac.Get("MD11_AOVHD_EVAC_SW"));
        ac.Fire(73774);                                     // Armed -> On: refused cover-closed
        Assert.Equal(1, ac.Get("MD11_AOVHD_EVAC_SW"));
    }

    [Fact]
    public void PacksAreInertInAirAuto()
    {
        var ac = new FakeMd11Aircraft();
        ac.Press(90289, 90290);
        Assert.Equal(0, ac.Get("MD11_OVHD_PNEU_PACK_1_OFF_LT"));
        ac.Press(90295, 90296);                             // Air -> MANUAL
        ac.Press(90289, 90290);
        Assert.Equal(1, ac.Get("MD11_OVHD_PNEU_PACK_1_OFF_LT"));
    }

    [Fact]
    public void LampsReadZeroUnpowered()
    {
        var ac = new FakeMd11Aircraft();
        ac.SelectIgnition(1);
        ac.Powered = false;
        Assert.Equal(0, ac.ReadCached("MD11_OVHD_ENG_A_LT"));
    }

    [Fact]
    public void InvertedRaise_Lowers()
    {
        var ac = new FakeMd11Aircraft();
        ac.Set("MD11_OVHD_LTS_SEAT_BELTS_SW", 1);
        ac.Inverted.Add(90249);
        ac.Fire(90249);
        Assert.Equal(0, ac.Get("MD11_OVHD_LTS_SEAT_BELTS_SW"));
    }

    [Fact]
    public async System.Threading.Tasks.Task SpoilerClick_FromDeployed_StowsThroughTheSpring()
    {
        var ac = new FakeMd11Aircraft();
        ac.DeployGroundSpoilers();
        ac.Fire(77829);
        Assert.Equal(1, ac.Get("MD11_SPDBRK_ARM"));          // SetSpoilerArm(false) from 2
        await ac.DelayAsync(1500);                           // 50 units at 50/s
        Assert.Equal(0, ac.Get("MD11_SPDBRK_HANDLE"));
        Assert.Equal(0, ac.Get("MD11_SPDBRK_ARM"));          // zeroed on arrival at RET
    }

    [Fact]
    public void SpoilerClick_ArmsOnlyAtRet()
    {
        var ac = new FakeMd11Aircraft();
        ac.Set("MD11_SPDBRK_HANDLE", 25);
        ac.Fire(77829);
        Assert.Equal(0, ac.Get("MD11_SPDBRK_ARM"));
        ac.Set("MD11_SPDBRK_HANDLE", 0);
        ac.Fire(77829);
        Assert.Equal(1, ac.Get("MD11_SPDBRK_ARM"));
        ac.Fire(77829);
        Assert.Equal(0, ac.Get("MD11_SPDBRK_ARM"));
    }
}

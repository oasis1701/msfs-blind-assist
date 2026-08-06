using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class FuelSystemLogicTests
{
    // ---- CenterTankDry: "at least one center pump running, and every running one reports
    //      low pressure" — (sw0||sw1) && (!sw0||lp0) && (!sw1||lp1). Rows verified live (M-4). ----

    [Fact] // M-4 777 (1,1,1,1): both on, both dry → dry
    public void CenterTankDry_BothOnBothDry_True() =>
        Assert.True(FuelSystemLogic.CenterTankDry(true, true, true, true));

    [Fact] // F5 fix: both on, pump-1 failed, tank full → surviving pump feeds → NOT dry
    public void CenterTankDry_BothOnOneFailed_False() =>
        Assert.False(FuelSystemLogic.CenterTankDry(true, true, true, false));

    [Fact] // only pump 2 on, its light out (healthy) → NOT dry (16-row table row 5)
    public void CenterTankDry_OnePumpHealthy_False() =>
        Assert.False(FuelSystemLogic.CenterTankDry(false, true, false, false));

    [Fact] // M-4 live-measured 777 row: only pump 2 on, its light LIT, tank 0 lb → dry
           // (single-pump dry run; table row 6)
    public void CenterTankDry_OnePumpOnAndLit_True() =>
        Assert.True(FuelSystemLogic.CenterTankDry(false, true, false, true));

    [Fact] // M-4 737 (1,0,1,0): only pump on, dry → dry
    public void CenterTankDry_SinglePumpDry_True() =>
        Assert.True(FuelSystemLogic.CenterTankDry(true, false, true, false));

    [Fact] // M-4 777 (0,0,0,0): both off → NOT dry (no running pump)
    public void CenterTankDry_BothOff_False() =>
        Assert.False(FuelSystemLogic.CenterTankDry(false, false, false, false));

    [Fact] // M-2 makes a "stale lit off-pump" row unreachable, but the expression must still
           // read it as NOT dry: off pump lit, other pump off → no RUNNING pump → not dry.
    public void CenterTankDry_OffPumpSpuriouslyLit_False() =>
        Assert.False(FuelSystemLogic.CenterTankDry(false, false, true, false));

    // ---- FuelSystemCredible: at least one WING pump switched ON and its light OUT (R-3a). ----

    [Fact] // cold-and-dark, all wing pumps off → not credible (swN fails)
    public void FuelSystemCredible_AllWingOff_False() =>
        Assert.False(FuelSystemLogic.FuelSystemCredible(
            false, false, false, false, false, false, false, false));

    [Fact] // wing ON + powered (light OUT) → credible
    public void FuelSystemCredible_WingOnPowered_True() =>
        Assert.True(FuelSystemLogic.FuelSystemCredible(
            true, false, false, false, false, false, false, false));

    [Fact] // wing ON but UNpowered (light lit) on the only-on pump → not credible
    public void FuelSystemCredible_WingOnUnpowered_False() =>
        Assert.False(FuelSystemLogic.FuelSystemCredible(
            true, true, false, false, false, false, false, false));

    [Fact] // any one healthy on-pump anywhere (aft-2) makes the system credible
    public void FuelSystemCredible_OneHealthyAftPump_True() =>
        Assert.True(FuelSystemLogic.FuelSystemCredible(
            false, false, false, false, false, false, true, false));

    // ---- BeforeStartFuelPumpsOk: wingOn && (centerOn == hasFuel) (§6 synthetic). ----

    [Fact] public void BsOk_WingOnCenterOnWithFuel_True()  => Assert.True(FuelSystemLogic.BeforeStartFuelPumpsOk(true, true, true));
    [Fact] public void BsOk_WingOnCenterOffNoFuel_True()   => Assert.True(FuelSystemLogic.BeforeStartFuelPumpsOk(true, false, false));
    [Fact] public void BsOk_WingOnCenterOnNoFuel_False()   => Assert.False(FuelSystemLogic.BeforeStartFuelPumpsOk(true, true, false));  // dry-run config
    [Fact] public void BsOk_WingOnCenterOffWithFuel_False()=> Assert.False(FuelSystemLogic.BeforeStartFuelPumpsOk(true, false, true)); // center should be on
    [Fact] public void BsOk_WingOff_False()                => Assert.False(FuelSystemLogic.BeforeStartFuelPumpsOk(false, false, false));

    // ---- SafeRoundToInt: the F13/M1 fix. (int)Math.Round(NaN) is int.MinValue on x64 —
    //      NaN MUST map to 0 so a pre-snapshot quantity can never pin the refuel floor low. ----

    [Fact] public void SafeRound_Nan_IsZero()      => Assert.Equal(0, FuelSystemLogic.SafeRoundToInt(double.NaN));
    [Fact] public void SafeRound_Normal_Rounds()   => Assert.Equal(5000, FuelSystemLogic.SafeRoundToInt(4999.6));
    [Fact] public void SafeRound_Zero_IsZero()     => Assert.Equal(0, FuelSystemLogic.SafeRoundToInt(0));
}

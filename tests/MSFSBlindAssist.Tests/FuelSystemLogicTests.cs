using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class FuelSystemLogicTests
{
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

using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class CenterPumpGateTests
{
    private const string L = "EVT_OH_FUEL_PUMP_L_CENTER";
    private const string R = "EVT_OH_FUEL_PUMP_R_CENTER";
    private const string Other = "EVT_OH_FUEL_PUMP_1_FORWARD";
    private const double Thr = CenterFuelPumpAutomation.ArmThresholdLbs; // 500

    [Fact] public void CenterOnEvent_Recognised()      { Assert.True(CenterPumpGate.IsCenterOnEvent(L)); Assert.True(CenterPumpGate.IsCenterOnEvent(R)); }
    [Fact] public void CenterOnEvent_RejectsOthers()   => Assert.False(CenterPumpGate.IsCenterOnEvent(Other));

    // ON below/at threshold → suppress (the empty-center "off" config is already correct).
    [Fact] public void SuppressCenterOn_LowQty_True()   => Assert.True(CenterPumpGate.ShouldSuppressCenterOn(L, isOn: true, centerQty: 0));
    [Fact] public void SuppressCenterOn_BoundaryQty_True() => Assert.True(CenterPumpGate.ShouldSuppressCenterOn(R, isOn: true, centerQty: Thr)); // <= threshold

    // ON with fuel present → do NOT suppress.
    [Fact] public void SuppressCenterOn_FuelPresent_False() => Assert.False(CenterPumpGate.ShouldSuppressCenterOn(L, isOn: true, centerQty: Thr + 1));

    // Param 0 (OFF) is NEVER gated, even on a bone-dry tank.
    [Fact] public void SuppressCenterOff_NeverGated()  => Assert.False(CenterPumpGate.ShouldSuppressCenterOn(L, isOn: false, centerQty: 0));

    // A non-center event is never gated.
    [Fact] public void OtherEvent_NeverGated()         => Assert.False(CenterPumpGate.ShouldSuppressCenterOn(Other, isOn: true, centerQty: 0));
}

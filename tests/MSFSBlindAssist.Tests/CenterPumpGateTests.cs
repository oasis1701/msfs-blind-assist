using MSFSBlindAssist.FirstOfficer;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class CenterPumpGateTests
{
    private const string L = "EVT_OH_FUEL_PUMP_L_CENTER";
    private const string R = "EVT_OH_FUEL_PUMP_R_CENTER";
    private const string Other = "EVT_OH_FUEL_PUMP_1_FORWARD";
    private const double Thr = CenterFuelPumpAutomation.OffThresholdLbs; // 1000

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

    // ---- iFly 737 MAX8 (task 4): the SAME gate, keyed on the iFly SDK field names. The
    // iFly executor's DispatchCoreAsync passes the def varKey it is about to write, so the
    // gate must recognise those two keys as well as the PMDG event names above.
    private const string IFlyL = "Fuel_CENTER_L_Switch_Status";
    private const string IFlyR = "Fuel_CENTER_R_Switch_Status";

    [Fact] public void IFlyCenterOnEvent_Recognised()  { Assert.True(CenterPumpGate.IsCenterOnEvent(IFlyL)); Assert.True(CenterPumpGate.IsCenterOnEvent(IFlyR)); }
    // ON below threshold → suppress (the dry-run hazard the gate exists for).
    [Fact] public void IFly_SuppressCenterOn_LowQty_True() => Assert.True(CenterPumpGate.ShouldSuppressCenterOn(IFlyL, isOn: true, centerQty: 300));
    // OFF is NEVER gated, on either aircraft's key spelling.
    [Fact] public void IFly_SuppressCenterOff_NeverGated() => Assert.False(CenterPumpGate.ShouldSuppressCenterOn(IFlyL, isOn: false, centerQty: 300));
    // ON with fuel present → do NOT suppress.
    [Fact] public void IFly_SuppressCenterOn_FuelPresent_False() => Assert.False(CenterPumpGate.ShouldSuppressCenterOn(IFlyR, isOn: true, centerQty: Thr + 1));
    // A wing pump on the iFly key spelling is never gated.
    [Fact] public void IFly_WingPump_NeverGated()      => Assert.False(CenterPumpGate.ShouldSuppressCenterOn("Fuel_L_FWD_Switch_Status", isOn: true, centerQty: 0));
}

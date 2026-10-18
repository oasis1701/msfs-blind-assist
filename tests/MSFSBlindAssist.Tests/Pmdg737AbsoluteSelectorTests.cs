// Characterization tests for the PMDG 737 NG3 absolute-position selector dispatch
// (PMDG737Definition.UsesAbsolutePositionDispatch / AbsoluteSelectorParameter).
// Pins which selectors bypass the generic click-walk, whose LEFTSINGLE = up
// assumption is wrong for them (live sweep 2026-10-18, [P737-3]), the position
// lights' reversed parameter, and the range guard on a stray index.

namespace MSFSBlindAssist.Tests;

using MSFSBlindAssist.Aircraft;

public class Pmdg737AbsoluteSelectorTests
{
    [Theory]
    [InlineData("MCP_BankLimitSel")]
    [InlineData("EFIS_ModeSel_0")]
    [InlineData("EFIS_ModeSel_1")]
    [InlineData("EFIS_RangeSel_0")]
    [InlineData("EFIS_RangeSel_1")]
    [InlineData("OH_WiperLSelector")]
    [InlineData("OH_WiperRSelector")]
    [InlineData("AIR_PressurizationModeSelector")]
    [InlineData("AIR_OutflowValveSwitch")]
    [InlineData("MAIN_N1SetSelector")]
    [InlineData("MAIN_SpdRefSelector")]
    [InlineData("ELEC_DCMeterSelector")]
    [InlineData("ELEC_ACMeterSelector")]
    [InlineData("LTS_PositionSw")]
    public void TheSelectorsSweptStepDownOnLeftsingleDispatchByAbsoluteTarget(string varKey)
    {
        Assert.True(PMDG737Definition.UsesAbsolutePositionDispatch(varKey));
    }

    [Theory]
    [InlineData("LTS_DomeWhiteSw")]
    [InlineData("COMM_FastenBeltsSelector")]
    [InlineData("AIR_PackSwitch_0")]
    [InlineData("AIR_IsolationValveSwitch")]
    [InlineData("EFIS_VORADFSel1_0")]
    [InlineData("MAIN_FuelFlowSelector")]
    public void TheSelectorsSweptStepUpOnLeftsingleStayOnTheWalk(string varKey)
    {
        Assert.False(PMDG737Definition.UsesAbsolutePositionDispatch(varKey));
    }

    [Theory]
    [InlineData(0, 2)] // STEADY
    [InlineData(1, 1)] // OFF
    [InlineData(2, 0)] // STROBE & STEADY
    public void ThePositionLightsCountFromTheOtherEnd(int target, int parameter)
    {
        Assert.Equal(parameter, PMDG737Definition.AbsoluteSelectorParameter("LTS_PositionSw", target, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    public void AnyOtherSelectorSendsItsTargetUnchanged(int target)
    {
        Assert.Equal(target, PMDG737Definition.AbsoluteSelectorParameter("MCP_BankLimitSel", target, 5));
    }

    [Theory]
    [InlineData("MCP_BankLimitSel", -1, 5)]
    [InlineData("MCP_BankLimitSel", 5, 5)]
    [InlineData("LTS_PositionSw", 3, 3)]
    [InlineData("LTS_PositionSw", -1, 3)]
    public void AnIndexOutsideTheDetentsSendsNothing(string varKey, int target, int positionCount)
    {
        Assert.Null(PMDG737Definition.AbsoluteSelectorParameter(varKey, target, positionCount));
    }
}

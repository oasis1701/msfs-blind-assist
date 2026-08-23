// Where the A380's "altimeter is on STD" flag is READ from.
//
// It is `A32NX_FCU_EFIS_{L,R}_DISPLAY_BARO_IS_STD` — the FCU's own output, written every frame
// from its baro_std (FlyByWireInterface.cpp:813, :2408), and the same name FBW's own
// FcuSimvarPublisher reads. That is a more direct source than the stock KOHLSMAN SETTING STD
// mirror it replaced.
//
// ⚠️ RETRACTION, kept deliberately. This file first claimed the stock mirror had lost its only
// writer with MsfsBaroManager.ts and could never change again, and that this was why a tester
// reported the altimeter stuck on QNH. Both claims are FALSE, disproven live on 1bbd304:
// BARO_PUSH moved KOHLSMAN SETTING STD:1 0->1 and BARO_PULL moved it 1->0, both sides, with
// nothing in MSFSBA writing it. The tester was simply running a pre-fix build (470a5cfa), which
// debug.log showed emitting the deleted H:A380X_EFIS_CP_BARO_PUSH_1. The real cure was the event
// migration, not this. Check the running binary before diagnosing behaviour.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class FlyByWireA380BaroStateTests
{
    [Theory]
    [InlineData("A32NX_FCU_LEFT_EIS_BARO_IS_STD", "A32NX_FCU_EFIS_L_DISPLAY_BARO_IS_STD")]
    [InlineData("A32NX_FCU_RIGHT_EIS_BARO_IS_STD", "A32NX_FCU_EFIS_R_DISPLAY_BARO_IS_STD")]
    public void The_std_flag_reads_the_live_fcu_lvar_not_the_dead_stock_simvar(
        string varKey, string expectedName)
    {
        var def = new FlyByWireA380Definition().GetVariables()[varKey];

        Assert.Equal(expectedName, def.Name);
        Assert.Equal(SimVarType.LVar, def.Type);
    }
}

// Where the A380's "altimeter is on STD" flag is READ from.
//
// FBW #10855 deleted MsfsBaroManager.ts, whose setupSyncToMsfs was the ONLY thing writing the
// stock `KOHLSMAN SETTING STD:{1,2}` simvar (1bbd304^ MsfsBaroManager.ts:158-160). At 1bbd304
// the WASM registers :1/:2 read-only and force-writes only :4 — the sim altimeter FBW pins to
// STD because it computes the displayed altitude itself (FlyByWireInterface.cpp:3061-3070).
//
// So the stock flag can never change, which is what a test pilot reported as "the altimeter
// won't switch between standard and QNH, it is stuck on QNH". The PUSH/PULL events were fine
// by then; the READBACK was dead, so the app could not see the change it had just made.
//
// The live state moved to `A32NX_FCU_EFIS_{L,R}_DISPLAY_BARO_IS_STD`, written every frame from
// the FCU's own baro_std output (FlyByWireInterface.cpp:813, :2408) and new in this commit.

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

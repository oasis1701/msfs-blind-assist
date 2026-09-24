// The A380 Ctrl+H window's TRK/FPA button decides which mode to ask for from the live mode. The
// variable cache is fed only by the 1 Hz batch and never written on a UI set, so a quick second press
// read the pre-first-press mode, recomputed the SAME target and announced "TRK FPA" a second time while
// SetTrkFpaMode (which reads the commanded view) sent nothing. The window reads the definition's
// commanded-or-cached view instead.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class A380TrkFpaCommandedModeTests
{
    [Fact]
    public void A_commanded_mode_is_seen_before_the_sim_confirms_it()
    {
        var def = new FlyByWireA380Definition();
        var mgr = new SimConnectManager(IntPtr.Zero);   // never connected: the cache is empty
        Assert.Null(def.TrkFpaModeCommandedOrCached(mgr));
        // What SetTrkFpaMode records as it fires the toggle (SetTrkFpaMode itself cannot run here: its
        // SendEvent needs the SimConnect assembly, which the test output does not carry).
        def.RememberCommandedValue("A32NX_TRK_FPA_MODE_ACTIVE", 1);
        Assert.Equal(1.0, def.TrkFpaModeCommandedOrCached(mgr));
    }
}

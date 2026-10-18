// The Cessna 172's warnings are the G1000's CAS messages and its autopilot modes are the FMA's,
// both spoken by the shared G1000 CAS watcher off the PFD (C172-17). The port's own low fuel, oil
// pressure and vacuum warnings are gone, so nothing says a caution twice, and the mode rows are
// quiet but still follow the aircraft in an open panel.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.G1000;

namespace MSFSBlindAssist.Tests;

public class CessnaC172CasTests
{
    [Fact]
    public void The_c172_is_a_g1000_display_owner()
    {
        Assert.IsAssignableFrom<IG1000DisplayOwner>(new CessnaC172G1000Definition());
    }

    [Theory]
    [InlineData("C172_OIL_PRESSURE", 5)]
    [InlineData("C172_VACUUM", 0)]
    [InlineData("C172_FUEL_LEFT_QTY", 2)]
    [InlineData("C172_FUEL_RIGHT_QTY", 2)]
    public void The_ports_warnings_are_silent_readouts_now(string key, double low)
    {
        var def = new CessnaC172G1000Definition();
        var said = new GatedSpeechCapture();

        Assert.True(def.ProcessSimVarUpdate(key, 50, said));
        Assert.True(def.ProcessSimVarUpdate(key, low, said));
        Assert.Empty(said.All);
    }

    [Theory]
    [InlineData("C172_AP_HDG_STATE", 1, true)]
    [InlineData("C172_AP_HDG_STATE", 0, true)]
    [InlineData("C172_AP_ALT_STATE", 1, true)]
    [InlineData("C172_AP_NAV_STATE", 1, true)]
    [InlineData("C172_AP_APR_STATE", 1, true)]
    [InlineData("C172_AP_BC_STATE", 1, true)]
    [InlineData("C172_AP_VS_STATE", 0, true)]
    [InlineData("C172G_AP_FLC_STATE", 1, true)]
    // The master and the flight director keep their OFF, which the FMA does not say.
    [InlineData("C172_AP_MASTER_STATE", 1, true)]
    [InlineData("C172_AP_MASTER_STATE", 0, false)]
    [InlineData("C172G_AP_FD_STATE", 1, true)]
    [InlineData("C172G_AP_FD_STATE", 0, false)]
    [InlineData("C172_STALL_WARNING", 1, false)]
    public void The_fma_speaks_the_modes_and_the_rows_keep_only_the_off(string key, double value, bool fma)
    {
        Assert.Equal(fma, CessnaC172G1000Definition.IsAutopilotModeSpokenByFma(key, value));
    }

    [Fact]
    public void A_mode_the_fma_speaks_is_handled_quietly()
    {
        var def = new CessnaC172G1000Definition();
        var said = new GatedSpeechCapture();

        Assert.True(def.ProcessSimVarUpdate("C172_AP_HDG_STATE", 1, said));
        Assert.True(def.ProcessSimVarUpdate("C172_AP_MASTER_STATE", 1, said));
        Assert.Empty(said.All);
    }

    [Fact]
    public void The_quiet_mode_rows_still_follow_the_aircraft_and_stay_cached()
    {
        var vars = new CessnaC172G1000Definition().GetVariables();

        foreach (string key in CessnaC172G1000Definition.FmaModeKeys)
        {
            Assert.True(vars[key].RefreshesControlWhenDefHandled(1), key);
            Assert.True(vars[key].RefreshesControlWhenDefHandled(0), key);
            // The nose wheel's read-back reads FLC, VS and ALT from the cache (DA40S-1).
            Assert.True(vars[key].IsAnnounced, key);
            Assert.Equal(SimConnect.UpdateFrequency.Continuous, vars[key].UpdateFrequency);
        }
    }
}

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// Pins the five places the HeadwindSim A339X airframe diverges from the FlyByWire
/// A32NX, each measured against the installed packages. These tests exist so a blind
/// re-copy from the A320 profile fails loudly instead of shipping a silent no-op.
/// See docs/superpowers/specs/2026-08-30-headwind-a330-first-officer-design.md.
/// </summary>
public class HwA330DivergenceTests
{
    // --- Divergence 1: nav & logo -------------------------------------------------
    // A32NX_LIGHTS_NAV_LOGO does not exist in the A339X package (A32NX: 14
    // occurrences, A339X: 0). A330_NEO_INTERIOR.xml:2054-2069 binds
    // SWITCH_OVHD_EXTLT_NAVLOGO to stock LIGHT LOGO / LIGHT NAV at index 0.

    [Fact]
    public void A330_nav_logo_state_reads_the_stock_simvar()
    {
        var v = new HeadwindA330Definition().GetVariables()["A32NX_LIGHTS_NAV_LOGO"];
        Assert.Equal("LIGHT NAV", v.Name);
        Assert.Equal(SimVarType.SimVar, v.Type);
    }

    [Fact]
    public void A320_nav_logo_state_still_reads_the_fbw_lvar()
    {
        var v = new FlyByWireA320Definition().GetVariables()["A32NX_LIGHTS_NAV_LOGO"];
        Assert.Equal("A32NX_LIGHTS_NAV_LOGO", v.Name);
        Assert.Equal(SimVarType.LVar, v.Type);
    }

    [Fact]
    public void A330_nav_logo_labels_are_two_position()
    {
        var v = new HeadwindA330Definition().GetVariables()["A32NX_LIGHTS_NAV_LOGO"];
        Assert.Equal("Off", v.ValueDescriptions[0]);
        Assert.Equal("On", v.ValueDescriptions[1]);
        Assert.False(v.ValueDescriptions.ContainsKey(2),
            "The A330 switch is two-position — there is no SYS1/SYS2 concept.");
    }

    // --- Divergence 3: seat-belt sign ---------------------------------------------
    // A330_NEO_INTERIOR.xml:1817-1823 — 0=ON, 1=AUTO, 2=OFF (three positions).
    // A320_NEO_INTERIOR.xml:1756-1762 — 1=ON, 0=OFF. The encoding is INVERTED.

    [Fact]
    public void A330_registers_the_seatbelt_switch_position()
    {
        var v = new HeadwindA330Definition().GetVariables()["SEATBELT_SIGN_POSITION"];
        Assert.Equal("XMLVAR_SWITCH_OVHD_INTLT_SEATBELT_Position", v.Name);
        Assert.Equal(SimVarType.LVar, v.Type);
        Assert.Equal("On",   v.ValueDescriptions[0]);
        Assert.Equal("Auto", v.ValueDescriptions[1]);
        Assert.Equal("Off",  v.ValueDescriptions[2]);
    }

    // --- Divergence 4: landing lights ---------------------------------------------
    // A330_NEO_INTERIOR.xml:2022-2034 — ONE two-position switch on LIGHT LANDING
    // indices 2 and 3. The A32NX has two Retractable switches on L:LIGHTING_LANDING_2/_3.

    [Fact]
    public void A330_registers_the_stock_landing_light_state()
    {
        var v = new HeadwindA330Definition().GetVariables()["LIGHT LANDING:2"];
        Assert.Equal("LIGHT LANDING:2", v.Name);
        Assert.Equal(SimVarType.SimVar, v.Type);
    }
}

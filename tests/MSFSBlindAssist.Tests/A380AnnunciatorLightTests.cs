using System.Linq;
using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The A380 overhead ANN LT switch (cockpit node `SWITCH_OVHD_INTLT_ANNLT`), Test / Bright /
/// Dim.
///
/// ⚠️ Two combos used to claim it. `A32NX_OVHD_INTLT_ANN` is the real input;
/// `A380X_OVHD_ANN_LT_POSITION` was a PHANTOM — FBW lists that name in `a380-simvars.md` but
/// nothing in the aircraft reads or writes it (checked case-insensitively across the installed
/// package). Because MSFSBA's own write created it, it read back whatever was last set and
/// looked like it worked, so the panel offered two entries for one switch with no way to tell
/// which was live. Removed 2026-09-03.
/// </summary>
public class A380AnnunciatorLightTests
{
    private static System.Collections.Generic.Dictionary<string, SimConnect.SimVarDefinition> Vars
        => new FlyByWireA380Definition().GetVariables();

    /// <summary>The phantom must not come back. Anything sourced from FBW's simvar docs alone
    /// has to be checked against the aircraft before it becomes a control.</summary>
    [Fact]
    public void The_phantom_annunciator_var_is_not_registered()
    {
        Assert.DoesNotContain("A380X_OVHD_ANN_LT_POSITION", Vars.Keys);
    }

    /// <summary>The surviving control is the real input and carries the cockpit's own label, so
    /// a pilot still finds "Annunciator Lights" where the dead entry used to be.</summary>
    [Fact]
    public void The_real_switch_carries_the_cockpit_label()
    {
        var v = Vars["A32NX_OVHD_INTLT_ANN"];

        Assert.Equal("A32NX_OVHD_INTLT_ANN", v.Name);
        Assert.Equal("Annunciator Lights", v.DisplayName);
        Assert.Equal("Test", v.ValueDescriptions[0]);
        Assert.Equal("Bright", v.ValueDescriptions[1]);
        Assert.Equal("Dim", v.ValueDescriptions[2]);
    }

    /// <summary>`A380X_OVHD_INTLT_ANN` is the 18 Hz cockpit-side MIRROR the button emissives
    /// read, not an input — writing it would be overwritten every frame. It must never become
    /// a control either.</summary>
    [Fact]
    public void The_emissive_mirror_is_not_a_control()
    {
        Assert.DoesNotContain("A380X_OVHD_INTLT_ANN", Vars.Keys);
    }

    /// <summary>Exactly one Interior Lighting entry claims the ANN LT switch — the duplicate is
    /// what made the dead one survive unnoticed.</summary>
    [Fact]
    public void Only_one_interior_lighting_entry_claims_the_switch()
    {
        var def = new FlyByWireA380Definition();
        var interior = def.GetPanelControls()["Interior Lighting"];

        Assert.Equal(1, interior.Count(k => k.Contains("INTLT_ANN") || k.Contains("ANN_LT")));
    }
}

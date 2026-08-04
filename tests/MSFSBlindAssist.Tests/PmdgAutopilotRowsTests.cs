using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the PMDG Ctrl+P autopilot window's row tables.
/// These pin the varKey -> state-field pairings, which are the failure mode with
/// history in this codebase: PMDG event names are swapped relative to annunciator
/// array indices in places, so an off-by-one here produces a button that silently
/// reports the wrong engine/side rather than failing loudly.
/// </summary>
public class PmdgAutopilotRowsTests
{
    [Fact]
    public void Pmdg737_exposes_a_yoke_ap_disconnect_variable()
    {
        var vars = new PMDG737Definition().GetVariables();

        Assert.True(vars.ContainsKey("YOKE_APDisc"));
        var def = vars["YOKE_APDisc"];
        Assert.Equal("YOKE_APDisc", def.Name);
        Assert.True(def.IsMomentary);
        Assert.True(def.RenderAsButton);
    }
}

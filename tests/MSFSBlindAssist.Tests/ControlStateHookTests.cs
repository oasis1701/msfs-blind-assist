using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The aircraft-agnostic seam MainForm uses to label a button or status row with a
/// definition-composed state. Pins the two defaults every existing aircraft relies on:
/// no dependencies, and no opinion.
/// </summary>
public class ControlStateHookTests
{
    [Fact]
    public void StateVariables_DefaultsToNull_SoExistingDefinitionsAreUnaffected()
    {
        var def = new SimVarDefinition { Name = "X", DisplayName = "X" };
        Assert.Null(def.StateVariables);
    }

    [Fact]
    public void BaseDefinition_HasNoOpinionByDefault()
    {
        // The MD-11 overrides this later; before Attach it must still say "no opinion".
        IAircraftDefinition def = new TFDiMD11Definition();
        Assert.False(def.TryDescribeControlState("MD11_OVHD_ELEC_BATT_BT", out var text));
        Assert.Equal("", text);
    }
}

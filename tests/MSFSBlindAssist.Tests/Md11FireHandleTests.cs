using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Spec D10: TFDi's three engine fire handles are COMPOSITE controls. The pull (0 Normal, 1 Generator
/// Field Disconnect) hands its fully-pulled position (2) over to the handle's own rotation (0 Bottle 1,
/// 1 Fuel and Hydraulic Disconnect, 2 Bottle 2). The map once gave each row the pull's var with the
/// rotation's words, so a stowed handle read "Bottle 1" and the walker turned the bottle-discharge
/// wheel while it read the pull. Each is now a read-only row composed from both vars, and SetControl
/// refuses it. The APU handle is not one: its outer var is its own rotation, so it stays a walkable
/// Bottle 1 / Normal / Bottle 2 combo.
/// </summary>
public class Md11FireHandleTests
{
    private static readonly Md11ControlMap Map = Md11ControlMap.Load();
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    public static IEnumerable<object[]> EngineHandles() => new[]
    {
        new object[] { "MD11_AOVHD_ENG1FIRE_KB", "MD11_AOVHD_ENG1FIRE_SW" },
        new object[] { "MD11_AOVHD_ENG2FIRE_KB", "MD11_AOVHD_ENG2FIRE_SW" },
        new object[] { "MD11_AOVHD_ENG3FIRE_KB", "MD11_AOVHD_ENG3FIRE_SW" },
    };

    /// <summary>A TFDi update that nests another tooltip this way lands here for review — it would
    /// otherwise become one more read-only row that nobody decided on. Four are decided: the three
    /// engine handles (D10) and the Elevator Feel knob (D13, pinned in Md11ElevatorFeelTests).</summary>
    [Fact]
    public void OnlyTheThreeEngineHandlesAndTheElevatorFeelKnob_AreComposites()
    {
        Assert.Equal(new[]
            {
                "MD11_AOVHD_ENG1FIRE_KB", "MD11_AOVHD_ENG2FIRE_KB", "MD11_AOVHD_ENG3FIRE_KB",
                "MD11_OVHD_FLTCTL_ELEVFEEL_KB",
            },
            Map.Controls.Where(c => c.Composite != null).Select(c => c.NodeId).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(EngineHandles))]
    public void EngineHandle_InTheMap_IsThePullThenItsOwnRotation(string nodeId, string pullVar)
    {
        var c = Map.Controls.Single(x => x.NodeId == nodeId);
        Assert.Equal(pullVar, c.StateVar);          // unchanged: the row's key reads the pull
        Assert.Empty(c.ValueMap);                   // no single map can say it
        var s = c.Composite!;
        Assert.Equal(pullVar, s.OuterVar);
        Assert.Equal(nodeId, s.InnerVar);
        Assert.Equal("2", s.Delegate);
        Assert.Equal(2, s.OuterWords.Count);
        Assert.Equal("Normal", s.OuterWords["0"]);
        Assert.Equal("Generator Field Disconnect", s.OuterWords["1"]);
        Assert.Equal(3, s.InnerWords.Count);
        Assert.Equal("Bottle 1", s.InnerWords["0"]);
        Assert.Equal("Fuel and Hydraulic Disconnect", s.InnerWords["1"]);
        Assert.Equal("Bottle 2", s.InnerWords["2"]);
    }

    [Theory]
    [MemberData(nameof(EngineHandles))]
    public void EngineHandle_IsAReadOnlyRow_ThatReadsBothVars(string nodeId, string pullVar)
    {
        var d = Vars[nodeId];
        var inner = Md11CompositeState.InnerKeyFor(nodeId);
        Assert.Equal(pullVar, d.Name);
        Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
        Assert.True(d.RenderAsReadOnlyStatus);      // never a walkable combo
        Assert.False(d.RenderAsButton);
        Assert.Equal(new[] { nodeId, inner }, d.StateVariables!);
        // Read-only with StateVariables, so MainForm builds the status TextBox, whose text is
        // TryDescribeControlState's (PanelRowRules); the pull's two words are only its fallback.
        Assert.True(PanelRowRules.IsReadOnlyStatusRow(d));
        Assert.Equal(2, d.ValueDescriptions.Count);
        Assert.Equal("Normal", d.ValueDescriptions[0]);
        Assert.Equal("Generator Field Disconnect", d.ValueDescriptions[1]);

        var rotation = Vars[inner];
        Assert.Equal(nodeId, rotation.Name);        // the handle's own L:var
        Assert.Equal(SimVarType.LVar, rotation.Type);
        Assert.Equal(UpdateFrequency.OnRequest, rotation.UpdateFrequency);
        Assert.False(rotation.IsAnnounced);
        Assert.DoesNotContain(inner, Def.GetPanelControls().Values.SelectMany(k => k));
    }

    [Fact]
    public void ApuFireHandle_StaysAWalkableCombo_WithItsCentre()
    {
        Assert.Null(Map.Controls.Single(x => x.NodeId == "MD11_AOVHD_APUFIRE_KB").Composite);
        var d = Vars["MD11_AOVHD_APUFIRE_KB"];
        Assert.Equal("MD11_AOVHD_APUFIRE_KB", d.Name);
        Assert.False(d.RenderAsReadOnlyStatus);
        Assert.Null(d.StateVariables);
        Assert.Equal(new[] { "Bottle 1", "Normal", "Bottle 2" },
            d.ValueDescriptions.OrderBy(kv => kv.Key).Select(kv => kv.Value));
        Assert.DoesNotContain(Md11CompositeState.InnerKeyFor("MD11_AOVHD_APUFIRE_KB"), Vars.Keys);
    }
}

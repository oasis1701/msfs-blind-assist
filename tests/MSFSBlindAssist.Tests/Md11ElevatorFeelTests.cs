using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Spec D13: TFDi's Elevator Feel knob is a COMPOSITE control behind an outer %{if}. Its MANUAL latch
/// (MD11_OVHD_FLTCTL_ELEVFEEL_BT) reads "Auto" while it is off; once it is on, the knob's OWN five
/// reference-speed positions take over. The map once gave the row the latch with the knob's words, so
/// an aircraft in Auto read "Decrease Reference Speed Fast", the walker turned the knob's wheel while it
/// read the latch, and the direct-write fallback then wrote the latch itself. Every test here reads the
/// SHIPPED map, never a fixture.
/// </summary>
public class Md11ElevatorFeelTests
{
    private const string Knob = "MD11_OVHD_FLTCTL_ELEVFEEL_KB";
    private const string Latch = "MD11_OVHD_FLTCTL_ELEVFEEL_BT";

    private static readonly Md11Control Control = Md11ControlMap.Load().Controls.Single(c => c.NodeId == Knob);
    private static readonly Dictionary<string, SimVarDefinition> Vars = new TFDiMD11Definition().GetVariables();

    [Fact]
    public void TheKnob_InTheMap_IsTheLatchThenItsOwnPosition()
    {
        Assert.Equal(Latch, Control.StateVar);          // unchanged: the row's key reads the latch
        Assert.Empty(Control.ValueMap);
        var s = Control.Composite!;
        Assert.Equal(Latch, s.OuterVar);
        var word = Assert.Single(s.OuterWords);         // ONE outer word: the case D13's C# rule is for
        Assert.Equal("0", word.Key);
        Assert.Equal("Auto", word.Value);
        Assert.Equal("1", s.Delegate);
        Assert.Equal(Knob, s.InnerVar);
        Assert.Equal(new[]
            {
                "Decrease Reference Speed Fast", "Decrease Reference Speed Slow", "Neutral",
                "Increase Reference Speed Slow", "Increase Reference Speed Fast",
            },
            s.InnerWords.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Value));
    }

    /// <summary>
    /// MainForm's row chain tries a slider, then a Button, then the numeric read-out (which declines a
    /// row PanelRowRules claims), then the status box — so a row that is neither slider nor Button and
    /// passes PanelRowRules can never reach the plain Button at the end of the chain, whose click writes
    /// 1 straight into the row's var: here the MANUAL latch. With its ONE description the old rule sent
    /// the knob exactly there.
    /// </summary>
    [Fact]
    public void TheKnob_IsTheReadOnlyStatusBox_NeverThePlainButtonThatWritesItsVar()
    {
        var d = Vars[Knob];
        Assert.Equal(Latch, d.Name);
        Assert.Equal(UpdateFrequency.OnRequest, d.UpdateFrequency);
        Assert.True(d.RenderAsReadOnlyStatus);
        Assert.False(d.RenderAsSlider);
        Assert.False(d.RenderAsButton);
        var only = Assert.Single(d.ValueDescriptions);  // ONE word: the case D13's rule exists for
        Assert.Equal(0.0, only.Key);
        Assert.Equal("Auto", only.Value);
        Assert.True(PanelRowRules.IsReadOnlyStatusRow(d));
    }

    /// <summary>
    /// The row's own key reads the LATCH, so a move of the knob reaches the row only through its
    /// StateVariables: MainForm relabels every control that lists the key just delivered
    /// (RebuildStateDependents, RelabelStateDependents — on every update, before the definition sees
    /// it), and the new text is TryDescribeControlState's. The knob's position is read under its own
    /// inner key, never through the row's.
    /// </summary>
    [Fact]
    public void TheKnob_IsRelabelledWhenTheLatchOrTheKnobIsRead()
    {
        var inner = Md11CompositeState.InnerKeyFor(Knob);
        Assert.Equal(new[] { Knob, inner }, Vars[Knob].StateVariables!);
        var position = Vars[inner];
        Assert.Equal(Knob, position.Name);              // the knob's own L:var
        Assert.Equal(SimVarType.LVar, position.Type);
        Assert.Equal(UpdateFrequency.OnRequest, position.UpdateFrequency);
        Assert.False(position.IsAnnounced);
    }

    /// <summary>What TryDescribeControlState composes for the row, from the shipped block.</summary>
    [Theory]
    [InlineData(0.0, null, "Auto")]                               // latch off: Auto, whatever the knob
    [InlineData(0.0, 2.0, "Auto")]
    [InlineData(1.0, 0.0, "Decrease Reference Speed Fast")]       // latch on: the knob's own word
    [InlineData(1.0, 1.0, "Decrease Reference Speed Slow")]
    [InlineData(1.0, 2.0, "Neutral")]
    [InlineData(1.0, 3.0, "Increase Reference Speed Slow")]
    [InlineData(1.0, 4.0, "Increase Reference Speed Fast")]
    public void TheKnob_SaysAutoOrTheKnobsOwnWord(double latch, double? knob, string expected)
        => Assert.Equal(expected, Md11CompositeState.Describe(Control.Composite, latch, knob));

    [Fact]
    public void TheKnob_IsRefusedByName()
        => Assert.Equal("Elevator Feel cannot be operated from this panel yet.",
                        Md11CompositeState.RefusalSentence(Control.DisplayLabel));
}

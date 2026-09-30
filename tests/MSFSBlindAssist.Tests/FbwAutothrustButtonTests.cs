using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class FbwAutothrustButtonTests
{
    [Theory]
    [InlineData(0.0, "Disengaged")]
    [InlineData(1.0, "Armed")]
    [InlineData(2.0, "Active")]
    public void The_A320_button_names_the_autothrust_state_its_definition_describes(double value, string state)
    {
        var defs = new FlyByWireA320Definition().GetVariables();

        var label = FbwAutothrustButton.Label(value, defs);

        Assert.Equal($"A/THR ({state})", label.Text);
        Assert.Equal($"Autothrust {state}", label.AccessibleName);
    }

    [Theory]
    [InlineData(0.0, "Disengaged")]
    [InlineData(1.0, "Armed")]
    [InlineData(2.0, "Active")]
    public void The_A380_button_names_the_autothrust_state_its_definition_describes(double value, string state)
    {
        var defs = new FlyByWireA380Definition().GetVariables();

        var label = FbwAutothrustButton.Label(value, defs);

        Assert.Equal($"A/THR ({state})", label.Text);
        Assert.Equal($"Autothrust {state}", label.AccessibleName);
    }

    [Fact]
    public void A_state_not_yet_delivered_names_no_state()
    {
        var defs = new FlyByWireA320Definition().GetVariables();

        var label = FbwAutothrustButton.Label(null, defs);

        Assert.Equal("A/THR", label.Text);
        Assert.Equal("Autothrust", label.AccessibleName);
    }

    [Fact]
    public void A_value_the_definition_does_not_describe_names_no_state()
    {
        var defs = new FlyByWireA320Definition().GetVariables();

        var label = FbwAutothrustButton.Label(7.0, defs);

        Assert.Equal("A/THR", label.Text);
        Assert.Equal("Autothrust", label.AccessibleName);
    }

    // The Headwind A330 opens the A320 autopilot window (it inherits the Ctrl+P handler),
    // and the A339X's own fbw.wasm publishes A32NX_AUTOTHRUST_STATUS.
    [Theory]
    [InlineData(0.0, "Disengaged")]
    [InlineData(1.0, "Armed")]
    [InlineData(2.0, "Active")]
    public void The_A330_button_names_the_autothrust_state_its_definition_describes(double value, string state)
    {
        var defs = new HeadwindA330Definition().GetVariables();

        var label = FbwAutothrustButton.Label(value, defs);

        Assert.Equal($"A/THR ({state})", label.Text);
        Assert.Equal($"Autothrust {state}", label.AccessibleName);
    }

    // The panel's status box and combos turn a value into words through the definition's
    // own DescriptionKeyFor, so the button must too, or the two can name different states.
    [Fact]
    public void The_value_is_read_through_the_definitions_own_description_key()
    {
        var defs = new Dictionary<string, SimVarDefinition>
        {
            [FbwAutothrustButton.StatusVar] = new SimVarDefinition
            {
                Name = FbwAutothrustButton.StatusVar,
                ValueDescriptions = new Dictionary<double, string> { [0] = "Disengaged", [2] = "Active" },
                ValueToDescriptionKey = v => v > 0 ? 2 : 0,
            },
        };

        var label = FbwAutothrustButton.Label(0.3, defs);

        Assert.Equal("Autothrust Active", label.AccessibleName);
    }
}

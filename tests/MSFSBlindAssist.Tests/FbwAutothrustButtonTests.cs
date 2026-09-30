using MSFSBlindAssist.Aircraft;
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

        Assert.Equal("Autothrust", label.AccessibleName);
    }
}

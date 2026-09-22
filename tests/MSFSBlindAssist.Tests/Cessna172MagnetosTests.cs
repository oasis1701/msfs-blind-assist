// tests/MSFSBlindAssist.Tests/Cessna172MagnetosTests.cs
// The C172's key switch has five positions but the sim exposes it as two magneto bools plus
// the starter bool (RECIP ENG LEFT/RIGHT MAGNETO:1, GENERAL ENG STARTER:1). MAGNETO1_SET takes
// 0 Off / 1 Right / 2 Left / 3 Both / 4 Start — the same numbering the composer returns, so a
// combo pick's key IS the event parameter.
using MSFSBlindAssist.Aircraft.C172;

namespace MSFSBlindAssist.Tests;

public class Cessna172MagnetosTests
{
    [Theory]
    [InlineData(false, false, false, Cessna172Magnetos.Off)]
    [InlineData(false, true,  false, Cessna172Magnetos.Right)]
    [InlineData(true,  false, false, Cessna172Magnetos.Left)]
    [InlineData(true,  true,  false, Cessna172Magnetos.Both)]
    public void The_two_magneto_bools_compose_into_one_position(bool left, bool right, bool starter, int expected)
    {
        Assert.Equal(expected, Cessna172Magnetos.Position(left, right, starter));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void An_engaged_starter_outranks_the_magnetos(bool left, bool right)
    {
        Assert.Equal(Cessna172Magnetos.Start, Cessna172Magnetos.Position(left, right, starter: true));
    }

    [Theory]
    [InlineData(Cessna172Magnetos.Off, "Off")]
    [InlineData(Cessna172Magnetos.Right, "Right")]
    [InlineData(Cessna172Magnetos.Left, "Left")]
    [InlineData(Cessna172Magnetos.Both, "Both")]
    [InlineData(Cessna172Magnetos.Start, "Start")]
    public void Each_position_has_speakable_text(int position, string expected)
    {
        Assert.Equal(expected, Cessna172Magnetos.Text(position));
    }

    // START is momentary and owned by the Start Engine button's state machine; a pilot must never
    // be able to leave the key there from a combo.
    [Fact]
    public void Start_is_not_a_selectable_combo_position()
    {
        Assert.Equal(4, Cessna172Magnetos.SelectablePositions.Count);
        Assert.False(Cessna172Magnetos.SelectablePositions.ContainsKey(Cessna172Magnetos.Start));
        Assert.False(Cessna172Magnetos.IsSelectable(Cessna172Magnetos.Start));
        Assert.True(Cessna172Magnetos.IsSelectable(Cessna172Magnetos.Both));
    }

    [Fact]
    public void Selectable_positions_are_keyed_on_the_event_parameter()
    {
        Assert.Equal("Off",   Cessna172Magnetos.SelectablePositions[0]);
        Assert.Equal("Right", Cessna172Magnetos.SelectablePositions[1]);
        Assert.Equal("Left",  Cessna172Magnetos.SelectablePositions[2]);
        Assert.Equal("Both",  Cessna172Magnetos.SelectablePositions[3]);
    }
}

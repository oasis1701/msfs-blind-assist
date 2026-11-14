// Tests for MSFSBlindAssist.Navigation.TurnShape.IsCleanTurn: the clean-turn test
// the taxi Turn pick uses to decide whether a charted path is a plain left or right.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class TurnShapeTests
{
    private static IReadOnlyList<(double Bearing, double LengthMeters)> Legs(params (double, double)[] legs)
        => legs.Select(l => (l.Item1, l.Item2)).ToList();

    private static IReadOnlyList<(double Bearing, double LengthMeters)> KbosConnector() => Legs(
        (223, 22.1), (234, 41.1), (251.6, 37.7), (264.2, 29.6), (111.5, 61.4), (110, 27.4), (110, 27.4));

    [Fact]
    public void Plain_left_is_clean_left_and_not_clean_right()
    {
        var legs = Legs((90, 200));
        Assert.True(TurnShape.IsCleanTurn(180, legs, 0, TurnSide.Left));
        Assert.False(TurnShape.IsCleanTurn(180, legs, 0, TurnSide.Right));
    }

    [Fact]
    public void Straight_ahead_is_neither()
    {
        var legs = Legs((185, 200));
        Assert.False(TurnShape.IsCleanTurn(180, legs, 0, TurnSide.Left));
        Assert.False(TurnShape.IsCleanTurn(180, legs, 0, TurnSide.Right));
    }

    [Fact]
    public void Kbos_connector_then_reversal_is_not_a_clean_left()
    {
        var legs = KbosConnector();
        Assert.False(TurnShape.IsCleanTurn(180, legs, 0, TurnSide.Left));
        Assert.True(TurnShape.IsCleanTurn(180, legs, 0, TurnSide.Right));
    }

    [Fact]
    public void Kbos_connector_with_the_join_at_its_end_is_not_a_clean_left_or_right()
    {
        var legs = KbosConnector();
        Assert.False(TurnShape.IsCleanTurn(180, legs, 130.5, TurnSide.Left));
        Assert.False(TurnShape.IsCleanTurn(180, legs, 130.5, TurnSide.Right));
    }

    [Fact]
    public void Kbos_off_chart_leg_is_a_clean_left()
    {
        var legs = Legs((183, 96.4), (109, 59.2), (110, 7.4), (108, 97.1));
        Assert.True(TurnShape.IsCleanTurn(180, legs, 96.4, TurnSide.Left));
    }

    [Fact]
    public void A_small_contrary_wiggle_is_tolerated()
    {
        var legs = Legs((199.4, 92.2), (110, 27.4), (109, 59.2));
        Assert.True(TurnShape.IsCleanTurn(180, legs, 92.2, TurnSide.Left));
    }

    [Fact]
    public void A_hairpin_is_never_clean()
    {
        var legs = Legs((10, 50), (150, 100));
        Assert.False(TurnShape.IsCleanTurn(0, legs, 50, TurnSide.Right));
    }

    [Fact]
    public void The_walk_stops_80_m_past_the_join()
    {
        var legs = Legs((90, 100), (90, 100), (270, 100));
        Assert.True(TurnShape.IsCleanTurn(180, legs, 0, TurnSide.Left));
    }

    [Fact]
    public void Empty_legs_are_not_clean()
    {
        var legs = Legs();
        Assert.False(TurnShape.IsCleanTurn(180, legs, 0, TurnSide.Left));
        Assert.False(TurnShape.IsCleanTurn(180, legs, 0, TurnSide.Right));
    }
}

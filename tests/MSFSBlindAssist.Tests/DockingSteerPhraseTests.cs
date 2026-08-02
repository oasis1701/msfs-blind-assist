// Characterization tests for the docking steering-demand phrase
// (DockingGuidanceManager.SteerPhrase): the spoken turn quantification added because
// the docking pan tone saturates at 15° — a hard-panned ear can't distinguish a 16°
// nudge from a 35° maximum-tiller swing on a slow-yawing airframe like the A380.
// Convention: + = steer right (ComputeLineupError).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class DockingSteerPhraseTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(2.9)]
    [InlineData(-2.9)]
    public void Deadband_is_silent(double deg)
        => Assert.Equal(string.Empty, DockingGuidanceManager.SteerPhrase(deg));

    [Theory]
    [InlineData(3.0, "Slight right, 5 degrees.")]
    [InlineData(8.0, "Slight right, 10 degrees.")]
    [InlineData(-8.0, "Slight left, 10 degrees.")]
    public void Small_demands_say_slight(double deg, string expected)
        => Assert.Equal(expected, DockingGuidanceManager.SteerPhrase(deg));

    [Theory]
    [InlineData(15.0, "Right, 15 degrees.")]
    [InlineData(-18.0, "Left, 20 degrees.")]
    public void Moderate_demands_are_plain(double deg, string expected)
        => Assert.Equal(expected, DockingGuidanceManager.SteerPhrase(deg));

    [Theory]
    [InlineData(25.0, "Sharp right, 25 degrees.")]
    [InlineData(35.0, "Sharp right, 35 degrees.")]
    [InlineData(-30.0, "Sharp left, 30 degrees.")]
    public void Saturated_demands_say_sharp(double deg, string expected)
        => Assert.Equal(expected, DockingGuidanceManager.SteerPhrase(deg));

    [Fact]
    public void Rounding_never_reports_zero_degrees()
        // 3.0-3.75° would round to 5, but pin the floor explicitly: no non-silent
        // phrase may ever say "0 degrees".
        => Assert.Equal("Slight right, 5 degrees.", DockingGuidanceManager.SteerPhrase(3.1));

    [Theory]
    [InlineData(12.4, "Slight right, 10 degrees.")]  // was "Right, 10 degrees." — plain word, Slight number
    [InlineData(-12.4, "Slight left, 10 degrees.")]
    [InlineData(24.9, "Sharp right, 25 degrees.")]   // was "Right, 25 degrees." — 25.0 said "Sharp"
    public void Strength_word_matches_the_number_actually_spoken(double deg, string expected)
        => Assert.Equal(expected, DockingGuidanceManager.SteerPhrase(deg));

    [Theory]
    [InlineData(12.5, "Right, 15 degrees.")]         // banker's rounding gave 10
    [InlineData(-12.5, "Left, 15 degrees.")]
    [InlineData(22.5, "Sharp right, 25 degrees.")]   // banker's rounding gave 20
    public void Midpoints_round_away_from_zero(double deg, string expected)
        => Assert.Equal(expected, DockingGuidanceManager.SteerPhrase(deg));
}

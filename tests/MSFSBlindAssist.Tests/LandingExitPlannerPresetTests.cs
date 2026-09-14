// Characterization tests for LandingExitPlannerPreset — what the Landing Exit Planner opens with
// (PR #236 review, finding F11: with no ILS destination set, the runway box fell back to the
// airport's first runway although the loaded flight plan names the arrival runway, and a plan made
// against that default is how issue #234 began).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class LandingExitPlannerPresetTests
{
    private static readonly LandingExitPlannerPresetResult Nothing = new(null, null);

    [Fact]
    public void The_ils_destination_wins_over_the_flight_plan()
        => Assert.Equal(new LandingExitPlannerPresetResult("OMDB", "12R"),
            LandingExitPlannerPreset.Resolve("omdb", "12R", "OMAA", "31L"));

    [Fact]
    public void Without_an_ils_destination_the_flight_plan_arrival_is_used()
        => Assert.Equal(new LandingExitPlannerPresetResult("OMDB", "30L"),
            LandingExitPlannerPreset.Resolve(null, null, " omdb ", " 30L "));

    [Fact]
    public void Blank_ils_values_are_ignored()
        => Assert.Equal(new LandingExitPlannerPresetResult("OMDB", "30L"),
            LandingExitPlannerPreset.Resolve("", "  ", "OMDB", "30L"));

    [Theory]
    [InlineData("OMDB", "")]
    [InlineData("OMDB", null)]
    [InlineData("", "30L")]
    [InlineData("   ", "30L")]
    [InlineData(null, "30L")]
    public void A_flight_plan_needs_both_its_arrival_airport_and_runway(string? icao, string? runway)
        => Assert.Equal(Nothing, LandingExitPlannerPreset.Resolve(null, null, icao, runway));

    [Fact]
    public void Nothing_known_opens_the_planner_empty()
        => Assert.Equal(Nothing, LandingExitPlannerPreset.Resolve(null, null, null, null));

    [Theory]
    [InlineData("09", "9")]
    [InlineData("30L", "30l")]
    [InlineData(" 30L ", "30L")]
    public void Designators_match_after_normalisation(string a, string b)
        => Assert.True(LandingExitPlannerPreset.DesignatorsMatch(a, b));

    [Theory]
    [InlineData("30L", "30R")]
    [InlineData("12L", "30R")]
    [InlineData("09", "")]
    [InlineData("09", null)]
    [InlineData(null, null)]
    public void Different_or_missing_designators_do_not_match(string? a, string? b)
        => Assert.False(LandingExitPlannerPreset.DesignatorsMatch(a, b));
}

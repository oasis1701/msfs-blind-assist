// Characterization tests for MSFSBlindAssist.Services.DistanceFormatter.
//
// Ports the golden cases from tools/DistanceUnitsProbe/Program.cs. DistanceFormatter is
// a DISPLAY layer only (never used for guidance thresholds — see CLAUDE.md), and its
// UnitProvider is process-global mutable state, so every test sets it explicitly and
// this class shares the "DistanceUnitGlobalState" collection with DistanceMilestonesTests
// to avoid cross-test races (see DistanceUnitGlobalStateCollection.cs).
//
// This is characterization, not spec verification: values are taken from the probe /
// derived by reasoning about the source and confirmed by running the tests; if a
// literal ever disagrees with actual output, the test must be corrected to match real
// output, not the other way around.

using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

[Collection("DistanceUnitGlobalState")]
public class DistanceFormatterTests
{
    // --- Metres mode ---------------------------------------------------------

    [Fact]
    public void FromMetres_rounds_to_nearest_10_between_100_and_500()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        Assert.Equal("150 metres", DistanceFormatter.FromMetres(152));
    }

    [Fact]
    public void FromMetres_rounds_to_nearest_5_below_100()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        Assert.Equal("45 metres", DistanceFormatter.FromMetres(47));
    }

    [Fact]
    public void FromMetres_short_form_omits_the_word_metres()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        Assert.Equal("250 m", DistanceFormatter.FromMetres(250, shortForm: true));
    }

    [Fact]
    public void FromFeet_converts_into_metres_when_metric_is_active()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        Assert.Equal("460 metres", DistanceFormatter.FromFeet(1500));
    }

    [Fact]
    public void UnitWord_is_metres_in_metric_mode()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        Assert.Equal("metres", DistanceFormatter.UnitWord());
    }

    [Fact]
    public void FromMetres_clamps_negative_values_to_zero()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        Assert.Equal("0 metres", DistanceFormatter.FromMetres(-5));
    }

    // --- Feet mode -------------------------------------------------------------

    [Fact]
    public void FromFeet_rounds_to_nearest_50_at_1490ft()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("1500 feet", DistanceFormatter.FromFeet(1490));
    }

    [Fact]
    public void FromFeet_rounds_to_nearest_25_below_200ft()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("50 feet", DistanceFormatter.FromFeet(60));
    }

    [Fact]
    public void FromFeet_short_form_omits_the_word_feet()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("500 ft", DistanceFormatter.FromFeet(500, shortForm: true));
    }

    [Fact]
    public void FromMetres_converts_into_feet_when_imperial_is_active()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("500 feet", DistanceFormatter.FromMetres(150));
    }

    [Fact]
    public void UnitWord_is_feet_in_imperial_mode()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("feet", DistanceFormatter.UnitWord());
    }

    // --- Singular unit word (rounded == 1) ------------------------------------

    [Fact]
    public void FromMetres_uses_singular_metre_when_rounded_to_1()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        // Below 100 rounds to the nearest 5, so 1 metre only appears via round:false.
        Assert.Equal("1 metre", DistanceFormatter.FromMetres(1, round: false));
    }

    [Fact]
    public void FromFeet_uses_singular_foot_when_rounded_to_1()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("1 foot", DistanceFormatter.FromFeet(1, round: false));
    }

    // --- Precise (round:false) informational distances ------------------------

    [Fact]
    public void FromFeet_exact_skips_coarse_stepping()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("12467 feet", DistanceFormatter.FromFeet(12467, round: false));
    }

    [Fact]
    public void FromFeet_exact_short_form_skips_coarse_stepping()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("148 ft", DistanceFormatter.FromFeet(148, shortForm: true, round: false));
    }

    [Fact]
    public void FromFeet_default_rounds_the_same_exact_value()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("12450 feet", DistanceFormatter.FromFeet(12467));
    }

    [Fact]
    public void FromFeet_exact_metres_conversion_skips_coarse_stepping()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        Assert.Equal("3800 metres", DistanceFormatter.FromFeet(12467, round: false));
    }

    [Fact]
    public void FromMetres_exact_skips_coarse_stepping()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        Assert.Equal("3801 metres", DistanceFormatter.FromMetres(3801, round: false));
    }

    [Fact]
    public void FromMetres_default_rounds_the_same_exact_value()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        Assert.Equal("3800 metres", DistanceFormatter.FromMetres(3801));
    }

    // --- Rounding-step boundaries (metres: <100 -> 5, <500 -> 10, else -> 50) ---

    [Theory]
    [InlineData(99.0, "100 metres")]   // just under the 100 m step-change boundary
    [InlineData(100.0, "100 metres")]  // at the boundary (step becomes 10)
    [InlineData(499.0, "500 metres")]  // just under the 500 m step-change boundary
    [InlineData(500.0, "500 metres")]  // at the boundary (step becomes 50)
    [InlineData(1000.0, "1000 metres")]
    public void FromMetres_rounding_step_changes_at_documented_boundaries(double input, string expected)
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
        Assert.Equal(expected, DistanceFormatter.FromMetres(input));
    }

    // --- Rounding-step boundaries (feet: <200 -> 25, else -> 50) ---------------

    [Theory]
    [InlineData(199.0, "200 feet")]
    [InlineData(200.0, "200 feet")]
    [InlineData(1000.0, "1000 feet")]
    public void FromFeet_rounding_step_changes_at_the_200ft_boundary(double input, string expected)
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal(expected, DistanceFormatter.FromFeet(input));
    }
}

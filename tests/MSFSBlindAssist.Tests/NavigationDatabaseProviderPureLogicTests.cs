// Characterization tests for the pure-logic helpers in
// MSFSBlindAssist.Database.NavigationDatabaseProvider and
// MSFSBlindAssist.Navigation.FlightPlanManager.AppendWaypoints.
//
// These methods were promoted from private/private static to internal/internal
// static (access-modifier only, zero logic changes) so they can be exercised
// directly here via InternalsVisibleTo. This is characterization, not spec
// verification: expected values were captured by reading the real method
// bodies and confirmed by running the tests; if a literal ever disagrees with
// actual output, the test must be corrected to match real output, never the
// production method.

using MSFSBlindAssist.Database;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class NavigationDatabaseProviderPureLogicTests
{
    // --- BuildManeuverLabel (never-drop-fix-less-leg) -----------------------

    [Fact]
    public void BuildManeuverLabel_CA_leg_contains_course_and_altitude()
    {
        // type='CA' (to-altitude), course=71, alt1=600, no turn direction.
        string label = NavigationDatabaseProvider.BuildManeuverLabel("CA", 71, "", 600);

        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.Contains("600", label);
        Assert.Contains("71", label);
        // Captured actual output from the real method body.
        Assert.Equal("Climb course 71° to 600 feet", label);
    }

    [Fact]
    public void BuildManeuverLabel_VM_vectors_leg_with_null_course_does_not_throw()
    {
        // type='VM' (heading to manual termination / vectors), course=null.
        string label = NavigationDatabaseProvider.BuildManeuverLabel("VM", null, "", null);

        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.Contains("vectors", label, StringComparison.OrdinalIgnoreCase);
        // Captured actual output: heading prefix ("Heading") + empty course collapses via Trim().
        Assert.Equal("Heading , vectors", label);
    }

    [Theory]
    [InlineData("L", "left turn")]
    [InlineData("R", "right turn")]
    [InlineData("", null)]
    public void BuildManeuverLabel_turn_direction_suffix(string turnDir, string? expectedSuffix)
    {
        string label = NavigationDatabaseProvider.BuildManeuverLabel("CA", 90, turnDir, 1000);

        if (expectedSuffix is null)
        {
            Assert.DoesNotContain("turn", label, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains(expectedSuffix, label);
        }
    }

    // --- FormatAltitudeRestriction -------------------------------------------

    [Theory]
    [InlineData("A", 5000.0, null, "AT 5000 FT")]
    [InlineData("+", 5000.0, null, "AT OR ABOVE 5000 FT")]
    [InlineData("-", 5000.0, null, "AT OR BELOW 5000 FT")]
    [InlineData("B", 3000.0, 5000.0, "BETWEEN 3000 AND 5000 FT")]
    public void FormatAltitudeRestriction_descriptor_wording(
        string descriptor, double? alt1, double? alt2, string expected)
    {
        string? actual = NavigationDatabaseProvider.FormatAltitudeRestriction(descriptor, alt1, alt2);

        Assert.Equal(expected, actual);
    }

    // --- ParseArincRunway ------------------------------------------------------

    [Theory]
    [InlineData("RW30B", 30, "B")]
    [InlineData("RW25R", 25, "R")]
    [InlineData("RW35", 35, "")]
    public void ParseArincRunway_valid_names(string arinc, int expectedNumber, string expectedSide)
    {
        var result = NavigationDatabaseProvider.ParseArincRunway(arinc);

        Assert.NotNull(result);
        Assert.Equal(expectedNumber, result!.Value.number);
        Assert.Equal(expectedSide, result.Value.side);
    }

    [Theory]
    [InlineData("VORA")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseArincRunway_non_runway_names_return_null(string? arinc)
    {
        var result = NavigationDatabaseProvider.ParseArincRunway(arinc);

        Assert.Null(result);
    }

    // --- SplitRunwayDesignator (zero-pad normalization) -------------------------

    [Theory]
    [InlineData("7R", 7, "R")]
    [InlineData("07R", 7, "R")]
    public void SplitRunwayDesignator_normalizes_zero_padding(string runway, int expectedNumber, string expectedSide)
    {
        var result = NavigationDatabaseProvider.SplitRunwayDesignator(runway);

        Assert.NotNull(result);
        Assert.Equal(expectedNumber, result!.Value.number);
        Assert.Equal(expectedSide, result.Value.side);
    }

    // --- ArincCoversTarget -----------------------------------------------------

    [Fact]
    public void ArincCoversTarget_broad_side_covers_both_L_and_R()
    {
        var tag = (30, "B");

        Assert.True(NavigationDatabaseProvider.ArincCoversTarget(tag, (30, "L")));
        Assert.True(NavigationDatabaseProvider.ArincCoversTarget(tag, (30, "R")));
    }

    [Fact]
    public void ArincCoversTarget_specific_side_does_not_cover_the_other_side()
    {
        var tag = (30, "L");

        Assert.False(NavigationDatabaseProvider.ArincCoversTarget(tag, (30, "R")));
    }

    // --- ProcedureServesRunway ---------------------------------------------------

    [Fact]
    public void ProcedureServesRunway_concrete_runway_name_is_authoritative_over_broad_arinc()
    {
        // runwayName "25L" is populated -> authoritative; must NOT fall back to the
        // broad "RW25B" arinc tag to also match "25R".
        bool result = NavigationDatabaseProvider.ProcedureServesRunway("25L", "RW25B", "25R");

        Assert.False(result);
    }

    [Theory]
    [InlineData("30L")]
    [InlineData("30R")]
    public void ProcedureServesRunway_jeppesen_fallback_when_runway_name_is_null(string target)
    {
        // runwayName is null -> falls back to the ARINC tag; "RW30B" covers both sides.
        bool result = NavigationDatabaseProvider.ProcedureServesRunway(null, "RW30B", target);

        Assert.True(result);
    }

    // --- FlightPlanManager.AppendWaypoints (transition boundary-fix dedup) -----

    private static WaypointFix Wp(string ident) => new() { Ident = ident };

    [Fact]
    public void AppendWaypoints_drops_duplicated_boundary_fix_case_insensitively()
    {
        var target = new List<WaypointFix> { Wp("FOO"), Wp("MERUV") };
        var additions = new List<WaypointFix> { Wp("meruv"), Wp("FIXB") };

        FlightPlanManager.AppendWaypoints(target, additions);

        Assert.Equal(new[] { "FOO", "MERUV", "FIXB" }, target.Select(w => w.Ident));
    }

    [Fact]
    public void AppendWaypoints_appends_all_when_no_boundary_duplicate()
    {
        var target = new List<WaypointFix> { Wp("A") };
        var additions = new List<WaypointFix> { Wp("B"), Wp("C") };

        FlightPlanManager.AppendWaypoints(target, additions);

        Assert.Equal(new[] { "A", "B", "C" }, target.Select(w => w.Ident));
    }
}

// Formatting tests for the two small helpers extracted out of
// MSFSBlindAssist.Forms.ElectronicFlightBagForm.GetRunwayDetailedInfo (Forms/ElectronicFlightBagForm.cs):
//   internal static string FormatRunwayHeadingsAndCoordinates(double endHeadingTrue, double magVar, object? endLonx, object? endLaty)
//   internal static string FormatRunwayPatternAltitude(object? patternAltitude, object? endAltitudeMsl)
// which take already-resolved column values (not a DB connection) and are called from
// GetRunwayDetailedInfo exactly where the original AppendLine calls were.
//
// SCOPE — read before assuming these tests guard the CLAUDE.md aliasing invariant: they do
// NOT. They pass hard-coded doubles/objects straight to the formatter, so they only pin
// correct RENDERING (F1 rounding, magnetic-heading subtraction, the "ft"/"°" suffixes, the
// DBNull-to-blank behavior) given values that are ALREADY correctly resolved. They never open
// a database and cannot detect a dropped `AS end_heading`/`AS end_altitude`/`AS end_lonx`/
// `AS end_laty` SQL alias, nor a C# revert from reader["end_heading"] back to the ambiguous
// bare reader["heading"] — the exact regression the CLAUDE.md invariant ("GetRunwayDetailedInfo
// must explicitly alias the runway_end columns") is about.
//
// That SQL-level regression is covered separately, by a synthetic-SQLite fixture test against
// the newly extracted internal static GetRunwayDetailedInfoCore(dbPath, simulatorVersion, icao,
// runwayId) — see tests/MSFSBlindAssist.Tests/RunwayInfoAliasingTests.cs. GetRunwayDetailedInfo
// (the private instance method) is now a two-line wrapper that resolves dbPath/simulatorVersion
// via DatabasePathResolver/SettingsManager exactly as before, then delegates to that core —
// byte-identical output, zero SQL/logic change from before the extraction. Full extraction of
// GetRunwayDetailedInfoCore (rather than reflection against a fully-constructed
// ElectronicFlightBagForm needing a FlightPlanManager/SimConnectManager/ScreenReaderAnnouncer/
// WaypointTracker) turned out not to need any of those dependencies, since the method only used
// `this` for the two lines now left behind in the public wrapper.

using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class ElectronicFlightBagFormRunwayInfoTests
{
    // --- FormatRunwayHeadingsAndCoordinates ---------------------------------------------

    [Fact]
    public void Secondary_end_shows_its_own_heading_not_the_reciprocal_primary_ends_heading()
    {
        // This test only pins RENDERING: given a value that is already the correct
        // (secondary) end's own heading, the formatter must render exactly that value and
        // nothing else. It does NOT exercise the SQL that resolves endHeadingTrue in the
        // first place — a dropped `AS end_heading` alias, or a C# revert to the ambiguous
        // bare reader["heading"], is invisible here because the caller passes in a plain
        // double. See RunwayInfoAliasingTests.cs for the fixture test that drives the real
        // aliased query and catches that regression.
        string result = ElectronicFlightBagForm.FormatRunwayHeadingsAndCoordinates(
            endHeadingTrue: 240.0, magVar: -13.5, endLonx: -122.375, endLaty: 37.618);

        Assert.Contains("True Heading:         240.0°", result);
        Assert.DoesNotContain("60.0°", result);
    }

    [Fact]
    public void Magnetic_heading_subtracts_mag_var_from_true_heading()
        // 240.0 - (-13.5) = 253.5
        => Assert.Contains(
            "Magnetic Heading:     253.5°",
            ElectronicFlightBagForm.FormatRunwayHeadingsAndCoordinates(240.0, -13.5, 0.0, 0.0));

    [Fact]
    public void Coordinates_use_the_selected_ends_own_longitude_and_latitude()
    {
        string result = ElectronicFlightBagForm.FormatRunwayHeadingsAndCoordinates(
            endHeadingTrue: 60.0, magVar: 5.0, endLonx: -122.375, endLaty: 37.618);

        Assert.Contains("Longitude:            -122.375", result);
        Assert.Contains("Latitude:             37.618", result);
    }

    [Fact]
    public void Missing_coordinates_render_as_blank_not_a_crash()
    {
        // reader["end_lonx"] is DBNull.Value when the column has no value; the original
        // code interpolates the raw reader value with no null-guard for this field
        // (unlike most other fields in the method, which explicitly check DBNull.Value) -
        // DBNull.Value.ToString() is "", so the line renders with a blank value.
        string result = ElectronicFlightBagForm.FormatRunwayHeadingsAndCoordinates(
            endHeadingTrue: 90.0, magVar: 0.0, endLonx: DBNull.Value, endLaty: DBNull.Value);

        Assert.Contains("Longitude:            \r\n", result);
        Assert.Contains("Latitude:             \r\n", result);
    }

    [Fact]
    public void Headings_and_coordinates_sections_are_both_present_with_correct_headers()
    {
        string result = ElectronicFlightBagForm.FormatRunwayHeadingsAndCoordinates(90.0, 0.0, 1.0, 2.0);
        Assert.Contains("HEADINGS:", result);
        Assert.Contains("COORDINATES:", result);
    }

    // --- FormatRunwayPatternAltitude ------------------------------------------------------

    [Fact]
    public void Pattern_altitude_and_msl_altitude_both_render_with_ft_suffix()
    {
        string result = ElectronicFlightBagForm.FormatRunwayPatternAltitude(patternAltitude: 1000, endAltitudeMsl: 13);

        Assert.Contains("Pattern Altitude:     1000 ft", result);
        Assert.Contains("Altitude (MSL):       13 ft", result);
    }

    [Fact]
    public void Msl_altitude_uses_the_selected_ends_own_altitude()
    {
        // Rendering only, same scope note as the heading test above: this just confirms the
        // formatter renders whatever endAltitudeMsl it's given, distinct from patternAltitude —
        // it does not exercise the `AS end_altitude` alias that resolves the real value (see
        // RunwayInfoAliasingTests.cs for that).
        string result = ElectronicFlightBagForm.FormatRunwayPatternAltitude(patternAltitude: 500, endAltitudeMsl: 42);
        Assert.Contains("Altitude (MSL):       42 ft", result);
        Assert.DoesNotContain("Altitude (MSL):       500 ft", result);
    }

    [Fact]
    public void Missing_altitude_values_render_as_blank_not_a_crash()
    {
        string result = ElectronicFlightBagForm.FormatRunwayPatternAltitude(DBNull.Value, DBNull.Value);
        Assert.Contains("Pattern Altitude:      ft", result);
        Assert.Contains("Altitude (MSL):        ft", result);
    }
}

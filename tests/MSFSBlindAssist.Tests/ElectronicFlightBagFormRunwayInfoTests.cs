// Characterization tests for the runway_end column-aliasing fix inside
// MSFSBlindAssist.Forms.ElectronicFlightBagForm.GetRunwayDetailedInfo (Forms/ElectronicFlightBagForm.cs).
//
// GetRunwayDetailedInfo itself is a large (~300 line) monolithic method: it opens a live
// SQLite connection via DatabasePathResolver/SettingsManager, runs a runway+runway_end+
// airport join, then a SECOND nested ILS query, then falls back to LittleNavMapProvider's
// spatial ILS recovery, interleaving all of that with StringBuilder formatting of ~40
// columns. Extracting the WHOLE method into a DB-free pure function (or standing up a
// synthetic-SQLite fixture and driving the private instance method through reflection on a
// fully-constructed ElectronicFlightBagForm, which itself needs a FlightPlanManager /
// SimConnectManager / ScreenReaderAnnouncer / WaypointTracker) was judged the HIGHER-risk
// option for this wave.
//
// Instead, the MINIMAL seam: the two blocks that actually consume the aliased runway_end
// columns (end_heading/end_lonx/end_laty/end_altitude — the exact columns the CLAUDE.md
// invariant "GetRunwayDetailedInfo must explicitly alias the runway_end columns" is about)
// were extracted VERBATIM (zero logic change) into
//   internal static string FormatRunwayHeadingsAndCoordinates(double endHeadingTrue, double magVar, object? endLonx, object? endLaty)
//   internal static string FormatRunwayPatternAltitude(object? patternAltitude, object? endAltitudeMsl)
// which take the already-resolved column values (not a DB connection) and are called from
// GetRunwayDetailedInfo exactly where the original AppendLine calls were. This directly pins
// the regression the invariant describes (a secondary runway end must show its OWN heading/
// coordinates/altitude, never the primary end's / reciprocal's 180°-off value) without
// touching the ILS resolution, LittleNavMapProvider fallback, or any other part of the
// monolithic method.

using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class ElectronicFlightBagFormRunwayInfoTests
{
    // --- FormatRunwayHeadingsAndCoordinates ---------------------------------------------

    [Fact]
    public void Secondary_end_shows_its_own_heading_not_the_reciprocal_primary_ends_heading()
    {
        // The exact bug this guards against: at an airport where primary end 06R has true
        // heading 60.0 and secondary end 24L has true heading 240.0, selecting 24L must
        // render 240.0 — a bare `r.*, re.*` join (no explicit end_heading alias) silently
        // resolved the ambiguous `heading` column to the runway-CENTER/primary-end value,
        // which would show 60.0 here (180 degrees off).
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
        // Same aliasing-fix rationale as the heading test above: end_altitude must be the
        // SELECTED end's own field elevation at the threshold, not the primary end's.
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

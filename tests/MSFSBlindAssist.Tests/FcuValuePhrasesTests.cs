// FcuValuePhrases turns an FCU selected value into the words the hardware-dial announcer speaks
// (PR #140), or null when the FCU window is not showing a selection.
//
// Heading and speed come from FBW's shim L:vars, which the FCU writes as -1 whenever the window
// shows dashes. V/S and FPA come from ARINC429 words whose SSM is Normal Operation ONLY while the
// window shows a selection in that word's own mode (FBW FcuComputer / A380PrimComputerFctl:
// No Computed Data while dashed or in the other mode) — the FCU's plain display values do NOT
// have that property: while dashed they carry the aircraft's LIVE heading, airspeed and vertical
// speed, which is how the first version announced the heading as the aircraft taxied.
//
// The units are display units (degrees, knots or Mach, feet per minute) on both airframes, so no
// SI conversion may appear here — FBW #10855's note on the A380 readout: a "looks like radians"
// guess mangles any heading of 006° or less, and the old ×196.85 spoke 500 fpm as "98400".

using System.Globalization;
using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FcuValuePhrasesTests : IDisposable
{
    private const uint FailureWarning = 0, NoComputedData = 1, FunctionalTest = 2, NormalOperation = 3;

    // The phrases format numbers in the CURRENT culture on purpose (they must match the readouts),
    // so the assertions below are only meaningful under a fixed one. Without this pin the suite is
    // red on a comma-decimal developer machine ("Mach 0,78") while en-US CI stays green.
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;
    public FcuValuePhrasesTests() => CultureInfo.CurrentCulture = new CultureInfo("en-US");
    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    /// <summary>Pack an ARINC429 word the way FBW's Arinc429Utils::toSimVar does.</summary>
    internal static double Word(uint ssm, float value) =>
        (double)(((ulong)ssm << 32) | (uint)BitConverter.SingleToInt32Bits(value));

    [Theory]
    [InlineData(345.0, "Heading 345 degrees")]
    [InlineData(5.0, "Heading 005 degrees")]     // inside the radian range an SI guess would scale
    [InlineData(0.0, "Heading 000 degrees")]
    [InlineData(360.0, "Heading 000 degrees")]
    [InlineData(249.6, "Heading 250 degrees")]
    [InlineData(359.6, "Heading 000 degrees")]   // round, THEN wrap — never "360"
    [InlineData(-0.0, "Heading 000 degrees")]    // not a dashes sentinel, and never "-000"
    public void Heading_speaks_whole_degrees(double shim, string expected)
    {
        Assert.Equal(expected, FcuValuePhrases.Heading(shim));
    }

    [Fact]
    public void Heading_is_silent_while_the_window_shows_dashes()
    {
        Assert.Null(FcuValuePhrases.Heading(-1.0));
    }

    [Theory]
    [InlineData(250.0, "Speed 250 knots")]
    [InlineData(249.7, "Speed 250 knots")]
    [InlineData(0.78, "Mach 0.78")]
    [InlineData(0.8, "Mach 0.80")]
    public void Speed_splits_mach_from_knots(double shim, string expected)
    {
        Assert.Equal(expected, FcuValuePhrases.Speed(shim));
    }

    [Fact]
    public void Speed_is_silent_while_the_window_shows_dashes()
    {
        Assert.Null(FcuValuePhrases.Speed(-1.0));
    }

    [Theory]
    [InlineData(10000.0, "feet", "Altitude 10000 feet")]
    [InlineData(3048.0, "meters", "Altitude 3048 meters")]
    [InlineData(100.0, "feet", "Altitude 100 feet")]
    public void Altitude_carries_its_unit(double value, string unit, string expected)
    {
        Assert.Equal(expected, FcuValuePhrases.Altitude(value, unit));
    }

    [Fact]
    public void Altitude_word_speaks_a_selection_in_feet()
    {
        Assert.Equal("Altitude 10000 feet", FcuValuePhrases.AltitudeWord(Word(NormalOperation, 10000f)));
    }

    [Fact]
    public void Altitude_word_is_silent_when_it_carries_no_computed_data()
    {
        Assert.Null(FcuValuePhrases.AltitudeWord(Word(NoComputedData, 5000f)));
    }

    [Theory]
    [InlineData(-1500f, "Vertical speed -1500 feet per minute")]
    [InlineData(500f, "Vertical speed 500 feet per minute")]
    [InlineData(1449f, "Vertical speed 1400 feet per minute")]   // the FCU's 100-fpm detent
    public void Vertical_speed_speaks_a_selection(float fpm, string expected)
    {
        Assert.Equal(expected, FcuValuePhrases.VerticalSpeed(Word(NormalOperation, fpm)));
    }

    [Fact]
    public void A_selected_vertical_speed_of_zero_is_spoken()
    {
        // Pushing the V/S knob levels off at V/S 0 — a real selection the window shows as "00",
        // unlike dashes. The old 0-means-dashes reading could not tell the two apart.
        Assert.Equal("Vertical speed 0 feet per minute",
            FcuValuePhrases.VerticalSpeed(Word(NormalOperation, 0f)));
    }

    [Theory]
    [InlineData(NoComputedData, -1300f)]   // dashed: the A32NX FCU carries the LIVE V/S here
    [InlineData(NoComputedData, 0f)]       // dashed on the A380 (value defaulted), or FPA mode
    public void Vertical_speed_is_silent_while_the_word_carries_no_computed_data(uint ssm, float fpm)
    {
        Assert.Null(FcuValuePhrases.VerticalSpeed(Word(ssm, fpm)));
    }

    [Theory]
    [InlineData(-3.0f, "FPA -3.0 degrees")]
    [InlineData(0.1f, "FPA 0.1 degrees")]
    [InlineData(-2.5f, "FPA -2.5 degrees")]
    [InlineData(0.0f, "FPA 0.0 degrees")]
    [InlineData(-0.0f, "FPA 0.0 degrees")]     // never "-0.0"
    public void Flight_path_angle_speaks_a_selection(float degrees, string expected)
    {
        Assert.Equal(expected, FcuValuePhrases.FlightPathAngle(Word(NormalOperation, degrees)));
    }

    [Theory]
    [InlineData(NoComputedData, -2.5f)]
    public void Flight_path_angle_is_silent_while_the_word_carries_no_computed_data(uint ssm, float degrees)
    {
        Assert.Null(FcuValuePhrases.FlightPathAngle(Word(ssm, degrees)));
    }

    [Fact]
    public void A_zero_speed_is_the_fcu_being_off_not_mach_zero() =>
        Assert.Equal(FcuValuePhrases.Unavailable, FcuValuePhrases.Speed(0.0));

    [Fact]
    public void A_failed_word_is_unavailable_and_a_no_computed_data_word_is_dashes()
    {
        Assert.Equal(FcuValuePhrases.Unavailable, FcuValuePhrases.AltitudeWord(Word(FailureWarning, 0f)));
        Assert.Equal(FcuValuePhrases.Unavailable, FcuValuePhrases.VerticalSpeed(Word(FailureWarning, 0f)));
        Assert.Equal(FcuValuePhrases.Unavailable, FcuValuePhrases.FlightPathAngle(Word(FailureWarning, 0f)));
        Assert.Null(FcuValuePhrases.VerticalSpeed(Word(NoComputedData, -1300f)));
        Assert.Null(FcuValuePhrases.FlightPathAngle(Word(NoComputedData, -3f)));
    }

    [Theory]
    [InlineData(FunctionalTest, 1000f)]   // the FCU's self-test: no selection to speak, and no dashes either
    public void A_functional_test_word_is_unavailable(uint ssm, float value)
    {
        Assert.Equal(FcuValuePhrases.Unavailable, FcuValuePhrases.AltitudeWord(Word(ssm, value)));
        Assert.Equal(FcuValuePhrases.Unavailable, FcuValuePhrases.VerticalSpeed(Word(ssm, value)));
        Assert.Equal(FcuValuePhrases.Unavailable, FcuValuePhrases.FlightPathAngle(Word(ssm, value)));
    }

    // ---- The Shift+H/S/V readouts: the words agree with the dial ----
    // A heading shim of -1 is dashes, said in words ("FCU heading managed"), never wrapped into
    // "359 degrees". A speed target is Mach below 10, else knots, and 0 is the FCU publishing
    // nothing (the A380 zeroes every output when its FCU is off) — never "mach 0.00".

    [Theory]
    [InlineData(-1.0, true, "FCU heading managed")]
    [InlineData(-1.0, false, "FCU heading managed")]
    [InlineData(345.0, false, "FCU heading 345 degrees, selected")]
    [InlineData(5.0, false, "FCU heading 005 degrees, selected")]
    [InlineData(360.0, true, "FCU heading 000 degrees, managed")]
    public void HeadingReadout_says_dashes_in_words(double shim, bool managed, string expected) =>
        Assert.Equal(expected, FcuValuePhrases.HeadingReadout(shim, managed));

    [Theory]
    [InlineData(0.78, "selected", "FCU speed mach 0.78, selected")]
    [InlineData(250.0, "selected", "FCU speed 250 knots, selected")]
    [InlineData(80.0, "selected", "FCU speed 080 knots, selected")]
    [InlineData(0.0, "selected", "FCU speed not available")]
    public void SpeedReadout_splits_mach_from_knots_and_never_says_mach_zero(double value, string status, string expected) =>
        Assert.Equal(expected, FcuValuePhrases.SpeedReadout(value, status));

    [Fact]
    public void The_vertical_and_not_available_readouts()
    {
        Assert.Equal("FCU vertical speed managed", FcuValuePhrases.ManagedVerticalReadout(fpaMode: false));
        Assert.Equal("FCU flight path angle managed", FcuValuePhrases.ManagedVerticalReadout(fpaMode: true));
        Assert.Equal("FCU altitude not available", FcuValuePhrases.NotAvailableReadout("altitude"));
    }

    [Theory]
    [InlineData(-1.0, null)]
    [InlineData(359.6, 0.0)]
    [InlineData(5.0, 5.0)]
    public void HeadingDegrees_is_the_one_normalisation(double shim, double? expected) =>
        Assert.Equal(expected, FcuValuePhrases.HeadingDegrees(shim));

    [Fact]
    public void The_suite_pins_en_US_so_a_comma_decimal_machine_formats_the_same()
    {
        Assert.Equal("en-US", CultureInfo.CurrentCulture.Name);
        Assert.Equal("Mach 0.78", FcuValuePhrases.Speed(0.78));
    }
}

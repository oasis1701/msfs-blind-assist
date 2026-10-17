// The Airport Lookup's (Shift+E) airport-row formatters in ElectronicFlightBagForm.
// Live values from fs2020.sqlite (2026-10-13): ESGG tower_frequency 118605 and atis_frequency
// 118380 (kHz), laty 57.65998458862305, lonx 12.291107177734375 (32-bit floats), mag_var
// 5.1530561447143555, longest_runway_heading 206.0055694580078, country NULL. The lookup printed
// "Tower: 0,119 MHz", the raw doubles in the Windows number format, and a blank Country line.
// From fs2024.sqlite (Navdatareader 1.2.4, 2026-10-06): the same frequencies in Hz (ESGG tower
// 118605000, KJFK ATIS 128725000), altitude 510.045959472656, transition_level 18000.0 (feet)
// or 0.0 where navdata has none.
// Every case runs under sv-SE (comma decimal): CI's en-US cannot catch a culture leak.

using System.Globalization;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class AirportLookupFormatTests
{
    private static void UnderCulture(string cultureName, Action assertions)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            assertions();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData(118605, "118.605 MHz")]   // ESGG tower
    [InlineData(118380, "118.380 MHz")]   // ESGG ATIS
    [InlineData(124800, "124.800 MHz")]   // CYQX ATIS
    [InlineData(122950, "122.950 MHz")]   // KJFK UNICOM
    public void Frequency_in_kilohertz_reads_as_megahertz_with_a_dot(int khz, string expected)
        => UnderCulture("sv-SE", () => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportFrequency((long)khz)));

    // MSFS 2024 builds store the same columns in Hz; dividing by 1000 alone read "118605.000 MHz".
    [Theory]
    [InlineData(118605000, "118.605 MHz")]   // ESGG tower
    [InlineData(128725000, "128.725 MHz")]   // KJFK ATIS
    [InlineData(122950000, "122.950 MHz")]   // KDEN UNICOM
    public void Frequency_in_hertz_reads_as_megahertz_with_a_dot(int hz, string expected)
        => UnderCulture("sv-SE", () => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportFrequency((long)hz)));

    [Fact]
    public void Missing_or_zero_frequency_reads_not_available()
    {
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportFrequency(DBNull.Value));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportFrequency(null));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportFrequency(0L));
    }

    [Theory]
    [InlineData(57.65998458862305, 'N', 'S', "57.6600 N")]     // ESGG
    [InlineData(12.291107177734375, 'E', 'W', "12.2911 E")]    // ESGG
    [InlineData(-54.56819152832031, 'E', 'W', "54.5682 W")]    // CYQX
    [InlineData(-33.9648, 'N', 'S', "33.9648 S")]
    [InlineData(-0.00003, 'E', 'W', "0.0000 E")]               // rounds to zero: no west
    public void Coordinate_reads_four_decimals_and_its_hemisphere(double degrees, char pos, char neg, string expected)
        => UnderCulture("sv-SE", () => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportCoordinate(degrees, pos, neg)));

    [Theory]
    [InlineData(5.1530561447143555, "5.2° E")]    // ESGG
    [InlineData(-17.333860397338867, "17.3° W")]  // CYQX
    [InlineData(0.01, "0.0°")]
    [InlineData(-17.25, "17.3° W")]               // a half rounds away from zero, not to even
    public void Mag_var_reads_one_decimal_and_its_direction(double degrees, string expected)
        => UnderCulture("sv-SE", () => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportMagVar(degrees)));

    [Theory]
    [InlineData(206.0055694580078, "206° true")]
    [InlineData(190.7532196044922, "191° true")]
    [InlineData(90.5, "91° true")]                // a half rounds away from zero, not to even
    [InlineData(91.5, "92° true")]
    public void Heading_reads_whole_degrees_true(double degrees, string expected)
        => UnderCulture("sv-SE", () => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportHeading(degrees)));

    [Theory]
    [InlineData(510.045959472656, "510 ft")]      // ESGG altitude, a 32-bit float
    [InlineData(492.000030517578, "492 ft")]      // ESGG tower altitude
    [InlineData(10802.0, "10802 ft")]
    [InlineData(5000.0, "5000 ft")]
    public void Feet_read_whole_with_no_float_noise(double feet, string expected)
        => UnderCulture("sv-SE", () => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportFeet(feet)));

    [Theory]
    [InlineData(3.40000009536743, "3.4°")]        // ESGG 21 loc width
    [InlineData(206.004821777344, "206.0°")]      // ESGG 21 loc heading
    [InlineData(3.0, "3.0°")]
    public void Angles_read_one_decimal(double degrees, string expected)
        => UnderCulture("sv-SE", () => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportDegrees(degrees)));

    [Theory]
    [InlineData(18000.0, "FL180")]                // CYQX, KJFK
    [InlineData(11800.0, "FL118")]
    [InlineData(0.0, "N/A")]                      // ESGG: navdata has none
    public void Transition_level_in_feet_reads_as_a_flight_level(double feet, string expected)
        => UnderCulture("sv-SE", () => Assert.Equal(expected, ElectronicFlightBagForm.FormatTransitionLevel(feet)));

    [Fact]
    public void Empty_text_reads_not_available()
    {
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportText(DBNull.Value));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportText(null));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportText("  "));
        Assert.Equal("Goteborg", ElectronicFlightBagForm.FormatAirportText("Goteborg"));
    }

    [Fact]
    public void An_empty_code_or_place_line_is_left_out()
    {
        var sb = new System.Text.StringBuilder();
        ElectronicFlightBagForm.AppendAirportTextIfPresent(sb, "  Country:      ", DBNull.Value);
        ElectronicFlightBagForm.AppendAirportTextIfPresent(sb, "  IATA:         ", "");
        Assert.Equal("", sb.ToString());
        ElectronicFlightBagForm.AppendAirportTextIfPresent(sb, "  City:         ", "Goteborg");
        Assert.Equal("  City:         Goteborg" + Environment.NewLine, sb.ToString());
    }

    [Fact]
    public void A_column_whose_text_is_N_A_is_still_listed()
    {
        // Presence is decided on the raw value, not on FormatAirportText's placeholder.
        var sb = new System.Text.StringBuilder();
        ElectronicFlightBagForm.AppendAirportTextIfPresent(sb, "  Local:        ", "N/A");
        Assert.Equal("  Local:        N/A" + Environment.NewLine, sb.ToString());
    }

    [Fact]
    public void Missing_numbers_read_not_available()
    {
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportCoordinate(DBNull.Value, 'N', 'S'));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportMagVar(DBNull.Value));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportHeading(DBNull.Value));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportFeet(DBNull.Value));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportDegrees(DBNull.Value));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatTransitionLevel(DBNull.Value));
    }
}

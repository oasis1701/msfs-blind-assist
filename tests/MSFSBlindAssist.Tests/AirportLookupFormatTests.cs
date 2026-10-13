// The Airport Lookup's (Shift+E) airport-row formatters in ElectronicFlightBagForm.
// Live values from fs2020.sqlite (2026-10-13): ESGG tower_frequency 118605 and atis_frequency
// 118380 (kHz), laty 57.65998458862305, lonx 12.291107177734375 (32-bit floats), mag_var
// 5.1530561447143555, longest_runway_heading 206.0055694580078, country NULL. The lookup printed
// "Tower: 0,119 MHz", the raw doubles in the Windows number format, and a blank Country line.
// Every case runs under sv-SE (comma decimal): CI's en-US cannot catch a culture leak.

using System.Globalization;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class AirportLookupFormatTests
{
    private static void UnderSwedish(Action body)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(118605, "118.605 MHz")]   // ESGG tower
    [InlineData(118380, "118.380 MHz")]   // ESGG ATIS
    [InlineData(124800, "124.800 MHz")]   // CYQX ATIS
    [InlineData(122950, "122.950 MHz")]   // KJFK UNICOM
    public void Frequency_in_kilohertz_reads_as_megahertz_with_a_dot(int khz, string expected)
        => UnderSwedish(() => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportFrequency((long)khz)));

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
    public void Coordinate_reads_four_decimals_and_its_hemisphere(double degrees, char pos, char neg, string expected)
        => UnderSwedish(() => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportCoordinate(degrees, pos, neg)));

    [Theory]
    [InlineData(5.1530561447143555, "5.2° E")]    // ESGG
    [InlineData(-17.333860397338867, "17.3° W")]  // CYQX
    [InlineData(0.01, "0.0°")]
    public void Mag_var_reads_one_decimal_and_its_direction(double degrees, string expected)
        => UnderSwedish(() => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportMagVar(degrees)));

    [Theory]
    [InlineData(206.0055694580078, "206°")]
    [InlineData(190.7532196044922, "191°")]
    public void Heading_reads_whole_degrees(double degrees, string expected)
        => UnderSwedish(() => Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportHeading(degrees)));

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
    public void Missing_numbers_read_not_available()
    {
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportCoordinate(DBNull.Value, 'N', 'S'));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportMagVar(DBNull.Value));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportHeading(DBNull.Value));
    }
}

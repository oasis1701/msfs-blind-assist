// ElectronicFlightBagForm.FormatAirportFrequency: the Airport Lookup's FREQUENCIES block.
// Navdata's airport row stores kHz (ESGG tower_frequency 118605, atis_frequency 118380);
// the lookup used to divide by 1,000,000 and print "Tower: 0,119 MHz".

using System.Globalization;
using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class AirportLookupFrequencyTests
{
    [Theory]
    [InlineData(118605, "118.605 MHz")]   // ESGG tower
    [InlineData(118380, "118.380 MHz")]   // ESGG ATIS
    [InlineData(124800, "124.800 MHz")]   // CYQX ATIS
    [InlineData(122950, "122.950 MHz")]   // KJFK UNICOM
    public void Kilohertz_reads_as_megahertz_with_a_dot_under_a_comma_culture(int khz, string expected)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal(expected, ElectronicFlightBagForm.FormatAirportFrequency((long)khz));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Missing_or_zero_reads_not_available()
    {
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportFrequency(DBNull.Value));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportFrequency(null));
        Assert.Equal("N/A", ElectronicFlightBagForm.FormatAirportFrequency(0L));
    }
}

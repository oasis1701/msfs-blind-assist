// tests/MSFSBlindAssist.Tests/SayIntentionsParkingLogTests.cs
using System.Globalization;
using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsParkingLogTests
{
    [Fact]
    public void The_parking_service_answer_is_described_by_its_name_and_position()
        => Assert.Equal("name='B 12' lat=33.6407 lon=-84.4277 heading=180",
            SayIntentionsService.DescribeParking(new SayIntentionsParking { Name = "B 12", Latitude = 33.6407, Longitude = -84.4277, Heading = 180 }));

    [Fact]
    public void A_missing_number_reads_as_a_dash()
        => Assert.Equal("name='B 12' lat=- lon=- heading=-",
            SayIntentionsService.DescribeParking(new SayIntentionsParking { Name = "B 12" }));

    [Fact]
    public void Numbers_are_invariant_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("name='B 12' lat=33.6407 lon=-84.4277 heading=180.5",
                SayIntentionsService.DescribeParking(new SayIntentionsParking { Name = "B 12", Latitude = 33.6407, Longitude = -84.4277, Heading = 180.5 }));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

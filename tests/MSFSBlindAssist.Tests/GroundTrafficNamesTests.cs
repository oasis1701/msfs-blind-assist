// Spoken aircraft names. PR #247 review L3 (the Alt+G summary lost the callsign whenever the airline
// was known; a name was frozen before the VATSIM type arrived) and L8 (two callsign formatters).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class GroundTrafficNamesTests
{
    [Theory]
    [InlineData("DAL123", "DAL 123")]
    [InlineData("EZY45MR", "EZY 45MR")]     // VATSIM two-letter suffix
    [InlineData("UAL12345", "UAL 12345")]   // five-digit flight number
    [InlineData("dal123", "DAL 123")]
    [InlineData("  BAW1  ", "BAW 1")]
    [InlineData("N12345", "N12345")]        // registration
    [InlineData("G-ABCD", "G-ABCD")]        // hyphen: already speakable
    [InlineData("UAL 123", "UAL 123")]      // already spaced
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void SpokenCallsign(string raw, string expected)
        => Assert.Equal(expected, GroundTrafficLogic.SpokenCallsign(raw));

    [Fact]
    public void SpokenCallsign_of_null_is_empty()
        => Assert.Equal("", GroundTrafficLogic.SpokenCallsign(null));

    [Theory]
    [InlineData("Delta", "DAL1234", "A20N", "Delta A320, DAL 1234")]
    [InlineData("Delta", "", "A20N", "Delta A320")]
    [InlineData("", "DAL123", "B738", "DAL 123, 737")]   // the callsign is already the name
    [InlineData("", "", "B77W", "777")]
    [InlineData("", "", "", "traffic")]
    public void SpokenNameWithCallsign(string airline, string callsign, string type, string expected)
        => Assert.Equal(expected, GroundTrafficLogic.SpokenNameWithCallsign(airline, callsign, type));

    [Theory]
    [InlineData(false, "A20N", true)]    // typeless name, type now known → rebuild
    [InlineData(false, "", false)]       // still unknown
    [InlineData(false, null, false)]
    [InlineData(true, "B738", false)]    // already has a type
    public void NameNeedsRefresh(bool nameHasType, string? resolvedType, bool expected)
        => Assert.Equal(expected, GroundTrafficLogic.NameNeedsRefresh(nameHasType, resolvedType));
}

using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class DA40AirframeTests
{
    [Theory]
    // The AircraftLoaded path exactly as the sim sent it (debug log, 2026-09-11).
    [InlineData(@"c:\users\franc\appdata\local\packages\microsoft.limitless_8wekyb3d8bbwe\localcache\packages\community\cows-da40\simobjects\airplanes\cows_da40ng\aircraft.cfg", DA40Airframe.NgCode)]
    [InlineData(@"C:\X\SimObjects\Airplanes\COWS_DA40XLS\aircraft.cfg", DA40Airframe.XlsCode)]
    // The connect title, with and without the ATC identification after it.
    [InlineData(" DA40-NG White", DA40Airframe.NgCode)]
    [InlineData("DA40-XLS Ribbon 2 - N40XL", DA40Airframe.XlsCode)]
    [InlineData(@"c:\community\fnx-aircraft-320\simobjects\airplanes\fnx_32x\aircraft.cfg", null)]
    [InlineData("Airbus A320neo", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void EachAirframeIsNamedByItsFolderOrTitle(string? input, string? expected)
        => Assert.Equal(expected, DA40Airframe.CodeFor(input));

    [Fact]
    public void TheXlsProfileOnALoadedNgSwapsToTheNg()
        => Assert.Equal(DA40Airframe.NgCode, DA40Airframe.SwapTarget(DA40Airframe.XlsCode, "DA40-NG White"));

    [Fact]
    public void TheNgProfileOnALoadedXlsSwapsToTheXls()
        => Assert.Equal(DA40Airframe.XlsCode,
            DA40Airframe.SwapTarget(DA40Airframe.NgCode, @"c:\p\simobjects\airplanes\cows_da40xls\aircraft.cfg"));

    [Fact]
    public void TheRightProfileStays()
        => Assert.Null(DA40Airframe.SwapTarget(DA40Airframe.NgCode, "DA40-NG Flow"));

    [Theory]
    // Never into the DA40 from another profile, and never out of it to anything else.
    [InlineData("A320", "DA40-NG White")]
    [InlineData(DA40Airframe.NgCode, "Airbus A320neo")]
    public void OnlyTheTwoDA40ProfilesEverSwap(string current, string title)
        => Assert.Null(DA40Airframe.SwapTarget(current, title));
}

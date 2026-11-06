using MSFSBlindAssist.Aircraft;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The per-aircraft Waypoint Flight Director profiles kept by aircraft code
/// (<see cref="WaypointFlightDirectorProfile.ForAircraftCode"/>) for aircraft whose definitions
/// arrive in other open pull requests (WFD-3).
/// </summary>
public class WaypointFlightDirectorProfileTests
{
    [Theory]
    [InlineData("COWS_DA40NG")]
    [InlineData("COWS_DA40XLS")]
    public void TheDa40GetsItsOwnProfileOnBothAirframes(string code)
    {
        var p = WaypointFlightDirectorProfile.ForAircraftCode(code);
        Assert.Equal(WaypointFlightDirectorProfile.CowsDA40.MaxBankDeg, p.MaxBankDeg);
        Assert.Equal(WaypointFlightDirectorProfile.CowsDA40.CaptureRadiusNm, p.CaptureRadiusNm);
        Assert.NotEqual(new WaypointFlightDirectorProfile().CaptureRadiusNm, p.CaptureRadiusNm);
    }

    [Theory]
    [InlineData("FBW_A320")]
    [InlineData("")]
    [InlineData(null)]
    public void AnyOtherCodeGetsTheBaseline(string? code)
    {
        var p = WaypointFlightDirectorProfile.ForAircraftCode(code);
        var baseline = new WaypointFlightDirectorProfile();
        Assert.Equal(baseline.MaxBankDeg, p.MaxBankDeg);
        Assert.Equal(baseline.KRollDegPerDegTrack, p.KRollDegPerDegTrack);
        Assert.Equal(baseline.CaptureRadiusNm, p.CaptureRadiusNm);
    }

    [Fact]
    public void TheDa40ToneRangeEqualsItsPitchLimit()
    {
        // WFD-2: the FD clamps pitch at MaxPitchDeg, so a tone that saturated earlier could not
        // show commands the FD itself gives.
        var p = WaypointFlightDirectorProfile.CowsDA40;
        Assert.Equal(p.MaxPitchDeg, p.TonePitchRangeDeg);
    }

    private sealed class Da40CodedDefinition : YourAircraftDefinition
    {
        public override string AircraftCode => "COWS_DA40NG";
    }

    [Fact]
    public void ADefinitionWithoutItsOwnOverrideIsRoutedByItsCode()
    {
        // The base class's default is what delivers the table's entry to the DA40, whose
        // definition (PR #242) does not override the method.
        Assert.Equal(WaypointFlightDirectorProfile.CowsDA40.CaptureRadiusNm,
            new Da40CodedDefinition().GetWaypointFlightDirectorProfile().CaptureRadiusNm);
        Assert.Equal(new WaypointFlightDirectorProfile().CaptureRadiusNm,
            new YourAircraftDefinition().GetWaypointFlightDirectorProfile().CaptureRadiusNm);
    }
}

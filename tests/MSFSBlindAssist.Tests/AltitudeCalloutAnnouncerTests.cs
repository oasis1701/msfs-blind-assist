using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Characterization tests for the 1,000-ft altitude-callout state machine (#130 fix:
/// the callout fires AT the boundary — the band change IS the crossing — and the 80-ft
/// hysteresis is ONLY a same-boundary hover debounce, never a delay on the first
/// crossing; the announced value is the thousand CROSSED, same number climbing or
/// descending). Uses the announce-sink constructor; the settings gate reads the
/// process-global SettingsManager, so this class joins the shared no-parallelism
/// collection and forces the flag around each test.
/// </summary>
[Collection("SettingsManagerGlobalState")]
public class AltitudeCalloutAnnouncerTests : IDisposable
{
    private readonly List<string> spoken = new();
    private readonly AltitudeCalloutAnnouncer sut;
    private readonly bool savedEnabled;

    public AltitudeCalloutAnnouncerTests()
    {
        savedEnabled = SettingsManager.Current.AltitudeCalloutsEnabled;
        SettingsManager.Current.AltitudeCalloutsEnabled = true;
        sut = new AltitudeCalloutAnnouncer(spoken.Add);
    }

    public void Dispose() => SettingsManager.Current.AltitudeCalloutsEnabled = savedEnabled;

    private void Feed(params double[] altitudes)
    {
        foreach (var alt in altitudes) sut.ProcessAltitude(alt, onGround: false);
    }

    [Fact]
    public void FirstAirborneSample_EstablishesBaselineSilently()
    {
        Feed(35_480);
        Assert.Empty(spoken);
    }

    [Fact]
    public void ClimbCrossing_AnnouncesTheThousandCrossed_AtTheBoundary()
    {
        // #130: "36,000" must speak the moment the band changes (36,010), not ~80 ft late.
        Feed(35_900, 36_010);
        Assert.Equal(new[] { "36000" }, spoken);
    }

    [Fact]
    public void DescentCrossing_AnnouncesTheThousandCrossed_NotTheBandEntered()
    {
        // Descending through 36,000 says "36000" (the thousand crossed), not "35000".
        Feed(36_150, 35_990);
        Assert.Equal(new[] { "36000" }, spoken);
    }

    [Fact]
    public void HoverOnAnnouncedThousand_DoesNotRepeat()
    {
        // Level-off ON the round thousand: band flutter across the line stays silent.
        Feed(35_900, 36_010, 35_995, 36_005, 35_990, 36_010);
        Assert.Equal(new[] { "36000" }, spoken);
    }

    [Fact]
    public void LeavingTheHover_NextThousandAnnouncesImmediately()
    {
        Feed(35_900, 36_010, 35_995, 36_010, 37_001);
        Assert.Equal(new[] { "36000", "37000" }, spoken);
    }

    [Fact]
    public void RecrossingAnnouncedThousand_OutsideHysteresis_Announces()
    {
        // Climb through 36,000, keep climbing clear of the 80-ft window, then descend
        // back through it from well above: a real re-crossing, not hover flutter.
        Feed(35_900, 36_010, 36_500, 35_900);
        Assert.Equal(new[] { "36000", "36000" }, spoken);
    }

    [Fact]
    public void OnGroundSamples_ResetBaseline_AndStaySilent()
    {
        sut.ProcessAltitude(1_200, onGround: true);
        Assert.Empty(spoken);
        // First airborne sample after ground is a silent re-baseline, not a callout.
        Feed(1_600);
        Assert.Empty(spoken);
        Feed(2_050);
        Assert.Equal(new[] { "2000" }, spoken);
    }

    [Fact]
    public void ResetBaseline_MakesNextSampleSilent()
    {
        Feed(4_500, 5_010);
        Assert.Equal(new[] { "5000" }, spoken);
        sut.ResetBaseline();
        Feed(9_800);            // silent baseline after teleport
        Assert.Equal(new[] { "5000" }, spoken);
        Feed(10_020);
        Assert.Equal(new[] { "5000", "10000" }, spoken);
    }

    [Fact]
    public void MultiThousandJump_AnnouncesTheLastThousandCrossed()
    {
        // Characterizes current behavior: a several-band jump produces ONE callout for
        // the boundary adjacent to the new band (climb: the new band's floor).
        Feed(4_500, 7_200);
        Assert.Equal(new[] { "7000" }, spoken);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(80_000)]     // above the 70,000 sanity ceiling
    [InlineData(-3_000)]     // below the -2,000 sanity floor
    public void InvalidSamples_AreIgnored(double bogus)
    {
        Feed(4_500);
        Feed(bogus);
        Feed(5_010);
        Assert.Equal(new[] { "5000" }, spoken);
    }

    [Fact]
    public void SettingOff_StaysSilent()
    {
        SettingsManager.Current.AltitudeCalloutsEnabled = false;
        Feed(4_500, 5_010);
        Assert.Empty(spoken);
    }
}

// Characterization tests for Pmdg737FlapSpeeds (Aircraft/Pmdg737FlapSpeeds.cs):
// the calculated PMDG 737 NG3 flap maneuvering speeds behind the output-mode
// Shift+1..6 hotkeys. Pins the measured VREF40 tables (each point read off that
// variant's own FMC APPROACH REF page), the interpolation between and the
// square-root extension beyond them, the variant pick from the CDA AircraftModel
// with the TITLE as fallback, the FCTM additive schedule (UP +70 / 1 +50 / 5 +30 /
// 10 +30 / 15 +20 / 25 +10), and the spoken readout.

namespace MSFSBlindAssist.Tests;

using System.Globalization;
using MSFSBlindAssist.Aircraft;

public class Pmdg737FlapSpeedsTests
{
    private const int Variant700 = 7;
    private const int Variant800 = 8;

    [Theory]
    [InlineData(100000, 114)]
    [InlineData(115000, 123)]
    [InlineData(127000, 130)]
    [InlineData(130000, 132)]
    [InlineData(145000, 140)]
    public void Vref40_On700_MatchesTheFmcAtEveryMeasuredWeight(double lbs, int fmcVref40)
    {
        Assert.Equal(fmcVref40, Pmdg737FlapSpeeds.Vref40Knots(lbs, Variant700));
    }

    [Theory]
    [InlineData(100000, 115)]
    [InlineData(115000, 124)]
    [InlineData(130000, 132)]
    [InlineData(145000, 140)]
    [InlineData(160000, 146)]
    public void Vref40_On800_MatchesTheFmcAtEveryMeasuredWeight(double lbs, int fmcVref40)
    {
        Assert.Equal(fmcVref40, Pmdg737FlapSpeeds.Vref40Knots(lbs, Variant800));
    }

    [Theory]
    [InlineData(9)]     // -900: borrows the -800's table
    [InlineData(null)]  // variant not known: the -800's, the one most flown
    public void The900AndUnknownVariants_UseThe800Table(int? variant)
    {
        Assert.Equal(115, Pmdg737FlapSpeeds.Vref40Knots(100000, variant));
    }

    [Fact]
    public void Vref40_BetweenMeasuredWeights_Interpolates()
    {
        // 107.5 klb: halfway from 114 to 123 = 118.5 -> 119 (away from zero).
        Assert.Equal(119, Pmdg737FlapSpeeds.Vref40Knots(107500, Variant700));
    }

    [Theory]
    [InlineData(90000, 108)]   // 114 * sqrt(90/100)  = 108.15
    [InlineData(155000, 145)]  // 140 * sqrt(155/145) = 144.75
    public void Vref40_BeyondTheTable_ExtendsBySquareRootFromTheEndPoint(double lbs, int expected)
    {
        Assert.Equal(expected, Pmdg737FlapSpeeds.Vref40Knots(lbs, Variant700));
    }

    [Theory]
    [InlineData(1, 6)]                                  // -600
    [InlineData(2, 7)] [InlineData(3, 7)] [InlineData(4, 7)]   // -700, BW, SSW
    [InlineData(13, 7)] [InlineData(14, 7)]             // -700 BDSF
    [InlineData(19, 7)] [InlineData(20, 7)]             // -700 BBJ
    [InlineData(5, 8)] [InlineData(6, 8)] [InlineData(7, 8)]   // -800, BW, SSW
    [InlineData(15, 8)] [InlineData(16, 8)]             // -800 BDSF
    [InlineData(17, 8)] [InlineData(18, 8)]             // -800 BCF
    [InlineData(21, 8)] [InlineData(22, 8)]             // -800 BBJ
    [InlineData(8, 9)] [InlineData(10, 9)] [InlineData(12, 9)] // -900, -900ER
    public void Variant_IsReadFromTheAircraftModel(int aircraftModel, int variant)
    {
        Assert.Equal(variant, Pmdg737FlapSpeeds.VariantFromModel(aircraftModel));
    }

    [Theory]
    [InlineData(0)]   // no CDA snapshot yet
    [InlineData(23)]  // a value the SDK header lists none for
    public void Variant_UnknownAircraftModel_IsNull(int aircraftModel)
    {
        Assert.Null(Pmdg737FlapSpeeds.VariantFromModel(aircraftModel));
    }

    [Fact]
    public void Variant_TheAircraftModelWinsOverTheTitle()
    {
        // A -700 (model 3) flying a livery whose title says nothing, or something else.
        Assert.Equal(7, Pmdg737FlapSpeeds.Variant(3, "Southwest Airlines N7xxx"));
        Assert.Equal(7, Pmdg737FlapSpeeds.Variant(3, "737-800 PAX BW SC"));
    }

    [Fact]
    public void Variant_FallsBackToTheTitle_OnlyWithoutAKnownModel()
    {
        Assert.Equal(7, Pmdg737FlapSpeeds.Variant(0, "737-700 PAX BW SC"));
        Assert.Null(Pmdg737FlapSpeeds.Variant(0, "Southwest Airlines N7xxx"));
    }

    [Theory]
    [InlineData("737-600 PAX SC", 6)]
    [InlineData("737-700 PAX BW SC", 7)]
    [InlineData("737-700BDSF BW", 7)]
    [InlineData("737-800BCF SSW", 8)]
    [InlineData("737-800 BBJ2 SSW", 8)]
    public void Variant_IsReadFromTheTitle(string title, int variant)
    {
        Assert.Equal(variant, Pmdg737FlapSpeeds.VariantFromTitle(title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Boeing 737 MAX 8")]
    public void Variant_UnrecognisedTitle_IsNull(string? title)
    {
        Assert.Null(Pmdg737FlapSpeeds.VariantFromTitle(title));
    }

    [Fact]
    public void The600_BorrowsThe700Table()
    {
        Assert.Equal(Pmdg737FlapSpeeds.Vref40Knots(120000, Variant700),
            Pmdg737FlapSpeeds.Vref40Knots(120000, 6));
    }

    [Fact]
    public void ManeuverSpeeds_FollowTheFctmAdditiveSchedule()
    {
        // 127 klb on the -700: VREF40 = 130, so UP 200 / 1 180 / 5 160 /
        // 10 160 / 15 150 / 25 140 (the PFD read 202 / - / 162 in flight).
        int[] expected = { 200, 180, 160, 160, 150, 140 };
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], Pmdg737FlapSpeeds.ManeuverSpeedKnots(127000, Variant700, i));
    }

    [Fact]
    public void FlapNames_AreSpokenForms()
    {
        Assert.Equal("Flaps up", Pmdg737FlapSpeeds.FlapName(0));
        Assert.Equal("Flaps 1", Pmdg737FlapSpeeds.FlapName(1));
        Assert.Equal("Flaps 25", Pmdg737FlapSpeeds.FlapName(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50000)]   // below MinWeightLbs — sim not ready / cold cache
    [InlineData(250000)]  // above MaxWeightLbs — garbage read
    public void OutOfRangeWeight_IsNotPlausible(double lbs)
    {
        Assert.False(Pmdg737FlapSpeeds.IsPlausibleWeight(lbs));
    }

    [Fact]
    public void Describe_SpeaksTheCalculatedSpeed()
    {
        Assert.Equal("Flaps up maneuver speed 200 knots, calculated",
            Pmdg737FlapSpeeds.Describe(127000, Variant700, 0));
        Assert.Equal("Flaps 5 maneuver speed 160 knots, calculated",
            Pmdg737FlapSpeeds.Describe(127000, Variant700, 2));
    }

    [Theory]
    [InlineData(null)]      // nothing delivered (timeout, disconnect, aircraft switch)
    [InlineData(50000.0)]   // delivered but not believable
    public void Describe_WithoutABelievableWeight_SaysUnavailable(double? lbs)
    {
        Assert.Equal("Flap speeds unavailable.", Pmdg737FlapSpeeds.Describe(lbs, Variant700, 0));
    }

    [Fact]
    public void Speeds_DoNotDependOnTheCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(119, Pmdg737FlapSpeeds.Vref40Knots(107500, Variant700));
            Assert.Equal(7, Pmdg737FlapSpeeds.VariantFromTitle("737-700 PAX BW SC"));
            // 107.5 klb on the -700: VREF40 119 + 30 = 149, spoken with no separator.
            Assert.Equal("Flaps 5 maneuver speed 149 knots, calculated",
                Pmdg737FlapSpeeds.Describe(107500, Variant700, 2));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
}

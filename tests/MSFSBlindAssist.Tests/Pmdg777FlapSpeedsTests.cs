// Characterization tests for Pmdg777FlapSpeeds (Aircraft/Pmdg777FlapSpeeds.cs):
// the calculated PMDG 777 flap maneuvering speeds behind the output-mode
// Shift+1..5 hotkeys. Pins the measured VREF30 tables (each point read off that
// variant's own FMC APPROACH REF page, 2026-10-12), the interpolation between and
// the end-value hold beyond them, the variant pick from the CDA AircraftModel, and
// the FCTM additive schedule (UP +80 / 1 +60 / 5 +40 / 15 +20 / 20 +20).

namespace MSFSBlindAssist.Tests;

using System.Globalization;
using MSFSBlindAssist.Aircraft;

public class Pmdg777FlapSpeedsTests
{
    // CDA AircraftModel values: 2 -200ER, 4 -200LR, 5 777F, 6 -300ER.
    private const int Model200Er = 2;
    private const int Model200Lr = 4;
    private const int ModelFreighter = 5;
    private const int Model300Er = 6;

    [Theory]
    [InlineData(350000, 134)]
    [InlineData(400000, 134)]
    [InlineData(450000, 135)]
    [InlineData(500000, 139)]
    [InlineData(575000, 149)]
    [InlineData(650000, 162)]
    [InlineData(710000, 174)]
    [InlineData(766000, 181)]
    public void Vref30_OnTheFreighter_MatchesTheFmcAtEveryMeasuredWeight(double lbs, int fmcVref30)
    {
        Assert.Equal(fmcVref30, Pmdg777FlapSpeeds.Vref30Knots(lbs, ModelFreighter));
    }

    [Theory]
    [InlineData(350000, 134)]
    [InlineData(400000, 134)]
    [InlineData(575000, 149)]
    [InlineData(650000, 162)]
    [InlineData(710000, 174)]
    [InlineData(766000, 181)]
    public void Vref30_On200Lr_MatchesTheFmcAndSharesTheFreighterTable(double lbs, int fmcVref30)
    {
        Assert.Equal(fmcVref30, Pmdg777FlapSpeeds.Vref30Knots(lbs, Model200Lr));
    }

    [Theory]
    [InlineData(400000, 127)]
    [InlineData(450000, 135)]
    [InlineData(500000, 142)]
    [InlineData(554000, 150)]
    [InlineData(625000, 159)]
    [InlineData(700000, 173)]
    [InlineData(775000, 183)]
    public void Vref30_On300Er_MatchesTheFmcAtEveryMeasuredWeight(double lbs, int fmcVref30)
    {
        Assert.Equal(fmcVref30, Pmdg777FlapSpeeds.Vref30Knots(lbs, Model300Er));
    }

    [Theory]
    [InlineData(325000, 127)]
    [InlineData(375000, 128)]
    [InlineData(425000, 132)]
    [InlineData(470000, 140)]
    [InlineData(525000, 147)]
    [InlineData(600000, 157)]
    [InlineData(630000, 160)]
    [InlineData(656000, 163)]
    public void Vref30_On200Er_MatchesTheFmcAtEveryMeasuredWeight(double lbs, int fmcVref30)
    {
        Assert.Equal(fmcVref30, Pmdg777FlapSpeeds.Vref30Knots(lbs, Model200Er));
    }

    [Fact]
    public void Vref30_BetweenMeasuredWeights_Interpolates()
    {
        // 680 klb on the freighter: halfway from 162 to 174 = 168.
        Assert.Equal(168, Pmdg777FlapSpeeds.Vref30Knots(680000, ModelFreighter));
    }

    [Theory]
    [InlineData(300000, ModelFreighter, 134)]  // below the table: the measured floor holds
    [InlineData(300000, Model200Er, 127)]
    [InlineData(790000, Model300Er, 183)]      // above MTOW: the end value holds
    public void Vref30_BeyondTheTable_HoldsTheEndValue(double lbs, int model, int expected)
    {
        Assert.Equal(expected, Pmdg777FlapSpeeds.Vref30Knots(lbs, model));
    }

    [Theory]
    [InlineData(1, 163)]  // -200 borrows the -200ER's table
    [InlineData(3, 183)]  // -300 borrows the -300ER's table
    public void UnshippedModels_BorrowTheirErSibling(int model, int vref30At656Or775)
    {
        double lbs = model == 1 ? 656000 : 775000;
        Assert.Equal(vref30At656Or775, Pmdg777FlapSpeeds.Vref30Knots(lbs, model));
    }

    [Theory]
    [InlineData(0)]  // CDA not ready yet
    [InlineData(7)]
    public void UnknownModel_HasNoSpeed(int model)
    {
        Assert.False(Pmdg777FlapSpeeds.IsKnownModel(model));
        Assert.Null(Pmdg777FlapSpeeds.Vref30Knots(500000, model));
        Assert.Null(Pmdg777FlapSpeeds.ManeuverSpeedKnots(500000, model, 0));
    }

    [Fact]
    public void ManeuverSpeeds_FollowTheFctmAdditiveSchedule()
    {
        // 650 klb on the freighter: VREF30 = 162, so UP 242 / 1 222 / 5 202 / 15 182 / 20 182.
        int[] expected = { 242, 222, 202, 182, 182 };
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], Pmdg777FlapSpeeds.ManeuverSpeedKnots(650000, ModelFreighter, i));
    }

    [Fact]
    public void FlapNames_AreSpokenForms()
    {
        Assert.Equal("Flaps up", Pmdg777FlapSpeeds.FlapName(0));
        Assert.Equal("Flaps 1", Pmdg777FlapSpeeds.FlapName(1));
        Assert.Equal("Flaps 20", Pmdg777FlapSpeeds.FlapName(4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(150000)]  // below MinWeightLbs — sim not ready / cold cache
    [InlineData(900000)]  // above MaxWeightLbs — garbage read
    public void OutOfRangeWeight_IsNotPlausible(double lbs)
    {
        Assert.False(Pmdg777FlapSpeeds.IsPlausibleWeight(lbs));
    }

    [Fact]
    public void Speeds_DoNotDependOnTheCulture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(168, Pmdg777FlapSpeeds.Vref30Knots(680000, ModelFreighter));
            Assert.Equal("Flaps 15", Pmdg777FlapSpeeds.FlapName(3));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
}

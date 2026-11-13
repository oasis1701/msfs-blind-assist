using MSFSBlindAssist.Aircraft.DA40;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The XLS start-readiness row: the first thing stopping a start, in the order a pilot can
/// act on them. Each blocker is a condition measured on the live aircraft
/// (docs/da40-xls-variables.md): the un-generated variations that make it structurally
/// unstartable with nothing in the cockpit saying so, the selector, the tank, the jet's
/// 0.8 bar pressure gate, vapour, and the priming state.
/// </summary>
public class DA40StartReadinessTests
{
    // Everything in order: master on, spreads generated, left tank with fuel, pump on,
    // pressure over the jet's gate, no vapour, primed.
    private static DA40StartInputs Ready() => new(
        EngineRunning: false, MasterOn: true, SpreadPressure: 1.51194, SelectorPosition: 0,
        FeedQuantityGal: 19.43, PumpOn: true, FuelPressureBar: 1.67, BoilFactor: 1.0,
        Prime: PrimeState.Primed);

    [Fact]
    public void EverythingInOrderIsReadyToCrank()
        => Assert.Equal("Ready to crank", DA40StartReadiness.Describe(Ready()));

    [Fact]
    public void ARunningEngineIsNotAStartQuestion()
        => Assert.Equal("Engine running", DA40StartReadiness.Describe(Ready() with { EngineRunning = true }));

    [Fact]
    public void MasterOffComesFirst_BecauseUnpoweredTheFuelLogicIsFrozen()
    {
        // Measured unpowered: ENG_FUEL_SYSTEM_SERVO_GRAM 21.6 against a cap of 0.51 and
        // ENG_FUEL_PRESS 19.75 - stale numbers no later blocker may be judged from.
        var i = Ready() with { MasterOn = false, FuelPressureBar = 19.75, Prime = PrimeState.Flooded };
        Assert.Equal("Master off", DA40StartReadiness.Describe(i));
    }

    [Fact]
    public void UngeneratedVariationsNameTheFix_BecauseNothingInTheCockpitDoes()
    {
        var i = Ready() with { SpreadPressure = 0, FuelPressureBar = 0, FeedQuantityGal = 0 };
        Assert.Equal("Engine variations not generated - Clear Engine Damage on the Reset panel",
            DA40StartReadiness.Describe(i));
    }

    [Fact]
    public void SelectorOffBeforeAnEmptyTank()
        => Assert.Equal("Fuel selector off",
            DA40StartReadiness.Describe(Ready() with { SelectorPosition = 2, FeedQuantityGal = 0 }));

    [Fact]
    public void SelectedTankEmpty()
        => Assert.Equal("Selected tank empty",
            DA40StartReadiness.Describe(Ready() with { FeedQuantityGal = 0, FuelPressureBar = 0 }));

    [Fact]
    public void VapourBeforePressure_BecauseItIsWhyThePressureIsLow()
        => Assert.Equal("Vapour in the fuel lines",
            DA40StartReadiness.Describe(Ready() with { BoilFactor = 0.6, FuelPressureBar = 0.5 }));

    [Theory]
    [InlineData(true, "No fuel pressure")]
    [InlineData(false, "No fuel pressure - electric pump off")]
    public void PressureUnderTheJetsGateSaysWhetherThePumpIsOn(bool pumpOn, string expected)
        => Assert.Equal(expected,
            DA40StartReadiness.Describe(Ready() with { PumpOn = pumpOn, FuelPressureBar = 0.79 }));

    [Theory]
    [InlineData(PrimeState.NotPrimed, "Not primed")]
    [InlineData(PrimeState.Flooded, "Flooded")]
    public void ThePrimingStateIsTheLastWord(PrimeState prime, string expected)
        => Assert.Equal(expected, DA40StartReadiness.Describe(Ready() with { Prime = prime }));

    [Theory]
    [InlineData(false, "Not computed, master off")]
    [InlineData(true, null)]
    [InlineData(null, null)]
    public void AFuelReadingIsOnlyFrozenWhenTheMasterIsKnownOff(bool? masterOn, string? expected)
        // Unknown (nothing read yet) must NOT claim the master is off - the row would say
        // so for the first second of every session.
        => Assert.Equal(expected, DA40StartReadiness.FrozenReason(masterOn));

    [Fact]
    public void TheFuelPressureRowRendersNormallyBeforeTheMasterHasBeenRead()
    {
        // The reading is the INDICATION's own psi now (DISP_FP), not the model's bar - so
        // this passes 27.5, what the gauge drew at VCBI, rather than 1.616.
        var def = new CowsDA40Definition(DA40Variant.XLS);
        Assert.True(def.TryGetDisplayOverride("DA40_XLS_FUEL_PRESSURE", 27.5, out string text));
        Assert.Equal("28 psi, green", text);
    }

    [Fact]
    public void PrimingNotYetClassifiableStillReportsThePressure()
        => Assert.Equal("Fuel pressure up", DA40StartReadiness.Describe(Ready() with { Prime = null }));
}

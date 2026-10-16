using MSFSBlindAssist.Aircraft.Learjet35;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The pure Learjet helpers: preselect, pressurization ladder, fuel, annunciator lamps.</summary>
public class Lj35LogicTests
{
    [Theory]
    [InlineData(15050, 15100)]
    [InlineData(-10, 0)]
    [InlineData(120000, 99900)]
    [InlineData(0, 0)]
    public void PreselectClampsToWholeHundreds(double typed, int expected) => Assert.Equal(expected, Lj35Preselect.Clamp(typed));

    [Fact]
    public void PreselectValidatesText()
    {
        Assert.False(Lj35Preselect.Validate("abc").ok);
        Assert.True(Lj35Preselect.Validate("15000").ok);
        Assert.False(Lj35Preselect.Validate("120000").ok);
    }

    [Theory]
    [InlineData(8749, "Normal")]
    [InlineData(8750, "Above 8750: automatic to manual, CAB ALT lamp")]
    [InlineData(10100, "Above 10100: warning horn, emergency descent")]
    [InlineData(11000, "Above 11000: cabin altitude limiter active")]
    [InlineData(14000, "Above 14000: passenger masks would deploy (not simulated)")]
    public void PressurizationLadder(double cabinAlt, string expected) => Assert.Equal(expected, Lj35Pressurization.Describe(cabinAlt));

    [Theory]
    [InlineData(100, 0)]
    [InlineData(10000, 99)]
    [InlineData(-1000, -10)]
    [InlineData(-1100, -10)]
    public void CabinAltitudeKnob(double feet, int position) => Assert.Equal(position, Lj35Pressurization.AltitudeKnobPosition(feet));

    [Theory]
    [InlineData(175, 8)]
    [InlineData(2500, 99)]
    public void CabinRateKnob(double fpm, int position) => Assert.Equal(position, Lj35Pressurization.RateKnobPosition(fpm));

    [Fact]
    public void FuelDescribeGivesBothUnits() => Assert.Contains("100 gallons", Lj35Fuel.Describe(670));

    [Fact]
    public void FuelTransferAdvice()
    {
        Assert.Contains("transfer", Lj35Fuel.TransferAdvice(759, 800, 500));
        Assert.Equal("Fuselage empty", Lj35Fuel.TransferAdvice(0, 0, 0));
        Assert.Equal("No transfer needed", Lj35Fuel.TransferAdvice(900, 900, 500));
    }

    private static Func<string, double> Read(params (string key, double value)[] values)
    {
        var d = values.ToDictionary(v => v.key, v => v.value, StringComparer.Ordinal);
        return k => d.TryGetValue(k, out var v) ? v : 0;
    }

    [Fact]
    public void LowFuelLampAtSixtyGallons()
    {
        var lamp = Lj35AnnunciatorLogic.Find("LJ35_ANN_LOW_FUEL")!;
        Assert.True(lamp.Lit(Read(("LJ35_FUEL_WING_L_GAL", 59), ("LJ35_FUEL_WING_R_GAL", 200))));
        Assert.False(lamp.Lit(Read(("LJ35_FUEL_WING_L_GAL", 61), ("LJ35_FUEL_WING_R_GAL", 61))));
    }

    [Fact]
    public void GeneratorLampNeedsOnLineAndVolts()
    {
        var lamp = Lj35AnnunciatorLogic.Find("LJ35_ANN_GEN_L")!;
        Assert.True(lamp.Lit(Read(("LJ35_GEN_L_ON", 1), ("LJ35_GEN_L_V", 9))));
        Assert.True(lamp.Lit(Read(("LJ35_GEN_L_ON", 0), ("LJ35_GEN_L_V", 28))));
        Assert.False(lamp.Lit(Read(("LJ35_GEN_L_ON", 1), ("LJ35_GEN_L_V", 28))));
    }

    [Fact]
    public void CabinAltitudeLampNeedsBusPower()
    {
        var lamp = Lj35AnnunciatorLogic.Find("LJ35_ANN_CAB_ALT")!;
        Assert.True(lamp.Lit(Read(("LJ35_CABIN_ALT", 8751), ("LJ35_BUS_MAIN_V", 28))));
        Assert.False(lamp.Lit(Read(("LJ35_CABIN_ALT", 8751), ("LJ35_BUS_MAIN_V", 0))));
    }

    [Fact]
    public void TakeoffTrimLampOnlyOnTheGround()
    {
        var lamp = Lj35AnnunciatorLogic.Find("LJ35_ANN_TRIM_TO")!;
        Assert.True(lamp.Lit(Read(("LJ35_TRIM_DEG", 4), ("LJ35_ON_GROUND", 1))));
        Assert.False(lamp.Lit(Read(("LJ35_TRIM_DEG", 4), ("LJ35_ON_GROUND", 0))));
        Assert.False(lamp.Lit(Read(("LJ35_TRIM_DEG", 6), ("LJ35_ON_GROUND", 1))));
    }

    [Fact]
    public void EveryLampInputIsCachedByTheDefinition()
    {
        var vars = new FlysimwareLearjet35ADefinition().GetVariables();
        var missing = Lj35AnnunciatorLogic.AllInputs
            .Where(k => !vars.TryGetValue(k, out var d) || d.UpdateFrequency != MSFSBlindAssist.SimConnect.UpdateFrequency.Continuous)
            .ToList();
        Assert.True(missing.Count == 0, "Lamp inputs not in the batch cache: " + string.Join(", ", missing));
    }

    [Fact]
    public void MasterCautionLatchesAndResets()
    {
        var mc = new Lj35MasterCaution();
        var read = Read(("LJ35_OIL_P_L", 10), ("LJ35_OIL_P_R", 40), ("LJ35_FUEL_WING_L_GAL", 200), ("LJ35_FUEL_WING_R_GAL", 200),
            ("LJ35_FUEL_PRESS_L", 5), ("LJ35_FUEL_PRESS_R", 5), ("LJ35_INV_PRI_BUS", 1), ("LJ35_INV_SEC_BUS", 1),
            ("LJ35_STALL_CIRCUIT_L", 1), ("LJ35_STALL_CIRCUIT_R", 1));
        Assert.Equal(new[] { "Oil pressure" }, mc.Evaluate(read));
        Assert.True(mc.IsLit);
        Assert.Empty(mc.Evaluate(read));
        mc.Reset();
        Assert.False(mc.IsLit);
        Assert.Empty(mc.Evaluate(read)); // still low, already acknowledged
    }
}

// The C172's refuelling, measured 2026-10-19 (C172-24): `FUEL TANK ... QUANTITY` written sets the tank's
// total, every read is its usable fuel (total minus 1.5), each tank holds 28 gallons, 26.5 usable, and the
// sim does not stop an overfill, so a typed load outside 0 to 26.5 is refused.

using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CessnaC172FuelTests
{
    [Fact]
    public void Each_tank_holds_28_gallons_26_and_a_half_usable()
    {
        Assert.Equal(28, CessnaC172BaseDefinition.TankCapacityGal);
        Assert.Equal(1.5, CessnaC172BaseDefinition.TankUnusableGal);
        Assert.Equal(26.5, CessnaC172BaseDefinition.TankUsableGal);
    }

    [Theory]
    [InlineData(0, 1.5)]       // empty of usable fuel: the unusable stays in the tank
    [InlineData(18.5, 20)]     // measured: 20 written read 18.5
    [InlineData(26.5, 28)]     // full
    public void The_write_is_the_tanks_total_the_typed_load_plus_the_unusable(double usable, double total)
    {
        Assert.Equal(total, CessnaC172BaseDefinition.TankTotalGallons(usable), 6);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(20, true)]
    [InlineData(26.5, true)]
    [InlineData(26.6, false)]   // the sim would take it: 30 written read 107 percent
    [InlineData(28, false)]     // the total, not a usable load
    [InlineData(-1, false)]
    [InlineData(double.NaN, false)]
    public void A_typed_load_is_taken_only_inside_the_usable_fuel(double gallons, bool taken)
    {
        Assert.Equal(taken, CessnaC172BaseDefinition.UsableFuelRefusal(gallons) == null);
    }

    [Theory]
    [InlineData(1.0, 0.0, null)]
    [InlineData(0.0, 0.0, "Refuelling is only possible on the ground.")]
    [InlineData(0.0, 2300.0, "Refuelling is only possible on the ground.")]
    [InlineData(1.0, 650.0, "Shut the engine down before refuelling.")]
    [InlineData(1.0, 47.0, "Shut the engine down before refuelling.")]   // the Starter cranking
    [InlineData(null, 0.0, "Refuelling is not available yet.")]
    [InlineData(1.0, null, "Refuelling is not available yet.")]
    public void Fuel_goes_in_only_on_the_ground_with_the_propeller_stopped(double? onGround, double? rpm, string? blocker)
    {
        Assert.Equal(blocker, CessnaC172BaseDefinition.RefuelBlocker(onGround, rpm));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_refuelling_words_read_the_same_in_every_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("Left tank 20.5 gallons.", CessnaC172BaseDefinition.DescribeTankLoad("Left", 20.5));
            Assert.Equal("Right tank 0.0 gallons.", CessnaC172BaseDefinition.DescribeTankLoad("Right", 0));
            Assert.Equal("Enter a fuel load between 0 and 26.5 gallons", CessnaC172BaseDefinition.UsableFuelRefusal(30));
            // Fill Both Tanks says what F then reads.
            Assert.Equal("Left 26.5, Right 26.5, Total 53.0 gallons",
                CessnaC172Gauges.DescribeFuelQuantities(CessnaC172BaseDefinition.TankUsableGal,
                    CessnaC172BaseDefinition.TankUsableGal));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_refuelling_rows_are_typed_and_a_button_with_no_ctrl_m_row()
    {
        var vars = new CessnaC172G1000Definition().GetVariables();
        foreach (string key in new[] { CessnaC172BaseDefinition.FuelLeftSetKey, CessnaC172BaseDefinition.FuelRightSetKey })
        {
            // A typo arrives as NaN and is refused, never 0, which would empty the tank.
            Assert.True(vars[key].UnparseableTextAsNaN, key);
            Assert.Equal(UpdateFrequency.Never, vars[key].UpdateFrequency);
        }
        Assert.Equal("Left Tank Fuel", vars[CessnaC172BaseDefinition.FuelLeftSetKey].DisplayName);
        Assert.Equal("Right Tank Fuel", vars[CessnaC172BaseDefinition.FuelRightSetKey].DisplayName);
        var fill = vars[CessnaC172BaseDefinition.FuelFillKey];
        Assert.True(fill.RenderAsButton);
        Assert.Equal("Fill Both Tanks", fill.DisplayName);

        var rows = MonitorRowBuilder.Build(vars).Select(r => r.Key).ToList();
        Assert.DoesNotContain(CessnaC172BaseDefinition.FuelLeftSetKey, rows);
        Assert.DoesNotContain(CessnaC172BaseDefinition.FuelRightSetKey, rows);
        Assert.DoesNotContain(CessnaC172BaseDefinition.FuelFillKey, rows);
    }
}

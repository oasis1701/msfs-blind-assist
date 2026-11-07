// The Engines window's rows are the pilot's only view of the Fenix's own engine gauges. They are
// spoken and brailled, so they must read the same under any PC culture (de-DE decimal comma, sv-SE
// U+2212 minus), never show "-0", and show "--" for a value the Fenix has not published.
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixEngineFormatTests
{
    private static readonly FenixEngineReading Idle = new(   // IAE A321 at idle, 2026-10-21 13:06
        new FenixEngineValues(true, 22.448190078473196, 475.38836459330855, 62.212353401252194, 437.8835188757868, 14.382028854544323, 149.60090176687544, 117.83659509082182),
        new FenixEngineValues(true, 22.445335864998796, 474.14250676655524, 62.391407703690426, 440.70427092395045, 14.267072188538169, 149.69811391994867, 116.73367427951749),
        17.0724731445315, 17.0685977935791, 1000);

    private static readonly string[] IdleKg =
    {
        "Engine 1: running", "Engine 2: running",
        "Engine 1 N1: 22.4 percent", "Engine 2 N1: 22.4 percent",
        "Engine 1 EGT: 475 degrees C", "Engine 2 EGT: 474 degrees C",
        "Engine 1 N2: 62.2 percent", "Engine 2 N2: 62.4 percent",
        "Engine 1 Fuel Flow: 438 kilograms per hour", "Engine 2 Fuel Flow: 441 kilograms per hour",
        "Engine 1 Oil Quantity: 14.4 quarts", "Engine 2 Oil Quantity: 14.3 quarts",
        "Engine 1 Oil Pressure: 150 psi", "Engine 2 Oil Pressure: 150 psi",
        "Engine 1 Oil Temperature: 118 degrees C", "Engine 2 Oil Temperature: 117 degrees C",
        "Total Air Temperature: 17 degrees C", "Outside Air Temperature: 17 degrees C",
    };

    [Fact]
    public void Rows_read_the_idle_engines_in_kilograms()
        => Assert.Equal(IdleKg, FenixEngineFormat.Rows(Idle, FenixEngineWeightUnit.Kilograms));

    [Fact]
    public void Rows_convert_fuel_flow_to_pounds()
    {
        var expected = (string[])IdleKg.Clone();
        expected[8] = "Engine 1 Fuel Flow: 965 pounds per hour";
        expected[9] = "Engine 2 Fuel Flow: 972 pounds per hour";
        Assert.Equal(expected, FenixEngineFormat.Rows(Idle, FenixEngineWeightUnit.Pounds));
    }

    [Fact]
    public void Rows_are_the_same_under_de_DE_and_sv_SE()
    {
        Assert.Equal(IdleKg, TestCulture.Run("de-DE", () => FenixEngineFormat.Rows(Idle, FenixEngineWeightUnit.Kilograms)));
        Assert.Equal(IdleKg, TestCulture.Run("sv-SE", () => FenixEngineFormat.Rows(Idle, FenixEngineWeightUnit.Kilograms)));
    }

    [Fact]
    public void Empty_reading_is_all_dashes()
    {
        var expected = IdleKg.Select(row => row[..row.IndexOf(": ", StringComparison.Ordinal)] + ": --").ToArray();
        Assert.Equal(18, expected.Length);
        Assert.Equal(expected, FenixEngineFormat.Rows(FenixEngineReading.None, FenixEngineWeightUnit.Pounds));
    }

    [Fact]
    public void Running_false_reads_not_running()
    {
        var reading = Idle with { Engine1 = Idle.Engine1 with { Running = false } };
        Assert.Equal("Engine 1: not running", FenixEngineFormat.Rows(reading, FenixEngineWeightUnit.Kilograms)[0]);
    }

    [Fact]
    public void Number_is_invariant_and_never_negative_zero()
    {
        Assert.Equal("22.4", TestCulture.Run("de-DE", () => FenixEngineFormat.Number(22.448, 1)));
        Assert.Equal("-12", TestCulture.Run("sv-SE", () => FenixEngineFormat.Number(-12.4, 0)));
        var cold = Idle with { OatC = -12.4 };
        Assert.Equal("Outside Air Temperature: -12 degrees C",
            TestCulture.Run("sv-SE", () => FenixEngineFormat.Rows(cold, FenixEngineWeightUnit.Kilograms)[17]));
        Assert.Equal("0", FenixEngineFormat.Number(-0.4, 0));
        Assert.Equal("0.0", FenixEngineFormat.Number(-0.04, 1));
        Assert.Equal("--", FenixEngineFormat.Number(null, 0));
        Assert.Equal("--", FenixEngineFormat.Number(double.NaN, 0));
        Assert.Equal("--", FenixEngineFormat.Number(double.PositiveInfinity, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FenixEngineFormat.Number(1, 2));
    }

    [Fact]
    public void Rows_hold_a_jittering_value()
    {
        var hold = new FenixEngineDisplayHold();
        var low = Idle with { Engine2 = Idle.Engine2 with { OilTemperature = 116.49 } };
        var high = Idle with { Engine2 = Idle.Engine2 with { OilTemperature = 116.51 } };
        Assert.Equal("Engine 2 Oil Temperature: 116 degrees C", FenixEngineFormat.Rows(low, FenixEngineWeightUnit.Kilograms, hold)[15]);
        Assert.Equal("Engine 2 Oil Temperature: 116 degrees C", FenixEngineFormat.Rows(high, FenixEngineWeightUnit.Kilograms, hold)[15]);
        Assert.Equal("Engine 2 Oil Temperature: 117 degrees C", FenixEngineFormat.Rows(high, FenixEngineWeightUnit.Kilograms)[15]);
    }

    [Fact]
    public void Weight_unit_comes_from_the_Fenix_setting()
    {
        static FenixSetting Unit(string value) => new("weight_unit", "Aircraft", value, new[] { "Kilograms", "Pounds" }, "Enum", "Livery", true);
        Assert.Equal(FenixEngineWeightUnit.Pounds, FenixEngineFormat.WeightUnitFrom(new[] { Unit("Pounds") }));
        Assert.Equal(FenixEngineWeightUnit.Kilograms, FenixEngineFormat.WeightUnitFrom(new[] { Unit("Kilograms") }));
        Assert.Null(FenixEngineFormat.WeightUnitFrom(new[] { Unit("Stones") }));
        Assert.Null(FenixEngineFormat.WeightUnitFrom(new[] { new FenixSetting("baro_unit", "Aircraft", "Hpa", Array.Empty<string>(), "Enum", "Livery", true) }));
        Assert.Null(FenixEngineFormat.WeightUnitFrom(Array.Empty<FenixSetting>()));
    }

    [Fact]
    public void Status_reads_the_variant_and_unit()
        => Assert.Equal(
            "Fenix A321, IAE engines, 2 extra tanks. Fuel flow in pounds per hour, the Fenix's weight unit. Values update every second; F5 reads them again.",
            FenixEngineFormat.Status("Fenix A321, IAE engines, 2 extra tanks", FenixEngineWeightUnit.Pounds, true, 1000, 2000));

    [Fact]
    public void Status_says_when_the_unit_is_a_fallback()
        => Assert.Equal(
            "Fuel flow in kilograms per hour; the Fenix's weight unit could not be read. Values update every second; F5 reads them again.",
            FenixEngineFormat.Status(null, FenixEngineWeightUnit.Kilograms, false, 1000, 2000));

    [Fact]
    public void Status_waits_before_the_first_value()
        => Assert.Equal(
            "Waiting for engine values from the Fenix. Is the Fenix loaded?",
            FenixEngineFormat.Status("x", FenixEngineWeightUnit.Pounds, true, null, 5000));

    [Fact]
    public void Status_says_when_values_stop()
    {
        Assert.Equal(
            "Fuel flow in pounds per hour, the Fenix's weight unit. No new values from the Fenix for more than 10 seconds; F5 reads them again.",
            FenixEngineFormat.Status(null, FenixEngineWeightUnit.Pounds, true, 1000, 11_001));
        Assert.Equal(
            "Fuel flow in pounds per hour, the Fenix's weight unit. Values update every second; F5 reads them again.",
            FenixEngineFormat.Status(null, FenixEngineWeightUnit.Pounds, true, 1000, 11_000));
    }
}

// tests/MSFSBlindAssist.Tests/Cessna172ReadoutsTests.cs
// MainForm's generic numeric formatter has no "MHz" case (a whole-MHz COM reads as a bare "137")
// and appends no unit for psi/fahrenheit/gallons/rpm, so every C172 status row is formatted here.
using System.Globalization;
using MSFSBlindAssist.Aircraft.C172;

namespace MSFSBlindAssist.Tests;

public class Cessna172ReadoutsTests : IDisposable
{
    private readonly CultureInfo _saved = CultureInfo.CurrentCulture;
    public Cessna172ReadoutsTests() => CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // comma-decimal locale
    public void Dispose() => CultureInfo.CurrentCulture = _saved;

    [Theory]
    [InlineData(121.5, "121.500 MHz")]
    [InlineData(137.0, "137.000 MHz")]
    [InlineData(0.0, "---.--- MHz")]
    public void Com_frequencies_always_carry_three_decimals(double mhz, string expected)
        => Assert.Equal(expected, Cessna172Readouts.ComFrequency(mhz));

    [Theory]
    [InlineData(110.3, "110.30 MHz")]
    [InlineData(0.0, "---.-- MHz")]
    public void Nav_frequencies_carry_two_decimals(double mhz, string expected)
        => Assert.Equal(expected, Cessna172Readouts.NavFrequency(mhz));

    [Fact]
    public void A_course_is_three_digits()
        => Assert.Equal("090 degrees", Cessna172Readouts.Course(90));

    [Fact]
    public void The_altimeter_display_shows_both_units()
        => Assert.Equal("29.92 inHg (1013 hPa)", Cessna172Readouts.Altimeter(29.92));

    [Theory]
    [InlineData(null, "Altimeter not available")]
    [InlineData(29.92, "Altimeter standard")]
    [InlineData(30.12, "Altimeter: 1020, 30.12")]
    public void The_spoken_altimeter_follows_the_hs787_form(double? inHg, string expected)
        => Assert.Equal(expected, Cessna172Readouts.AltimeterSpoken(inHg));

    [Theory]
    [InlineData(118.0, true)]
    [InlineData(136.975, true)]
    [InlineData(117.995, false)]
    [InlineData(137.0, false)]
    public void Com_validation_is_118_to_136_975(double mhz, bool ok)
        => Assert.Equal(ok, Cessna172Readouts.IsValidComMhz(mhz));

    [Theory]
    [InlineData(27.0, true)]
    [InlineData(31.5, true)]
    [InlineData(26.99, false)]
    [InlineData(31.51, false)]
    public void Altimeter_validation_is_27_to_31_5(double inHg, bool ok)
        => Assert.Equal(ok, Cessna172Readouts.IsValidAltimeterInHg(inHg));

    // KOHLSMAN_SET takes millibars × 16 (the A380 convention), NOT inHg × 16.
    [Fact]
    public void Kohlsman_set_parameter_is_millibars_times_sixteen()
        => Assert.Equal(16211u, Cessna172Readouts.KohlsmanSetParam(29.92));   // 1013.2 mb × 16

    [Theory]
    [InlineData(0, 0u)]
    [InlineData(100, 16383u)]
    [InlineData(50, 8192u)]
    public void Mixture_set_parameter_spans_0_to_16383(double pct, uint expected)
        => Assert.Equal(expected, Cessna172Readouts.MixtureSetParam(pct));

    [Fact]
    public void Engine_fuel_and_electrical_rows_carry_their_units()
    {
        Assert.Equal("2400 RPM", Cessna172Readouts.Rpm(2400.4));
        Assert.Equal("62 psi", Cessna172Readouts.Psi(62.2));
        Assert.Equal("180 °F", Cessna172Readouts.Fahrenheit(180.3));
        Assert.Equal("8.5 gallons per hour", Cessna172Readouts.GallonsPerHour(8.49));
        Assert.Equal("26.0 gallons", Cessna172Readouts.Gallons(26.01));
        Assert.Equal("28.1 volts", Cessna172Readouts.Volts(28.07));
        Assert.Equal("-4.2 amps", Cessna172Readouts.Amps(-4.2));
        Assert.Equal("75 percent", Cessna172Readouts.Percent(75.4));
    }
}

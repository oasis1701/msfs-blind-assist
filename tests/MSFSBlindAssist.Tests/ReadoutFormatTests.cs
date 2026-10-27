using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Read-only numeric rows render "{value} {units}". SimVarDefinition's default Units is
/// "number" — SimConnect's word for a dimensionless quantity — and it leaked into speech as
/// "V1: 0 number" on every unit-less read-out. A placeholder is never spoken; a real unit is.
/// </summary>
public class ReadoutFormatTests
{
    [Theory]
    [InlineData(0, false, "0 degrees Celsius")]
    [InlineData(15, false, "15 degrees Celsius")]
    [InlineData(-42, false, "-42 degrees Celsius")]
    [InlineData(0, true, "32 degrees Fahrenheit")]
    [InlineData(100, true, "212 degrees Fahrenheit")]
    [InlineData(-40, true, "-40 degrees Fahrenheit")]
    [InlineData(-42, true, "-44 degrees Fahrenheit")]
    [InlineData(15, true, "59 degrees Fahrenheit")]
    [InlineData(20.4, false, "20 degrees Celsius")]
    [InlineData(20.4, true, "69 degrees Fahrenheit")]
    public void OutsideTemperature_ConvertsBeforeRounding_AndNamesTheUnit(
        double celsius, bool useFahrenheit, string expected)
    {
        foreach (string culture in new[] { "en-US", "de-DE", "sv-SE" })
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                Assert.Equal(expected, ReadoutFormat.OutsideTemperature(celsius, useFahrenheit));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }
    }

    [Theory]
    [InlineData("50", "number", "50")]
    [InlineData("50", "Number", "50")]
    [InlineData("50", " number ", "50")]
    [InlineData("50", "", "50")]
    [InlineData("50", null, "50")]
    [InlineData("5000", "feet", "5000 feet")]
    [InlineData("29.92", "inHg", "29.92 inHg")]
    public void WithUnit_DropsThePlaceholder_KeepsARealUnit(string value, string? units, string expected)
    {
        Assert.Equal(expected, ReadoutFormat.WithUnit(value, units));
    }
}

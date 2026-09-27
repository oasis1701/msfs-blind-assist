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

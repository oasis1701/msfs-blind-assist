// Characterization tests for Pmdg777Altimeter (Aircraft/Pmdg777Altimeter.cs): the PMDG 777
// altimeter as the B key and the altimeter callout speak it. Every number is spoken with a
// decimal point whatever the Windows number format: CI runs under en-US, so the comma-decimal
// cultures are set here, or a culture leak ("29,92") would pass unnoticed.

namespace MSFSBlindAssist.Tests;

using System.Globalization;
using MSFSBlindAssist.Aircraft;

public class Pmdg777AltimeterTests
{
    private static string UnderCulture(string name, Func<string> speak)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            return speak();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void ASetting_IsSpokenInBothUnitsWithADecimalPoint(string culture)
    {
        Assert.Equal("Altimeter: 1018, 30.05", UnderCulture(culture, () => Pmdg777Altimeter.Sentence(30.05)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void TheInchesKeepTwoDecimals_EvenWhenTheLastIsZero(string culture)
    {
        Assert.Equal("Altimeter: 1009, 29.80", UnderCulture(culture, () => Pmdg777Altimeter.Sentence(29.80)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Standard_IsSpokenAsStandard(string culture)
    {
        Assert.Equal("Altimeter standard", UnderCulture(culture, () => Pmdg777Altimeter.Sentence(29.92)));
    }

    [Theory]
    [InlineData(29.9213, "Altimeter standard")]        // 1013.25 hPa
    [InlineData(29.9142, "Altimeter: 1013, 29.91")]    // a local 1013 hPa
    [InlineData(29.93, "Altimeter: 1014, 29.93")]
    public void Standard_IsReadingsWithinFiveThousandthsOf2992(double inHg, string expected)
    {
        Assert.Equal(expected, Pmdg777Altimeter.Sentence(inHg));
    }
}

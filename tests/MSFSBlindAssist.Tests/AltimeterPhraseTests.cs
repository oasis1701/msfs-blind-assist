// Characterization tests for AltimeterPhrase (Aircraft/AltimeterPhrase.cs): the altimeter as the
// PMDG 777's B key and altimeter callout speak it. Every number is spoken with a decimal point
// whatever the Windows number format: CI runs under en-US, so the comma-decimal cultures are set
// here, or a culture leak ("29,92") would pass unnoticed.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class AltimeterPhraseTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void ASetting_IsSpokenInBothUnitsWithADecimalPoint(string culture)
    {
        Assert.Equal("Altimeter: 1018, 30.05", TestCulture.Run(culture, () => AltimeterPhrase.Sentence(30.05)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void TheInchesKeepTwoDecimals_EvenWhenTheLastIsZero(string culture)
    {
        Assert.Equal("Altimeter: 1009, 29.80", TestCulture.Run(culture, () => AltimeterPhrase.Sentence(29.80)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Standard_IsSpokenAsStandard(string culture)
    {
        Assert.Equal("Altimeter standard", TestCulture.Run(culture, () => AltimeterPhrase.Sentence(29.92)));
    }

    [Theory]
    [InlineData(29.9213, "Altimeter standard")]        // 1013.25 hPa
    [InlineData(29.9151, "Altimeter standard")]        // just inside the 0.005 band, both sides
    [InlineData(29.9249, "Altimeter standard")]
    [InlineData(29.9149, "Altimeter: 1013, 29.91")]    // just outside it, both sides
    [InlineData(29.9251, "Altimeter: 1013, 29.93")]
    [InlineData(29.93, "Altimeter: 1014, 29.93")]
    public void Standard_IsReadingsWithinFiveThousandthsOf2992(double inHg, string expected)
    {
        Assert.Equal(expected, AltimeterPhrase.Sentence(inHg));
    }

    [Theory]
    [InlineData(980, "28.94")]
    [InlineData(1009, "29.80")]
    [InlineData(1012, "29.88")]
    [InlineData(1013, "29.91")]   // the closest local setting to standard: never "standard"
    [InlineData(1014, "29.94")]
    [InlineData(1020, "30.12")]
    public void A_whole_hPa_setting_reads_back_as_the_same_hPa(int hpa, string inches)
    {
        Assert.Equal($"Altimeter: {hpa}, {inches}", AltimeterPhrase.Sentence(hpa / AltimeterPhrase.HpaPerInHg));
    }
}

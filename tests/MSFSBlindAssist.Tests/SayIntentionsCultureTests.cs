using System.Globalization;
using MSFSBlindAssist.Services.SayIntentions;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Regex case-folding must be culture-invariant. Under tr-TR, IgnoreCase folds the
/// pattern letter I with the current culture, so \b(?:TAXI|VIA)\b stops matching
/// "taxi" — which silently killed the whole import for Turkish-locale users. Every
/// IgnoreCase regex in the SayIntentions integration therefore carries
/// RegexOptions.CultureInvariant; these tests run the load-bearing paths under tr-TR.
/// </summary>
public class SayIntentionsCultureTests
{
    private static void UnderCulture(string cultureName, Action assertions)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            assertions();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void AClearanceIsStillRecognizedUnderTurkishCaseFolding() => UnderCulture("tr-TR", () =>
    {
        Assert.True(SayIntentionsClearanceParser.LooksLikeTaxiClearance("Taxi to runway 22 via Alpha"));
        Assert.True(SayIntentionsClearanceParser.LooksLikeTaxiClearance("taxi to gate B6 via Lima"));
    });

    [Fact]
    public void PhoneticTaxiwaysStillResolveUnderTurkishCaseFolding() => UnderCulture("tr-TR", () =>
    {
        Assert.Equal(new[] { "L", "I" },
            SayIntentionsClearanceParser.ParseTaxiways("Taxi via Lima, India", new[] { "L", "I" }));
    });

    [Fact]
    public void TheHoldShortMaskStillFiresUnderTurkishCaseFolding() => UnderCulture("tr-TR", () =>
    {
        Assert.Equal("22",
            SayIntentionsClearanceParser.ParseDestinationRunway(
                "Taxi to runway 22 via Alpha, holding short of runway 4L"));
        Assert.Equal("04L", SayIntentionsClearanceParser.ParseHoldShortRunway("holding short of runway 4L"));
    });

    [Fact]
    public void TheClassifierStillClassifiesUnderTurkishCaseFolding() => UnderCulture("tr-TR", () =>
    {
        // No station/channel signal on purpose: this forces the AtcVocabulary path,
        // whose TAXI (with its I) is exactly what tr-TR folding broke.
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", "", null, "Taxi to runway 22 via Alpha"));
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", null, "INTERCOM", "Cabin crew doors to arrival"));
    });

    [Fact]
    public void ParkingNamesStillNormalizeUnderTurkishCaseFolding() => UnderCulture("tr-TR", () =>
    {
        // "Parking" and "Position" both carry an i through the keyword/noise regexes.
        Assert.Equal("A24", SayIntentionsClearanceParser.NormalizeParkingName("South Terminal Parking A24"));
        Assert.Equal("B6", SayIntentionsClearanceParser.NormalizeParkingName("Position B06"));
    });
}

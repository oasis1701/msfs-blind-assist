using System.Globalization;
using MSFSBlindAssist;
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
        // No station/channel signal on purpose: this forces the AtcVocabulary path.
        // The message carries ONLY the TAXI keyword (no RUNWAY, GROUND, etc.), so this
        // can only pass via the TAXI branch — whose I is exactly what tr-TR folding broke.
        Assert.True(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", "", null, "Taxi via Alpha and Bravo"));
        Assert.False(SayIntentionsTransmissionClassifier.IsRadioTransmission(
            "", null, "INTERCOM", "Cabin crew doors to arrival"));
    });

    [Fact]
    public void AGateKeywordStillMatchesUnderTurkishCaseFolding() => UnderCulture("tr-TR", () =>
    {
        // GateInClearance sees the raw mixed-case clearance, and PARKING/POSITION
        // carry the letter I that tr-TR folding breaks.
        Assert.Equal("A24", SayIntentionsClearanceParser.ParseDestinationGate("Taxi to parking A24 via Lima"));
    });

    [Fact]
    public void TheClearancePlanStillSplitsUnderTurkishCaseFolding() => UnderCulture("tr-TR", () =>
    {
        // ParseClearanceTaxiPlan's own ViaWord ("\bVIA\b", IgnoreCase) sees the raw
        // mixed-case clearance before ScanTaxiways ever runs, and carries the letter I
        // tr-TR folding breaks — a gap Task 1's Services-only sweep missed because this
        // regex lives in MainForm.SayIntentions.cs, not Services/SayIntentions/.
        var (taxiways, holdShorts, _) = MainForm.ParseClearanceTaxiPlan(
            "Runway 22 taxi via Alpha, hold short of runway 15, then Bravo", new[] { "A", "B" });
        Assert.Equal(new[] { "A", "B" }, taxiways);
        Assert.Equal(("A", "15"), (holdShorts[0].AfterTaxiway, holdShorts[0].Runway));
    });
}

// Characterization tests for SayIntentionsClearanceParser's runway extraction.
//
// SAFETY-CRITICAL (PR #86 review finding #1): a taxi clearance to a GATE
// routinely contains "hold short of runway NN". The original implementation used
// a leftmost Regex.Match for the destination runway, so that hold-short runway
// BECAME the taxi destination — routing a blind pilot at an active runway they
// had just been told to hold short of. The fix masks every "hold short of X" /
// "cross X" span before searching for a destination, so the two extractions can
// never collide.
//
// Runway output convention: zero-padded two digits + optional L/C/R ("05",
// "15L"). Both spoken ("one five left") and written ("15L") forms normalize to
// the same token so they compare cleanly against navdata RunwayIDs.
//
// This is characterization, not spec verification: if a literal ever disagrees
// with actual output, correct the test to match real output, not vice versa.

using MSFSBlindAssist.Services.SayIntentions;

namespace MSFSBlindAssist.Tests;

public class SayIntentionsClearanceParserTests
{
    // ---- The bug that motivated the rework ----

    [Fact]
    public void GateClearanceWithHoldShortDoesNotYieldARunwayDestination()
    {
        const string clearance = "Taxi to gate A9 via Alpha, Bravo, hold short of runway 15";
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(clearance));
        Assert.Equal("15", SayIntentionsClearanceParser.ParseHoldShortRunway(clearance));
    }

    [Fact]
    public void RunwayClearanceKeepsItsDestinationDespiteATrailingHoldShort()
    {
        // The exact clearance from PR #86's manual smoke test at CYYZ.
        const string clearance =
            "Runway one-five-left via Alpha-Tango, Alpha-Tango, Romeo, Bravo, hold short of runway two-three";
        Assert.Equal("15L", SayIntentionsClearanceParser.ParseDestinationRunway(clearance));
        Assert.Equal("23", SayIntentionsClearanceParser.ParseHoldShortRunway(clearance));
    }

    [Fact]
    public void CrossingInstructionIsNotADestination()
    {
        const string clearance = "Taxi to gate B12 via Charlie, cross runway 09, then Delta";
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(clearance));
    }

    // ---- Spoken-form normalization ----

    [Theory]
    [InlineData("one five left", "15L")]
    [InlineData("zero niner", "09")]
    [InlineData("two three", "23")]
    [InlineData("one-five-left", "15L")]
    [InlineData("tree", "03")]
    [InlineData("fife", "05")]
    public void SpokenRunwaysNormalize(string spoken, string expected)
    {
        Assert.Equal(expected, SayIntentionsClearanceParser.CleanRunway(
            SayIntentionsClearanceParser.NormalizeSpokenRunway(spoken)));
    }

    [Fact]
    public void WrittenRunwayWithSpacedSideKeepsTheSide()
    {
        // PR #86 matched "15" and stopped, dropping "left" — at an airport with
        // 15L/15R that silently resolved to no runway at all.
        Assert.Equal("15L", SayIntentionsClearanceParser.ParseDestinationRunway("Taxi to runway 15 left via Alpha"));
    }

    [Fact]
    public void SingleDigitRunwayIsZeroPadded()
    {
        Assert.Equal("05", SayIntentionsClearanceParser.CleanRunway("5"));
    }

    [Fact]
    public void CleanRunwayRejectsTextWithNoDigits()
    {
        Assert.Null(SayIntentionsClearanceParser.CleanRunway("niner"));
    }

    // ---- Taxi-clearance gating (stops a landing clearance becoming a route) ----

    [Theory]
    [InlineData("Taxi to gate A9 via Alpha", true)]
    [InlineData("Runway 15L via Bravo, Charlie", true)]
    [InlineData("Cleared to land runway 23", false)]
    [InlineData("Contact tower on 118.7", false)]
    public void TaxiClearanceGating(string text, bool expected)
    {
        Assert.Equal(expected, SayIntentionsClearanceParser.LooksLikeTaxiClearance(text));
    }

    // ---- Taxiway sequence ----
    //
    // knownTaxiways always comes from the live TaxiGraph, so only names the
    // airport really has can ever be returned. These fixtures mirror CYYZ, the
    // airport PR #86 was smoke-tested at.

    private static readonly string[] CyyzTaxiways = { "AT", "A", "T", "R", "B", "D", "H", "Q" };

    [Fact]
    public void PhoneticTaxiwaysResolveToTheirNavdataNames()
    {
        var result = SayIntentionsClearanceParser.ParseTaxiways(
            "Runway one-five-left via Alpha-Tango, Romeo, Bravo, hold short of runway two-three",
            CyyzTaxiways);
        Assert.Equal(new[] { "AT", "R", "B" }, result);
    }

    [Fact]
    public void RouteContinuesPastARunwayCrossing()
    {
        // PR #86 split the route text at "cross" and dropped everything after,
        // losing the post-crossing taxiway. Clearances legitimately continue
        // (and reuse taxiways) across a crossing — the KBOS pattern recorded in
        // docs/taxi-guidance.md.
        var result = SayIntentionsClearanceParser.ParseTaxiways(
            "Taxi to gate via Bravo, cross runway 15, then Delta", CyyzTaxiways);
        Assert.Equal(new[] { "B", "D" }, result);
    }

    [Fact]
    public void TheEnglishArticleIsNotTaxiwayAlpha()
    {
        // Literal single-letter alternatives match case-SENSITIVELY, so lowercase
        // "a"/"at" in prose can never be read as taxiway A / AT.
        var result = SayIntentionsClearanceParser.ParseTaxiways(
            "Taxi to a stand at the terminal via Romeo", CyyzTaxiways);
        Assert.Equal(new[] { "R" }, result);
    }

    [Fact]
    public void LongerTaxiwayNamesWinOverTheirPrefixes()
    {
        var result = SayIntentionsClearanceParser.ParseTaxiways("via Alpha-Tango", CyyzTaxiways);
        Assert.Equal(new[] { "AT" }, result);
    }

    [Fact]
    public void SequenceTerminatesAtAFrequencyHandoff()
    {
        var result = SayIntentionsClearanceParser.ParseTaxiways(
            "via Bravo, Romeo, contact tower on Delta point seven", CyyzTaxiways);
        Assert.Equal(new[] { "B", "R" }, result);
    }

    [Fact]
    public void ConsecutiveDuplicatesCollapse()
    {
        // SI speech repeats a taxiway across a hold-short; the dialog models each
        // taxiway once, so consecutive repeats collapse.
        var result = SayIntentionsClearanceParser.ParseTaxiways(
            "via Alpha-Tango, Alpha-Tango, Romeo", CyyzTaxiways);
        Assert.Equal(new[] { "AT", "R" }, result);
    }

    [Fact]
    public void NonConsecutiveReuseIsPreserved()
    {
        // A taxiway legitimately reappears later in a clearance — never dedupe globally.
        var result = SayIntentionsClearanceParser.ParseTaxiways("via Bravo, Romeo, Bravo", CyyzTaxiways);
        Assert.Equal(new[] { "B", "R", "B" }, result);
    }

    [Fact]
    public void NoViaKeywordYieldsNoTaxiways()
    {
        Assert.Empty(SayIntentionsClearanceParser.ParseTaxiways("Taxi to gate A9", CyyzTaxiways));
    }
}

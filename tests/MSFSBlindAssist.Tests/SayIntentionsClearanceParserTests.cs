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

    // Every spoken variant of a hold instruction must mask, not just the exact
    // "hold short of". The first fix handled CROSS(ING) but only bare "hold short",
    // so a pilot READBACK — "holding short of runway 15", which is what SayIntentions
    // publishes as the most recent transmission — still made 15 the destination.
    [Theory]
    [InlineData("Taxi to gate A9 via Alpha Bravo, holding short of runway 15")]
    [InlineData("Taxi to gate A9 via Alpha Bravo, hold-short of runway 15")]
    [InlineData("Taxi to gate A9 via Alpha Bravo, hold short of the runway 15")]
    [InlineData("Taxi to gate A9 via Alpha Bravo, remain short of runway 15")]
    [InlineData("Taxi to gate A9 via Alpha Bravo, holding short runway 15")]
    public void EveryHoldPhrasingMasksTheDestination(string clearance)
    {
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(clearance));
        Assert.Equal("15", SayIntentionsClearanceParser.ParseHoldShortRunway(clearance));
    }

    [Fact]
    public void IcaoHoldingPointIsAHoldNotADestination()
    {
        // ICAO phraseology. Routing a blind pilot ONTO 27 here would be the same
        // failure in different words.
        const string clearance = "Taxi to the holding point runway 27 via Alpha";
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(clearance));
        Assert.Equal("27", SayIntentionsClearanceParser.ParseHoldShortRunway(clearance));
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
        //
        // The prose MUST sit after "via" — ParseTaxiways only scans the route text,
        // so an article before the keyword exercises nothing (the first version of
        // this test put it before "via" and passed without touching the mechanism).
        // "a", "at" and the lowercase "t" of "taxi"/"terminal" must all be inert:
        // only Romeo survives.
        var result = SayIntentionsClearanceParser.ParseTaxiways(
            "Runway 15L via Romeo, then a short taxi at the terminal", CyyzTaxiways);
        Assert.Equal(new[] { "R" }, result);
    }

    [Fact]
    public void SpokenAlphanumericTaxiwaysResolveWholly()
    {
        // "Bravo Four" must reach B4, not decay to B. B is a real taxiway, so the
        // wrong route would be delivered with full confidence and never reported
        // as skipped. Affects any airport with alphanumeric taxiways (KJFK, EGLL…).
        var known = new[] { "AT", "A", "T", "R", "B", "B4", "K", "N" };
        Assert.Equal(new[] { "B4", "K" },
            SayIntentionsClearanceParser.ParseTaxiways("Taxi to gate A9 via Bravo Four, Kilo", known));
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

    // ---- Taxiways the airport does not have ----
    //
    // ParseTaxiways can only ever return names the graph knows, so a taxiway the
    // clearance names but the airport does not have vanished without a word: at CYYZ
    // "via Alpha, Kilo, Romeo" announced "Via A, R" and the pilot had no way to tell a
    // leg had gone missing — the route then takes a different path than ATC cleared.
    // ScanTaxiways reports those tokens alongside the ones that resolved.
    //
    // Detection is deliberately PHONETIC-ONLY. A bare uppercase designator would
    // false-positive on ordinary abbreviations, and a false "could not apply K" teaches
    // the pilot to distrust the whole announcement — much worse than a miss.

    [Fact]
    public void ATaxiwayTheAirportDoesNotHaveIsReported()
    {
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Runway one-five-left via Alpha, Kilo, Romeo", CyyzTaxiways);

        Assert.Equal(new[] { "A", "R" }, scan.Resolved);
        Assert.Equal(new[] { "K" }, scan.Unresolved);
    }

    [Fact]
    public void ParseTaxiwaysStillReturnsOnlyTheResolvedNames()
    {
        // The original signature has callers and tests of its own; the scan is additive.
        Assert.Equal(new[] { "A", "R" },
            SayIntentionsClearanceParser.ParseTaxiways("via Alpha, Kilo, Romeo", CyyzTaxiways));
    }

    [Fact]
    public void ATaxiwayConsumedAsPartOfALongerNameIsNotReportedMissing()
    {
        // "Alpha-Tango" resolves whole to AT. Reading its two words on their own would
        // report A and T missing against a route that resolved perfectly.
        //
        // The known set here deliberately has NO bare A or T: an airport can have AT
        // without either. With CYYZ's full list both words happen to name real
        // taxiways, which hides whether the overlap guard is doing anything at all.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Runway one-five-left via Alpha-Tango, Romeo, Bravo, hold short of runway two-three",
            new[] { "AT", "R", "B" });

        Assert.Equal(new[] { "AT", "R", "B" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void ProseAfterTheRouteIsNeverReportedAsAMissingTaxiway()
    {
        // Only NATO words count, so ordinary lowercase prose carries nothing to report.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Runway 15L via Romeo, then a short taxi at the terminal", CyyzTaxiways);

        Assert.Equal(new[] { "R" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void TextAfterARouteTerminatorIsNotScannedForMissingTaxiways()
    {
        // A frequency is not a route. The unresolved scan reads exactly the text the
        // resolved scan does, so it stops at the same terminator.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "via Bravo, Romeo, contact ground on Kilo point seven", CyyzTaxiways);

        Assert.Equal(new[] { "B", "R" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void AnAtisInformationLetterIsNotATaxiway()
    {
        // "advise you have information Sierra" is the ATIS letter. CYYZ has no S, so
        // this reported a missing taxiway S; at an airport that HAS an S it silently
        // appended S to the route instead. INFORMATION ends the route for both scans.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Taxi to gate A9 via Bravo, advise you have information Sierra", CyyzTaxiways);

        Assert.Equal(new[] { "B" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void NothingBeforeTheViaKeywordIsScanned()
    {
        // The destination is not part of the route: a gate spelled phonetically must
        // never come back as a taxiway the airport is missing.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Taxi to gate Kilo nine via Alpha", CyyzTaxiways);

        Assert.Equal(new[] { "A" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void AMissingAlphanumericTaxiwayIsReportedWhole()
    {
        // Reporting "Bravo Four" as B would name a taxiway the clearance never said.
        var known = new[] { "A", "R" };
        var scan = SayIntentionsClearanceParser.ScanTaxiways("via Alpha, Bravo Four", known);

        Assert.Equal(new[] { "A" }, scan.Resolved);
        Assert.Equal(new[] { "B4" }, scan.Unresolved);
    }

    [Fact]
    public void ARepeatedMissingTaxiwayIsReportedOnce()
    {
        var scan = SayIntentionsClearanceParser.ScanTaxiways("via Kilo, Romeo, Kilo", CyyzTaxiways);

        Assert.Equal(new[] { "R" }, scan.Resolved);
        Assert.Equal(new[] { "K" }, scan.Unresolved);
    }

    [Fact]
    public void ATaxiwayTheGraphSpellsWithASpaceIsNotReportedMissing()
    {
        // BuildTaxiwayPattern has no phonetic branch for a name with a space, so
        // "Bravo Four" cannot resolve against a graph that spells it "B 4" — but the
        // airport plainly has it, and saying otherwise would be a false alarm.
        var scan = SayIntentionsClearanceParser.ScanTaxiways("via Bravo Four", new[] { "B 4" });

        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void AClearanceWithNoRouteReportsNothingMissing()
    {
        var scan = SayIntentionsClearanceParser.ScanTaxiways("Taxi to gate A9", CyyzTaxiways);

        Assert.Empty(scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    // ---- Parking names ----

    [Theory]
    [InlineData("Gate A9", "A9")]
    [InlineData("A-9", "A9")]                       // PR #86 truncated this to "A"
    [InlineData("Parking 12L", "12L")]
    [InlineData("A9 - Terminal 1", "A9")]           // a SPACED dash IS a descriptor separator
    [InlineData("Ramp 4", "4")]
    [InlineData("", "")]
    public void ParkingNamesNormalize(string raw, string expected)
    {
        Assert.Equal(expected, SayIntentionsClearanceParser.NormalizeParkingName(raw));
    }

    [Theory]
    [InlineData("Taxi to gate A9 via Alpha", "A9")]
    [InlineData("Taxi to stand 41 via Bravo", "41")]
    [InlineData("Runway 15L via Alpha", null)]
    // The normalizer handled "A-9" but the CAPTURE did not admit a hyphen, so the
    // match stopped at the bare letter and the pilot was routed to stand "A" — or,
    // with no such stand, fell through to the departure RUNWAY as the destination.
    [InlineData("Taxi to gate A-9 via Alpha", "A9")]
    [InlineData("Taxi to stand B-12 via Alpha", "B12")]
    public void GateDestinationExtraction(string clearance, string? expected)
    {
        Assert.Equal(expected, SayIntentionsClearanceParser.ParseDestinationGate(clearance));
    }
}

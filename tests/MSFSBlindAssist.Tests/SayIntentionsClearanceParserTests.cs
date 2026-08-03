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

    [Theory]
    // KDTW Ground, live, 2026-07-31, verbatim: SayIntentions writes the crossing with a
    // HYPHEN. The separator between the prefix and the runway was spelled `\s+`, so the
    // mask did not cover this span at all and the leftmost "runway 4R" became the
    // DESTINATION — a taxiing aircraft routed AT the active runway it had just been
    // cleared to cross, which is precisely what masking exists to prevent. It stayed
    // latent only because the transmission was being discarded for an unrelated reason
    // (the clearance lookup tested just the newest message, and an advisory had landed
    // on top of it four seconds later). Fixing that lookup alone would have exposed this.
    [InlineData("cross-runway 4R, then continue taxi via K, Q")]
    [InlineData("Taxi to gate B12 via Charlie, cross-runway 09, then Delta")]
    [InlineData("Taxi to gate B12 via Charlie, crossing-runway 09, then Delta")]
    public void AHyphenatedCrossingIsMaskedToo(string clearance)
    {
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(clearance));
    }

    [Fact]
    public void AHyphenatedSeparatorDoesNotSwallowARealRunwayDestination()
    {
        // The other half of the same change: widening the separator must not let the
        // mask reach past a crossing and eat the runway the clearance actually routes to.
        Assert.Equal("15L", SayIntentionsClearanceParser.ParseDestinationRunway(
            "Runway 15L via Bravo, cross-runway 04L, Charlie"));
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

    // ---- Multi-runway hold-short and crossing lists ----
    //
    // CrossPrefix/HoldPrefix originally bound exactly ONE runway token, so "cross
    // runway 28L and runway 28R" left 28R unmasked and the destination capture read
    // it as the place to taxi TO on a gate-bound clearance. Plural "cross runways 4L
    // and 4R" masked nothing at all. RunwayList is the one shared spelling of the
    // list tail, used by both the mask and the hold-short capture.

    [Fact]
    public void ACrossingListedAsTwoSingularRunwaysIsWhollyMasked()
    {
        string clearance = "Taxi to gate B6 via Bravo, cross runway 28L and runway 28R";
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(clearance));
        Assert.Equal("B6", SayIntentionsClearanceParser.ParseDestinationGate(clearance));
        Assert.Equal(new[] { "B" }, SayIntentionsClearanceParser.ParseTaxiways(clearance, new[] { "B" }));
    }

    [Fact]
    public void APluralRunwaysCrossingIsMaskedToo()
    {
        string clearance = "Continue taxi via Bravo, cross runways 4L and 4R, then Charlie";
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(clearance));
        Assert.Equal(new[] { "B", "C" },
            SayIntentionsClearanceParser.ParseTaxiways(clearance, new[] { "B", "C" }));
    }

    [Fact]
    public void APluralHoldShortYieldsEveryRunwayItNames()
    {
        Assert.Equal(new[] { "04L", "04R" },
            SayIntentionsClearanceParser.ParseHoldShortRunways(
                "Taxi via Alpha, hold short of runways 4L and 4R"));
    }

    // ---- Runway side binding ----
    //
    // RunwayToken's side suffix used to be `\s*(?:LEFT|RIGHT|CENTER|CENTRE|[LCR])?` —
    // \s* could reach across a masked hold-short span, and the bare [LCR] had no
    // trailing boundary, so "taxi to runway 22 remain this frequency" parsed
    // destination "22R" from the r of "remain".

    [Theory]
    [InlineData("Taxi to runway 22 remain this frequency", "22")]
    [InlineData("Taxi to runway 22, hold short of runway 4L, remain this frequency", "22")]
    [InlineData("Taxi to runway 15 left via Alpha", "15L")]
    public void ASideLetterOnlyBindsWhenItIsARunwaySide(string clearance, string expected)
        => Assert.Equal(expected, SayIntentionsClearanceParser.ParseDestinationRunway(clearance));

    // ---- The list tail must never fabricate a runway ----
    //
    // RunwayList's tail originally reused the full RunwayToken, whose spoken branch
    // is `(?:...|[-\s])+` — one bare space or hyphen alone satisfies that `+`. So
    // after a real "," or "and", ANY next character could make the tail "succeed":
    // a following digit run ("737") was read as a second runway, a following bare
    // spoken word ("one", "Center") was read as a second runway (or, for Center,
    // simply consumed and lost as a taxiway), and even ordinary prose let the tail
    // eat the separator's own trailing space for no reason. The fix gives the TAIL
    // its own narrower token — written runways only, digits capped at two — so it
    // can only ever extend the list onto a REAL second runway.

    [Theory]
    [InlineData("Taxi to gate A9 via Alpha, hold short of runway 15, 737 on the runway", new[] { "15" })]
    [InlineData("Hold short of runway 22, one moment please", new[] { "22" })]
    [InlineData("Hold short of runways 4L and 4R", new[] { "04L", "04R" })]
    public void TheListTailNeverFabricatesARunway(string clearance, string[] expected)
        => Assert.Equal(expected, SayIntentionsClearanceParser.ParseHoldShortRunways(clearance));

    [Fact]
    public void ACompassNamedTaxiwayAfterACrossingSurvives()
        => Assert.Equal(new[] { "H", "C", "A" }, SayIntentionsClearanceParser.ParseTaxiways(
            "Taxi via Hotel, cross runway 22, Center, Alpha", new[] { "H", "C", "A" }));

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

    [Theory]
    [InlineData("one five right", "15R")]
    [InlineData("two four center", "24C")]
    [InlineData("three one centre", "31C")]
    public void SpokenRightAndCenterSidesNormalize(string spoken, string expected)
        => Assert.Equal(expected,
            SayIntentionsClearanceParser.CleanRunway(
                SayIntentionsClearanceParser.NormalizeSpokenRunway(spoken)));

    [Fact]
    public void ASpokenRightDestinationParses()
        => Assert.Equal("15R", SayIntentionsClearanceParser.ParseDestinationRunway(
            "Taxi to runway one five right via Alpha"));

    [Fact]
    public void ASpokenCenterHoldShortParses()
        => Assert.Equal("24C", SayIntentionsClearanceParser.ParseHoldShortRunway(
            "Hold short of runway two four center"));

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

    // A squawk legitimately ENDS a taxi clearance — RouteTerminator lists SQUAWK for
    // exactly that reason. Excluding on it rejected real clearances, leaving
    // ClearanceText null so the destination fell through to the departure runway and
    // the pilot heard "no taxiways matched, using shortest path" — the same silent
    // failure the exclusion existed to prevent, reached from the other side.
    [Theory]
    [InlineData("Runway 22R, taxi via Alpha, Bravo. Squawk 4571.")]
    [InlineData("Taxi to runway 15L via Alpha, hold short of runway 22. Squawk 1200.")]
    [InlineData("Runway 22R, taxi via Bravo, squawk 0231, departure frequency 124.1.")]
    public void ATaxiClearanceEndingInASquawkIsStillATaxiClearance(string text)
    {
        Assert.True(SayIntentionsClearanceParser.LooksLikeTaxiClearance(text));
    }

    // ...and clearance delivery is still excluded without leaning on the squawk. Both
    // are verbatim from a live KBOS capture — the readback matters because SI
    // publishes it too, and it is the newest transmission at the moment a pilot might
    // press the import key.
    [Theory]
    [InlineData("Cleared to Miami via the SSOXS7 departure, then as filed. Climb and maintain 5000. Squawk 4571.")]
    [InlineData("Cleared to Miami via the SSOXS7 departure, then as filed, climb and maintain five thousand, squawk 4571.")]
    public void ClearanceDeliveryIsStillNotATaxiClearance(string text)
    {
        Assert.False(SayIntentionsClearanceParser.LooksLikeTaxiClearance(text));
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
    public void AnApostropheContractionIsNotATaxiway()
        => Assert.Equal(new[] { "A", "B" },
            SayIntentionsClearanceParser.ParseTaxiways(
                "Taxi via Alpha, Bravo, I'll call your crossing", new[] { "A", "B", "I" }));

    [Fact]
    public void ATypographicApostropheContractionIsNotATaxiway()
        => Assert.Equal(new[] { "A", "B" }, SayIntentionsClearanceParser.ParseTaxiways(
            "Taxi via Alpha, Bravo, I’ll call your crossing", new[] { "A", "B", "I" }));

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

    // RouteTerminator originally stopped only at CONTACT/MONITOR/SQUAWK/REMAIN/
    // REPORT/GIVE WAY/FOLLOW/INFORMATION. CAUTION/TRAFFIC/EXPECT open the same kind
    // of advisory tail SI appends after a real clearance, and a phonetic word inside
    // one ("caution golf cart crossing") became a route leg ATC never cleared.
    [Theory]
    [InlineData("Runway 4L taxi via Kilo, Quebec, caution golf cart crossing", new[] { "K", "Q" })]
    [InlineData("Taxi via Alpha, Bravo, traffic is a Boeing 737 on short final", new[] { "A", "B" })]
    [InlineData("Taxi via Alpha, expect further clearance on the way", new[] { "A" })]
    [InlineData("Taxi via Alpha, Bravo, monitor ground on point nine", new[] { "A", "B" })]
    [InlineData("Taxi via Alpha, report reaching the ramp", new[] { "A" })]
    [InlineData("Taxi via Alpha, give way to the Airbus, then Bravo", new[] { "A" })]
    [InlineData("Taxi via Alpha, follow the company 737 ahead", new[] { "A" })]
    public void AnAdvisoryTailNeverAddsALeg(string clearance, string[] expected)
        => Assert.Equal(expected, SayIntentionsClearanceParser.ParseTaxiways(
            clearance, new[] { "A", "B", "C", "G", "K", "Q", "F", "N" }));

    [Fact]
    public void APushbackApprovalCarryingTheRouteIsAcceptedAsATaxiClearance()
    {
        // Pinned deliberately: SI can fold the taxi clearance into the pushback
        // approval, and the route information in it is real — rejecting the shape
        // would lose the clearance. EXPECT terminates only ROUTE text after "via";
        // ahead of it, "expect runway 22L" stays the destination.
        string text = "Pushback approved, expect runway 22L, after push taxi via Alpha, hold short of runway 22L";
        Assert.True(SayIntentionsClearanceParser.LooksLikeTaxiClearance(text));
        Assert.Equal(new[] { "A" }, SayIntentionsClearanceParser.ParseTaxiways(text, new[] { "A" }));
        Assert.Equal("22L", SayIntentionsClearanceParser.ParseDestinationRunway(text));
        Assert.Equal("22L", SayIntentionsClearanceParser.ParseHoldShortRunway(text));
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

    // ---- Compass words spoken for a single letter (LEPA, 2026-07-29) ----
    //
    // Palma Ground, live: "Taxi to holding point runway 24R via LE, E, North, H2."
    // LEPA's navdata calls that taxiway N. SayIntentions rendered the bare letter as
    // the plain-English word "North", not the NATO "November", and both halves of the
    // machinery missed it: the pattern could not match "North" (it stopped at the
    // trailing "orth"), and the phonetic-only unresolved scan could not report it
    // either. The pilot heard a three-taxiway route with nothing to say a leg had gone.
    //
    // NORTH/SOUTH/EAST/WEST/CENTER/CENTRE are therefore spoken forms of N/S/E/W/C in
    // the SAME table as ALPHA/BRAVO, which wires the match and the report at once.

    // Every taxiway LEPA really has (navdata, 2026-07). It is the right fixture
    // because N and its numbered siblings both exist — a compass word has to reach the
    // bare letter without eating N1..N6 — and because W5 exists while a bare W does
    // not, which is what makes "West" reportable here.
    private static readonly string[] LepaTaxiways =
    {
        "A", "B", "C", "D", "E", "F", "G", "J", "K", "L", "M", "N", "P", "Q", "S", "Z",
        "H1", "H2", "H4", "H5", "H6", "H7", "H8", "H9", "H10",
        "LA", "LB", "LC", "LD", "LE", "LF", "LG", "LJ", "LK", "LM", "LP", "LQ",
        "N1", "N3", "N4", "N5", "N6",
        "S1", "S2", "S3", "T1", "T2", "W5"
    };

    private const string PalmaClearance =
        "Taxi to holding point runway 24R via LE, E, North, H2.";

    [Fact]
    public void TheLivePalmaClearanceKeepsItsCompassTaxiway()
    {
        var scan = SayIntentionsClearanceParser.ScanTaxiways(PalmaClearance, LepaTaxiways);

        Assert.Equal(new[] { "LE", "E", "N", "H2" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void TheLivePalmaClearanceStillHoldsShortRatherThanRoutingOntoTheRunway()
    {
        // The ICAO holding point is unchanged by any of this — pinned so the compass
        // work can never quietly turn 24R into the destination.
        Assert.Null(SayIntentionsClearanceParser.ParseDestinationRunway(PalmaClearance));
        Assert.Equal("24R", SayIntentionsClearanceParser.ParseHoldShortRunway(PalmaClearance));
        Assert.True(SayIntentionsClearanceParser.LooksLikeTaxiClearance(PalmaClearance));
    }

    [Theory]
    [InlineData("North", "N")]
    [InlineData("South", "S")]
    [InlineData("East", "E")]
    [InlineData("West", "W")]
    [InlineData("Center", "C")]
    [InlineData("Centre", "C")]
    // SayIntentions' capitalization is LLM output, not a contract, so the word matches
    // in any case exactly as ALPHA does. Prose is ruled out by context, not by case —
    // the case asymmetry protects the single-letter LITERAL branch and nothing else.
    [InlineData("north", "N")]
    [InlineData("NORTH", "N")]
    public void ACompassWordIsTheLetterItSpells(string spoken, string expected)
    {
        var known = new[] { "A", "C", "E", "N", "S", "W" };

        Assert.Equal(new[] { "A", expected },
            SayIntentionsClearanceParser.ParseTaxiways($"Taxi to gate 12 via Alpha, {spoken}", known));
    }

    [Fact]
    public void ACompassWordForATaxiwayTheAirportDoesNotHaveIsReported()
    {
        // LEPA has W5 but no bare W. Before compass words were scanned this vanished
        // in silence — the exact second half of the Palma failure.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Taxi to holding point runway 24R via LE, West, H2.", LepaTaxiways);

        Assert.Equal(new[] { "LE", "H2" }, scan.Resolved);
        Assert.Equal(new[] { "W" }, scan.Unresolved);
    }

    // ---- ...and the prose that must NOT read as a taxiway ----
    //
    // This is what a compass word costs that a NATO word does not: "north" is an
    // ordinary English word and it does appear after "via". A false "could not apply
    // North" teaches the pilot to distrust the whole announcement, and a false MATCH
    // silently adds a leg ATC never cleared. A compass word is therefore only a
    // taxiway when nothing around it makes it a direction.

    [Fact]
    public void ADirectionOfTravelIsNotATaxiway()
    {
        // LEPA HAS N, so without the guard this quietly inserts it into the route.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Taxi to gate 12 via H2, then taxi north on Alpha", LepaTaxiways);

        Assert.Equal(new[] { "H2", "A" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void ADirectionalPlaceNameIsNotATaxiway()
    {
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Taxi via H2 to the north side of the terminal", LepaTaxiways);

        Assert.Equal(new[] { "H2" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void ADirectionIsNotReportedAsAMissingTaxiwayEither()
    {
        // The mirror image: CYYZ has no N, so the same prose that would falsely MATCH
        // at LEPA would falsely REPORT here. One guard has to cover both scans, or the
        // announcement contradicts itself from one airport to the next.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Runway 15L via Romeo, then taxi north on Bravo", CyyzTaxiways);

        Assert.Equal(new[] { "R", "B" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void ARunwaySideIsNotTaxiwayCharlie()
    {
        // "Center" is the side of a runway designator. A hold-short or crossing runway
        // is already masked out; a runway named after the via keyword is not, so the
        // word sits in the route text with a comma after it and nothing else to say it
        // is not a taxiway.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Taxi via H2 to runway 24 Center", LepaTaxiways);

        Assert.Equal(new[] { "H2" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void AMaskedCrossingRunwaySideIsNotTaxiwayCharlieEither()
    {
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Taxi to gate 12 via H2, cross runway 24 center, then B", LepaTaxiways);

        Assert.Equal(new[] { "H2", "B" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Theory]
    [InlineData("and")]
    [InlineData("then")]
    public void ACompassWordJoinedToTheNextTaxiwayIsStillATaxiway(string connector)
    {
        // "LE, North and H2" is how English writes the last two items of a list, and
        // SayIntentions writes English. Without this the leg is dropped in silence —
        // the Palma failure again, one word further along.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            $"via LE, North {connector} H2", LepaTaxiways);

        Assert.Equal(new[] { "LE", "N", "H2" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void AHoldShortDoesNotMakeTheTaxiwayBeforeItReadAsProse()
    {
        // A hold-short attaches straight onto the last taxiway with no comma — the
        // ordinary phrasing — and it is blanked to spaces before the scan. "North" is
        // therefore followed by twenty-odd blanks and then whatever came after the
        // hold, which is prose ("for landing traffic"). The guard looks at the word
        // IMMEDIATELY after, so a blanked span reads as "nothing follows", which is
        // what it is. Reaching across it drops the last taxiway of the clearance.
        var scan = SayIntentionsClearanceParser.ScanTaxiways(
            "Taxi to gate 12 via LE, North hold short of runway 06L for landing traffic.",
            LepaTaxiways);

        Assert.Equal(new[] { "LE", "N" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    [Fact]
    public void ThePhoneticNamesAreUntouchedByTheDirectionGuard()
    {
        // The guard is scoped to compass words and must stay there. A NATO word is not
        // English: "Bravo" needs no context to be a taxiway, and holding it to the same
        // test would drop it from every clearance that says where the route ends.
        Assert.Equal(new[] { "B" },
            SayIntentionsClearanceParser.ParseTaxiways("Taxi via Bravo to the gate", CyyzTaxiways));
    }

    [Fact]
    public void ACompassWordFollowedByTheNextDesignatorIsStillATaxiway()
    {
        // A route list without commas still reads as a route: what follows "North" is
        // another taxiway, not English.
        var scan = SayIntentionsClearanceParser.ScanTaxiways("via LE E North H2", LepaTaxiways);

        Assert.Equal(new[] { "LE", "E", "N", "H2" }, scan.Resolved);
        Assert.Empty(scan.Unresolved);
    }

    // ---- Compass words compose with the digits and the longest-match rule ----

    [Theory]
    [InlineData("North One", "N1")]
    [InlineData("North 1", "N1")]
    [InlineData("N1", "N1")]
    [InlineData("North Five", "N5")]
    public void ACompassWordCarriesItsDigit(string spoken, string expected)
    {
        // LEPA has N AND N1..N6. The bare letter must not eat the numbered name.
        Assert.Equal(new[] { expected },
            SayIntentionsClearanceParser.ParseTaxiways($"via {spoken}", LepaTaxiways));
    }

    [Fact]
    public void ANumberedCompassTaxiwayTheAirportLacksIsReportedWhole()
    {
        // LEPA has N and N1, but no N2. Reporting "N" here would name a taxiway the
        // clearance never said, and resolving to N would route the wrong leg.
        var scan = SayIntentionsClearanceParser.ScanTaxiways("via H2, North Two", LepaTaxiways);

        Assert.Equal(new[] { "H2" }, scan.Resolved);
        Assert.Equal(new[] { "N2" }, scan.Unresolved);
    }

    [Fact]
    public void ALongerCompassNameStillBeatsItsPrefix()
    {
        var known = new[] { "N", "E", "NE" };

        Assert.Equal(new[] { "NE" },
            SayIntentionsClearanceParser.ParseTaxiways("via North East", known));
    }

    // ---- Parking names ----

    [Theory]
    [InlineData("Gate A9", "A9")]
    [InlineData("A-9", "A9")]                       // PR #86 truncated this to "A"
    [InlineData("Parking 12L", "12L")]
    [InlineData("A9 - Terminal 1", "A9")]           // a SPACED dash IS a descriptor separator
    [InlineData("Ramp 4", "4")]
    [InlineData("", "")]
    // A leading zero is padding, not identity. Live EDDB: SayIntentions published
    // "Gate B06" while the scenery calls that stand B6 (navdata GB + 6, and
    // LittleNavMapProvider maps the "GB" gate code to "B"). The two never compared
    // equal, the assigned gate could not resolve, and destination resolution fell
    // through to the ARRIVAL RUNWAY — taxi guidance drove a landed aircraft at 24L
    // along the taxiways ATC had given for the gate.
    [InlineData("Gate B06", "B6")]
    [InlineData("B6", "B6")]
    [InlineData("Gate A09", "A9")]
    [InlineData("Parking 041", "41")]
    public void ParkingNamesNormalize(string raw, string expected)
    {
        Assert.Equal(expected, SayIntentionsClearanceParser.NormalizeParkingName(raw));
    }

    // Only LEADING zeros go. B10 must never collapse to B1 — those are two stands,
    // and matching the wrong one is the failure this whole normalization exists to
    // avoid, just pointed the other way.
    [Theory]
    [InlineData("Gate B10", "B10")]
    [InlineData("Gate B1", "B1")]
    [InlineData("Stand 100", "100")]
    [InlineData("Gate 0", "0")]
    [InlineData("Gate B06L", "B6L")]
    public void OnlyLeadingZerosAreStrippedFromAStandNumber(string raw, string expected)
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

// Unit tests for MSFSBlindAssist.Navigation.RerouteOffer — the sentences of "Ask before switching to
// a new route when off route" (UserSettings.TaxiGuidanceConfirmReroute). With it on, a mid-taxi
// recalculation is OFFERED and only adopted when the pilot presses Continue ([ Y): LFPG 2026-11-05,
// a slow hairpin read as off-route and the adopted route opened with a 180° reversal while the pilot
// was following the controller's clearance.

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class RerouteOfferTests
{
    [Fact]
    public void Offer_NamesTheTaxiwaysTheDistanceAndTheKey()
        => Assert.Equal(
            "Off route. New route available via Q, E, G3. 1.2 kilometres to B 13. Press left bracket, Y to accept.",
            RerouteOffer.Compose(new[] { "Q", "E", "G3" }, "1.2 kilometres", "B 13"));

    [Fact]
    public void Offer_WithNoTaxiwayNames_DropsTheViaClause()
        => Assert.Equal(
            "Off route. New route available. 120 metres to Gate A 29. Press left bracket, Y to accept.",
            RerouteOffer.Compose(Array.Empty<string>(), "120 metres", "Gate A 29"));

    // "Route changed" means a route that HAS changed; an offer never says it.
    [Fact]
    public void Offer_NeverClaimsTheRouteChanged()
        => Assert.DoesNotContain("Route changed",
            RerouteOffer.Compose(new[] { "C" }, "900 metres", "Runway 27L"));

    // Leading with "Off route" puts the offer with the safety lines of a position frame, ahead of a
    // turn callout spoken in the same frame (STR-22).
    [Fact]
    public void Offer_IsOrderedAheadOfAnOrdinaryCalloutInTheSameFrame()
    {
        string offer = RerouteOffer.Compose(new[] { "C" }, "900 metres", "Runway 27L");

        string? utterance = TaxiGuidanceManager.ComposeFrameUtterance(new[] { "In 30 metres, slight right.", offer });

        Assert.NotNull(utterance);
        Assert.StartsWith(offer, utterance);
    }

    [Fact]
    public void StatusSuffix_FollowsTheStatusSentenceWithTheSameKey()
    {
        Assert.Equal(" New route offered. Press left bracket, Y to accept.", RerouteOffer.StatusSuffix);
        Assert.EndsWith(RerouteOffer.AcceptHint, RerouteOffer.StatusSuffix);
    }

    [Fact]
    public void NothingToAccept_SaysGuidanceKeepsTheOriginalRoute()
        => Assert.Equal("No new route. Continuing on original route.", RerouteOffer.NothingToAccept);

    // A missed Turn pick is true whether or not the offer is taken, and the offer can name the very
    // taxiways of the route being flown: without the sentence nothing says why the tone points back.
    // The key hint stays the last thing heard.
    [Fact]
    public void Offer_SaysAMissedTurnBeforeTheKeyHint()
        => Assert.Equal(
            "Off route. New route available via C, D. 900 metres to Runway 27L. " +
            "The left turn onto taxiway C was missed. Press left bracket, Y to accept.",
            RerouteOffer.Compose(new[] { "C", "D" }, "900 metres", "Runway 27L",
                new[] { "The left turn onto taxiway C was missed." }));

    [Fact]
    public void Offer_WithNoNotes_IsUnchanged()
        => Assert.Equal(
            RerouteOffer.Compose(new[] { "C" }, "900 metres", "Runway 27L"),
            RerouteOffer.Compose(new[] { "C" }, "900 metres", "Runway 27L", Array.Empty<string>()));

    // The offer stays, and so does the key, while ANY of the detector's three triggers holds. A
    // wrong-way roll trips goingBackward from 10 m behind the segment's start, inside the 25 m route
    // tolerance: withdrawing on the tolerance alone took the offer back one frame after it was spoken.
    [Theory]
    [InlineData(true, false, false, true)]    // back on the line, nothing else wrong: withdrawn
    [InlineData(true, false, true, false)]    // inside the tolerance but rolling the wrong way: stands
    [InlineData(true, true, false, false)]    // inside the next segment's tolerance, far behind this one's start: stands
    [InlineData(false, false, false, false)]  // still off the line: stands
    [InlineData(false, true, true, false)]
    public void BackOnRoute_NeedsEveryOffRouteTriggerGone(
        bool withinTolerance, bool farBehindStart, bool goingBackward, bool expected)
        => Assert.Equal(expected, RerouteOffer.BackOnRoute(withinTolerance, farBehindStart, goingBackward));

    // A paired-runway crossing offer keeps Continue only while it STANDS. Once it is taken, or when
    // none was made, the key takes the reroute offer at once, not when the window would have closed.
    [Fact]
    public void KeyIsFree_OnlyAStandingPairedCrossingOfferKeepsTheKey()
    {
        var now = new DateTime(2026, 11, 5, 19, 47, 0, DateTimeKind.Utc);
        var until = now.AddSeconds(9);

        Assert.False(RerouteOffer.KeyIsFree(pairedCrossingOfferStands: true, now, until));
        Assert.True(RerouteOffer.KeyIsFree(pairedCrossingOfferStands: false, now, until));
        Assert.True(RerouteOffer.KeyIsFree(pairedCrossingOfferStands: true, until.AddSeconds(1), until));
        Assert.True(RerouteOffer.KeyIsFree(pairedCrossingOfferStands: false, now, DateTime.MinValue));
    }

    // Repeat-last (Ctrl+Y) must not replay "Press left bracket, Y to accept." for an offer that is gone.
    [Fact]
    public void InstructionAfterOffer_GoesBackToWhatTheOfferReplaced()
    {
        string offer = RerouteOffer.Compose(new[] { "C" }, "900 metres", "Runway 27L");

        Assert.Equal("In 30 metres, slight right.",
            RerouteOffer.InstructionAfterOffer(offer, offer, "In 30 metres, slight right."));
        Assert.Equal("", RerouteOffer.InstructionAfterOffer(offer, offer, ""));
    }

    [Fact]
    public void InstructionAfterOffer_KeepsAnythingSpokenSinceTheOffer()
    {
        string offer = RerouteOffer.Compose(new[] { "C" }, "900 metres", "Runway 27L");

        Assert.Equal("Left now, taxiway C.",
            RerouteOffer.InstructionAfterOffer("Left now, taxiway C.", offer, "In 30 metres, slight right."));
        Assert.Equal("Left now, taxiway C.",
            RerouteOffer.InstructionAfterOffer("Left now, taxiway C.", null, ""));
    }
}

// The offer as the manager builds it: the distance from TaxiGuidanceManager.FormatDistance, then
// RerouteOffer.Compose. CI runs under en-US, so this runs under de-DE and sv-SE, where a plain
// interpolated number reads "1,2 kilometres". DistanceFormatter's unit is process-wide, so it runs
// in the collection that serialises it.
[Collection("DistanceUnitGlobalState")]
public class RerouteOfferDecimalCultureTests
{
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Offer_ReadsItsDistanceWithADecimalPoint(string culture)
    {
        var saved = DistanceFormatter.UnitProvider;
        try
        {
            DistanceFormatter.UnitProvider = () => DistanceUnit.Metres;
            Assert.Equal(
                "Off route. New route available via Q, E. 2.7 kilometres to B 13. Press left bracket, Y to accept.",
                TestCulture.Run(culture, () => RerouteOffer.Compose(
                    new[] { "Q", "E" }, TaxiGuidanceManager.FormatDistance(2700), "B 13")));
            DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
            Assert.Equal(
                "Off route. New route available via Q, E. 1.5 nautical miles to B 13. Press left bracket, Y to accept.",
                TestCulture.Run(culture, () => RerouteOffer.Compose(
                    new[] { "Q", "E" }, TaxiGuidanceManager.FormatDistance(2700), "B 13")));
        }
        finally
        {
            DistanceFormatter.UnitProvider = saved;
        }
    }
}

// Unit tests for MSFSBlindAssist.Navigation.RerouteOffer — the sentences of "Ask before switching to
// a new route when off route" (UserSettings.TaxiGuidanceConfirmReroute). With it on, a mid-taxi
// recalculation is OFFERED and only adopted when the pilot presses Continue ([ Y): LFPG 2026-11-05,
// a slow hairpin read as off-route and the adopted route opened with a 180° reversal while the pilot
// was following the controller's clearance.

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;

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
}

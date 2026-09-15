// Unit tests for MSFSBlindAssist.Navigation.RouteChangedCallout — the spoken callout for a route the
// off-route detector RECALCULATED mid-taxi. It names the new taxiways AND every runway the new route
// crosses or enters (KSFO 2026-07-01: unexplained hold-short callouts read as a giant loop), built
// from the route's recorded runway events so a crossing that could not be held is named too.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class RouteChangedCalloutTests
{
    private static TaxiRouteRunwayEvent Cross(string d, bool held = true) => new() { Kind = RunwayEventKind.Crossing, Designator = d, Held = held };
    private static TaxiRouteRunwayEvent Enter(string d) => new() { Kind = RunwayEventKind.Entry, Designator = d, Held = true };

    [Fact]
    public void NoRunwayEvents_KeepsTheExistingWordingExactly()
        => Assert.Equal("Route changed. Now via Z, D. 380 metres to Runway 04R.",
            RouteChangedCallout.Compose(new[] { "Z", "D" }, "380 metres", "Runway 04R", Array.Empty<TaxiRouteRunwayEvent>()));

    [Fact]
    public void NoTaxiwayNames_KeepsTheExistingShortForm()
        => Assert.Equal("Route changed. 120 metres to Gate A 29.",
            RouteChangedCallout.Compose(Array.Empty<string>(), "120 metres", "Gate A 29", null));

    // The clause rides with the TAXIWAY list, ahead of the distance: a destination name can carry
    // commas of its own, and truncation by the next immediate callout takes the END of the sentence.
    [Fact]
    public void Crossings_AreNamedWithTheTaxiwaysAheadOfTheDistance()
        => Assert.Equal("Route changed. Now via D, crossing runways 26R and 04L. 2.6 kilometres to Runway 04R.",
            RouteChangedCallout.Compose(new[] { "D" }, "2.6 kilometres", "Runway 04R", new[] { Cross("26R"), Cross("04L") }));

    // Never the bare sentence "Crossing runway 26R." — byte-identical to the live incursion callout.
    [Fact]
    public void Crossings_AttachToRouteChanged_WhenThereAreNoTaxiwayNames()
    {
        string callout = RouteChangedCallout.Compose(Array.Empty<string>(), "900 metres", "Runway 08L", new[] { Cross("26R") });

        Assert.Equal("Route changed, crossing runway 26R. 900 metres to Runway 08L.", callout);
        Assert.DoesNotContain(". Crossing ", callout);
        Assert.DoesNotContain("Crossing runway", callout);
    }

    [Fact]
    public void CommaBearingDestinationName_CannotSwallowTheCrossingClause()
    {
        string callout = RouteChangedCallout.Compose(new[] { "B" }, "400 metres",
            "A 24A - Gate Medium, also A24 (online)", new[] { Cross("09L") });

        Assert.Equal("Route changed. Now via B, crossing runway 09L. 400 metres to A 24A - Gate Medium, also A24 (online).", callout);
        Assert.True(callout.IndexOf("crossing runway 09L", StringComparison.Ordinal) < callout.IndexOf("A 24A", StringComparison.Ordinal));
    }

    [Fact]
    public void An_earlier_crossing_of_the_destination_strip_is_named()
        => Assert.Equal("Route changed. Now via D, crossing runway 04R. 1.2 kilometres to Runway 04R.",
            RouteChangedCallout.Compose(new[] { "D" }, "1.2 kilometres", "Runway 04R", new[] { Cross("04R") }));

    [Fact]
    public void A_crossing_that_could_not_be_held_is_still_named()
        => Assert.Equal("Route changed. Now via D, crossing runway 26R. 900 metres to Gate B 12.",
            RouteChangedCallout.Compose(new[] { "D" }, "900 metres", "Gate B 12", new[] { Cross("26R", held: false) }));

    [Fact]
    public void An_entry_is_named_as_entering()
        => Assert.Equal("Route changed. Now via B, entering runway 01. 300 metres to Gate C 4.",
            RouteChangedCallout.Compose(new[] { "B" }, "300 metres", "Gate C 4", new[] { Enter("01") }));

    [Fact]
    public void OneRunwayCrossedTwice_ReadsAsTwiceNotAsTwoRunways()
    {
        string callout = RouteChangedCallout.Compose(new[] { "Q" }, "1.1 kilometres", "Runway 10R", new[] { Cross("28R"), Cross("10L") });

        Assert.Contains("twice", callout);
        Assert.DoesNotContain("crossing runways", callout);
    }
}

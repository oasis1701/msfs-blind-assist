// Characterization tests for MSFSBlindAssist.Services.TaxiAugment.GateAliasResolver.
//
// No dedicated probe exists; cases derived by reading ResolveAliases (number-match,
// letter-agreement, restatement/dedup, the distance backstop, and the ambiguous-
// concourse guard for letterless gates) and confirmed by running the tests. ParkingSpot
// is a plain POCO here (no DB round-trip needed), so this stays a pure unit test.
//
// This is characterization, not spec verification: if a literal ever disagrees with
// actual output, the test must be corrected to match real output, not the other way
// around.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.TaxiAugment;

namespace MSFSBlindAssist.Tests;

public class GateAliasResolverTests
{
    private static ParkingSpot Gate(string name, int number, string suffix = "", double lat = 0.0, double lon = 0.0)
        => new ParkingSpot { Name = name, Number = number, Suffix = suffix, Latitude = lat, Longitude = lon };

    [Fact]
    public void Null_gate_returns_no_aliases()
    {
        var result = GateAliasResolver.ResolveAliases(null!, new List<(string, double, double)> { ("A51", 0, 0) });

        Assert.Empty(result);
    }

    [Fact]
    public void Null_online_stands_returns_no_aliases()
    {
        var result = GateAliasResolver.ResolveAliases(Gate("N", 3), null!);

        Assert.Empty(result);
    }

    [Fact]
    public void Empty_online_stands_returns_no_aliases()
    {
        var result = GateAliasResolver.ResolveAliases(Gate("N", 3), new List<(string, double, double)>());

        Assert.Empty(result);
    }

    [Fact]
    public void A_gate_with_no_numeric_identity_never_matches()
    {
        var result = GateAliasResolver.ResolveAliases(
            Gate("N", 0), new List<(string, double, double)> { ("N3", 0, 0) });

        Assert.Empty(result);
    }

    [Fact]
    public void An_exact_restatement_of_the_gate_is_not_returned_as_an_alias()
    {
        // Online source calling navdata "N3" by the exact same name is not a new alias.
        var result = GateAliasResolver.ResolveAliases(
            Gate("N", 3), new List<(string, double, double)> { ("N3", 0, 0) });

        Assert.Empty(result);
    }

    [Fact]
    public void A_letter_carrying_gate_accepts_a_same_letter_same_number_suffix_variant()
    {
        var result = GateAliasResolver.ResolveAliases(
            Gate("N", 3), new List<(string, double, double)> { ("N3A", 0, 0) });

        Assert.Equal(new[] { "N3A" }, result);
    }

    [Fact]
    public void A_letter_carrying_gate_rejects_a_different_letter_with_the_same_number()
    {
        var result = GateAliasResolver.ResolveAliases(
            Gate("N", 3), new List<(string, double, double)> { ("A3", 0, 0) });

        Assert.Empty(result);
    }

    [Fact]
    public void A_letterless_gate_accepts_an_online_concourse_prefix()
    {
        var result = GateAliasResolver.ResolveAliases(
            Gate("", 51), new List<(string, double, double)> { ("A51", 0, 0) });

        Assert.Equal(new[] { "A51" }, result);
    }

    [Fact]
    public void A_mismatched_number_is_never_matched()
    {
        var result = GateAliasResolver.ResolveAliases(
            Gate("N", 3), new List<(string, double, double)> { ("N4", 0, 0) });

        Assert.Empty(result);
    }

    [Fact]
    public void A_candidate_with_no_number_is_never_matched()
    {
        var result = GateAliasResolver.ResolveAliases(
            Gate("N", 3), new List<(string, double, double)> { ("HAWKER", 0, 0) });

        Assert.Empty(result);
    }

    [Fact]
    public void A_candidate_farther_than_the_distance_backstop_is_skipped()
    {
        // ~1.1 km apart at the equator (0.01 deg longitude) -- well past the 150 m default.
        var result = GateAliasResolver.ResolveAliases(
            Gate("", 51, lat: 0.0, lon: 0.0),
            new List<(string, double, double)> { ("A51", 0.0, 0.01) });

        Assert.Empty(result);
    }

    [Fact]
    public void Passing_zero_maxMeters_disables_the_distance_backstop()
    {
        var result = GateAliasResolver.ResolveAliases(
            Gate("", 51, lat: 0.0, lon: 0.0),
            new List<(string, double, double)> { ("A51", 0.0, 0.01) },
            maxMeters: 0);

        Assert.Equal(new[] { "A51" }, result);
    }

    [Fact]
    public void Duplicate_online_names_are_deduplicated()
    {
        var result = GateAliasResolver.ResolveAliases(
            Gate("", 51),
            new List<(string, double, double)> { ("A51", 0, 0), ("A51", 0, 0), ("a51", 0, 0) });

        Assert.Equal(new[] { "A51" }, result);
    }

    [Fact]
    public void An_ambiguous_concourse_on_a_letterless_gate_drops_all_lettered_candidates()
    {
        // Two different concourse letters both claim bare gate 51 -- the real concourse is
        // unknown, so adopting either would mislabel the stand; drop the lettered ones.
        var result = GateAliasResolver.ResolveAliases(
            Gate("", 51),
            new List<(string, double, double)> { ("A51", 0, 0), ("B51", 0, 0) });

        Assert.Empty(result);
    }

    [Fact]
    public void An_ambiguous_concourse_still_keeps_a_letterless_suffix_candidate()
    {
        // A MARS-suffix candidate ("51A") carries no concourse letter, so the ambiguity
        // between A51/B51 must not remove it.
        var result = GateAliasResolver.ResolveAliases(
            Gate("", 51),
            new List<(string, double, double)> { ("A51", 0, 0), ("B51", 0, 0), ("51A", 0, 0) });

        Assert.Equal(new[] { "51A" }, result);
    }

    [Fact]
    public void ResolveAliases_is_idempotent()
    {
        var gate = Gate("", 51);
        var stands = new List<(string, double, double)> { ("A51", 0, 0), ("51B", 0, 0) };

        var first = GateAliasResolver.ResolveAliases(gate, stands);
        var second = GateAliasResolver.ResolveAliases(gate, stands);

        Assert.Equal(first, second);
    }
}

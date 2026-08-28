// The gate.select attempt SEQUENCE, testable without a socket.
//
// Live-probed against a running GSX (KATL, PMDG 737, 2026-08-27):
//   gate:" Gate 5" (uiGateName verbatim)          -> not_found
//   gate:"Gate 5"  (trimmed)                      -> not_found
//   gate:"Concourse T (T1-T21) | Gate 5" (uiName) -> not_found
//   gate:"T5" / "T 5" / "5" / "4420"              -> not_found
//   gate:5         (JSON int)                     -> ambiguous + full candidate list
//   gate:"Gate T 5" (bglName from that list)      -> PREPARED
//
// 14 attempts in gsx-gate-select.log before this change: 9 not_found, 5 bad_args,
// 0 prepared. The feature had never once worked.

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxGateSelectPlanTests
{
    // Suffix defaults to EMPTY because Number and Suffix are both StandId.Parse's reading of
    // the SAME uiGateName -- a stand identified as " Gate 5" really does carry Suffix "". The
    // fixture used to plant an unrelated "Z" as noise against a label being rebuilt from
    // Name/Number/Suffix; the unrelated Name still covers that, and a suffixed stand now has
    // its own tests below, because the suffix decides whether the number may be sent at all.
    private static ParkingSpot Spot(string? identifier, string? uiName, int number, string suffix = "")
        => new()
        {
            AirportICAO = "KATL",
            // Deliberately unrelated to the identifier so nothing can pass by coincidence.
            Name = "Totally Unrelated Label",
            Number = number,
            Suffix = suffix,
            GsxIdentifier = identifier,
            GsxUiName = uiName,
            Source = GateSource.Gsx,
        };

    [Fact]
    public void The_first_attempt_is_the_stand_number_as_an_int()
    {
        object? first = GsxGateSelectPlan.FirstAttempt(
            Spot(" Gate 5", "Concourse T (T1-T21) | Gate 5", 5));
        Assert.Equal(5, Assert.IsType<int>(first));
    }

    [Fact]
    public void A_numberless_stand_falls_straight_to_the_verbatim_identifier()
    {
        object? first = GsxGateSelectPlan.FirstAttempt(Spot("Helipad", null, 0));
        Assert.Equal("Helipad", Assert.IsType<string>(first));
    }

    [Fact]
    public void A_stand_with_no_identifier_at_all_has_no_attempt()
    {
        Assert.Null(GsxGateSelectPlan.FirstAttempt(Spot(null, null, 0)));
    }

    [Fact]
    public void A_numbered_stand_with_no_GSX_identifier_still_has_no_attempt()
    {
        // The load-bearing half of the guard. A spot carrying a number but NO GsxIdentifier
        // is a navdata/.ini-sourced spot: only GsxRemoteParkingReader populates the
        // identifier, and CLAUDE.md holds that such a list "cannot be auto-selected --
        // gate.select degrades to BadArgs, i.e. to manual selection, which is the
        // pre-existing baseline and the intended degradation".
        //
        // Sending its number anyway would be the WORST case of the number route: navdata's
        // stand numbering is scenery-authored and disagrees with GSX (46 of 222 KJFK stands),
        // and such a spot has no GsxUiName either -- so if the number happens to resolve
        // uniquely, GSX prepares a stand nothing can check, because
        // ResolvedGateContradictsRequest has no fully-qualified name to compare.
        Assert.Null(GsxGateSelectPlan.FirstAttempt(Spot(null, null, 42)));
        Assert.Null(GsxGateSelectPlan.FirstAttempt(Spot("   ", null, 42)));
    }

    [Fact]
    public void The_fallback_is_the_verbatim_identifier_leading_space_included()
    {
        Assert.Equal(" Gate 5", GsxGateSelectPlan.FallbackAttempt(
            Spot(" Gate 5", "Concourse T (T1-T21) | Gate 5", 5)));
    }

    [Fact]
    public void The_fallback_is_null_when_the_number_was_already_the_only_attempt()
    {
        // A numberless stand's first attempt IS the identifier, so there is nothing left.
        Assert.Null(GsxGateSelectPlan.FallbackAttempt(Spot("Helipad", null, 0)));
    }

    [Fact]
    public void A_SUFFIXED_stand_never_sends_its_base_number()
    {
        // "Gate 12A" parses to Number 12 + Suffix "A", so the number ALONE names a different
        // stand -- and at an airport that also has a plain "Gate 12" the request is wrong by
        // construction, not merely ambiguous. Worse, for a stand GSX publishes no uiName for,
        // the echoed number then matches RequestedNumber and CLEARS
        // ResolvedGateContradictsRequest: GSX prepares "Ramp 5" for a pilot who picked
        // "Ramp 5B" and nothing is spoken. Send the verbatim identifier instead -- which is
        // the pre-existing behaviour, failing loudly with not_found.
        object? first = GsxGateSelectPlan.FirstAttempt(Spot(" Gate 12A", null, 12, "A"));
        Assert.Equal(" Gate 12A", Assert.IsType<string>(first));
    }

    [Fact]
    public void A_SUFFIXED_stand_has_no_fallback_because_the_identifier_already_went_first()
    {
        // The two methods share one predicate so the sequence cannot drift: the fallback
        // offers the identifier exactly when the number went first.
        Assert.Null(GsxGateSelectPlan.FallbackAttempt(Spot(" Gate 12A", null, 12, "A")));
    }

    [Fact]
    public void The_plan_never_rebuilds_a_label_from_our_own_fields()
    {
        var spot = Spot(" Gate 5", "Concourse T (T1-T21) | Gate 5", 5);
        object? first = GsxGateSelectPlan.FirstAttempt(spot);
        string? fallback = GsxGateSelectPlan.FallbackAttempt(spot);
        Assert.NotEqual(spot.Describe(), first?.ToString());
        Assert.NotEqual(spot.Describe(), fallback);
        Assert.NotEqual("Totally Unrelated Label 5", fallback);

        // Same, on the path where a suffix IS present: never "5A", never the label.
        var suffixed = Spot(" Gate 5A", null, 5, "A");
        Assert.NotEqual("5A", GsxGateSelectPlan.FirstAttempt(suffixed)?.ToString());
        Assert.NotEqual(suffixed.Describe(), GsxGateSelectPlan.FirstAttempt(suffixed)?.ToString());
    }
}

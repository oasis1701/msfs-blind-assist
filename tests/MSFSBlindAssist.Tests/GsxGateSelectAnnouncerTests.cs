using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins <see cref="GsxGateSelectAnnouncer.Describe"/> — the mapping from a
/// <see cref="GsxGateSelectResult"/> to the phrase (if any) TaxiAssistForm speaks via the
/// queued announcer. Every result here is built the same way
/// GsxGateSelectResultTests/GsxRemoteGateSelectorTests build theirs: hand-written wire JSON
/// through <see cref="GsxFrame.Parse"/> and <see cref="GsxGateSelectResult.FromFrame"/>, so
/// this file never has to reach into GsxGateSelectResult's private construction.
///
/// See the Spec 2 wiring task: exactly four situations must be announced (too_small,
/// assigned_to_other, ambiguous, a successful revoke-and-reprepare) and everything else must
/// stay silent.
/// </summary>
public class GsxGateSelectAnnouncerTests
{
    private static GsxGateSelectResult Result(string json) =>
        GsxGateSelectResult.FromFrame(GsxFrame.Parse(json));

    private const string PreparedNoWarnings = """
        { "type": "result", "id": "g-1", "ok": true,
          "payload": { "code": "ok", "status": "prepared",
                       "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" },
                       "warnings": [] } }
        """;

    private const string PreparedTooSmall = """
        { "type": "result", "id": "g-1", "ok": true,
          "payload": { "code": "ok", "status": "prepared",
                       "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" },
                       "warnings": ["too_small"] } }
        """;

    private const string PreparedTooSmallNoGateEcho = """
        { "type": "result", "id": "g-1", "ok": true,
          "payload": { "code": "ok", "status": "prepared", "warnings": ["too_small"] } }
        """;

    private const string AssignedToOther = """
        { "type": "result", "id": "g-1", "ok": false,
          "error": { "code": "assigned_to_other",
                     "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" } } }
        """;

    private const string AssignedToOtherNoGateEcho = """
        { "type": "result", "id": "g-1", "ok": false, "error": { "code": "assigned_to_other" } }
        """;

    private const string Ambiguous1 = """
        { "type": "result", "id": "g-1", "ok": false,
          "error": { "code": "ambiguous", "message": "multiple parkings matched",
                     "candidates": [
                       { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" }
                     ] } }
        """;

    private const string Ambiguous4 = """
        { "type": "result", "id": "g-1", "ok": false,
          "error": { "code": "ambiguous", "message": "multiple parkings matched",
                     "candidates": [
                       { "uiName": "Gate A1", "gate": "A1", "number": 1, "bglName": "Parking 1" },
                       { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" },
                       { "uiName": "Gate A120", "gate": "A120", "number": 120, "bglName": "Parking 120" },
                       { "uiName": "Gate A121", "gate": "A121", "number": 121, "bglName": "Parking 121" }
                     ] } }
        """;

    private const string AmbiguousNoCandidates = """
        { "type": "result", "id": "g-1", "ok": false,
          "error": { "code": "ambiguous", "message": "multiple parkings matched" } }
        """;

    private const string AlreadyThere = """
        { "type": "result", "id": "g-1", "ok": false, "error": { "code": "already_parked" } }
        """;

    // ── The four announced outcomes ─────────────────────────────────────────

    [Fact]
    public void Too_small_warning_is_spoken_with_the_resolved_gate_name()
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(Result(PreparedTooSmall));

        Assert.NotNull(phrase);
        Assert.Contains("Gate A12", phrase);
        Assert.Contains("too small", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Too_small_warning_falls_back_to_a_generic_name_when_GSX_echoed_no_gate()
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(Result(PreparedTooSmallNoGateEcho));

        Assert.NotNull(phrase);
        Assert.Contains("too small", phrase, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Gate A12", phrase);
    }

    [Fact]
    public void Assigned_to_other_is_spoken_and_never_mentions_forcing_it()
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(Result(AssignedToOther));

        Assert.NotNull(phrase);
        Assert.Contains("Gate A12", phrase);
        Assert.Contains("occupied", phrase, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("force", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Assigned_to_other_falls_back_to_a_generic_name_when_GSX_echoed_no_gate()
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(Result(AssignedToOtherNoGateEcho));

        Assert.NotNull(phrase);
        Assert.Contains("occupied", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ambiguous_names_every_candidate_when_there_are_few()
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(Result(Ambiguous1));

        Assert.NotNull(phrase);
        Assert.Contains("Gate A12", phrase);
        // No residual-count suffix ("...and N more") when every candidate was named --
        // note the surrounding template text legitimately says "more than one" and "a
        // more specific one" regardless, so the residual suffix itself (", and N more")
        // is what must be absent, not the bare word "more".
        Assert.DoesNotContain(", and", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ambiguous_caps_the_named_candidates_and_reports_a_residual_count()
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(Result(Ambiguous4));

        Assert.NotNull(phrase);
        Assert.Contains("Gate A1,", phrase);
        Assert.Contains("Gate A12,", phrase);
        Assert.Contains("Gate A120", phrase);
        // Fourth candidate's own name is capped out; only the residual count survives.
        Assert.DoesNotContain("Gate A121", phrase);
        Assert.Contains("1 more", phrase);
    }

    [Fact]
    public void Ambiguous_with_no_parseable_candidates_still_announces_something()
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(Result(AmbiguousNoCandidates));

        Assert.NotNull(phrase);
        Assert.Contains("more than one", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_successful_revoke_and_reprepare_is_spoken()
    {
        var result = Result(PreparedNoWarnings);
        result.WasRevokedAndReprepared = true;

        string? phrase = GsxGateSelectAnnouncer.Describe(result);

        Assert.NotNull(phrase);
        Assert.Contains("released", phrase, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("previous stand", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_revoke_and_reprepare_that_also_warns_too_small_speaks_both_facts_once()
    {
        var result = Result(PreparedTooSmall);
        result.WasRevokedAndReprepared = true;

        string? phrase = GsxGateSelectAnnouncer.Describe(result);

        Assert.NotNull(phrase);
        Assert.Contains("released", phrase, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("too small", phrase, StringComparison.OrdinalIgnoreCase);
    }

    // ── Everything else stays silent ────────────────────────────────────────

    [Fact]
    public void A_plain_first_try_prepare_with_no_warnings_says_nothing()
    {
        Assert.Null(GsxGateSelectAnnouncer.Describe(Result(PreparedNoWarnings)));
    }

    [Fact]
    public void Already_there_says_nothing()
    {
        Assert.Null(GsxGateSelectAnnouncer.Describe(Result(AlreadyThere)));
    }

    [Theory]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "not_found" } }""")]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "bad_args" } }""")]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "no_airport" } }""")]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "services_active" } }""")]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "gsx_not_running" } }""")]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "some_future_code" } }""")]
    public void Every_non_announced_failure_code_says_nothing(string json)
    {
        Assert.Null(GsxGateSelectAnnouncer.Describe(Result(json)));
    }

    [Fact]
    public void A_transport_failure_says_nothing()
    {
        Assert.Null(GsxGateSelectAnnouncer.Describe(GsxGateSelectResult.FromFrame(GsxFrame.Parse("not json"))));
    }
}

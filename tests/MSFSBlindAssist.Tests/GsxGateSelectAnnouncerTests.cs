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
/// Every outcome that ENDS the request speaks (prepared, already-there, not-found, bad-args,
/// occupied, ambiguous), plus the two cross-cutting facts (a revoke-and-reprepare, a resolved
/// stand that is not the one requested). The rest — no-airport, a double services_active, an
/// unrecognised code, a transport failure, and the 4.0.8 message the form latches itself —
/// stay silent, and the tests at the bottom pin that.
/// </summary>
public class GsxGateSelectAnnouncerTests
{
    private static GsxGateSelectResult Result(string json) =>
        GsxGateSelectResult.FromFrame(GsxFrame.Parse(json));

    /// <summary>A result as the SELECTOR hands it over: parsed from a frame, then stamped with
    /// the identifier that was actually sent. Nothing else in the app builds one this way, so
    /// tests that care about the requested-vs-resolved comparison must do the same.</summary>
    private static GsxGateSelectResult ResultFor(string json, string requestedIdentifier)
    {
        var result = Result(json);
        result.RequestedIdentifier = requestedIdentifier;
        return result;
    }

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

    private const string AlreadySelectedElsewhere = """
        { "type": "result", "id": "g-1", "ok": false,
          "error": { "code": "already_selected",
                     "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" } } }
        """;

    // ── The positive confirmation ───────────────────────────────────────────

    [Fact]
    public void A_successful_selection_is_confirmed_by_name()
    {
        // Before this integration the old menu-walking selector said "GSX: A 6 selected."
        // Losing it made "GSX prepared your stand" acoustically identical to "GSX is not
        // running" and to "the request timed out" -- and a blind pilot's first evidence
        // either way is the absence of services on arrival.
        string? phrase = GsxGateSelectAnnouncer.Describe(ResultFor(PreparedNoWarnings, "Gate A12"));

        Assert.NotNull(phrase);
        Assert.Contains("prepared", phrase, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gate A12", phrase);
    }

    [Fact]
    public void A_successful_selection_with_no_gate_echo_still_names_what_was_asked_for()
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(
            ResultFor("""{ "type": "result", "id": "g-1", "ok": true, "payload": { "status": "prepared" } }""",
                      "Stand H6"));

        Assert.NotNull(phrase);
        Assert.Contains("Stand H6", phrase);
    }

    // ── The requested-vs-resolved mismatch (C1) ─────────────────────────────

    [Fact]
    public void A_stand_GSX_resolved_elsewhere_is_called_out_by_both_names()
    {
        // GSX's uiGateName -- the only identity field it publishes per parking -- is unique
        // at some airports and not others (98/98 distinct at ENGM; 128 of 231 KJFK stands
        // share one). When GSX can pick between them it either says `ambiguous` or resolves
        // to ONE of them, and in that second case the pilot taxis to a stand GSX did not
        // prepare -- previously in complete silence.
        string? phrase = GsxGateSelectAnnouncer.Describe(ResultFor(PreparedNoWarnings, "Gate B7"));

        Assert.NotNull(phrase);
        Assert.Contains("Gate B7", phrase);    // what the pilot picked
        Assert.Contains("Gate A12", phrase);   // what GSX actually prepared
    }

    [Theory]
    // GSX's own documented shape pairs a full uiName with a bare gate id, so which of the two
    // equals the identifier we sent depends on GSX's spelling, not on whether it picked the
    // right stand. Matching EITHER has to clear the check.
    [InlineData("Gate A12")]
    [InlineData("A12")]
    // Trimmed and case-insensitive, for the same reason: a spelling difference is not a
    // different stand.
    [InlineData("  gate a12  ")]
    public void An_echo_that_answers_to_what_was_sent_is_not_a_mismatch(string requested)
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(ResultFor(PreparedNoWarnings, requested));

        Assert.NotNull(phrase);
        Assert.DoesNotContain("Careful", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    // An echo we cannot interpret is NOT a mismatch -- a false alarm here teaches the pilot to
    // ignore the real one. No gate object at all...
    [InlineData("""{ "type": "result", "id": "g-1", "ok": true, "payload": { "status": "prepared" } }""")]
    // ...and one whose identity strings are both blank.
    [InlineData("""
        { "type": "result", "id": "g-1", "ok": true,
          "payload": { "status": "prepared", "gate": { "uiName": "", "gate": "", "number": 12 } } }
        """)]
    public void An_uninterpretable_echo_never_cries_wolf(string json)
    {
        var result = ResultFor(json, "Gate B7");

        Assert.False(result.ResolvedGateContradictsRequest);
        Assert.DoesNotContain("Careful", GsxGateSelectAnnouncer.Describe(result)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_result_that_never_reached_the_selector_is_never_a_mismatch()
    {
        // FromFrame alone cannot know what was requested -- only the selector stamps that --
        // so an unstamped result must compare as "nothing to say", not as a contradiction.
        Assert.False(Result(PreparedNoWarnings).ResolvedGateContradictsRequest);
    }

    // ── The remaining announced outcomes ────────────────────────────────────

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

    [Fact]
    public void Already_there_is_spoken_rather_than_left_acoustically_blank()
    {
        // The guide's "nothing to do" is about not RETRYING. The pilot still pressed
        // Calculate, and this is one of the four ways that ends.
        string? phrase = GsxGateSelectAnnouncer.Describe(ResultFor(AlreadyThere, "Gate A12"));

        Assert.NotNull(phrase);
        Assert.Contains("already", phrase, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gate A12", phrase);
    }

    [Fact]
    public void Already_selected_at_a_DIFFERENT_stand_names_both()
    {
        // already_selected fires when the pilot asked for a stand GSX is NOT set up at, and
        // error.gate is the only thing naming the one it means. Silent, this is the same
        // failure as C1 by another route: the pilot taxis to their pick while GSX is
        // committed elsewhere. The phrase must never imply GSX moved to their pick.
        string? phrase = GsxGateSelectAnnouncer.Describe(ResultFor(AlreadySelectedElsewhere, "Gate B7"));

        Assert.NotNull(phrase);
        Assert.Contains("Gate B7", phrase);
        Assert.Contains("Gate A12", phrase);
        Assert.DoesNotContain("prepared", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Not_found_says_so_and_names_the_stand_that_was_asked_for()
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(
            ResultFor("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "not_found" } }""",
                      "Gate B7"));

        Assert.NotNull(phrase);
        Assert.Contains("Gate B7", phrase);
        Assert.Contains("no stand was prepared", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bad_args_says_no_stand_was_prepared()
    {
        string? phrase = GsxGateSelectAnnouncer.Describe(
            Result("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "bad_args" } }"""));

        Assert.NotNull(phrase);
        Assert.Contains("could not prepare", phrase, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_locally_decided_bad_args_speaks_too()
    {
        // Reached without sending anything, when the chosen spot has no GsxIdentifier -- i.e.
        // whenever the gate list came from the .ini/navdata fallback instead of the Remote
        // API. It is still "GSX prepared nothing", which is the half the pilot must hear.
        string? phrase = GsxGateSelectAnnouncer.Describe(
            GsxGateSelectResult.Local(GsxGateSelectOutcome.BadArgs, "No GSX identifier is available for this spot."));

        Assert.NotNull(phrase);
        Assert.Contains("could not prepare", phrase, StringComparison.OrdinalIgnoreCase);
    }

    // ── Everything else stays silent ────────────────────────────────────────

    [Theory]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "no_airport" } }""")]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "services_active" } }""")]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "some_future_code" } }""")]
    // The two codes Task 7 Part C gave their own outcomes. They were silent when they
    // pooled into Unavailable and must stay silent now that they don't: neither is a
    // fact a pilot can act on mid-flight, and "GSX is unreachable" already has its own
    // surface in GsxService.UnavailableReason. This is the concrete demonstration that
    // adding an outcome member does NOT accidentally give it a voice.
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "gsx_not_running" } }""")]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "auth_required" } }""")]
    public void Every_non_announced_failure_code_says_nothing(string json)
    {
        Assert.Null(GsxGateSelectAnnouncer.Describe(Result(json)));
    }

    [Fact]
    public void A_transport_failure_says_nothing()
    {
        Assert.Null(GsxGateSelectAnnouncer.Describe(GsxGateSelectResult.FromFrame(GsxFrame.Parse("not json"))));
    }

    // ── The pilot hears the STAND, never the wire value ─────────────────────

    [Fact]
    public void The_spoken_label_outranks_the_wire_identifier_on_a_mismatch()
    {
        // What goes in gate.select's `gate` argument is now a bare stand NUMBER, so
        // RequestedIdentifier renders as "5" -- and "you selected 5" is not something a
        // blind pilot can act on. RequestedLabel carries the stand as their dropdown
        // showed it, and every phrase that names the pilot's own pick must prefer it.
        var result = ResultFor(PreparedNoWarnings, "5");
        result.RequestedLabel = "B 25 - Gate Medium";

        string? phrase = GsxGateSelectAnnouncer.Describe(result);

        Assert.NotNull(phrase);
        Assert.Contains("B 25 - Gate Medium", phrase);
        Assert.DoesNotContain("selected 5,", phrase);
        Assert.Contains("Gate A12", phrase);   // still names what GSX actually prepared
    }

    [Theory]
    // Every branch that names the pilot's own pick, not just the mismatch one.
    [InlineData("""{ "type": "result", "id": "g-1", "ok": false, "error": { "code": "not_found" } }""")]
    [InlineData(AlreadyThere)]
    [InlineData("""{ "type": "result", "id": "g-1", "ok": true, "payload": { "status": "prepared" } }""")]
    public void Every_branch_that_names_the_request_uses_the_spoken_label(string json)
    {
        var result = ResultFor(json, "5");
        result.RequestedLabel = "B 25 - Gate Medium";

        string? phrase = GsxGateSelectAnnouncer.Describe(result);

        Assert.NotNull(phrase);
        Assert.Contains("B 25 - Gate Medium", phrase);
    }

    [Fact]
    public void Without_a_spoken_label_the_wire_identifier_is_still_used()
    {
        // A result built by anything other than the selector carries no label. Naming the
        // identifier is still far better than a bare "the stand", so the chain degrades
        // rather than dropping to the generic fallback.
        string? phrase = GsxGateSelectAnnouncer.Describe(ResultFor(PreparedNoWarnings, "Gate B7"));

        Assert.NotNull(phrase);
        Assert.Contains("Gate B7", phrase);
    }

    [Fact]
    public void A_blank_spoken_label_falls_through_rather_than_speaking_nothing()
    {
        var result = ResultFor(PreparedNoWarnings, "Gate B7");
        result.RequestedLabel = "   ";

        string? phrase = GsxGateSelectAnnouncer.Describe(result);

        Assert.NotNull(phrase);
        Assert.Contains("Gate B7", phrase);
    }

    // ── The 4.0.8 message: a constant, deliberately outside Describe ─────────

    [Fact]
    public void GateSelectUnsupported_says_nothing_through_Describe_the_form_owns_the_latch()
    {
        // Describe must stay silent here even though this IS a spoken condition: the
        // message is once-per-dialog, and a once-only latch cannot live in a stateless
        // mapper. TaxiAssistForm reads the outcome and speaks the constant itself.
        // Returning it here would repeat it on every gate-destination Calculate.
        var result = GsxGateSelectResult.Local(
            GsxGateSelectOutcome.GateSelectUnsupported, "GSX does not advertise gate.select");

        Assert.Null(GsxGateSelectAnnouncer.Describe(result));
    }

    [Fact]
    public void The_unsupported_message_names_4_0_8_and_tells_the_pilot_what_to_do()
    {
        string m = GsxGateSelectAnnouncer.GateSelectUnsupportedMessage;

        // 4.0.8, not 4.0.1: the Remote API shipped in 4.0.1 but gate.select did not,
        // so a pilot sent to 4.0.1 would land on a build where this still does nothing.
        Assert.Contains("4.0.8", m);
        Assert.DoesNotContain("4.0.1", m);
        // The fallback action, or the pilot is left with a version number and no move.
        Assert.Contains("GSX menu", m);
        // It must NOT claim GSX is broken or absent -- on this path GSX is running and
        // answering, and everything else in Access GSX works normally.
        Assert.DoesNotContain("not running", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unavailable", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not reachable", m, StringComparison.OrdinalIgnoreCase);
    }
}

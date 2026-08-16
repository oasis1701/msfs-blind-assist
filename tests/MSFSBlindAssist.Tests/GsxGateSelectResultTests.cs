using System.Text.Json;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the parsing of GSX's <c>gate.select</c> result — see
/// docs/superpowers/specs/2026-08-12-gsx-remote-api-gate-list-and-selection-design.md
/// §"GsxRemoteGateSelector.cs — REVISED" and the vendor guide §8.14.
///
/// Every wire shape here is HAND-WRITTEN from the guide, not a live capture — no
/// `gate.select` response has been captured against a running GSX yet. Do not
/// mistake these literals for characterized wire data the way the other
/// Fixtures/*.json files are.
/// </summary>
public class GsxGateSelectResultTests
{
    private static GsxFrame Frame(string json) => GsxFrame.Parse(json);

    // ── Success ──────────────────────────────────────────────────────────────

    [Fact]
    public void Success_payload_maps_to_Prepared_with_the_echoed_gate()
    {
        var frame = Frame("""
            { "type": "result", "id": "g-1", "ok": true,
              "payload": { "code": "ok", "status": "prepared",
                           "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" },
                           "warnings": [] } }
            """);

        var result = GsxGateSelectResult.FromFrame(frame);

        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.NotNull(result.ResolvedGate);
        Assert.Equal("Gate A12", result.ResolvedGate!.UiName);
        Assert.Equal("A12", result.ResolvedGate.Gate);
        Assert.Equal(12, result.ResolvedGate.Number);
        Assert.Equal("Parking 12", result.ResolvedGate.BglName);
        Assert.Empty(result.Warnings);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public void Success_with_too_small_warning_is_still_Prepared_and_the_warning_survives()
    {
        var frame = Frame("""
            { "type": "result", "id": "g-1", "ok": true,
              "payload": { "code": "ok", "status": "prepared",
                           "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" },
                           "warnings": ["too_small"] } }
            """);

        var result = GsxGateSelectResult.FromFrame(frame);

        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.Equal("Gate A12", result.ResolvedGate!.UiName);
        Assert.Single(result.Warnings);
        Assert.Contains("too_small", result.Warnings);
    }

    // ── Ambiguous / candidates ──────────────────────────────────────────────

    [Fact]
    public void Ambiguous_surfaces_the_candidate_list()
    {
        var frame = Frame("""
            { "type": "result", "id": "g-2", "ok": false,
              "error": { "code": "ambiguous", "message": "multiple parkings matched",
                         "candidates": [
                           { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" },
                           { "uiName": "Gate A120", "gate": "A120", "number": 120, "bglName": "Parking 120" }
                         ] } }
            """);

        var result = GsxGateSelectResult.FromFrame(frame);

        Assert.Equal(GsxGateSelectOutcome.Ambiguous, result.Outcome);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal("Gate A12", result.Candidates[0].UiName);
        Assert.Equal(12, result.Candidates[0].Number);
        Assert.Equal("Gate A120", result.Candidates[1].UiName);
        Assert.Equal(120, result.Candidates[1].Number);
        Assert.Equal("multiple parkings matched", result.Message);
        Assert.Null(result.ResolvedGate);
    }

    [Fact]
    public void Ambiguous_with_a_malformed_candidate_entry_skips_it_rather_than_throwing()
    {
        var frame = Frame("""
            { "type": "result", "id": "g-2", "ok": false,
              "error": { "code": "ambiguous",
                         "candidates": [
                           "not an object",
                           { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" }
                         ] } }
            """);

        var result = GsxGateSelectResult.FromFrame(frame);

        Assert.Equal(GsxGateSelectOutcome.Ambiguous, result.Outcome);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("Gate A12", candidate.UiName);
    }

    [Fact]
    public void Candidate_missing_number_leaves_it_null_rather_than_zero()
    {
        var frame = Frame("""
            { "type": "result", "id": "g-2", "ok": false,
              "error": { "code": "ambiguous",
                         "candidates": [ { "uiName": "Gate A12", "gate": "A12", "bglName": "Parking 12" } ] } }
            """);

        var candidate = Assert.Single(GsxGateSelectResult.FromFrame(frame).Candidates);
        Assert.Null(candidate.Number);
    }

    [Fact]
    public void Candidate_number_of_the_wrong_json_kind_degrades_to_null_not_a_throw()
    {
        var frame = Frame("""
            { "type": "result", "id": "g-2", "ok": false,
              "error": { "code": "ambiguous",
                         "candidates": [ { "uiName": "Gate A12", "gate": "A12", "number": "twelve", "bglName": "Parking 12" } ] } }
            """);

        var candidate = Assert.Single(GsxGateSelectResult.FromFrame(frame).Candidates);
        Assert.Null(candidate.Number);
    }

    // ── Each documented error code → its enum value ─────────────────────────

    [Fact]
    public void Not_found_maps_to_NotFound()
    {
        var frame = Frame("""{"type":"result","id":"g-3","ok":false,"error":{"code":"not_found","message":"no parking matched \"ZZZ\""}}""");
        var result = GsxGateSelectResult.FromFrame(frame);
        Assert.Equal(GsxGateSelectOutcome.NotFound, result.Outcome);
        Assert.Equal("no parking matched \"ZZZ\"", result.Message);
        Assert.Equal("not_found", result.RawCode);
    }

    [Fact]
    public void Already_parked_maps_to_AlreadyThere()
    {
        var frame = Frame("""{"type":"result","id":"g-4","ok":false,"error":{"code":"already_parked"}}""");
        Assert.Equal(GsxGateSelectOutcome.AlreadyThere, GsxGateSelectResult.FromFrame(frame).Outcome);
    }

    [Fact]
    public void Already_selected_also_maps_to_AlreadyThere()
    {
        // The guide says "nothing to do" for both already_parked and
        // already_selected — the caller must not have to distinguish them.
        var frame = Frame("""{"type":"result","id":"g-5","ok":false,"error":{"code":"already_selected"}}""");
        Assert.Equal(GsxGateSelectOutcome.AlreadyThere, GsxGateSelectResult.FromFrame(frame).Outcome);
    }

    [Fact]
    public void Already_there_still_echoes_the_gate_when_the_wire_sends_one()
    {
        // "Nothing to do" (no retry needed) is not the same as "nothing useful in
        // the payload" — the guide's own assignGate example reads error.gate for
        // already_parked/already_selected too, same as assigned_to_other.
        // already_selected in particular can fire when the pilot asked for a
        // DIFFERENT stand from the one already prepared, and error.gate is the
        // only way to tell them which stand that actually is.
        var frame = Frame("""
            { "type": "result", "id": "g-4", "ok": false,
              "error": { "code": "already_selected",
                         "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" } } }
            """);

        var result = GsxGateSelectResult.FromFrame(frame);

        Assert.Equal(GsxGateSelectOutcome.AlreadyThere, result.Outcome);
        Assert.NotNull(result.ResolvedGate);
        Assert.Equal("Gate A12", result.ResolvedGate!.UiName);
    }

    [Fact]
    public void Already_there_leaves_ResolvedGate_null_when_the_wire_sends_no_gate()
    {
        var frame = Frame("""{"type":"result","id":"g-4","ok":false,"error":{"code":"already_parked"}}""");

        var result = GsxGateSelectResult.FromFrame(frame);

        Assert.Equal(GsxGateSelectOutcome.AlreadyThere, result.Outcome);
        Assert.Null(result.ResolvedGate);
    }

    [Fact]
    public void Services_active_maps_to_ServicesActive()
    {
        var frame = Frame("""{"type":"result","id":"g-6","ok":false,"error":{"code":"services_active"}}""");
        var result = GsxGateSelectResult.FromFrame(frame);
        Assert.Equal(GsxGateSelectOutcome.ServicesActive, result.Outcome);
        Assert.Equal("services_active", result.RawCode);
    }

    [Fact]
    public void Assigned_to_other_maps_to_AssignedToOther_and_echoes_the_occupied_gate()
    {
        var frame = Frame("""
            { "type": "result", "id": "g-7", "ok": false,
              "error": { "code": "assigned_to_other",
                         "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" } } }
            """);

        var result = GsxGateSelectResult.FromFrame(frame);

        Assert.Equal(GsxGateSelectOutcome.AssignedToOther, result.Outcome);
        Assert.NotNull(result.ResolvedGate);
        Assert.Equal("Gate A12", result.ResolvedGate!.UiName);
    }

    [Fact]
    public void No_airport_maps_to_NoAirport()
    {
        var frame = Frame("""{"type":"result","id":"g-8","ok":false,"error":{"code":"no_airport"}}""");
        Assert.Equal(GsxGateSelectOutcome.NoAirport, GsxGateSelectResult.FromFrame(frame).Outcome);
    }

    [Fact]
    public void Bad_args_maps_to_BadArgs()
    {
        var frame = Frame("""{"type":"result","id":"g-9","ok":false,"error":{"code":"bad_args","message":"gate missing"}}""");
        var result = GsxGateSelectResult.FromFrame(frame);
        Assert.Equal(GsxGateSelectOutcome.BadArgs, result.Outcome);
        Assert.Equal("gate missing", result.Message);
    }

    // ── Unrecognised codes → Unavailable, but diagnosable ────────────────────

    [Fact]
    public void An_unrecognised_future_code_maps_to_Unavailable_but_preserves_it_in_RawCode()
    {
        var frame = Frame("""{"type":"result","id":"g-10","ok":false,"error":{"code":"weather_hold","message":"future GSX behaviour"}}""");
        var result = GsxGateSelectResult.FromFrame(frame);
        Assert.Equal(GsxGateSelectOutcome.Unavailable, result.Outcome);
        Assert.Equal("weather_hold", result.RawCode);
        Assert.Equal("future GSX behaviour", result.Message);
    }

    [Fact]
    public void The_remaining_generic_transport_codes_still_map_to_Unavailable_with_RawCode_preserved()
    {
        // unknown_verb / internal are generic protocol codes the transport can surface
        // on any verb, and nothing distinguishes what a reader should DO about them, so
        // they stay pooled under Unavailable with the raw string preserved. (gsx_not_running
        // and auth_required were split out of this pool — see the two tests below.)
        var frame = Frame("""{"type":"result","id":"g-11","ok":false,"error":{"code":"unknown_verb"}}""");
        var result = GsxGateSelectResult.FromFrame(frame);
        Assert.Equal(GsxGateSelectOutcome.Unavailable, result.Outcome);
        Assert.Equal("unknown_verb", result.RawCode);
    }

    // ── The two named transport codes (Task 7 Part C) ────────────────────────
    // Both were previously pooled into Unavailable. Neither is spoken; they are named
    // so gsx-gate-select.log — the documented first stop for "gate not found" — says
    // WHICH of three very different things happened, instead of one word for all.

    [Fact]
    public void Gsx_not_running_gets_its_own_outcome_and_still_preserves_the_raw_code()
    {
        var frame = Frame("""{"type":"result","id":"g-11","ok":false,"error":{"code":"gsx_not_running","message":"Couatl is not running"}}""");
        var result = GsxGateSelectResult.FromFrame(frame);
        Assert.Equal(GsxGateSelectOutcome.GsxNotRunning, result.Outcome);
        Assert.Equal("gsx_not_running", result.RawCode);
        Assert.Equal("Couatl is not running", result.Message);
    }

    [Fact]
    public void Auth_required_gets_its_own_outcome_and_still_preserves_the_raw_code()
    {
        // Should never occur — authRequired is false on every captured hello and the
        // socket is localhost-only. Named precisely so that if it ever does, the log
        // says so rather than leaving the reader to guess behind "Unavailable".
        var frame = Frame("""{"type":"result","id":"g-12","ok":false,"error":{"code":"auth_required"}}""");
        var result = GsxGateSelectResult.FromFrame(frame);
        Assert.Equal(GsxGateSelectOutcome.AuthRequired, result.Outcome);
        Assert.Equal("auth_required", result.RawCode);
    }

    // ── Malformed / non-result frames → TransportFailure, never a throw ─────

    [Fact]
    public void A_non_result_frame_maps_to_TransportFailure()
    {
        var frame = Frame("""{"type":"event","topic":"engine","gsxRunning":true}""");
        Assert.Equal(GsxGateSelectOutcome.TransportFailure, GsxGateSelectResult.FromFrame(frame).Outcome);
    }

    [Fact]
    public void Unparseable_json_becomes_an_Unknown_frame_which_maps_to_TransportFailure()
    {
        var frame = Frame("not json at all");
        Assert.Equal(GsxGateSelectOutcome.TransportFailure, GsxGateSelectResult.FromFrame(frame).Outcome);
    }

    [Fact]
    public void A_null_frame_reference_maps_to_TransportFailure_rather_than_throwing()
    {
        var result = GsxGateSelectResult.FromFrame(null!);
        Assert.Equal(GsxGateSelectOutcome.TransportFailure, result.Outcome);
    }

    // ── Graceful degradation on a well-formed but oddly-shaped result frame ─

    [Fact]
    public void Ok_true_with_no_payload_at_all_still_reads_as_Prepared_with_nothing_echoed()
    {
        var frame = Frame("""{"type":"result","id":"g-1","ok":true}""");
        var result = GsxGateSelectResult.FromFrame(frame);
        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.Null(result.ResolvedGate);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Ok_true_with_a_non_object_payload_does_not_throw()
    {
        var frame = Frame("""{"type":"result","id":"g-1","ok":true,"payload":"unexpected"}""");
        var result = GsxGateSelectResult.FromFrame(frame);
        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.Null(result.ResolvedGate);
    }

    [Fact]
    public void Ok_false_with_no_error_object_at_all_degrades_to_Unavailable_with_no_RawCode_to_preserve()
    {
        var frame = Frame("""{"type":"result","id":"g-1","ok":false}""");
        var result = GsxGateSelectResult.FromFrame(frame);
        Assert.Equal(GsxGateSelectOutcome.Unavailable, result.Outcome);
        Assert.Null(result.RawCode);
    }

    [Fact]
    public void A_gate_field_of_the_wrong_json_kind_leaves_ResolvedGate_null_not_a_blank_candidate()
    {
        // A non-object "gate" must not silently become a candidate with every
        // field defaulted to "" — that reads to a caller as GSX having echoed
        // an (empty) stand rather than having echoed nothing at all.
        var success = Frame("""{"type":"result","id":"g-1","ok":true,"payload":{"code":"ok","status":"prepared","gate":"oops"}}""");
        Assert.Null(GsxGateSelectResult.FromFrame(success).ResolvedGate);

        var assignedToOther = Frame("""{"type":"result","id":"g-7","ok":false,"error":{"code":"assigned_to_other","gate":42}}""");
        Assert.Null(GsxGateSelectResult.FromFrame(assignedToOther).ResolvedGate);
    }
}

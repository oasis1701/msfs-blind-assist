using System.Text.Json;
using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins <see cref="GsxRemoteGateSelector"/>'s decision logic — the request builder plus
/// Task 1's interpreter — entirely without a socket, via an injected send delegate and an
/// injected capability set. See
/// docs/superpowers/specs/2026-08-12-gsx-remote-api-gate-list-and-selection-design.md
/// §"GsxRemoteGateSelector.cs — REVISED".
///
/// Every wire shape here is hand-written from the guide, same caveat as
/// GsxGateSelectResultTests: no live gate.select capture exists yet.
/// </summary>
public class GsxRemoteGateSelectorTests
{
    // ── Fixtures ────────────────────────────────────────────────────────────

    /// <summary>
    /// A GSX-sourced spot whose Name/Number are DELIBERATELY unrelated to
    /// <paramref name="identifier"/>, so a test asserting the sent value equals
    /// <paramref name="identifier"/> cannot pass by accident of the two strings
    /// coinciding — it only passes if the code actually reads GsxIdentifier and
    /// never rebuilds a label from Describe()/Name/Number.
    /// <para>Suffix is EMPTY, and that is not noise: Number and Suffix are both
    /// StandId.Parse's reading of the same uiGateName, and a SUFFIXED stand does not send
    /// its base number at all (GsxGateSelectPlan) — planting an unrelated suffix here would
    /// model a stand that cannot exist and would silently move every test in this file onto
    /// the identifier rung. The suffix rule has its own tests in GsxGateSelectPlanTests.</para>
    /// </summary>
    private static ParkingSpot SpotWithIdentifier(string? identifier) => new()
    {
        Name = "Totally Unrelated Label",
        Number = 999,
        Suffix = "",
        Source = GateSource.Gsx,
        GsxIdentifier = identifier,
    };

    private static readonly Func<IReadOnlyCollection<string>> HasGateCapability = () => new[] { "gate", "handlerData" };
    private static readonly Func<IReadOnlyCollection<string>> NoGateCapability = () => new[] { "handlerData" };

    private static GsxFrame PreparedFrame() => GsxFrame.Parse("""
        { "type": "result", "id": "g-1", "ok": true,
          "payload": { "code": "ok", "status": "prepared",
                       "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" },
                       "warnings": [] } }
        """);

    private static GsxFrame PreparedFrameWithTooSmallWarning() => GsxFrame.Parse("""
        { "type": "result", "id": "g-1", "ok": true,
          "payload": { "code": "ok", "status": "prepared",
                       "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" },
                       "warnings": ["too_small"] } }
        """);

    private static GsxFrame ServicesActiveFrame() => GsxFrame.Parse("""
        { "type": "result", "id": "g-1", "ok": false,
          "error": { "code": "services_active", "message": "GSX is committed at a gate" } }
        """);

    private static GsxFrame AssignedToOtherFrame() => GsxFrame.Parse("""
        { "type": "result", "id": "g-1", "ok": false,
          "error": { "code": "assigned_to_other",
                     "gate": { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" } } }
        """);

    private static GsxFrame AmbiguousFrame() => GsxFrame.Parse("""
        { "type": "result", "id": "g-1", "ok": false,
          "error": { "code": "ambiguous", "message": "multiple parkings matched",
                     "candidates": [
                       { "uiName": "Gate A12", "gate": "A12", "number": 12, "bglName": "Parking 12" },
                       { "uiName": "Gate A120", "gate": "A120", "number": 120, "bglName": "Parking 120" }
                     ] } }
        """);

    private static GsxFrame AlreadyParkedFrame() => GsxFrame.Parse("""
        { "type": "result", "id": "g-1", "ok": false, "error": { "code": "already_parked" } }
        """);

    private static JsonElement ToJson(object? value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    private static string? ExtractString(object? args, string prop)
    {
        var e = ToJson(args);
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
    }

    private static bool ExtractBool(object? args, string prop)
    {
        var e = ToJson(args);
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.True;
    }

    private static int? ExtractInt(object? args, string prop)
    {
        var e = ToJson(args);
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v)
               && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)
            ? i : null;
    }

    /// <summary>The `gate` argument as text, whether it went as a JSON int or a string.</summary>
    private static string? ExtractGateAsText(object? args)
    {
        var e = ToJson(args);
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("gate", out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    private static GsxFrame AmbiguousGate5Frame() => GsxFrame.Parse("""
        { "type": "result", "id": "g-1", "ok": false,
          "error": { "code": "ambiguous", "message": "multiple parkings match '5'",
                     "candidates": [
                       { "uiName": "Concourse T (T1-T21) | Gate 5", "gate": " Gate 5", "number": 5, "bglName": "Gate T 5" },
                       { "uiName": "Delta Tech Ops (E1-21) | Gate 5", "gate": " Gate 5", "number": 5, "bglName": "Gate E 5" } ] } }
        """);

    private static GsxFrame PreparedGateT5Frame() => GsxFrame.Parse("""
        { "type": "result", "id": "g-2", "ok": true,
          "payload": { "code": "ok", "status": "prepared",
                       "gate": { "uiName": "Concourse T (T1-T21) | Gate 5", "gate": " Gate 5", "number": 5, "bglName": "Gate T 5" },
                       "warnings": [] } }
        """);

    private static GsxFrame NotFoundFrame() => GsxFrame.Parse("""
        { "type": "result", "id": "g-1", "ok": false,
          "error": { "code": "not_found", "message": "no parking matches" } }
        """);

    /// <summary>The KATL Concourse T stand, with GSX's own strings including their spaces.</summary>
    private static ParkingSpot KatlT5() => new()
    {
        Name = "Totally Unrelated Label",
        Number = 5,
        Suffix = "",               // StandId.Parse(" Gate 5") -- no suffix on this stand
        Source = GateSource.Gsx,
        GsxIdentifier = " Gate 5",
        GsxUiName = "Concourse T (T1-T21) | Gate 5",
    };

    /// <summary>
    /// A KATL GA ramp, as the committed handlerData fixture publishes it: GSX gives it a
    /// <c>uiGateName</c> ("Ramp 1") but NO <c>uiName</c> at all — 3 of that fixture's 8
    /// stands, 13 of the airport's 294. So <c>ExpectedUiName</c> is null, the
    /// fully-qualified comparison cannot run, and the echoed NUMBER is the only identity
    /// left that can prove GSX resolved the stand we asked for.
    /// </summary>
    private static ParkingSpot KatlRamp1() => new()
    {
        Name = "",                 // StandId.Parse("Ramp 1") drops the stand-type word
        Number = 1,
        Suffix = "",
        Type = 2,                  // Ramp GA
        Source = GateSource.Gsx,
        GsxIdentifier = "Ramp 1",
        GsxUiName = null,
    };

    private static GsxFrame PreparedRamp1Frame() => GsxFrame.Parse("""
        { "type": "result", "id": "g-2", "ok": true,
          "payload": { "code": "ok", "status": "prepared",
                       "gate": { "uiName": "", "gate": "Ramp 1", "number": 1, "bglName": "Ramp 1" },
                       "warnings": [] } }
        """);

    // ── Capability gate ─────────────────────────────────────────────────────

    [Fact]
    public async Task Capability_absent_returns_GateSelectUnsupported_and_never_sends_the_verb()
    {
        // A NON-EMPTY capability list that lacks 'gate' is positive evidence: GSX said
        // hello, listed what it can do, and gate.select was not on the list -- i.e. a
        // connected build older than 4.0.8. That is the ONE case where naming a version
        // to the pilot is truthful, so it gets its own outcome (TaxiAssistForm speaks it
        // once per dialog).
        bool sent = false;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sent = true; return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            NoGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.False(sent);
        Assert.Equal(GsxGateSelectOutcome.GateSelectUnsupported, result.Outcome);
    }

    [Fact]
    public async Task An_empty_capability_set_is_Unavailable_not_GateSelectUnsupported()
    {
        // Knowing NOTHING about GSX (no hello yet / Remote API not connected / GSX not
        // running) is not the same as knowing GSX is too old, and must never be reported
        // as such: it would tell a pilot whose GSX merely isn't running to go and install
        // a version they may already have. Empty stays Unavailable, which is silent.
        bool sent = false;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sent = true; return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            () => Array.Empty<string>());

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.False(sent);
        Assert.Equal(GsxGateSelectOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task Capability_check_is_an_exact_token_match_not_a_substring()
    {
        // "gates" is not "gate" -- the capability list is a set of discrete tokens,
        // not a blob to substring-search. A loose match here would let the verb be
        // sent to a GSX build that never advertised gate.select at all.
        bool sent = false;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sent = true; return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            () => new[] { "gates", "handlerDataX" });

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.False(sent);
        Assert.Equal(GsxGateSelectOutcome.GateSelectUnsupported, result.Outcome);
    }

    [Fact]
    public async Task A_capabilities_provider_that_throws_is_treated_as_nothing_known_not_a_throw()
    {
        // SelectGateAsync's own contract is "never throws" -- that must hold even
        // against a misbehaving caller-supplied capabilities delegate, the same way
        // it already holds against a misbehaving send delegate. A throw tells us
        // nothing about GSX's version, so it lands with the empty case (Unavailable,
        // silent) and never claims the build is too old.
        bool sent = false;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sent = true; return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            () => throw new InvalidOperationException("capabilities not ready"));

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.False(sent);
        Assert.Equal(GsxGateSelectOutcome.Unavailable, result.Outcome);
    }

    // ── Identifier: GSX's own value, never Describe() ───────────────────────

    // REPLACES Happy_path_sends_gate_select_with_the_raw_identifier_verbatim.
    // gate.select does not answer to uiGateName -- live-probed 2026-08-27 at KATL, it
    // returns not_found for the verbatim identifier, the trimmed form and uiName alike.
    // The number goes first. SpotWithIdentifier's Number is 999, deliberately unrelated to
    // "Gate A12", so this cannot pass by the two coinciding.
    [Fact]
    public async Task Happy_path_sends_the_stand_number_as_a_json_int()
    {
        string? sentVerb = null;
        object? sentArgs = null;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sentVerb = verb; sentArgs = args; return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal("gate.select", sentVerb);
        Assert.Equal(999, ExtractInt(sentArgs, "gate"));
        Assert.Null(ExtractString(sentArgs, "gate"));   // an int, not a string
        Assert.False(ExtractBool(sentArgs, "revokeServices"));
        Assert.False(ExtractBool(sentArgs, "force"));
        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
    }

    [Fact]
    public async Task A_numberless_stand_still_sends_the_verbatim_identifier()
    {
        object? sentArgs = null;
        var target = SpotWithIdentifier(" Gate 5");
        target.Number = 0;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sentArgs = args; return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            HasGateCapability);

        await selector.SelectGateAsync(target);

        Assert.Equal(" Gate 5", ExtractString(sentArgs, "gate"));
    }

    [Fact]
    public async Task An_ambiguous_reply_is_retried_with_the_matched_candidates_bglName()
    {
        var sent = new List<string?>();
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                sent.Add(ExtractGateAsText(args));
                return Task.FromResult<GsxFrame?>(
                    sent.Count == 1 ? AmbiguousGate5Frame() : PreparedGateT5Frame());
            },
            HasGateCapability);

        var result = await selector.SelectGateAsync(KatlT5());

        Assert.Equal(2, sent.Count);
        Assert.Equal("5", sent[0]);              // the number, as an int
        Assert.Equal("Gate T 5", sent[1]);       // the candidate's own bglName
        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.False(result.ResolvedGateContradictsRequest);
    }

    [Fact]
    public async Task An_ambiguous_reply_with_no_unique_match_is_surfaced_not_guessed()
    {
        var sent = new List<string?>();
        var target = KatlT5();
        target.GsxUiName = "Concourse D (D1-D46) | Gate 5";   // matches neither candidate
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sent.Add(ExtractGateAsText(args)); return Task.FromResult<GsxFrame?>(AmbiguousGate5Frame()); },
            HasGateCapability);

        var result = await selector.SelectGateAsync(target);

        // One send. No second guess. The announcer's Ambiguous arm speaks as it always did.
        Assert.Single(sent);
        Assert.Equal(GsxGateSelectOutcome.Ambiguous, result.Outcome);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public async Task GSX_naming_a_different_stand_is_flagged()
    {
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(PreparedGateT5Frame()),
            HasGateCapability);

        var target = KatlT5();
        target.GsxUiName = "Delta Tech Ops (E1-21) | Gate 5";   // we asked for a DIFFERENT stand
        var result = await selector.SelectGateAsync(target);

        Assert.True(result.ResolvedGateContradictsRequest);
    }

    [Fact]
    public async Task A_not_found_falls_back_to_the_verbatim_identifier()
    {
        // The last resort: exactly what this app sent before, so a build or an airport where
        // the number route does not apply is never worse off than it was.
        var sent = new List<string?>();
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                sent.Add(ExtractGateAsText(args));
                return Task.FromResult<GsxFrame?>(sent.Count == 1 ? NotFoundFrame() : PreparedFrame());
            },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(new[] { "999", "Gate A12" }, sent);
        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
    }

    [Fact]
    public async Task The_identifier_sent_is_never_a_Describe_string()
    {
        string? sentGate = null;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sentGate = ExtractGateAsText(args); return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            HasGateCapability);

        var target = SpotWithIdentifier("Gate A12");
        string describe = target.Describe();
        // Guard the guard: if this ever failed, the assertion below would pass by accident.
        Assert.NotEqual("Gate A12", describe);

        await selector.SelectGateAsync(target);

        Assert.NotEqual(describe, sentGate);
        // Nor a label rebuilt from Name/Number/Suffix.
        Assert.NotEqual("Totally Unrelated Label 999Z", sentGate);
    }

    [Fact]
    public async Task Missing_identifier_fails_cleanly_as_BadArgs_without_sending()
    {
        bool sent = false;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sent = true; return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier(null));

        Assert.False(sent);
        Assert.Equal(GsxGateSelectOutcome.BadArgs, result.Outcome);
    }

    [Fact]
    public async Task Blank_identifier_also_fails_cleanly_as_BadArgs_without_sending()
    {
        bool sent = false;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sent = true; return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("   "));

        Assert.False(sent);
        Assert.Equal(GsxGateSelectOutcome.BadArgs, result.Outcome);
    }

    // ── services_active: retry exactly once, never a loop ───────────────────

    [Fact]
    public async Task Services_active_retries_exactly_once_with_revokeServices_true()
    {
        var revokeFlags = new List<bool>();
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                revokeFlags.Add(ExtractBool(args, "revokeServices"));
                return Task.FromResult<GsxFrame?>(ServicesActiveFrame());
            },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        // Still services_active on the retry -- must NOT try a third time.
        Assert.Equal(new[] { false, true }, revokeFlags);
        Assert.Equal(GsxGateSelectOutcome.ServicesActive, result.Outcome);
    }

    [Fact]
    public async Task Services_active_retry_can_succeed_and_that_result_is_what_is_returned()
    {
        int callCount = 0;
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                callCount++;
                bool revoke = ExtractBool(args, "revokeServices");
                return Task.FromResult<GsxFrame?>(revoke ? PreparedFrame() : ServicesActiveFrame());
            },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(2, callCount);
        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
    }

    // ── RequestedIdentifier: what makes a wrong resolution detectable at all ──

    [Fact]
    public async Task The_identifier_sent_is_stamped_on_the_result()
    {
        // A result frame does not echo the request, so without this stamp nothing downstream
        // can compare the stand GSX named against the stand the pilot picked -- and GSX's
        // uiGateName collides at real airports (128 of 231 KJFK stands share one).
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(PreparedFrame()),
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate B7"));

        // The string rendering of what was FINALLY sent -- the number, since that is what
        // gate.select answers to. It must survive the int path, or the comparison below
        // silently disarms.
        Assert.Equal("999", result.RequestedIdentifier);
        // PreparedFrame() resolves to "Gate A12"/"A12" -- neither answers to what we sent, so
        // this is the silent wrong-stand case the check exists for.
        Assert.True(result.ResolvedGateContradictsRequest);
    }

    [Fact]
    public async Task The_identifier_is_stamped_on_the_retry_result_too()
    {
        // The retry builds a SECOND result; the stamp has to be on whichever one is returned,
        // or a revoke-and-reprepare would silently lose the comparison.
        //
        // Deliberately a CORRECT-stand fixture: GSX resolved exactly the stand that was
        // asked for, so the comparison must come back clean. That last assertion is the
        // only one in the suite pinning "a right answer is NOT flagged", and a false alarm
        // here teaches the pilot to ignore the real one.
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                bool revoke = ExtractBool(args, "revokeServices");
                return Task.FromResult<GsxFrame?>(revoke ? PreparedRamp1Frame() : ServicesActiveFrame());
            },
            HasGateCapability);

        var result = await selector.SelectGateAsync(KatlRamp1());

        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.Equal("1", result.RequestedIdentifier);
        Assert.False(result.ResolvedGateContradictsRequest);
    }

    [Fact]
    public async Task A_correct_selection_of_a_stand_with_no_uiName_is_not_flagged_as_a_mismatch()
    {
        // The regression this exists for. The NUMBER goes on the wire, so
        // RequestedIdentifier renders as "1" -- which no echoed TEXT field can ever equal.
        // With ExpectedUiName null (GSX publishes no uiName for this stand) the comparison
        // fell through to the echoed strings and reported a contradiction on a perfectly
        // correct resolution: "Careful: you selected 1, but GSX prepared Ramp 1." It also
        // wrote resolvedMismatch=true into gsx-gate-select.log -- the one token that log
        // reserves for the single anomaly worth grepping.
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(PreparedRamp1Frame()),
            HasGateCapability);

        var result = await selector.SelectGateAsync(KatlRamp1());

        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.Equal(1, result.RequestedNumber);
        Assert.False(result.ResolvedGateContradictsRequest);
        Assert.DoesNotContain("Careful", GsxGateSelectAnnouncer.Describe(result)!,
                              StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_number_that_resolved_to_a_DIFFERENT_stand_is_still_flagged()
    {
        // The other half of the same rule: a matching number clears the check, a different
        // one must not. PreparedFrame() echoes number 12 against a request for 999.
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(PreparedFrame()),
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(999, result.RequestedNumber);
        Assert.True(result.ResolvedGateContradictsRequest);
    }

    [Fact]
    public async Task A_string_send_stamps_no_number_so_a_coincidental_echo_cannot_clear_it()
    {
        // A numberless stand goes as a STRING, and gate.select treats "5" and 5 as
        // different requests -- so an echoed number proves nothing about a string send.
        // RequestedNumber must be null on this path, which is exactly why the sent number
        // is stamped rather than re-parsed out of RequestedIdentifier.
        var target = SpotWithIdentifier(" Gate 5");
        target.Number = 0;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(PreparedFrame()),
            HasGateCapability);

        var result = await selector.SelectGateAsync(target);

        Assert.Null(result.RequestedNumber);
    }

    [Fact]
    public async Task The_services_active_retry_resends_the_number_as_an_int_not_as_text()
    {
        // The retry must re-send what ACTUALLY reached GSX -- and a number rendered back to
        // a string is a DIFFERENT request: live probing has "5" returning not_found where 5
        // returns a usable answer. Carrying the rendered string here would make every
        // revoke-and-reprepare fail.
        var kinds = new List<JsonValueKind>();
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                var e = ToJson(args);
                kinds.Add(e.GetProperty("gate").ValueKind);
                return Task.FromResult<GsxFrame?>(
                    ExtractBool(args, "revokeServices") ? PreparedFrame() : ServicesActiveFrame());
            },
            HasGateCapability);

        await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(new[] { JsonValueKind.Number, JsonValueKind.Number }, kinds);
    }

    [Fact]
    public async Task The_services_active_retry_resends_the_bglName_once_the_ambiguity_resolved_it()
    {
        // Ordering matters: number -> ambiguous -> bglName -> services_active -> retry.
        // The retry must carry the bglName, never regress to the number that was already
        // answered with "several stands match".
        var sent = new List<string?>();
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                sent.Add(ExtractGateAsText(args));
                return Task.FromResult<GsxFrame?>(sent.Count switch
                {
                    1 => AmbiguousGate5Frame(),
                    2 => ServicesActiveFrame(),
                    _ => PreparedGateT5Frame(),
                });
            },
            HasGateCapability);

        var result = await selector.SelectGateAsync(KatlT5());

        Assert.Equal(new[] { "5", "Gate T 5", "Gate T 5" }, sent);
        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.True(result.WasRevokedAndReprepared);
    }

    [Fact]
    public async Task A_locally_refused_request_carries_no_requested_identifier()
    {
        // Nothing was sent, so there is no request to compare anything against. The
        // announcer's fallbacks depend on being able to tell that apart from a real send.
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(PreparedFrame()),
            NoGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(GsxGateSelectOutcome.GateSelectUnsupported, result.Outcome);
        Assert.Null(result.RequestedIdentifier);
        Assert.False(result.ResolvedGateContradictsRequest);
    }

    // ── WasRevokedAndReprepared: the caller's signal to announce a torn-down stand ──

    [Fact]
    public async Task A_successful_services_active_retry_sets_WasRevokedAndReprepared()
    {
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                bool revoke = ExtractBool(args, "revokeServices");
                return Task.FromResult<GsxFrame?>(revoke ? PreparedFrame() : ServicesActiveFrame());
            },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.True(result.WasRevokedAndReprepared);
    }

    [Fact]
    public async Task A_first_try_success_does_not_set_WasRevokedAndReprepared()
    {
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(PreparedFrame()),
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.False(result.WasRevokedAndReprepared);
    }

    [Fact]
    public async Task A_services_active_retry_that_is_still_busy_does_not_set_WasRevokedAndReprepared()
    {
        // The retry itself came back services_active again (double-busy) -- not Prepared,
        // so there is nothing to claim was "reprepared".
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(ServicesActiveFrame()),
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(GsxGateSelectOutcome.ServicesActive, result.Outcome);
        Assert.False(result.WasRevokedAndReprepared);
    }

    [Fact]
    public async Task A_services_active_retry_that_lands_on_an_occupied_stand_does_not_set_WasRevokedAndReprepared()
    {
        // The retry succeeded in the sense of getting a definite answer, but that answer
        // was assigned_to_other, not Prepared -- the revoke happened, but there is no new
        // stand set up to announce.
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                bool revoke = ExtractBool(args, "revokeServices");
                return Task.FromResult<GsxFrame?>(revoke ? AssignedToOtherFrame() : ServicesActiveFrame());
            },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(GsxGateSelectOutcome.AssignedToOther, result.Outcome);
        Assert.False(result.WasRevokedAndReprepared);
    }

    [Fact]
    public async Task Already_there_does_not_trigger_a_retry()
    {
        int callCount = 0;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { callCount++; return Task.FromResult<GsxFrame?>(AlreadyParkedFrame()); },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(1, callCount);
        Assert.Equal(GsxGateSelectOutcome.AlreadyThere, result.Outcome);
    }

    // ── assigned_to_other: return it, NEVER auto-force ───────────────────────

    [Fact]
    public async Task Assigned_to_other_is_returned_without_a_retry_and_force_is_never_sent()
    {
        var sentArgsList = new List<object?>();
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sentArgsList.Add(args); return Task.FromResult<GsxFrame?>(AssignedToOtherFrame()); },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(GsxGateSelectOutcome.AssignedToOther, result.Outcome);
        Assert.Equal("Gate A12", result.ResolvedGate!.UiName);
        Assert.Single(sentArgsList);
        Assert.False(ExtractBool(sentArgsList[0], "force"));
    }

    // ── ambiguous: surface candidates, never guess ───────────────────────────

    [Fact]
    public async Task Ambiguous_surfaces_candidates_and_never_sends_a_second_guess()
    {
        int sendCount = 0;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sendCount++; return Task.FromResult<GsxFrame?>(AmbiguousFrame()); },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A1"));

        Assert.Equal(1, sendCount);
        Assert.Equal(GsxGateSelectOutcome.Ambiguous, result.Outcome);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal("Gate A12", result.Candidates[0].UiName);
        Assert.Equal("Gate A120", result.Candidates[1].UiName);
    }

    // ── Warnings survive end-to-end ───────────────────────────────────────

    [Fact]
    public async Task Too_small_warning_survives_to_the_caller()
    {
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(PreparedFrameWithTooSmallWarning()),
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.Contains("too_small", result.Warnings);
    }

    // ── Transport failure never throws ───────────────────────────────────────

    [Fact]
    public async Task A_null_frame_from_the_sender_maps_to_TransportFailure_not_a_throw()
    {
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(null),
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(GsxGateSelectOutcome.TransportFailure, result.Outcome);
    }

    [Fact]
    public async Task A_sender_that_throws_never_lets_the_exception_escape()
    {
        var selector = new GsxRemoteGateSelector(
            (verb, args) => throw new InvalidOperationException("socket not open"),
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(GsxGateSelectOutcome.TransportFailure, result.Outcome);
    }

    // ── Reentrancy: overlapping calls serialise ──────────────────────────────

    [Fact]
    public async Task Overlapping_calls_serialise_the_second_never_sends_until_the_first_finishes()
    {
        var enteredFirstSend = new TaskCompletionSource();
        var releaseFirstSend = new TaskCompletionSource();
        int sendCount = 0;

        async Task<GsxFrame?> Send(string verb, object? args)
        {
            int n = Interlocked.Increment(ref sendCount);
            if (n == 1)
            {
                enteredFirstSend.TrySetResult();
                await releaseFirstSend.Task;
            }
            return PreparedFrame();
        }

        var selector = new GsxRemoteGateSelector(Send, HasGateCapability);

        Task<GsxGateSelectResult> call1 = selector.SelectGateAsync(SpotWithIdentifier("Gate A1"));
        await enteredFirstSend.Task; // call1's Send is now in-flight and blocked

        Task<GsxGateSelectResult> call2 = selector.SelectGateAsync(SpotWithIdentifier("Gate B2"));

        // If the reentrancy guard were missing, call2's Send (which does not block)
        // would run and bump sendCount to 2 well within this window.
        await Task.Delay(50);
        Assert.Equal(1, sendCount);

        releaseFirstSend.TrySetResult();

        var r1 = await call1;
        var r2 = await call2;

        Assert.Equal(2, sendCount);
        Assert.Equal(GsxGateSelectOutcome.Prepared, r1.Outcome);
        Assert.Equal(GsxGateSelectOutcome.Prepared, r2.Outcome);
    }

    [Fact]
    public async Task A_services_active_retry_cannot_be_interleaved_by_an_overlapping_call()
    {
        // Regression target named in the spec: two overlapping gate.select calls
        // must never let the second call's send land BETWEEN the first call's
        // initial attempt and its own revoke retry.
        var firstAttemptStarted = new TaskCompletionSource();
        var releaseFirstAttempt = new TaskCompletionSource();
        var calls = new List<(int call, bool revoke)>();
        int sendCount = 0;

        async Task<GsxFrame?> Send(string verb, object? args)
        {
            int n = Interlocked.Increment(ref sendCount);
            bool revoke = ExtractBool(args, "revokeServices");
            if (n == 1)
            {
                calls.Add((1, revoke));
                firstAttemptStarted.TrySetResult();
                await releaseFirstAttempt.Task;
                return ServicesActiveFrame(); // first call's initial attempt: still busy
            }
            calls.Add((n == 2 ? 1 : 2, revoke));
            return PreparedFrame();
        }

        var selector = new GsxRemoteGateSelector(Send, HasGateCapability);

        Task<GsxGateSelectResult> call1 = selector.SelectGateAsync(SpotWithIdentifier("Gate A1"));
        await firstAttemptStarted.Task;

        Task<GsxGateSelectResult> call2 = selector.SelectGateAsync(SpotWithIdentifier("Gate B2"));
        await Task.Delay(50);
        Assert.Equal(1, sendCount); // call2 still parked behind the gate

        releaseFirstAttempt.TrySetResult();
        await call1;
        await call2;

        // Expected send order: call1-initial(false), call1-retry(true), call2-initial(false).
        Assert.Equal(3, calls.Count);
        Assert.Equal((1, false), calls[0]);
        Assert.Equal((1, true), calls[1]);
        Assert.Equal((2, false), calls[2]);
    }

    // ── The lower rungs of the ladder: revoke/force, wire types, three sends ──

    [Fact]
    public async Task The_bglName_rung_also_sends_revokeServices_false_and_force_false()
    {
        // Only the plain first send and the services_active retry were pinned on these two.
        // Every rung below them carries the same rules: never revoke on an ordinary
        // attempt, and NEVER force -- force overrides a stand GSX assigned to AI traffic,
        // which would put a blind pilot nose-to-nose with an aircraft they cannot see.
        var revokes = new List<bool>();
        var forces = new List<bool>();
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                revokes.Add(ExtractBool(args, "revokeServices"));
                forces.Add(ExtractBool(args, "force"));
                return Task.FromResult<GsxFrame?>(
                    revokes.Count == 1 ? AmbiguousGate5Frame() : PreparedGateT5Frame());
            },
            HasGateCapability);

        await selector.SelectGateAsync(KatlT5());

        Assert.Equal(new[] { false, false }, revokes);
        Assert.Equal(new[] { false, false }, forces);
    }

    [Fact]
    public async Task The_fallback_rung_also_sends_revokeServices_false_and_force_false()
    {
        var revokes = new List<bool>();
        var forces = new List<bool>();
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                revokes.Add(ExtractBool(args, "revokeServices"));
                forces.Add(ExtractBool(args, "force"));
                return Task.FromResult<GsxFrame?>(
                    revokes.Count == 1 ? NotFoundFrame() : PreparedFrame());
            },
            HasGateCapability);

        await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal(new[] { false, false }, revokes);
        Assert.Equal(new[] { false, false }, forces);
    }

    [Fact]
    public async Task The_ambiguity_rungs_send_a_json_int_then_a_json_string()
    {
        // ExtractGateAsText renders a JSON number and a JSON string identically, so the
        // ordering assertions above constrain the VALUES but not the TYPES -- and the type
        // is the whole point of this ladder: live probing has 5 returning a usable answer
        // where "5" returns not_found.
        var kinds = new List<JsonValueKind>();
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                kinds.Add(ToJson(args).GetProperty("gate").ValueKind);
                return Task.FromResult<GsxFrame?>(
                    kinds.Count == 1 ? AmbiguousGate5Frame() : PreparedGateT5Frame());
            },
            HasGateCapability);

        await selector.SelectGateAsync(KatlT5());

        Assert.Equal(new[] { JsonValueKind.Number, JsonValueKind.String }, kinds);
    }

    [Fact]
    public async Task An_ambiguity_resolved_to_a_bglName_GSX_then_rejects_still_falls_back()
    {
        // The full three-rung ladder, untested until now: number -> ambiguous -> bglName ->
        // not_found -> the verbatim identifier. Each rung has to be reachable from the rung
        // above it, not only from the first send.
        var sent = new List<string?>();
        var kinds = new List<JsonValueKind>();
        var selector = new GsxRemoteGateSelector(
            (verb, args) =>
            {
                sent.Add(ExtractGateAsText(args));
                kinds.Add(ToJson(args).GetProperty("gate").ValueKind);
                return Task.FromResult<GsxFrame?>(sent.Count switch
                {
                    1 => AmbiguousGate5Frame(),
                    2 => NotFoundFrame(),
                    _ => PreparedGateT5Frame(),
                });
            },
            HasGateCapability);

        var result = await selector.SelectGateAsync(KatlT5());

        Assert.Equal(new[] { "5", "Gate T 5", " Gate 5" }, sent);
        Assert.Equal(new[] { JsonValueKind.Number, JsonValueKind.String, JsonValueKind.String }, kinds);
        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
    }

    // ── What the pilot HEARS names the stand, never the wire value ───────────

    [Fact]
    public async Task A_genuine_mismatch_names_the_stand_the_pilot_picked_not_the_number_sent()
    {
        // The wire value is a bare number now, and "you selected 999" is not something a
        // pilot can act on. The label they picked out of the dropdown is.
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(PreparedFrame()),
            HasGateCapability);

        var target = SpotWithIdentifier("Gate A12");
        var result = await selector.SelectGateAsync(target);

        Assert.True(result.ResolvedGateContradictsRequest);
        Assert.Equal(target.Describe(), result.RequestedLabel);

        string phrase = GsxGateSelectAnnouncer.Describe(result)!;
        Assert.Contains(target.Describe(), phrase);
        // Not the bare wire rendering. (The label itself contains "999" -- the stand number
        // is part of its name -- so only the naked form can be asserted absent.)
        Assert.DoesNotContain("selected 999,", phrase);
    }

    [Fact]
    public async Task A_not_found_names_the_stand_the_pilot_picked_not_the_number_sent()
    {
        // Same rule on the other reachable path: "GSX could not find 999" tells the pilot
        // nothing at all about which stand GSX could not find.
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(NotFoundFrame()),
            HasGateCapability);

        var target = SpotWithIdentifier("Gate A12");
        var result = await selector.SelectGateAsync(target);

        string phrase = GsxGateSelectAnnouncer.Describe(result)!;
        Assert.Contains(target.Describe(), phrase);
        Assert.DoesNotContain("find 999,", phrase);
    }

    [Fact]
    public async Task A_locally_refused_request_carries_no_spoken_label_either()
    {
        // Nothing was sent, so there is no request to name -- the announcer's own generic
        // fallbacks depend on being able to tell that apart from a real send.
        var selector = new GsxRemoteGateSelector(
            (verb, args) => Task.FromResult<GsxFrame?>(PreparedFrame()),
            NoGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Null(result.RequestedLabel);
        Assert.Null(result.RequestedNumber);
    }
}

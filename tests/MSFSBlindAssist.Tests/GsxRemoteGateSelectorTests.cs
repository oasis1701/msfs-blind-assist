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
    /// A GSX-sourced spot whose Name/Number/Suffix are DELIBERATELY unrelated to
    /// <paramref name="identifier"/>, so a test asserting the sent value equals
    /// <paramref name="identifier"/> cannot pass by accident of the two strings
    /// coinciding — it only passes if the code actually reads GsxIdentifier and
    /// never rebuilds a label from Describe()/Name/Number.
    /// </summary>
    private static ParkingSpot SpotWithIdentifier(string? identifier) => new()
    {
        Name = "Totally Unrelated Label",
        Number = 999,
        Suffix = "Z",
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

    [Fact]
    public async Task Happy_path_sends_gate_select_with_the_raw_identifier_verbatim()
    {
        string? sentVerb = null;
        object? sentArgs = null;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sentVerb = verb; sentArgs = args; return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            HasGateCapability);

        var result = await selector.SelectGateAsync(SpotWithIdentifier("Gate A12"));

        Assert.Equal("gate.select", sentVerb);
        Assert.Equal("Gate A12", ExtractString(sentArgs, "gate"));
        Assert.False(ExtractBool(sentArgs, "revokeServices"));
        Assert.False(ExtractBool(sentArgs, "force"));
        Assert.Equal(GsxGateSelectOutcome.Prepared, result.Outcome);
        Assert.Equal("Gate A12", result.ResolvedGate!.UiName);
    }

    [Fact]
    public async Task The_identifier_sent_is_never_a_Describe_string()
    {
        string? sentGate = null;
        var selector = new GsxRemoteGateSelector(
            (verb, args) => { sentGate = ExtractString(args, "gate"); return Task.FromResult<GsxFrame?>(PreparedFrame()); },
            HasGateCapability);

        var target = SpotWithIdentifier("Gate A12");
        string describe = target.Describe();
        // Guard the guard: if this ever failed, the test below would pass by accident.
        Assert.NotEqual("Gate A12", describe);

        await selector.SelectGateAsync(target);

        Assert.Equal("Gate A12", sentGate);
        Assert.NotEqual(describe, sentGate);
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
}

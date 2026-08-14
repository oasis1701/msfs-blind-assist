using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// Sends one GSX Remote API command and hands back its result frame — or null on any
/// transport-level failure (not connected, send failed, timed out). This is the ONE seam
/// <see cref="GsxRemoteGateSelector"/> depends on, so its decision logic (capability gate,
/// retry-once-on-<c>services_active</c>, never-auto-<c>force</c>) is fully unit-testable
/// without a live socket. The production wiring is
/// <c>async (verb, args) =&gt; (await remoteConnection.SendAsync(verb, args)).Frame</c>.
/// </summary>
public delegate Task<GsxFrame?> GsxCommandSender(string verb, object? args);

/// <summary>
/// Selects a GSX parking stand via the documented <c>gate.select</c> verb — one request,
/// one typed response, no menu interaction. Replaces the retired menu-walking
/// <c>Services/Gsx/GsxGateSelector.cs</c> (1013 lines, backtracking page-aware DFS) and
/// <c>GsxMenuClassifier</c> (762 lines of leaf-vs-category heuristics): neither is on this
/// path any more — there is no live menu state to traverse or classify.
///
/// See docs/superpowers/specs/2026-08-12-gsx-remote-api-gate-list-and-selection-design.md
/// §"GsxRemoteGateSelector.cs — REVISED" for the full design rationale, and the vendor
/// guide §8.14 for the wire shapes.
/// </summary>
public sealed class GsxRemoteGateSelector
{
    private const string GateCapability = "gate";

    // The documented first stop for "gate not found" (carried over from the retired
    // menu-walking selector's walk-log). Only ever fed already-parsed, already-safe
    // fields (identifier string, GsxGateSelectResult's own members) — NEVER a raw
    // frame: handlerData (which frames can carry) holds user data.
    private static readonly LogChannel GateSelectLog = Log.Channel("gsx-gate-select");

    private readonly GsxCommandSender _send;
    private readonly Func<IReadOnlyCollection<string>> _capabilities;

    // A live DFS over GSX's menu used to need an Interlocked reject-the-second-caller
    // latch, because two interleaved traversals could press arbitrary wrong entries on
    // the one shared live menu. gate.select has no traversal to corrupt — but two
    // overlapping SelectGateAsync calls (e.g. a double Calculate-click) can still fire
    // two gate.select requests whose results interleave, and the second could
    // revokeServices the first mid-flight. A SemaphoreSlim(1,1) SERIALIZES overlapping
    // calls instead of rejecting the second outright: the second call's SendAsync call
    // (including its own retry-with-revoke, if it needs one) only ever starts once the
    // first call has fully finished. That is strictly safe (no interleaving is possible
    // by construction) and friendlier than a reject, now that the whole operation is one
    // fast round trip instead of a walk that could run up to 180s.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GsxRemoteGateSelector(GsxCommandSender send, Func<IReadOnlyCollection<string>> capabilities)
    {
        _send = send ?? throw new ArgumentNullException(nameof(send));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    /// <summary>
    /// Prepares <paramref name="target"/> at GSX. Never throws — every failure mode
    /// (missing capability, missing identifier, transport failure, any GSX error code)
    /// comes back as a typed <see cref="GsxGateSelectResult"/> instead.
    /// </summary>
    public async Task<GsxGateSelectResult> SelectGateAsync(ParkingSpot target)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await SelectGateLockedAsync(target).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GsxGateSelectResult> SelectGateLockedAsync(ParkingSpot target)
    {
        string label = DescribeForLog(target);

        // Feature-check FIRST, against hello.capabilities — never a version number
        // (the vendor guide's own instruction). No 'gate' token means this GSX build
        // predates 4.0.8 and gate.select does not exist: never attempt the verb.
        // A misbehaving caller-supplied provider must not break the "never throws"
        // promise either -- treat a throw the same as "no capabilities known yet".
        IReadOnlyCollection<string>? capabilities;
        try { capabilities = _capabilities(); }
        catch (Exception ex)
        {
            Log.Debug("Gsx", $"gate.select capabilities provider threw: {ex.Message}");
            capabilities = null;
        }

        // Two DIFFERENT no-send cases, and conflating them puts a false statement in
        // front of a blind pilot. An EMPTY (or unreadable) capability set means nothing
        // is known about GSX at all -- most often the Remote API simply isn't connected
        // yet, or GSX isn't running -- so this build's version cannot be inferred from it.
        // A NON-EMPTY set that lacks 'gate' is positive evidence: GSX said hello, listed
        // what it can do, and gate.select wasn't on the list. Only the second justifies
        // telling the pilot to update GSX (TaxiAssistForm speaks that, once per instance);
        // saying it for the first would tell someone whose GSX merely isn't running to go
        // and install a version they may already have. Every live hello frame carries
        // several tokens (the committed gsx-hello.json fixture has nine), and Access GSX's
        // menu/services/settings work on 4.0.1-4.0.7 builds, so a connected older GSX
        // always lands in the second case, never the first.
        if (capabilities is null || capabilities.Count == 0)
        {
            LogNoSend(label, "no capabilities known (Remote API not connected?)", GsxGateSelectOutcome.Unavailable);
            return GsxGateSelectResult.Local(
                GsxGateSelectOutcome.Unavailable,
                "GSX has not advertised its capabilities yet.");
        }

        if (!capabilities.Contains(GateCapability, StringComparer.Ordinal))
        {
            LogNoSend(label, "no 'gate' capability (GSX 4.0.8+ required)", GsxGateSelectOutcome.GateSelectUnsupported);
            return GsxGateSelectResult.Local(
                GsxGateSelectOutcome.GateSelectUnsupported,
                "GSX does not advertise gate.select (requires GSX 4.0.8 or newer).");
        }

        // GSX's OWN identifier, verbatim — never a label rebuilt from Describe() or
        // from Name/Number/Suffix. A round-trip through our own formatting is exactly
        // how the wrong stand gets selected (spec ruling).
        string? identifier = target?.GsxIdentifier;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            LogNoSend(label, "target spot has no GsxIdentifier", GsxGateSelectOutcome.BadArgs);
            return GsxGateSelectResult.Local(
                GsxGateSelectOutcome.BadArgs,
                "No GSX identifier is available for this spot.");
        }

        GsxGateSelectResult result = await SendSelectAsync(identifier, revokeServices: false).ConfigureAwait(false);
        LogAttempt(label, identifier, revokeServices: false, result);

        // services_active: GSX is already committed at a gate. Retry EXACTLY once
        // with revokeServices: true -- the pilot asked for a different stand, and
        // revoking the setup at the old one is the obvious, expected consequence
        // (matches what the in-game menu does). Never loop: whatever the retry comes
        // back with -- success, services_active again, or anything else -- is final.
        if (result.Outcome == GsxGateSelectOutcome.ServicesActive)
        {
            result = await SendSelectAsync(identifier, revokeServices: true).ConfigureAwait(false);
            LogAttempt(label, identifier, revokeServices: true, result);

            // Mark the retry's own success so a caller can tell "prepared immediately"
            // apart from "prepared after tearing down the previous stand's services" --
            // the pilot needs to hear the latter (spec: "a successful revoke-and-reprepare
            // ... so the pilot knows the previous stand's setup was torn down"). Only on
            // Prepared: revokeServices:true already performed the revoke as a side effect
            // regardless of what the retry itself returns, but Prepared is the one outcome
            // this flag is defined to mean "and the new stand is now set up too".
            if (result.Outcome == GsxGateSelectOutcome.Prepared)
                result.WasRevokedAndReprepared = true;
        }

        return result;
    }

    private async Task<GsxGateSelectResult> SendSelectAsync(string identifier, bool revokeServices)
    {
        GsxFrame? frame;
        try
        {
            frame = await _send("gate.select", new
            {
                gate = identifier,
                revokeServices,
                force = false,   // NEVER true here, and no parameter above ever
                                  // threads a caller-supplied value in: force
                                  // overrides a stand GSX assigned to AI traffic,
                                  // and doing that silently would put a blind
                                  // pilot nose-to-nose with an aircraft they
                                  // cannot see. assigned_to_other is surfaced to
                                  // the caller instead -- see SelectGateLockedAsync.
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug("Gsx", $"gate.select transport threw: {ex.Message}");
            frame = null;
        }

        // FromFrame's parameter isn't annotated nullable, but it explicitly handles a
        // null frame (-> TransportFailure) -- same null-forgiving idiom Task 1's own
        // tests use (GsxGateSelectResultTests.A_null_frame_reference_maps_to...).
        var result = GsxGateSelectResult.FromFrame(frame!);

        // A result frame does not echo the request, so nothing downstream could otherwise
        // tell whether the stand GSX named is the one we asked for. Stamping it here is
        // what lets GsxGateSelectResult.ResolvedGateContradictsRequest catch GSX resolving
        // a colliding uiGateName to a DIFFERENT stand -- which at KJFK is 128 of 231 stands'
        // worth of opportunity, and used to be completely silent.
        result.RequestedIdentifier = identifier;
        return result;
    }

    private static string DescribeForLog(ParkingSpot? target)
    {
        if (target is null) return "(null target)";
        try { return target.Describe(); }
        catch { return "(spot)"; }
    }

    private static void LogNoSend(string label, string reason, GsxGateSelectOutcome outcome)
    {
        try { GateSelectLog.Info($"target=\"{label}\" identifierSent=(none) outcome={outcome} noSendReason=\"{reason}\""); }
        catch { /* logging must never break the selector */ }
    }

    private static void LogAttempt(string label, string identifier, bool revokeServices, GsxGateSelectResult result)
    {
        try
        {
            string resolved = result.ResolvedGate is { } g
                ? $"{g.UiName} (gate={g.Gate} number={g.Number?.ToString() ?? "?"} bglName={g.BglName})"
                : "(none)";
            string warnings = result.Warnings.Count > 0 ? string.Join(",", result.Warnings) : "(none)";
            // GSX's OWN error text, not ours. It is the single most useful field for
            // diagnosing not_found / bad_args / no_airport -- the codes that produce a
            // silent no-op the pilot can only investigate through this log -- and it was
            // parsed and then discarded until now. Newlines are flattened so one attempt
            // stays one log line (the log is read by eye, and a multi-line entry breaks
            // the target=… identifierSent=… scan pattern). Safe to log: it is GSX's own
            // short diagnostic string, never a raw frame (handlerData carries user data).
            string message = string.IsNullOrWhiteSpace(result.Message)
                ? "(none)"
                : result.Message.Replace("\r", " ").Replace("\n", " ").Trim();
            // Appended ONLY when true, so an ordinary line keeps the exact
            // target=… identifierSent=… shape this log is scanned by eye for, while the one
            // anomaly worth grepping ("GSX prepared a stand I did not ask for") has a token.
            string mismatch = result.ResolvedGateContradictsRequest ? " resolvedMismatch=true" : "";
            GateSelectLog.Info(
                $"target=\"{label}\" identifierSent=\"{identifier}\" revokeServices={revokeServices} " +
                $"outcome={result.Outcome} resolvedGate={resolved} warnings={warnings} " +
                $"rawCode={result.RawCode ?? "(none)"} message=\"{message}\"{mismatch}");
        }
        catch { /* logging must never break the selector */ }
    }
}

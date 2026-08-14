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

        if (capabilities is null || !capabilities.Contains(GateCapability, StringComparer.Ordinal))
        {
            LogNoSend(label, "no 'gate' capability (GSX 4.0.8+ required)", GsxGateSelectOutcome.Unavailable);
            return GsxGateSelectResult.Local(
                GsxGateSelectOutcome.Unavailable,
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
        return GsxGateSelectResult.FromFrame(frame!);
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
            GateSelectLog.Info(
                $"target=\"{label}\" identifierSent=\"{identifier}\" revokeServices={revokeServices} " +
                $"outcome={result.Outcome} resolvedGate={resolved} warnings={warnings} " +
                $"rawCode={result.RawCode ?? "(none)"}");
        }
        catch { /* logging must never break the selector */ }
    }
}

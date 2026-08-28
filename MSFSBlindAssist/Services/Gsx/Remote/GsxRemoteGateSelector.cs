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
            // Names the CAPABILITY, never a version number. CLAUDE.md and docs/gsx.md both hold
            // that 4.0.8 appears in exactly two user-facing strings and nowhere else in code
            // (GsxService.ReasonNoRemoteApi and GsxGateSelectAnnouncer.GateSelectUnsupportedMessage)
            // so that a future floor change is a two-site edit rather than a hunt -- and those two
            // are the ones pinned by tests. These are diagnostic strings, so they carried no
            // benefit to offset being extra places the number has to be found and changed.
            LogNoSend(label, "hello.capabilities does not include 'gate'",
                      GsxGateSelectOutcome.GateSelectUnsupported);
            return GsxGateSelectResult.Local(
                GsxGateSelectOutcome.GateSelectUnsupported,
                "GSX does not advertise the 'gate' capability, so gate.select is unavailable.");
        }

        // GSX's OWN values only -- a number it published, or a bglName it handed back.
        // Never a label rebuilt from Name/Number/Suffix or Describe(): a round-trip through
        // our own formatting is how the wrong stand gets selected (spec ruling).
        object? firstAttempt = GsxGateSelectPlan.FirstAttempt(target);
        if (firstAttempt is null)
        {
            LogNoSend(label, "target spot has no GsxIdentifier", GsxGateSelectOutcome.BadArgs);
            return GsxGateSelectResult.Local(
                GsxGateSelectOutcome.BadArgs,
                "No GSX identifier is available for this spot.");
        }

        // Whatever ACTUALLY reached GSX last, kept as the object it was sent as. The
        // services_active retry below re-sends THIS, so it must never regress to an earlier
        // attempt -- and must never be re-derived from result.RequestedIdentifier, which is a
        // string rendering: re-sending the number 5 as the text "5" is a different request
        // that live probing showed returns not_found.
        object lastSent = firstAttempt;

        GsxGateSelectResult result = await SendSelectAsync(firstAttempt, target, revokeServices: false)
            .ConfigureAwait(false);
        LogAttempt(label, Render(firstAttempt), revokeServices: false, result);

        // Ambiguous: GSX refused to guess between same-numbered stands and handed back the
        // full candidate list -- the ONLY place a client can obtain a bglName, which is the
        // only identifier gate.select accepts. Resolve it to OUR stand and re-send. If the
        // match is not unique we do NOT guess either: the Ambiguous result falls through and
        // GsxGateSelectAnnouncer surfaces it exactly as it does today.
        if (result.Outcome == GsxGateSelectOutcome.Ambiguous)
        {
            var matched = GsxGateCandidateMatcher.Match(
                result.Candidates, target?.GsxUiName, target?.GsxIdentifier, target?.Number ?? 0);
            if (matched != null)
            {
                lastSent = matched.BglName;
                result = await SendSelectAsync(matched.BglName, target, revokeServices: false)
                    .ConfigureAwait(false);
                LogAttempt(label, matched.BglName, revokeServices: false, result);
            }
        }

        // Last resort: the verbatim identifier -- today's behaviour, so a build or an
        // airport where the number route does not apply is never worse than before.
        //
        // BadArgs as well as NotFound. Both mean "that argument bought nothing", and the
        // promise this rung exists to keep is that no airport ends up worse off than it was
        // before the number was tried first. Gated on NotFound alone, a GSX build that
        // rejects the int-typed argument outright ends the sequence having never made the
        // attempt the old code always made -- the claim held everywhere except the one
        // reply shape that would need it. The fallback is idempotent and last-resort, so
        // widening the gate costs one wasted frame in the case where it cannot help.
        if ((result.Outcome == GsxGateSelectOutcome.NotFound
                || result.Outcome == GsxGateSelectOutcome.BadArgs)
            && GsxGateSelectPlan.FallbackAttempt(target) is { } fallback)
        {
            lastSent = fallback;
            result = await SendSelectAsync(fallback, target, revokeServices: false).ConfigureAwait(false);
            LogAttempt(label, fallback, revokeServices: false, result);
        }

        // services_active: GSX is already committed at a gate. Retry EXACTLY once
        // with revokeServices: true -- the pilot asked for a different stand, and
        // revoking the setup at the old one is the obvious, expected consequence
        // (matches what the in-game menu does). Never loop: whatever the retry comes
        // back with -- success, services_active again, or anything else -- is final.
        if (result.Outcome == GsxGateSelectOutcome.ServicesActive)
        {
            result = await SendSelectAsync(lastSent, target, revokeServices: true).ConfigureAwait(false);
            LogAttempt(label, Render(lastSent), revokeServices: true, result);

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

    /// <summary>
    /// Sends one <c>gate.select</c>. <paramref name="identifier"/> is deliberately
    /// <see cref="object"/>: gate.select accepts a stand NUMBER as a JSON int (which is what
    /// it actually resolves -- live-probed 2026-08-27) as well as a string, and the two are
    /// different requests on the wire. The declared type must stay <c>object</c> all the way
    /// into the anonymous type, or System.Text.Json would have no runtime type to serialise
    /// the int as a JSON number.
    /// </summary>
    private async Task<GsxGateSelectResult> SendSelectAsync(
        object identifier, ParkingSpot? target, bool revokeServices)
    {
        GsxFrame? frame;
        try
        {
            frame = await _send("gate.select", new
            {
                gate = identifier,   // int OR string -- the guide accepts both
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
        //
        // The invariant string rendering of whatever was FINALLY sent, so the comparison can
        // never silently disarm on the int path.
        result.RequestedIdentifier = Render(identifier);
        // The number ONLY when the request actually went as one -- a JSON int and the string
        // "5" are different requests to gate.select, and Render() flattens that distinction
        // away. This is what lets a matching echoed number clear the contradiction check on
        // the int path without a numeric-looking STRING request borrowing the same clearance.
        result.RequestedNumber = identifier as int?;
        // The stand as the PILOT knows it (their dropdown's own label), for speech only --
        // never sent. Without it the mismatch warning reads "you selected 5".
        result.RequestedLabel = SpokenLabel(target);
        // GSX's fully-qualified name for the stand the pilot picked. uiGateName collides (235
        // of KATL's 294 stands share one) so comparing GSX's echo against it proves nothing;
        // uiName is unique for 281 of 294 and is what makes the check above able to answer.
        result.ExpectedUiName = target?.GsxUiName;
        return result;
    }

    /// <summary>The wire value as text, for the stamp and the log. Invariant culture, so a
    /// number never renders with a locale's own digits or separators.</summary>
    private static string Render(object identifier)
        => identifier as string
           ?? Convert.ToString(identifier, System.Globalization.CultureInfo.InvariantCulture)
           ?? "";

    /// <summary>The stand's pilot-facing label — what its dropdown entry read — or null when
    /// there is no target to name. SPOKEN only; it must never reach the wire.</summary>
    private static string? SpokenLabel(ParkingSpot? target)
    {
        if (target is null) return null;
        try { return target.Describe(); }
        catch { return null; }
    }

    private static string DescribeForLog(ParkingSpot? target)
    {
        if (target is null) return "(null target)";
        try { return target.Describe(); }
        catch { return "(spot)"; }
    }

    private static void LogNoSend(string label, string reason, GsxGateSelectOutcome outcome)
    {
        try
        {
            GateSelectLog.Info($"target={GsxDiagnosticLog.Quote(label)} identifierSent=(none) " +
                               $"outcome={outcome} noSendReason={GsxDiagnosticLog.Quote(reason)}");
        }
        catch { /* logging must never break the selector */ }
    }

    private static void LogAttempt(string label, string identifier, bool revokeServices, GsxGateSelectResult result)
    {
        try
        {
            GateSelectLog.Info(FormatAttempt(label, identifier, revokeServices, result));
        }
        catch { /* logging must never break the selector */ }
    }

    /// <summary>
    /// One <c>gsx-gate-select.log</c> attempt line. Split out from <see cref="LogAttempt"/>
    /// purely as a test seam: this is the one formatter in either GSX log that BROKE the
    /// channel's stated shape rule ("no unquoted value may contain a space"), it has just been
    /// re-authored, and <c>GsxDiagnosticLogTests.No_unquoted_value_contains_a_space</c> reached
    /// only the five <c>GsxDiagnosticLog.Format*</c> helpers. Nothing else stopped a revert of
    /// <c>identifierSent</c> from <see cref="GsxDiagnosticLog.QuoteVerbatim"/> back to
    /// <see cref="GsxDiagnosticLog.Quote"/> — which would re-hide the LEADING SPACE in GSX's
    /// own stand name (281 of KATL's 294 stands) that this whole package exists to expose.
    /// Internal, reached via Properties/InternalsVisibleTo.cs — the same pattern as
    /// <c>GsxMenuAnnounceResolver</c> and <c>GsxRangeBoundsResolver</c>.
    /// </summary>
    internal static string FormatAttempt(
        string label, string identifier, bool revokeServices, GsxGateSelectResult result)
    {
        // Quoted key=value tokens, not free prose. This field used to render
        // `{UiName} (gate={Gate} number=... bglName=...)` -- unquoted, with spaces and
        // parentheses -- which made it the one formatter in either GSX log that broke the
        // channel's stated shape rule ("no unquoted value may contain a space"). The shape
        // test did not reach it either; it does now, through FormatAttempt.
        string resolved = result.ResolvedGate is { } g
            ? $"resolvedUiName={GsxDiagnosticLog.QuoteVerbatim(g.UiName)} " +
              $"resolvedGate={GsxDiagnosticLog.QuoteVerbatim(g.Gate)} " +
              $"resolvedNumber={(g.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(none)")} " +
              $"resolvedBglName={GsxDiagnosticLog.QuoteVerbatim(g.BglName)}"
            : "resolvedUiName=(none) resolvedGate=(none) resolvedNumber=(none) resolvedBglName=(none)";
        string warnings = result.Warnings.Count > 0 ? string.Join(",", result.Warnings) : "(none)";
        // GSX's OWN error text, not ours. It is the single most useful field for
        // diagnosing not_found / bad_args / no_airport -- the codes that produce a
        // silent no-op the pilot can only investigate through this log -- and it was
        // parsed and then discarded until now. Safe to log: it is GSX's own short
        // diagnostic string, never a raw frame (handlerData carries user data).
        //
        // Quoted through GsxDiagnosticLog.Quote, the sibling channel's formatter, rather
        // than by hand. The hand-rolled version mapped \r and \n but not \t, did not
        // collapse whitespace runs, and -- the one that matters -- did NOT escape embedded
        // double quotes. This field is vendor free text, so a GSX message containing a "
        // closed the field early and broke the target=… identifierSent=… scan this log is
        // read by eye for: unparseable exactly on the malformed input that prompted the
        // report. Quote() also renders an empty value as (none), which is what the manual
        // IsNullOrWhiteSpace branch was doing.
        string message = GsxDiagnosticLog.Quote(result.Message);
        // Appended ONLY when true, so an ordinary line keeps the exact
        // target=… identifierSent=… shape this log is scanned by eye for, while the one
        // anomaly worth grepping ("GSX prepared a stand I did not ask for") has a token.
        string mismatch = result.ResolvedGateContradictsRequest ? " resolvedMismatch=true" : "";
        return
            $"target={GsxDiagnosticLog.Quote(label)} " +
            $"identifierSent={GsxDiagnosticLog.QuoteVerbatim(identifier)} " +
            $"revokeServices={revokeServices} " +
            $"outcome={result.Outcome} {resolved} warnings={warnings} " +
            $"rawCode={result.RawCode ?? "(none)"} message={message}{mismatch}";
    }
}

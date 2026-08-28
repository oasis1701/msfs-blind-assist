using System.Text.Json;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// The interpreted outcome of a <c>gate.select</c> request — see
/// docs/superpowers/specs/2026-08-12-gsx-remote-api-gate-list-and-selection-design.md
/// §"GsxRemoteGateSelector.cs — REVISED" and the vendor guide §8.14.
/// </summary>
public enum GsxGateSelectOutcome
{
    /// <summary>GSX prepared the stand. Check <see cref="GsxGateSelectResult.Warnings"/> —
    /// a <c>too_small</c> warning still lands here; GSX prepares the stand anyway.</summary>
    Prepared,
    /// <summary>Maps BOTH <c>already_parked</c> and <c>already_selected</c> — the guide
    /// says "nothing to do" for either, so the caller must not have to tell them apart.
    /// "Nothing to do" means no retry is needed, not that nothing useful came back:
    /// <see cref="GsxGateSelectResult.ResolvedGate"/> is still populated from
    /// <c>error.gate</c>, which matters for <c>already_selected</c> — it can fire when
    /// the pilot asked for a DIFFERENT stand from the one already prepared, and
    /// <c>error.gate</c> is the only way to tell them which stand that actually is.</summary>
    AlreadyThere,
    NotFound,
    /// <summary>Several parkings matched the sent identifier; see <see cref="GsxGateSelectResult.Candidates"/>.</summary>
    Ambiguous,
    /// <summary>GSX is already committed at a gate. Retryable once with <c>revokeServices: true</c>.</summary>
    ServicesActive,
    /// <summary>The stand is AI-occupied. <see cref="GsxGateSelectResult.ResolvedGate"/> echoes it.
    /// Never auto-retry with <c>force: true</c>.</summary>
    AssignedToOther,
    NoAirport,
    BadArgs,
    /// <summary>GSX's engine is not running (<c>gsx_not_running</c>) — the socket answered,
    /// Couatl did not. Named separately from <see cref="Unavailable"/> purely so
    /// <c>gsx-gate-select.log</c> — the documented first stop for "gate not found" — states
    /// which of three very different things happened instead of flattening all of them into
    /// one word. Silent to the pilot: GSX being down already has its own surface
    /// (<c>GsxService.UnavailableReason</c>, spoken on an explicit Access GSX action).</summary>
    GsxNotRunning,
    /// <summary>The Remote API demanded authentication (<c>auth_required</c>). Should never
    /// occur: every captured <c>hello</c> frame carries <c>authRequired: false</c>, and the
    /// socket is localhost-only. Named anyway so that if it ever DOES happen, whoever reads
    /// <c>gsx-gate-select.log</c> is told, rather than left guessing behind a generic
    /// "Unavailable".</summary>
    AuthRequired,
    /// <summary>Decided LOCALLY, without sending anything: GSX advertised a capability set
    /// that does NOT contain <c>gate</c>, i.e. a connected build older than 4.0.8. Distinct
    /// from <see cref="Unavailable"/> because it is the ONE case where naming a version
    /// number to the pilot is truthful — everything else under <see cref="Unavailable"/>
    /// could equally be a 4.0.8 build answering something we don't understand, and telling
    /// that pilot to upgrade would be a lie. See <c>GsxRemoteGateSelector</c>'s capability
    /// gate and <c>TaxiAssistForm</c>'s once-per-instance latch.</summary>
    GateSelectUnsupported,
    /// <summary>A result frame carrying an error code this build does not recognise (e.g.
    /// <c>unknown_verb</c>, <c>internal</c>, or a future GSX addition), OR a capability set
    /// we could not read at all (empty/unavailable — GSX not reachable yet, so nothing is
    /// known about which build it is). The original string survives in
    /// <see cref="GsxGateSelectResult.RawCode"/> so a future GSX code is diagnosable rather
    /// than silently flattened.</summary>
    Unavailable,
    /// <summary>The frame was not a result at all, or was too malformed to interpret —
    /// never thrown, always returned.</summary>
    TransportFailure,
}

/// <summary>
/// One stand identity as GSX echoes it — from a successful <c>gate.select</c>'s
/// <c>payload.gate</c>, an <c>ambiguous</c> error's <c>error.candidates</c>, or an
/// <c>assigned_to_other</c> error's <c>error.gate</c>. Same shape in all three places:
/// <c>{"uiName":"Gate A12","gate":"A12","number":12,"bglName":"Parking 12"}</c>.
/// </summary>
public sealed record GsxGateSelectCandidate(string UiName, string Gate, int? Number, string BglName);

/// <summary>
/// Pure parsing of a <c>gate.select</c> result frame. Never throws: a non-result frame,
/// a malformed one, or an unrecognised error code all degrade to a typed outcome rather
/// than an exception — see <see cref="FromFrame"/>.
///
/// Every hand-written literal in the test suite for this type comes from the vendor
/// guide's documented shapes, not a live capture — no <c>gate.select</c> response has
/// been captured against a running GSX yet.
/// </summary>
public sealed class GsxGateSelectResult
{
    public GsxGateSelectOutcome Outcome { get; private init; }

    /// <summary>The stand GSX actually resolved the identifier to — echoed on a
    /// <see cref="GsxGateSelectOutcome.Prepared"/> success (<c>payload.gate</c>), and on
    /// an <see cref="GsxGateSelectOutcome.AssignedToOther"/> or
    /// <see cref="GsxGateSelectOutcome.AlreadyThere"/> failure (both read <c>error.gate</c>).
    /// Null for every other outcome, and null whenever GSX's response omitted or
    /// malformed the field, even for those three.</summary>
    public GsxGateSelectCandidate? ResolvedGate { get; private init; }

    /// <summary>Best-effort, non-fatal warnings GSX prepared the stand under regardless —
    /// e.g. <c>["too_small"]</c>. Only ever populated alongside <see cref="GsxGateSelectOutcome.Prepared"/>.
    /// Always speak these: it is GSX's own verdict on the real airframe, and a blind
    /// pilot has no other route to the information.</summary>
    public IReadOnlyList<string> Warnings { get; private init; } = Array.Empty<string>();

    /// <summary>The <c>ambiguous</c> candidate list — several stands matched the sent
    /// identifier and GSX refused to guess. Empty for every other outcome.</summary>
    public IReadOnlyList<GsxGateSelectCandidate> Candidates { get; private init; } = Array.Empty<GsxGateSelectCandidate>();

    /// <summary>The original GSX error code string, when the result was a failure.
    /// Always preserved — even for a code this build maps to
    /// <see cref="GsxGateSelectOutcome.Unavailable"/> because it doesn't recognise it —
    /// so a future GSX code is diagnosable rather than silently flattened. Null on
    /// success or when the frame carried no code to preserve.</summary>
    public string? RawCode { get; private init; }

    /// <summary>GSX's own error message, when it sent one. Null on success.</summary>
    public string? Message { get; private init; }

    /// <summary>
    /// True when this <see cref="GsxGateSelectOutcome.Prepared"/> result came from
    /// <see cref="GsxRemoteGateSelector"/>'s automatic <c>services_active</c> retry
    /// (<c>revokeServices: true</c>) rather than the initial attempt. A single result frame
    /// carries no way to know it was a retry — <see cref="FromFrame"/> never sets this — so
    /// <see cref="GsxRemoteGateSelector"/> flips it on the retry's own result after the fact,
    /// once it knows the retry is what produced it. Additive: defaults to <c>false</c>, so
    /// nothing about a plain first-try <see cref="FromFrame"/> parse changes. Lets a caller
    /// announce that the PREVIOUS stand's services were torn down — distinct information
    /// from an ordinary "prepared", which the spec requires the pilot be told (a
    /// <c>services_active</c> retry means GSX was already committed at a different gate).
    /// </summary>
    public bool WasRevokedAndReprepared { get; internal set; }

    /// <summary>
    /// The identifier <see cref="GsxRemoteGateSelector"/> actually put in <c>gate.select</c>'s
    /// <c>gate</c> argument, rendered as invariant text — the stand's NUMBER, a <c>bglName</c>
    /// GSX handed back in an <c>ambiguous</c> reply, or (last resort)
    /// <see cref="Database.Models.ParkingSpot.GsxIdentifier"/> verbatim, whichever the attempt
    /// sequence in <see cref="GsxGateSelectPlan"/> got as far as. Rendered rather than typed
    /// because the number goes on the wire as a JSON int and this is a comparison field, not a
    /// re-send value — the selector re-sends the ORIGINAL object, never this string.
    /// A result frame does not echo the request, so <see cref="FromFrame"/> never sets this; the
    /// selector stamps it after the fact. Null for a locally-decided result (nothing was sent)
    /// and for any frame parsed without the selector's involvement.
    /// <para>
    /// It exists so <see cref="ResolvedGateContradictsRequest"/> can compare what GSX says it
    /// selected against what we asked for — see there.
    /// </para>
    /// </summary>
    public string? RequestedIdentifier { get; internal set; }

    /// <summary>
    /// The stand NUMBER that went in <c>gate.select</c>'s <c>gate</c> argument as a JSON int,
    /// or null when the request went as a string (a numberless stand's verbatim identifier,
    /// or a <c>bglName</c> resolved out of an <c>ambiguous</c> reply) and null for a
    /// locally-decided result. Stamped by <c>GsxRemoteGateSelector</c> alongside
    /// <see cref="RequestedIdentifier"/>.
    /// <para>
    /// It is STAMPED rather than re-parsed out of <see cref="RequestedIdentifier"/>, and that
    /// is load-bearing: a rendered <c>"5"</c> cannot be told apart from a numberless stand
    /// whose <c>uiGateName</c> genuinely IS <c>"5"</c>, and <c>gate.select</c> treats those as
    /// two different requests (live-probed — the int resolves, the string returns
    /// <c>not_found</c>). Reading a number back out of the rendering would let an echoed
    /// number clear <see cref="ResolvedGateContradictsRequest"/> on a request that never
    /// carried one.
    /// </para>
    /// </summary>
    public int? RequestedNumber { get; internal set; }

    /// <summary>
    /// The stand as the PILOT knows it — <c>ParkingSpot.Describe()</c>, the base of the label
    /// their dropdown showed. Stamped by <c>GsxRemoteGateSelector</c>; null for a
    /// locally-decided result and for any frame parsed without the selector's involvement.
    /// <para>
    /// <c>Describe()</c> deliberately, not <c>ToString()</c>: the combo carries
    /// <c>ToString()</c>, which appends any online aliases (", also A24 (online)") — useful to
    /// read on screen, a recital in a spoken warning. Everything a pilot identifies the stand
    /// by is in <c>Describe()</c>.
    /// </para>
    /// <para>
    /// This is a SPOKEN label and nothing else. <see cref="RequestedIdentifier"/> is the wire
    /// value and stays exactly that (<c>gsx-gate-select.log</c>'s <c>identifierSent=</c> field
    /// depends on it), but the wire value is now usually a bare stand number, and
    /// <i>"Careful: you selected 5"</i> gives a blind pilot nothing to act on. This must never
    /// be SENT — a label rebuilt from our own parsed fields is precisely how the wrong stand
    /// gets selected (see <c>ParkingSpot.GsxIdentifier</c>); it exists only so
    /// <see cref="GsxGateSelectAnnouncer"/> can name the stand rather than the number.
    /// </para>
    /// </summary>
    public string? RequestedLabel { get; internal set; }

    /// <summary>
    /// The stand's own <c>ParkingSpot.GsxUiName</c> — GSX's fully-qualified name for the
    /// stand the pilot picked. Stamped by <c>GsxRemoteGateSelector</c> alongside
    /// <see cref="RequestedIdentifier"/>; null when the stand carries no <c>uiName</c>.
    /// <para>
    /// This is what finally closes the collision problem
    /// <see cref="ResolvedGateContradictsRequest"/> documents as structural. <c>uiGateName</c>
    /// is shared by 235 of KATL's 294 stands, so comparing GSX's echo against it proves
    /// nothing; <c>uiName</c> is unique for 281 of 294.
    /// </para>
    /// </summary>
    public string? ExpectedUiName { get; internal set; }

    /// <summary>
    /// True when GSX named a stand that answers to NEITHER of the identifiers it echoes for
    /// what we sent — i.e. GSX resolved our identifier to a DIFFERENT stand than the pilot
    /// picked.
    /// <para>
    /// This is reachable in normal operation, not a paranoia check. <c>uiGateName</c> — the
    /// only identity field GSX actually publishes per parking — is unique at some airports and
    /// not others: 98/98 distinct on a live ENGM read, but at KJFK 128 of 231 selectable stands
    /// share one with another stand ("Gate 2" names five physically different stands across five
    /// terminals). When GSX can pick between them it either answers <c>ambiguous</c> (surfaced)
    /// or resolves to one of the matches — and in that second case the pilot taxis to a stand
    /// GSX did not prepare.
    /// </para>
    /// <para>
    /// Deliberately CONSERVATIVE, because a false alarm here teaches the pilot to ignore a real
    /// one. Matching ANY identity GSX echoes for what we sent clears it. Both echoed strings
    /// are compared (trimmed, ordinal-ignore-case): the guide's own shape pairs a full
    /// <c>uiName</c> ("Gate A12") with a bare <c>gate</c> ("A12"), so which one equals what we
    /// sent depends on GSX's spelling, not on whether it picked the right stand. So is the
    /// echoed <c>bglName</c>, which is what an <c>ambiguous</c> reply's resend actually puts
    /// on the wire — clearing ONLY, never counted as an identity worth judging against. So is
    /// the
    /// echoed <c>number</c>, whenever the request itself went as a number
    /// (<see cref="RequestedNumber"/>) — which since <c>GsxGateSelectPlan</c> is the usual
    /// case, and is the ONLY identity available for a stand GSX publishes no <c>uiName</c> for.
    /// An echo we cannot interpret — no resolved gate at all, or one whose strings are all
    /// blank — is NOT a mismatch: say nothing rather than cry wolf.
    /// </para>
    /// <para>
    /// <b><see cref="ExpectedUiName"/> is what closes the collision case the identifier
    /// comparison alone could not.</b> When two different stands share an IDENTICAL
    /// <c>uiGateName</c> — the case above, "Gate 2" naming five physically different KJFK
    /// stands — GSX echoes that same string back whichever one it resolved to, so the identifier
    /// comparison is false exactly as it would be for a correct resolution. GSX's
    /// fully-qualified <c>uiName</c> does not collide the same way (unique for 281 of KATL's
    /// 294 stands where <c>uiGateName</c> is shared by 235), and it IS published per parking —
    /// the earlier claim that nothing available to us disambiguates them was written before
    /// <c>ParkingSpot.GsxUiName</c> carried it. When both sides have one it decides, and this
    /// property finally answers the question it was named for.
    /// </para>
    /// <para>
    /// The limit is now narrower but real: a stand GSX publishes no <c>uiName</c> for (KATL's
    /// unnamed GA ramps, 13 of 294) carries no <see cref="ExpectedUiName"/>, so it falls
    /// through — and on the usual path that is to the NUMBER comparison, not the identifier
    /// comparison, because <c>GsxGateSelectPlan</c> sends a stand's number whenever it has one.
    /// The number is the WEAKER guarantee of the two: KATL's Concourse T and Delta Tech Ops
    /// both answer to 5, so an echoed number matches whichever stand GSX picked. Only a spot
    /// that went as a STRING (a numberless stand's verbatim identifier, or a <c>bglName</c>
    /// resolved out of an <c>ambiguous</c> reply) reaches the identifier comparison the
    /// paragraph above describes. Do not let a future reader conclude from a green result on
    /// EITHER of those paths that GSX prepared the stand the pilot picked — there it only means
    /// GSX did not name a different one.
    /// </para>
    /// <para>
    /// The check is still worth having, and must not be removed on the strength of that: it
    /// catches a resolution to a DIFFERENTLY-named stand, which is the other half of the same
    /// failure and the half we can actually see. Do not let a future reader conclude from a green
    /// result here that GSX prepared the stand the pilot picked — it only means GSX did not name
    /// a different one.
    /// </para>
    /// </summary>
    public bool ResolvedGateContradictsRequest
    {
        get
        {
            string requested = RequestedIdentifier?.Trim() ?? string.Empty;
            if (requested.Length == 0 || ResolvedGate is not { } echoed) return false;

            // Prefer the fully-qualified name when BOTH sides have one: it is unique where
            // the identifier is not, so this is the one comparison that can actually catch
            // GSX resolving to a different stand. Same conservative rules as below --
            // trimmed, ordinal-ignore-case, and an uninterpretable echo (a blank uiName) is
            // never a mismatch, it just falls through to the identifier comparison.
            //
            // Deliberately BELOW the "nothing was sent / nothing was echoed" early return
            // above, so a locally-decided result (capability gate, no identifier) can never
            // report a contradiction no matter what is stamped on it.
            if (!string.IsNullOrWhiteSpace(ExpectedUiName)
                && !string.IsNullOrWhiteSpace(echoed.UiName))
            {
                return !string.Equals(
                    echoed.UiName.Trim(), ExpectedUiName.Trim(),
                    StringComparison.OrdinalIgnoreCase);
            }

            // The NUMBER is one of the identities we actually send -- since gate.select
            // answers to a JSON int and to almost nothing else, it is now the USUAL one --
            // so a matching echoed number clears the check exactly as a matching string
            // does. Without this, a stand GSX publishes no uiName for (KATL's GA ramps, 13
            // of 294) falls straight through to the string comparison below, which a
            // rendered number ("1") can essentially never satisfy: a perfectly correct
            // resolution announced "Careful: you selected 1, but GSX prepared Ramp 1." and
            // wrote resolvedMismatch=true into the log. A false alarm here teaches the pilot
            // to ignore the real one.
            //
            // Strictly BELOW the fully-qualified comparison above, and that ordering is
            // load-bearing: the KATL ambiguity this path exists for is two stands SHARING a
            // number (Concourse T and Delta Tech Ops both answer to 5), so the number
            // matches whichever one GSX picked. Clearing on it first would silently disarm
            // the check in exactly the collision case it was written for.
            //
            // RequestedNumber, never a number re-parsed out of RequestedIdentifier: a
            // numberless stand whose identifier IS "5" went as a string, which gate.select
            // treats as a different request, and an echoed number would then be a
            // coincidence of digits rather than evidence.
            if (RequestedNumber is { } requestedNumber && echoed.Number == requestedNumber)
                return false;

            string uiName = echoed.UiName?.Trim() ?? string.Empty;
            string gate = echoed.Gate?.Trim() ?? string.Empty;

            // The echoed bglName CLEARS, and only clears. It is one of the identities we
            // actually send -- the selector re-sends a matched candidate's bglName to resolve
            // an `ambiguous` reply, and that send stamps it as RequestedIdentifier with
            // RequestedNumber NULL, because it went as a string. On that rung the uiName
            // comparison is skipped whenever the echo carries no uiName and the number
            // comparison cannot run at all, so comparing uiName/gate alone reported a
            // contradiction for the one value GSX had just confirmed: a bglName resend of
            // "Gate T 5" echoing {uiName:"", gate:" Gate 5", bglName:"Gate T 5"} announced
            // "Careful: you selected ..., but GSX prepared ..." on a CORRECT selection.
            //
            // Placed ABOVE the interpretability guard and deliberately NOT added to it, so
            // this can only ever turn a mismatch into a match. Were bglName also to count as
            // "something interpretable", an echo with both strings blank would start being
            // JUDGED on a namespace of GSX's own that need not resemble what we sent -- new
            // false alarms, in the one property whose whole design rule is that a false alarm
            // teaches the pilot to ignore the real one.
            string bglName = echoed.BglName?.Trim() ?? string.Empty;
            if (bglName.Length > 0 && bglName.Equals(requested, StringComparison.OrdinalIgnoreCase))
                return false;

            if (uiName.Length == 0 && gate.Length == 0) return false; // nothing interpretable

            return !uiName.Equals(requested, StringComparison.OrdinalIgnoreCase)
                && !gate.Equals(requested, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Builds a result decided LOCALLY by <see cref="GsxRemoteGateSelector"/>, without ever
    /// sending a frame over the wire — the capability gate (no <c>gate</c> token in
    /// <c>hello.capabilities</c>, or no capability set to read at all) and a target spot
    /// with no identifier to send are the only cases. Internal: all are pre-flight checks
    /// the selector makes before it has
    /// anything from GSX to interpret, so there is no frame for a caller outside this
    /// assembly to have parsed via <see cref="FromFrame"/> in the first place.
    /// </summary>
    internal static GsxGateSelectResult Local(GsxGateSelectOutcome outcome, string? message = null) =>
        new() { Outcome = outcome, Message = message };

    public static GsxGateSelectResult FromFrame(GsxFrame frame)
    {
        if (frame is null || frame.Type != GsxFrameType.Result)
            return new GsxGateSelectResult { Outcome = GsxGateSelectOutcome.TransportFailure };

        try
        {
            return frame.Ok
                ? ParseSuccess(frame.Payload)
                : ParseFailure(frame.ErrorCode, frame.ErrorMessage, frame.Error);
        }
        catch (Exception)
        {
            // Defensive backstop: every accessor below already guards its own
            // ValueKind, but FromFrame must never throw even if a future GSX
            // build reshapes the payload in some way those guards miss.
            return new GsxGateSelectResult { Outcome = GsxGateSelectOutcome.TransportFailure };
        }
    }

    private static GsxGateSelectResult ParseSuccess(JsonElement payload)
    {
        return new GsxGateSelectResult
        {
            Outcome = GsxGateSelectOutcome.Prepared,
            ResolvedGate = ParseCandidateField(payload, "gate"),
            Warnings = StrList(payload, "warnings"),
        };
    }

    private static GsxGateSelectResult ParseFailure(string? code, string? message, JsonElement error)
    {
        switch (code)
        {
            case "already_parked":
            case "already_selected":
                // "Nothing to do" (no retry needed) does not mean nothing useful is in
                // the payload: the guide's own assignGate example reads error.gate for
                // both these codes, same as assigned_to_other. already_selected in
                // particular can fire when the pilot asked for a DIFFERENT stand from
                // the one already prepared, and error.gate is the only way to tell them
                // which stand that actually is.
                return new GsxGateSelectResult
                {
                    Outcome = GsxGateSelectOutcome.AlreadyThere,
                    ResolvedGate = ParseCandidateField(error, "gate"),
                    RawCode = code,
                    Message = message,
                };

            case "not_found":
                return new GsxGateSelectResult { Outcome = GsxGateSelectOutcome.NotFound, RawCode = code, Message = message };

            case "ambiguous":
                return new GsxGateSelectResult
                {
                    Outcome = GsxGateSelectOutcome.Ambiguous,
                    RawCode = code,
                    Message = message,
                    Candidates = ParseCandidateList(error, "candidates"),
                };

            case "services_active":
                return new GsxGateSelectResult { Outcome = GsxGateSelectOutcome.ServicesActive, RawCode = code, Message = message };

            case "assigned_to_other":
                return new GsxGateSelectResult
                {
                    Outcome = GsxGateSelectOutcome.AssignedToOther,
                    ResolvedGate = ParseCandidateField(error, "gate"),
                    RawCode = code,
                    Message = message,
                };

            case "no_airport":
                return new GsxGateSelectResult { Outcome = GsxGateSelectOutcome.NoAirport, RawCode = code, Message = message };

            case "bad_args":
                return new GsxGateSelectResult { Outcome = GsxGateSelectOutcome.BadArgs, RawCode = code, Message = message };

            // The two generic transport-level codes worth telling apart. Neither is
            // spoken (see GsxGateSelectAnnouncer) and neither is retryable here — they
            // are named so gsx-gate-select.log says WHICH failure occurred. "GSX is not
            // running", "GSX wants a password" and "GSX said something this build has
            // never seen" demand three different next moves from whoever reads that log,
            // and one shared "Unavailable" gave them no way to tell.
            case "gsx_not_running":
                return new GsxGateSelectResult { Outcome = GsxGateSelectOutcome.GsxNotRunning, RawCode = code, Message = message };

            case "auth_required":
                return new GsxGateSelectResult { Outcome = GsxGateSelectOutcome.AuthRequired, RawCode = code, Message = message };

            default:
                // Covers a genuinely unrecognised/future code and the remaining generic
                // transport-level ones (unknown_verb, internal) that carry no dedicated
                // member here. `code` is null when the frame had no error object (or no
                // code inside it) at all — there is nothing to preserve in that case, so
                // RawCode stays null too.
                return new GsxGateSelectResult { Outcome = GsxGateSelectOutcome.Unavailable, RawCode = code, Message = message };
        }
    }

    /// <summary>Reads a nested candidate object off <paramref name="parent"/>'s
    /// <paramref name="field"/> — <c>payload.gate</c> or <c>error.gate</c>. Null
    /// when the field is absent OR present with the wrong JSON kind, never a
    /// candidate with every member defaulted to empty: a caller must be able to
    /// tell "GSX echoed nothing" apart from "GSX echoed an actual stand".</summary>
    private static GsxGateSelectCandidate? ParseCandidateField(JsonElement parent, string field)
        => parent.ValueKind == JsonValueKind.Object
           && parent.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.Object
           ? ParseCandidate(v) : null;

    private static GsxGateSelectCandidate ParseCandidate(JsonElement e) => new(
        Str(e, "uiName") ?? "",
        Str(e, "gate") ?? "",
        Int(e, "number"),
        Str(e, "bglName") ?? "");

    private static IReadOnlyList<GsxGateSelectCandidate> ParseCandidateList(JsonElement error, string name)
    {
        if (error.ValueKind != JsonValueKind.Object
            || !error.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<GsxGateSelectCandidate>();

        var list = new List<GsxGateSelectCandidate>();
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object)
                list.Add(ParseCandidate(item));
        return list;
    }

    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
           ? v.GetString() : null;

    private static int? Int(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)
           ? i : null;

    private static IReadOnlyList<string> StrList(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString()!);
        return list;
    }
}

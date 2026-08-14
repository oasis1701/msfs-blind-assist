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
    /// <c>gate</c> argument — <see cref="Database.Models.ParkingSpot.GsxIdentifier"/>, verbatim.
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
    /// one. Both echoed strings are compared (trimmed, ordinal-ignore-case) and matching EITHER
    /// clears it: the guide's own shape pairs a full <c>uiName</c> ("Gate A12") with a bare
    /// <c>gate</c> ("A12"), so which one equals what we sent depends on GSX's spelling, not on
    /// whether it picked the right stand. An echo we cannot interpret — no resolved gate at all,
    /// or one whose strings are all blank — is NOT a mismatch: say nothing rather than cry wolf.
    /// </para>
    /// </summary>
    public bool ResolvedGateContradictsRequest
    {
        get
        {
            string requested = RequestedIdentifier?.Trim() ?? string.Empty;
            if (requested.Length == 0 || ResolvedGate is not { } echoed) return false;

            string uiName = echoed.UiName?.Trim() ?? string.Empty;
            string gate = echoed.Gate?.Trim() ?? string.Empty;
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

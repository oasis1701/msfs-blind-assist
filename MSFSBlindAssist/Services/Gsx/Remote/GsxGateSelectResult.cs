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
    /// says "nothing to do" for either, so the caller must not have to tell them apart.</summary>
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
    /// <summary>A result frame carrying an error code this build does not recognise —
    /// including the generic transport-level codes (<c>gsx_not_running</c>,
    /// <c>auth_required</c>, <c>unknown_verb</c>, <c>internal</c>). The original string
    /// survives in <see cref="GsxGateSelectResult.RawCode"/> so a future GSX code is
    /// diagnosable rather than silently flattened.</summary>
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
    /// <see cref="GsxGateSelectOutcome.Prepared"/> success (<c>payload.gate</c>) or an
    /// <see cref="GsxGateSelectOutcome.AssignedToOther"/> failure (<c>error.gate</c>).
    /// Null for every other outcome.</summary>
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
                return new GsxGateSelectResult { Outcome = GsxGateSelectOutcome.AlreadyThere, RawCode = code, Message = message };

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

            default:
                // Covers both a genuinely unrecognised/future code and the generic
                // transport-level codes (gsx_not_running, auth_required, unknown_verb,
                // internal) that carry no dedicated member here. `code` is null when
                // the frame had no error object (or no code inside it) at all — there
                // is nothing to preserve in that case, so RawCode stays null too.
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

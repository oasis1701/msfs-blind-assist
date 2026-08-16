using System.Text.Json;

namespace MSFSBlindAssist.Services.Gsx.Remote;

public enum GsxFrameType { Unknown, Hello, Snapshot, Patch, Event, Result }

/// <summary>
/// One frame off the GSX Couatl Remote API socket. Parsing NEVER throws: an
/// unrecognised or malformed frame becomes <see cref="GsxFrameType.Unknown"/>,
/// because a GSX update that adds a frame type must not kill the connection.
/// </summary>
public sealed class GsxFrame
{
    public GsxFrameType Type { get; private init; }
    public string? Id { get; private init; }

    // hello
    public int Protocol { get; private init; }
    public bool GsxRunning { get; private init; }
    public IReadOnlyList<string> Capabilities { get; private init; } = Array.Empty<string>();

    // patch
    public string? Path { get; private init; }
    /// <summary>Patch path without its leading slash — the state-store key.</summary>
    public string? Key { get; private init; }
    public JsonElement Value { get; private init; }

    // event
    public string? Topic { get; private init; }
    public bool Restarting { get; private init; }

    // result
    public bool Ok { get; private init; }
    public string? ErrorCode { get; private init; }
    public string? ErrorMessage { get; private init; }
    /// <summary>
    /// The raw <c>payload</c> object of a successful result, e.g. <c>gate.select</c>'s
    /// <c>{"code":"ok","status":"prepared","gate":{...},"warnings":[...]}</c>.
    /// Undefined (default) when absent, or when the frame isn't a result at all —
    /// every reader must <c>ValueKind</c>-guard before use, same as every other
    /// <see cref="JsonElement"/> on this type.
    /// </summary>
    public JsonElement Payload { get; private init; }
    /// <summary>
    /// The raw <c>error</c> object of a failing result. <see cref="ErrorCode"/> and
    /// <see cref="ErrorMessage"/> cover <c>code</c>/<c>message</c>; some verbs attach
    /// further members a caller needs — e.g. <c>gate.select</c>'s <c>error.candidates</c>
    /// (ambiguous) or <c>error.gate</c> (assigned to another aircraft). Undefined
    /// (default) when absent.
    /// </summary>
    public JsonElement Error { get; private init; }

    // snapshot (whole object; the store copies its top-level keys)
    public JsonElement Root { get; private init; }

    private static readonly GsxFrame UnknownFrame = new() { Type = GsxFrameType.Unknown };

    public static GsxFrame Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement r = doc.RootElement.Clone();   // Clone: outlives the JsonDocument
            if (r.ValueKind != JsonValueKind.Object) return UnknownFrame;

            string type = Str(r, "type") ?? "";
            string? id = Str(r, "id");

            switch (type)
            {
                case "hello":
                    return new GsxFrame
                    {
                        Type = GsxFrameType.Hello, Id = id,
                        Protocol = Int(r, "protocol"),
                        GsxRunning = Bool(r, "gsxRunning"),
                        Capabilities = StrList(r, "capabilities"),
                    };
                case "snapshot":
                    return new GsxFrame { Type = GsxFrameType.Snapshot, Id = id, Root = r };
                case "patch":
                {
                    string? path = Str(r, "path");
                    return new GsxFrame
                    {
                        Type = GsxFrameType.Patch, Id = id, Path = path,
                        Key = string.IsNullOrEmpty(path) ? null : path.TrimStart('/'),
                        Value = r.TryGetProperty("value", out var v) ? v : default,
                    };
                }
                case "event":
                    return new GsxFrame
                    {
                        Type = GsxFrameType.Event, Id = id,
                        Topic = Str(r, "topic"),
                        GsxRunning = Bool(r, "gsxRunning"),
                        Restarting = Bool(r, "restarting"),
                    };
                case "result":
                {
                    string? code = null, message = null;
                    JsonElement error = default;
                    if (r.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.Object)
                    {
                        error = e;
                        code = Str(e, "code");
                        message = Str(e, "message");
                    }
                    return new GsxFrame
                    {
                        Type = GsxFrameType.Result, Id = id,
                        Ok = Bool(r, "ok"), ErrorCode = code, ErrorMessage = message,
                        Payload = r.TryGetProperty("payload", out var p) ? p : default,
                        Error = error,
                    };
                }
                default:
                    return UnknownFrame;
            }
        }
        catch (JsonException)
        {
            return UnknownFrame;
        }
    }

    // ValueKind == Object guarded before TryGetProperty, which throws InvalidOperationException
    // on anything else. Parse() promises never to throw, and it reads `type` off the root before
    // anything has established that the root is an object — the ValueKind check above happens to
    // cover that today, but the guarantee should not rest on statement order in one caller.
    private static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
           && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i) ? i : 0;

    private static IReadOnlyList<string> StrList(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object
            || !e.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>();
        foreach (var item in v.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String)
                list.Add(item.GetString()!);
        return list;
    }
}

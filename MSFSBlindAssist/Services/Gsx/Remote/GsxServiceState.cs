using System.Text.Json;

namespace MSFSBlindAssist.Services.Gsx.Remote;

/// <summary>
/// One row of GSX's published <c>services</c> array. Replaces the tooltip-prose
/// regexes in the old GsxService.TextRules: every field here is stated by GSX.
/// Optional members (detail / progress / operator) are genuinely absent on most
/// services, so they are nullable rather than defaulted.
/// </summary>
public sealed class GsxServiceState
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string State { get; init; } = "";
    public int StateRaw { get; init; }
    public string StateText { get; init; } = "";
    public string? Operator { get; init; }
    public bool CanTrigger { get; init; }
    public bool CanBypass { get; init; }

    public string? BusPhase { get; init; }
    public string? Phase { get; init; }
    public int? PaxDone { get; init; }
    public int? PaxTotal { get; init; }
    public int? BagsPercent { get; init; }

    public int? ProgressCurrent { get; init; }
    public int? ProgressTotal { get; init; }
    public string? ProgressUnit { get; init; }
    public string ProgressText { get; init; } = "";

    public static IReadOnlyList<GsxServiceState> ParseList(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array) return Array.Empty<GsxServiceState>();
        var list = new List<GsxServiceState>();
        foreach (var e in array.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            JsonElement detail = Obj(e, "detail");
            JsonElement pax = detail.ValueKind == JsonValueKind.Object ? Obj(detail, "pax") : default;
            JsonElement prog = Obj(e, "progress");

            list.Add(new GsxServiceState
            {
                Id = Str(e, "id") ?? "",
                DisplayName = Str(e, "displayName") ?? "",
                State = Str(e, "state") ?? "",
                StateRaw = Int(e, "stateRaw") ?? 0,
                StateText = Str(e, "stateText") ?? "",
                Operator = Str(e, "operator"),
                CanTrigger = Bool(e, "canTrigger"),
                CanBypass = Bool(e, "canBypass"),
                BusPhase = detail.ValueKind == JsonValueKind.Object ? Str(detail, "busPhase") : null,
                Phase = detail.ValueKind == JsonValueKind.Object ? Str(detail, "phase") : null,
                PaxDone = pax.ValueKind == JsonValueKind.Object ? Int(pax, "done") : null,
                PaxTotal = pax.ValueKind == JsonValueKind.Object ? Int(pax, "total") : null,
                BagsPercent = detail.ValueKind == JsonValueKind.Object ? Int(detail, "bagsPercent") : null,
                ProgressCurrent = prog.ValueKind == JsonValueKind.Object ? Int(prog, "current") : null,
                ProgressTotal = prog.ValueKind == JsonValueKind.Object ? Int(prog, "total") : null,
                ProgressUnit = prog.ValueKind == JsonValueKind.Object ? Str(prog, "unit") : null,
                ProgressText = Str(e, "progressText") ?? "",
            });
        }
        return list;
    }

    private static JsonElement Obj(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) ? v : default;

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int? Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int i)
           ? i : null;
}

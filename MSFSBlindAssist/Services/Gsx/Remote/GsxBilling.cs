using System.Text.Json;

namespace MSFSBlindAssist.Services.Gsx.Remote;

public sealed record GsxBillingTimer(string SubService, string Friendly, double Hours,
                                     bool Running, double Amount);

/// <summary>GSX's running service charges. Drives the persistent-connection callouts.</summary>
public sealed class GsxBilling
{
    public IReadOnlyList<GsxBillingTimer> Timers { get; private init; } = Array.Empty<GsxBillingTimer>();
    public bool AnyRunning => Timers.Any(t => t.Running);

    public static readonly GsxBilling Empty = new();

    public static GsxBilling Parse(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object ||
            !v.TryGetProperty("timers", out var ts) || ts.ValueKind != JsonValueKind.Array)
            return Empty;

        var list = new List<GsxBillingTimer>();
        foreach (var t in ts.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object) continue;
            list.Add(new GsxBillingTimer(
                Str(t, "subService") ?? "",
                Str(t, "friendly") ?? "",
                Num(t, "hours"),
                t.TryGetProperty("running", out var r) && r.ValueKind == JsonValueKind.True,
                Num(t, "amount")));
        }
        return new GsxBilling { Timers = list };
    }

    internal static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static double Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}

public sealed record GsxReceiptLine(string Label, double Amount);

/// <summary>
/// A completed-service invoice. The <c>logo</c> member is a base64 image and is
/// deliberately never read — it has no screen-reader value.
/// </summary>
public sealed class GsxReceipt
{
    public string Operator { get; private init; } = "";
    public double Total { get; private init; }
    public IReadOnlyList<GsxReceiptLine> Lines { get; private init; } = Array.Empty<GsxReceiptLine>();

    public static GsxReceipt? Parse(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return null;

        var lines = new List<GsxReceiptLine>();
        if (v.TryGetProperty("lines", out var ls) && ls.ValueKind == JsonValueKind.Array)
            foreach (var l in ls.EnumerateArray())
                if (l.ValueKind == JsonValueKind.Object)
                    lines.Add(new GsxReceiptLine(GsxBilling.Str(l, "label") ?? "",
                                                 GsxBilling.Num(l, "amount")));

        return new GsxReceipt
        {
            Operator = GsxBilling.Str(v, "operator") ?? "",
            Total = GsxBilling.Num(v, "total"),
            Lines = lines,
        };
    }
}

using System.Text.Json;

namespace MSFSBlindAssist.Services.Gsx.Remote;

public sealed record GsxBillingTimer(string SubService, string Friendly, double Hours,
                                     bool Running, double Amount);

/// <summary>One charge line inside a <see cref="GsxBillingBuilder"/>. GSX names the
/// text member <c>description</c> (not <c>label</c>).</summary>
public sealed record GsxBillingLine(string Description, double Amount);

/// <summary>
/// One already-totalled invoice section GSX publishes under <c>billing.builders</c>
/// — e.g. <c>{"friendly":"Ground Handling","subtotal":1761.42,"lines":[…]}</c>.
/// </summary>
public sealed record GsxBillingBuilder(string Friendly, double Subtotal,
                                       IReadOnlyList<GsxBillingLine> Lines);

/// <summary>
/// GSX's charges. Two independent halves:
/// <list type="bullet">
/// <item><c>timers</c> — persistent connections still RUNNING and accruing
///   (jetway, GPU, …). Drives the persistent-connection callouts.</item>
/// <item><c>builders</c> — the money already CHARGED, pre-totalled by GSX per
///   section. This is the ONLY place GSX publishes a figure for a completed
///   service: the <c>/receipt</c> frame carries none (see <see cref="GsxReceipt"/>).</item>
/// </list>
/// The two are parsed independently — a frame carrying only one of them must
/// not discard the other.
/// </summary>
public sealed class GsxBilling
{
    public IReadOnlyList<GsxBillingTimer> Timers { get; private init; } = Array.Empty<GsxBillingTimer>();
    public IReadOnlyList<GsxBillingBuilder> Builders { get; private init; } = Array.Empty<GsxBillingBuilder>();

    public bool AnyRunning => Timers.Any(t => t.Running);

    /// <summary>True when GSX has published at least one charged section.</summary>
    public bool HasBuilders => Builders.Count > 0;

    /// <summary>
    /// The session's charged total — the sum of every builder's own GSX-computed
    /// subtotal. Never speak a figure derived from anywhere else: this is the one
    /// number GSX actually states.
    /// </summary>
    public double BuildersTotal => Builders.Sum(b => b.Subtotal);

    public static readonly GsxBilling Empty = new();

    public static GsxBilling Parse(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return Empty;

        var timers = new List<GsxBillingTimer>();
        if (v.TryGetProperty("timers", out var ts) && ts.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in ts.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.Object) continue;
                timers.Add(new GsxBillingTimer(
                    Str(t, "subService") ?? "",
                    Str(t, "friendly") ?? "",
                    Num(t, "hours"),
                    t.TryGetProperty("running", out var r) && r.ValueKind == JsonValueKind.True,
                    Num(t, "amount")));
            }
        }

        var builders = new List<GsxBillingBuilder>();
        if (v.TryGetProperty("builders", out var bs) && bs.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in bs.EnumerateArray())
            {
                if (b.ValueKind != JsonValueKind.Object) continue;

                var lines = new List<GsxBillingLine>();
                if (b.TryGetProperty("lines", out var ls) && ls.ValueKind == JsonValueKind.Array)
                    foreach (var l in ls.EnumerateArray())
                        if (l.ValueKind == JsonValueKind.Object)
                            lines.Add(new GsxBillingLine(Str(l, "description") ?? "", Num(l, "amount")));

                builders.Add(new GsxBillingBuilder(Str(b, "friendly") ?? "", Num(b, "subtotal"), lines));
            }
        }

        if (timers.Count == 0 && builders.Count == 0) return Empty;
        return new GsxBilling { Timers = timers, Builders = builders };
    }

    internal static string? Str(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object
           && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static double Num(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object
           && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    internal static bool Bool(JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object
           && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}

/// <summary>
/// GSX's completed-service invoice NOTIFICATION.
///
/// The real <c>/receipt</c> frame carries exactly six members — <c>canPrint</c>,
/// <c>html</c>, <c>logo</c>, <c>operator</c>, <c>printPreview</c>, <c>printer</c>
/// — verified against the raw 2026-08 live capture. It carries NO <c>total</c>
/// and NO line items; an earlier model invented both and every invoice therefore
/// announced "Total 0.00" while the real charge (1761.42 in that capture) sat in
/// <c>billing.builders[].subtotal</c>. Source money from <see cref="GsxBilling"/>,
/// never from here.
///
/// <c>html</c> (the rendered invoice) and <c>logo</c> (a base64 image) are
/// deliberately never read — both are large blobs with no screen-reader value.
/// </summary>
public sealed class GsxReceipt
{
    /// <summary>The handling company that issued the invoice, e.g. "OneJet".</summary>
    public string Operator { get; private init; } = "";

    /// <summary>GSX's own "this invoice can be printed" flag.</summary>
    public bool CanPrint { get; private init; }

    public static GsxReceipt? Parse(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object) return null;

        return new GsxReceipt
        {
            Operator = GsxBilling.Str(v, "operator") ?? "",
            CanPrint = GsxBilling.Bool(v, "canPrint"),
        };
    }
}

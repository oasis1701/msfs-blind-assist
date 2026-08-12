using System.Text.Json;

namespace MSFSBlindAssist.Services.Gsx.Remote;

public enum GsxFieldType { Unknown, Toggle, Choice, Range, Text, Info, Separator }

public sealed record GsxSettingsButton(string Key, string Label, bool Disabled);
public sealed record GsxSettingsChoice(double Value, string Label);

/// <summary>One control on GSX's published settings page.</summary>
public sealed class GsxSettingsField
{
    public GsxFieldType Type { get; init; }
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Tooltip { get; init; } = "";

    public double? NumericValue { get; init; }
    public string? TextValue { get; init; }

    public IReadOnlyList<GsxSettingsChoice> Choices { get; init; } = Array.Empty<GsxSettingsChoice>();
    public double Min { get; init; }
    public double Max { get; init; }
    public double Step { get; init; }
    public string Unit { get; init; } = "";
    public bool IsFloat { get; init; }
    public int MaxLength { get; init; }
    public string Placeholder { get; init; } = "";
    public IReadOnlyList<GsxSettingsButton> Buttons { get; init; } = Array.Empty<GsxSettingsButton>();
}

public sealed class GsxSettingsSubtab
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public IReadOnlyList<GsxSettingsField> Fields { get; init; } = Array.Empty<GsxSettingsField>();
}

public sealed class GsxSettingsTab
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public IReadOnlyList<GsxSettingsSubtab> Subtabs { get; init; } = Array.Empty<GsxSettingsSubtab>();
}

/// <summary>
/// GSX's typed settings page. Replaces scraping GSX's settings HTML: every
/// control states its own type, bounds, unit and tooltip.
/// </summary>
public sealed class GsxSettingsSchema
{
    public IReadOnlyList<GsxSettingsTab> Tabs { get; private init; } = Array.Empty<GsxSettingsTab>();

    public static readonly GsxSettingsSchema Empty = new();

    public IEnumerable<GsxSettingsField> AllFields()
        => Tabs.SelectMany(t => t.Subtabs).SelectMany(s => s.Fields);

    public static GsxSettingsSchema Parse(JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.Object ||
            !v.TryGetProperty("tabs", out var tabs) || tabs.ValueKind != JsonValueKind.Array)
            return Empty;

        var list = new List<GsxSettingsTab>();
        foreach (var t in tabs.EnumerateArray())
        {
            if (t.ValueKind != JsonValueKind.Object) continue;
            var subs = new List<GsxSettingsSubtab>();
            if (t.TryGetProperty("subtabs", out var st) && st.ValueKind == JsonValueKind.Array)
                foreach (var s in st.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object) continue;
                    subs.Add(new GsxSettingsSubtab
                    {
                        Id = Str(s, "id") ?? "",
                        Label = Str(s, "label") ?? "",
                        Fields = ParseFields(s),
                    });
                }

            list.Add(new GsxSettingsTab
            {
                Id = Str(t, "id") ?? "",
                Label = Str(t, "label") ?? "",
                Subtabs = subs,
            });
        }
        return new GsxSettingsSchema { Tabs = list };
    }

    private static IReadOnlyList<GsxSettingsField> ParseFields(JsonElement subtab)
    {
        if (!subtab.TryGetProperty("fields", out var fs) || fs.ValueKind != JsonValueKind.Array)
            return Array.Empty<GsxSettingsField>();

        var list = new List<GsxSettingsField>();
        foreach (var f in fs.EnumerateArray())
        {
            if (f.ValueKind != JsonValueKind.Object) continue;

            var choices = new List<GsxSettingsChoice>();
            if (f.TryGetProperty("choices", out var cs) && cs.ValueKind == JsonValueKind.Array)
                foreach (var c in cs.EnumerateArray())
                    if (c.ValueKind == JsonValueKind.Array)
                    {
                        var pair = c.EnumerateArray().ToArray();
                        if (pair.Length >= 2 && pair[0].ValueKind == JsonValueKind.Number)
                            choices.Add(new GsxSettingsChoice(pair[0].GetDouble(),
                                                              pair[1].GetString() ?? ""));
                    }

            var buttons = new List<GsxSettingsButton>();
            if (f.TryGetProperty("buttons", out var bs) && bs.ValueKind == JsonValueKind.Array)
                foreach (var b in bs.EnumerateArray())
                    if (b.ValueKind == JsonValueKind.Object)
                        buttons.Add(new GsxSettingsButton(
                            Str(b, "key") ?? "", Str(b, "button") ?? "",
                            b.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True));

            JsonElement val = f.TryGetProperty("value", out var vv) ? vv : default;

            list.Add(new GsxSettingsField
            {
                Type = ParseType(Str(f, "type")),
                Key = Str(f, "key") ?? "",
                Label = Str(f, "label") ?? "",
                Tooltip = Str(f, "tooltip") ?? "",
                NumericValue = val.ValueKind == JsonValueKind.Number ? val.GetDouble() : null,
                TextValue = val.ValueKind == JsonValueKind.String ? val.GetString() : null,
                Choices = choices,
                Min = Num(f, "min"), Max = Num(f, "max"), Step = Num(f, "step"),
                Unit = Str(f, "unit") ?? "",
                IsFloat = f.TryGetProperty("float", out var fl) && fl.ValueKind == JsonValueKind.True,
                MaxLength = (int)Num(f, "maxlength"),
                Placeholder = Str(f, "placeholder") ?? "",
                Buttons = buttons,
            });
        }
        return list;
    }

    private static GsxFieldType ParseType(string? t) => t switch
    {
        "toggle" => GsxFieldType.Toggle,
        "choice" => GsxFieldType.Choice,
        "range" => GsxFieldType.Range,
        "text" => GsxFieldType.Text,
        "info" => GsxFieldType.Info,
        "separator" => GsxFieldType.Separator,
        _ => GsxFieldType.Unknown,
    };

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Num(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}

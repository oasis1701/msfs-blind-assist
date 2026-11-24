using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Reads and writes a <see cref="Bbs757EfbScreenModel"/> as JSON, so the tablet-reader fixtures are stored as the
/// page model the pilot is given rather than a screenshot. The fixture loader deserialises them, so the tests need no
/// capture and no recognition. The shape is deliberately plain — rectangles and points
/// as arrays, enums as their names — so a committed <c>.model.json</c> is easy to read and diff.
/// </summary>
internal static class Bbs757EfbModelJson
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    public static string Write(Bbs757EfbScreenModel m)
    {
        var o = new JsonObject
        {
            ["app"] = m.App,
            ["section"] = m.Section,
            ["scrollable"] = m.Scrollable,
            ["menu"] = Items(m.Menu),
            ["items"] = Items(m.Items),
            ["topBar"] = Items(m.TopBar),
            ["notices"] = new JsonArray(m.Notices.Select(n => (JsonNode)new JsonObject
            {
                ["id"] = n.Id,
                ["text"] = n.Text,
                ["time"] = n.Time,
            }).ToArray()),
            ["viewport"] = Rect(m.Viewport),
            ["keyboard"] = m.Keyboard is null ? null : new JsonObject
            {
                ["box"] = Rect(m.Keyboard.Box),
                ["keys"] = new JsonArray(m.Keyboard.Keys.Select(k => (JsonNode)new JsonObject
                {
                    ["name"] = k.Name, ["lower"] = k.Lower, ["upper"] = k.Upper, ["third"] = k.Third, ["hit"] = Pt(k.Hit),
                }).ToArray()),
            },
            ["question"] = m.Question is null ? null : new JsonObject
            {
                ["text"] = m.Question.Text,
                ["buttons"] = Items(m.Question.Buttons),
                ["rows"] = Items(m.Question.Rows),
            },
        };
        return o.ToJsonString(Pretty);
    }

    public static Bbs757EfbScreenModel Read(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        var question = o["question"] is JsonObject q
            ? new Bbs757EfbQuestion(Str(q, "text"), ReadItems(q["buttons"])) { Rows = ReadItems(q["rows"]) }
            : null;
        // Plan 18's models list their notifications top first, newest first, with no id: each is given the one its place says.
        var notices = o["notices"]?.AsArray() ?? new JsonArray();
        return new Bbs757EfbScreenModel(Str(o, "app"), Str(o, "section"), ReadItems(o["menu"]), ReadItems(o["items"]))
        {
            Scrollable = o["scrollable"]?.GetValue<bool>() ?? false,
            TopBar = ReadItems(o["topBar"]),
            Notices = notices.Select((n, index) => new Bbs757EfbNotice(n!.AsObject()["id"]?.GetValue<int>() ?? notices.Count - index,
                Str(n.AsObject(), "text"), n.AsObject()["time"]?.GetValue<string>())).ToList(),
            Question = question,
            Viewport = o["viewport"] is JsonArray vp ? ReadRect(vp) : Bbs757EfbScreen.ContentArea,
            Keyboard = o["keyboard"] is JsonObject kb
                ? new Bbs757EfbKeyboard(ReadRect(kb["box"]!.AsArray()),
                    kb["keys"]!.AsArray().Select(k => new Bbs757EfbKey(Str(k!.AsObject(), "name"), Str(k.AsObject(), "lower"),
                        Str(k.AsObject(), "upper"), Str(k.AsObject(), "third"), ReadPt(k.AsObject()["hit"]!.AsArray()))).ToList())
                : null,
        };
    }

    // ---- items ---------------------------------------------------------------

    private static JsonArray Items(IReadOnlyList<Bbs757EfbItem> items) =>
        new(items.Select(Item).ToArray());

    private static JsonNode Item(Bbs757EfbItem i)
    {
        var o = new JsonObject
        {
            ["kind"] = i.Kind.ToString(),
            ["text"] = i.Text,
            ["box"] = Rect(i.Box),
            ["hit"] = Pt(i.Hit),
            ["clickable"] = i.Clickable,
            ["key"] = i.Key,
            ["tint"] = i.Tint.ToString(),
            ["on"] = i.On,
            ["selected"] = i.Selected,
            ["value"] = i.Value,
            ["level"] = i.Level,
            ["label"] = i.Label,
            ["name"] = i.Name,
            ["slider"] = i.Slider is null ? null
                : new JsonArray(i.Slider.TrackLeft, i.Slider.TrackRight, i.Slider.KnobX),
        };
        // A drop-down's entries only when set, so every model written before plan 21 reads the same.
        if (i.Entries is { } entries) o["entries"] = new JsonArray(entries.Select(e => (JsonNode?)e).ToArray());
        return o;
    }

    private static IReadOnlyList<Bbs757EfbItem> ReadItems(JsonNode? array)
    {
        if (array is not JsonArray a) return Array.Empty<Bbs757EfbItem>();
        return a.Select(n =>
        {
            var o = n!.AsObject();
            var slider = o["slider"] is JsonArray s ? new Bbs757EfbSlider(s[0]!.GetValue<int>(), s[1]!.GetValue<int>(), s[2]!.GetValue<int>()) : null;
            return new Bbs757EfbItem(
                Enum.Parse<Bbs757EfbKind>(Str(o, "kind")),
                Str(o, "text"),
                ReadRect(o["box"]!.AsArray()),
                ReadPt(o["hit"]!.AsArray()),
                o["clickable"]!.GetValue<bool>(),
                Str(o, "key"),
                Enum.Parse<Bbs757EfbTint>(Str(o, "tint")),
                o["on"]?.GetValue<bool>(),
                o["selected"]!.GetValue<bool>(),
                o["value"]?.GetValue<string>() ?? "",
                slider,
                o["level"]?.GetValue<int>() ?? 0,
                Str(o, "label"),
                Str(o, "name"),
                Entries: o["entries"]?.AsArray().Select(e => e!.GetValue<string>()).ToList());
        }).ToList();
    }

    // ---- primitives ----------------------------------------------------------

    private static JsonArray Rect(Rectangle r) => new(r.X, r.Y, r.Width, r.Height);
    private static JsonArray Pt(Point p) => new(p.X, p.Y);
    private static Rectangle ReadRect(JsonArray a) => new(a[0]!.GetValue<int>(), a[1]!.GetValue<int>(), a[2]!.GetValue<int>(), a[3]!.GetValue<int>());
    private static Point ReadPt(JsonArray a) => new(a[0]!.GetValue<int>(), a[1]!.GetValue<int>());
    private static string Str(JsonObject o, string key) => o[key]?.GetValue<string>() ?? "";
}

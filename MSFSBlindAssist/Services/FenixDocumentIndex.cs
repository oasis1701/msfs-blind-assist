using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>One document as the gateway lists it. <c>Kind</c> is as served: "Markdown", or "Pdf".</summary>
public sealed record FenixDocumentItem(string Path, string Kind, string Source);

/// <summary>A node of the documents tree: a folder (no <see cref="Document"/>) or one document.</summary>
public sealed record FenixDocumentNode(string Name, string Path, FenixDocumentItem? Document, IReadOnlyList<FenixDocumentNode> Children)
{
    /// <summary>True for a folder; its <see cref="Path"/> is the folder path without the leading "fenix/".</summary>
    public bool IsFolder => Document is null;
}

/// <summary>The served document list: parsing, the folder tree in the EFB's order, and a path lookup.</summary>
public static class FenixDocumentIndex
{
    /// <summary>The folder the pilot's own documents live in; always present at the root of the tree.</summary>
    public const string UserDocumentsFolder = "my-documents";

    private const string LeadingFolder = "fenix";

    /// <summary>Reads <c>{"items":[...]}</c>; null when the text is anything else. An item without a string path is skipped.</summary>
    public static IReadOnlyList<FenixDocumentItem>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        JToken? root;
        // DateParseHandling.None: a date-shaped string stays a string.
        try { root = JsonConvert.DeserializeObject<JToken>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }); }
        catch (Exception) { return null; }
        if (root is not JObject || root["items"] is not JArray array) return null;

        var items = new List<FenixDocumentItem>();
        foreach (var token in array)
        {
            if (token is not JObject obj || obj["path"] is not { Type: JTokenType.String } path) continue;
            items.Add(new FenixDocumentItem(
                (string)path!,
                obj["kind"]?.Type == JTokenType.String ? (string)obj["kind"]! : "",
                obj["source"]?.Type == JTokenType.String ? (string)obj["source"]! : ""));
        }
        return items;
    }

    /// <summary>The EFB's tree: each level lists its folders (first seen first), then its documents (list order); an empty my-documents folder is added at the root if absent.</summary>
    public static IReadOnlyList<FenixDocumentNode> BuildTree(IReadOnlyList<FenixDocumentItem> items)
    {
        var root = new Folder("", "");
        foreach (var item in items)
        {
            var segments = FenixDocumentNames.Split(item.Path);
            int start = segments.Length > 0 && string.Equals(segments[0], LeadingFolder, StringComparison.Ordinal) ? 1 : 0;
            var folder = root;
            for (int i = start; i < segments.Length - 1; i++)
            {
                if (!folder.Folders.TryGetValue(segments[i], out var next))
                {
                    string path = folder.Path.Length == 0 ? segments[i] : folder.Path + "/" + segments[i];
                    next = new Folder(FenixDocumentNames.Humanize(segments[i]), path);
                    folder.Folders.Add(segments[i], next);
                    folder.Order.Add(next);
                }
                folder = next;
            }
            folder.Files.Add(new FenixDocumentNode(FenixDocumentNames.DisplayName(item.Path), item.Path, item, Array.Empty<FenixDocumentNode>()));
        }

        if (!root.Folders.ContainsKey(UserDocumentsFolder))
            root.Order.Add(new Folder(FenixDocumentNames.Humanize(UserDocumentsFolder), UserDocumentsFolder));
        return root.ToNodes();
    }

    /// <summary>Documents by path, "\" read as "/", case ignored; the first of two equal paths wins.</summary>
    public static IReadOnlyDictionary<string, FenixDocumentItem> ByPath(IReadOnlyList<FenixDocumentItem> items)
    {
        var map = new Dictionary<string, FenixDocumentItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items) map.TryAdd(item.Path.Replace('\\', '/'), item);
        return map;
    }

    private sealed class Folder(string name, string path)
    {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public Dictionary<string, Folder> Folders { get; } = new(StringComparer.Ordinal);
        public List<Folder> Order { get; } = new();
        public List<FenixDocumentNode> Files { get; } = new();

        public List<FenixDocumentNode> ToNodes()
        {
            var nodes = new List<FenixDocumentNode>();
            foreach (var f in Order) nodes.Add(new FenixDocumentNode(f.Name, f.Path, null, f.ToNodes()));
            nodes.AddRange(Files);
            return nodes;
        }
    }
}

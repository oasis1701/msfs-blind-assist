using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>One search hit as the window shows it. <c>Snippet</c> is plain text (the served markup removed).</summary>
public sealed record FenixDocumentSearchResult(string Path, string Name, string FolderTrail, string Snippet);

/// <summary>The gateway's document search answer: parsing it and the line spoken for one result.</summary>
public static class FenixDocumentSearch
{
    private static readonly Regex Tags = new("<[^>]*>", RegexOptions.CultureInvariant);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Reads a JSON array of hits; null when the text is anything else. An entry without a string path is skipped.
    /// The name comes from the path as the EFB's own list does, never from the served title.
    /// </summary>
    public static IReadOnlyList<FenixDocumentSearchResult>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        JToken? root;
        // DateParseHandling.None: a date-shaped string stays a string.
        try { root = JsonConvert.DeserializeObject<JToken>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }); }
        catch (Exception) { return null; }
        if (root is not JArray array) return null;

        var results = new List<FenixDocumentSearchResult>();
        foreach (var token in array)
        {
            if (token is not JObject obj || obj["path"] is not { Type: JTokenType.String } pathToken) continue;
            string path = (string)pathToken!;
            string? snippet = obj["snippet"]?.Type == JTokenType.String ? (string)obj["snippet"]! : null;
            results.Add(new FenixDocumentSearchResult(
                path,
                FenixDocumentNames.DisplayName(path),
                FenixDocumentNames.FolderTrail(path),
                PlainSnippet(snippet)));
        }
        return results;
    }

    /// <summary>The served snippet as plain text: tags removed, entities decoded, whitespace collapsed and trimmed.</summary>
    public static string PlainSnippet(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        string s = Tags.Replace(html, "");
        s = WebUtility.HtmlDecode(s);
        return Whitespace.Replace(s, " ").Trim();
    }

    /// <summary>The name, then ", " and the folder trail when there is one, then ": " and the snippet when there is one.</summary>
    public static string ResultText(FenixDocumentSearchResult r)
    {
        string text = r.Name;
        if (r.FolderTrail.Length > 0) text += ", " + r.FolderTrail;
        if (r.Snippet.Length > 0) text += ": " + r.Snippet;
        return text;
    }
}

namespace MSFSBlindAssist.Services;

/// <summary>The Fenix gateway's document endpoints (paths on the gateway, query escaped).</summary>
public static class FenixDocumentEndpoints
{
    /// <summary>The list of every document.</summary>
    public const string Index = "/fenix/documents";

    /// <summary>The shortest search term the window sends.</summary>
    public const int MinimumSearchLength = 3;

    /// <summary>The server-side search request for a term (trimmed, at most 10 hits).</summary>
    public static string Search(string term)
        => "/fenix/documents/search?q=" + Uri.EscapeDataString(term.Trim()) + "&limit=10";

    /// <summary>One document's content; each path segment is escaped, the slashes stay.</summary>
    public static string Content(string documentPath)
        => "/fenix/documents/content/" + string.Join("/", documentPath.Split('/').Select(Uri.EscapeDataString));
}

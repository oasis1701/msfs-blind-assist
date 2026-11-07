using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>The fixed texts of the Fenix documents window. Every number is formatted with the invariant culture.</summary>
public static class FenixDocumentTexts
{
    public const string Loading = "Loading Fenix documents...";
    /// <summary>F5 while a load runs: the Fenix options window's words.</summary>
    public const string StillLoading = "Still loading.";
    public const string NoAnswer = "The Fenix did not answer. Load the Fenix in the simulator, then press F5.";
    public const string TooShort = "Type at least 3 characters to search.";
    /// <summary>The EFB's own words.</summary>
    public const string NoResults = "No results found.";
    /// <summary>The EFB's own words.</summary>
    public const string EmptyFolder = "This folder is empty";
    public const string NotSent = "The Fenix did not send this document. Press Enter to try again.";
    public const string NotListed = "This document is no longer in the Fenix's list. Press F5.";
    public const string PdfNotReadable = "This is a PDF. The Fenix shows PDF pages only as pictures, so MSFS Blind Assist cannot read it.";
    public const string NoPins = "No pinned documents.";
    /// <summary>The reader's page (and the fallback's first line) before any document is open.</summary>
    public const string NoDocumentTitle = "No document open";
    public const string NoDocumentHint = "Choose a document in the list and press Enter.";

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    public static string Ready(int documents, int pinned)
        => $"{N(documents)} documents, {N(pinned)} pinned. Enter opens, F6 moves between the document, the list and the search box, Control+F searches, Control+P opens the pinned document, Control+D pins or unpins, Escape closes.";

    public static string Refreshed(int documents, int pinned)
        => $"Refreshed: {N(documents)} documents, {N(pinned)} pinned.";

    public static string PinnedNode(int n) => $"Pinned ({N(n)})";

    public static string ResultsNode(string term, int n) => $"Search results for \"{term}\" ({N(n)})";

    /// <summary>The document's name, then ", " and its folder trail when it has one, then ", " and the pin's label when it has one.</summary>
    public static string PinnedItem(string path, string? label)
    {
        string text = FenixDocumentNames.DisplayName(path);
        string trail = FenixDocumentNames.FolderTrail(path);
        if (trail.Length > 0) text += ", " + trail;
        if (!string.IsNullOrEmpty(label)) text += ", " + label;
        return text;
    }

    /// <summary>"Name. Folder trail." or "Name." for a document in no folder.</summary>
    public static string Opened(string path)
    {
        string name = FenixDocumentNames.DisplayName(path);
        string trail = FenixDocumentNames.FolderTrail(path);
        return trail.Length > 0 ? $"{name}. {trail}." : $"{name}.";
    }

    /// <summary>The tree node's name for a document, with " (PDF)" after a PDF's.</summary>
    public static string FileNode(FenixDocumentItem item)
    {
        string name = FenixDocumentNames.DisplayName(item.Path);
        return string.Equals(item.Kind, "Pdf", StringComparison.Ordinal) ? name + " (PDF)" : name;
    }
}

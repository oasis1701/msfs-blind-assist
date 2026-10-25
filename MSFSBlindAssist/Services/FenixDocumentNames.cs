using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The names the Fenix EFB shows for its documents and folders. <see cref="Humanize"/> is the EFB's own
/// pipe; everything here is ordinal and culture-invariant, whatever the PC's culture.
/// </summary>
public static class FenixDocumentNames
{
    private const string Sentinel = "\u0001";
    private const string LeadingFolder = "fenix";

    /// <summary>The EFB's name pipe: "__" is a slash, "_" a space, a hyphen after a lowercase letter a space, then each word's first letter in capitals.</summary>
    public static string Humanize(string? segment)
    {
        if (string.IsNullOrEmpty(segment)) return "";
        string s = segment.Replace("__", Sentinel, StringComparison.Ordinal);
        s = Regex.Replace(s, "_+", " ");
        s = Regex.Replace(s, "([a-z])-", "$1 ");
        s = Regex.Replace(s, @"\s+", " ");
        s = s.Replace(Sentinel, "/", StringComparison.Ordinal).Trim();
        return Regex.Replace(s, @"\b\w", m => m.Value.ToUpperInvariant(), RegexOptions.ECMAScript);
    }

    /// <summary>The document's title: its last segment without the extension, humanized.</summary>
    public static string DisplayName(string path)
    {
        string[] parts = Split(path);
        string last = parts.Length == 0 ? "" : parts[^1];
        int dot = last.LastIndexOf('.');
        if (dot > 0 && dot < last.Length - 1) last = last[..dot];
        return Humanize(last);
    }

    /// <summary>The folders above the document, without a leading "fenix", humanized and joined with " &gt; "; "" for none.</summary>
    public static string FolderTrail(string path)
    {
        string[] parts = Split(path);
        int start = parts.Length > 0 && string.Equals(parts[0], LeadingFolder, StringComparison.Ordinal) ? 1 : 0;
        var folders = new List<string>();
        for (int i = start; i < parts.Length - 1; i++) folders.Add(Humanize(parts[i]));
        return string.Join(" > ", folders);
    }

    internal static string[] Split(string? path)
        => (path ?? "").Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
}

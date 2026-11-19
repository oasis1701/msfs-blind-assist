using System.Net;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Turns a Fenix document's served HTML into a script-free reading page for a WebView2 with scripts off
/// (NVDA browse mode), and into a plain-text fallback. The Fenix EFB draws some things with a picture or a
/// script; both are replaced by text here. Everything is ordinal and culture-invariant.
/// </summary>
public static class FenixDocumentPage
{
    public const string ContentSecurityPolicy = "default-src 'none'; img-src http://127.0.0.1:8083; style-src 'unsafe-inline'";
    public const string Notice = "FOR FLIGHT SIMULATOR USE ONLY";

    private const RegexOptions Opts = RegexOptions.CultureInvariant;

    private static readonly Regex MetaTag = new("<meta\\b[^>]*>", Opts | RegexOptions.IgnoreCase);
    private static readonly Regex PhasesPicture = new(
        "<img\\b[^>]*\\bsrc=\"http://127\\.0\\.0\\.1:8083/fenix/documents/inhibit\\.png\\?phases=([^\"]*)\"[^>]*>", Opts);
    private static readonly Regex ChecklistLine = new(
        "<div class=\"checklist(?: checklist--no-value)?\"[^>]*><span>([^<]*)</span><span>([^<]*)</span></div>", Opts);
    private static readonly Regex ToggleAllButton = new(
        "<button\\b[^>]*data-checklist-action=\"toggle-all\"[^>]*>[^<]*</button>", Opts);
    private static readonly Regex DetailsOpen = new("<details\\b", Opts);
    private static readonly Regex ColourWord = new(
        "<(h[1-6]|p|span|strong)\\b([^>]*\\bclass=\"[^\"]*\\bdoc-color-([a-z]+)\\b[^\"]*\"[^>]*)>(.*?)</\\1>",
        Opts | RegexOptions.Singleline);
    private static readonly Regex LineBreak = new("<br\\s*/?>", Opts | RegexOptions.IgnoreCase);
    private static readonly Regex BlockClose = new(
        "</(?:p|div|h[1-6]|li|tr|summary|details|table|blockquote)\\s*>", Opts | RegexOptions.IgnoreCase);
    private static readonly Regex CellClose = new("</t[dh]\\s*>", Opts | RegexOptions.IgnoreCase);
    private static readonly Regex AnyTag = new("<[^>]*>", Opts);
    private static readonly Regex PhaseA = new("^(\\d+)a$", Opts);

    /// <summary>The <c>html</c> string of a content response, or null for bad JSON or no string <c>html</c>.</summary>
    public static string? ParseHtml(string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(contentJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("html", out var html)) return null;
            return html.ValueKind == System.Text.Json.JsonValueKind.String ? html.GetString() : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>The inert page: the policy is the first meta after the charset, the served markup lands in the body.</summary>
    public static string Build(string documentPath, string servedHtml)
        => Page(FenixDocumentNames.DisplayName(documentPath), Rewrite(servedHtml ?? "", documentPath, forText: false));

    /// <summary>The same inert page with a title and one paragraph of text (both encoded): what the reader shows before a document is open.</summary>
    public static string Placeholder(string title, string text)
        => Page(title, "<p>" + WebUtility.HtmlEncode(text ?? "") + "</p>");

    // The skeleton both pages share: the policy is the first meta after the charset; the title is encoded here.
    private static string Page(string title, string body)
        => "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">"
            + "<meta http-equiv=\"Content-Security-Policy\" content=\"" + ContentSecurityPolicy + "\">"
            + "<title>" + WebUtility.HtmlEncode(title ?? "") + "</title><style>" + Style + "</style></head><body>" + body + "</body></html>";

    /// <summary>The document as plain lines (non-empty, trimmed, joined with CRLF).</summary>
    public static string ToPlainText(string documentPath, string servedHtml)
    {
        string s = Rewrite(servedHtml ?? "", documentPath, forText: true);
        s = LineBreak.Replace(s, "\n");
        s = BlockClose.Replace(s, m => m.Value + "\n");
        s = CellClose.Replace(s, " ");
        s = AnyTag.Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        var lines = new List<string>();
        foreach (string raw in s.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0) lines.Add(line);
        }
        return string.Join("\r\n", lines);
    }

    // The order 1..6 matters: the colour rewrite runs after the checklist lines became plain markup.
    private static string Rewrite(string html, string documentPath, bool forText)
    {
        // 0. Served meta tags go. A meta refresh in a pilot's own Markdown is the one navigation the policy
        // cannot stop, and the reader would hand an http/https target to the default browser without a key
        // press. The page's own charset and policy metas are added after the rewrite, so they stay.
        // Each match becomes a space, not nothing: removing it could join its neighbours into a new meta
        // ("<me<meta>ta ..."); with the space the leftover "<me ta ...>" is an inert unknown element.
        string s = MetaTag.Replace(html, " ");

        // 1. Flight phases: the EFB draws them as a picture.
        s = PhasesPicture.Replace(s, m => "<span class=\"phases\">Flight phases: " + PhaseList(m.Groups[1].Value) + "</span>");

        // 2. Checklist lines: a tick box on the page, a plain line in the text.
        s = ChecklistLine.Replace(s, m =>
        {
            string label = m.Groups[1].Value;
            string value = m.Groups[2].Value;
            string line = value.Trim().Length == 0 ? label : label + ": " + value;
            return forText ? "<div>" + line + "</div>" : "<div class=\"checklist\"><label><input type=\"checkbox\"> " + line + "</label></div>";
        });

        // 3. Buttons that only work with the EFB's script.
        s = ToggleAllButton.Replace(s, "");

        // 4. Every section open (page only).
        if (!forText) s = DetailsOpen.Replace(s, "<details open");

        // 5. Colours said in words.
        s = ColourWord.Replace(s, "<$1$2>$4<span class=\"colour-word\"> ($3)</span></$1>");

        // 6. The notice the EFB adds under its own documents.
        if (documentPath.StartsWith("fenix/", StringComparison.Ordinal))
            s += "<p class=\"notice\">" + Notice + "</p>";
        return s;
    }

    private static string PhaseList(string raw)
    {
        var tokens = new List<string>();
        foreach (string part in raw.Split(','))
        {
            string t = part.Trim();
            if (t.Length == 0) continue;
            tokens.Add(PhaseA.Replace(t, "$1 + (a)"));
        }
        return tokens.Count == 0 ? "none" : string.Join(", ", tokens);
    }

    private const string Style = @"
  :root { color-scheme: light dark; }
  body { font-family: ""Segoe UI"", sans-serif; font-size: 15px; line-height: 1.5; margin: 8px 12px; background: #ffffff; color: #1a1a1a; }
  h1, h2, h3 { font-size: 1.15em; margin: 0.9em 0 0.4em; }
  h4, h5, h6 { font-size: 1em; margin: 0.8em 0 0.3em; }
  p, ul, ol { margin: 0.4em 0; }
  ul, ol { padding-left: 1.6em; }
  li { margin: 0.25em 0; }
  table { border-collapse: collapse; }
  th, td { border: 1px solid #c8c8c8; padding: 2px 6px; }
  .checklist { display: block; margin: 0.25em 0; }
  .doc-color-red { color: #b00020; }
  .doc-color-amber { color: #8a5a00; }
  .notice { margin-top: 1.2em; font-size: 0.9em; }
  @media (prefers-color-scheme: dark) {
    body { background: #1e1e1e; color: #e8e8e8; }
    th, td { border-color: #555555; }
    .doc-color-red { color: #ff8a80; }
    .doc-color-amber { color: #ffca4d; }
  }
";
}

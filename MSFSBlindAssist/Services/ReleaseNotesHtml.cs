using Markdig;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Renders a GitHub release body (Markdown) as the complete HTML document the update
/// dialog's WebView2 shows. Pure — no I/O, no UI — so the rendering rules are pinned by
/// <c>ReleaseNotesHtmlTests</c>.
///
/// The pipeline choices are deliberate:
///   - DisableHtml: a release body carries third-party text — GitHub's generated notes
///     quote contributor PR titles verbatim — so HTML-shaped input must render as escaped
///     text, never as markup. Scripting is additionally disabled on the WebView itself,
///     but this is the layer that makes injected markup inert everywhere.
///   - Soft line breaks stay SOFT (no SoftlineBreakAsHardlineBreak). Both of this repo's
///     note producers hand-wrap their Markdown at ~90 columns (see any changelog.d
///     fragment), and GitHub's Releases page renders those bodies as flowing paragraphs.
///     Hard-breaking each source newline reproduced the author's editor wrapping inside
///     the dialog's narrower box — every line wrapped once and left a short orphan line
///     (the "lines are way too short" report). Structure (paragraphs, bullets, headings)
///     is what fixes the old TextBox's run-on-line problem; hard breaks are not needed.
///   - AutoLinks: the generated notes end each bullet with a bare PR URL
///     ("... in https://github.com/.../pull/184"), which should be a real link.
/// </summary>
public static class ReleaseNotesHtml
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .Build();

    public static string Build(string? markdown)
    {
        var body = string.IsNullOrWhiteSpace(markdown)
            ? "<p>No release notes available.</p>"
            : Markdown.ToHtml(markdown, Pipeline);

        // The <title> is what a screen reader announces when focus enters the document.
        // Styling is minimal on purpose: system font, modest heading scale, and a
        // prefers-color-scheme block so the page follows the user's light/dark setting.
        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Release notes</title>
<style>
  :root { color-scheme: light dark; }
  body {
    font-family: "Segoe UI", sans-serif;
    font-size: 15px;
    line-height: 1.5;
    margin: 8px 12px;
    background: #ffffff;
    color: #1a1a1a;
  }
  h1, h2, h3 { font-size: 1.15em; margin: 0.9em 0 0.4em; }
  p, ul, ol { margin: 0.4em 0; }
  ul, ol { padding-left: 1.6em; }
  li { margin: 0.25em 0; }
  code {
    font-family: Consolas, monospace;
    font-size: 0.95em;
    background: #f0f0f0;
    padding: 0 3px;
    border-radius: 3px;
  }
  hr { border: none; border-top: 1px solid #c8c8c8; margin: 0.9em 0; }
  a { color: #0b57d0; }
  @media (prefers-color-scheme: dark) {
    body { background: #1e1e1e; color: #e8e8e8; }
    code { background: #333333; }
    hr { border-top-color: #555555; }
    a { color: #8ab4f8; }
  }
</style>
</head>
<body>
{{body}}
</body>
</html>
""";
    }
}

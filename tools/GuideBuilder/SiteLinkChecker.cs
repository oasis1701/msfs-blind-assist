using System.Net;
using System.Text.RegularExpressions;

namespace GuideBuilder;

/// <summary>
/// Checks the links in a built copy of the website: every internal link from a page under <c>under</c> must reach a
/// built file, and a #fragment an id in it. It also catches a "page.md" link the site's Jekyll did not turn into a
/// page address. Reads only the folder it is given, so the tests build one in a temporary directory.
/// </summary>
public static class SiteLinkChecker
{
    private static readonly Regex Href = new(@"\shref=""(?<href>[^""]*)""", RegexOptions.CultureInvariant);
    private static readonly Regex Id = new(@"\s(?:id|name)=""(?<id>[^""]*)""", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Check(string siteRoot, string under)
    {
        string root = Path.GetFullPath(siteRoot);
        string start = Path.GetFullPath(Path.Combine(root, under));
        var problems = new List<string>();
        var idCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        HashSet<string> IdsOf(string file)
        {
            if (!idCache.TryGetValue(file, out HashSet<string>? ids))
            {
                ids = Id.Matches(File.ReadAllText(file)).Select(m => WebUtility.HtmlDecode(m.Groups["id"].Value)).ToHashSet(StringComparer.Ordinal);
                idCache[file] = ids;
            }

            return ids;
        }

        if (!Directory.Exists(start))
        {
            problems.Add($"{under}: not found in the built site.");
            return problems;
        }

        foreach (string page in Directory.GetFiles(start, "*.html", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string shown = Path.GetRelativePath(root, page).Replace('\\', '/');
            foreach (Match m in Href.Matches(File.ReadAllText(page)))
            {
                string href = WebUtility.HtmlDecode(m.Groups["href"].Value);
                if (href.Length == 0 || GuideMarkdown.IsExternal(href) || href.StartsWith("//", StringComparison.Ordinal)) continue;

                (string path, string fragment) = GuideMarkdown.Split(href);
                int query = path.IndexOf('?');
                if (query >= 0) path = path[..query];

                if (path.EndsWith(".md", StringComparison.Ordinal))
                {
                    problems.Add($"{shown}: links to {href}; the site left a page.md link unconverted (is jekyll-relative-links on?).");
                    continue;
                }

                string target = path.Length == 0
                    ? page
                    : Path.GetFullPath(path.StartsWith('/') ? Path.Combine(root, Uri.UnescapeDataString(path.TrimStart('/'))) : Path.Combine(Path.GetDirectoryName(page)!, Uri.UnescapeDataString(path)));
                if (Directory.Exists(target)) target = Path.Combine(target, "index.html");

                if (!File.Exists(target))
                {
                    problems.Add($"{shown}: links to {href}, which the built site does not have.");
                    continue;
                }

                if (fragment.Length > 0 && target.EndsWith(".html", StringComparison.OrdinalIgnoreCase) && !IdsOf(target).Contains(Uri.UnescapeDataString(fragment)))
                {
                    problems.Add($"{shown}: links to {href}, but that page has no id \"{fragment}\".");
                }
            }
        }

        return problems;
    }
}

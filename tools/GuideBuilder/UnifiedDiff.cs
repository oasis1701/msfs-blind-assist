namespace GuideBuilder;

/// <summary>Reads the added lines out of a unified diff (<c>git diff -U0</c>), per file. Pure.</summary>
public static class UnifiedDiff
{
    public static IReadOnlyList<AddedLine> AddedLines(string diffText)
    {
        var result = new List<AddedLine>();
        string? path = null;
        bool afterOldHeader = false;

        foreach (string raw in diffText.Replace("\r\n", "\n").Split('\n'))
        {
            // "+++ " is a file header only directly after a "--- " line; an added "++i;" reads "+++i;" and is text.
            if (afterOldHeader && raw.StartsWith("+++ ", StringComparison.Ordinal))
            {
                string target = raw[4..].Split('\t')[0];
                path = target == "/dev/null" ? null : target.StartsWith("b/", StringComparison.Ordinal) ? target[2..] : target;
                afterOldHeader = false;
                continue;
            }

            afterOldHeader = raw.StartsWith("--- ", StringComparison.Ordinal);
            if (afterOldHeader) continue;

            if (raw.StartsWith('+') && path is not null)
            {
                result.Add(new AddedLine(path, raw[1..]));
            }
        }

        return result;
    }
}

/// <summary>One line a PR adds, with the path of the file it is added to.</summary>
public sealed record AddedLine(string Path, string Text);

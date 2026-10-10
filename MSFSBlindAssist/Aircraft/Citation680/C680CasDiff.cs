namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// Turns two CAS snapshots into what to say. Pure. A message is keyed by (severity, text), so a
/// message whose severity changes is a clear of the old and a post of the new — the pilot hears
/// the escalation.
/// </summary>
public static class C680CasDiff
{
    public static (List<(string cls, string text)> posted, List<(string cls, string text)> cleared) Diff(
        IReadOnlyList<(string cls, string text)> before, IReadOnlyList<(string cls, string text)> after)
    {
        var b = new HashSet<(string, string)>(before);
        var a = new HashSet<(string, string)>(after);
        return (after.Where(x => !b.Contains(x)).Distinct().ToList(), before.Where(x => !a.Contains(x)).Distinct().ToList());
    }

    /// <summary>"warning: ENGINE FIRE L" as the agent renders it → (warning, ENGINE FIRE L); an unprefixed row is status.</summary>
    public static (string cls, string text) ParseRow(string row)
    {
        int i = row.IndexOf(": ", StringComparison.Ordinal);
        if (i > 0)
        {
            string cls = row.Substring(0, i);
            if (cls is "warning" or "caution" or "advisory" or "status") return (cls, row.Substring(i + 2));
        }
        return ("status", row);
    }

    public static string Phrase(string cls, string text, bool posted)
    {
        string word = cls switch { "warning" => "Warning", "caution" => "Caution", "advisory" => "Advisory", _ => "Status" };
        return posted ? $"{word}: {text}" : $"{word} cleared: {text}";
    }

    /// <summary>The window text: one line per message, warnings first, then cautions, advisories and status; "No messages" when empty.</summary>
    public static List<string> Lines(IReadOnlyList<(string cls, string text)> current)
    {
        if (current.Count == 0) return new List<string> { "No CAS messages" };
        int Rank(string c) => c switch { "warning" => 0, "caution" => 1, "advisory" => 2, _ => 3 };
        return current.OrderBy(x => Rank(x.cls)).Select(x => Phrase(x.cls, x.text, posted: true)).ToList();
    }
}

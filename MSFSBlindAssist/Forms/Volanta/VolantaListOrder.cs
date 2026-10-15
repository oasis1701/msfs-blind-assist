namespace MSFSBlindAssist.Forms.Volanta;

/// <summary>
/// The order the Volanta window's lists show their rows in. Pure, so the rule is tested. Normally the rows are
/// in the order given (nearest first). While the pilot is in a list (<c>hold</c>), the rows already shown keep
/// their places, those gone drop out, and new ones follow at the end in the given order: arrowing down then
/// always reaches the next row, never one that moved under the cursor.
/// </summary>
internal static class VolantaListOrder
{
    public static IReadOnlyList<T> Arrange<T>(IReadOnlyList<string> shownKeys, IReadOnlyList<T> sorted,
        Func<T, string> key, bool hold)
    {
        if (!hold || shownKeys.Count == 0) return sorted;
        var byKey = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var row in sorted) byKey.TryAdd(key(row), row);
        var shown = new HashSet<string>(shownKeys, StringComparer.Ordinal);
        var arranged = new List<T>(sorted.Count);
        foreach (string k in shownKeys)
            if (byKey.TryGetValue(k, out var row)) arranged.Add(row);
        foreach (var row in sorted)
            if (!shown.Contains(key(row))) arranged.Add(row);
        return arranged;
    }

    /// <summary>
    /// A key for each line of a details list, so the reader's place follows the FIELD, not the line number: the
    /// field name before ": " ("Ground speed: 380 knots" is "Ground speed"), a heading line ending in ":" as itself
    /// ("ATIS:"), any other line as "text". A repeated key gets its occurrence after it ("text#2", "text#3"), so
    /// the lines of an ATIS keep their order and every key is unique.
    /// </summary>
    public static IReadOnlyList<string> DetailKeys(IReadOnlyList<string> lines)
    {
        var keys = new List<string>(lines.Count);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            string field = colon > 0 ? line[..colon] : line.EndsWith(':') ? line : "text";
            int n = seen.TryGetValue(field, out int count) ? count + 1 : 1;
            seen[field] = n;
            keys.Add(n == 1 ? field : $"{field}#{n}");
        }
        return keys;
    }
}

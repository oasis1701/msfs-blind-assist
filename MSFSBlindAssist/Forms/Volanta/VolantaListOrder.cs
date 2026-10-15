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
}

namespace MSFSBlindAssist.Forms.Volanta;

/// <summary>A plan from <see cref="VolantaListOrder.PlanHeldUpdate"/>: the row indices to remove, highest first;
/// then how many rows to insert at the top (new status lines, arranged rows 0 to <see cref="InsertTop"/> - 1);
/// then the range of rows to rewrite in place (empty when <see cref="RewriteTo"/> is below <see cref="RewriteFrom"/>).</summary>
internal sealed record HeldUpdate(IReadOnlyList<int> Remove, int InsertTop, int RewriteFrom, int RewriteTo);

/// <summary>
/// The order the Volanta window's lists show their rows in. Pure, so the rule is tested. Normally the rows are
/// in the order given (nearest first). While the pilot is in a list (<c>hold</c>), the rows already shown keep
/// their places, those gone drop out, and new ones follow at the end in the given order: arrowing down then
/// always reaches the next row, never one that moved under the cursor. A status line (<see cref="StatusKeyPrefix"/>)
/// is the exception: it is always first.
/// </summary>
internal static class VolantaListOrder
{
    /// <summary>A row whose key starts with this is a status line, not an item ("Connected to Volanta, but it is
    /// minimized"): given first, it stays first, held or not, so the reader meets it at the top of the list.</summary>
    public const string StatusKeyPrefix = "\0status:";

    public static bool IsStatusKey(string key) => key.StartsWith(StatusKeyPrefix, StringComparison.Ordinal);

    public static IReadOnlyList<T> Arrange<T>(IReadOnlyList<string> shownKeys, IReadOnlyList<T> sorted,
        Func<T, string> key, bool hold)
    {
        if (!hold || shownKeys.Count == 0) return sorted;
        var byKey = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var row in sorted) byKey.TryAdd(key(row), row);
        var shown = new HashSet<string>(shownKeys, StringComparer.Ordinal);
        var arranged = new List<T>(sorted.Count);
        foreach (var row in sorted)
            if (IsStatusKey(key(row))) arranged.Add(row);
        foreach (string k in shownKeys)
            if (!IsStatusKey(k) && byKey.TryGetValue(k, out var row)) arranged.Add(row);
        foreach (var row in sorted)
            if (!IsStatusKey(key(row)) && !shown.Contains(key(row))) arranged.Add(row);
        return arranged;
    }

    /// <summary>
    /// How the list the reader is in reaches <paramref name="arrangedKeys"/> (<see cref="Arrange"/> with hold)
    /// without rewriting every row (VOL-9): the rows gone are removed, highest index first so each index still
    /// holds; new status lines are inserted at the top and the other new rows join at the end; and only the rows
    /// within <paramref name="window"/> of the reader's row are rewritten (<see cref="HeldUpdate.RewriteFrom"/> to
    /// <see cref="HeldUpdate.RewriteTo"/>, inclusive). The reader's row is where <paramref name="selectedKey"/>
    /// ends up, else their old row number, else the top. Null when the rows shown are not a held arrangement of
    /// these keys: then every row is rewritten.
    /// </summary>
    public static HeldUpdate? PlanHeldUpdate(IReadOnlyList<string> shownKeys, IReadOnlyList<string> arrangedKeys,
        string? selectedKey, int selectedIndex, int window)
    {
        var wanted = new HashSet<string>(arrangedKeys, StringComparer.Ordinal);
        var shown = new HashSet<string>(shownKeys, StringComparer.Ordinal);
        var remove = new List<int>();
        for (int i = shownKeys.Count - 1; i >= 0; i--)
            if (!wanted.Contains(shownKeys[i])) remove.Add(i);
        int insertTop = 0;
        while (insertTop < arrangedKeys.Count && IsStatusKey(arrangedKeys[insertTop]) && !shown.Contains(arrangedKeys[insertTop]))
            insertTop++;
        int kept = 0;
        foreach (string k in shownKeys)
        {
            if (!wanted.Contains(k)) continue;
            if (insertTop + kept >= arrangedKeys.Count || arrangedKeys[insertTop + kept] != k) return null;
            kept++;
        }
        if (arrangedKeys.Count == 0) return new HeldUpdate(remove, 0, 0, -1);
        int center = -1;
        if (selectedKey != null)
            for (int i = 0; i < arrangedKeys.Count && center < 0; i++)
                if (arrangedKeys[i] == selectedKey) center = i;
        if (center < 0) center = Math.Clamp(selectedIndex, 0, arrangedKeys.Count - 1);
        return new HeldUpdate(remove, insertTop, Math.Max(0, center - window), Math.Min(arrangedKeys.Count - 1, center + window));
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

namespace MSFSBlindAssist.Navigation;

/// <summary>
/// The pilot's per-row picks (hold shorts of every kind, Turn picks) are keyed by a row's position in
/// the clearance. When route loading removes rows from the clearance (a runway used as a taxi leg, a
/// first taxiway the route never drives [RTE-39]), every pick moves with the rows that remain, and a
/// pick on a removed row goes with it. One helper for every pick kind and every removal, so no map is
/// left keyed to rows that are no longer there.
/// </summary>
public static class ClearanceRows
{
    /// <summary>The first <paramref name="count"/> rows, for a removal from the front of the clearance.</summary>
    public static IReadOnlyCollection<int> Leading(int count) => Enumerable.Range(0, Math.Max(0, count)).ToList();

    /// <summary>The rows that remain once <paramref name="removedRows"/> (in the old numbering, any order)
    /// are gone, renumbered; null stays null.</summary>
    public static List<int>? Without(IReadOnlyList<int>? rows, IReadOnlyCollection<int> removedRows)
        => rows?.Where(row => !removedRows.Contains(row)).Select(row => Renumber(row, removedRows)).ToList();

    /// <inheritdoc cref="Without(IReadOnlyList{int}?, IReadOnlyCollection{int})"/>
    public static Dictionary<int, T>? Without<T>(IReadOnlyDictionary<int, T>? picks, IReadOnlyCollection<int> removedRows)
    {
        if (picks == null) return null;
        var result = new Dictionary<int, T>();
        foreach (var (row, pick) in picks)
        {
            if (removedRows.Contains(row)) continue;
            result[Renumber(row, removedRows)] = pick;
        }
        return result;
    }

    /// <summary>How many of the picks in <paramref name="pickRows"/> sit on one of
    /// <paramref name="removedRows"/>, and so go with it.</summary>
    public static int CountOn(IReadOnlyCollection<int> removedRows, params IEnumerable<int>?[] pickRows)
        => pickRows.Sum(rows => rows?.Count(removedRows.Contains) ?? 0);

    /// <summary>What the pilot hears when hold shorts went with a runway leg route loading skipped:
    /// a stop they set, and would otherwise expect, is not there. Null when none did.</summary>
    public static string? DroppedHoldShortsNote(int dropped) => dropped switch
    {
        <= 0 => null,
        1 => "The hold short set on a skipped runway leg was dropped.",
        _ => "The hold shorts set on skipped runway legs were dropped.",
    };

    private static int Renumber(int row, IReadOnlyCollection<int> removedRows) => row - removedRows.Count(r => r < row);
}

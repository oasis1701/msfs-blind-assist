namespace MSFSBlindAssist.Services;

/// <summary>
/// Which rows the Monitor Manager list (Ctrl+M) shows, by mute state. Checked = announcing
/// and unchecked = muted, so <see cref="Muted"/> keeps the rows whose key is in the
/// aircraft's disabled set and <see cref="Unmuted"/> keeps the rest.
/// </summary>
public enum MonitorFilterMode
{
    All,
    Muted,
    Unmuted
}

/// <summary>
/// One row of a Monitor Manager list: <paramref name="Key"/> is the variable key mutes are
/// stored under, <paramref name="Label"/> is what the pilot sees and searches (a definition's
/// DisplayName, or the raw key when it has none).
/// </summary>
public readonly record struct MonitorRow(string Key, string Label);

/// <summary>
/// Search + mute-state filtering for the Monitor Manager dialogs. Deliberately pure — no
/// WinForms, no settings access: the caller passes in the aircraft's live disabled-variable
/// collection, which keeps this half unit-testable and keeps the forms layer out of it.
///
/// The two filters compose with AND, and <see cref="Apply"/> never re-sorts: rows arrive
/// already sorted by label (see MonitorRowBuilder) and that order must survive every
/// keystroke.
/// </summary>
public static class MonitorVariableFilter
{
    /// <summary>True when <paramref name="row"/> satisfies BOTH the search text and the mode.</summary>
    public static bool Matches(MonitorRow row, string? search, MonitorFilterMode mode,
                               ICollection<string> disabled)
    {
        bool muted = disabled.Contains(row.Key);
        if (mode == MonitorFilterMode.Muted && !muted) return false;
        if (mode == MonitorFilterMode.Unmuted && muted) return false;

        string term = (search ?? string.Empty).Trim();
        if (term.Length == 0) return true;

        // OrdinalIgnoreCase, never ToLower()/ToLowerInvariant(): culture-sensitive folding
        // maps "I" to dotless "ı" under tr-TR and the search silently stops matching. This
        // repo has already paid for that lesson once (SayIntentionsCultureTests).
        return row.Label.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The rows to show, in the order they were given.</summary>
    public static List<MonitorRow> Apply(IReadOnlyList<MonitorRow> rows, string? search,
                                         MonitorFilterMode mode, ICollection<string> disabled)
    {
        var result = new List<MonitorRow>(rows.Count);
        foreach (var row in rows)
        {
            if (Matches(row, search, mode, disabled)) result.Add(row);
        }
        return result;
    }
}

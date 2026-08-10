using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Builds the row list every Monitor Manager dialog (Ctrl+M) shows, from an aircraft's
/// variable definitions. Replaces the five hand-rolled copies of this loop that had drifted
/// across the per-aircraft forms.
///
/// A variable is listed when it is Continuous AND IsAnnounced AND not
/// ExcludeFromMonitorManager. Those first two together are what puts a variable on the
/// auto-announce path; the third is the opt-out for variables that ride the continuous
/// stream for plumbing reasons but are never spoken individually (silent caches, detail vars
/// whose speech rides another entry) — a row for those would be a checkbox that does nothing.
/// </summary>
public static class MonitorRowBuilder
{
    public static List<MonitorRow> Build(IReadOnlyDictionary<string, SimVarDefinition> variables)
    {
        var rows = new List<MonitorRow>();
        foreach (var kv in variables)
        {
            var def = kv.Value;
            if (def.UpdateFrequency != UpdateFrequency.Continuous) continue;
            if (!def.IsAnnounced) continue;
            if (def.ExcludeFromMonitorManager) continue;
            rows.Add(new MonitorRow(kv.Key, LabelFor(kv.Key, def)));
        }

        // Sort by what the pilot actually reads. Ties break on the key: List.Sort is unstable
        // and a Dictionary has no guaranteed enumeration order, so without a tie-break two
        // same-labelled rows could swap places between runs.
        rows.Sort((a, b) =>
        {
            int byLabel = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            return byLabel != 0 ? byLabel : string.CompareOrdinal(a.Key, b.Key);
        });
        return rows;
    }

    /// <summary>The label for a row: the definition's DisplayName, or the raw key when it has
    /// none — which is also what keeps raw keys findable through the search box.</summary>
    public static string LabelFor(string key, SimVarDefinition def)
        => string.IsNullOrEmpty(def.DisplayName) ? key : def.DisplayName;
}

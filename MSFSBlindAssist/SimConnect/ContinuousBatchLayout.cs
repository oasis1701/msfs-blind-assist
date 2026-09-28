namespace MSFSBlindAssist.SimConnect;

/// <summary>
/// Which variables ride the continuous batches, in what order, and so which batch each lands in —
/// the layout <see cref="SimConnectManager"/>'s StartContinuousMonitoring registers, kept pure so the
/// batch a var lands in can be pinned. The order is load-bearing: SimConnect returns a data definition's
/// datums sorted by full name, so the struct slots are mapped in that same order. Anything that judges
/// one var against another at a batch END (the FCU callouts' health gate, pinned by
/// FcuHealthBatchMembershipTests) needs both in the same batch.
/// </summary>
internal static class ContinuousBatchLayout
{
    /// <summary>Datums per batch; each GenericBatchN struct holds exactly this many doubles.</summary>
    public const int BatchSize = 300;

    /// <summary>
    /// Continuous + IsAnnounced, not PMDG (read from the PMDG client data area) and not
    /// ExcludeFromBatch (streams on its own per-var subscription).
    /// </summary>
    public static bool RidesBatch(SimVarDefinition def) =>
        def.UpdateFrequency == UpdateFrequency.Continuous
        && def.IsAnnounced
        && def.Type != SimVarType.PMDGVar
        && !def.ExcludeFromBatch;

    /// <summary>The name registered with SimConnect: an L:var carries its "L:" prefix.</summary>
    public static string FullName(SimVarDefinition def) =>
        def.Type == SimVarType.LVar ? $"L:{def.Name}" : def.Name;

    /// <summary>
    /// The batch-covered variables in registration order: collected in the dictionary's own order,
    /// then sorted ordinally by <see cref="FullName"/> to match SimConnect's internal ordering.
    /// </summary>
    public static List<KeyValuePair<string, SimVarDefinition>> Order(IEnumerable<KeyValuePair<string, SimVarDefinition>> variables)
    {
        var continuousVariables = new List<KeyValuePair<string, SimVarDefinition>>();
        foreach (var kvp in variables)
        {
            if (RidesBatch(kvp.Value))
                continuousVariables.Add(kvp);
        }

        // CRITICAL: Sort variables alphabetically by FULL NAME (with prefix) to match SimConnect's internal ordering
        continuousVariables.Sort((a, b) => string.CompareOrdinal(FullName(a.Value), FullName(b.Value)));
        return continuousVariables;
    }

    /// <summary>The 1-based batch the var at <paramref name="orderIndex"/> in <see cref="Order"/> lands in.</summary>
    public static int BatchNumberOf(int orderIndex) => orderIndex / BatchSize + 1;

    /// <summary>Every batch-covered key's 1-based batch number (uncapped: past batch 5 nothing is registered).</summary>
    public static Dictionary<string, int> BatchNumbers(IEnumerable<KeyValuePair<string, SimVarDefinition>> variables)
    {
        var ordered = Order(variables);
        var result = new Dictionary<string, int>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
            result[ordered[i].Key] = BatchNumberOf(i);
        return result;
    }
}

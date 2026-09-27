namespace MSFSBlindAssist.Aircraft.MD11;

/// <summary>
/// The persisted half of the walker's polarity learning. Settings carry the node ids whose step
/// polarity is INVERTED (<see cref="Settings.UserSettings.Md11InvertedStepControls"/>); a
/// conventional control is simply absent, so an empty list is the state every install starts in.
/// Pure so it can be pinned; the definition wires <see cref="Md11SelectorWalker.LoadPolarity"/>
/// and <see cref="Md11SelectorWalker.SavePolarity"/> to it.
/// </summary>
public static class Md11PolarityStore
{
    /// <summary>true = conventional, false = inverted, null = never learned.</summary>
    public static bool? Load(IReadOnlyCollection<string> invertedIds, string nodeId)
        => invertedIds.Contains(nodeId, StringComparer.Ordinal) ? false : null;

    /// <summary>
    /// The list after recording <paramref name="conventional"/> for <paramref name="nodeId"/>: the
    /// SAME instance when nothing changes, a NEW list otherwise. The caller swaps the new list in
    /// rather than mutating the old one — the settings serializer may be reading it on another
    /// thread — and saves only when the instance changed.
    /// </summary>
    public static List<string> With(List<string> invertedIds, string nodeId, bool conventional)
    {
        bool listed = invertedIds.Contains(nodeId, StringComparer.Ordinal);
        if (conventional == !listed) return invertedIds;   // already recorded that way

        var updated = new List<string>(invertedIds);
        if (conventional) updated.RemoveAll(id => string.Equals(id, nodeId, StringComparison.Ordinal));
        else updated.Add(nodeId);
        return updated;
    }
}

namespace MSFSBlindAssist.FirstOfficer.Models;

/// <summary>
/// The text the First Officer window speaks when a checklist line is ticked by hand, and
/// shows on its status line. Pure, so the format is pinned by tests rather than by the form.
///
/// A hand tick speaks only what the screen reader cannot (CORE-7): NVDA already says
/// "checked" or "not checked" for the tree's own state image, so the tick adds the value the
/// First Officer reads for a line with a <see cref="ChecklistItem{TExec,TState}.LiveValue"/>
/// ("flaps 1") and nothing for a line without one. Repeating "{label}: checked" over NVDA's
/// own word was the double announcement CORE-7 forbids. The status line keeps the label and
/// the state: it is read on its own, away from the tree.
/// </summary>
public static class ChecklistItemSpeech
{
    /// <summary>What a hand tick adds to the screen reader's own "checked": the line's live
    /// value, or null when it has none.</summary>
    public static string? TickText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    public static string StatusText(string label, string? value, bool isChecked)
    {
        string status = isChecked ? "Complete" : "Incomplete";
        return string.IsNullOrWhiteSpace(value) ? $"{label} — {status}" : $"{label}, {value} — {status}";
    }
}

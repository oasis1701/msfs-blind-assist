namespace MSFSBlindAssist.FirstOfficer.Models;

/// <summary>
/// The text the First Officer window speaks when a checklist line is ticked by hand, and
/// shows on its status line. A line with a <see cref="ChecklistItem{TExec,TState}.LiveValue"/>
/// carries the value the First Officer reads ("Flaps setting: SET (both), flaps 1: checked");
/// without one the text is exactly what it was before live values existed. Pure, so the
/// format is pinned by tests rather than by the form.
/// </summary>
public static class ChecklistItemSpeech
{
    public static string TickText(string label, string? value, bool isChecked)
    {
        string status = isChecked ? "checked" : "unchecked";
        return string.IsNullOrWhiteSpace(value) ? $"{label}: {status}" : $"{label}, {value}: {status}";
    }

    public static string StatusText(string label, string? value, bool isChecked)
    {
        string status = isChecked ? "Complete" : "Incomplete";
        return string.IsNullOrWhiteSpace(value) ? $"{label} — {status}" : $"{label}, {value} — {status}";
    }
}

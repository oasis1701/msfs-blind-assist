namespace MSFSBlindAssist.Aircraft.Citation680;

/// <summary>
/// What the EFB window speaks after acting on a row. Pure, so it is testable without a form. Rows
/// come from coherent-c680-efb-agent.js: "[Label]" buttons, "Name: state" toggles and settings,
/// plain text otherwise.
/// </summary>
public static class C680EfbRows
{
    /// <summary>
    /// After acting on <paramref name="pressedRow"/>: a toggle or setting ("Wheel Chocks: on") speaks
    /// the row that now carries the same name ("Wheel Chocks: off"), wherever it moved to; a button
    /// speaks its label, with ", done" when the press removed the button — Home's "[Remove Wheel
    /// Chocks]" disappears with its alert, and re-reading the same list position would speak an
    /// unrelated row.
    /// </summary>
    public static string SpokenAfterAct(string pressedRow, IReadOnlyList<string> newRows)
    {
        if (pressedRow.StartsWith("[", StringComparison.Ordinal))
        {
            // A choice button carries ", selected" when chosen ("[BeyondATC]" → "[BeyondATC, selected]"):
            // match on the label without it, and speak the label the button carries now.
            string label = pressedRow.Trim('[', ']');
            string bare = StripSelected(label);
            foreach (var r in newRows)
            {
                if (!r.StartsWith("[", StringComparison.Ordinal)) continue;
                string now = r.Trim('[', ']');
                if (string.Equals(StripSelected(now), bare, StringComparison.Ordinal)) return now;
            }
            return bare + ", done";
        }
        int colon = pressedRow.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0)
        {
            string name = pressedRow.Substring(0, colon + 2);
            var now = newRows.FirstOrDefault(r => r.StartsWith(name, StringComparison.Ordinal));
            if (now != null) return now;
        }
        return pressedRow;
    }

    private const string Selected = ", selected";

    private static string StripSelected(string label)
        => label.EndsWith(Selected, StringComparison.Ordinal) ? label.Substring(0, label.Length - Selected.Length) : label;
}

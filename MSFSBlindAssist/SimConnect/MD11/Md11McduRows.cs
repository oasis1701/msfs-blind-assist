namespace MSFSBlindAssist.SimConnect.MD11;

/// <summary>Which screen row a line of the MCDU window's list stands for.</summary>
public enum Md11McduRowKind
{
    Title,
    /// <summary>The small-font label above an LSK line. Dropped from the list when blank.</summary>
    Label,
    /// <summary>An LSK line, "n: …" — what Ctrl+n / Alt+n act on. Always listed, blank or not.</summary>
    Value,
    Scratchpad,
}

/// <summary>
/// One row of the window's list: its text and the screen row it stands for.
/// <paramref name="Line"/> is 1-6 for a label or value, 0 for the title and scratchpad.
/// </summary>
public readonly record struct Md11McduRow(string Text, Md11McduRowKind Kind, int Line);

/// <summary>
/// Turns an MCDU screen into the window's list rows, and says where the cursor goes after a redraw.
///
/// The list is NOT positionally stable, and that is deliberate: a blank label row is dropped
/// (a run of empty lines is noise to arrow through) while a blank VALUE row is kept (an empty
/// LSK line is a real, selectable state on a CDU). So the index of "6:" depends on how many of
/// the six labels above it are blank — measured on the live aircraft (2026-09-07 16:57): item 12
/// on the Left unit's ACT F-PLN page, item 7 on the Center unit's MENU page.
///
/// The window used to put the cursor back by INDEX after every redraw, which is what every
/// other CDU form in this app does — and on the F-PLN page it fails within a few slews: a
/// waypoint with no procedure label scrolls into one of the six pairs, "6:" moves up one item,
/// and the pilot reading line 6 is silently handed the scratchpad. Reported as "hitting Alt+Up
/// more than a few times will cause the focus to jump off of line 6". Hence
/// <see cref="Restore"/>: the cursor follows the ROW IT WAS ON, not the number it sat at.
/// </summary>
public static class Md11McduRows
{
    /// <summary>The MD-11's MCDU is a 14-row grid: title, six label/value pairs, scratchpad.</summary>
    private const int LskRows = 6;

    private const int TitleRow = 0;
    private const int ScratchpadRow = Md11McduLayout.Rows - 1;   // 13

    /// <summary>
    /// The window's rows for <paramref name="screen"/>, in list order. The label row is
    /// unnumbered and sits above its value; the value row carries the LSK number, so "3:" is
    /// what Ctrl+3 / Alt+3 acts on.
    /// </summary>
    public static IReadOnlyList<Md11McduRow> Build(Md11McduScreen screen)
    {
        var rows = new List<Md11McduRow>(Md11McduLayout.Rows)
        {
            new($"Title: {screen.Lines[TitleRow].Trim()}", Md11McduRowKind.Title, 0),
        };

        for (int i = 0; i < LskRows; i++)
        {
            var label = screen.Lines[1 + 2 * i].TrimEnd();
            var value = screen.Lines[2 + 2 * i].TrimEnd();

            if (!string.IsNullOrWhiteSpace(label))
                rows.Add(new("   " + label, Md11McduRowKind.Label, i + 1));
            rows.Add(new($"{i + 1}: {value}", Md11McduRowKind.Value, i + 1));
        }

        rows.Add(new($"Scratchpad: {screen.Lines[ScratchpadRow].Trim()}", Md11McduRowKind.Scratchpad, 0));
        return rows;
    }

    /// <summary>
    /// The index in <paramref name="rows"/> the cursor should sit on after a redraw, given the
    /// row it was on before, or -1 to leave it alone (no previous row, or nothing to follow).
    ///
    /// A label whose row has vanished (it went blank) hands the cursor to the line it labelled:
    /// the value row is always present, and it is what the pilot was reading about.
    /// </summary>
    public static int Restore(IReadOnlyList<Md11McduRow> rows, Md11McduRow? previous)
    {
        if (previous is not { } wanted) return -1;

        int exact = IndexOf(rows, wanted.Kind, wanted.Line);
        if (exact >= 0) return exact;

        return wanted.Kind == Md11McduRowKind.Label
            ? IndexOf(rows, Md11McduRowKind.Value, wanted.Line)
            : -1;
    }

    /// <summary>
    /// Where the cursor lands when the PAGE changes, and when a redraw left nothing selected:
    /// LSK line 1's VALUE row, found by identity. Never "item 1" — the window used that, and item
    /// 1 is line 1's LABEL on every page whose first label is not blank (the F-PLN page's FROM
    /// header), a row no line-select key acts on. Falls back to the title row, then the first
    /// row; -1 for an empty list.
    /// </summary>
    public static int PageStart(IReadOnlyList<Md11McduRow> rows)
    {
        int line1 = IndexOf(rows, Md11McduRowKind.Value, 1);
        if (line1 >= 0) return line1;
        int title = IndexOf(rows, Md11McduRowKind.Title, 0);
        if (title >= 0) return title;
        return rows.Count > 0 ? 0 : -1;
    }

    private static int IndexOf(IReadOnlyList<Md11McduRow> rows, Md11McduRowKind kind, int line)
    {
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Kind == kind && rows[i].Line == line) return i;
        return -1;
    }
}

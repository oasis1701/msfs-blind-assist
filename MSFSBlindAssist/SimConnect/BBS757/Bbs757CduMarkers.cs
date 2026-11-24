using System.Text;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// Marks the selected option of a CDU row the way the other Boeing CDU windows do: "X " in front of it,
/// so a pilot who cannot see the highlight hears and reads which option is chosen. What marks a selection
/// was measured live in cruise on 2026-10-28 ([B757-56]): on eleven pages, then about 55 on a full tour, a modified LEGS page among them,
/// BlueBird's CDU sent only the colour <c>g</c> and never an inverse cell; a selection there is drawn as
/// text (<c>&lt;ACT&gt;</c>), which the window already reads, and size marks labels and units, never a
/// selection. So only an inverse run is marked (colour and size mark nothing), in case a page not read
/// there uses it. Pure.
/// </summary>
public static class Bbs757CduMarkers
{
    private const string Marker = "X ";

    // A braille line holds 40 cells and Build puts three in front of a label row (its indent) or a data
    // row ("n: "), so a marked row may be 37 cells and no more. Only a row past that loses markers; one
    // marker a gap cannot pay for (27 cells at most) and several together up to that keep theirs.
    private const int BrailleCells = 40;
    private const int RowPrefixCells = 3;
    private const int MaxCells = BrailleCells - RowPrefixCells;

    /// <summary>
    /// The row's text with "X " written in front of each inverse run, without moving anything after it.
    /// A run is a maximal stretch of inverse cells; one made only of blanks marks nothing, and a run of
    /// several words gets one marker, in front of its first character. The marker takes the two blanks
    /// in front of that character when they are free (and, after text, one blank more stays between that
    /// text and the "X", so "&lt;FMC   &lt;ACT&gt;" reads "&lt;FMC X &lt;ACT&gt;"). When the gap is too narrow
    /// (the run is at column 0 or 1, or the text before it is close), the missing cells are inserted and
    /// paid back by the next gap with a blank to spare, as the FBW MCDU format does for its green
    /// selections (<c>FbwMcduFormat.PositionLine</c>); a gap always keeps one blank, so words never merge.
    /// Everything after a paying gap is back in its column, which a braille display depends on, and the
    /// text is longer only when no gap after the run can pay. Several runs go left to right, each paying
    /// its own debt. Markers are left off, the rightmost first, only when the row would pass 37 cells,
    /// which with Build's three-cell prefix is the 40 cells of a braille line.
    /// <para>
    /// A row with no inverse cell is exactly its characters, the line of the screen. The text is not
    /// trimmed: <see cref="Bbs757CduRows.Build"/> does that.
    /// </para>
    /// </summary>
    public static string Mark(IReadOnlyList<Bbs757CduCell> row)
    {
        string text = new(row.Select(c => c.Ch).ToArray());
        var marks = MarkedColumns(row);
        if (marks.Count == 0) return text;

        string marked = Lay(text, marks);
        while (marks.Count > 0 && marked.TrimEnd().Length > MaxCells)
        {
            marks.RemoveAt(marks.Count - 1);
            marked = marks.Count == 0 ? text : Lay(text, marks);
        }
        return marked;
    }

    /// <summary>The column of the first character of each inverse run that has one, left to right.</summary>
    private static List<int> MarkedColumns(IReadOnlyList<Bbs757CduCell> row)
    {
        var columns = new List<int>();
        int start = 0;
        while (start < row.Count)
        {
            if (!row[start].Inverse) { start++; continue; }
            int end = start;
            while (end < row.Count && row[end].Inverse) end++;
            for (int c = start; c < end; c++)
            {
                if (IsBlank(row[c].Ch)) continue;
                columns.Add(c);
                break;
            }
            start = end;
        }
        return columns;
    }

    /// <summary>
    /// <paramref name="text"/> with "X " inserted in front of each character at <paramref name="marks"/>,
    /// gap by gap: the blanks in front of a character are its gap, the gap shrinks by what the row still
    /// owes for earlier markers, and grows to fit the marker it carries. A gap after text keeps one blank.
    /// </summary>
    private static string Lay(string text, List<int> marks)
    {
        var sb = new StringBuilder(text.Length + Marker.Length * marks.Count);
        int owed = 0;    // cells inserted for markers and not yet paid back by a gap
        int start = 0;   // where the gap in front of the next character begins
        while (start < text.Length)
        {
            int end = start;
            while (end < text.Length && IsBlank(text[end])) end++;
            if (end == text.Length)
            {
                sb.Append(text, start, end - start);   // trailing blanks: nothing after them to move
                break;
            }

            bool marked = marks.Contains(end);
            int gap = end - start;
            // The least the gap can be: one blank between earlier text and what follows, and the marker.
            int least = (start > 0 && (gap > 0 || marked) ? 1 : 0) + (marked ? Marker.Length : 0);   // 1 only after text
            int width = Math.Max(least, gap - owed);
            owed += width - gap;

            sb.Append(' ', marked ? width - Marker.Length : width);
            if (marked) sb.Append(Marker);
            sb.Append(text[end]);
            start = end + 1;
        }
        return sb.ToString();
    }

    private static bool IsBlank(char c) => char.IsWhiteSpace(c);
}

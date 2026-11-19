using System.Globalization;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The rows of the Fenix DCDU window: one snapshot of the display channel turned into what the captain's DCDU draws, as
/// plain words laid out as the MCDU window is. Every line goes through <see cref="FenixChannelDcdu.Plain"/> and a blank
/// line makes no row.
/// </summary>
/// <remarks>
/// <para>No color is named: the owner ruled it for the DCDU (2026-11-03), over spec 2.6 and [FNX-24], as the MCDU window
/// names none. The format codes are read and dropped.</para>
/// <para>The rows run: the header on one row, the five message lines, the three info lines, then the key rows. Every line
/// the Fenix sends is 24 columns wide, and a row that holds a left and a right part lays them out on those 24 columns
/// with <see cref="FbwMcduFormat.PositionLine(string, string, string, int)"/>, the A32NX DCDU window's way. The header is
/// the left half at the left edge and the right half at the right edge, both trimmed ("2021Z FROM EDDF     OPEN"). A
/// message or info line keeps the spacing the Fenix drew inside it and loses its padding at the end. A key row is the row's
/// number first: a left key's label follows "&lt;" ("1 &lt;*UNABLE"), a right key's label ends with "&gt;" at the right edge
/// ("1 &lt;*UNABLE        STBY*&gt;"), and a row with a right key alone is its number and the label at the right edge. A key
/// row with no label on either side makes no row, so a key alone on a side still reads under its own number ([A320-16]).</para>
/// <para>A row that cannot hold both of its parts on the 24 columns is not clipped: its parts are joined by one space, so no
/// word is ever lost to the grid.</para>
/// <para>A screen with no row at all reads <see cref="NoMessageRow"/>, the A32NX window's words, at any brightness: a DCDU
/// turned all the way down still receives a message (measured 2026-11-03), so darkness says nothing about what is waiting. The
/// only figure the app writes is a key row's number, 1 or 2, so no row depends on the PC's culture.</para>
/// </remarks>
internal static class FenixChannelDcduRows
{
    /// <summary>The one row of a DCDU that draws nothing, in the A32NX DCDU window's words.</summary>
    public const string NoMessageRow = "(no CPDLC message displayed)";

    // The width of every line of the DCDU, and of the A32NX DCDU window.
    private const int Columns = 24;

    /// <summary>
    /// The window's rows for <paramref name="s"/>: <see cref="RowsOf"/> of the captain's DCDU, or <see cref="NoMessageRow"/>
    /// when it draws nothing. When the DCDU cannot be read the one status row, the section's problem, which also goes to
    /// <paramref name="reportProblem"/>.
    /// </summary>
    /// <param name="s">The snapshot to compose.</param>
    /// <param name="reportProblem">Where a problem the DCDU cannot be read for goes.</param>
    public static FenixChannelRows Compose(FenixChannelSnapshot s, Action<string>? reportProblem = null)
    {
        FenixChannelSection<FenixDcduScreen> section = FenixChannelDcdu.Read(s);
        if (!section.IsOk)
        {
            string problem = section.Problem ?? "no data";
            reportProblem?.Invoke(problem);
            return FenixChannelRows.NotAvailable(problem);
        }

        IReadOnlyList<string> rows = RowsOf(section.Value!);
        return FenixChannelRows.Of(rows.Count == 0 ? [NoMessageRow] : rows);
    }

    /// <summary>
    /// The rows <paramref name="screen"/> draws, in the order the class remarks give: none when every line is blank.
    /// </summary>
    public static IReadOnlyList<string> RowsOf(FenixDcduScreen screen)
    {
        var rows = new List<string>();

        if (HeaderText(screen) is { } header) rows.Add(header);

        rows.AddRange(LinesOf(screen.Message));
        rows.AddRange(LinesOf(screen.Info));

        // The first row of keys, then the second: each holds a left key and a right key.
        string?[] keys = [KeyRow(1, screen.LeftKey1, screen.RightKey1), KeyRow(2, screen.LeftKey2, screen.RightKey2)];
        rows.AddRange(keys.OfType<string>());

        return rows;
    }

    /// <summary>
    /// The header as one 24-column row: its left half at the left edge and its right half at the right edge, both trimmed
    /// ("2021Z FROM EDDF     OPEN"). A blank half leaves its side empty. Null when both are blank.
    /// </summary>
    public static string? HeaderText(FenixDcduScreen screen)
    {
        string left = FenixChannelDcdu.Plain(screen.HeaderLeft).Trim();
        string right = FenixChannelDcdu.Plain(screen.HeaderRight).Trim();

        return left.Length == 0 && right.Length == 0 ? null : OnGrid(left, right);
    }

    /// <summary>
    /// The first message line that has words, as a sentence (runs of spaces made one). Null when no message line has any:
    /// the header, the info lines and the keys are not the message.
    /// </summary>
    public static string? FirstMessageLine(FenixDcduScreen screen)
        => screen.Message.Select(line => FenixChannelDcdu.Spoken(line)).FirstOrDefault(line => line.Length > 0);

    // The lines that have words, each as the display drew it with its end trimmed.
    private static IEnumerable<string> LinesOf(IReadOnlyList<string> lines)
        => lines.Select(line => FenixChannelDcdu.Plain(line)).Where(line => line.Length > 0);

    // "1 <*UNABLE        STBY*>": the row's number, the left key's label after "<", and the right key's label with the
    // arrow the DCDU draws after it at the right edge. Null when neither side has a label.
    private static string? KeyRow(int row, string leftLabel, string rightLabel)
    {
        string left = FenixChannelDcdu.Plain(leftLabel).Trim();
        string right = FenixChannelDcdu.Plain(rightLabel).Trim();
        if (left.Length == 0 && right.Length == 0) return null;

        string number = row.ToString(CultureInfo.InvariantCulture);
        return OnGrid(left.Length == 0 ? number : $"{number} <{left}", right.Length == 0 ? "" : right + ">");
    }

    // The left text at the left edge of the 24 columns and the right text at the right edge. Two parts that do not fit with a
    // space between them, or a part wider than the grid, are joined by one space instead: the grid clips and lets one part
    // write over the other, and a word the pilot needs must not be lost to that.
    private static string OnGrid(string left, string right)
    {
        int gap = left.Length > 0 && right.Length > 0 ? 1 : 0;
        if (left.Length + gap + right.Length > Columns) return gap == 1 ? $"{left} {right}" : left + right;

        return FbwMcduFormat.PositionLine(left, "", right, Columns);
    }
}

namespace MSFSBlindAssist.Services;

/// <summary>
/// The rows of the Fenix DCDU window: one snapshot of the display channel turned into what the captain's DCDU draws,
/// worded for a screen reader. Every line goes through <see cref="FenixChannelSpeech"/> with its colors, and a blank
/// line makes no row.
/// </summary>
/// <remarks>
/// <para>The rows run: the header (its left half, then its right, as <see cref="HeaderText"/>), the five message lines,
/// the three info lines, then the soft keys top to bottom and left to right (L1, R1, L2, R2). A key keeps its slot number
/// and its side arrow with its label, before the spoken color, as the A32NX window shows its keys: a left key reads
/// "1 &lt;*UNABLE, cyan" and a right key "1 WILCO*&gt;, cyan". A key with no label makes no row, so a key alone on a side
/// still reads under its own number ([A320-16]).</para>
/// <para>A screen with no row at all reads <see cref="NoMessageRow"/>, the A32NX window's words. The only figure the
/// app writes is a key's slot number, 1 or 2, so no row depends on the PC's culture.</para>
/// </remarks>
internal static class FenixChannelDcduRows
{
    /// <summary>The one row of a DCDU that draws nothing, in the A32NX DCDU window's words.</summary>
    public const string NoMessageRow = "(no CPDLC message displayed)";

    /// <summary>
    /// The window's rows for <paramref name="s"/>: <see cref="RowsOf"/> of the captain's DCDU, or <see cref="NoMessageRow"/>
    /// when it draws nothing. When the DCDU cannot be read the one status row, the section's problem, which also goes to
    /// <paramref name="reportProblem"/>.
    /// </summary>
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

        rows.AddRange(screen.Message.Select(line => FenixChannelSpeech.Line(line)).OfType<string>());
        rows.AddRange(screen.Info.Select(line => FenixChannelSpeech.Line(line)).OfType<string>());

        // Top to bottom, left to right: the keys of the first row, then the keys of the second.
        string?[] keys =
        [
            LeftKeyRow(1, screen.LeftKey1), RightKeyRow(1, screen.RightKey1),
            LeftKeyRow(2, screen.LeftKey2), RightKeyRow(2, screen.RightKey2)
        ];
        rows.AddRange(keys.OfType<string>());

        return rows;
    }

    /// <summary>
    /// The header as one line: its left half and its right half spoken and joined by ", " ("1426Z FROM EDGG, white, OPEN,
    /// green"), or just the half that has words. Null when both are blank.
    /// </summary>
    public static string? HeaderText(FenixDcduScreen screen)
    {
        string[] halves = [.. new[] { screen.HeaderLeft, screen.HeaderRight }
            .Select(half => FenixChannelSpeech.Line(half)).OfType<string>()];

        return halves.Length == 0 ? null : string.Join(", ", halves);
    }

    /// <summary>
    /// The first message line that has words, spoken with its colors. Null when no message line has any: the header, the
    /// info lines and the keys are not the message.
    /// </summary>
    public static string? FirstMessageLine(FenixDcduScreen screen)
        => screen.Message.Select(line => FenixChannelSpeech.Line(line)).OfType<string>().FirstOrDefault();

    // "1 <*UNABLE, cyan": the slot, the arrow the DCDU draws before a left label and the label's speech. Null for a blank
    // label.
    private static string? LeftKeyRow(int slot, string label)
        => FenixChannelSpeech.Line(label) is { } spoken ? $"{slot} <{spoken}" : null;

    // U+2800, the blank braille cell the display uses as a spacer, which the parser reads as a space. Written as a cast
    // because the character cannot be seen in source.
    private const char BrailleBlank = (char)0x2800;

    // "1 WILCO*>, cyan": the slot and the label with the arrow the DCDU draws after a right label. The arrow is added to
    // the coded label so that it is spoken in the label's last stretch, but only to a label that has words: a blank one
    // makes no row, and the arrow alone never does. The label's trailing padding (spaces, the braille blank) is left off
    // first, so that the arrow follows the word: speech keeps one space, so "WILCO*   " + ">" would read "WILCO* >".
    private static string? RightKeyRow(int slot, string label)
        => FenixChannelSpeech.Line(label) is not null
            && FenixChannelSpeech.Line(label.TrimEnd(' ', BrailleBlank) + ">") is { } spoken
            ? $"{slot} {spoken}"
            : null;
}

namespace MSFSBlindAssist.Services;

/// <summary>
/// A column of the FMA, in the order the columns run along a row. <see cref="Unknown"/> is the column of every slot the
/// layout gives no place.
/// </summary>
internal enum FenixFmaColumn { Thrust, Vertical, Lateral, Approach, Engagement, Unknown }

/// <summary>
/// Where the FMA's slots sit by row and column, and how a column is worded. The PFD window and the FMA announcer both
/// word the FMA through <see cref="ColumnPhrase"/> and <see cref="NameOf"/>, so the window and the announcement never
/// say one column two ways.
/// </summary>
/// <remarks>
/// <para>The layout is inferred, not confirmed. It comes from the slots seen on 3.0.0.7613: the Thrust column's MACH,
/// MAN, FLX +56 and LVR CLB, the Vertical column's ALT CRZ, the Lateral column's NAV, and the Engagement column's AP1,
/// 1 FD 2 and A/THR. The Approach column was never seen. Plan 08, the FMA announcer, confirms the layout in the sim and
/// may change this file.</para>
/// <para>Slots 0 to 14 are three rows of five, row by row. A slot's row is its number divided by five, plus one (the rows
/// count from 1), and its column is the remainder, counted along <see cref="FenixFmaColumn"/>. A number outside 0 to 14
/// has no place: <see cref="Of"/> gives it row 0 and the <see cref="FenixFmaColumn.Unknown"/> column.</para>
/// <para>Row 3 of the Thrust, Vertical and Lateral columns is not part of its column. It holds the FMA's messages, such
/// as LVR CLB (<see cref="IsMessage"/>), and a column's phrase leaves it out. Row 3 of the Approach and the Engagement
/// columns belongs to its column.</para>
/// <para>The display draws a column in pieces, one for each row, so a column is spoken as one line from its rows, the way
/// <see cref="FenixChannelSpeech"/> speaks any text drawn in pieces. The Engagement column is not one text: its rows are
/// separate statuses (AP, FD and A/THR), so each row is a line of its own, and the lines are joined with "; ".</para>
/// </remarks>
internal static class FenixChannelFmaLayout
{
    private const int ColumnsPerRow = 5;
    private const int Rows = 3;
    private const int MessageRow = 3;

    // The columns in the order they run along a row. A slot's column is the entry at its number's remainder by five.
    private static readonly FenixFmaColumn[] ColumnOrder =
    [
        FenixFmaColumn.Thrust, FenixFmaColumn.Vertical, FenixFmaColumn.Lateral, FenixFmaColumn.Approach,
        FenixFmaColumn.Engagement
    ];

    /// <summary>
    /// The row (1 to 3) and the column of one slot of the FMA. Slot 15, and any other number outside 0 to 14, has no
    /// place in the layout: it is row 0 of the <see cref="FenixFmaColumn.Unknown"/> column.
    /// </summary>
    public static (int Row, FenixFmaColumn Column) Of(int slot)
    {
        if (slot < 0 || slot >= Rows * ColumnsPerRow) return (0, FenixFmaColumn.Unknown);

        return (slot / ColumnsPerRow + 1, ColumnOrder[slot % ColumnsPerRow]);
    }

    /// <summary>
    /// The name a column is spoken under: Thrust, Vertical, Lateral, Approach or Engagement. The Unknown column has
    /// none, and neither has a value the enum does not have: an empty string.
    /// </summary>
    public static string NameOf(FenixFmaColumn column) => column switch
    {
        FenixFmaColumn.Thrust => "Thrust",
        FenixFmaColumn.Vertical => "Vertical",
        FenixFmaColumn.Lateral => "Lateral",
        FenixFmaColumn.Approach => "Approach",
        FenixFmaColumn.Engagement => "Engagement",
        _ => ""
    };

    /// <summary>
    /// True for a slot that holds one of the FMA's messages, such as LVR CLB: row 3 of the Thrust, Vertical and Lateral
    /// columns. Row 3 of the Approach and the Engagement columns belongs to its column, and a number outside 0 to 14 is
    /// no message.
    /// </summary>
    public static bool IsMessage(int slot)
    {
        (int row, FenixFmaColumn column) = Of(slot);

        return row == MessageRow
            && column is FenixFmaColumn.Thrust or FenixFmaColumn.Vertical or FenixFmaColumn.Lateral;
    }

    /// <summary>
    /// One column of the FMA spoken as a line, or null when it has nothing to say. The Thrust, Vertical, Lateral and
    /// Approach columns speak the rows that are not messages, in row order, as the pieces of one line
    /// (<see cref="FenixChannelSpeech"/>'s <c>Line</c> of the rows' texts). The Engagement column speaks each row as a
    /// line of its own and joins them with "; ". Null when every row is blank.
    /// </summary>
    /// <remarks>
    /// <para>A slot past the end of the list counts as blank, so a short list is never an error: the frame's FMA holds
    /// none or all 16 slots today, and a later feature may pass what it has.</para>
    /// <para>The <see cref="FenixFmaColumn.Unknown"/> column has no phrase, and neither has a value the enum does not
    /// have.</para>
    /// <para>A row with a letter <see cref="FenixChannelText"/> does not know cannot be spoken, which is not an error
    /// here. A column drawn as one line is null then, because the line cannot be told. The Engagement column leaves that
    /// row out and speaks the others. A pilot never hears either of them, because the trust gate keeps the PFD untrusted
    /// while the FMA holds such a letter ([FNX-24]); the method only must not throw on it.</para>
    /// </remarks>
    public static string? ColumnPhrase(IReadOnlyList<FenixFmaSlot> slots, FenixFmaColumn column)
    {
        // The column's slot in row 1; its slots in rows 2 and 3 are five and ten after it.
        int rowOneSlot = Array.IndexOf(ColumnOrder, column);
        if (rowOneSlot < 0) return null;

        if (column == FenixFmaColumn.Engagement)
        {
            var statuses = new List<string>();
            for (int row = 0; row < Rows; row++)
            {
                string? status = FenixChannelSpeech.Line(TextAt(slots, rowOneSlot + row * ColumnsPerRow));
                if (status is not null) statuses.Add(status);
            }

            return statuses.Count == 0 ? null : string.Join("; ", statuses);
        }

        var parts = new List<string?>();
        for (int row = 0; row < Rows; row++)
        {
            int slot = rowOneSlot + row * ColumnsPerRow;
            if (!IsMessage(slot)) parts.Add(TextAt(slots, slot));
        }

        return FenixChannelSpeech.Line(parts);
    }

    // The text of one slot. A slot past the end of the list counts as blank, and so does one built without its text (a
    // struct's default).
    private static string? TextAt(IReadOnlyList<FenixFmaSlot> slots, int slot) =>
        slot < slots.Count ? slots[slot].Text : null;
}

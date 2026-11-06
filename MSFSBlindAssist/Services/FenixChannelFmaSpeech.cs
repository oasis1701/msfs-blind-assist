namespace MSFSBlindAssist.Services;

/// <summary>
/// What the pilot would hear of the FMA at one instant: one phrase for each spoken column and one line for each message,
/// null where blank. Two readings are equal when they would sound the same, so the change box, the flash and the size
/// and underline letters (which are never spoken) never make a difference.
/// </summary>
/// <remarks>
/// Both lists always have the same length for every reading (<see cref="FenixChannelFmaSpeech.Read"/> builds one entry
/// per spoken column and one per message slot), so two readings line up element by element.
/// </remarks>
internal sealed record FenixFmaReading(IReadOnlyList<string?> Columns, IReadOnlyList<string?> Messages)
{
    public bool Equals(FenixFmaReading? other) =>
        other is not null && SameLines(Columns, other.Columns) && SameLines(Messages, other.Messages);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (string? line in Columns) hash.Add(line, StringComparer.Ordinal);
        hash.Add(-1);
        foreach (string? line in Messages) hash.Add(line, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    private static bool SameLines(IReadOnlyList<string?> a, IReadOnlyList<string?> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        }

        return true;
    }
}

/// <summary>
/// The FMA as a pilot hears it: the Thrust, Vertical, Lateral and Approach columns, each one line through
/// <see cref="FenixChannelFmaLayout.ColumnPhrase"/>, and the messages of row 3. The Engagement column (AP, FD, A/THR; the
/// FCU lights already say them) and slots the layout gives no place are in no reading. A change is announced from two
/// readings, so only what is heard can make a change. While LAND, FLARE or ROLL OUT shows in the Vertical column, the
/// Lateral column says nothing: the Fenix keeps sending a lateral mode then that the PFD does not draw.
/// </summary>
internal static class FenixChannelFmaSpeech
{
    public const string ChangePrefix = "FMA: ";
    public const string MessagePrefix = "FMA message: ";

    public static IReadOnlyList<FenixFmaColumn> SpokenColumns { get; } =
        [FenixFmaColumn.Thrust, FenixFmaColumn.Vertical, FenixFmaColumn.Lateral, FenixFmaColumn.Approach];

    // The message slots, in column order and then slot order (the layout's own IsMessage names them).
    private static readonly int[] MessageSlots =
        Enumerable.Range(0, FenixChannelFields.FmaEntry.SlotCount)
            .Where(FenixChannelFmaLayout.IsMessage)
            .OrderBy(slot => FenixChannelFmaLayout.Of(slot).Column)
            .ThenBy(slot => slot)
            .ToArray();

    // The modes the real FMA draws across the Vertical and Lateral columns.
    private static readonly HashSet<string> CommonModes = new(StringComparer.Ordinal) { "LAND", "FLARE", "ROLL OUT" };

    // The Vertical column's row-1 slot, found through the layout.
    private static readonly int VerticalModeSlot =
        Enumerable.Range(0, FenixChannelFields.FmaEntry.SlotCount)
            .First(slot => FenixChannelFmaLayout.Of(slot) == (1, FenixFmaColumn.Vertical));

    /// <summary>
    /// Whether the PFD draws LAND, FLARE or ROLL OUT across the Vertical and Lateral columns, so no lateral mode is drawn.
    /// The announcer and the PFD window both read it, so the two never disagree about that slot.
    /// </summary>
    internal static bool ShowsCommonMode(IReadOnlyList<FenixFmaSlot> slots)
    {
        FenixTextLine line = FenixChannelText.Parse(slots[VerticalModeSlot].Text);
        if (line.UnknownCodes.Count > 0) return false;

        string mode = FenixChannelText.CollapseSpaces(line.JoinedText.Replace('!', ' '));
        return CommonModes.Contains(mode);
    }

    public static FenixFmaReading Read(IReadOnlyList<FenixFmaSlot> slots)
    {
        if (slots.Count != FenixChannelFields.FmaEntry.SlotCount)
        {
            throw new ArgumentException(
                $"The FMA has {FenixChannelFields.FmaEntry.SlotCount} slots, not {slots.Count}.", nameof(slots));
        }

        string?[] columns = SpokenColumns.Select(column => FenixChannelFmaLayout.ColumnPhrase(slots, column)).ToArray();

        // The real A320 draws LAND, FLARE and ROLL OUT across the Vertical and Lateral columns. The Fenix keeps sending
        // the lateral mode in the lateral slot underneath (its field 112 was 1 exactly while they showed, measured in
        // flight 2026-11-04), which the PFD does not draw, so the owner ruled it silent. Field 112 is not decoded, so the
        // three words are the signal. Recognizing the mode reads the parsed text; nothing is spoken from it.
        if (ShowsCommonMode(slots)) columns[SpokenColumns.ToList().IndexOf(FenixFmaColumn.Lateral)] = null;

        string?[] messages = MessageSlots.Select(slot => FenixChannelSpeech.Line(slots[slot].Text)).ToArray();

        return new FenixFmaReading(columns, messages);
    }

    /// <summary>
    /// The announcement for the change from one reading to the next, or null when nothing speakable changed. A column
    /// or message that went blank adds no part.
    /// </summary>
    public static string? Change(FenixFmaReading before, FenixFmaReading after)
    {
        var columnParts = new List<string>();
        for (int i = 0; i < SpokenColumns.Count; i++)
        {
            string? phrase = after.Columns[i];
            if (phrase is null || string.Equals(phrase, before.Columns[i], StringComparison.Ordinal)) continue;
            columnParts.Add($"{FenixChannelFmaLayout.NameOf(SpokenColumns[i])}: {phrase}");
        }

        var parts = new List<string>();
        if (columnParts.Count > 0) parts.Add(ChangePrefix + string.Join("; ", columnParts));

        for (int i = 0; i < after.Messages.Count; i++)
        {
            string? line = after.Messages[i];
            if (line is null || string.Equals(line, before.Messages[i], StringComparison.Ordinal)) continue;
            parts.Add(MessagePrefix + line);
        }

        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>
    /// The slots that hold text but have no column in the layout, as <c>slot 15 shows "wMORE DRAG"</c> (the coded text
    /// as sent), for the log. They are never spoken.
    /// </summary>
    public static IReadOnlyList<string> Unplaced(IReadOnlyList<FenixFmaSlot> slots)
    {
        var lines = new List<string>();
        for (int slot = 0; slot < slots.Count; slot++)
        {
            string text = slots[slot].Text;
            if (string.IsNullOrEmpty(text)) continue;
            if (FenixChannelFmaLayout.Of(slot).Column != FenixFmaColumn.Unknown) continue;
            lines.Add($"slot {slot} shows \"{text}\"");
        }

        return lines;
    }
}

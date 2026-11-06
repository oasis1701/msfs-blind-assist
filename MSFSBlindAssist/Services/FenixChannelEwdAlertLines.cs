namespace MSFSBlindAssist.Services;

/// <summary>
/// What a memo with a blank Title is called on the E/WD, and which texts its line may hold.
/// </summary>
internal readonly record struct FenixEwdMemoWords(string Text, IReadOnlyList<string> LineTexts);

/// <summary>
/// Finds the E/WD line of an ECAM entry's title, so the entry can be spoken in the color of its own line. It decides
/// which line or pair of lines holds the title and which color that stretch has; it reads nothing from the sim and
/// speaks nothing.
/// </summary>
/// <remarks>
/// <para>A title matches a line when the two are equal with the colors left out, a run of two or more dots counted as
/// one space and whitespace collapsed and trimmed, on both sides (measured: the LDG memo's Title "LDG LDG GEAR......DN"
/// stayed so while its line was "guLDGu LDG GEAR DN"). A single dot is text ("T.O"). A title the E/WD wraps matches two
/// consecutive lines joined with a space ("urCONFIGu " over "rFLAPS NOT IN T.O CONFIG"). The first match top to bottom in
/// the title's own column wins (failures in the left column, memos in the right), then the other column.</para>
/// <para>A title's words are <see cref="FenixChannelSpeech.Line(IReadOnlyList{string?})"/> of the found lines as its
/// parts, so a color never carries from one line to the next.</para>
/// </remarks>
internal static class FenixChannelEwdAlertLines
{
    /// <summary>
    /// The line as sent that holds the title, or the two consecutive lines of a title the E/WD wraps, in order. The own
    /// column is searched first, then the other. Null when nothing matches.
    /// </summary>
    public static IReadOnlyList<string>? Find(string title, IReadOnlyList<string> ownColumn,
        IReadOnlyList<string> otherColumn)
    {
        string wanted = Normalize(title);
        if (wanted.Length == 0) return null;

        return FindIn(wanted, ownColumn) ?? FindIn(wanted, otherColumn);
    }

    /// <summary>
    /// The color of the first stretch whose text is not whitespace, each line parsed on its own. Null when there is none,
    /// and when that stretch has no color.
    /// </summary>
    public static FenixTextColor? TitleColor(IReadOnlyList<string> lines)
    {
        foreach (string line in lines)
        {
            foreach (FenixTextRun run in FenixChannelText.Parse(line).Runs)
            {
                if (!string.IsNullOrWhiteSpace(run.Text)) return run.Color;
            }
        }

        return null;
    }

    /// <summary>
    /// What a memo with a blank Title is called, and which texts its line may hold, from the name after "MEMO-". The
    /// E/WD's own words differ from the entry's for some memos (owner's ruling, spec row 61, measured 2026-11-04). Only
    /// "AUTO BRK LO" was measured; the other two settings are the knob's others, and a text that never matches costs only
    /// a memo spoken without its color.
    /// </summary>
    public static FenixEwdMemoWords MemoWords(string name) => name switch
    {
        "FUEL < 3 T" => new("FOB BELOW 3 T", ["FOB BELOW 3 T"]),
        "T.CAS STBY" => new("TCAS STBY", ["TCAS STBY"]),
        "T.O. INHIBIT" => new("T.O INHIBIT", ["T.O INHIBIT"]),
        "SPEED BRAKE" => new("SPEED BRK", ["SPEED BRK"]),
        "AUTO BRK" => new("AUTO BRK", ["AUTO BRK LO", "AUTO BRK MED", "AUTO BRK MAX"]),
        _ => new(name, [name])
    };

    private static IReadOnlyList<string>? FindIn(string wanted, IReadOnlyList<string> column)
    {
        for (int i = 0; i < column.Count; i++)
        {
            string line = LineText(column[i]);
            if (string.Equals(line, wanted, StringComparison.Ordinal)) return [column[i]];

            if (i + 1 < column.Count
                && string.Equals(Normalize(line + " " + LineText(column[i + 1])), wanted, StringComparison.Ordinal))
            {
                return [column[i], column[i + 1]];
            }
        }

        return null;
    }

    private static string LineText(string coded) => Normalize(FenixChannelText.Parse(coded).JoinedText);

    // A run of two or more dots is one space, then white space is collapsed and trimmed.
    private static string Normalize(string text) => FenixChannelText.CollapseSpaces(DotRun.Replace(text, " "));

    private static readonly System.Text.RegularExpressions.Regex DotRun =
        new(@"\.{2,}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

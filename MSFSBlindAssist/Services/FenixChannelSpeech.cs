namespace MSFSBlindAssist.Services;

/// <summary>
/// Speaks the Fenix display channel's coded text with its colors. Every window row and every announcement that reads
/// channel text goes through this one function, so a line is worded the same wherever a pilot hears it ([FNX-24]).
/// </summary>
/// <remarks>
/// <para>A line of one color ends with it ("ENG 1 FIRE, red"). A line of several names each stretch's color after that
/// stretch's words, the stretches joined by ", " ("FOB: white, 4560 green, KG cyan"). Adjacent stretches of one color
/// merge into one, whatever else sets them apart (a box, flashing, a size, an underline, or an attribute a later
/// feature adds): stretches merge by color alone. The colors are amber, cyan, green, magenta, white and red. Yellow
/// has no name yet, because it is spoken only once a capture confirms what the display draws with it: a yellow
/// stretch is spoken without a color, as a stretch with no color is.</para>
/// <para>Sizes, underline, box and flashing are never spoken. <c>!</c> is a space, as the FMA draws one ("gALT!CRZ" is
/// "ALT CRZ, green"). Runs of spaces collapse to one and the ends are trimmed. A stretch of nothing is skipped. A
/// stretch of only spaces is skipped as well, so it is never named and does not stand between two stretches of one
/// color, but it still keeps the words on either side of it apart ("wAg wB" is "A B, white", never "AB").</para>
/// <para>A line with a letter <see cref="FenixChannelText"/> does not know is never spoken: it is drawn in a way the
/// app cannot tell, so its display is untrusted, and the speech refuses it a second time. A line with nothing to say
/// is not spoken either. Both come back as null.</para>
/// <para>Text the display draws in pieces is spoken as one line from its parts: the FMA's rows of one column, the
/// thrust limit's elements, a title the E/WD wraps onto two lines. Each part is parsed on its own, so no color carries
/// from one part to the next, and the parts' runs are joined in order with a space between parts.</para>
/// <para>The text before a part's first color letter is not colorless on the screen. The program's own code that draws
/// the channel's text starts every line in the color of the letter <c>w</c>: its three text routines all do (read on
/// the installed build 3.0.0.7613, 2026-11-03). So a part that holds a color letter speaks its leading colorless text
/// as white: "FLX c+56" is "FLX white, +56 cyan", and after a "wbMAN" part, "MAN FLX white, +56 cyan". Only the leading
/// text: a colorless run after a colored one, which only a line built by hand can have, stays colorless ("A green, B").
/// A part with no color letter at all, such as an ECAM entry's title, stays colorless too: nothing in its text says
/// what the display draws it in, and no color is ever guessed. A part holds a color letter when a run of the parsed
/// part has a color. A letter with no text after it leaves no such run, so "FLXc" stays colorless, which is the safe
/// direction.</para>
/// </remarks>
internal static class FenixChannelSpeech
{
    // The space between two parts. As a stretch of only a space it is never named and has no color of its own, but it
    // keeps the last word of one part and the first word of the next apart.
    private static readonly FenixTextRun PartBreak = new(" ", null, false, FenixTextSize.Normal, false, false);

    /// <summary>
    /// One coded text spoken as a line, the same as <c>Line([coded])</c>. Null for a null or blank text and for one
    /// with a letter the app does not know.
    /// </summary>
    public static string? Line(string? coded) => Line([coded]);

    /// <summary>
    /// Several coded texts spoken as one line, the way the display draws them in pieces. Each part is parsed on its
    /// own, so no color carries from one part to the next, the parts' runs are joined in order with a space between
    /// parts, and the result is spoken as one line. Null when every part is blank, or when any part holds a letter the
    /// app does not know.
    /// </summary>
    public static string? Line(IReadOnlyList<string?> parts)
    {
        var runs = new List<FenixTextRun>();
        foreach (string? part in parts)
        {
            FenixTextLine parsed = FenixChannelText.Parse(part);
            if (parsed.UnknownCodes.Count > 0) return null;

            AddPart(runs, parsed);
        }

        return Speak(runs);
    }

    /// <summary>
    /// A line that was parsed already (to read a number out of it, say), spoken as the one part it is, the same as its
    /// coded text. Null for a blank line and for one with a letter the app does not know.
    /// </summary>
    public static string? Line(FenixTextLine line)
    {
        if (line.UnknownCodes.Count > 0) return null;

        var runs = new List<FenixTextRun>();
        AddPart(runs, line);
        return Speak(runs);
    }

    /// <summary>
    /// The word that names a color after its stretch. Yellow has none, and neither has a value the enum does not have:
    /// the stretch is spoken without a color, so none is ever guessed.
    /// </summary>
    public static string ColorName(FenixTextColor color) => color switch
    {
        FenixTextColor.Amber => "amber",
        FenixTextColor.Cyan => "cyan",
        FenixTextColor.Green => "green",
        FenixTextColor.Magenta => "magenta",
        FenixTextColor.White => "white",
        FenixTextColor.Red => "red",
        FenixTextColor.Yellow => "",
        _ => ""
    };

    // Adds a part's runs after the ones before it, with the break between the parts. A part that holds a color speaks
    // the colorless runs before its first color as white (see the remarks); a colorless run after it, which only a line
    // built by hand can have (a color once set stays set: FenixChannelText's remarks), stays as it is. A part with no
    // color adds its runs as they are.
    private static void AddPart(List<FenixTextRun> runs, FenixTextLine part)
    {
        if (runs.Count > 0) runs.Add(PartBreak);

        bool holdsColor = part.Runs.Any(run => run.Color is not null);
        bool seenColor = false;
        foreach (FenixTextRun run in part.Runs)
        {
            seenColor |= run.Color is not null;
            runs.Add(holdsColor && !seenColor ? run with { Color = FenixTextColor.White } : run);
        }
    }

    // Merges the runs into stretches by color alone and words the result. Null when no stretch has a word in it.
    private static string? Speak(IReadOnlyList<FenixTextRun> runs)
    {
        var stretches = new List<(string Text, FenixTextColor? Color)>();

        foreach (FenixTextRun run in runs)
        {
            string text = run.Text.Replace('!', ' ');

            // A stretch of nothing is skipped, and nothing was drawn where it stood.
            if (text.Length == 0) continue;

            // A stretch of only spaces is skipped too: it has no color of its own to stand between two stretches of
            // one color. What it drew was a space, so it goes on the stretch before it (if there is one) to keep the
            // words apart.
            if (string.IsNullOrWhiteSpace(text))
            {
                if (stretches.Count > 0) stretches[^1] = (stretches[^1].Text + " ", stretches[^1].Color);
                continue;
            }

            if (stretches.Count > 0 && stretches[^1].Color == run.Color)
            {
                stretches[^1] = (stretches[^1].Text + text, run.Color);
            }
            else
            {
                stretches.Add((text, run.Color));
            }
        }

        if (stretches.Count == 0) return null;

        // A line of one color ends with it, after a comma. A line of several names each stretch's color straight after
        // its words and joins the stretches with commas.
        if (stretches.Count == 1)
        {
            string words = Tidy(stretches[0].Text);
            string name = NameOf(stretches[0].Color);
            return name.Length == 0 ? words : $"{words}, {name}";
        }

        return string.Join(", ", stretches.Select(stretch =>
        {
            string words = Tidy(stretch.Text);
            string name = NameOf(stretch.Color);
            return name.Length == 0 ? words : $"{words} {name}";
        }));
    }

    private static string NameOf(FenixTextColor? color) => color is { } named ? ColorName(named) : "";

    // Runs of white space become one space, and the ends are trimmed.
    private static string Tidy(string text) => FenixChannelText.CollapseSpaces(text);
}

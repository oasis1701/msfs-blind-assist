using System.Text;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The color a format code gives a run of channel text: the six letters <see cref="FenixMcduFormat"/> also
/// handles for the Fenix MCDU markup, and red, whose letter <c>r</c> was measured on the channel (a TO CONFIG
/// warning, 2026-11-02). The color is the meaning (a warning, a memo, an action), so it stays on the run as data.
/// </summary>
internal enum FenixTextColor { White, Green, Cyan, Amber, Magenta, Yellow, Red }

/// <summary>The size a format code gives a run: <c>s</c> small, <c>l</c> large, and Normal until one of them.</summary>
internal enum FenixTextSize { Normal, Small, Large }

/// <summary>
/// One stretch of a line drawn one way. <paramref name="Text"/> has its spacers as spaces and no code in it.
/// <paramref name="Color"/> is null for text that comes before any color code. <paramref name="Boxed"/> and
/// <paramref name="Flashing"/> are attributes like <paramref name="Underlined"/>, never colors: <c>b</c> draws a box
/// around a stretch (the FMA's MAN at take-off) and <c>f</c> makes it flash (the FMA's LVR MCT, the upper ECAM's
/// SHUT DOWN).
/// </summary>
internal readonly record struct FenixTextRun(string Text, FenixTextColor? Color, bool Underlined, FenixTextSize Size,
    bool Boxed, bool Flashing);

/// <summary>
/// One parsed line of channel text: its runs in order, and the letters it used as codes that the parser does
/// not know.
/// </summary>
/// <param name="Runs">The text in order, one run for each stretch drawn the same way. No run is empty.</param>
/// <param name="UnknownCodes">
/// Each lowercase letter the line used as a code that the app does not know, once and in the order it first
/// came. Such a letter is dropped from the text, so the line may be drawn in a way the app cannot tell: the
/// trust gate does not trust the display of a line that has one.
/// </param>
internal sealed record FenixTextLine(IReadOnlyList<FenixTextRun> Runs, IReadOnlyList<char> UnknownCodes)
{
    /// <summary>
    /// The runs' text joined, with the colors, underline, box, flashing and size left out. For parsing a number out
    /// of the line and for the log, never for speech: a read of channel text says each color (FNX-24).
    /// </summary>
    public string JoinedText => string.Concat(Runs.Select(run => run.Text));
}

/// <summary>
/// Parses one line of the Fenix display channel's text (an FMA slot, an upper ECAM line, the FOB text) into
/// runs that keep their colors. A lowercase letter before a run is a format code, not text: the color
/// letters <c>w g c a m y r</c> (white, green, cyan, amber, magenta, yellow, red), <c>u</c>, <c>b</c> and
/// <c>f</c> to toggle underline, a box and flashing, and <c>s</c> or <c>l</c> for a small or large size. The
/// letters <c>p</c> and <c>d</c> are known and draw nothing (<c>d</c> adds a space between the words either side of
/// it). Display text is uppercase, so an uppercase letter is never a code.
/// </summary>
/// <remarks>
/// <para>The colors are the meaning, so the codes are parsed into data and never stripped: a read that speaks
/// channel text says each run's color, and <see cref="FenixTextLine.JoinedText"/> is only for parsing a
/// number and for the log (FNX-24). This is not <see cref="FenixMcduFormat"/>, which turns the MCDU's markup
/// into plain text for a screen reader; the two share no code.</para>
/// <para>A line starts with no color, no underline, no box, no flashing and the normal size. Each code then
/// changes one of them and holds until another code changes it, so text before any color code has no color. The
/// run being built closes whenever a code changes the color, underline, box, flashing or size (<c>u</c>, <c>b</c>
/// and <c>f</c> always do, as they toggle), so a code that restates what the line is already in does not split it,
/// and no run is empty.</para>
/// <para>Two letters are known and are attributes that are never spoken: <c>p</c> and <c>d</c>. Neither is a
/// toggle, neither closes the run or changes its color, underline, box, flashing or size, and neither is kept on a
/// <see cref="FenixTextRun"/>, so no reader can speak one. <c>p</c> draws nothing and adds nothing to the run. It came
/// in the FMA's slot 1 (<c>bgpV/Scp+1800</c>) for the ten seconds after each of the two V/S reversions, not with every
/// flash of the flight director's bars, and in an E/WD memo (<c>pgIR IN ALIGN 1 MN</c>); the text stays drawn. <c>d</c>
/// moves the drawing position right and draws nothing (the FMA's <c>RADIOdc 200</c>, the ND's ground speed line),
/// and speech merges stretches by color alone, so it adds a space to the run being built: the words either side of
/// it stay two words ("GS 456"), and no run closes.</para>
/// <para>U+2800, the blank braille cell the display uses as a spacer, becomes a space. Any other lowercase
/// ASCII letter is a code the app does not know: it goes into <see cref="FenixTextLine.UnknownCodes"/>, once
/// and in order, and is dropped from the text. A lowercase letter outside a to z is text, not a code.</para>
/// </remarks>
internal static class FenixChannelText
{
    // U+2800. Named, and written as a cast, because the character cannot be seen in source.
    private const char BrailleBlank = (char)0x2800;

    private static readonly FenixTextLine EmptyLine = new(Array.Empty<FenixTextRun>(), Array.Empty<char>());

    /// <summary>
    /// Parses one coded line into runs. Null and an empty string are an empty line: no runs and no unknown
    /// codes.
    /// </summary>
    public static FenixTextLine Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return EmptyLine;

        var runs = new List<FenixTextRun>();
        List<char>? unknown = null;
        var text = new StringBuilder(raw.Length);

        FenixTextColor? color = null;
        bool underlined = false;
        bool boxed = false;
        bool flashing = false;
        FenixTextSize size = FenixTextSize.Normal;

        foreach (char c in raw)
        {
            // Only a to z are codes. Everything else is text, the braille blank as a space.
            if (c is < 'a' or > 'z')
            {
                text.Append(c == BrailleBlank ? ' ' : c);
                continue;
            }

            switch (c)
            {
                case 'w': SetColor(FenixTextColor.White); break;
                case 'g': SetColor(FenixTextColor.Green); break;
                case 'c': SetColor(FenixTextColor.Cyan); break;
                case 'a': SetColor(FenixTextColor.Amber); break;
                case 'm': SetColor(FenixTextColor.Magenta); break;
                case 'y': SetColor(FenixTextColor.Yellow); break;
                case 'r': SetColor(FenixTextColor.Red); break;
                case 'u':
                    CloseRun();
                    underlined = !underlined;
                    break;
                case 'b':
                    CloseRun();
                    boxed = !boxed;
                    break;
                case 'f':
                    CloseRun();
                    flashing = !flashing;
                    break;
                case 's': SetSize(FenixTextSize.Small); break;
                case 'l': SetSize(FenixTextSize.Large); break;
                case 'p':
                    // Known, and nothing to the run: it neither closes it, changes it nor draws a character.
                    break;
                case 'd':
                    // Moves the drawing position right and draws nothing, so the words either side of it are two words
                    // on the screen. Speech merges stretches by color alone, which would join them ("GS456"): the
                    // space keeps them apart. It closes no run.
                    text.Append(' ');
                    break;
                default:
                    // A letter the app does not know: listed once, and dropped from the text.
                    unknown ??= new List<char>();
                    if (!unknown.Contains(c)) unknown.Add(c);
                    break;
            }
        }

        CloseRun();

        IReadOnlyList<char> unknownCodes = unknown is null ? Array.Empty<char>() : unknown;
        return new FenixTextLine(runs, unknownCodes);

        void SetColor(FenixTextColor newColor)
        {
            if (newColor == color) return;
            CloseRun();
            color = newColor;
        }

        void SetSize(FenixTextSize newSize)
        {
            if (newSize == size) return;
            CloseRun();
            size = newSize;
        }

        // Ends the run being built as drawn so far. Nothing is added when no text came since the last code.
        void CloseRun()
        {
            if (text.Length == 0) return;
            runs.Add(new FenixTextRun(text.ToString(), color, underlined, size, boxed, flashing));
            text.Clear();
        }
    }
}

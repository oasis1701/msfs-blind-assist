using System.Text.Json;

namespace MSFSBlindAssist.SimConnect.BBS757;

/// <summary>
/// One cell of a CDU frame: its character and how the CDU draws it. <see cref="Color"/> is the frame's
/// one-letter colour (<c>g</c> green) or a space when the frame gave none; <see cref="Small"/> is the
/// small font (the CDU draws the label rows and the page counter in it); <see cref="Inverse"/> is the
/// inverse-video flag. A blank, missing or bad cell is <see cref="Blank"/>.
/// </summary>
public readonly record struct Bbs757CduCell(char Ch, char Color, bool Small, bool Inverse)
{
    /// <summary>A space with no colour, large and not inverse: the cell a frame leaves empty.</summary>
    public static Bbs757CduCell Blank { get; } = new(' ', ' ', false, false);
}

/// <summary>
/// One BlueBird 757 CDU screen as text: always <see cref="Bbs757CduFrame.Rows"/> lines of exactly
/// <see cref="Bbs757CduFrame.Columns"/> characters, blanks as spaces. Line 0 is the page title,
/// lines 1-12 the six label and data rows of the line select keys, line 13 the scratchpad.
/// <see cref="Cells"/> is the same screen cell by cell, with each cell's colour, size and inverse flag.
/// </summary>
public sealed record Bbs757CduScreen(IReadOnlyList<string> Lines)
{
    /// <summary>
    /// The screen's 14 rows of 24 cells; the character of <c>Cells[r][c]</c> is <c>Lines[r][c]</c>.
    /// <see cref="Bbs757CduFrame.Parse"/> gives the frame's real attributes. A screen made from lines
    /// alone (<c>new Bbs757CduScreen(lines)</c>, <see cref="Bbs757CduFrame.Blank"/>) has plain cells: no
    /// colour, large, not inverse. Lines short of 24 characters, or fewer than 14, are padded with blanks.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<Bbs757CduCell>> Cells { get; init; } = PlainCells(Lines);

    /// <summary>The page title, trimmed ("ACT RTE 1          1/3").</summary>
    public string Title => Lines[0].Trim();

    /// <summary>The scratchpad line, trimmed: an entry or a message ("NAV DATA OUT OF DATE").</summary>
    public string Scratchpad => Lines[Bbs757CduFrame.ScratchpadRow].Trim();

    private static Bbs757CduCell[][] PlainCells(IReadOnlyList<string> lines)
    {
        var cells = new Bbs757CduCell[Bbs757CduFrame.Rows][];
        for (int row = 0; row < cells.Length; row++)
        {
            string line = row < lines.Count ? lines[row] : "";
            cells[row] = new Bbs757CduCell[Bbs757CduFrame.Columns];
            for (int col = 0; col < cells[row].Length; col++)
                cells[row][col] = col < line.Length ? new Bbs757CduCell(line[col], ' ', false, false) : Bbs757CduCell.Blank;
        }
        return cells;
    }
}

/// <summary>
/// Reads a CDU frame from the 757's Comm Bus (<c>BBS_CDU_DISPLAY_1</c>/<c>_2</c>, see
/// docs/b757.md, "How the screens are read and the keys pressed"): MobiFlight WinCtrl JSON <c>{"Target":"Display","Data":[...]}</c>,
/// 14 rows of 24 cells row-major, a cell <c>[]</c> (blank) or <c>[char, color, size, inverse]</c>.
/// Each cell's character makes the screen's lines and the whole cell, with its colour, size and inverse
/// flag, makes <see cref="Bbs757CduScreen.Cells"/>. Pure, and it never throws: a short frame is padded,
/// extra cells are ignored, a bad cell or attribute reads as its default, and missing or malformed
/// JSON, or a payload for another target, is a blank screen.
/// </summary>
public static class Bbs757CduFrame
{
    public const int Rows = 14;
    public const int Columns = 24;
    public const int ScratchpadRow = 13;

    /// <summary>The CDU's entry-box glyph, read as an underscore so a screen reader names it.</summary>
    private const char BoxGlyph = '☐';

    private static readonly string BlankLine = new(' ', Columns);

    public static Bbs757CduScreen Blank { get; } = new(Enumerable.Repeat(BlankLine, Rows).ToArray());

    public static Bbs757CduScreen Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Blank;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Blank;
            if (root.TryGetProperty("Target", out var target) && TextOf(target) != "Display")
                return Blank;
            if (!root.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Array) return Blank;

            var grid = new Bbs757CduCell[Rows * Columns];
            Array.Fill(grid, Bbs757CduCell.Blank);
            int i = 0;
            foreach (var cell in data.EnumerateArray())
            {
                if (i >= grid.Length) break;
                grid[i++] = CellOf(cell);
            }

            var lines = new string[Rows];
            var cells = new Bbs757CduCell[Rows][];
            for (int row = 0; row < Rows; row++)
            {
                cells[row] = grid[(row * Columns)..((row + 1) * Columns)];
                lines[row] = new string(Array.ConvertAll(cells[row], c => c.Ch));
            }
            return new Bbs757CduScreen(lines) { Cells = cells };
        }
        catch (JsonException)
        {
            return Blank;
        }
    }

    /// <summary>
    /// A cell: <see cref="Bbs757CduCell.Blank"/> unless it is an array that starts with a string, then
    /// that string's character (<see cref="CharOf"/>) and the colour, size and inverse flag after it.
    /// Each attribute is read on its own, so a bad one gives its default and never blanks the character;
    /// an inverse space is kept as one, because a highlighted run has blanks inside it.
    /// </summary>
    private static Bbs757CduCell CellOf(JsonElement cell)
    {
        if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() == 0) return Bbs757CduCell.Blank;
        if (TextOf(cell[0]) is not { } text) return Bbs757CduCell.Blank;
        int length = cell.GetArrayLength();
        return new Bbs757CduCell(
            CharOf(text),
            length > 1 ? ColorOf(cell[1]) : ' ',
            length > 2 && IsOne(cell[2]),
            length > 3 && IsOne(cell[3]));
    }

    /// <summary>A cell's character: the first of its string, else a space.</summary>
    private static char CharOf(string? s)
    {
        if (string.IsNullOrEmpty(s)) return ' ';
        char c = s[0];
        if (c == BoxGlyph) return '_';
        if (char.IsControl(c) || char.IsSurrogate(c)) return ' ';
        return c;
    }

    /// <summary>A cell's colour: the first character of its string ("g" green), else a space.</summary>
    private static char ColorOf(JsonElement color)
    {
        string? s = TextOf(color);
        return string.IsNullOrEmpty(s) ? ' ' : s[0];
    }

    /// <summary>
    /// A JSON string's text, or null when the element is not a string or its text cannot be read:
    /// <c>GetString</c> throws <see cref="InvalidOperationException"/>, not a <see cref="JsonException"/>,
    /// for a lone surrogate escape (<c>"\uD83D"</c>), which must not cost the whole screen.
    /// </summary>
    private static string? TextOf(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.String) return null;
        try
        {
            return element.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Whether a size or inverse flag is set: the number 1 or JSON true, nothing else.</summary>
    private static bool IsOne(JsonElement flag) =>
        flag.ValueKind == JsonValueKind.True
        || (flag.ValueKind == JsonValueKind.Number && flag.TryGetDouble(out double d) && d == 1);

    /// <summary>
    /// The screen as the CDU window lists it, the texts of <see cref="Bbs757CduRows.Build"/> (the shape
    /// the other aircraft's CDU windows share: "Title: ...", label rows, "n:" data rows, "Scratchpad: ...";
    /// a blank screen is one row saying which CDU, <paramref name="cdu"/> 1 the captain's or 2 the
    /// first officer's).
    /// </summary>
    public static IReadOnlyList<string> ToDisplayLines(Bbs757CduScreen screen, int cdu = 1) =>
        Bbs757CduRows.Build(screen, cdu).Select(r => r.Text).ToList();
}

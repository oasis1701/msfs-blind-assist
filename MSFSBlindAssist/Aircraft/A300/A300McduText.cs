using System.Text;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>The A300's two MCDUs.</summary>
public enum A300McduUnit
{
    Captain = 1,
    FirstOfficer = 2,
}

/// <summary>One decoded MCDU screen: 14 rows of 24 columns, trailing blanks trimmed.</summary>
public sealed class A300McduScreen
{
    public A300McduUnit Unit { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();

    public string Title => Lines.Count > 0 ? Lines[0] : string.Empty;
    public string Scratchpad => Lines.Count > A300McduText.ScratchpadRow ? Lines[A300McduText.ScratchpadRow] : string.Empty;

    /// <summary>Every cell empty: an unpowered MCDU, which the aircraft clears to spaces.</summary>
    public bool IsBlank => Lines.All(string.IsNullOrWhiteSpace);

    public bool SameContent(A300McduScreen other) =>
        Unit == other.Unit && Lines.SequenceEqual(other.Lines, StringComparer.Ordinal);
}

/// <summary>
/// Decodes the A300's MCDU export: the SimConnect client data areas <c>iniAirbusMCDU_1</c> (captain)
/// and <c>iniAirbusMCDU_2</c> (first officer), 14 rows x 24 columns x 3 bytes = 1008 bytes. Each
/// cell is the character (ASCII, 0 = empty), a colour code and a small-font flag. The A300-600's
/// MCDU is monochrome, so the colour carries nothing and is not read.
///
/// The characters are what the aircraft's own MCDU fonts (INI_CDUA / INI_CDUB) draw, and a few of
/// those are symbols rather than letters — read from the font outlines (2026-10-03):
/// <list type="bullet">
/// <item><c>!</c> is an empty entry box (a field the MCDU wants filled): read as <c>#</c>, so a
/// box stays distinguishable from the dashes of an optional field.</item>
/// <item><c>^</c> is the degree sign; <c>a</c> and <c>l</c> are a left arrow, <c>b</c> and
/// <c>r</c> a right arrow (the "more pages" arrow beside a title); <c>{</c> and <c>}</c> are up
/// and down arrows; <c>~</c> is a triangle (delta).</item>
/// <item>Every other lowercase letter is drawn as its capital.</item>
/// </list>
/// Pure: the data manager hands it the bytes.
/// </summary>
public static class A300McduText
{
    public const int Rows = 14;
    public const int Cols = 24;
    public const int CellBytes = 3;
    public const int DataSize = Rows * Cols * CellBytes;   // 1008
    public const int ScratchpadRow = Rows - 1;

    public static string AreaName(A300McduUnit unit) => $"iniAirbusMCDU_{(int)unit}";

    /// <summary>The character a pilot reads for one cell.</summary>
    public static char Glyph(byte cell) => cell switch
    {
        0 => ' ',
        (byte)'!' => '#',
        (byte)'^' => '°',                  // degree sign
        (byte)'a' or (byte)'l' => '←',     // left arrow
        (byte)'b' or (byte)'r' => '→',     // right arrow
        (byte)'{' => '↑',                  // up arrow
        (byte)'}' => '↓',                  // down arrow
        (byte)'~' => 'Δ',                  // delta
        >= (byte)'a' and <= (byte)'z' => (char)(cell - 32),
        >= 32 and < 127 => (char)cell,
        _ => ' ',
    };

    /// <summary>Decodes one area; null when the payload is not the expected size.</summary>
    public static A300McduScreen? Decode(A300McduUnit unit, ReadOnlySpan<byte> data)
    {
        if (data.Length < DataSize)
            return null;
        var lines = new string[Rows];
        var sb = new StringBuilder(Cols);
        for (int row = 0; row < Rows; row++)
        {
            sb.Clear();
            for (int col = 0; col < Cols; col++)
                sb.Append(Glyph(data[(row * Cols + col) * CellBytes]));
            lines[row] = sb.ToString().TrimEnd();
        }
        return new A300McduScreen { Unit = unit, Lines = lines };
    }
}

/// <summary>What the MCDU window does with the frame a poll found.</summary>
public enum A300McduDisplayAction
{
    /// <summary>Draw the page.</summary>
    ShowContent,
    /// <summary>Keep the page and the cursor where they are: the frame is the FMS between pages.</summary>
    HoldLastPage,
    /// <summary>The MCDU has stayed blank: say so.</summary>
    ShowBlank,
    /// <summary>The window has just started following this unit on a blank it cannot judge yet.</summary>
    ShowWaiting,
}

/// <summary>
/// Tells the A300 FMS's blank frame between pages from a blank MCDU, the MD-11 window's rule
/// (<c>Md11McduPresence</c>). The FMS publishes an all-blank frame of 70 to 370 ms on every page key
/// (measured in flight, 2026-10-04: 60 of 363 frames during four key presses), and the window used to show
/// "is blank. It may be unpowered." on the first one, dropping the cursor. A blank is believed only
/// once it has lasted <see cref="SettleMs"/>; until then the page on screen is held. Pure: the
/// window hands it each poll's frame and the time.
/// </summary>
public sealed class A300McduBlankFilter
{
    /// <summary>About 2.7 times the longest blank frame measured, and four of the window's 250 ms
    /// polls, so a blank is never believed on one poll.</summary>
    public const int SettleMs = 1000;

    private DateTime? _blankSince;

    /// <summary>A poll's frame on the unit the window is following.</summary>
    public A300McduDisplayAction Judge(A300McduScreen screen, DateTime now)
    {
        if (!screen.IsBlank)
        {
            _blankSince = null;
            return A300McduDisplayAction.ShowContent;
        }
        _blankSince ??= now;
        return (now - _blankSince.Value).TotalMilliseconds >= SettleMs
            ? A300McduDisplayAction.ShowBlank
            : A300McduDisplayAction.HoldLastPage;
    }

    /// <summary>The window starts following a unit (an open, a re-show, a unit switch). Nothing on
    /// screen is this unit's page to hold, so a blank not yet judged shows the waiting row; the
    /// clock starts again, because while the window was not polling it could not see how long the
    /// unit had been blank.</summary>
    public A300McduDisplayAction Restart(A300McduScreen screen, DateTime now)
    {
        _blankSince = null;
        var action = Judge(screen, now);
        return action == A300McduDisplayAction.HoldLastPage ? A300McduDisplayAction.ShowWaiting : action;
    }
}

/// <summary>Which screen row a line of the MCDU window's list stands for.</summary>
public enum A300McduRowKind
{
    Title,
    /// <summary>The small label above an LSK line. Dropped from the list when blank.</summary>
    Label,
    /// <summary>An LSK line, "n: …" — what the line-select keys act on. Always listed.</summary>
    Value,
    Scratchpad,
}

/// <summary>One row of the window's list: its text and the screen row it stands for.</summary>
public readonly record struct A300McduRow(string Text, A300McduRowKind Kind, int Line);

/// <summary>
/// The window's rows for a screen, in the MD-11 window's form: the title, each non-blank label above
/// its numbered LSK line ("3: …", what Ctrl+3 / Alt+3 act on), and the scratchpad. Blank labels
/// are dropped, so a row's index moves between pages; the cursor is put back by the row it was on
/// (<see cref="Restore"/>), never by index.
/// </summary>
public static class A300McduRows
{
    private const int LskRows = 6;

    public static IReadOnlyList<A300McduRow> Build(A300McduScreen screen)
    {
        var rows = new List<A300McduRow>(A300McduText.Rows)
        {
            new($"Title: {screen.Title.Trim()}", A300McduRowKind.Title, 0),
        };
        for (int i = 0; i < LskRows; i++)
        {
            string label = screen.Lines[1 + 2 * i].TrimEnd();
            string value = screen.Lines[2 + 2 * i].TrimEnd();
            if (!string.IsNullOrWhiteSpace(label))
                rows.Add(new("   " + label, A300McduRowKind.Label, i + 1));
            rows.Add(new($"{i + 1}: {value}", A300McduRowKind.Value, i + 1));
        }
        rows.Add(new($"Scratchpad: {screen.Scratchpad.Trim()}", A300McduRowKind.Scratchpad, 0));
        return rows;
    }

    /// <summary>Where the cursor goes after a redraw on the same page: the row it was on, or the
    /// line a vanished label belonged to; -1 to leave it.</summary>
    public static int Restore(IReadOnlyList<A300McduRow> rows, A300McduRow? previous)
    {
        if (previous is not { } wanted)
            return -1;
        int exact = IndexOf(rows, wanted.Kind, wanted.Line);
        if (exact >= 0)
            return exact;
        return wanted.Kind == A300McduRowKind.Label ? IndexOf(rows, A300McduRowKind.Value, wanted.Line) : -1;
    }

    /// <summary>Where the cursor lands on a new page: line 1's value row, else the title.</summary>
    public static int PageStart(IReadOnlyList<A300McduRow> rows)
    {
        int line1 = IndexOf(rows, A300McduRowKind.Value, 1);
        if (line1 >= 0)
            return line1;
        int title = IndexOf(rows, A300McduRowKind.Title, 0);
        return title >= 0 ? title : rows.Count > 0 ? 0 : -1;
    }

    private static int IndexOf(IReadOnlyList<A300McduRow> rows, A300McduRowKind kind, int line)
    {
        for (int i = 0; i < rows.Count; i++)
            if (rows[i].Kind == kind && rows[i].Line == line)
                return i;
        return -1;
    }
}

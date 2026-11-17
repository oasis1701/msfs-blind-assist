using System.Text;
using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Plan 09 Task 4: an inverse run is a selected option, and the CDU window reads it with "X " in front
/// as the other Boeing windows do, without moving the row's later columns. BlueBird itself sent no
/// inverse cell on any page read (measured 2026-10-28, [B757-56]), so these rows are built here.
/// </summary>
public class Bbs757CduMarkersTests
{
    private static Bbs757CduScreen Fixture(string name) =>
        Bbs757CduFrame.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Encoding.UTF8));

    /// <summary>One 24-cell row: the text padded with blanks, the cells in the given ranges inverse.</summary>
    private static IReadOnlyList<Bbs757CduCell> Row(string text, params Range[] inverse)
    {
        string padded = text.PadRight(Bbs757CduFrame.Columns);
        var cells = padded.Select(c => new Bbs757CduCell(c, 'g', false, false)).ToArray();
        foreach (var range in inverse)
        {
            var (start, length) = range.GetOffsetAndLength(cells.Length);
            for (int i = start; i < start + length; i++)
                cells[i] = cells[i] with { Inverse = true };
        }
        return cells;
    }

    /// <summary>The screen with one row's text and cells replaced, so Lines and Cells still agree.</summary>
    private static Bbs757CduScreen With(Bbs757CduScreen screen, int row, string text, params Range[] inverse)
    {
        var lines = screen.Lines.ToArray();
        lines[row] = text.PadRight(Bbs757CduFrame.Columns);
        var cells = screen.Cells.ToArray();
        cells[row] = Row(text, inverse);
        return new Bbs757CduScreen(lines) { Cells = cells };
    }

    /// <summary>
    /// The inverse run reads with "X " immediately in front of its first character; the marker takes the
    /// blanks in front of it, so the row is still 24 cells. A run of several words is marked once.
    /// </summary>
    [Fact]
    public void Inverse_run_marked()
    {
        // The MENU page's row, <ACT> inverse: the marker takes two of the three blanks before it.
        Assert.Equal("<FMC X <ACT>        BBS>", Bbs757CduMarkers.Mark(Row("<FMC   <ACT>        BBS>", 7..12)));

        // Only BBS> inverse: it is the last field, with a wide gap in front of it.
        Assert.Equal("<FMC   <ACT>      X BBS>", Bbs757CduMarkers.Mark(Row("<FMC   <ACT>        BBS>", 20..24)));

        // A run of two words has one marker, in front of the first; the blanks before it stay a gap.
        Assert.Equal(" X ONE TWO".PadRight(Bbs757CduFrame.Columns), Bbs757CduMarkers.Mark(Row("   ONE TWO", 3..10)));

        // Two separate runs are both marked.
        Assert.Equal("<FMC X <ACT>      X BBS>", Bbs757CduMarkers.Mark(Row("<FMC   <ACT>        BBS>", 7..12, 20..24)));
    }

    /// <summary>
    /// A row with a marker keeps its right-hand values in their columns (plan 09 Review Focus): the marker
    /// takes blanks from the gap before it, or is inserted and paid back by the next gap with a blank to
    /// spare, which always keeps one so words never merge.
    /// </summary>
    [Fact]
    public void Columns_kept()
    {
        const string row = "<ACT>   <SEL>      EXIT>";
        int select = row.IndexOf("<SEL>", StringComparison.Ordinal);
        int exit = row.IndexOf("EXIT>", StringComparison.Ordinal);

        // A run at column 0 has no blanks in front of it: "X " is inserted and paid back by the gap after it.
        string atZero = Bbs757CduMarkers.Mark(Row(row, 0..5));
        Assert.Equal("X <ACT> <SEL>      EXIT>", atZero);
        Assert.Equal(select, atZero.IndexOf("<SEL>", StringComparison.Ordinal));
        Assert.Equal(exit, atZero.IndexOf("EXIT>", StringComparison.Ordinal));
        Assert.Equal(Bbs757CduFrame.Columns, atZero.Length);

        // At column 1 one blank is free and one is inserted.
        const string atOne = " <ACT>  <SEL>      EXIT>";
        string marked = Bbs757CduMarkers.Mark(Row(atOne, 1..6));
        Assert.Equal("X <ACT> <SEL>      EXIT>", marked);
        Assert.Equal(atOne.IndexOf("<SEL>", StringComparison.Ordinal), marked.IndexOf("<SEL>", StringComparison.Ordinal));
        Assert.Equal(atOne.IndexOf("EXIT>", StringComparison.Ordinal), marked.IndexOf("EXIT>", StringComparison.Ordinal));

        // A gap of one blank is too narrow for the marker: it is inserted in full and paid back further on.
        const string tight = "<A> <B>        LAST>";
        marked = Bbs757CduMarkers.Mark(Row(tight, 4..7));
        Assert.Equal("<A> X <B>      LAST>", marked.TrimEnd());
        Assert.Equal(tight.IndexOf("LAST>", StringComparison.Ordinal), marked.IndexOf("LAST>", StringComparison.Ordinal));

        // Several runs: each is marked left to right and pays its own debt.
        const string three = "<A>  <B>  <C>      LAST>";
        marked = Bbs757CduMarkers.Mark(Row(three, 0..3, 5..8, 10..13));
        Assert.Equal("X <A> X <B> X <C>  LAST>", marked);
        Assert.Equal(three.IndexOf("LAST>", StringComparison.Ordinal), marked.IndexOf("LAST>", StringComparison.Ordinal));
        Assert.Equal(Bbs757CduFrame.Columns, marked.Length);
    }

    /// <summary>
    /// Only when no gap after the run can pay is the row longer, by the marker's width, and the words
    /// before the run stay where they were.
    /// </summary>
    [Fact]
    public void Row_is_longer_only_when_no_gap_can_pay()
    {
        string marked = Bbs757CduMarkers.Mark(Row("<ACT><SEL>", 0..5));
        Assert.Equal("X <ACT><SEL>", marked.TrimEnd());
        Assert.Equal(Bbs757CduFrame.Columns + 2, marked.Length);

        // A run that starts inside a word still leaves a blank between that word and the marker.
        Assert.Equal("AB X CD", Bbs757CduMarkers.Mark(Row("ABCD", 2..4)).TrimEnd());
    }

    /// <summary>
    /// A selection keeps its marker whenever the row still fits a 40-cell braille line after Build's
    /// three-cell prefix ("n: " or the label's indent), even when no gap can pay and it grows past 26
    /// cells: a pilot who cannot see the highlight must not lose the "X" for a row the line has room for.
    /// </summary>
    [Fact]
    public void Marker_is_kept_while_the_row_fits_the_braille_line()
    {
        // '>' touching the text before it, in the last column: "X " and the separator blank make 27 cells.
        const string full = "ABCDEFGHIJKLMNOPQRSTUVW>";
        string marked = Bbs757CduMarkers.Mark(Row(full, 23..24));
        Assert.Equal("ABCDEFGHIJKLMNOPQRSTUVW X >", marked);
        Assert.Equal(27, marked.Length);

        // The same through Build: the row and its prefix are 30 cells.
        var screen = With(Fixture("bbs757-cdu-menu.json"), 2, full, 23..24);
        var value = Bbs757CduRows.Build(screen, 1).Single(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 1);
        Assert.Equal("1: ABCDEFGHIJKLMNOPQRSTUVW X >", value.Text);
    }

    /// <summary>
    /// Only past the braille line are markers left off, the rightmost first: a marker that no gap can pay
    /// for widens the row, and the row plus Build's three-cell prefix stays within 40 cells.
    /// </summary>
    [Fact]
    public void Markers_that_would_pass_the_braille_line_are_left_off()
    {
        // Twelve one-letter options, each its own inverse run in a single-blank gap: 12 markers would make 47 cells.
        const string letters = "A B C D E F G H I J K L";
        var inverse = Enumerable.Range(0, 12).Select(i => (i * 2)..(i * 2 + 1)).ToArray();

        string marked = Bbs757CduMarkers.Mark(Row(letters, inverse));

        // Seven markers make 37 cells; an eighth would make 39, and a value row's "n: " would pass 40.
        Assert.Equal("X A X B X C X D X E X F X G H I J K L", marked.TrimEnd());
        Assert.Equal(37, marked.TrimEnd().Length);

        var screen = With(Fixture("bbs757-cdu-menu.json"), 2, letters, inverse);
        var value = Bbs757CduRows.Build(screen, 1).Single(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 1);
        Assert.Equal(40, value.Text.Length);
    }

    /// <summary>
    /// A run of blanks only is no option: it gets no marker and the row reads as it does without it. In
    /// a run that starts with blanks the marker goes in front of the first character, and the run's
    /// own blanks are the room it takes.
    /// </summary>
    [Fact]
    public void Blank_inverse_unmarked()
    {
        Assert.Equal("<FMC   <ACT>        BBS>", Bbs757CduMarkers.Mark(Row("<FMC   <ACT>        BBS>", 4..7)));
        Assert.Equal(new string(' ', 24), Bbs757CduMarkers.Mark(Row("", 0..24)));
        Assert.Equal("AB  CD", Bbs757CduMarkers.Mark(Row("AB  CD", 2..4)).TrimEnd());

        // Blanks and text inverse together: the marker takes the run's leading blanks, the text keeps its column.
        string marked = Bbs757CduMarkers.Mark(Row("  ABC       ", 0..5));
        Assert.Equal("X ABC", marked.TrimEnd());
        Assert.Equal(2, marked.IndexOf('A'));
    }

    /// <summary>
    /// A row without an inverse cell is returned as it is: the same 24 characters as its line, so every
    /// page captured so far reads as it did before markers existed (colour and size mark nothing, the
    /// live measurement of 2026-10-28).
    /// </summary>
    [Theory]
    [InlineData("bbs757-cdu-menu.json")]
    [InlineData("bbs757-cdu-legs.json")]
    [InlineData("bbs757-cdu-legs-mod.json")]
    [InlineData("bbs757-cdu-vnav-crz.json")]
    [InlineData("bbs757-cdu-progress.json")]
    [InlineData("bbs757-cdu-navradio.json")]
    public void Rows_without_inverse_are_the_lines_unchanged(string fixture)
    {
        var screen = Fixture(fixture);

        Assert.DoesNotContain(screen.Cells.SelectMany(row => row), c => c.Inverse);
        for (int row = 0; row < Bbs757CduFrame.Rows; row++)
            Assert.Equal(screen.Lines[row], Bbs757CduMarkers.Mark(screen.Cells[row]));
    }

    /// <summary>An inverse option in a data row reads "n: ... X ..." and its right-hand value stays in its column.</summary>
    [Fact]
    public void Inverse_value_row_reads_with_the_marker_and_keeps_its_columns()
    {
        var menu = Fixture("bbs757-cdu-menu.json");
        const string line = "<FMC   <ACT>        BBS>";
        Assert.Equal(line, menu.Lines[2]);   // the MENU's first line select key row

        var rows = Bbs757CduRows.Build(With(menu, 2, line, 7..12), 1);

        var value = rows.Single(r => r.Kind == Bbs757CduRowKind.Value && r.Line == 1);
        Assert.Equal("1: <FMC X <ACT>        BBS>", value.Text);
        Assert.Equal("1: ".Length + line.IndexOf("BBS>", StringComparison.Ordinal), value.Text.IndexOf("BBS>", StringComparison.Ordinal));

        // The rest of the window is what it was.
        static IEnumerable<string> Others(IEnumerable<Bbs757CduRow> all) =>
            all.Where(r => !(r.Kind == Bbs757CduRowKind.Value && r.Line == 1)).Select(r => r.Text);
        var plain = Bbs757CduRows.Build(menu, 1);
        Assert.Equal(plain.Count, rows.Count);
        Assert.Equal(Others(plain), Others(rows));
    }

    /// <summary>A label row is marked too, and keeps the three-space indent in front of the marker.</summary>
    [Fact]
    public void Inverse_label_row_is_marked()
    {
        var rows = Bbs757CduRows.Build(With(Fixture("bbs757-cdu-menu.json"), 1, "SELECT   TYPE", 9..13), 1);

        Assert.Contains("   SELECT X TYPE", rows.Where(r => r.Kind == Bbs757CduRowKind.Label).Select(r => r.Text));
    }

    /// <summary>
    /// The title and the scratchpad are not marked (the page name and the typed entry are read as text),
    /// and a row of dashes is still the page's separator and is dropped (R71) however its cells were drawn.
    /// </summary>
    [Fact]
    public void Title_scratchpad_and_separator_rows_keep_their_rules()
    {
        var legs = Fixture("bbs757-cdu-legs.json");
        var plain = Bbs757CduRows.Build(legs, 1);

        var screen = With(legs, 0, legs.Lines[0].TrimEnd(), 0..24);
        screen = With(screen, 13, "FLT NUMBER UPLINK", 0..24);
        screen = With(screen, 11, new string('-', 24), 0..24);
        var rows = Bbs757CduRows.Build(screen, 1);

        Assert.Equal(plain.Select(r => r.Text), rows.Select(r => r.Text));
    }
}

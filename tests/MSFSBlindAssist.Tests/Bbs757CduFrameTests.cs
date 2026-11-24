using System.Text;
using System.Text.Json;
using MSFSBlindAssist.SimConnect.BBS757;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The BlueBird 757 CDU screen arrives as MobiFlight WinCtrl JSON on the Comm Bus
/// (docs/b757.md, "How the screens are read and the keys pressed"): <c>{"Target":"Display","Data":[336 cells]}</c>, 14 rows of 24
/// cells, a cell <c>[]</c> or <c>[char, color, size, inverse]</c>. Whatever arrives, the window
/// gets exactly 14 lines of 24 characters and never an exception.
/// </summary>
public class Bbs757CduFrameTests
{
    /// <summary>The IDENT page at power-up (cdu.md), with its blank row 5 restored and the scratchpad message on row 13.</summary>
    private static readonly string[] IdentLines =
    {
        "          IDENT",
        " MODEL           ENGINES",
        "757-200           PW2037",
        " NAV DATA         ACTIVE",
        "B572503001 MAR20APR16/25",
        "",
        "           FEB20MAR19/25",
        " OP PROGRAM      CO DATA",
        "4618BBSR02C07     BB1001",
        " OPC             DRAG/FF",
        "8912RA422WL4   +0.0/+2.0",
        "------------------------",
        "<INDEX         POS INIT>",
        "NAV DATA OUT OF DATE",
    };

    /// <summary>Builds the wire format from text lines: a space is <c>[]</c>, anything else <c>[c,"g",size,0]</c>.</summary>
    private static string FrameJson(IReadOnlyList<string> lines, int cellCount = 336)
    {
        var cells = new List<object[]>();
        for (int row = 0; row < 14; row++)
        {
            string line = row < lines.Count ? lines[row].PadRight(24) : new string(' ', 24);
            int size = row % 2 == 1 ? 1 : 0;   // label rows small, as the aircraft draws them
            foreach (char c in line)
                cells.Add(c == ' ' ? Array.Empty<object>() : new object[] { c.ToString(), "g", size, 0 });
        }
        return JsonSerializer.Serialize(new { Target = "Display", Data = cells.Take(cellCount).ToList() });
    }

    private static void AssertGrid(Bbs757CduScreen screen)
    {
        Assert.Equal(14, screen.Lines.Count);
        Assert.All(screen.Lines, line => Assert.Equal(24, line.Length));
        AssertCells(screen);
    }

    /// <summary>The cells are 14 rows of 24.</summary>
    private static void AssertCellShape(Bbs757CduScreen screen)
    {
        Assert.Equal(14, screen.Cells.Count);
        Assert.All(screen.Cells, row => Assert.Equal(24, row.Count));
    }

    /// <summary>The cells are 14 rows of 24 and hold the characters of the lines: one reading, with attributes.</summary>
    private static void AssertCells(Bbs757CduScreen screen)
    {
        AssertCellShape(screen);
        for (int row = 0; row < 14; row++)
            Assert.Equal(screen.Lines[row], new string(screen.Cells[row].Select(c => c.Ch).ToArray()));
    }

    private static void AssertBlank(Bbs757CduScreen screen)
    {
        AssertGrid(screen);
        Assert.All(screen.Lines, line => Assert.Equal(new string(' ', 24), line));
        Assert.All(screen.Cells.SelectMany(row => row), c => Assert.Equal(Bbs757CduCell.Blank, c));
    }

    [Fact]
    public void Ident_frame_reads_as_its_14_lines()
    {
        var screen = Bbs757CduFrame.Parse(FrameJson(IdentLines));

        AssertGrid(screen);
        Assert.Equal("          IDENT         ", screen.Lines[0]);
        Assert.Equal("757-200           PW2037", screen.Lines[2]);
        Assert.Equal("<INDEX         POS INIT>", screen.Lines[12]);
        Assert.StartsWith("NAV DATA OUT OF DATE", screen.Lines[13]);
        Assert.Equal("IDENT", screen.Title);
        Assert.Equal("NAV DATA OUT OF DATE", screen.Scratchpad);
    }

    [Fact]
    public void A_100_cell_frame_pads_to_14_by_24()
    {
        var screen = Bbs757CduFrame.Parse(FrameJson(IdentLines, cellCount: 100));

        AssertGrid(screen);
        Assert.Equal("          IDENT         ", screen.Lines[0]);
        Assert.Equal(" NAV DATA         ACTIVE", screen.Lines[3]);
        Assert.Equal("B572" + new string(' ', 20), screen.Lines[4]);   // cells 96-99, then nothing
        for (int row = 5; row < 14; row++)
            Assert.Equal(new string(' ', 24), screen.Lines[row]);
    }

    [Fact]
    public void Cells_past_336_are_ignored()
    {
        var cells = Enumerable.Repeat(new object[] { "A", "g", 0, 0 }, 400).ToList();
        var screen = Bbs757CduFrame.Parse(JsonSerializer.Serialize(new { Target = "Display", Data = cells }));

        AssertGrid(screen);
        Assert.All(screen.Lines, line => Assert.Equal(new string('A', 24), line));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"Target\":\"Display\"}")]
    [InlineData("{\"Target\":\"Display\",\"Data\":5}")]
    [InlineData("{\"Target\":\"Display\",\"Data\":[]}")]
    [InlineData("{\"Target\":\"Display\",\"Data\":[[\"A\",\"g\",0,0]")]   // truncated
    [InlineData("{\"Target\":\"\\uD83D\",\"Data\":[[\"A\",\"g\",0,0]]}")]   // a target that is not text
    public void Missing_or_bad_json_gives_14_blank_lines(string? json)
    {
        AssertBlank(Bbs757CduFrame.Parse(json));
    }

    [Fact]
    public void A_payload_for_another_target_is_not_a_screen()
    {
        string json = FrameJson(IdentLines).Replace("\"Display\"", "\"Font\"");

        AssertBlank(Bbs757CduFrame.Parse(json));
    }

    [Fact]
    public void The_box_glyph_reads_as_an_underscore()
    {
        var lines = new[] { "", "", "\u2610\u2610\u2610\u2610" };

        var screen = Bbs757CduFrame.Parse(FrameJson(lines));

        Assert.Equal("____" + new string(' ', 20), screen.Lines[2]);
    }

    [Fact]
    public void Odd_cells_read_as_blanks_and_never_throw()
    {
        // A number, null, an empty string, a control character and a two-character string.
        string json = "{\"Target\":\"Display\",\"Data\":[[5,\"g\",0,0],[null],[\"\"],[\"\\u0007\"],[\"AB\",\"g\",0,0],\"x\",null,{}]}";

        var screen = Bbs757CduFrame.Parse(json);

        AssertGrid(screen);
        Assert.Equal("    A" + new string(' ', 19), screen.Lines[0]);
    }

    [Fact]
    public void The_live_legs_frame_reads_with_its_degree_signs()
    {
        // Captured from CDU 1 on 2026-10-25 (ACT RTE 1 LEGS, KSEA to KDEN).
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "bbs757-cdu-legs.json"), Encoding.UTF8);

        var screen = Bbs757CduFrame.Parse(json);

        AssertGrid(screen);
        Assert.Equal("  ACT RTE 1 LEGS     1/2", screen.Lines[0]);
        Assert.Equal(" 307°                   ", screen.Lines[1]);
        Assert.Equal("SEA           ---/------", screen.Lines[2]);
        Assert.Equal("<RTE 2 LEGS    RTE DATA>", screen.Lines[12]);
        Assert.Equal("FLT NUMBER UPLINK", screen.Scratchpad);
    }

    [Fact]
    public void Display_lines_follow_the_cdu_window_layout()
    {
        var lines = Bbs757CduFrame.ToDisplayLines(Bbs757CduFrame.Parse(FrameJson(IdentLines)));

        Assert.Equal(new[]
        {
            "Title: IDENT",
            "    MODEL           ENGINES",
            "1: 757-200           PW2037",
            "    NAV DATA         ACTIVE",
            "2: B572503001 MAR20APR16/25",
            "3:            FEB20MAR19/25",   // a blank label row adds no line
            "    OP PROGRAM      CO DATA",
            "4: 4618BBSR02C07     BB1001",
            "    OPC             DRAG/FF",
            "5: 8912RA422WL4   +0.0/+2.0",
            // the 24 dashes above line 6 are left out (R71)
            "6: <INDEX         POS INIT>",
            "Scratchpad: NAV DATA OUT OF DATE",
        }, lines);
    }

    [Fact]
    public void A_blank_screen_says_which_cdu_it_is()
    {
        var screen = Bbs757CduFrame.Parse(null);

        Assert.Equal(new[] { "Captain CDU screen blank" }, Bbs757CduFrame.ToDisplayLines(screen));
        Assert.Equal(new[] { "First officer CDU screen blank" }, Bbs757CduFrame.ToDisplayLines(screen, 2));
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Encoding.UTF8);

    /// <summary>
    /// Plan 09 Task 2 keeps each cell's colour, size and inverse flag beside the text; the text itself
    /// is what it always was. These are the 14 lines of the live legs frame and of a synthetic frame
    /// (a box glyph, a degree sign, a control character, a surrogate, a two-character string, an empty
    /// string, all carrying attributes) as they read before the cells existed.
    /// </summary>
    [Fact]
    public void Lines_unchanged()
    {
        var legs = Bbs757CduFrame.Parse(ReadFixture("bbs757-cdu-legs.json"));

        Assert.Equal(new[]
        {
            "  ACT RTE 1 LEGS     1/2",
            " 307°                   ",
            "SEA           ---/------",
            " 076°     24NM          ",
            "NORMY         ---/------",
            " 077°     53NM          ",
            "BLUIT         ---/------",
            " 090°     46NM          ",
            "MWH           ---/------",
            " 112°    275NM          ",
            "KU87M         ---/------",
            "------------------------",
            "<RTE 2 LEGS    RTE DATA>",
            "FLT NUMBER UPLINK       ",
        }, legs.Lines);

        string json = """
            {"Target":"Display","Data":[["☐","g",0,1],["°","r",1,1],["\u0007","g",0,1],
            ["😀","g",1,0],["AB","m",1,1],[""],["Z","g",0,0]]}
            """;
        var synthetic = Bbs757CduFrame.Parse(json);

        Assert.Equal("_°  A Z" + new string(' ', 17), synthetic.Lines[0]);
        for (int row = 1; row < 14; row++)
            Assert.Equal(new string(' ', 24), synthetic.Lines[row]);
    }

    private static Bbs757CduCell Cell(char ch, char color = 'g', bool small = false, bool inverse = false) =>
        new(ch, color, small, inverse);

    /// <summary>
    /// The live legs frame (captured 2026-10-25) keeps its sizes and its colour: the title and the
    /// waypoint names are large, the leg figures small, the page counter "1/2" small in all three cells
    /// (its "/" too), while the "/" of an unset speed/altitude field ("---/------", the dashes small) is large.
    /// Nothing in it is inverse.
    /// </summary>
    [Fact]
    public void Cells_from_the_legs_fixture()
    {
        var screen = Bbs757CduFrame.Parse(ReadFixture("bbs757-cdu-legs.json"));

        AssertCells(screen);
        Assert.Equal(Cell('A'), screen.Cells[0][2]);                // the title "ACT RTE 1 LEGS"
        Assert.Equal(Cell('G'), screen.Cells[0][14]);
        Assert.Equal(new[] { Cell('1', small: true), Cell('/', small: true), Cell('2', small: true) },
            screen.Cells[0].Skip(21));                              // the page counter
        Assert.Equal(Cell('°', small: true), screen.Cells[1][4]);   // the leg's course "307°"
        Assert.Equal(Cell('S'), screen.Cells[2][0]);                // the waypoint "SEA"
        Assert.Equal(Cell('-', small: true), screen.Cells[2][14]);  // the empty field "---/------"
        Assert.Equal(Cell('/'), screen.Cells[2][17]);               // its large "/"
        Assert.Equal(Cell('-', small: true), screen.Cells[2][18]);

        var all = screen.Cells.SelectMany(row => row).ToList();
        Assert.All(all.Where(c => c.Ch != ' '), c => Assert.Equal('g', c.Color));
        Assert.All(all.Where(c => c.Ch == ' '), c => Assert.Equal(Bbs757CduCell.Blank, c));
        Assert.DoesNotContain(all, c => c.Inverse);
    }

    /// <summary>
    /// A cell is <c>[char, color, size, inverse]</c>: the colour is the first character of its string,
    /// size and inverse are small and inverse only for the number 1 (or JSON true). A space can be inverse
    /// (a highlighted run keeps its blanks); a cell missing its later elements has the defaults for them.
    /// </summary>
    [Fact]
    public void Colour_size_and_inverse_read_from_the_cell()
    {
        string json = """
            {"Target":"Display","Data":[["A","g",0,0],["B","w",1,1],["C","mx",1,0],["D","g",true,true],
            [" ","g",0,1],["E"],["F","c"],["G","g",1.0,1.0]]}
            """;

        var row = Bbs757CduFrame.Parse(json).Cells[0];

        Assert.Equal(Cell('A'), row[0]);
        Assert.Equal(Cell('B', 'w', small: true, inverse: true), row[1]);
        Assert.Equal(Cell('C', 'm', small: true), row[2]);                 // the first character of "mx"
        Assert.Equal(Cell('D', small: true, inverse: true), row[3]);       // JSON true
        Assert.Equal(Cell(' ', inverse: true), row[4]);                    // an inverse space is kept
        Assert.Equal(Cell('E', color: ' '), row[5]);                       // no attributes: the defaults
        Assert.Equal(Cell('F', 'c'), row[6]);
        Assert.Equal(Cell('G', small: true, inverse: true), row[7]);       // 1.0 is the number 1
        Assert.Equal(Bbs757CduCell.Blank, row[8]);
    }

    /// <summary>
    /// A bad attribute gives that field's default and never blanks a good character; a cell that is not
    /// an array, is empty or does not start with a string is a blank cell.
    /// </summary>
    [Fact]
    public void Bad_attributes_keep_the_character()
    {
        string json = """
            {"Target":"Display","Data":[["A",5,"x",null],["B",["g"],{},[1]],["C","g",2,2],["D","",1.5,-1],
            ["E",null,false,false],["F","g",[1],"1"],["H","\uD83D",1,1],
            5,null,"x",{},[],[5,"g",0,1],[null,"g",1,1],[["A"],"g",1,1]]}
            """;

        var row = Bbs757CduFrame.Parse(json).Cells[0];

        Assert.Equal(Cell('A', ' '), row[0]);
        Assert.Equal(Cell('B', ' '), row[1]);
        Assert.Equal(Cell('C'), row[2]);
        Assert.Equal(Cell('D', ' '), row[3]);
        Assert.Equal(Cell('E', ' '), row[4]);
        Assert.Equal(Cell('F'), row[5]);
        Assert.Equal(Cell('H', ' ', small: true, inverse: true), row[6]);   // a lone surrogate is no colour
        Assert.All(row.Skip(7).Take(8), c => Assert.Equal(Bbs757CduCell.Blank, c));
    }

    public static TheoryData<string, string> OddFrames => new()
    {
        { "fewer cells", """{"Target":"Display","Data":[["A","g",0,0],["B","g",0,0]]}""" },
        { "wrong types", """{"Target":"Display","Data":[["A",5,"x",null],["B",["g"],{},[1]],["C","g",2,2],[5,"g",0,1],5,null,"x",{}]}""" },
        { "wrong lengths", """{"Target":"Display","Data":[["A"],["B","g"],["C","g",1],[],[[]],["D","g",1,1,9,9,9]]}""" },
        { "unreadable characters", """{"Target":"Display","Data":[[""],["\u0007","g",1,1],["\uD83D","g",1,1],["😀","g",1,1],["☐","g",0,1]]}""" },
        { "no title", FrameJson(new[] { "", " LABEL", "DATA" }) },
        { "title only", FrameJson(new[] { "  ACT RTE 1 LEGS     1/2" }, cellCount: 24) },
    };

    /// <summary>
    /// Whatever a frame holds, the window still gets 14 rows of 24 cells that agree with the lines, and
    /// the row builder still yields its rows (plan 09's review focus: fewer cells, bad cells or no title).
    /// </summary>
    [Theory]
    [MemberData(nameof(OddFrames))]
    public void Bad_cells_never_throw(string name, string json)
    {
        var screen = Bbs757CduFrame.Parse(json);
        var rows = Bbs757CduRows.Build(screen, 1);

        AssertGrid(screen);
        Assert.NotEmpty(rows);
        Assert.True(rows[0].Kind == Bbs757CduRowKind.Title, $"{name}: the first row is the title row");
        Assert.All(rows, r => Assert.True(r.Text.Length <= 40 && r.Text == r.Text.TrimEnd(), $"{name}: {r.Text}"));
    }

    [Fact]
    public void A_frame_with_no_title_lists_its_rows_under_an_empty_title()
    {
        var screen = Bbs757CduFrame.Parse(FrameJson(new[] { "", " LABEL", "DATA" }));

        Assert.Equal(new[]
        {
            "Title:", "    LABEL", "1: DATA", "2:", "3:", "4:", "5:", "6:", "Scratchpad:",
        }, Bbs757CduRows.Build(screen, 1).Select(r => r.Text));
    }

    /// <summary>
    /// A screen made from lines alone (the tests, <see cref="Bbs757CduFrame.Blank"/>) has the cells of
    /// its characters with no colour, large and not inverse, padded or cut to 14 by 24.
    /// </summary>
    [Fact]
    public void A_screen_made_from_lines_has_plain_cells()
    {
        var screen = new Bbs757CduScreen(new[] { "AB", "C" + new string('-', 30) });

        Assert.Equal(new[] { "AB", "C" + new string('-', 30) }, screen.Lines);
        AssertCellShape(screen);
        Assert.Equal(Cell('A', color: ' '), screen.Cells[0][0]);
        Assert.Equal(Cell('B', color: ' '), screen.Cells[0][1]);
        Assert.Equal(Bbs757CduCell.Blank, screen.Cells[0][2]);
        Assert.Equal(Cell('-', color: ' '), screen.Cells[1][23]);
        Assert.Equal(Bbs757CduCell.Blank, screen.Cells[13][0]);
        Assert.All(Bbs757CduFrame.Blank.Cells.SelectMany(row => row), c => Assert.Equal(Bbs757CduCell.Blank, c));
    }
}

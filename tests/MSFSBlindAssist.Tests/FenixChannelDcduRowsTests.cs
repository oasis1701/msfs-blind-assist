// Characterization tests for FenixChannelDcduRows, which turns the captain's DCDU of one snapshot of the Fenix display channel
// into the rows of the DCDU window. Pure logic: the pushes are built here with FenixChannelProtoWriter and the field numbers
// written out literally (186 for the DCDU, 2 to 15 for its lines), and the screens that need no decoding are constructed.
//
// The contract: every line is spoken through FenixChannelSpeech with its colors, and a blank line makes no row. The rows run
// the header (the left half then the right, joined ", "), the five message lines, the three info lines and the soft keys
// top to bottom, left to right (L1, R1, L2, R2). A key keeps its slot number and its side arrow with its label, before the
// spoken color: a left key "1 <" and the label, a right key "1 " and the label with its ">" appended, so the arrow shares the
// label's last stretch. A blank label makes no row, and the arrow does not make one. A screen with no row reads one row of
// words; a snapshot that cannot be read is the one status problem, and it is reported.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDcduRowsTests
{
    // 14 spaces, as the empty DCDU's Info2 and Info3 were measured on 3.0.0.7613 (2026-11-03).
    private const string Spaces14 = "              ";

    // A DCDU part with the print frame set, as the empty DCDU was measured, and the lines given: field number and text.
    private static FenixChannelProtoWriter Content(params (int Field, string Text)[] lines)
    {
        var content = new FenixChannelProtoWriter().Bool(1, true);
        foreach ((int field, string text) in lines)
        {
            content.String(field, text);
        }

        return content;
    }

    private static FenixChannelSnapshot Snapshot(bool trusted, string reason, FenixChannelProtoWriter? dcdu)
    {
        FenixChannelPush? push = dcdu is null
            ? null
            : FenixChannelPush.From(new FenixChannelProtoWriter().Message(186, dcdu).ToArray());
        return new(new FenixDisplayVerdict(trusted, reason), null, push, null, 0, 1);
    }

    // A trusted snapshot of the given lines, composed with nothing reported.
    private static IReadOnlyList<string> RowsOf(params (int Field, string Text)[] lines)
    {
        var reported = new List<string>();
        FenixChannelRows rows = FenixChannelDcduRows.Compose(Snapshot(true, "trusted", Content(lines)), reported.Add);

        Assert.Null(rows.Problem);
        Assert.Empty(reported);
        return rows.Rows!;
    }

    // A constructed screen: the header halves, the message lines and info lines given from the first (the rest blank), then
    // the keys in the order L1, L2, R1, R2 as the decoder's record has them.
    private static FenixDcduScreen Screen(string headerLeft = "", string headerRight = "", string[]? message = null,
        string[]? info = null, string leftKey1 = "", string leftKey2 = "", string rightKey1 = "", string rightKey2 = "")
        => new(headerLeft, headerRight, Fill(message, 5), Fill(info, 3), leftKey1, leftKey2, rightKey1, rightKey2);

    private static string[] Fill(string[]? lines, int count)
        => Enumerable.Range(0, count).Select(i => lines is not null && i < lines.Length ? lines[i] : "").ToArray();

    // The lines of the brief's message: a CLIMB TO FL350 uplink from EDGG with three of the four soft keys labeled.
    private static readonly (int Field, string Text)[] ClimbLines =
    [
        (2, "w1426Z FROM EDGG"), (3, "gOPEN"), (4, "cCLIMB TO"), (5, "cFL350"), (12, "c*UNABLE"), (14, "cWILCO*"),
        (13, "c*STBY")
    ];

    private static readonly string[] ClimbRows =
    [
        "1426Z FROM EDGG, white, OPEN, green", "CLIMB TO, cyan", "FL350, cyan", "1 <*UNABLE, cyan", "1 WILCO*>, cyan",
        "2 <*STBY, cyan"
    ];

    private static void InCulture(string name, Action action)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- The rows ---------------------------------------------------------------------------

    [Fact]
    public void A_message_reads_header_lines_info_and_keys_in_order()
    {
        Assert.Equal(ClimbRows, RowsOf(ClimbLines));
    }

    [Fact]
    public void The_rows_run_header_message_info_then_the_keys_top_to_bottom_and_left_to_right()
    {
        // Every line given, no two alike. The keys read L1, R1, L2, R2: by position, the way the DCDU draws them.
        IReadOnlyList<string> rows = RowsOf(
            (2, "wLEFT"), (3, "gRIGHT"), (4, "cM1"), (5, "cM2"), (6, "cM3"), (7, "cM4"), (8, "cM5"),
            (9, "gI1"), (10, "gI2"), (11, "gI3"), (12, "cL1"), (13, "cL2"), (14, "cR1"), (15, "cR2"));

        Assert.Equal(
            new[]
            {
                "LEFT, white, RIGHT, green", "M1, cyan", "M2, cyan", "M3, cyan", "M4, cyan", "M5, cyan",
                "I1, green", "I2, green", "I3, green", "1 <L1, cyan", "1 R1>, cyan", "2 <L2, cyan", "2 R2>, cyan"
            },
            rows.ToArray());
    }

    [Fact]
    public void A_blank_line_makes_no_row()
    {
        // The header halves, message lines, info lines and key labels that are empty, spaces or only a color letter say
        // nothing; the lines around them keep their order.
        IReadOnlyList<string> rows = RowsOf(
            (2, Spaces14), (3, "gOPEN"), (4, ""), (5, "cFL350"), (6, "c  "), (7, "c"), (9, Spaces14), (10, "gINFO"),
            (12, Spaces14), (13, "c*STBY"), (14, ""), (15, "c"));

        Assert.Equal(new[] { "OPEN, green", "FL350, cyan", "INFO, green", "2 <*STBY, cyan" }, rows.ToArray());
    }

    [Fact]
    public void A_braille_spacer_and_runs_of_spaces_read_as_the_speech_has_them()
    {
        // A message line padded with runs of spaces, and an info line the DCDU indents with the braille blank.
        Assert.Equal(new[] { "FL350 AT 1426Z, cyan", "ACTIVE, green" },
            RowsOf((4, "cFL350   AT  1426Z"), (9, "g⠀ACTIVE")).ToArray());
    }

    [Fact]
    public void The_measured_empty_dcdu_reads_one_row()
    {
        // Field 1 true, fields 2 to 15 all present: 2 to 9 and 12 to 15 empty, Info2 and Info3 14 spaces.
        (int, string)[] empty = Enumerable.Range(2, 14).Select(field => (field, field is 10 or 11 ? Spaces14 : "")).ToArray();

        Assert.Equal(new[] { FenixChannelDcduRows.NoMessageRow }, RowsOf(empty).ToArray());
        Assert.Equal("(no CPDLC message displayed)", FenixChannelDcduRows.NoMessageRow);

        // Nothing sent at all but the print frame is the same screen.
        Assert.Equal(new[] { FenixChannelDcduRows.NoMessageRow }, RowsOf().ToArray());
    }

    [Fact]
    public void Rows_of_a_screen_are_the_rows_it_draws_and_none_when_it_draws_nothing()
    {
        // RowsOf leaves the empty screen's words to Compose, so a caller can tell a screen with nothing on it.
        Assert.Empty(FenixChannelDcduRows.RowsOf(Screen(info: ["", Spaces14, Spaces14])));
        Assert.Equal(new[] { "FL350, cyan", "2 RECALL*>, cyan" },
            FenixChannelDcduRows.RowsOf(Screen(message: ["cFL350"], rightKey2: "cRECALL*")).ToArray());
    }

    // ---- The keys ---------------------------------------------------------------------------

    [Fact]
    public void A_lone_right_key_keeps_its_slot()
    {
        // The empty-state DCDU can draw one key alone on a side: it keeps its number and its side, never moving up to the
        // first row's place ([A320-16]).
        Assert.Equal(new[] { "2 RECALL*>, cyan" }, RowsOf((15, "cRECALL*")).ToArray());
        Assert.Equal(new[] { "1 RECALL*>, cyan" }, RowsOf((14, "cRECALL*")).ToArray());
        Assert.Equal(new[] { "2 <*STBY, cyan" }, RowsOf((13, "c*STBY")).ToArray());
        Assert.Equal(new[] { "1 <*UNABLE, cyan" }, RowsOf((12, "c*UNABLE")).ToArray());
    }

    [Fact]
    public void A_blank_label_makes_no_row_and_its_arrow_does_not_make_one()
    {
        // The arrow is added to a label that has words; it is never spoken alone.
        FenixDcduScreen blank = Screen(leftKey1: "c ", leftKey2: "", rightKey1: Spaces14, rightKey2: "c");

        Assert.Empty(FenixChannelDcduRows.RowsOf(blank));
    }

    [Fact]
    public void The_right_arrow_shares_the_last_stretch_of_a_label()
    {
        // A label of two colors: the ">" belongs to the last stretch, as the DCDU draws it at the end of the label.
        FenixDcduScreen screen = Screen(leftKey1: "cWILCO gSEND", rightKey1: "cWILCO gSEND");

        Assert.Equal(new[] { "1 <WILCO cyan, SEND green", "1 WILCO cyan, SEND> green" },
            FenixChannelDcduRows.RowsOf(screen).ToArray());
    }

    [Fact]
    public void A_padded_right_key_keeps_its_arrow_on_the_word()
    {
        // The empty DCDU pads its info lines with spaces, so a padded label is plausible: the arrow follows the word, not the
        // padding (speech keeps one space, so it would read "WILCO* >"). The braille blank is padding too (the parser reads
        // it as a space), written as a cast because it cannot be seen in source.
        char blank = (char)0x2800;

        Assert.Equal(new[] { "1 WILCO*>, cyan" },
            FenixChannelDcduRows.RowsOf(Screen(rightKey1: "cWILCO*   ")).ToArray());
        Assert.Equal(new[] { "2 WILCO*>, cyan" },
            FenixChannelDcduRows.RowsOf(Screen(rightKey2: "cWILCO*" + blank + blank)).ToArray());
        Assert.Equal(new[] { "1 WILCO*>, cyan" },
            FenixChannelDcduRows.RowsOf(Screen(rightKey1: "cWILCO* " + blank + " ")).ToArray());

        // A label that is nothing but padding still makes no row.
        Assert.Empty(FenixChannelDcduRows.RowsOf(Screen(rightKey1: "c" + blank + blank, rightKey2: "c   ")));
    }

    // ---- The header and the first message line ----------------------------------------------

    [Fact]
    public void The_header_joins_its_halves_and_skips_a_blank_one()
    {
        Assert.Equal("1426Z FROM EDGG, white, OPEN, green",
            FenixChannelDcduRows.HeaderText(Screen("w1426Z FROM EDGG", "gOPEN")));
        Assert.Equal("1426Z FROM EDGG, white", FenixChannelDcduRows.HeaderText(Screen("w1426Z FROM EDGG", Spaces14)));
        Assert.Equal("OPEN, green", FenixChannelDcduRows.HeaderText(Screen("", "gOPEN")));
        Assert.Null(FenixChannelDcduRows.HeaderText(Screen(Spaces14, "")));
    }

    [Fact]
    public void The_first_message_line_is_the_first_one_with_words()
    {
        // The blank lines before it, the info lines and the keys are not it.
        FenixDcduScreen screen = Screen(message: ["", "c  ", "cFL350", "cMAINTAIN"], info: ["gINFO"], leftKey1: "c*UNABLE");

        Assert.Equal("FL350, cyan", FenixChannelDcduRows.FirstMessageLine(screen));
        Assert.Null(FenixChannelDcduRows.FirstMessageLine(Screen(info: ["gINFO"], leftKey1: "c*UNABLE")));
    }

    // ---- A snapshot that cannot be read -----------------------------------------------------

    [Fact]
    public void A_problem_is_reported_and_not_available()
    {
        // Not trusted: the verdict's reason, whatever the message says.
        var reported = new List<string>();
        FenixChannelRows off = FenixChannelDcduRows.Compose(Snapshot(false, "display off", Content(ClimbLines)), reported.Add);

        Assert.Null(off.Rows);
        Assert.Equal("display off", off.Problem);
        Assert.Equal(new[] { "display off" }, reported.ToArray());

        // Trusted with no DCDU at all, and with a letter the app does not know: the decoder's problem, never rows with
        // letters dropped.
        reported.Clear();
        FenixChannelRows none = FenixChannelDcduRows.Compose(Snapshot(true, "trusted", null), reported.Add);
        FenixChannelRows unknown = FenixChannelDcduRows.Compose(Snapshot(true, "trusted", Content((5, "climb"))), reported.Add);

        Assert.Null(none.Rows);
        Assert.Equal("no DCDU content", none.Problem);
        Assert.Null(unknown.Rows);
        Assert.Equal("unknown code 'i' in the DCDU message", unknown.Problem);
        Assert.Equal(new[] { "no DCDU content", "unknown code 'i' in the DCDU message" }, reported.ToArray());

        // The reporter is optional.
        Assert.Equal("display off", FenixChannelDcduRows.Compose(Snapshot(false, "display off", null)).Problem);
    }

    // ---- Culture ----------------------------------------------------------------------------

    [Fact]
    public void Rows_read_the_same_in_de_DE()
    {
        // CI runs under en-US: a row built with the PC's culture would read differently here.
        InCulture("de-DE", () =>
        {
            Assert.Equal(ClimbRows, RowsOf(ClimbLines));
            Assert.Equal("1426Z FROM EDGG, white, OPEN, green",
                FenixChannelDcduRows.HeaderText(Screen("w1426Z FROM EDGG", "gOPEN")));
            Assert.Equal("CLIMB TO, cyan", FenixChannelDcduRows.FirstMessageLine(Screen(message: ["cCLIMB TO"])));
        });
    }
}

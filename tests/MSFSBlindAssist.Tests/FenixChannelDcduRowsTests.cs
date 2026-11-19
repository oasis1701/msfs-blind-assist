// Characterization tests for FenixChannelDcduRows, which turns the captain's DCDU of one snapshot of the Fenix display channel
// into the rows of the DCDU window. Pure logic: the pushes are built here with FenixChannelProtoWriter and the field numbers
// written out literally (186 for the DCDU, 2 to 15 for its lines), and the screens that need no decoding are constructed.
//
// The contract (owner's ruling of 2026-11-03, over the spec's color names): the window speaks no color names, and is laid out
// as the MCDU window is. Every line is FenixChannelDcdu.Plain, which keeps the spacing the Fenix drew inside the line and
// trims its end, and a blank line makes no row. The rows run the header on one 24-column row (left half at the left, right half
// at the right, both trimmed), the five message lines, the three info lines, then one row for each key row: the number, then a
// left key's label after "<" and a right key's label before ">" at the right edge. A row with neither label makes no row. A
// screen with no row reads one row of words; a snapshot that cannot be read is the one status problem, and it is reported.
//
// The fixtures are the first real messages of build 3.0.0.7613 (2026-11-03), line for line as the Fenix sent them.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDcduRowsTests
{
    // 14 spaces, as the empty DCDU's Info2 and Info3 were measured on 3.0.0.7613 (2026-11-03).
    private const string Spaces14 = "              ";

    // U+2800, the blank braille cell the display uses as a spacer, written as a cast because it cannot be seen in source.
    private static readonly string Spacer = ((char)0x2800).ToString();

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

    // A row of the 24-column grid: the left text at the left edge and the right text at the right edge, the gap spaces. Laid
    // out here by hand, so a test does not use the code that is being tested.
    private static string Grid(string left, string right)
    {
        Assert.True(left.Length + right.Length <= 24, "the test's own row is wider than the grid");
        return left + new string(' ', 24 - left.Length - right.Length) + right;
    }

    // ---- The messages the Fenix really sent -------------------------------------------------

    // The first message, page 1 (fields 2 to 15, in order). Each message line is 24 characters, codes included.
    private static readonly (int Field, string Text)[] DepartRequestPage1 =
    [
        (2, "g 2021Z FROM EDDF"), (3, "cOPEN"),
        (4, "DEPART REQUEST STATUS . ".PadRight(24)), (5, "FSM 2010 261103 cEDDFw  ".PadRight(24)),
        (6, "cBVI4RKw  RCD REQUEST".PadRight(24)), (7, "BEING PROCESSED".PadRight(24)),
        (8, "cSTANDBYw  IF cNO".PadRight(24)), (9, ""), (10, "MSG        PGE"), (11, "1/2        1/2"),
        (12, ""), (13, ""), (14, ""), (15, "cCLOSE*")
    ];

    // The same message, page 2. The keys of this page were not recorded, so none are given.
    private static readonly (int Field, string Text)[] DepartRequestPage2 =
    [
        (2, "g 2021Z FROM EDDF"), (3, "cOPEN"),
        (4, "cSTANDBYw  IF cNO".PadRight(24)), (5, "RESPONSEw IN c2 MINSw".PadRight(24)),
        (6, "REQUEST cNEW FLIGHTw IN ".PadRight(24)), (7, "APP".PadRight(24)), (8, ""),
        (9, ""), (10, "MSG        PGE"), (11, "1/2        2/2")
    ];

    // The second message, a clearance stepped to (one page), with its four answer keys as the Fenix sent them.
    private static readonly (int Field, string Text)[] Clearance =
    [
        (2, "g 2021Z FROM EDDF"), (3, "cOPEN"),
        (4, "CLRD TO cEGSSw.".PadRight(24)), (5, "cOBOK2Ew DEPARTURE.".PadRight(24)),
        (6, "MNTN c4000w. SQUAWK".PadRight(24)), (7, "c4172w. EXPCT RWY".PadRight(24)), (8, "c07Cw.".PadRight(24)),
        (9, ""), (10, "MSG".PadRight(14)), (11, "2/2".PadRight(14)),
        (12, "c*UNABLE"), (13, ""), (14, "cSTBY*"), (15, "cWILCO*")
    ];

    // The same message about 1.2 s after one press of WILCO: the status says WILCO, the answer is open to cancel or send.
    private static readonly (int Field, string Text)[] ClearanceAfterWilco =
    [
        (2, "g 2021Z FROM EDDF"), (3, "ciWILCO"),
        (4, "CLRD TO cEGSSw.".PadRight(24)), (5, "cOBOK2Ew DEPARTURE.".PadRight(24)),
        (6, "MNTN c4000w. SQUAWK".PadRight(24)), (7, "c4172w. EXPCT RWY".PadRight(24)), (8, "c07Cw.".PadRight(24)),
        (9, ""), (10, "MSG".PadRight(14)), (11, "2/2".PadRight(14)),
        (12, "c*CANCEL"), (13, ""), (14, ""), (15, "cSEND*")
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
    public void The_first_message_reads_as_the_dcdu_draws_it()
    {
        // The literal rows of the brief: the header on one row, the message lines with the spacing the Fenix drew, the info
        // lines, and the one key row that has a label (the second), its number first and its label at the right edge.
        Assert.Equal(
            new[]
            {
                "2021Z FROM EDDF     OPEN",
                "DEPART REQUEST STATUS .",
                "FSM 2010 261103 EDDF",
                "BVI4RK  RCD REQUEST",
                "BEING PROCESSED",
                "STANDBY  IF NO",
                "MSG        PGE",
                "1/2        1/2",
                "2                CLOSE*>"
            },
            RowsOf(DepartRequestPage1).ToArray());
    }

    [Fact]
    public void The_second_page_of_that_message_reads_its_own_lines()
    {
        Assert.Equal(
            new[]
            {
                "2021Z FROM EDDF     OPEN",
                "STANDBY  IF NO",
                "RESPONSE IN 2 MINS",
                "REQUEST NEW FLIGHT IN",
                "APP",
                "MSG        PGE",
                "1/2        2/2"
            },
            RowsOf(DepartRequestPage2).ToArray());
    }

    [Fact]
    public void A_clearance_reads_with_its_answer_keys_on_the_grid()
    {
        // Row 1 has a left and a right key, row 2 has only a right one (its number stands alone); the left key follows its
        // number and "<", the right key ends at the right edge with its ">". The info lines are "MSG" and "2/2" with their
        // padding trimmed.
        Assert.Equal(
            new[]
            {
                "2021Z FROM EDDF     OPEN",
                "CLRD TO EGSS.",
                "OBOK2E DEPARTURE.",
                "MNTN 4000. SQUAWK",
                "4172. EXPCT RWY",
                "07C.",
                "MSG",
                "2/2",
                "1 <*UNABLE        STBY*>",
                "2                WILCO*>"
            },
            RowsOf(Clearance).ToArray());
    }

    [Fact]
    public void A_chosen_answer_reads_its_status_and_its_cancel_and_send_keys()
    {
        // About 1.2 s after WILCO: the header's right half is the answer ("ciWILCO"), key 1 on the left cancels and key 2 on
        // the right sends. The answer letter "i" is a code and is not read.
        Assert.Equal(
            new[]
            {
                "2021Z FROM EDDF    WILCO",
                "CLRD TO EGSS.",
                "OBOK2E DEPARTURE.",
                "MNTN 4000. SQUAWK",
                "4172. EXPCT RWY",
                "07C.",
                "MSG",
                "2/2",
                "1 <*CANCEL",
                "2                 SEND*>"
            },
            RowsOf(ClearanceAfterWilco).ToArray());
    }

    [Fact]
    public void A_key_the_pilot_is_pressing_reads_as_its_label()
    {
        // About 0.3 s after the press the key's own label is "ciWILCO*" for half a second: the same row as before and after.
        (int, string)[] pressing = [.. Clearance.Select(line => line.Field == 15 ? (15, "ciWILCO*") : line)];

        Assert.Equal(RowsOf(Clearance), RowsOf(pressing));
        Assert.Equal("2                WILCO*>", RowsOf(pressing)[^1]);
    }

    [Fact]
    public void No_row_names_a_color()
    {
        foreach ((int, string)[] message in new[] { DepartRequestPage1, DepartRequestPage2, Clearance, ClearanceAfterWilco })
        {
            foreach (string row in RowsOf(message))
            {
                foreach (string color in new[] { "white", "green", "cyan", "amber", "magenta", "red", "yellow" })
                {
                    Assert.DoesNotContain(color, row, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void The_rows_run_header_message_info_then_the_key_rows_top_to_bottom()
    {
        // Every line given, no two alike. The key rows read 1 then 2, each with its left key and its right key.
        IReadOnlyList<string> rows = RowsOf(
            (2, "wLEFT"), (3, "gRIGHT"), (4, "cM1"), (5, "cM2"), (6, "cM3"), (7, "cM4"), (8, "cM5"),
            (9, "gI1"), (10, "gI2"), (11, "gI3"), (12, "cL1"), (13, "cL2"), (14, "cR1"), (15, "cR2"));

        Assert.Equal(
            new[]
            {
                Grid("LEFT", "RIGHT"), "M1", "M2", "M3", "M4", "M5", "I1", "I2", "I3", Grid("1 <L1", "R1>"),
                Grid("2 <L2", "R2>")
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

        Assert.Equal(new[] { Grid("", "OPEN"), "FL350", "INFO", "2 <*STBY" }, rows.ToArray());
    }

    [Fact]
    public void The_spacing_the_fenix_drew_inside_a_line_is_kept()
    {
        // A message line padded with runs of spaces keeps them (only its end is trimmed); an info line the DCDU indents with
        // the braille blank keeps the indent as a space.
        Assert.Equal(new[] { "FL350   AT  1426Z", " ACTIVE" },
            RowsOf((4, "cFL350   AT  1426Z   "), (9, "g" + Spacer + "ACTIVE")).ToArray());
    }

    [Fact]
    public void The_exclamation_mark_is_a_space()
    {
        Assert.Equal(new[] { "ALT CRZ" }, RowsOf((4, "gALT!CRZ!!")).ToArray());
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
    public void A_screen_that_cannot_be_read_stays_the_one_problem_row()
    {
        var reported = new List<string>();

        FenixChannelRows off = FenixChannelDcduRows.Compose(Snapshot(false, "display off", null), reported.Add);

        Assert.Equal("display off", off.Problem);
        Assert.Equal(new[] { "display off" }, reported.ToArray());
    }

    [Fact]
    public void Rows_of_a_screen_are_the_rows_it_draws_and_none_when_it_draws_nothing()
    {
        // RowsOf leaves the empty screen's words to Compose, so a caller can tell a screen with nothing on it.
        Assert.Empty(FenixChannelDcduRows.RowsOf(Screen(info: ["", Spaces14, Spaces14])));
        Assert.Equal(new[] { "FL350", Grid("2", "RECALL*>") },
            FenixChannelDcduRows.RowsOf(Screen(message: ["cFL350"], rightKey2: "cRECALL*")).ToArray());
    }

    // ---- The keys ---------------------------------------------------------------------------

    [Fact]
    public void A_lone_right_key_keeps_its_slot_and_a_lone_left_key_too()
    {
        // The empty-state DCDU can draw one key alone on a side: it keeps its number and its side, never moving up to the
        // first row's place ([A320-16]).
        Assert.Equal(new[] { Grid("2", "RECALL*>") }, RowsOf((15, "cRECALL*")).ToArray());
        Assert.Equal(new[] { Grid("1", "RECALL*>") }, RowsOf((14, "cRECALL*")).ToArray());
        Assert.Equal(new[] { "2 <*STBY" }, RowsOf((13, "c*STBY")).ToArray());
        Assert.Equal(new[] { "1 <*UNABLE" }, RowsOf((12, "c*UNABLE")).ToArray());
    }

    [Fact]
    public void Two_keys_on_one_row_share_it_and_a_row_with_neither_makes_none()
    {
        // Key row 1: left key 1 and right key 1 on one row. Key row 2 has no label on either side.
        Assert.Equal(new[] { Grid("1 <*UNABLE", "WILCO*>") },
            RowsOf((12, "c*UNABLE"), (14, "cWILCO*")).ToArray());

        // Key row 2: left key 2 and right key 2 on one row, and key row 1 is absent.
        Assert.Equal(new[] { Grid("2 <*STBY", "SEND*>") }, RowsOf((13, "c*STBY"), (15, "cSEND*")).ToArray());
    }

    [Fact]
    public void A_blank_label_makes_no_row_and_its_arrow_does_not_make_one()
    {
        // The arrow is added to a label that has words; it is never read alone.
        FenixDcduScreen blank = Screen(leftKey1: "c ", leftKey2: "", rightKey1: Spaces14, rightKey2: "c");

        Assert.Empty(FenixChannelDcduRows.RowsOf(blank));
    }

    [Fact]
    public void A_label_of_two_colors_reads_as_one_label()
    {
        // No color is named, so two stretches of one label read as the words they are; the right arrow follows the last.
        FenixDcduScreen screen = Screen(leftKey1: "cWILCO gSEND", rightKey2: "cWILCO gSEND");

        Assert.Equal(new[] { "1 <WILCO SEND", Grid("2", "WILCO SEND>") },
            FenixChannelDcduRows.RowsOf(screen).ToArray());
    }

    [Fact]
    public void A_padded_right_key_keeps_its_arrow_on_the_word()
    {
        // The empty DCDU pads its info lines with spaces, so a padded label is plausible: the arrow follows the word, not the
        // padding. The braille blank is padding too (the parser reads it as a space).
        Assert.Equal(new[] { Grid("1", "WILCO*>") },
            FenixChannelDcduRows.RowsOf(Screen(rightKey1: "cWILCO*   ")).ToArray());
        Assert.Equal(new[] { Grid("2", "WILCO*>") },
            FenixChannelDcduRows.RowsOf(Screen(rightKey2: "cWILCO*" + Spacer + Spacer)).ToArray());
        Assert.Equal(new[] { Grid("1", "WILCO*>") },
            FenixChannelDcduRows.RowsOf(Screen(rightKey1: "cWILCO* " + Spacer + " ")).ToArray());

        // A label that is nothing but padding still makes no row.
        Assert.Empty(FenixChannelDcduRows.RowsOf(Screen(rightKey1: "c" + Spacer + Spacer, rightKey2: "c   ")));
    }

    [Fact]
    public void A_key_row_wider_than_the_grid_loses_no_word()
    {
        // Left and right labels that cannot both stand on 24 columns are joined by one space, whole, never clipped or
        // written over one another.
        Assert.Equal(new[] { "1 <*REQUEST CLEARANCE NOW STBY*>" },
            FenixChannelDcduRows.RowsOf(Screen(leftKey1: "c*REQUEST CLEARANCE NOW", rightKey1: "cSTBY*")).ToArray());

        // A left label wider than the grid alone is not clipped either.
        Assert.Equal(new[] { "2 <*REQUEST CLEARANCE NOW AGAIN" },
            FenixChannelDcduRows.RowsOf(Screen(leftKey2: "c*REQUEST CLEARANCE NOW AGAIN")).ToArray());
    }

    // ---- The header and the first message line ----------------------------------------------

    [Fact]
    public void The_header_puts_its_halves_at_the_two_edges_of_one_row()
    {
        Assert.Equal("1426Z FROM EDGG     OPEN",
            FenixChannelDcduRows.HeaderText(Screen("w1426Z FROM EDGG", "gOPEN")));

        // Both halves trimmed: the Fenix's left half starts with a space after its color letter.
        Assert.Equal("2021Z FROM EDDF     OPEN",
            FenixChannelDcduRows.HeaderText(Screen("g 2021Z FROM EDDF ", "cOPEN  ")));
    }

    [Fact]
    public void The_header_skips_a_blank_half_and_is_null_when_both_are()
    {
        Assert.Equal("1426Z FROM EDGG", FenixChannelDcduRows.HeaderText(Screen("w1426Z FROM EDGG", Spaces14)));
        Assert.Equal(Grid("", "OPEN"), FenixChannelDcduRows.HeaderText(Screen("", "gOPEN")));
        Assert.Null(FenixChannelDcduRows.HeaderText(Screen(Spaces14, "")));
    }

    [Fact]
    public void A_header_wider_than_the_grid_loses_no_word()
    {
        Assert.Equal("1426Z FROM EDGG CTR WILCO SENDING",
            FenixChannelDcduRows.HeaderText(Screen("w1426Z FROM EDGG CTR", "gWILCO SENDING")));
    }

    [Fact]
    public void The_first_message_line_is_the_first_one_with_words()
    {
        // The blank lines before it, the info lines and the keys are not it.
        FenixDcduScreen screen = Screen(message: ["", "c  ", "cFL350", "cMAINTAIN"], info: ["gINFO"], leftKey1: "c*UNABLE");

        Assert.Equal("FL350", FenixChannelDcduRows.FirstMessageLine(screen));
        Assert.Null(FenixChannelDcduRows.FirstMessageLine(Screen(info: ["gINFO"], leftKey1: "c*UNABLE")));
    }

    [Fact]
    public void The_first_message_line_collapses_runs_of_spaces()
    {
        // Spoken text: the spacing a row keeps is not wanted in a sentence.
        Assert.Equal("BVI4RK RCD REQUEST",
            FenixChannelDcduRows.FirstMessageLine(Screen(message: ["cBVI4RKw  RCD REQUEST   "])));
    }

    // ---- A snapshot that cannot be read -----------------------------------------------------

    [Fact]
    public void A_problem_is_reported_and_not_available()
    {
        // Not trusted: the verdict's reason, whatever the message says.
        var reported = new List<string>();
        FenixChannelRows off = FenixChannelDcduRows.Compose(Snapshot(false, "display off", Content(DepartRequestPage1)), reported.Add);

        Assert.Null(off.Rows);
        Assert.Equal("display off", off.Problem);
        Assert.Equal(new[] { "display off" }, reported.ToArray());

        // Trusted with no DCDU at all, and with a letter the app does not know: the decoder's problem, never rows with
        // letters dropped.
        reported.Clear();
        FenixChannelRows none = FenixChannelDcduRows.Compose(Snapshot(true, "trusted", null), reported.Add);
        FenixChannelRows unknown = FenixChannelDcduRows.Compose(Snapshot(true, "trusted", Content((5, "hello"))), reported.Add);

        Assert.Null(none.Rows);
        Assert.Equal("no DCDU content", none.Problem);
        Assert.Null(unknown.Rows);
        Assert.Equal("unknown code 'h' in the DCDU message", unknown.Problem);
        Assert.Equal(new[] { "no DCDU content", "unknown code 'h' in the DCDU message" }, reported.ToArray());

        // The reporter is optional.
        Assert.Equal("display off", FenixChannelDcduRows.Compose(Snapshot(false, "display off", null)).Problem);
    }

    // ---- Culture ----------------------------------------------------------------------------

    [Fact]
    public void Rows_read_the_same_in_de_DE_and_sv_SE()
    {
        // CI runs under en-US: a row built with the PC's culture would read differently here. The only figure the app
        // writes is a key row's number.
        foreach (string culture in new[] { "de-DE", "sv-SE" })
        {
            InCulture(culture, () =>
            {
                Assert.Equal(
                    new[]
                    {
                        "2021Z FROM EDDF     OPEN", "DEPART REQUEST STATUS .", "FSM 2010 261103 EDDF", "BVI4RK  RCD REQUEST",
                        "BEING PROCESSED", "STANDBY  IF NO", "MSG        PGE", "1/2        1/2", "2                CLOSE*>"
                    },
                    RowsOf(DepartRequestPage1).ToArray());
                Assert.Equal("1 <*UNABLE        STBY*>", RowsOf(Clearance)[^2]);
                Assert.Equal("1426Z FROM EDGG     OPEN",
                    FenixChannelDcduRows.HeaderText(Screen("w1426Z FROM EDGG", "gOPEN")));
                Assert.Equal("CLIMB TO", FenixChannelDcduRows.FirstMessageLine(Screen(message: ["cCLIMB TO"])));
            });
        }
    }
}

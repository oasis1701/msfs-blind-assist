// Characterization tests for FenixChannelFmaLayout, which places the FMA's slots by row and column and words a column
// as one line. Pure logic: every slot is written here, the measured ones as the display sent them and with the mode it
// showed in a comment, never read from Fenix's own capture.
//
// The layout was measured in flight on 3.0.0.7613 on 2026-11-04, from take-off to an ILS autoland: every slot from 0
// to 14 that held text, all but slot 12, sat where these tests put it, and slot 15 held no text all flight
// (The_measured_slots_land_in_their_columns names the modes seen in each). Where a test needs text the flight never
// showed, in slot 12 or only to show how the layout joins rows, the text is made up and says so.
//
// The contract: slots 0-14 are three rows of five, row 1 first, the columns running Thrust, Vertical, Lateral,
// Approach, Engagement. Row 3 of the first three columns is the FMA's message and not part of its column. A column's
// rows are the slots Of puts in it, never worked out from the grid a second time. A column is spoken as one line from
// its rows through FenixChannelSpeech, except the Engagement column, whose rows are separate statuses and are joined by
// "; ". A slot past the end of the list counts as blank, the Unknown column has no phrase, and a row with a letter the
// app does not know is never spoken: it spoils a column's one line, and it is left out of the Engagement column.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelFmaLayoutTests
{
    // U+2800, the blank braille cell the display uses as a spacer. Named because it cannot be seen in source.
    private const char Spacer = (char)0x2800;

    private static readonly FenixFmaColumn[] Columns =
    {
        FenixFmaColumn.Thrust, FenixFmaColumn.Vertical, FenixFmaColumn.Lateral, FenixFmaColumn.Approach,
        FenixFmaColumn.Engagement
    };

    // ---- The grid ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 1, nameof(FenixFmaColumn.Thrust))]
    [InlineData(1, 1, nameof(FenixFmaColumn.Vertical))]
    [InlineData(2, 1, nameof(FenixFmaColumn.Lateral))]
    [InlineData(3, 1, nameof(FenixFmaColumn.Approach))]
    [InlineData(4, 1, nameof(FenixFmaColumn.Engagement))]
    [InlineData(5, 2, nameof(FenixFmaColumn.Thrust))]
    [InlineData(6, 2, nameof(FenixFmaColumn.Vertical))]
    [InlineData(7, 2, nameof(FenixFmaColumn.Lateral))]
    [InlineData(8, 2, nameof(FenixFmaColumn.Approach))]
    [InlineData(9, 2, nameof(FenixFmaColumn.Engagement))]
    [InlineData(10, 3, nameof(FenixFmaColumn.Thrust))]
    [InlineData(11, 3, nameof(FenixFmaColumn.Vertical))]
    [InlineData(12, 3, nameof(FenixFmaColumn.Lateral))]
    [InlineData(13, 3, nameof(FenixFmaColumn.Approach))]
    [InlineData(14, 3, nameof(FenixFmaColumn.Engagement))]
    public void Slots_zero_to_fourteen_are_three_rows_of_five(int slot, int row, string column)
    {
        Assert.Equal((row, Enum.Parse<FenixFmaColumn>(column)), FenixChannelFmaLayout.Of(slot));
    }

    // Every slot seen holding text in the flight of 2026-11-04 on 3.0.0.7613, each with the modes it showed. Slot 12 is
    // the only one of 0 to 14 never seen, and slot 15 held no text all flight.
    [Theory]
    [InlineData(0, 1, nameof(FenixFmaColumn.Thrust))]       // MAN, THR CLB, SPEED, MACH, THR IDLE
    [InlineData(1, 1, nameof(FenixFmaColumn.Vertical))]     // SRS, CLB, ALT CST*, OP CLB, ALT*, ALT CRZ, DES, V/S, ALT, OP DES, G/S*, G/S, LAND, FLARE, ROLL OUT
    [InlineData(2, 1, nameof(FenixFmaColumn.Lateral))]      // NAV, HDG, LOC *, LOC
    [InlineData(3, 1, nameof(FenixFmaColumn.Approach))]     // CAT3, CAT1
    [InlineData(4, 1, nameof(FenixFmaColumn.Engagement))]   // AP1, AP2, AP1+2
    [InlineData(5, 2, nameof(FenixFmaColumn.Thrust))]       // FLX +52, THR, MCT
    [InlineData(6, 2, nameof(FenixFmaColumn.Vertical))]     // CLB, ALT, G/S armed
    [InlineData(7, 2, nameof(FenixFmaColumn.Lateral))]      // NAV, LOC armed
    [InlineData(8, 2, nameof(FenixFmaColumn.Approach))]     // SINGLE, DUAL
    [InlineData(9, 2, nameof(FenixFmaColumn.Engagement))]   // FD 1 2
    [InlineData(10, 3, nameof(FenixFmaColumn.Thrust))]      // LVR CLB
    [InlineData(11, 3, nameof(FenixFmaColumn.Vertical))]    // MORE DRAG
    [InlineData(13, 3, nameof(FenixFmaColumn.Approach))]    // RADIO 200
    [InlineData(14, 3, nameof(FenixFmaColumn.Engagement))]  // A/THR
    public void The_measured_slots_land_in_their_columns(int slot, int row, string column)
    {
        (int actualRow, FenixFmaColumn actualColumn) = FenixChannelFmaLayout.Of(slot);

        Assert.Equal(row, actualRow);
        Assert.Equal(Enum.Parse<FenixFmaColumn>(column), actualColumn);
    }

    [Fact]
    public void Slot_fifteen_is_unknown()
    {
        // Slot 15 held no text through a whole flight, so nothing says where it would draw.
        Assert.Equal((0, FenixFmaColumn.Unknown), FenixChannelFmaLayout.Of(15));
        Assert.False(FenixChannelFmaLayout.IsMessage(15));

        // No other number outside 0-14 has a place either.
        foreach (int slot in new[] { -1, 16, 20, int.MinValue, int.MaxValue })
        {
            Assert.Equal((0, FenixFmaColumn.Unknown), FenixChannelFmaLayout.Of(slot));
            Assert.False(FenixChannelFmaLayout.IsMessage(slot));
        }

        // It is in no column, so whatever it holds, no column's phrase has it.
        FenixFmaSlot[] slots = Fma((15, "gSLOT 15"));
        foreach (FenixFmaColumn column in Columns)
        {
            Assert.Null(FenixChannelFmaLayout.ColumnPhrase(slots, column));
        }
    }

    // ---- The messages -----------------------------------------------------------------------------

    [Theory]
    [InlineData(10, true)]     // Thrust, row 3: LVR CLB
    [InlineData(11, true)]     // Vertical, row 3
    [InlineData(12, true)]     // Lateral, row 3
    [InlineData(13, false)]    // Approach, row 3: it belongs to its column
    [InlineData(14, false)]    // Engagement, row 3: A/THR belongs to its column
    [InlineData(0, false)]
    public void Row_three_of_the_first_three_columns_is_a_message(int slot, bool expected)
    {
        Assert.Equal(expected, FenixChannelFmaLayout.IsMessage(slot));
    }

    [Fact]
    public void No_other_slot_is_a_message()
    {
        // Every number from -5 to 24: rows 1 and 2, the Approach and Engagement columns, and what is outside the grid.
        int[] messages = Enumerable.Range(-5, 30).Where(FenixChannelFmaLayout.IsMessage).ToArray();

        Assert.Equal(new[] { 10, 11, 12 }, messages);
    }

    // ---- The names --------------------------------------------------------------------------------

    [Fact]
    public void Each_column_has_its_name_and_the_unknown_one_has_none()
    {
        Assert.Equal("Thrust", FenixChannelFmaLayout.NameOf(FenixFmaColumn.Thrust));
        Assert.Equal("Vertical", FenixChannelFmaLayout.NameOf(FenixFmaColumn.Vertical));
        Assert.Equal("Lateral", FenixChannelFmaLayout.NameOf(FenixFmaColumn.Lateral));
        Assert.Equal("Approach", FenixChannelFmaLayout.NameOf(FenixFmaColumn.Approach));
        Assert.Equal("Engagement", FenixChannelFmaLayout.NameOf(FenixFmaColumn.Engagement));

        // A value the enum does not have is never named either.
        Assert.Equal("", FenixChannelFmaLayout.NameOf(FenixFmaColumn.Unknown));
        Assert.Equal("", FenixChannelFmaLayout.NameOf((FenixFmaColumn)99));
    }

    // ---- A column's phrase ------------------------------------------------------------------------

    [Fact]
    public void A_column_phrase_joins_its_rows()
    {
        // One frame with the texts below as the display sends them (see ExampleFrame). Its Lateral and Approach
        // columns are blank.
        FenixFmaSlot[] frame = ExampleFrame();

        // Vertical: the rows are drawn in pieces of one line, each stretch's color named after its words.
        Assert.Equal("ALT green, G/S cyan", Phrase(frame, FenixFmaColumn.Vertical));

        // Thrust: FLX comes before the cyan +56, so it is white, the same white as the MAN above it. Row 3, LVR CLB,
        // is the FMA's message and is not part of the column.
        Assert.Equal("MAN FLX white, +56 cyan", Phrase(frame, FenixFmaColumn.Thrust));

        // Engagement: AP, FD and A/THR are separate statuses, so each row is a line of its own, joined by "; ".
        Assert.Equal("AP1, white; 1 2, white; A/THR, cyan", Phrase(frame, FenixFmaColumn.Engagement));

        // A column with nothing in any of its rows has no phrase.
        Assert.Null(Phrase(frame, FenixFmaColumn.Lateral));
        Assert.Null(Phrase(frame, FenixFmaColumn.Approach));
    }

    [Fact]
    public void A_blank_row_adds_nothing_to_its_column()
    {
        // Row 2 only: the first row is blank, and nothing of it is left in the line.
        Assert.Equal("FLX white, +56 cyan", Phrase(Fma((5, "FLX c+56")), FenixFmaColumn.Thrust));

        // Row 1 and row 3 of the Engagement column with the row between them blank: the blank row is left out.
        Assert.Equal("AP1, white; A/THR, cyan", Phrase(Fma((4, "wAP1"), (14, "cA/THR")), FenixFmaColumn.Engagement));
        Assert.Equal("1 2, white", Phrase(Fma((9, "w1   2")), FenixFmaColumn.Engagement));
    }

    [Fact]
    public void The_message_is_not_part_of_its_column_whatever_it_holds()
    {
        Assert.Equal("MAN, white", Phrase(Fma((0, "wbMAN"), (10, "wfLVR CLB")), FenixFmaColumn.Thrust));
        Assert.Equal("MAN, white", Phrase(Fma((0, "wbMAN"), (10, "xLVR CLB")), FenixFmaColumn.Thrust));

        // The column has nothing else, so there is no phrase: the message alone does not make one.
        Assert.Null(Phrase(Fma((10, "wfLVR CLB")), FenixFmaColumn.Thrust));

        // The same for the Vertical and the Lateral column. The flight showed MORE DRAG in slot 11 and nothing in slot
        // 12, so this text is made up.
        Assert.Equal("ALT, green", Phrase(Fma((1, "gALT"), (11, "wfMSG")), FenixFmaColumn.Vertical));
        Assert.Equal("NAV, green", Phrase(Fma((2, "gNAV"), (12, "wfMSG")), FenixFmaColumn.Lateral));
        Assert.Null(Phrase(Fma((11, "wfMSG")), FenixFmaColumn.Vertical));
        Assert.Null(Phrase(Fma((12, "wfMSG")), FenixFmaColumn.Lateral));
    }

    [Fact]
    public void A_column_phrase_follows_where_Of_puts_each_slot()
    {
        // Of alone says where a slot sits, and plan 08 may change it after measuring. A phrase that worked the grid out
        // for itself would go on reading the old slot, and the window and the announcement this layout keeps in step
        // would part. So this asks Of, not the grid: each slot, alone in a frame, is heard in the column Of puts it in,
        // unless it is a message, and in no other.
        for (int slot = 0; slot < 15; slot++)
        {
            FenixFmaSlot[] frame = Fma((slot, "gTEXT"));
            foreach (FenixFmaColumn column in Columns)
            {
                bool heard = FenixChannelFmaLayout.Of(slot).Column == column && !FenixChannelFmaLayout.IsMessage(slot);

                Assert.True(
                    (heard ? "TEXT, green" : null) == Phrase(frame, column),
                    $"slot {slot} in the {column} column: Of says {FenixChannelFmaLayout.Of(slot)}");
            }
        }
    }

    [Fact]
    public void The_approach_columns_third_row_is_part_of_the_column()
    {
        // The flight showed CAT3, DUAL and RADIO 200 in these three slots; the texts here are made up and only show that
        // all of the Approach column's rows, row 3 included, are in the column, in row order.
        FenixFmaSlot[] slots = Fma((3, "gA"), (8, "gB"), (13, "gC"));

        Assert.Equal("A B C, green", Phrase(slots, FenixFmaColumn.Approach));
    }

    [Fact]
    public void A_column_of_blank_looking_slots_has_no_phrase()
    {
        // What a blank slot can look like: nothing, a color letter alone, spaces, the display's spacer, "!" as a space.
        string[] blanks = { "", "g", " ", "g!!", Spacer.ToString(), "g" + Spacer + Spacer + "!", "cs" };

        foreach (string blank in blanks)
        {
            FenixFmaSlot[] slots = Enumerable.Repeat(new FenixFmaSlot(blank, false), 16).ToArray();
            foreach (FenixFmaColumn column in Columns)
            {
                Assert.Null(Phrase(slots, column));
            }
        }
    }

    // ---- A short list -----------------------------------------------------------------------------

    [Fact]
    public void A_slot_past_the_end_of_the_list_counts_as_blank()
    {
        // No slots at all: every column is empty, and nothing throws.
        foreach (FenixFmaColumn column in Columns)
        {
            Assert.Null(Phrase(Array.Empty<FenixFmaSlot>(), column));
        }

        // The first seven slots: row 1 of every column, and row 2 of the Thrust and Vertical columns. The rest of the
        // grid is past the end of the list, so the Engagement column's rows 2 and 3 count as blank, not as missing.
        FenixFmaSlot[] seven = ExampleFrame().Take(7).ToArray();
        Assert.Equal("MAN FLX white, +56 cyan", Phrase(seven, FenixFmaColumn.Thrust));
        Assert.Equal("ALT green, G/S cyan", Phrase(seven, FenixFmaColumn.Vertical));
        Assert.Null(Phrase(seven, FenixFmaColumn.Lateral));
        Assert.Null(Phrase(seven, FenixFmaColumn.Approach));
        Assert.Equal("AP1, white", Phrase(seven, FenixFmaColumn.Engagement));

        // One slot: only the first column has anything.
        FenixFmaSlot[] one = { new("wbMAN", false) };
        Assert.Equal("MAN, white", Phrase(one, FenixFmaColumn.Thrust));
        foreach (FenixFmaColumn column in new[]
                 {
                     FenixFmaColumn.Vertical, FenixFmaColumn.Lateral, FenixFmaColumn.Approach, FenixFmaColumn.Engagement
                 })
        {
            Assert.Null(Phrase(one, column));
        }
    }

    [Fact]
    public void A_slot_with_no_text_counts_as_blank()
    {
        // A slot built without its text (a struct's default) has none, and the column is spoken from the rest.
        FenixFmaSlot[] slots = Fma((1, "gALT"));
        slots[6] = default;

        Assert.Equal("ALT, green", Phrase(slots, FenixFmaColumn.Vertical));
    }

    // ---- The unknown column -----------------------------------------------------------------------

    [Fact]
    public void The_unknown_column_has_no_phrase()
    {
        FenixFmaSlot[] full = Fma((0, "wbMAN"), (1, "gALT"), (2, "gNAV"), (3, "gA"), (4, "wAP1"), (5, "FLX c+56"),
            (6, "cG/S"), (7, "gB"), (8, "gC"), (9, "w1   2"), (10, "wfLVR CLB"), (11, "gD"), (12, "gE"), (13, "gF"),
            (14, "cA/THR"), (15, "gG"));

        Assert.Null(Phrase(full, FenixFmaColumn.Unknown));

        // Nor does a value the enum does not have.
        Assert.Null(Phrase(full, (FenixFmaColumn)99));
        Assert.Null(Phrase(full, (FenixFmaColumn)(-1)));
    }

    // ---- A letter the app does not know -----------------------------------------------------------

    [Fact]
    public void A_row_with_an_unknown_letter_spoils_a_column_drawn_as_one_line()
    {
        // "k" is not a code the app knows. The column is one line from its rows, so the row spoils the whole line,
        // whichever row it is, and nothing is thrown.
        Assert.Null(Phrase(Fma((1, "gALT"), (6, "kG/S")), FenixFmaColumn.Vertical));
        Assert.Null(Phrase(Fma((1, "kALT"), (6, "cG/S")), FenixFmaColumn.Vertical));
        Assert.Null(Phrase(Fma((0, "wbMAN"), (5, "FLX k+56")), FenixFmaColumn.Thrust));

        // Another column of the same frame is spoken as before.
        FenixFmaSlot[] frame = Fma((0, "wbMAN"), (1, "kALT"), (6, "cG/S"));
        Assert.Equal("MAN, white", Phrase(frame, FenixFmaColumn.Thrust));
        Assert.Null(Phrase(frame, FenixFmaColumn.Vertical));
    }

    [Fact]
    public void A_row_with_an_unknown_letter_is_left_out_of_the_engagement_column()
    {
        // The Engagement column's rows are separate statuses: the row is left out, and the others are still spoken.
        Assert.Equal("AP1, white; A/THR, cyan",
            Phrase(Fma((4, "wAP1"), (9, "k1   2"), (14, "cA/THR")), FenixFmaColumn.Engagement));
        Assert.Equal("1 2, white; A/THR, cyan",
            Phrase(Fma((4, "kAP1"), (9, "w1   2"), (14, "cA/THR")), FenixFmaColumn.Engagement));
        Assert.Equal("AP1, white; 1 2, white",
            Phrase(Fma((4, "wAP1"), (9, "w1   2"), (14, "kA/THR")), FenixFmaColumn.Engagement));

        // All of them: nothing is left to speak.
        Assert.Null(Phrase(Fma((4, "kAP1"), (9, "k1   2"), (14, "kA/THR")), FenixFmaColumn.Engagement));
    }

    // ---- Culture ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_column_phrase_is_the_same_in_every_culture(string culture)
    {
        InCulture(culture, () =>
        {
            FenixFmaSlot[] frame = ExampleFrame();
            Assert.Equal("MAN FLX white, +56 cyan", Phrase(frame, FenixFmaColumn.Thrust));
            Assert.Equal("ALT green, G/S cyan", Phrase(frame, FenixFmaColumn.Vertical));
            Assert.Equal("AP1, white; 1 2, white; A/THR, cyan", Phrase(frame, FenixFmaColumn.Engagement));
            Assert.Null(Phrase(frame, FenixFmaColumn.Lateral));

            // Not text the FMA is known to draw: it checks that the text is carried as it came, a decimal point staying
            // a point where the PC's culture writes a comma, and a hyphen staying a hyphen where sv-SE writes a minus.
            Assert.Equal("QNH white, -1013.2 cyan", Phrase(Fma((2, "wQNH"), (7, "c-1013.2")), FenixFmaColumn.Lateral));

            Assert.Equal("Thrust", FenixChannelFmaLayout.NameOf(FenixFmaColumn.Thrust));
            Assert.Equal("Engagement", FenixChannelFmaLayout.NameOf(FenixFmaColumn.Engagement));
            Assert.Equal((2, FenixFmaColumn.Lateral), FenixChannelFmaLayout.Of(7));
            Assert.True(FenixChannelFmaLayout.IsMessage(10));
        });
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    // A 16-slot FMA as the decoder builds it: blank where no text is given, and no change box on any slot.
    private static FenixFmaSlot[] Fma(params (int Slot, string Text)[] texts)
    {
        FenixFmaSlot[] slots = Enumerable.Repeat(new FenixFmaSlot("", false), 16).ToArray();
        foreach ((int slot, string text) in texts)
        {
            slots[slot] = new FenixFmaSlot(text, false);
        }

        return slots;
    }

    // One frame with the texts the tests word: MAN, ALT and G/S, the thrust limit FLX +56, the message LVR CLB, and the
    // engagement statuses AP1, 1 FD 2 and A/THR, each as the display sends it. The other slots are blank, so the
    // Lateral and the Approach columns have nothing in them.
    private static FenixFmaSlot[] ExampleFrame() => Fma(
        (0, "wbMAN"), (1, "gALT"), (4, "wAP1"), (5, "FLX c+56"), (6, "cG/S"), (9, "w1   2"), (10, "wfLVR CLB"),
        (14, "cA/THR"));

    private static string? Phrase(IReadOnlyList<FenixFmaSlot> slots, FenixFmaColumn column) =>
        FenixChannelFmaLayout.ColumnPhrase(slots, column);

    // Runs the action under another culture, current and UI, and puts both back.
    private static void InCulture(string name, Action action)
    {
        CultureInfo savedCulture = CultureInfo.CurrentCulture;
        CultureInfo savedUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            CultureInfo.CurrentUICulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUiCulture;
        }
    }
}

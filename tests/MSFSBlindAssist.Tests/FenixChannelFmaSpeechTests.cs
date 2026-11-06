// Characterization tests for FenixChannelFmaSpeech: what the pilot would hear of the FMA at one instant (Read), the
// announcement for a change between two instants (Change) and the slots no column claims (Unplaced). Pure logic: the
// slots are written here as the display sends them. Slot numbers are FenixChannelFmaLayout's.
//
// What is compared is what is heard, never the raw slots: the change box, and the b, f, s, l and u letters, are never
// spoken, so they never make a column speak again. Column 5 and unplaced slots are in no reading.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelFmaSpeechTests
{
    private static List<FenixFmaSlot> Blank() =>
        Enumerable.Range(0, FenixChannelFields.FmaEntry.SlotCount).Select(_ => new FenixFmaSlot("", false)).ToList();

    private static List<FenixFmaSlot> Slots(params (int Slot, string Text)[] texts) => Slots(false, texts);

    private static List<FenixFmaSlot> Slots(bool changeBox, params (int Slot, string Text)[] texts)
    {
        List<FenixFmaSlot> slots = Blank();
        foreach ((int slot, string text) in texts) slots[slot] = new FenixFmaSlot(text, changeBox);
        return slots;
    }

    private static string? Heard(List<FenixFmaSlot> before, List<FenixFmaSlot> after) =>
        FenixChannelFmaSpeech.Change(FenixChannelFmaSpeech.Read(before), FenixChannelFmaSpeech.Read(after));

    [Fact]
    public void A_one_row_column_is_spoken_with_its_color()
    {
        Assert.Equal("FMA: Thrust: THR CLB, green; Vertical: CLB, green",
            Heard(Blank(), Slots((0, "gTHR CLB"), (1, "gCLB"))));
        Assert.Equal("FMA: Vertical: ALT CRZ, green", Heard(Blank(), Slots((1, "gALT!CRZ"))));
    }

    [Fact]
    public void A_columns_rows_are_one_line()
    {
        Assert.Equal("FMA: Vertical: ALT green, G/S cyan", Heard(Blank(), Slots((1, "gALT"), (6, "cG/S"))));
        Assert.Equal("FMA: Approach: CAT 3 DUAL, white", Heard(Blank(), Slots((3, "wCAT 3"), (8, "wDUAL"))));
        // Text before a color letter is white (plan 01), so FLX joins MAN.
        Assert.Equal("FMA: Thrust: MAN FLX white, +56 cyan", Heard(Blank(), Slots((0, "wbMAN"), (5, "FLX c+56"))));
    }

    [Fact]
    public void Row_three_is_an_fma_message()
    {
        Assert.Equal("FMA message: LVR CLB, white", Heard(Blank(), Slots((10, "wfLVR CLB"))));
        Assert.Equal("FMA: Thrust: THR CLB, green; FMA message: LVR ASYM, amber",
            Heard(Blank(), Slots((0, "gTHR CLB"), (10, "aLVR ASYM"))));
    }

    [Fact]
    public void An_armed_mode_becoming_active_is_one_change_of_its_column()
    {
        Assert.Equal("FMA: Lateral: LOC*, green",
            Heard(Slots((2, "gNAV"), (7, "cLOC")), Slots((2, "gLOC*"), (7, ""))));
    }

    [Fact]
    public void A_column_or_message_that_goes_blank_says_nothing()
    {
        Assert.Null(Heard(Slots((0, "gSPEED")), Blank()));
        Assert.Null(Heard(Slots((10, "wfLVR CLB")), Blank()));
    }

    [Fact]
    public void What_is_compared_is_what_is_heard()
    {
        FenixFmaReading Reading(List<FenixFmaSlot> slots) => FenixChannelFmaSpeech.Read(slots);

        Assert.Equal(Reading(Slots(false, (6, "cALT"))), Reading(Slots(true, (6, "cALT"))));
        Assert.Equal(Reading(Slots((0, "wMAN"))), Reading(Slots((0, "wbMAN"))));
        Assert.Equal(Reading(Slots((10, "wLVR CLB"))), Reading(Slots((10, "wfLVR CLB"))));
        Assert.Equal(Reading(Slots((1, "gALT"))), Reading(Slots((1, "gsALT"))));
        Assert.Equal(Reading(Slots((1, "gALT"))), Reading(Slots((1, "glALT"))));
        Assert.Equal(Reading(Slots((1, "gALT"))), Reading(Slots((1, "guALT"))));
        Assert.Equal(Reading(Slots((1, "gALT CRZ"))), Reading(Slots((1, "gALT!CRZ"))));
        Assert.Equal(Reading(Slots((1, "gALT CRZ"))).GetHashCode(), Reading(Slots((1, "gALT!CRZ"))).GetHashCode());
        Assert.NotEqual(Reading(Slots((6, "cALT"))), Reading(Slots((6, "mALT"))));
    }

    private static List<FenixFmaSlot> Boxed(params (int Slot, string Text, bool Box)[] texts)
    {
        List<FenixFmaSlot> slots = Blank();
        foreach ((int slot, string text, bool box) in texts) slots[slot] = new FenixFmaSlot(text, box);
        return slots;
    }

    private static string? ColumnOf(List<FenixFmaSlot> slots, FenixFmaColumn column) =>
        FenixChannelFmaSpeech.Read(slots).Columns[FenixChannelFmaSpeech.SpokenColumns.ToList().IndexOf(column)];

    [Fact]
    public void The_common_modes_are_heard_once()
    {
        List<FenixFmaSlot> before = Slots((0, "gSPEED"), (1, "gG/S"), (2, "gLOC"), (3, "CAT3"), (4, "wAP1+2"),
            (8, "DUAL"), (9, "w1   2"), (14, "wA/THR"));
        List<FenixFmaSlot> land = Boxed((0, "gSPEED", false), (1, "gLAND", true), (2, "gLOC", false),
            (3, "CAT3", false), (4, "wAP1+2", false), (8, "DUAL", false), (9, "w1   2", false), (14, "wA/THR", false));
        List<FenixFmaSlot> flare = Boxed((0, "gSPEED", false), (1, "gFLARE", true), (2, "gLOC", false),
            (3, "CAT3", false), (4, "wAP1+2", false), (8, "DUAL", false), (9, "w1   2", false), (14, "wA/THR", false));
        List<FenixFmaSlot> rollOut = Boxed((0, "", true), (1, "gROLL OUT", true), (2, "gLOC", false),
            (3, "CAT3", false), (4, "wAP1+2", false), (8, "DUAL", false), (9, "w1   2", false));

        Assert.Equal("FMA: Vertical: LAND, green", Heard(before, land));
        Assert.Equal("FMA: Vertical: FLARE, green", Heard(land, flare));
        Assert.Equal("FMA: Vertical: ROLL OUT, green", Heard(flare, rollOut));
    }

    [Theory]
    [InlineData("gLAND")]
    [InlineData("gFLARE")]
    [InlineData("gROLL OUT")]
    [InlineData("gROLL!OUT")]
    public void The_lateral_mode_under_a_common_mode_is_never_spoken(string vertical)
    {
        Assert.Null(ColumnOf(Slots((1, vertical), (2, "gLOC")), FenixFmaColumn.Lateral));
        Assert.Equal("LOC, green", ColumnOf(Slots((1, "gG/S"), (2, "gLOC")), FenixFmaColumn.Lateral));
    }

    [Fact]
    public void The_lateral_mode_is_silent_on_the_rollout_as_recorded()
    {
        List<FenixFmaSlot> before = Boxed((1, "gROLL OUT", false), (2, "gLOC", false), (3, "CAT1", true),
            (4, "", true), (9, "w1   2", false));
        List<FenixFmaSlot> after = Boxed((1, "gROLL OUT", false), (2, "gLOC *", true), (3, "CAT1", true),
            (4, "", true), (9, "w1   2", false));

        Assert.Null(Heard(before, after));
    }

    [Fact]
    public void A_lateral_mode_is_heard_once_the_common_mode_ends()
    {
        Assert.Equal("FMA: Vertical: SRS, green; Lateral: GA TRK, green",
            Heard(Slots((1, "gLAND"), (2, "gLOC")), Slots((1, "gSRS"), (2, "gGA TRK"))));
        Assert.Equal("FMA: Vertical: G/S, green; Lateral: LOC, green",
            Heard(Slots((1, "gLAND"), (2, "gLOC")), Slots((1, "gG/S"), (2, "gLOC"))));
    }

    [Fact]
    public void The_approach_columns_third_row_follows_the_ruling()
    {
        const string radio = "RADIOdc⠀⠀ 200";
        List<FenixFmaSlot> shown = Slots((3, "CAT3"), (8, "DUAL"), (13, radio));
        FenixFmaReading reading = FenixChannelFmaSpeech.Read(shown);

        Assert.Equal("CAT3 DUAL, RADIO white, 200 cyan", ColumnOf(shown, FenixFmaColumn.Approach));
        Assert.DoesNotContain(reading.Messages, line => line is not null && line.Contains("RADIO"));
        Assert.False(FenixChannelFmaLayout.IsMessage(13));
        Assert.Equal("FMA: Approach: RADIO white, 200 cyan", Heard(Blank(), Slots((13, radio))));
    }

    [Fact]
    public void Column_five_is_never_spoken()
    {
        List<FenixFmaSlot> before = Slots((4, "wAP1"), (9, "w1   2"), (14, "cA/THR"));
        List<FenixFmaSlot> after = Slots((14, "wA/THR"));

        Assert.Equal(FenixChannelFmaSpeech.Read(before), FenixChannelFmaSpeech.Read(after));
        Assert.Null(Heard(before, after));

        after[0] = new FenixFmaSlot("gTHR CLB", false);
        Assert.Equal("FMA: Thrust: THR CLB, green", Heard(before, after));
    }

    [Fact]
    public void An_unplaced_slot_is_never_spoken_and_is_named_for_the_log()
    {
        List<FenixFmaSlot> shown = Slots((15, "wMORE DRAG"));

        Assert.Equal(FenixChannelFmaSpeech.Read(Blank()), FenixChannelFmaSpeech.Read(shown));
        Assert.Null(Heard(Blank(), shown));
        Assert.Equal(new[] { "slot 15 shows \"wMORE DRAG\"" }, FenixChannelFmaSpeech.Unplaced(shown));
        Assert.Empty(FenixChannelFmaSpeech.Unplaced(Blank()));
    }

    [Fact]
    public void The_reading_has_one_entry_per_spoken_column_and_per_message()
    {
        FenixFmaReading reading = FenixChannelFmaSpeech.Read(Blank());

        Assert.Equal(FenixChannelFmaSpeech.SpokenColumns.Count, reading.Columns.Count);
        Assert.Equal(3, reading.Messages.Count);
        Assert.All(reading.Columns, Assert.Null);
        Assert.All(reading.Messages, Assert.Null);
        Assert.Throws<ArgumentException>(() => FenixChannelFmaSpeech.Read(new List<FenixFmaSlot>()));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Speech_is_the_same_in_every_culture(string culture)
    {
        string? heard = TestCulture.Run(culture,
            () => Heard(Blank(), Slots((0, "wbMAN"), (5, "FLX c+56"), (10, "aLVR ASYM"))));

        Assert.Equal("FMA: Thrust: MAN FLX white, +56 cyan; FMA message: LVR ASYM, amber", heard);
    }
}

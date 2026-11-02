// Characterization tests for FenixChannelText, which parses one coded line of the Fenix display channel
// (an FMA slot, an upper ECAM line, the FOB text) into runs. Pure logic: every line is written here, never
// read from Fenix's own capture.
//
// The contract the trust gate and the readouts build on: a lowercase letter before a run sets its color,
// underline or size and is never kept as text, and the colors stay on the runs as data, because the color
// is the meaning (a warning, a memo, an action) and is never stripped for speech. A letter the app does not
// know is listed on the line, once and in the order it came, so the gate can distrust the display that
// drew it. Display text is uppercase, so an uppercase letter is never a code.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelTextTests
{
    // U+2800, the blank braille cell the display uses as a spacer. Named because it cannot be seen in source.
    private const char Spacer = (char)0x2800;

    // ---- Colors, underline and size -------------------------------------------------------------

    [Theory]
    [InlineData('w', nameof(FenixTextColor.White))]
    [InlineData('g', nameof(FenixTextColor.Green))]
    [InlineData('c', nameof(FenixTextColor.Cyan))]
    [InlineData('a', nameof(FenixTextColor.Amber))]
    [InlineData('m', nameof(FenixTextColor.Magenta))]
    [InlineData('y', nameof(FenixTextColor.Yellow))]
    [InlineData('r', nameof(FenixTextColor.Red))]
    public void Each_color_letter_sets_its_color(char code, string color)
    {
        FenixTextLine line = FenixChannelText.Parse($"{code}TEXT");

        Assert.Equal(new[] { Run("TEXT", Enum.Parse<FenixTextColor>(color)) }, line.Runs);
        Assert.Empty(line.UnknownCodes);
    }

    [Fact]
    public void A_run_before_any_code_has_no_color()
    {
        FenixTextLine line = FenixChannelText.Parse("15");

        Assert.Equal(new[] { Run("15", null) }, line.Runs);
        Assert.Empty(line.UnknownCodes);
    }

    [Fact]
    public void U_toggles_underline()
    {
        FenixTextLine line = FenixChannelText.Parse("guT.Ou AUTO BRK");

        Assert.Equal(new[]
        {
            Run("T.O", FenixTextColor.Green, underlined: true),
            Run(" AUTO BRK", FenixTextColor.Green)
        }, line.Runs);
        Assert.Empty(line.UnknownCodes);
    }

    [Fact]
    public void Sizes_follow_s_and_l()
    {
        FenixTextLine line = FenixChannelText.Parse("gs KGl X");

        Assert.Equal(new[]
        {
            Run(" KG", FenixTextColor.Green, size: FenixTextSize.Small),
            Run(" X", FenixTextColor.Green, size: FenixTextSize.Large)
        }, line.Runs);
        Assert.Empty(line.UnknownCodes);
    }

    [Fact]
    public void A_code_that_changes_nothing_does_not_split_a_run()
    {
        // The second g restates the green the line is already in, and the second s the small size: neither
        // changes how the text is drawn, so the text stays one run.
        FenixTextLine line = FenixChannelText.Parse("gsABgCDsEF");

        Assert.Equal(new[] { Run("ABCDEF", FenixTextColor.Green, size: FenixTextSize.Small) }, line.Runs);
        Assert.Empty(line.UnknownCodes);
    }

    // ---- The spacer and what is never a code ----------------------------------------------------

    [Fact]
    public void The_braille_blank_reads_as_a_space()
    {
        FenixTextLine line = FenixChannelText.Parse("g---w" + Spacer + "/" + Spacer + "g---");

        Assert.Equal(new[]
        {
            Run("---", FenixTextColor.Green),
            Run(" / ", FenixTextColor.White),
            Run("---", FenixTextColor.Green)
        }, line.Runs);
        Assert.Equal("--- / ---", line.JoinedText);
        Assert.Empty(line.UnknownCodes);
    }

    [Fact]
    public void Uppercase_is_never_a_code()
    {
        FenixTextLine line = FenixChannelText.Parse("wPARK BRK");

        Assert.Equal(new[] { Run("PARK BRK", FenixTextColor.White) }, line.Runs);
        Assert.Empty(line.UnknownCodes);
    }

    [Fact]
    public void A_lowercase_letter_outside_ascii_is_text_not_a_code()
    {
        // U+00E9 is a lowercase letter, but only a to z are codes: it stays in the text and is not unknown.
        const char EAcute = (char)0xE9;

        FenixTextLine line = FenixChannelText.Parse("g" + EAcute + "TEXT");

        Assert.Equal(new[] { Run(EAcute + "TEXT", FenixTextColor.Green) }, line.Runs);
        Assert.Empty(line.UnknownCodes);
    }

    // ---- A letter the app does not know ---------------------------------------------------------

    [Fact]
    public void An_unknown_letter_is_listed_and_not_kept_as_text()
    {
        FenixTextLine line = FenixChannelText.Parse("xENG 1 FIRE");

        Assert.Equal(new[] { 'x' }, line.UnknownCodes);
        Assert.Equal(new[] { Run("ENG 1 FIRE", null) }, line.Runs);
        Assert.Equal("ENG 1 FIRE", line.JoinedText);
    }

    [Fact]
    public void Each_unknown_letter_is_listed_once_in_the_order_it_came()
    {
        // x comes first and again later, so the list is neither sorted nor repeating: x, d, q.
        FenixTextLine line = FenixChannelText.Parse("xAdBqCxD");

        Assert.Equal(new[] { 'x', 'd', 'q' }, line.UnknownCodes);
        Assert.Equal("ABCD", line.JoinedText);
    }

    // ---- Whole lines ----------------------------------------------------------------------------

    [Fact]
    public void The_fob_line_parses_into_three_runs()
    {
        FenixTextLine line = FenixChannelText.Parse("wFOB:   g2940 cs KG");

        Assert.Equal(new[]
        {
            Run("FOB:   ", FenixTextColor.White),
            Run("2940 ", FenixTextColor.Green),
            Run(" KG", FenixTextColor.Cyan, size: FenixTextSize.Small)
        }, line.Runs);
        Assert.Equal("FOB:   2940  KG", line.JoinedText);
        Assert.Empty(line.UnknownCodes);
    }

    [Fact]
    public void The_red_lines_of_a_takeoff_config_warning_parse()
    {
        // The TO CONFIG test with the flaps up, on 3.0.0.7613 (2026-11-02): the upper ECAM's red warning lines, the
        // title underlined. Red's letter is r; the u after CONFIG ends the underline, and the space after it is still
        // red. Neither line holds a code the app does not know.
        FenixTextLine title = FenixChannelText.Parse("urCONFIGu ");
        FenixTextLine flaps = FenixChannelText.Parse("rFLAPS NOT IN T.O CONFIG");

        Assert.Equal(new[]
        {
            Run("CONFIG", FenixTextColor.Red, underlined: true),
            Run(" ", FenixTextColor.Red)
        }, title.Runs);
        Assert.Equal("CONFIG ", title.JoinedText);
        Assert.Empty(title.UnknownCodes);

        Assert.Equal(new[] { Run("FLAPS NOT IN T.O CONFIG", FenixTextColor.Red) }, flaps.Runs);
        Assert.Equal("FLAPS NOT IN T.O CONFIG", flaps.JoinedText);
        Assert.Empty(flaps.UnknownCodes);
    }

    [Fact]
    public void The_findings_memo_line_parses()
    {
        FenixTextLine line = FenixChannelText.Parse("g    CABINc......CHECK");

        Assert.Equal(new[]
        {
            Run("    CABIN", FenixTextColor.Green),
            Run("......CHECK", FenixTextColor.Cyan)
        }, line.Runs);
        Assert.Empty(line.UnknownCodes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Null_or_empty_is_an_empty_line(string? raw)
    {
        FenixTextLine line = FenixChannelText.Parse(raw);

        Assert.Empty(line.Runs);
        Assert.Empty(line.UnknownCodes);
        Assert.Equal("", line.JoinedText);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static FenixTextRun Run(string text, FenixTextColor? color, bool underlined = false,
        FenixTextSize size = FenixTextSize.Normal) => new(text, color, underlined, size);
}

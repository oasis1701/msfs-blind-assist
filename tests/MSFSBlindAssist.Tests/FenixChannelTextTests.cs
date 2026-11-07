// Characterization tests for FenixChannelText, which parses one coded line of the Fenix display channel
// (an FMA slot, an upper ECAM line, the FOB text) into runs. Pure logic: every line is written here, never
// read from Fenix's own capture.
//
// The contract the trust gate and the readouts build on: a lowercase letter before a run sets its color,
// underline, box, flashing or size and is never kept as text, and the colors stay on the runs as data,
// because the color is the meaning (a warning, a memo, an action) and is never stripped for speech. A letter
// the app does not know is listed on the line, once and in the order it came, so the gate can distrust the
// display that drew it. Display text is uppercase, so an uppercase letter is never a code. The letters p and d are
// known: neither is text, an unknown code or kept on a run, and neither is ever spoken. p changes nothing. d moves the
// drawing position right, so it adds a space to the run being built, which keeps the words either side of it apart in
// speech and splits no run.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelTextTests
{
    // U+2800, the blank braille cell the display uses as a spacer. Named because it cannot be seen in source.
    private const char Spacer = (char)0x2800;

    // ---- Colors, underline, box, flashing and size ----------------------------------------------

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
    public void B_and_f_toggle_a_box_and_flashing_like_underline_and_keep_the_color()
    {
        // Each letter flips its attribute, as u does, and closes the run before it; neither changes the color in force.
        FenixTextLine line = FenixChannelText.Parse("cbAfBbfC");

        Assert.Equal(new[]
        {
            Run("A", FenixTextColor.Cyan, boxed: true),
            Run("B", FenixTextColor.Cyan, boxed: true, flashing: true),
            Run("C", FenixTextColor.Cyan)
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
        // x comes first and again later, so the list is neither sorted nor repeating: x, k, q.
        FenixTextLine line = FenixChannelText.Parse("xAkBqCxD");

        Assert.Equal(new[] { 'x', 'k', 'q' }, line.UnknownCodes);
        Assert.Equal("ABCD", line.JoinedText);
    }

    // ---- x1: known with its digit ---------------------------------------------------------------

    [Fact]
    public void The_x1_code_is_known_and_draws_nothing()
    {
        // Measured: the PFD's amber ILS with the LS button off on an ILS approach, and the baro text through the transition
        // altitude. The digit is the code's: the flag is "ILS", never "1ILS".
        FenixTextLine flag = FenixChannelText.Parse("ax1ILS");
        FenixTextLine baro = FenixChannelText.Parse("x1QNH c1029");

        Assert.Equal(new[] { Run("ILS", FenixTextColor.Amber) }, flag.Runs);
        Assert.Empty(flag.UnknownCodes);
        Assert.Equal(new[] { Run("QNH ", null), Run("1029", FenixTextColor.Cyan) }, baro.Runs);
        Assert.Empty(baro.UnknownCodes);
    }

    [Theory]
    [InlineData("xILS", "ILS")]
    [InlineData("x2ILS", "2ILS")]
    [InlineData("x12ILS", "12ILS")]
    [InlineData("x", "")]
    public void An_x_without_exactly_the_digit_1_stays_unknown(string raw, string text)
    {
        FenixTextLine line = FenixChannelText.Parse(raw);

        Assert.Equal(new[] { 'x' }, line.UnknownCodes);
        Assert.Equal(text, line.JoinedText);
    }

    // ---- p and d: known, and never spoken -------------------------------------------------------

    [Fact]
    public void The_p_letter_is_known_and_draws_nothing()
    {
        // The FMA for the ten seconds of the flight director's flash, where the text stays drawn: p changes
        // nothing on the run (it is no toggle), so V/S keeps its green and box and +1800 its cyan and box. In an E/WD
        // memo it comes first on the line, ahead of the color.
        FenixTextLine flash = FenixChannelText.Parse("bgpV/Scp+1800");
        FenixTextLine memo = FenixChannelText.Parse("pgIR IN ALIGN 1 MN");

        Assert.Equal(new[]
        {
            Run("V/S", FenixTextColor.Green, boxed: true),
            Run("+1800", FenixTextColor.Cyan, boxed: true)
        }, flash.Runs);
        Assert.Equal("V/S+1800", flash.JoinedText);
        Assert.Empty(flash.UnknownCodes);

        Assert.Equal(new[] { Run("IR IN ALIGN 1 MN", FenixTextColor.Green) }, memo.Runs);
        Assert.Empty(memo.UnknownCodes);
    }

    [Fact]
    public void The_i_letter_is_known_and_draws_nothing()
    {
        // The DCDU, measured 2026-11-03: a pressed key's label for about half a second, and a chosen answer's status. i
        // changes nothing on the run and draws no character, so a line with it holds no unknown code on any display.
        FenixTextLine pressed = FenixChannelText.Parse("ciWILCO*");
        FenixTextLine status = FenixChannelText.Parse("ciWILCO");

        Assert.Equal(new[] { Run("WILCO*", FenixTextColor.Cyan) }, pressed.Runs);
        Assert.Empty(pressed.UnknownCodes);
        Assert.Equal(new[] { Run("WILCO", FenixTextColor.Cyan) }, status.Runs);
        Assert.Empty(status.UnknownCodes);
    }

    [Fact]
    public void The_d_letter_is_known_and_keeps_the_words_either_side_apart()
    {
        // The FMA's minimum (RADIO 200, two spacers after the c) and the ND's ground speed line: d moves the drawing
        // position right and draws nothing, so it is neither text nor an unknown code. The two sides are separate
        // words on the screen, but speech merges stretches by color alone, so d adds a space to the run being built
        // (and closes no run).
        FenixTextLine minimum = FenixChannelText.Parse("RADIOdc" + Spacer + Spacer + " 200");
        FenixTextLine groundSpeed = FenixChannelText.Parse("wsGSlgd456");

        Assert.Equal(new[]
        {
            Run("RADIO ", null),
            Run("   200", FenixTextColor.Cyan)
        }, minimum.Runs);
        Assert.Empty(minimum.UnknownCodes);

        Assert.Equal(new[]
        {
            Run("GS", FenixTextColor.White, size: FenixTextSize.Small),
            Run(" 456", FenixTextColor.Green, size: FenixTextSize.Large)
        }, groundSpeed.Runs);
        Assert.Empty(groundSpeed.UnknownCodes);

        // With the color restated before it, d splits no run of its own: only the size does.
        FenixTextLine sameColor = FenixChannelText.Parse("wsGSlwd456");

        Assert.Equal(new[]
        {
            Run("GS", FenixTextColor.White, size: FenixTextSize.Small),
            Run(" 456", FenixTextColor.White, size: FenixTextSize.Large)
        }, sameColor.Runs);
        Assert.Equal("GS 456", sameColor.JoinedText);
        Assert.Empty(sameColor.UnknownCodes);
    }

    [Fact]
    public void Speech_reads_the_lines_with_p_and_d()
    {
        // Neither letter is a color, so neither is ever named, and a line holding one is spoken, not held back as
        // untrusted.
        Assert.Equal("V/S green, +1800 cyan", FenixChannelSpeech.Line("bgpV/Scp+1800"));
        Assert.Equal("RADIO white, 200 cyan", FenixChannelSpeech.Line("RADIOdc" + Spacer + Spacer + " 200"));
        Assert.Equal("IR IN ALIGN 1 MN, green", FenixChannelSpeech.Line("pgIR IN ALIGN 1 MN"));

        // The words either side of a d are two words: stretches of one color merge, and they stay apart.
        Assert.Equal("GS 456, white", FenixChannelSpeech.Line("wsGSlwd456"));
        Assert.Equal("GS white, 456 green", FenixChannelSpeech.Line("wsGSlgd456"));
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
    public void The_box_and_flash_lines_measured_in_flight_parse()
    {
        // In flight on 3.0.0.7613 (an A321, 2026-11-02): the take-off FMA's MAN, which the real FMA shows boxed; LVR
        // MCT after an engine failure, which it flashes; and the upper ECAM after an engine shutdown, where f opens
        // and closes a stretch as u does. Each letter was unknown then, and untrusted its display while it showed.
        FenixTextLine man = FenixChannelText.Parse("wbMAN");
        FenixTextLine lever = FenixChannelText.Parse("wfLVR MCT");
        FenixTextLine shutDown = FenixChannelText.Parse("uaENG 1u afSHUT DOWNf");

        Assert.Equal(new[] { Run("MAN", FenixTextColor.White, boxed: true) }, man.Runs);
        Assert.Equal(new[] { Run("LVR MCT", FenixTextColor.White, flashing: true) }, lever.Runs);
        Assert.Equal(new[]
        {
            Run("ENG 1", FenixTextColor.Amber, underlined: true),
            Run(" ", FenixTextColor.Amber),
            Run("SHUT DOWN", FenixTextColor.Amber, flashing: true)
        }, shutDown.Runs);
        Assert.Equal("ENG 1 SHUT DOWN", shutDown.JoinedText);
        Assert.Empty(man.UnknownCodes);
        Assert.Empty(lever.UnknownCodes);
        Assert.Empty(shutDown.UnknownCodes);
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

    // ---- The shared text helpers ----------------------------------------------------------------

    [Theory]
    [InlineData("  GS   456 \t TAS  ", "GS 456 TAS")]
    [InlineData("A", "A")]
    [InlineData("   ", "")]
    [InlineData("", "")]
    [InlineData("A⠀B", "A⠀B")]   // the braille blank is no white space here: Parse makes it a space first
    public void Collapse_spaces_makes_each_run_one_space_and_trims_the_ends(string text, string expected)
    {
        Assert.Equal(expected, FenixChannelText.CollapseSpaces(text));
    }

    [Theory]
    [InlineData("  wVAKSU g318c  ", "VAKSU 318")]
    [InlineData(null, "")]
    [InlineData("gPPOS   ", "PPOS")]
    public void Trimmed_text_drops_the_codes_and_trims_the_ends_only(string? coded, string expected)
    {
        Assert.Equal(expected, FenixChannelText.TrimmedText(coded));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private static FenixTextRun Run(string text, FenixTextColor? color, bool underlined = false,
        FenixTextSize size = FenixTextSize.Normal, bool boxed = false, bool flashing = false) =>
        new(text, color, underlined, size, boxed, flashing);
}

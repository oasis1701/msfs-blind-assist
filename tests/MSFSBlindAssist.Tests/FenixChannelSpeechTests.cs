// Characterization tests for FenixChannelSpeech, the one function that speaks the Fenix display channel's coded text
// (an FMA slot, an upper ECAM line, the FOB text) with its colors. Pure logic: every line is written here, the
// measured ones as the display sent them, never read from Fenix's own capture.
//
// The contract: a line of one color ends with it ("ENG 1 FIRE, red"); a line of several names each stretch's color
// after its words, the stretches joined by ", " ("FOB: white, 4560 green, KG cyan"); adjacent stretches of one color
// merge, whatever else sets them apart (box, flash, size, underline), and a stretch of only spaces is never named but
// still separates its words; "!" is a space; sizes, underline, box and flash are never spoken; a line with a letter the
// app does not know is never spoken, because its display is untrusted. Text the display draws in pieces is spoken as
// one line from its parts, each parsed on its own so that no color carries from one part to the next. The Fenix draws
// the text before a part's first color letter in white, so that text is spoken as white when the part holds a color,
// and a part with no color letter at all stays colorless, so that no color is ever guessed. Yellow has no name until a
// capture confirms it: a yellow stretch is spoken without a color.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelSpeechTests
{
    // U+2800, the blank braille cell the display uses as a spacer. Named because it cannot be seen in source.
    private const char Spacer = (char)0x2800;

    // ---- A line of one color ----------------------------------------------------------------------

    [Theory]
    [InlineData("urENG 1 FIREu ", "ENG 1 FIRE, red")]                          // an engine fire; the title is underlined
    [InlineData("uaELECu aGEN 1 FAULT", "ELEC GEN 1 FAULT, amber")]             // the underline ends, the amber goes on
    [InlineData("c -THR LEVER 1.......IDLE", "-THR LEVER 1.......IDLE, cyan")]  // an action line; its leading space goes
    [InlineData("mT.O INHIBIT", "T.O INHIBIT, magenta")]
    public void A_one_color_line_ends_with_its_color(string coded, string expected)
    {
        Assert.Equal(expected, FenixChannelSpeech.Line(coded));
    }

    // ---- A line of several colors -----------------------------------------------------------------

    [Theory]
    [InlineData("wFOB:   g4560 cs KG", "FOB: white, 4560 green, KG cyan")]
    [InlineData("g    CABINc......CHECK", "CABIN green, ......CHECK cyan")]    // the findings memo line
    [InlineData("wAgBwC", "A white, B green, C white")]                         // the same color twice, not adjacent
    public void A_mixed_line_names_each_stretch(string coded, string expected)
    {
        Assert.Equal(expected, FenixChannelSpeech.Line(coded));
    }

    [Theory]
    [InlineData("wbMANb wFLX", "MAN FLX, white")]               // a box on the first
    [InlineData("wfLVRf wMCT", "LVR MCT, white")]               // flashing on the first
    [InlineData("gAs Bl C", "A B C, green")]                    // three sizes
    [InlineData("guT.Ou AUTO BRK", "T.O AUTO BRK, green")]      // an underline on the first
    public void Adjacent_stretches_of_one_color_merge(string coded, string expected)
    {
        Assert.Equal(expected, FenixChannelSpeech.Line(coded));
    }

    // ---- The exclamation mark, and what is never spoken -------------------------------------------

    [Theory]
    [InlineData("gALT!CRZ", "ALT CRZ, green")]
    [InlineData("g!!ALT!!!CRZ!", "ALT CRZ, green")]      // runs of spaces collapse to one and the ends are trimmed
    [InlineData("gALT!cG/S", "ALT green, G/S cyan")]     // the space ends the green stretch; it is not a stretch of its own
    public void The_exclamation_mark_is_a_space(string coded, string expected)
    {
        Assert.Equal(expected, FenixChannelSpeech.Line(coded));
    }

    [Theory]
    [InlineData("wbMAN", "MAN, white")]                                   // boxed, as the take-off FMA draws MAN
    [InlineData("wfLVR MCT", "LVR MCT, white")]                           // flashing, as LVR MCT after an engine failure
    [InlineData("cs KG", "KG, cyan")]                                     // small
    [InlineData("gl 12", "12, green")]                                    // large
    [InlineData("uaENG 1u afSHUT DOWNf", "ENG 1 SHUT DOWN, amber")]       // the underline is not spoken either
    public void Box_flash_and_size_are_never_spoken(string coded, string expected)
    {
        Assert.Equal(expected, FenixChannelSpeech.Line(coded));
    }

    [Theory]
    [InlineData("xENG 1 FIRE")]
    [InlineData("gALT kCRZ")]
    [InlineData("wFOB:   g4560 ck KG")]   // the letter is dropped and the number still reads: the speech itself must refuse
    [InlineData("k")]                      // nothing but the letter
    public void A_line_with_an_unknown_letter_is_never_spoken(string coded)
    {
        Assert.Null(FenixChannelSpeech.Line(coded));
        Assert.Null(FenixChannelSpeech.Line(FenixChannelText.Parse(coded)));
    }

    // ---- Culture ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Speech_is_the_same_in_every_culture(string culture)
    {
        // The text is carried as it came: a decimal point stays a point where the PC's culture writes a comma.
        InCulture(culture, () =>
        {
            Assert.Equal("FOB: white, 4560 green, KG cyan", FenixChannelSpeech.Line("wFOB:   g4560 cs KG"));
            Assert.Equal("QNH white, 1013.2 cyan", FenixChannelSpeech.Line("wQNH c1013.2"));
            Assert.Equal("CLB cyan, 1.618 green", FenixChannelSpeech.Line(new[] { "cCLB", "g1.618" }));
            Assert.Equal("CLB cyan, 1.618 green", FenixChannelSpeech.Line(FenixChannelText.Parse("cCLB g1.618")));
            Assert.Null(FenixChannelSpeech.Line("xFOB:   g4560"));

            Assert.Equal("amber", FenixChannelSpeech.ColorName(FenixTextColor.Amber));
            Assert.Equal("cyan", FenixChannelSpeech.ColorName(FenixTextColor.Cyan));
            Assert.Equal("green", FenixChannelSpeech.ColorName(FenixTextColor.Green));
            Assert.Equal("magenta", FenixChannelSpeech.ColorName(FenixTextColor.Magenta));
            Assert.Equal("white", FenixChannelSpeech.ColorName(FenixTextColor.White));
            Assert.Equal("red", FenixChannelSpeech.ColorName(FenixTextColor.Red));
        });
    }

    // ---- Text drawn in pieces ---------------------------------------------------------------------

    [Fact]
    public void Parts_are_parsed_alone_and_spoken_as_one_line()
    {
        // Two rows of an FMA column, each in a color of its own: one stretch for each, named after its words.
        Assert.Equal("ALT green, G/S cyan", FenixChannelSpeech.Line(new[] { "gALT", "cG/S" }));

        // One color across the parts is one stretch, and the parts are still two words.
        Assert.Equal("CAT 3 DUAL, white", FenixChannelSpeech.Line(new[] { "wCAT 3", "wDUAL" }));

        // The thrust limit's pieces: the second part holds a color, so its leading FLX is white, the same white as MAN.
        Assert.Equal("MAN FLX white, +56 cyan", FenixChannelSpeech.Line(new[] { "wbMAN", "FLX c+56" }));

        // A title the E/WD wraps onto two lines: the trailing space of the first and the join make one space.
        Assert.Equal("CONFIG FLAPS NOT IN T.O CONFIG, red",
            FenixChannelSpeech.Line(new[] { "urCONFIGu ", "rFLAPS NOT IN T.O CONFIG" }));

        // A blank part adds nothing.
        Assert.Equal("CLB cyan, 1.618 green", FenixChannelSpeech.Line(new[] { "cCLB", "g1.618", "" }));
        Assert.Equal("CLB cyan, 1.618 green", FenixChannelSpeech.Line(new string?[] { "cCLB", null, "  ", "g1.618" }));

        // A part with a letter the app does not know spoils the whole line, whichever part it is.
        Assert.Null(FenixChannelSpeech.Line(new[] { "gALT", "kG/S" }));
        Assert.Null(FenixChannelSpeech.Line(new[] { "kALT", "cG/S" }));

        // No color carries from one part to the next: CRZ has no color letter, so it is not green.
        Assert.Equal("ALT green, CRZ", FenixChannelSpeech.Line(new[] { "gALT", "CRZ" }));
    }

    [Fact]
    public void A_single_part_is_the_same_line_as_its_coded_text()
    {
        Assert.Equal(FenixChannelSpeech.Line("wFOB:   g4560 cs KG"), FenixChannelSpeech.Line(new[] { "wFOB:   g4560 cs KG" }));
        Assert.Equal(FenixChannelSpeech.Line("FLX c+56"), FenixChannelSpeech.Line(new[] { "FLX c+56" }));
        Assert.Equal(FenixChannelSpeech.Line("xFOB:"), FenixChannelSpeech.Line(new[] { "xFOB:" }));
    }

    // ---- Text before a color letter ---------------------------------------------------------------

    [Theory]
    [InlineData("FLX c+56", "FLX white, +56 cyan")]
    [InlineData("A cB gC", "A white, B cyan, C green")]
    [InlineData("FLX y+56", "FLX white, +56")]            // yellow counts as a color letter, and has no name yet
    [InlineData("X rLAND ASAP", "X white, LAND ASAP red")]
    [InlineData(" rLAND ASAP", "LAND ASAP, red")]         // only a space before it: nothing to speak as white
    public void A_part_with_a_color_letter_speaks_the_text_before_it_as_white(string coded, string expected)
    {
        Assert.Equal(expected, FenixChannelSpeech.Line(coded));
        Assert.Equal(expected, FenixChannelSpeech.Line(FenixChannelText.Parse(coded)));
        Assert.Equal(expected, FenixChannelSpeech.Line(new[] { coded }));
    }

    [Fact]
    public void Only_the_text_before_the_first_color_is_spoken_as_white()
    {
        // The parser never leaves a colorless run after a colored one, because a color once set stays set, but a line can
        // be built by hand. Nothing says what the display draws that run in, so it stays colorless: no color is guessed.
        var after = new FenixTextLine(new[] { Run("A", FenixTextColor.Green), Run(" B", null) }, Array.Empty<char>());
        var aroundIt = new FenixTextLine(
            new[] { Run("X ", null), Run("A", FenixTextColor.Green), Run(" B", null) }, Array.Empty<char>());

        Assert.Equal("A green, B", FenixChannelSpeech.Line(after));
        Assert.Equal("X white, A green, B", FenixChannelSpeech.Line(aroundIt));
    }

    [Fact]
    public void A_title_with_no_color_letter_stays_colorless()
    {
        // An ECAM entry's title has no color letter at all: the display's color for it is not in the text, so none is
        // spoken, and the part is colorless next to a colored one rather than white.
        Assert.Equal("ENG 1 FIRE", FenixChannelSpeech.Line("ENG 1 FIRE"));
        Assert.Equal("ENG 1 FIRE", FenixChannelSpeech.Line(FenixChannelText.Parse("ENG 1 FIRE")));
        Assert.Equal("ENG 1 FIRE", FenixChannelSpeech.Line(new[] { "ENG 1 FIRE" }));
        Assert.Equal("ENG 1 FIRE, NAV cyan", FenixChannelSpeech.Line(new[] { "ENG 1 FIRE", "cNAV" }));
    }

    [Fact]
    public void A_color_letter_with_no_text_after_it_colors_nothing()
    {
        // The c leaves no colored run, so the part holds no color: FLX stays colorless, the safe direction.
        Assert.Equal("FLX", FenixChannelSpeech.Line("FLXc"));
        Assert.Equal("FLX, OK green", FenixChannelSpeech.Line(new[] { "FLXc", "gOK" }));
    }

    // ---- Stretches of only spaces, and of nothing -------------------------------------------------

    [Theory]
    [InlineData("wAg wB", "A B, white")]        // the green space is not named, and it is not nothing: never "AB"
    [InlineData("wA g wB", "A B, white")]       // the spaces on either side of it collapse to one
    [InlineData("wAg cB", "A white, B cyan")]   // between two colors it is not named either
    [InlineData("g wA", "A, white")]            // leading
    [InlineData("wAg ", "A, white")]            // trailing
    public void A_stretch_of_only_spaces_is_not_named_but_still_separates_words(string coded, string expected)
    {
        Assert.Equal(expected, FenixChannelSpeech.Line(coded));
    }

    [Fact]
    public void An_empty_stretch_is_skipped_and_separates_nothing()
    {
        // The parser never makes an empty run, but a line can be built by hand.
        var leading = new FenixTextLine(new[] { Run("", FenixTextColor.Red), Run("OK", FenixTextColor.Green) },
            Array.Empty<char>());
        var between = new FenixTextLine(
            new[] { Run("A", FenixTextColor.Green), Run("", FenixTextColor.Red), Run("B", FenixTextColor.Green) },
            Array.Empty<char>());

        Assert.Equal("OK, green", FenixChannelSpeech.Line(leading));
        Assert.Equal("AB, green", FenixChannelSpeech.Line(between));   // nothing was drawn between them
    }

    // ---- Blank lines ------------------------------------------------------------------------------

    [Fact]
    public void A_blank_line_is_never_spoken()
    {
        string?[] blanks =
        {
            null, "", " ", "g", "gw", "g ", "g!!", Spacer.ToString(), "g" + Spacer + Spacer + "!", "cs"
        };

        foreach (string? coded in blanks)
        {
            Assert.Null(FenixChannelSpeech.Line(coded));
            Assert.Null(FenixChannelSpeech.Line(FenixChannelText.Parse(coded)));
            Assert.Null(FenixChannelSpeech.Line(new[] { coded }));
        }

        Assert.Null(FenixChannelSpeech.Line(new string?[] { null, "", "  ", "g" }));
        Assert.Null(FenixChannelSpeech.Line(Array.Empty<string?>()));
    }

    // ---- A parsed line ----------------------------------------------------------------------------

    [Theory]
    [InlineData("urENG 1 FIREu ")]
    [InlineData("uaELECu aGEN 1 FAULT")]
    [InlineData("wFOB:   g4560 cs KG")]
    [InlineData("gALT!CRZ")]
    [InlineData("wfLVR MCT")]
    [InlineData("FLX c+56")]
    [InlineData("ENG 1 FIRE")]
    [InlineData("wAg wB")]
    [InlineData("xENG 1 FIRE")]
    [InlineData("")]
    public void A_parsed_line_is_spoken_like_its_coded_text(string coded)
    {
        Assert.Equal(FenixChannelSpeech.Line(coded), FenixChannelSpeech.Line(FenixChannelText.Parse(coded)));
    }

    // ---- The color names --------------------------------------------------------------------------

    [Fact]
    public void Each_color_has_its_name_and_yellow_has_none()
    {
        Assert.Equal("amber", FenixChannelSpeech.ColorName(FenixTextColor.Amber));
        Assert.Equal("cyan", FenixChannelSpeech.ColorName(FenixTextColor.Cyan));
        Assert.Equal("green", FenixChannelSpeech.ColorName(FenixTextColor.Green));
        Assert.Equal("magenta", FenixChannelSpeech.ColorName(FenixTextColor.Magenta));
        Assert.Equal("white", FenixChannelSpeech.ColorName(FenixTextColor.White));
        Assert.Equal("red", FenixChannelSpeech.ColorName(FenixTextColor.Red));

        // Yellow is spoken only once a capture confirms what it draws, and a value the enum does not have is never
        // guessed at either.
        Assert.Equal("", FenixChannelSpeech.ColorName(FenixTextColor.Yellow));
        Assert.Equal("", FenixChannelSpeech.ColorName((FenixTextColor)99));
    }

    [Theory]
    [InlineData("yTEXT", "TEXT")]
    [InlineData("cA yB", "A cyan, B")]
    public void A_yellow_stretch_is_spoken_without_a_color(string coded, string expected)
    {
        Assert.Equal(expected, FenixChannelSpeech.Line(coded));
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static FenixTextRun Run(string text, FenixTextColor? color) =>
        new(text, color, false, FenixTextSize.Normal, false, false);

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

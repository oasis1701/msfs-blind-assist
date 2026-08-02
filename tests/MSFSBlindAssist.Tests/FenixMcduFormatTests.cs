// Characterization tests for MSFSBlindAssist.Services.FenixMcduFormat — decodes Fenix A320
// MCDU display markup (inline color codes a/c/g/m/w/y + size codes s/l) into accessible
// plain text.
//
// The CONFIG > FAILURES fixtures below are verbatim lines captured live from the Fenix
// GraphQL dataref `aircraft.mcdu1.display` (2026-08-02), before and after a single LSK1L
// press. That press moved BOTH the cyan color code and the large-font code from ALL onto
// NONE together, which is what establishes "cyan + large == the selected option".

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixMcduFormatTests
{
    // --- Basics -----------------------------------------------------------------------

    [Fact]
    public void StripFormatCodes_null_or_empty_returns_empty()
    {
        Assert.Equal("", FenixMcduFormat.StripFormatCodes(null!));
        Assert.Equal("", FenixMcduFormat.StripFormatCodes(""));
    }

    [Fact]
    public void StripFormatCodes_plain_text_passes_through()
        => Assert.Equal("RETURN", FenixMcduFormat.StripFormatCodes("RETURN"));

    [Fact]
    public void StripFormatCodes_codes_are_case_sensitive_so_uppercase_text_survives()
    {
        // 'a','c','g','m','w','y','s','l' are codes only in lowercase — uppercase display
        // text containing those letters must not be eaten.
        Assert.Equal("ALL", FenixMcduFormat.StripFormatCodes("ALL"));
        Assert.Equal("MINOR", FenixMcduFormat.StripFormatCodes("MINOR"));
        Assert.Equal("MACH", FenixMcduFormat.StripFormatCodes("MACH"));
    }

    [Theory]
    [InlineData("#", "-")]
    [InlineData("&", "Δ")]
    [InlineData("¤", "↑")]
    [InlineData("¥", "↓")]
    [InlineData("¢", "→")]
    [InlineData("£", "←")]
    public void StripFormatCodes_maps_special_glyphs(string raw, string expected)
        => Assert.Equal(expected, FenixMcduFormat.StripFormatCodes(raw));

    // --- Rule 1: the original green marker (must not regress) --------------------------

    [Fact]
    public void StripFormatCodes_single_color_line_gets_no_marker()
    {
        // Captured: the FAILURE TYPE / TRIGGER label row. Green labels + whitespace-only
        // white runs = effectively one color, so nothing is marked.
        Assert.Equal(
            "FAILURE TYPE     TRIGGER",
            FenixMcduFormat.StripFormatCodes("sgFAILURE TYPEw     gTRIGGERw"));
    }

    [Fact]
    public void StripFormatCodes_mixed_colors_with_green_marks_the_green_segment()
        => Assert.Equal("ALT *FL370", FenixMcduFormat.StripFormatCodes("wALT gFL370"));

    [Fact]
    public void StripFormatCodes_mixed_colors_without_green_does_not_mark_by_color()
    {
        // Cyan is used all over the MCDU (entry fields, brackets, the cycle arrow), so it
        // must never trigger the color rule on its own.
        Assert.Equal("ALT FL370", FenixMcduFormat.StripFormatCodes("wALT cFL370"));
    }

    // --- Rule 2: the large-among-small selected option ---------------------------------

    [Fact]
    public void StripFormatCodes_marks_the_selected_option_when_ALL_is_active()
    {
        // Live capture, failure type = ALL: NONE and MINOR are s(mall), ALL is left large
        // and cyan. Before this rule the line decoded with no selection info whatsoever.
        Assert.Equal(
            "←NONE/MINOR/*ALL  RANDOM*",
            FenixMcduFormat.StripFormatCodes("c£wsNONEl/sMINORl/cALLw  cRANDOM*w"));
    }

    [Fact]
    public void StripFormatCodes_marks_the_selected_option_when_NONE_is_active()
    {
        // Same line after one LSK1L press — the marker follows the selection.
        Assert.Equal(
            "←*NONE/MINOR/ALL  RANDOM*",
            FenixMcduFormat.StripFormatCodes("c£wcNONEw/sMINORl/sALLl  cRANDOM*w"));
    }

    [Fact]
    public void StripFormatCodes_marks_the_selected_option_in_a_two_option_group()
    {
        // Live capture, failure rate = REALISTIC (large) vs HIGH (small).
        Assert.Equal(
            "←*REALISTIC/HIGH",
            FenixMcduFormat.StripFormatCodes("c£wcREALISTICw/sHIGHl"));
    }

    [Fact]
    public void StripFormatCodes_leading_cycle_arrow_does_not_absorb_the_marker()
    {
        // The '<-' arrow stays large even when the option next to it is small. Reading the
        // token's size from its letters/digits only is what keeps that from misreporting
        // the first option as selected.
        string result = FenixMcduFormat.StripFormatCodes("c£wsNONEl/sMINORl/cALLw");
        Assert.StartsWith("←NONE", result);
        Assert.Equal("←NONE/MINOR/*ALL", result);
    }

    // --- Rule 2 must stay conservative -------------------------------------------------

    [Fact]
    public void StripFormatCodes_uniform_size_option_group_is_not_marked()
    {
        // Page counters and same-size data must be left alone.
        Assert.Equal("1/1", FenixMcduFormat.StripFormatCodes("1/1"));
        Assert.Equal("FROM/TO", FenixMcduFormat.StripFormatCodes("sFROM/TOl"));
        Assert.Equal("250/.78", FenixMcduFormat.StripFormatCodes("250/.78"));
    }

    [Fact]
    public void StripFormatCodes_field_without_a_slash_is_not_marked()
        => Assert.Equal("REALISTIC", FenixMcduFormat.StripFormatCodes("cREALISTICw"));

    [Fact]
    public void StripFormatCodes_group_with_two_large_options_is_ambiguous_and_unmarked()
        => Assert.Equal("A/B/C", FenixMcduFormat.StripFormatCodes("A/B/sCl"));

    [Fact]
    public void StripFormatCodes_option_with_no_letters_or_digits_disqualifies_the_group()
        => Assert.Equal("----/----", FenixMcduFormat.StripFormatCodes("s----l/----"));

    [Fact]
    public void StripFormatCodes_option_mixing_sizes_internally_disqualifies_the_group()
        => Assert.Equal("ABC/DEF", FenixMcduFormat.StripFormatCodes("sABlC/sDEFl"));

    [Fact]
    public void StripFormatCodes_option_group_is_bounded_by_whitespace()
    {
        // The trailing RANDOM* sits in a separate column and must not be pulled into the
        // NONE/MINOR/ALL group as a fourth option.
        Assert.Equal(
            "←NONE/MINOR/*ALL  RANDOM*",
            FenixMcduFormat.StripFormatCodes("c£wsNONEl/sMINORl/cALLw  cRANDOM*w"));
    }

    // --- Both rules on one line --------------------------------------------------------

    [Fact]
    public void StripFormatCodes_green_and_size_rules_do_not_double_mark()
    {
        // A green large option among small siblings gets exactly one marker.
        string result = FenixMcduFormat.StripFormatCodes("wsAl/gB");
        Assert.Equal("A/*B", result);
        Assert.Equal(1, result.Count(c => c == '*'));
    }
}

// Characterization tests for MSFSBlindAssist.Forms.HotkeyListForm's filtering logic
// (Forms/HotkeyListForm.cs): the "All Categories" 3-char-prefix sentinel and the
// mode/category section matching used by ApplyFilters.
//
// Production seam (minimal, behavior-neutral, documented at the call site too):
//   - `HotkeyMode` (nested enum) and `CategorySection` (nested class) promoted
//     private -> internal so a test can construct/inspect them directly. Their members
//     were already `public` within the nested type.
//   - `AllCategoriesLabel` const promoted private -> internal.
//   - The inline 3-line "isAllCategoriesSentinel" boolean expression inside ApplyFilters
//     was extracted VERBATIM (zero logic change) into a new `internal static
//     IsAllCategoriesSentinel(string)` method, because it was not already a separable
//     unit and the whole form is otherwise heavily entangled (file-reading constructor,
//     full WinForms control tree). See CLAUDE.md's Forms-cluster test-wave notes for why
//     this item, unlike most others in the wave, needed an extraction rather than a bare
//     access-modifier promotion.
//
// The whole HotkeyListForm was NOT constructed for these tests (its constructor reads
// HotkeyGuides\*.txt relative to AppDomain.CurrentDomain.BaseDirectory and builds a full
// WinForms control tree) — that's out of scope for pure filtering-logic characterization.

using MSFSBlindAssist.Forms;

namespace MSFSBlindAssist.Tests;

public class HotkeyListFormFilterTests
{
    // --- IsAllCategoriesSentinel: the "All Categories" 3-char-prefix sentinel ----------

    [Theory]
    [InlineData("")]
    [InlineData("All Categories")]
    [InlineData("all categories")]
    [InlineData("ALL CATEGORIES")]
    [InlineData("All Categ")]
    [InlineData("all c")]
    [InlineData("all")] // exactly 3 chars, the floor
    public void IsAllCategoriesSentinel_matches_empty_or_a_3plus_char_prefix(string search)
        => Assert.True(HotkeyListForm.IsAllCategoriesSentinel(search));

    [Theory]
    [InlineData("a")]  // 1 char - below the floor
    [InlineData("al")] // 2 chars - below the floor
    public void IsAllCategoriesSentinel_rejects_short_prefixes_below_the_3char_floor(string search)
    {
        // This is the exact regression this sentinel guards against: without the 3-char
        // floor, "a"/"al" would short-circuit filtering for real categories that start
        // with those letters (Altitude, Airspeed) by being misread as "show everything".
        Assert.False(HotkeyListForm.IsAllCategoriesSentinel(search));
    }

    [Theory]
    [InlineData("Altitude")]  // a real category name, not a prefix of "All Categories"
    [InlineData("Airspeed")]
    [InlineData("zzz")]       // 3+ chars but not a prefix at all
    [InlineData("Categories")] // suffix, not prefix, of the sentinel label
    public void IsAllCategoriesSentinel_rejects_non_prefix_search_terms(string search)
        => Assert.False(HotkeyListForm.IsAllCategoriesSentinel(search));

    // --- CategorySection: mode/category section matching ------------------------------

    private static HotkeyListForm.CategorySection MakeAltitudeSection(HotkeyListForm.HotkeyMode mode = HotkeyListForm.HotkeyMode.Output) =>
        new(mode, "Altitude",
            "Altitude:\n" +
            "  A  Announce altitude.\n" +
            "\n" +
            "  Shift+A  Announce altitude in feet and meters.\n" +
            "\n" +
            "  Ctrl+A  Set target altitude on the MCP.\n");

    // InlineData can't carry the internal HotkeyMode enum directly across a public xUnit
    // Theory signature (CS0051: a public member can't expose a less-accessible parameter
    // type) — pass the underlying int and cast inside the method body instead.
    [Theory]
    [InlineData((int)HotkeyListForm.HotkeyMode.Output, "Output ] - Altitude")]
    [InlineData((int)HotkeyListForm.HotkeyMode.Input, "Input [ - Altitude")]
    [InlineData((int)HotkeyListForm.HotkeyMode.General, "General - Altitude")]
    public void DisplayName_is_formatted_per_mode(int modeValue, string expected)
    {
        var section = MakeAltitudeSection((HotkeyListForm.HotkeyMode)modeValue);
        Assert.Equal(expected, section.DisplayName);
    }

    [Theory]
    [InlineData((int)HotkeyListForm.HotkeyMode.Output, 0)]
    [InlineData((int)HotkeyListForm.HotkeyMode.Input, 1)]
    [InlineData((int)HotkeyListForm.HotkeyMode.General, 2)]
    public void ModeOrder_ranks_Output_before_Input_before_General(int modeValue, int expectedOrder)
        => Assert.Equal(expectedOrder, MakeAltitudeSection((HotkeyListForm.HotkeyMode)modeValue).ModeOrder);

    [Fact]
    public void Matches_finds_a_keyword_anywhere_in_the_section_text()
    {
        var section = MakeAltitudeSection();
        Assert.True(section.Matches("MCP"));
        Assert.True(section.Matches("mcp")); // case-insensitive
        Assert.False(section.Matches("Autobrake"));
    }

    [Fact]
    public void Matches_finds_the_category_name_itself()
    {
        var section = MakeAltitudeSection();
        Assert.True(section.Matches("Altitude"));
    }

    [Fact]
    public void GetFilteredText_returns_the_full_section_when_the_search_matches_the_category_name()
    {
        var section = MakeAltitudeSection();
        string result = section.GetFilteredText("Altitude");
        Assert.Equal(section.SectionText, result);
    }

    [Fact]
    public void GetFilteredText_returns_the_full_section_when_the_search_matches_the_display_name()
    {
        var section = MakeAltitudeSection();
        string result = section.GetFilteredText("Output ]");
        Assert.Equal(section.SectionText, result);
    }

    [Fact]
    public void GetFilteredText_keeps_the_header_line_plus_only_matching_command_blocks()
    {
        var section = MakeAltitudeSection();
        string result = section.GetFilteredText("Shift+A");

        Assert.Contains("Altitude:", result);
        Assert.Contains("Shift+A  Announce altitude in feet and meters.", result);
        Assert.DoesNotContain("Ctrl+A", result);
        Assert.DoesNotContain("  A  Announce altitude.", result); // the plain "A" block, not the Shift+A one
    }

    [Fact]
    public void GetFilteredText_can_match_inside_a_wrapped_description_line()
    {
        // A continuation/description line belongs to the block whose hotkey line precedes
        // it; searching a word that only appears in that wrapped line must still pull in
        // the whole block (hotkey line + its description).
        var section = new HotkeyListForm.CategorySection(
            HotkeyListForm.HotkeyMode.Output, "Approach",
            "Approach:\n" +
            "  N  Announce next waypoint.\n" +
            "             Description continues describing waypoint sequencing here.\n");

        string result = section.GetFilteredText("sequencing");

        Assert.Contains("Approach:", result);
        Assert.Contains("Announce next waypoint.", result);
        Assert.Contains("sequencing", result);
    }
}

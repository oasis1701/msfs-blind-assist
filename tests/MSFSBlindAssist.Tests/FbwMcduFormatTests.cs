// Characterization tests for MSFSBlindAssist.Services.FbwMcduFormat — decodes FlyByWire
// MCDU cell markup ({green}/{small}/{sp}/{end}/...) into accessible plain text and builds
// an MCDUDisplayData from a canned SimBridge "side" JSON payload. Every target method here
// was already `public static` — no production access-modifier changes or seams were needed
// for this item.

using Newtonsoft.Json.Linq;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FbwMcduFormatTests
{
    // --- DecodeCell: color tags, {sp}, {end}, drop tags, LSK glyph braces --------------

    [Fact]
    public void DecodeCell_null_or_empty_returns_empty()
    {
        Assert.Equal("", FbwMcduFormat.DecodeCell(null));
        Assert.Equal("", FbwMcduFormat.DecodeCell(""));
    }

    [Fact]
    public void DecodeCell_plain_text_with_no_tags_passes_through()
        => Assert.Equal("1/1", FbwMcduFormat.DecodeCell("1/1"));

    [Fact]
    public void DecodeCell_single_color_segment_has_no_asterisk_marker()
        => Assert.Equal("FL370", FbwMcduFormat.DecodeCell("{green}FL370{end}"));

    [Fact]
    public void DecodeCell_mixed_colors_prefixes_the_green_segment_with_an_asterisk()
    {
        // Plain text can't convey color, so a cell that mixes white + green text marks the
        // green (highlighted/entry) portion with a leading '*' instead of dropping the
        // distinction entirely.
        string result = FbwMcduFormat.DecodeCell("ALT {green}FL370{end}");
        Assert.Equal("ALT *FL370", result);
    }

    [Fact]
    public void DecodeCell_sp_tag_inserts_a_literal_space()
        => Assert.Equal("A  B", FbwMcduFormat.DecodeCell("A{sp}{sp}B"));

    [Theory]
    [InlineData("{small}HELLO{end}")]
    [InlineData("{big}HELLO{end}")]
    [InlineData("{left}HELLO{end}")]
    [InlineData("{right}HELLO{end}")]
    public void DecodeCell_style_only_tags_are_silently_dropped(string cell)
        => Assert.Equal("HELLO", FbwMcduFormat.DecodeCell(cell));

    [Fact]
    public void DecodeCell_lone_open_brace_glyph_is_dropped_but_its_content_is_kept()
    {
        // The FBW MCDU's LSK arrow/bracket glyph is an unmatched '{' (no known {tag} closes
        // it) preceding a real value like a runway designator — the glyph must be dropped
        // WITHOUT eating the value that follows it.
        Assert.Equal("08L", FbwMcduFormat.DecodeCell("{08L"));
    }

    [Fact]
    public void DecodeCell_stray_closing_brace_glyph_is_dropped()
        => Assert.Equal("08L", FbwMcduFormat.DecodeCell("08L}"));

    // --- PositionLine: 24-col left/center/right layout ----------------------------------

    [Fact]
    public void PositionLine_left_only_is_left_aligned_and_trailing_space_trimmed()
        => Assert.Equal("CO RTE", FbwMcduFormat.PositionLine("CO RTE", "", "", 24));

    [Fact]
    public void PositionLine_right_only_is_right_aligned()
    {
        string result = FbwMcduFormat.PositionLine("", "", "DEF", 10);
        Assert.Equal(10, result.Length);
        Assert.EndsWith("DEF", result);
        Assert.Equal("       DEF", result);
    }

    [Fact]
    public void PositionLine_center_only_is_centered_then_trailing_space_trimmed()
        => Assert.Equal("    MID", FbwMcduFormat.PositionLine("", "MID", "", 11));

    [Fact]
    public void PositionLine_left_and_right_do_not_collide_when_there_is_room()
    {
        string result = FbwMcduFormat.PositionLine("ABC", "", "DEF", 10);
        Assert.Equal("ABC    DEF", result);
    }

    [Fact]
    public void PositionLine_blank_everything_returns_empty_string()
        => Assert.Equal("", FbwMcduFormat.PositionLine("", "", "", 24));

    // --- LitAnnunciators -----------------------------------------------------------------

    [Fact]
    public void LitAnnunciators_null_token_returns_an_empty_list()
        => Assert.Empty(FbwMcduFormat.LitAnnunciators(null));

    [Fact]
    public void LitAnnunciators_returns_only_true_flags_in_the_documented_order()
    {
        var ann = new JObject
        {
            ["rdy"] = true,
            ["fail"] = true,
            ["fmgc"] = false,
            ["fm1"] = false,
        };

        var result = FbwMcduFormat.LitAnnunciators(ann);

        Assert.Equal(new[] { "FAIL", "RDY" }, result); // fail (order idx 0) before rdy (order idx 6), regardless of JSON key order
    }

    [Fact]
    public void LitAnnunciators_ignores_a_non_boolean_value_even_if_truthy_looking()
    {
        // ann["fail"]?.Type == JTokenType.Boolean is a strict type check — a string "true"
        // must NOT be treated as lit.
        var ann = new JObject { ["fail"] = "true" };
        Assert.Empty(FbwMcduFormat.LitAnnunciators(ann));
    }

    // --- JoinColumns -----------------------------------------------------------------------

    [Fact]
    public void JoinColumns_joins_non_blank_parts_with_three_spaces()
        => Assert.Equal("A   C", FbwMcduFormat.JoinColumns("A", " ", "C"));

    [Fact]
    public void JoinColumns_all_blank_returns_empty()
        => Assert.Equal("", FbwMcduFormat.JoinColumns("", "  ", null!));

    // --- BuildDisplayData: full canned SimBridge "side" payload -------------------------

    [Fact]
    public void BuildDisplayData_assembles_title_page_scratchpad_annunciators_arrows_and_lines()
    {
        var side = new JObject
        {
            ["title"] = "{green}INIT{end}",
            ["page"] = "1/1",
            ["scratchpad"] = "DELETE",
            ["annunciators"] = new JObject { ["fail"] = true },
            ["arrows"] = new JArray { true, false, true, false },
            ["lines"] = new JArray
            {
                new JArray { "CO RTE", null!, null! },     // label row (k=0)
                new JArray { "LFPG/EGLL", null!, null! },  // value row (k=0)
                new JArray { "ALTN", null!, null! },       // label row (k=1)
                new JArray { "", null!, null! },           // value row (k=1)
            },
        };

        var data = FbwMcduFormat.BuildDisplayData(side);

        Assert.Equal("INIT", data.Title);
        Assert.Equal("1/1", data.Page);
        Assert.Equal("DELETE", data.Scratchpad);
        Assert.Equal(new[] { "FAIL" }, data.Annunciators);
        Assert.Equal(new[] { true, false, true, false }, data.Arrows);

        Assert.Equal("CO RTE", data.Lines[0].LeftLabel);
        Assert.Equal("LFPG/EGLL", data.Lines[0].LeftValue);
        Assert.Equal("ALTN", data.Lines[1].LeftLabel);
        Assert.Equal("", data.Lines[1].LeftValue);

        Assert.Equal("INIT", data.RawLines[0]);
        Assert.Equal("CO RTE", data.RawLines[1]);
        Assert.Equal("LFPG/EGLL", data.RawLines[2]);
        Assert.Equal("ALTN", data.RawLines[3]);
        Assert.Equal("", data.RawLines[4]);
        Assert.Equal("DELETE", data.RawLines[13]);

        // Rows beyond the supplied `lines` array (k=2..5) must resolve to blank, not throw.
        Assert.Equal("", data.RawLines[5]);
        Assert.Equal("", data.RawLines[12]);
    }

    [Fact]
    public void BuildDisplayData_missing_optional_fields_produce_blank_defaults_not_exceptions()
    {
        var side = new JObject(); // completely empty payload
        var data = FbwMcduFormat.BuildDisplayData(side);

        Assert.Equal("", data.Title);
        Assert.Equal("", data.Page);
        Assert.Equal("", data.Scratchpad);
        Assert.Empty(data.Annunciators);
        Assert.Equal(new[] { false, false, false, false }, data.Arrows);
    }
}

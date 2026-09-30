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

    // --- Green markers must not cost a column --------------------------------------------

    private static string Sp(int n) => string.Concat(Enumerable.Repeat("{sp}", n));

    private static MCDUDisplayData SingleValueRow(string valueCell, string right = "", string center = "")
        => FbwMcduFormat.BuildDisplayData(new JObject
        {
            ["lines"] = new JArray
            {
                new JArray { "", "", "" },
                new JArray { valueCell, right, center },
            },
        });

    [Fact]
    public void BuildDisplayData_keeps_the_right_hand_value_when_green_markers_precede_it()
    {
        // Live INIT FUEL PRED line 2 (2026-09-26): FBW sends the whole row, BLOCK included,
        // in cell 0, padded to exactly 24 columns. The three '*' markers once counted as
        // columns and pushed "5.1" past column 24, so BLOCK fuel was clipped away. The three
        // touching green pieces are one value with one '*', which takes the blank padding
        // column in front of "3.1".
        string cell = "{white}{sp}{sp}{small}{green}3.1{end}{end}{small}{green}/{end}{end}"
                    + "{small}{green}0137{end}{end}" + Sp(11)
                    + "{cyan}5.1{end}{small}{end}{big}{end}{end}";

        var data = SingleValueRow(cell);

        Assert.Equal(" *3.1/0137" + new string(' ', 11) + "5.1", data.RawLines[2]);
        Assert.Equal("  *3.1/0137" + new string(' ', 11) + "5.1", data.Lines[0].LeftValue);
    }

    [Fact]
    public void BuildDisplayData_keeps_a_marked_right_cell_on_the_right_edge()
    {
        // The '*' takes the blank column in front, so the value still ends at column 24 like
        // every unmarked right value, and the line stays 24 wide.
        var data = SingleValueRow("A", right: "{green}ON{end}/OFF");

        Assert.Equal("A" + new string(' ', 16) + "*ON/OFF", data.RawLines[2]);
        Assert.Equal("*ON/OFF", data.Lines[0].RightValue);
    }

    [Fact]
    public void BuildDisplayData_centres_a_marked_centre_cell_on_its_text()
        => Assert.Equal(new string(' ', 8) + "*ON/OFF", SingleValueRow("", center: "{green}ON{end}/OFF").RawLines[2]);

    [Fact]
    public void BuildDisplayData_keeps_the_space_between_text_and_a_marker()
        // The blank column right after "ALT" is a word gap, not free padding: insert the '*'.
        => Assert.Equal("ALT *FL370", SingleValueRow("ALT {green}FL370{end}").RawLines[2]);

    [Fact]
    public void BuildDisplayData_does_not_truncate_a_placeholder_after_green_markers()
    {
        // Same page, line 4: LW "---.-" at the right came out as "--".
        string cell = "{small}{green}0.8{end}{small}{green}/{end}{small}{green}0022{end}"
                    + Sp(11) + "{cyan}---.-{end}"; // 8 + 11 + 5 = FBW's 24 columns

        var data = SingleValueRow(cell);

        // No blank column in front of "0.8", so the '*' is inserted and the gap after the value
        // gives the column back: "---.-" still ends at column 24.
        Assert.Equal("*0.8/0022" + new string(' ', 10) + "---.-", data.RawLines[2]);
    }

    // --- Live pages, 2026-09-30 (FBW A320 at KIAH, cells as captured) ------------------------

    [Fact]
    public void DecodeCell_gives_touching_green_pieces_one_star()
        // FBW sends a green value as separate pieces ("0.8" cyan, "/" green, "0011" green); a
        // pilot sees one green value, so it is marked once, not "0.8*/*0011".
        => Assert.Equal("0.8*/0011", FbwMcduFormat.DecodeCell("{cyan}0.8{end}{green}/{end}{green}0011{end}"));

    [Fact]
    public void DecodeCell_keeps_a_star_for_each_green_value_a_gap_separates()
        => Assert.Equal("*A *BC", FbwMcduFormat.DecodeCell("{green}A{end} {green}B{end}{white}C{end}"));

    [Fact]
    public void PositionLine_non_breaking_padding_never_overwrites_text()
        // FBW pads its cells with U+00A0, not ' '. Padding is blank whichever space it is.
        => Assert.Equal("ABCDX", FbwMcduFormat.PositionLine("ABCD", "", "\u00A0\u00A0X", 5));

    [Fact]
    public void BuildDisplayData_lets_a_star_take_non_breaking_padding()
    {
        // PERF APPR line 5 (VAPP/VLS): the gap before VLS is four U+00A0.
        var data = SingleValueRow("{white}{cyan}{small}136{end}{end}\u00A0\u00A0\u00A0\u00A0{green}131{end}{end}",
                                  right: "{white}{cyan}FULL/{end}{small}CONF3{end}*{end}");

        Assert.Equal("136   *131   FULL/CONF3*", data.RawLines[2]);
    }

    [Fact]
    public void BuildDisplayData_keeps_init_fuel_pred_block_fuel_on_the_right_edge()
    {
        // INIT FUEL PRED line 2 (TRIP/TIME ... BLOCK), whole row in cell 0.
        var data = SingleValueRow("{white}{sp}{small}{green}10.2{end}{end}{small}{green}/{end}{end}"
                                + "{small}{green}0213{end}{end}" + Sp(10) + "{cyan}14.0{end}{small}{end}{big}{end}{end}");

        Assert.Equal("*10.2/0213" + new string(' ', 10) + "14.0", data.RawLines[2]);
    }

    [Fact]
    public void BuildDisplayData_repays_an_inserted_star_from_the_next_gap()
    {
        // INIT FUEL PRED line 4 (ALTN/TIME, TOW/LW): the '*' in front of "/0011" has text right
        // before it, so it is inserted; the gap after it gives that column back and still leaves
        // room for the '*' in front of TOW, so LW ends at column 24.
        var data = SingleValueRow("{white}{sp}{sp}{small}{cyan}0.8{end}{end}{small}{green}/{end}{end}"
                                + "{small}{green}0011{end}{end}{sp}{sp}{sp}{small}{green}155.3{end}{end}"
                                + "{small}{green}/{end}{end}{small}{green}145.1{end}{end}{small}{end}{big}{end}{end}");

        Assert.Equal("  0.8*/0011 *155.3/145.1", data.RawLines[2]);
    }

    [Fact]
    public void BuildDisplayData_keeps_extra_time_on_the_right_edge()
    {
        // INIT FUEL PRED line 6 (MIN DEST FOB, EXTRA/TIME).
        var data = SingleValueRow("{white}{sp}{sp}{small}{cyan}2.9{end}{end}" + Sp(11)
                                + "{small}{green}0.0{end}{end}{small}{green}/{end}{end}{small}{green}0000{end}{end}"
                                + "{small}{end}{big}{end}{end}");

        Assert.Equal("  2.9" + new string(' ', 10) + "*0.0/0000", data.RawLines[2]);
    }

    [Fact]
    public void BuildDisplayData_keeps_the_right_cell_in_place_after_an_inserted_star()
    {
        // PERF TAKE OFF line 2 (VR, SLT RETR, TO SHIFT): "S=" leaves no free column for the
        // '*' in front of 201, and the gap before TO SHIFT gives one back.
        var data = SingleValueRow("{white}{cyan}149{end}{small}\u00A0\u00A0\u00A0{end}\u00A0S={green}201{end}{end}",
                                  right: "{white}{inop}{small}[M]{end}[\u00A0\u00A0]*{end}{end}");

        Assert.Equal("149    S=*201   [M][  ]*", data.RawLines[2]);
    }

    [Fact]
    public void BuildDisplayData_never_pays_a_star_back_from_inside_an_entry_box()
    {
        // FBW draws an entry box from the same U+00A0 it pads with ("[\xa0\xa0\xa0]", PERF TAKE
        // OFF FLAPS/THS "/[\xa0\xa0\xa0]", the ATC pages' "[\xa0\xa0]"). Its blanks are the box's
        // width, not a gap: the '*' inserted after "S=" is paid back by the padding after the box,
        // and the box keeps all three columns.
        var data = SingleValueRow("{white}S={end}{green}201{end}{white}/[\u00A0\u00A0\u00A0]{end}"
                                + Sp(10) + "{cyan}ABC{end}");

        Assert.Equal("S=*201/[   ]" + new string(' ', 9) + "ABC", data.RawLines[2]);
    }

    [Fact]
    public void DecodeCell_turns_a_non_breaking_space_into_a_plain_space()
        // The scratchpad, title and page go through DecodeCell alone, never PositionLine.
        => Assert.Equal("NOT ALLOWED", FbwMcduFormat.DecodeCell("{white}NOT\u00A0ALLOWED{end}"));

    [Fact]
    public void BuildDisplayData_cell_values_use_plain_spaces()
    {
        var data = SingleValueRow("{cyan}[\u00A0\u00A0]{end}", right: "{cyan}[\u00A0]{end}");

        Assert.Equal("[  ]", data.Lines[0].LeftValue);
        Assert.Equal("[ ]", data.Lines[0].RightValue);
    }

    [Fact]
    public void PositionLine_leaves_a_literal_asterisk_as_an_ordinary_column()
        // Callers outside BuildDisplayData (the DCDU) pass text in which '*' is real content.
        => Assert.Equal("INSERT*   X", FbwMcduFormat.PositionLine("INSERT*", "", "X", 11));

    // --- Source hygiene ------------------------------------------------------------------

    [Theory]
    [InlineData("MSFSBlindAssist/Services/FbwMcduFormat.cs")]
    [InlineData("tests/MSFSBlindAssist.Tests/FbwMcduFormatTests.cs")]
    [InlineData("tools/fbw-mcdu-probe/mcdu-format.js")]
    [InlineData("tools/fbw-mcdu-probe/mcdu-format.test.js")]
    public void Source_spells_the_no_break_space_as_an_escape(string relativePath)
    {
        // A literal U+00A0 looks exactly like a space: "ch == ' ' || ch == ' '" reads as a
        // duplicate compare that a tidy-up deletes, and an editor that normalises it turns the
        // tests that use it into tests of an ordinary space. Both happen with no compile error.
        const char noBreakSpace = (char)0xA0;
        string path = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        string[] lines = File.ReadAllLines(path);
        var offending = new List<int>();
        for (int n = 0; n < lines.Length; n++)
        {
            if (lines[n].IndexOf(noBreakSpace) >= 0) { offending.Add(n + 1); }
        }

        Assert.True(offending.Count == 0,
            $"{relativePath} has a literal U+00A0 on line(s) {string.Join(", ", offending)}; write it as an escape.");
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}

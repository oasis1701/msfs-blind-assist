using MSFSBlindAssist.Tests.Walkthroughs;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the parser behind <see cref="WalkthroughTemplateTests"/>: how a compiled template's doc-regions render
/// and which code blocks a walkthrough's markdown holds. Design: the 2026-10-08 "walkthroughs compile" spec
/// (PR 4 of the docs-restructure fix sequence).
/// </summary>
public class WalkthroughParsingTests
{
    private const string Nested = "    // doc-region: a\n    int x;\n    // doc-region: b\n    int y;\n    // doc-region-end: b\n    // doc-region-end: a\n";

    [Fact]
    public void A_region_renders_its_lines_dedented()
    {
        RegionMap map = DocRegions.Parse("class C\n{\n    // doc-region: a\n    int x;\n        int y;\n    // doc-region-end: a\n}\n");
        Assert.Empty(map.Problems);
        Assert.Equal("int x;\n    int y;", map.Render("a"));
    }

    [Fact]
    public void A_nested_region_shows_in_full_without_its_markers()
        => Assert.Equal("int x;\nint y;", DocRegions.Parse(Nested).Render("a"));

    [Fact]
    public void A_collapsed_region_is_one_ellipsis_line_in_its_outer_region()
        => Assert.Equal("int x;\n// ...",
            DocRegions.Parse(Nested.Replace("// doc-region: b", "// doc-region: b (collapsed)")).Render("a"));

    [Fact]
    public void A_collapsed_region_renders_in_full_on_its_own()
        => Assert.Equal("int y;",
            DocRegions.Parse(Nested.Replace("// doc-region: b", "// doc-region: b (collapsed)")).Render("b"));

    [Fact]
    public void A_collapsed_region_inside_a_shown_region_still_collapses()
    {
        RegionMap map = DocRegions.Parse(
            "// doc-region: a\n"
            + "int x;\n"
            + "// doc-region: b\n"
            + "if (x)\n"
            + "{\n"
            + "    // doc-region: c (collapsed)\n"
            + "    int hidden;\n"
            + "    // doc-region-end: c\n"
            + "}\n"
            + "// doc-region-end: b\n"
            + "// doc-region-end: a\n");
        Assert.Empty(map.Problems);
        string rendered = map.Render("a");
        Assert.Equal("int x;\nif (x)\n{\n    // ...\n}", rendered);
        Assert.DoesNotContain("hidden", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Leading_and_trailing_blank_lines_are_ignored()
    {
        Assert.Equal("int x;", DocRegions.Parse("// doc-region: a\n\nint x;\n   \n// doc-region-end: a\n").Render("a"));
        DocBlock block = Assert.Single(DocBlocks.CSharpBlocks("```csharp\n\nint x;\n\n```"));
        Assert.Equal("int x;", block.Text);
    }

    [Fact]
    public void Crlf_and_a_bom_parse_like_lf()
    {
        const string Lf = "class C\n{\n    // doc-region: a\n    int x;\n        int y;\n    // doc-region-end: a\n}\n";
        RegionMap lf = DocRegions.Parse(Lf);
        RegionMap crlf = DocRegions.Parse("﻿" + Lf.Replace("\n", "\r\n"));
        Assert.Empty(crlf.Problems);
        Assert.Equal(lf.Names.OrderBy(n => n, StringComparer.Ordinal), crlf.Names.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(lf.Render("a"), crlf.Render("a"));

        const string Markdown = "Intro:\n<!-- template: a -->\n```csharp\nint x;\n    int y;\n```\n";
        DocBlock lfBlock = Assert.Single(DocBlocks.CSharpBlocks(Markdown));
        DocBlock crlfBlock = Assert.Single(DocBlocks.CSharpBlocks("﻿" + Markdown.Replace("\n", "\r\n")));
        Assert.Equal(lfBlock, crlfBlock);
    }

    [Theory]
    [InlineData("// doc-region: a\nx\n", "line 1:")]
    [InlineData("x\n// doc-region-end: a\n", "line 2:")]
    [InlineData("// doc-region: a\n// doc-region: b\n// doc-region-end: a\n// doc-region-end: b\n", "line 3:")]
    [InlineData("// doc-region: a\n// doc-region-end: a\n// doc-region: a\n// doc-region-end: a\n", "line 3:")]
    public void Malformed_templates_are_reported_with_their_line(string template, string expectedStart)
    {
        IReadOnlyList<string> problems = DocRegions.Parse(template).Problems;
        Assert.True(problems.Any(p => p.StartsWith(expectedStart, StringComparison.Ordinal)),
            $"expected a problem starting '{expectedStart}', got: {string.Join(" | ", problems)}");
    }

    [Theory]
    [InlineData("// doc-region panel-variable", true)]
    [InlineData("//doc-region: a", true)]
    [InlineData("// doc-region: Panel", true)]
    [InlineData("// doc-region-end:a", true)]
    [InlineData("// doc-region: a (folded)", true)]
    [InlineData("// Doc-Region: a", true)]
    [InlineData("// see the doc-regions below", false)]
    public void Near_miss_region_markers_are_reported(string line, bool reported)
        => Assert.Equal(reported, DocRegions.Parse("int x;\n" + line + "\nint y;\n").Problems.Count > 0);

    [Fact]
    public void Csharp_blocks_are_found_with_their_marker()
    {
        IReadOnlyList<DocBlock> blocks = DocBlocks.CSharpBlocks(
            "# Doc\n"                                        // 1
            + "<!-- template: panel-variable -->\n"          // 2
            + "```csharp\n"                                  // 3
            + "int x;\n"                                     // 4
            + "```\n"                                        // 5
            + "Text\n"                                       // 6
            + "<!-- fragment: MSFSBlindAssist/X.cs#Y -->\n"  // 7
            + "```cs\n"                                      // 8
            + "Y();\n"                                       // 9
            + "```\n"                                        // 10
            + "```bash\n"                                    // 11
            + "dotnet test\n"                                // 12
            + "```\n");                                      // 13
        Assert.Collection(blocks,
            b => Assert.Equal((3, MarkerKind.Template, "panel-variable"), (b.FenceLine, b.Kind, b.Argument)),
            b => Assert.Equal((8, MarkerKind.Fragment, "MSFSBlindAssist/X.cs#Y"), (b.FenceLine, b.Kind, b.Argument)));
    }

    [Fact]
    public void A_fence_inside_a_list_item_is_found_and_its_indentation_removed()
    {
        DocBlock block = Assert.Single(DocBlocks.CSharpBlocks(
            "1. Step\n   <!-- fragment: a/B.cs#C -->\n   ```csharp\n   C();\n       D();\n   ```\n"));
        Assert.Equal(MarkerKind.Fragment, block.Kind);
        Assert.Equal("a/B.cs#C", block.Argument);
        Assert.Equal("C();\n    D();", block.Text);
    }

    [Fact]
    public void Fences_of_any_length_tildes_and_tag_case_are_found()
    {
        IReadOnlyList<DocBlock> blocks = DocBlocks.CSharpBlocks(
            "````csharp\n```\ninner();\n```\n````\n"
            + "~~~cs\ntilde();\n~~~\n"
            + "```C#\nupper();\n```\n");
        Assert.Equal(3, blocks.Count);
        Assert.Equal("```\ninner();\n```", blocks[0].Text);
        Assert.Equal("tilde();", blocks[1].Text);
        Assert.Equal("upper();", blocks[2].Text);
    }

    [Fact]
    public void An_unmarked_block_reports_the_line_above_its_fence()
    {
        DocBlock block = Assert.Single(DocBlocks.CSharpBlocks("Some prose:\n\n```csharp\nx\n```"));
        Assert.Equal(MarkerKind.None, block.Kind);
        Assert.Equal("", block.Argument);
        Assert.Equal("Some prose:", block.LineAboveFence);
    }

    [Fact]
    public void A_marker_must_be_the_last_nonblank_line_before_the_fence()
        => Assert.Equal(MarkerKind.None,
            Assert.Single(DocBlocks.CSharpBlocks("<!-- template: a -->\nA prose line.\n```csharp\nx\n```\n")).Kind);

    [Theory]
    [InlineData("REQUEST_OUTSIDE_TEMP = 323,", "OUTSIDE_TEMP", false)]
    [InlineData("e.VarName == \"OUTSIDE_TEMP\"", "OUTSIDE_TEMP", true)]
    [InlineData("GetAltitudeControlTypeX()", "GetAltitudeControlType", false)]
    [InlineData("x.GetAltitudeControlType()", "GetAltitudeControlType", true)]
    public void An_identifier_inside_a_longer_name_does_not_count(string text, string word, bool found)
        => Assert.Equal(found, DocBlocks.ContainsWord(text, word));
}

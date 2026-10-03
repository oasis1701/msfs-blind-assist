// Tests for ChangelogBuilder.ChangelogFragment.Parse — the filename/content validator
// behind the per-PR changelog fragments (see CLAUDE.md "Release notes").
//
// Parse is pure: it takes a file NAME and its CONTENT as strings and never touches the
// filesystem or git, so every case here is expressible as a literal. The error strings
// are asserted loosely (Contains) because their exact wording is a UX detail that may
// change; what must not change is that the message names the offending file.

using ChangelogBuilder;

namespace MSFSBlindAssist.Tests;

public class ChangelogFragmentTests
{
    [Theory]
    [InlineData("182-docking-speed.aircraft.md", ChangelogCategory.Aircraft)]
    [InlineData("182-si-import.feature.md", ChangelogCategory.Feature)]
    [InlineData("182-tone-fade.improvement.md", ChangelogCategory.Improvement)]
    [InlineData("182-autobrake.fix.md", ChangelogCategory.Fix)]
    [InlineData("182-ci-bump.internal.md", ChangelogCategory.Internal)]
    public void Parse_accepts_every_category(string fileName, ChangelogCategory expected)
    {
        var result = ChangelogFragment.Parse(fileName, "Something changed.");

        Assert.True(result.Ok);
        Assert.Equal(expected, result.Fragment!.Category);
    }

    [Fact]
    public void Parse_extracts_the_slug()
    {
        var result = ChangelogFragment.Parse("182-docking-speed-callouts.improvement.md", "Body.");

        Assert.Equal("docking-speed-callouts", result.Fragment!.Slug);
    }

    [Fact]
    public void Parse_extracts_the_pr_number()
    {
        var result = ChangelogFragment.Parse("182-docking.fix.md", "Body.");

        Assert.Equal(182, result.Fragment!.PrNumber);
    }

    [Fact]
    public void Parse_accepts_a_large_pr_number()
    {
        var result = ChangelogFragment.Parse("100000-x.fix.md", "Body.");

        Assert.True(result.Ok);
        Assert.Equal(100000, result.Fragment!.PrNumber);
    }

    [Fact]
    public void Parse_trims_the_body()
    {
        var result = ChangelogFragment.Parse("182-a.fix.md", "\n\n  Body text.  \n\n");

        Assert.Equal("Body text.", result.Fragment!.Body);
    }

    [Fact]
    public void Parse_rejects_an_unknown_category()
    {
        var result = ChangelogFragment.Parse("182-a.bugfix.md", "Body.");

        Assert.False(result.Ok);
        Assert.Contains("182-a.bugfix.md", result.Error!);
    }

    [Theory]
    [InlineData("182-Uppercase.fix.md")]        // slug must be lower-case
    [InlineData("182--leading-dash.fix.md")]    // slug must start alphanumeric
    [InlineData("182-no-category.md")]          // category segment missing
    [InlineData("182-a.fix.markdown")]          // wrong extension
    [InlineData("182-a.fix.txt")]
    [InlineData("182-under_score.fix.md")]      // underscore not allowed
    [InlineData("docking.fix.md")]              // no numeric PR prefix at all
    [InlineData("0182-docking.fix.md")]         // leading zero on the PR prefix
    [InlineData("abc-docking.fix.md")]          // non-numeric PR prefix
    [InlineData("182.fix.md")]                  // PR number with no slug
    public void Parse_rejects_a_malformed_filename(string fileName)
    {
        var result = ChangelogFragment.Parse(fileName, "Body.");

        Assert.False(result.Ok);
        Assert.Contains(fileName, result.Error!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n  \n")]
    public void Parse_rejects_an_empty_body(string content)
    {
        var result = ChangelogFragment.Parse("182-a.fix.md", content);

        Assert.False(result.Ok);
        Assert.Contains("182-a.fix.md", result.Error!);
    }

    [Fact]
    public void Parse_accepts_a_full_path_and_uses_only_the_file_name()
    {
        var result = ChangelogFragment.Parse("changelog.d/182-a-b.fix.md", "Body.");

        Assert.True(result.Ok);
        Assert.Equal("a-b", result.Fragment!.Slug);
    }
}

// Tests for ChangelogBuilder.ChangelogRenderer — turns parsed fragments into the release
// body that is prepended to GitHub's generated notes.
//
// Ordering is asserted exactly because it must be deterministic: categories in a fixed
// editorial order (new aircraft first, fixes last) and, within a category, entries
// ordered by PR number numerically, then by slug as a tiebreak. Non-determinism here
// would make release notes reshuffle between runs for no reason.

using ChangelogBuilder;

namespace MSFSBlindAssist.Tests;

public class ChangelogRendererTests
{
    private static ChangelogFragment F(int prNumber, string slug, ChangelogCategory cat, string body) =>
        new(prNumber, slug, cat, body);

    [Fact]
    public void Render_returns_empty_for_no_fragments()
    {
        Assert.Equal("", ChangelogRenderer.Render([]));
    }

    [Fact]
    public void Render_emits_heading_and_bullet()
    {
        var body = ChangelogRenderer.Render([F(1, "a", ChangelogCategory.Fix, "It works now.")]);

        Assert.Equal("## Fixes\n\n- It works now.\n", body);
    }

    [Fact]
    public void Render_orders_categories_editorially_not_alphabetically()
    {
        var body = ChangelogRenderer.Render(
        [
            F(1, "d", ChangelogCategory.Fix, "Fix."),
            F(1, "c", ChangelogCategory.Improvement, "Improvement."),
            F(1, "b", ChangelogCategory.Feature, "Feature."),
            F(1, "a", ChangelogCategory.Aircraft, "Aircraft."),
        ]);

        var order = new[]
        {
            body.IndexOf("## New aircraft", StringComparison.Ordinal),
            body.IndexOf("## New features", StringComparison.Ordinal),
            body.IndexOf("## Improvements", StringComparison.Ordinal),
            body.IndexOf("## Fixes", StringComparison.Ordinal),
        };

        Assert.All(order, i => Assert.True(i >= 0));
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void Render_orders_entries_alphabetically_within_a_category()
    {
        var body = ChangelogRenderer.Render(
        [
            F(1, "zebra", ChangelogCategory.Fix, "Zebra."),
            F(1, "apple", ChangelogCategory.Fix, "Apple."),
            F(1, "mango", ChangelogCategory.Fix, "Mango."),
        ]);

        Assert.Equal("## Fixes\n\n- Apple.\n- Mango.\n- Zebra.\n", body);
    }

    [Fact]
    public void Render_orders_entries_by_pr_number_numerically_not_lexicographically()
    {
        // As strings, "1000" < "182" < "99" (first-character/lexicographic comparison) —
        // exactly backwards. Listed out of order here too, so the assertion only passes
        // if Render actually sorts rather than happening to preserve input order.
        var body = ChangelogRenderer.Render(
        [
            F(1000, "x", ChangelogCategory.Fix, "Thousand."),
            F(182, "y", ChangelogCategory.Fix, "One eighty two."),
            F(99, "z", ChangelogCategory.Fix, "Ninety nine."),
        ]);

        Assert.Equal(
            "## Fixes\n\n- Ninety nine.\n- One eighty two.\n- Thousand.\n",
            body);
    }

    [Fact]
    public void Render_orders_entries_with_the_same_pr_number_by_slug()
    {
        var body = ChangelogRenderer.Render(
        [
            F(182, "b", ChangelogCategory.Fix, "B fragment."),
            F(182, "a", ChangelogCategory.Fix, "A fragment."),
        ]);

        Assert.Equal("## Fixes\n\n- A fragment.\n- B fragment.\n", body);
    }

    [Fact]
    public void Render_omits_a_category_with_no_entries()
    {
        var body = ChangelogRenderer.Render([F(1, "a", ChangelogCategory.Fix, "Fix.")]);

        Assert.DoesNotContain("## New aircraft", body);
        Assert.DoesNotContain("## New features", body);
        Assert.DoesNotContain("## Improvements", body);
    }

    [Fact]
    public void Render_excludes_internal_fragments()
    {
        var body = ChangelogRenderer.Render(
        [
            F(1, "a", ChangelogCategory.Internal, "Refactored the thing."),
            F(2, "b", ChangelogCategory.Fix, "Real fix."),
        ]);

        Assert.DoesNotContain("Refactored the thing.", body);
        Assert.Contains("Real fix.", body);
    }

    [Fact]
    public void Render_returns_empty_when_every_fragment_is_internal()
    {
        var body = ChangelogRenderer.Render([F(1, "a", ChangelogCategory.Internal, "Internal.")]);

        Assert.Equal("", body);
    }

    [Fact]
    public void Render_indents_continuation_lines_under_the_bullet()
    {
        var body = ChangelogRenderer.Render(
            [F(1, "a", ChangelogCategory.Fix, "First line.\nSecond line.\nThird line.")]);

        Assert.Equal("## Fixes\n\n- First line.\n  Second line.\n  Third line.\n", body);
    }

    [Fact]
    public void Render_leaves_blank_lines_blank_rather_than_indenting_them()
    {
        var body = ChangelogRenderer.Render(
            [F(1, "a", ChangelogCategory.Fix, "Para one.\n\nPara two.")]);

        // A blank line indented to "  " would be trailing whitespace.
        Assert.Equal("## Fixes\n\n- Para one.\n\n  Para two.\n", body);
    }

    [Fact]
    public void Render_normalises_windows_line_endings()
    {
        var body = ChangelogRenderer.Render(
            [F(1, "a", ChangelogCategory.Fix, "First.\r\nSecond.")]);

        Assert.DoesNotContain("\r", body);
        Assert.Equal("## Fixes\n\n- First.\n  Second.\n", body);
    }

    [Fact]
    public void Render_separates_sections_with_a_blank_line()
    {
        var body = ChangelogRenderer.Render(
        [
            F(1, "a", ChangelogCategory.Feature, "Feature."),
            F(2, "b", ChangelogCategory.Fix, "Fix."),
        ]);

        Assert.Equal("## New features\n\n- Feature.\n\n## Fixes\n\n- Fix.\n", body);
    }
}

// Tests for ChangelogBuilder.ChangelogRenderer — turns parsed fragments into the release
// body that is prepended to GitHub's generated notes.
//
// Ordering is asserted exactly because it must be deterministic: categories in a fixed
// editorial order (new aircraft first, fixes last) and entries alphabetical by slug
// within a category. Non-determinism here would make release notes reshuffle between
// runs for no reason.

using ChangelogBuilder;

namespace MSFSBlindAssist.Tests;

public class ChangelogRendererTests
{
    private static ChangelogFragment F(string slug, ChangelogCategory cat, string body) =>
        new(slug, cat, body);

    [Fact]
    public void Render_returns_empty_for_no_fragments()
    {
        Assert.Equal("", ChangelogRenderer.Render([]));
    }

    [Fact]
    public void Render_emits_heading_and_bullet()
    {
        var body = ChangelogRenderer.Render([F("a", ChangelogCategory.Fix, "It works now.")]);

        Assert.Equal("## Fixes\n\n- It works now.\n", body);
    }

    [Fact]
    public void Render_orders_categories_editorially_not_alphabetically()
    {
        var body = ChangelogRenderer.Render(
        [
            F("d", ChangelogCategory.Fix, "Fix."),
            F("c", ChangelogCategory.Improvement, "Improvement."),
            F("b", ChangelogCategory.Feature, "Feature."),
            F("a", ChangelogCategory.Aircraft, "Aircraft."),
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
            F("zebra", ChangelogCategory.Fix, "Zebra."),
            F("apple", ChangelogCategory.Fix, "Apple."),
            F("mango", ChangelogCategory.Fix, "Mango."),
        ]);

        Assert.Equal("## Fixes\n\n- Apple.\n- Mango.\n- Zebra.\n", body);
    }

    [Fact]
    public void Render_omits_a_category_with_no_entries()
    {
        var body = ChangelogRenderer.Render([F("a", ChangelogCategory.Fix, "Fix.")]);

        Assert.DoesNotContain("## New aircraft", body);
        Assert.DoesNotContain("## New features", body);
        Assert.DoesNotContain("## Improvements", body);
    }

    [Fact]
    public void Render_excludes_internal_fragments()
    {
        var body = ChangelogRenderer.Render(
        [
            F("a", ChangelogCategory.Internal, "Refactored the thing."),
            F("b", ChangelogCategory.Fix, "Real fix."),
        ]);

        Assert.DoesNotContain("Refactored the thing.", body);
        Assert.Contains("Real fix.", body);
    }

    [Fact]
    public void Render_returns_empty_when_every_fragment_is_internal()
    {
        var body = ChangelogRenderer.Render([F("a", ChangelogCategory.Internal, "Internal.")]);

        Assert.Equal("", body);
    }

    [Fact]
    public void Render_indents_continuation_lines_under_the_bullet()
    {
        var body = ChangelogRenderer.Render(
            [F("a", ChangelogCategory.Fix, "First line.\nSecond line.\nThird line.")]);

        Assert.Equal("## Fixes\n\n- First line.\n  Second line.\n  Third line.\n", body);
    }

    [Fact]
    public void Render_leaves_blank_lines_blank_rather_than_indenting_them()
    {
        var body = ChangelogRenderer.Render(
            [F("a", ChangelogCategory.Fix, "Para one.\n\nPara two.")]);

        // A blank line indented to "  " would be trailing whitespace.
        Assert.Equal("## Fixes\n\n- Para one.\n\n  Para two.\n", body);
    }

    [Fact]
    public void Render_normalises_windows_line_endings()
    {
        var body = ChangelogRenderer.Render(
            [F("a", ChangelogCategory.Fix, "First.\r\nSecond.")]);

        Assert.DoesNotContain("\r", body);
        Assert.Equal("## Fixes\n\n- First.\n  Second.\n", body);
    }

    [Fact]
    public void Render_separates_sections_with_a_blank_line()
    {
        var body = ChangelogRenderer.Render(
        [
            F("a", ChangelogCategory.Feature, "Feature."),
            F("b", ChangelogCategory.Fix, "Fix."),
        ]);

        Assert.Equal("## New features\n\n- Feature.\n\n## Fixes\n\n- Fix.\n", body);
    }
}

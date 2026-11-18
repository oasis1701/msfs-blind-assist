using MSFSBlindAssist.SimConnect.BBS757;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Plan 20, Task 8 fix round: a title ending in a colon names the one value or control beside it (never a heading), the Systems zoom
/// buttons are content, not side-menu entries, and no element of any captured page has an empty name.
/// </summary>
public class Bbs757EfbColonLabelTests
{
    private static Bbs757EfbElements.Built Built(string fixture) =>
        Bbs757EfbElements.Build(Bbs757EfbModelReader.Read(Bbs757EfbTreeJson.Load(fixture)), null, Array.Empty<Bbs757EfbExtraElement>());

    private static List<(string Text, string Kind, string Level)> Elements(Bbs757EfbElements.Built b) =>
        Enumerable.Range(0, b.Count)
            .Select(i => (b.Data[$"items.{i}.text"], b.Data[$"items.{i}.kind"], b.Data[$"items.{i}.level"])).ToList();

    /// <summary>Every committed tree, under each culture the listings run under.</summary>
    public static IEnumerable<object[]> Fixtures() => Bbs757EfbPageListingTests.Fixtures();

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void No_element_of_any_page_has_an_empty_name(string name, string culture)
    {
        var b = TestCulture.Run(culture, () => Built(name));
        for (int i = 0; i < b.Count; i++)
            Assert.False(string.IsNullOrWhiteSpace(b.Data[$"items.{i}.text"]), $"{name}: element {i} ({b.Data[$"items.{i}.kind"]}) has no name");
    }

    [Fact]
    public void The_Systems_zoom_buttons_are_content_named_from_their_controls()
    {
        var els = Elements(Built("30-systems"));
        int sections = els.FindIndex(e => e.Text == "Sections");
        foreach (var name in new[] { "Elec zoom in", "Elec zoom out", "Elec zoom reset" })
        {
            int at = els.FindIndex(e => e.Text == name);
            Assert.True(at >= 0 && at < sections, name + " is a content button before Sections");
            Assert.Equal("button", els[at].Kind);
        }
        Assert.Equal(new[] { "Sections", "Home" }, els.Skip(sections).Select(e => e.Text));
    }

    [Fact]
    public void The_checklist_keeps_its_side_menu()
    {
        // BlueBird marks the checklist shown (BEFORE START when recaptured, 2026-11-07): its entry says so, under Sections.
        var texts = Elements(Built("40-checklist")).Select(e => e.Text).ToList();
        Assert.True(texts.IndexOf("Before start (current page)") > texts.IndexOf("Sections"));
        Assert.True(texts.IndexOf("After start") > texts.IndexOf("Sections"));
    }

    [Fact]
    public void A_status_title_with_its_value_is_one_row()
    {
        var els = Elements(Built("51-settings-aircraft"));
        Assert.Contains(els, e => e.Text == "Automatic step climb status: OFF" && e.Kind == "");
        Assert.DoesNotContain(els, e => e.Text.StartsWith("Automatic step climb status:", StringComparison.Ordinal) && e.Level == "2");
        Assert.DoesNotContain(els, e => e.Text == "OFF");
    }

    [Fact]
    public void A_title_with_a_control_beside_it_is_that_controls_label()
    {
        var els = Elements(Built("13-aircraft-ground-services"));
        Assert.Contains(els, e => e.Text == "GND SRVC Mode: BBS" && e.Kind == "button");
        Assert.DoesNotContain(els, e => e.Text == "GND SRVC Mode:");
        Assert.DoesNotContain(els, e => e.Text == "BBS");
    }

    [Fact]
    public void Pretty_checks_each_part_of_a_slashed_word()
    {
        Assert.Equal("Automatic GPU/jetway", Bbs757EfbVocabulary.Pretty("AUTOMATIC GPU/JETWAY"));
        Assert.Equal("Ground A/C", Bbs757EfbVocabulary.Pretty("GROUND A/C"));
        Assert.Equal("Approach / minimums", Bbs757EfbVocabulary.Pretty("APPROACH / MINIMUMS"));
    }
}

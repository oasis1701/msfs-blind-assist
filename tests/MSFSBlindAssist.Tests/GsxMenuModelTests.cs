using System.Text.Json;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

public class GsxMenuModelTests
{
    private static GsxMenuModel Live()
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "gsx-menu.json"));
        return GsxMenuModel.Parse(JsonDocument.Parse(json).RootElement.Clone());
    }

    [Fact]
    public void Parses_the_live_menu()
    {
        var m = Live();
        Assert.Equal(10, m.Count);
        Assert.Equal("Request Deboarding", m.Entries[0]);
        Assert.Equal("Reposition Aircraft", m.Entries[9]);
        Assert.Contains("KJFK", m.Title);
        Assert.Contains("Gate 20A", m.Subtitle);
    }

    [Fact]
    public void Parallel_arrays_align_with_entries()
    {
        var m = Live();
        Assert.Equal(m.Count, m.Disabled.Count);
        Assert.Equal(m.Count, m.StateClass.Count);
        // "Operate Jetway" is the completed one in the capture
        int jetway = m.Entries.ToList().FindIndex(e => e.Contains("Jetway"));
        Assert.Equal("gsx-state-completed", m.StateClass[jetway]);
        Assert.Equal("Completed", m.StateSuffix(jetway));
    }

    [Fact]
    public void Ragged_arrays_do_not_throw_and_default_to_enabled()
    {
        var m = GsxMenuModel.Parse(JsonDocument.Parse(
            """{"entries":["A","B","C"],"disabled":[true]}""").RootElement);
        Assert.Equal(3, m.Count);
        Assert.True(m.Disabled[0]);
        Assert.False(m.Disabled[2]);
        Assert.False(m.IsSelectable(0));
        Assert.True(m.IsSelectable(2));
    }

    [Fact]
    public void ResolveIndex_returns_painted_index_when_label_still_matches()
    {
        var m = Live();
        Assert.Equal(3, m.ResolveIndex(3, "Request Boarding"));
    }

    [Fact]
    public void ResolveIndex_relocates_when_the_menu_shifted_under_us()
    {
        var m = Live();
        // pretend we painted "Request Boarding" at 0; it actually lives at 3
        Assert.Equal(3, m.ResolveIndex(0, "Request Boarding"));
    }

    [Fact]
    public void ResolveIndex_refuses_when_the_label_is_gone()
    {
        var m = Live();
        Assert.Equal(-1, m.ResolveIndex(2, "Some Entry That Navigated Away"));
    }

    [Fact]
    public void Out_of_range_is_never_selectable()
    {
        var m = Live();
        Assert.False(m.IsSelectable(-1));
        Assert.False(m.IsSelectable(999));
    }

    [Fact]
    public void Empty_menu_is_safe()
    {
        var m = GsxMenuModel.Parse(JsonDocument.Parse("{}").RootElement);
        Assert.Equal(0, m.Count);
        Assert.False(m.IsSelectable(0));
        Assert.Equal(-1, m.ResolveIndex(0, "anything"));
    }

    [Fact]
    public void ResolveIndex_refuses_ambiguous_labels_when_painted_index_is_stale()
    {
        var m = GsxMenuModel.Parse(JsonDocument.Parse(
            """{"entries":["Request Boarding","Other","Request Boarding"]}""").RootElement);
        // Painted index 1 pointed at "Other", but we're now looking for "Request Boarding"
        // which exists at both 0 and 2 — ambiguous, refuse.
        Assert.Equal(-1, m.ResolveIndex(1, "Request Boarding"));
    }

    [Fact]
    public void ResolveIndex_accepts_ambiguous_label_when_painted_index_still_holds_it()
    {
        var m = GsxMenuModel.Parse(JsonDocument.Parse(
            """{"entries":["Request Boarding","Other","Request Boarding"]}""").RootElement);
        // Painted index 2 still holds "Request Boarding" — that is positive evidence,
        // return it even though the label is duplicated elsewhere.
        Assert.Equal(2, m.ResolveIndex(2, "Request Boarding"));
    }

    [Fact]
    public void ResolveIndex_still_relocates_with_single_match()
    {
        var m = GsxMenuModel.Parse(JsonDocument.Parse(
            """{"entries":["Deboarding","Request Boarding","Refuel"]}""").RootElement);
        // Single match at index 1, painted index was 0 (stale) — relocate to 1.
        Assert.Equal(1, m.ResolveIndex(0, "Request Boarding"));
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "2")]
    [InlineData(8, "9")]
    [InlineData(9, "0")]
    public void Shortcut_matches_the_accessgsx_keyboard_convention(int index, string expected)
        => Assert.Equal(expected, GsxMenuModel.Shortcut(index));

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(13)]
    [InlineData(14)]
    public void Shortcut_never_claims_a_letter_for_a_menu_entry(int index)
    {
        // A-E are GSX's own system block (Customize Airport/Airplane, Settings,
        // Restart GSX, Reload SimBrief). Rendering "A." beside menu entry 11
        // would tell a blind pilot to press a key that runs something else.
        string shortcut = GsxMenuModel.Shortcut(index);
        Assert.False(string.IsNullOrEmpty(shortcut));
        Assert.DoesNotContain(shortcut, new[] { "A", "B", "C", "D", "E" });
    }

    [Fact]
    public void Shortcut_beyond_the_numbered_range_still_renders_something()
    {
        // No GSX menu observed so far exceeds 10 entries, but the display must
        // never throw or go blank for a longer page.
        string shortcut = GsxMenuModel.Shortcut(20);
        Assert.False(string.IsNullOrEmpty(shortcut));
    }

    [Fact]
    public void StateSuffix_maps_the_live_performed_value_to_in_progress()
    {
        // GSX's real wire value is "gsx-state-performed", not "-performing" --
        // captured live at EDDF (2026-08) on "113/143 passengers boarded" mid-
        // boarding. Before this, the switch's default case returned null and the
        // in-progress cue was silently lost to the screen reader entirely.
        var m = GsxMenuModel.Parse(JsonDocument.Parse(
            """{"entries":["113/143 passengers boarded"],"stateClass":["gsx-state-performed"]}""").RootElement);
        Assert.Equal("In progress", m.StateSuffix(0));
    }

    [Fact]
    public void StateSuffix_maps_the_live_requested_value_to_requested()
    {
        // Live at EDDF: requesting deboarding produced "Deboarding requested"
        // with stateClass 'gsx-state-requested' -- the third and last stateClass
        // value observed across the whole session, alongside -performed and
        // -completed. The information isn't currently LOST (the entry text
        // already says "requested"), but the suffix should stay consistent with
        // the other two mapped states rather than silently returning null here.
        var m = GsxMenuModel.Parse(JsonDocument.Parse(
            """{"entries":["Deboarding requested"],"stateClass":["gsx-state-requested"]}""").RootElement);
        Assert.Equal("Requested", m.StateSuffix(0));
    }

    [Fact]
    public void StateSuffix_still_maps_the_never_observed_performing_spelling()
    {
        // "-performing" was never once seen across the whole EDDF session that
        // found the "-performed" bug above, but it costs nothing to keep and we
        // cannot prove no GSX build ever emits it.
        var m = GsxMenuModel.Parse(JsonDocument.Parse(
            """{"entries":["x"],"stateClass":["gsx-state-performing"]}""").RootElement);
        Assert.Equal("In progress", m.StateSuffix(0));
    }
}

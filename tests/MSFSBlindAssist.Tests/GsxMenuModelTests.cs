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
}

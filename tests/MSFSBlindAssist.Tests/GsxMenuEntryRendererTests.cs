using System.Text.Json;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// AccessGSXForm.RepopulateMenu feeds GsxMenuEntryRenderer.RenderLines directly into
/// the rendered/spoken menu text. These pin the blank-padding fix at the rendering
/// layer: a blank entry must be skipped outright rather than rendered as a bare
/// numbered row, and the entries either side of a run of blanks must keep the
/// shortcut number matching their REAL GSX index -- never a compacted position --
/// because that index is exactly what GsxService.PickMenuEntry sends as menu.pick's
/// index. Internal type, reached via InternalsVisibleTo
/// (Properties/InternalsVisibleTo.cs) -- same pattern as GsxMenuAnnounceResolverTests,
/// GsxRangeBoundsResolverTests and GsxActiveServiceResolverTests.
/// </summary>
public class GsxMenuEntryRendererTests
{
    private static GsxMenuModel Fixture(string name)
    {
        string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        return GsxMenuModel.Parse(JsonDocument.Parse(json).RootElement.Clone());
    }

    [Fact]
    public void Blank_slots_produce_no_row_but_the_real_entries_keep_their_true_indices()
    {
        // Real capture: EDDF "menu.search" for "A15" -- one match at index 0,
        // "Back" at index 9, blank padding ("") at every index in between,
        // disabled = [false x10] throughout.
        var m = Fixture("gsx-menu-search-blank.json");

        var lines = GsxMenuEntryRenderer.RenderLines(m);

        Assert.Equal(2, lines.Count);
        // Index 0's shortcut is "1" -- verbatim entry text (leading/trailing
        // spaces included, exactly as GSX published it).
        Assert.Equal("1. " + m.Entries[0], lines[0]);
        Assert.Contains("Gate A15 with Safedock", lines[0]);
        // Index 9's shortcut is "0" -- NOT "2", which is what a compacted
        // (post-skip) position would print. This is the numbering-alignment
        // half of the fix: the printed number must stay the real GSX index,
        // because that same index is what menu.pick receives.
        Assert.Equal("0. Back", lines[1]);
    }

    [Fact]
    public void A_leading_blank_entry_is_skipped_without_disturbing_later_indices()
    {
        var m = GsxMenuModel.Parse(JsonDocument.Parse(
            """{"entries":["","Real"]}""").RootElement);

        Assert.Equal(new[] { "2. Real" }, GsxMenuEntryRenderer.RenderLines(m));
    }

    [Fact]
    public void An_all_blank_menu_renders_no_rows_at_all()
    {
        var m = GsxMenuModel.Parse(JsonDocument.Parse(
            """{"entries":["", "   ", ""]}""").RootElement);

        Assert.Empty(GsxMenuEntryRenderer.RenderLines(m));
    }

    [Fact]
    public void A_disabled_but_worded_entry_still_renders_with_its_unavailable_suffix()
    {
        // A blank slot and a disabled slot are different things -- GSX conveys
        // unavailability through entry TEXT (see docs/gsx.md), and a disabled
        // entry that still carries real text must keep rendering, suffix and
        // all. Only a genuinely blank entry is skipped.
        var m = GsxMenuModel.Parse(JsonDocument.Parse(
            """{"entries":["Request Boarding"],"disabled":[true]}""").RootElement);

        Assert.Equal(
            new[] { "1. Request Boarding — unavailable" },
            GsxMenuEntryRenderer.RenderLines(m));
    }

    [Fact]
    public void A_fully_populated_menu_renders_one_line_per_entry_exactly_as_before()
    {
        // Regression pin against the original 10-entry KJFK capture (no blank
        // slots at all): every entry still renders, in order, with its state
        // suffix -- unchanged behaviour for a normal menu.
        var m = Fixture("gsx-menu.json");

        var lines = GsxMenuEntryRenderer.RenderLines(m);

        Assert.Equal(10, lines.Count);
        Assert.Equal("1. Request Deboarding", lines[0]);
        Assert.Equal("6. Operate Jetway — Completed", lines[5]);   // gsx-state-completed
        Assert.Equal("0. Reposition Aircraft", lines[9]);
    }
}

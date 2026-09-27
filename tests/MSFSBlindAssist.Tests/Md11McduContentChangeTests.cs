using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// What counts as a NEW screen (review round 2, C8). The manager keeps the old object for an
/// identical repeat, and the window's poll skips a screen it has already rendered by reference —
/// so whatever the manager calls "different" costs a re-render and a cursor restore. The
/// per-line font size used to count: decoded into LineIsLarge and read by nothing but that
/// comparison, so a font-only change re-rendered the page and re-restored the cursor for no
/// visible or audible difference. Text and annunciators still count.
/// </summary>
public class Md11McduContentChangeTests
{
    private static Md11McduExportData Page(string title, bool large, bool msg = false)
    {
        var raw = new Md11McduExportData { Dspy = true, Msg = msg, Text = new Md11McduChar[Md11McduLayout.Chars] };
        for (var i = 0; i < title.Length; i++)
            raw.Text[i] = new Md11McduChar { Value = title[i], Large = large };
        return raw;
    }

    [Fact]
    public void A_font_only_change_keeps_the_screen_the_window_already_rendered()
    {
        var manager = new Md11McduDataManager();
        manager.Deliver(Md11McduUnit.Left, Page("ACT F-PLN", large: true));
        var rendered = manager.GetScreen(Md11McduUnit.Left);

        manager.Deliver(Md11McduUnit.Left, Page("ACT F-PLN", large: false));

        Assert.Same(rendered, manager.GetScreen(Md11McduUnit.Left));
    }

    [Fact]
    public void A_text_change_is_a_new_screen()
    {
        var manager = new Md11McduDataManager();
        manager.Deliver(Md11McduUnit.Left, Page("ACT F-PLN", large: true));
        var rendered = manager.GetScreen(Md11McduUnit.Left);

        manager.Deliver(Md11McduUnit.Left, Page("MENU", large: true));

        Assert.NotSame(rendered, manager.GetScreen(Md11McduUnit.Left));
        Assert.Equal("MENU", manager.GetScreen(Md11McduUnit.Left)!.Title);
    }

    [Fact]
    public void An_annunciator_change_with_the_same_text_is_a_new_screen()
    {
        // MSG lighting is an event with no text change behind it — the window announces it.
        var manager = new Md11McduDataManager();
        manager.Deliver(Md11McduUnit.Center, Page("MENU", large: true));
        var rendered = manager.GetScreen(Md11McduUnit.Center);

        manager.Deliver(Md11McduUnit.Center, Page("MENU", large: true, msg: true));

        Assert.NotSame(rendered, manager.GetScreen(Md11McduUnit.Center));
        Assert.True(manager.GetScreen(Md11McduUnit.Center)!.Msg);
    }
}

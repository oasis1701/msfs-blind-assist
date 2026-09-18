using MSFSBlindAssist.SimConnect.MD11;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// Pins the reuse path of the MCDU client-data manager: there is ONE per SimConnect connection.
///
/// The manager's Register() latch was written "once per connection", but every switch INTO the
/// MD-11 (and every re-click of its Aircraft menu item) constructed a NEW manager, so the latch
/// was per-instance and the second MD-11 load on a connection re-issued MapClientDataNameToID
/// and the three AddToClientDataDefinition calls against ids that were still defined and still
/// carrying the first load's live subscriptions — the DUPLICATE_ID / changed-under-a-live-request
/// case the latch existed to prevent.
///
/// The fix keeps the manager across aircraft switches and, on reuse, RESETS its cache before the
/// snapshot is re-issued: the aircraft was unloaded in between, so the cached page belongs to the
/// previous load, and a later-opened window must not read it as current if the snapshot never
/// answers. That reset is what these tests pin, through the only members the MCDU window reads —
/// GetScreen, fed by Deliver: Reset must forget every unit's page, and a snapshot identical to the
/// old page must land as a first delivery again — a NEW screen object, which the window's
/// reference shortcut (it skips a render when GetScreen hands back the object it last drew) then
/// renders — rather than be dropped as an identical repeat.
/// </summary>
public class Md11McduManagerReuseTests
{
    /// <summary>A raw export with <paramref name="title"/> on row 0 and nothing else.</summary>
    private static Md11McduExportData Page(string title)
    {
        var raw = new Md11McduExportData { Dspy = true, Text = new Md11McduChar[Md11McduLayout.Chars] };
        for (var i = 0; i < title.Length; i++)
            raw.Text[i] = new Md11McduChar { Value = title[i], Large = true };
        return raw;
    }

    private static readonly Md11McduUnit[] AllUnits = { Md11McduUnit.Left, Md11McduUnit.Center, Md11McduUnit.Right };

    [Fact]
    public void Reset_forgets_every_screen_so_the_window_reports_no_data_until_the_snapshot_lands()
    {
        var manager = new Md11McduDataManager();
        manager.Deliver(Md11McduUnit.Left, Page("MENU"));
        manager.Deliver(Md11McduUnit.Right, Page("A/C STATUS"));
        Assert.NotNull(manager.GetScreen(Md11McduUnit.Left));
        Assert.NotNull(manager.GetScreen(Md11McduUnit.Right));

        manager.Reset();

        foreach (var unit in AllUnits)
            Assert.Null(manager.GetScreen(unit));
    }

    [Fact]
    public void A_snapshot_identical_to_the_old_page_lands_again_after_Reset_as_a_new_screen()
    {
        var manager = new Md11McduDataManager();
        manager.Deliver(Md11McduUnit.Left, Page("MENU"));
        var beforeReset = manager.GetScreen(Md11McduUnit.Left);

        manager.Reset();
        // The re-issued start-up snapshot: the aircraft came back on the same page.
        manager.Deliver(Md11McduUnit.Left, Page("MENU"));

        var afterReset = manager.GetScreen(Md11McduUnit.Left);
        Assert.NotNull(afterReset);
        Assert.Equal("MENU", afterReset!.Title);
        // A NEW object: the snapshot landed as a first delivery. Dropped as an identical repeat, it
        // would still be the pre-Reset object and the window's reference shortcut would skip it.
        Assert.NotSame(beforeReset, afterReset);
    }

    [Fact]
    public void An_identical_repeat_without_a_Reset_is_still_dropped_by_content()
    {
        // The snapshot echoing what the subscription just delivered must keep the OLD object —
        // the form's reference shortcut depends on it. Reset must not have loosened this.
        var manager = new Md11McduDataManager();

        manager.Deliver(Md11McduUnit.Center, Page("MENU"));
        var first = manager.GetScreen(Md11McduUnit.Center);
        manager.Deliver(Md11McduUnit.Center, Page("MENU"));

        Assert.Same(first, manager.GetScreen(Md11McduUnit.Center));
    }

    [Fact]
    public void A_changed_page_replaces_the_cached_screen()
    {
        var manager = new Md11McduDataManager();
        manager.Deliver(Md11McduUnit.Right, Page("MENU"));
        var first = manager.GetScreen(Md11McduUnit.Right);

        manager.Deliver(Md11McduUnit.Right, Page("A/C STATUS"));

        var current = manager.GetScreen(Md11McduUnit.Right);
        Assert.NotSame(first, current);
        Assert.Equal("A/C STATUS", current!.Title);
    }
}

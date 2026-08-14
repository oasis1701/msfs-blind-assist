using System.Text.Json;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Services.Gsx.Remote;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// AccessGSXForm.OnMenuChangedUi used to speak the full rendered menu on every single
/// GsxService.MenuChanged event. Live at EDDF, GSX republishes the whole /menu object
/// roughly 3 times a second while a service runs, because one entry embeds a live
/// counter -- three consecutive captured payloads differed only here:
///
///   "113/143 passengers boarded" -> "114/143 passengers boarded" -> "115/143 ..."
///
/// every other entry byte-identical. Announcing that unconditionally, into a QUEUED,
/// non-interrupting announcer, buried every other callout in the app behind an
/// unbounded backlog. GsxMenuAnnounceResolver.ShouldAnnounce is the pure decision of
/// whether a fresh menu differs from the last one ANNOUNCED enough to re-speak it; the
/// menu ListBox itself is always repopulated regardless (see AccessGSXForm.RepopulateMenu),
/// so an unannounced tick is still readable on demand.
///
/// Internal type, reached via InternalsVisibleTo (Properties/InternalsVisibleTo.cs) --
/// same pattern as GsxRangeBoundsResolverTests / GsxActiveServiceResolverTests.
/// </summary>
public class GsxMenuAnnounceResolverTests
{
    private static GsxMenuModel Menu(string title, params string[] entries)
    {
        string json = JsonSerializer.Serialize(new { title, entries });
        return GsxMenuModel.Parse(JsonDocument.Parse(json).RootElement);
    }

    [Fact]
    public void Digit_only_tick_inside_one_entry_does_not_announce()
    {
        // The exact live EDDF transition: entry 1 ticks from 113 to 114 of 143
        // passengers boarded; every other entry is untouched.
        var previous = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "113/143 passengers boarded", "Operate Jetway");
        var current = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "114/143 passengers boarded", "Operate Jetway");

        Assert.False(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }

    [Fact]
    public void Digit_run_crossing_a_length_boundary_is_still_a_digit_only_change()
    {
        // 9 -> 10 changes the digit run's own length, not just its value -- still
        // purely a counter tick, never a word-level change. Needs more than one
        // entry in the menu, or "the one entry changed" and "every entry changed"
        // are the same event and the page-turn guard (correctly) can't tell them
        // apart -- exactly what real GSX menus never ask it to do.
        var previous = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "9/143 passengers boarded", "Operate Jetway");
        var current = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "10/143 passengers boarded", "Operate Jetway");

        Assert.False(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }

    [Fact]
    public void Byte_identical_menu_does_not_announce()
    {
        var previous = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Request Boarding");
        var current = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Request Boarding");

        Assert.False(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }

    [Fact]
    public void Word_level_entry_change_announces()
    {
        // Live at EDDF: this entry changed from "Request Boarding" to "Boarding
        // no longer possible" once boarding closed -- a real availability
        // transition, not a tick, and GSX's `disabled` array never flagged it.
        var previous = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Request Boarding", "Operate Jetway");
        var current = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Boarding no longer possible", "Operate Jetway");

        Assert.True(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }

    [Fact]
    public void Pushback_observed_word_level_change_announces()
    {
        // Live at EDDF: requesting pushback changed this entry's text from
        // "Customize this Parking position" to "Reset position".
        var previous = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Customize this Parking position");
        var current = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Reset position");

        Assert.True(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }

    [Fact]
    public void Title_change_announces_even_when_every_entry_is_identical()
    {
        var previous = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Request Boarding");
        var current = Menu("Activate Services at EDDF/Frankfurt Main", "Request Deboarding", "Request Boarding");

        Assert.True(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }

    [Fact]
    public void Entry_count_change_announces()
    {
        var previous = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Request Boarding");
        var current = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Request Boarding", "Additional Services ▶");

        Assert.True(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }

    [Fact]
    public void Page_turn_where_every_entry_changes_digits_only_still_announces()
    {
        // GSX paginates its own menus at 10 entries with an ordinary-looking
        // "Next Page" entry (confirmed live at EDDF) -- a paged stand list can
        // plausibly change EVERY entry by digits alone (Gate A11 -> Gate A21,
        // etc). A counter tick touches one entry; a page turn touches all of
        // them, and this guard is what tells the two apart.
        var previous = Menu("Select a parking position", "Gate A11", "Gate A12", "Gate A13", "Gate A14");
        var current = Menu("Select a parking position", "Gate A21", "Gate A22", "Gate A23", "Gate A24");

        Assert.True(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }

    [Fact]
    public void Exactly_half_the_entries_changing_by_digits_only_does_not_trip_the_guard()
    {
        // The guard is "more than half," not "at least half" -- exactly half
        // changing by digits alone is still ordinary ticking, not a page turn.
        var previous = Menu("Activate Services at EDDF/Frankfurt", "1/10 done", "2/10 done", "Operate Jetway", "Request Boarding");
        var current = Menu("Activate Services at EDDF/Frankfurt", "3/10 done", "4/10 done", "Operate Jetway", "Request Boarding");

        Assert.False(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }

    [Fact]
    public void More_than_half_the_entries_changing_by_digits_only_trips_the_guard()
    {
        var previous = Menu("Activate Services at EDDF/Frankfurt", "1/10 done", "2/10 done", "3/10 done", "Request Boarding");
        var current = Menu("Activate Services at EDDF/Frankfurt", "4/10 done", "5/10 done", "6/10 done", "Request Boarding");

        Assert.True(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }

    [Fact]
    public void First_appearance_from_empty_always_announces()
    {
        var current = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Request Boarding");

        Assert.True(GsxMenuAnnounceResolver.ShouldAnnounce(GsxMenuModel.Empty, current));
    }

    [Fact]
    public void Previously_empty_from_a_prior_hide_always_announces()
    {
        // AccessGSXForm.OnMenuHiddenUi resets _renderedMenu to GsxMenuModel.Empty
        // whenever GSX reports the menu hidden -- the reopened menu must always
        // speak in full even if it happens to look like the one before the hide.
        var previous = GsxMenuModel.Parse(JsonDocument.Parse("{}").RootElement);
        var current = Menu("Activate Services at EDDF/Frankfurt", "Request Deboarding", "Request Boarding");

        Assert.Equal(0, previous.Count);
        Assert.True(GsxMenuAnnounceResolver.ShouldAnnounce(previous, current));
    }
}

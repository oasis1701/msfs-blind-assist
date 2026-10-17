using MSFSBlindAssist.Forms.Volanta;

namespace MSFSBlindAssist.Tests.Volanta;

// Characterization tests for Forms/Volanta/VolantaListOrder: while the pilot is in a Volanta list its rows keep
// their places, so arrowing down always reaches the next row.
public class VolantaListOrderTests
{
    private static IReadOnlyList<string> Arrange(string[] shown, string[] sorted, bool hold)
        => VolantaListOrder.Arrange(shown, sorted, k => k, hold);

    [Fact]
    public void Not_holding_takes_the_new_order()
        => Assert.Equal(new[] { "c", "a", "b" }, Arrange(new[] { "a", "b", "c" }, new[] { "c", "a", "b" }, hold: false));

    [Fact]
    public void Holding_keeps_shown_rows_in_place_and_adds_new_ones_at_the_end_in_sorted_order()
        => Assert.Equal(new[] { "a", "b", "c", "e", "d" },
            Arrange(new[] { "a", "b", "c" }, new[] { "e", "c", "a", "d", "b" }, hold: true));

    [Fact]
    public void Holding_drops_rows_that_are_gone()
        => Assert.Equal(new[] { "a", "c", "d" }, Arrange(new[] { "a", "b", "c" }, new[] { "d", "c", "a" }, hold: true));

    [Fact]
    public void Holding_an_empty_list_takes_the_sorted_order()
        => Assert.Equal(new[] { "b", "a" }, Arrange(Array.Empty<string>(), new[] { "b", "a" }, hold: true));

    [Fact]
    public void Holding_with_everything_gone_leaves_nothing()
        => Assert.Empty(Arrange(new[] { "a" }, Array.Empty<string>(), hold: true));

    [Fact]
    public void Detail_lines_are_keyed_by_their_field_so_the_reader_stays_on_it()
    {
        var levelled = VolantaListOrder.DetailKeys(new[] { "Altitude: 36,000 feet", "Ground speed: 442 knots" });
        var climbing = VolantaListOrder.DetailKeys(new[] { "Altitude: 35,100 feet, climbing", "Vertical speed: 1500 feet per minute", "Ground speed: 440 knots" });
        Assert.Equal(new[] { "Altitude", "Ground speed" }, levelled);
        Assert.Equal(new[] { "Altitude", "Vertical speed", "Ground speed" }, climbing);   // a line above appeared: same key
    }

    [Fact]
    public void Headings_and_free_text_lines_get_unique_keys_in_order()
        => Assert.Equal(new[] { "Station", "ATIS:", "text", "text#2", "Info", "text#3" },
            VolantaListOrder.DetailKeys(new[] { "Station: EDDP_GND", "ATIS:", "Leipzig Ground", "Briefing at example", "Info: Q", "No aircraft selected." }));

    [Fact]
    public void A_repeated_field_gets_its_occurrence()
        => Assert.Equal(new[] { "Pilot", "Pilot#2" }, VolantaListOrder.DetailKeys(new[] { "Pilot: a", "Pilot: b" }));

    // VOL-9: the list the reader is in never has every row rewritten; rows gone are removed, new ones join at the
    // end, and only the rows near the reader's are rewritten.

    private static string[] Keys(int count, int from = 0) => Enumerable.Range(from, count).Select(i => "k" + i).ToArray();

    [Fact]
    public void A_held_update_removes_gone_rows_highest_first_and_rewrites_only_near_the_reader()
    {
        var shown = Keys(1000);
        var arranged = VolantaListOrder.Arrange(shown, shown.Where(k => k != "k10" && k != "k900").Concat(new[] { "new" }).ToArray(), k => k, hold: true);
        var plan = VolantaListOrder.PlanHeldUpdate(shown, arranged, "k500", 500, window: 150)!;
        Assert.Equal(new[] { 900, 10 }, plan.Remove);
        Assert.Equal(499 - 150, plan.RewriteFrom);   // k500 is row 499 once k10 is gone
        Assert.Equal(499 + 150, plan.RewriteTo);
    }

    [Fact]
    public void A_held_update_keeps_its_window_inside_the_list()
    {
        var plan = VolantaListOrder.PlanHeldUpdate(Keys(20), Keys(20), "k3", 3, window: 150)!;
        Assert.Equal((0, 19), (plan.RewriteFrom, plan.RewriteTo));
        Assert.Empty(plan.Remove);
    }

    [Fact]
    public void Without_a_selected_row_the_window_is_the_old_row_number_else_the_top()
    {
        Assert.Equal(40, VolantaListOrder.PlanHeldUpdate(Keys(500), Keys(500), "gone", 50, window: 10)!.RewriteFrom);
        Assert.Equal(0, VolantaListOrder.PlanHeldUpdate(Keys(500), Keys(500), null, -1, window: 10)!.RewriteFrom);
        var emptied = VolantaListOrder.PlanHeldUpdate(Keys(3), Array.Empty<string>(), "k1", 1, window: 10)!;
        Assert.Equal(new[] { 2, 1, 0 }, emptied.Remove);
        Assert.True(emptied.RewriteTo < emptied.RewriteFrom);
    }

    [Fact]
    public void Rows_not_in_a_held_order_have_no_plan_so_every_row_is_rewritten()
        => Assert.Null(VolantaListOrder.PlanHeldUpdate(new[] { "a", "b", "c" }, new[] { "c", "a", "b" }, "a", 0, window: 10));

    // A status line ("Volanta's web map is loading, so this list is not updating yet") is always the first row, held
    // or not, and goes in at the top without rewriting the rows below it.

    private const string Loading = VolantaListOrder.StatusKeyPrefix + "loading";
    private const string Lost = VolantaListOrder.StatusKeyPrefix + "lost";

    [Fact]
    public void Holding_puts_a_new_status_line_first_and_keeps_the_other_rows_in_place()
        => Assert.Equal(new[] { Loading, "a", "b", "c", "d" },
            Arrange(new[] { "a", "b", "c" }, new[] { Loading, "c", "a", "d", "b" }, hold: true));

    [Fact]
    public void Holding_keeps_a_shown_status_line_first()
        => Assert.Equal(new[] { Loading, "a", "b" }, Arrange(new[] { Loading, "a", "b" }, new[] { Loading, "b", "a" }, hold: true));

    [Fact]
    public void Holding_drops_a_status_line_that_is_gone()
        => Assert.Equal(new[] { "a", "b" }, Arrange(new[] { Loading, "a", "b" }, new[] { "b", "a" }, hold: true));

    [Fact]
    public void A_held_update_inserts_a_new_status_line_at_the_top_and_rewrites_only_near_the_reader()
    {
        var shown = Keys(1000);
        var arranged = VolantaListOrder.Arrange(shown, new[] { Loading }.Concat(shown).ToArray(), k => k, hold: true);
        var plan = VolantaListOrder.PlanHeldUpdate(shown, arranged, "k500", 500, window: 150)!;
        Assert.Empty(plan.Remove);
        Assert.Equal(1, plan.InsertTop);
        Assert.Equal((501 - 150, 501 + 150), (plan.RewriteFrom, plan.RewriteTo));   // k500 is row 501 below the line
    }

    [Fact]
    public void A_held_update_removes_a_status_line_that_is_gone_and_inserts_none()
    {
        var plan = VolantaListOrder.PlanHeldUpdate(new[] { Loading, "a", "b" }, new[] { "a", "b" }, "a", 1, window: 10)!;
        Assert.Equal(new[] { 0 }, plan.Remove);
        Assert.Equal(0, plan.InsertTop);
    }

    [Fact]
    public void A_held_update_replaces_a_status_line_whose_reason_changed()
    {
        var plan = VolantaListOrder.PlanHeldUpdate(new[] { Loading, "a", "b" }, new[] { Lost, "a", "b" }, "a", 1, window: 10)!;
        Assert.Equal(new[] { 0 }, plan.Remove);
        Assert.Equal(1, plan.InsertTop);
    }

    [Fact]
    public void A_held_update_with_a_shown_status_line_inserts_nothing()
        => Assert.Equal(0, VolantaListOrder.PlanHeldUpdate(new[] { Loading, "a" }, new[] { Loading, "a", "b" }, "a", 1, window: 10)!.InsertTop);
}

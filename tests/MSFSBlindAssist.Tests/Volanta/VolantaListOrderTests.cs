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
}

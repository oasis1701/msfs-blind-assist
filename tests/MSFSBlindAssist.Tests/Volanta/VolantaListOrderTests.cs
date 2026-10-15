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
}

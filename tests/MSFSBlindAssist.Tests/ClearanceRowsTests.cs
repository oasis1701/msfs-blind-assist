// Tests for MSFSBlindAssist.Navigation.ClearanceRows: the pilot's per-row picks follow the rows that
// remain when route loading removes rows from a clearance (a runway used as a taxi leg, a first
// taxiway the route never drives [RTE-39]).

using MSFSBlindAssist.Database.Models;
using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class ClearanceRowsTests
{
    [Fact]
    public void A_list_of_rows_moves_down_past_each_removed_row()
    {
        // Rows 0..4, row 1 removed: row 0 stays, rows 2..4 become 1..3.
        Assert.Equal(new List<int> { 0, 1, 3 }, ClearanceRows.Without(new List<int> { 0, 2, 4 }, new[] { 1 }));
    }

    [Fact]
    public void A_pick_on_a_removed_row_goes_with_it()
    {
        Assert.Equal(new List<int> { 1 }, ClearanceRows.Without(new List<int> { 1, 2 }, new[] { 1 }));
    }

    [Fact]
    public void Removing_the_leading_rows_moves_every_pick_down_by_that_many()
    {
        Assert.Equal(new List<int> { 0, 3 }, ClearanceRows.Without(new List<int> { 2, 5 }, ClearanceRows.Leading(2)));

        var picks = ClearanceRows.Without(new Dictionary<int, string> { [5] = "07C" }, ClearanceRows.Leading(2));
        Assert.Equal("07C", Assert.Single(picks!).Value);
        Assert.Equal(3, picks!.Keys.Single());
    }

    [Fact]
    public void Picks_of_any_kind_move_with_their_rows()
    {
        var picks = ClearanceRows.Without(
            new Dictionary<int, TurnSide> { [0] = TurnSide.Right, [2] = TurnSide.Left, [4] = TurnSide.Right }, new[] { 1, 3 });

        Assert.Equal(TurnSide.Right, picks![0]);
        Assert.Equal(TurnSide.Left, picks[1]);
        Assert.Equal(TurnSide.Right, picks[2]);
        Assert.Equal(3, picks.Count);
    }

    // The runway-leg pass removes rows walking from the end, so its list comes in descending order.
    [Fact]
    public void The_order_rows_were_removed_in_does_not_matter()
    {
        Assert.Equal(new List<int> { 0, 2 }, ClearanceRows.Without(new List<int> { 0, 4 }, new[] { 3, 1 }));
    }

    [Fact]
    public void Nothing_removed_changes_nothing()
    {
        Assert.Equal(new List<int> { 0, 2 }, ClearanceRows.Without(new List<int> { 0, 2 }, Array.Empty<int>()));
    }

    [Fact]
    public void Null_stays_null()
    {
        Assert.Null(ClearanceRows.Without((List<int>?)null, new[] { 0 }));
        Assert.Null(ClearanceRows.Without((Dictionary<int, string>?)null, new[] { 0 }));
    }

    [Fact]
    public void Leading_names_the_first_rows()
    {
        Assert.Equal(new[] { 0, 1, 2 }, ClearanceRows.Leading(3));
        Assert.Empty(ClearanceRows.Leading(0));
    }
}

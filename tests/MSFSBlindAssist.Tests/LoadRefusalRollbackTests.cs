// Whether a LoadRoute reachability refusal must roll back the state it had already
// overwritten, leaving the route currently being flown untouched. PR #238 review follow-up
// (Important 5): LoadRoute's three refusal sites (no start node, no buildable route, first
// leg crosses a runway) each independently hand-typed the same
// `reachability != ReachabilityClass.Unchanged` condition around their own
// RestoreLoadRouteRollback call -- exactly two of the three having silently drifted from the
// third was the defect Task 7 fixed, and hand-re-establishing agreement leaves the same drift
// surface open for the next edit. This predicate is the one place the decision lives now; all
// three sites call it instead of re-typing the comparison.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LoadRefusalRollbackTests
{
    [Fact]
    public void Unchanged_never_rolls_back()
    {
        // The aircraft was already on (or headed toward) the destination's own network --
        // an ordinary refusal here has nothing to do with route reachability, and rolling it
        // back too is a separate, unasked-for change.
        Assert.False(LoadRefusalRollback.ShouldRestore(ReachabilityClass.Unchanged));
    }

    [Fact]
    public void Destination_not_connected_rolls_back()
    {
        Assert.True(LoadRefusalRollback.ShouldRestore(ReachabilityClass.DestinationNotConnected));
    }

    [Fact]
    public void Leaving_unconnected_position_rolls_back()
    {
        // This is the exact class that fell through with NO rollback at two of the three
        // call sites before Task 7's fix -- the defect this predicate exists to prevent from
        // silently recurring at a fourth site (or drifting back at these three).
        Assert.True(LoadRefusalRollback.ShouldRestore(ReachabilityClass.LeavingUnconnectedPosition));
    }
}

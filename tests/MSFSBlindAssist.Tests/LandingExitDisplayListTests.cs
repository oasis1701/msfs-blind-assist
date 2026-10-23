// Characterization tests for LandingExitDisplayList — the exit list the pilot reads.
//
// Regression pinned: KDTW 22L listed 57 exits (Y9 x4, Z7 x6, Z5 x8 …), every copy of a
// taxiway name resolving to the SAME ApronNodeId — one physical turnoff read out many times.
// The collapse is display-only; guidance keeps GetLandingExits' full list.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitDisplayListTests
{
    private static LandingExit Exit(string name, int node, int apron, string type, double distFt) => new()
    {
        TaxiwayName = name, NodeId = node, ApronNodeId = apron, ExitType = type,
        DistanceFromThresholdFeet = distFt,
    };

    [Fact]
    public void Same_name_same_apron_is_one_row_keeping_the_first_forward_entry()
    {
        // KDTW 22L Y9: End, End, Normal, End — all apron 2451.
        var exits = new List<LandingExit>
        {
            Exit("Y9", 2454, 2451, "End", 525),
            Exit("Y9", 2455, 2451, "End", 586),
            Exit("Y9", 2456, 2451, "Normal", 816),
            Exit("Y9", 2457, 2451, "End", 894),
        };

        var shown = LandingExitDisplayList.Collapse(exits);

        var y9 = Assert.Single(shown);
        Assert.Equal(2456, y9.NodeId);
    }

    [Fact]
    public void Group_with_no_forward_entry_keeps_its_threshold_nearest_row()
    {
        var exits = new List<LandingExit>
        {
            Exit("V", 1234, 1742, "End", 5736),
            Exit("V", 1235, 1742, "End", 5828),
        };

        Assert.Equal(1234, Assert.Single(LandingExitDisplayList.Collapse(exits)).NodeId);
    }

    [Fact]
    public void Same_name_with_different_apron_nodes_are_separate_turnoffs()
    {
        // EGLL 09R S5W at 5659 and 6605 ft; KDTW 22L R to the right AND to the left.
        var exits = new List<LandingExit>
        {
            Exit("S5W", 10, 100, "Normal", 5659),
            Exit("S5W", 20, 200, "Normal", 6605),
        };

        Assert.Equal(2, LandingExitDisplayList.Collapse(exits).Count);
    }

    [Fact]
    public void Unnamed_or_apronless_rows_are_always_shown_and_order_is_kept()
    {
        var exits = new List<LandingExit>
        {
            Exit("", 1, 50, "End", 100),
            Exit("A", 2, 2, "Normal", 200),     // apron == its own node: nothing to group on
            Exit("A", 3, -1, "Normal", 300),    // no apron at all
            Exit("B", 4, 60, "Normal", 400),
            Exit("B", 5, 60, "End", 500),
        };

        var shown = LandingExitDisplayList.Collapse(exits);

        Assert.Equal(new[] { 1, 2, 3, 4 }, shown.Select(e => e.NodeId));
        Assert.Equal(5, exits.Count);   // the input — guidance's list — is untouched
    }
}

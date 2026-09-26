// The Landing Exit planner's pre-selected exit (KMEM 36L: the list opens with M3, an 18R RET that is a
// 130-degree turnaround for 36L at 1,400 ft — it must never be the default).

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class LandingExitDefaultTests
{
    private static LandingExit E(double angle, bool vacates = true) => new() { ExitAngleDegrees = angle, VacatesRunway = vacates };

    [Fact]
    public void A_turnaround_is_skipped_for_the_first_takeable_exit()
        => Assert.Equal(2, LandingExitDefault.Index(new[] { E(130), E(130), E(73) }));

    [Fact]
    public void An_exit_that_does_not_get_clear_is_skipped()
        => Assert.Equal(1, LandingExitDefault.Index(new[] { E(90, vacates: false), E(30) }));

    [Fact]
    public void With_only_turnarounds_the_first_that_gets_clear_is_used()
        => Assert.Equal(1, LandingExitDefault.Index(new[] { E(130, vacates: false), E(130) }));

    [Fact]
    public void With_nothing_getting_clear_the_first_forward_exit_is_used()
        => Assert.Equal(1, LandingExitDefault.Index(new[] { E(130, vacates: false), E(90, vacates: false) }));

    [Fact]
    public void A_turnaround_that_gets_clear_never_beats_a_forward_exit()
        // M3 (a 130-degree turnaround that vacates) ahead of M5 (forward, flagged): the re-plan would guide
        // to M5, never to M3, so the planner does not pre-select M3 either.
        => Assert.Equal(1, LandingExitDefault.Index(new[] { E(130), E(90, vacates: false) }));

    [Fact]
    public void With_only_flagged_turnarounds_the_first_is_used()
        => Assert.Equal(0, LandingExitDefault.Index(new[] { E(130, vacates: false), E(130, vacates: false) }));

    private static LandingExit X(string name, int node, double distFt, double angle)
        => new() { TaxiwayName = name, NodeId = node, DistanceFromThresholdFeet = distFt, ExitAngleDegrees = angle };

    [Fact]
    public void The_pick_is_restored_by_its_node()
    {
        var pick = X("C", 30, 3281, 90);
        var rebuilt = new[] { X("B", 10, 1312, 90), X("C", 20, 2231, 130), X("C", 30, 3281, 90) };
        Assert.Equal(2, LandingExitDefault.RestoreIndex(rebuilt, pick));
    }

    [Fact]
    public void Without_its_node_the_pick_goes_to_the_nearest_same_named_exit_of_its_kind()
    {
        // The forward C was picked; the rebuilt list lists the C turnaround first. The first entry of the name
        // used to win, and moved the pick onto the turnaround with nothing said.
        var pick = X("C", 30, 3281, 90);
        var rebuilt = new[] { X("B", 10, 1312, 90), X("C", 20, 2231, 130), X("C", 31, 3300, 90) };
        Assert.Equal(2, LandingExitDefault.RestoreIndex(rebuilt, pick));
    }

    [Fact]
    public void A_pick_no_longer_offered_is_not_restored()
    {
        var pick = X("C", 30, 3281, 90);
        var rebuilt = new[] { X("B", 10, 1312, 90), X("C", 20, 2231, 130) };
        Assert.Equal(-1, LandingExitDefault.RestoreIndex(rebuilt, pick));
        Assert.Equal(-1, LandingExitDefault.RestoreIndex(rebuilt, null));
    }

    [Fact]
    public void An_empty_list_has_no_default()
        => Assert.Equal(-1, LandingExitDefault.Index(Array.Empty<LandingExit>()));
}

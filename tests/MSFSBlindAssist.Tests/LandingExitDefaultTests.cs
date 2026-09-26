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
    public void With_nothing_usable_the_first_is_used()
        => Assert.Equal(0, LandingExitDefault.Index(new[] { E(130, vacates: false), E(90, vacates: false) }));

    [Fact]
    public void An_empty_list_has_no_default()
        => Assert.Equal(-1, LandingExitDefault.Index(Array.Empty<LandingExit>()));
}

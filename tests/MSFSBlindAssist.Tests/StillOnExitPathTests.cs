// Characterization tests for LandingExitPathFollow.StillOnExitPath — the missed-exit hold-off
// for a pilot who is on the chosen exit's own path (owner ruling 2026-09-23: following the tone
// must never earn a false "Missed"). Shape: KSTL 30R E2 / KDTW 27L T5, exits that stay close to
// the runway for 100-250 m before leaving it.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class StillOnExitPathTests
{
    private const double D = 1.0 / 111132.0;   // degrees per metre (equator)

    // Runway heading east along lat 0; the exit path drifts left (north) slowly: 5 m off the
    // centreline 120 m past the junction, 30 m off at 300 m.
    private static readonly List<(double Lat, double Lon)> ShallowExit = new()
    {
        (0.0, 0.0), (5 * D, 120 * D), (12 * D, 200 * D), (30 * D, 300 * D),
    };

    [Fact]
    public void Pilot_on_the_shallow_exit_is_not_called_missed()
    {
        // 150 m past the junction, 7 m left — on the exit's path, heading essentially runway.
        Assert.True(LandingExitPathFollow.StillOnExitPath(ShallowExit, 7 * D, 150 * D, 150 / 0.3048, 100));
    }

    [Fact]
    public void Straight_roll_is_called_once_the_path_has_moved_away()
    {
        // Still on the centreline 300 m past the junction: the path is 30 m away.
        Assert.False(LandingExitPathFollow.StillOnExitPath(ShallowExit, 0.0, 300 * D, 300 / 0.3048, 100));
    }

    [Fact]
    public void Hold_off_is_bounded_even_on_the_path()
    {
        double past = 100 + LandingExitPathFollow.OnPathWindowFeet + 1;
        Assert.False(LandingExitPathFollow.StillOnExitPath(ShallowExit, 5 * D, 120 * D, past, 100));
    }

    [Fact]
    public void No_path_changes_nothing()
    {
        Assert.False(LandingExitPathFollow.StillOnExitPath(null, 0, 0, 200, 100));
    }
}

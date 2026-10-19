using MSFSBlindAssist.Services;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// When the Waypoint Flight Director may call a fix flown past by the abeam test alone.
///
/// XLS climbing out of VCBI, 2026-10-15: "direct MTL" engaged on runway heading 222 with MTL
/// 92 nm away on a bearing of 127 - 95 degrees off the track, so "past abeam" on the very first
/// frame - and the FD said "Flight director active. Tracking WP1." and "Final waypoint reached.
/// Flight director off." in the same breath. A direct-to issued while flying away from the fix
/// is the commonest ATC instruction there is; the FD has to turn the pilot round, not quit.
/// </summary>
public class WaypointFlightDirectorSequencingTests
{
    [Fact]
    public void ADirectToTheOnlyFixBehindTheWingIsTurnedTowardsNotPassed()
        => Assert.False(WaypointFlightDirectorManager.PassedByAbeam(
            moving: true, pastAbeam: true, isFinalFix: true, seenAhead: false));

    [Fact]
    public void TheFinalFixIsPassedOnceItHasBeenAhead()
        => Assert.True(WaypointFlightDirectorManager.PassedByAbeam(
            moving: true, pastAbeam: true, isFinalFix: true, seenAhead: true));

    [Fact]
    public void AnEarlierFixBehindIsStillSequencedPastAsBefore()
        // Engaging a route late, with fixes already flown and a later one ahead, still skips
        // them - that behaviour is why the abeam test exists.
        => Assert.True(WaypointFlightDirectorManager.PassedByAbeam(
            moving: true, pastAbeam: true, isFinalFix: false, seenAhead: false));

    [Fact]
    public void NothingIsPassedWhileStoppedOrAhead()
    {
        Assert.False(WaypointFlightDirectorManager.PassedByAbeam(false, true, false, true));
        Assert.False(WaypointFlightDirectorManager.PassedByAbeam(true, false, true, true));
    }
}

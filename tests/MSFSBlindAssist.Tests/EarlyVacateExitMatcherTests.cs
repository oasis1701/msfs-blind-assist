// Characterization tests for RolloutExitGate.MatchEarlyVacateExit — "which exit did the
// pilot actually turn onto?" when the handoff fires away from the planned one.
//
// Regression pinned: KSEA 34L → Z, 2026-08-21. The handoff re-routed to the PLANNED
// exit's node even though the aircraft had left the runway 2,232 ft short of it. With no
// runway edges in the taxi graph, A* produced the only route that exists between the two:
// 1,678 m up the east-side parallel taxiway T and back down Z toward the runway.
//
// signedAlongPast is POSITIVE when the aircraft is PAST that exit. Sign of the lateral
// argument is POSITIVE = right of the runway direction, matching ExitSide "Right".

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class EarlyVacateExitMatcherTests
{
    private static LandingExit Exit(int nodeId, string name, string side) => new LandingExit
    {
        NodeId = nodeId,
        TaxiwayName = name,
        ExitSide = side
    };

    // KSEA 34L, as flown. Planned exit Z; the aircraft vacated right, 810 ft past J's
    // throat. E is 800 ft AHEAD, beyond the 600 ft forward slack, so it is excluded even
    // though it is geometrically nearer in a straight line. N is 1,452 ft behind, beyond
    // the 1,400 ft cap.
    [Fact]
    public void Ksea34L_PicksTheLastExitActuallyPassed()
    {
        var q = Exit(1, "Q", "Right");
        var p = Exit(2, "P", "Right");
        var n = Exit(3, "N", "Right");
        var j = Exit(4, "J", "Right");
        var e = Exit(5, "E", "Right");
        var z = Exit(6, "Z", "Right");
        var all = new[] { q, p, n, j, e, z };

        var passed = new Dictionary<int, double>
        {
            [1] = 3550.0, [2] = 2625.0, [3] = 1452.0, [4] = 810.0, [5] = -800.0, [6] = -2232.0
        };

        var match = RolloutExitGate.MatchEarlyVacateExit(
            all, z, ex => passed[ex.NodeId], aircraftLateralSignedMetres: 51.2);

        Assert.Same(j, match);
    }

    // You cannot vacate at an exit you have not reached. An exit further ahead than the
    // forward slack is never a candidate.
    [Fact]
    public void AnExitStillAhead_IsNotACandidate()
    {
        var ahead = Exit(1, "E", "Right");
        var planned = Exit(2, "Z", "Right");
        var passedM = new Dictionary<int, double> { [1] = -800.0, [2] = -2232.0 };

        Assert.Null(RolloutExitGate.MatchEarlyVacateExit(
            new[] { ahead, planned }, planned, ex => passedM[ex.NodeId], 51.2));
    }

    // The forward slack exists because a hold-short-marker exit node can read forward of
    // the pavement junction the pilot actually turned at. 600 ft is the boundary.
    [Fact]
    public void ForwardSlackBoundaryIsSixHundredFeet()
    {
        var near = Exit(1, "J", "Right");
        var planned = Exit(2, "Z", "Right");

        Assert.Same(near, RolloutExitGate.MatchEarlyVacateExit(
            new[] { near, planned }, planned, _ => -600.0, 51.2));
        Assert.Null(RolloutExitGate.MatchEarlyVacateExit(
            new[] { near, planned }, planned, _ => -600.1, 51.2));
    }

    // Beyond 1,400 ft behind, an exit is no longer the same physical turnoff.
    [Fact]
    public void MaxPassedBoundaryIsFourteenHundredFeet()
    {
        var behind = Exit(1, "N", "Right");
        var planned = Exit(2, "Z", "Right");

        Assert.Same(behind, RolloutExitGate.MatchEarlyVacateExit(
            new[] { behind, planned }, planned, _ => 1400.0, 51.2));
        Assert.Null(RolloutExitGate.MatchEarlyVacateExit(
            new[] { behind, planned }, planned, _ => 1400.1, 51.2));
    }

    // A runway with exits on both sides: the side the aircraft actually moved to decides.
    [Fact]
    public void ExitsOnBothSides_TheAircraftsOwnSideWins()
    {
        var right = Exit(1, "J", "Right");
        var left = Exit(2, "K", "Left");
        var planned = Exit(3, "Z", "Right");
        var all = new[] { right, left, planned };
        var passedM = new Dictionary<int, double> { [1] = 810.0, [2] = 700.0, [3] = -2232.0 };

        Assert.Same(left, RolloutExitGate.MatchEarlyVacateExit(
            all, planned, ex => passedM[ex.NodeId], aircraftLateralSignedMetres: -51.2));
        Assert.Same(right, RolloutExitGate.MatchEarlyVacateExit(
            all, planned, ex => passedM[ex.NodeId], aircraftLateralSignedMetres: 51.2));
    }

    // A blank ExitSide (bearing unknown at graph-build time) must not be excluded —
    // it is ranked on distance alone. Excluding it would strand the pilot at exactly
    // the airports whose navdata is already thin.
    [Fact]
    public void BlankExitSide_IsRankedNotRejected()
    {
        var blank = Exit(1, "J", "");
        var planned = Exit(2, "Z", "Right");

        Assert.Same(blank, RolloutExitGate.MatchEarlyVacateExit(
            new[] { blank, planned }, planned, _ => 810.0, 51.2));
    }

    // The planned exit is never its own early-vacate match.
    [Fact]
    public void ThePlannedExit_IsNeverTheMatch()
    {
        var planned = Exit(1, "Z", "Right");

        Assert.Null(RolloutExitGate.MatchEarlyVacateExit(
            new[] { planned }, planned, _ => 100.0, 51.2));
    }

    // Nearest wins when several qualify — the last one reached, not the first in the list.
    [Fact]
    public void NearestQualifyingExitWins()
    {
        var far = Exit(1, "N", "Right");
        var near = Exit(2, "J", "Right");
        var planned = Exit(3, "Z", "Right");
        var passedM = new Dictionary<int, double> { [1] = 1300.0, [2] = 200.0, [3] = -2232.0 };

        Assert.Same(near, RolloutExitGate.MatchEarlyVacateExit(
            new[] { far, near, planned }, planned, ex => passedM[ex.NodeId], 51.2));
    }

    // Empty and null inputs degrade to "no match", which the caller turns into a spoken
    // closure rather than a route.
    [Fact]
    public void NoCandidates_ReturnsNull()
    {
        var planned = Exit(1, "Z", "Right");
        Assert.Null(RolloutExitGate.MatchEarlyVacateExit(
            Array.Empty<LandingExit>(), planned, _ => 100.0, 51.2));
    }
}

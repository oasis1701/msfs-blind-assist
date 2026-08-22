// Characterization tests for RolloutExitGate.IsVacateAwayFromPlannedExit — "did the pilot
// leave the runway somewhere OTHER than the exit they picked?"
//
// This is asked ONLY when the aircraft is already laterally clear of the pavement (the
// caller's own conjunct), and that is what makes the 350 ft threshold sound rather than
// tuned. GetLandingExits refuses any exit node more than halfWidth + 15 m off the axis,
// while "clear of the runway" is halfWidth + 10 m — so the node corridor extends exactly
// 5 m past the clear boundary. An aircraft off the pavement on its OWN exit, whose own
// track leaves the axis at angle theta, can therefore be at most 5 m / tan(theta) short of
// that exit's node: 313 ft at a 3-degree track, 61 ft at 15 degrees. Theta is the
// AIRCRAFT'S track angle, not an exit-angle constant — GetLandingExits enforces no minimum
// exit angle for hold-short-derived nodes. 350 rounds up that worst case, and the
// derivation alone is what carries it: no spacing floor between DISTINCT exits is claimed,
// the one real datum being the closest same-name pair kept, EGLL 09R S4E at 433 ft.
//
// signedAlongPastPlannedFeet is POSITIVE when the aircraft is PAST the exit, so "short of
// the exit" is NEGATIVE.
//
// The straight-line distances below are consistent with an aircraft ~150 ft off the axis —
// genuinely clear of a 200 ft runway, which is the precondition under which this rule is
// ever asked — and stay under TurnWindowFeet (1,000) so each case isolates the along-track
// clause. FarOffToTheSide_IsCaughtByTheStraightLineClause is the deliberate exception.

using MSFSBlindAssist.Navigation;

namespace MSFSBlindAssist.Tests;

public class EarlyVacateAlongTrackGateTests
{
    // The motivating case: exits ~800 ft apart on a parallel-taxiway layout, pilot vacates
    // at the neighbour. Before this rule the straight-line 1,000 ft gate said "not far
    // enough" and the handoff re-routed to the exit that was skipped.
    [Fact]
    public void VacateEightHundredFeetShort_IsAwayFromThePlannedExit()
    {
        Assert.True(RolloutExitGate.IsVacateAwayFromPlannedExit(
            pastPlannedExit: false,
            signedAlongPastPlannedFeet: -800.0,
            distToPlannedExitFeet: 814.0));   // 800 along + 150 lateral
    }

    // A genuine turn onto the PLANNED exit. Once laterally clear, a 15-degree exit's node
    // is at most 61 ft ahead — nowhere near 350 — so the branch must not be entered and the
    // planned exit is kept.
    [Fact]
    public void GenuineTurnOntoThePlannedExit_IsNotAwayFromIt()
    {
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(
            pastPlannedExit: false,
            signedAlongPastPlannedFeet: -61.0,
            distToPlannedExitFeet: 162.0));   // 61 along + 150 lateral
    }

    // The worst practical case: a 3-degree track off the axis, the shallowest worth
    // tabulating, puts the aircraft's own exit node at most 313 ft ahead. Inside the
    // threshold.
    [Fact]
    public void ShallowestAdmissibleExit_AtItsGeometricLimit_IsNotAwayFromIt()
    {
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(
            pastPlannedExit: false,
            signedAlongPastPlannedFeet: -313.0,
            distToPlannedExitFeet: 347.0));   // 313 along + 150 lateral
    }

    // Boundary, both sides. Asserted at the next representable double so a strict/inclusive
    // mutation is caught.
    [Fact]
    public void BoundaryIsThreeHundredAndFiftyFeetShort()
    {
        double atThreshold = -RolloutExitGate.VacatedShortAlongTrackFeet;

        // 350 along + 150 lateral, and well under TurnWindowFeet so only the along-track
        // clause can decide.
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(false, atThreshold, 381.0));
        Assert.True(RolloutExitGate.IsVacateAwayFromPlannedExit(
            false, Math.BitDecrement(atThreshold), 381.0));
    }

    // Past the exit short-circuits regardless of the other two arguments — the overshoot
    // detector owns that case, not the early-vacate retarget.
    [Fact]
    public void PastThePlannedExit_IsNeverAnEarlyVacate()
    {
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(true, -5000.0, 5000.0));
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(true, 900.0, 900.0));
    }

    // A POSITIVE along-track value means past the exit, and must never read as "short of"
    // it even when the caller has not set pastPlannedExit.
    [Fact]
    public void PositiveAlongTrack_NeverReadsAsShortOfTheExit()
    {
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(false, 800.0, 814.0));
    }

    // The straight-line clause is NOT redundant. An aircraft that has driven a long way off
    // the side reads a small along-track distance while being nowhere near the exit; the
    // 1,000 ft straight-line test still catches it.
    [Fact]
    public void FarOffToTheSide_IsCaughtByTheStraightLineClause()
    {
        Assert.True(RolloutExitGate.IsVacateAwayFromPlannedExit(
            pastPlannedExit: false,
            signedAlongPastPlannedFeet: -100.0,   // barely short along the runway
            distToPlannedExitFeet: 1200.0));      // but 1,200 ft away in a straight line
    }

    // Sanity: with both clauses false the answer is false, so the branch stays closed on an
    // ordinary near-exit handoff.
    [Fact]
    public void NearTheExit_IsNotAVacateAwayFromIt()
    {
        Assert.False(RolloutExitGate.IsVacateAwayFromPlannedExit(false, -20.0, 152.0));
    }
}

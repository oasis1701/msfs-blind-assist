using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// In a crosswind the lateral law steers the ground track all the way to touchdown, crab and all; it
/// never switches to the heading low down. Banking the nose straight turns the path with it, and the wind
/// then carries the aircraft off the centreline: on the first sim flight (2026-11-06, Fenix A320, KATL 27L,
/// a 1.8° crab) the old switch at 100 ft asked 1.4° of left bank, the aircraft drifted from 11 ft to 62 ft
/// left by touchdown and left the runway's side 3.6 s later. The decrab is the pilot's, with the rudder in
/// the flare (the owner, 2026-11-06).
/// </summary>
public class VisualGuidanceCrosswindTests
{
    [Theory]
    [InlineData(0.5)]    // about 220 ft above the threshold
    [InlineData(0.15)]   // about 110 ft
    [InlineData(0.1)]    // about 90 ft
    [InlineData(0.05)]   // about 75 ft
    public void A_crab_on_the_centreline_asks_no_bank_down_to_the_flare(double alongNm)
    {
        var rig = new VisualGuidanceTestRig();
        rig.Place(6.0, 0, 2, trackRelDeg: 0);
        rig.UpdateAt(0);
        rig.FlyFor(10, 30, _ => rig.Place(6.0, 0, 2, trackRelDeg: 0));
        Assert.Equal("Final", rig.Phase);

        // Nose 2° right of the path into a wind from the right, the track straight down the centreline.
        rig.FlyFor(3, 30, _ => rig.Place(alongNm, 0, 2, trackRelDeg: 0));
        Assert.InRange(rig.Bank, -0.3, 0.3);
    }
}

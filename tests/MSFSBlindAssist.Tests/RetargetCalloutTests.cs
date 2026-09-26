// The one utterance a landing-exit retarget speaks (KMEM 36L 2026-09-26: "Missed taxiway M6.
// Retargeting taxiway M7, 650 feet ahead." was cut off after 65 ms by a stale "Taxiway M7, 900 feet.").

using MSFSBlindAssist.Navigation;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

[Collection("DistanceUnitGlobalState")]
public class RetargetCalloutTests
{
    [Fact]
    public void A_miss_with_no_leftover_turn_is_worded_as_before()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("Missed taxiway M6. Retargeting taxiway M7, 650 feet ahead.",
            RetargetCallout.Compose(RetargetReason.Missed, "M6", "M7", 631, straighten: false, slowDown: false));
    }

    [Fact]
    public void Kmem_a_miss_while_still_turning_says_straighten()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("Missed taxiway M6. Straighten. Retargeting taxiway M7, 650 feet ahead.",
            RetargetCallout.Compose(RetargetReason.Missed, "M6", "M7", 631, straighten: true, slowDown: false));
    }

    [Fact]
    public void Too_fast_names_the_exit_to_continue_to()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("Too fast for taxiway M6. Continue to taxiway M7, 900 feet.",
            RetargetCallout.Compose(RetargetReason.TooFast, "M6", "M7", 880, straighten: false, slowDown: false));
        Assert.Equal("Too fast for taxiway M6. Straighten. Continue to taxiway M7, 900 feet. Slow down.",
            RetargetCallout.Compose(RetargetReason.TooFast, "M6", "M7", 880, straighten: true, slowDown: true));
    }

    [Fact]
    public void An_earlier_exit_keeps_its_wording_and_never_straightens()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("Taking earlier exit, taxiway M5, 800 feet ahead.",
            RetargetCallout.Compose(RetargetReason.Earlier, "M7", "M5", 800, straighten: true, slowDown: false));
        Assert.Equal("Taking earlier exit, earlier exit, 800 feet ahead.",
            RetargetCallout.Compose(RetargetReason.Earlier, "M7", "", 800, straighten: false, slowDown: false));
    }

    [Fact]
    public void Unnamed_exits_read_naturally()
    {
        DistanceFormatter.UnitProvider = () => DistanceUnit.Feet;
        Assert.Equal("Missed exit. Retargeting next exit, 500 feet ahead.",
            RetargetCallout.Compose(RetargetReason.Missed, "", null, 500, straighten: false, slowDown: false));
    }

    [Fact]
    public void Too_fast_with_nothing_ahead_never_says_turn()
    {
        Assert.Equal("Taxiway M9, too fast to turn. Slow down.", RetargetCallout.ComposeTooFastNoExit("M9"));
        Assert.Equal("Exit, too fast to turn. Slow down.", RetargetCallout.ComposeTooFastNoExit(""));
    }

    [Fact]
    public void Kmem_the_retarget_retires_the_900_and_500_callouts_but_never_turn_now()
    {
        // 631 ft to M7 at 48.1 kt, M7 high-speed 22.7 degrees: the stale "900 feet" and the
        // "500 feet" would both come due while the sentence is still being spoken.
        var r = TouchdownCallout.RetireExitCallouts(631, 48.1, "High-speed", RetargetCallout.LeadSeconds,
            1500, 900, 500, 150, RolloutExitGate.MaxTurnSpeedKts(22.7));
        Assert.True(r.Retire1500);
        Assert.True(r.Retire900);
        Assert.True(r.Retire500);
        Assert.False(r.RetireTurnNow);
        Assert.False(r.SlowDown);   // 48 kt is fine for a 23-degree exit
    }
}

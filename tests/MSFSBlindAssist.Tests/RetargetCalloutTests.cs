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
    public void No_reachable_exit_is_one_sentence_and_a_too_fast_call_never_says_missed()
    {
        Assert.Equal("Missed taxiway M6. No reachable exit remaining.",
            RetargetCallout.ComposeNoReachableExit(RetargetReason.Missed, "M6"));
        Assert.Equal("Too fast for taxiway M6. No reachable exit remaining.",
            RetargetCallout.ComposeNoReachableExit(RetargetReason.TooFast, "M6"));
        Assert.Equal("Missed exit. No reachable exit remaining.",
            RetargetCallout.ComposeNoReachableExit(RetargetReason.Missed, ""));
    }

    [Theory]
    // An earlier-exit fall-forward that reaches the planned exit (or passes it) stays on it, silently.
    [InlineData(RetargetReason.Earlier, 5000, 5000, true)]
    [InlineData(RetargetReason.Earlier, 5600, 5000, true)]
    // One still short of the planned exit is another earlier exit: announced as one.
    [InlineData(RetargetReason.Earlier, 4400, 5000, false)]
    // A miss or a too-fast call is already past, or declining, the exit it had.
    [InlineData(RetargetReason.Missed, 5600, 5000, false)]
    [InlineData(RetargetReason.TooFast, 5600, 5000, false)]
    public void Only_an_earlier_exit_retarget_stays_on_the_planned_exit(
        RetargetReason reason, double candidateFt, double plannedFt, bool stays)
        => Assert.Equal(stays, RetargetCallout.StaysOnPlannedExit(reason, candidateFt, plannedFt));

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
        var r = RetargetCallout.Retire(RetargetReason.Missed, straighten: true, 631, 48.1, "High-speed",
            1500, 900, 500, 150, RolloutExitGate.MaxTurnSpeedKts(22.7));
        Assert.True(r.Retire1500);
        Assert.True(r.Retire900);
        Assert.True(r.Retire500);
        Assert.False(r.RetireTurnNow);
        Assert.False(r.SlowDown);   // 48 kt is fine for a 23-degree exit
    }

    [Theory]
    // Measured worst sentence durations (System.Speech Rate 0, every distance to 3,550 ft) plus a fifth,
    // rounded up to the half second - see RetargetCallout.LeadSecondsFor.
    [InlineData(RetargetReason.TooFast, true, true, 13.5)]
    [InlineData(RetargetReason.TooFast, true, false, 11.5)]
    [InlineData(RetargetReason.TooFast, false, true, 11.5)]
    [InlineData(RetargetReason.TooFast, false, false, 9.5)]
    [InlineData(RetargetReason.Missed, true, true, 13.0)]
    [InlineData(RetargetReason.Missed, true, false, 11.5)]
    [InlineData(RetargetReason.Missed, false, true, 11.5)]
    [InlineData(RetargetReason.Missed, false, false, 9.5)]
    [InlineData(RetargetReason.Earlier, false, true, 9.5)]
    [InlineData(RetargetReason.Earlier, false, false, 8.0)]
    public void Each_sentence_has_its_own_measured_lead(RetargetReason reason, bool straighten, bool slowDown, double lead)
        => Assert.Equal(lead, RetargetCallout.LeadSecondsFor(reason, straighten, slowDown));

    [Fact]
    public void A_short_earlier_exit_sentence_leaves_the_new_exits_500_ft_call_to_speak()
    {
        // "Taking earlier exit, taxiway A5, 1000 feet ahead." at 30 kt: A5's 500 ft point is 11 s away and the
        // sentence lasts about 6. The one 13 s lead retired that call, and the pilot heard nothing more until
        // the turn.
        var r = RetargetCallout.Retire(RetargetReason.Earlier, straighten: false, 1000, 30.0, "Normal",
            1500, 900, 500, 150, RolloutExitGate.SlowDownAboveKts(90.0, "Normal"));
        Assert.True(r.Retire1500);
        Assert.False(r.Retire500);
        Assert.False(r.SlowDown);
    }

    [Fact]
    public void A_sentence_that_folds_slow_down_is_judged_with_its_longer_lead()
    {
        // Too fast, 900 ft to a 90-degree exit at 45 kt: the sentence without "Slow down." already retires the
        // 500 ft call (above its 30 kt line), so "Slow down." folds in - and the call stays retired under the
        // longer sentence's lead.
        var r = RetargetCallout.Retire(RetargetReason.TooFast, straighten: true, 900, 45.0, "Normal",
            1500, 900, 500, 150, RolloutExitGate.SlowDownAboveKts(90.0, "Normal"));
        Assert.True(r.Retire500);
        Assert.True(r.SlowDown);
    }
}

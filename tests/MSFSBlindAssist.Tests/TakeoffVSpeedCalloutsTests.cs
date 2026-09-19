// Characterization tests for the takeoff V-speed callout state machine
// (Aircraft/TakeoffVSpeedCallouts.cs, shared by the iFly 737 MAX8 and the TFDi
// MD-11) — the spoken "V1" / "Rotate" / "V2" calls fed from a high-frequency
// AIRSPEED INDICATED subscription.
//
// The safety-shaped contracts pinned here:
// - arming requires ground + slow (< 40 kt) + V1 and VR set, so a mid-roll or
//   mid-flight connect stays silent and a landing rollout can NEVER fire;
// - crossings are upward-edge-only and fire exactly once per roll;
// - a rejected takeoff re-arms for the next attempt;
// - "V1"/"Rotate" are ground calls, "V2" may complete just after liftoff.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class TakeoffVSpeedCalloutsTests
{
    private static TakeoffVSpeedCallouts NewArmed(double v1 = 140, double vr = 144, double v2 = 150)
    {
        var t = new TakeoffVSpeedCallouts();
        t.SetV1(v1);
        t.SetVR(vr);
        t.SetV2(v2);
        Assert.Empty(t.ProcessSample(0, onGround: true));   // arming sample
        return t;
    }

    private static List<string> Roll(TakeoffVSpeedCallouts t, bool onGround, params double[] samples)
    {
        var all = new List<string>();
        foreach (double ias in samples)
            all.AddRange(t.ProcessSample(ias, onGround));
        return all;
    }

    [Fact]
    public void Nominal_takeoff_calls_v1_rotate_on_the_roll_and_v2_after_liftoff()
    {
        var t = NewArmed(v1: 140, vr: 144, v2: 150);

        Assert.Equal(new[] { "V1" }, Roll(t, onGround: true, 60, 100, 139.9, 140.0));
        Assert.Equal(new[] { "Rotate" }, Roll(t, onGround: true, 143, 144.2));
        // Lifts off between VR and V2; V2 completes airborne.
        Assert.Equal(new[] { "V2" }, Roll(t, onGround: false, 147, 151));

        // Climb-out and the whole rest of the flight stay silent.
        Assert.Empty(Roll(t, onGround: false, 180, 250, 210));
    }

    [Fact]
    public void Long_flat_roll_calls_v2_on_the_ground_too()
    {
        var t = NewArmed(v1: 140, vr: 144, v2: 150);
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, Roll(t, onGround: true, 100, 141, 145, 151));
    }

    [Fact]
    public void One_sample_gap_across_two_thresholds_speaks_both_in_order()
    {
        var t = NewArmed(v1: 140, vr: 144, v2: 150);
        Assert.Equal(new[] { "V1", "Rotate" }, Roll(t, onGround: true, 120, 138, 146));
    }

    [Fact]
    public void Jitter_around_a_threshold_fires_once()
    {
        var t = NewArmed(v1: 140, vr: 160, v2: 170);
        Assert.Equal(new[] { "V1" }, Roll(t, onGround: true, 139, 140.2, 139.8, 140.3, 141));
    }

    [Fact]
    public void Never_armed_at_speed_so_landing_rollout_stays_silent()
    {
        var t = new TakeoffVSpeedCallouts();
        t.SetV1(140);
        t.SetVR(144);
        t.SetV2(150);

        // Approach + touchdown at speed: the first ground sample is fast, so the
        // machine never arms; the decel crossings are downward anyway.
        Assert.Empty(Roll(t, onGround: false, 180, 160, 145));
        Assert.Empty(Roll(t, onGround: true, 138, 120, 80, 45));

        // Below 40 kt it re-arms silently; taxi speeds never reach a threshold.
        Assert.Empty(Roll(t, onGround: true, 35, 12, 25, 30));
    }

    [Fact]
    public void Connect_mid_roll_stays_silent_for_that_departure()
    {
        var t = new TakeoffVSpeedCallouts();
        t.SetV1(140);
        t.SetVR(144);
        t.SetV2(150);

        // First sample already at 120 kt on the roll: never arms, no calls even
        // as the thresholds are crossed.
        Assert.Empty(Roll(t, onGround: true, 120, 141, 146, 152));
    }

    [Fact]
    public void Rejected_takeoff_rearms_for_the_next_attempt()
    {
        var t = NewArmed(v1: 140, vr: 144, v2: 150);

        Assert.Equal(new[] { "V1" }, Roll(t, onGround: true, 100, 141));
        // Reject: decelerate on the runway. Downward crossings are silent.
        Assert.Empty(Roll(t, onGround: true, 120, 80, 42));
        // Below 40 the flags reset; the second attempt calls everything again.
        Assert.Empty(Roll(t, onGround: true, 30, 15));
        Assert.Equal(new[] { "V1", "Rotate" }, Roll(t, onGround: true, 90, 142, 145));
    }

    [Fact]
    public void Momentary_bounce_during_the_roll_keeps_the_remaining_calls()
    {
        var t = NewArmed(v1: 140, vr: 144, v2: 150);
        Assert.Equal(new[] { "V1" }, Roll(t, onGround: true, 100, 140.5));
        // A bump flicks SIM ON GROUND off below V2 — still armed.
        Assert.Empty(Roll(t, onGround: false, 141.5));
        Assert.Equal(new[] { "Rotate" }, Roll(t, onGround: true, 144.5));
        Assert.Equal(new[] { "V2" }, Roll(t, onGround: false, 151));
    }

    [Fact]
    public void No_v2_set_still_calls_v1_and_rotate_then_finishes_at_liftoff()
    {
        var t = NewArmed(v1: 140, vr: 144, v2: 0);
        Assert.Equal(new[] { "V1", "Rotate" }, Roll(t, onGround: true, 100, 141, 145));
        // Airborne with nothing left to call: disarmed; later samples silent.
        Assert.Empty(Roll(t, onGround: false, 155, 200));
        Assert.Empty(Roll(t, onGround: true, 130));   // (hypothetical fast ground sample)
    }

    [Fact]
    public void Unset_speeds_never_arm_and_clearing_speeds_mid_roll_disarms()
    {
        var t = new TakeoffVSpeedCallouts();
        Assert.Empty(Roll(t, onGround: true, 0, 100, 150, 200));

        var u = NewArmed(v1: 140, vr: 144, v2: 150);
        Assert.Empty(Roll(u, onGround: true, 100, 120));
        u.SetV1(0);   // FMC route wipe mid-roll
        Assert.Empty(Roll(u, onGround: true, 141, 146, 152));
    }

    [Fact]
    public void Threshold_lowered_below_current_speed_mid_roll_skips_silently()
    {
        var t = NewArmed(v1: 140, vr: 150, v2: 160);
        Assert.Empty(Roll(t, onGround: true, 100, 130));
        // FMC drops V1 to 120 while already doing 130: no upward crossing will
        // ever happen for it — silent skip, the rest still calls.
        t.SetV1(120);
        Assert.Equal(new[] { "Rotate" }, Roll(t, onGround: true, 151));
    }

    [Fact]
    public void Speeds_below_the_arm_threshold_are_treated_as_unset()
    {
        var t = new TakeoffVSpeedCallouts();
        t.SetV1(20);   // garbage — below the 40 kt arm band
        t.SetVR(25);
        t.SetV2(30);
        Assert.Empty(Roll(t, onGround: true, 5, 15, 22, 27, 35, 45));
    }

    [Fact]
    public void Ifly_minus_one_unset_sentinel_never_arms()
    {
        // The iFly WASM publishes -1 (not 0) for a V-speed the FMC hasn't
        // computed — live-verified 2026-07-24 on a loaded MAX8.
        var t = new TakeoffVSpeedCallouts();
        t.SetV1(-1);
        t.SetVR(-1);
        t.SetV2(-1);
        Assert.Empty(Roll(t, onGround: true, 5, 30, 80, 150));
    }

    [Fact]
    public void Invalid_airspeed_samples_are_ignored()
    {
        var t = NewArmed(v1: 140, vr: 144, v2: 150);
        Assert.Empty(t.ProcessSample(double.NaN, onGround: true));
        Assert.Empty(t.ProcessSample(-5, onGround: true));
        // The NaN/negative samples must not have corrupted the last-sample edge.
        Assert.Equal(new[] { "V1" }, Roll(t, onGround: true, 120, 141));
    }
    /// <summary>
    /// A SimConnect reconnect resets the machine's ROLL but keeps its SPEEDS. An arm from before
    /// the drop must not survive into a later landing: after Reset nothing fires until a fresh arm
    /// on the ground below 40 kt — a landing's decelerating rollout never fires, a roll straight
    /// from the drop without that arming sample stays silent, and the next take-off then speaks
    /// without the speeds having to be fed again (a reconnect does not reliably redeliver them;
    /// a machine that forgot them was silent for the rest of the session).
    /// </summary>
    [Fact]
    public void Reset_DisarmsButKeepsTheSpeeds_UntilAFreshArm()
    {
        var t = NewArmed();
        t.Reset();
        Assert.Empty(Roll(t, onGround: true, 100, 141, 145, 151));   // not armed: nothing fires, speeds or no speeds
        Assert.Empty(t.ProcessSample(160, onGround: false));         // a landing: airborne and fast…
        Assert.Empty(Roll(t, onGround: true, 152, 146, 141, 100));   // …then decelerating through every speed
        Assert.Empty(t.ProcessSample(5, onGround: true));            // the fresh arm, with the kept speeds
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, Roll(t, onGround: true, 100, 141, 145, 151));
    }

    // ---- One utterance per sample (Compose) ---------------------------------------------
    // Both definitions speak the roll calls with AnnounceImmediate, which INTERRUPTS: spoken one
    // by one, the second call of a sample cut the first off — "V1" by "Rotate" on every take-off
    // with V1 = VR, routine on a limiting runway.

    [Fact]
    public void V1_equal_to_VR_crosses_both_on_one_sample_and_composes_one_sentence()
    {
        var t = NewArmed(v1: 144, vr: 144, v2: 150);
        Assert.Empty(t.ProcessSample(120, onGround: true));
        var crossed = t.ProcessSample(144.5, onGround: true);
        Assert.Equal(new[] { "V1", "Rotate" }, crossed);
        Assert.Equal("V1, Rotate", TakeoffVSpeedCallouts.Compose(crossed, _ => false));
    }

    [Fact]
    public void A_sample_gap_across_all_three_thresholds_composes_all_three_in_order()
    {
        var t = NewArmed(v1: 140, vr: 144, v2: 150);
        Assert.Empty(t.ProcessSample(120, onGround: true));
        var crossed = t.ProcessSample(152, onGround: true);
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, crossed);
        Assert.Equal("V1, Rotate, V2", TakeoffVSpeedCallouts.Compose(crossed, _ => false));
    }

    [Theory]
    [InlineData("V1", "Rotate, V2")]
    [InlineData("Rotate", "V1, V2")]
    [InlineData("V2", "V1, Rotate")]
    public void Compose_leaves_out_only_the_muted_call(string mutedCall, string expected)
        => Assert.Equal(expected, TakeoffVSpeedCallouts.Compose(new[] { "V1", "Rotate", "V2" }, c => c == mutedCall));

    [Fact]
    public void Compose_says_nothing_when_every_call_is_muted_or_none_was_crossed()
    {
        Assert.Null(TakeoffVSpeedCallouts.Compose(new[] { "V1", "Rotate", "V2" }, _ => true));
        Assert.Null(TakeoffVSpeedCallouts.Compose(Array.Empty<string>(), _ => false));
        Assert.Equal("V2", TakeoffVSpeedCallouts.Compose(new[] { "V2" }, _ => false));   // a single call is just itself
    }

    /// <summary>
    /// A flight loaded into the cruise from a parked, armed aircraft: the per-frame airspeed lands
    /// before the 1 Hz SIM_ON_GROUND, so the first cruise sample still carries the parked ground
    /// flag. Still armed, that one sample crosses every speed on the "ground"; reset (the MD-11's
    /// context reset now does it), it is silent — and so is the flight that follows.
    /// </summary>
    [Fact]
    public void A_cruise_sample_with_a_stale_ground_flag_fires_everything_while_armed_and_nothing_after_a_reset()
    {
        var armed = NewArmed(v1: 150, vr: 155, v2: 162);
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, armed.ProcessSample(280, onGround: true));   // why the reset must happen

        var reset = NewArmed(v1: 150, vr: 155, v2: 162);
        reset.Reset();
        Assert.Empty(reset.ProcessSample(280, onGround: true));    // the stale ground flag
        Assert.Empty(reset.ProcessSample(281, onGround: false));   // SIM_ON_GROUND catches up
        Assert.Empty(reset.ProcessSample(240, onGround: false));   // the rest of the flight
    }
}

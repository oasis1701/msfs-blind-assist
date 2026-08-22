// The RPN string SendEvent hands to the MobiFlight command channel for an H: or dotted event.
//
// The channel DEDUPS: two consecutive byte-identical command strings fire once. Four places in
// this codebase already work around it with a leading "<seq> 0 *" (which pushes seq, pushes 0,
// multiplies to an inert 0 that is left on the stack and discarded) — the A320 and A380 SD-page
// writes, the A380 RMP keypresses, and the A380 seat-motor ramp. SendEvent did not, so every
// TOGGLE button reached through it could be switched on and never off again: "on" and "off" are
// the same event, hence the same string, and the second press was swallowed.

using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class CalcEventCodeTests
{
    // The bug itself. A pilot sets an EFIS filter On, then Off: both picks fire the one
    // A32NX.FCU_EFIS_L_WPT_PUSH toggle event, so without a per-call discriminator the Off is
    // dropped and the filter can never be turned back off.
    [Fact]
    public void Repeated_presses_of_one_event_produce_distinct_command_strings()
    {
        string first = SimConnectManager.BuildCalcEventCode("A32NX.FCU_EFIS_L_WPT_PUSH", 0, seq: 1);
        string second = SimConnectManager.BuildCalcEventCode("A32NX.FCU_EFIS_L_WPT_PUSH", 0, seq: 2);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_dotted_event_still_fires_the_k_event_with_its_data()
    {
        Assert.Equal("1 0 * 0 (>K:A32NX.FCU_LOC_PUSH)",
            SimConnectManager.BuildCalcEventCode("A32NX.FCU_LOC_PUSH", 0, seq: 1));
    }

    // The data parameter is a real argument for some events, so it must survive the prefix and
    // stay the value (>K:) pops — the inert 0 sits below it on the stack.
    [Fact]
    public void A_dotted_event_carries_a_non_zero_data_parameter_through()
    {
        Assert.Equal("4 0 * 3 (>K:SOME.EVENT)",
            SimConnectManager.BuildCalcEventCode("SOME.EVENT", 3, seq: 4));
    }

    // H: events take no value, so the prefix is the only thing on the stack.
    [Fact]
    public void An_h_event_keeps_its_no_argument_form()
    {
        Assert.Equal("2 0 * (>H:A32NX.SOME_H_EVENT)",
            SimConnectManager.BuildCalcEventCode("H:A32NX.SOME_H_EVENT", 0, seq: 2));
    }

    // ---- FBW FCU events bypass the calc-path PROBE -----------------------------------------
    //
    // SendEvent only uses the calculator path once the MSFSBA_BRIDGE_PROBE round-trip has
    // verified it, and otherwise falls back to TransmitClientEvent. The FlyByWire FCU does not
    // receive that fallback — it consumes A32NX.FCU_* strictly as calculator K-events.
    //
    // Measured on a live machine 2026-08-22: the probe writes its nonce fine (the L:var held the
    // exact value) but the read-back never arrives, so the path is never verified and every
    // A32NX.FCU_* routed through SendEvent silently went nowhere — reported as "the FCU won't
    // accept". FireFCUButton had always sidestepped this by calling ExecuteCalculatorCode
    // directly, which is why the knob buttons worked while the combos did not.
    [Theory]
    [InlineData("A32NX.FCU_EFIS_L_BARO_PUSH")]
    [InlineData("A32NX.FCU_LOC_PUSH")]
    [InlineData("A32NX.FCU_EFIS_R_NDB_PUSH")]
    public void Fbw_fcu_events_are_routed_around_the_probe(string eventName)
    {
        Assert.True(SimConnectManager.IsFbwFcuEvent(eventName));
    }

    [Theory]
    [InlineData("AUTO_THROTTLE_ARM")]     // stock event — the legacy transport is correct
    [InlineData("KOHLSMAN_SET")]
    [InlineData("A32NX.SOMETHING_ELSE")]  // dotted but not an FCU button; leave it gated
    public void Everything_else_keeps_the_normal_routing(string eventName)
    {
        Assert.False(SimConnectManager.IsFbwFcuEvent(eventName));
    }
}

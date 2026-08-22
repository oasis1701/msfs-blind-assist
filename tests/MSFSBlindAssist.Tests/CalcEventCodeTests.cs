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
}

// The A380's approach-minimum read-outs and call-outs (A380Minimums).
//
// Two A380 facts the shared ApproachMinimums decode does not carry:
//  - a DH of 0 is NO DH: the MFD's RadioAltitudeFormat parses "NO"/"NODH" to
//    RADIO_ALTITUDE_NODH_VALUE = 0 and rejects a typed 0 (minimum 1), and the PFD shows NO DH
//    for it (FBW DataEntryFormats.tsx, FmcAircraftInterface.ts). On the A32NX a typed 0 is a
//    valid entry, so the shared decode keeps calling it "0 feet".
//  - since FBW #10929 (76db0aef1) the L:vars mirror the active plan, so the FMS's own clear of
//    both minimums on an approach change (FlightPlan.setApproach, #10724) now reaches them.
//    That is a change the pilot did not make, so it is spoken.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class A380MinimumsTests
{
    private const int NothingSeen = -2;

    [Fact]
    public void A_decision_height_of_zero_reads_no_dh()
    {
        Assert.Equal("No DH", A380Minimums.DisplayText(isDecisionHeight: true, 0.0));
        Assert.Equal("No DH", A380Minimums.Announcement(isDecisionHeight: true, previousFeet: -1, feet: 0));
    }

    [Theory]
    [InlineData(true, 200.0, "200 feet")]
    [InlineData(true, -1.0, "Not set")]
    [InlineData(false, 0.0, "Not set")]
    [InlineData(false, 940.0, "940 feet")]
    public void Other_values_read_as_the_shared_decode_does(bool dh, double value, string expected)
    {
        Assert.Equal(expected, A380Minimums.DisplayText(dh, value));
    }

    [Theory]
    [InlineData(false, -1, 940, "Baro minimum 940 feet")]
    [InlineData(true, -1, 200, "Decision height 200 feet")]
    [InlineData(true, 200, 250, "Decision height 250 feet")]
    [InlineData(false, NothingSeen, 940, "Baro minimum 940 feet")]   // a set minimum speaks on connect
    public void A_set_or_changed_minimum_is_spoken(bool dh, int previous, int feet, string expected)
    {
        Assert.Equal(expected, A380Minimums.Announcement(dh, previous, feet));
    }

    [Theory]
    [InlineData(false, 940, "Baro minimum cleared")]
    [InlineData(true, 200, "Decision height cleared")]
    [InlineData(true, 0, "Decision height cleared")]   // NO DH cleared
    public void A_cleared_minimum_is_spoken(bool dh, int previous, string expected)
    {
        Assert.Equal(expected, A380Minimums.Announcement(dh, previous, feet: -1));
    }

    [Theory]
    [InlineData(false, NothingSeen)]   // nothing set at connect
    [InlineData(true, -1)]             // still unset
    public void An_unset_minimum_that_was_never_set_is_silent(bool dh, int previous)
    {
        Assert.Null(A380Minimums.Announcement(dh, previous, feet: -1));
    }

    // A new flight (or a reconnect) starts the MFD over, and FBW's initSimVars writes MDA 0 and
    // DH -1: the first read after a context reset is the new situation, not a clear to narrate.
    [Theory]
    [InlineData(false, 940)]
    [InlineData(true, 0)]
    public void The_first_read_after_a_context_reset_does_not_speak_a_clear(bool dh, int previous)
    {
        Assert.Null(A380Minimums.Announcement(dh, previous, feet: -1, reseeding: true));
    }

    [Fact]
    public void The_first_read_after_a_context_reset_still_speaks_a_new_minimum()
    {
        Assert.Equal("Baro minimum 500 feet",
            A380Minimums.Announcement(isDecisionHeight: false, previousFeet: 940, feet: 500, reseeding: true));
    }

    [Fact]
    public void An_unchanged_minimum_is_silent()
    {
        Assert.Null(A380Minimums.Announcement(isDecisionHeight: false, previousFeet: 940, feet: 940));
    }
}

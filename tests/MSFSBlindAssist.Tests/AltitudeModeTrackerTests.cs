// The A380 FCU altitude-mode call-out's SEQUENCING, separate from the managed/selected
// RULE (which AltitudeManagedStateTests pins). Everything here is about WHEN the app
// speaks: baseline-first, readiness, the autoland transient, and re-baselining across a
// SimConnect reconnect.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class AltitudeModeTrackerTests
{
    // Vertical modes: 0 None, 12 OP CLB (selected), 22 CLB (managed), 40 SRS.
    // Armed bits: 4 = CLB armed (managed), 0 = nothing armed.

    private static AltitudeModeTracker Ready(int vertical = 12, int armed = 0)
    {
        var t = new AltitudeModeTracker();
        t.OnVerticalMode(vertical);
        t.OnVerticalArmed(armed);
        return t;
    }

    [Fact]
    public void Nothing_is_known_until_both_primary_inputs_have_reported()
    {
        var t = new AltitudeModeTracker();
        Assert.False(t.IsKnown);
        t.OnVerticalMode(12);
        Assert.False(t.IsKnown);   // armed bitmask still missing
        t.OnVerticalArmed(0);
        Assert.True(t.IsKnown);
    }

    [Fact]
    public void The_first_reading_is_a_silent_baseline()
    {
        var t = new AltitudeModeTracker();
        Assert.Null(t.OnVerticalMode(12));
        Assert.Null(t.OnVerticalArmed(0));
    }

    [Fact]
    public void A_genuine_change_speaks_once()
    {
        var t = Ready(vertical: 12, armed: 0);
        Assert.Equal("Altitude Mode: Managed", t.OnVerticalMode(22));
        Assert.Null(t.OnVerticalMode(22));      // unchanged, silent
        Assert.Equal("Altitude Mode: Selected", t.OnVerticalMode(12));
    }

    [Fact]
    public void Arming_a_managed_mode_flips_the_state_from_the_armed_input()
    {
        var t = Ready(vertical: 12, armed: 0);
        Assert.Equal("Altitude Mode: Managed", t.OnVerticalArmed(4));
    }

    [Fact]
    public void No_vertical_mode_is_silent_and_does_not_strand_the_baseline()
    {
        // Cold and dark: vertical mode 0. The state must still be answerable, and the
        // FIRST real mode afterwards must not announce a change that happened while
        // nothing was flying the aircraft vertically.
        var t = new AltitudeModeTracker();
        t.OnVerticalMode(0);
        t.OnVerticalArmed(0);
        Assert.Null(t.OnVerticalMode(0));
        Assert.False(t.IsManaged);
    }

    [Fact]
    public void The_autoland_vertical_mode_dropout_never_announces_selected()
    {
        // The #10855 shim files LAND/FLARE/ROLL OUT under the LATERAL mode and leaves the
        // vertical mode at None, so the flare must stay Managed in BOTH arrival orders.
        var t = Ready(vertical: 31, armed: 16);       // G/S track, G/S armed
        t.OnLateralMode(31);                          // LOC track
        Assert.True(t.IsManaged);

        t.OnVerticalMode(0);                          // vertical drops FIRST
        Assert.True(t.IsManaged);                     // must NOT read Selected mid-flare
        Assert.Null(t.OnLateralMode(32));             // LAND arrives second — silent
        Assert.True(t.IsManaged);
    }

    [Fact]
    public void IsManaged_is_preserved_not_recomputed_while_vertical_mode_is_none()
    {
        // The armed bitmask must stay 0 throughout, so the ONLY thing keeping IsManaged true
        // across the dropout is the preservation itself. With a managed bit armed (as the
        // sibling test has) IsManaged reads true unconditionally and the assertion proves
        // nothing — moving `IsManaged = managed;` above the vertical==0 early return would
        // still pass it.
        var t = Ready(vertical: 22, armed: 0);   // CLB: managed by MODE alone, nothing armed
        Assert.True(t.IsManaged);

        Assert.Null(t.OnVerticalMode(0));        // the dropout
        Assert.True(t.IsManaged);                // PRESERVED, not recomputed to false
    }

    [Fact]
    public void Reset_re_baselines_so_flight_two_does_not_inherit_flight_one()
    {
        // Land managed, reconnect at a cold gate, take off again: the first real mode of
        // flight 2 must be a silent baseline, not a change against flight 1's value.
        var t = Ready(vertical: 22, armed: 4);        // managed
        Assert.True(t.IsManaged);

        t.Reset();
        Assert.False(t.IsKnown);
        Assert.Null(t.OnVerticalMode(0));             // cold gate
        Assert.Null(t.OnVerticalArmed(0));

        // Must stay UNMANAGED (SRS, not e.g. CLB): flight 1 ended managed (_spoken == true),
        // so a reading that would itself be unmanaged is the one that DIVERGES if Reset()
        // failed to clear that stale value — a managed reading here would coincidentally
        // match flight 1's leftover true either way and prove nothing. It also cannot be
        // swapped for a managed value to additionally probe the NoVerticalMode freeze
        // (AltitudeModeTrackerTests' sibling test above covers that): the cold-gate armed(0)
        // call just above already ran the freeze at _vertical==0, latching _spoken to the
        // post-Reset IsManaged (false), so ANY managed reading here is a genuine change from
        // that latched false and is REQUIRED by the (unchanged, approved) production code to
        // announce — Assert.Null could never pass for one, independent of whether Reset()
        // works.
        Assert.Null(t.OnVerticalMode(40));            // SRS at rotation — silent baseline
    }

    [Fact]
    public void Reset_clears_a_baseline_taken_at_vertical_mode_none()
    {
        // The ??= freeze: a baseline taken while the FMA showed None must not survive Reset.
        var t = new AltitudeModeTracker();
        t.OnVerticalMode(0);
        t.OnVerticalArmed(0);
        t.Reset();
        Assert.False(t.IsKnown);
    }

    [Fact]
    public void An_unknown_tracker_reports_not_managed_but_says_so_via_IsKnown()
    {
        // Consumers must be able to render "--" rather than a confident wrong answer.
        var t = new AltitudeModeTracker();
        Assert.False(t.IsKnown);
        t.OnVerticalMode(22);                          // managed mode, armed still unknown
        Assert.False(t.IsKnown);
    }
}

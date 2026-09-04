using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The two FBW airframes wire their WING anti-ice pushbutton DIFFERENTLY behind the same
/// template name (`FBW_Airbus_AntiIce_Wing`) — A32NX writes
/// `L:A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION`, A380X fires the stock
/// `(&gt;K:TOGGLE_STRUCTURAL_DEICE)` and reads `A:STRUCTURAL DEICE SWITCH`. This pins both
/// airframes' registrations so neither gets "harmonised" onto the other, and pins the write
/// decision that a 2026-07 change and its first fix each got wrong.
///
/// The measurements, and why the write-stick test could not catch it, are in
/// docs/a380x.md, "Wing anti-ice is the STOCK switch".
/// </summary>
public class FlyByWireWingAntiIceWiringTests
{
    private static SimVarDefinition A380Var(string key) =>
        new FlyByWireA380Definition().GetVariables()[key];

    /// <summary>The A380 combo must read the STOCK switch the overhead PB actually drives.
    /// If this ever reads an `A32NX_…` L:var again, the control is dead on this airframe.</summary>
    [Fact]
    public void A380_wing_anti_ice_is_backed_by_the_stock_structural_deice_switch()
    {
        var v = A380Var("WING_ANTI_ICE_OVHD");

        Assert.Equal("STRUCTURAL DEICE SWITCH", v.Name);
        Assert.Equal(SimVarType.SimVar, v.Type);
    }

    /// <summary>It stays auto-announced, so a change made in the 3-D cockpit or by the
    /// aircraft reaches a blind pilot — the combo is the only other channel.</summary>
    [Fact]
    public void A380_wing_anti_ice_still_announces_background_changes()
    {
        var v = A380Var("WING_ANTI_ICE_OVHD");

        Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
        Assert.True(v.IsAnnounced);
        Assert.Equal("Off", v.ValueDescriptions[0]);
        Assert.Equal("On", v.ValueDescriptions[1]);
    }

    /// <summary>The A380 must NOT register the A32NX's button L:var at all.
    /// ⚠️ This asserts on the NAMES, not the keys: the 2026-07 regression was
    /// `vars["WING_ANTI_ICE_OVHD"].Name = "A32NX_BUTTON_…"`, and that string has never been a
    /// dictionary KEY on this airframe — so a Keys-only check passes on the broken build and
    /// pins nothing.</summary>
    [Fact]
    public void A380_does_not_register_the_a32nx_wing_anti_ice_button_lvar()
    {
        var vars = new FlyByWireA380Definition().GetVariables();

        Assert.DoesNotContain("A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION",
            vars.Values.Select(v => v.Name));
        Assert.DoesNotContain("A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION", vars.Keys);
    }

    /// <summary>...while the A32NX keeps it, because there the template really does write it.
    /// The fix must not be "harmonised" across the two jets in either direction.</summary>
    [Fact]
    public void A32nx_keeps_its_own_wing_anti_ice_button_lvar()
    {
        var vars = new FlyByWireA320Definition().GetVariables();

        Assert.True(vars.ContainsKey("A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION"));
        Assert.Equal("A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION",
            vars["A32NX_BUTTON_OVHD_ANTI_ICE_WING_POSITION"].Name);
    }

    /// <summary>The separate read-only flow status stays — it is what honestly reports that
    /// FBW has not modelled the A380 wing anti-ice pneumatic yet (re-measured 2026-09-03:
    /// real switch ON, `_SYSTEM_ON` still 0). It is a DIFFERENT var from the switch, so the
    /// two must not collapse onto one underlying name.</summary>
    [Fact]
    public void A380_keeps_the_separate_flow_status_readout()
    {
        var flow = A380Var("A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON");

        Assert.Equal("A32NX_PNEU_WING_ANTI_ICE_SYSTEM_ON", flow.Name);
        Assert.NotEqual(A380Var("WING_ANTI_ICE_OVHD").Name, flow.Name);
    }

    // ------------------------------------------------------------------
    // The WRITE half. The registration tests above pin the state side; the bug was in the
    // actuator, and before these the whole write path was uncovered while CLAUDE.md and
    // docs/a380x.md both claimed this file pinned it.
    // ------------------------------------------------------------------

    /// <summary>A cold cache means UNKNOWN, not Off, so BOTH directions must still fire.
    /// `?? 0` instead makes "Off" unsendable and makes "On" toggle an already-on switch OFF —
    /// wing anti-ice going off in icing in answer to the pilot selecting it on. The batch cache
    /// is emptied on every aircraft switch and reconnect, so this state is routine.</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.0)]
    public void An_unknown_live_value_always_fires_the_toggle(double desired) =>
        Assert.True(A380ToggleCommand.ShouldFire(desired, null));

    /// <summary>It is a TOGGLE, not a set: fire only when the pick differs from the live
    /// value, or every second press undoes the one before it.</summary>
    [Theory]
    [InlineData(1.0, 0.0, true)]
    [InlineData(0.0, 1.0, true)]
    [InlineData(1.0, 1.0, false)]
    [InlineData(0.0, 0.0, false)]
    public void The_toggle_fires_only_when_the_pick_differs(double desired, double current, bool fires) =>
        Assert.Equal(fires, A380ToggleCommand.ShouldFire(desired, current));
}

// A registered variable a blind pilot cannot reach is the same as no variable at all.
//
// VarNameCollisionTests checks the direction "every panel key resolves to a variable". This is
// the OTHER direction, for the handful of vars whose whole reason to exist is a readout: a var
// can be registered, given a TryGetDisplayOverride decoder, and still be dead, because nothing
// lists it in a panel or a status box, so it is never requested and the decoder never runs.
// Nothing fails, nothing is logged — the readout is simply absent.

using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests;

public class FlyByWireA380DisplayReachabilityTests
{
    // Vars that exist ONLY to be read out, with the status box that has to carry them. Each has
    // a decoder in TryGetDisplayOverride that only runs when a panel's display list requests the
    // key — no hotkey and no window ever requests these directly, and where the var IS Continuous
    // + IsAnnounced its own ProcessSimVarUpdate handler deliberately never speaks it (always
    // returns true, or is ExcludeFromMonitorManager) — so being listed here is the only thing
    // that puts it in front of a pilot.
    public static TheoryData<string, string> ReadoutOnlyVariables() => new()
    {
        // PRIM FG discrete word 3 bit 29 (altIsCrzAlt) — the ALT CRZ / ALT CRZ* the PFD's own
        // FMA shows. Continuous and IsAnnounced, but ExcludeFromMonitorManager and a
        // ProcessSimVarUpdate handler that always returns true keep it from ever announcing
        // itself, so the PFD status box is its only route out.
        { "PFD", "FMA_CRUISE_ALT_MODE" },
        // The ND option filter, decoded from its three lights. The row is keyed on the WPT
        // LIGHT, never on the ND_FILTER_{side} combo: that combo is an Act() action control
        // whose own key has no backing L:var, and a data definition bound to a nonexistent
        // L:var never delivers, so a row keyed on it would read "--" for the whole session.
        { "EFIS Captain", "A32NX_FCU_EFIS_L_WPT_LIGHT_ON" },
        { "EFIS First Officer", "A32NX_FCU_EFIS_R_WPT_LIGHT_ON" },
        // Altitude managed/selected, decoded from the derived AltitudeModeTracker. The key's own
        // L:var is FBW #10855's dead one (hardcoded to 0) — the actual call-out is emitted off
        // A32NX_FMA_VERTICAL_MODE — so this FCU panel row is the only thing that puts the
        // altitude mode in front of a pilot at all.
        { "FCU", "A32NX_FCU_ALT_MANAGED" },
    };

    [Theory]
    [MemberData(nameof(ReadoutOnlyVariables))]
    public void A_readout_only_variable_is_listed_in_its_status_box(string panel, string varKey)
    {
        var display = new FlyByWireA380Definition().GetPanelDisplayVariables();

        Assert.True(display.TryGetValue(panel, out var keys),
            $"A380 has no '{panel}' display list at all.");
        Assert.True(keys!.Contains(varKey),
            $"'{varKey}' is registered and decoded but listed in no panel, so it is never "
            + $"requested and a pilot can never hear it. Expected it in the '{panel}' status box.");
    }
}

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
    // a decoder in TryGetDisplayOverride and no other consumer — no hotkey, no window, no
    // auto-announce — so being listed here is the only thing that puts it in front of a pilot.
    public static TheoryData<string, string> ReadoutOnlyVariables() => new()
    {
        // PRIM FG discrete word 3 bit 29 (altIsCrzAlt) — the ALT CRZ / ALT CRZ* the PFD's own
        // FMA shows. OnRequest and not announced, so the PFD status box is its only route out.
        { "PFD", "FMA_CRUISE_ALT_MODE" },
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

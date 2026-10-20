// Characterization test for the PMDG 737 NG3 No Smoking switch. The SDK header
// lists three positions (0 OFF, 1 AUTO, 2 ON), but the NG3 models two: swept
// live 2026-10-20 with tools/PMDGDispatchTester, a LEFTSINGLE or RIGHTSINGLE
// toggles OFF <-> AUTO, and position 2 is refused by the CDA write, the
// absolute transmit and the cockpit's own K:ROTOR_BRAKE click, wheel and drag
// codes. With ON offered, picking it walked one LEFTSINGLE from AUTO and
// landed OFF.

namespace MSFSBlindAssist.Tests;

using MSFSBlindAssist.Aircraft;

public class Pmdg737NoSmokingSwitchTests
{
    [Fact]
    public void TheNoSmokingSwitchOffersOnlyTheTwoPositionsTheNg3Models()
    {
        var varDef = new PMDG737Definition().GetVariables()["COMM_NoSmokingSelector"];

        // Two positions send the CDA direct-position write (absolute, idempotent:
        // live-verified); a third would put it back on the toggling click-walk.
        Assert.Equal(new Dictionary<double, string> { [0] = "OFF", [1] = "AUTO" }, varDef.ValueDescriptions);
        Assert.False(PMDG737Definition.UsesAbsolutePositionDispatch("COMM_NoSmokingSelector"));
    }
}

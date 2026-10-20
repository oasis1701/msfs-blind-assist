// Characterization tests for the PMDG 737 NG3 No Smoking switch: two positions,
// set by the CDA write whatever the cached snapshot says (P737-3, swept 2026-10-20).

namespace MSFSBlindAssist.Tests;

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

public class Pmdg737NoSmokingSwitchTests
{
    private const string Key = "COMM_NoSmokingSelector";

    [Fact]
    public void TheNoSmokingSwitchOffersOnlyTheTwoPositionsTheNg3Models()
    {
        var varDef = new PMDG737Definition().GetVariables()[Key];

        Assert.Equal(new Dictionary<double, string> { [0] = "OFF", [1] = "AUTO" }, varDef.ValueDescriptions);
        Assert.False(PMDG737Definition.UsesAbsolutePositionDispatch(Key));
    }

    // The guard reads the 1 Hz snapshot: a quick AUTO -> OFF -> AUTO pick, or a pick of OFF
    // before the first snapshot (it reads 0), would send nothing. Other toggles keep it.
    [Fact]
    public void NoSmokingSendsEveryPickWhateverTheSnapshotShows()
    {
        var vars = new PMDG737Definition().GetVariables();

        Assert.False(PMDG737Definition.GuardsAlreadyAtTarget(Key, vars[Key]));
        Assert.True(PMDG737Definition.GuardsAlreadyAtTarget("AIR_EquipCoolingSupplyNORM", vars["AIR_EquipCoolingSupplyNORM"]));
    }

    // With no data manager the click-walk refuses aloud ("Switch not ready"); the two-position
    // CDA write is sent without one, so the pick is handled and nothing is spoken.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_pick_takes_the_two_position_write_not_the_click_walk(int target)
    {
        var def = new PMDG737Definition();
        var speech = new SpeechCapture();

        Assert.True(def.HandleUIVariableSet(Key, target, def.GetVariables()[Key], new SimConnectManager(IntPtr.Zero), speech));
        Assert.Empty(speech.All);
    }
}

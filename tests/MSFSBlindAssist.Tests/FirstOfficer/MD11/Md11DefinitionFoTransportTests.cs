using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer.MD11;

/// <summary>
/// The definition-side First Officer transport. Without Attach there is no bus, so every write
/// refuses (false) and never throws; the external-write allow-list is the repo's CLOSED
/// non-CEVENT write set and nothing else.
/// </summary>
public class Md11DefinitionFoTransportTests
{
    [Theory]
    [InlineData("MD11_EXTCTL_CAP_BARO", true)]
    [InlineData("MD11_EXTCTL_FO_BARO", true)]
    [InlineData("MD11_EXTCTL_STBY_BARO", true)]
    [InlineData("MD11_DIALAFLAP_WHEEL_RNG", true)]
    [InlineData("MD11_FLAP_RNG", false)]                 // the flap handle: refused everywhere
    [InlineData("MD11_SPDBRK_HANDLE", false)]
    [InlineData("MD11_MIP_GEAR_SW", false)]
    [InlineData("MD11_OVHD_ELEC_BATT_BT", false)]        // a control's own L:var
    [InlineData("CEVENT", false)]
    [InlineData("", false)]
    public void ExternalWriteAllowList_IsTheClosedSet(string var, bool allowed)
        => Assert.Equal(allowed, TFDiMD11Definition.IsFoExternalWriteAllowed(var));

    [Fact]
    public async Task WithoutAttach_EveryWriteRefuses_AndNothingThrows()
    {
        using var def = new TFDiMD11Definition();
        Assert.False(def.FoTransportReady);
        Assert.Equal(0, def.FoBusPending);
        Assert.False(def.FoFireEvent(90150));
        Assert.False(def.FoPress(90150, 90151));
        Assert.False(await def.FoHoldAsync(73748, 73749, 10));
        Assert.False(def.FoWriteExternal("MD11_EXTCTL_CAP_BARO", 29.92));
        def.NoteFoActuation("MD11_OVHD_ELEC_BATT_BT");   // no UI context: runs inline, must not throw
        def.MuteLampSpeechFor(1000);
        Assert.False(def.FoIsDcPowered());
    }

    [Fact]
    public void RefusesARawIdOfZeroOrLess()
    {
        using var def = new TFDiMD11Definition();
        Assert.False(def.FoFireEvent(0));
        Assert.False(def.FoPress(0, 0));
    }

    /// <summary>
    /// A First Officer stow after landing passes the pull 2 -> 1 -> 0 and runs the lever 50 -> 0
    /// (TFDi's own retract). Narrated, that says "Ground spoilers armed" mid-stow and walks every
    /// detent aloud over the flow. Inside the FO quiet window both read-outs must RECORD the new
    /// state and stay silent. The announcer is null: reaching it would throw.
    /// </summary>
    [Fact]
    public void FoActuationOfTheSpoilerLever_SilencesItsArmAndTravelReadouts()
    {
        using var def = new TFDiMD11Definition();
        MSFSBlindAssist.Accessibility.ScreenReaderAnnouncer announcer = null!;
        Assert.True(def.ProcessSimVarUpdate(Md11SpeedbrakeSystem.ArmKey, 2, announcer));    // baselines: silent
        Assert.True(def.ProcessSimVarUpdate(Md11SpeedbrakeSystem.LeverKey, 50, announcer));

        def.NoteFoActuation(Md11SpeedbrakeSystem.LeverKey);                                 // no UI context: inline

        Assert.True(def.ProcessSimVarUpdate(Md11SpeedbrakeSystem.ArmKey, 1, announcer));    // would say "armed"
        Assert.True(def.ProcessSimVarUpdate(Md11SpeedbrakeSystem.LeverKey, 25, announcer)); // would say "2/3 extended"
        Assert.True(def.ProcessSimVarUpdate(Md11SpeedbrakeSystem.LeverKey, 0, announcer));  // would say "retracted"
        Assert.True(def.ProcessSimVarUpdate(Md11SpeedbrakeSystem.ArmKey, 0, announcer));    // would say "disarmed"
    }
}

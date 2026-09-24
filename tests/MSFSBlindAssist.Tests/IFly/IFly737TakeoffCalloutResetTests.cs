using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist.Tests.IFly;

/// <summary>
/// The iFly 737 MAX8's take-off roll callouts ride the shared <see cref="TakeoffVSpeedCallouts"/>
/// machine (whose contract is pinned in TakeoffVSpeedCalloutsTests). These pin this definition's
/// own two resets — including the CONTEXT reset, the half a flight load reaches, which the MD-11
/// gained first and this aircraft went without.
/// </summary>
public class IFly737TakeoffCalloutResetTests
{
    /// <summary>
    /// A flight load raises the context reset and never ResetAnnouncementBaselines, so the context
    /// reset is where the roll callouts' ARM has to go — on this definition as much as on the
    /// MD-11's. Parked with the speeds set (armed), then a flight loaded into the cruise: IFLY_IAS
    /// is per-frame and SIM_ON_GROUND rides the 1 Hz batch, so the first cruise sample still says
    /// "on the ground". Without the override that one sample called "V1", "Rotate" and "V2" at
    /// altitude.
    /// </summary>
    [Fact]
    public void AContextReset_DropsTheRollArm_SoAFlightLoadedIntoTheCruiseCallsNothing()
    {
        var def = new IFly737MAXDefinition();
        var machine = def.TakeoffCallouts;
        machine.SetV1(145);
        machine.SetVR(150);
        machine.SetV2(158);
        Assert.Empty(machine.ProcessSample(0, onGround: true));     // parked with the speeds set: armed

        def.OnSimContextReset();                                     // the flight load

        Assert.Empty(machine.ProcessSample(280, onGround: true));    // the loaded cruise, stale ground flag
        // Through the definition's own branch too: its ground flag starts true and only
        // SIM_ON_GROUND corrects it. Nothing crosses, so the announcer (null here) is never reached.
        Assert.True(def.ProcessSimVarUpdate("IFLY_IAS", 281, null!));
    }

    /// <summary>
    /// The reconnect reset still does the same — an arm from before a SimConnect drop must not
    /// survive into a later landing rollout. Both overrides, one machine.
    /// </summary>
    [Fact]
    public void AReconnect_DropsTheRollArmToo()
    {
        var def = new IFly737MAXDefinition();
        var machine = def.TakeoffCallouts;
        machine.SetV1(145);
        machine.SetVR(150);
        Assert.Empty(machine.ProcessSample(0, onGround: true));

        def.ResetAnnouncementBaselines();

        Assert.Empty(machine.ProcessSample(280, onGround: true));
    }

    /// <summary>
    /// The speeds SURVIVE both resets: the SDK shared memory fires only on change and its re-seed
    /// is an initial snapshot MainForm drops, so a reset that cleared them silenced every callout
    /// for the rest of the session. A fresh ground arm still calls the roll.
    /// </summary>
    [Fact]
    public void TheSpeedsSurviveAContextReset_SoTheNextRollStillCalls()
    {
        var def = new IFly737MAXDefinition();
        var machine = def.TakeoffCallouts;
        machine.SetV1(145);
        machine.SetVR(150);
        machine.SetV2(158);

        def.OnSimContextReset();

        Assert.Empty(machine.ProcessSample(10, onGround: true));     // re-arms below 40 kt on the ground
        Assert.Empty(machine.ProcessSample(100, onGround: true));
        Assert.Equal(new[] { "V1" }, machine.ProcessSample(146, onGround: true));
        Assert.Equal(new[] { "Rotate" }, machine.ProcessSample(151, onGround: true));
        Assert.Equal(new[] { "V2" }, machine.ProcessSample(160, onGround: false));
    }
}

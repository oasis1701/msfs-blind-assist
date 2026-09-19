using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The MD-11's take-off roll callouts ("V1", "Rotate", "V2") ride the shared
/// TakeoffVSpeedCallouts machine (its contract is pinned in TakeoffVSpeedCalloutsTests); these
/// pin the MD-11's plumbing — the per-frame airspeed feed, the V-speed exports that arm it, and
/// the Ctrl+M rows that mute each call.
/// </summary>
public class Md11TakeoffCalloutsTests
{
    private static readonly TFDiMD11Definition Def = new();
    private static Dictionary<string, SimVarDefinition> Vars => Def.GetVariables();

    /// <summary>
    /// The airspeed feed is a per-var SIM_FRAME subscription (the G_FORCE pattern): the 1 Hz batch
    /// would call "Rotate" up to a second late. It is consumed, never spoken, and has no Ctrl+M
    /// row of its own — the callouts are muted through the V-speed rows.
    /// </summary>
    [Fact]
    public void TheAirspeedFeed_IsPerFrame_Silent_AndHiddenFromCtrlM()
    {
        var ias = Vars[Md11TakeoffCallouts.IasKey];
        Assert.Equal("AIRSPEED INDICATED", ias.Name);
        Assert.Equal("knots", ias.Units);
        Assert.Equal(SimVarType.SimVar, ias.Type);
        Assert.Equal(UpdateFrequency.Continuous, ias.UpdateFrequency);
        Assert.True(ias.IsAnnounced);              // monitored at all
        Assert.True(ias.ExcludeFromBatch);
        Assert.True(ias.HighFrequency);
        Assert.True(ias.ExcludeFromMonitorManager);
    }

    /// <summary>The rows that mute the calls are the V-speed read-outs themselves, so they must be in Ctrl+M.</summary>
    [Theory]
    [InlineData("V1", "MD11_V1", "V1")]
    [InlineData("Rotate", "MD11_VR", "Rotate speed")]
    [InlineData("V2", "MD11_V2", "V2")]
    public void EachCallout_IsMutedByItsVSpeedRow(string callout, string key, string rowName)
    {
        Assert.Equal(key, Md11TakeoffCallouts.MuteKeyFor(callout));
        Assert.True(Md11TakeoffCallouts.IsVSpeedKey(key));
        var row = Vars[key];
        Assert.Equal(rowName, row.DisplayName);
        Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);   // delivered in the background at all
        Assert.True(row.IsAnnounced);                                     // monitored at all
        Assert.False(row.ExcludeFromMonitorManager);
    }

    /// <summary>
    /// A callout the mute table does not know maps to no row, so it is spoken rather than
    /// silently swallowed by the V2 checkbox — fail open for a safety cue.
    /// </summary>
    [Fact]
    public void AnUnknownCallout_IsNeverMuted()
    {
        Assert.DoesNotContain(Md11TakeoffCallouts.MuteKeyFor("Vfs"), Md11VSpeeds.Keys);
        Assert.False(Md11TakeoffCallouts.IsVSpeedKey("MD11_ENG1_N1"));
    }

    /// <summary>
    /// The FMS exports arm the machine and a roll then speaks the three calls in order; an export
    /// the FMS has not filled (TFDi reads 0 there) leaves it disarmed, so a roll without V-speeds
    /// is silent rather than wrong.
    /// </summary>
    [Fact]
    public void TheExports_ArmTheMachine_AndARollSpeaksInOrder()
    {
        var m = new TakeoffVSpeedCallouts();
        Md11TakeoffCallouts.Feed(m, "MD11_V1", 0);
        Md11TakeoffCallouts.Feed(m, "MD11_VR", 0);
        Assert.Empty(m.ProcessSample(0, onGround: true));
        Assert.Empty(m.ProcessSample(160, onGround: true));   // no speeds yet: silent

        Md11TakeoffCallouts.Feed(m, "MD11_V1", 145);
        Md11TakeoffCallouts.Feed(m, "MD11_VR", 150);
        Md11TakeoffCallouts.Feed(m, "MD11_V2", 158);
        Md11TakeoffCallouts.Feed(m, "MD11_ENG1_N1", 95);     // not a V-speed: must change no speed below
        Assert.Empty(m.ProcessSample(10, onGround: true));    // arms below 40 kt on the ground
        Assert.Empty(m.ProcessSample(60, onGround: true));
        Assert.Empty(m.ProcessSample(100, onGround: true));   // a V-speed of 95 from the N1 feed would fire here
        Assert.Empty(m.ProcessSample(140, onGround: true));
        Assert.Equal(new[] { "V1" }, m.ProcessSample(146, onGround: true));
        Assert.Equal(new[] { "Rotate" }, m.ProcessSample(151, onGround: true));
        Assert.Empty(m.ProcessSample(157, onGround: true));
        Assert.Equal(new[] { "V2" }, m.ProcessSample(160, onGround: false));
    }

    /// <summary>
    /// One sentence per sample, and a muted row drops only its own call from it — the MD-11's
    /// Ctrl+M test (IsMuted, over MuteKeyFor) exactly as the definition hands it to Compose. A
    /// call with no row is never muted, even by a set that holds an empty key: fail open.
    /// </summary>
    [Fact]
    public void AMutedRow_DropsOnlyItsOwnCall_FromTheOneSentence()
    {
        var rotateMuted = new HashSet<string> { Md11TakeoffCallouts.VrKey };
        Assert.True(Md11TakeoffCallouts.IsMuted("Rotate", rotateMuted));
        Assert.False(Md11TakeoffCallouts.IsMuted("V1", rotateMuted));
        Assert.Equal("V1, V2", TakeoffVSpeedCallouts.Compose(new[] { "V1", "Rotate", "V2" },
            c => Md11TakeoffCallouts.IsMuted(c, rotateMuted)));

        var allMuted = new HashSet<string> { Md11TakeoffCallouts.V1Key, Md11TakeoffCallouts.VrKey, Md11TakeoffCallouts.V2Key, "" };
        Assert.Null(TakeoffVSpeedCallouts.Compose(new[] { "V1", "Rotate", "V2" }, c => Md11TakeoffCallouts.IsMuted(c, allMuted)));
        Assert.False(Md11TakeoffCallouts.IsMuted("Vfs", allMuted));   // no row: spoken, whatever is ticked
    }

    /// <summary>
    /// A flight load raises the context reset and never ResetAnnouncementBaselines, so the context
    /// reset is where the roll callouts' ARM must go. Parked with the speeds set (armed), then a
    /// flight loaded into the cruise: MD11_IAS (per frame) lands before the 1 Hz SIM_ON_GROUND, so
    /// the first cruise sample still says "on the ground". Before the fix the arm survived the load
    /// and that one sample called "V1", "Rotate" and "V2" at once.
    /// </summary>
    [Fact]
    public void AContextReset_DropsTheRollArm_SoAFlightLoadedIntoTheCruiseCallsNothing()
    {
        var def = new TFDiMD11Definition();
        var machine = def.TakeoffCallouts;
        Md11TakeoffCallouts.Feed(machine, Md11TakeoffCallouts.V1Key, 150);
        Md11TakeoffCallouts.Feed(machine, Md11TakeoffCallouts.VrKey, 155);
        Md11TakeoffCallouts.Feed(machine, Md11TakeoffCallouts.V2Key, 162);
        Assert.Empty(machine.ProcessSample(0, onGround: true));    // parked with the speeds set: armed

        def.OnSimContextReset();                                    // the flight load

        Assert.Empty(machine.ProcessSample(280, onGround: true));  // the loaded cruise, stale ground flag
        // Through the definition's own branch too: its ground flag starts true and only SIM_ON_GROUND
        // corrects it. Nothing crosses, so the announcer (null here) is never reached.
        Assert.True(def.ProcessSimVarUpdate(Md11TakeoffCallouts.IasKey, 281, null!));
    }
}

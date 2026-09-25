using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The A380's take-off roll callouts ("V1", "Rotate", "V2", added 2026-09-25) ride the shared
/// TakeoffVSpeedCallouts machine (its contract is pinned in TakeoffVSpeedCalloutsTests); these pin
/// the A380's plumbing — the per-frame airspeed feed, the FMS V-speeds that arm it, the Ctrl+M rows
/// that mute each call, and the two resets every user of the machine needs.
/// </summary>
[Collection("SettingsManagerGlobalState")]
public class A380TakeoffCalloutsTests : IDisposable
{
    private readonly List<string> _savedMutes;

    public A380TakeoffCalloutsTests()
    {
        _savedMutes = SettingsManager.Current.A380DisabledMonitorVariables;
        Mute();   // nothing muted unless a test says so, whatever this machine's own settings hold
    }

    public void Dispose()
    {
        SettingsManager.Current.A380DisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static void Mute(params string[] keys)
    {
        SettingsManager.Current.A380DisabledMonitorVariables = new List<string>(keys);
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static Dictionary<string, SimVarDefinition> Vars => new FlyByWireA380Definition().GetVariables();

    /// <summary>A definition with the FMS speeds entered and the aircraft parked: armed for a roll.</summary>
    private static FlyByWireA380Definition Parked(SpeechCapture speech, double v1 = 142, double vr = 148, double v2 = 155)
    {
        var def = new FlyByWireA380Definition();
        def.ProcessSimVarUpdate(A380TakeoffCallouts.V1Key, v1, speech);
        def.ProcessSimVarUpdate(A380TakeoffCallouts.VrKey, vr, speech);
        def.ProcessSimVarUpdate(A380TakeoffCallouts.V2Key, v2, speech);
        def.ProcessSimVarUpdate(A380TakeoffCallouts.IasKey, 5, speech);
        return def;
    }

    private static void Roll(FlyByWireA380Definition def, SpeechCapture speech, params double[] samples)
    {
        foreach (var ias in samples) def.ProcessSimVarUpdate(A380TakeoffCallouts.IasKey, ias, speech);
    }

    [Fact]
    public void The_airspeed_feed_is_per_frame_silent_and_hidden_from_ctrl_m()
    {
        // The 1 Hz batch would call "Rotate" up to a second (~5 kt) late — useless as an action cue.
        var ias = Vars[A380TakeoffCallouts.IasKey];
        Assert.Equal("AIRSPEED INDICATED", ias.Name);
        Assert.Equal("knots", ias.Units);
        Assert.Equal(SimVarType.SimVar, ias.Type);
        Assert.Equal(UpdateFrequency.Continuous, ias.UpdateFrequency);
        Assert.True(ias.IsAnnounced);
        Assert.True(ias.ExcludeFromBatch);
        Assert.True(ias.HighFrequency);
        Assert.True(ias.ExcludeFromMonitorManager);
    }

    [Theory]
    [InlineData("V1", "PFD_V1", "AIRLINER_V1_SPEED")]
    [InlineData("Rotate", "PFD_VR", "AIRLINER_VR_SPEED")]
    [InlineData("V2", "PFD_V2", "AIRLINER_V2_SPEED")]
    public void Each_callout_is_muted_by_its_v_speed_row(string callout, string key, string name)
    {
        Assert.Equal(key, A380TakeoffCallouts.MuteKeyFor(callout));
        var row = Vars[key];
        Assert.Equal(name, row.Name);                                    // what the A380 FMS writes
        Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
        Assert.True(row.IsAnnounced);
        Assert.False(row.ExcludeFromMonitorManager);
    }

    [Fact]
    public void A_roll_calls_v1_rotate_and_v2_in_order_as_interruptions()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);

        Roll(def, speech, 60, 120, 141, 143);
        Roll(def, speech, 147, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156, 170);

        // Interrupting, deliberately: an action cue whose value is its timing must not queue behind
        // a speed callout already being spoken.
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, speech.Interrupts);
    }

    [Fact]
    public void The_v_speeds_still_reach_the_monitor()
    {
        // Peeked, never consumed: the generic monitor still speaks "V1: 142 knots" as they are entered.
        var def = new FlyByWireA380Definition();
        Assert.False(def.ProcessSimVarUpdate(A380TakeoffCallouts.V1Key, 142, new SpeechCapture()));
        Assert.True(def.ProcessSimVarUpdate(A380TakeoffCallouts.IasKey, 5, new SpeechCapture()));
    }

    [Fact]
    public void V1_equal_to_vr_is_one_utterance()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech, v1: 150, vr: 150, v2: 158);

        Roll(def, speech, 100, 149, 151);

        Assert.Equal(new[] { "V1, Rotate" }, speech.Interrupts);
    }

    [Fact]
    public void A_muted_v1_row_leaves_rotate_and_v2()
    {
        Mute(A380TakeoffCallouts.V1Key);
        var speech = new SpeechCapture();
        var def = Parked(speech);

        Roll(def, speech, 100, 143, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156);

        Assert.Equal(new[] { "Rotate", "V2" }, speech.Interrupts);
    }

    [Fact]
    public void Cleared_v_speeds_leave_the_roll_silent()
    {
        // -1 for V1/VR and 0 for V2 is how the A380 FMS clears them (a runway change sends them back
        // for confirmation, FlightManagementComputer.ts / FmcAircraftInterface.ts).
        var speech = new SpeechCapture();
        var def = Parked(speech, v1: -1, vr: -1, v2: 0);

        Roll(def, speech, 60, 143, 149, 156);

        Assert.Empty(speech.Interrupts);
    }

    [Fact]
    public void Connecting_mid_roll_stays_silent()
    {
        var speech = new SpeechCapture();
        var def = new FlyByWireA380Definition();
        def.ProcessSimVarUpdate(A380TakeoffCallouts.V1Key, 142, speech);
        def.ProcessSimVarUpdate(A380TakeoffCallouts.VrKey, 148, speech);

        Roll(def, speech, 120, 143, 149);   // never below 40 kt on the ground: never armed

        Assert.Empty(speech.Interrupts);
    }

    [Fact]
    public void A_context_reset_drops_the_arm_so_a_flight_loaded_into_the_cruise_calls_nothing()
    {
        // A flight load raises the context reset and never ResetAnnouncementBaselines. The per-frame
        // airspeed lands before the 1 Hz SIM_ON_GROUND, so the loaded cruise's first sample still
        // reads "on the ground"; an arm kept from the ramp would call all three at once.
        var speech = new SpeechCapture();
        var def = Parked(speech);

        def.OnSimContextReset();
        Roll(def, speech, 280);

        Assert.Empty(speech.Interrupts);
    }

    [Fact]
    public void A_reconnect_drops_the_arm_but_keeps_the_speeds()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);

        def.ResetAnnouncementBaselines();
        Roll(def, speech, 150);              // disarmed: nothing, even crossing V1 and VR
        Assert.Empty(speech.Interrupts);

        Roll(def, speech, 5, 143);           // a new roll from a standstill: the speeds are still known
        Assert.Equal(new[] { "V1" }, speech.Interrupts);
    }
}

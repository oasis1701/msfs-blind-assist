using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The FBW A320's take-off roll callouts ("V1", "Rotate", "V2", added 2026-10-11) ride the shared
/// TakeoffVSpeedCallouts machine (its contract is pinned in TakeoffVSpeedCalloutsTests); these pin
/// the A320's plumbing — the per-frame airspeed feed, the FMS V-speeds that arm it, the Ctrl+M rows
/// that mute each call, the two resets every user of the machine needs, and the A32NX FMS's own
/// post-takeoff clear. The Headwind A330 inherits all of it from the A320 definition.
/// </summary>
[Collection("SettingsManagerGlobalState")]
public class A320TakeoffCalloutsTests : IDisposable
{
    private readonly List<string> _savedMutes;

    public A320TakeoffCalloutsTests()
    {
        _savedMutes = SettingsManager.Current.A32NXDisabledMonitorVariables;
        Mute();   // nothing muted unless a test says so, whatever this machine's own settings hold
    }

    public void Dispose()
    {
        SettingsManager.Current.A32NXDisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static void Mute(params string[] keys)
    {
        SettingsManager.Current.A32NXDisabledMonitorVariables = new List<string>(keys);
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    public static TheoryData<string> Airframes() => new() { "A320", "HW_A330" };

    private static FlyByWireA320Definition New(string airframe = "A320") =>
        airframe == "HW_A330" ? new HeadwindA330Definition() : new FlyByWireA320Definition();

    private static Dictionary<string, SimVarDefinition> Vars(string airframe = "A320") => New(airframe).GetVariables();

    /// <summary>A definition with the FMS speeds entered and the aircraft parked: armed for a roll.</summary>
    private static FlyByWireA320Definition Parked(SpeechCapture speech, double v1 = 142, double vr = 148, double v2 = 155,
        string airframe = "A320")
    {
        var def = New(airframe);
        def.ProcessSimVarUpdate(A320TakeoffCallouts.V1Key, v1, speech);
        def.ProcessSimVarUpdate(A320TakeoffCallouts.VrKey, vr, speech);
        def.ProcessSimVarUpdate(A320TakeoffCallouts.V2Key, v2, speech);
        def.ProcessSimVarUpdate(A320TakeoffCallouts.IasKey, 5, speech);
        return def;
    }

    private static void Roll(FlyByWireA320Definition def, SpeechCapture speech, params double[] samples)
    {
        foreach (var ias in samples) def.ProcessSimVarUpdate(A320TakeoffCallouts.IasKey, ias, speech);
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void The_airspeed_feed_is_per_frame_silent_and_hidden_from_ctrl_m(string airframe)
    {
        // The 1 Hz batch would call "Rotate" up to a second (~5 kt) late — useless as an action cue.
        var ias = Vars(airframe)[A320TakeoffCallouts.IasKey];
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
        Assert.Equal(key, A320TakeoffCallouts.Keys.MuteKeyFor(callout));
        var row = Vars()[key];
        Assert.Equal(name, row.Name);                                    // what the A32NX FMS writes
        Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
        Assert.True(row.IsAnnounced);
        Assert.False(row.ExcludeFromMonitorManager);
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void A_roll_calls_v1_rotate_and_v2_in_order_as_interruptions(string airframe)
    {
        var speech = new SpeechCapture();
        var def = Parked(speech, airframe: airframe);

        Roll(def, speech, 60, 120, 141, 143);
        Roll(def, speech, 147, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156, 170);

        // Interrupting, deliberately: an action cue whose value is its timing must not queue behind
        // a speed callout already being spoken (Takeoff Assist's "100 knots", a ground-speed call).
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, speech.Interrupts);
    }

    [Fact]
    public void The_roll_is_called_in_the_takeoff_phase_and_the_fms_clear_after_it_stays_silent()
    {
        // The A32NX FMS enters TAKEOFF at take-off thrust and clears all three speeds once the phase
        // is PAST it (A32NX_FMCMainDisplay.ts): the roll still has its speeds, and the clear that
        // follows in the climb is consumed silently (FbwVSpeedNotSetTests) without a call.
        var speech = new SpeechCapture();
        var def = Parked(speech);
        def.ProcessSimVarUpdate("A32NX_FMGC_FLIGHT_PHASE", 1, speech);   // take-off thrust

        Roll(def, speech, 60, 143, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156);
        def.ProcessSimVarUpdate("A32NX_FMGC_FLIGHT_PHASE", 2, speech);   // climb: the FMS clears them
        Assert.True(def.ProcessSimVarUpdate(A320TakeoffCallouts.V1Key, -1, speech));
        Assert.True(def.ProcessSimVarUpdate(A320TakeoffCallouts.VrKey, -1, speech));
        Assert.True(def.ProcessSimVarUpdate(A320TakeoffCallouts.V2Key, 0, speech));
        Roll(def, speech, 180, 220);

        Assert.Equal(new[] { "V1", "Rotate", "V2" }, speech.Interrupts);
    }

    [Fact]
    public void The_v_speeds_still_reach_the_monitor()
    {
        // Peeked, never consumed: the generic monitor still speaks "V1: 142 knots" as they are entered.
        var def = new FlyByWireA320Definition();
        Assert.False(def.ProcessSimVarUpdate(A320TakeoffCallouts.V1Key, 142, new SpeechCapture()));
        Assert.False(def.ProcessSimVarUpdate(A320TakeoffCallouts.VrKey, 148, new SpeechCapture()));
        Assert.False(def.ProcessSimVarUpdate(A320TakeoffCallouts.V2Key, 155, new SpeechCapture()));
        Assert.True(def.ProcessSimVarUpdate(A320TakeoffCallouts.IasKey, 5, new SpeechCapture()));
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
        Mute(A320TakeoffCallouts.V1Key);
        var speech = new SpeechCapture();
        var def = Parked(speech);

        Roll(def, speech, 100, 143, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156);

        Assert.Equal(new[] { "Rotate", "V2" }, speech.Interrupts);
    }

    [Fact]
    public void The_feed_is_kept_out_of_the_mute_wrap_because_its_calls_ride_the_v_speed_rows()
    {
        // Wrapped, a mute on the feed's own key would silence all three; the branch checks each call's
        // row itself. The feed is hidden from Ctrl+M, so it can never be muted as a row of its own.
        var def = new FlyByWireA320Definition();
        Assert.True(def.IsMuteWrapExempt(A320TakeoffCallouts.IasKey));
        Assert.False(DefAnnounceMuteSets.ShouldWrap(def, A320TakeoffCallouts.IasKey, SettingsManager.Current));
        Assert.True(new HeadwindA330Definition().IsMuteWrapExempt(A320TakeoffCallouts.IasKey));
    }

    [Fact]
    public void A_suppressed_announcer_calls_nothing()
    {
        // The connect and aircraft-switch grace windows set Suppressed; AnnounceImmediate ignores it,
        // so the branch checks it itself.
        var speech = new SpeechCapture();
        var def = Parked(speech);

        speech.Suppressed = true;
        Roll(def, speech, 143);

        Assert.Empty(speech.Interrupts);
    }

    [Theory]
    [InlineData(-1, -1, 0)]   // the A32NX since FBW #10855 (and the A380): V1/VR -1, V2 0
    [InlineData(0, 0, 0)]     // the clear before #10855, which the Headwind A330's fork still writes
    public void Cleared_v_speeds_leave_the_roll_silent(double v1, double vr, double v2)
    {
        foreach (var airframe in new[] { "A320", "HW_A330" })
        {
            var speech = new SpeechCapture();
            var def = Parked(speech, v1, vr, v2, airframe);

            Roll(def, speech, 60, 143, 149, 156);

            Assert.Empty(speech.Interrupts);
        }
    }

    [Fact]
    public void Connecting_mid_roll_stays_silent()
    {
        var speech = new SpeechCapture();
        var def = new FlyByWireA320Definition();
        def.ProcessSimVarUpdate(A320TakeoffCallouts.V1Key, 142, speech);
        def.ProcessSimVarUpdate(A320TakeoffCallouts.VrKey, 148, speech);

        Roll(def, speech, 120, 143, 149);   // never below 40 kt on the ground: never armed

        Assert.Empty(speech.Interrupts);
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void A_context_reset_drops_the_arm_so_a_flight_loaded_into_the_cruise_calls_nothing(string airframe)
    {
        // A flight load raises the context reset and never ResetAnnouncementBaselines. The per-frame
        // airspeed lands before the 1 Hz SIM_ON_GROUND, so the loaded cruise's first sample still
        // reads "on the ground"; an arm kept from the ramp would call all three at once.
        var speech = new SpeechCapture();
        var def = Parked(speech, airframe: airframe);

        def.OnSimContextReset();
        Roll(def, speech, 280);

        Assert.Empty(speech.Interrupts);
    }

    [Theory]
    [MemberData(nameof(Airframes))]
    public void A_reconnect_drops_the_arm_but_keeps_the_speeds(string airframe)
    {
        // The A330 overrides ResetAnnouncementBaselines for its altimeter; it must still reach the A320's.
        var speech = new SpeechCapture();
        var def = Parked(speech, airframe: airframe);

        def.ResetAnnouncementBaselines();
        Roll(def, speech, 150);              // disarmed: nothing, even crossing V1 and VR
        Assert.Empty(speech.Interrupts);

        Roll(def, speech, 5, 143);           // a new roll from a standstill: the speeds are still known
        Assert.Equal(new[] { "V1" }, speech.Interrupts);
    }
}

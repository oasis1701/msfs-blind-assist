using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The Fenix A320's take-off roll callouts ("V1", "Rotate", "V2") ride the shared
/// TakeoffVSpeedCallouts machine (contract in TakeoffVSpeedCalloutsTests), so they follow the MCDU
/// performance speeds live whether or not Takeoff Assist is on. These pin the Fenix's plumbing,
/// under a comma-decimal culture. The Fenix's own FWC also calls "V1" (sound.xml FWC_V_ONE_L/_R,
/// LocalVar A320_Audio:v1); the owner decided the app's call arrives alongside it.
/// </summary>
[Collection("SettingsManagerGlobalState")]
public class FenixTakeoffCalloutsTests : IDisposable
{
    private readonly CultureInfo _savedCulture = CultureInfo.CurrentCulture;
    private readonly List<string> _savedMutes;

    public FenixTakeoffCalloutsTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        _savedMutes = SettingsManager.Current.FenixDisabledMonitorVariables;
        Mute();
    }

    public void Dispose()
    {
        SettingsManager.Current.FenixDisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
        CultureInfo.CurrentCulture = _savedCulture;
    }

    private static void Mute(params string[] keys)
    {
        SettingsManager.Current.FenixDisabledMonitorVariables = new List<string>(keys);
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static FenixA320Definition Parked(SpeechCapture speech, double v1 = 142, double vr = 148, double v2 = 155)
    {
        var def = new FenixA320Definition();
        def.ProcessSimVarUpdate(FenixTakeoffCallouts.V1Key, v1, speech);
        def.ProcessSimVarUpdate(FenixTakeoffCallouts.VrKey, vr, speech);
        def.ProcessSimVarUpdate(FenixTakeoffCallouts.V2Key, v2, speech);
        def.ProcessSimVarUpdate(FenixTakeoffCallouts.IasKey, 5, speech);
        return def;
    }

    private static void Roll(FenixA320Definition def, SpeechCapture speech, params double[] samples)
    {
        foreach (var ias in samples) def.ProcessSimVarUpdate(FenixTakeoffCallouts.IasKey, ias, speech);
    }

    [Fact]
    public void Keys_name_the_fenix_speed_rows()
    {
        Assert.Equal("FENIX_TAKEOFF_CALLOUT_IAS", FenixTakeoffCallouts.IasKey);
        Assert.Equal("N_MISC_PERF_TO_V1", FenixTakeoffCallouts.V1Key);
        Assert.Equal("N_MISC_PERF_TO_VR", FenixTakeoffCallouts.VrKey);
        Assert.Equal("N_MISC_PERF_TO_V2", FenixTakeoffCallouts.V2Key);
        Assert.Equal(FenixTakeoffCallouts.V1Key, FenixTakeoffCallouts.Keys.MuteKeyFor("V1"));
        Assert.Equal(FenixTakeoffCallouts.VrKey, FenixTakeoffCallouts.Keys.MuteKeyFor("Rotate"));
        Assert.Equal(FenixTakeoffCallouts.V2Key, FenixTakeoffCallouts.Keys.MuteKeyFor("V2"));
    }

    [Fact]
    public void Definition_registers_the_feed()
    {
        var def = new FenixA320Definition();
        var ias = def.GetVariables()[FenixTakeoffCallouts.IasKey];
        Assert.Equal("AIRSPEED INDICATED", ias.Name);
        Assert.Equal("knots", ias.Units);
        Assert.Equal(SimVarType.SimVar, ias.Type);
        Assert.Equal(UpdateFrequency.Continuous, ias.UpdateFrequency);
        Assert.True(ias.IsAnnounced);
        Assert.True(ias.ExcludeFromBatch);
        Assert.True(ias.HighFrequency);
        Assert.True(ias.ExcludeFromMonitorManager);
        Assert.Equal(FenixTakeoffCallouts.IasKey, def.TakeoffCalloutFeedKey);
        Assert.True(def.IsMuteWrapExempt(FenixTakeoffCallouts.IasKey));
    }

    [Theory]
    [InlineData("N_MISC_PERF_TO_V1")]
    [InlineData("N_MISC_PERF_TO_VR")]
    [InlineData("N_MISC_PERF_TO_V2")]
    public void Speed_rows_read_not_set_below_1(string key)
    {
        var row = new FenixA320Definition().GetVariables()[key];
        Assert.Equal(1, row.NotSetBelow);
        Assert.Equal("knots", row.Units);
        Assert.True(row.IsNotSet(0));
        Assert.False(row.IsNotSet(140));
    }

    [Fact]
    public void Takeoff_assist_has_no_fenix_vspeed_code()
    {
        string root = RepoRoot();
        foreach (var rel in new[] { "Services/TakeoffAssistManager.cs", "MainForm.Hotkeys.cs" })
        {
            string text = File.ReadAllText(Path.Combine(root, "MSFSBlindAssist", rel));
            Assert.DoesNotContain("FenixVSpeeds", text);
            Assert.DoesNotContain("N_MISC_PERF_TO_V1", text);
        }
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

        Assert.Equal(new[] { "V1", "Rotate", "V2" }, speech.Interrupts);
    }

    [Fact]
    public void The_v_speeds_still_reach_the_monitor()
    {
        var def = new FenixA320Definition();
        Assert.False(def.ProcessSimVarUpdate(FenixTakeoffCallouts.V1Key, 142, new SpeechCapture()));
        Assert.False(def.ProcessSimVarUpdate(FenixTakeoffCallouts.VrKey, 148, new SpeechCapture()));
        Assert.False(def.ProcessSimVarUpdate(FenixTakeoffCallouts.V2Key, 155, new SpeechCapture()));
        Assert.True(def.ProcessSimVarUpdate(FenixTakeoffCallouts.IasKey, 5, new SpeechCapture()));
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
        Mute(FenixTakeoffCallouts.V1Key);
        var speech = new SpeechCapture();
        var def = Parked(speech);

        Roll(def, speech, 100, 143, 149);
        def.ProcessSimVarUpdate("SIM_ON_GROUND", 0, speech);
        Roll(def, speech, 156);

        Assert.Equal(new[] { "Rotate", "V2" }, speech.Interrupts);
    }

    [Fact]
    public void A_suppressed_announcer_calls_nothing()
    {
        var speech = new SpeechCapture();
        var def = Parked(speech);

        speech.Suppressed = true;
        Roll(def, speech, 143);

        Assert.Empty(speech.Interrupts);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-1, -1, 0)]
    public void Cleared_v_speeds_leave_the_roll_silent(double v1, double vr, double v2)
    {
        var speech = new SpeechCapture();
        var def = Parked(speech, v1, vr, v2);

        Roll(def, speech, 60, 143, 149, 156);

        Assert.Empty(speech.Interrupts);
    }

    [Fact]
    public void A_context_reset_drops_the_arm_so_a_flight_loaded_into_the_cruise_calls_nothing()
    {
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
        Roll(def, speech, 150);
        Assert.Empty(speech.Interrupts);

        Roll(def, speech, 5, 143);
        Assert.Equal(new[] { "V1" }, speech.Interrupts);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MSFSBlindAssist.sln"))) return dir.FullName;
        throw new InvalidOperationException($"MSFSBlindAssist.sln was not found above {AppContext.BaseDirectory}");
    }
}

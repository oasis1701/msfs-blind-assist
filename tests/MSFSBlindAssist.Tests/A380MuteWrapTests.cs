// The FBW A380 inside MainForm's Ctrl+M mute wrap (DefAnnounceMuteSets), which it joined on
// 2026-09-25. The wrap assumes a branch speaks only for its own row. Most A380 branches check their
// row themselves, and several never did (the baro, spoilers, minimums and weight-unit call-outs) —
// the wrap is what makes those rows mute. But a few branches also speak a call-out ANOTHER row
// owns: wrapped, muting "Vertical Mode" or "Armed Vertical Modes" silenced "Altitude Mode" too.
// IsMuteWrapExempt keeps those out of the wrap. The baro rows are pinned in A380BaroMuteTests.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

[Collection("SettingsManagerGlobalState")]
public class A380MuteWrapTests : IDisposable
{
    private const string VerticalMode = "A32NX_FMA_VERTICAL_MODE";
    private const string AltitudeModeRow = "A32NX_FCU_ALT_MANAGED";
    private const string ManagedPhrase = "Altitude Mode: Managed";
    private const int Alt = 10;        // FMA vertical mode ALT: level at the FCU altitude (selected)
    private const int Clb = 22;        // FMA vertical mode CLB (managed)
    private const int ClbArmed = 4;    // FMA armed vertical bitmask: CLB armed (managed)

    private readonly List<string> _savedMutes;

    public A380MuteWrapTests()
    {
        // The definition checks its local rows against SettingsManager.Current, so MainForm's wrap
        // is given the same list here.
        _savedMutes = SettingsManager.Current.A380DisabledMonitorVariables;
        Mute();
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

    private static void Deliver(FlyByWireA380Definition def, GatedSpeechCapture speech, string key, double value) =>
        MuteWrap.Deliver(def, speech, SettingsManager.Current, key, value);

    /// <summary>Both inputs reported, level at the selected altitude: the tracker's baseline, which says nothing.</summary>
    private static FlyByWireA380Definition LevelAtSelectedAltitude(GatedSpeechCapture speech)
    {
        var def = new FlyByWireA380Definition();
        Deliver(def, speech, ArmedAltitudeMode.ArmedVerticalKey, 0);
        Deliver(def, speech, VerticalMode, Alt);
        Assert.Empty(speech.All);
        return def;
    }

    // ---- A row that speaks another row's call-out ----

    [Fact]
    public void Muting_vertical_mode_leaves_the_altitude_mode_call_out()
    {
        Mute(VerticalMode);
        var speech = new GatedSpeechCapture();
        var def = LevelAtSelectedAltitude(speech);

        Deliver(def, speech, VerticalMode, Clb);

        Assert.Equal(new[] { ManagedPhrase }, speech.All);
    }

    [Fact]
    public void Muting_armed_vertical_modes_silences_climb_armed_but_not_the_altitude_mode()
    {
        Mute(ArmedAltitudeMode.ArmedVerticalKey);
        var speech = new GatedSpeechCapture();
        var def = LevelAtSelectedAltitude(speech);

        Deliver(def, speech, ArmedAltitudeMode.ArmedVerticalKey, ClbArmed);

        Assert.Equal(new[] { ManagedPhrase }, speech.All);
    }

    [Fact]
    public void Arming_climb_with_nothing_muted_speaks_both_rows()
    {
        var speech = new GatedSpeechCapture();
        var def = LevelAtSelectedAltitude(speech);

        Deliver(def, speech, ArmedAltitudeMode.ArmedVerticalKey, ClbArmed);

        Assert.Equal(new[] { "Climb armed", ManagedPhrase }, speech.All);
    }

    [Theory]
    [InlineData(VerticalMode, Clb)]
    [InlineData(ArmedAltitudeMode.ArmedVerticalKey, ClbArmed)]
    public void The_altitude_mode_row_still_mutes_its_own_call_out(string key, int value)
    {
        Mute(AltitudeModeRow);
        var speech = new GatedSpeechCapture();
        var def = LevelAtSelectedAltitude(speech);

        Deliver(def, speech, key, value);

        Assert.DoesNotContain(ManagedPhrase, speech.All);
    }

    // ---- Rows whose branches never checked their mute: the wrap is what makes them work ----

    public static TheoryData<string, double, double, string> UngatedRows() => new()
    {
        { "A32NX_SPOILERS_HANDLE_POSITION", 0.0, 0.5, "Spoilers 50 percent" },
        { "AIRLINER_DECISION_HEIGHT", 200.0, 250.0, "Decision height 250 feet" },
        { "AIRLINER_MINIMUM_DESCENT_ALTITUDE", 500.0, 600.0, "Baro minimum 600 feet" },
        { "A32NX_EFB_USING_METRIC_UNIT", 0.0, 1.0, "Weight units kilograms" },
    };

    [Theory]
    [MemberData(nameof(UngatedRows))]
    public void A_row_that_never_checked_its_mute_now_mutes(string key, double first, double second, string phrase)
    {
        var speech = new GatedSpeechCapture();
        var def = new FlyByWireA380Definition();
        Deliver(def, speech, key, first);
        speech.All.Clear();                       // a baseline, or a first entry spoken before the mute

        Deliver(def, speech, key, second);
        Assert.Equal(new[] { phrase }, speech.All);

        Mute(key);
        speech.All.Clear();
        Deliver(def, speech, key, first);
        Deliver(def, speech, key, second);
        Assert.Empty(speech.All);
    }

    // ---- The exemption list ----

    private static readonly string[] ExemptKeys =
    {
        "A32NX_FMA_VERTICAL_MODE", "A32NX_FMA_LATERAL_MODE", "A32NX_FMA_VERTICAL_ARMED",
        A380TakeoffCallouts.IasKey,
        "A32NX_FCU_EFIS_L_VORD_LIGHT_ON", "A32NX_FCU_EFIS_R_VORD_LIGHT_ON",
        "A32NX_FCU_EFIS_L_NDB_LIGHT_ON", "A32NX_FCU_EFIS_R_NDB_LIGHT_ON",
        "A32NX_EWD_LOWER_LEFT_LINE_1", "A32NX_EWD_LOWER_RIGHT_LINE_10",
    };

    // Their call-outs are their own rows' — including the rows those exempt branches speak FOR.
    private static readonly string[] WrappedKeys =
    {
        AltitudeModeRow, "A32NX_FMA_LATERAL_ARMED", "PFD_V1", "PFD_VR", "PFD_V2",
        "A32NX_FCU_EFIS_L_WPT_LIGHT_ON", "A32NX_FCU_LEFT_EIS_BARO_HPA", "A32NX_SPOILERS_HANDLE_POSITION",
        "PFD_AUTOLAND", "A32NX_AUTOTHRUST_TLA:1", "FMA_FG_ALERTS", "A32NX_FMGC_FLIGHT_PHASE",
    };

    [Fact]
    public void Every_exempt_variable_is_left_unwrapped_and_every_other_is_wrapped()
    {
        Mute(ExemptKeys.Concat(WrappedKeys).ToArray());
        var def = new FlyByWireA380Definition();

        foreach (var key in ExemptKeys)
            Assert.False(DefAnnounceMuteSets.ShouldWrap(def, key, SettingsManager.Current), key);
        foreach (var key in WrappedKeys)
            Assert.True(DefAnnounceMuteSets.ShouldWrap(def, key, SettingsManager.Current), key);
    }

    [Fact]
    public void Every_exempt_variable_is_registered()
    {
        // A renamed variable would leave its exemption naming nothing, and its branch wrapped again.
        var vars = new FlyByWireA380Definition().GetVariables();
        foreach (var key in ExemptKeys)
            Assert.True(vars.ContainsKey(key), key);
    }
}

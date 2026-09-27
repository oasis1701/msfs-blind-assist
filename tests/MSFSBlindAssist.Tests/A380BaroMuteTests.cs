// The A380's baro call-outs — the setting ("Captain altimeter 1020 hectopascals"), STD/QNH and the
// hPa/inHg unit — are announced from INSIDE ProcessSimVarUpdate, which returns true, so MainForm's
// generic Ctrl+M gate never sees them. Every other self-announcing airframe is muted by MainForm
// wrapping ProcessSimVarUpdate in announcer.Suppressed (DefAnnounceMuteSets); the A380 was left out
// and these branches never checked A380DisabledMonitorVariablesSet themselves, so their six Ctrl+M
// rows muted nothing (found 2026-09-25).

using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

public class A380BaroMuteTests
{
    /// <summary>What MainForm.OnSimVarUpdated does around the definition (its Step 2.5).</summary>
    private static void Deliver(FlyByWireA380Definition def, ScreenReaderAnnouncer announcer,
        UserSettings settings, string key, double value) =>
        MuteWrap.Deliver(def, announcer, settings, key, value);

    private static UserSettings Muting(params string[] keys)
    {
        var s = new UserSettings { A380DisabledMonitorVariables = new List<string>(keys) };
        s.RebuildDisabledMonitorVariableCaches();
        return s;
    }

    private static double Hpa(float value) =>
        (double)((3UL << 32) | BitConverter.SingleToUInt32Bits(value));

    public static TheoryData<string> BaroKeys() => new()
    {
        "A32NX_FCU_LEFT_EIS_BARO_HPA", "A32NX_FCU_RIGHT_EIS_BARO_HPA",
        "A32NX_FCU_LEFT_EIS_BARO_IS_STD", "A32NX_FCU_RIGHT_EIS_BARO_IS_STD",
        "A32NX_FCU_EFIS_L_BARO_IS_INHG", "A32NX_FCU_EFIS_R_BARO_IS_INHG",
    };

    [Theory]
    [MemberData(nameof(BaroKeys))]
    public void Every_baro_call_out_has_a_ctrl_m_row(string key)
    {
        var def = new FlyByWireA380Definition().GetVariables()[key];

        Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
        Assert.True(def.IsAnnounced);
        Assert.False(def.ExcludeFromMonitorManager);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void The_setting_row_mutes_the_setting_call_out(bool muted, int spoken)
    {
        var def = new FlyByWireA380Definition();
        var speech = new GatedSpeechCapture();
        var settings = muted ? Muting("A32NX_FCU_LEFT_EIS_BARO_HPA") : Muting();

        Deliver(def, speech, settings, "A32NX_FCU_LEFT_EIS_BARO_HPA", Hpa(1013));   // seeds silently
        Deliver(def, speech, settings, "A32NX_FCU_LEFT_EIS_BARO_HPA", Hpa(1020));

        Assert.Equal(spoken, speech.All.Count);
        if (spoken == 1) Assert.Equal("Captain altimeter 1020 hectopascals", speech.All[0]);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void The_std_row_mutes_the_std_call_out(bool muted, int spoken)
    {
        var def = new FlyByWireA380Definition();
        var speech = new GatedSpeechCapture();
        var settings = muted ? Muting("A32NX_FCU_RIGHT_EIS_BARO_IS_STD") : Muting();

        Deliver(def, speech, settings, "A32NX_FCU_RIGHT_EIS_BARO_IS_STD", 0);        // baseline
        Deliver(def, speech, settings, "A32NX_FCU_RIGHT_EIS_BARO_IS_STD", 1);

        Assert.Equal(spoken, speech.All.Count);
        if (spoken == 1) Assert.Equal("First officer altimeter standard", speech.All[0]);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void The_unit_row_mutes_the_unit_call_out(bool muted, int spoken)
    {
        var def = new FlyByWireA380Definition();
        var speech = new GatedSpeechCapture();
        var settings = muted ? Muting("A32NX_FCU_EFIS_L_BARO_IS_INHG") : Muting();

        Deliver(def, speech, settings, "A32NX_FCU_EFIS_L_BARO_IS_INHG", 0);          // baseline, hPa
        Deliver(def, speech, settings, "A32NX_FCU_EFIS_L_BARO_IS_INHG", 1);

        Assert.Equal(spoken, speech.All.Count);
    }

    [Fact]
    public void A_muted_row_leaves_the_other_side_speaking()
    {
        var def = new FlyByWireA380Definition();
        var speech = new GatedSpeechCapture();
        var settings = Muting("A32NX_FCU_LEFT_EIS_BARO_HPA");

        Deliver(def, speech, settings, "A32NX_FCU_LEFT_EIS_BARO_HPA", Hpa(1013));
        Deliver(def, speech, settings, "A32NX_FCU_RIGHT_EIS_BARO_HPA", Hpa(1013));
        Deliver(def, speech, settings, "A32NX_FCU_LEFT_EIS_BARO_HPA", Hpa(1020));
        Deliver(def, speech, settings, "A32NX_FCU_RIGHT_EIS_BARO_HPA", Hpa(1020));

        Assert.Equal(new[] { "First officer altimeter 1020 hectopascals" }, speech.All);
    }
}

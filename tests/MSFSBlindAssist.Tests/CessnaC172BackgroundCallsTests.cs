// Two background calls the C172 speaks itself (2026-10-19): "Ident off" when the transponder's ident ends
// by itself about 18 s after the press, and the ELT going off by itself (C172-26), measured with ELT_ON,
// which sets ELT ACTIVATED with the switch still at ARM, as a hard landing would. Both keep their own
// baseline and speak queued, so their Ctrl+M rows mute them through MainForm's wrap (VAR-8).

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class CessnaC172BackgroundCallsTests
{
    private const string Ident = CessnaC172BaseDefinition.IdentStateKey;
    private const string Elt = CessnaC172G1000Definition.EltActivatedKey;
    private const double SwitchOn = 0, SwitchArm = 1;

    [Theory]
    [InlineData(1.0, 0.0, true)]
    [InlineData(0.0, 1.0, false)]   // the start: the Ident button says it
    [InlineData(0.0, 0.0, false)]
    [InlineData(1.0, 1.0, false)]
    [InlineData(null, 0.0, false)]  // the first reading is a baseline
    public void Only_the_end_of_an_ident_is_a_call(double? was, double now, bool ended) =>
        Assert.Equal(ended, CessnaC172BaseDefinition.IdentEnded(was, now));

    [Fact]
    public void The_ident_says_ident_off_once_when_it_ends_and_a_new_flight_starts_afresh()
    {
        var def = new CessnaC172G1000Definition();
        var speech = new GatedSpeechCapture();

        Assert.True(def.ProcessSimVarUpdate(Ident, 0, speech));   // true: the generic announcer stays out
        def.ProcessSimVarUpdate(Ident, 1, speech);
        def.ProcessSimVarUpdate(Ident, 1, speech);
        def.ProcessSimVarUpdate(Ident, 0, speech);
        Assert.Equal(new[] { "Ident off" }, speech.All);

        def.ProcessSimVarUpdate(Ident, 1, speech);
        def.OnSimContextReset();
        def.ProcessSimVarUpdate(Ident, 0, speech);   // a flight load cut it short: not an ident ending
        Assert.Single(speech.All);
    }

    [Theory]
    [InlineData(0.0, 1.0, SwitchArm, false, "ELT activated", true)]    // by itself: ELT_ON, or a hard landing
    [InlineData(0.0, 1.0, null, false, "ELT activated", true)]         // switch not read yet: not the pilot's
    [InlineData(0.0, 1.0, SwitchOn, false, null, false)]               // the pilot's own switch, said by the switch
    [InlineData(1.0, 0.0, SwitchArm, true, "ELT stopped transmitting", false)]
    [InlineData(1.0, 0.0, SwitchArm, false, null, false)]              // the pilot's own transmission ending
    [InlineData(1.0, 1.0, SwitchArm, true, null, true)]
    [InlineData(null, 1.0, SwitchArm, false, null, false)]             // the first reading is a baseline
    public void The_elt_is_spoken_only_when_it_goes_off_by_itself_and_when_that_stops(
        double? was, double now, double? eltSwitch, bool announced, string? call, bool nowAnnounced)
    {
        var (c, a) = CessnaC172G1000Definition.EltActivationCall(was, now, eltSwitch, announced);
        Assert.Equal(call, c);
        Assert.Equal(nowAnnounced, a);
    }

    [Fact]
    public void An_elt_going_off_by_itself_is_heard_and_its_reset_confirmed_but_the_pilots_own_is_quiet()
    {
        var def = new CessnaC172G1000Definition();
        var speech = new GatedSpeechCapture();

        def.ProcessSimVarUpdate("C172G_ELT", SwitchArm, speech);
        Assert.True(def.ProcessSimVarUpdate(Elt, 0, speech));
        def.ProcessSimVarUpdate(Elt, 1, speech);                      // measured: ELT_ON with the switch at ARM
        def.ProcessSimVarUpdate("C172G_ELT", SwitchArm, speech);      // the reset, ARM written again
        def.ProcessSimVarUpdate(Elt, 0, speech);
        Assert.Equal(new[] { "ELT activated", "ELT stopped transmitting" }, speech.All);

        def.ProcessSimVarUpdate("C172G_ELT", SwitchOn, speech);       // the pilot switches it on
        def.ProcessSimVarUpdate(Elt, 1, speech);
        def.ProcessSimVarUpdate("C172G_ELT", SwitchArm, speech);
        def.ProcessSimVarUpdate(Elt, 0, speech);
        Assert.Equal(2, speech.All.Count);
    }

    [Fact]
    public void Their_ctrl_m_rows_mute_them()
    {
        var def = new CessnaC172G1000Definition();
        var speech = new GatedSpeechCapture();
        var settings = new UserSettings { C172DisabledMonitorVariables = new List<string> { Ident, Elt } };
        settings.RebuildDisabledMonitorVariableCaches();

        foreach (var (key, value) in new[] { (Ident, 0.0), (Ident, 1.0), (Ident, 0.0), (Elt, 0.0), (Elt, 1.0) })
            MuteWrap.Deliver(def, speech, settings, key, value);

        Assert.Empty(speech.All);
    }

    [Fact]
    public void The_ident_button_keeps_its_own_read_and_only_the_watch_is_batched()
    {
        var vars = new CessnaC172G1000Definition().GetVariables();
        Assert.Equal("TRANSPONDER IDENT:1", vars["C172_XPNDR_IDENTING"].Name);
        Assert.Equal(SimConnect.UpdateFrequency.OnRequest, vars["C172_XPNDR_IDENTING"].UpdateFrequency);
        Assert.Equal("TRANSPONDER IDENT:1", vars[Ident].Name);
        Assert.Equal(SimConnect.UpdateFrequency.Continuous, vars[Ident].UpdateFrequency);
        Assert.True(vars[Ident].IsAnnounced);
        Assert.Equal("ELT ACTIVATED", vars[Elt].Name);
        Assert.True(vars[Elt].IsAnnounced);
    }
}

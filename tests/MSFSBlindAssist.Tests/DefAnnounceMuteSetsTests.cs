// Which Ctrl+M list MainForm consults when it wraps a definition's ProcessSimVarUpdate in
// announcer.Suppressed — the only way a Ctrl+M mute reaches an announcement a definition makes from
// INSIDE ProcessSimVarUpdate, since such a definition returns true and exits before the generic
// monitor gate. The FBW A380 was the one self-announcing airframe left out, so its baro value, STD
// and unit call-outs (and the other branches that never checked the list themselves) kept speaking
// with their rows unticked (found 2026-09-25).

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests;

public class DefAnnounceMuteSetsTests
{
    // One distinct marker per list, so a test can tell WHICH list came back.
    private static UserSettings Marked()
    {
        var s = new UserSettings
        {
            A380DisabledMonitorVariables = new() { "a380" },
            A32NXDisabledMonitorVariables = new() { "a32nx" },
            HS787DisabledMonitorVariables = new() { "hs787" },
            IFlyDisabledMonitorVariables = new() { "ifly" },
            PMDGDisabledMonitorVariables = new() { "pmdg" },
            Md11DisabledMonitorVariables = new() { "md11" },
            FenixDisabledMonitorVariables = new() { "fenix" },
        };
        s.RebuildDisabledMonitorVariableCaches();
        return s;
    }

    [Theory]
    [InlineData("FBW_A380", "a380")]
    [InlineData("A320", "a32nx")]
    [InlineData("HW_A330", "a32nx")]
    [InlineData("HS_787", "hs787")]
    [InlineData("IFLY_737MAX8", "ifly")]
    [InlineData("PMDG_737", "pmdg")]
    [InlineData("PMDG_777", "pmdg")]
    [InlineData("TFDI_MD11", "md11")]
    public void Each_self_announcing_airframe_is_muted_by_its_own_list(string aircraftCode, string marker)
    {
        var set = DefAnnounceMuteSets.For(aircraftCode, Marked());

        Assert.NotNull(set);
        Assert.Contains(marker, set!);
        Assert.Single(set!);
    }

    [Fact]
    public void The_fenix_announces_on_the_generic_path_and_is_not_wrapped()
    {
        // Its Ctrl+M gate lives in the generic monitor path further down MainForm; unchanged.
        Assert.Null(DefAnnounceMuteSets.For("FENIX_A320CEO", Marked()));
    }

    [Fact]
    public void An_a380_baro_row_mutes_its_call_out()
    {
        var s = new UserSettings { A380DisabledMonitorVariables = new() { "A32NX_FCU_LEFT_EIS_BARO_HPA" } };
        s.RebuildDisabledMonitorVariableCaches();

        Assert.True(DefAnnounceMuteSets.IsMuted("FBW_A380", "A32NX_FCU_LEFT_EIS_BARO_HPA", s));
        Assert.False(DefAnnounceMuteSets.IsMuted("FBW_A380", "A32NX_FCU_RIGHT_EIS_BARO_HPA", s));
        Assert.False(DefAnnounceMuteSets.IsMuted("A320", "A32NX_FCU_LEFT_EIS_BARO_HPA", s));
    }

    [Fact]
    public void A_definition_with_no_exemptions_is_wrapped_exactly_where_it_is_muted()
    {
        // The base default: every branch's call-outs are its own row's.
        var def = new FlyByWireA320Definition();
        var s = new UserSettings { A32NXDisabledMonitorVariables = new() { "A32NX_FCU_LEFT_EIS_BARO_HPA" } };
        s.RebuildDisabledMonitorVariableCaches();

        Assert.False(def.IsMuteWrapExempt("A32NX_FCU_LEFT_EIS_BARO_HPA"));
        Assert.True(DefAnnounceMuteSets.ShouldWrap(def, "A32NX_FCU_LEFT_EIS_BARO_HPA", s));
        Assert.False(DefAnnounceMuteSets.ShouldWrap(def, "A32NX_FCU_RIGHT_EIS_BARO_HPA", s));
    }

    [Fact]
    public void An_exempt_variable_is_never_wrapped_even_when_muted()
    {
        // The A380's vertical mode speaks the altitude mode, which "Altitude Mode" owns.
        var def = new FlyByWireA380Definition();
        var s = new UserSettings { A380DisabledMonitorVariables = new() { "A32NX_FMA_VERTICAL_MODE" } };
        s.RebuildDisabledMonitorVariableCaches();

        Assert.True(DefAnnounceMuteSets.IsMuted(def.AircraftCode, "A32NX_FMA_VERTICAL_MODE", s));
        Assert.False(DefAnnounceMuteSets.ShouldWrap(def, "A32NX_FMA_VERTICAL_MODE", s));
    }
}

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The shared "glideslope alive / lost" call-out reads the sim's NAV 1, which BlueBird's 757 never
/// tunes ([B757-5], ruling R56): the 757 registers no glideslope row and never
/// speaks it, while an aircraft on the stock receivers keeps both.
/// </summary>
public class Bbs757GlideslopeGateTests
{
    private const string GlideslopeKey = "MON_GlideSlopeAlive";

    [Fact]
    public void Bbs757_registers_no_glideslope_row()
    {
        var def = new BBS757Definition();

        Assert.False(def.UsesStockNavReceivers);
        Assert.DoesNotContain(GlideslopeKey, def.GetVariables().Keys);
        Assert.DoesNotContain(def.MonitorRows(), r => r.Key == GlideslopeKey);   // no Ctrl+M row ([VAR-9])
    }

    [Fact]
    public void Pmdg737_keeps_its_glideslope_row()
    {
        var def = new PMDG737Definition();

        Assert.True(def.UsesStockNavReceivers);
        var row = def.GetVariables()[GlideslopeKey];
        Assert.Equal("NAV HAS GLIDE SLOPE:1", row.Name);
        Assert.Equal(UpdateFrequency.Continuous, row.UpdateFrequency);
        Assert.True(row.IsAnnounced);
        Assert.False(row.ExcludeFromMonitorManager);
    }

    [Fact]
    public void Pmdg737_still_speaks_glideslope_alive_and_lost()
    {
        var def = new PMDG737Definition();
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate(GlideslopeKey, 1, speech);
        def.ProcessSimVarUpdate(GlideslopeKey, 0, speech);

        Assert.Equal(new[] { "Glideslope alive", "Glideslope lost" }, speech.All);
    }

    [Fact]
    public void Bbs757_never_speaks_glideslope_alive()
    {
        var def = new BBS757Definition { NestRunning = () => true };
        var speech = new SpeechCapture();

        def.ProcessSimVarUpdate(GlideslopeKey, 1, speech);
        def.ProcessSimVarUpdate(GlideslopeKey, 0, speech);

        Assert.Empty(speech.All);
    }
}

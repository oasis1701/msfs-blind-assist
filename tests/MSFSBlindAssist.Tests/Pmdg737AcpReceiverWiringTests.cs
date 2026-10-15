// The PMDG 737 definition's wiring of its ACP VHF receiver buttons (Pmdg737AcpReceivers, whose own
// bookkeeping Pmdg737AcpReceiversTests pins): every event resolves, so a press never falls through
// to MainForm's L:var fallback; the Calls panel holds the buttons, each labelled On/Off by its own
// derived state; the receiver fields stay out of the Ctrl+M wrap, and the seat's list checks its row.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests;

[Collection("SettingsManagerGlobalState")]
public class Pmdg737AcpReceiverWiringTests : IDisposable
{
    private readonly List<string> _savedMutes;

    public Pmdg737AcpReceiverWiringTests()
    {
        _savedMutes = SettingsManager.Current.PMDGDisabledMonitorVariables;
        Mute();   // nothing muted unless a test says so, whatever this machine's own settings hold
    }

    public void Dispose()
    {
        SettingsManager.Current.PMDGDisabledMonitorVariables = _savedMutes;
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    private static void Mute(params string[] keys)
    {
        SettingsManager.Current.PMDGDisabledMonitorVariables = new List<string>(keys);
        SettingsManager.Current.RebuildDisabledMonitorVariableCaches();
    }

    [Fact]
    public void Every_receiver_event_is_in_the_737_event_table()
    {
        // A miss would skip the receiver branch and press nothing in the cockpit.
        foreach (var r in Pmdg737AcpReceivers.All)
            Assert.True(PMDG737Definition.EventIds.ContainsKey(r.EventName), r.EventName);
    }

    [Fact]
    public void The_Calls_panel_holds_every_receiver_button_labelled_by_its_own_state()
    {
        var def = new PMDG737Definition();
        var calls = def.GetPanelControls()["Calls"];
        foreach (var r in Pmdg737AcpReceivers.All)
        {
            Assert.Contains(r.Key, calls);
            var v = def.GetVariables()[r.Key];
            Assert.Equal(SimVarType.PMDGVar, v.Type);
            Assert.True(v.RenderAsButton);
            Assert.Equal(r.DisplayName, v.DisplayName);
            // PMDGNG3DataManager publishes the receiver's bit under the button's key (the field name
            // MainForm maps back to the key), and the button reads its label from it.
            Assert.Equal(r.Key, v.Name);
            Assert.Equal(r.Key, v.StateVariable);
        }
    }

    [Theory]
    [InlineData("COMM_ReceiverSwitches_0", true)]
    [InlineData("COMM_ReceiverSwitches_1", true)]
    [InlineData("COMM_ReceiverSwitches_2", true)]
    [InlineData("COMM_SelectedMic_0", false)]
    public void Only_the_receiver_fields_are_mute_wrap_exempt(string varName, bool exempt)
    {
        Assert.Equal(exempt, new PMDG737Definition().IsMuteWrapExempt(varName));
    }

    [Fact]
    public void A_background_change_speaks_the_seats_list()
    {
        var speech = new SpeechCapture();
        Assert.True(new PMDG737Definition().ProcessSimVarUpdate("COMM_ReceiverSwitches_0", 0x3, speech));
        Assert.Equal(["Captain receivers: VHF1, VHF2"], speech.All);
    }

    [Fact]
    public void A_muted_row_silences_the_seats_list()
    {
        // The wrap does not cover this branch (IsMuteWrapExempt), so the branch checks its own row.
        Mute("COMM_ReceiverSwitches_1");
        var speech = new SpeechCapture();
        Assert.True(new PMDG737Definition().ProcessSimVarUpdate("COMM_ReceiverSwitches_1", 0x2, speech));
        Assert.Empty(speech.All);
    }

    [Fact]
    public void A_receiver_buttons_state_update_is_consumed_silently()
    {
        // The derived On/Off value relabels the button (MainForm's StateVariable path) and is never spoken.
        var speech = new SpeechCapture();
        Assert.True(new PMDG737Definition().ProcessSimVarUpdate("ACP_Capt_RecVHF2", 1, speech));
        Assert.Empty(speech.All);
    }
}

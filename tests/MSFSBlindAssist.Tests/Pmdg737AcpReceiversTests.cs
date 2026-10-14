// Characterization tests for Pmdg737AcpReceivers (Aircraft/Pmdg737AcpReceivers.cs): the PMDG
// 737's ACP VHF receiver buttons. Pins the key/event/bit table, the target a press sends (the
// opposite of the newest unconfirmed press, else of the cached bitmask: the CDA parameter is the
// target state, measured 2026-10-14), and which read-back a COMM_ReceiverSwitches update speaks.

namespace MSFSBlindAssist.Tests;

using MSFSBlindAssist.Aircraft;

public class Pmdg737AcpReceiversTests
{
    private const string Capt = "COMM_ReceiverSwitches_0";
    private const string Fo = "COMM_ReceiverSwitches_1";

    private static Pmdg737AcpReceivers.Receiver R(string key) => Pmdg737AcpReceivers.ByKey[key];

    [Theory]
    [InlineData("ACP_Capt_RecVHF1", "Captain VHF 1 Receiver", "EVT_ACP_CAPT_REC_VHF1", Capt, 0x1u)]
    [InlineData("ACP_Capt_RecVHF2", "Captain VHF 2 Receiver", "EVT_ACP_CAPT_REC_VHF2", Capt, 0x2u)]
    [InlineData("ACP_Capt_RecVHF3", "Captain VHF 3 Receiver", "EVT_ACP_CAPT_REC_VHF3", Capt, 0x4u)]
    [InlineData("ACP_FO_RecVHF1", "First Officer VHF 1 Receiver", "EVT_ACP_FO_REC_VHF1", Fo, 0x1u)]
    [InlineData("ACP_FO_RecVHF2", "First Officer VHF 2 Receiver", "EVT_ACP_FO_REC_VHF2", Fo, 0x2u)]
    [InlineData("ACP_FO_RecVHF3", "First Officer VHF 3 Receiver", "EVT_ACP_FO_REC_VHF3", Fo, 0x4u)]
    public void Table_MapsEachButtonToItsEventAndBit(string key, string name, string evt, string field, uint bit)
    {
        var r = R(key);
        Assert.Equal(name, r.DisplayName);
        Assert.Equal(evt, r.EventName);
        Assert.Equal(field, r.StateField);
        Assert.Equal(bit, r.Bit);
    }

    [Fact]
    public void Table_HasSixButtons()
    {
        Assert.Equal(6, Pmdg737AcpReceivers.All.Count);
    }

    [Theory]
    [InlineData(0x0u, true)]      // off -> turn on
    [InlineData(0x2u, false)]     // VHF 2 on -> turn off
    [InlineData(0x1u, true)]      // only VHF 1 on -> VHF 2 turns on
    [InlineData(0x2103u, false)]  // VHF 2 among others -> turn off
    public void TargetFor_NoPendingPress_IsTheOppositeOfTheCachedBit(uint cached, bool turnOn)
    {
        Assert.Equal(turnOn, Pmdg737AcpReceivers.TargetFor([], R("ACP_Capt_RecVHF2"), cached));
    }

    [Fact]
    public void TargetFor_QuickSecondPress_FlipsThePendingTargetNotTheStaleCache()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        var vhf2 = R("ACP_Capt_RecVHF2");
        Pmdg737AcpReceivers.AddPress(pending, new(vhf2, TurnOn: true, AtMs: 0));

        // The cache still shows VHF 2 off; the second press must turn it back off.
        Assert.False(Pmdg737AcpReceivers.TargetFor(pending, vhf2, 0x0u));
    }

    [Fact]
    public void TargetFor_PendingPressOfAnotherReceiver_IsIgnored()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, new(R("ACP_Capt_RecVHF1"), TurnOn: true, AtMs: 0));

        Assert.True(Pmdg737AcpReceivers.TargetFor(pending, R("ACP_Capt_RecVHF2"), 0x0u));
    }

    [Fact]
    public void AddPress_SupersedesAnOlderPressOfTheSameReceiver()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        var vhf2 = R("ACP_Capt_RecVHF2");
        var first = new Pmdg737AcpReceivers.Press(vhf2, TurnOn: true, AtMs: 0);
        Pmdg737AcpReceivers.AddPress(pending, first);
        Pmdg737AcpReceivers.AddPress(pending, new(vhf2, TurnOn: false, AtMs: 100));

        Assert.Single(pending);
        Assert.False(pending[0].TurnOn);
        // The superseded press's timeout finds nothing to report.
        Assert.False(Pmdg737AcpReceivers.ExpireIfUnconfirmed(pending, first));
    }

    [Theory]
    [InlineData(true, 0x2u, "Captain VHF 2 Receiver on")]
    [InlineData(false, 0x0u, "Captain VHF 2 Receiver off")]   // the last receiver off: no "none"
    [InlineData(false, 0x1u, "Captain VHF 2 Receiver off")]
    public void CompleteReadbacks_BitAtTarget_SpeaksThatReceiverOnly(bool turnOn, uint bits, string expected)
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, new(R("ACP_Capt_RecVHF2"), turnOn, AtMs: 0));

        var said = Pmdg737AcpReceivers.CompleteReadbacks(pending, Capt, bits, out bool inFlight);

        Assert.Equal([expected], said);
        Assert.False(inFlight);
        Assert.Empty(pending);
    }

    [Fact]
    public void CompleteReadbacks_BitNotYetAtTarget_StaysInFlight()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, new(R("ACP_Capt_RecVHF2"), TurnOn: true, AtMs: 0));

        var said = Pmdg737AcpReceivers.CompleteReadbacks(pending, Capt, 0x1u, out bool inFlight);

        Assert.Empty(said);
        Assert.True(inFlight);   // caller stays quiet instead of speaking the list
        Assert.Single(pending);
    }

    [Fact]
    public void CompleteReadbacks_TwoPressesInOneUpdate_SpeaksBothOldestFirst()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, new(R("ACP_Capt_RecVHF1"), TurnOn: true, AtMs: 0));
        Pmdg737AcpReceivers.AddPress(pending, new(R("ACP_Capt_RecVHF2"), TurnOn: true, AtMs: 50));

        var said = Pmdg737AcpReceivers.CompleteReadbacks(pending, Capt, 0x3u, out bool inFlight);

        Assert.Equal(["Captain VHF 1 Receiver on", "Captain VHF 2 Receiver on"], said);
        Assert.False(inFlight);
    }

    [Fact]
    public void CompleteReadbacks_OtherSeatsUpdate_LeavesThePressPending()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, new(R("ACP_Capt_RecVHF2"), TurnOn: true, AtMs: 0));

        var said = Pmdg737AcpReceivers.CompleteReadbacks(pending, Fo, 0x2u, out bool inFlight);

        Assert.Empty(said);
        Assert.False(inFlight);  // nothing pending on the FO field: the FO list may speak
        Assert.Single(pending);
    }

    [Fact]
    public void CompleteReadbacks_NoPresses_IsSilentAndNotInFlight()
    {
        var said = Pmdg737AcpReceivers.CompleteReadbacks([], Capt, 0x3u, out bool inFlight);

        Assert.Empty(said);
        Assert.False(inFlight);
    }

    [Fact]
    public void ExpireIfUnconfirmed_UnmovedPress_IsReportedOnce()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        var press = new Pmdg737AcpReceivers.Press(R("ACP_FO_RecVHF2"), TurnOn: true, AtMs: 0);
        Pmdg737AcpReceivers.AddPress(pending, press);

        Assert.True(Pmdg737AcpReceivers.ExpireIfUnconfirmed(pending, press));
        Assert.False(Pmdg737AcpReceivers.ExpireIfUnconfirmed(pending, press));
    }

    [Fact]
    public void ExpireIfUnconfirmed_ConfirmedPress_IsNotReported()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        var press = new Pmdg737AcpReceivers.Press(R("ACP_FO_RecVHF2"), TurnOn: true, AtMs: 0);
        Pmdg737AcpReceivers.AddPress(pending, press);
        Pmdg737AcpReceivers.CompleteReadbacks(pending, Fo, 0x2u, out _);

        Assert.False(Pmdg737AcpReceivers.ExpireIfUnconfirmed(pending, press));
    }
}

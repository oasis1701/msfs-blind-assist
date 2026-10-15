// Characterization tests for Pmdg737AcpReceivers (Aircraft/Pmdg737AcpReceivers.cs): the PMDG
// 737's ACP VHF receiver buttons. Pins the key/event/bit table, the bitmask's spoken labels, the
// target a press sends (the opposite of the newest unconfirmed press, else of the cached bitmask:
// the CDA parameter is the target state, measured 2026-10-14), which read-back a
// COMM_ReceiverSwitches update speaks, and what a press no update completed speaks.

namespace MSFSBlindAssist.Tests;

using MSFSBlindAssist.Aircraft;

public class Pmdg737AcpReceiversTests
{
    private const string Capt = "COMM_ReceiverSwitches_0";
    private const string Fo = "COMM_ReceiverSwitches_1";

    private static Pmdg737AcpReceivers.Receiver R(string key) => Pmdg737AcpReceivers.ByKey[key];

    private static Pmdg737AcpReceivers.Press P(string key, bool turnOn) => new(R(key), turnOn);

    [Theory]
    [InlineData("ACP_Capt_RecVHF1", "Captain VHF1 Receiver", "EVT_ACP_CAPT_REC_VHF1", Capt, 0, 0x1u)]
    [InlineData("ACP_Capt_RecVHF2", "Captain VHF2 Receiver", "EVT_ACP_CAPT_REC_VHF2", Capt, 0, 0x2u)]
    [InlineData("ACP_Capt_RecVHF3", "Captain VHF3 Receiver", "EVT_ACP_CAPT_REC_VHF3", Capt, 0, 0x4u)]
    [InlineData("ACP_FO_RecVHF1", "First Officer VHF1 Receiver", "EVT_ACP_FO_REC_VHF1", Fo, 1, 0x1u)]
    [InlineData("ACP_FO_RecVHF2", "First Officer VHF2 Receiver", "EVT_ACP_FO_REC_VHF2", Fo, 1, 0x2u)]
    [InlineData("ACP_FO_RecVHF3", "First Officer VHF3 Receiver", "EVT_ACP_FO_REC_VHF3", Fo, 1, 0x4u)]
    public void Table_MapsEachButtonToItsEventAndBit(string key, string name, string evt, string field, int seat, uint bit)
    {
        var r = R(key);
        Assert.Equal(name, r.DisplayName);
        Assert.Equal(evt, r.EventName);
        Assert.Equal(field, r.StateField);
        Assert.Equal(seat, r.Seat);
        Assert.Equal(bit, r.Bit);
    }

    [Fact]
    public void Table_HasSixButtons()
    {
        Assert.Equal(6, Pmdg737AcpReceivers.All.Count);
    }

    [Theory]
    [InlineData(0x0u, "none")]
    [InlineData(0x3u, "VHF1, VHF2")]
    [InlineData(0x2103u, "VHF1, VHF2, NAV1, SPKR")]
    [InlineData(0x3FFFu, "VHF1, VHF2, VHF3, HF1, HF2, FLT, SVC, PA, NAV1, NAV2, ADF1, ADF2, MKR, SPKR")]
    public void Describe_NamesTheSelectedReceiversInBitOrder(uint bits, string expected)
    {
        Assert.Equal(expected, Pmdg737AcpReceivers.Describe(bits));
    }

    [Fact]
    public void The_buttons_speak_VHF_as_the_seats_list_does()
    {
        // One spelling in the Calls panel: the mic selector and the seat's list say "VHF2".
        foreach (var r in Pmdg737AcpReceivers.All)
            Assert.Contains(Pmdg737AcpReceivers.Describe(r.Bit), r.DisplayName);
    }

    [Theory]
    [InlineData("ACP_Capt_RecVHF2", new uint[] { 0x2, 0x0, 0x0 }, true)]
    [InlineData("ACP_Capt_RecVHF2", new uint[] { 0x5, 0x2, 0x0 }, false)]   // the FO's VHF2 is not the captain's
    [InlineData("ACP_FO_RecVHF3", new uint[] { 0x0, 0x4, 0x0 }, true)]
    [InlineData("ACP_FO_RecVHF3", new uint[] { 0x4 }, false)]               // short array: off, never a throw
    public void IsOn_ReadsTheReceiversBitInItsSeatsElement(string key, uint[] switches, bool on)
    {
        Assert.Equal(on, Pmdg737AcpReceivers.IsOn(R(key), switches));
    }

    [Fact]
    public void IsOn_NoSnapshotArray_IsOff()
    {
        Assert.False(Pmdg737AcpReceivers.IsOn(R("ACP_Capt_RecVHF1"), null));
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
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF2", turnOn: true));

        // The cache still shows VHF 2 off; the second press must turn it back off.
        Assert.False(Pmdg737AcpReceivers.TargetFor(pending, R("ACP_Capt_RecVHF2"), 0x0u));
    }

    [Fact]
    public void TargetFor_PendingPressOfAnotherReceiver_IsIgnored()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF1", turnOn: true));

        Assert.True(Pmdg737AcpReceivers.TargetFor(pending, R("ACP_Capt_RecVHF2"), 0x0u));
    }

    [Fact]
    public void AddPress_SupersedesAnOlderPressOfTheSameReceiver()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        var first = P("ACP_Capt_RecVHF2", turnOn: true);
        Pmdg737AcpReceivers.AddPress(pending, first);
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF2", turnOn: false));

        Assert.Single(pending);
        Assert.False(pending[0].TurnOn);
        // The superseded press's timeout finds nothing to report.
        Assert.False(Pmdg737AcpReceivers.ExpireIfUnconfirmed(pending, first));
    }

    [Fact]
    public void ExpireIfUnconfirmed_ALookAlikePress_IsNotTheOneWaiting()
    {
        // Each press is its own: an old timer never expires a later press of the same receiver and
        // target, however alike the two are.
        var pending = new List<Pmdg737AcpReceivers.Press>();
        var old = P("ACP_Capt_RecVHF2", turnOn: true);
        var current = P("ACP_Capt_RecVHF2", turnOn: true);
        Pmdg737AcpReceivers.AddPress(pending, current);

        Assert.False(Pmdg737AcpReceivers.ExpireIfUnconfirmed(pending, old));
        Assert.Single(pending);
    }

    [Theory]
    [InlineData(true, 0x0u, 0x2u, "Captain VHF2 Receiver on")]
    [InlineData(false, 0x2u, 0x0u, "Captain VHF2 Receiver off")]   // the last receiver off: no "none"
    [InlineData(false, 0x3u, 0x1u, "Captain VHF2 Receiver off")]
    public void CompleteReadbacks_BitAtTarget_SpeaksThatReceiverOnly(bool turnOn, uint previous, uint bits, string expected)
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF2", turnOn));

        var said = Pmdg737AcpReceivers.CompleteReadbacks(pending, Capt, previous, bits, out bool otherChange);

        Assert.Equal([expected], said);
        Assert.False(otherChange);
        Assert.Empty(pending);
    }

    [Fact]
    public void CompleteReadbacks_PressedBitPassingThrough_StaysQuiet()
    {
        // A quick double press seen between two snapshots: the second press (off) is pending and
        // VHF 2 shows on. The pilot made that change, so neither a read-back nor the list speaks.
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF2", turnOn: false));

        var said = Pmdg737AcpReceivers.CompleteReadbacks(pending, Capt, 0x0u, 0x2u, out bool otherChange);

        Assert.Empty(said);
        Assert.False(otherChange);
        Assert.Single(pending);
    }

    [Fact]
    public void CompleteReadbacks_ABackgroundChangeWhileAPressIsPending_StillSpeaksTheList()
    {
        // VHF 2 pressed (not yet moved, an unpowered ACP say); VHF 1 changes from elsewhere.
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF2", turnOn: true));

        var said = Pmdg737AcpReceivers.CompleteReadbacks(pending, Capt, 0x0u, 0x1u, out bool otherChange);

        Assert.Empty(said);
        Assert.True(otherChange);
        Assert.Single(pending);
    }

    [Fact]
    public void CompleteReadbacks_ABackgroundChangeBesideAReadback_SpeaksBoth()
    {
        // VHF 2's press lands in the same snapshot as a VHF 1 change the pilot did not make.
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF2", turnOn: true));

        var said = Pmdg737AcpReceivers.CompleteReadbacks(pending, Capt, 0x0u, 0x3u, out bool otherChange);

        Assert.Equal(["Captain VHF2 Receiver on"], said);
        Assert.True(otherChange);
    }

    [Fact]
    public void CompleteReadbacks_NoBaselineBesideAPress_StaysQuiet()
    {
        // Nothing to compare against: the press decides, as before a baseline existed.
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF2", turnOn: true));

        Pmdg737AcpReceivers.CompleteReadbacks(pending, Capt, null, 0x3u, out bool otherChange);

        Assert.False(otherChange);
    }

    [Fact]
    public void CompleteReadbacks_TwoPressesInOneUpdate_SpeaksBothOldestFirst()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF1", turnOn: true));
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF2", turnOn: true));

        var said = Pmdg737AcpReceivers.CompleteReadbacks(pending, Capt, 0x0u, 0x3u, out bool otherChange);

        Assert.Equal(["Captain VHF1 Receiver on", "Captain VHF2 Receiver on"], said);
        Assert.False(otherChange);
    }

    [Fact]
    public void CompleteReadbacks_OtherSeatsUpdate_LeavesThePressPendingAndSpeaksThatSeatsList()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        Pmdg737AcpReceivers.AddPress(pending, P("ACP_Capt_RecVHF2", turnOn: true));

        var said = Pmdg737AcpReceivers.CompleteReadbacks(pending, Fo, 0x0u, 0x2u, out bool otherChange);

        Assert.Empty(said);
        Assert.True(otherChange);   // nothing pressed on the FO field: the FO list speaks
        Assert.Single(pending);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0x1u)]
    public void CompleteReadbacks_NoPresses_IsABackgroundChange(uint? previous)
    {
        var said = Pmdg737AcpReceivers.CompleteReadbacks([], Capt, previous, 0x3u, out bool otherChange);

        Assert.Empty(said);
        Assert.True(otherChange);
    }

    [Fact]
    public void ExpireIfUnconfirmed_UnmovedPress_IsReportedOnce()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        var press = P("ACP_FO_RecVHF2", turnOn: true);
        Pmdg737AcpReceivers.AddPress(pending, press);

        Assert.True(Pmdg737AcpReceivers.ExpireIfUnconfirmed(pending, press));
        Assert.False(Pmdg737AcpReceivers.ExpireIfUnconfirmed(pending, press));
    }

    [Fact]
    public void ExpireIfUnconfirmed_ConfirmedPress_IsNotReported()
    {
        var pending = new List<Pmdg737AcpReceivers.Press>();
        var press = P("ACP_FO_RecVHF2", turnOn: true);
        Pmdg737AcpReceivers.AddPress(pending, press);
        Pmdg737AcpReceivers.CompleteReadbacks(pending, Fo, 0x0u, 0x2u, out _);

        Assert.False(Pmdg737AcpReceivers.ExpireIfUnconfirmed(pending, press));
    }

    [Theory]
    [InlineData(true, 0x0u, "First Officer VHF2 Receiver did not change")]   // PMDG ignored it
    [InlineData(true, 0x2u, "First Officer VHF2 Receiver on")]
    [InlineData(false, 0x0u, "First Officer VHF2 Receiver off")]  // quick double press: back where it was, no update
    [InlineData(false, 0x2u, "First Officer VHF2 Receiver did not change")]
    public void ExpiryReadback_SpeaksTheResultWhenTheBitIsAtTargetElseNoChange(bool turnOn, uint bits, string expected)
    {
        Assert.Equal(expected, Pmdg737AcpReceivers.ExpiryReadback(P("ACP_FO_RecVHF2", turnOn), bits));
    }
}

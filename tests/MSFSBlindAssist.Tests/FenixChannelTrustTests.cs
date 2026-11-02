// Characterization tests for FenixChannelTrust, the gate between the Fenix display channel (the gRPC service
// on 127.0.0.1:18083) and a pilot. Pure logic with the clock passed in: every frame is built here, never read
// from Fenix's own capture.
//
// The contract: a display's values are read out only while the gate trusts that display, and a wrong verdict
// is the costliest defect in the channel, so every reason text is pinned. A display is trusted only when
// every condition holds, and the verdict is the first one that fails, in this order: the channel is
// connected; a data message has come since it connected; the stream has not stalled; the build is known;
// the build is one the app has checked; no message was malformed and no field had the wrong shape since it
// connected (a latch). Then the display's own conditions: each cross-check with SimConnect has agreed at
// least once and has no disagreement in force (a disagreement stays in force until a message agrees, and one
// that lasts three seconds without a break latches until the channel reconnects), and no line the display
// covers holds a text code the app does not know.
//
// SimConnect's batch comes about once a second, so a value of its is compared with the channel as the channel
// was when the value arrived, not as it is now.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelTrustTests
{
    private const string CheckedBuild = "3.0.0.7613";

    // U+2800, the blank braille cell the display uses as a spacer. Named because it cannot be seen in source.
    private const char Spacer = (char)0x2800;

    private static readonly FenixDisplayVerdict Trusted = new(true, "trusted");

    private static readonly FenixChannelDisplay[] AllDisplays =
        { FenixChannelDisplay.Pfd, FenixChannelDisplay.Ewd, FenixChannelDisplay.Sd };

    // ---- The channel-wide conditions, in the order of the table -----------------------------------

    [Fact]
    public void Everything_agreeing_is_trusted()
    {
        FenixChannelTrust gate = TrustedGate();

        Assert.Equal(Trusted, Pfd(gate, 0));
        Assert.Equal(Trusted, Ewd(gate, 0));
        Assert.Equal(Trusted, Sd(gate, 0));
    }

    [Fact]
    public void Not_connected_is_not_trusted()
    {
        var gate = new FenixChannelTrust(new HashSet<string> { CheckedBuild });

        AssertAll(gate, 0, Untrusted("channel not answering"));

        gate.OnDisconnected("connection refused");

        AssertAll(gate, 0, Untrusted("channel not answering: connection refused"));
    }

    [Fact]
    public void A_trusted_channel_that_stops_answering_is_not_trusted()
    {
        FenixChannelTrust gate = TrustedGate();

        gate.OnDisconnected("connection reset");

        AssertAll(gate, 0, Untrusted("channel not answering: connection reset"));
    }

    [Fact]
    public void No_message_yet_is_not_trusted()
    {
        var gate = new FenixChannelTrust(new HashSet<string> { CheckedBuild });
        gate.OnConnected(0);

        // No frame and no build yet: the missing message is the reason, not the unknown build. It is not a
        // stall either, however long the wait: a stall is a stream that was running.
        AssertAll(gate, 0, Untrusted("no data yet"));
        AssertAll(gate, 60_000, Untrusted("no data yet"));
    }

    [Fact]
    public void A_stalled_stream_is_not_trusted()
    {
        FenixChannelTrust gate = TrustedGate();   // the last frame came at t=0

        AssertAll(gate, 2_000, Trusted);
        AssertAll(gate, 2_001, Untrusted("stream stalled"));

        gate.OnFrame(Frame(), 2_500);

        AssertAll(gate, 2_500, Trusted);
    }

    [Fact]
    public void An_unknown_build_is_not_trusted()
    {
        var gate = new FenixChannelTrust(new HashSet<string> { CheckedBuild });
        gate.OnConnected(0);   // no OnBuild: the build query has not answered
        Compare(gate);
        gate.OnFrame(Frame(), 0);

        AssertAll(gate, 0, Untrusted("build unknown"));
    }

    [Fact]
    public void An_unchecked_build_is_not_trusted()
    {
        FenixChannelTrust gate = ConnectedGate();
        gate.OnBuild("3.0.0.7700");
        Compare(gate);
        gate.OnFrame(Frame(), 0);

        AssertAll(gate, 0, Untrusted("build 3.0.0.7700 not checked"));
    }

    [Theory]
    [InlineData("3.0.0.761")]
    [InlineData("3.0.0.76130")]
    [InlineData("3.0.0.7613 ")]
    public void A_build_must_match_a_checked_one_exactly(string build)
    {
        FenixChannelTrust gate = ConnectedGate();
        gate.OnBuild(build);
        Compare(gate);
        gate.OnFrame(Frame(), 0);

        AssertAll(gate, 0, Untrusted($"build {build} not checked"));
    }

    [Fact]
    public void An_empty_checked_list_trusts_nothing()
    {
        var gate = new FenixChannelTrust(new HashSet<string>());
        gate.OnConnected(0);
        gate.OnBuild(CheckedBuild);
        Compare(gate);
        gate.OnFrame(Frame(), 0);

        AssertAll(gate, 0, Untrusted("build 3.0.0.7613 not checked"));
    }

    [Fact]
    public void The_channel_wide_reasons_come_in_the_order_of_the_table()
    {
        var gate = new FenixChannelTrust(new HashSet<string> { CheckedBuild });
        gate.OnConnected(0);
        Compare(gate);

        // No frame yet outranks the unknown build.
        Assert.Equal(Untrusted("no data yet"), Sd(gate, 5_000));

        gate.OnFrame(Frame(), 0);

        // A stalled stream outranks the unknown build.
        Assert.Equal(Untrusted("build unknown"), Sd(gate, 2_000));
        Assert.Equal(Untrusted("stream stalled"), Sd(gate, 2_001));

        gate.OnBuild("3.0.0.7700");
        gate.OnFrame(FenixChannelFrame.MalformedFrame, 100);

        // An unchecked build outranks the shape latch.
        Assert.Equal(Untrusted("build 3.0.0.7700 not checked"), Sd(gate, 100));

        gate.OnBuild(CheckedBuild);

        Assert.Equal(Untrusted("malformed message"), Sd(gate, 100));
    }

    // ---- Shapes: a latch until reconnect ----------------------------------------------------------

    [Fact]
    public void A_wrong_shape_takes_down_every_display_until_reconnect()
    {
        FenixChannelTrust gate = TrustedGate();

        // Two fields in the wrong shape: the reason names the first.
        gate.OnFrame(Frame(wrongShape: new[] { "110", "111" }), 100);

        AssertAll(gate, 100, Untrusted("field 110 has the wrong shape"));

        // A good frame does not lift it.
        gate.OnFrame(Frame(), 200);

        AssertAll(gate, 200, Untrusted("field 110 has the wrong shape"));

        gate.OnConnected(300);
        gate.OnBuild(CheckedBuild);
        gate.OnFrame(Frame(), 300);

        AssertAll(gate, 300, Trusted);
    }

    [Fact]
    public void The_first_shape_problem_is_the_one_that_stays()
    {
        FenixChannelTrust gate = TrustedGate();

        gate.OnFrame(Frame(wrongShape: new[] { "110" }), 100);
        gate.OnFrame(FenixChannelFrame.MalformedFrame, 200);
        gate.OnFrame(Frame(wrongShape: new[] { "111" }), 300);

        AssertAll(gate, 300, Untrusted("field 110 has the wrong shape"));
    }

    [Fact]
    public void A_malformed_message_latches_the_channel()
    {
        FenixChannelTrust gate = TrustedGate();

        gate.OnFrame(FenixChannelFrame.MalformedFrame, 100);

        AssertAll(gate, 100, Untrusted("malformed message"));

        gate.OnFrame(Frame(), 200);

        AssertAll(gate, 200, Untrusted("malformed message"));
    }

    [Fact]
    public void A_malformed_message_is_still_a_message()
    {
        // The first message of the connection is malformed: the reason is the latch, not "no data yet".
        FenixChannelTrust fresh = ConnectedGate();
        Compare(fresh);

        fresh.OnFrame(FenixChannelFrame.MalformedFrame, 0);

        AssertAll(fresh, 0, Untrusted("malformed message"));

        // A bad message after three seconds of silence shows the stream is alive: the reason is the latch,
        // not a stall.
        FenixChannelTrust gate = TrustedGate();

        gate.OnFrame(Frame(wrongShape: new[] { "111" }), 3_000);

        AssertAll(gate, 3_000, Untrusted("field 111 has the wrong shape"));
    }

    [Fact]
    public void A_wrong_shaped_ecam_entries_message_latches_the_channel()
    {
        FenixChannelTrust gate = TrustedGate();

        gate.OnEcamEntries(new FenixEcamEntries(false, new[] { "1.3" }, Array.Empty<FenixEcamEntry>(), false));

        // Its own reason, so it cannot be taken for a fault of the display data.
        AssertAll(gate, 0, Untrusted("ECAM entries field 1.3 has the wrong shape"));

        gate.OnFrame(Frame(), 100);

        AssertAll(gate, 100, Untrusted("ECAM entries field 1.3 has the wrong shape"));

        gate.OnConnected(200);
        gate.OnBuild(CheckedBuild);
        gate.OnFrame(Frame(), 200);

        AssertAll(gate, 200, Trusted);
    }

    [Fact]
    public void A_malformed_ecam_entries_message_latches_the_channel()
    {
        FenixChannelTrust gate = TrustedGate();

        gate.OnEcamEntries(new FenixEcamEntries(true, Array.Empty<string>(), Array.Empty<FenixEcamEntry>(), false));

        AssertAll(gate, 0, Untrusted("malformed ECAM entries message"));
    }

    [Fact]
    public void A_well_shaped_ecam_entries_message_changes_nothing()
    {
        FenixChannelTrust gate = TrustedGate();

        gate.OnEcamEntries(new FenixEcamEntries(false, Array.Empty<string>(),
            new[] { new FenixEcamEntry("21", "PACK 1 FAULT", "AIR COND", true) }, false));
        gate.OnEcamEntries(new FenixEcamEntries(false, Array.Empty<string>(), Array.Empty<FenixEcamEntry>(), true));

        AssertAll(gate, 0, Trusted);
    }

    [Fact]
    public void An_ecam_entries_message_is_not_a_data_message()
    {
        FenixChannelTrust gate = ConnectedGate();

        gate.OnEcamEntries(new FenixEcamEntries(false, Array.Empty<string>(), Array.Empty<FenixEcamEntry>(), true));

        AssertAll(gate, 0, Untrusted("no data yet"));
    }

    // ---- Pushes with no data ----------------------------------------------------------------------

    [Fact]
    public void A_push_with_no_data_is_not_a_message()
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);

        gate.OnFrame(FenixChannelFrame.NoData, 100);

        AssertAll(gate, 100, Untrusted("no data yet"));

        gate.OnFrame(Frame(), 200);

        AssertAll(gate, 200, Trusted);
    }

    [Fact]
    public void Pushes_with_no_data_do_not_keep_the_stream_alive_and_never_latch()
    {
        FenixChannelTrust gate = TrustedGate();   // the last push with data came at t=0

        // Their default values (altitude 0, both flight directors off) disagree with every comparison, so
        // had these pushes been read, five seconds of them would have latched the PFD.
        Feed(gate, 100, 2_000, FenixChannelFrame.NoData);

        AssertAll(gate, 2_000, Trusted);
        AssertAll(gate, 2_001, Untrusted("stream stalled"));

        Feed(gate, 2_100, 5_000, FenixChannelFrame.NoData);

        AssertAll(gate, 5_000, Untrusted("stream stalled"));

        gate.OnFrame(Frame(), 5_100);

        AssertAll(gate, 5_100, Trusted);
    }

    // ---- The cross-checks: waiting, disagreeing and latched -----------------------------------------

    [Fact]
    public void A_check_not_yet_passed_keeps_its_display_untrusted()
    {
        FenixChannelTrust gate = ConnectedGate();
        gate.OnComparison(FenixComparison.FdLightCapt, 1, 0);
        gate.OnComparison(FenixComparison.FdLightFo, 1, 0);
        gate.OnComparison(FenixComparison.MagHeadingDeg, 90, 0);
        gate.OnComparison(FenixComparison.FuelKg, 2938, 0);   // SimConnect has not delivered the altitude
        gate.OnFrame(Frame(), 0);

        Assert.Equal(Untrusted("altitude not checked yet"), Pfd(gate, 0));
        Assert.Equal(Trusted, Ewd(gate, 0));
        Assert.Equal(Trusted, Sd(gate, 0));

        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 100);
        gate.OnFrame(Frame(), 100);

        Assert.Equal(Trusted, Pfd(gate, 100));
    }

    [Fact]
    public void A_pending_disagreement_untrusts_without_latching()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 100);

        Feed(gate, 100, 2_900, Frame());

        // Not latched: the reason says "disagrees" with both values, and only the PFD is affected.
        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 2_900));
        Assert.Equal(Trusted, Ewd(gate, 2_900));
        Assert.Equal(Trusted, Sd(gate, 2_900));

        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 3_000);
        gate.OnFrame(Frame(), 3_000);

        Assert.Equal(Trusted, Pfd(gate, 3_000));
    }

    [Fact]
    public void A_disagreement_lasting_the_hold_latches_until_reconnect()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 4_000);

        Feed(gate, 4_000, 6_900, Frame());   // disagreeing since t=4000: 2,900 ms

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 6_900));

        gate.OnFrame(Frame(), 7_000);   // 3,000 ms: it counts

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 7_000));

        // Agreeing again changes nothing, and the reason keeps the values that latched it.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 7_200);
        gate.OnFrame(Frame(), 7_200);

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 7_200));
        Assert.Equal(Trusted, Ewd(gate, 7_200));
        Assert.Equal(Trusted, Sd(gate, 7_200));

        // Nor does a different disagreement: a latched check is inert.
        gate.OnComparison(FenixComparison.AltitudeFt, 6000, 7_300);
        gate.OnFrame(Frame(), 7_300);

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 7_300));

        // SimConnect's altitude agrees again, and the channel reconnects: it starts fresh. The comparison
        // values are kept across a reconnect, so the next agreeing frame is enough.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 8_000);
        gate.OnConnected(8_000);
        gate.OnBuild(CheckedBuild);
        gate.OnFrame(Frame(), 8_000);

        AssertAll(gate, 8_000, Trusted);
    }

    [Fact]
    public void A_waiting_value_breaks_a_disagreement()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 0);
        Feed(gate, 0, 2_000, Frame());   // disagreeing for 2,000 ms

        // SimConnect's value goes missing for one frame. That breaks the run toward the 3,000 ms: it starts
        // again. It does not end the disagreement, which stays in force until a frame agrees, so the display
        // is still not trusted.
        gate.OnComparison(FenixComparison.AltitudeFt, double.NaN, 2_500);
        gate.OnFrame(Frame(), 2_500);

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 2_500));

        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 2_600);
        Feed(gate, 2_600, 5_500, Frame());   // a second run, counted from t=2600

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 5_500));

        gate.OnFrame(Frame(), 5_600);   // 3,000 ms after the second run began

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 5_600));
    }

    [Fact]
    public void A_missing_value_does_not_end_a_disagreement()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 100);
        Feed(gate, 100, 1_000, Frame());   // disagreeing

        // SimConnect stops delivering the altitude: ten seconds of waiting. The disagreement is still in force,
        // and it never latches, since no run of 3,000 ms can build up.
        gate.OnComparison(FenixComparison.AltitudeFt, double.NaN, 1_100);
        Feed(gate, 1_100, 11_000, Frame());

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 11_000));

        // Only a frame that agrees ends it.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 11_100);
        gate.OnFrame(Frame(), 11_100);

        Assert.Equal(Trusted, Pfd(gate, 11_100));
    }

    [Fact]
    public void A_disagreement_on_a_check_that_never_passed_is_still_a_disagreement()
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, altitude: 5000);
        gate.OnFrame(Frame(), 0);   // it disagrees at the very first look

        gate.OnComparison(FenixComparison.AltitudeFt, double.NaN, 100);
        gate.OnFrame(Frame(), 100);   // and then it waits

        // "Disagrees" is the reason, not "not checked yet".
        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 100));
    }

    [Fact]
    public void A_disagreement_interleaved_with_waiting_frames_never_latches_and_never_reads_trusted()
    {
        FenixChannelTrust gate = TrustedGate();   // the captain's flight director is on, and the light says so

        // The light reads 0 on one frame (the channel's flight director is on: a disagreement) and 2 on the
        // next (waiting), for ten seconds. Each waiting frame restarts the hold clock, so no run ever reaches
        // 3,000 ms, and the disagreement stays in force through the waiting frames.
        for (long t = 100; t <= 10_000; t += 100)
        {
            gate.OnComparison(FenixComparison.FdLightCapt, t % 200 == 0 ? 2 : 0, t);
            gate.OnFrame(Frame(), t);

            Assert.Equal(Untrusted("FD captain disagrees: channel on, sim off"), Pfd(gate, t));
        }

        // A frame that agrees ends it.
        gate.OnComparison(FenixComparison.FdLightCapt, 1, 10_100);
        gate.OnFrame(Frame(), 10_100);

        Assert.Equal(Trusted, Pfd(gate, 10_100));
    }

    [Fact]
    public void An_agreeing_frame_restarts_the_hold_clock()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 100);
        Feed(gate, 100, 2_900, Frame());   // disagreeing for 2,800 ms

        // One frame agrees: the disagreement is over, and so is the run toward the 3,000 ms.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 3_000);
        gate.OnFrame(Frame(), 3_000);

        Assert.Equal(Trusted, Pfd(gate, 3_000));

        // It disagrees again: a new run, counted from its own first frame (t=3100).
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 3_100);
        Feed(gate, 3_100, 5_900, Frame());

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 5_900));

        gate.OnFrame(Frame(), 6_100);   // 3,000 ms after t=3100

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 6_100));
    }

    [Fact]
    public void A_stall_restarts_the_hold_clock()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 0);
        Feed(gate, 0, 2_000, Frame());   // disagreeing for 2,000 ms, then the stream goes quiet

        AssertAll(gate, 4_001, Untrusted("stream stalled"));

        // It resumes, still disagreeing. A stall is a break in the run toward the 3,000 ms, as a waiting value
        // is: the run counts from the first frame after it (t=5000), not from t=0, so it is not already over.
        Feed(gate, 5_000, 7_900, Frame());

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 7_900));

        gate.OnFrame(Frame(), 8_000);   // 3,000 ms after the first frame after the stall

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 8_000));
    }

    [Fact]
    public void After_a_stall_a_waiting_frame_leaves_the_disagreement_in_force()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 0);
        Feed(gate, 0, 2_000, Frame());   // disagreeing, then the stream goes quiet

        // It resumes with a frame whose altitude cannot be read. The stall restarted the hold clock and did
        // not end the disagreement, and a waiting frame does not either.
        gate.OnFrame(Frame(altitude: double.NaN), 5_000);

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 5_000));

        // A frame that agrees ends it.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 5_100);
        gate.OnFrame(Frame(), 5_100);

        Assert.Equal(Trusted, Pfd(gate, 5_100));
    }

    [Fact]
    public void Pushes_with_no_data_do_not_hide_a_stall_from_the_hold_clock()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 0);
        Feed(gate, 0, 2_000, Frame());   // disagreeing for 2,000 ms

        // Pushes with no data are not messages, so they are not the frame "seen before": the stream is still
        // stalled when the next push with data comes, and the run counts from that one.
        Feed(gate, 2_100, 4_900, FenixChannelFrame.NoData);
        Feed(gate, 5_000, 7_900, Frame());

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 7_900));

        gate.OnFrame(Frame(), 8_000);

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 8_000));
    }

    [Theory]
    [InlineData(3_000, "altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft")]   // 2,000 ms of silence: not a stall
    [InlineData(3_001, "altitude disagrees: channel 1,000 ft, sim 5,000 ft")]   // 2,001 ms: a stall
    public void A_stall_restarts_the_hold_clock_only_past_the_stale_limit(long nextFrameMs, string reason)
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 0);
        Feed(gate, 0, 1_000, Frame());   // disagreeing since t=0

        gate.OnFrame(Frame(), nextFrameMs);

        Assert.Equal(Untrusted(reason), Pfd(gate, nextFrameMs));
    }

    [Fact]
    public void A_stall_does_not_clear_a_passed_check()
    {
        FenixChannelTrust gate = TrustedGate();   // every check has passed

        // Ten seconds of silence, then a frame whose altitude cannot be read: the check had passed, and a
        // stall is not a reconnect.
        gate.OnFrame(Frame(altitude: double.NaN), 10_000);

        Assert.Equal(Trusted, Pfd(gate, 10_000));
    }

    [Fact]
    public void A_stall_does_not_lift_a_latch()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 100);
        Feed(gate, 100, 3_100, Frame());   // latched

        // Ten seconds of silence, then a frame that agrees.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 13_100);
        gate.OnFrame(Frame(), 13_100);

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 13_100));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_channel_value_waits_instead_of_latching(double channelAltitude)
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);

        Feed(gate, 0, 10_000, Frame(altitude: channelAltitude));

        // Never passed and never disagreeing: "not checked yet", however long it goes on.
        Assert.Equal(Untrusted("altitude not checked yet"), Pfd(gate, 10_000));
        Assert.Equal(Trusted, Ewd(gate, 10_000));

        // And never latched: a frame with a real value passes the check.
        gate.OnFrame(Frame(), 10_100);

        Assert.Equal(Trusted, Pfd(gate, 10_100));
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_non_finite_simulator_value_waits_instead_of_latching(double simAltitude)
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, altitude: simAltitude);

        Feed(gate, 0, 10_000, Frame());

        Assert.Equal(Untrusted("altitude not checked yet"), Pfd(gate, 10_000));
    }

    [Fact]
    public void The_first_check_that_is_not_ok_gives_the_reason()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 100);
        Feed(gate, 100, 3_100, Frame());

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 3_100));

        // The captain's flight director now starts to disagree as well. It comes first in the PFD's order,
        // so it gives the reason, though the latched altitude is the heavier fault.
        gate.OnComparison(FenixComparison.FdLightCapt, 0, 3_200);
        gate.OnFrame(Frame(), 3_200);

        Assert.Equal(Untrusted("FD captain disagrees: channel on, sim off"), Pfd(gate, 3_200));
    }

    // ---- SimConnect's value is compared with the channel as it was when the value arrived -----------

    [Theory]
    [InlineData(3.0)]
    [InlineData(6.0)]
    public void A_turn_stays_trusted_although_simconnect_comes_once_a_second(double degreesPerSecond)
    {
        // 3 degrees a second is a standard-rate turn. At that rate the second of lag stays just inside the
        // 3 degree tolerance, so only the row of twice the rate fails when the values are not aligned.
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);   // the flight directors, altitude and fuel agree and stay put

        Fly(gate, FenixComparison.MagHeadingDeg,
            frameAt: ms => Frame(heading: 90 + degreesPerSecond * ms / 1000.0),
            simValueAt: ms => 90 + degreesPerSecond * ms / 1000.0,
            untilMs: 10_000,
            afterFrame: t => Assert.Equal(Trusted, Pfd(gate, t)));
    }

    [Theory]
    [InlineData(100.0)]
    [InlineData(200.0)]
    public void A_climb_stays_trusted_although_simconnect_comes_once_a_second(double feetPerSecond)
    {
        // 100 feet a second is a strong climb (6,000 feet a minute). At that rate the second of lag stays just
        // inside the 100 foot tolerance, so only the row of twice the rate fails when the values are not aligned.
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);

        Fly(gate, FenixComparison.AltitudeFt,
            frameAt: ms => Frame(altitude: 1000 + feetPerSecond * ms / 1000.0),
            simValueAt: ms => 1000 + feetPerSecond * ms / 1000.0,
            untilMs: 10_000,
            afterFrame: t => Assert.Equal(Trusted, Pfd(gate, t)));
    }

    [Fact]
    public void A_turn_with_the_channel_heading_ten_degrees_off_still_disagrees_and_latches()
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);

        Fly(gate, FenixComparison.MagHeadingDeg,
            frameAt: ms => Frame(heading: 100 + 3.0 * ms / 1000.0),   // 10 degrees ahead of SimConnect's
            simValueAt: ms => 90 + 3.0 * ms / 1000.0,
            untilMs: 10_000,
            afterFrame: t =>
            {
                // It disagrees from the first frame (t=0) and counts from the frame at t=3000.
                string expected = t < 3_000 ? "heading disagrees: " : "heading latched until reconnect: ";
                Assert.StartsWith(expected, Pfd(gate, t).Reason, StringComparison.Ordinal);
            });
    }

    [Fact]
    public void A_flight_director_press_that_the_light_reports_late_never_reads_as_a_disagreement()
    {
        // The captain presses the FD button at t=1000. The channel's flag flips at once, and SimConnect's batch
        // brings the light's new value at t=1800.
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, fdCapt: 0);   // the flight director is off, and the light says so

        for (long t = 0; t <= 4_000; t += 100)
        {
            if (t == 800) gate.OnComparison(FenixComparison.FdLightCapt, 0, 800);       // the batch before the press
            if (t == 1_800) gate.OnComparison(FenixComparison.FdLightCapt, 1, 1_800);   // the batch after it

            gate.OnFrame(Frame(fdCapt: t >= 1_000), t);

            Assert.Equal(Trusted, Pfd(gate, t));
        }
    }

    [Fact]
    public void A_parked_aircraft_stays_trusted_with_simconnect_delivered_once()
    {
        // A value that does not change is not delivered again: SimConnect spoke once, at t=0, and the channel
        // has read the same ever since.
        FenixChannelTrust gate = TrustedGate();

        for (long t = 100; t <= 20_000; t += 100)
        {
            gate.OnFrame(Frame(), t);

            Assert.Equal(Trusted, Pfd(gate, t));
        }

        AssertAll(gate, 20_000, Trusted);
    }

    [Fact]
    public void A_simulator_value_that_has_not_changed_still_judges_the_channel()
    {
        FenixChannelTrust gate = TrustedGate();   // SimConnect said 1,000 ft at t=0 and has said nothing since
        Feed(gate, 100, 4_900, Frame());

        // The channel's altitude jumps. SimConnect's old value, older than the window, is compared with the
        // channel's value now, so the disagreement counts from t=5000.
        Feed(gate, 5_000, 7_900, Frame(altitude: 1500));

        Assert.Equal(Untrusted("altitude disagrees: channel 1,500 ft, sim 1,000 ft"), Pfd(gate, 7_900));

        gate.OnFrame(Frame(altitude: 1500), 8_000);

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,500 ft, sim 1,000 ft"), Pfd(gate, 8_000));
    }

    [Fact]
    public void A_simulator_value_is_aligned_with_the_channel_only_within_the_window()
    {
        FenixChannelTrust gate = TrustedGate();   // SimConnect's 1,000 ft arrived at t=0, with the channel at 1,000 ft

        // The channel climbs 300 ft while SimConnect has said nothing. Within 1,500 ms of the arrival, the
        // value is compared with the channel as it was then, at t=0: it agrees.
        gate.OnFrame(Frame(altitude: 1300), 1_000);

        Assert.Equal(Trusted, Pfd(gate, 1_000));

        gate.OnFrame(Frame(altitude: 1300), 1_500);   // exactly the window: still within it

        Assert.Equal(Trusted, Pfd(gate, 1_500));

        // One millisecond later the value is older than the window: it is compared with the channel now.
        gate.OnFrame(Frame(altitude: 1300), 1_501);

        Assert.Equal(Untrusted("altitude disagrees: channel 1,300 ft, sim 1,000 ft"), Pfd(gate, 1_501));
    }

    [Fact]
    public void Messages_older_than_the_window_are_not_kept()
    {
        FenixChannelTrust gate = TrustedGate();   // a message at t=0 with the altitude at 1,000 ft

        // SimConnect delivers 1,000 ft again at t=400, and the next message comes at t=1900 (no stall), with
        // the altitude at 1,300 ft. The message at t=0 is older than the window by then, so it is gone, and
        // the one nearest the arrival that is still kept is the current one.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 400);
        gate.OnFrame(Frame(altitude: 1300), 1_900);

        Assert.Equal(Untrusted("altitude disagrees: channel 1,300 ft, sim 1,000 ft"), Pfd(gate, 1_900));
    }

    [Fact]
    public void On_a_tie_the_later_message_is_the_one_compared()
    {
        FenixChannelTrust gate = TrustedGate();   // a message at t=0 with the altitude at 1,000 ft

        // SimConnect's value arrives midway between the message at t=0 and the one at t=100.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 50);
        gate.OnFrame(Frame(altitude: 1300), 100);

        Assert.Equal(Untrusted("altitude disagrees: channel 1,300 ft, sim 1,000 ft"), Pfd(gate, 100));
    }

    [Fact]
    public void A_reason_names_the_channel_value_that_was_compared()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 100);
        gate.OnFrame(Frame(altitude: 1000), 100);

        // The channel has moved on by the next message. The reason gives the value that was compared with
        // SimConnect's (the one from t=100), not the newest.
        gate.OnFrame(Frame(altitude: 1200), 600);

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 600));
    }

    [Fact]
    public void A_kept_channel_value_that_is_missing_makes_the_comparison_wait()
    {
        FenixChannelTrust gate = TrustedGate();

        // SimConnect's 1,000 ft arrives with a message whose altitude cannot be read.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 500);
        gate.OnFrame(Frame(altitude: double.NaN), 500);

        // The channel reads 1,300 ft at the next message. SimConnect's value is compared with the message
        // nearest its arrival, which could not be read: the comparison waits, and nothing disagrees.
        gate.OnFrame(Frame(altitude: 1300), 1_000);

        Assert.Equal(Trusted, Pfd(gate, 1_000));

        // Once the value is older than the window, it is compared with the channel now.
        gate.OnFrame(Frame(altitude: 1300), 2_100);

        Assert.Equal(Untrusted("altitude disagrees: channel 1,300 ft, sim 1,000 ft"), Pfd(gate, 2_100));
    }

    [Fact]
    public void A_reconnect_clears_the_kept_messages()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 4_800);   // delivered again just before the reconnect
        gate.OnFrame(Frame(), 4_800);   // a kept message that agrees with it

        gate.OnConnected(5_000);
        gate.OnBuild(CheckedBuild);
        gate.OnFrame(Frame(altitude: 1300), 5_000);

        // The message at t=4800 belongs to the connection that ended. Only the first message of the new one
        // is kept, and it disagrees with SimConnect's value (which stays, with its arrival time).
        Assert.Equal(Untrusted("altitude disagrees: channel 1,300 ft, sim 1,000 ft"), Pfd(gate, 5_000));
    }

    // ---- Each comparison ----------------------------------------------------------------------------

    [Theory]
    [InlineData("captain", true, 0.0, "FD captain disagrees: channel on, sim off")]
    [InlineData("captain", false, 1.0, "FD captain disagrees: channel off, sim on")]
    [InlineData("first officer", true, 0.0, "FD first officer disagrees: channel on, sim off")]
    [InlineData("first officer", false, 1.0, "FD first officer disagrees: channel off, sim on")]
    public void A_flight_director_disagreement_names_its_side(string side, bool channelOn, double light, string reason)
    {
        bool captain = side == "captain";
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(captain ? FenixComparison.FdLightCapt : FenixComparison.FdLightFo, light, 100);

        gate.OnFrame(captain ? Frame(fdCapt: channelOn) : Frame(fdFo: channelOn), 100);

        Assert.Equal(Untrusted(reason), Pfd(gate, 100));
        Assert.Equal(Trusted, Ewd(gate, 100));
        Assert.Equal(Trusted, Sd(gate, 100));
    }

    [Theory]
    [InlineData(2.0)]
    [InlineData(0.5)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void The_fd_check_skips_light_values_other_than_zero_and_one(double light)
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.FdLightCapt, light, 100);   // the annunciator light test, for one

        Feed(gate, 100, 10_000, Frame(fdCapt: false));   // the channel's flight director is off

        Assert.Equal(Trusted, Pfd(gate, 10_000));
    }

    [Fact]
    public void The_fd_check_waits_for_a_light_that_reads_zero_or_one()
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, fdCapt: 2);

        Feed(gate, 0, 10_000, Frame());

        Assert.Equal(Untrusted("FD captain not checked yet"), Pfd(gate, 10_000));
    }

    // ---- The annunciator light test -----------------------------------------------------------------

    [Fact]
    public void The_light_test_never_reads_as_an_fd_disagreement()
    {
        // The annunciator light test lights both FD lamps: they read exactly 1 with both flight directors off, while
        // the channel's FD flags follow the pushbuttons and stay off (measured on 3.0.0.7613, 2026-11-02, where it
        // latched the FD captain check 3 s into the test). While the test is on, both FD checks wait.
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, fdCapt: 0, fdFo: 0);
        gate.OnFrame(Frame(fdCapt: false, fdFo: false), 0);
        Assert.Equal(Trusted, Pfd(gate, 0));

        gate.OnLightTest(true);
        gate.OnComparison(FenixComparison.FdLightCapt, 1, 100);
        gate.OnComparison(FenixComparison.FdLightFo, 1, 100);

        // Five seconds of frames, longer than the hold: neither check disagrees, so neither latches.
        for (long t = 100; t <= 5_100; t += 100)
        {
            gate.OnFrame(Frame(fdCapt: false, fdFo: false), t);

            Assert.Equal(Trusted, Pfd(gate, t));
        }
    }

    [Fact]
    public void After_the_light_test_a_real_fd_disagreement_still_latches()
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, fdCapt: 0, fdFo: 0);
        gate.OnFrame(Frame(fdCapt: false, fdFo: false), 0);   // both FD checks pass, off and off
        gate.OnLightTest(true);
        gate.OnComparison(FenixComparison.FdLightCapt, 1, 100);
        gate.OnComparison(FenixComparison.FdLightFo, 1, 100);
        Feed(gate, 100, 5_000, Frame(fdCapt: false, fdFo: false));

        // The test ends, and the first officer's lamp goes out, but the captain's goes on reading 1 with the flight
        // director off: a real disagreement, which counts as any other does from the first frame without the test.
        gate.OnLightTest(false);
        gate.OnComparison(FenixComparison.FdLightFo, 0, 5_100);
        Feed(gate, 5_100, 8_000, Frame(fdCapt: false, fdFo: false));

        Assert.Equal(Untrusted("FD captain disagrees: channel off, sim on"), Pfd(gate, 8_000));

        gate.OnFrame(Frame(fdCapt: false, fdFo: false), 8_100);   // MismatchHoldMs after t=5100

        Assert.Equal(Untrusted("FD captain latched until reconnect: channel off, sim on"), Pfd(gate, 8_100));
    }

    [Fact]
    public void The_light_test_skips_only_the_fd_checks()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnLightTest(true);
        gate.OnComparison(FenixComparison.AltitudeFt, 5000, 100);

        Feed(gate, 100, 3_100, Frame());

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 3_100));
    }

    [Fact]
    public void A_light_test_leaves_an_fd_disagreement_in_force_without_latching_it()
    {
        // As for any waiting value: a disagreement already in force when the test begins stays in force, and each
        // frame of the test restarts its hold clock, so it never latches. The FD checks wait through the test even
        // when the lamp would agree. A frame that agrees after the test ends the disagreement.
        FenixChannelTrust gate = TrustedGate();   // both flight directors on, and both lamps say so
        gate.OnComparison(FenixComparison.FdLightCapt, 0, 100);
        Feed(gate, 100, 2_000, Frame());   // the lamp reads 0 with the flight director on: 1,900 ms of disagreement

        gate.OnLightTest(true);
        gate.OnComparison(FenixComparison.FdLightCapt, 1, 2_100);
        Feed(gate, 2_100, 12_000, Frame());

        Assert.Equal(Untrusted("FD captain disagrees: channel on, sim off"), Pfd(gate, 12_000));

        gate.OnLightTest(false);
        gate.OnFrame(Frame(), 12_100);   // the lamp reads 1, the flight director is on: it agrees

        Assert.Equal(Trusted, Pfd(gate, 12_100));
    }

    [Fact]
    public void The_light_test_outlasts_a_reconnect()
    {
        // The switch is SimConnect's value, as the lamps are, and SimConnect does not deliver it again while it stays
        // at Test: a reconnect in the middle of the test must not forget it.
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, fdCapt: 0, fdFo: 0);
        gate.OnFrame(Frame(fdCapt: false, fdFo: false), 0);
        gate.OnLightTest(true);
        gate.OnComparison(FenixComparison.FdLightCapt, 1, 100);
        gate.OnComparison(FenixComparison.FdLightFo, 1, 100);

        gate.OnConnected(1_000);
        gate.OnBuild(CheckedBuild);
        Feed(gate, 1_000, 6_000, Frame(fdCapt: false, fdFo: false));

        // Started fresh, the FD checks have not passed, and while they wait they never disagree or latch.
        Assert.Equal(Untrusted("FD captain not checked yet"), Pfd(gate, 6_000));
    }

    [Theory]
    [InlineData(1100.0, null)]   // exactly 100 ft apart: within the tolerance
    [InlineData(900.0, null)]
    [InlineData(1101.0, "altitude disagrees: channel 1,000 ft, sim 1,101 ft")]
    [InlineData(899.0, "altitude disagrees: channel 1,000 ft, sim 899 ft")]
    public void Altitude_is_compared_within_one_hundred_feet(double simAltitude, string? reason)
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, altitude: simAltitude);

        gate.OnFrame(Frame(altitude: 1000), 0);

        Assert.Equal(reason is null ? Trusted : Untrusted(reason), Pfd(gate, 0));
    }

    [Theory]
    [InlineData(359.0, 1.0, null)]   // 2 apart across north
    [InlineData(1.0, 359.0, null)]
    [InlineData(360.0, 0.0, null)]
    [InlineData(4.0, 1.0, null)]   // exactly 3 degrees: within the tolerance
    [InlineData(355.0, 1.0, "heading disagrees: channel 355.0 deg, sim 1.0 deg")]   // 6 apart across north
    [InlineData(4.5, 1.0, "heading disagrees: channel 4.5 deg, sim 1.0 deg")]
    [InlineData(0.0, 180.0, "heading disagrees: channel 0.0 deg, sim 180.0 deg")]
    public void Heading_is_compared_across_north(double channelHeading, double simHeading, string? reason)
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, heading: simHeading);

        gate.OnFrame(Frame(heading: channelHeading), 0);

        Assert.Equal(reason is null ? Trusted : Untrusted(reason), Pfd(gate, 0));
    }

    [Theory]
    [InlineData("wFOB:   g2940 cs KG", 2938.0, null)]
    [InlineData("wFOB:   g6480 cs LBS", 2939.0, null)]
    [InlineData("wFOB:   g2940 cs KG", 2700.0, "fuel disagrees: channel 2,940 kg, sim 2,700 kg")]
    [InlineData("wFOB:   g6480 cs LBS", 2700.0, "fuel disagrees: channel 2,939 kg, sim 2,700 kg")]
    public void Fuel_is_compared_in_kilograms(string fob, double simFuelKg, string? reason)
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, fuelKg: simFuelKg);

        gate.OnFrame(Frame(fob: fob), 0);

        Assert.Equal(reason is null ? Trusted : Untrusted(reason), Ewd(gate, 0));
    }

    [Theory]
    [InlineData("wFOB:   g10150 cs KG", null)]
    [InlineData("wFOB:   g10250 cs KG", "fuel disagrees: channel 10,250 kg, sim 10,000 kg")]
    public void Fuel_tolerance_is_two_percent_above_five_tonnes(string fob, string? reason)
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, fuelKg: 10_000);

        gate.OnFrame(Frame(fob: fob), 0);

        Assert.Equal(reason is null ? Trusted : Untrusted(reason), Ewd(gate, 0));
    }

    [Theory]
    [InlineData("wFOB:   g3030 cs KG", null)]   // 92 kg from 2938
    [InlineData("wFOB:   g3050 cs KG", "fuel disagrees: channel 3,050 kg, sim 2,938 kg")]   // 112 kg: 2 percent of 2938 is only 59
    public void Fuel_tolerance_is_at_least_one_hundred_kilograms(string fob, string? reason)
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, fuelKg: 2938);

        gate.OnFrame(Frame(fob: fob), 0);

        Assert.Equal(reason is null ? Trusted : Untrusted(reason), Ewd(gate, 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("wFOB:   g--- cs KG")]
    [InlineData("wFOB:   g2940 cs TONS")]
    [InlineData("wFUEL   g2940 cs KG")]
    [InlineData("wFOB:   g12,340 cs KG")]   // a grouping separator: never read as 12.34
    [InlineData("wFOB:   g12 340 cs KG")]
    [InlineData("wFOB:   g2940.5 cs KG")]   // nor is a decimal: only whole numbers are read
    [InlineData("wFOB:   g2940,5 cs KG")]
    public void A_fob_text_that_does_not_parse_waits_instead_of_latching(string fob)
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);

        Feed(gate, 0, 10_000, Frame(fob: fob));

        Assert.Equal(Untrusted("fuel not checked yet"), Ewd(gate, 10_000));
        Assert.Equal(Trusted, Pfd(gate, 10_000));
        Assert.Equal(Trusted, Sd(gate, 10_000));
    }

    [Fact]
    public void A_fob_grouped_with_the_braille_spacer_waits_too()
    {
        // The display's own spacer reads as a space, so this is "12 340", no more a number than "12,340".
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);

        Feed(gate, 0, 10_000, Frame(fob: "wFOB:   g12" + Spacer + "340 cs KG"));

        Assert.Equal(Untrusted("fuel not checked yet"), Ewd(gate, 10_000));
    }

    [Fact]
    public void A_fuel_mismatch_leaves_the_pfd_trusted()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.FuelKg, 5000, 100);

        Feed(gate, 100, 3_100, Frame());

        Assert.Equal(Untrusted("fuel latched until reconnect: channel 2,940 kg, sim 5,000 kg"), Ewd(gate, 3_100));
        Assert.Equal(Trusted, Pfd(gate, 3_100));
        Assert.Equal(Trusted, Sd(gate, 3_100));
    }

    // ---- Text codes the app does not know ---------------------------------------------------------

    [Fact]
    public void An_unknown_code_untrusts_its_display_without_latching()
    {
        FenixChannelTrust gate = TrustedGate();

        // Ten seconds, long enough for any latch: it is an exact condition, not a noisy one.
        Feed(gate, 100, 10_000, Frame(upperLeft: new[] { "rENG 1 FIRE" }));

        Assert.Equal(Untrusted("unknown code 'r' in the upper ECAM"), Ewd(gate, 10_000));
        Assert.Equal(Trusted, Pfd(gate, 10_000));
        Assert.Equal(Trusted, Sd(gate, 10_000));

        gate.OnFrame(Frame(), 10_100);

        Assert.Equal(Trusted, Ewd(gate, 10_100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void An_unknown_code_in_the_fma_untrusts_the_pfd(int slot)
    {
        FenixChannelTrust gate = TrustedGate();

        gate.OnFrame(Frame(fma: FmaWith(slot, "dX")), 100);

        Assert.Equal(Untrusted("unknown code 'd' in the FMA"), Pfd(gate, 100));
        Assert.Equal(Trusted, Ewd(gate, 100));
        Assert.Equal(Trusted, Sd(gate, 100));
    }

    [Theory]
    [InlineData("right", "unknown code 'x' in the upper ECAM")]
    [InlineData("fob", "unknown code 'q' in the FOB line")]
    public void An_unknown_code_names_the_line_it_came_from(string line, string reason)
    {
        FenixChannelTrust gate = TrustedGate();
        FenixChannelFrame frame = line == "right"
            ? Frame(upperRight: new[] { "wOK", "xFAULT" })
            : Frame(fob: "wFOB:   g2940 cq KG");   // the q is dropped, so the number still parses

        gate.OnFrame(frame, 100);

        Assert.Equal(Untrusted(reason), Ewd(gate, 100));
        Assert.Equal(Trusted, Pfd(gate, 100));
    }

    [Fact]
    public void The_first_unknown_code_in_reading_order_is_named()
    {
        FenixChannelTrust gate = TrustedGate();

        // The left column comes before the right one, and within a line the first letter wins.
        gate.OnFrame(Frame(upperLeft: new[] { "wOK", "dFAULTx" }, upperRight: new[] { "rBAD" }), 100);

        Assert.Equal(Untrusted("unknown code 'd' in the upper ECAM"), Ewd(gate, 100));
    }

    [Fact]
    public void An_unknown_code_is_judged_after_the_cross_checks()
    {
        FenixChannelTrust gate = ConnectedGate();
        gate.OnComparison(FenixComparison.FdLightCapt, 1, 0);
        gate.OnComparison(FenixComparison.FdLightFo, 1, 0);
        gate.OnComparison(FenixComparison.MagHeadingDeg, 90, 0);   // no altitude yet
        gate.OnFrame(Frame(fma: FmaWith(0, "dX")), 0);

        Assert.Equal(Untrusted("altitude not checked yet"), Pfd(gate, 0));

        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 100);
        gate.OnFrame(Frame(fma: FmaWith(0, "dX")), 100);

        Assert.Equal(Untrusted("unknown code 'd' in the FMA"), Pfd(gate, 100));
    }

    // ---- A frame with no FMA ----------------------------------------------------------------------

    [Fact]
    public void A_frame_with_no_fma_untrusts_only_the_pfd_and_never_latches()
    {
        // protobuf-net leaves out an empty or null array, so an unpowered PFD can send no FMA slot at all. That is
        // not a reshaped field (the decoder records no "111"): the PFD has nothing to read, and the E/WD and the SD
        // keep their verdicts.
        FenixChannelTrust gate = TrustedGate();

        // Ten seconds of them, long enough for any latch.
        Feed(gate, 100, 10_000, Frame(fma: Array.Empty<FenixFmaSlot>()));

        Assert.Equal(Untrusted("no FMA"), Pfd(gate, 10_000));
        Assert.Equal(Trusted, Ewd(gate, 10_000));
        Assert.Equal(Trusted, Sd(gate, 10_000));

        // The next frame with its 16 slots trusts the PFD again: nothing latched.
        gate.OnFrame(Frame(), 10_100);

        AssertAll(gate, 10_100, Trusted);
    }

    [Fact]
    public void No_fma_is_judged_after_the_cross_checks()
    {
        FenixChannelTrust gate = ConnectedGate();
        gate.OnComparison(FenixComparison.FdLightCapt, 1, 0);
        gate.OnComparison(FenixComparison.FdLightFo, 1, 0);
        gate.OnComparison(FenixComparison.MagHeadingDeg, 90, 0);   // no altitude yet
        gate.OnFrame(Frame(fma: Array.Empty<FenixFmaSlot>()), 0);

        Assert.Equal(Untrusted("altitude not checked yet"), Pfd(gate, 0));

        gate.OnComparison(FenixComparison.AltitudeFt, 1000, 100);
        gate.OnFrame(Frame(fma: Array.Empty<FenixFmaSlot>()), 100);

        Assert.Equal(Untrusted("no FMA"), Pfd(gate, 100));
    }

    // ---- A reconnect starts fresh -----------------------------------------------------------------

    [Fact]
    public void A_reconnect_starts_fresh()
    {
        FenixChannelTrust gate = TrustedGate();

        gate.OnConnected(5_000);

        AssertAll(gate, 5_000, Untrusted("no data yet"));

        gate.OnFrame(Frame(), 5_000);

        AssertAll(gate, 5_000, Untrusted("build unknown"));

        gate.OnBuild(CheckedBuild);

        // SimConnect's values were kept, so the one agreeing frame is enough.
        AssertAll(gate, 5_000, Trusted);
    }

    [Fact]
    public void A_reconnect_clears_the_checks_that_passed()
    {
        FenixChannelTrust gate = TrustedGate();   // every check has passed

        gate.OnConnected(5_000);
        gate.OnBuild(CheckedBuild);
        gate.OnFrame(Frame(altitude: double.NaN), 5_000);

        Assert.Equal(Untrusted("altitude not checked yet"), Pfd(gate, 5_000));
    }

    // ---- Failing closed ---------------------------------------------------------------------------

    [Fact]
    public void A_display_the_gate_does_not_know_is_not_trusted()
    {
        FenixChannelTrust gate = TrustedGate();

        FenixDisplayVerdict verdict = gate.VerdictFor((FenixChannelDisplay)7, 0);

        Assert.False(verdict.Trusted);
    }

    [Fact]
    public void A_comparison_the_gate_does_not_know_is_ignored()
    {
        FenixChannelTrust gate = TrustedGate();

        Exception? thrown = Record.Exception(() => gate.OnComparison((FenixComparison)99, 1, 0));

        Assert.Null(thrown);
        AssertAll(gate, 0, Trusted);
    }

    // ---- Culture: reasons, descriptions and the FOB number never follow the PC's -------------------

    [Theory]
    [InlineData("de-DE", "wFOB:   g2940 cs KG", 2938.0)]
    [InlineData("sv-SE", "wFOB:   g2940 cs KG", 2938.0)]
    [InlineData("de-DE", "wFOB:   g12340 cs KG", 12338.0)]
    [InlineData("sv-SE", "wFOB:   g12340 cs KG", 12338.0)]
    public void The_fob_text_parses_under_comma_decimal_cultures(string culture, string fob, double simFuelKg)
    {
        InCulture(culture, () =>
        {
            // A fuel check that has never passed, so only a number that parsed can make the E/WD trusted.
            FenixChannelTrust gate = ConnectedGate();
            Compare(gate, fuelKg: simFuelKg);

            gate.OnFrame(Frame(fob: fob), 0);

            Assert.Equal(Trusted, Ewd(gate, 0));
        });
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Reasons_give_numbers_in_the_invariant_culture(string culture)
    {
        InCulture(culture, () =>
        {
            FenixChannelTrust altitude = ConnectedGate();
            Compare(altitude, altitude: 2050);
            altitude.OnFrame(Frame(altitude: 1023), 0);
            Assert.Equal(Untrusted("altitude disagrees: channel 1,023 ft, sim 2,050 ft"), Pfd(altitude, 0));

            FenixChannelTrust heading = ConnectedGate();
            Compare(heading, heading: 1.5);
            heading.OnFrame(Frame(heading: 355.5), 0);
            Assert.Equal(Untrusted("heading disagrees: channel 355.5 deg, sim 1.5 deg"), Pfd(heading, 0));

            FenixChannelTrust fuel = ConnectedGate();
            Compare(fuel, fuelKg: 2700.4);
            fuel.OnFrame(Frame(), 0);
            Assert.Equal(Untrusted("fuel disagrees: channel 2,940 kg, sim 2,700 kg"), Ewd(fuel, 0));
        });
    }

    [Fact]
    public void Describes_each_comparison_in_the_invariant_culture()
    {
        InCulture("de-DE", () =>
        {
            FenixChannelTrust gate = ConnectedGate();
            Compare(gate, altitude: 1020);
            gate.OnFrame(Frame(altitude: 1023), 0);

            Assert.Contains("altitude: channel 1,023 ft, sim 1,020 ft", gate.DescribeComparisons());
        });
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Describes_all_five_comparisons_in_order(string culture)
    {
        InCulture(culture, () =>
        {
            FenixChannelTrust gate = ConnectedGate();
            Compare(gate, fdCapt: 1, fdFo: 0, altitude: 1020, heading: 90, fuelKg: 2938.2);

            gate.OnFrame(Frame(fdCapt: true, fdFo: false, altitude: 1023, heading: 90.5, fob: "wFOB:   g6480 cs LBS"), 0);

            Assert.Equal(new[]
            {
                "FD captain: channel on, sim on",
                "FD first officer: channel off, sim off",
                "altitude: channel 1,023 ft, sim 1,020 ft",
                "heading: channel 90.5 deg, sim 90.0 deg",
                "fuel: channel 2,939 kg, sim 2,938 kg"
            }, gate.DescribeComparisons());
        });
    }

    [Fact]
    public void Describes_a_missing_value_as_missing()
    {
        var gate = new FenixChannelTrust(new HashSet<string>());

        Assert.Equal(new[]
        {
            "FD captain: channel missing, sim missing",
            "FD first officer: channel missing, sim missing",
            "altitude: channel missing, sim missing",
            "heading: channel missing, sim missing",
            "fuel: channel missing, sim missing"
        }, gate.DescribeComparisons());

        // A frame has come, but SimConnect has delivered only the altitude, and the frame's altitude and
        // FOB text cannot be read.
        gate.OnConnected(0);
        gate.OnComparison(FenixComparison.AltitudeFt, 1020, 0);
        gate.OnFrame(Frame(altitude: double.NaN, fob: ""), 0);

        Assert.Equal(new[]
        {
            "FD captain: channel on, sim missing",
            "FD first officer: channel on, sim missing",
            "altitude: channel missing, sim 1,020 ft",
            "heading: channel 90.0 deg, sim missing",
            "fuel: channel missing, sim missing"
        }, gate.DescribeComparisons());
    }

    [Fact]
    public void Describes_a_light_that_reads_neither_zero_nor_one_by_its_raw_value()
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate, fdCapt: 2, fdFo: 0.5);

        gate.OnFrame(Frame(), 0);

        IReadOnlyList<string> lines = gate.DescribeComparisons();
        Assert.Equal("FD captain: channel on, sim 2", lines[0]);
        Assert.Equal("FD first officer: channel on, sim 0.5", lines[1]);
    }

    [Fact]
    public void A_frame_that_fails_its_shape_check_gives_no_values()
    {
        FenixChannelTrust gate = TrustedGate();

        // The defaults a malformed frame carries (altitude 0, both flight directors off) are not what the
        // channel said, so the description keeps the last good values.
        gate.OnFrame(FenixChannelFrame.MalformedFrame, 100);

        IReadOnlyList<string> lines = gate.DescribeComparisons();
        Assert.Contains("FD captain: channel on, sim on", lines);
        Assert.Contains("altitude: channel 1,000 ft, sim 1,000 ft", lines);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    // A gate that has connected at t=0 and learned a checked build: no comparison value and no frame yet.
    private static FenixChannelTrust ConnectedGate()
    {
        var gate = new FenixChannelTrust(new HashSet<string> { CheckedBuild });
        gate.OnConnected(0);
        gate.OnBuild(CheckedBuild);
        return gate;
    }

    // SimConnect's side of the five comparisons, delivered at atMs. The defaults agree with Frame()'s.
    private static void Compare(FenixChannelTrust gate, double fdCapt = 1, double fdFo = 1, double altitude = 1000,
        double heading = 90, double fuelKg = 2938, long atMs = 0)
    {
        gate.OnComparison(FenixComparison.FdLightCapt, fdCapt, atMs);
        gate.OnComparison(FenixComparison.FdLightFo, fdFo, atMs);
        gate.OnComparison(FenixComparison.AltitudeFt, altitude, atMs);
        gate.OnComparison(FenixComparison.MagHeadingDeg, heading, atMs);
        gate.OnComparison(FenixComparison.FuelKg, fuelKg, atMs);
    }

    // A gate that trusts every display: connected, a checked build, every comparison agreeing, one frame at t=0.
    private static FenixChannelTrust TrustedGate()
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);
        gate.OnFrame(Frame(), 0);
        return gate;
    }

    // A frame that passes every check against Compare()'s defaults: 16 empty FMA slots, both flight directors
    // on, altitude 1000, heading 90, and the FOB line of a real push.
    private static FenixChannelFrame Frame(bool fdCapt = true, bool fdFo = true, double altitude = 1000,
        double heading = 90, string fob = "wFOB:   g2940 cs KG", IReadOnlyList<FenixFmaSlot>? fma = null,
        IReadOnlyList<string>? upperLeft = null, IReadOnlyList<string>? upperRight = null,
        IReadOnlyList<string>? wrongShape = null) =>
        new(Malformed: false, WrongShapeFields: wrongShape ?? Array.Empty<string>(),
            Fd: FenixFdState.Default with { FdOnCapt = fdCapt, FdOnFo = fdFo },
            Fma: fma ?? EmptyFma(), UpperEcamLeft: upperLeft ?? Array.Empty<string>(),
            UpperEcamRight: upperRight ?? Array.Empty<string>(), FwcPage: 0, FwcPhase: 0,
            Altitude: altitude, MagHeading: heading, FobText: fob);

    private static FenixFmaSlot[] EmptyFma() => Enumerable.Repeat(new FenixFmaSlot("", false), 16).ToArray();

    private static FenixFmaSlot[] FmaWith(int slot, string text)
    {
        FenixFmaSlot[] fma = EmptyFma();
        fma[slot] = new FenixFmaSlot(text, false);
        return fma;
    }

    // Feeds the same frame every 100 ms from one time to the other, both included.
    private static void Feed(FenixChannelTrust gate, long fromMs, long toMs, FenixChannelFrame frame)
    {
        for (long t = fromMs; t <= toMs; t += 100)
        {
            gate.OnFrame(frame, t);
        }
    }

    // Flies a channel and a SimConnect that both follow a value over time, from t=0 to untilMs: a frame every
    // 60 ms (the channel sends about 17 a second), and SimConnect's value once a second, starting at t=0. A
    // delivery arrives between the frames, at its own second, and carries the value at the moment it arrives.
    // afterFrame sees the time of each frame once the gate has taken it.
    private static void Fly(FenixChannelTrust gate, FenixComparison kind, Func<long, FenixChannelFrame> frameAt,
        Func<long, double> simValueAt, long untilMs, Action<long> afterFrame)
    {
        long nextDeliveryMs = 0;
        for (long t = 0; t <= untilMs; t += 60)
        {
            while (nextDeliveryMs <= t)
            {
                gate.OnComparison(kind, simValueAt(nextDeliveryMs), nextDeliveryMs);
                nextDeliveryMs += 1_000;
            }

            gate.OnFrame(frameAt(t), t);
            afterFrame(t);
        }
    }

    private static FenixDisplayVerdict Untrusted(string reason) => new(false, reason);

    private static FenixDisplayVerdict Pfd(FenixChannelTrust gate, long nowMs) =>
        gate.VerdictFor(FenixChannelDisplay.Pfd, nowMs);

    private static FenixDisplayVerdict Ewd(FenixChannelTrust gate, long nowMs) =>
        gate.VerdictFor(FenixChannelDisplay.Ewd, nowMs);

    private static FenixDisplayVerdict Sd(FenixChannelTrust gate, long nowMs) =>
        gate.VerdictFor(FenixChannelDisplay.Sd, nowMs);

    private static void AssertAll(FenixChannelTrust gate, long nowMs, FenixDisplayVerdict expected)
    {
        foreach (FenixChannelDisplay display in AllDisplays)
        {
            Assert.Equal(expected, gate.VerdictFor(display, nowMs));
        }
    }

    // Runs the action under another culture, current and UI, and puts both back.
    private static void InCulture(string name, Action action)
    {
        CultureInfo savedCulture = CultureInfo.CurrentCulture;
        CultureInfo savedUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            CultureInfo.CurrentUICulture = new CultureInfo(name);
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
            CultureInfo.CurrentUICulture = savedUiCulture;
        }
    }
}

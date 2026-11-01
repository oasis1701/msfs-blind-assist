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
// least once and is not disagreeing now (a disagreement that lasts three seconds latches until the channel
// reconnects), and no line the display covers holds a text code the app does not know.

using System.Globalization;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelTrustTests
{
    private const string CheckedBuild = "3.0.0.7613";

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

    // ---- The cross-checks: waiting, pending and latched ---------------------------------------------

    [Fact]
    public void A_check_not_yet_passed_keeps_its_display_untrusted()
    {
        FenixChannelTrust gate = ConnectedGate();
        gate.OnComparison(FenixComparison.FdLightCapt, 1);
        gate.OnComparison(FenixComparison.FdLightFo, 1);
        gate.OnComparison(FenixComparison.MagHeadingDeg, 90);
        gate.OnComparison(FenixComparison.FuelKg, 2938);   // SimConnect has not delivered the altitude
        gate.OnFrame(Frame(), 0);

        Assert.Equal(Untrusted("altitude not checked yet"), Pfd(gate, 0));
        Assert.Equal(Trusted, Ewd(gate, 0));
        Assert.Equal(Trusted, Sd(gate, 0));

        gate.OnComparison(FenixComparison.AltitudeFt, 1000);
        gate.OnFrame(Frame(), 100);

        Assert.Equal(Trusted, Pfd(gate, 100));
    }

    [Fact]
    public void A_pending_disagreement_untrusts_without_latching()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000);

        Feed(gate, 100, 2_900, Frame());

        // Not latched: the reason says "disagrees" with both values, and only the PFD is affected.
        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 2_900));
        Assert.Equal(Trusted, Ewd(gate, 2_900));
        Assert.Equal(Trusted, Sd(gate, 2_900));

        gate.OnComparison(FenixComparison.AltitudeFt, 1000);
        gate.OnFrame(Frame(), 3_000);

        Assert.Equal(Trusted, Pfd(gate, 3_000));
    }

    [Fact]
    public void A_disagreement_lasting_the_hold_latches_until_reconnect()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000);

        Feed(gate, 4_000, 6_900, Frame());   // disagreeing since t=4000: 2,900 ms

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 6_900));

        gate.OnFrame(Frame(), 7_000);   // 3,000 ms: it counts

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 7_000));

        // Agreeing again changes nothing, and the reason keeps the values that latched it.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000);
        gate.OnFrame(Frame(), 7_200);

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 7_200));
        Assert.Equal(Trusted, Ewd(gate, 7_200));
        Assert.Equal(Trusted, Sd(gate, 7_200));

        // Nor does a different disagreement: a latched check is inert.
        gate.OnComparison(FenixComparison.AltitudeFt, 6000);
        gate.OnFrame(Frame(), 7_300);

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 7_300));

        // SimConnect's altitude agrees again, and the channel reconnects: it starts fresh. The comparison
        // values are kept across a reconnect, so the next agreeing frame is enough.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000);
        gate.OnConnected(8_000);
        gate.OnBuild(CheckedBuild);
        gate.OnFrame(Frame(), 8_000);

        AssertAll(gate, 8_000, Trusted);
    }

    [Fact]
    public void A_waiting_value_breaks_a_disagreement()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000);
        Feed(gate, 0, 2_000, Frame());   // disagreeing for 2,000 ms

        // SimConnect's value goes missing for one frame: the disagreement is over, not paused. The check
        // had passed, so a waiting value does not take the trust away.
        gate.OnComparison(FenixComparison.AltitudeFt, double.NaN);
        gate.OnFrame(Frame(), 2_500);

        Assert.Equal(Trusted, Pfd(gate, 2_500));

        gate.OnComparison(FenixComparison.AltitudeFt, 5000);
        Feed(gate, 2_600, 5_500, Frame());   // a second run, counted from t=2600

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 5_500));

        gate.OnFrame(Frame(), 5_600);   // 3,000 ms after the second run began

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 5_600));
    }

    [Fact]
    public void A_stall_breaks_a_pending_disagreement()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000);
        Feed(gate, 0, 2_000, Frame());   // disagreeing for 2,000 ms, then the stream goes quiet

        AssertAll(gate, 4_001, Untrusted("stream stalled"));

        // It resumes, still disagreeing. A stall is a break, as a waiting value is: the run counts from the
        // first frame after it (t=5000), not from t=0, so the 3,000 ms are not already over.
        Feed(gate, 5_000, 7_900, Frame());

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 7_900));

        gate.OnFrame(Frame(), 8_000);   // 3,000 ms after the first frame after the stall

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 8_000));
    }

    [Fact]
    public void Pushes_with_no_data_do_not_hide_a_stall_from_a_pending_disagreement()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000);
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
    public void A_stall_breaks_a_pending_disagreement_only_past_the_stale_limit(long nextFrameMs, string reason)
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.AltitudeFt, 5000);
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
        gate.OnComparison(FenixComparison.AltitudeFt, 5000);
        Feed(gate, 100, 3_100, Frame());   // latched

        // Ten seconds of silence, then a frame that agrees.
        gate.OnComparison(FenixComparison.AltitudeFt, 1000);
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
        gate.OnComparison(FenixComparison.AltitudeFt, 5000);
        Feed(gate, 100, 3_100, Frame());

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(gate, 3_100));

        // The captain's flight director now starts to disagree as well. It comes first in the PFD's order,
        // so it gives the reason, though the latched altitude is the heavier fault.
        gate.OnComparison(FenixComparison.FdLightCapt, 0);
        gate.OnFrame(Frame(), 3_200);

        Assert.Equal(Untrusted("FD captain disagrees: channel on, sim off"), Pfd(gate, 3_200));
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
        gate.OnComparison(captain ? FenixComparison.FdLightCapt : FenixComparison.FdLightFo, light);

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
        gate.OnComparison(FenixComparison.FdLightCapt, light);   // the annunciator light test, for one

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
    public void A_fuel_mismatch_leaves_the_pfd_trusted()
    {
        FenixChannelTrust gate = TrustedGate();
        gate.OnComparison(FenixComparison.FuelKg, 5000);

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
        gate.OnComparison(FenixComparison.FdLightCapt, 1);
        gate.OnComparison(FenixComparison.FdLightFo, 1);
        gate.OnComparison(FenixComparison.MagHeadingDeg, 90);   // no altitude yet
        gate.OnFrame(Frame(fma: FmaWith(0, "dX")), 0);

        Assert.Equal(Untrusted("altitude not checked yet"), Pfd(gate, 0));

        gate.OnComparison(FenixComparison.AltitudeFt, 1000);
        gate.OnFrame(Frame(fma: FmaWith(0, "dX")), 100);

        Assert.Equal(Untrusted("unknown code 'd' in the FMA"), Pfd(gate, 100));
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

        Exception? thrown = Record.Exception(() => gate.OnComparison((FenixComparison)99, 1));

        Assert.Null(thrown);
        AssertAll(gate, 0, Trusted);
    }

    // ---- Culture: reasons, descriptions and the FOB number never follow the PC's -------------------

    [Theory]
    [InlineData("de-DE", "wFOB:   g2940.5 cs KG")]
    [InlineData("de-DE", "wFOB:   g2940,5 cs KG")]
    [InlineData("sv-SE", "wFOB:   g2940.5 cs KG")]
    [InlineData("sv-SE", "wFOB:   g2940,5 cs KG")]
    public void The_fob_text_parses_under_comma_decimal_cultures(string culture, string fob)
    {
        InCulture(culture, () =>
        {
            // A fuel check that has never passed, so only a number that parsed can make the E/WD trusted.
            FenixChannelTrust gate = ConnectedGate();
            Compare(gate);

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
        gate.OnComparison(FenixComparison.AltitudeFt, 1020);
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

    // SimConnect's side of the five comparisons. The defaults agree with Frame()'s.
    private static void Compare(FenixChannelTrust gate, double fdCapt = 1, double fdFo = 1, double altitude = 1000,
        double heading = 90, double fuelKg = 2938)
    {
        gate.OnComparison(FenixComparison.FdLightCapt, fdCapt);
        gate.OnComparison(FenixComparison.FdLightFo, fdFo);
        gate.OnComparison(FenixComparison.AltitudeFt, altitude);
        gate.OnComparison(FenixComparison.MagHeadingDeg, heading);
        gate.OnComparison(FenixComparison.FuelKg, fuelKg);
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

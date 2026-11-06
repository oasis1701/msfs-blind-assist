// Characterization tests for FenixChannelTrust, the gate between the Fenix display channel (the gRPC service
// on 127.0.0.1:18083) and a pilot. Pure logic with the clock passed in: every frame is built here, never read
// from Fenix's own capture.
//
// The contract: a display's values are read out only while the gate trusts that display, and a wrong verdict
// is the costliest defect in the channel, so every reason text is pinned. A display is trusted only when
// every condition holds, and the verdict is the first one that fails, in this order: the channel is
// connected; a data message has come since it connected; the stream has not stalled; the build is known;
// the build is one the app has checked; no message was malformed and no field had the wrong shape since it
// connected (a latch). Then the display's own conditions: a powered display unit shows it, or for the ISIS its
// own message says it is powered (else "display off"); for the PFD, its frame has an FMA; and no line the display
// covers holds a text code the app does not know. The gate judges six displays: the PFD, the E/WD, the SD, the ND,
// the ISIS and the DCDU; the ND, the ISIS and the DCDU have no text check of the gate's. A verdict says that the
// display itself is dark (IsDisplayOff) only for "display off", which comes after every channel-wide condition: a
// stopped, silent or latched channel, or a build not checked, never reads as a dark display, whatever the last frame
// showed.
//
// The gate never compares the channel's values with SimConnect's ([FNX-22]), so no value in a frame can untrust a
// display.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelTrustTests
{
    private const string CheckedBuild = "3.0.0.7613";

    private static readonly FenixDisplayVerdict Trusted = new(true, "trusted");

    private static readonly FenixChannelDisplay[] AllDisplays =
    {
        FenixChannelDisplay.Pfd, FenixChannelDisplay.Ewd, FenixChannelDisplay.Sd,
        FenixChannelDisplay.Nd, FenixChannelDisplay.Isis, FenixChannelDisplay.Dcdu
    };

    // ---- The channel-wide conditions, in the order of the table -----------------------------------

    [Fact]
    public void A_healthy_channel_with_every_display_live_is_trusted()
    {
        FenixChannelTrust gate = TrustedGate();

        Assert.Equal(Trusted, Pfd(gate, 0));
        Assert.Equal(Trusted, Ewd(gate, 0));
        Assert.Equal(Trusted, Sd(gate, 0));
        Assert.Equal(Trusted, Nd(gate, 0));
        Assert.Equal(Trusted, Isis(gate, 0));
        Assert.Equal(Trusted, Dcdu(gate, 0));
    }

    [Theory]
    [InlineData(true, 90.0, "wFOB:   g2940 cs KG")]
    [InlineData(false, 271.5, "wFOB:   g6480 cs LBS")]
    [InlineData(false, double.NaN, "")]              // what a display cannot draw yet
    [InlineData(false, -5.1, "wFOB:    aXXX cs KG")]  // a heading near north goes negative; the crosses of a dark FOB
    public void No_value_in_a_frame_untrusts_a_display(bool flightDirectors, double heading, string fob)
    {
        // The gate never judges a value: what a display draws can differ from SimConnect's for no fault of the channel,
        // and comparing them untrusted healthy displays in flight ([FNX-22], 2026-11-04). Whatever a frame says, a live
        // display of a healthy channel is trusted.
        FenixChannelTrust gate = ConnectedGate();

        gate.OnFrame(Frame(fdOn: flightDirectors, heading: heading, fob: fob), 0);

        AssertAll(gate, 0, Trusted);
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
        gate.OnFrame(Frame(), 0);

        AssertAll(gate, 0, Untrusted("build unknown"));
    }

    [Fact]
    public void An_unchecked_build_is_not_trusted()
    {
        FenixChannelTrust gate = ConnectedGate();
        gate.OnBuild("3.0.0.7700");
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
        gate.OnFrame(Frame(), 0);

        AssertAll(gate, 0, Untrusted($"build {build} not checked"));
    }

    [Fact]
    public void An_empty_checked_list_trusts_nothing()
    {
        var gate = new FenixChannelTrust(new HashSet<string>());
        gate.OnConnected(0);
        gate.OnBuild(CheckedBuild);
        gate.OnFrame(Frame(), 0);

        AssertAll(gate, 0, Untrusted("build 3.0.0.7613 not checked"));
    }

    [Fact]
    public void The_channel_wide_reasons_come_in_the_order_of_the_table()
    {
        var gate = new FenixChannelTrust(new HashSet<string> { CheckedBuild });
        gate.OnConnected(0);

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

        gate.OnFrame(FenixChannelFrame.NoData, 100);

        AssertAll(gate, 100, Untrusted("no data yet"));

        gate.OnFrame(Frame(), 200);

        AssertAll(gate, 200, Trusted);
    }

    [Fact]
    public void Pushes_with_no_data_do_not_keep_the_stream_alive_and_never_latch()
    {
        FenixChannelTrust gate = TrustedGate();   // the last push with data came at t=0

        Feed(gate, 100, 2_000, FenixChannelFrame.NoData);

        AssertAll(gate, 2_000, Trusted);
        AssertAll(gate, 2_001, Untrusted("stream stalled"));

        Feed(gate, 2_100, 5_000, FenixChannelFrame.NoData);

        AssertAll(gate, 5_000, Untrusted("stream stalled"));

        gate.OnFrame(Frame(), 5_100);

        AssertAll(gate, 5_100, Trusted);
    }

    // ---- A display no powered unit shows ----------------------------------------------------------

    [Fact]
    public void A_dark_display_is_not_trusted()
    {
        // Cold and dark the channel still sends its altitude and heading (3.0.0.7613, 2026-11-02), so a read would speak
        // what no display shows. Being live is each display's first condition of its own, the SD's included.
        FenixChannelTrust gate = ConnectedGate();

        Feed(gate, 0, 5_000, ColdAndDark());

        AssertAll(gate, 5_000, Untrusted("display off"));
    }

    [Fact]
    public void A_display_is_trusted_once_it_is_live()
    {
        FenixChannelTrust gate = ConnectedGate();
        Feed(gate, 0, 1_000, Dark());

        AssertAll(gate, 1_000, Untrusted("display off"));

        gate.OnFrame(Frame(), 1_100);   // power-up: a powered unit shows each display

        AssertAll(gate, 1_100, Trusted);

        gate.OnFrame(Dark(), 1_200);   // and dark again

        AssertAll(gate, 1_200, Untrusted("display off"));
    }

    [Fact]
    public void Each_display_is_live_on_its_own()
    {
        // One display at a time is live in a frame, the five others off: each verdict follows its own flag only.
        FenixChannelTrust gate = TrustedGate();

        long nowMs = 100;
        foreach (FenixChannelDisplay live in AllDisplays)
        {
            gate.OnFrame(OnlyLive(live), nowMs);

            foreach (FenixChannelDisplay display in AllDisplays)
            {
                Assert.Equal(
                    display == live ? Trusted : Untrusted("display off"),
                    gate.VerdictFor(display, nowMs));
            }

            nowMs += 100;
        }
    }

    [Fact]
    public void Display_off_comes_before_the_displays_other_conditions()
    {
        // No FMA and an unknown code in the upper ECAM: a dark display reads "display off" before either, and each shows
        // once the display is live.
        FenixChannelTrust gate = ConnectedGate();
        FenixFmaSlot[] noFma = Array.Empty<FenixFmaSlot>();
        string[] unknown = { "xFAULT" };

        gate.OnFrame(Frame(fma: noFma, upperLeft: unknown, pfdLive: false, ewdLive: false, sdLive: false,
            ndLive: false, isisPowered: false, dcduLive: false), 0);

        AssertAll(gate, 0, Untrusted("display off"));

        gate.OnFrame(Frame(fma: noFma, upperLeft: unknown), 100);

        Assert.Equal(Untrusted("no FMA"), Pfd(gate, 100));
        Assert.Equal(Untrusted("unknown code 'x' in the upper ECAM"), Ewd(gate, 100));
        Assert.Equal(Trusted, Sd(gate, 100));   // the SD's one condition is being live
        Assert.Equal(Trusted, Nd(gate, 100));   // and the ND's: neither the FMA nor the upper ECAM is its to judge
        Assert.Equal(Trusted, Isis(gate, 100));   // the ISIS and the DCDU, like the SD, have being live only
        Assert.Equal(Trusted, Dcdu(gate, 100));
    }

    [Fact]
    public void The_channel_wide_conditions_come_before_display_off()
    {
        var gate = new FenixChannelTrust(new HashSet<string> { CheckedBuild });
        gate.OnConnected(0);
        gate.OnFrame(Dark(), 0);

        AssertAll(gate, 0, Untrusted("build unknown"));

        gate.OnBuild(CheckedBuild);

        AssertAll(gate, 0, Untrusted("display off"));

        gate.OnFrame(FenixChannelFrame.MalformedFrame, 100);

        AssertAll(gate, 100, Untrusted("malformed message"));
    }

    [Fact]
    public void A_frame_of_defaults_trusts_no_display()
    {
        // A RemoteData holding only fields the app does not read decodes to a frame of defaults: with no duData and no
        // ISIS part, every display is off, so it can never read trusted.
        FenixChannelTrust gate = TrustedGate();

        gate.OnFrame(new FenixChannelFrame(false, Array.Empty<string>(), FenixFdState.Default,
            Array.Empty<FenixFmaSlot>(), Array.Empty<string>(), Array.Empty<string>(), 0, 0, 0, ""), 100);

        AssertAll(gate, 100, Untrusted("display off"));
    }

    // ---- Whether a verdict says the display itself is dark -------------------------------------------

    [Fact]
    public void The_display_off_verdict_says_the_display_is_dark()
    {
        // The channel is healthy and no powered unit shows the display: the one verdict whose key opens the window on its
        // status row (spec 2.4) and not its AI read. Each display is dark on its own.
        FenixChannelTrust gate = ConnectedGate();
        Feed(gate, 0, 1_000, Dark());

        foreach (FenixChannelDisplay display in AllDisplays)
        {
            FenixDisplayVerdict verdict = gate.VerdictFor(display, 1_000);

            Assert.Equal(Untrusted("display off"), verdict);
            Assert.True(verdict.IsDisplayOff, $"the {display} is dark");
        }

        // One display dark in a frame the others are lit in.
        gate.OnFrame(Frame(ewdLive: false), 1_100);

        Assert.True(Ewd(gate, 1_100).IsDisplayOff);
        Assert.False(Pfd(gate, 1_100).IsDisplayOff);
    }

    [Fact]
    public void A_trusted_verdict_is_not_display_off()
    {
        FenixChannelTrust gate = TrustedGate();

        foreach (FenixChannelDisplay display in AllDisplays)
        {
            Assert.False(gate.VerdictFor(display, 0).IsDisplayOff, $"the {display} is lit");
        }

        // The words alone do not make it so: a verdict that trusts is never dark.
        Assert.False(new FenixDisplayVerdict(true, "display off").IsDisplayOff);
        Assert.True(new FenixDisplayVerdict(false, "display off").IsDisplayOff);
        Assert.False(new FenixDisplayVerdict(false, "Display off").IsDisplayOff);
    }

    [Fact]
    public void A_lit_display_whose_own_condition_fails_is_not_display_off()
    {
        // Each of these keeps its key on the AI read: the screen is live, and the display's own condition says why it
        // cannot be read.
        FenixChannelTrust gate = ConnectedGate();

        gate.OnFrame(Frame(fma: Array.Empty<FenixFmaSlot>(), upperLeft: new[] { "xFAULT" }), 0);

        Assert.Equal(Untrusted("no FMA"), Pfd(gate, 0));
        Assert.False(Pfd(gate, 0).IsDisplayOff);
        Assert.Equal(Untrusted("unknown code 'x' in the upper ECAM"), Ewd(gate, 0));
        Assert.False(Ewd(gate, 0).IsDisplayOff);
    }

    [Fact]
    public void A_build_that_is_not_checked_is_not_display_off_though_the_display_is_dark()
    {
        FenixChannelTrust gate = ConnectedGate();
        gate.OnBuild("3.0.0.7700");
        gate.OnFrame(Dark(), 0);

        foreach (FenixChannelDisplay display in AllDisplays)
        {
            FenixDisplayVerdict verdict = gate.VerdictFor(display, 0);

            Assert.Equal(Untrusted("build 3.0.0.7700 not checked"), verdict);
            Assert.False(verdict.IsDisplayOff, $"the {display} reads the channel's reason");
        }
    }

    [Fact]
    public void A_stopped_or_silent_channel_is_not_display_off_though_the_display_also_reads_dark()
    {
        // The channel-wide reason wins over the display's own, so a screen that may still be live is never taken for a
        // dark one: in each of these the channel fails in its own way, and where there is a frame at all, every display
        // in it is dark.
        void AssertNone(FenixChannelTrust gate, long nowMs, string reason)
        {
            foreach (FenixChannelDisplay display in AllDisplays)
            {
                FenixDisplayVerdict verdict = gate.VerdictFor(display, nowMs);

                Assert.Equal(Untrusted(reason), verdict);
                Assert.False(verdict.IsDisplayOff, $"the {display} reads '{reason}'");
            }
        }

        // The stream stops answering after the displays went dark.
        FenixChannelTrust stopped = ConnectedGate();
        stopped.OnFrame(Dark(), 0);
        Assert.True(Pfd(stopped, 0).IsDisplayOff);
        stopped.OnDisconnected("connection reset");
        AssertNone(stopped, 0, "channel not answering: connection reset");

        // Never connected.
        AssertNone(new FenixChannelTrust(new HashSet<string> { CheckedBuild }), 0, "channel not answering");

        // Connected, no message yet.
        var silent = new FenixChannelTrust(new HashSet<string> { CheckedBuild });
        silent.OnConnected(0);
        AssertNone(silent, 60_000, "no data yet");

        // The stream stalls after a dark frame.
        FenixChannelTrust stalled = ConnectedGate();
        stalled.OnFrame(Dark(), 0);
        Assert.True(Pfd(stalled, 2_000).IsDisplayOff);
        AssertNone(stalled, 2_001, "stream stalled");

        // A dark frame, then a malformed message: the latch.
        FenixChannelTrust latched = ConnectedGate();
        latched.OnFrame(Dark(), 0);
        latched.OnFrame(FenixChannelFrame.MalformedFrame, 100);
        AssertNone(latched, 100, "malformed message");
    }

    [Fact]
    public void A_display_the_gate_does_not_know_is_not_display_off()
    {
        FenixChannelTrust gate = ConnectedGate();
        gate.OnFrame(Dark(), 0);

        FenixDisplayVerdict verdict = gate.VerdictFor((FenixChannelDisplay)99, 0);

        Assert.Equal(Untrusted("not a display of the channel"), verdict);
        Assert.False(verdict.IsDisplayOff);
    }

    // ---- The ND, the ISIS and the DCDU ------------------------------------------------------------

    [Fact]
    public void The_nd_is_live_on_a_powered_nd_unit()
    {
        FenixChannelTrust gate = ConnectedGate();

        gate.OnFrame(Frame(ndLive: false), 0);

        Assert.Equal(Untrusted("display off"), Nd(gate, 0));

        gate.OnFrame(Frame(), 100);

        Assert.Equal(Trusted, Nd(gate, 100));
    }

    [Fact]
    public void The_displays_with_no_text_of_the_gates_stay_trusted_while_the_others_are_not()
    {
        // The gate reads no text of the SD, the ND, the ISIS or the DCDU: an FMA that is gone and an unknown code in the
        // upper ECAM take the PFD and the E/WD down, and these four stay trusted while they are live.
        FenixChannelTrust gate = TrustedGate();

        Feed(gate, 100, 5_000, Frame(fma: Array.Empty<FenixFmaSlot>(), upperLeft: new[] { "xFAULT" }));

        Assert.Equal(Untrusted("no FMA"), Pfd(gate, 5_000));
        Assert.Equal(Untrusted("unknown code 'x' in the upper ECAM"), Ewd(gate, 5_000));
        Assert.Equal(Trusted, Sd(gate, 5_000));
        Assert.Equal(Trusted, Nd(gate, 5_000));
        Assert.Equal(Trusted, Isis(gate, 5_000));
        Assert.Equal(Trusted, Dcdu(gate, 5_000));
    }

    // ---- Text codes the app does not know ---------------------------------------------------------

    [Fact]
    public void An_unknown_code_untrusts_its_display_without_latching()
    {
        FenixChannelTrust gate = TrustedGate();

        // Ten seconds, long enough for any latch: it is an exact condition, not a noisy one.
        Feed(gate, 100, 10_000, Frame(upperLeft: new[] { "xENG 1 FIRE" }));

        Assert.Equal(Untrusted("unknown code 'x' in the upper ECAM"), Ewd(gate, 10_000));
        Assert.Equal(Trusted, Pfd(gate, 10_000));
        Assert.Equal(Trusted, Sd(gate, 10_000));
        Assert.Equal(Trusted, Nd(gate, 10_000));
        Assert.Equal(Trusted, Isis(gate, 10_000));
        Assert.Equal(Trusted, Dcdu(gate, 10_000));

        gate.OnFrame(Frame(), 10_100);

        Assert.Equal(Trusted, Ewd(gate, 10_100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    public void An_unknown_code_in_the_fma_untrusts_the_pfd(int slot)
    {
        FenixChannelTrust gate = TrustedGate();

        gate.OnFrame(Frame(fma: FmaWith(slot, "kX")), 100);

        Assert.Equal(Untrusted("unknown code 'k' in the FMA"), Pfd(gate, 100));
        Assert.Equal(Trusted, Ewd(gate, 100));
        Assert.Equal(Trusted, Sd(gate, 100));
        Assert.Equal(Trusted, Nd(gate, 100));
        Assert.Equal(Trusted, Isis(gate, 100));
        Assert.Equal(Trusted, Dcdu(gate, 100));
    }

    [Theory]
    [InlineData("right", "unknown code 'x' in the upper ECAM")]
    [InlineData("fob", "unknown code 'q' in the FOB line")]
    public void An_unknown_code_names_the_line_it_came_from(string line, string reason)
    {
        FenixChannelTrust gate = TrustedGate();
        FenixChannelFrame frame = line == "right"
            ? Frame(upperRight: new[] { "wOK", "xFAULT" })
            : Frame(fob: "wFOB:   g2940 cq KG");

        gate.OnFrame(frame, 100);

        Assert.Equal(Untrusted(reason), Ewd(gate, 100));
        Assert.Equal(Trusted, Pfd(gate, 100));
    }

    [Fact]
    public void The_first_unknown_code_in_reading_order_is_named()
    {
        FenixChannelTrust gate = TrustedGate();

        // The left column comes before the right one, and within a line the first letter wins.
        gate.OnFrame(Frame(upperLeft: new[] { "wOK", "kFAULTx" }, upperRight: new[] { "qBAD" }), 100);

        Assert.Equal(Untrusted("unknown code 'k' in the upper ECAM"), Ewd(gate, 100));
    }

    [Fact]
    public void The_red_lines_of_a_takeoff_config_warning_keep_the_ewd_trusted()
    {
        // The TO CONFIG test with the flaps up on 3.0.0.7613 (2026-11-02) read "unknown code 'r' in the upper ECAM"
        // while red's letter was unknown. Red is r, so those lines hold no unknown code.
        FenixChannelTrust gate = TrustedGate();

        gate.OnFrame(Frame(upperLeft: new[]
        {
            "urCONFIGu ", "rSLATS NOT IN T.O CONFIG", "urCONFIGu ", "rFLAPS NOT IN T.O CONFIG"
        }), 100);

        Assert.Equal(Trusted, Ewd(gate, 100));
    }

    // ---- A frame with no FMA ----------------------------------------------------------------------

    [Fact]
    public void A_frame_with_no_fma_untrusts_only_the_pfd_and_never_latches()
    {
        // protobuf-net leaves out an empty or null array, so an unpowered PFD can send no FMA slot at all. That is
        // not a reshaped field (the decoder records no "111"): the PFD has nothing to read, and every other display
        // keeps its verdict.
        FenixChannelTrust gate = TrustedGate();

        // Ten seconds of them, long enough for any latch.
        Feed(gate, 100, 10_000, Frame(fma: Array.Empty<FenixFmaSlot>()));

        Assert.Equal(Untrusted("no FMA"), Pfd(gate, 10_000));
        Assert.Equal(Trusted, Ewd(gate, 10_000));
        Assert.Equal(Trusted, Sd(gate, 10_000));
        Assert.Equal(Trusted, Nd(gate, 10_000));
        Assert.Equal(Trusted, Isis(gate, 10_000));
        Assert.Equal(Trusted, Dcdu(gate, 10_000));

        // The next frame with its 16 slots trusts the PFD again: nothing latched.
        gate.OnFrame(Frame(), 10_100);

        AssertAll(gate, 10_100, Trusted);
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

        AssertAll(gate, 5_000, Trusted);
    }

    // ---- Failing closed ---------------------------------------------------------------------------

    [Fact]
    public void A_display_the_gate_does_not_know_is_not_trusted()
    {
        FenixChannelTrust gate = TrustedGate();

        FenixDisplayVerdict verdict = gate.VerdictFor((FenixChannelDisplay)7, 0);

        Assert.False(verdict.Trusted);
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    // A gate that has connected at t=0 and learned a checked build: no frame yet.
    private static FenixChannelTrust ConnectedGate()
    {
        var gate = new FenixChannelTrust(new HashSet<string> { CheckedBuild });
        gate.OnConnected(0);
        gate.OnBuild(CheckedBuild);
        return gate;
    }

    // A gate that trusts every display: connected, a checked build, one frame at t=0.
    private static FenixChannelTrust TrustedGate()
    {
        FenixChannelTrust gate = ConnectedGate();
        gate.OnFrame(Frame(), 0);
        return gate;
    }

    // A frame with nothing wrong: every display live (a powered unit shows each of the five that units carry, and the
    // ISIS says it is powered; a test about another condition never repeats the flags), 16 empty FMA slots, both flight
    // directors on, heading 90, and the FOB line of a real push.
    private static FenixChannelFrame Frame(bool fdOn = true, double heading = 90, string fob = "wFOB:   g2940 cs KG",
        IReadOnlyList<FenixFmaSlot>? fma = null, IReadOnlyList<string>? upperLeft = null,
        IReadOnlyList<string>? upperRight = null, IReadOnlyList<string>? wrongShape = null, bool pfdLive = true,
        bool ewdLive = true, bool sdLive = true, bool ndLive = true, bool isisPowered = true, bool dcduLive = true) =>
        new(Malformed: false, WrongShapeFields: wrongShape ?? Array.Empty<string>(),
            Fd: FenixFdState.Default with { FdOnCapt = fdOn, FdOnFo = fdOn },
            Fma: fma ?? EmptyFma(), UpperEcamLeft: upperLeft ?? Array.Empty<string>(),
            UpperEcamRight: upperRight ?? Array.Empty<string>(), FwcPage: 0, FwcPhase: 0,
            MagHeading: heading, FobText: fob)
        {
            PfdLive = pfdLive,
            EwdLive = ewdLive,
            SdLive = sdLive,
            NdLive = ndLive,
            IsisPowered = isisPowered,
            DcduLive = dcduLive
        };

    // A frame as 3.0.0.7613 sent it cold and dark (2026-11-02): no display live, the 16 FMA slots empty, both flight
    // directors off and the FOB amount amber crosses, while the heading goes on tracking the sim's.
    private static FenixChannelFrame ColdAndDark() =>
        Frame(fdOn: false, fob: "wFOB:    aXXX cs KG", pfdLive: false, ewdLive: false, sdLive: false,
            ndLive: false, isisPowered: false, dcduLive: false);

    // A frame with every display off and nothing else wrong.
    private static FenixChannelFrame Dark() => Frame(pfdLive: false, ewdLive: false, sdLive: false, ndLive: false,
        isisPowered: false, dcduLive: false);

    // A frame with this one display live and the five others off, and nothing else wrong.
    private static FenixChannelFrame OnlyLive(FenixChannelDisplay live) => Frame(
        pfdLive: live == FenixChannelDisplay.Pfd, ewdLive: live == FenixChannelDisplay.Ewd,
        sdLive: live == FenixChannelDisplay.Sd, ndLive: live == FenixChannelDisplay.Nd,
        isisPowered: live == FenixChannelDisplay.Isis, dcduLive: live == FenixChannelDisplay.Dcdu);

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

    private static FenixDisplayVerdict Nd(FenixChannelTrust gate, long nowMs) =>
        gate.VerdictFor(FenixChannelDisplay.Nd, nowMs);

    private static FenixDisplayVerdict Isis(FenixChannelTrust gate, long nowMs) =>
        gate.VerdictFor(FenixChannelDisplay.Isis, nowMs);

    private static FenixDisplayVerdict Dcdu(FenixChannelTrust gate, long nowMs) =>
        gate.VerdictFor(FenixChannelDisplay.Dcdu, nowMs);

    private static void AssertAll(FenixChannelTrust gate, long nowMs, FenixDisplayVerdict expected)
    {
        foreach (FenixChannelDisplay display in AllDisplays)
        {
            Assert.Equal(expected, gate.VerdictFor(display, nowMs));
        }
    }
}

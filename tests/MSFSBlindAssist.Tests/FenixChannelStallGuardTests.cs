// Characterization tests for the stall guard of FenixChannelTrust, the gate between the Fenix display channel (the
// gRPC service on 127.0.0.1:18083) and a pilot. The gate compares the channel with SimConnect's values, which reach
// the app on the UI thread, so while that thread is blocked they are stale: the channel goes on (a climb, a turn, a
// flight director press) and the app's copy of SimConnect's value does not, and the comparison would disagree for no
// fault of the Fenix. A timer on the UI thread (the announcer host) therefore gives the gate a heartbeat
// (OnUiHeartbeat), and while the newest one is older than StallGuardMs the gate does not compare.
//
// The contract: while the newest heartbeat is older than StallGuardMs (1,000 ms; a heartbeat exactly that old is not
// over it), every comparison waits, as a missing value does: its hold clock restarts, nothing passes, nothing starts
// or ends a disagreement, a check that passed stays passed and a latch stays latched. The channel's values are still
// kept. Before the first heartbeat ever the guard is off, so a gate with no host (every other test of the channel, and
// a monitor nothing beats for) judges as it always did, and the heartbeat belongs to the UI thread and not to a
// connection, so a reconnect keeps it. A heartbeat that ends a stall longer than StallGuardMs returns the line for
// the log, once; every other heartbeat returns null.
//
// Pure logic with the clock passed in, as in FenixChannelTrustTests. The one exception is the last test, which lets
// the monitor's real clock run past the limit once to prove that the monitor hands its heartbeat to the gate and
// writes the line it gets back: it is the only test in this file that waits.

using System.Globalization;
using MSFSBlindAssist.Services;
using static MSFSBlindAssist.Tests.MonitorTestKit;

namespace MSFSBlindAssist.Tests;

public class FenixChannelStallGuardTests
{
    private static readonly FenixDisplayVerdict Trusted = new(true, "trusted");

    // ---- While the UI thread is stalled, every comparison waits -------------------------------------

    [Fact]
    public void A_stalled_ui_thread_makes_comparisons_wait()
    {
        // Nothing passes. SimConnect's five values agree with the channel's, but the thread that delivers them ran its
        // timer last at t=0 and the first message comes at t=1,001: no check passes on values that may be stale, however
        // long the stall goes on. The displays with no cross-check do not depend on it.
        FenixChannelTrust fresh = ConnectedGate();
        Compare(fresh);
        fresh.OnUiHeartbeat(0);

        fresh.OnFrame(Frame(), 1_001);

        Assert.Equal(Untrusted("FD captain not checked yet"), Pfd(fresh, 1_001));

        Feed(fresh, 1_100, 10_000, Frame());

        Assert.Equal(Untrusted("FD captain not checked yet"), Pfd(fresh, 10_000));
        Assert.Equal(Untrusted("fuel not checked yet"), Ewd(fresh, 10_000));
        Assert.Equal(Untrusted("heading not checked yet"), Nd(fresh, 10_000));
        Assert.Equal(Trusted, Sd(fresh, 10_000));
        Assert.Equal(Trusted, Isis(fresh, 10_000));
        Assert.Equal(Trusted, Dcdu(fresh, 10_000));

        // Nothing ends a disagreement. The altitude disagrees from the first message, while the thread still beats.
        FenixChannelTrust disagreeing = ConnectedGate();
        Compare(disagreeing, altitude: 5_000);   // SimConnect says 5,000 ft, the channel 1,000 ft
        disagreeing.OnUiHeartbeat(0);
        Feed(disagreeing, 0, 1_000, Frame());   // 1,000 ms of disagreement; a beat 1,000 ms old is not over the limit

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(disagreeing, 1_000));

        // The thread blocks, and the channel's altitude comes to agree with the SimConnect value the app last had.
        // That is waiting, not agreeing: the disagreement stays in force, whatever the messages say.
        Feed(disagreeing, 1_100, 10_000, Frame(altitude: 5_000));

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(disagreeing, 10_000));

        // The thread runs again, and the first comparison after the stall is the one that counts: it agrees.
        disagreeing.OnUiHeartbeat(10_100);
        disagreeing.OnFrame(Frame(altitude: 5_000), 10_100);

        Assert.Equal(Trusted, Pfd(disagreeing, 10_100));
    }

    [Fact]
    public void A_stall_never_starts_a_disagreement_or_a_latch()
    {
        // Every check has passed and the thread beats at t=0, then blocks for eight seconds. The channel goes on: both
        // flight directors go off, the aircraft climbs and turns, the fuel burns. SimConnect's values (delivered at t=0)
        // stand still, as they do on a blocked thread. Compared, all five would disagree once the t=0 values are out of
        // the 1,500 ms window and latch three seconds on; waiting, none does, so no display is ever untrusted.
        FenixChannelTrust gate = TrustedGate();
        gate.OnUiHeartbeat(0);
        FenixChannelFrame movedOn = Frame(
            fdCapt: false, fdFo: false, altitude: 3_000, heading: 180, fob: "wFOB:   g2000 cs KG");

        for (long t = 100; t <= 8_000; t += 100)
        {
            gate.OnFrame(movedOn, t);

            AssertAll(gate, t, Trusted);
        }

        // The thread runs again and brings SimConnect's values with it. They agree, and no latch is left behind.
        gate.OnUiHeartbeat(8_100);
        Compare(gate, fdCapt: 0, fdFo: 0, altitude: 3_000, heading: 180, fuelKg: 2_000, atMs: 8_100);
        gate.OnFrame(movedOn, 8_100);

        AssertAll(gate, 8_100, Trusted);

        // A disagreement already running when the stall begins is not latched by it either. This one is real (5,000 ft
        // from SimConnect against 1,000 ft); the stall's ten seconds of messages are waiting, so each one restarts the
        // hold clock, and the disagreement stays in force without latching, however long the stall goes on.
        FenixChannelTrust running = ConnectedGate();
        Compare(running, altitude: 5_000);
        running.OnUiHeartbeat(0);
        Feed(running, 0, 1_000, Frame());       // disagreeing for 1,000 ms, the beat not yet over the limit
        Feed(running, 1_100, 11_000, Frame());   // the thread blocks: ten more seconds of the same disagreement

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(running, 11_000));

        // And once it runs again the count starts with the first comparison after the stall, not before it: not
        // latched 2,900 ms later, latched 3,000 ms later.
        Run(running, 11_100, 14_000, Frame());

        Assert.Equal(Untrusted("altitude disagrees: channel 1,000 ft, sim 5,000 ft"), Pfd(running, 14_000));

        Run(running, 14_100, 14_100, Frame());

        Assert.Equal(Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(running, 14_100));
    }

    [Fact]
    public void A_fresh_heartbeat_compares_again()
    {
        // A check that could not pass during the stall passes on the first message after the beat that ends it.
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);
        gate.OnUiHeartbeat(0);
        Feed(gate, 1_100, 3_000, Frame());

        Assert.Equal(Untrusted("FD captain not checked yet"), Pfd(gate, 3_000));

        gate.OnUiHeartbeat(3_100);
        gate.OnFrame(Frame(), 3_100);

        AssertAll(gate, 3_100, Trusted);

        // And a disagreement is found at once: the guard postpones a comparison, it does not excuse one. The channel
        // climbs through a stall while the app's SimConnect value stands still (waiting, so still trusted); when the
        // thread runs again, SimConnect's fresh altitude is not the channel's either.
        FenixChannelTrust climbing = TrustedGate();
        climbing.OnUiHeartbeat(0);
        Feed(climbing, 100, 3_000, Frame(altitude: 3_000));

        AssertAll(climbing, 3_000, Trusted);

        climbing.OnUiHeartbeat(3_100);
        climbing.OnComparison(FenixComparison.AltitudeFt, 5_000, 3_100);
        climbing.OnFrame(Frame(altitude: 3_000), 3_100);

        Assert.Equal(Untrusted("altitude disagrees: channel 3,000 ft, sim 5,000 ft"), Pfd(climbing, 3_100));
    }

    [Theory]
    [InlineData(1_000, true)]    // 1,000 ms after the last heartbeat: exactly StallGuardMs, not over it
    [InlineData(1_001, false)]   // 1,001 ms: over it
    public void A_stall_of_exactly_the_guard_is_not_over_it(long messageMs, bool compares)
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);
        gate.OnUiHeartbeat(0);

        gate.OnFrame(Frame(), messageMs);

        Assert.Equal(compares ? Trusted : Untrusted("FD captain not checked yet"), Pfd(gate, messageMs));
    }

    // ---- Before the first heartbeat, and across a reconnect ---------------------------------------

    [Fact]
    public void No_heartbeat_yet_means_no_guard()
    {
        // A gate with no host never hears a heartbeat, and judges as it always did however late the clock reads: the
        // first message a minute after connecting passes its checks, and a disagreement latches after the hold.
        FenixChannelTrust passing = ConnectedGate();
        Compare(passing);

        passing.OnFrame(Frame(), 60_000);

        AssertAll(passing, 60_000, Trusted);

        FenixChannelTrust disagreeing = ConnectedGate();
        Compare(disagreeing, altitude: 5_000);

        Feed(disagreeing, 60_000, 63_000, Frame());

        Assert.Equal(
            Untrusted("altitude latched until reconnect: channel 1,000 ft, sim 5,000 ft"), Pfd(disagreeing, 63_000));
    }

    [Fact]
    public void The_heartbeat_outlasts_a_reconnect()
    {
        // The heartbeat is the UI thread's, not the connection's, as SimConnect's values are. A channel that reconnects
        // while the thread is stalled does not end the stall: the first message of the new connection finds the beat
        // just as old, and waits.
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);
        gate.OnUiHeartbeat(0);

        gate.OnConnected(1_500);
        gate.OnBuild(CheckedBuild);
        gate.OnFrame(Frame(), 2_000);

        Assert.Equal(Untrusted("FD captain not checked yet"), Pfd(gate, 2_000));

        gate.OnUiHeartbeat(2_100);
        gate.OnFrame(Frame(), 2_100);

        AssertAll(gate, 2_100, Trusted);
    }

    // ---- The log line -----------------------------------------------------------------------------

    [Fact]
    public void A_stall_is_logged_once_when_it_ends()
    {
        // Under a comma-decimal culture, as the PC's UI thread may have (the heartbeat is called there): the gate
        // writes the length of the stall in the invariant culture, and the monitor, which formats no number, writes
        // what it is given.
        InCulture("de-DE", () =>
        {
            // The gate, with fixed times: the line comes with the heartbeat that ends a stall longer than StallGuardMs.
            var gate = new FenixChannelTrust(Checked);

            Assert.Null(gate.OnUiHeartbeat(0));      // the first heartbeat ever: no stall before it
            Assert.Null(gate.OnUiHeartbeat(100));    // the host's timer, every 100 ms
            Assert.Null(gate.OnUiHeartbeat(200));
            Assert.Equal("UI thread stalled 1.6 s; comparisons waited", gate.OnUiHeartbeat(1_800));   // a dot, never "1,6"
            Assert.Null(gate.OnUiHeartbeat(1_900));   // once: the beats that follow say nothing
            Assert.Null(gate.OnUiHeartbeat(2_000));

            Assert.Null(gate.OnUiHeartbeat(3_000));   // 1,000 ms after the beat before: exactly the limit, not a stall
            Assert.Equal("UI thread stalled 1.0 s; comparisons waited", gate.OnUiHeartbeat(4_001));   // 1,001 ms after: a stall
            Assert.Equal("UI thread stalled 12.3 s; comparisons waited", gate.OnUiHeartbeat(16_301));   // each stall has its own line

            // The monitor, with its own clock: a stall just over the limit between two heartbeats writes one line, and
            // the heartbeat right after it writes none. The one real wait of this file.
            var lines = new Lines();
            using FenixChannelMonitor monitor = NewIdleMonitor(lines);

            monitor.OnUiHeartbeat();
            long beatMs = Environment.TickCount64;
            while (Environment.TickCount64 - beatMs <= 1_050)
            {
                Thread.Sleep(10);
            }

            monitor.OnUiHeartbeat();
            monitor.OnUiHeartbeat();

            string line = Assert.Single(lines.All);
            Assert.Matches(@"^UI thread stalled [1-9][0-9]*\.[0-9] s; comparisons waited$", line);
        });
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    // A gate that has connected at t=0 and learned a checked build: no comparison value and no frame yet.
    private static FenixChannelTrust ConnectedGate()
    {
        var gate = new FenixChannelTrust(Checked);
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

    // A gate that trusts every display: connected, a checked build, every comparison agreeing, one frame at t=0. No
    // heartbeat yet: a test that wants the guard beats once itself.
    private static FenixChannelTrust TrustedGate()
    {
        FenixChannelTrust gate = ConnectedGate();
        Compare(gate);
        gate.OnFrame(Frame(), 0);
        return gate;
    }

    // A frame that passes every check against Compare()'s defaults: every display live, 16 empty FMA slots, both flight
    // directors on, altitude 1000, heading 90, and the FOB line of a real push. A test moves what it needs to.
    private static FenixChannelFrame Frame(bool fdCapt = true, bool fdFo = true, double altitude = 1000,
        double heading = 90, string fob = "wFOB:   g2940 cs KG") =>
        new(Malformed: false, WrongShapeFields: Array.Empty<string>(),
            Fd: FenixFdState.Default with { FdOnCapt = fdCapt, FdOnFo = fdFo },
            Fma: Enumerable.Repeat(new FenixFmaSlot("", false), 16).ToArray(), UpperEcamLeft: Array.Empty<string>(),
            UpperEcamRight: Array.Empty<string>(), FwcPage: 0, FwcPhase: 0,
            Altitude: altitude, MagHeading: heading, FobText: fob)
        {
            PfdLive = true,
            EwdLive = true,
            SdLive = true,
            NdLive = true,
            IsisPowered = true,
            DcduLive = true
        };

    // Feeds the same frame every 100 ms from one time to the other, both included: the channel alone, with no
    // heartbeat, as it comes while the UI thread is blocked.
    private static void Feed(FenixChannelTrust gate, long fromMs, long toMs, FenixChannelFrame frame)
    {
        for (long t = fromMs; t <= toMs; t += 100)
        {
            gate.OnFrame(frame, t);
        }
    }

    // The same with the UI thread running: the host's heartbeat just before each frame.
    private static void Run(FenixChannelTrust gate, long fromMs, long toMs, FenixChannelFrame frame)
    {
        for (long t = fromMs; t <= toMs; t += 100)
        {
            gate.OnUiHeartbeat(t);
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

    // Every display's verdict, with the display and the time in the failure: a loop over many messages says where it broke.
    private static void AssertAll(FenixChannelTrust gate, long nowMs, FenixDisplayVerdict expected)
    {
        foreach (FenixChannelDisplay display in AllDisplays)
        {
            FenixDisplayVerdict actual = gate.VerdictFor(display, nowMs);

            Assert.True(expected == actual, $"{display} at t={nowMs} ms: expected {expected}, got {actual}");
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

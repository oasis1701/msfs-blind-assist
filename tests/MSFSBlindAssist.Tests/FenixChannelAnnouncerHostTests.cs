// Characterization tests for the announcer host of the Fenix display channel (Services/FenixChannelAnnouncers.cs): the
// WinForms timer that ticks every 100 ms on the UI thread, stamps the stall guard's heartbeat and then runs one optional
// sampler for each announcer. The host is tested on its own with fake delegates: no monitor, no announcer, no sim. Tick is
// what the timer calls, so a test calls it itself and no test pumps a message loop; the one timer a test starts never
// fires without one. The definition never starts a host here, because FenixTestHost keeps every background seam off
// (BackgroundSeamsEnabled), so what Start and Stop of the definition wire together is checked in the sim ([CORE-5]).
//
// The contract: each tick calls the heartbeat first and then every sampler in the order given, with one reading of the
// clock for the whole tick that every sampler is handed. A sampler, or the heartbeat, that throws is logged once for each
// distinct "<exception type>: <message>" (32 are remembered, then forgotten, as the monitor's own problems are) and costs
// nothing else: the other samplers of the tick still run, the timer stays on and the next tick runs them all again; a log
// that throws is swallowed as well. Start makes the one timer, at 100 ms, and a second Start does nothing. Dispose stops and
// disposes it, never throws, and a second Dispose does nothing; after it a tick does nothing at all (no beat, no sampler,
// no clock reading) and Start makes no timer.
//
// The host formats no number: a log line is the exception's own type and message, so it reads the same where the PC's
// culture writes a comma.

using System.Reflection;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelAnnouncerHostTests
{
    // A host on fakes. Whatever a test does not give is a delegate that does nothing, a clock at 0 and no samplers.
    private static FenixChannelAnnouncers NewHost(
        Action? heartbeat = null,
        IReadOnlyList<Action<long>>? samplers = null,
        Action<string>? logProblem = null,
        Func<long>? clock = null)
        => new(heartbeat ?? (() => { }), samplers ?? [], logProblem ?? (_ => { }), clock ?? (() => 0));

    private static System.Windows.Forms.Timer TimerOf(FenixChannelAnnouncers host)
        => (System.Windows.Forms.Timer)typeof(FenixChannelAnnouncers)
            .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    // ---- The tick -----------------------------------------------------------------------------------

    [Fact]
    public void The_host_beats_then_samples_in_order()
    {
        var calls = new List<(string Who, long Now)>();
        int clockReads = 0;
        long clockNow = 5_000;
        using FenixChannelAnnouncers host = NewHost(
            heartbeat: () => calls.Add(("beat", 0)),
            samplers:
            [
                now => calls.Add(("first", now)),
                now => calls.Add(("second", now)),
                now => calls.Add(("third", now)),
            ],
            clock: () =>
            {
                clockReads++;
                return clockNow += 100;
            });

        host.Tick();
        host.Tick();

        // The beat first, then each sampler in the order given. The clock is read once for a tick and every sampler of
        // that tick gets the same reading, so the samplers agree on when the tick was.
        Assert.Equal(
            new (string, long)[]
            {
                ("beat", 0), ("first", 5_100), ("second", 5_100), ("third", 5_100),
                ("beat", 0), ("first", 5_200), ("second", 5_200), ("third", 5_200),
            },
            calls);
        Assert.Equal(2, clockReads);
    }

    [Fact]
    public void A_throwing_sampler_does_not_stop_the_others_or_the_timer()
    {
        var ran = new List<string>();
        using FenixChannelAnnouncers host = NewHost(
            heartbeat: () => ran.Add("beat"),
            samplers:
            [
                _ =>
                {
                    ran.Add("first");
                    throw new InvalidOperationException("the first fails");
                },
                _ => ran.Add("second"),
                _ =>
                {
                    ran.Add("third");
                    throw new NullReferenceException();
                },
                _ => ran.Add("fourth"),
            ]);
        host.Start();
        Assert.True(host.IsRunning);
        string[] everyone = ["beat", "first", "second", "third", "fourth"];

        // The throw of the first costs nothing: the ones after it run, the one after the second throw too, and the
        // timer is still on, so the next tick runs them all again.
        Exception? escaped = Record.Exception(host.Tick);
        Assert.Null(escaped);
        Assert.Equal(everyone, ran);
        Assert.True(host.IsRunning);

        ran.Clear();
        escaped = Record.Exception(host.Tick);
        Assert.Null(escaped);
        Assert.Equal(everyone, ran);
        Assert.True(host.IsRunning);
    }

    [Fact]
    public void A_throwing_heartbeat_does_not_stop_the_samplers()
    {
        // The heartbeat has its own isolation: it is called first, and a throw there must not take the samplers' turn
        // with it. (The monitor's own heartbeat never throws; this is the host not relying on that.)
        var ran = new List<string>();
        var logged = new List<string>();
        using FenixChannelAnnouncers host = NewHost(
            heartbeat: () => throw new ObjectDisposedException("monitor"),
            samplers: [_ => ran.Add("first"), _ => ran.Add("second")],
            logProblem: logged.Add);
        host.Start();

        Exception? escaped = Record.Exception(host.Tick);

        Assert.Null(escaped);
        Assert.Equal(new[] { "first", "second" }, ran);
        Assert.True(host.IsRunning);
        Assert.Single(logged);
        Assert.StartsWith("ObjectDisposedException: ", logged[0], StringComparison.Ordinal);
    }

    // ---- The log ------------------------------------------------------------------------------------

    [Fact]
    public void A_throw_is_logged_once()
    {
        var logged = new List<string>();
        Exception? secondThrows = null;
        using FenixChannelAnnouncers host = NewHost(
            samplers:
            [
                _ => throw new InvalidOperationException("no speed limits"),
                _ =>
                {
                    if (secondThrows is not null) throw secondThrows;
                },
            ],
            logProblem: logged.Add);

        // The same throw at every tick is one line, however long it goes on.
        host.Tick();
        host.Tick();
        host.Tick();
        Assert.Equal(new[] { "InvalidOperationException: no speed limits" }, logged);

        // The line is the whole of what is remembered: the same type and message from another sampler is the same
        // line, and the same type with another message is a new one.
        secondThrows = new InvalidOperationException("no speed limits");
        host.Tick();
        Assert.Single(logged);

        secondThrows = new InvalidOperationException("no vertical profile");
        host.Tick();
        host.Tick();
        Assert.Equal(
            new[]
            {
                "InvalidOperationException: no speed limits",
                "InvalidOperationException: no vertical profile",
            },
            logged);

        // Another exception type with a message already heard is a new line too.
        secondThrows = new ArgumentException("no speed limits");
        host.Tick();
        Assert.Equal("ArgumentException: no speed limits", logged[^1]);
        Assert.Equal(3, logged.Count);
    }

    [Fact]
    public void A_log_that_throws_costs_nothing()
    {
        // The log is the monitor's, which never throws; this is the host not relying on that. The fault is swallowed,
        // the samplers after the one that threw still run, and the timer stays on.
        var ran = new List<string>();
        int logCalls = 0;
        using FenixChannelAnnouncers host = NewHost(
            samplers:
            [
                _ => throw new InvalidOperationException("the first fails"),
                _ => ran.Add("second"),
            ],
            logProblem: _ =>
            {
                logCalls++;
                throw new IOException("the log is gone");
            });
        host.Start();

        Exception? escaped = Record.Exception(host.Tick);

        Assert.Null(escaped);
        Assert.Equal(new[] { "second" }, ran);
        Assert.Equal(1, logCalls);
        Assert.True(host.IsRunning);
    }

    [Fact]
    public void The_log_remembers_32_distinct_lines_and_then_forgets_them()
    {
        // The memory is bounded as the monitor's is: 32 distinct lines are remembered, and the line that would be the
        // 33rd empties the memory first. So a fault whose message changes at every tick cannot grow it without end.
        var logged = new List<string>();
        int fault = 0;
        using FenixChannelAnnouncers host = NewHost(
            samplers: [_ => throw new InvalidOperationException($"fault {fault}")],
            logProblem: logged.Add);

        for (fault = 0; fault < 32; fault++) host.Tick();
        Assert.Equal(32, logged.Count);

        // All 32 are remembered: hearing them again says nothing.
        for (fault = 0; fault < 32; fault++) host.Tick();
        Assert.Equal(32, logged.Count);

        // The 33rd distinct line is logged and empties the memory first; it alone is remembered after.
        fault = 32;
        host.Tick();
        Assert.Equal(33, logged.Count);
        host.Tick();
        Assert.Equal(33, logged.Count);

        // The first line is forgotten, so it is logged again.
        fault = 0;
        host.Tick();
        Assert.Equal(34, logged.Count);
        Assert.Equal("InvalidOperationException: fault 0", logged[^1]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_log_line_reads_the_same_under_a_comma_decimal_culture(string culture)
    {
        // The line is the exception's own type and message, formatted by nothing here, so a number inside the message
        // keeps its decimal point where the PC's culture writes a comma.
        string[] logged = TestCulture.Run(culture, () =>
        {
            var lines = new List<string>();
            using FenixChannelAnnouncers host = NewHost(
                samplers: [_ => throw new InvalidOperationException("speed 250.5 is outside 0.0-1.0")],
                logProblem: lines.Add);

            host.Tick();
            host.Tick();
            return lines.ToArray();
        });

        Assert.Equal(new[] { "InvalidOperationException: speed 250.5 is outside 0.0-1.0" }, logged);
    }

    // ---- Start and Dispose --------------------------------------------------------------------------

    [Fact]
    public void Start_makes_the_one_timer_at_100_ms_and_Dispose_ends_it()
    {
        using FenixChannelAnnouncers host = NewHost();   // disposed again at the end: a second Dispose is harmless
        Assert.False(host.IsRunning);   // nothing runs before Start

        host.Start();

        Assert.True(host.IsRunning);
        System.Windows.Forms.Timer timer = TimerOf(host);
        Assert.Equal(100, timer.Interval);
        bool timerDisposed = false;
        timer.Disposed += (_, _) => timerDisposed = true;

        // A second Start does nothing: it is still the one timer, not a second one beside it.
        host.Start();
        Assert.Same(timer, TimerOf(host));
        Assert.True(host.IsRunning);

        host.Dispose();

        Assert.False(host.IsRunning);
        Assert.False(timer.Enabled);
        Assert.True(timerDisposed);
    }

    [Fact]
    public void After_Dispose_a_tick_does_nothing_and_Start_makes_no_timer()
    {
        var ran = new List<string>();
        int clockReads = 0;
        using FenixChannelAnnouncers host = NewHost(   // disposed again at the end: a second Dispose is harmless
            heartbeat: () => ran.Add("beat"),
            samplers: [_ => ran.Add("sampler")],
            clock: () =>
            {
                clockReads++;
                return 0;
            });
        host.Start();
        host.Tick();
        Assert.Equal(new[] { "beat", "sampler" }, ran);
        Assert.Equal(1, clockReads);

        host.Dispose();
        ran.Clear();
        clockReads = 0;

        // The timer's last tick can still be queued when the host ends: it finds the host ended and does nothing.
        Exception? escaped = Record.Exception(host.Tick);
        Assert.Null(escaped);
        Assert.Empty(ran);
        Assert.Equal(0, clockReads);

        // Neither a Start nor a second Dispose brings anything back or throws.
        escaped = Record.Exception(host.Start);
        Assert.Null(escaped);
        Assert.False(host.IsRunning);
        escaped = Record.Exception(host.Dispose);
        Assert.Null(escaped);
        Assert.False(host.IsRunning);
    }

    [Fact]
    public void A_host_that_was_never_started_disposes_cleanly()
    {
        FenixChannelAnnouncers host = NewHost();

        Exception? escaped = Record.Exception(host.Dispose);

        Assert.Null(escaped);
        Assert.False(host.IsRunning);
    }
}

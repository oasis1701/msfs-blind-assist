// Characterization tests for the announcer host of the Fenix display channel (Services/FenixChannelAnnouncers.cs): the
// WinForms timer that ticks every 100 ms on the UI thread and runs one optional
// sampler for each announcer. The host is tested on its own with fake delegates: no monitor, no announcer, no sim. Tick is
// what the timer calls, so a test calls it itself and no test pumps a message loop; the one timer a test starts never
// fires without one. The definition never starts a host here, because FenixTestHost keeps every background seam off
// (BackgroundSeamsEnabled), so what Start and Stop of the definition wire together is checked in the sim ([CORE-5]).
//
// The contract: each tick calls every sampler in the order given, with one reading of the
// clock for the whole tick that every sampler is handed. A sampler that throws is logged once for each
// distinct "<exception type>: <message> (at <type>.<method>)", the method being the first of the app's own on its stack
// trace, never a helper of the framework's, so that the log says which of several samplers it was (32 lines are
// remembered, then forgotten, as the monitor's own problems are), and costs nothing else: the other samplers of the tick
// still run, the timer stays on and the next tick runs them all again; a log that throws is swallowed as well. Start makes
// the one timer, at 100 ms, and a second Start does nothing. Dispose stops and disposes it, never throws, and a second
// Dispose does nothing; after it a tick does nothing at all (no sampler, no clock reading) and Start makes no
// timer.
//
// The host formats no number: a log line is the exception's own type and message and the names of the app's method it
// came from, so it reads the same where the PC's culture writes a comma.

using System.Diagnostics;
using System.Reflection;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelAnnouncerHostTests
{
    // A host on fakes. Whatever a test does not give is a delegate that does nothing, a clock at 0 and no samplers.
    private static FenixChannelAnnouncers NewHost(
        IReadOnlyList<Action<long>>? samplers = null,
        Action<string>? logProblem = null,
        Func<long>? clock = null)
        => new(samplers ?? [], logProblem ?? (_ => { }), clock ?? (() => 0));

    private static System.Windows.Forms.Timer TimerOf(FenixChannelAnnouncers host)
        => (System.Windows.Forms.Timer)typeof(FenixChannelAnnouncers)
            .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;

    // Throw sites with names the log lines below can write out: a lambda's is the compiler's.
    private static void NoSpeedLimits(long now) => throw new InvalidOperationException("no speed limits");

    private static void NoSpeedLimitsEither(long now) => throw new InvalidOperationException("no speed limits");

    private static void Throw(Exception exception) => throw exception;

    // An index past the end, which the framework throws from a helper of its own, below the method named here.
    private static void IndexesAList(long now) => _ = new List<int>()[3];

    private static void IndexesAnotherList(long now) => _ = new List<int>()[3];

    private static void IndexesAnArray(long now)
    {
        int[] one = new int[1];
        _ = one[3];
    }

    // ---- The tick -----------------------------------------------------------------------------------

    [Fact]
    public void The_host_samples_in_order()
    {
        var calls = new List<(string Who, long Now)>();
        int clockReads = 0;
        long clockNow = 5_000;
        using FenixChannelAnnouncers host = NewHost(
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

        // Each sampler in the order given. The clock is read once for a tick and every sampler of that tick gets the
        // same reading, so the samplers agree on when the tick was.
        Assert.Equal(
            new (string, long)[]
            {
                ("first", 5_100), ("second", 5_100), ("third", 5_100),
                ("first", 5_200), ("second", 5_200), ("third", 5_200),
            },
            calls);
        Assert.Equal(2, clockReads);
    }

    [Fact]
    public void A_throwing_sampler_does_not_stop_the_others_or_the_timer()
    {
        var ran = new List<string>();
        using FenixChannelAnnouncers host = NewHost(
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
        string[] everyone = ["first", "second", "third", "fourth"];

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

    // ---- The log ------------------------------------------------------------------------------------

    [Fact]
    public void A_throw_is_logged_once()
    {
        var logged = new List<string>();
        Exception? secondThrows = null;
        using FenixChannelAnnouncers host = NewHost(
            samplers:
            [
                NoSpeedLimits,
                _ =>
                {
                    if (secondThrows is not null) Throw(secondThrows);
                },
            ],
            logProblem: logged.Add);

        // The same throw at every tick is one line, however long it goes on.
        host.Tick();
        host.Tick();
        host.Tick();
        Assert.Equal(
            new[] { "InvalidOperationException: no speed limits (at FenixChannelAnnouncerHostTests.NoSpeedLimits)" },
            logged);

        // The line is the whole of what is remembered: the same type and message from another site is a new line, and
        // so is the same type with another message.
        secondThrows = new InvalidOperationException("no speed limits");
        host.Tick();
        host.Tick();
        secondThrows = new InvalidOperationException("no vertical profile");
        host.Tick();
        host.Tick();
        Assert.Equal(
            new[]
            {
                "InvalidOperationException: no speed limits (at FenixChannelAnnouncerHostTests.NoSpeedLimits)",
                "InvalidOperationException: no speed limits (at FenixChannelAnnouncerHostTests.Throw)",
                "InvalidOperationException: no vertical profile (at FenixChannelAnnouncerHostTests.Throw)",
            },
            logged);

        // Another exception type with a message already heard is a new line too.
        secondThrows = new ArgumentException("no speed limits");
        host.Tick();
        Assert.Equal("ArgumentException: no speed limits (at FenixChannelAnnouncerHostTests.Throw)", logged[^1]);
        Assert.Equal(4, logged.Count);
    }

    [Fact]
    public void The_same_exception_from_two_sites_is_two_lines()
    {
        // Plans 08, 09 and 11 each add a sampler, and the host's constructor knows them only as delegates: the site is how
        // the log tells apart two that throw the same exception. Two that throw it from one site are one line.
        var logged = new List<string>();
        using FenixChannelAnnouncers host = NewHost(
            samplers: [NoSpeedLimits, NoSpeedLimitsEither, NoSpeedLimits],
            logProblem: logged.Add);

        host.Tick();
        host.Tick();

        Assert.Equal(
            new[]
            {
                "InvalidOperationException: no speed limits (at FenixChannelAnnouncerHostTests.NoSpeedLimits)",
                "InvalidOperationException: no speed limits (at FenixChannelAnnouncerHostTests.NoSpeedLimitsEither)",
            },
            logged);
    }

    [Fact]
    public void A_throw_inside_the_framework_names_the_apps_own_frame()
    {
        // An index past the end of a list or an array is thrown from a helper of the framework's, below the sampler
        // (ThrowHelper.ThrowArgumentOutOfRange_IndexMustBeLessException and ThrowHelpers.ThrowIndexOutOfRangeException,
        // measured on .NET 10). A line naming that tells nobody which sampler threw, so it names the first frame of the
        // app's own, and two samplers that fault the same way are two lines. The tests' namespace is inside the app's.
        var logged = new List<string>();
        using FenixChannelAnnouncers host = NewHost(
            samplers: [IndexesAList, IndexesAnotherList, IndexesAnArray],
            logProblem: logged.Add);

        host.Tick();
        host.Tick();

        Assert.Equal(3, logged.Count);
        Assert.StartsWith("ArgumentOutOfRangeException: ", logged[0], StringComparison.Ordinal);
        Assert.EndsWith(" (at FenixChannelAnnouncerHostTests.IndexesAList)", logged[0], StringComparison.Ordinal);
        Assert.StartsWith("ArgumentOutOfRangeException: ", logged[1], StringComparison.Ordinal);
        Assert.EndsWith(" (at FenixChannelAnnouncerHostTests.IndexesAnotherList)", logged[1], StringComparison.Ordinal);
        Assert.StartsWith("IndexOutOfRangeException: ", logged[2], StringComparison.Ordinal);
        Assert.EndsWith(" (at FenixChannelAnnouncerHostTests.IndexesAnArray)", logged[2], StringComparison.Ordinal);
    }

    [Fact]
    public void A_throw_in_a_lambda_names_the_lambda()
    {
        // The definition's samplers are lambdas. A lambda's type is nested, and it reads the namespace of the type around
        // it, so it counts as the app's own: its name is the compiler's, with the method it was written in inside it.
        var logged = new List<string>();
        using FenixChannelAnnouncers host = NewHost(
            samplers: [_ => throw new InvalidOperationException("no speed limits")],
            logProblem: logged.Add);

        host.Tick();

        string line = Assert.Single(logged);
        Assert.StartsWith("InvalidOperationException: no speed limits (at ", line, StringComparison.Ordinal);
        Assert.Contains(nameof(A_throw_in_a_lambda_names_the_lambda), line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exception_with_no_throw_site_is_the_line_as_it_is()
    {
        // Never thrown, so it has no stack trace to name a method from: the type and the message alone.
        Assert.Equal(
            "InvalidOperationException: no speed limits",
            FenixChannelAnnouncers.ProblemLine(new InvalidOperationException("no speed limits")));
    }

    [Fact]
    public async Task An_exception_with_no_frame_of_the_apps_is_the_line_as_it_is()
    {
        // Thrown and caught inside the framework: the task's delegate is the framework's own method, and the task keeps
        // what it threw. So it has a stack trace, with no frame of the app's in it, and a framework's helper is never
        // named in the app's place. (WhenAny, because an await of the task itself would add the test's frames.)
        Func<object?, int> parse = Convert.ToInt32;
        Task<int> task = Task.Factory.StartNew(parse, "not a number");
        await Task.WhenAny(task);
        Exception thrown = task.Exception!.InnerException!;
        Assert.True(new StackTrace(thrown).FrameCount > 0, "the exception has no stack trace");

        Assert.Equal($"FormatException: {thrown.Message}", FenixChannelAnnouncers.ProblemLine(thrown));
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
            samplers: [_ => Throw(new InvalidOperationException($"fault {fault}"))],
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
        Assert.Equal("InvalidOperationException: fault 0 (at FenixChannelAnnouncerHostTests.Throw)", logged[^1]);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_log_line_reads_the_same_under_a_comma_decimal_culture(string culture)
    {
        // The line is the exception's own type and message and the names of the method that threw it, formatted by
        // nothing here, so a number inside the message keeps its decimal point where the PC's culture writes a comma.
        string[] logged = TestCulture.Run(culture, () =>
        {
            var lines = new List<string>();
            using FenixChannelAnnouncers host = NewHost(
                samplers: [_ => Throw(new InvalidOperationException("speed 250.5 is outside 0.0-1.0"))],
                logProblem: lines.Add);

            host.Tick();
            host.Tick();
            return lines.ToArray();
        });

        Assert.Equal(
            new[] { "InvalidOperationException: speed 250.5 is outside 0.0-1.0 (at FenixChannelAnnouncerHostTests.Throw)" },
            logged);
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
            samplers: [_ => ran.Add("sampler")],
            clock: () =>
            {
                clockReads++;
                return 0;
            });
        host.Start();
        host.Tick();
        Assert.Equal(new[] { "sampler" }, ran);
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

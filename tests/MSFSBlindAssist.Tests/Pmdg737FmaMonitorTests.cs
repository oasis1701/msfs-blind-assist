// The FMA monitor: the state machine that waits for the PMDG SDK, analyses PMDG's file, locates and
// verifies its memory against the SDK, and speaks each FMA change after the settle. Driven by a fake
// environment with a settable clock (100 ms per Tick()) and fake samplers: no sim and no timer.

using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public sealed class Pmdg737FmaMonitorTests : IDisposable
{
    private static readonly Pmdg737FmaLayout Layout = GoldenLayouts.Fma;

    // A/T: MCP SPD, Roll: HDG SEL, Pitch: ALT HOLD, AFDS: CMD.
    private static readonly FmaState Base = new(3, 3, 0, 6, 0, 3);
    private const string BaseReadout = "A/T: MCP SPD. Roll: HDG SEL. Pitch: ALT HOLD. AFDS: CMD.";
    private static readonly FmaSdkSnapshot Sdk = new(true, true, 10000, 90);

    private const string StoppedMismatch = "FMA announcements stopped: values do not match the SDK";
    private const string StoppedNotFound = "FMA announcements stopped: aircraft memory not found";
    private const string UnavailableNotFound = "FMA announcements unavailable: aircraft memory not found";
    private const string UnavailableMismatch = "FMA announcements unavailable: values do not match the SDK";
    private const string Resumed = "FMA announcements resumed";
    private const string InternalErrorLine = "FMA announcements unavailable: internal error, see the log";

    // Output P while stopped on a mismatch: never the last reading presented as current.
    private const string StoppedReadout = StoppedMismatch + ". Last known: " + BaseReadout;

    private static FmaSample Sample(FmaState fma, int alt = 10000) => new(fma, true, true, alt, 90, true);

    private static Task<FmaLocateResult> Found(params IFmaSampler[] candidates) =>
        Task.FromResult(new FmaLocateResult(candidates, null));

    private static Task<FmaLocateResult> NotFound() =>
        Task.FromResult(new FmaLocateResult(Array.Empty<IFmaSampler>(), null));

    private static Task<FmaAnalysis> NotRecognised() =>
        Task.FromResult(new FmaAnalysis(Array.Empty<Pmdg737FmaLayout>(), FmaReasons.FileNotRecognised));

    // Every sampler any test created: each must be disposed at most once (the real ones share one
    // ref-counted process handle, so a second Dispose would close it under another sampler).
    private readonly List<FakeFmaEnvironment> _envs = new();

    public void Dispose()
    {
        foreach (var env in _envs)
            foreach (var s in env.Samplers)
                Assert.True(s.DisposeCount <= 1, $"a sampler was disposed {s.DisposeCount} times");
    }

    private FakeFmaEnvironment NewEnv()
    {
        var env = new FakeFmaEnvironment();
        _envs.Add(env);
        return env;
    }

    private Harness New() => new(NewEnv());

    [Fact]
    public void Waits_for_sdk_before_analysing()
    {
        var h = New();
        h.Env.Sdk = null;
        h.Start();
        h.Tick(5);
        Assert.Equal(0, h.Env.AnalyseCalls);
        Assert.Equal("WaitingForSdk", h.State);

        h.Env.Sdk = Sdk;
        h.Tick();
        Assert.Equal(1, h.Env.AnalyseCalls);
        Assert.Equal("Analysing", h.State);
    }

    [Fact]
    public void Analysis_failure_speaks_the_display_files_line_once()
    {
        // No layout: the four display readers' one line (WASM-29), never the FMA's own "unavailable".
        var h = New();
        h.Env.Analyse = NotRecognised;
        h.Start();
        h.Tick(50);
        Assert.Equal(new[] { Pmdg737DisplaySpeech.FilesNotLoaded }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.Equal(0, h.Env.LocateCalls);
        Assert.Contains("Analysing -> Unavailable: PMDG file not recognised", h.Env.Logged);
    }

    [Fact]
    public void A_layout_missing_from_a_loaded_file_says_its_own_line()
    {
        // A layout file loaded, but the FMA's layout in it is a failure: the FMA's own line, as before, and the four
        // readers' shared line is left unused (WASM-29).
        var h = New();
        h.Env.Analyse = () => Task.FromResult(
            new FmaAnalysis(Array.Empty<Pmdg737FmaLayout>(), FmaReasons.FileNotRecognised) { LayoutFileLoaded = true });
        h.Start();
        h.Tick(5);
        Assert.Equal(new[] { "FMA announcements unavailable: PMDG file not recognised" }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        var later = new List<string>();
        h.Env.FilesNotice.Say(muted: false, later.Add);
        Assert.Equal(new[] { Pmdg737DisplaySpeech.FilesNotLoaded }, later); // not used up
    }

    [Fact]
    public void Not_found_retries_every_5s_then_unavailable_at_120s_then_every_30s()
    {
        var h = New();
        h.Env.Locate = NotFound;
        h.Start();
        h.Tick(1200); // to 119.9 s
        Assert.Equal(24, h.Env.LocateCalls); // 0 to 115 s after the first attempt
        var times = h.Env.LocateTimes.ToArray();
        Assert.All(times.Zip(times.Skip(1)), p => Assert.Equal(5000, p.Second - p.First));
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("Locating", h.State);

        h.Tick(10);
        Assert.Equal(new[] { UnavailableNotFound }, h.Env.Spoken);
        Assert.Equal(UnavailableNotFound, h.Monitor.DescribeForReadout());

        // Not final: a long load can outlast the window, so the search goes on, slowly and silently.
        h.Tick(3000); // to about 7 minutes
        times = h.Env.LocateTimes.ToArray();
        Assert.True(times.Length > 30, $"{times.Length} attempts");
        Assert.All(times.Skip(23).Zip(times.Skip(24)), p => Assert.Equal(30_000, p.Second - p.First));
        Assert.Single(h.Env.Spoken);
        Assert.Equal("Locating", h.State);
        Assert.Equal(UnavailableNotFound, h.Monitor.DescribeForReadout());
    }

    [Fact]
    public void Found_after_the_window_says_resumed()
    {
        var h = New();
        h.Env.Locate = NotFound;
        h.Start();
        h.Tick(1300);
        Assert.Equal(new[] { UnavailableNotFound }, h.Env.Spoken);

        h.Env.Locate = () => Found(h.Env.NewSampler());
        h.RunUntilRunning(310); // the next attempt is at most 30 s away
        Assert.Equal(new[] { UnavailableNotFound, Resumed }, h.Env.Spoken); // it never ran: no change to tell
        Assert.Equal(BaseReadout, h.Monitor.DescribeForReadout());
        h.Tick(20);
        Assert.Equal(2, h.Env.Spoken.Length);
    }

    [Fact]
    public void Last_candidate_disagreeing_keeps_searching_and_is_unavailable_at_120s()
    {
        var h = New();
        h.Env.Locate = () => Found(h.Env.NewSampler(Sample(Base, alt: 31000))); // never matches the SDK's 10000
        h.Start();
        h.Tick(1200); // to 119.9 s: every attempt finds it, and it disagrees for its 2 s
        Assert.Equal(24, h.Env.LocateCalls);
        Assert.Empty(h.Env.Spoken);
        Assert.NotEqual("Unavailable", h.State);

        h.Tick(10);
        Assert.Equal(new[] { UnavailableMismatch }, h.Env.Spoken);
        Assert.Equal(UnavailableMismatch, h.Monitor.DescribeForReadout());
        h.Tick(600);
        Assert.Equal(26, h.Env.LocateCalls); // then every 30 s
        Assert.Single(h.Env.Spoken);
        Assert.Equal(UnavailableMismatch, h.Monitor.DescribeForReadout());
    }

    [Fact]
    public void Last_candidate_failing_its_read_keeps_the_window()
    {
        // Searching again after a failed read stays in the same 120 s window: a new window per attempt
        // would search every 5 s forever and never say "unavailable".
        var h = New();
        h.Env.Locate = () =>
        {
            var s = h.Env.NewSampler();
            s.Fail = true;
            return Found(s);
        };
        h.Start();
        h.Tick(1200);
        Assert.Equal(24, h.Env.LocateCalls);
        Assert.Empty(h.Env.Spoken);
        h.Tick(10);
        Assert.Equal(new[] { UnavailableNotFound }, h.Env.Spoken);
    }

    [Fact]
    public void Window_line_says_stopped_when_it_had_been_running()
    {
        var h = New().Start();
        h.RunUntilRunning();
        h.Env.Sdk = null; // the simulator went away: silent
        h.Tick();
        h.Env.Locate = () => Found(h.Env.NewSampler(Sample(Base with { Roll = 4 }, alt: 31000)));
        h.Env.Sdk = Sdk; // a new 120 s window, with no 10 s stop: the reader was not running when it began
        h.Tick(1150);
        Assert.Empty(h.Env.Spoken);
        h.Tick(100); // the window ends (its last attempt's 2 s verification may run past 120 s)
        Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);
        Assert.Equal(StoppedReadout, h.Monitor.DescribeForReadout()); // the last good FMA, from before the SDK gap

        h.Env.Locate = () => Found(h.Env.NewSampler(Sample(Base with { Roll = 4 })));
        h.RunUntilRunning(310);
        Assert.Equal(new[] { StoppedMismatch, Resumed + ", Roll: VOR/LOC" }, h.Env.Spoken);
    }

    [Theory]
    [InlineData(FmaReasons.FileNotRecognised)]
    [InlineData(FmaReasons.FilesNotFound)]
    [InlineData(FmaReasons.NoAccess)]
    [InlineData(FmaReasons.UnexpectedValues)]
    [InlineData(FmaReasons.InternalError)]
    public void Terminal_reasons_stay_terminal(string reason)
    {
        var h = New();
        switch (reason)
        {
            case FmaReasons.FileNotRecognised:
            case FmaReasons.FilesNotFound:
                h.Env.Analyse = () => Task.FromResult(new FmaAnalysis(Array.Empty<Pmdg737FmaLayout>(), reason));
                break;
            case FmaReasons.NoAccess:
                h.Env.Locate = () => Task.FromResult(new FmaLocateResult(Array.Empty<IFmaSampler>(), reason));
                break;
            case FmaReasons.UnexpectedValues:
                h.Env.Locate = () =>
                {
                    var s = h.Env.NewSampler();
                    s.Script.Enqueue(Sample(Base)); // a clean baseline, then corrupt
                    s.Sample = Sample(Base with { Pitch = 999 });
                    return Found(s);
                };
                break;
            case FmaReasons.InternalError:
                h.Env.Locate = () => Task.FromException<FmaLocateResult>(new IOException("boom"));
                break;
        }
        h.Start();
        h.TickUntil(() => h.State == "Unavailable", 100);
        int locates = h.Env.LocateCalls;
        var spoken = h.Env.Spoken;
        Assert.Single(spoken);

        h.Tick(3000); // 5 minutes
        Assert.Equal("Unavailable", h.State);
        Assert.Equal(locates, h.Env.LocateCalls);
        Assert.Equal(spoken, h.Env.Spoken);
        Assert.Equal(Pmdg737FmaSpeech.Unavailable(reason), h.Monitor.DescribeForReadout());
    }

    [Fact]
    public void No_access_is_unavailable_at_once()
    {
        var h = New();
        h.Env.Locate = () => Task.FromResult(new FmaLocateResult(Array.Empty<IFmaSampler>(), FmaReasons.NoAccess));
        h.Start();
        h.Tick(3); // analyse, locate, result
        Assert.Equal(new[] { "FMA announcements unavailable: no access to the simulator" }, h.Env.Spoken);
        h.Tick(100);
        Assert.Equal(1, h.Env.LocateCalls);
        Assert.Single(h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
    }

    [Fact]
    public void Verified_start_is_silent()
    {
        var h = New().Start();
        h.RunUntilRunning();
        h.Tick(20);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("Running", h.State);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Change_spoken_after_settle_grouped(string culture)
    {
        var spoken = TestCulture.Run(culture, () =>
        {
            var h = New().Start();
            var s = h.RunUntilRunning();
            s.Sample = Sample(Base with { Roll = 4 }); // t: Roll HDG SEL -> VOR/LOC
            h.Tick();
            s.Sample = Sample(Base with { Roll = 4, Pitch = 7 }); // t + 100: Pitch ALT HOLD -> G/S
            h.Tick(2); // t + 100, t + 200
            Assert.Empty(h.Env.Spoken);
            h.Tick(); // t + 300
            Assert.Equal(new[] { "FMA: Roll: VOR/LOC, Pitch: G/S" }, h.Env.Spoken);
            h.Tick(30);
            return h.Env.Spoken;
        });
        Assert.Equal(new[] { "FMA: Roll: VOR/LOC, Pitch: G/S" }, spoken);
    }

    [Fact]
    public void Only_change_lines_get_the_fma_prefix()
    {
        // The status lines already start with "FMA announcements", and "resumed" carries its change list
        // after a comma: neither gets a second "FMA: ". Output P never gets it.
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Sdk = Sdk with { McpAltitude = 12300 };
        h.Tick(30);
        s.Sample = Sample(Base with { Roll = 4 });
        h.Env.Sdk = Sdk;
        h.Tick(30);
        s.Sample = Sample(Base with { Roll = 4, Pitch = 7 });
        h.Tick(30);
        Assert.Equal(new[] { StoppedMismatch, Resumed + ", Roll: VOR/LOC", "FMA: Pitch: G/S" }, h.Env.Spoken);
        Assert.Equal("A/T: MCP SPD. Roll: VOR/LOC. Pitch: G/S. AFDS: CMD.", h.Monitor.DescribeForReadout());
    }

    [Fact]
    public void Column_texts_only_while_running()
    {
        // The PFD window's FMA rows: the columns of the latest good reading, none while starting or stopped.
        var h = New().Start();
        Assert.Null(h.Monitor.ColumnTexts());
        var s = h.RunUntilRunning();
        s.Sample = Sample(Base with { Roll = 4, Pitch = 7 });
        h.Tick(30);
        Assert.Equal(new[] { "A/T: MCP SPD", "Roll: VOR/LOC", "Pitch: G/S", "AFDS: CMD" }, h.Monitor.ColumnTexts());
        h.Env.Sdk = Sdk with { McpAltitude = 12300 };
        h.Tick(30);
        Assert.Null(h.Monitor.ColumnTexts());
    }

    [Fact]
    public void Change_reverted_within_the_settle_is_silent()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Sample(Base with { Roll = 4 });
        h.Tick(2);
        s.Sample = Sample(Base);
        h.Tick(20);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Muted_changes_silent_and_baseline_advances()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.IsMuted = true;
        s.Sample = Sample(Base with { Roll = 4 });
        h.Tick(10);
        h.Env.IsMuted = false;
        h.Tick(10);
        Assert.Empty(h.Env.Spoken);

        s.Sample = Sample(Base with { Roll = 4, Pitch = 7 }); // the baseline already holds VOR/LOC
        h.Tick(10);
        Assert.Equal(new[] { "FMA: Pitch: G/S" }, h.Env.Spoken);
    }

    [Fact]
    public void Muted_status_lines_are_silent()
    {
        var u = New();
        u.Env.IsMuted = true;
        u.Env.Analyse = NotRecognised;
        u.Start();
        u.Tick(10);
        Assert.Empty(u.Env.Spoken);
        Assert.Equal("Unavailable", u.State);
        Assert.Equal("FMA announcements unavailable: PMDG file not recognised", u.Monitor.DescribeForReadout());

        var h = New().Start();
        h.RunUntilRunning();
        h.Env.IsMuted = true;
        h.Env.Sdk = Sdk with { McpAltitude = 12300 };
        h.Tick(30);
        Assert.Equal("Mismatched", h.State);
        h.Env.Sdk = Sdk;
        h.Tick();
        Assert.Equal("Running", h.State);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Mismatch_over_2s_stops_once_and_resumes_with_what_changed()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Sdk = Sdk with { McpAltitude = 12300 }; // a value memory never held
        h.Tick(19);
        Assert.Empty(h.Env.Spoken); // inside the 2 s tolerance
        h.Tick(2);
        Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);
        Assert.Equal("Mismatched", h.State);

        // A localizer and glideslope capture while stopped: not spoken yet (it is not a good reading)...
        s.Sample = Sample(Base with { Roll = 4, Pitch = 7 });
        h.Tick(30);
        Assert.Single(h.Env.Spoken);
        Assert.Equal(StoppedReadout, h.Monitor.DescribeForReadout()); // Output P: stopped, and the last good state

        // ...but never swallowed: the first good reading tells the pilot what they missed.
        h.Env.Sdk = Sdk;
        h.Tick();
        Assert.Equal("Running", h.State);
        h.Tick(30);
        Assert.Equal(new[] { StoppedMismatch, Resumed + ", Roll: VOR/LOC, Pitch: G/S" }, h.Env.Spoken);
        Assert.Equal("A/T: MCP SPD. Roll: VOR/LOC. Pitch: G/S. AFDS: CMD.", h.Monitor.DescribeForReadout());
    }

    [Fact]
    public void Change_during_a_quick_silent_relocate_is_spoken_alone()
    {
        var h = New().Start();
        var first = h.RunUntilRunning();
        h.Tick(60); // past the 5 s between attempts
        var pending = new TaskCompletionSource<FmaLocateResult>();
        h.Env.Locate = () => pending.Task;
        first.Fail = true;
        h.Tick(30); // a 3 s search
        Assert.Equal("Locating", h.State);
        Assert.Empty(h.Env.Spoken);

        pending.SetResult(new FmaLocateResult(new IFmaSampler[] { h.Env.NewSampler(Sample(Base with { Roll = 4, Pitch = 7 })) }, null));
        h.RunUntilRunning(10);
        h.Tick(20);
        Assert.Equal(new[] { "FMA: Roll: VOR/LOC, Pitch: G/S" }, h.Env.Spoken);
    }

    [Fact]
    public void Relocate_over_10s_says_stopped_then_resumed_with_what_changed()
    {
        var h = New().Start();
        var first = h.RunUntilRunning();
        h.Tick(60);
        h.Env.Locate = NotFound;
        first.Fail = true;
        h.Tick(100); // lost, then 9.9 s of searching
        Assert.Empty(h.Env.Spoken);
        h.Tick();
        Assert.Equal(new[] { StoppedNotFound }, h.Env.Spoken);
        h.Tick(100);
        Assert.Single(h.Env.Spoken);

        h.Env.Locate = () => Found(h.Env.NewSampler(Sample(Base with { Roll = 4 })));
        h.RunUntilRunning(100);
        h.Tick(20);
        Assert.Equal(new[] { StoppedNotFound, Resumed + ", Roll: VOR/LOC" }, h.Env.Spoken);
    }

    [Fact]
    public void First_running_after_restart_is_silent_even_when_the_fma_changed()
    {
        var h = New().Start();
        h.RunUntilRunning();
        h.Env.Locate = () => Found(h.Env.NewSampler(Sample(Base with { Roll = 4 })));
        h.Monitor.Restart(); // a new flight: its first FMA is a new baseline, not a change
        h.RunUntilRunning(10);
        h.Tick(20);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("A/T: MCP SPD. Roll: VOR/LOC. Pitch: ALT HOLD. AFDS: CMD.", h.Monitor.DescribeForReadout());
    }

    // A stop line the pilot heard survives a flight reload (Restart): the new flight's first reading is
    // still a baseline, so no change list, but it says "resumed" so the "stopped" is never left unanswered.

    [Theory]
    [InlineData("lost")]
    [InlineData("mismatch")]
    [InlineData("corrupt")]
    [InlineData("never found")]
    public void Stop_heard_before_a_restart_is_answered_by_resumed_alone(string how)
    {
        var h = New().Start();
        string heard;
        switch (how)
        {
            case "lost": // the memory lost in flight: "stopped" at 10 s
            {
                var first = h.RunUntilRunning();
                h.Tick(60);
                h.Env.Locate = NotFound;
                first.Fail = true;
                h.Tick(110);
                heard = StoppedNotFound;
                break;
            }
            case "mismatch":
                h.RunUntilRunning();
                h.Env.Sdk = Sdk with { McpAltitude = 12300 };
                h.Tick(30);
                h.Env.Sdk = Sdk;
                heard = StoppedMismatch;
                break;
            case "corrupt": // ten corrupt readings: the one stop that waits for a new flight
            {
                var s = h.RunUntilRunning();
                s.Sample = Sample(Base with { Pitch = 999 });
                h.Tick(10);
                heard = "FMA announcements stopped: unexpected values";
                break;
            }
            default: // the search window ran out before the reader ever ran
                h.Env.Locate = NotFound;
                h.Tick(1300);
                heard = UnavailableNotFound;
                break;
        }
        Assert.Equal(new[] { heard }, h.Env.Spoken);

        h.Env.Locate = () => Found(h.Env.NewSampler(Sample(Base with { Roll = 4 })));
        h.Monitor.Restart(); // a flight reload
        Assert.Equal("FMA reader waiting for PMDG SDK data", h.Monitor.DescribeForReadout()); // a new search, as before
        h.RunUntilRunning(10);
        h.Tick(30);
        // Exactly one "resumed", with no change list: the new flight's FMA is a baseline, not a change.
        Assert.Equal(new[] { heard, Resumed }, h.Env.Spoken);
        Assert.Equal("A/T: MCP SPD. Roll: VOR/LOC. Pitch: ALT HOLD. AFDS: CMD.", h.Monitor.DescribeForReadout());

        h.Monitor.Restart(); // answered: the next reload is silent again
        h.RunUntilRunning(10);
        h.Tick(30);
        Assert.Equal(new[] { heard, Resumed }, h.Env.Spoken);
    }

    [Fact]
    public void Stop_heard_then_a_restart_whose_search_runs_out_says_its_own_line_then_resumed_once()
    {
        var h = New().Start();
        var first = h.RunUntilRunning();
        h.Tick(60);
        h.Env.Locate = NotFound;
        first.Fail = true;
        h.Tick(110);
        Assert.Equal(new[] { StoppedNotFound }, h.Env.Spoken);

        h.Monitor.Restart(); // the new flight's search runs out too: its own line, as before
        h.Tick(1300);
        Assert.Equal(new[] { StoppedNotFound, UnavailableNotFound }, h.Env.Spoken);

        h.Env.Locate = () => Found(h.Env.NewSampler());
        h.RunUntilRunning(310);
        h.Tick(30);
        Assert.Equal(new[] { StoppedNotFound, UnavailableNotFound, Resumed }, h.Env.Spoken);
    }

    [Theory]
    [InlineData("Start")]
    [InlineData("Stop")]
    public void Stop_heard_is_forgotten_by_a_new_start_or_a_stop(string how)
    {
        // Selecting the 737 again (a new environment) or leaving it starts afresh, as before: no "resumed".
        var h = New().Start();
        h.RunUntilRunning();
        h.Env.Sdk = Sdk with { McpAltitude = 12300 };
        h.Tick(30);
        Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);

        var next = NewEnv();
        if (how == "Stop")
        {
            h.Monitor.Stop();
            next = h.Env;
            next.Sdk = Sdk;
        }
        h.Monitor.Start(next, startTimer: false);
        for (int i = 0; i < 20 && h.State != "Running"; i++) { h.Monitor.Tick(); next.NowMs += 100; }
        Assert.Equal("Running", h.State);
        for (int i = 0; i < 30; i++) { h.Monitor.Tick(); next.NowMs += 100; }
        Assert.Equal(how == "Stop" ? new[] { StoppedMismatch } : Array.Empty<string>(), next.Spoken);
        if (how == "Start") Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);
    }

    [Fact]
    public void Pointers_that_disagree_never_pass_the_cross_check()
    {
        var v = New();
        v.Env.Locate = () => Found(v.Env.NewSampler(Sample(Base) with { PointersAgree = false }));
        v.Start();
        v.Tick(1210);
        Assert.Equal(new[] { UnavailableMismatch }, v.Env.Spoken);
        Assert.NotEqual("Running", v.State);

        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Sample(Base) with { PointersAgree = false };
        h.Tick(25);
        Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);
        Assert.Equal("Mismatched", h.State);
    }

    // One integrity gate (WASM-6): only a good reading (pointers agree, cross-check passes, no corrupt
    // value) may become the baseline, be read by Output P, or be spoken. A reading that fails the
    // cross-check inside the 2 s tolerance was once spoken, and a corrupt Verifying baseline produced a
    // phantom "Pitch: ALT HOLD".

    [Fact]
    public void Reading_with_disagreeing_pointers_is_never_spoken_or_read()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Sample(Base with { Roll = 4 }) with { PointersAgree = false };
        for (int i = 0; i < 30; i++)
        {
            h.Tick();
            Assert.DoesNotContain("VOR/LOC", h.Monitor.DescribeForReadout());
        }
        Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);
        Assert.Equal(StoppedReadout, h.Monitor.DescribeForReadout());
    }

    [Fact]
    public void Change_on_ticks_failing_the_cross_check_waits_for_a_good_tick()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Sdk = Sdk with { McpAltitude = 12300 }; // a value memory never held, for under 2 s
        s.Sample = Sample(Base with { Roll = 4 });
        h.Tick(15);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(BaseReadout, h.Monitor.DescribeForReadout());

        h.Env.Sdk = Sdk; // from here a good tick carries the change
        h.Tick(3);
        Assert.Empty(h.Env.Spoken); // the settle starts at the first good tick
        h.Tick();
        Assert.Equal(new[] { "FMA: Roll: VOR/LOC" }, h.Env.Spoken);
        h.Tick(20);
        Assert.Single(h.Env.Spoken);
        Assert.Equal("Running", h.State);
    }

    [Fact]
    public void Open_settle_window_waits_out_failing_ticks_for_a_good_one()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Sample(Base with { Roll = 4 }); // t: a good tick opens the window
        h.Tick();
        h.Env.Sdk = Sdk with { McpAltitude = 12300 }; // t + 100 to t + 500: the cross-check fails
        h.Tick(5);
        Assert.Empty(h.Env.Spoken); // the window's 300 ms ran out on failing ticks
        h.Env.Sdk = Sdk;
        h.Tick(); // t + 600: the first good tick speaks
        Assert.Equal(new[] { "FMA: Roll: VOR/LOC" }, h.Env.Spoken);
    }

    [Fact]
    public void Corrupt_sample_never_becomes_the_baseline()
    {
        var h = New();
        FakeSampler? s = null;
        h.Env.Locate = () =>
        {
            s = h.Env.NewSampler();
            for (int i = 0; i < 3; i++) s.Script.Enqueue(Sample(Base with { Pitch = 999 })); // agrees with the SDK
            return Found(s);
        };
        h.Start();
        h.RunUntilRunning();
        Assert.Equal(4, s!.Reads); // Running on the first clean reading, not a corrupt one
        Assert.Equal(BaseReadout, h.Monitor.DescribeForReadout());
        h.Tick(20);
        Assert.Empty(h.Env.Spoken); // no phantom "Pitch: ALT HOLD"
    }

    [Fact]
    public void Continuous_knob_turn_never_stops_reader()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        long start = h.Env.NowMs;
        int MemoryAlt(long t) => t < start ? 10000 : 10000 + (int)((t - start) / 100) * 100;
        // The SDK reports the memory value from 1 s before, updated once a second.
        h.Env.SdkAt = t => Sdk with { McpAltitude = MemoryAlt(t - t % 1000 - 1000) };
        for (int i = 0; i < 100; i++) // 10 s, +100 every tick
        {
            s.Sample = Sample(Base, alt: MemoryAlt(h.Env.NowMs));
            h.Tick();
        }
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("Running", h.State);
    }

    [Fact]
    public void Second_candidate_layout_used_when_first_fails_cross_check()
    {
        var h = New();
        var secondLayout = Layout with { StatusWords = Layout.StatusWords with { Words = new[] { "FD", "CMD (second)" } } };
        FakeSampler? c0 = null, c1 = null, c2 = null;
        h.Env.Locate = () =>
        {
            c0 = h.Env.NewSampler(Sample(Base, alt: 31000)); // never matches the SDK's 10000
            c1 = h.Env.NewSampler(layout: secondLayout);
            c2 = h.Env.NewSampler();
            return Found(c0, c1, c2);
        };
        h.Start();
        var running = h.RunUntilRunning();
        Assert.Same(c1, running);
        Assert.True(c0!.Disposed);
        Assert.True(c0.Reads >= 20); // it had its 2 s
        Assert.True(c2!.Disposed); // the others go as soon as one passes
        Assert.Equal(1, h.Env.LocateCalls);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("A/T: MCP SPD. Roll: HDG SEL. Pitch: ALT HOLD. AFDS: CMD (second).", h.Monitor.DescribeForReadout());
    }

    [Fact]
    public void Second_candidate_used_when_first_fails_to_read()
    {
        // A wrong layout usually fails the read itself (its pointer lies outside the module) rather than
        // the cross-check; a relocate would only return the same candidates, so the next one is tried.
        var h = New();
        FakeSampler? c0 = null, c1 = null;
        h.Env.Locate = () =>
        {
            c0 = h.Env.NewSampler();
            c0.Fail = true;
            c1 = h.Env.NewSampler();
            return Found(c0, c1);
        };
        h.Start();
        var running = h.RunUntilRunning();
        Assert.Same(c1, running);
        Assert.True(c0!.Disposed);
        Assert.Equal(1, h.Env.LocateCalls);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Read_failure_relocates_silently_with_new_locate()
    {
        var h = New().Start();
        var first = h.RunUntilRunning();
        h.Tick(60); // 6 s: past the 5 s between locate attempts
        first.Fail = true;
        h.Tick();
        Assert.True(first.Disposed);
        Assert.Equal(2, h.Env.LocateCalls);

        var second = h.RunUntilRunning();
        Assert.NotSame(first, second);
        h.Tick(20);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Relocate_waits_5s_after_the_last_attempt()
    {
        var h = New().Start();
        var first = h.RunUntilRunning();
        first.Fail = true;
        h.TickUntil(() => h.Env.LocateCalls == 2, 100);
        Assert.Equal(h.Env.LocateTimes[0] + 5000, h.Env.LocateTimes[1]);
        h.RunUntilRunning();
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Memory_lost_and_not_found_again_says_stopped_once()
    {
        var h = New().Start();
        var first = h.RunUntilRunning();
        h.Env.Locate = NotFound;
        first.Fail = true;
        h.Tick(1300);
        // "Stopped" at 10 s; the window running out at 120 s for the same reason says nothing more.
        Assert.Equal(new[] { StoppedNotFound }, h.Env.Spoken);
        Assert.Equal("Locating", h.State); // still searching, every 30 s
        Assert.Equal(StoppedNotFound, h.Monitor.DescribeForReadout());
    }

    [Fact]
    public void Mismatch_stop_then_memory_lost_says_stopped_again_with_the_new_reason()
    {
        var h = New().Start();
        var first = h.RunUntilRunning();
        h.Env.Sdk = Sdk with { McpAltitude = 12300 };
        h.Tick(30);
        Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);
        h.Env.Locate = NotFound;
        first.Fail = true;
        h.Tick(1250);
        Assert.Equal(new[] { StoppedMismatch, StoppedNotFound }, h.Env.Spoken);
        Assert.Equal(StoppedNotFound, h.Monitor.DescribeForReadout());
    }

    [Fact]
    public void Resumed_spoken_when_running_again_after_a_mismatch_stop_and_relocate()
    {
        var h = New().Start();
        var first = h.RunUntilRunning();
        h.Env.Sdk = Sdk with { McpAltitude = 12300 };
        h.Tick(30);
        Assert.Equal("Mismatched", h.State);
        first.Fail = true;
        h.Env.Sdk = Sdk;
        h.Tick();
        Assert.Equal("Locating", h.State);
        h.RunUntilRunning(100);
        Assert.Equal(new[] { StoppedMismatch, "FMA announcements resumed" }, h.Env.Spoken);
    }

    [Fact]
    public void Sdk_disappearing_returns_to_waiting_silently()
    {
        var h = New().Start();
        var first = h.RunUntilRunning();
        h.Env.Sdk = null;
        h.Tick();
        Assert.Equal("WaitingForSdk", h.State);
        Assert.True(first.Disposed);
        h.Tick(100);
        Assert.Equal(1, h.Env.LocateCalls);

        h.Env.Sdk = Sdk;
        h.RunUntilRunning();
        Assert.Equal(1, h.Env.AnalyseCalls); // the layouts are kept
        Assert.Equal(2, h.Env.LocateCalls);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Change_during_an_sdk_gap_is_spoken_alone()
    {
        // The SDK going away is silent and keeps the session: a change found when it returns is a
        // change, not a new baseline (only Start and Restart begin one).
        var h = New().Start();
        h.RunUntilRunning();
        h.Env.Sdk = null;
        h.Tick(20);
        Assert.Equal("WaitingForSdk", h.State);
        h.Env.Locate = () => Found(h.Env.NewSampler(Sample(Base with { Roll = 4 })));
        h.Env.Sdk = Sdk;
        h.RunUntilRunning(100);
        h.Tick(20);
        Assert.Equal(new[] { "FMA: Roll: VOR/LOC" }, h.Env.Spoken);
    }

    [Fact]
    public void Corrupt_values_for_10_ticks_stop()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Sample(Base with { Pitch = 999 }); // far beyond the pitch table
        h.Tick(9);
        Assert.Empty(h.Env.Spoken);
        h.Tick();
        Assert.Equal(new[] { "FMA announcements stopped: unexpected values" }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.True(s.Disposed);
        Assert.Contains(h.Env.Logged, l => l.Contains("Pitch = 999"));
        h.Tick(50);
        Assert.Single(h.Env.Spoken);
        Assert.Equal("FMA announcements unavailable: unexpected values", h.Monitor.DescribeForReadout());
    }

    [Fact]
    public void Corrupt_count_restarts_after_a_good_tick()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Sample(Base with { Pitch = 999 });
        h.Tick(9);
        s.Sample = Sample(Base);
        h.Tick();
        s.Sample = Sample(Base with { Pitch = 999 });
        h.Tick(9);
        s.Sample = Sample(Base);
        h.Tick(10);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("Running", h.State);
    }

    [Fact]
    public void No_word_value_logged_once_and_spoken_unrecognised()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        foreach (int roll in new[] { 2, 9, 2, 9 }) // inside the roll table's range, but no word
        {
            s.Sample = Sample(Base with { Roll = roll });
            h.Tick(10);
        }
        Assert.Equal(Enumerable.Repeat("FMA: Roll: unrecognised", 4), h.Env.Spoken);
        Assert.Equal("Running", h.State);
        Assert.Single(h.Env.Logged, l => l.Contains("Roll = 2"));
        Assert.Single(h.Env.Logged, l => l.Contains("Roll = 9"));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Readout_text_per_state(string culture) => TestCulture.Run(culture, () =>
    {
        const string Armed = "A/T: MCP SPD. Roll: HDG SEL, armed VOR/LOC. Pitch: ALT HOLD, armed G/S. AFDS: CMD.";
        var h = New();
        Assert.Equal("FMA reader starting", h.Monitor.DescribeForReadout());
        h.Env.Sdk = null;
        h.Start();
        h.Tick(3);
        Assert.Equal("WaitingForSdk", h.State);
        Assert.Equal("FMA reader waiting for PMDG SDK data", h.Monitor.DescribeForReadout());

        h.Env.Sdk = Sdk;
        h.Tick();
        Assert.Equal("Analysing", h.State);
        Assert.Equal("FMA reader starting", h.Monitor.DescribeForReadout());
        h.Tick();
        Assert.Equal("Locating", h.State);
        Assert.Equal("FMA reader starting", h.Monitor.DescribeForReadout());
        h.Tick();
        Assert.Equal("Verifying", h.State);
        Assert.Equal("FMA reader starting", h.Monitor.DescribeForReadout());

        var s = h.RunUntilRunning();
        Assert.Equal(BaseReadout, h.Monitor.DescribeForReadout());
        s.Sample = Sample(Base with { RollArmed = 4, PitchArmed = 7 });
        h.Tick();
        h.Env.IsMuted = true; // Output P is not muted with the announcements
        Assert.Equal(Armed, h.Monitor.DescribeForReadout());

        h.Env.Sdk = Sdk with { McpAltitude = 12300 };
        h.Tick(21);
        Assert.Equal("Mismatched", h.State);
        string stopped = StoppedMismatch + ". Last known: " + Armed;
        Assert.Equal(stopped, h.Monitor.DescribeForReadout());

        // Once the pilot has heard "stopped", Output P repeats it until the reader runs again: never
        // "starting" or "waiting" in between.
        s.Fail = true;
        h.Tick();
        Assert.Equal("Locating", h.State);
        Assert.Equal(stopped, h.Monitor.DescribeForReadout());
        h.Env.Sdk = null;
        h.Tick();
        Assert.Equal("WaitingForSdk", h.State);
        Assert.Equal(stopped, h.Monitor.DescribeForReadout());

        var r = New().Start();
        var first = r.RunUntilRunning();
        r.Tick(60);
        r.Env.Locate = NotFound;
        first.Fail = true;
        r.Tick(50);
        Assert.Equal("Locating", r.State);
        Assert.Equal("FMA reader starting", r.Monitor.DescribeForReadout()); // a short search: nothing said yet
        r.Tick(60);
        Assert.Equal(StoppedNotFound, r.Monitor.DescribeForReadout()); // "stopped" was said at 10 s

        var u = New();
        u.Env.Analyse = NotRecognised;
        u.Start();
        u.Tick(5);
        Assert.Equal("FMA announcements unavailable: PMDG file not recognised", u.Monitor.DescribeForReadout());
        return 0;
    });

    [Fact]
    public void Restart_keeps_layouts_and_relocates()
    {
        var h = New().Start();
        var first = h.RunUntilRunning();
        h.Monitor.Restart();
        Assert.Equal("WaitingForSdk", h.State);
        Assert.True(first.Disposed);

        var second = h.RunUntilRunning(10); // at once: a restart does not wait out the locate spacing
        Assert.NotSame(first, second);
        Assert.Equal(1, h.Env.AnalyseCalls);
        Assert.Equal(2, h.Env.LocateCalls);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Exception_in_tick_goes_unavailable_and_logs()
    {
        var h = New();
        FakeSampler? s = null;
        h.Env.Locate = () =>
        {
            s = h.Env.NewSampler();
            s.Throw = new InvalidOperationException("boom");
            return Found(s);
        };
        h.Start();
        h.Tick(10);
        Assert.Equal(new[] { InternalErrorLine }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.Contains(h.Env.Logged, l => l.Contains("boom") && l.Contains(nameof(FakeSampler.TryRead))); // with its stack
        Assert.True(s!.Disposed);
        h.Tick(20);
        Assert.Single(h.Env.Spoken);

        // The timer keeps ticking, so a restart recovers.
        h.Env.Locate = () => Found(h.Env.NewSampler());
        h.Monitor.Restart();
        h.RunUntilRunning();
        Assert.Single(h.Env.Spoken);
    }

    [Fact]
    public void Faulted_analysis_task_is_internal_error()
    {
        var h = New();
        h.Env.Analyse = () => Task.FromException<FmaAnalysis>(new InvalidOperationException("analysis blew up"));
        h.Start();
        h.Tick(10);
        Assert.Equal(new[] { InternalErrorLine }, h.Env.Spoken);
        Assert.Contains(h.Env.Logged, l => l.Contains("analysis blew up"));
        Assert.Equal("Unavailable", h.State);
        Assert.Equal(0, h.Env.LocateCalls);
    }

    [Fact]
    public void Faulted_locate_task_is_internal_error()
    {
        var h = New();
        h.Env.Locate = () => Task.FromException<FmaLocateResult>(new IOException("locate blew up"));
        h.Start();
        h.Tick(10);
        Assert.Equal(new[] { InternalErrorLine }, h.Env.Spoken);
        Assert.Contains(h.Env.Logged, l => l.Contains("locate blew up"));
        Assert.Equal("Unavailable", h.State);
    }

    [Fact]
    public void Start_twice_is_safe()
    {
        var h = New();
        h.Start();
        h.Start(); // before any tick
        var first = h.RunUntilRunning();
        h.Start(); // while running: one restart on the same environment
        Assert.Equal("WaitingForSdk", h.State);
        Assert.True(first.Disposed);
        h.RunUntilRunning(10);
        Assert.Equal(1, h.Env.AnalyseCalls);
        Assert.Equal(2, h.Env.LocateCalls);
        Assert.Empty(h.Env.Spoken);

        // A new environment may point at another sim, so its files are analysed again.
        var other = NewEnv();
        h.Monitor.Start(other, startTimer: false);
        for (int i = 0; i < 10 && h.State != "Running"; i++) { h.Monitor.Tick(); other.NowMs += 100; }
        Assert.Equal("Running", h.State);
        Assert.Equal(1, other.AnalyseCalls);
        Assert.All(h.Env.Samplers, s => Assert.True(s.Disposed));
        Assert.Empty(h.Env.Spoken);
        Assert.Empty(other.Spoken);
    }

    [Fact]
    public void Stop_disposes_everything_and_ticks_do_nothing()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Monitor.Stop();
        Assert.Equal("Stopped", h.State);
        Assert.True(s.Disposed);
        int reads = s.Reads;
        h.Tick(20);
        Assert.Equal(reads, s.Reads);
        Assert.Equal(1, h.Env.LocateCalls);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("FMA reader starting", h.Monitor.DescribeForReadout());
        h.Monitor.Stop();
        h.Monitor.Dispose();

        h.Start(); // no layout cache: a new start analyses again
        h.RunUntilRunning();
        Assert.Equal(2, h.Env.AnalyseCalls);
    }

    [Theory]
    [InlineData("Stop")]
    [InlineData("Restart")]
    [InlineData("Start")]
    [InlineData("SdkLost")]
    public void Abandoned_locate_still_disposes_its_samplers(string how)
    {
        var h = New();
        var pending = new List<TaskCompletionSource<FmaLocateResult>>();
        h.Env.Locate = () =>
        {
            var t = new TaskCompletionSource<FmaLocateResult>();
            pending.Add(t);
            return t.Task;
        };
        h.Start();
        h.Tick(3);
        Assert.Equal("Locating", h.State);
        switch (how)
        {
            case "Stop": h.Monitor.Stop(); break;
            case "Restart": h.Monitor.Restart(); break;
            case "Start": h.Monitor.Start(h.Env, startTimer: false); break;
            case "SdkLost": h.Env.Sdk = null; h.Tick(); break;
        }
        h.Tick(5);

        var a = h.Env.NewSampler();
        var b = h.Env.NewSampler();
        pending[0].SetResult(new FmaLocateResult(new IFmaSampler[] { a, b }, null));
        Assert.True(SpinWait.SpinUntil(() => a.Disposed && b.Disposed, 5000));
        h.Tick(5);
        Assert.Equal(0, a.Reads); // the monitor never uses an abandoned result
        Assert.Equal(0, b.Reads);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Abandoned_analysis_fault_is_logged()
    {
        var h = New();
        var tcs = new TaskCompletionSource<FmaAnalysis>();
        h.Env.Analyse = () => tcs.Task;
        h.Start();
        h.Tick(2);
        Assert.Equal("Analysing", h.State);
        h.Monitor.Stop();
        tcs.SetException(new InvalidOperationException("late failure"));
        Assert.True(SpinWait.SpinUntil(() => h.Env.Logged.Any(l => l.Contains("late failure")), 5000));
        Assert.Empty(h.Env.Spoken);
    }

    private sealed class Harness
    {
        public Harness(FakeFmaEnvironment env) => Env = env;

        public FakeFmaEnvironment Env { get; }
        public Pmdg737FmaMonitor Monitor { get; } = new();
        public string State => Monitor.StateName;

        public Harness Start()
        {
            Monitor.Start(Env, startTimer: false);
            return this;
        }

        /// <summary>One Tick() at the current time per count, each followed by 100 ms on the clock.</summary>
        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Monitor.Tick();
                Env.NowMs += 100;
            }
        }

        public void TickUntil(Func<bool> done, int maxTicks = 300)
        {
            for (int i = 0; i < maxTicks && !done(); i++) Tick();
            Assert.True(done(), $"not reached within {maxTicks} ticks; state {State}");
        }

        /// <summary>Ticks until Running and returns the one sampler still alive (the one being read).</summary>
        public FakeSampler RunUntilRunning(int maxTicks = 300)
        {
            TickUntil(() => State == "Running", maxTicks);
            return Env.Samplers.Single(s => !s.Disposed);
        }
    }

    internal sealed class FakeSampler : IFmaSampler
    {
        private int _disposeCount;

        public FakeSampler(Pmdg737FmaLayout layout, FmaSample sample)
        {
            Layout = layout;
            Sample = sample;
        }

        public Pmdg737FmaLayout Layout { get; }

        /// <summary>What a read returns once <see cref="Script"/> is used up.</summary>
        public FmaSample Sample { get; set; }

        public Queue<FmaSample> Script { get; } = new();
        public bool Fail { get; set; }
        public Exception? Throw { get; set; }
        public int Reads { get; private set; }
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public bool Disposed => DisposeCount > 0;

        public bool TryRead(out FmaSample sample)
        {
            // A read after Dispose is a monitor bug; throwing turns it into a visible internal error.
            if (Disposed) throw new ObjectDisposedException(nameof(FakeSampler));
            Reads++;
            if (Throw is not null) throw Throw;
            sample = Fail ? default : Script.Count > 0 ? Script.Dequeue() : Sample;
            return !Fail;
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    internal sealed class FakeFmaEnvironment : IFmaEnvironment
    {
        private readonly object _gate = new();
        private readonly List<string> _spoken = new();
        private readonly List<string> _logged = new();

        public FakeFmaEnvironment() => Locate = () => Found(NewSampler());

        public long NowMs { get; set; }
        public FmaSdkSnapshot? Sdk { get; set; } = Pmdg737FmaMonitorTests.Sdk;

        /// <summary>When set, the SDK snapshot as a function of the clock (overrides <see cref="Sdk"/>).</summary>
        public Func<long, FmaSdkSnapshot?>? SdkAt { get; set; }

        public bool IsMuted { get; set; }

        /// <summary>This environment's own module session's notice (WASM-29).</summary>
        public Pmdg737DisplayFilesNotice FilesNotice { get; } = new();

        public Func<Task<FmaAnalysis>> Analyse { get; set; } =
            () => Task.FromResult(new FmaAnalysis(new[] { Layout }, null));
        public Func<Task<FmaLocateResult>> Locate { get; set; }
        public int AnalyseCalls { get; private set; }
        public int LocateCalls => LocateTimes.Count;
        public List<long> LocateTimes { get; } = new();
        public List<FakeSampler> Samplers { get; } = new();

        public string[] Spoken { get { lock (_gate) return _spoken.ToArray(); } }
        public string[] Logged { get { lock (_gate) return _logged.ToArray(); } }

        public FakeSampler NewSampler(FmaSample? sample = null, Pmdg737FmaLayout? layout = null)
        {
            var s = new FakeSampler(layout ?? Layout, sample ?? Sample(Base));
            lock (_gate) Samplers.Add(s);
            return s;
        }

        public FmaSdkSnapshot? ReadSdk() => SdkAt is { } at ? at(NowMs) : Sdk;

        public void Speak(string text) { lock (_gate) _spoken.Add(text); }
        public void Log(string line) { lock (_gate) _logged.Add(line); }

        public Task<FmaAnalysis> AnalyseAsync()
        {
            AnalyseCalls++;
            return Analyse();
        }

        public Task<FmaLocateResult> LocateAsync(IReadOnlyList<Pmdg737FmaLayout> layouts)
        {
            LocateTimes.Add(NowMs);
            return Locate();
        }
    }
}

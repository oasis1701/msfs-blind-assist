// The PFD monitor: the navigation display monitor's state machine (wait for the simulator's speed and altitude,
// analyse, locate, verify, run) with the PFD's gate (PMDG's IAS and altitude against the simulator's in some reading of
// the last 3 s, a flagged value skipped; no corrupt reading from the sampler), one Ctrl+M row, the window's view and
// its full reading, when Output Alt+P falls back to the AI read, and the check's measuring line in the log. Driven by a fake
// environment with a settable clock (500 ms per Tick()) and fake samplers.

using System.Globalization;
using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.Pmdg737PfdTestMemory;

namespace MSFSBlindAssist.Tests;

public sealed class Pmdg737PfdMonitorTests : IDisposable
{
    private static readonly Pmdg737PfdLayout Layout = M.Layout;

    /// <summary>IAS 160.2 and altitude 5000, lit, the PFD on the Captain's unit, no message up.</summary>
    private static readonly PfdSample Live = Pmdg737PfdSpeechTests.With();

    private static readonly PfdSample Disagree = Pmdg737PfdSpeechTests.With("ALT DISAGREE");

    private static PfdSample Dark(PfdSample s) => s with { UnitPages = s.UnitPages.Select(_ => Layout.Units.DarkPages[0]).ToList() };

    private static readonly PfdSimSnapshot Sim = new(161, 5010);

    private const string StoppedMismatch = "PFD announcements stopped: values do not match the simulator";
    private const string StoppedNotFound = "PFD announcements stopped: aircraft memory not found";
    private const string Resumed = "PFD announcements resumed";
    private const string DisagreeLine = "PFD message: ALT DISAGREE";

    private readonly List<FakeEnvironment> _envs = new();

    public void Dispose()
    {
        foreach (var env in _envs)
            foreach (var s in env.Samplers)
                Assert.True(s.DisposeCount <= 1, $"a sampler was disposed {s.DisposeCount} times");
    }

    private Harness New()
    {
        var env = new FakeEnvironment();
        _envs.Add(env);
        return new Harness(env);
    }

    [Fact]
    public void Missing_simulator_values_wait()
    {
        var h = New();
        h.Env.Sim = null;
        h.Start();
        h.Tick(20);
        Assert.Equal("WaitingForSimulator", h.State);
        Assert.Equal(0, h.Env.AnalyseCalls);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "Primary flight display starting" }, h.Monitor.View(null, "").Lines);
        Assert.False(h.Monitor.KeyFallsBackToAi(out _));

        // A value not delivered yet (a cache miss) waits too, never reads as 0.
        h.Env.Sim = new PfdSimSnapshot(null, 5000);
        h.Tick(10);
        Assert.Equal("WaitingForSimulator", h.State);

        h.Env.Sim = Sim;
        h.RunUntilRunning();
        // Running, the simulator going away (a disconnect) drops the reading silently.
        h.Env.Sim = null;
        h.Tick(3);
        Assert.Equal("WaitingForSimulator", h.State);
        Assert.Empty(h.Env.Spoken);
        Assert.Null(h.Monitor.Latest);
    }

    [Fact]
    public void First_good_reading_is_silent()
    {
        var h = New().Start();
        h.RunUntilRunning();
        h.Env.Locate = () => Found(h.Env.NewSampler(Disagree));
        h.Monitor.Restart();
        h.RunUntilRunning();
        h.Tick(10);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void A_message_after_settle_is_spoken_once()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Disagree;
        h.Tick(1);
        Assert.Empty(h.Env.Spoken);
        h.Tick(1);
        Assert.Equal(new[] { DisagreeLine }, h.Env.Spoken);
        h.Tick(10);
        Assert.Single(h.Env.Spoken);
    }

    [Fact]
    public void A_change_reverted_inside_settle_is_silent()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Script.Enqueue(Disagree);
        h.Tick(10);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Muted_row_silences_messages_and_status()
    {
        var h = New().Start();
        h.Env.Muted = true;
        var s = h.RunUntilRunning();
        s.Sample = Disagree;
        h.Tick(5);
        h.Env.Sim = Sim with { IasKnots = 250 };
        h.Tick(10);
        Assert.Equal("Mismatched", h.State);
        Assert.Empty(h.Env.Spoken);

        h.Env.Muted = false;
        h.Env.Sim = Sim;
        h.Tick(5);
        Assert.Equal(new[] { Resumed }, h.Env.Spoken);
    }

    [Fact]
    public void Mismatch_for_2_s_says_stopped_then_resumed_with_the_missed_change()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Sim = Sim with { AltitudeFt = 5600 };   // no reading of the last 3 s matches
        h.Tick(3);
        Assert.Empty(h.Env.Spoken);   // within the 2 s
        h.Tick(1);
        Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);
        Assert.Equal(new[] { "Primary flight display stopped: values do not match the simulator, trying again" }, h.Monitor.View(null, "").Lines);
        Assert.Null(h.Monitor.View(null, "").Sample);
        Assert.Null(h.Monitor.Latest);

        s.Sample = Disagree;
        h.Env.Sim = Sim;
        h.Tick(2);
        Assert.Equal(new[] { StoppedMismatch, Resumed, DisagreeLine }, h.Env.Spoken);
        Assert.Equal("Running", h.State);
    }

    [Fact]
    public void An_instrument_failing_is_spoken_never_stopped()
    {
        // The SPD flag up, PMDG's IAS gone to 0 while the simulator flies at 250: the reading still counts.
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Pmdg737PfdSpeechTests.With("SPD") with { Ias = 0 };
        h.Env.Sim = Sim with { IasKnots = 250 };
        h.Tick(30);
        Assert.Equal(new[] { "PFD message: SPD" }, h.Env.Spoken);
        Assert.Equal("Running", h.State);
    }

    [Fact]
    public void Lost_memory_for_10_s_says_stopped()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Locate = () => Found();
        s.Fail = true;
        h.Tick(19);
        Assert.Empty(h.Env.Spoken);
        h.Tick(2);
        Assert.Equal(new[] { StoppedNotFound }, h.Env.Spoken);
        h.Env.Locate = () => Found(h.Env.NewSampler());
        h.RunUntilRunning(100);
        Assert.Equal(new[] { StoppedNotFound, Resumed }, h.Env.Spoken);
    }

    [Fact]
    public void Ten_corrupt_readings_are_unavailable()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Corrupt = "the IAS is not a number while its tape is drawn";
        h.Tick(9);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("Running", h.State);
        h.Tick(1);
        Assert.Equal(new[] { "PFD announcements stopped: unexpected values" }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.True(h.Monitor.KeyFallsBackToAi(out string? sayFirst));
        Assert.Null(sayFirst);
        Assert.Equal(new[] { "Primary flight display unavailable: unexpected values" }, h.Monitor.View(null, "").Lines);
        Assert.Contains(h.Env.Logged, l => l.Contains("not a number", StringComparison.Ordinal));
    }

    [Fact]
    public void No_messages_for_10_s_after_light_up()
    {
        var h = New();
        h.Env.Locate = () => Found(h.Env.NewSampler(Dark(Live)));
        h.Start();
        var s = h.RunUntilRunning();
        Assert.Equal(new[] { "Captain PFD: off" }, h.Monitor.View(null, "").Lines);
        s.Sample = Dark(Disagree);
        h.Tick(5);
        Assert.Empty(h.Env.Spoken);

        s.Sample = Live;   // the displays light up
        h.Tick(4);
        s.Sample = Disagree;
        h.Tick(16);
        Assert.Empty(h.Env.Spoken);

        s.Sample = Pmdg737PfdSpeechTests.With("ALT DISAGREE", "NO VSPD");   // 10 s after lighting up, changes speak again
        h.Tick(3);
        Assert.Equal(new[] { "PFD message: NO VSPD" }, h.Env.Spoken);
    }

    [Fact]
    public void Not_shown_means_no_comparison()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        var elsewhere = Live with { UnitPages = Live.UnitPages.Select(_ => 3).ToList() };   // every unit on the ND page
        s.Sample = elsewhere;
        h.Tick(3);
        Assert.Equal(new[] { "Captain PFD: not on any display" }, h.Monitor.View(null, "").Lines);
        s.Sample = Disagree with { UnitPages = elsewhere.UnitPages };
        h.Tick(5);
        Assert.Empty(h.Env.Spoken);
        // Back on a screen: what it shows is the baseline, only later changes speak.
        s.Sample = Disagree;
        h.Tick(5);
        Assert.Empty(h.Env.Spoken);
        s.Sample = Live;
        h.Tick(3);
        Assert.Equal(new[] { "PFD message gone: ALT DISAGREE" }, h.Env.Spoken);
    }

    [Fact]
    public void An_undrawn_pfd_is_not_checked_against_the_simulator()
    {
        // Dark, or on no screen, PMDG draws no PFD: its speed and altitude may sit anywhere (cold and dark is not
        // measured yet); the reading still counts and the window says where the PFD is, never "stopped".
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Dark(Live) with { Ias = 0, Altitude = 0 };
        h.Tick(30);
        Assert.Equal("Running", h.State);
        Assert.Equal(new[] { "Captain PFD: off" }, h.Monitor.View(null, "").Lines);
        s.Sample = Live with { Ias = 0, Altitude = 0, UnitPages = Live.UnitPages.Select(_ => 3).ToList() };
        h.Tick(30);
        Assert.Equal("Running", h.State);
        Assert.Equal(new[] { "Captain PFD: not on any display" }, h.Monitor.View(null, "").Lines);
        Assert.Empty(h.Env.Spoken);

        // Drawn again with values that still disagree: compared, and stopped after 2 s.
        s.Sample = Live with { Ias = 0, Altitude = 0 };
        h.Tick(5);
        Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);
    }

    [Fact]
    public void View_running_is_the_rows_and_the_sample()
    {
        var h = New().Start();
        h.RunUntilRunning();
        var view = h.Monitor.View(new[] { "A/T: N1" }, "");
        Assert.Equal("A/T: N1", view.Lines[0]);
        Assert.NotNull(view.Sample);
        Assert.Same(Layout, view.Layout);
        Assert.Same(view.Sample, h.Monitor.Latest);
    }

    [Fact]
    public void View_after_Restart_is_starting_never_the_old_sample()
    {
        var h = New().Start();
        h.RunUntilRunning();
        h.Monitor.Restart();
        var view = h.Monitor.View(null, "FMA reader starting");
        Assert.Equal(new[] { "Primary flight display starting" }, view.Lines);
        Assert.Null(view.Sample);
        Assert.Null(view.Layout);
        Assert.Null(h.Monitor.Latest);
    }

    [Fact]
    public void Window_open_makes_the_sampler_full()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        Assert.False(s.Full);
        h.Monitor.WindowOpen = true;
        Assert.True(s.Full);
        // A sampler found later (a reload) starts full while the window is open.
        h.Monitor.Restart();
        var next = h.RunUntilRunning();
        Assert.NotSame(s, next);
        Assert.True(next.Full);
        h.Monitor.WindowOpen = false;
        Assert.False(next.Full);
    }

    [Fact]
    public void Keep_full_reads_the_full_set_with_the_window_closed()
    {
        // The Captain's minimums setter reads its value from a full reading, from the Ctrl+P panel too.
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Monitor.KeepFull = true;
        Assert.True(s.Full);
        h.Monitor.WindowOpen = true;
        h.Monitor.WindowOpen = false;   // the window closing keeps the set full while a setter needs it
        Assert.True(s.Full);
        h.Monitor.KeepFull = false;
        Assert.False(s.Full);
        h.Monitor.WindowOpen = true;
        h.Monitor.KeepFull = false;     // and the setter ending keeps it full while the window is open
        Assert.True(s.Full);
        // A sampler found later (a reload) starts full while a setter needs it.
        h.Monitor.WindowOpen = false;
        h.Monitor.KeepFull = true;
        h.Monitor.Restart();
        var next = h.RunUntilRunning();
        Assert.True(next.Full);
    }

    [Fact]
    public void Key_opens_the_window_while_starting_and_for_a_short_stop()
    {
        var h = New();
        h.Env.Sim = null;
        h.Start();
        Assert.False(h.Monitor.KeyFallsBackToAi(out _));
        h.Env.Sim = Sim;
        var s = h.RunUntilRunning();
        Assert.False(h.Monitor.KeyFallsBackToAi(out _));
        h.Env.Locate = () => Found();
        s.Fail = true;
        h.Tick(10);
        Assert.False(h.Monitor.KeyFallsBackToAi(out _));
        h.Tick(11);
        Assert.True(h.Monitor.KeyFallsBackToAi(out string? sayFirst)); // stopped for 10 s: straight to the AI read
        Assert.Null(sayFirst);
    }

    [Fact]
    public void Key_falls_back_when_unavailable()
    {
        var h = New();
        h.Env.Analyse = () => Task.FromResult(new PfdAnalysis(Array.Empty<Pmdg737PfdLayout>(), FmaReasons.FileNotRecognised));
        h.Start();
        h.Tick(3);
        Assert.Equal(new[] { Pmdg737DisplaySpeech.FilesNotLoaded }, h.Env.Spoken); // the four readers' one line (WASM-29)
        Assert.True(h.Monitor.KeyFallsBackToAi(out string? sayFirst));
        Assert.Null(sayFirst);
        Assert.Equal(new[] { "Primary flight display unavailable: PMDG file not recognised" }, h.Monitor.View(null, "").Lines);
    }

    [Fact]
    public void A_layout_missing_from_a_loaded_file_says_its_own_line()
    {
        // A layout file loaded, but this reader's layout in it is a failure: its own line, as before, and the four
        // readers' shared line is left unused (WASM-29). Alt+P still goes straight to the AI read, saying nothing.
        var h = New();
        h.Env.Analyse = () => Task.FromResult(
            new PfdAnalysis(Array.Empty<Pmdg737PfdLayout>(), FmaReasons.FileNotRecognised) { LayoutFileLoaded = true });
        h.Start();
        h.Tick(3);
        Assert.Equal(new[] { "PFD announcements unavailable: PMDG file not recognised" }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.True(h.Monitor.KeyFallsBackToAi(out string? sayFirst));
        Assert.Null(sayFirst);
        var later = new List<string>();
        h.Env.FilesNotice.Say(muted: false, later.Add);
        Assert.Equal(new[] { Pmdg737DisplaySpeech.FilesNotLoaded }, later); // not used up
    }

    [Fact]
    public void Key_says_classic_first_when_the_captain_screen_is_classic()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Live with { UnitPages = new[] { Layout.Units.ClassicPages[0], 3, 5, 6, 3, 2 } };
        h.Tick(2);
        Assert.True(h.Monitor.KeyFallsBackToAi(out string? sayFirst));
        Assert.Equal("Classic EFIS is selected, falling back to AI reading", sayFirst);
        Assert.Equal(new[] { "Captain PFD: classic EFIS display, not read" }, h.Monitor.View(null, "").Lines);
        s.Sample = Live;
        h.Tick(2);
        Assert.False(h.Monitor.KeyFallsBackToAi(out sayFirst));
        Assert.Null(sayFirst);
    }

    [Fact]
    public void Check_line_every_5_s_and_on_a_large_difference()
    {
        var h = New().Start();
        h.RunUntilRunning();
        int Checks() => h.Env.Logged.Count(l => l.StartsWith("check: ", StringComparison.Ordinal));
        int before = Checks();
        h.Tick(10);   // 5 s
        Assert.Equal(before + 1, Checks());
        Assert.Contains("check: ias pmdg 160.2 sim 161.0 diff -0.8, alt pmdg 5000.0 sim 5010.0 diff -10.0, spd flag no, alt flag no", h.Env.Logged);

        // Past half the tolerance (11.3 kt of 21): every reading is logged while it lasts.
        h.Env.Sim = Sim with { IasKnots = 171.5 };
        before = Checks();
        h.Tick(3);
        Assert.Equal(before + 3, Checks());
        Assert.Contains("check: ias pmdg 160.2 sim 171.5 diff -11.3, alt pmdg 5000.0 sim 5010.0 diff -10.0, spd flag no, alt flag no", h.Env.Logged);

        // Parked: PMDG's tape floor against the simulator's 0 is no difference to log.
        h.Env.Sim = Sim with { IasKnots = 0 };
        h.Env.Samplers.Single(x => !x.Disposed).Sample = Live with { Ias = Layout.Speed.Geometry.Floor };
        h.Tick(1);
        before = Checks();
        h.Tick(3);
        Assert.Equal(before, Checks());
    }

    [Fact]
    public void Check_line_is_invariant_under_sv_SE()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("check: ias pmdg 160.2 sim 161.0 diff -0.8, alt pmdg 5000.0 sim 5010.0 diff -10.0, spd flag no, alt flag no",
                Pmdg737PfdMonitor.CheckLine(Live, Sim, Layout));
            Assert.Equal("check: ias pmdg 0.0 sim none diff none, alt pmdg 5000.0 sim 5010.0 diff -10.0, spd flag yes, alt flag no",
                Pmdg737PfdMonitor.CheckLine(Pmdg737PfdSpeechTests.With("SPD") with { Ias = 0 }, Sim with { IasKnots = null }, Layout));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Stop_releases_every_sampler()
    {
        var h = New().Start();
        h.RunUntilRunning();
        h.Monitor.Stop();
        Assert.All(h.Env.Samplers, s => Assert.True(s.Disposed));
        Assert.Equal("Stopped", h.State);
    }

    // ---- fakes ----

    private static Task<PfdLocateResult> Found(params IPfdSampler[] candidates) =>
        Task.FromResult(new PfdLocateResult(candidates, null));

    private sealed class Harness
    {
        public Harness(FakeEnvironment env) => Env = env;

        public FakeEnvironment Env { get; }
        public Pmdg737PfdMonitor Monitor { get; } = new();
        public string State => Monitor.StateName;

        public Harness Start()
        {
            Monitor.Start(Env, startTimer: false);
            return this;
        }

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Monitor.Tick();
                Env.NowMs += Pmdg737PfdMonitor.TickMs;
            }
        }

        public FakeSampler RunUntilRunning(int maxTicks = 100)
        {
            for (int i = 0; i < maxTicks && State != "Running"; i++) Tick();
            Assert.True(State == "Running", $"not running within {maxTicks} ticks; state {State}");
            return Env.Samplers.Single(s => !s.Disposed);
        }
    }

    /// <summary>Also drives the reader in <see cref="Pmdg737PfdWiringTests"/>.</summary>
    internal sealed class FakeSampler : IPfdSampler
    {
        public FakeSampler(PfdSample sample) => Sample = sample;

        public Pmdg737PfdLayout Layout => Pmdg737PfdMonitorTests.Layout;
        public bool Full { get; set; }
        public PfdSample Sample { get; set; }
        public Queue<PfdSample> Script { get; } = new();
        public bool Fail { get; set; }
        public string? Corrupt { get; set; }
        public int DisposeCount { get; private set; }
        public bool Disposed => DisposeCount > 0;

        public bool TryRead(out PfdSample sample, out string? corrupt)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(FakeSampler));
            corrupt = Fail ? null : Corrupt;
            sample = Fail || Corrupt is not null ? null! : Script.Count > 0 ? Script.Dequeue() : Sample;
            return !Fail && Corrupt is null;
        }

        public void Dispose() => DisposeCount++;
    }

    /// <summary>Also drives the reader in <see cref="Pmdg737PfdWiringTests"/>.</summary>
    internal sealed class FakeEnvironment : IPfdEnvironment
    {
        public FakeEnvironment() => Locate = () => Found(NewSampler());

        public long NowMs { get; set; }
        public PfdSimSnapshot? Sim { get; set; } = Pmdg737PfdMonitorTests.Sim;
        public bool Muted { get; set; }
        public Pmdg737DisplayFilesNotice FilesNotice { get; init; } = new();
        public Func<Task<PfdAnalysis>> Analyse { get; set; } =
            () => Task.FromResult(new PfdAnalysis(new[] { Layout }, null));
        public Func<Task<PfdLocateResult>> Locate { get; set; }
        public int AnalyseCalls { get; private set; }
        public List<FakeSampler> Samplers { get; } = new();
        public List<string> SpokenList { get; } = new();
        public string[] Spoken => SpokenList.ToArray();
        public List<string> Logged { get; } = new();

        public FakeSampler NewSampler(PfdSample? sample = null)
        {
            var s = new FakeSampler(sample ?? Live);
            Samplers.Add(s);
            return s;
        }

        public PfdSimSnapshot? ReadSimulator() => Sim;
        public bool IsMuted() => Muted;
        public void Speak(string text) => SpokenList.Add(text);
        public void Log(string line) => Logged.Add(line);

        public Task<PfdAnalysis> AnalyseAsync()
        {
            AnalyseCalls++;
            return Analyse();
        }

        public Task<PfdLocateResult> LocateAsync(IReadOnlyList<Pmdg737PfdLayout> layouts) => Locate();
    }
}

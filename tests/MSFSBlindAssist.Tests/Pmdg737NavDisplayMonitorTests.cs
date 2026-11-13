// The navigation display monitor: the engine display monitor's state machine (wait for the PMDG SDK,
// analyse, locate, verify against the SDK, run) with the ND's gate (the display units' fingerprint, the
// Captain's EFIS mode and range against the SDK, no corrupt reading from the sampler), one Ctrl+M row, the
// window's view, when Output Alt+N falls back to the AI read and Output Ctrl+W's answer. Driven by a fake environment with a
// settable clock (500 ms per Tick()) and fake samplers.

using MSFSBlindAssist.Services.PMDG;
using Xunit;
using M = MSFSBlindAssist.Tests.NdTestMemory;

namespace MSFSBlindAssist.Tests;

public sealed class Pmdg737NavDisplayMonitorTests : IDisposable
{
    private static readonly Pmdg737NavDisplayLayout Layout = M.Layout;

    /// <summary>The live reading with TCAS in STBY: EMPAD the TO waypoint, MAP, range index 2.</summary>
    private static readonly NavDisplaySample Live = Ground().Read();

    private static readonly NavDisplaySample Terrain = Ground(m => m.SetCondition("TERRAIN")).Read();

    private static readonly NavDisplaySample Sequenced = Ground(m => m.SetKeys(0, m.RouteNodes[0], m.RouteNodes[2])).Read();

    private static M Ground(Action<M>? change = null)
    {
        var m = new M();
        m.SetCondition("TCAS OFF");
        change?.Invoke(m);
        return m;
    }

    private static NavDisplaySample Dark(NavDisplaySample s) => s with { UnitPages = s.UnitPages.Select(_ => Layout.Units.DarkPages[0]).ToList() };

    private static readonly NavDisplaySdkSnapshot Sdk = new(Layout.Geometry.Modes.Map, 2);

    private const string StoppedMismatch = "ND announcements stopped: values do not match the SDK";
    private const string StoppedNotFound = "ND announcements stopped: aircraft memory not found";
    private const string Resumed = "ND announcements resumed";
    private const string TerrainLine = "ND message: TERRAIN";
    private const string SequencedLine = "ND: PT753, 23.0 NM";

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
    public void Waiting_for_sdk_is_silent()
    {
        var h = New();
        h.Env.Sdk = null;
        h.Start();
        h.Tick(20);
        Assert.Equal("WaitingForSdk", h.State);
        Assert.Equal(0, h.Env.AnalyseCalls);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "Navigation display starting" }, h.Monitor.View().Lines);
        // A key press says why, as Output P does ("FMA reader waiting for PMDG SDK data"); the window, as Engine
        // displays', says starting.
        Assert.Equal("ND waypoint unavailable: waiting for PMDG SDK data", h.Monitor.WaypointLine());
    }

    [Fact]
    public void First_good_reading_is_silent()
    {
        var h = New().Start();
        h.RunUntilRunning();
        // A flight reload whose first reading shows TERRAIN: the new baseline, not news.
        h.Env.Locate = () => Found(h.Env.NewSampler(Terrain));
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
        s.Sample = Terrain;
        h.Tick(1); // seen: settling
        Assert.Empty(h.Env.Spoken);
        h.Tick(1); // held for 500 ms
        Assert.Equal(new[] { TerrainLine }, h.Env.Spoken);
        h.Tick(10);
        Assert.Single(h.Env.Spoken);
    }

    [Fact]
    public void A_waypoint_change_reverted_inside_settle_is_silent()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Script.Enqueue(Sequenced);
        h.Tick(10);
        Assert.Empty(h.Env.Spoken);
        s.Sample = Sequenced;
        h.Tick(3);
        Assert.Equal(new[] { SequencedLine }, h.Env.Spoken);
    }

    [Fact]
    public void Muted_row_silences_messages_waypoints_and_status()
    {
        var h = New().Start();
        h.Env.Muted = true;
        var s = h.RunUntilRunning();
        s.Sample = Terrain with { Active = Sequenced.Active };
        h.Tick(5);
        h.Env.Sdk = Sdk with { ModeSel = Layout.Geometry.Modes.Plan };
        h.Tick(10);
        Assert.Equal("Mismatched", h.State);
        Assert.Empty(h.Env.Spoken);

        // Muted changes still advance the baseline: unmuted, nothing is said late, and "resumed" is owed.
        h.Env.Muted = false;
        h.Env.Sdk = Sdk;
        h.Tick(5);
        Assert.Equal(new[] { Resumed }, h.Env.Spoken);
    }

    [Fact]
    public void Mismatch_for_2_s_says_stopped_then_resumed_with_the_missed_change()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Sdk = Sdk with { RangeSel = 3 }; // the SDK disagrees: the range knob
        h.Tick(3);
        Assert.Empty(h.Env.Spoken); // within the 2 s
        h.Tick(1);
        Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);
        Assert.Equal(new[] { "Navigation display stopped: values do not match the SDK, trying again" }, h.Monitor.View().Lines);
        Assert.Null(h.Monitor.View().Sample);

        s.Sample = Sequenced; // changed while stopped: told after "resumed", never swallowed
        h.Env.Sdk = Sdk;
        h.Tick(2);
        Assert.Equal(new[] { StoppedMismatch, Resumed, SequencedLine }, h.Env.Spoken);
        Assert.Equal("Running", h.State);
    }

    [Fact]
    public void Lost_memory_for_10_s_says_stopped()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Locate = () => Found(); // the next searches find nothing
        s.Fail = true;
        h.Tick(19);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "Navigation display stopped: aircraft memory not found, trying again" }, h.Monitor.View().Lines);
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
        s.Corrupt = "the route: a node visited twice (a loop)";
        h.Tick(9);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("Running", h.State);
        h.Tick(1);
        Assert.Equal(new[] { "ND announcements stopped: unexpected values" }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.True(h.Monitor.KeyFallsBackToAi());
        Assert.Equal(new[] { "Navigation display unavailable: unexpected values" }, h.Monitor.View().Lines);
        Assert.Contains(h.Env.Logged, l => l.Contains("a node visited twice", StringComparison.Ordinal));
    }

    [Fact]
    public void A_corrupt_reading_between_good_ones_is_dropped()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Corrupt = "the route: a node visited twice (a loop)";
        h.Tick(3);
        s.Corrupt = null;
        s.Sample = Terrain;
        h.Tick(3);
        Assert.Equal(new[] { TerrainLine }, h.Env.Spoken);
        Assert.Equal("Running", h.State);
    }

    [Fact]
    public void Units_out_of_order_never_verify()
    {
        var h = New();
        h.Env.Locate = () => Found(h.Env.NewSampler(Live with { UnitIndices = new[] { 0, 1, 3, 2, 4, 5 } }));
        h.Start();
        h.Tick(40);
        Assert.NotEqual("Running", h.State);
    }

    [Fact]
    public void No_comparison_until_10_s_after_light_up()
    {
        var h = New();
        h.Env.Locate = () => Found(h.Env.NewSampler(Dark(Live)));
        h.Start();
        var s = h.RunUntilRunning();
        Assert.Equal(new[] { "Captain ND: off" }, h.Monitor.View().Lines);
        s.Sample = Dark(Terrain);
        h.Tick(5);
        Assert.Empty(h.Env.Spoken); // dark: nothing

        s.Sample = Live; // the displays light up
        h.Tick(4);
        s.Sample = Sequenced; // the FMC comes up a moment later
        h.Tick(16);
        Assert.Empty(h.Env.Spoken);

        s.Sample = Terrain with { Active = Sequenced.Active }; // 10 s after lighting up, changes speak again
        h.Tick(3);
        Assert.Equal(new[] { TerrainLine }, h.Env.Spoken);
    }

    [Fact]
    public void View_running_is_the_rows_and_the_sample()
    {
        var h = New().Start();
        h.RunUntilRunning();
        var view = h.Monitor.View();
        Assert.Equal("Captain ND: MAP, range 20", view.Lines[0]);
        Assert.Contains("Message: TCAS OFF", view.Lines);
        Assert.NotNull(view.Sample);
        Assert.Same(Layout, view.Layout);
    }

    [Fact]
    public void View_after_Restart_is_starting_never_the_old_sample()
    {
        var h = New().Start();
        h.RunUntilRunning();
        h.Monitor.Restart();
        var view = h.Monitor.View();
        Assert.Equal(new[] { "Navigation display starting" }, view.Lines);
        Assert.Null(view.Sample);
        Assert.Null(view.Layout);
    }

    [Fact]
    public void Key_opens_the_window_while_starting_and_for_a_short_stop()
    {
        var h = New();
        h.Env.Sdk = null;
        h.Start();
        Assert.False(h.Monitor.KeyFallsBackToAi());
        h.Env.Sdk = Sdk;
        var s = h.RunUntilRunning();
        Assert.False(h.Monitor.KeyFallsBackToAi());
        h.Env.Locate = () => Found();
        s.Fail = true;
        h.Tick(10);
        Assert.False(h.Monitor.KeyFallsBackToAi());
        h.Tick(11);
        Assert.True(h.Monitor.KeyFallsBackToAi()); // stopped for 10 s
    }

    [Fact]
    public void Key_falls_back_when_unavailable()
    {
        var h = New();
        h.Env.Analyse = () => Task.FromResult(new NavDisplayAnalysis(Array.Empty<Pmdg737NavDisplayLayout>(), FmaReasons.FileNotRecognised));
        h.Start();
        h.Tick(3);
        Assert.Equal(new[] { Pmdg737DisplaySpeech.FilesNotLoaded }, h.Env.Spoken); // the four readers' one line (WASM-29)
        Assert.True(h.Monitor.KeyFallsBackToAi());
        Assert.Equal(new[] { "Navigation display unavailable: PMDG file not recognised" }, h.Monitor.View().Lines);
        Assert.Equal("ND waypoint unavailable: PMDG file not recognised", h.Monitor.WaypointLine());
    }

    [Fact]
    public void A_layout_missing_from_a_loaded_file_says_its_own_line()
    {
        // A layout file loaded, but this reader's layout in it is a failure: its own line, as before, and the four
        // readers' shared line is left unused (WASM-29). Alt+N still goes straight to the AI read.
        var h = New();
        h.Env.Analyse = () => Task.FromResult(
            new NavDisplayAnalysis(Array.Empty<Pmdg737NavDisplayLayout>(), FmaReasons.FileNotRecognised) { LayoutFileLoaded = true });
        h.Start();
        h.Tick(3);
        Assert.Equal(new[] { "ND announcements unavailable: PMDG file not recognised" }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.True(h.Monitor.KeyFallsBackToAi());
        var later = new List<string>();
        h.Env.FilesNotice.Say(muted: false, later.Add);
        Assert.Equal(new[] { Pmdg737DisplaySpeech.FilesNotLoaded }, later); // not used up
    }

    [Fact]
    public void Waypoint_line_answers_with_no_display_showing_the_nd()
    {
        var h = New();
        h.Env.Sdk = null;
        Assert.Equal("Navigation display starting", h.Monitor.WaypointLine());
        h.Start();
        Assert.Equal("ND waypoint unavailable: waiting for PMDG SDK data", h.Monitor.WaypointLine());
        h.Env.Sdk = Sdk;
        var s = h.RunUntilRunning();
        Assert.Equal("EMPAD, 1533.3Z, 6.8 NM", h.Monitor.WaypointLine());

        // The ND on no display (the unit on another page): Ctrl+W still answers from the FMC.
        s.Sample = Live with { UnitPages = Live.UnitPages.Select(_ => 2).ToList() };
        h.Tick(2);
        Assert.Equal(new[] { "Captain ND: not on any display" }, h.Monitor.View().Lines);
        Assert.Equal("EMPAD, 1533.3Z, 6.8 NM", h.Monitor.WaypointLine());

        s.Sample = Live with { Active = null };
        h.Tick(2);
        Assert.Equal("No active waypoint", h.Monitor.WaypointLine());

        h.Env.Locate = () => Found();
        s.Fail = true;
        h.Tick(2);
        Assert.Equal("ND waypoint unavailable: aircraft memory not found", h.Monitor.WaypointLine());
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

    private static Task<NavDisplayLocateResult> Found(params INavDisplaySampler[] candidates) =>
        Task.FromResult(new NavDisplayLocateResult(candidates, null));

    private sealed class Harness
    {
        public Harness(FakeEnvironment env) => Env = env;

        public FakeEnvironment Env { get; }
        public Pmdg737NavDisplayMonitor Monitor { get; } = new();
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
                Env.NowMs += Pmdg737NavDisplayMonitor.TickMs;
            }
        }

        public FakeSampler RunUntilRunning(int maxTicks = 100)
        {
            for (int i = 0; i < maxTicks && State != "Running"; i++) Tick();
            Assert.True(State == "Running", $"not running within {maxTicks} ticks; state {State}");
            return Env.Samplers.Single(s => !s.Disposed);
        }
    }

    /// <summary>Also drives the reader in <see cref="Pmdg737NavDisplayWiringTests"/>.</summary>
    internal sealed class FakeSampler : INavDisplaySampler
    {
        public FakeSampler(NavDisplaySample sample) => Sample = sample;

        public Pmdg737NavDisplayLayout Layout => Pmdg737NavDisplayMonitorTests.Layout;
        public NavDisplaySample Sample { get; set; }
        public Queue<NavDisplaySample> Script { get; } = new();
        public bool Fail { get; set; }
        public string? Corrupt { get; set; }
        public int DisposeCount { get; private set; }
        public bool Disposed => DisposeCount > 0;

        public bool TryRead(out NavDisplaySample sample, out string? corrupt)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(FakeSampler));
            corrupt = Fail ? null : Corrupt;
            sample = Fail || Corrupt is not null ? null! : Script.Count > 0 ? Script.Dequeue() : Sample;
            return !Fail && Corrupt is null;
        }

        public void Dispose() => DisposeCount++;
    }

    /// <summary>Also drives the reader in <see cref="Pmdg737NavDisplayWiringTests"/>.</summary>
    internal sealed class FakeEnvironment : INavDisplayEnvironment
    {
        public FakeEnvironment() => Locate = () => Found(NewSampler());

        public long NowMs { get; set; }
        public NavDisplaySdkSnapshot? Sdk { get; set; } = Pmdg737NavDisplayMonitorTests.Sdk;
        public bool Muted { get; set; }
        public Pmdg737DisplayFilesNotice FilesNotice { get; init; } = new();
        public Func<Task<NavDisplayAnalysis>> Analyse { get; set; } =
            () => Task.FromResult(new NavDisplayAnalysis(new[] { Layout }, null));
        public Func<Task<NavDisplayLocateResult>> Locate { get; set; }
        public int AnalyseCalls { get; private set; }
        public List<FakeSampler> Samplers { get; } = new();
        public List<string> SpokenList { get; } = new();
        public string[] Spoken => SpokenList.ToArray();
        public List<string> Logged { get; } = new();

        public FakeSampler NewSampler(NavDisplaySample? sample = null)
        {
            var s = new FakeSampler(sample ?? Live);
            Samplers.Add(s);
            return s;
        }

        public NavDisplaySdkSnapshot? ReadSdk() => Sdk;
        public bool IsMuted() => Muted;
        public void Speak(string text) => SpokenList.Add(text);
        public void Log(string line) => Logged.Add(line);

        public Task<NavDisplayAnalysis> AnalyseAsync()
        {
            AnalyseCalls++;
            return Analyse();
        }

        public Task<NavDisplayLocateResult> LocateAsync(IReadOnlyList<Pmdg737NavDisplayLayout> layouts) => Locate();
    }
}

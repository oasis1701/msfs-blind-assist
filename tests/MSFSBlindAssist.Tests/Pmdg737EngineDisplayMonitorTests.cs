// The engine display monitor: the FMA monitor's state machine (wait for the PMDG SDK, analyse, locate,
// verify against the SDK, run) with the engine display gate (the fuel tanks against the SDK, the display units'
// fingerprint, no corrupt value), three Ctrl+M rows, the window's view and when Output Alt+E falls back to the AI read.
// Driven by a fake environment with a settable clock (100 ms per Tick()) and fake samplers.

using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public sealed class Pmdg737EngineDisplayMonitorTests : IDisposable
{
    private static readonly Pmdg737EngineDisplayLayout Layout = GoldenLayouts.EngineDisplays;

    private static EngineDisplaySample Idle => EngineDisplaySampleFactory.Idle();

    // The SDK's tanks for the idle sample: left is tank 1, right tank 2.
    private static readonly EngineDisplaySdkSnapshot Sdk = new(8626, 7782, 8628);

    private static EngineDisplaySample StartValve(EngineDisplaySample s, int engine1 = 2) =>
        s with { Engines = new[] { s.Engines[0] with { StartValve = engine1 }, s.Engines[1] } };

    private const string StoppedMismatch = "Engine display announcements stopped: values do not match the SDK";
    private const string StoppedNotFound = "Engine display announcements stopped: aircraft memory not found";
    private const string Resumed = "Engine display announcements resumed";
    private const string StartValveLine = "Upper DU alert: engine 1 START VALVE OPEN";

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
        h.Tick(50);
        Assert.Equal("WaitingForSdk", h.State);
        Assert.Equal(0, h.Env.AnalyseCalls);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "Engine displays starting" }, h.Monitor.View().Upper);
    }

    [Fact]
    public void First_good_reading_is_silent()
    {
        var h = New().Start();
        h.RunUntilRunning();
        // A flight reload whose first reading has a start valve open: the new baseline, not news.
        h.Env.Locate = () => Found(h.Env.NewSampler(StartValve(Idle)));
        h.Monitor.Restart();
        h.RunUntilRunning();
        h.Tick(20);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Alert_after_settle_is_spoken_once()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = StartValve(Idle);
        h.Tick(3); // 0, 100, 200 ms: settling
        Assert.Empty(h.Env.Spoken);
        h.Tick(1); // 300 ms
        Assert.Equal(new[] { StartValveLine }, h.Env.Spoken);
        h.Tick(30);
        Assert.Single(h.Env.Spoken);
    }

    [Fact]
    public void Change_and_back_inside_settle_is_silent()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Script.Enqueue(StartValve(Idle));
        s.Script.Enqueue(StartValve(Idle));
        h.Tick(20);
        Assert.Empty(h.Env.Spoken);
    }

    [Fact]
    public void Each_mute_row_mutes_only_its_kind()
    {
        var change = StartValve(Idle) with { ThrustMode = 4 };
        change = change with { Engines = new[] { change.Engines[0] with { Egt = 930 }, change.Engines[1] } };
        var expected = new Dictionary<EngineDisplayMuteRow, string>
        {
            [EngineDisplayMuteRow.Alerts] = StartValveLine,
            [EngineDisplayMuteRow.Colors] = "Upper DU: engine 1 EGT amber",
            [EngineDisplayMuteRow.ThrustMode] = "Upper DU: thrust mode CLB",
        };
        foreach (var muted in expected.Keys)
        {
            var h = New().Start();
            h.Env.Muted.Add(muted);
            var s = h.RunUntilRunning();
            s.Sample = change;
            h.Tick(10);
            Assert.Equal(expected.Where(kv => kv.Key != muted).Select(kv => kv.Value), h.Env.Spoken);
        }
    }

    [Fact]
    public void Muted_changes_still_advance_the_baseline()
    {
        var h = New().Start();
        h.Env.Muted.Add(EngineDisplayMuteRow.Alerts);
        var s = h.RunUntilRunning();
        s.Sample = StartValve(Idle);
        h.Tick(10);
        h.Env.Muted.Clear();
        h.Tick(10);
        Assert.Empty(h.Env.Spoken); // the start valve was already said (muted): never said late
    }

    [Fact]
    public void Status_lines_follow_the_alerts_row()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Muted.Add(EngineDisplayMuteRow.Alerts);
        h.Env.Sdk = Sdk with { FuelCenterLb = 9000 };
        h.Tick(30);
        Assert.Equal("Mismatched", h.State);
        Assert.Empty(h.Env.Spoken);

        h.Env.Muted.Clear();
        h.Env.Muted.Add(EngineDisplayMuteRow.Colors);
        h.Env.Muted.Add(EngineDisplayMuteRow.ThrustMode);
        h.Env.Sdk = Sdk;
        h.Tick(5);
        Assert.Equal(new[] { Resumed }, h.Env.Spoken);
        Assert.False(s.Disposed);
    }

    [Fact]
    public void Mismatch_for_2_s_says_stopped_then_resumed_with_the_missed_change()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Sdk = Sdk with { FuelLeftLb = 8000 }; // the SDK disagrees: tank 1
        h.Tick(19);
        Assert.Empty(h.Env.Spoken); // within the 2 s
        h.Tick(2);
        Assert.Equal(new[] { StoppedMismatch }, h.Env.Spoken);
        Assert.Equal(new[] { "Engine displays stopped: values do not match the SDK, trying again" }, h.Monitor.View().Lower);

        s.Sample = StartValve(Idle); // changed while stopped: told after "resumed", never swallowed
        h.Env.Sdk = Sdk;
        h.Tick(3);
        Assert.Equal(new[] { StoppedMismatch, Resumed, StartValveLine }, h.Env.Spoken);
        Assert.Equal("Running", h.State);
    }

    [Fact]
    public void Lost_memory_for_10_s_says_stopped()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Locate = () => Found(); // the next searches find nothing
        s.Fail = true;
        h.Tick(99);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal(new[] { "Engine displays stopped: aircraft memory not found, trying again" }, h.Monitor.View().Upper);
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
        s.Sample = Idle with { Engines = new[] { Idle.Engines[0] with { N1 = double.NaN }, Idle.Engines[1] } };
        h.Tick(9);
        Assert.Empty(h.Env.Spoken);
        h.Tick(1);
        Assert.Equal(new[] { "Engine display announcements stopped: unexpected values" }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.True(h.Monitor.KeyFallsBackToAi());
        Assert.Equal(new[] { "Engine displays unavailable: unexpected values" }, h.Monitor.View().Upper);
    }

    [Theory]
    [InlineData(-5.0, 600.0)]   // N1 below -1
    [InlineData(21.0, 1400.0)]  // EGT above 1 300
    public void Values_out_of_range_are_corrupt(double n1, double egt)
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Idle with { Engines = new[] { Idle.Engines[0] with { N1 = n1, Egt = egt }, Idle.Engines[1] } };
        h.Tick(10);
        Assert.Equal("Unavailable", h.State);
    }

    [Fact]
    public void Unknown_page_is_not_corrupt()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = Idle with { LowerPage = 14 };
        h.Tick(30);
        Assert.Equal("Running", h.State);
        Assert.Equal(new[] { "Lower display: page 14" }, h.Monitor.View().Lower);
        s.Sample = Idle with { LowerPage = 256 };
        h.Tick(10);
        Assert.Equal("Unavailable", h.State);
    }

    [Fact]
    public void Units_out_of_order_never_verify()
    {
        var h = New();
        h.Env.Locate = () => Found(h.Env.NewSampler(Idle with { UnitIndices = new[] { 0, 1, 3, 2, 4, 5 } }));
        h.Start();
        h.Tick(100);
        Assert.NotEqual("Running", h.State);
    }

    [Fact]
    public void Power_up_from_constructor_values_is_a_silent_baseline()
    {
        // Cold and dark at load (live 2026-10-24): constructor values, X-BLD START set on both engines,
        // tanks 0 in memory and the SDK alike. Power-up then brings the real values: nothing is announced.
        var junk = Idle with
        {
            Initialised = false, EgtRedLine = 933, EgtMaxContinuous = 860, N1RedLine = 100, UpperPage = 4,
            Engines = new[] { Idle.Engines[0] with { CrossBleedStart = true, N1 = 0 }, Idle.Engines[1] with { CrossBleedStart = true, N1 = 0 } },
            FuelQuantity = new[] { 0.0, 0.0, 0.0 },
        };
        var h = New();
        h.Env.Sdk = new EngineDisplaySdkSnapshot(0, 0, 0);
        h.Env.Locate = () => Found(h.Env.NewSampler(junk));
        h.Start();
        var s = h.RunUntilRunning();
        Assert.Equal(new[] { "Upper display: off" }, h.Monitor.View().Upper);

        var powered = StartValve(Idle) with { FuelQuantity = new[] { 0.0, 0.0, 0.0 } };
        s.Sample = powered with { Engines = new[] { powered.Engines[0] with { LowOilPressure = 2 }, powered.Engines[1] with { LowOilPressure = 2 } } };
        h.Tick(30);
        Assert.Empty(h.Env.Spoken);
        Assert.Equal("Upper display: primary engine page", h.Monitor.View().Upper[0]);

        h.Tick(80); // past the 10 s after the displays lit up
        s.Sample = powered; // the first change after the baseline is spoken
        h.Tick(10);
        Assert.Equal(new[] { "Upper DU alert gone: engine 1 LOW OIL PRESSURE, engine 2 LOW OIL PRESSURE" }, h.Env.Spoken);
    }

    [Fact]
    public void Displays_lighting_up_are_a_silent_baseline_for_10_s()
    {
        // Live 2026-10-24, cold and dark: PMDG's aircraft has initialised, every display unit is off (page 0),
        // A/T LIM is set (the FMC is unpowered) and the empty tanks read LOW. None of it is shown, so none
        // of it is said; nor is what settles in the first seconds after the displays light up.
        var dark = Idle with { UpperPage = 0, LowerPage = 0, AutothrottleLimit = true, FuelWarning = new[] { 0, 1, 1 } };
        var h = New();
        h.Env.Locate = () => Found(h.Env.NewSampler(dark));
        h.Start();
        var s = h.RunUntilRunning();
        s.Sample = dark with { AutothrottleLimit = false };
        h.Tick(20);
        Assert.Empty(h.Env.Spoken); // dark: nothing
        Assert.Equal(new[] { "Upper display: off" }, h.Monitor.View().Upper);

        s.Sample = Idle with { AutothrottleLimit = true, FuelWarning = new[] { 0, 1, 1 } }; // the displays light up
        h.Tick(20);
        s.Sample = Idle; // the FMC and the fuel indication come up a moment later
        h.Tick(80);
        Assert.Empty(h.Env.Spoken);

        s.Sample = StartValve(Idle); // 10 s after lighting up, changes speak again
        h.Tick(10);
        Assert.Equal(new[] { StartValveLine }, h.Env.Spoken);
    }

    [Fact]
    public void A_reader_starting_on_lit_displays_speaks_at_once()
    {
        // The quiet time follows the displays lighting up, not every start: a reload in flight speaks the
        // first change after its baseline as before.
        var h = New().Start();
        var s = h.RunUntilRunning();
        s.Sample = StartValve(Idle);
        h.Tick(5);
        Assert.Equal(new[] { StartValveLine }, h.Env.Spoken);
    }

    [Fact]
    public void View_running_shows_both_lists_and_the_sample()
    {
        var h = New().Start();
        h.RunUntilRunning();
        var view = h.Monitor.View();
        Assert.Equal("Upper display: primary engine page", view.Upper[0]);
        Assert.Equal("Lower display: secondary engine page", view.Lower[0]);
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
        Assert.Equal(new[] { "Engine displays starting" }, view.Upper);
        Assert.Equal(new[] { "Engine displays starting" }, view.Lower);
        Assert.Null(view.Sample);
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
        h.Tick(50);
        Assert.False(h.Monitor.KeyFallsBackToAi());
    }

    [Fact]
    public void Key_falls_back_after_10_s_stopped()
    {
        var h = New().Start();
        var s = h.RunUntilRunning();
        h.Env.Locate = () => Found();
        s.Fail = true;
        h.Tick(101);
        Assert.True(h.Monitor.KeyFallsBackToAi());

        var m = New().Start();
        m.RunUntilRunning();
        m.Env.Sdk = Sdk with { FuelRightLb = 1 };
        m.Tick(21); // stopped on the mismatch
        Assert.False(m.Monitor.KeyFallsBackToAi());
        m.Tick(100);
        Assert.True(m.Monitor.KeyFallsBackToAi());
    }

    [Fact]
    public void Key_falls_back_when_unavailable()
    {
        var h = New();
        h.Env.Analyse = () => Task.FromResult(new EngineDisplayAnalysis(Array.Empty<Pmdg737EngineDisplayLayout>(), FmaReasons.FileNotRecognised));
        h.Start();
        h.Tick(5);
        Assert.Equal(new[] { Pmdg737DisplaySpeech.FilesNotLoaded }, h.Env.Spoken); // the four readers' one line (WASM-29)
        Assert.True(h.Monitor.KeyFallsBackToAi());
        Assert.Equal(new[] { "Engine displays unavailable: PMDG file not recognised" }, h.Monitor.View().Upper);
    }

    [Fact]
    public void A_layout_missing_from_a_loaded_file_says_its_own_line()
    {
        // A layout file loaded, but this reader's layout in it is a failure: its own line, as before, and the four
        // readers' shared line is left unused (WASM-29). Alt+E still goes straight to the AI read.
        var h = New();
        h.Env.Analyse = () => Task.FromResult(
            new EngineDisplayAnalysis(Array.Empty<Pmdg737EngineDisplayLayout>(), FmaReasons.FileNotRecognised) { LayoutFileLoaded = true });
        h.Start();
        h.Tick(5);
        Assert.Equal(new[] { "Engine display announcements unavailable: PMDG file not recognised" }, h.Env.Spoken);
        Assert.Equal("Unavailable", h.State);
        Assert.True(h.Monitor.KeyFallsBackToAi());
        var later = new List<string>();
        h.Env.FilesNotice.Say(muted: false, later.Add);
        Assert.Equal(new[] { Pmdg737DisplaySpeech.FilesNotLoaded }, later); // not used up
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

    private static Task<EngineDisplayLocateResult> Found(params IEngineDisplaySampler[] candidates) =>
        Task.FromResult(new EngineDisplayLocateResult(candidates, null));

    private sealed class Harness
    {
        public Harness(FakeEnvironment env) => Env = env;

        public FakeEnvironment Env { get; }
        public Pmdg737EngineDisplayMonitor Monitor { get; } = new();
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
                Env.NowMs += 100;
            }
        }

        public FakeSampler RunUntilRunning(int maxTicks = 300)
        {
            for (int i = 0; i < maxTicks && State != "Running"; i++) Tick();
            Assert.True(State == "Running", $"not running within {maxTicks} ticks; state {State}");
            return Env.Samplers.Single(s => !s.Disposed);
        }
    }

    /// <summary>Also drives the reader in <see cref="Pmdg737EngineDisplaysWiringTests"/>.</summary>
    internal sealed class FakeSampler : IEngineDisplaySampler
    {
        public FakeSampler(EngineDisplaySample sample) => Sample = sample;

        public Pmdg737EngineDisplayLayout Layout => Pmdg737EngineDisplayMonitorTests.Layout;
        public EngineDisplaySample Sample { get; set; }
        public Queue<EngineDisplaySample> Script { get; } = new();
        public bool Fail { get; set; }
        public int DisposeCount { get; private set; }
        public bool Disposed => DisposeCount > 0;

        public bool TryRead(out EngineDisplaySample sample)
        {
            if (Disposed) throw new ObjectDisposedException(nameof(FakeSampler));
            sample = Fail ? null! : Script.Count > 0 ? Script.Dequeue() : Sample;
            return !Fail;
        }

        public void Dispose() => DisposeCount++;
    }

    /// <summary>Also drives the reader in <see cref="Pmdg737EngineDisplaysWiringTests"/>.</summary>
    internal sealed class FakeEnvironment : IEngineDisplayEnvironment
    {
        public FakeEnvironment() => Locate = () => Found(NewSampler());

        public long NowMs { get; set; }
        public EngineDisplaySdkSnapshot? Sdk { get; set; } = Pmdg737EngineDisplayMonitorTests.Sdk;
        public HashSet<EngineDisplayMuteRow> Muted { get; } = new();
        public Pmdg737DisplayFilesNotice FilesNotice { get; init; } = new();
        public Func<Task<EngineDisplayAnalysis>> Analyse { get; set; } =
            () => Task.FromResult(new EngineDisplayAnalysis(new[] { Layout }, null));
        public Func<Task<EngineDisplayLocateResult>> Locate { get; set; }
        public int AnalyseCalls { get; private set; }
        public List<FakeSampler> Samplers { get; } = new();
        public List<string> SpokenList { get; } = new();
        public string[] Spoken => SpokenList.ToArray();
        public List<string> Logged { get; } = new();

        public FakeSampler NewSampler(EngineDisplaySample? sample = null)
        {
            var s = new FakeSampler(sample ?? Idle);
            Samplers.Add(s);
            return s;
        }

        public EngineDisplaySdkSnapshot? ReadSdk() => Sdk;
        public bool IsMuted(EngineDisplayMuteRow row) => Muted.Contains(row);
        public void Speak(string text) => SpokenList.Add(text);
        public void Log(string line) => Logged.Add(line);

        public Task<EngineDisplayAnalysis> AnalyseAsync()
        {
            AnalyseCalls++;
            return Analyse();
        }

        public Task<EngineDisplayLocateResult> LocateAsync(IReadOnlyList<Pmdg737EngineDisplayLayout> layouts) => Locate();
    }
}

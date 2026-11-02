// The one line the PMDG 737's four display readers share when no layout file loaded (the module analysis has no
// builds; WASM-29): said once per module session by the first reader to get there whose own announcements are not
// muted, never again after a flight reload, and once more in a new session. A reader unavailable for another reason,
// its own layout missing from a file that loaded included, keeps its own line. Four real monitors on fake environments
// sharing one Pmdg737DisplayFilesNotice, as StartPmdg737Displays wires them, ticked in the order FMA, engine displays,
// ND, PFD. The fakes' analyses below model no builds (LayoutFileLoaded false) unless a test says otherwise.

using MSFSBlindAssist.Services.PMDG;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737DisplayFilesNoticeTests
{
    private const string Line = "PMDG display files could not be loaded; the display keys fall back to AI reading.";

    [Fact]
    public void The_line_is_the_owner_s_text() => Assert.Equal(Line, Pmdg737DisplaySpeech.FilesNotLoaded);

    [Fact]
    public void Four_readers_with_no_layout_say_one_line()
    {
        var r = new Readers();
        r.StartSession();
        r.Tick(2);

        Assert.Equal(new[] { ("FMA", Line) }, r.Spoken);
        Assert.All(r.States, s => Assert.Equal("Unavailable", s));
        // Each reader still logs its own reason, and its window still says why (window text is not speech).
        Assert.All(r.Logs, log => Assert.Contains("Analysing -> Unavailable: PMDG file not recognised", log));
        Assert.Equal("FMA announcements unavailable: PMDG file not recognised", r.Fma.DescribeForReadout());
        Assert.Equal(new[] { "Engine displays unavailable: PMDG file not recognised" }, r.Engine.View().Upper);
        Assert.Equal(new[] { "Navigation display unavailable: PMDG file not recognised" }, r.Nd.View().Lines);
        Assert.Equal(new[] { "Primary flight display unavailable: PMDG file not recognised" }, r.Pfd.View(null, "").Lines);
        r.Stop();
    }

    [Theory]
    [InlineData("FMA", "engine displays")]
    [InlineData("FMA;engine displays", "ND")]
    [InlineData("FMA;engine displays;ND", "PFD")]
    [InlineData("engine displays", "FMA")]
    public void A_muted_reader_leaves_the_line_to_the_next(string muted, string speaker)
    {
        var r = new Readers();
        r.StartSession(muted.Split(';'));
        r.Tick(2);

        Assert.Equal(new[] { (speaker, Line) }, r.Spoken);
        r.Stop();
    }

    [Fact]
    public void The_engine_displays_follow_their_alerts_row()
    {
        // The line is a status line, and the engine display's status lines follow the alerts row: its colours and
        // thrust mode rows muted leave it speaking.
        var r = new Readers();
        r.StartSession(new[] { "FMA" }, engineRowsMuted: new[] { EngineDisplayMuteRow.Colors, EngineDisplayMuteRow.ThrustMode });
        r.Tick(2);

        Assert.Equal(new[] { ("engine displays", Line) }, r.Spoken);
        r.Stop();
    }

    [Fact]
    public void All_four_muted_say_nothing()
    {
        var r = new Readers();
        r.StartSession(new[] { "FMA", "engine displays", "ND", "PFD" });
        r.Tick(2);

        Assert.Empty(r.Spoken);
        Assert.All(r.States, s => Assert.Equal("Unavailable", s));
        r.Stop();
    }

    [Fact]
    public void A_flight_reload_never_repeats_it()
    {
        var r = new Readers();
        r.StartSession();
        r.Tick(2);
        Assert.Single(r.Spoken);

        r.Restart(); // a flight reload: the same module session, the analysis run again
        Assert.All(r.States, s => Assert.NotEqual("Unavailable", s));
        r.Tick(2);

        Assert.All(r.States, s => Assert.Equal("Unavailable", s));
        Assert.Single(r.Spoken);
        r.Stop();
    }

    [Fact]
    public void A_new_session_says_it_again()
    {
        // An aircraft switch away and back (or an app restart) stops the readers and starts them on a new module
        // session, with a new notice.
        var r = new Readers();
        r.StartSession();
        r.Tick(2);
        r.Stop();

        r.StartSession();
        r.Tick(2);

        Assert.Equal(new[] { ("FMA", Line), ("FMA", Line) }, r.Spoken);
        r.Stop();
    }

    [Fact]
    public void Another_reason_keeps_the_reader_s_own_line()
    {
        // The FMA's analysis faults (an internal error): it says its own line, as before; the other three have no
        // layout and share the one line.
        var r = new Readers();
        r.StartSession(fmaAnalysis: () => Task.FromException<FmaAnalysis>(new IOException("boom")));
        r.Tick(2);

        Assert.Equal(new[]
        {
            ("FMA", "FMA announcements unavailable: internal error, see the log"),
            ("engine displays", Line),
        }, r.Spoken);
        r.Stop();
    }

    [Fact]
    public void A_reader_missing_from_a_loaded_file_says_its_own_line_and_the_others_read()
    {
        // The module has a build (a layout file loaded) whose FMA layout failed while the other three derived theirs:
        // the files did load, so the FMA says its own line as before, the shared line is neither said nor used up,
        // and the other three read.
        var notice = new Pmdg737DisplayFilesNotice();
        var spoken = new List<(string Reader, string Text)>();
        var fmaEnv = new FmaEnv("FMA", notice, spoken)
        {
            Analyse = () => Task.FromResult(
                new FmaAnalysis(Array.Empty<Pmdg737FmaLayout>(), FmaReasons.FileNotRecognised) { LayoutFileLoaded = true }),
        };
        var engineEnv = new Pmdg737EngineDisplayMonitorTests.FakeEnvironment { FilesNotice = notice };
        var ndEnv = new Pmdg737NavDisplayMonitorTests.FakeEnvironment { FilesNotice = notice };
        var pfdEnv = new Pmdg737PfdMonitorTests.FakeEnvironment { FilesNotice = notice };
        var fma = new Pmdg737FmaMonitor();
        var engine = new Pmdg737EngineDisplayMonitor();
        var nd = new Pmdg737NavDisplayMonitor();
        var pfd = new Pmdg737PfdMonitor();
        fma.Start(fmaEnv, startTimer: false);
        engine.Start(engineEnv, startTimer: false);
        nd.Start(ndEnv, startTimer: false);
        pfd.Start(pfdEnv, startTimer: false);
        string[] Running() => new[] { engine.StateName, nd.StateName, pfd.StateName };
        for (int i = 0; i < 50 && Running().Any(s => s != "Running"); i++)
        {
            fma.Tick();
            engine.Tick();
            engineEnv.NowMs += Pmdg737EngineDisplayMonitor.TickMs;
            nd.Tick();
            ndEnv.NowMs += Pmdg737NavDisplayMonitor.TickMs;
            pfd.Tick();
            pfdEnv.NowMs += Pmdg737PfdMonitor.TickMs;
        }

        Assert.Equal("Unavailable", fma.StateName);
        Assert.Equal(new[] { "Running", "Running", "Running" }, Running());
        Assert.Equal(new[] { ("FMA", "FMA announcements unavailable: PMDG file not recognised") }, spoken);
        Assert.Empty(engineEnv.Spoken);
        Assert.Empty(ndEnv.Spoken);
        Assert.Empty(pfdEnv.Spoken);
        var later = new List<string>();
        notice.Say(muted: false, later.Add);
        Assert.Equal(new[] { Line }, later); // not used up
        fma.Stop();
        engine.Stop();
        nd.Stop();
        pfd.Stop();
    }

    /// <summary>The four readers, each on its own fake environment; every session's environments share one notice.</summary>
    private sealed class Readers
    {
        private readonly List<(string Reader, string Text)> _spoken = new();
        private readonly List<FakeBase> _envs = new();

        public Pmdg737FmaMonitor Fma { get; } = new();
        public Pmdg737EngineDisplayMonitor Engine { get; } = new();
        public Pmdg737NavDisplayMonitor Nd { get; } = new();
        public Pmdg737PfdMonitor Pfd { get; } = new();

        public (string Reader, string Text)[] Spoken => _spoken.ToArray();
        public string[] States => new[] { Fma.StateName, Engine.StateName, Nd.StateName, Pfd.StateName };
        public IEnumerable<List<string>> Logs => _envs.Select(e => e.Logged);

        /// <summary>A new module session, as StartPmdg737Displays makes one: a new notice, new environments.</summary>
        public void StartSession(IReadOnlyCollection<string>? muted = null, IReadOnlyCollection<EngineDisplayMuteRow>? engineRowsMuted = null,
            Func<Task<FmaAnalysis>>? fmaAnalysis = null)
        {
            muted ??= Array.Empty<string>();
            var notice = new Pmdg737DisplayFilesNotice();
            var fma = new FmaEnv("FMA", notice, _spoken) { Muted = muted.Contains("FMA") };
            if (fmaAnalysis is not null) fma.Analyse = fmaAnalysis;
            var engine = new EngineEnv("engine displays", notice, _spoken);
            if (muted.Contains("engine displays")) engine.MutedRows.Add(EngineDisplayMuteRow.Alerts);
            foreach (var row in engineRowsMuted ?? Array.Empty<EngineDisplayMuteRow>()) engine.MutedRows.Add(row);
            var nd = new NdEnv("ND", notice, _spoken) { Muted = muted.Contains("ND") };
            var pfd = new PfdEnv("PFD", notice, _spoken) { Muted = muted.Contains("PFD") };
            _envs.Clear();
            _envs.AddRange(new FakeBase[] { fma, engine, nd, pfd });
            Fma.Start(fma, startTimer: false);
            Engine.Start(engine, startTimer: false);
            Nd.Start(nd, startTimer: false);
            Pfd.Start(pfd, startTimer: false);
        }

        public void Tick(int count = 1)
        {
            for (int i = 0; i < count; i++)
            {
                Fma.Tick();
                Engine.Tick();
                Nd.Tick();
                Pfd.Tick();
            }
        }

        public void Restart()
        {
            Fma.Restart();
            Engine.Restart();
            Nd.Restart();
            Pfd.Restart();
        }

        public void Stop()
        {
            Fma.Stop();
            Engine.Stop();
            Nd.Stop();
            Pfd.Stop();
        }
    }

    /// <summary>What the four fakes share: the notice, the speech every reader adds to (with its name), and the log.</summary>
    private abstract class FakeBase(string name, Pmdg737DisplayFilesNotice notice, List<(string Reader, string Text)> spoken)
    {
        public long NowMs => 0;
        public Pmdg737DisplayFilesNotice FilesNotice => notice;
        public List<string> Logged { get; } = new();
        public void Speak(string text) => spoken.Add((name, text));
        public void Log(string line) => Logged.Add(line);
    }

    private sealed class FmaEnv(string name, Pmdg737DisplayFilesNotice notice, List<(string, string)> spoken)
        : FakeBase(name, notice, spoken), IFmaEnvironment
    {
        public bool Muted { get; set; }
        public Func<Task<FmaAnalysis>> Analyse { get; set; } =
            () => Task.FromResult(new FmaAnalysis(Array.Empty<Pmdg737FmaLayout>(), FmaReasons.FileNotRecognised));
        public bool IsMuted => Muted;
        public FmaSdkSnapshot? ReadSdk() => new FmaSdkSnapshot(true, true, 10000, 90);
        public Task<FmaAnalysis> AnalyseAsync() => Analyse();
        public Task<FmaLocateResult> LocateAsync(IReadOnlyList<Pmdg737FmaLayout> layouts) =>
            Task.FromResult(new FmaLocateResult(Array.Empty<IFmaSampler>(), null));
    }

    private sealed class EngineEnv(string name, Pmdg737DisplayFilesNotice notice, List<(string, string)> spoken)
        : FakeBase(name, notice, spoken), IEngineDisplayEnvironment
    {
        public HashSet<EngineDisplayMuteRow> MutedRows { get; } = new();
        public bool IsMuted(EngineDisplayMuteRow row) => MutedRows.Contains(row);
        public EngineDisplaySdkSnapshot? ReadSdk() => new EngineDisplaySdkSnapshot(0, 0, 0);
        public Task<EngineDisplayAnalysis> AnalyseAsync() =>
            Task.FromResult(new EngineDisplayAnalysis(Array.Empty<Pmdg737EngineDisplayLayout>(), FmaReasons.FileNotRecognised));
        public Task<EngineDisplayLocateResult> LocateAsync(IReadOnlyList<Pmdg737EngineDisplayLayout> layouts) =>
            Task.FromResult(new EngineDisplayLocateResult(Array.Empty<IEngineDisplaySampler>(), null));
    }

    private sealed class NdEnv(string name, Pmdg737DisplayFilesNotice notice, List<(string, string)> spoken)
        : FakeBase(name, notice, spoken), INavDisplayEnvironment
    {
        public bool Muted { get; set; }
        public bool IsMuted() => Muted;
        public NavDisplaySdkSnapshot? ReadSdk() => new NavDisplaySdkSnapshot(2, 2);
        public Task<NavDisplayAnalysis> AnalyseAsync() =>
            Task.FromResult(new NavDisplayAnalysis(Array.Empty<Pmdg737NavDisplayLayout>(), FmaReasons.FileNotRecognised));
        public Task<NavDisplayLocateResult> LocateAsync(IReadOnlyList<Pmdg737NavDisplayLayout> layouts) =>
            Task.FromResult(new NavDisplayLocateResult(Array.Empty<INavDisplaySampler>(), null));
    }

    private sealed class PfdEnv(string name, Pmdg737DisplayFilesNotice notice, List<(string, string)> spoken)
        : FakeBase(name, notice, spoken), IPfdEnvironment
    {
        public bool Muted { get; set; }
        public bool IsMuted() => Muted;
        public PfdSimSnapshot? ReadSimulator() => new PfdSimSnapshot(161, 5010);
        public Task<PfdAnalysis> AnalyseAsync() =>
            Task.FromResult(new PfdAnalysis(Array.Empty<Pmdg737PfdLayout>(), FmaReasons.FileNotRecognised));
        public Task<PfdLocateResult> LocateAsync(IReadOnlyList<Pmdg737PfdLayout> layouts) =>
            Task.FromResult(new PfdLocateResult(Array.Empty<IPfdSampler>(), null));
    }
}

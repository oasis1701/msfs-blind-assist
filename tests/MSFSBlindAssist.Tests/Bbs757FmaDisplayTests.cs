// The 757 FMA as one reader of the 757's display monitor: its names, keys and log, its layouts and samplers from the
// shared module session, no simulator input and no power state, and its words; then the monitor driving it over synthetic
// module memory, so a change BlueBird writes is spoken as the pilot would hear it.

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Bbs757FmaDisplayTests
{
    private static readonly Bbs757FmaLayout Layout = Bbs757FmaReaderTests.Layout;

    private static Bbs757FmaDisplay Display(List<string>? log = null) => new(line => log?.Add(line));

    private static IBbs757DisplayReader<Bbs757FmaLayout, Bbs757FmaReading> Reader(List<string>? log = null) => Display(log);

    private static ModuleBuild Build(object? fma, string layoutFile = "BBS752.json") =>
        new(Bbs757Module.Description, "BBS752.wasm", layoutFile,
            fma is null ? Array.Empty<ReaderResult>() : new[] { new ReaderResult(Bbs757Module.Fma, fma, null) });

    private sealed class CountingHandle : IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;
    }

    /// <summary>A build located over <paramref name="bytes"/>, on a handle whose first lease (the session's) is
    /// <paramref name="sessionLease"/>.</summary>
    private static LocatedModule Located(ModuleBuild build, byte[] bytes, CountingHandle handle, out IDisposable sessionLease)
    {
        var shared = SharedHandle.Open(handle, out sessionLease);
        var memory = new ModuleMemory(Bbs757FmaReaderTests.Source(bytes), Bbs757FmaReaderTests.Base);
        return new LocatedModule(build, new ModuleAnchor(Layout.AnchorAddress, Layout.Anchor), memory, shared);
    }

    [Fact]
    public void The_reader_is_named_fma_on_its_own_row_and_log()
    {
        Assert.Equal("FMA", Bbs757FmaDisplay.Name);
        Assert.Equal("MSFSBA_BBS757_FMA", Bbs757FmaDisplay.MuteKey);
        Assert.Equal("bbs757-fma", Bbs757FmaDisplay.LogChannel);
        Assert.Equal(100, Bbs757FmaDisplay.TickMs);
        Assert.Equal("Flight Mode Annunciator", Bbs757FmaDisplay.MonitorRowLabel);

        var reader = Reader();
        Assert.Equal("FMA", reader.Name);
        Assert.Equal("MSFSBA_BBS757_FMA", reader.MuteKey);
        Assert.Equal("bbs757-fma", reader.LogChannel);
        Assert.Equal(100, reader.TickMs);
    }

    [Fact]
    public void The_fma_takes_no_simulator_input()
    {
        // B757-30: its F/D switch and MCP target gate is gone; the FMA reads memory alone.
        Assert.Empty(Reader().SimInputs);
        Assert.Empty(new Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading>(Display()).SimInputs);
    }

    [Fact]
    public void The_fma_has_no_power_state()
    {
        var reading = Bbs757FmaReader.Decode(Layout, Bbs757FmaSpeechTests.Cruise);
        Assert.Null(Reader().Lit(reading));
        Assert.Null(Reader().Lit(default));
    }

    [Fact]
    public void Layouts_are_each_builds_fma_layout_once_in_file_order()
    {
        var other = Layout with { Pitch = 11928 };
        var analysis = new ModuleAnalysis(new[]
        {
            Build(Layout, "a.json"), Build(null, "b.json"), Build(other, "c.json"), Build(Layout with { SourceFile = "x" }, "d.json"),
        }, null);
        Assert.Equal(new[] { Layout, other }, Reader().LayoutsIn(analysis));
        Assert.Empty(Reader().LayoutsIn(new ModuleAnalysis(Array.Empty<ModuleBuild>(), "BlueBird 757 files not found")));
    }

    [Fact]
    public void A_sampler_reads_the_builds_own_layout_on_its_own_lease()
    {
        var handle = new CountingHandle();
        var module = Located(Build(Layout), Bbs757FmaReaderTests.Memory(), handle, out var sessionLease);

        // Another build's layout: no sampler, and no lease taken.
        Assert.Null(Reader().SamplerFor(module, Layout with { Pitch = 11928 }));

        var sampler = Reader().SamplerFor(module, Layout);
        Assert.NotNull(sampler);
        Assert.True(sampler!.TryRead(out var reading));
        Assert.Equal("SPD", reading.AutoThrottle);
        Assert.Equal(35000, reading.McpAltitude);

        // The sampler's lease keeps the handle open after the session lets go; disposing it closes the handle.
        sessionLease.Dispose();
        Assert.Equal(0, handle.Disposed);
        sampler.Dispose();
        Assert.Equal(1, handle.Disposed);

        // A closed handle: the monitor's "the simulator handle closed during the memory search".
        Assert.Throws<ObjectDisposedException>(() => Reader().SamplerFor(module, Layout));
    }

    [Fact]
    public void A_change_is_one_line_on_the_fma_row()
    {
        var before = Bbs757FmaReader.Decode(Layout, Bbs757FmaSpeechTests.Cruise with { At = 1 });
        var after = Bbs757FmaReader.Decode(Layout, Bbs757FmaSpeechTests.Cruise with { At = 2 });
        Assert.Equal(new[] { new Bbs757SpokenLine("MSFSBA_BBS757_FMA", "FMA: A/T: EPR") }, Reader().Changes(before, after, withPrefix: true));
        Assert.Equal(new[] { new Bbs757SpokenLine("MSFSBA_BBS757_FMA", "A/T: EPR") }, Reader().Changes(before, after, withPrefix: false));
        // Only the MCP moved: nothing the FMA draws changed, so nothing is said.
        Assert.Empty(Reader().Changes(before, before with { McpAltitude = 12000 }, withPrefix: true));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    [InlineData("en-US")]
    public void Rows_are_the_fma_speech(string culture)
    {
        var reading = Bbs757FmaReader.Decode(Bbs757FmaSpeechTests.Layout, Bbs757FmaSpeechTests.Cruise);
        Assert.Equal(new[] { "A/T: SPD", "Roll: LNAV, armed LOC", "Pitch: VNAV PTH, armed G/S", "AFDS: CMD" },
            TestCulture.Run(culture, () => Reader().Rows(reading)));
    }

    [Fact]
    public void The_monitor_speaks_an_fma_change_read_from_memory()
    {
        // The whole path: the session's build and location, the first reading that is not corrupt a silent baseline (no
        // simulator value asked for), then a change BlueBird writes (SPD to EPR) spoken once, with the "FMA: " prefix,
        // after it settles.
        var bytes = Bbs757FmaReaderTests.Memory();
        var handle = new CountingHandle();
        var build = Build(Layout);
        var located = Located(build, bytes, handle, out var sessionLease);
        var env = new Bbs757DisplayMonitorTests.FakeEnvironment
        {
            Analyse = () => Task.FromResult(new ModuleAnalysis(new[] { build }, null)),
            Locate = () => Task.FromResult(new ModuleLocation(new[] { located }, null)),
        };
        var log = new List<string>();
        var monitor = new Bbs757DisplayMonitor<Bbs757FmaLayout, Bbs757FmaReading>(Display(log));
        monitor.Start(env, startTimer: false);

        void Tick(int n)
        {
            for (int i = 0; i < n; i++)
            {
                env.NowMs += 100;
                monitor.Tick();
            }
        }

        Tick(10);
        Assert.Equal("Running", monitor.StateName);
        Assert.Empty(env.Spoken);
        Assert.Equal(new[] { "A/T: SPD", "Roll: LNAV, armed LOC", "Pitch: TO, armed G/S", "AFDS: CMD" }, monitor.View());
        Assert.Empty(env.KeysAsked);   // memory alone

        Bbs757FmaReaderTests.Put(bytes, Bbs757FmaReaderTests.Tmc + 444, 2);
        Tick(10);
        Assert.Equal(new[] { "FMA: A/T: EPR" }, env.Spoken);

        // A value with no word: spoken, logged once on the reader's own log.
        Bbs757FmaReaderTests.Put(bytes, Bbs757FmaReaderTests.Tmc + 444, 7);
        Tick(10);
        Assert.Equal(new[] { "FMA: A/T: EPR", "FMA: A/T: unrecognised" }, env.Spoken);
        Assert.Equal(new[] { "value with no word, spoken as unrecognised: A/T = 7" }, log);

        // Stopping releases the sampler's lease: once the session lets go too, the handle closes.
        monitor.Stop();
        Assert.Equal(0, handle.Disposed);
        sessionLease.Dispose();
        Assert.Equal(1, handle.Disposed);
    }
}

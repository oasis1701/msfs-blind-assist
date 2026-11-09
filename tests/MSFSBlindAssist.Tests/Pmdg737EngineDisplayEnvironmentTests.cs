// The app's side of the engine display monitor, in the parts that need no simulator: the PMDG SDK's tanks
// (never read while SimConnect is disconnected, WASM-7), and the engine display reader's view of the
// shared module session: its layouts from the analysis and its samplers from the memory search.

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737EngineDisplayEnvironmentTests
{
    private const ulong Base = 0x10000000;

    private static readonly Pmdg737EngineDisplayLayout Layout = GoldenLayouts.EngineDisplays with { SourceFile = "real" };

    private static ModuleBuild BuildOf(Pmdg737EngineDisplayLayout layout) => Pmdg737TestBuilds.Of(layout.SourceFile, engine: layout);

    private static Pmdg737EngineDisplayLayout? EngineOf(ModuleBuild build) => build.Layout<Pmdg737EngineDisplayLayout>(Pmdg737Module.EngineDisplays);

    private static double Field(string name) => name switch
    {
        "FUEL_QtyLeft" => 8626, "FUEL_QtyCenter" => 7782, "FUEL_QtyRight" => 8628, _ => double.NaN,
    };

    [Fact]
    public void Disconnected_sim_reads_no_sdk()
    {
        // The data manager keeps its last snapshot after a disconnect: the connection is checked first.
        Assert.Null(Pmdg737EngineDisplayEnvironment.Snapshot(connected: false, ready: true, Field));
        Assert.Null(Pmdg737EngineDisplayEnvironment.Snapshot(connected: true, ready: false, Field));
        Assert.Equal(new EngineDisplaySdkSnapshot(FuelLeftLb: 8626, FuelCenterLb: 7782, FuelRightLb: 8628),
            Pmdg737EngineDisplayEnvironment.Snapshot(connected: true, ready: true, Field));
    }

    [Fact]
    public void ToEngineAnalysis_keeps_distinct_engine_layouts_in_order()
    {
        var two = Layout with { SourceFile = "two", Engine = Layout.Engine with { Size = Layout.Engine.Size + 8 } };
        var failed = Pmdg737TestBuilds.Of("failed");
        var a = new ModuleAnalysis(new[] { BuildOf(Layout), failed, BuildOf(two), BuildOf(Layout with { SourceFile = "again" }) }, null);

        var engine = Pmdg737EngineDisplayEnvironment.ToEngineAnalysis(a);
        Assert.Null(engine.Failure);
        Assert.Equal(new[] { "real", "two" }, engine.Layouts.Select(l => l.SourceFile));

        Assert.True(engine.LayoutFileLoaded);

        // A layout file loaded, but this reader's layout failed in it: its own line, not the shared one (WASM-29).
        var none = Pmdg737EngineDisplayEnvironment.ToEngineAnalysis(new ModuleAnalysis(new[] { failed }, null));
        Assert.Empty(none.Layouts);
        Assert.Equal(FmaReasons.FileNotRecognised, none.Failure);
        Assert.True(none.LayoutFileLoaded);

        // No layout file loaded (no builds): the four readers' shared line.
        var missing = Pmdg737EngineDisplayEnvironment.ToEngineAnalysis(
            new ModuleAnalysis(Array.Empty<ModuleBuild>(), FmaReasons.FilesNotFound));
        Assert.Equal(FmaReasons.FilesNotFound, missing.Failure);
        Assert.False(missing.LayoutFileLoaded);
    }

    [Fact]
    public void ToEngineCandidates_makes_one_sampler_per_requested_layout_found()
    {
        var one = BuildOf(Layout);
        var two = BuildOf(Layout with { SourceFile = "two", Engine = Layout.Engine with { Size = Layout.Engine.Size + 8 } });
        var src = Source();
        var location = ModuleSession.Locate(src, new[] { one, two }, _ => { });

        var r = Pmdg737EngineDisplayEnvironment.ToEngineCandidates(location, new[] { EngineOf(two)! });
        Assert.Null(r.Failure);
        Assert.Same(EngineOf(two), Assert.Single(r.Candidates).Layout);
        Assert.IsType<ModuleEngineDisplaySampler>(r.Candidates[0]);

        location.ReleaseSessionLease();
        Assert.Equal(0, src.Disposals); // the sampler holds its own lease
        r.Candidates[0].Dispose();
        Assert.Equal(1, src.Disposals);

        var denied = Pmdg737EngineDisplayEnvironment.ToEngineCandidates(ModuleSession.NotOpened(FmaReasons.NoAccess), new[] { Layout });
        Assert.Empty(denied.Candidates);
        Assert.Equal(FmaReasons.NoAccess, denied.Failure);
    }

    [Fact]
    public void ToEngineCandidates_on_a_closed_handle_is_not_found_yet()
    {
        var one = BuildOf(Layout);
        var location = ModuleSession.Locate(Source(), new[] { one }, _ => { });
        location.ReleaseSessionLease();
        var r = Pmdg737EngineDisplayEnvironment.ToEngineCandidates(location, new[] { Layout });
        Assert.Empty(r.Candidates);
        Assert.Null(r.Failure);
    }

    /// <summary>Module memory holding the engine display anchor, counting disposals.</summary>
    private static CountingSource Source()
    {
        var bytes = new byte[Layout.AnchorAddress + 0x1000];
        Layout.Anchor.CopyTo(bytes, (int)Layout.AnchorAddress);
        return new CountingSource(new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes));
    }

    private sealed class CountingSource : IProcessMemorySource
    {
        private readonly FakeProcessMemory _inner;

        public CountingSource(FakeProcessMemory inner) => _inner = inner;

        public int Disposals { get; private set; }
        public IEnumerable<MemoryRegion> Regions() => _inner.Regions();
        public MemoryRegion? RegionAt(ulong address) => _inner.RegionAt(address);
        public bool TryRead(ulong address, Span<byte> into) => _inner.TryRead(address, into);
        public void Dispose() => Disposals++;
    }
}

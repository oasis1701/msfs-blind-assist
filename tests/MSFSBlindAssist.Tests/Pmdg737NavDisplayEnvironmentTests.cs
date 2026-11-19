// The app's side of the navigation display monitor, in the parts that need no simulator: the PMDG SDK's
// Captain EFIS mode and range (never read while SimConnect is disconnected, WASM-7), and the ND reader's
// view of the shared module session: its layouts from the analysis and its samplers from the memory search.

using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737NavDisplayEnvironmentTests
{
    private const ulong Base = 0x10000000;

    private static readonly Pmdg737NavDisplayLayout Layout = GoldenLayouts.NavDisplay with { SourceFile = "real" };

    private static ModuleBuild BuildOf(Pmdg737NavDisplayLayout layout) => Pmdg737TestBuilds.Of(layout.SourceFile, nd: layout);

    private static Pmdg737NavDisplayLayout? NdOf(ModuleBuild build) => build.Layout<Pmdg737NavDisplayLayout>(Pmdg737Module.NavDisplay);

    /// <summary>A second, different build's layout.</summary>
    private static Pmdg737NavDisplayLayout Two => Layout with { SourceFile = "two", Objects = Layout.Objects with { Fms = Layout.Objects.Fms with { Size = Layout.Objects.Fms.Size + 8 } } };

    private static double Field(string name) => name switch
    {
        "EFIS_ModeSel_0" => 2, "EFIS_RangeSel_0" => 3, _ => double.NaN,
    };

    [Fact]
    public void Disconnected_sim_reads_no_sdk()
    {
        // The data manager keeps its last snapshot after a disconnect: the connection is checked first.
        Assert.Null(Pmdg737NavDisplayEnvironment.Snapshot(connected: false, ready: true, Field));
        Assert.Null(Pmdg737NavDisplayEnvironment.Snapshot(connected: true, ready: false, Field));
        Assert.Equal(new NavDisplaySdkSnapshot(ModeSel: 2, RangeSel: 3), Pmdg737NavDisplayEnvironment.Snapshot(connected: true, ready: true, Field));
    }

    [Fact]
    public void ToNavAnalysis_keeps_distinct_nd_layouts_in_order()
    {
        var failed = Pmdg737TestBuilds.Of("failed");
        var a = new ModuleAnalysis(new[] { BuildOf(Layout), failed, BuildOf(Two), BuildOf(Layout with { SourceFile = "again" }) }, null);

        var nd = Pmdg737NavDisplayEnvironment.ToNavAnalysis(a);
        Assert.Null(nd.Failure);
        Assert.Equal(new[] { "real", "two" }, nd.Layouts.Select(l => l.SourceFile));

        Assert.True(nd.LayoutFileLoaded);

        // A layout file loaded, but this reader's layout failed in it: its own line, not the shared one (WASM-29).
        var none = Pmdg737NavDisplayEnvironment.ToNavAnalysis(new ModuleAnalysis(new[] { failed }, null));
        Assert.Empty(none.Layouts);
        Assert.Equal(FmaReasons.FileNotRecognised, none.Failure);
        Assert.True(none.LayoutFileLoaded);

        // No layout file loaded (no builds): the four readers' shared line.
        var missing = Pmdg737NavDisplayEnvironment.ToNavAnalysis(new ModuleAnalysis(Array.Empty<ModuleBuild>(), FmaReasons.FilesNotFound));
        Assert.Equal(FmaReasons.FilesNotFound, missing.Failure);
        Assert.False(missing.LayoutFileLoaded);
    }

    [Fact]
    public void ToNavCandidates_makes_one_sampler_per_requested_layout_found()
    {
        var one = BuildOf(Layout);
        var two = BuildOf(Two);
        var src = Source();
        var location = ModuleSession.Locate(src, new[] { one, two }, _ => { });

        var r = Pmdg737NavDisplayEnvironment.ToNavCandidates(location, new[] { NdOf(two)! });
        Assert.Null(r.Failure);
        Assert.Same(NdOf(two), Assert.Single(r.Candidates).Layout);
        Assert.IsType<ModuleNavDisplaySampler>(r.Candidates[0]);

        location.ReleaseSessionLease();
        Assert.Equal(0, src.Disposals); // the sampler holds its own lease
        r.Candidates[0].Dispose();
        Assert.Equal(1, src.Disposals);

        var denied = Pmdg737NavDisplayEnvironment.ToNavCandidates(ModuleSession.NotOpened(FmaReasons.NoAccess), new[] { Layout });
        Assert.Empty(denied.Candidates);
        Assert.Equal(FmaReasons.NoAccess, denied.Failure);
    }

    [Fact]
    public void ToNavCandidates_on_a_closed_handle_is_not_found_yet()
    {
        var location = ModuleSession.Locate(Source(), new[] { BuildOf(Layout) }, _ => { });
        location.ReleaseSessionLease();
        var r = Pmdg737NavDisplayEnvironment.ToNavCandidates(location, new[] { Layout });
        Assert.Empty(r.Candidates);
        Assert.Null(r.Failure);
    }

    /// <summary>Module memory holding the ND anchor, counting disposals.</summary>
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

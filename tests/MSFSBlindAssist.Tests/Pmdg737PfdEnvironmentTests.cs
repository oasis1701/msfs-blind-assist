// The app's side of the PFD monitor, in the parts that need no simulator: the simulator's airspeed and altitude from
// the app's cache (nothing while SimConnect is disconnected, a value not delivered yet null, never 0), and the PFD
// reader's view of the shared module session: its layouts from the analysis and its samplers from the memory search.

using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class Pmdg737PfdEnvironmentTests
{
    private const ulong Base = 0x10000000;

    private static readonly Pmdg737PfdLayout Layout = Pmdg737PfdTestMemory.Layout;

    private static ModuleBuild BuildOf(Pmdg737PfdLayout layout) => Pmdg737TestBuilds.Of(layout.SourceFile, pfd: layout);

    private static Pmdg737PfdLayout? PfdOf(ModuleBuild build) => build.Layout<Pmdg737PfdLayout>(Pmdg737Module.Pfd);

    /// <summary>A second, different build's layout.</summary>
    private static Pmdg737PfdLayout Two => Layout with { SourceFile = "two", Objects = Layout.Objects with { Glass = Layout.Objects.Glass with { Size = Layout.Objects.Glass.Size + 8 } } };

    private static double? Cached(string key) => key switch
    {
        PMDG737Definition.PfdCheckIasKey => 160.4,
        "INDICATED_ALTITUDE" => 5012,
        _ => null,
    };

    [Fact]
    public void Disconnected_sim_reads_no_simulator_values()
    {
        // The cache keeps the last values after a disconnect: the connection is checked first.
        Assert.Null(Pmdg737PfdEnvironment.Snapshot(connected: false, Cached));
        Assert.Equal(new PfdSimSnapshot(160.4, 5012), Pmdg737PfdEnvironment.Snapshot(connected: true, Cached));
    }

    [Fact]
    public void Cache_miss_is_null_not_zero()
    {
        Assert.Equal(new PfdSimSnapshot(null, 5012),
            Pmdg737PfdEnvironment.Snapshot(connected: true, key => key == PMDG737Definition.PfdCheckIasKey ? null : Cached(key)));
        Assert.Equal(new PfdSimSnapshot(null, null), Pmdg737PfdEnvironment.Snapshot(connected: true, _ => null));
    }

    [Fact]
    public void ToPfdAnalysis_and_ToPfdCandidates()
    {
        var failed = Pmdg737TestBuilds.Of("failed");
        var a = new ModuleAnalysis(new[] { BuildOf(Layout), failed, BuildOf(Two), BuildOf(Layout with { SourceFile = "again" }) }, null);
        var pfd = Pmdg737PfdEnvironment.ToPfdAnalysis(a);
        Assert.Null(pfd.Failure);
        Assert.Equal(new[] { Layout.SourceFile, "two" }, pfd.Layouts.Select(l => l.SourceFile));
        Assert.True(pfd.LayoutFileLoaded);
        // A layout file loaded, but this reader's layout failed in it: its own line, not the shared one (WASM-29).
        var ownFailed = Pmdg737PfdEnvironment.ToPfdAnalysis(new ModuleAnalysis(new[] { failed }, null));
        Assert.Equal(FmaReasons.FileNotRecognised, ownFailed.Failure);
        Assert.True(ownFailed.LayoutFileLoaded);
        // No layout file loaded (no builds): the four readers' shared line.
        var missing = Pmdg737PfdEnvironment.ToPfdAnalysis(new ModuleAnalysis(Array.Empty<ModuleBuild>(), FmaReasons.FilesNotFound));
        Assert.Equal(FmaReasons.FilesNotFound, missing.Failure);
        Assert.False(missing.LayoutFileLoaded);

        var one = BuildOf(Layout);
        var two = BuildOf(Two);
        var src = Source();
        var location = ModuleSession.Locate(src, new[] { one, two }, _ => { });
        var r = Pmdg737PfdEnvironment.ToPfdCandidates(location, new[] { PfdOf(two)! });
        Assert.Null(r.Failure);
        Assert.Same(PfdOf(two), Assert.Single(r.Candidates).Layout);
        Assert.IsType<ModulePfdSampler>(r.Candidates[0]);
        location.ReleaseSessionLease();
        Assert.Equal(0, src.Disposals);   // the sampler holds its own lease
        r.Candidates[0].Dispose();
        Assert.Equal(1, src.Disposals);

        var denied = Pmdg737PfdEnvironment.ToPfdCandidates(ModuleSession.NotOpened(FmaReasons.NoAccess), new[] { Layout });
        Assert.Empty(denied.Candidates);
        Assert.Equal(FmaReasons.NoAccess, denied.Failure);

        // A handle closed meanwhile (a reload racing the search) is not found yet.
        var closed = ModuleSession.Locate(Source(), new[] { BuildOf(Layout) }, _ => { });
        closed.ReleaseSessionLease();
        var none = Pmdg737PfdEnvironment.ToPfdCandidates(closed, new[] { Layout });
        Assert.Empty(none.Candidates);
        Assert.Null(none.Failure);
    }

    /// <summary>Module memory holding the PFD anchor, counting disposals.</summary>
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

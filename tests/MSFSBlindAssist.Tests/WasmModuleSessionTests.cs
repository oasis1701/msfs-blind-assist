// The shared module session: one file analysis and one memory search for every reader of a WASM module's
// memory (here the PMDG 737's). Covers how the installed files become builds through the layout source (and
// what the log says about each), how one simulator handle is shared by the session and every reader and
// closed exactly once, the "no access" mapping, when a search is joined, reused or run again, the FMA's view
// of the shared results, and the background runner that always completes its task.

using System.Security.Cryptography;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class WasmModuleSessionTests
{
    /// <summary>The FMA fixture's saved layout file: its FMA derived, the other three readers failed.</summary>
    private static readonly LayoutFile Real = GoldenLayouts.Load("fma");

    private static readonly LayoutFile OtherBuild = OtherBuildOf(Real, Pmdg737Module.Fma, l => ((Pmdg737FmaLayout)l) with { ObjectSize = 20000 });

    private static ModuleAnalysis Analyse(IReadOnlyList<string> files, Func<string, LayoutLookup> find, List<string> log) =>
        ModuleSession.Analyse(files, new FakeLayoutSource(find), log.Add);

    /// <summary>What the layout store answers for an installed file whose build saved <paramref name="file"/>: each
    /// layout filled in with the installed file, the build named by its layout file.</summary>
    private static LayoutLookup Found(LayoutFile file, string wasmFile)
    {
        var module = Pmdg737Module.Description;
        var readers = file.Readers
            .Select(r => r.Layout is null ? r : r with { Layout = module.Reader(r.Reader).WithSourceFile(r.Layout, wasmFile) })
            .ToList();
        return new LayoutLookup(new ModuleBuild(module, wasmFile, file.FileName, readers), null);
    }

    /// <summary>Another build's layout file: <paramref name="file"/> with one reader's layout changed, under another code
    /// hash (so another file name), as a PMDG update that moved that reader's fields would be saved.</summary>
    private static LayoutFile OtherBuildOf(LayoutFile file, string reader, Func<object, object> change) => file with
    {
        CodeHash = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(file.CodeHash))),
        Readers = file.Readers.Select(r => r.Reader == reader ? r with { Layout = change(r.Layout!) } : r).ToList(),
    };

    private static Pmdg737FmaLayout? FmaOf(ModuleBuild build) => build.Layout<Pmdg737FmaLayout>(Pmdg737Module.Fma);

    private static Pmdg737EngineDisplayLayout? EngineOf(ModuleBuild build) => build.Layout<Pmdg737EngineDisplayLayout>(Pmdg737Module.EngineDisplays);

    private static Pmdg737NavDisplayLayout? NdOf(ModuleBuild build) => build.Layout<Pmdg737NavDisplayLayout>(Pmdg737Module.NavDisplay);

    private static Pmdg737PfdLayout? PfdOf(ModuleBuild build) => build.Layout<Pmdg737PfdLayout>(Pmdg737Module.Pfd);

    // ---- the file analysis ----

    [Fact]
    public void No_files_is_files_not_found()
    {
        var log = new List<string>();
        var a = Analyse(Array.Empty<string>(), _ => throw new InvalidOperationException("never read"), log);
        Assert.Empty(a.Builds);
        Assert.Equal(FmaReasons.FilesNotFound, a.Failure);
        Assert.Equal(FmaReasons.FilesNotFound, Pmdg737FmaEnvironment.ToFmaAnalysis(a).Failure);
        Assert.Contains(log, l => l.Contains(FmaReasons.FilesNotFound, StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_build_installed_twice_gives_one_build_and_names_both_files()
    {
        var log = new List<string>();
        var a = Analyse(new[] { @"C:\a\737NG3.wasm", @"C:\b\737NG3.wasm" }, f => Found(Real, f), log);
        Assert.Null(a.Failure);
        var build = Assert.Single(a.Builds);
        Assert.Equal(@"C:\a\737NG3.wasm", build.SourceFile);
        Assert.Equal(@"C:\a\737NG3.wasm", FmaOf(build)!.SourceFile);
        Assert.Contains(log, l => l.StartsWith(@"layout from C:\a\737NG3.wasm: ", StringComparison.Ordinal));
        Assert.Contains(log, l => l == @"file analysis: C:\b\737NG3.wasm: same layout as C:\a\737NG3.wasm");
        Assert.DoesNotContain(log, l => l.StartsWith(@"layout from C:\b\", StringComparison.Ordinal));
    }

    [Fact]
    public void Two_builds_give_two_builds_in_file_order()
    {
        var log = new List<string>();
        var a = Analyse(new[] { "one", "two" }, f => Found(f == "one" ? Real : OtherBuild, f), log);
        Assert.Equal(new[] { "one", "two" }, a.Builds.Select(b => b.SourceFile));
        Assert.Equal(new uint[] { GoldenLayouts.Fma.ObjectSize, 20000 }, a.Builds.Select(b => FmaOf(b)!.ObjectSize));
    }

    [Fact]
    public void A_file_that_fails_is_logged_with_its_path_and_the_others_still_count()
    {
        var log = new List<string>();
        // Each reason is the layout source's; the session logs it with the file's path.
        var a = Analyse(new[] { "locked", "garbage", "good" }, f => f switch
        {
            "locked" => new LayoutLookup(null, "IOException: in use"),
            "garbage" => new LayoutLookup(null, "WasmFormatException: not a wasm module"),
            _ => Found(Real, f),
        }, log);
        Assert.Equal("good", Assert.Single(a.Builds).SourceFile);
        Assert.Null(a.Failure);
        Assert.Contains(log, l => l == "file analysis: locked: not recognised: IOException: in use");
        Assert.Contains(log, l => l.StartsWith("file analysis: garbage: not recognised: WasmFormatException: ", StringComparison.Ordinal));
    }

    [Fact]
    public void Every_file_failing_is_file_not_recognised_with_each_reason_logged()
    {
        var log = new List<string>();
        // An older build: the FMA failed too, so no reader derived anything.
        string reason = "Fields: FMARoll is not among loadState's keys.";
        var omitted = Real with
        {
            Readers = Real.Readers.Select(r => r.Reader == Pmdg737Module.Fma ? new ReaderResult(r.Reader, null, reason) : r).ToList(),
        };
        var a = Analyse(new[] { "old", "denied" }, f => f == "old" ? Found(omitted, f) : new LayoutLookup(null, "UnauthorizedAccessException: no"), log);
        Assert.Empty(a.Builds);
        Assert.Equal(FmaReasons.FileNotRecognised, a.Failure);
        Assert.Equal(FmaReasons.FileNotRecognised, Pmdg737FmaEnvironment.ToFmaAnalysis(a).Failure);
        Assert.Contains(log, l => l == $"file analysis: old: FMA not recognised: {reason}");
        Assert.Contains(log, l => l == "file analysis: denied: not recognised: UnauthorizedAccessException: no");
    }

    [Fact]
    public void An_unexpected_exception_escapes_so_the_monitor_reports_an_internal_error()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Analyse(new[] { "x" }, _ => throw new InvalidOperationException("bug"), new List<string>()));
    }

    // ---- the memory search ----

    private const ulong Base = 0x10000000;

    private static Pmdg737FmaLayout RealLayout => GoldenLayouts.Fma with { SourceFile = "real" };

    private static ModuleBuild BuildOf(Pmdg737FmaLayout layout) => Pmdg737TestBuilds.Of(layout.SourceFile, fma: layout);

    /// <summary>One allocation holding the layout's anchor, where the sim keeps the module's memory.</summary>
    private static CountingSource Memory(Pmdg737FmaLayout layout)
    {
        var bytes = new byte[0x40000];
        layout.Anchor.CopyTo(bytes, (int)layout.AnchorAddress);
        return new CountingSource(new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes));
    }

    [Fact]
    public void Nothing_found_disposes_the_simulator_handle_before_returning()
    {
        var src = new CountingSource(new FakeProcessMemory());
        var log = new List<string>();
        var r = ModuleSession.Locate(src, new[] { BuildOf(RealLayout) }, log.Add);
        Assert.Empty(r.Found);
        Assert.Null(r.Failure);
        Assert.Equal(1, src.Disposals);
        Assert.Contains(log, l => l == "memory search: the anchor at 0x366ac not found in exactly one allocation");
    }

    [Fact]
    public void Builds_with_one_anchor_are_searched_once_and_share_the_handle()
    {
        var a = BuildOf(RealLayout);
        // The same anchor in another array: anchors are compared by content.
        var b = BuildOf(RealLayout with { SourceFile = "other", ObjectSize = 20000, Anchor = RealLayout.Anchor.ToArray() });
        var src = Memory(FmaOf(a)!);
        var log = new List<string>();
        var r = ModuleSession.Locate(src, new[] { a, b }, log.Add);

        Assert.Equal(new[] { a, b }, r.Found.Select(m => m.Build));
        Assert.Null(r.Failure);
        Assert.Equal(1, src.RegionWalks);
        Assert.Single(log, l => l.StartsWith("memory search: the anchor at 0x366ac found in the allocation at 0x10000000", StringComparison.Ordinal));
        Assert.Equal(0, src.Disposals);

        var leaseA = r.Found[0].AcquireLease();
        var leaseB = r.Found[1].AcquireLease();
        leaseA.Dispose();
        leaseA.Dispose(); // a second dispose of the same lease counts once
        r.ReleaseSessionLease();
        Assert.Equal(0, src.Disposals);
        leaseB.Dispose();
        Assert.Equal(1, src.Disposals);
    }

    [Fact]
    public void Only_builds_whose_anchor_matched_are_found()
    {
        var a = BuildOf(RealLayout);
        var stranger = BuildOf(RealLayout with { SourceFile = "stranger", AnchorAddress = RealLayout.AnchorAddress + 0x1000 });
        var src = Memory(FmaOf(a)!);
        var r = ModuleSession.Locate(src, new[] { stranger, a }, _ => { });
        Assert.Same(a, Assert.Single(r.Found).Build);
        Assert.Equal(2, src.RegionWalks);
        r.ReleaseSessionLease();
        Assert.Equal(1, src.Disposals);
    }

    [Fact]
    public void The_handle_is_closed_exactly_once_however_many_threads_dispose_the_leases()
    {
        var builds = Enumerable.Range(0, 16)
            .Select(i => BuildOf(RealLayout with { SourceFile = $"f{i}", ObjectSize = 20000u + (uint)i }))
            .ToArray();
        for (int round = 0; round < 50; round++)
        {
            var src = Memory(FmaOf(builds[0])!);
            var r = ModuleSession.Locate(src, builds, _ => { });
            Assert.Equal(16, r.Found.Count);
            var releases = r.Found.Select(m => m.AcquireLease()).Select(l => (Action)l.Dispose)
                .Append(r.ReleaseSessionLease)
                .ToList();
            using var go = new ManualResetEventSlim();
            var threads = releases.Select(release => new Thread(() => { go.Wait(); release(); release(); })).ToList();
            threads.ForEach(t => t.Start());
            go.Set();
            threads.ForEach(t => t.Join());
            Assert.Equal(1, src.Disposals);
        }
    }

    [Fact]
    public void A_search_that_throws_still_closes_the_handle()
    {
        var src = new CountingSource(new FakeProcessMemory()) { ThrowOnWalk = true };
        Assert.Throws<InvalidOperationException>(() => ModuleSession.Locate(src, new[] { BuildOf(RealLayout) }, _ => { }));
        Assert.Equal(1, src.Disposals);
    }

    [Theory]
    [InlineData("no access to the simulator", FmaReasons.NoAccess)]
    [InlineData("simulator not running", null)]
    [InlineData(null, null)]
    public void Only_no_access_is_final_when_the_simulator_cannot_be_opened(string? failure, string? expected)
    {
        var r = ModuleSession.NotOpened(failure);
        Assert.Empty(r.Found);
        Assert.Equal(expected, r.Failure);
    }

    // ---- the session: one analysis, one search, shared ----

    private static ModuleSession NewSession(FakeHost host) =>
        new(Pmdg737Module.Description, host, RealLayouts());

    /// <summary>A layout source answering the FMA fixture's saved layout file for every installed file.</summary>
    private static FakeLayoutSource RealLayouts() => new(f => Found(Real, f));

    [Fact]
    public void Two_readers_share_one_analysis()
    {
        var host = new FakeHost { Deferred = true };
        using var session = NewSession(host);
        var first = session.AnalyseAsync();
        var second = session.AnalyseAsync();
        Assert.Same(first, second);
        host.RunPending();
        Assert.Same(first, session.AnalyseAsync());
        Assert.Equal(1, host.FindCalls);
        Assert.Single(Done(first).Builds);
    }

    [Fact]
    public void A_search_in_flight_is_joined()
    {
        var host = new FakeHost { Deferred = true };
        using var session = NewSession(host);
        var first = session.LocateAsync();
        var second = session.LocateAsync();
        Assert.Same(first, second);
        host.RunPending();
        Assert.Equal(1, host.OpenCalls);
        Assert.Single(Done(first).Found);
    }

    [Fact]
    public void A_search_completed_within_5_s_is_reused()
    {
        var host = new FakeHost();
        using var session = NewSession(host);
        var first = session.LocateAsync();
        host.NowMs = ModuleSession.ReuseMs - 1;
        Assert.Same(first, session.LocateAsync());
        Assert.Equal(1, host.OpenCalls);
    }

    [Fact]
    public void After_5_s_a_new_search_runs()
    {
        var host = new FakeHost();
        using var session = NewSession(host);
        var first = session.LocateAsync();
        host.NowMs = ModuleSession.ReuseMs;
        var second = session.LocateAsync();
        Assert.NotSame(first, second);
        Assert.Equal(2, host.OpenCalls);
        Assert.Equal(1, host.Opened[0].Disposals); // the old location's session lease was released
        Assert.Equal(0, host.Opened[1].Disposals);
    }

    [Fact]
    public void Reuse_counts_from_the_search_start()
    {
        // A reader retrying 5 s after its last attempt began always gets a fresh search, however long
        // that search took: the FMA's retries are 5 s apart from attempt start.
        var host = new FakeHost { Deferred = true };
        using var session = NewSession(host);
        var first = session.LocateAsync();
        host.NowMs = 3_000;
        host.RunPending();
        Assert.Single(Done(first).Found);
        host.NowMs = ModuleSession.ReuseMs;
        Assert.NotSame(first, session.LocateAsync());
        host.RunPending();
        Assert.Equal(2, host.OpenCalls);
    }

    [Fact]
    public void A_location_whose_memory_is_gone_is_not_reused()
    {
        var host = new FakeHost();
        using var session = NewSession(host);
        var first = session.LocateAsync();
        host.Opened[0].Gone = true; // the module moved or the simulator closed
        host.NowMs = 1;
        Assert.NotSame(first, session.LocateAsync());
        Assert.Equal(2, host.OpenCalls);
        Assert.Equal(1, host.Opened[0].Disposals);
    }

    [Fact]
    public void A_faulted_search_is_not_reused()
    {
        var host = new FakeHost { ThrowOnFirstOpen = true };
        using var session = NewSession(host);
        Assert.True(session.LocateAsync().IsFaulted);
        Assert.Single(Done(session.LocateAsync()).Found);
        Assert.Equal(2, host.OpenCalls);
    }

    [Fact]
    public void Disposing_with_a_search_in_flight_closes_its_handle_when_it_ends()
    {
        var host = new FakeHost { Deferred = true };
        var session = NewSession(host);
        var search = session.LocateAsync();
        session.Dispose();
        host.RunPending();
        Assert.Single(Done(search).Found);
        Assert.Equal(1, host.Opened[0].Disposals);
    }

    [Fact]
    public void An_empty_search_is_never_reused()
    {
        var host = new FakeHost { OpenEmpty = true };
        using var session = NewSession(host);
        Assert.Empty(Done(session.LocateAsync()).Found);
        host.NowMs = 1;
        Assert.Empty(Done(session.LocateAsync()).Found);
        Assert.Equal(2, host.OpenCalls);
    }

    [Fact]
    public void Invalidate_forces_a_new_search()
    {
        var host = new FakeHost();
        using var session = NewSession(host);
        var first = session.LocateAsync();
        session.Invalidate();
        Assert.NotSame(first, session.LocateAsync());
        Assert.Equal(2, host.OpenCalls);
        Assert.Equal(1, host.Opened[0].Disposals);
    }

    [Fact]
    public void Invalidate_detaches_a_search_in_flight_and_its_handle_closes_when_it_ends()
    {
        var host = new FakeHost { Deferred = true };
        using var session = NewSession(host);
        var first = session.LocateAsync();
        session.Invalidate();
        var second = session.LocateAsync();
        Assert.NotSame(first, second);
        host.RunPending();
        Assert.Equal(2, host.OpenCalls);
        Assert.Equal(1, host.Opened[0].Disposals);
        Assert.Equal(0, host.Opened[1].Disposals);
    }

    [Fact]
    public void Invalidate_analyses_again_only_after_a_failed_analysis()
    {
        var host = new FakeHost { Files = Array.Empty<string>() };
        using var session = NewSession(host);
        var failed = session.AnalyseAsync();
        Assert.Equal(FmaReasons.FilesNotFound, Done(failed).Failure);
        session.Invalidate();
        host.Files = new[] { "real" };
        var good = session.AnalyseAsync();
        Assert.NotSame(failed, good);
        Assert.Single(Done(good).Builds);
        session.Invalidate();
        Assert.Same(good, session.AnalyseAsync());
        Assert.Equal(2, host.FindCalls);
    }

    [Fact]
    public void A_session_knows_when_its_files_were_analysed_for_another_simulator()
    {
        var host = new FakeHost { SimulatorVersion = "FS2020" };
        using var session = NewSession(host);
        // Nothing analysed yet: nothing to start again.
        Assert.False(session.AnalysedForOtherSimulator("FS2024"));

        Done(session.AnalyseAsync());
        Assert.False(session.AnalysedForOtherSimulator("FS2020"));
        Assert.True(session.AnalysedForOtherSimulator("FS2024"));
        // A version not detected never counts.
        Assert.False(session.AnalysedForOtherSimulator("Unknown"));
        Assert.False(session.AnalysedForOtherSimulator(""));
        Assert.False(session.AnalysedForOtherSimulator(null));

        // Invalidate keeps a good analysis, and with it the simulator it was for.
        host.SimulatorVersion = "FS2024";
        session.Invalidate();
        Assert.True(session.AnalysedForOtherSimulator("FS2024"));

        session.Dispose();
        Assert.False(session.AnalysedForOtherSimulator("FS2024"));
    }

    [Fact]
    public void The_handle_closes_after_the_last_reader_lease_and_the_session_released_it()
    {
        var host = new FakeHost();
        var session = NewSession(host);
        var location = Done(session.LocateAsync());
        var lease = location.Found[0].AcquireLease();
        session.Dispose();
        Assert.Equal(0, host.Opened[0].Disposals);
        lease.Dispose();
        Assert.Equal(1, host.Opened[0].Disposals);
    }

    [Fact]
    public void AcquireLease_after_the_handle_closed_throws()
    {
        var host = new FakeHost();
        var session = NewSession(host);
        var location = Done(session.LocateAsync());
        session.Dispose();
        Assert.Equal(1, host.Opened[0].Disposals);
        Assert.Throws<ObjectDisposedException>(() => location.Found[0].AcquireLease());
    }

    // ---- the FMA's view ----

    [Fact]
    public void ToFmaAnalysis_keeps_distinct_fma_layouts_in_order()
    {
        var one = RealLayout;
        var two = RealLayout with { SourceFile = "two", ObjectSize = 20000 };
        var failed = Pmdg737TestBuilds.Of("failed");
        var a = new ModuleAnalysis(
            new[] { BuildOf(one), failed, BuildOf(two), BuildOf(one with { SourceFile = "again" }) }, null);

        var fma = Pmdg737FmaEnvironment.ToFmaAnalysis(a);
        Assert.Null(fma.Failure);
        Assert.Equal(new[] { one, two }, fma.Layouts);
        Assert.Equal(new[] { "real", "two" }, fma.Layouts.Select(l => l.SourceFile));

        Assert.True(fma.LayoutFileLoaded);

        // A layout file loaded, but the FMA's own layout failed in it: the FMA's own line, not the shared one (WASM-29).
        var none = Pmdg737FmaEnvironment.ToFmaAnalysis(new ModuleAnalysis(new[] { failed }, null));
        Assert.Empty(none.Layouts);
        Assert.Equal(FmaReasons.FileNotRecognised, none.Failure);
        Assert.True(none.LayoutFileLoaded);

        // No layout file loaded (no builds): the four readers' shared line.
        var missing = Pmdg737FmaEnvironment.ToFmaAnalysis(
            new ModuleAnalysis(Array.Empty<ModuleBuild>(), FmaReasons.FilesNotFound));
        Assert.Equal(FmaReasons.FilesNotFound, missing.Failure);
        Assert.False(missing.LayoutFileLoaded);
        Assert.False(Pmdg737FmaEnvironment.ToFmaAnalysis(new ModuleAnalysis(Array.Empty<ModuleBuild>(), FmaReasons.FileNotRecognised)).LayoutFileLoaded);
    }

    [Fact]
    public void ToFmaCandidates_makes_one_sampler_per_requested_layout_found()
    {
        var one = BuildOf(RealLayout);
        var two = BuildOf(RealLayout with { SourceFile = "two", ObjectSize = 20000 });
        var src = Memory(FmaOf(one)!);
        var location = ModuleSession.Locate(src, new[] { one, two }, _ => { });

        var r = Pmdg737FmaEnvironment.ToFmaCandidates(location, new[] { FmaOf(two)! });
        Assert.Null(r.Failure);
        Assert.Same(FmaOf(two), Assert.Single(r.Candidates).Layout);

        location.ReleaseSessionLease();
        Assert.Equal(0, src.Disposals); // the sampler holds its own lease
        r.Candidates[0].Dispose();
        Assert.Equal(1, src.Disposals);

        var denied = Pmdg737FmaEnvironment.ToFmaCandidates(ModuleSession.NotOpened("no access to the simulator"), new[] { RealLayout });
        Assert.Empty(denied.Candidates);
        Assert.Equal(FmaReasons.NoAccess, denied.Failure);
    }

    [Fact]
    public void ToFmaCandidates_on_a_closed_handle_is_not_found_yet()
    {
        var one = BuildOf(RealLayout);
        var location = ModuleSession.Locate(Memory(FmaOf(one)!), new[] { one }, _ => { });
        location.ReleaseSessionLease();
        var r = Pmdg737FmaEnvironment.ToFmaCandidates(location, new[] { FmaOf(one)! });
        Assert.Empty(r.Candidates);
        Assert.Null(r.Failure);
    }

    // ---- two readers' layouts ----

    /// <summary>The engine display fixture's saved layout file: its engine displays derived, the other three failed.</summary>
    private static readonly LayoutFile EngineFile = GoldenLayouts.Load("engine-displays");

    private static Pmdg737EngineDisplayLayout EngineLayout => GoldenLayouts.EngineDisplays with { SourceFile = "engine" };

    [Fact]
    public void A_build_whose_fma_failed_is_still_located_by_the_engine_anchor()
    {
        var layout = EngineLayout;
        var build = Pmdg737TestBuilds.Of("engine", engine: layout);
        Assert.Equal(new[] { new ModuleAnchor(layout.AnchorAddress, layout.Anchor) }, build.Anchors);

        var bytes = new byte[layout.AnchorAddress + 0x1000];
        layout.Anchor.CopyTo(bytes, (int)layout.AnchorAddress);
        var src = new CountingSource(new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes));
        var location = ModuleSession.Locate(src, new[] { build }, _ => { });

        var found = Assert.Single(location.Found);
        Assert.Same(build, found.Build);
        Assert.Equal(layout.AnchorAddress, found.Anchor.Address);
        Assert.Empty(Pmdg737FmaEnvironment.ToFmaCandidates(location, new[] { RealLayout }).Candidates);
        location.ReleaseSessionLease();
    }

    [Fact]
    public void A_build_whose_engine_layout_failed_still_serves_the_fma()
    {
        var log = new List<string>();
        var a = Analyse(new[] { "fma" }, f => Found(Real, f), log);
        var build = Assert.Single(a.Builds);
        Assert.NotNull(FmaOf(build));
        Assert.Null(EngineOf(build));
        Assert.Equal(new[] { new ModuleAnchor(FmaOf(build)!.AnchorAddress, FmaOf(build)!.Anchor) }, build.Anchors);
        Assert.Single(Pmdg737FmaEnvironment.ToFmaAnalysis(a).Layouts);
        Assert.Contains(log, l => l.StartsWith("file analysis: fma: engine displays not recognised: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_with_only_the_engine_displays_is_a_build_without_an_fma()
    {
        var log = new List<string>();
        var a = Analyse(new[] { "engine" }, f => Found(EngineFile, f), log);
        var build = Assert.Single(a.Builds);
        Assert.Null(FmaOf(build));
        Assert.Equal(EngineLayout, EngineOf(build));
        Assert.Equal(FmaReasons.FileNotRecognised, Pmdg737FmaEnvironment.ToFmaAnalysis(a).Failure);
        Assert.Contains(log, l => l.StartsWith("file analysis: engine: FMA not recognised: ", StringComparison.Ordinal));
        Assert.Contains(log, l => l.StartsWith("engine displays from engine: anchor 0xe6000", StringComparison.Ordinal));
    }

    [Fact]
    public void Builds_differing_only_in_their_engine_layout_are_two_builds()
    {
        var log = new List<string>();
        var other = OtherBuildOf(EngineFile, Pmdg737Module.EngineDisplays, l =>
        {
            var engine = (Pmdg737EngineDisplayLayout)l;
            return engine with { Engine = engine.Engine with { Size = engine.Engine.Size + 8 } };
        });
        var a = Analyse(new[] { "one", "two", "three" }, f => Found(f == "two" ? other : EngineFile, f), log);
        Assert.Equal(new[] { "one", "two" }, a.Builds.Select(b => b.SourceFile));
        Assert.Contains(log, l => l == "file analysis: three: same layout as one");
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_logged_engine_layout_is_the_same_in_any_culture(string culture)
    {
        var layout = EngineLayout;
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        IReadOnlyList<string> invariant, local;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            invariant = Pmdg737LayoutDescriptions.Describe(layout);
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
            local = Pmdg737LayoutDescriptions.Describe(layout);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
        Assert.Equal(invariant, local);
        Assert.Contains(local, l => l.Contains("18.2173", StringComparison.Ordinal));
        Assert.Contains(local, l => l.Contains("N1 136/32", StringComparison.Ordinal));
    }

    // ---- the navigation display's layout ----

    /// <summary>The navigation display fixture's saved layout file: its ND derived, the other three failed.</summary>
    private static readonly LayoutFile NdFile = GoldenLayouts.Load("nav-display");

    private static Pmdg737NavDisplayLayout NdLayout => GoldenLayouts.NavDisplay with { SourceFile = "nd" };

    [Fact]
    public void A_build_whose_nd_layout_failed_still_serves_the_fma_and_engine_displays()
    {
        var log = new List<string>();
        var a = Analyse(new[] { "fma", "engine" }, f => Found(f == "fma" ? Real : EngineFile, f), log);

        Assert.Equal(new[] { "fma", "engine" }, a.Builds.Select(b => b.SourceFile));
        Assert.All(a.Builds, b => Assert.Null(NdOf(b)));
        Assert.NotNull(FmaOf(a.Builds[0]));
        Assert.Equal(EngineLayout, EngineOf(a.Builds[1]));
        Assert.Single(Pmdg737FmaEnvironment.ToFmaAnalysis(a).Layouts);
        Assert.Contains(log, l => l.StartsWith("file analysis: fma: navigation display not recognised: ", StringComparison.Ordinal));
        Assert.Contains(log, l => l.StartsWith("file analysis: engine: navigation display not recognised: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_build_whose_fma_and_engine_failed_is_located_by_the_nd_anchor()
    {
        var log = new List<string>();
        var a = Analyse(new[] { "nd" }, f => Found(NdFile, f), log);
        var build = Assert.Single(a.Builds);
        Assert.Null(FmaOf(build));
        Assert.Null(EngineOf(build));
        var layout = NdOf(build)!;
        Assert.Equal(NdLayout, layout);
        Assert.Equal(new[] { new ModuleAnchor(layout.AnchorAddress, layout.Anchor) }, build.Anchors);
        Assert.Contains(log, l => l.StartsWith("navigation display from nd: anchor 0xe4530", StringComparison.Ordinal));
        Assert.Contains(log, l => l.StartsWith("file analysis: nd: FMA not recognised: ", StringComparison.Ordinal));

        var bytes = new byte[layout.AnchorAddress + 0x1000];
        layout.Anchor.CopyTo(bytes, (int)layout.AnchorAddress);
        var src = new CountingSource(new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes));
        var location = ModuleSession.Locate(src, new[] { build }, _ => { });

        var found = Assert.Single(location.Found);
        Assert.Same(build, found.Build);
        Assert.Equal(layout.AnchorAddress, found.Anchor.Address);
        location.ReleaseSessionLease();
    }

    // ---- the primary flight display's layout ----

    /// <summary>The primary flight display fixture's saved layout file: its PFD derived, the other three failed.</summary>
    private static readonly LayoutFile PfdFile = GoldenLayouts.Load("pfd");

    private static Pmdg737PfdLayout PfdLayout => GoldenLayouts.Pfd with { SourceFile = "pfd" };

    [Fact]
    public void A_build_whose_pfd_layout_failed_still_serves_the_other_three()
    {
        var log = new List<string>();
        var a = Analyse(new[] { "fma", "engine", "nd" }, f => Found(f switch { "fma" => Real, "engine" => EngineFile, _ => NdFile }, f), log);

        Assert.Equal(new[] { "fma", "engine", "nd" }, a.Builds.Select(b => b.SourceFile));
        Assert.All(a.Builds, b => Assert.Null(PfdOf(b)));
        Assert.NotNull(FmaOf(a.Builds[0]));
        Assert.Equal(EngineLayout, EngineOf(a.Builds[1]));
        Assert.Equal(NdLayout, NdOf(a.Builds[2]));
        foreach (string file in new[] { "fma", "engine", "nd" })
            Assert.Contains(log, l => l.StartsWith($"file analysis: {file}: primary flight display not recognised: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_build_whose_other_layouts_failed_is_located_by_the_pfd_anchor()
    {
        var log = new List<string>();
        var a = Analyse(new[] { "pfd" }, f => Found(PfdFile, f), log);
        var build = Assert.Single(a.Builds);
        Assert.Null(FmaOf(build));
        Assert.Null(EngineOf(build));
        Assert.Null(NdOf(build));
        var layout = PfdOf(build)!;
        Assert.Equal(PfdLayout, layout);
        Assert.Equal(new[] { new ModuleAnchor(layout.AnchorAddress, layout.Anchor) }, build.Anchors);
        Assert.Contains(log, l => l.StartsWith("primary flight display from pfd: anchor 0xdf9a0", StringComparison.Ordinal));
        Assert.Contains(log, l => l.StartsWith("file analysis: pfd: navigation display not recognised: ", StringComparison.Ordinal));

        var bytes = new byte[layout.AnchorAddress + 0x1000];
        layout.Anchor.CopyTo(bytes, (int)layout.AnchorAddress);
        var src = new CountingSource(new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes));
        var location = ModuleSession.Locate(src, new[] { build }, _ => { });

        var found = Assert.Single(location.Found);
        Assert.Same(build, found.Build);
        Assert.Equal(layout.AnchorAddress, found.Anchor.Address);
        location.ReleaseSessionLease();
    }

    [Fact]
    public void An_optional_pfd_part_that_failed_is_logged_by_name()
    {
        var log = new List<string>();
        var pfd = PfdLayout;
        var failures = new[] { new PfdPartFailure("markers", "no ring") };
        var build = Pmdg737TestBuilds.Of("pfd", pfd: pfd with { Optional = pfd.Optional with { Markers = null, Failures = failures } });
        ModuleSession.Analyse(new[] { "pfd" }, new FakeLayoutSource(_ => new LayoutLookup(build, null)), log.Add);

        Assert.Contains(log, l => l == "file analysis: pfd: primary flight display markers not recognised: no ring");
    }

    // ---- the definition owns the session ----

    [Fact]
    public void A_new_session_disposes_the_previous_one()
    {
        var pmdg = new PMDG737Definition();
        var host = new FakeHost();
        var first = pmdg.ResetModuleSession(host, RealLayouts());
        var second = pmdg.ResetModuleSession(host, RealLayouts());
        Assert.Same(second, pmdg.ModuleSession);
        Assert.Throws<ObjectDisposedException>(() => StartSearch(first));
    }

    [Fact]
    public void Switching_away_disposes_the_session()
    {
        var pmdg = new PMDG737Definition();
        var host = new FakeHost();
        var session = pmdg.ResetModuleSession(host, RealLayouts());
        pmdg.OnSwitchedAway();
        Assert.Null(pmdg.ModuleSession);
        Assert.Throws<ObjectDisposedException>(() => StartSearch(session));
    }

    /// <summary>The fake host runs work synchronously, so the session's tasks are already complete.</summary>
    private static T Done<T>(Task<T> task)
    {
        Assert.True(task.IsCompletedSuccessfully);
        return task.GetAwaiter().GetResult();
    }

    /// <summary>A disposed session refuses before it returns a task.</summary>
    private static void StartSearch(ModuleSession session) => session.LocateAsync();

    /// <summary>
    /// The app's side of the session, faked: a settable clock, counted file listings and simulator
    /// opens (each open gives a fresh memory holding the real anchor, or an empty one), and background
    /// work run at once or held until <see cref="RunPending"/>.
    /// </summary>
    private sealed class FakeHost : IModuleHost
    {
        private readonly Queue<Action> _pending = new();

        public long NowMs { get; set; }
        public string SimulatorVersion { get; set; } = "FS2024";
        public IReadOnlyList<string> Files { get; set; } = new[] { "real" };
        public bool OpenEmpty { get; init; }
        public bool ThrowOnFirstOpen { get; init; }
        public bool Deferred { get; init; }
        public int FindCalls { get; private set; }
        public int OpenCalls { get; private set; }
        public List<CountingSource> Opened { get; } = new();
        public List<string> Lines { get; } = new();

        public IReadOnlyList<string> FindInstalledFiles(string simulatorVersion, Action<string> log)
        {
            FindCalls++;
            return Files;
        }

        /// <summary>The session never reads a file itself: its layout source does (WASM-11).</summary>
        public byte[] ReadFile(string path) => throw new InvalidOperationException($"the session read {path}");

        public IProcessMemorySource? OpenSimulator(string simulatorVersion, out string? failure)
        {
            OpenCalls++;
            failure = null;
            if (ThrowOnFirstOpen && OpenCalls == 1) throw new InvalidOperationException("open failed");
            var src = OpenEmpty ? new CountingSource(new FakeProcessMemory()) : Memory(RealLayout);
            Opened.Add(src);
            return src;
        }

        public void Log(string line) => Lines.Add(line);

        public Task<T> RunInBackground<T>(string name, Func<T> work)
        {
            var done = new TaskCompletionSource<T>();
            void Run()
            {
                try
                {
                    done.SetResult(work());
                }
                catch (Exception ex)
                {
                    done.SetException(ex);
                }
            }
            if (Deferred) _pending.Enqueue(Run);
            else Run();
            return done.Task;
        }

        public void RunPending()
        {
            while (_pending.Count > 0) _pending.Dequeue()();
        }
    }

    /// <summary>Delegates to a fake, counting region walks and disposals; can throw on a walk, or lose
    /// all its memory (<see cref="Gone"/>).</summary>
    private sealed class CountingSource : IProcessMemorySource
    {
        private readonly FakeProcessMemory _inner;
        private int _disposals;

        public CountingSource(FakeProcessMemory inner) => _inner = inner;

        public int Disposals => Volatile.Read(ref _disposals);
        public int RegionWalks { get; private set; }
        public bool ThrowOnWalk { get; init; }
        public bool Gone { get; set; }

        public IEnumerable<MemoryRegion> Regions()
        {
            RegionWalks++;
            if (ThrowOnWalk) throw new InvalidOperationException("walk failed");
            return Gone ? Array.Empty<MemoryRegion>() : _inner.Regions();
        }

        public MemoryRegion? RegionAt(ulong address) => Gone ? null : _inner.RegionAt(address);
        public bool TryRead(ulong address, Span<byte> into) => !Gone && _inner.TryRead(address, into);
        public void Dispose() => Interlocked.Increment(ref _disposals);
    }
}

public class WasmModuleHostTests
{
    [Fact]
    public async Task The_background_job_runs_on_its_own_low_priority_background_thread()
    {
        int caller = Environment.CurrentManagedThreadId;
        var (id, background, priority) = await ModuleHost.OnBackgroundThread("test job",
            () => (Environment.CurrentManagedThreadId, Thread.CurrentThread.IsBackground, Thread.CurrentThread.Priority));
        Assert.NotEqual(caller, id);
        Assert.True(background);
        Assert.Equal(ThreadPriority.BelowNormal, priority);
    }

    [Fact]
    public async Task A_job_that_throws_faults_its_task_instead_of_leaving_it_pending()
    {
        var task = ModuleHost.OnBackgroundThread<int>("test job", () => throw new InvalidOperationException("boom"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("boom", ex.Message);
        Assert.True(task.IsFaulted);
    }
}

/// <summary>A layout source answering with whatever the test gives.</summary>
internal sealed class FakeLayoutSource(Func<string, LayoutLookup> find) : ILayoutSource
{
    public LayoutLookup Find(string wasmFile) => find(wasmFile);
}

/// <summary>Builds of the PMDG 737 module from layouts in hand; a reader given no layout failed ("not a … build").
/// The layout file is named after the source file, so two builds are the same only when their source files are.</summary>
internal static class Pmdg737TestBuilds
{
    public static ModuleBuild Of(string sourceFile, Pmdg737FmaLayout? fma = null, Pmdg737EngineDisplayLayout? engine = null,
        Pmdg737NavDisplayLayout? nd = null, Pmdg737PfdLayout? pfd = null) =>
        new(Pmdg737Module.Description, sourceFile, sourceFile + ".json", new[]
        {
            Result(Pmdg737Module.Fma, fma),
            Result(Pmdg737Module.EngineDisplays, engine),
            Result(Pmdg737Module.NavDisplay, nd),
            Result(Pmdg737Module.Pfd, pfd),
        });

    private static ReaderResult Result(string reader, object? layout) =>
        layout is null ? new ReaderResult(reader, null, $"not a {reader} build") : new ReaderResult(reader, layout, null);
}

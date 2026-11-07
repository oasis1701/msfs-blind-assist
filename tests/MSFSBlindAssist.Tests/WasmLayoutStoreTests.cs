// The layout store: a current layout file is used as it is; a missing, damaged, unknown, older or unfitting one is
// regenerated when a generator is present and makes the readers unavailable otherwise (WASM-26); a generator failure
// saves nothing and the next load tries again; the generator's version is asked once per store; files that are not
// <codeHash>-*.json are never read. Hand-built tiny .wasm files, a two-reader test module and a scripted generator
// stand in for the real ones, so no process starts.

using System.Text;
using MSFSBlindAssist.DisplayLayouts;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;
using static MSFSBlindAssist.Tests.TinyLayouts;

namespace MSFSBlindAssist.Tests;

public sealed class WasmLayoutStoreTests : IDisposable
{
    private const string A = @"C:\sim\Community\tiny-a\panel\Tiny.wasm";
    private const string B = @"C:\sim\Community\tiny-b\panel\Tiny.wasm";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "msfsba-layout-store-" + Guid.NewGuid().ToString("N"));
    private readonly string _bundled = Path.Combine(Path.GetTempPath(), "msfsba-layout-bundled-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, byte[]> _installed = new(StringComparer.OrdinalIgnoreCase) { [A] = Wasm, [B] = OtherCode };
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _reads = new();
    private readonly List<string> _log = new();

    public void Dispose()
    {
        foreach (string dir in new[] { _root, _bundled })
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private string Folder => Path.Combine(_root, TestModule.Id);

    private string BundledFolder => Path.Combine(_bundled, TestModule.Id);

    private LayoutStore Store(ILayoutGenerator? generator) => new(TestModule, _root, _bundled, generator, ReadFile, _log.Add);

    private FakeGenerator Generator(int? version = 1) => new(this) { Version = version };

    /// <summary>The installed .wasm files from memory, everything else from disk; every read is recorded.</summary>
    private byte[] ReadFile(string path)
    {
        _reads.Add(path);
        if (_unreadable.Contains(path)) throw new IOException("in use");
        return _installed.TryGetValue(path, out var bytes) ? bytes : File.ReadAllBytes(path);
    }

    private string Save(LayoutFile file, string? name = null) => SaveText(Text(file), name ?? file.FileName);

    private string SaveText(string text, string name) => WriteTo(Folder, text, name);

    private string SaveBundled(LayoutFile file, string? name = null) => WriteTo(BundledFolder, Text(file), name ?? file.FileName);

    private static string WriteTo(string folder, string text, string name)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    private string[] SavedFiles() => Directory.Exists(Folder) ? Directory.GetFiles(Folder).Order(StringComparer.Ordinal).ToArray() : Array.Empty<string>();

    private static int GeneratorVersionOf(string path)
    {
        Assert.True(LayoutFileFormat.TryRead(TestModule, File.ReadAllText(path), out var file, out var failure), failure);
        return file.GeneratorVersion;
    }

    [Fact]
    public void A_current_file_is_used_without_running_the_generator()
    {
        var layout = new TinyLayout(DataAddress, Data[..8], "");
        string saved = Save(FileFor(Wasm, first: layout));
        var generator = Generator();

        var found = Store(generator).Find(A);

        Assert.Null(found.Failure);
        var build = found.Build!;
        Assert.Same(TestModule, build.Module);
        Assert.Equal(A, build.SourceFile);
        Assert.Equal(saved, build.LayoutFile);
        Assert.Equal(A, build.Layout<TinyLayout>("first")!.SourceFile); // the store fills in the source file the file leaves out
        Assert.Null(build.Layout<TinyLayout>("second"));
        Assert.Equal("no second table", build.Failure("second"));
        Assert.Equal(new[] { new ModuleAnchor(DataAddress, Data[..8]) }, build.Anchors);
        Assert.Equal(0, generator.RunCalls);
        Assert.Contains(_log, l => l.StartsWith($"layouts: {A}: ", StringComparison.Ordinal) && l.Contains($"using {saved}", StringComparison.Ordinal));
        Assert.Contains(_log, l => l.Contains("generator version 1", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_file_runs_the_generator_and_loads_what_it_wrote()
    {
        var generator = Generator();

        var found = Store(generator).Find(A);

        Assert.Null(found.Failure);
        Assert.Equal(1, generator.RunCalls);
        Assert.Equal((A, _root), generator.LastRun);
        string written = Assert.Single(SavedFiles());
        Assert.Equal(written, found.Build!.LayoutFile);
        Assert.Equal(Path.Combine(Folder, FileFor(Wasm).FileName), written);
        Assert.Contains(_log, l => l == $"layouts: generator: written: {written}");
        Assert.Contains(_log, l => l == "layouts: generator: first: not recognised: no first table");
        Assert.Contains(_log, l => l.StartsWith($"layouts: {A}: ", StringComparison.Ordinal) && l.Contains("running the generator", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_file_without_a_generator_is_not_recognised()
    {
        var store = Store(null);

        var found = store.Find(A);

        Assert.Null(found.Build);
        Assert.Equal("no layout for this build and no generator", found.Failure);
        Assert.Contains(_log, l => l.StartsWith($"layouts: {A}: ", StringComparison.Ordinal) && l.Contains("no generator", StringComparison.Ordinal));

        // What the pilot then hears: "PMDG file not recognised", as today, and the AI fallback.
        var analysis = ModuleSession.Analyse(new[] { A }, store, _log.Add);
        Assert.Empty(analysis.Builds);
        Assert.Equal(FmaReasons.FileNotRecognised, analysis.Failure);
        Assert.Contains(_log, l => l == $"file analysis: {A}: not recognised: no layout for this build and no generator");
    }

    [Fact]
    public void A_generator_crash_saves_nothing_and_the_next_load_tries_again()
    {
        var generator = new FakeGenerator(this)
        {
            OnRun = (_, _) => new GeneratorRun(-532462766, false, new[] { "Unhandled exception. System.InvalidOperationException: boom" }),
        };
        var store = Store(generator);

        var first = store.Find(A);

        Assert.Null(first.Build);
        Assert.Equal("the layout generator failed (exit code -532462766)", first.Failure);
        Assert.Empty(SavedFiles());
        Assert.Contains(_log, l => l == "layouts: generator: Unhandled exception. System.InvalidOperationException: boom");

        var again = store.Find(A); // a flight reload analyses a failed analysis again
        Assert.Null(again.Build);
        Assert.Equal(2, generator.RunCalls);
    }

    [Fact]
    public void A_non_zero_exit_is_unavailable_and_saves_nothing()
    {
        // A layouts folder that cannot be written: the generator says why on stderr, exits 1 and leaves nothing.
        const string Denied = @"internal error: UnauthorizedAccessException: Access to the path 'C:\Users\Zoé\layouts\tinytest' is denied.";
        var generator = new FakeGenerator(this) { OnRun = (_, _) => new GeneratorRun(1, false, new[] { Denied }) };
        var store = Store(generator);

        var found = store.Find(A);

        Assert.Null(found.Build);
        Assert.Equal("the layout generator failed (exit code 1)", found.Failure);
        Assert.Empty(SavedFiles());
        Assert.Contains(_log, l => l == $"layouts: generator: {Denied}");
        Assert.Contains(_log, l => l.StartsWith($"layouts: {A}: ", StringComparison.Ordinal) && l.Contains("exit code 1", StringComparison.Ordinal));
        Assert.Equal(FmaReasons.FileNotRecognised, ModuleSession.Analyse(new[] { A }, store, _log.Add).Failure);
    }

    [Fact]
    public void A_timeout_is_unavailable_and_logged()
    {
        var generator = new FakeGenerator(this) { OnRun = (_, _) => new GeneratorRun(null, true, new[] { "fma: derived" }) };

        var found = Store(generator).Find(A);

        Assert.Null(found.Build);
        Assert.Equal("the layout generator timed out", found.Failure);
        Assert.Empty(SavedFiles());
        Assert.Contains(_log, l => l == "layouts: generator: fma: derived");
        Assert.Contains(_log, l => l.StartsWith($"layouts: {A}: ", StringComparison.Ordinal) && l.Contains("timed out", StringComparison.Ordinal));
    }

    [Fact]
    public void An_older_generator_version_regenerates()
    {
        string old = Save(FileFor(Wasm, generatorVersion: 1));
        var generator = Generator(version: 2);

        var found = Store(generator).Find(A);

        Assert.Null(found.Failure);
        Assert.Equal(1, generator.RunCalls);
        Assert.Equal(old, found.Build!.LayoutFile); // the same build gets the same name: the generator wrote over it
        Assert.Equal(2, GeneratorVersionOf(old));
        Assert.Contains(_log, l => l.Contains("generator version 2", StringComparison.Ordinal));
        Assert.Contains(_log, l => l.StartsWith($"layouts: skipped {old}: ", StringComparison.Ordinal)
            && l.Contains("generator version 1 is older than 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_generator_an_older_version_file_is_used()
    {
        // The public build: no generator to compare with, so any file that fits is current.
        string saved = Save(FileFor(Wasm, generatorVersion: 1));

        var found = Store(null).Find(A);

        Assert.Null(found.Failure);
        Assert.Equal(saved, found.Build!.LayoutFile);
    }

    [Fact]
    public void Unknown_generator_version_uses_any_fitting_file()
    {
        // A generator whose --version answer is garbage, or that crashed or timed out when asked.
        string saved = Save(FileFor(Wasm, generatorVersion: 1));
        var generator = Generator(version: null);
        var store = Store(generator);

        var found = store.Find(A);

        Assert.Equal(saved, found.Build!.LayoutFile);
        Assert.Equal(0, generator.RunCalls);
        Assert.Contains(_log, l => l.Contains("generator version unknown", StringComparison.Ordinal));

        // It still runs the generator for a build that has no file.
        var other = store.Find(B);
        Assert.Null(other.Failure);
        Assert.Equal(1, generator.RunCalls);
        Assert.Equal(Path.Combine(Folder, FileFor(OtherCode).FileName), other.Build!.LayoutFile);
    }

    [Fact]
    public void An_unknown_format_version_counts_as_missing()
    {
        var file = FileFor(Wasm);
        string text = Text(file);
        Assert.Contains("\"formatVersion\": 1,", text, StringComparison.Ordinal);
        string unknown = SaveText(text.Replace("\"formatVersion\": 1,", "\"formatVersion\": 99,", StringComparison.Ordinal), file.FileName);
        var generator = Generator();

        var found = Store(generator).Find(A);

        Assert.Null(found.Failure);
        Assert.Equal(1, generator.RunCalls);
        Assert.Equal(unknown, found.Build!.LayoutFile);
        Assert.Contains(_log, l => l.StartsWith($"layouts: skipped {unknown}: ", StringComparison.Ordinal)
            && l.Contains("format version 99 is not known", StringComparison.Ordinal));

        // Without a generator the readers are unavailable, never a partial layout.
        SaveText(text.Replace("\"formatVersion\": 1,", "\"formatVersion\": 99,", StringComparison.Ordinal), file.FileName);
        Assert.Equal("no layout for this build and no generator", Store(null).Find(A).Failure);
    }

    [Fact]
    public void A_file_that_does_not_fit_counts_as_missing()
    {
        // Same code hash, so the file is a candidate; the data the generator read differs.
        string variantFile = Save(FileFor(Variant));
        var generator = Generator();

        var found = Store(generator).Find(A);

        Assert.Null(found.Failure);
        Assert.Equal(1, generator.RunCalls);
        Assert.NotEqual(variantFile, found.Build!.LayoutFile);
        Assert.Contains(_log, l => l == $"layouts: skipped {variantFile}: data differs");
    }

    [Fact]
    public void A_damaged_file_counts_as_missing()
    {
        string codeHash = Image(Wasm).CodeHash;
        string damaged = SaveText("{\n  \"formatVersion\": 1,\n  \"module\": \"tinytest\",\n  \"generatorV", $"{codeHash}-0000000000000000.json");
        string locked = SaveText(Text(FileFor(Wasm)), $"{codeHash}-1111111111111111.json");
        _unreadable.Add(locked);
        var generator = Generator();

        var found = Store(generator).Find(A);

        Assert.Null(found.Failure);
        Assert.Equal(1, generator.RunCalls);
        Assert.Equal(Path.Combine(Folder, FileFor(Wasm).FileName), found.Build!.LayoutFile);
        Assert.Contains(_log, l => l.StartsWith($"layouts: skipped {damaged}: not a layout file: ", StringComparison.Ordinal));
        Assert.Contains(_log, l => l == $"layouts: skipped {locked}: IOException: in use");
    }

    [Fact]
    public void A_saved_failure_is_used_and_not_regenerated()
    {
        // A build whose readers all failed still has its file, so the generator does not run on every load.
        string saved = Save(FileFor(Wasm));
        var generator = Generator();
        var store = Store(generator);

        var found = store.Find(A);

        Assert.Equal(saved, found.Build!.LayoutFile);
        Assert.Empty(found.Build.Anchors);
        Assert.Equal(0, generator.RunCalls);

        var analysis = ModuleSession.Analyse(new[] { A }, store, _log.Add);
        Assert.Equal(FmaReasons.FileNotRecognised, analysis.Failure);
        Assert.Equal(0, generator.RunCalls);
        Assert.Contains(_log, l => l == $"file analysis: {A}: first display not recognised: no first table");
        Assert.Contains(_log, l => l == $"file analysis: {A}: second display not recognised: no second table");
    }

    [Fact]
    public void Two_variants_keep_a_file_each_and_neither_regenerates()
    {
        _installed[B] = Variant;
        Assert.Equal(Image(Wasm).CodeHash, Image(Variant).CodeHash);
        var generator = Generator();
        var store = Store(generator);

        string a = store.Find(A).Build!.LayoutFile;
        Assert.Equal(1, generator.RunCalls);
        string b = store.Find(B).Build!.LayoutFile; // A's file is a candidate, and does not fit
        Assert.Equal(2, generator.RunCalls);
        Assert.NotEqual(a, b);
        Assert.Equal(new[] { a, b }.Order(StringComparer.Ordinal), SavedFiles());

        foreach (var s in new[] { store, Store(generator) })
        {
            Assert.Equal(a, s.Find(A).Build!.LayoutFile);
            Assert.Equal(b, s.Find(B).Build!.LayoutFile);
        }
        Assert.Equal(2, generator.RunCalls);
    }

    [Fact]
    public void Leftover_tmp_files_are_ignored()
    {
        // A generator killed mid-write leaves <name>.json.tmp; it would fit, and must never be read. Neither is a file
        // whose name is not <codeHash>-*.json.
        var file = FileFor(Wasm);
        string text = Text(file);
        string codeHash = file.CodeHash;
        var strays = new[]
        {
            SaveText(text, file.FileName + ".tmp"),
            SaveText(text, $"{codeHash}.json"),
            SaveText(text, $"{codeHash}-0000000000000000.json.bak"),
            SaveText(text, "notes.txt"),
            SaveText(text, $"x{codeHash}-0000000000000000.json"),
        };

        var found = Store(null).Find(A);

        Assert.Null(found.Build);
        Assert.Equal("no layout for this build and no generator", found.Failure);
        foreach (string stray in strays)
        {
            Assert.DoesNotContain(stray, _reads);
            Assert.DoesNotContain(_log, l => l.Contains(stray, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(null)]
    public void The_generator_version_is_asked_once_per_store(int? version)
    {
        var generator = Generator(version);
        var store = Store(generator);

        store.Find(A);
        store.Find(B);
        store.Find(A);

        Assert.Equal(1, generator.VersionCalls);
        Store(generator).Find(A);
        Assert.Equal(2, generator.VersionCalls); // the shared ProcessLayoutGenerator caches its own answer for the app session
    }

    [Fact]
    public void A_file_that_cannot_be_read_or_parsed_is_not_recognised()
    {
        _unreadable.Add(A);
        _installed[B] = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var generator = Generator();
        var store = Store(generator);

        Assert.Equal("IOException: in use", store.Find(A).Failure);
        Assert.StartsWith("Not a wasm module", store.Find(B).Failure, StringComparison.Ordinal);
        Assert.Equal(0, generator.RunCalls);
    }

    [Fact]
    public void A_bundled_file_is_used_when_appdata_has_none()
    {
        var layout = new TinyLayout(DataAddress, Data[..8], "");
        string bundled = SaveBundled(FileFor(Wasm, first: layout));

        var found = Store(null).Find(A);

        Assert.Null(found.Failure);
        Assert.Equal(bundled, found.Build!.LayoutFile);
        Assert.Contains(_log, l => l.Contains($"using {bundled} (bundled;", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, false)]
    public void The_higher_generator_version_wins_across_folders(int appData, int bundled, bool bundledWins)
    {
        string fromAppData = Save(FileFor(Wasm, appData));
        string fromBundled = SaveBundled(FileFor(Wasm, bundled));

        var found = Store(null).Find(A);

        Assert.Null(found.Failure);
        Assert.Equal(bundledWins ? fromBundled : fromAppData, found.Build!.LayoutFile);
    }

    [Fact]
    public void On_a_tie_the_appdata_file_is_used()
    {
        string fromAppData = Save(FileFor(Wasm, 1));
        SaveBundled(FileFor(Wasm, 1));

        var found = Store(null).Find(A);

        Assert.Equal(fromAppData, found.Build!.LayoutFile);
        Assert.StartsWith(_root, found.Build.LayoutFile, StringComparison.Ordinal);
    }

    [Fact]
    public void A_damaged_bundled_file_counts_as_missing_and_the_appdata_file_is_used()
    {
        var file = FileFor(Wasm);
        string bundled = SaveBundled(file, file.FileName); // placeholder, overwritten with damaged text below
        File.WriteAllText(bundled, "{", new UTF8Encoding(false));
        string fromAppData = Save(file);

        var found = Store(null).Find(A);

        Assert.Equal(fromAppData, found.Build!.LayoutFile);
        Assert.Contains(_log, l => l.StartsWith($"layouts: skipped {bundled}:", StringComparison.Ordinal));
    }

    [Fact]
    public void With_a_generator_an_older_bundled_file_runs_it_and_its_file_wins()
    {
        string bundled = SaveBundled(FileFor(Wasm, 1));
        var before = Directory.GetFiles(BundledFolder).Select(f => (f, File.ReadAllBytes(f))).ToArray();
        var generator = Generator(version: 2);

        var found = Store(generator).Find(A);

        Assert.Null(found.Failure);
        Assert.Equal(1, generator.RunCalls);
        Assert.Equal(Path.Combine(Folder, FileFor(Wasm).FileName), found.Build!.LayoutFile);
        var after = Directory.GetFiles(BundledFolder).Select(f => (f, File.ReadAllBytes(f))).ToArray();
        Assert.Equal(before.Select(b => b.f), after.Select(b => b.f));
        for (int i = 0; i < before.Length; i++) Assert.Equal(before[i].Item2, after[i].Item2);
        Assert.Contains(_log, l => l.StartsWith($"layouts: skipped {bundled}: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_missing_bundled_folder_changes_nothing()
    {
        Assert.False(Directory.Exists(_bundled));
        string saved = Save(FileFor(Wasm));

        var found = Store(null).Find(A);

        Assert.Equal(saved, found.Build!.LayoutFile);
        Assert.DoesNotContain(_log, l => l.Contains("cannot list", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_bundled_folder_a_missing_file_is_still_not_recognised()
    {
        var store = new LayoutStore(TestModule, _root, null, null, ReadFile, _log.Add);

        var found = store.Find(A);

        Assert.Null(found.Build);
        Assert.Equal("no layout for this build and no generator", found.Failure);
    }

    /// <summary>A generator scripted with its version answer and what a run does: by default it writes the layout file
    /// for the .wasm it is given, at its version (1 when unknown), and exits 0 with the real generator's lines.</summary>
    private sealed class FakeGenerator : ILayoutGenerator
    {
        private readonly WasmLayoutStoreTests _test;

        public FakeGenerator(WasmLayoutStoreTests test) => _test = test;

        public int? Version { get; init; } = 1;
        public Func<string, string, GeneratorRun>? OnRun { get; init; }
        public int VersionCalls { get; private set; }
        public int RunCalls { get; private set; }
        public (string Wasm, string Root) LastRun { get; private set; }

        public int? AskVersion()
        {
            VersionCalls++;
            return Version;
        }

        public GeneratorRun Run(string wasmFile, string layoutsRoot)
        {
            RunCalls++;
            LastRun = (wasmFile, layoutsRoot);
            return (OnRun ?? Write)(wasmFile, layoutsRoot);
        }

        private GeneratorRun Write(string wasmFile, string layoutsRoot)
        {
            var file = FileFor(_test._installed[wasmFile], Version ?? 1);
            string folder = Path.Combine(layoutsRoot, TestModule.Id);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, file.FileName);
            File.WriteAllText(path, LayoutFileFormat.Write(TestModule, file), new UTF8Encoding(false));
            return new GeneratorRun(0, false, new[] { "first: not recognised: no first table", "second: not recognised: no second table", $"written: {path}" });
        }
    }
}

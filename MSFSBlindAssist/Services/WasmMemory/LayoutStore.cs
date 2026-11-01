using System.Security;
using System.Text;
using MSFSBlindAssist.DisplayLayouts;
using static System.FormattableString;

namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>Where a module session gets each installed file's build: its readers' layouts, or why there are none.</summary>
public interface ILayoutSource
{
    /// <summary>The build of <paramref name="wasmFile"/>, or the reason there is none (the session logs it as "not
    /// recognised: &lt;reason&gt;"). Throws only for an unexpected error, which the readers report as an internal error.</summary>
    LayoutLookup Find(string wasmFile);
}

/// <summary>A build, or the reason there is none.</summary>
public sealed record LayoutLookup(ModuleBuild? Build, string? Failure);

/// <summary>
/// The app's layouts: the files the generator wrote, one per build of a module, in
/// <c>&lt;root&gt;\&lt;module id&gt;\&lt;code hash&gt;-&lt;data hash, 16 characters&gt;.json</c>, in the folder under
/// %APPDATA% and in the bundled folder beside the app. For an installed file it uses, of the files in either folder
/// whose name starts with the file's code hash and that are current, the one with the highest generator version (on a
/// tie the %APPDATA% one, then the first by name). Current means it reads, its format, module and readers are known
/// (<see cref="LayoutFileFormat.TryRead"/>), it fits the installed file (<see cref="LayoutFit.Fits"/>), and its
/// generator version is not older than the generator's when that is known. Any other file counts as missing, never as
/// a partial layout (WASM-26): with a generator the store runs it and looks once more, without one the readers are
/// unavailable. The store only reads, neither folder is ever written by it: the generator alone writes a layout file,
/// and only under %APPDATA%; a run that fails leaves the next load to try again. Every step is logged with a "layouts: "
/// prefix.
/// </summary>
public sealed class LayoutStore : ILayoutSource
{
    private const string NoGenerator = "no layout for this build and no generator";

    private readonly ModuleDescription _module;
    private readonly string _root;
    private readonly string? _bundledRoot;
    private readonly ILayoutGenerator? _generator;
    private readonly Func<string, byte[]> _readFile;
    private readonly Action<string> _log;
    private readonly object _versionGate = new();
    private bool _versionAsked;
    private int? _generatorVersion;

    /// <param name="module">The module whose layouts these are.</param>
    /// <param name="root">The layouts folder; the module's files are in its subfolder named by the module id.</param>
    /// <param name="bundledRoot">The layouts folder shipped beside the app, read only, or null when there is none.</param>
    /// <param name="generator">The generator, or null when the app has none (a public build).</param>
    /// <param name="readFile">Reads a whole file, the installed .wasm and the layout files alike.</param>
    /// <param name="log">The module's log.</param>
    public LayoutStore(ModuleDescription module, string root, string? bundledRoot, ILayoutGenerator? generator, Func<string, byte[]> readFile, Action<string> log)
    {
        _module = module ?? throw new ArgumentNullException(nameof(module));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _bundledRoot = bundledRoot;
        _generator = generator;
        _readFile = readFile ?? throw new ArgumentNullException(nameof(readFile));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>%APPDATA%\MSFSBlindAssist\layouts: one folder for every profile, since a layout belongs to the PMDG build.</summary>
    public static string DefaultRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFSBlindAssist", "layouts");

    /// <summary>The name of the layouts folder shipped beside the exe. The release and preview workflows write that
    /// folder (UPD-10); <c>LayoutBundlerWorkflowTests</c> pins that they use this name.</summary>
    public const string BundledFolderName = "layouts";

    /// <summary>The layouts folder shipped beside the exe, which the store only reads.</summary>
    public static string BundledRoot { get; } = Path.Combine(AppContext.BaseDirectory, BundledFolderName);

    private string Folder => Path.Combine(_root, _module.Id);

    private string FoldersText() => string.Join(" or ", Folders().Select(f => f.Folder));

    public LayoutLookup Find(string wasmFile)
    {
        ArgumentNullException.ThrowIfNull(wasmFile);
        byte[] bytes;
        try
        {
            bytes = _readFile(wasmFile);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return new LayoutLookup(null, $"{ex.GetType().Name}: {ex.Message}");
        }
        if (!WasmFileImage.TryParse(bytes, out var image, out string? notWasm)) return new LayoutLookup(null, notWasm);

        int? version = GeneratorVersion();
        string generator = _generator is null ? "no generator"
            : version is { } v ? Invariant($"generator version {v}")
            : "generator version unknown";
        _log($"layouts: {wasmFile}: code hash {image.CodeHash}; {generator}");

        var found = FindCurrent(image, version);
        bool generated = false;
        if (found is null && _generator is not null)
        {
            _log($"layouts: {wasmFile}: no current layout in {FoldersText()}, running the generator");
            var run = _generator.Run(wasmFile, _root);
            foreach (string line in run.Output) _log($"layouts: generator: {line}");
            string? failure = run.TimedOut ? "the layout generator timed out"
                : run.ExitCode is not { } exitCode ? "the layout generator did not run"
                : exitCode != 0 ? Invariant($"the layout generator failed (exit code {exitCode})")
                : null;
            if (failure is null)
            {
                found = FindCurrent(image, version);
                if (found is null) failure = "the layout generator wrote no layout that fits this build";
            }
            if (failure is not null)
            {
                _log($"layouts: {wasmFile}: {failure}");
                return new LayoutLookup(null, failure);
            }
            generated = true;
        }
        if (found is not { } current)
        {
            _log($"layouts: {wasmFile}: {NoGenerator}");
            return new LayoutLookup(null, NoGenerator);
        }

        var (path, file, bundled) = current;
        _log(Invariant($"layouts: {wasmFile}: using {path} ({(generated ? "generated" : bundled ? "bundled" : "saved")}; generator version {file.GeneratorVersion}, data hash {file.DataHash})"));
        var readers = file.Readers
            .Select(r => r.Layout is null ? r : r with { Layout = _module.Reader(r.Reader).WithSourceFile(r.Layout, wasmFile) })
            .ToList();
        return new LayoutLookup(new ModuleBuild(_module, wasmFile, path, readers), null);
    }

    /// <summary>The generator's version, asked at most once per store; null without a generator or when it gave none.</summary>
    private int? GeneratorVersion()
    {
        if (_generator is null) return null;
        lock (_versionGate)
        {
            if (!_versionAsked)
            {
                _generatorVersion = _generator.AskVersion();
                _versionAsked = true;
            }
            return _generatorVersion;
        }
    }

    /// <summary>Of the current files in both folders, the one with the highest generator version, the %APPDATA% one
    /// first on a tie (then the first by name); each file skipped is logged with its reason.</summary>
    private (string Path, LayoutFile File, bool Bundled)? FindCurrent(WasmFileImage image, int? generatorVersion)
    {
        (string Path, LayoutFile File, bool Bundled)? best = null;
        foreach (var (folder, bundled) in Folders())
        {
            foreach (string path in Candidates(folder, image.CodeHash))
            {
                string? why = WhyNotCurrent(path, image, generatorVersion, out var file);
                if (why is not null)
                {
                    _log($"layouts: skipped {path}: {why}");
                    continue;
                }
                // Strictly higher only, so on a tie the earlier one stays: the %APPDATA% folder, then ordinal name.
                if (best is not { } held || file!.GeneratorVersion > held.File.GeneratorVersion) best = (path, file!, bundled);
            }
        }
        return best;
    }

    /// <summary>The folders to read, the %APPDATA% one first; the bundled one only when there is one.</summary>
    private IEnumerable<(string Folder, bool Bundled)> Folders()
    {
        yield return (Folder, false);
        if (_bundledRoot is not null) yield return (Path.Combine(_bundledRoot, _module.Id), true);
    }

    /// <summary>A folder's <c>&lt;codeHash&gt;-*.json</c> files, sorted ordinal. Nothing else is ever read: not
    /// the .tmp a killed generator leaves, nor any other file. A folder that is missing or cannot be listed has none.</summary>
    private List<string> Candidates(string folder, string codeHash)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(folder)) return new List<string>();
            files = Directory.GetFiles(folder);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            _log($"layouts: cannot list {folder}: {ex.GetType().Name}: {ex.Message}");
            return new List<string>();
        }
        string prefix = codeHash + "-";
        return files
            .Where(f =>
            {
                string name = Path.GetFileName(f);
                return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            })
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Null when the file at <paramref name="path"/> is current, else why not.</summary>
    private string? WhyNotCurrent(string path, WasmFileImage image, int? generatorVersion, out LayoutFile? file)
    {
        file = null;
        string text;
        try
        {
            text = Utf8Text(_readFile(path));
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
        if (!LayoutFileFormat.TryRead(_module, text, out var loaded, out string? unreadable)) return unreadable;
        if (!LayoutFit.Fits(image, loaded, out string? misfit)) return misfit;
        if (generatorVersion is { } newest && loaded.GeneratorVersion < newest)
            return Invariant($"generator version {loaded.GeneratorVersion} is older than {newest}");
        file = loaded;
        return null;
    }

    /// <summary>The bytes as UTF-8 text, a byte order mark (which the generator never writes) dropped.</summary>
    private static string Utf8Text(byte[] bytes)
    {
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return bom ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3) : Encoding.UTF8.GetString(bytes);
    }

    private static bool IsReadFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or SecurityException;
}

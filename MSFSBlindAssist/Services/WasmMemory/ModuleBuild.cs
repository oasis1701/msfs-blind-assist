using MSFSBlindAssist.DisplayLayouts;

namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>
/// One distinct build of a WASM module's file: each of the module's readers' results (a layout, or the reason there
/// is none), loaded from one layout file and named by the first installed file holding the build. A reader that
/// failed keeps its reason for the log, and never stops another.
/// </summary>
public sealed class ModuleBuild
{
    private readonly Dictionary<string, ReaderResult> _byReader;

    /// <param name="module">The module the build is of.</param>
    /// <param name="sourceFile">The installed .wasm file; each layout carries it too.</param>
    /// <param name="layoutFile">The layout file the results came from: two builds are the same when it is.</param>
    /// <param name="readers">One result per reader of <paramref name="module"/>, each once.</param>
    /// <exception cref="ArgumentException">A result is for no reader of the module, or for one twice.</exception>
    public ModuleBuild(ModuleDescription module, string sourceFile, string layoutFile, IReadOnlyList<ReaderResult> readers)
    {
        Module = module ?? throw new ArgumentNullException(nameof(module));
        SourceFile = sourceFile ?? throw new ArgumentNullException(nameof(sourceFile));
        LayoutFile = layoutFile ?? throw new ArgumentNullException(nameof(layoutFile));
        Readers = readers ?? throw new ArgumentNullException(nameof(readers));

        _byReader = new Dictionary<string, ReaderResult>(StringComparer.Ordinal);
        foreach (var result in readers)
        {
            if (!module.Readers.Any(r => r.Id == result.Reader))
                throw new ArgumentException($"reader {result.Reader} is not one of module {module.Id}'s", nameof(readers));
            if (!_byReader.TryAdd(result.Reader, result))
                throw new ArgumentException($"reader {result.Reader} is given twice", nameof(readers));
        }

        var anchors = new List<ModuleAnchor>(module.Readers.Count);
        foreach (var reader in module.Readers)
        {
            if (_byReader.TryGetValue(reader.Id, out var result) && result.Layout is { } layout)
                anchors.Add(reader.AnchorOf(layout));
        }
        Anchors = anchors;
    }

    public ModuleDescription Module { get; }

    public string SourceFile { get; }

    public string LayoutFile { get; }

    public IReadOnlyList<ReaderResult> Readers { get; }

    /// <summary>Each derived reader's anchor, in the module's reader order: the search tries them in order.</summary>
    public IReadOnlyList<ModuleAnchor> Anchors { get; }

    /// <summary>The reader's layout, or null when it has none.</summary>
    /// <exception cref="KeyNotFoundException">The module has no such reader.</exception>
    /// <exception cref="InvalidCastException">The reader's layout is not a <typeparamref name="T"/>.</exception>
    public T? Layout<T>(string reader) where T : class
    {
        var layout = Result(reader)?.Layout;
        return layout is null ? null : (T)layout;
    }

    /// <summary>Why the reader has no layout; null when it has one.</summary>
    /// <exception cref="KeyNotFoundException">The module has no such reader.</exception>
    public string? Failure(string reader) => Result(reader)?.Failure;

    /// <summary>True when both came from the same layout file (the same build installed twice) and at least one reader
    /// derived a layout.</summary>
    public bool SameLayoutsAs(ModuleBuild other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Anchors.Count > 0 && string.Equals(LayoutFile, other.LayoutFile, StringComparison.OrdinalIgnoreCase);
    }

    private ReaderResult? Result(string reader)
    {
        Module.Reader(reader); // throws for a reader the module does not have
        return _byReader.GetValueOrDefault(reader);
    }
}

namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>One display reader of a WASM module, seen without its layout type: the file finder, the layout store
/// and the generator work through this.</summary>
public abstract class LayoutReader
{
    protected LayoutReader(string id, string logName)
    {
        Id = id;
        LogName = logName;
    }

    /// <summary>The reader's stable id, such as "fma".</summary>
    public string Id { get; }

    /// <summary>The name today's log lines give the reader, such as "FMA".</summary>
    public string LogName { get; }

    public abstract Type LayoutType { get; }
    public abstract ModuleAnchor AnchorOf(object layout);
    public abstract object WithSourceFile(object layout, string sourceFile);
    public abstract IReadOnlyList<string> Describe(object layout);
    public abstract IReadOnlyList<(string Part, string Reason)> PartFailures(object layout);
}

public sealed class LayoutReader<T> : LayoutReader where T : class
{
    private readonly Func<T, ModuleAnchor> _anchorOf;
    private readonly Func<T, string, T> _withSourceFile;
    private readonly Func<T, IReadOnlyList<string>> _describe;
    private readonly Func<T, IReadOnlyList<(string Part, string Reason)>>? _partFailures;

    public LayoutReader(string id, string logName, Func<T, ModuleAnchor> anchorOf, Func<T, string, T> withSourceFile,
        Func<T, IReadOnlyList<string>> describe, Func<T, IReadOnlyList<(string Part, string Reason)>>? partFailures = null)
        : base(id, logName)
    {
        _anchorOf = anchorOf;
        _withSourceFile = withSourceFile;
        _describe = describe;
        _partFailures = partFailures;
    }

    public override Type LayoutType => typeof(T);
    public override ModuleAnchor AnchorOf(object layout) => _anchorOf((T)layout);
    public override object WithSourceFile(object layout, string sourceFile) => _withSourceFile((T)layout, sourceFile);
    public override IReadOnlyList<string> Describe(object layout) => _describe((T)layout);

    public override IReadOnlyList<(string Part, string Reason)> PartFailures(object layout) =>
        _partFailures is null ? Array.Empty<(string, string)>() : _partFailures((T)layout);
}

/// <summary>Where a WASM module's file lives and which readers it has.</summary>
/// <param name="Id">The module's stable id, such as "pmdg737".</param>
/// <param name="WasmFileName">The file's name, such as 737NG3.wasm.</param>
/// <param name="PackageNamePattern">A regex (ignore case) the package folder's name must match.</param>
/// <param name="PathInPackage">The folders from the package to the file's folder; a segment holding * is a folder pattern.</param>
/// <param name="Readers">The readers, in anchor-search order.</param>
public sealed record ModuleDescription(string Id, string WasmFileName, string PackageNamePattern,
    IReadOnlyList<string> PathInPackage, IReadOnlyList<LayoutReader> Readers)
{
    /// <summary>The reader with this id; KeyNotFoundException when there is none.</summary>
    public LayoutReader Reader(string id) =>
        Readers.FirstOrDefault(r => r.Id == id) ?? throw new KeyNotFoundException($"module {Id} has no reader '{id}'");
}

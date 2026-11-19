namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>Every WASM module the app knows.</summary>
public static class ModuleCatalog
{
    public static IReadOnlyList<ModuleDescription> All { get; } = new[] { Pmdg737Module.Description, Bbs757Module.Description };

    /// <summary>The module whose file name is the last segment of <paramref name="path"/> (ordinal, ignore case); null when none.</summary>
    public static ModuleDescription? ForFileName(string path)
    {
        string name = Path.GetFileName(path);
        return All.FirstOrDefault(m => string.Equals(m.WasmFileName, name, StringComparison.OrdinalIgnoreCase));
    }
}

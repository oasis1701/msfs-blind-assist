namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>Finds a WASM module's linear memory in the sim process by a known byte anchor.</summary>
public static class WasmMemoryLocator
{
    /// <summary>
    /// The allocation base whose bytes at <paramref name="anchorAddress"/> equal <paramref name="anchor"/>,
    /// considering only committed, <c>MEM_PRIVATE</c>, readable allocations. Null unless exactly one matches.
    /// </summary>
    public static ulong? FindLinearBase(IProcessMemorySource src, uint anchorAddress, ReadOnlySpan<byte> anchor)
    {
        var seen = new HashSet<ulong>();
        var buffer = new byte[anchor.Length];
        ulong? found = null;
        int matches = 0;
        foreach (var region in src.Regions())
        {
            if (!region.IsCommitted || !region.IsPrivate || !region.IsReadable) continue;
            if (!seen.Add(region.AllocationBase)) continue;
            if (!new ModuleMemory(src, region.AllocationBase).IsInsideModule(anchorAddress, (uint)anchor.Length)) continue;
            if (!src.TryRead(region.AllocationBase + anchorAddress, buffer)) continue;
            if (!anchor.SequenceEqual(buffer)) continue;
            found = region.AllocationBase;
            matches++;
        }
        return matches == 1 ? found : null;
    }
}

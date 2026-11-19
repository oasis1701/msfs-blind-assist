namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>One run of pages from VirtualQueryEx. IsReadable means plain read/write-style pages without PAGE_GUARD.</summary>
public readonly record struct MemoryRegion(ulong BaseAddress, ulong AllocationBase, ulong Size, bool IsCommitted, bool IsPrivate, bool IsReadable);

/// <summary>
/// A read-only view of another process's memory. The only operations are walking regions and reading
/// bytes; nothing can write, pause or alter the process. Tests supply a fake.
/// </summary>
public interface IProcessMemorySource : IDisposable
{
    IEnumerable<MemoryRegion> Regions();

    /// <summary>True only when every requested byte was read; never throws.</summary>
    bool TryRead(ulong address, Span<byte> into);

    /// <summary>The region containing this address, or null when there is none.</summary>
    MemoryRegion? RegionAt(ulong address);
}

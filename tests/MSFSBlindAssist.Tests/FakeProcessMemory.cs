using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Tests;

/// <summary>An in-memory IProcessMemorySource: regions with optional backing bytes, and addresses whose reads fail.</summary>
internal sealed class FakeProcessMemory : IProcessMemorySource
{
    private readonly List<(MemoryRegion Region, byte[]? Bytes)> _regions = new();
    private readonly List<(ulong Start, ulong End)> _failing = new();

    public FakeProcessMemory Add(MemoryRegion region, byte[]? bytes = null)
    {
        _regions.Add((region, bytes));
        return this;
    }

    public FakeProcessMemory FailReadsAt(ulong start, ulong length)
    {
        _failing.Add((start, start + length));
        return this;
    }

    /// <summary>Changes the region that starts at this address (PMDG committing memory after start).</summary>
    public FakeProcessMemory Replace(ulong baseAddress, MemoryRegion region, byte[]? bytes = null)
    {
        int k = _regions.FindIndex(r => r.Region.BaseAddress == baseAddress);
        _regions[k] = (region, bytes ?? _regions[k].Bytes);
        return this;
    }

    /// <summary>How many times RegionAt was asked (VirtualQueryEx on the real process: ~200 µs each).</summary>
    public int RegionQueries { get; private set; }

    public IEnumerable<MemoryRegion> Regions() => _regions.Select(r => r.Region);

    public MemoryRegion? RegionAt(ulong address)
    {
        RegionQueries++;
        foreach (var (r, _) in _regions)
            if (address >= r.BaseAddress && address < r.BaseAddress + r.Size) return r;
        return null;
    }

    public bool TryRead(ulong address, Span<byte> into)
    {
        ulong end = address + (ulong)into.Length;
        foreach (var (s, e) in _failing)
            if (address < e && end > s) return false;
        foreach (var (r, bytes) in _regions)
        {
            if (address < r.BaseAddress || end > r.BaseAddress + r.Size) continue;
            if (bytes == null) return false;
            ulong offset = address - r.BaseAddress;
            if (offset + (ulong)into.Length > (ulong)bytes.Length) return false;
            bytes.AsSpan((int)offset, into.Length).CopyTo(into);
            return true;
        }
        return false;
    }

    public void Dispose() { }
}

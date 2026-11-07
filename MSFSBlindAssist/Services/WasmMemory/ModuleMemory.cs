using System.Buffers.Binary;

namespace MSFSBlindAssist.Services.WasmMemory;

/// <summary>
/// Reads at module (wasm) addresses inside a located linear memory. Every read is bounds-checked
/// against the process's current regions, re-walked each call, so nothing is captured once; inside a
/// <see cref="RememberRegions"/> scope (one reading) each region is asked about once. Does not own the source.
/// </summary>
public sealed class ModuleMemory
{
    private readonly IProcessMemorySource _src;
    private readonly ulong _linearBase;

    public ModuleMemory(IProcessMemorySource src, ulong linearBase)
    {
        _src = src;
        _linearBase = linearBase;
    }

    /// <summary>
    /// Remembers each region's answer until the returned scope is disposed, on the calling thread only, so one
    /// reading asks Windows once per region instead of for every field (VirtualQueryEx costs ~200 µs on the sim,
    /// a read ~1 µs: live, 2026-10-25). For ONE reading only, never kept across readings: PMDG commits memory
    /// after start (WASM-4). A region found missing is asked again. A scope inside a scope shares the outer one.
    /// </summary>
    public IDisposable RememberRegions()
    {
        if (_remembered is not null) return new Forget(() => { });
        // The thread first: another thread never sees the list with the previous scope's thread.
        _rememberingThread = Environment.CurrentManagedThreadId;
        _remembered = new List<MemoryRegion>();
        return new Forget(() => _remembered = null);
    }

    private List<MemoryRegion>? _remembered;
    private int _rememberingThread;

    private sealed class Forget(Action forget) : IDisposable
    {
        public void Dispose() => forget();
    }

    private MemoryRegion? RegionAt(ulong address)
    {
        if (_remembered is not { } remembered || _rememberingThread != Environment.CurrentManagedThreadId) return _src.RegionAt(address);
        foreach (var r in remembered)
            if (address >= r.BaseAddress && address < r.BaseAddress + r.Size) return r;
        var found = _src.RegionAt(address);
        if (found is { } f) remembered.Add(f);
        return found;
    }

    public bool TryReadInt32(uint wasmAddress, out int value)
    {
        value = 0;
        if (!IsInsideModule(wasmAddress, 4)) return false;
        Span<byte> b = stackalloc byte[4];
        if (!_src.TryRead(_linearBase + wasmAddress, b)) return false;
        value = BinaryPrimitives.ReadInt32LittleEndian(b);
        return true;
    }

    public bool TryReadDouble(uint wasmAddress, out double value)
    {
        value = 0;
        if (!IsInsideModule(wasmAddress, 8)) return false;
        Span<byte> b = stackalloc byte[8];
        if (!_src.TryRead(_linearBase + wasmAddress, b)) return false;
        value = BinaryPrimitives.ReadDoubleLittleEndian(b);
        return true;
    }

    public bool TryReadByte(uint wasmAddress, out byte value)
    {
        value = 0;
        if (!IsInsideModule(wasmAddress, 1)) return false;
        Span<byte> b = stackalloc byte[1];
        if (!_src.TryRead(_linearBase + wasmAddress, b)) return false;
        value = b[0];
        return true;
    }

    /// <summary>Reads <paramref name="into"/>'s length from this address when every byte lies inside the module,
    /// one read per region (a block such as a route node may straddle two).</summary>
    public bool TryRead(uint wasmAddress, Span<byte> into)
    {
        if (!IsInsideModule(wasmAddress, (uint)into.Length)) return false;
        ulong cursor = _linearBase + wasmAddress;
        int done = 0;
        while (done < into.Length)
        {
            if (RegionAt(cursor) is not { } r) return false;
            int take = (int)Math.Min((ulong)(into.Length - done), r.BaseAddress + r.Size - cursor);
            if (take <= 0 || !_src.TryRead(cursor, into.Slice(done, take))) return false;
            done += take;
            cursor += (ulong)take;
        }
        return true;
    }

    /// <summary>True when the module's memory still holds exactly <paramref name="expected"/> at this address
    /// (an anchor: false once the module moved or the process went away).</summary>
    public bool Matches(uint wasmAddress, ReadOnlySpan<byte> expected)
    {
        if (!IsInsideModule(wasmAddress, (uint)expected.Length)) return false;
        var buffer = new byte[expected.Length];
        return _src.TryRead(_linearBase + wasmAddress, buffer) && expected.SequenceEqual(buffer);
    }

    /// <summary>True when every byte of the range lies in committed, readable regions of this module's allocation.</summary>
    public bool IsInsideModule(uint wasmAddress, uint length)
    {
        ulong cursor = _linearBase + wasmAddress;
        ulong end = cursor + length;
        while (cursor < end)
        {
            var region = RegionAt(cursor);
            if (region is not { } r) return false;
            if (!r.IsCommitted || !r.IsReadable || r.AllocationBase != _linearBase) return false;
            ulong regionEnd = r.BaseAddress + r.Size;
            if (regionEnd <= cursor) return false;
            cursor = regionEnd;
        }
        return true;
    }
}

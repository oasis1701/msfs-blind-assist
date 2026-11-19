using MSFSBlindAssist.Services.WasmMemory;

namespace MSFSBlindAssist.Services.PMDG;

/// <summary>Reads one FMA sample from the 737 module's memory through a derived layout. Owns its memory source.</summary>
public sealed class ModuleFmaSampler : IFmaSampler
{
    private readonly ModuleMemory _memory;
    private readonly IDisposable _ownedSource;

    public Pmdg737FmaLayout Layout { get; }

    public ModuleFmaSampler(ModuleMemory memory, IDisposable ownedSource, Pmdg737FmaLayout layout)
    {
        _memory = memory;
        _ownedSource = ownedSource;
        Layout = layout;
    }

    public bool TryRead(out FmaSample sample)
    {
        sample = default;
        var l = Layout;
        if (!_memory.TryReadInt32(l.InstanceGlobal, out int pi)) return false;
        if (!_memory.TryReadInt32(l.DisplayGlobal, out int di)) return false;
        uint p = (uint)pi;
        if (p == 0 || !_memory.IsInsideModule(p, l.ObjectSize)) return false;

        if (!Int(p, l.FmaAt, out int at) || !Int(p, l.Roll, out int roll) || !Int(p, l.RollArmed, out int rollArmed)
            || !Int(p, l.Pitch, out int pitch) || !Int(p, l.PitchArmed, out int pitchArmed) || !Int(p, l.FmaAp, out int status)
            || !Byte(p, l.Fd1, out byte fd1) || !Byte(p, l.Fd2, out byte fd2)
            || !Int(p, l.McpAlt, out int alt) || !Int(p, l.McpHdg, out int hdg))
            return false;

        sample = new FmaSample(new FmaState(at, roll, rollArmed, pitch, pitchArmed, status),
            fd1 != 0, fd2 != 0, alt, hdg, (uint)di == p);
        return true;
    }

    private bool Int(uint p, int offset, out int v) => _memory.TryReadInt32(p + (uint)offset, out v);
    private bool Byte(uint p, int offset, out byte v) => _memory.TryReadByte(p + (uint)offset, out v);

    public void Dispose() => _ownedSource.Dispose();
}

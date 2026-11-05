using System.Buffers.Binary;
using MSFSBlindAssist.Services.PMDG;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class ModuleFmaSamplerTests
{
    private const ulong B = 0x10000000;
    private const uint P = 0x2000;

    private static readonly Pmdg737FmaLayout Layout = GoldenLayouts.Fma with
    {
        InstanceGlobal = 0x100, DisplayGlobal = 0x104, ObjectSize = 0x400,
        FmaAp = 0x10, FmaAt = 0x14, Pitch = 0x18, PitchArmed = 0x1C, Roll = 0x20, RollArmed = 0x24,
        Fd1 = 0x30, Fd2 = 0x31, McpAlt = 0x34, McpHdg = 0x38,
    };

    private static void Put(byte[] b, uint at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan((int)at), v);

    private static byte[] Memory(int instance = (int)P, int display = (int)P)
    {
        var b = new byte[0x4000];
        Put(b, 0x100, instance);
        Put(b, 0x104, display);
        Put(b, P + 0x10, 1); Put(b, P + 0x14, 2); Put(b, P + 0x18, 3);
        Put(b, P + 0x1C, 4); Put(b, P + 0x20, 5); Put(b, P + 0x24, 6);
        b[P + 0x30] = 1; b[P + 0x31] = 0;
        Put(b, P + 0x34, 12000); Put(b, P + 0x38, 270);
        return b;
    }

    private static ModuleFmaSampler Sampler(byte[] bytes)
    {
        var src = new FakeProcessMemory().Add(new MemoryRegion(B, B, (ulong)bytes.Length, true, true, true), bytes);
        return new ModuleFmaSampler(new ModuleMemory(src, B), src, Layout);
    }

    [Fact]
    public void Reads_fields_at_object_offsets()
    {
        using var s = Sampler(Memory());
        Assert.True(s.TryRead(out var r));
        Assert.Equal(new FmaState(At: 2, Roll: 5, RollArmed: 6, Pitch: 3, PitchArmed: 4, Status: 1), r.Fma);
        Assert.True(r.Fd1);
        Assert.False(r.Fd2);
        Assert.Equal(12000, r.McpAlt);
        Assert.Equal(270, r.McpHdg);
        Assert.True(r.PointersAgree);
        Assert.Same(Layout, s.Layout);
    }

    [Fact]
    public void Null_pointer_is_a_failed_read()
    {
        using var s = Sampler(Memory(instance: 0));
        Assert.False(s.TryRead(out var r));
        Assert.Equal(default, r);
    }

    [Fact]
    public void Object_outside_module_is_a_failed_read()
    {
        using var s = Sampler(Memory(instance: 0x3F00)); // object runs past the 0x4000 region
        Assert.False(s.TryRead(out var r));
        Assert.Equal(default, r);
    }

    [Fact]
    public void Display_pointer_differs_sets_PointersAgree_false()
    {
        using var s = Sampler(Memory(display: 0x3000));
        Assert.True(s.TryRead(out var r));
        Assert.False(r.PointersAgree);
    }

    private sealed class Probe : IProcessMemorySource
    {
        public bool Disposed;
        public IEnumerable<MemoryRegion> Regions() => Array.Empty<MemoryRegion>();
        public MemoryRegion? RegionAt(ulong address) => null;
        public bool TryRead(ulong address, Span<byte> into) => false;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void Dispose_disposes_owned_source()
    {
        var p = new Probe();
        new ModuleFmaSampler(new ModuleMemory(p, B), p, Layout).Dispose();
        Assert.True(p.Disposed);
    }
}

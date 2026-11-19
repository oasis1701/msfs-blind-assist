using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

public class WasmMemoryLocatorTests
{
    private const uint AnchorAddress = 0x366ac;
    private static readonly byte[] Anchor = { 0x50, 0x4D, 0x44, 0x47, 0x01, 0x02, 0x03, 0x04 };

    private static byte[] WithAnchor(int size = 0x40000)
    {
        var b = new byte[size];
        Anchor.CopyTo(b, (int)AnchorAddress);
        return b;
    }

    private static MemoryRegion Region(ulong baseAddr, ulong size, ulong allocBase, bool committed = true, bool priv = true, bool readable = true)
        => new(baseAddr, allocBase, size, committed, priv, readable);

    [Fact]
    public void Finds_anchor_at_allocation_base()
    {
        var src = new FakeProcessMemory().Add(Region(0x10000000, 0x40000, 0x10000000), WithAnchor());
        Assert.Equal(0x10000000UL, WasmMemoryLocator.FindLinearBase(src, AnchorAddress, Anchor));
    }

    [Fact]
    public void Ignores_image_regions()
    {
        var src = new FakeProcessMemory().Add(Region(0x10000000, 0x40000, 0x10000000, priv: false), WithAnchor());
        Assert.Null(WasmMemoryLocator.FindLinearBase(src, AnchorAddress, Anchor));
    }

    [Fact]
    public void Two_matches_are_ambiguous()
    {
        var src = new FakeProcessMemory()
            .Add(Region(0x10000000, 0x40000, 0x10000000), WithAnchor())
            .Add(Region(0x20000000, 0x40000, 0x20000000), WithAnchor());
        Assert.Null(WasmMemoryLocator.FindLinearBase(src, AnchorAddress, Anchor));
    }

    [Fact]
    public void Unreadable_allocations_are_skipped()
    {
        var src = new FakeProcessMemory()
            .Add(Region(0x10000000, 0x40000, 0x10000000), WithAnchor())
            .Add(Region(0x20000000, 0x40000, 0x20000000), WithAnchor())
            .FailReadsAt(0x10000000UL + AnchorAddress, 8);
        Assert.Equal(0x20000000UL, WasmMemoryLocator.FindLinearBase(src, AnchorAddress, Anchor));
    }

    [Fact]
    public void Anchor_beyond_a_small_allocation_is_not_read_from_a_following_image()
    {
        var src2 = new FakeProcessMemory()
            .Add(Region(0x10000000, 0x10000, 0x10000000), new byte[0x10000])
            .Add(Region(0x10010000, 0x40000, 0x10010000, priv: false), WithAnchor(0x40000));
        // the image holds the anchor exactly where base + 0x366ac lands
        Assert.Null(WasmMemoryLocator.FindLinearBase(src2, 0x10000 + AnchorAddress, Anchor));
    }

    [Fact]
    public void Allocation_made_of_two_regions_counts_once()
    {
        const ulong b = 0x10000000;
        var src = new FakeProcessMemory()
            .Add(Region(b, 0x40000, b), WithAnchor())
            .Add(Region(b + 0x40000, 0x10000, b), new byte[0x10000]);
        Assert.Equal(b, WasmMemoryLocator.FindLinearBase(src, AnchorAddress, Anchor));
    }

    [Fact]
    public void Region_of_a_different_allocation_or_unreadable_is_not_inside()
    {
        const ulong b = 0x10000000;
        var other = new FakeProcessMemory()
            .Add(Region(b, 0x1000, b), new byte[0x1000])
            .Add(Region(b + 0x1000, 0x1000, 0x20000000), new byte[0x1000]);
        Assert.False(new ModuleMemory(other, b).IsInsideModule(0x1000, 4));
        var unreadable = new FakeProcessMemory()
            .Add(Region(b, 0x1000, b), new byte[0x1000])
            .Add(Region(b + 0x1000, 0x1000, b, readable: false), new byte[0x1000]);
        Assert.False(new ModuleMemory(unreadable, b).IsInsideModule(0x1000, 4));
    }

    [Fact]
    public void Uncommitted_or_unreadable_regions_are_not_candidates()
    {
        var src = new FakeProcessMemory()
            .Add(Region(0x10000000, 0x40000, 0x10000000, committed: false), WithAnchor())
            .Add(Region(0x20000000, 0x40000, 0x20000000, readable: false), WithAnchor());
        Assert.Null(WasmMemoryLocator.FindLinearBase(src, AnchorAddress, Anchor));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Object_in_later_committed_region_is_inside(bool secondCommitted, bool expected)
    {
        const ulong b = 0x10000000;
        var src = new FakeProcessMemory()
            .Add(Region(b, 0x280000, b), WithAnchor(0x280000))
            .Add(Region(b + 0x6bd000, 0x10000, b, committed: secondCommitted));
        var mem = new ModuleMemory(src, b);
        Assert.Equal(expected, mem.IsInsideModule(0x6bd740, 19768));
        Assert.True(mem.IsInsideModule(0x100, 16));
        Assert.False(mem.IsInsideModule(0x27fff8, 16)); // runs into the gap
    }

    [Fact]
    public void Bounds_check_sees_memory_committed_after_construction()
    {
        const ulong b = 0x10000000;
        var src = new FakeProcessMemory().Add(Region(b, 0x1000, b), new byte[0x1000]);
        var mem = new ModuleMemory(src, b);
        Assert.False(mem.IsInsideModule(0x2000, 4));
        src.Add(Region(b + 0x2000, 0x1000, b), new byte[0x1000]);
        Assert.True(mem.IsInsideModule(0x2000, 4));
    }

    [Fact]
    public void Reads_values_inside_and_refuses_outside()
    {
        const ulong b = 0x10000000;
        var bytes = new byte[0x1000];
        bytes[0x10] = 0x78; bytes[0x11] = 0x56; bytes[0x12] = 0x34; bytes[0x13] = 0x12;
        var src = new FakeProcessMemory().Add(Region(b, 0x1000, b), bytes);
        var mem = new ModuleMemory(src, b);
        Assert.True(mem.TryReadInt32(0x10, out int v));
        Assert.Equal(0x12345678, v);
        Assert.True(mem.TryReadByte(0x10, out byte by));
        Assert.Equal(0x78, by);
        Assert.False(mem.TryReadInt32(0xFFE, out _));
        Assert.False(mem.TryReadByte(0x5000, out _));
    }

    [Fact]
    public void Reads_a_double_inside_and_refuses_one_crossing_the_end()
    {
        const ulong b = 0x10000000;
        var bytes = new byte[0x1000];
        BitConverter.TryWriteBytes(bytes.AsSpan(0x20), 8628.594);
        var src = new FakeProcessMemory().Add(Region(b, 0x1000, b), bytes);
        var mem = new ModuleMemory(src, b);
        Assert.True(mem.TryReadDouble(0x20, out double v));
        Assert.Equal(8628.594, v);
        Assert.False(mem.TryReadDouble(0xFFC, out _));
    }

    [Fact]
    public void TryRead_spans_regions_inside_the_module()
    {
        const ulong b = 0x10000000;
        var first = new byte[0x1000];
        var second = new byte[0x1000];
        new byte[] { 1, 2, 3, 4 }.CopyTo(first, 0xFFC);
        new byte[] { 5, 6, 7, 8 }.CopyTo(second, 0);
        var src = new FakeProcessMemory().Add(Region(b, 0x1000, b), first).Add(Region(b + 0x1000, 0x1000, b), second);
        var mem = new ModuleMemory(src, b);

        var into = new byte[8];
        Assert.True(mem.TryRead(0xFFC, into));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, into);
    }

    [Fact]
    public void TryRead_outside_the_module_fails()
    {
        const ulong b = 0x10000000;
        var src = new FakeProcessMemory().Add(Region(b, 0x1000, b), new byte[0x1000]).Add(Region(b + 0x1000, 0x1000, 0x20000000), new byte[0x1000]);
        var mem = new ModuleMemory(src, b);

        Assert.True(mem.TryRead(0x10, new byte[16]));
        Assert.False(mem.TryRead(0xFFC, new byte[8])); // runs into another allocation
        Assert.False(mem.TryRead(0x3000, new byte[4])); // nothing there
    }

    // VirtualQueryEx costs ~200 µs on the sim and a read ~1 µs (live, 2026-10-25): a reading of many objects
    // remembers each region's answer until it ends.
    [Fact]
    public void Remembered_regions_are_asked_once_while_remembering()
    {
        const ulong b = 0x10000000;
        var src = new FakeProcessMemory().Add(Region(b, 0x1000, b), new byte[0x1000]).Add(Region(b + 0x1000, 0x1000, b), new byte[0x1000]);
        var mem = new ModuleMemory(src, b);

        using (mem.RememberRegions())
        {
            for (uint k = 0; k < 10; k++)
            {
                Assert.True(mem.TryReadInt32(0x10 + 4 * k, out _));
                Assert.True(mem.TryRead(0xFF0 + k, new byte[16])); // across both regions
                Assert.True(mem.IsInsideModule(0x1800, 8));
            }
        }
        Assert.Equal(2, src.RegionQueries);

        Assert.True(mem.TryReadInt32(0x10, out _));
        Assert.Equal(3, src.RegionQueries); // not remembered: asked again
    }

    [Fact]
    public void Remembered_regions_are_forgotten_when_the_reading_ends()
    {
        // PMDG commits memory after start (WASM-4): a region asked about in one reading is asked again in the next.
        const ulong b = 0x10000000;
        var src = new FakeProcessMemory().Add(Region(b, 0x1000, b), new byte[0x1000]).Add(Region(b + 0x1000, 0x1000, b, committed: false), new byte[0x1000]);
        var mem = new ModuleMemory(src, b);

        using (mem.RememberRegions())
        {
            Assert.False(mem.IsInsideModule(0x1800, 4));
            src.Replace(b + 0x1000, Region(b + 0x1000, 0x1000, b));
            Assert.False(mem.IsInsideModule(0x1800, 4)); // the same reading keeps its answer
        }
        using (mem.RememberRegions()) Assert.True(mem.IsInsideModule(0x1800, 4));
    }

    [Fact]
    public void A_region_found_missing_is_asked_again_while_remembering()
    {
        const ulong b = 0x10000000;
        var src = new FakeProcessMemory().Add(Region(b, 0x1000, b), new byte[0x1000]);
        var mem = new ModuleMemory(src, b);

        using (mem.RememberRegions())
        {
            Assert.False(mem.IsInsideModule(0x1800, 4));
            src.Add(Region(b + 0x1000, 0x1000, b), new byte[0x1000]);
            Assert.True(mem.IsInsideModule(0x1800, 4));
        }
    }

    [Fact]
    public void Remembering_holds_only_on_the_thread_that_asked()
    {
        const ulong b = 0x10000000;
        var src = new FakeProcessMemory().Add(Region(b, 0x1000, b), new byte[0x1000]);
        var mem = new ModuleMemory(src, b);

        using (mem.RememberRegions())
        {
            Assert.True(mem.IsInsideModule(0x10, 4));
            int before = src.RegionQueries;
            // A thread of its own (Task.Wait may run a task inline on the waiting thread).
            bool inside = false;
            var other = new Thread(() => inside = mem.IsInsideModule(0x10, 4));
            other.Start();
            other.Join();
            Assert.True(inside);
            Assert.Equal(before + 1, src.RegionQueries);
        }
    }
}

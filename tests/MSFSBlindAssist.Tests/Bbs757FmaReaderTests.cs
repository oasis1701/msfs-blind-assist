using System.Buffers.Binary;
using MSFSBlindAssist.Services.BBS757;
using MSFSBlindAssist.Services.WasmMemory;
using Xunit;

namespace MSFSBlindAssist.Tests;

/// <summary>The 757 FMA reader finds the Captain PFD in the gauge manager by its vptr, follows it to the
/// autoflight and thrust objects, and reads each column's word through the layout; from the autoflight object it reads
/// the AFDS status, the two F/D bytes and, through its MCP pointer, the MCP's selected altitude and heading.</summary>
public class Bbs757FmaReaderTests
{
    internal const ulong Base = 0x20000000;
    internal const uint Manager = 0x1000, Mcp = 0x3000, CaptPfd = 0x4000, FoPfd = 0x4800, Afds = 0x5000, Tmc = 0x6000;
    internal const int Size = 0x8000;
    private const uint PfdVptr = 0x1406c;

    internal static readonly Bbs757FmaLayout Layout = new(
        SourceFile: "", AnchorAddress: 0x400, Anchor: new byte[] { 9, 9, 9, 9 },
        ManagerBase: Manager, ManagerSpan: 0x100, PfdVptr: PfdVptr, PfdToAfds: 612, PfdToTmc: 616,
        Pitch: 11924, PitchArmed: 11944, Roll: 11932, RollArmed: 11940, AtMode: 444,
        AtWords: new Bbs757WordTable(new Dictionary<int, string> { [1] = "SPD", [2] = "EPR", [5] = "FLCH" }),
        PitchWords: new Bbs757WordTable(new Dictionary<int, string> { [1] = "TO", [6] = "VNAV PTH" }),
        PitchArmedWords: new Bbs757WordTable(new Dictionary<int, string> { [9] = "G/S" }),
        RollWords: new Bbs757WordTable(new Dictionary<int, string> { [5] = "LNAV", [8] = "TO" }),
        RollArmedWords: new Bbs757WordTable(new Dictionary<int, string> { [3] = "LOC" }),
        Status: 11948, StatusWords: new Bbs757WordTable(new Dictionary<int, string> { [1] = "CMD" }), FdWord: "FD",
        FdCaptain: 11957, FdFirstOfficer: 11958, AfdsToMcp: 11848, McpAltitude: 856, McpHeading: 848,
        McpValueType: Bbs757McpValueType.F64);

    internal static void Put(byte[] b, uint at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan((int)at), v);

    internal static void PutDouble(byte[] b, uint at, double v) => BinaryPrimitives.WriteDoubleLittleEndian(b.AsSpan((int)at), v);

    /// <summary>One module's memory with the FMA: by default the Captain's F/D on, the First Officer's off, CMD, and the
    /// MCP at 35,000 ft and heading 65 (f64, as this build stores them).</summary>
    internal static byte[] Memory(int at = 1, int pitch = 1, int pitchArmed = 9, int roll = 5, int rollArmed = 3,
        int status = 1, byte fdCaptain = 1, byte fdFirstOfficer = 0, double altitude = 35000, double heading = 65,
        bool captainVptr = true, bool foFirst = false, uint mcp = Mcp)
    {
        var b = new byte[Size];
        // Manager: a couple of null slots, then the FO pfd then the Captain pfd (or FO first if asked), then
        // the autoflight/thrust pointers hanging off the Captain pfd.
        Put(b, Manager + 0x08, foFirst ? (int)FoPfd : (int)CaptPfd);
        Put(b, Manager + 0x10, foFirst ? (int)CaptPfd : (int)FoPfd);
        Put(b, CaptPfd, captainVptr ? (int)PfdVptr : 0x9999);
        Put(b, FoPfd, (int)PfdVptr);
        Put(b, CaptPfd + 612, (int)Afds);
        Put(b, CaptPfd + 616, (int)Tmc);
        Put(b, FoPfd + 612, (int)Afds);
        Put(b, FoPfd + 616, (int)Tmc);
        Put(b, Tmc + 444, at);
        Put(b, Afds + 11924, pitch);
        Put(b, Afds + 11944, pitchArmed);
        Put(b, Afds + 11932, roll);
        Put(b, Afds + 11940, rollArmed);
        Put(b, Afds + 11948, status);
        b[Afds + 11957] = fdCaptain;
        b[Afds + 11958] = fdFirstOfficer;
        Put(b, Afds + 11848, (int)mcp);
        if (mcp + 864 <= Size)
        {
            PutDouble(b, mcp + 856, altitude);
            PutDouble(b, mcp + 848, heading);
        }
        return b;
    }

    internal static FakeProcessMemory Source(byte[] bytes) =>
        new FakeProcessMemory().Add(new MemoryRegion(Base, Base, (ulong)bytes.Length, true, true, true), bytes);

    private static Bbs757FmaReader Reader(byte[] bytes, Bbs757FmaLayout? layout = null)
    {
        var src = Source(bytes);
        return new Bbs757FmaReader(new ModuleMemory(src, Base), src, layout ?? Layout);
    }

    [Fact]
    public void Reads_each_column_as_its_word()
    {
        using var r = Reader(Memory());
        Assert.True(r.TryRead(out var fma));
        Assert.False(fma.Corrupt);
        Assert.Equal("SPD", fma.AutoThrottle);
        Assert.Equal("TO", fma.Pitch);
        Assert.Equal("G/S", fma.PitchArmed);
        Assert.Equal("LNAV", fma.Roll);
        Assert.Equal("LOC", fma.RollArmed);
        Assert.Equal((1, 1, 9, 5, 3), (fma.AtValue, fma.PitchValue, fma.PitchArmedValue, fma.RollValue, fma.RollArmedValue));
    }

    [Fact]
    public void A_zero_field_is_blank()
    {
        using var r = Reader(Memory(pitchArmed: 0, rollArmed: 0));
        Assert.True(r.TryRead(out var fma));
        Assert.Null(fma.PitchArmed);
        Assert.Null(fma.RollArmed);
        Assert.Equal("TO", fma.Pitch);
        Assert.Equal("LNAV", fma.Roll);
    }

    [Fact]
    public void An_unknown_nonzero_value_reads_as_unrecognised()
    {
        using var r = Reader(Memory(at: 7));
        Assert.True(r.TryRead(out var fma));
        Assert.Equal(Bbs757WordTable.Unrecognised, fma.AutoThrottle);
        Assert.Equal(7, fma.AtValue);
    }

    [Fact]
    public void The_captain_pfd_is_the_first_vptr_match_not_the_first_officers()
    {
        // FO pfd sits at the lower manager slot; the reader must still pick the Captain's (also a vptr match)
        // only because it is first — so put the Captain's first and the FO's second, and check afds is followed.
        using var r = Reader(Memory());
        Assert.True(r.TryFindPfd(out uint pfd));
        Assert.Equal(CaptPfd, pfd);
    }

    [Fact]
    public void A_negative_field_is_corrupt()
    {
        using var r = Reader(Memory(roll: -5));
        Assert.True(r.TryRead(out var fma));
        Assert.True(fma.Corrupt);
    }

    [Fact]
    public void A_status_far_beyond_any_word_is_corrupt()
    {
        using var r = Reader(Memory(status: 4096));
        Assert.True(r.TryRead(out var fma));
        Assert.True(fma.Corrupt);
    }

    [Fact]
    public void No_vptr_match_is_a_failed_read()
    {
        // Make neither the Captain's nor the First Officer's pfd carry the vptr.
        var bytes = Memory(captainVptr: false);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan((int)FoPfd), 0x1234);
        using var r = Reader(bytes);
        Assert.False(r.TryRead(out _));
    }

    [Fact]
    public void Reads_the_status_column_and_the_fd_bytes()
    {
        using (var r = Reader(Memory(status: 1, fdCaptain: 1, fdFirstOfficer: 0)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Equal("CMD", fma.Status);
            Assert.Equal(1, fma.StatusValue);
            Assert.True(fma.FdCaptain);
            Assert.False(fma.FdFirstOfficer);
        }
        using (var r = Reader(Memory(status: 0, fdCaptain: 0, fdFirstOfficer: 1)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Null(fma.Status);
            Assert.Equal(0, fma.StatusValue);
            Assert.False(fma.FdCaptain);
            Assert.True(fma.FdFirstOfficer);
        }
        // Each byte is read "& 1", as BlueBird's getter does: 3 is on, 2 is off.
        using (var r = Reader(Memory(fdCaptain: 2, fdFirstOfficer: 3)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.False(fma.FdCaptain);
            Assert.True(fma.FdFirstOfficer);
            Assert.False(fma.Corrupt);
        }
    }

    // R4: BlueBird draws "CMD" only for the status values in its table; any other value draws "FD" while the
    // Captain's F/D is on, else nothing, so a status value with no word is never "unrecognised".
    [Fact]
    public void A_status_with_no_word_reads_fd_while_the_captains_fd_is_on_never_unrecognised()
    {
        using (var r = Reader(Memory(status: 2, fdCaptain: 1)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Equal("FD", fma.Status);
            Assert.Equal(2, fma.StatusValue);
            Assert.False(fma.Corrupt);
        }
        using (var r = Reader(Memory(status: 0, fdCaptain: 1)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Equal("FD", fma.Status);
        }
        using (var r = Reader(Memory(status: 2, fdCaptain: 0, fdFirstOfficer: 1)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Null(fma.Status);
            Assert.Equal(2, fma.StatusValue);
            Assert.False(fma.Corrupt);
        }
        // CMD wins over the F/D.
        using (var r = Reader(Memory(status: 1, fdCaptain: 1)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Equal("CMD", fma.Status);
        }
    }

    // R5: BlueBird draws the pitch and roll columns, each with its armed mode, only while the Captain's F/D is on or
    // the status is CMD; the A/T column always. The raw values are still read.
    [Fact]
    public void With_the_captains_fd_off_and_no_cmd_only_the_at_column_is_drawn()
    {
        using (var r = Reader(Memory(status: 2, fdCaptain: 0, fdFirstOfficer: 1)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Equal("SPD", fma.AutoThrottle);
            Assert.Null(fma.Pitch);
            Assert.Null(fma.PitchArmed);
            Assert.Null(fma.Roll);
            Assert.Null(fma.RollArmed);
            Assert.Null(fma.Status);
            Assert.Equal((1, 1, 9, 5, 3), (fma.AtValue, fma.PitchValue, fma.PitchArmedValue, fma.RollValue, fma.RollArmedValue));
            Assert.False(fma.Corrupt);
        }
        // Raw values are still checked while not drawn.
        using (var r = Reader(Memory(status: 0, fdCaptain: 0, roll: -5)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.True(fma.Corrupt);
        }
        // CMD with the Captain's F/D off draws them.
        using (var r = Reader(Memory(status: 1, fdCaptain: 0)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Equal(("TO", "G/S", "LNAV", "LOC"), (fma.Pitch, fma.PitchArmed, fma.Roll, fma.RollArmed));
            Assert.Equal("CMD", fma.Status);
        }
    }

    [Fact]
    public void Reads_the_mcp_altitude_and_heading_through_the_layout_route()
    {
        using (var r = Reader(Memory(altitude: 35000, heading: 65)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Equal(35000, fma.McpAltitude);
            Assert.Equal(65, fma.McpHeading);
            Assert.False(fma.Corrupt);
        }
        // Another MCP object: the pointer at afds+AfdsToMcp is followed, never a fixed address.
        using (var r = Reader(Memory(altitude: 12000, heading: 360, mcp: 0x2000)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Equal(12000, fma.McpAltitude);
            Assert.Equal(360, fma.McpHeading);
        }
        // Whole numbers: a value a hair off one rounds to it.
        using (var r = Reader(Memory(altitude: 9999.9999, heading: 64.9999)))
        {
            Assert.True(r.TryRead(out var fma));
            Assert.Equal(10000, fma.McpAltitude);
            Assert.Equal(65, fma.McpHeading);
        }
    }

    [Fact]
    public void Reads_an_i32_mcp_when_the_layout_says_so()
    {
        var bytes = Memory();
        Put(bytes, Mcp + 856, 24000);
        Put(bytes, Mcp + 848, 270);
        using var r = Reader(bytes, Layout with { McpValueType = Bbs757McpValueType.I32 });
        Assert.True(r.TryRead(out var fma));
        Assert.Equal(24000, fma.McpAltitude);
        Assert.Equal(270, fma.McpHeading);
        Assert.False(fma.Corrupt);
    }

    [Theory]
    [InlineData(double.NaN, 65)]
    [InlineData(double.PositiveInfinity, 65)]
    [InlineData(-1, 65)]
    [InlineData(50001, 65)]
    [InlineData(35000, double.NaN)]
    [InlineData(35000, double.NegativeInfinity)]
    [InlineData(35000, -1)]
    [InlineData(35000, 361)]
    public void An_mcp_value_out_of_range_is_corrupt(double altitude, double heading)
    {
        using var r = Reader(Memory(altitude: altitude, heading: heading));
        Assert.True(r.TryRead(out var fma));
        Assert.True(fma.Corrupt);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(50000, 360)]
    public void The_mcp_range_ends_are_in_range(double altitude, double heading)
    {
        using var r = Reader(Memory(altitude: altitude, heading: heading));
        Assert.True(r.TryRead(out var fma));
        Assert.False(fma.Corrupt);
    }

    [Fact]
    public void A_read_outside_the_module_is_lost()
    {
        // The MCP pointer leads outside the module: a failed read (the memory moved or went away), never a reading.
        using (var r = Reader(Memory(mcp: 0x9000)))
            Assert.False(r.TryRead(out _));
        // A null MCP pointer: the object is not there.
        using (var r = Reader(Memory(mcp: 0)))
            Assert.False(r.TryRead(out _));
        // The autoflight object's F/D bytes past the module's end.
        using (var r = Reader(Memory(), Layout with { FdFirstOfficer = Size }))
            Assert.False(r.TryRead(out _));
    }

    [Fact]
    public void One_reading_asks_windows_about_each_region_once()
    {
        // VirtualQueryEx costs ~200 µs on the simulator: one reading of one region asks about it once (WASM-15).
        var src = Source(Memory());
        using var r = new Bbs757FmaReader(new ModuleMemory(src, Base), src, Layout);
        Assert.True(r.TryRead(out _));
        Assert.Equal(1, src.RegionQueries);
    }
}

// A synthetic BGL: header, one SceneryObject section, one subsection, three LibraryObject records.
// Never a payware file. Layout as measured on three Community packages, 2026-09-06.
using MSFSBlindAssist.Services.SceneryIndex;

namespace MSFSBlindAssist.Tests;

public class BglPlacementReaderTests
{
    internal static byte[] BuildBgl(int rec, params (double lat, double lon, double hdg, Guid guid)[] objs)
    {
        const int header = 0x38, sectionEntry = 20, subEntry = 16;
        int sectionTable = header, subTable = sectionTable + sectionEntry, data = subTable + subEntry;
        var b = new byte[data + objs.Length * rec];
        void U32(int at, uint v) => BitConverter.TryWriteBytes(b.AsSpan(at, 4), v);
        void U16(int at, ushort v) => BitConverter.TryWriteBytes(b.AsSpan(at, 2), v);
        void F64(int at, double v) => BitConverter.TryWriteBytes(b.AsSpan(at, 8), v);

        U32(0x00, 0x19920201); U32(0x14, 1);
        U32(sectionTable + 0, 0x25); U32(sectionTable + 4, 1); U32(sectionTable + 8, 1);
        U32(sectionTable + 12, (uint)subTable); U32(sectionTable + 16, subEntry);
        U32(subTable + 0, 0); U32(subTable + 4, (uint)objs.Length); U32(subTable + 8, (uint)data); U32(subTable + 12, (uint)(objs.Length * rec));

        int p = data;
        foreach (var (lat, lon, hdg, guid) in objs)
        {
            U16(p, 0x0B); U16(p + 2, (ushort)rec);
            U32(p + 4, (uint)Math.Round((lon + 180.0) * (3.0 * (1 << 28)) / 360.0));
            U32(p + 8, (uint)Math.Round((90.0 - lat) * (2.0 * (1 << 28)) / 180.0));
            U16(p + 22, (ushort)Math.Round(hdg * 65536.0 / 360.0));
            if (rec == 92) { F64(p + 44, lat); F64(p + 52, lon); }   // as the real 92-byte record does, so a reader still looking at +44 reads garbage
            guid.ToByteArray().CopyTo(b, p + rec - 20);
            p += rec;
        }
        return b;
    }

    internal static byte[] BuildBgl(params (double lat, double lon, double hdg, Guid guid)[] objs) => BuildBgl(64, objs);

    // One 0x25 section whose `entryCount` subsection-table entries all point at the SAME 64-byte
    // record — the shape that, with no cumulative read budget, makes Read(Stream) allocate and
    // re-scan the same buffer once per entry. ~entryCount*16 bytes (16 bytes/entry).
    private static byte[] BuildManyEntriesSameData(int entryCount, Guid guid)
    {
        const int header = 0x38, sectionEntry = 20, subTableEntry = 16, rec = 64;
        int sectionTable = header, subTable = sectionTable + sectionEntry, data = subTable + entryCount * subTableEntry;
        var b = new byte[data + rec];
        void U32(int at, uint v) => BitConverter.TryWriteBytes(b.AsSpan(at, 4), v);
        void U16(int at, ushort v) => BitConverter.TryWriteBytes(b.AsSpan(at, 2), v);

        U32(0x00, 0x19920201); U32(0x14, 1);
        U32(sectionTable + 0, 0x25); U32(sectionTable + 4, 1); U32(sectionTable + 8, (uint)entryCount);
        U32(sectionTable + 12, (uint)subTable); U32(sectionTable + 16, (uint)(entryCount * subTableEntry));
        for (int i = 0; i < entryCount; i++)
        {
            int so = subTable + i * subTableEntry;
            U32(so + 8, (uint)data); U32(so + 12, rec);   // every entry: the SAME dataOff/dataSize
        }

        U16(data, 0x0B); U16(data + 2, rec);
        U32(data + 4, (uint)Math.Round((14.477 + 180.0) * (3.0 * (1 << 28)) / 360.0));
        U32(data + 8, (uint)Math.Round((90.0 - 35.857) * (2.0 * (1 << 28)) / 180.0));
        guid.ToByteArray().CopyTo(b, data + rec - 20);
        return b;
    }

    // One 0x25 section whose `guids.Length` subsection-table entries each point at their OWN
    // distinct 64-byte record — a legitimate multi-subsection file, none of it repeated.
    private static byte[] BuildManyEntriesDistinctData(params Guid[] guids)
    {
        const int header = 0x38, sectionEntry = 20, subTableEntry = 16, rec = 64;
        int entryCount = guids.Length;
        int sectionTable = header, subTable = sectionTable + sectionEntry, firstData = subTable + entryCount * subTableEntry;
        var b = new byte[firstData + entryCount * rec];
        void U32(int at, uint v) => BitConverter.TryWriteBytes(b.AsSpan(at, 4), v);
        void U16(int at, ushort v) => BitConverter.TryWriteBytes(b.AsSpan(at, 2), v);

        U32(0x00, 0x19920201); U32(0x14, 1);
        U32(sectionTable + 0, 0x25); U32(sectionTable + 4, 1); U32(sectionTable + 8, (uint)entryCount);
        U32(sectionTable + 12, (uint)subTable); U32(sectionTable + 16, (uint)(entryCount * subTableEntry));
        for (int i = 0; i < entryCount; i++)
        {
            int so = subTable + i * subTableEntry;
            int p = firstData + i * rec;
            U32(so + 8, (uint)p); U32(so + 12, rec);
            U16(p, 0x0B); U16(p + 2, rec);
            U32(p + 4, (uint)Math.Round((10.0 + i + 180.0) * (3.0 * (1 << 28)) / 360.0));
            U32(p + 8, (uint)Math.Round((90.0 - (10.0 + i)) * (2.0 * (1 << 28)) / 180.0));
            guids[i].ToByteArray().CopyTo(b, p + rec - 20);
        }
        return b;
    }

    [Fact]
    public void Reads_position_heading_and_guid_of_each_library_object()
    {
        var g1 = Guid.Parse("416f6b5f-f52e-4744-858a-29067c13cdb0");
        var g2 = Guid.Parse("82cb66da-9f5b-4116-a774-a6ce91702279");
        var bgl = BuildBgl((47.27064, -122.57373, 277.0, g1), (47.26765, -122.57497, 7.0, g2));
        var placed = BglPlacementReader.Read(bgl);
        Assert.Equal(2, placed.Count);
        Assert.InRange(placed[0].Lat, 47.27063, 47.27065);
        Assert.InRange(placed[0].Lon, -122.57374, -122.57372);
        Assert.InRange(placed[0].HeadingDeg, 276.9, 277.1);
        Assert.Equal(g1, placed[0].ModelGuid);
        Assert.Equal(g2, placed[1].ModelGuid);
    }

    [Fact]
    public void Non_bgl_and_truncated_input_yield_empty_or_partial_never_throw()
    {
        Assert.Empty(BglPlacementReader.Read(new byte[] { 1, 2, 3 }));
        Assert.Empty(BglPlacementReader.Read(Array.Empty<byte>()));
        var bgl = BuildBgl((1, 1, 0, Guid.NewGuid()), (2, 2, 0, Guid.NewGuid()));
        var cut = bgl.AsSpan(0, bgl.Length - 40).ToArray();          // second record truncated
        Assert.Single(BglPlacementReader.Read(cut));
    }

    [Fact]
    public void Records_that_are_not_library_objects_are_skipped()
    {
        var bgl = BuildBgl((1, 1, 0, Guid.NewGuid()));
        BitConverter.TryWriteBytes(bgl.AsSpan(0x38 + 20 + 16, 2), (ushort)0x0E);   // flip the record id
        Assert.Empty(BglPlacementReader.Read(bgl));
    }

    [Fact]
    public void Corrupt_subsection_offset_near_uint_max_does_not_throw()
    {
        var bgl = BuildBgl((1, 1, 0, Guid.NewGuid()));
        // Section entry 0 lives at 0x38: (type, flags, subCount, offset, size). Poison the subsection offset.
        BitConverter.TryWriteBytes(bgl.AsSpan(0x38 + 12, 4), 0xFFFFFFF0u);
        Assert.Empty(BglPlacementReader.Read(bgl));
    }

    [Theory]
    [InlineData(64)] [InlineData(92)]
    public void The_model_guid_sits_20_bytes_before_the_end_of_the_record_whatever_its_size(int recordSize)
    {
        var g = Guid.Parse("540e0601-b3bb-44b4-88a6-e44d5f60ccaf");
        var placed = BglPlacementReader.Read(BuildBgl(recordSize, (35.857, 14.477, 90.0, g)));
        var only = Assert.Single(placed);
        Assert.Equal(g, only.ModelGuid);                 // 92-byte records (MSFS 2024 SDK): +72, not +44
        Assert.InRange(only.Lat, 35.8569, 35.8571);
    }

    [Fact]
    public void The_stream_reader_matches_the_span_reader_and_never_reads_past_the_placements()
    {
        var g1 = Guid.NewGuid(); var g2 = Guid.NewGuid();
        byte[] bgl = BuildBgl((47.27064, -122.57373, 277.0, g1), (47.26765, -122.57497, 7.0, g2));
        byte[] padded = bgl.Concat(new byte[4 * 1024 * 1024]).ToArray();       // a model library's bulk after the tables
        using var counting = new CountingStream(new MemoryStream(padded));
        Assert.Equal(BglPlacementReader.Read(bgl), BglPlacementReader.Read(counting));
        Assert.True(counting.BytesRead < 64 * 1024, $"read {counting.BytesRead} bytes of a {padded.Length}-byte file");
    }

    [Fact]
    public void A_truncated_or_foreign_stream_yields_what_parsed_and_never_throws()
    {
        Assert.Empty(BglPlacementReader.Read(new MemoryStream(new byte[10])));
        Assert.Empty(BglPlacementReader.Read(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(new string('x', 4096)))));
        byte[] bgl = BuildBgl((1.0, 2.0, 0.0, Guid.NewGuid()));
        Assert.Empty(BglPlacementReader.Read(new MemoryStream(bgl.Take(bgl.Length - 30).ToArray())));   // record cut short
    }

    [Fact]
    public void A_cumulative_budget_ends_the_stream_read_before_scanning_every_duplicate_entry()
    {
        var g = Guid.NewGuid();
        const int entryCount = 5000, rec = 64;
        byte[] bgl = BuildManyEntriesSameData(entryCount, g);          // ~78 KB fixture; unbounded work would be entryCount*rec = 320,000 bytes
        const long budget = 100_000;                                  // subs table alone is entryCount*16 = 80,000 — leaves ~20,000 for data reads
        using var counting = new CountingStream(new MemoryStream(bgl));

        var placed = BglPlacementReader.Read(counting, budget);

        Assert.NotEmpty(placed);
        Assert.True(placed.Count < entryCount, $"parsed all {placed.Count} of {entryCount} entries — the budget never engaged");
        Assert.All(placed, p => Assert.Equal(g, p.ModelGuid));
        Assert.True(counting.BytesRead <= budget + rec, $"read {counting.BytesRead} bytes against a {budget}-byte budget");
        Assert.True(counting.BytesRead < (long)entryCount * rec, $"read {counting.BytesRead} bytes — the unbounded total would be {(long)entryCount * rec}");
    }

    [Fact]
    public void The_span_overloads_cumulative_budget_also_stops_a_pathological_scan()
    {
        var g = Guid.NewGuid();
        const int entryCount = 5000;
        byte[] bgl = BuildManyEntriesSameData(entryCount, g);
        var placed = BglPlacementReader.Read(bgl, 100_000L);
        Assert.NotEmpty(placed);
        Assert.True(placed.Count < entryCount, $"parsed all {placed.Count} of {entryCount} entries — the budget never engaged");
        Assert.All(placed, p => Assert.Equal(g, p.ModelGuid));
    }

    [Fact]
    public void A_legitimate_multi_subsection_file_under_the_budget_returns_every_placement()
    {
        var guids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        byte[] bgl = BuildManyEntriesDistinctData(guids);
        var placed = BglPlacementReader.Read(new MemoryStream(bgl));    // default (128 MB) budget — this ~1 KB file is nowhere near it
        Assert.Equal(guids.Length, placed.Count);
        for (int i = 0; i < guids.Length; i++) Assert.Equal(guids[i], placed[i].ModelGuid);
    }

    private sealed class CountingStream(Stream inner) : Stream
    {
        public long BytesRead { get; private set; }
        public override int Read(byte[] buffer, int offset, int count) { int n = inner.Read(buffer, offset, count); BytesRead += n; return n; }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

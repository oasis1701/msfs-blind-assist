using System.Buffers.Binary;

namespace MSFSBlindAssist.Services.SceneryIndex;

public readonly record struct ScenePlacement(double Lat, double Lon, double HeadingDeg, Guid ModelGuid);

/// <summary>
/// Reads LibraryObject placements out of an MSFS scenery BGL. Layout measured 2026-09-06 on
/// Orbx KTIW, imaginesim KATL and Axonos KJAC (see the plan/spec). Two record layouts are in
/// the wild: the classic 64-byte record (model GUID at +44) and the 92-byte record the MSFS
/// 2024 SDK writes (GUID at +72; +44 holds the latitude as a double instead, so a reader still
/// looking at +44 there reads garbage). The GUID is always the 16 bytes immediately before the
/// record's trailing 4-byte scale field, so both layouts are read via <c>size − 20</c>. Measured
/// 2026-09-20 across ~35 Community packages: 33 use 64-byte records, 2 (iniBuilds LMML,
/// Glideslope KMEM) use 92-byte records. Bounds-checked at every step: a truncated or foreign
/// file yields whatever parsed cleanly, never an exception. Every single buffer (one section's
/// subsection table, one entry's placement data) is capped at <see cref="MaxSubsectionBytes"/>,
/// but nothing stops a corrupt/hostile file from declaring millions of small, individually
/// in-bounds entries that all point at the same region — so <see cref="DefaultMaxTotalBytes"/>
/// additionally bounds the CUMULATIVE bytes read/scanned across one call; once spent, the read
/// ends and returns whatever was parsed so far, same as any other hostile-input exit, rather
/// than keep allocating and re-scanning for as long as the file keeps declaring more entries.
/// </summary>
public static class BglPlacementReader
{
    private const uint Magic = 0x19920201;
    private const int HeaderSize = 0x38, SectionEntrySize = 20;
    private const uint SceneryObjectSection = 0x25;
    private const ushort LibraryObjectId = 0x0B;
    private const int LibraryObjectSize = 64;
    private const double LonScale = 360.0 / (3.0 * (1 << 28));
    private const double LatScale = 180.0 / (2.0 * (1 << 28));
    private const int MaxSubsectionBytes = 64 * 1024 * 1024;

    // Real placement files are tiny (the largest measured, iniBuilds LMML, carries 5,841
    // 92-byte records ≈ 0.5 MB; a header-only scan of 2,451 real BGLs read 21.3 MB in total) —
    // 128 MB leaves over 200x headroom for one file while still ending a pathological read in a
    // bounded, small amount of work instead of hours.
    private const long DefaultMaxTotalBytes = 128L * 1024 * 1024;

    public static List<ScenePlacement> Read(ReadOnlySpan<byte> b) => Read(b, DefaultMaxTotalBytes);

    /// <summary>Test seam for <see cref="DefaultMaxTotalBytes"/> (see the class summary). The span
    /// overload allocates nothing on a slice, so the only cost a pathological subsection table
    /// buys here is CPU time re-scanning the same bytes and repeatedly re-adding the same
    /// placements to the result — <paramref name="maxTotalBytes"/> bounds that too, counted as
    /// bytes scanned rather than bytes read off a stream.</summary>
    internal static List<ScenePlacement> Read(ReadOnlySpan<byte> b, long maxTotalBytes)
    {
        var result = new List<ScenePlacement>();
        if (b.Length < HeaderSize || U32(b, 0) != Magic) return result;
        uint sections = U32(b, 0x14);
        long totalRead = 0;
        for (uint s = 0; s < sections; s++)
        {
            int e = HeaderSize + (int)s * SectionEntrySize;
            if (e + SectionEntrySize > b.Length) break;
            if (U32(b, e) != SceneryObjectSection) continue;
            uint subCount = U32(b, e + 8), subOff = U32(b, e + 12), subSize = U32(b, e + 16);
            if (subCount == 0 || subSize < 16 || subOff > int.MaxValue || subOff >= b.Length || (long)subOff + subSize > b.Length) continue;
            int subEntry = (int)(subSize / subCount);
            if (subEntry < 16) continue;
            for (uint i = 0; i < subCount; i++)
            {
                int so = (int)subOff + (int)i * subEntry;
                if ((long)so + subEntry > b.Length) continue;
                uint dataOff = U32(b, so + subEntry - 8), dataSize = U32(b, so + subEntry - 4);
                if (dataOff > int.MaxValue || dataOff >= b.Length) continue;
                long end = Math.Min((long)dataOff + dataSize, b.Length);
                long sliceLen = end - dataOff;
                if (totalRead + sliceLen > maxTotalBytes) return result;    // cumulative budget spent: end the whole read
                totalRead += sliceLen;
                ReadRecords(b.Slice((int)dataOff, (int)sliceLen), result);
            }
        }
        return result;
    }

    /// <summary>Header, section table and the SceneryObject (0x25) subsections only, by seeking —
    /// never the whole file. Same result as the span overload; never throws.</summary>
    public static List<ScenePlacement> Read(Stream bgl) => Read(bgl, DefaultMaxTotalBytes);

    /// <summary>Test seam for <see cref="DefaultMaxTotalBytes"/> — see the class summary.</summary>
    internal static List<ScenePlacement> Read(Stream bgl, long maxTotalBytes)
    {
        var result = new List<ScenePlacement>();
        try
        {
            if (!bgl.CanSeek) return result;
            long length = bgl.Length;
            byte[] header = new byte[HeaderSize];
            if (!Fill(bgl, 0, header) || U32(header, 0) != Magic) return result;
            uint sections = U32(header, 0x14);
            if (sections == 0 || sections > 4096) return result;
            byte[] table = new byte[(int)sections * SectionEntrySize];
            if (!Fill(bgl, HeaderSize, table)) return result;

            long totalRead = 0;
            byte[] data = Array.Empty<byte>();   // rented/grown across entries — a legitimate multi-entry file never churns the LOH
            for (int s = 0; s < sections; s++)
            {
                int e = s * SectionEntrySize;
                if (U32(table, e) != SceneryObjectSection) continue;
                uint subCount = U32(table, e + 8), subOff = U32(table, e + 12), subSize = U32(table, e + 16);
                if (subCount == 0 || subSize < 16 || subSize > MaxSubsectionBytes || (long)subOff + subSize > length) continue;
                int subEntry = (int)(subSize / subCount);
                if (subEntry < 16) continue;
                if (totalRead + subSize > maxTotalBytes) return result;    // cumulative budget spent: end the whole read
                byte[] subs = new byte[subSize];
                totalRead += subSize;
                if (!Fill(bgl, subOff, subs)) continue;

                for (int i = 0; i < subCount; i++)
                {
                    int so = i * subEntry;
                    if (so + subEntry > subs.Length) break;
                    uint dataOff = U32(subs, so + subEntry - 8), dataSize = U32(subs, so + subEntry - 4);
                    if (dataSize == 0 || dataSize > MaxSubsectionBytes || (long)dataOff + dataSize > length) continue;
                    if (totalRead + dataSize > maxTotalBytes) return result;    // cumulative budget spent: end the whole read
                    if (data.Length < dataSize) data = new byte[dataSize];
                    totalRead += dataSize;
                    if (Fill(bgl, dataOff, data, (int)dataSize)) ReadRecords(data.AsSpan(0, (int)dataSize), result);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException or ArgumentException) { }
        return result;
    }

    private static bool Fill(Stream s, long at, byte[] into) => Fill(s, at, into, into.Length);

    private static bool Fill(Stream s, long at, byte[] into, int count)
    {
        s.Position = at;
        int have = 0;
        while (have < count) { int n = s.Read(into, have, count - have); if (n <= 0) return false; have += n; }
        return true;
    }

    private static void ReadRecords(ReadOnlySpan<byte> data, List<ScenePlacement> into)
    {
        int end = data.Length, p = 0;
        while (p + 4 <= end)
        {
            ushort id = U16(data, p), size = U16(data, p + 2);
            if (size < 4 || p + size > end) break;
            if (id == LibraryObjectId && size >= LibraryObjectSize)
            {
                double lon = U32(data, p + 4) * LonScale - 180.0;
                double lat = 90.0 - U32(data, p + 8) * LatScale;
                double hdg = U16(data, p + 22) * (360.0 / 65536.0);
                // The GUID is the 16 bytes before the trailing 4-byte scale: +44 in the classic
                // 64-byte record, +72 in the 92-byte record the MSFS 2024 SDK writes (where +44
                // holds the latitude as a double). size − 20 is right for both; +44 resolved
                // 0 of 7,557 placements at iniBuilds LMML.
                var guid = new Guid(data.Slice(p + size - 20, 16));
                into.Add(new ScenePlacement(lat, lon, hdg, guid));
            }
            p += size;
        }
    }

    private static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at, 4));
    private static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(at, 2));
}

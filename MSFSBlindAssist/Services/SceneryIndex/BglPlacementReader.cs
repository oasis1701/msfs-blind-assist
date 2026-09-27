using System.Buffers.Binary;

namespace MSFSBlindAssist.Services.SceneryIndex;

public readonly record struct ScenePlacement(double Lat, double Lon, double HeadingDeg, Guid ModelGuid);

/// <summary>
/// Reads LibraryObject placements out of an MSFS scenery BGL (layout in docs/taxi-guidance.md, "The
/// scenery tier"). Two record layouts exist — classic 64-byte and the MSFS 2024 SDK's 92-byte — and
/// the model GUID is the 16 bytes before the trailing 4-byte scale in both (<c>size − 20</c>).
/// Bounds-checked throughout: a truncated or foreign file yields what parsed, never an exception.
/// Each buffer is capped at <see cref="MaxSubsectionBytes"/> and the whole call at
/// <see cref="DefaultMaxTotalBytes"/>, so a hostile file declaring millions of entries still ends.
/// One parser: the span overloads delegate to the stream one.
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

    // The largest real placement file is 1.27 MB (CYYZ); 128 MB is ~100x headroom that still ends
    // a pathological read quickly.
    private const long DefaultMaxTotalBytes = 128L * 1024 * 1024;

    /// <summary>Tests only; delegates to the stream overload so there is one parser.</summary>
    public static List<ScenePlacement> Read(ReadOnlySpan<byte> b) => Read(b, DefaultMaxTotalBytes);

    /// <summary>Test seam for <see cref="DefaultMaxTotalBytes"/>.</summary>
    internal static List<ScenePlacement> Read(ReadOnlySpan<byte> b, long maxTotalBytes)
        => Read(new MemoryStream(b.ToArray()), maxTotalBytes);

    /// <summary>Header, section table and the SceneryObject (0x25) subsections only, by seeking —
    /// never the whole file. Never throws.</summary>
    public static List<ScenePlacement> Read(Stream bgl) => Read(bgl, DefaultMaxTotalBytes, out _);

    /// <summary>
    /// As <see cref="Read(Stream)"/>; <paramref name="complete"/> is false only when a transient
    /// failure (IOException, a stream disposed under it) cut the read short, so the disk caches do not
    /// keep it. A malformed file or a spent budget answers the same on every re-read, so it stays true.
    /// </summary>
    public static List<ScenePlacement> Read(Stream bgl, out bool complete) => Read(bgl, DefaultMaxTotalBytes, out complete);

    /// <summary>Test seam for <see cref="DefaultMaxTotalBytes"/>.</summary>
    internal static List<ScenePlacement> Read(Stream bgl, long maxTotalBytes) => Read(bgl, maxTotalBytes, out _);

    /// <summary>Test seam for <see cref="DefaultMaxTotalBytes"/>.</summary>
    internal static List<ScenePlacement> Read(Stream bgl, long maxTotalBytes, out bool complete)
    {
        complete = true;
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
            // Reused across entries, grown only when needed, so a normal file never churns the LOH.
            byte[] data = Array.Empty<byte>();
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
        // The file is still READABLE in principle, so the read did not finish: say so.
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { complete = false; }
        // A stream that cannot do what this reader needs answers the same way every time.
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException) { }
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
                // +44 in a 64-byte record, +72 in a 92-byte one (where +44 is a latitude double).
                var guid = new Guid(data.Slice(p + size - 20, 16));
                into.Add(new ScenePlacement(lat, lon, hdg, guid));
            }
            p += size;
        }
    }

    private static uint U32(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(at, 4));
    private static ushort U16(ReadOnlySpan<byte> b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(at, 2));
}

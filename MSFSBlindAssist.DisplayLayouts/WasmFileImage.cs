using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace MSFSBlindAssist.DisplayLayouts;

/// <summary>
/// A .wasm file as the layout fingerprint sees it: a hash of everything but the data section, and the active data
/// segments' bytes. Self-contained on purpose: its section walk and its data segment parse must accept and reject
/// the same segment forms as the generator's own parse, and <see cref="FindData"/> must find what the generator's
/// search finds, or a layout would not fit the file it was written from.
/// </summary>
public sealed class WasmFileImage
{
    private readonly byte[] _bytes;
    private readonly List<(uint Address, int FileOffset, int Length)> _segments = new();
    private DataRange[]? _ranges;

    /// <summary>SHA-256, lowercase hex, over the raw bytes (id byte, size bytes, contents) of every section but the
    /// data section (id 11), in file order. Custom sections, the name section among them, count.</summary>
    public string CodeHash { get; private set; } = "";

    private WasmFileImage(byte[] bytes) => _bytes = bytes;

    /// <summary>The active segments with a constant offset: address and length.</summary>
    public IReadOnlyList<DataRange> DataSegments =>
        _ranges ??= _segments.Select(s => new DataRange(s.Address, s.Length)).ToArray();

    public static bool TryParse(byte[] bytes, [NotNullWhen(true)] out WasmFileImage? image, [NotNullWhen(false)] out string? failure)
    {
        image = null;
        failure = null;
        if (bytes is null || bytes.Length < 8 || bytes[0] != 0x00 || bytes[1] != 0x61 || bytes[2] != 0x73 || bytes[3] != 0x6D)
        {
            failure = "Not a wasm module (bad magic).";
            return false;
        }
        if (bytes[4] != 1 || bytes[5] != 0 || bytes[6] != 0 || bytes[7] != 0)
        {
            failure = "Unsupported wasm version.";
            return false;
        }

        var parsed = new WasmFileImage(bytes);
        try
        {
            parsed.Walk();
        }
        catch (FormatException ex)
        {
            failure = ex.Message;
            return false;
        }
        image = parsed;
        return true;
    }

    /// <summary>True only when [address, address+length) lies inside one active segment.</summary>
    public bool TryReadData(uint address, int length, out ReadOnlySpan<byte> bytes)
    {
        bytes = default;
        if (length < 0) return false;
        foreach (var s in _segments)
        {
            if (address < s.Address) continue;
            long start = address - (long)s.Address;
            if (start + length > s.Length) continue;
            bytes = new ReadOnlySpan<byte>(_bytes, s.FileOffset + (int)start, length);
            return true;
        }
        return false;
    }

    /// <summary>Every address where the pattern occurs, each active segment searched on its own in segment order
    /// (overlapping matches included, none across a segment boundary).</summary>
    public IReadOnlyList<uint> FindData(ReadOnlySpan<byte> pattern)
    {
        var hits = new List<uint>();
        if (pattern.IsEmpty) return hits;
        foreach (var s in _segments)
        {
            var span = new ReadOnlySpan<byte>(_bytes, s.FileOffset, s.Length);
            int from = 0;
            while (from <= span.Length - pattern.Length)
            {
                int at = span.Slice(from).IndexOf(pattern);
                if (at < 0) break;
                hits.Add(s.Address + (uint)(from + at));
                from += at + 1;
            }
        }
        return hits;
    }

    private void Walk()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int pos = 8;
        while (pos < _bytes.Length)
        {
            int sectionStart = pos;
            byte id = _bytes[pos++];
            uint size = ReadU32(ref pos, _bytes.Length);
            if (size > (uint)(_bytes.Length - pos)) throw new FormatException("Truncated section.");
            int end = pos + (int)size;
            if (id == 11) ParseData(pos, end);
            else hash.AppendData(_bytes, sectionStart, end - sectionStart);
            pos = end;
        }
        CodeHash = Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // The same parse as the generator's.
    private void ParseData(int pos, int end)
    {
        uint count = ReadU32(ref pos, end);
        for (uint i = 0; i < count; i++)
        {
            uint flags = ReadU32(ref pos, end);
            if (flags > 2) throw new FormatException("Unknown data segment flags.");
            bool active = flags != 1;
            uint address = 0;
            bool constOffset = false;
            if (active)
            {
                if (flags == 2) ReadU32(ref pos, end); // explicit memory index
                byte op = ReadByte(ref pos, end);
                if (op == 0x41)
                {
                    address = unchecked((uint)ReadS32(ref pos, end));
                    constOffset = true;
                }
                else if (op == 0x23)
                {
                    ReadU32(ref pos, end); // global.get: address not known statically
                }
                else
                {
                    throw new FormatException("Unsupported data segment offset expression.");
                }
                if (ReadByte(ref pos, end) != 0x0B)
                    throw new FormatException("Data segment offset expression not terminated.");
            }
            uint len = ReadU32(ref pos, end);
            if (len > (uint)(end - pos)) throw new FormatException("Truncated data segment.");
            if (active && constOffset)
                _segments.Add((address, pos, (int)len));
            pos += (int)len;
        }
    }

    private byte ReadByte(ref int pos, int end)
    {
        if (pos >= end) throw new FormatException("Unexpected end of data.");
        return _bytes[pos++];
    }

    private uint ReadU32(ref int pos, int end)
    {
        ulong result = 0;
        for (int i = 0; i < 5; i++)
        {
            byte b = ReadByte(ref pos, end);
            result |= (ulong)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0)
            {
                if (result > uint.MaxValue) throw new FormatException("LEB128 value out of range.");
                return (uint)result;
            }
        }
        throw new FormatException("LEB128 longer than 5 bytes.");
    }

    private int ReadS32(ref int pos, int end)
    {
        long result = 0;
        for (int i = 0; i < 5; i++)
        {
            byte b = ReadByte(ref pos, end);
            result |= (long)(b & 0x7F) << (7 * i);
            if ((b & 0x80) == 0)
            {
                int shift = 7 * (i + 1);
                if ((b & 0x40) != 0 && shift < 64) result |= -1L << shift;
                return unchecked((int)result);
            }
        }
        throw new FormatException("LEB128 longer than 5 bytes.");
    }
}

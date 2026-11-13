using System.Buffers.Binary;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The protobuf wire types the Fenix display channel's messages use. Groups (3 and 4) and the reserved
/// types (6 and 7) have no member: <see cref="FenixChannelWire"/> reports a tag that carries one as
/// malformed.
/// </summary>
internal enum FenixWireType
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    Fixed32 = 5
}

/// <summary>
/// A forward-only reader over one protobuf message held in memory: the wire format under the Fenix
/// display channel's gRPC messages (127.0.0.1:18083). A caller reads a tag, then reads the value by its
/// wire type or skips it, until <see cref="TryReadTag"/> says false. A nested message is a
/// length-delimited value: <see cref="TryReadLengthDelimited"/> gives its span, and a new reader walks it.
/// </summary>
/// <remarks>
/// <para>Nothing throws, whatever the bytes are. A truncation, a varint past 64 bits, a length that is
/// negative or past the end of the data, a field number of 0 or past 2^29 - 1 and the wire types the
/// reader does not offer (groups and the reserved ones) all make the read return false and set
/// <see cref="IsMalformed"/>, and the reader then stays malformed: every later read returns false too.
/// A caller drops the whole message when <see cref="IsMalformed"/> is true, since a value read out of
/// damaged bytes might be wrong and the app never speaks a guess. Running out of data at a field
/// boundary is the normal end, not malformed.</para>
/// <para>The reader knows protobuf's framing only, not any Fenix message: which field number means what
/// belongs to the parsers built on it. It is a mutable struct, so pass it by <c>ref</c>: a copy keeps its
/// own position.</para>
/// </remarks>
internal ref struct FenixChannelWire
{
    private readonly ReadOnlySpan<byte> _data;
    private int _pos;

    /// <param name="data">One whole message: a gRPC frame's payload, or the span of a nested part.</param>
    public FenixChannelWire(ReadOnlySpan<byte> data)
    {
        _data = data;
        _pos = 0;
        IsMalformed = false;
    }

    /// <summary>True once a read hit bytes that are not valid protobuf (see the remarks). It stays true.</summary>
    public bool IsMalformed { get; private set; }

    /// <summary>
    /// True when every byte of the data has been read: the normal end. A packed list (varints back to back in one
    /// length-delimited value, with no tags) is walked with a reader of its own until this is true. A malformed
    /// reader is never at the end, so a caller stops at the read that returned false.
    /// </summary>
    public bool IsAtEnd => !IsMalformed && _pos >= _data.Length;

    /// <summary>
    /// Reads the next field's number and wire type. False at the end of the data (<see cref="IsMalformed"/>
    /// stays false), and false with <see cref="IsMalformed"/> true for a malformed tag or a reader that
    /// already was. The caller reads or skips the value before asking for the next tag.
    /// </summary>
    public bool TryReadTag(out int field, out FenixWireType type)
    {
        field = 0;
        type = FenixWireType.Varint;
        if (IsMalformed || _pos >= _data.Length) return false;
        if (!TryReadVarint(out ulong tag)) return false;

        // A tag is (field << 3) | wire type in 32 bits: a larger value carries a field number past 2^29 - 1.
        if (tag > uint.MaxValue) return Fail();

        int fieldNumber = (int)(tag >> 3);
        int wireType = (int)(tag & 7);
        if (fieldNumber == 0) return Fail();
        if (wireType is 3 or 4 or 6 or 7) return Fail();   // start group, end group, reserved

        field = fieldNumber;
        type = (FenixWireType)wireType;
        return true;
    }

    /// <summary>
    /// Reads a varint of one to ten bytes. A negative int32 comes as ten bytes, the sign extended to 64
    /// bits: <c>unchecked((int)(long)value)</c> gives it back.
    /// </summary>
    public bool TryReadVarint(out ulong value)
    {
        value = 0;
        if (IsMalformed) return false;

        ulong result = 0;
        int pos = _pos;
        for (int shift = 0; ; shift += 7)
        {
            if (pos >= _data.Length) return Fail();   // cut short

            byte b = _data[pos++];
            // The tenth byte (shift 63) can only add bit 63. Anything above 1 there is a value past 64
            // bits, or says that an eleventh byte follows.
            if (shift == 63 && b > 1) return Fail();

            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
        }

        _pos = pos;
        value = result;
        return true;
    }

    /// <summary>Reads a fixed64 as a little-endian IEEE double. NaN and the infinities are valid values.</summary>
    public bool TryReadFixed64(out double value)
    {
        value = 0;
        if (!TryTake(8, out ReadOnlySpan<byte> bytes)) return false;

        value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes));
        return true;
    }

    /// <summary>Reads a fixed32 as a little-endian IEEE float.</summary>
    public bool TryReadFixed32(out float value)
    {
        value = 0;
        if (!TryTake(4, out ReadOnlySpan<byte> bytes)) return false;

        value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
        return true;
    }

    /// <summary>
    /// Reads a length-delimited value (a string, bytes or a nested message) as a span of the data, with no
    /// copy. The span is empty for a length of 0.
    /// </summary>
    public bool TryReadLengthDelimited(out ReadOnlySpan<byte> value)
    {
        value = default;
        if (!TryReadVarint(out ulong length)) return false;

        // Compared as unsigned, so a length written as a negative int32 is a huge number, never a slice.
        if (length > (ulong)(_data.Length - _pos)) return Fail();

        return TryTake((int)length, out value);
    }

    /// <summary>
    /// Skips the value of a field the caller does not know, by the wire type its tag gave. A wire type the
    /// reader does not offer (a cast number, such as 3) is malformed.
    /// </summary>
    public bool TrySkip(FenixWireType type) => type switch
    {
        FenixWireType.Varint => TryReadVarint(out _),
        FenixWireType.Fixed64 => TryTake(8, out _),
        FenixWireType.Fixed32 => TryTake(4, out _),
        FenixWireType.LengthDelimited => TryReadLengthDelimited(out _),
        _ => Fail()
    };

    // Takes the next count bytes, or marks the reader malformed when fewer are left.
    private bool TryTake(int count, out ReadOnlySpan<byte> bytes)
    {
        bytes = default;
        if (IsMalformed) return false;
        if (_data.Length - _pos < count) return Fail();

        bytes = _data.Slice(_pos, count);
        _pos += count;
        return true;
    }

    private bool Fail()
    {
        IsMalformed = true;
        return false;
    }
}

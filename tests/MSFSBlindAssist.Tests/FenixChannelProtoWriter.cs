// Test helper for the Fenix display channel tests: builds protobuf bytes, and the gRPC frame around
// them, one field at a time. It writes only what a test asks for and knows nothing about Fenix's own
// messages: every test names its field numbers and values itself, so no test depends on a capture.

using System.Buffers.Binary;
using System.Text;

namespace MSFSBlindAssist.Tests;

internal sealed class FenixChannelProtoWriter
{
    private readonly MemoryStream _bytes = new();

    // A varint field (wire type 0). A negative int32 goes on the wire sign-extended to ten bytes:
    // pass unchecked((ulong)(long)value).
    public FenixChannelProtoWriter Varint(int field, ulong value)
    {
        Tag(field, 0);
        WriteVarint(value);
        return this;
    }

    public FenixChannelProtoWriter Bool(int field, bool value) => Varint(field, value ? 1UL : 0UL);

    // A fixed64 field: a little-endian IEEE double.
    public FenixChannelProtoWriter Double(int field, double value)
    {
        Tag(field, 1);
        Span<byte> bits = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bits, BitConverter.DoubleToInt64Bits(value));
        _bytes.Write(bits);
        return this;
    }

    // A fixed32 field: a little-endian IEEE float.
    public FenixChannelProtoWriter Float(int field, float value)
    {
        Tag(field, 5);
        Span<byte> bits = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bits, BitConverter.SingleToInt32Bits(value));
        _bytes.Write(bits);
        return this;
    }

    // A length-delimited field holding the string's UTF-8 bytes.
    public FenixChannelProtoWriter String(int field, string value)
        => LengthDelimited(field, Encoding.UTF8.GetBytes(value));

    // A length-delimited field holding a nested message.
    public FenixChannelProtoWriter Message(int field, FenixChannelProtoWriter inner)
        => LengthDelimited(field, inner.ToArray());

    // A raw tag with no value after it, for building malformed input: a wire type the reader refuses
    // (3, 4, 6, 7), field 0, or a field number past 29 bits.
    public FenixChannelProtoWriter Tag(int field, int wireType)
    {
        WriteVarint(((ulong)(uint)field << 3) | (uint)wireType);
        return this;
    }

    // Bytes exactly as given, for truncated and over-long input.
    public FenixChannelProtoWriter Raw(params byte[] bytes)
    {
        _bytes.Write(bytes, 0, bytes.Length);
        return this;
    }

    public byte[] ToArray() => _bytes.ToArray();

    // One gRPC length-prefixed message: a flag byte (1 = compressed), the message length as four
    // big-endian bytes, then the message.
    public static byte[] Frame(byte[] message, byte flag = 0)
    {
        byte[] frame = new byte[5 + message.Length];
        frame[0] = flag;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1, 4), (uint)message.Length);
        message.CopyTo(frame, 5);
        return frame;
    }

    private FenixChannelProtoWriter LengthDelimited(int field, byte[] body)
    {
        Tag(field, 2);
        WriteVarint((ulong)body.Length);
        _bytes.Write(body, 0, body.Length);
        return this;
    }

    private void WriteVarint(ulong value)
    {
        while (value >= 0x80)
        {
            _bytes.WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        _bytes.WriteByte((byte)value);
    }
}

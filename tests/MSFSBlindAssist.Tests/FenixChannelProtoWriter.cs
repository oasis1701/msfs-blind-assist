// Test helper for the Fenix display channel tests: builds protobuf bytes, and the gRPC frame around
// them, one field at a time. It writes only what a test asks for and knows nothing about Fenix's own
// messages: every test names its field numbers and values itself, so no test depends on a capture.
// The exceptions are GoodPush, a whole display push, the display units it carries (DuData, with
// the two unit layouts measured in the sim, written out by hand) and the ISIS part it carries when
// a test asks for one.

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

    // A repeated varint field in packed form: one length-delimited field holding every value back to back. (The
    // unpacked form is one Varint field per value.)
    public FenixChannelProtoWriter PackedVarints(int field, params ulong[] values)
    {
        var body = new FenixChannelProtoWriter();
        foreach (ulong value in values)
        {
            body.WriteVarint(value);
        }

        return LengthDelimited(field, body.ToArray());
    }

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

    // What the eleven display units of 3.0.0.7613 showed on 2026-11-02, in DisplayTypeEnum's numbers and in the order
    // the units come. Powered: the captain's PFD (0), the captain's ND (2), the first officer's ND (3) and PFD (1), the
    // upper (4) and lower (5) ECAM, then the two DCDUs (9, 10) and the three DRAIMS (13, 14, 15). Cold and dark: the
    // six main units on the diagonal line (6), then the same five.
    public static readonly int[] PoweredContent = [0, 2, 3, 1, 4, 5, 9, 10, 13, 14, 15];
    public static readonly int[] ColdContent = [6, 6, 6, 6, 6, 6, 9, 10, 13, 14, 15];

    // A DUData part, the display units: Power (field 1, a bool each) and Content (field 2, DisplayTypeEnum each), one
    // entry per unit, unpacked (one varint field per entry, as the Fenix sends them) or packed; then Brightness
    // (field 3, a double each), which the decoder skips.
    public static FenixChannelProtoWriter DuData(bool[] power, int[] content, bool packed = false,
        double[]? brightness = null)
    {
        var units = new FenixChannelProtoWriter();
        if (packed)
        {
            units.PackedVarints(1, power.Select(on => on ? 1UL : 0UL).ToArray());
            units.PackedVarints(2, content.Select(shown => (ulong)shown).ToArray());
        }
        else
        {
            foreach (bool on in power)
            {
                units.Bool(1, on);
            }
            foreach (int shown in content)
            {
                units.Varint(2, (ulong)shown);
            }
        }
        foreach (double level in brightness ?? [])
        {
            units.Double(3, level);
        }

        return units;
    }

    // A whole AircraftDataPush that passes every check of the trust gate: the eleven display units powered as
    // measured (every display they carry live), the 16 FMA slots (empty), both flight director lights as given, a
    // magnetic heading, the FOB line, the left upper ECAM column when a test passes one, and field 197, an altitude the
    // real push carries and the app does not read. With
    // displaysOn false the units are as measured cold and dark: unpowered, and none of those displays live. The ISIS
    // is no display unit: its own part (field 182, Powered set) is written only when isisPowered is, so a push has
    // none by default and the ISIS is off. The field numbers are written out here on purpose, not read from
    // FenixChannelFields: a wrong number in the app's table must fail a test, not be copied into the helper that
    // builds the test's input.
    public static byte[] GoodPush(double magHeading = 90, string fob = "wFOB:   g2940 cs KG",
        bool fdOnCapt = true, bool fdOnFo = true, string[]? upperEcamLeft = null, bool displaysOn = true,
        bool isisPowered = false)
    {
        var remote = new FenixChannelProtoWriter()
            .Message(12, DuData(Enumerable.Repeat(displaysOn, 11).ToArray(), displaysOn ? PoweredContent : ColdContent))
            .Message(110, new FenixChannelProtoWriter().Bool(5, fdOnCapt).Bool(6, fdOnFo));
        for (int slot = 0; slot < 16; slot++)
        {
            remote.Message(111, new FenixChannelProtoWriter());
        }
        remote.String(153, fob);
        foreach (string line in upperEcamLeft ?? [])
        {
            remote.Message(173, new FenixChannelProtoWriter().String(1, line));
        }
        if (isisPowered)
        {
            remote.Message(182, new FenixChannelProtoWriter().Bool(1, true));
        }
        remote.Message(194, new FenixChannelProtoWriter().Double(1, magHeading));
        remote.Double(197, 1000);

        return new FenixChannelProtoWriter().Message(1, remote).ToArray();
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

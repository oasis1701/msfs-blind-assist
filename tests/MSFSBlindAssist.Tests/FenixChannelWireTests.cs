// Characterization tests for FenixChannelWire, the protobuf wire-format reader under the Fenix display
// channel client (the gRPC service on 127.0.0.1:18083). Pure logic: every message is built byte by byte
// here or with FenixChannelProtoWriter, never read from Fenix's own capture.
//
// The contract the channel's message parsers build on: a read gives a value or false and never throws
// (not even on random bytes), and input that is truncated, over-long or not valid protobuf sets
// IsMalformed, so a parser drops the whole message instead of speaking a guess. Running out of data
// at a field boundary is the normal end of a message, not malformed.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelWireTests
{
    // ---- Values ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0x08, 0x00 }, 0UL)]
    [InlineData(new byte[] { 0x08, 0x7F }, 127UL)]
    [InlineData(new byte[] { 0x08, 0x80, 0x01 }, 128UL)]
    [InlineData(new byte[] { 0x08, 0xAC, 0x02 }, 300UL)]
    [InlineData(new byte[] { 0x08, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x01 }, ulong.MaxValue)]
    public void Reads_varints_of_every_length(byte[] bytes, ulong expected)
    {
        var wire = new FenixChannelWire(bytes);

        Assert.True(wire.TryReadTag(out int field, out var type));
        Assert.Equal(1, field);
        Assert.Equal(FenixWireType.Varint, type);
        Assert.True(wire.TryReadVarint(out ulong value));
        Assert.Equal(expected, value);
        Assert.False(wire.IsMalformed);

        // The writer must encode the same bytes: the later tests build their messages with it.
        Assert.Equal(bytes, new FenixChannelProtoWriter().Varint(1, expected).ToArray());
    }

    [Fact]
    public void Reads_a_negative_int32_as_ten_bytes()
    {
        byte[] bytes = new FenixChannelProtoWriter().Varint(1, unchecked((ulong)(long)-1)).ToArray();
        Assert.Equal(11, bytes.Length);   // the tag, then ten value bytes

        var wire = new FenixChannelWire(bytes);

        Assert.True(wire.TryReadTag(out _, out _));
        Assert.True(wire.TryReadVarint(out ulong value));
        Assert.Equal(-1, unchecked((int)(long)value));
        Assert.False(wire.IsMalformed);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-12.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Reads_doubles_including_nan_and_infinities(double expected)
    {
        byte[] bytes = new FenixChannelProtoWriter().Double(3, expected).ToArray();
        var wire = new FenixChannelWire(bytes);

        Assert.True(wire.TryReadTag(out int field, out var type));
        Assert.Equal(3, field);
        Assert.Equal(FenixWireType.Fixed64, type);
        Assert.True(wire.TryReadFixed64(out double value));
        if (double.IsNaN(expected))
        {
            Assert.True(double.IsNaN(value));
        }
        else
        {
            Assert.Equal(expected, value);
        }
        Assert.False(wire.IsMalformed);
    }

    [Fact]
    public void Reads_floats()
    {
        byte[] bytes = new FenixChannelProtoWriter().Float(2, 1.5f).ToArray();
        var wire = new FenixChannelWire(bytes);

        Assert.True(wire.TryReadTag(out int field, out var type));
        Assert.Equal(2, field);
        Assert.Equal(FenixWireType.Fixed32, type);
        Assert.True(wire.TryReadFixed32(out float value));
        Assert.Equal(1.5f, value);
        Assert.False(wire.IsMalformed);
    }

    [Fact]
    public void Reads_nested_parts()
    {
        var inner = new FenixChannelProtoWriter().Double(1, 2.5);
        byte[] bytes = new FenixChannelProtoWriter().Message(110, inner).ToArray();

        var outer = new FenixChannelWire(bytes);
        Assert.True(outer.TryReadTag(out int outerField, out var outerType));
        Assert.Equal(110, outerField);
        Assert.Equal(FenixWireType.LengthDelimited, outerType);
        Assert.True(outer.TryReadLengthDelimited(out ReadOnlySpan<byte> part));

        var nested = new FenixChannelWire(part);
        Assert.True(nested.TryReadTag(out int field, out var type));
        Assert.Equal(1, field);
        Assert.Equal(FenixWireType.Fixed64, type);
        Assert.True(nested.TryReadFixed64(out double value));
        Assert.Equal(2.5, value);

        // Both readers end cleanly: the part holds nothing else, and neither is malformed.
        Assert.False(nested.TryReadTag(out _, out _));
        Assert.False(nested.IsMalformed);
        Assert.False(outer.TryReadTag(out _, out _));
        Assert.False(outer.IsMalformed);
    }

    [Fact]
    public void Skips_unknown_fields_of_every_wire_type()
    {
        byte[] bytes = new FenixChannelProtoWriter()
            .Varint(7, 300)
            .Double(8, 1.25)
            .String(9, "unknown")
            .Float(10, 0.5f)
            .Varint(5, 42)
            .ToArray();

        var wire = new FenixChannelWire(bytes);
        var seen = new List<int>();
        ulong found = 0;
        while (wire.TryReadTag(out int field, out var type))
        {
            seen.Add(field);
            if (field == 5)
            {
                Assert.True(wire.TryReadVarint(out found));
            }
            else
            {
                Assert.True(wire.TrySkip(type));
            }
        }

        // Each skip took exactly its own field's bytes: the next tag landed on the next field.
        Assert.Equal(new[] { 7, 8, 9, 10, 5 }, seen);
        Assert.Equal(42UL, found);
        Assert.False(wire.IsMalformed);
    }

    [Fact]
    public void Treats_empty_input_as_the_end_not_as_malformed()
    {
        var wire = new FenixChannelWire(ReadOnlySpan<byte>.Empty);

        Assert.False(wire.TryReadTag(out _, out _));
        Assert.False(wire.IsMalformed);
        Assert.True(wire.IsAtEnd);
    }

    [Fact]
    public void Reads_a_packed_list_until_its_end()
    {
        // A packed repeated varint is one length-delimited value holding the varints back to back, with no tags: a
        // reader of its own walks it until IsAtEnd, the normal end, not malformed.
        byte[] bytes = new FenixChannelProtoWriter().PackedVarints(2, 0, 300, 5).ToArray();
        var outer = new FenixChannelWire(bytes);
        Assert.True(outer.TryReadTag(out int field, out var type));
        Assert.Equal(2, field);
        Assert.Equal(FenixWireType.LengthDelimited, type);
        Assert.True(outer.TryReadLengthDelimited(out ReadOnlySpan<byte> packed));

        var list = new FenixChannelWire(packed);
        var values = new List<ulong>();
        while (!list.IsAtEnd)
        {
            Assert.True(list.TryReadVarint(out ulong value));
            values.Add(value);
        }

        Assert.Equal(new ulong[] { 0, 300, 5 }, values);
        Assert.False(list.IsMalformed);
        Assert.True(outer.IsAtEnd);
    }

    [Fact]
    public void Is_at_the_end_only_once_no_byte_is_left()
    {
        var wire = new FenixChannelWire(new byte[] { 0x08, 0x01 });

        Assert.False(wire.IsAtEnd);
        Assert.True(wire.TryReadTag(out _, out _));
        Assert.False(wire.IsAtEnd);   // the value is still to come
        Assert.True(wire.TryReadVarint(out _));
        Assert.True(wire.IsAtEnd);
    }

    [Fact]
    public void A_malformed_reader_is_never_at_the_end()
    {
        // A tag of field 0 is malformed though its byte was read, so the reader stands past its last byte; that is no
        // normal end. A packed list cut inside its last varint fails the read, and is not at its end either.
        var fieldZero = new FenixChannelWire(new byte[] { 0x00 });
        Assert.False(fieldZero.TryReadTag(out _, out _));
        Assert.True(fieldZero.IsMalformed);
        Assert.False(fieldZero.IsAtEnd);

        var cut = new FenixChannelWire(new byte[] { 0x05, 0x80 });
        Assert.True(cut.TryReadVarint(out _));
        Assert.False(cut.TryReadVarint(out _));
        Assert.True(cut.IsMalformed);
        Assert.False(cut.IsAtEnd);
    }

    // ---- Malformed input ------------------------------------------------------------------------

    [Theory]
    [InlineData(new byte[] { 0x08 })]                    // a tag with no value
    [InlineData(new byte[] { 0x09, 0x00, 0x00 })]        // a fixed64 cut short
    [InlineData(new byte[] { 0x12, 0x05, 0x41 })]        // a length past the end
    [InlineData(new byte[] { 0x08, 0x80 })]              // a varint cut short
    [InlineData(new byte[] { 0x0D, 0x00, 0x00, 0x00 })]  // a fixed32 cut short
    public void Reports_truncated_input_as_malformed(byte[] bytes)
    {
        var wire = new FenixChannelWire(bytes);

        Assert.True(wire.TryReadTag(out _, out var type));
        Assert.False(ReadValue(ref wire, type));
        Assert.True(wire.IsMalformed);
    }

    [Fact]
    public void Reports_a_tag_cut_short_as_malformed()
    {
        var wire = new FenixChannelWire(new byte[] { 0x80 });

        Assert.False(wire.TryReadTag(out _, out _));
        Assert.True(wire.IsMalformed);
    }

    [Fact]
    public void Reports_an_eleven_byte_varint_as_malformed()
    {
        // The tag, ten bytes with the continuation bit set, then a last byte: eleven value bytes.
        byte[] bytes = [0x08, .. Enumerable.Repeat((byte)0xFF, 10), 0x01];
        var wire = new FenixChannelWire(bytes);

        Assert.True(wire.TryReadTag(out _, out _));
        Assert.False(wire.TryReadVarint(out _));
        Assert.True(wire.IsMalformed);
    }

    [Fact]
    public void Reports_a_tenth_varint_byte_above_one_as_malformed()
    {
        // Ten value bytes, but the tenth is 2: a value past 64 bits.
        byte[] bytes = [0x08, .. Enumerable.Repeat((byte)0xFF, 9), 0x02];
        var wire = new FenixChannelWire(bytes);

        Assert.True(wire.TryReadTag(out _, out _));
        Assert.False(wire.TryReadVarint(out _));
        Assert.True(wire.IsMalformed);
    }

    [Fact]
    public void Reports_a_negative_length_as_malformed()
    {
        // Field 2, length-delimited, with the length -1 written as the ten-byte negative int32.
        byte[] bytes = [0x12, .. Enumerable.Repeat((byte)0xFF, 9), 0x01];
        var wire = new FenixChannelWire(bytes);

        Assert.True(wire.TryReadTag(out _, out var type));
        Assert.Equal(FenixWireType.LengthDelimited, type);
        Assert.False(wire.TryReadLengthDelimited(out _));
        Assert.True(wire.IsMalformed);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    public void Reports_groups_and_reserved_wire_types_as_malformed(int wireType)
    {
        byte[] bytes = new FenixChannelProtoWriter().Tag(1, wireType).ToArray();
        var wire = new FenixChannelWire(bytes);

        Assert.False(wire.TryReadTag(out _, out _));
        Assert.True(wire.IsMalformed);
    }

    [Fact]
    public void Reports_field_zero_as_malformed()
    {
        var wire = new FenixChannelWire(new byte[] { 0x00 });

        Assert.False(wire.TryReadTag(out _, out _));
        Assert.True(wire.IsMalformed);
    }

    [Fact]
    public void Reads_the_largest_field_number_and_reports_the_next_as_malformed()
    {
        const int Largest = (1 << 29) - 1;

        var valid = new FenixChannelWire(new FenixChannelProtoWriter().Tag(Largest, 0).ToArray());
        Assert.True(valid.TryReadTag(out int field, out _));
        Assert.Equal(Largest, field);
        Assert.False(valid.IsMalformed);

        // One past it, the tag no longer fits 32 bits.
        var tooLarge = new FenixChannelWire(new FenixChannelProtoWriter().Tag(1 << 29, 0).ToArray());
        Assert.False(tooLarge.TryReadTag(out _, out _));
        Assert.True(tooLarge.IsMalformed);
    }

    [Fact]
    public void Refuses_to_skip_a_wire_type_it_does_not_read()
    {
        var wire = new FenixChannelWire(new byte[] { 0x01, 0x02, 0x03 });

        Assert.False(wire.TrySkip((FenixWireType)3));
        Assert.True(wire.IsMalformed);
    }

    [Fact]
    public void Stays_malformed_after_a_failed_read()
    {
        // A length of 9 with two bytes left. The 08 01 behind it would read as a field if the reader
        // carried on.
        var wire = new FenixChannelWire(new byte[] { 0x12, 0x09, 0x08, 0x01 });

        Assert.True(wire.TryReadTag(out _, out _));
        Assert.False(wire.TryReadLengthDelimited(out _));
        Assert.True(wire.IsMalformed);

        Assert.False(wire.TryReadTag(out _, out _));
        Assert.False(wire.TryReadVarint(out _));
        Assert.False(wire.TrySkip(FenixWireType.Varint));
        Assert.True(wire.IsMalformed);
    }

    [Fact]
    public void Never_throws_on_random_bytes()
    {
        var random = new Random(1234);

        Exception? thrown = Record.Exception(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                byte[] bytes = new byte[random.Next(0, 65)];
                random.NextBytes(bytes);

                var wire = new FenixChannelWire(bytes);
                while (wire.TryReadTag(out _, out FenixWireType type))
                {
                    if (!wire.TrySkip(type)) break;
                }
            }
        });

        Assert.Null(thrown);
    }

    // ---- The writer the later tests build their messages with -----------------------------------

    [Fact]
    public void The_writer_writes_a_bool_as_a_one_byte_varint()
    {
        Assert.Equal(new byte[] { 0x10, 0x01 }, new FenixChannelProtoWriter().Bool(2, true).ToArray());
        Assert.Equal(new byte[] { 0x10, 0x00 }, new FenixChannelProtoWriter().Bool(2, false).ToArray());
    }

    [Fact]
    public void The_writer_frames_a_message_with_a_flag_and_a_big_endian_length()
    {
        // 300 is 0x012C: two of the four length bytes are non-zero, so a wrong byte order shows.
        byte[] message = new byte[300];
        Array.Fill(message, (byte)0xAB);

        byte[] frame = FenixChannelProtoWriter.Frame(message);

        Assert.Equal(305, frame.Length);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x01, 0x2C }, frame[..5]);
        Assert.Equal(message, frame[5..]);

        Assert.Equal(new byte[] { 0x01, 0x00, 0x00, 0x00, 0x00 }, FenixChannelProtoWriter.Frame([], 1));
    }

    // ---- Helpers --------------------------------------------------------------------------------

    // The read that goes with a tag's wire type.
    private static bool ReadValue(ref FenixChannelWire wire, FenixWireType type) => type switch
    {
        FenixWireType.Varint => wire.TryReadVarint(out _),
        FenixWireType.Fixed64 => wire.TryReadFixed64(out _),
        FenixWireType.Fixed32 => wire.TryReadFixed32(out _),
        _ => wire.TryReadLengthDelimited(out _)
    };
}

// Characterization tests for FenixChannelDecoder, which turns one message of the Fenix display channel
// (the gRPC service on 127.0.0.1:18083) into a frame: the flight director, the FMA, the upper ECAM
// columns and the fields the trust gate cross-checks, or an ECAM entry list. Pure logic: every message is
// built here with FenixChannelProtoWriter, with the field numbers written as plain numbers (never read
// from FenixChannelFields, so a wrong number in the app's table fails a test), and never read from
// Fenix's own capture.
//
// The contract the trust gate builds on: the decoder never throws and never turns damaged or reshaped
// data into confident values. Bytes that are not valid protobuf anywhere in a message, a nested part
// included, make the whole message Malformed with no value in it; a field in another wire shape is
// recorded by its path ("110", "110.5") and the rest still decodes; a field left out reads as its
// protobuf default. A push with nothing in it to read (no PrimaryData, or an empty one) is NoData:
// neither damaged nor reshaped, so it records no wrong shape and the gate skips it.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDecoderTests
{
    // U+2800, the blank braille cell the display uses as a spacer. Named because it cannot be seen in source.
    private const char Spacer = (char)0x2800;

    // ---- The flight director --------------------------------------------------------------------

    [Fact]
    public void Decodes_the_flight_director()
    {
        // Pitch, Fpa and Yawbar are bars that are not drawn: NaN.
        FenixChannelFrame frame = Decode(Remote().Message(110, new FenixChannelProtoWriter()
            .Double(1, double.NaN).Double(2, double.NaN).Double(3, -3.5).Double(4, double.NaN)
            .Bool(5, true).Bool(6, true).Bool(9, true)));

        Assert.True(double.IsNaN(frame.Fd.Pitch));
        Assert.True(double.IsNaN(frame.Fd.Fpa));
        Assert.Equal(-3.5, frame.Fd.Roll);
        Assert.True(double.IsNaN(frame.Fd.Yawbar));
        Assert.True(frame.Fd.FdOnCapt);
        Assert.True(frame.Fd.FdOnFo);
        Assert.True(frame.Fd.FdFlash);
        Assert.False(frame.Fd.FdInhibit);
        Assert.True(frame.ShapesOk);
    }

    [Fact]
    public void Reads_each_flight_director_bar_from_its_own_field()
    {
        FenixChannelFrame frame = Decode(Remote().Message(110, new FenixChannelProtoWriter()
            .Double(1, 1.5).Double(2, 2.5).Double(3, 3.5).Double(4, 4.5)));

        Assert.Equal(1.5, frame.Fd.Pitch);
        Assert.Equal(2.5, frame.Fd.Fpa);
        Assert.Equal(3.5, frame.Fd.Roll);
        Assert.Equal(4.5, frame.Fd.Yawbar);
    }

    [Theory]
    [InlineData(5, nameof(FenixFdState.FdOnCapt))]
    [InlineData(6, nameof(FenixFdState.FdOnFo))]
    [InlineData(7, nameof(FenixFdState.TrkFpa))]
    [InlineData(8, nameof(FenixFdState.RollGuidance))]
    [InlineData(9, nameof(FenixFdState.FdFlash))]
    [InlineData(10, nameof(FenixFdState.FdInhibit))]
    [InlineData(11, nameof(FenixFdState.InhibitRoll))]
    [InlineData(12, nameof(FenixFdState.InhibitPitch))]
    public void Reads_each_flight_director_flag_from_its_own_field(int field, string flag)
    {
        FenixChannelFrame frame = Decode(Remote().Message(110, new FenixChannelProtoWriter().Bool(field, true)));

        // Exactly the one flag the field belongs to is set.
        string[] set = typeof(FenixFdState).GetProperties()
            .Where(p => p.PropertyType == typeof(bool) && (bool)p.GetValue(frame.Fd)!)
            .Select(p => p.Name)
            .ToArray();
        Assert.Equal(new[] { flag }, set);
        Assert.True(frame.ShapesOk);
    }

    [Fact]
    public void Reads_a_flag_sent_as_any_non_zero_varint_as_set()
    {
        FenixChannelFrame frame = Decode(Remote().Message(110, new FenixChannelProtoWriter().Varint(5, 2)));

        Assert.True(frame.Fd.FdOnCapt);
        Assert.True(frame.ShapesOk);
    }

    // ---- The FMA --------------------------------------------------------------------------------

    [Fact]
    public void Decodes_sixteen_fma_slots()
    {
        var remote = new FenixChannelProtoWriter();
        for (int slot = 0; slot < 16; slot++)
        {
            var entry = new FenixChannelProtoWriter();
            if (slot == 0)
            {
                entry.String(1, "gMACH");
            }
            if (slot == 3)
            {
                entry.String(1, "wAP1").Bool(2, true);
            }
            remote.Message(111, entry);
        }

        FenixChannelFrame frame = Decode(remote);

        Assert.Equal(16, frame.Fma.Count);
        Assert.Equal("gMACH", frame.Fma[0].Text);
        Assert.False(frame.Fma[0].ChangeBox);
        Assert.Equal("wAP1", frame.Fma[3].Text);
        Assert.True(frame.Fma[3].ChangeBox);
        Assert.Equal("", frame.Fma[5].Text);
        Assert.False(frame.Fma[5].ChangeBox);
        Assert.True(frame.ShapesOk);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(17)]
    public void An_fma_without_sixteen_slots_is_a_wrong_shape(int slots)
    {
        // The altitude keeps RemoteData from being empty at zero slots: an empty one is no data at all.
        var remote = new FenixChannelProtoWriter().Double(197, 1000.0);
        for (int slot = 0; slot < slots; slot++)
        {
            remote.Message(111, new FenixChannelProtoWriter());
        }

        FenixChannelFrame frame = Decode(remote);

        // Once, whatever the count, and the slots that did come are kept as sent.
        Assert.Equal(new[] { "111" }, frame.WrongShapeFields);
        Assert.Equal(slots, frame.Fma.Count);
        Assert.False(frame.ShapesOk);
        Assert.False(frame.Malformed);
        Assert.True(frame.HasData);
    }

    [Fact]
    public void A_wrong_shaped_fma_field_is_recorded_by_its_path()
    {
        // Fifteen empty slots and one whose Text came as a varint and whose ChangeBox came as a string.
        var remote = new FenixChannelProtoWriter();
        for (int slot = 0; slot < 15; slot++)
        {
            remote.Message(111, new FenixChannelProtoWriter());
        }
        remote.Message(111, new FenixChannelProtoWriter().Varint(1, 5).String(2, "x"));

        FenixChannelFrame frame = Decode(remote);

        // Sixteen slots still, so there is no "111" of its own; the odd slot reads as empty.
        Assert.Equal(new[] { "111.1", "111.2" }, frame.WrongShapeFields);
        Assert.Equal(16, frame.Fma.Count);
        Assert.Equal(new FenixFmaSlot("", false), frame.Fma[15]);
    }

    // ---- The upper ECAM columns, the numbers and the text ---------------------------------------

    [Fact]
    public void Decodes_the_upper_ecam_columns_with_a_null_element()
    {
        FenixChannelFrame frame = Decode(Remote()
            .Message(173, new FenixChannelProtoWriter().String(1, "guT.Ou AUTO BRKc.....MAX"))
            .Message(173, new FenixChannelProtoWriter())
            .Message(173, new FenixChannelProtoWriter().String(1, "g    SIGNS ON"))
            .Message(174, new FenixChannelProtoWriter().String(1, "gPARK BRK")));

        // The empty element is a null string on the wire: it reads as "", and keeps its place.
        Assert.Equal(new[] { "guT.Ou AUTO BRKc.....MAX", "", "g    SIGNS ON" }, frame.UpperEcamLeft);
        Assert.Equal(new[] { "gPARK BRK" }, frame.UpperEcamRight);
        Assert.True(frame.ShapesOk);
    }

    [Theory]
    [InlineData(173)]
    [InlineData(174)]
    public void A_wrong_shaped_column_element_is_recorded_by_its_path(int column)
    {
        FenixChannelFrame frame = Decode(Remote().Message(column, new FenixChannelProtoWriter().Varint(1, 5)));

        Assert.Equal(new[] { $"{column}.1" }, frame.WrongShapeFields);
        IReadOnlyList<string> lines = column == 173 ? frame.UpperEcamLeft : frame.UpperEcamRight;
        Assert.Equal(new[] { "" }, lines);
    }

    [Fact]
    public void Decodes_page_phase_altitude_heading_and_fob()
    {
        FenixChannelFrame frame = Decode(Remote()
            .Varint(10, 12)
            .Varint(11, 2)
            .Double(197, 1023.5)
            .Message(194, new FenixChannelProtoWriter().Double(1, 271.25).Double(2, 270.0).Double(3, -5.1))
            .String(153, "wFOB:   g2940 cs KG"));

        Assert.Equal(12, frame.FwcPage);
        Assert.Equal(2, frame.FwcPhase);
        Assert.Equal(1023.5, frame.Altitude);
        Assert.Equal(271.25, frame.MagHeading);   // field 1 of the heading, not the track or the variation
        Assert.Equal("wFOB:   g2940 cs KG", frame.FobText);
        Assert.True(frame.ShapesOk);
    }

    [Fact]
    public void Reads_page_and_phase_as_int32_including_negative_values()
    {
        // A negative int32 goes on the wire sign-extended to ten bytes.
        FenixChannelFrame frame = Decode(Remote()
            .Varint(10, unchecked((ulong)(long)-3))
            .Varint(11, unchecked((ulong)(long)-1)));

        Assert.Equal(-3, frame.FwcPage);
        Assert.Equal(-1, frame.FwcPhase);
        Assert.True(frame.ShapesOk);
    }

    [Fact]
    public void Keeps_text_exactly_as_sent()
    {
        // The braille spacer, runs of spaces and the format letters are the display's own, and valid UTF-8 of
        // every length passes: two bytes (a degree sign) and four (an emoji) as well as the spacer's three.
        // The decoder trims, folds and strips nothing.
        string fob = $"  wFOB:{Spacer}{Spacer}  g2940 cs KG  ";
        string right = $" g{Spacer}PARK BRK {(char)0x00B0}{char.ConvertFromUtf32(0x1F600)} ";

        FenixChannelFrame frame = Decode(Remote()
            .String(153, fob)
            .Message(174, new FenixChannelProtoWriter().String(1, right)));

        Assert.Equal(fob, frame.FobText);
        Assert.Equal(new[] { right }, frame.UpperEcamRight);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE })]              // not UTF-8 at all
    [InlineData(new byte[] { 0x80 })]                    // a continuation byte with no start
    [InlineData(new byte[] { 0xE2, 0xA0 })]              // the braille spacer cut after two of its three bytes
    [InlineData(new byte[] { 0xC0, 0xAF })]              // an overlong "/"
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]        // a surrogate half
    [InlineData(new byte[] { 0xF4, 0x90, 0x80, 0x80 })]  // past U+10FFFF
    public void A_string_that_is_not_valid_utf8_is_a_wrong_shape(byte[] bytes)
    {
        // protobuf-net writes only valid UTF-8, so such bytes are a reshaped field (a string, bytes and a
        // message share wire type 2) or a framing fault: never a string with replacement characters in it.
        var remote = Remote();
        RawString(remote, 153, bytes);

        FenixChannelFrame frame = Decode(remote);

        Assert.Equal(new[] { "153" }, frame.WrongShapeFields);
        Assert.Equal("", frame.FobText);   // the field took its default
        Assert.False(frame.ShapesOk);
        Assert.False(frame.Malformed);
        Assert.True(frame.HasData);
    }

    [Fact]
    public void An_fma_text_that_is_not_valid_utf8_is_a_wrong_shape()
    {
        // Fifteen empty slots and one whose Text is not UTF-8 but whose ChangeBox is fine.
        var remote = new FenixChannelProtoWriter();
        for (int slot = 0; slot < 15; slot++)
        {
            remote.Message(111, new FenixChannelProtoWriter());
        }
        remote.Message(111, RawString(new FenixChannelProtoWriter(), 1, 0xFF, 0xFE).Bool(2, true));

        FenixChannelFrame frame = Decode(remote);

        Assert.Equal(new[] { "111.1" }, frame.WrongShapeFields);
        Assert.Equal(16, frame.Fma.Count);
        Assert.Equal(new FenixFmaSlot("", true), frame.Fma[15]);   // empty text, the change box as sent
    }

    [Theory]
    [InlineData(173)]
    [InlineData(174)]
    public void A_column_element_that_is_not_valid_utf8_is_a_wrong_shape(int column)
    {
        FenixChannelFrame frame = Decode(Remote()
            .Message(column, RawString(new FenixChannelProtoWriter(), 1, 0xFF, 0xFE)));

        Assert.Equal(new[] { $"{column}.1" }, frame.WrongShapeFields);
        IReadOnlyList<string> lines = column == 173 ? frame.UpperEcamLeft : frame.UpperEcamRight;
        Assert.Equal(new[] { "" }, lines);   // the element keeps its place, as an empty string
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void An_ecam_string_that_is_not_valid_utf8_is_a_wrong_shape(int badField)
    {
        // An entry of Ata "A", Name "N" and Title "T", with one of the three replaced by bytes that are not UTF-8.
        string[] text = ["A", "N", "T"];
        var entry = new FenixChannelProtoWriter();
        for (int field = 1; field <= 3; field++)
        {
            if (field == badField)
            {
                RawString(entry, field, 0xFF, 0xFE);
            }
            else
            {
                entry.String(field, text[field - 1]);
            }
        }
        byte[] message = new FenixChannelProtoWriter().Message(1, entry).ToArray();

        FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(message);

        text[badField - 1] = "";   // the bad one took its default; the other two are as sent
        Assert.Equal(new[] { $"1.{badField}" }, entries.WrongShapeFields);
        Assert.Equal(new[] { new FenixEcamEntry(text[0], text[1], text[2], false) }, entries.Entries);
        Assert.False(entries.ShapesOk);
        Assert.False(entries.Malformed);
    }

    [Fact]
    public void Left_out_fields_read_as_defaults()
    {
        // Only the 16 empty FMA slots every push carries.
        FenixChannelFrame frame = Decode(Remote());

        Assert.Equal(FenixFdState.Default, frame.Fd);
        Assert.Equal(0.0, frame.Altitude);
        Assert.Equal(0.0, frame.MagHeading);
        Assert.Equal("", frame.FobText);
        Assert.Empty(frame.UpperEcamLeft);
        Assert.Empty(frame.UpperEcamRight);
        Assert.Equal(0, frame.FwcPage);
        Assert.Equal(0, frame.FwcPhase);
        Assert.True(frame.ShapesOk);
    }

    // ---- Wrong shapes and unknown fields --------------------------------------------------------

    [Fact]
    public void A_wrong_shaped_field_is_recorded_and_the_rest_still_decode()
    {
        FenixChannelFrame frame = Decode(Remote().Varint(110, 1).Double(197, 1000.0));

        Assert.Equal(new[] { "110" }, frame.WrongShapeFields);
        Assert.Equal(1000.0, frame.Altitude);
        Assert.Equal(FenixFdState.Default, frame.Fd);   // the wrong-shaped field took its default
        Assert.False(frame.Malformed);
        Assert.False(frame.ShapesOk);
    }

    [Fact]
    public void A_wrong_shape_seen_more_than_once_is_recorded_once()
    {
        // Two elements of the left column whose string came as a varint: one finding, not two. Without
        // this, sixteen FMA slots with the same bad field would fill the log with one line each.
        FenixChannelFrame frame = Decode(Remote()
            .Message(173, new FenixChannelProtoWriter().Varint(1, 5))
            .Message(173, new FenixChannelProtoWriter().Varint(1, 6)));

        Assert.Equal(new[] { "173.1" }, frame.WrongShapeFields);
        Assert.Equal(new[] { "", "" }, frame.UpperEcamLeft);
    }

    [Fact]
    public void A_field_sent_twice_takes_the_last_value_and_a_part_sent_twice_is_merged()
    {
        // As protobuf reads them: the flight director arrives in two parts, the second with a new Roll.
        FenixChannelFrame frame = Decode(Remote()
            .Double(197, 1000.0)
            .Message(110, new FenixChannelProtoWriter().Bool(5, true).Double(3, 1.5))
            .Double(197, 2000.0)
            .Message(110, new FenixChannelProtoWriter().Bool(6, true).Double(3, 2.5)));

        Assert.Equal(2000.0, frame.Altitude);
        Assert.Equal(2.5, frame.Fd.Roll);
        Assert.True(frame.Fd.FdOnCapt);   // only in the first part
        Assert.True(frame.Fd.FdOnFo);     // only in the second
        Assert.True(frame.ShapesOk);
    }

    [Fact]
    public void A_wrong_shaped_sub_field_is_recorded_by_its_path()
    {
        // FdOnCapt came as a fixed64.
        FenixChannelFrame frame = Decode(Remote()
            .Message(110, new FenixChannelProtoWriter().Double(5, 1.0).Bool(6, true)));

        Assert.Equal(new[] { "110.5" }, frame.WrongShapeFields);
        Assert.False(frame.Fd.FdOnCapt);
        Assert.True(frame.Fd.FdOnFo);   // the next field of the same part still decoded
        Assert.False(frame.ShapesOk);
    }

    [Fact]
    public void A_push_whose_first_field_is_not_a_part_is_a_wrong_shape()
    {
        byte[] push = new FenixChannelProtoWriter().Varint(1, 5).ToArray();

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(push);

        // A finding, not "no data": the first path is "1", and the gate must not skip the push.
        Assert.Equal("1", frame.WrongShapeFields[0]);
        Assert.True(frame.HasData);
        Assert.False(frame.Malformed);
        Assert.False(frame.ShapesOk);
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        // Unknown numbers in the push, in RemoteData and inside the flight director, in every wire type
        // the reader offers.
        var fd = new FenixChannelProtoWriter().Bool(5, true).Varint(13, 1).String(99, "y").Double(77, 2.5);
        var remote = Remote().Varint(2, 1).Varint(999, 5).String(250, "x").Float(251, 0.5f).Message(110, fd);
        byte[] push = new FenixChannelProtoWriter().Varint(2, 1).Message(1, remote).String(7, "z").ToArray();

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(push);

        Assert.True(frame.ShapesOk);
        Assert.True(frame.Fd.FdOnCapt);
    }

    // ---- A push with no display data ------------------------------------------------------------

    [Fact]
    public void An_empty_message_is_no_data_not_a_wrong_shape()
    {
        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(ReadOnlySpan<byte>.Empty);

        Assert.Same(FenixChannelFrame.NoData, frame);
        Assert.False(frame.HasData);
        Assert.True(frame.ShapesOk);
        Assert.False(frame.Malformed);
        Assert.Empty(frame.WrongShapeFields);
    }

    [Fact]
    public void A_push_whose_first_field_is_an_empty_message_is_no_data()
    {
        byte[] push = new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter()).ToArray();

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(push);

        Assert.Same(FenixChannelFrame.NoData, frame);
        Assert.False(frame.HasData);
        Assert.True(frame.ShapesOk);
        Assert.False(frame.Malformed);
    }

    [Fact]
    public void A_push_without_a_first_field_is_no_data_whatever_else_it_carries()
    {
        // Fields of numbers the app does not know, and no field 1.
        byte[] push = new FenixChannelProtoWriter().Varint(2, 1).String(7, "x").ToArray();

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(push);

        Assert.Same(FenixChannelFrame.NoData, frame);
        Assert.True(frame.ShapesOk);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_empty_part_beside_a_full_one_does_not_hide_the_data(bool emptyFirst)
    {
        var empty = new FenixChannelProtoWriter();
        var full = Remote().Double(197, 1000.0);
        var push = new FenixChannelProtoWriter();
        if (emptyFirst)
        {
            push.Message(1, empty).Message(1, full);
        }
        else
        {
            push.Message(1, full).Message(1, empty);
        }

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(push.ToArray());

        Assert.True(frame.HasData);
        Assert.Equal(1000.0, frame.Altitude);
        Assert.True(frame.ShapesOk);
    }

    [Fact]
    public void The_no_data_frame_has_every_value_at_its_default()
    {
        FenixChannelFrame frame = FenixChannelFrame.NoData;

        Assert.False(frame.HasData);
        Assert.False(frame.Malformed);
        Assert.True(frame.ShapesOk);
        Assert.Empty(frame.WrongShapeFields);
        Assert.Equal(FenixFdState.Default, frame.Fd);
        Assert.Empty(frame.Fma);
        Assert.Empty(frame.UpperEcamLeft);
        Assert.Empty(frame.UpperEcamRight);
        Assert.Equal(0, frame.FwcPage);
        Assert.Equal(0, frame.FwcPhase);
        Assert.Equal(0.0, frame.Altitude);
        Assert.Equal(0.0, frame.MagHeading);
        Assert.Equal("", frame.FobText);
    }

    [Fact]
    public void Every_other_frame_says_it_has_data()
    {
        // A good push, a malformed one, one whose field 1 has the wrong wire type and a RemoteData with no
        // FMA: each is data or a finding, never "nothing to read", so the gate must not skip any of them.
        byte[][] pushes =
        [
            FenixChannelProtoWriter.GoodPush(),
            FenixChannelProtoWriter.GoodPush()[..20],
            new FenixChannelProtoWriter().Varint(1, 5).ToArray(),
            new FenixChannelProtoWriter().Message(1, new FenixChannelProtoWriter().Double(197, 1000.0)).ToArray()
        ];

        foreach (byte[] push in pushes)
        {
            Assert.True(FenixChannelDecoder.DecodeDisplayData(push).HasData);
        }
        Assert.True(FenixChannelFrame.MalformedFrame.HasData);
        Assert.True(new FenixChannelFrame(false, [], FenixFdState.Default, [], [], [], 0, 0, 0, 0, "").HasData);
    }

    // ---- Malformed messages ---------------------------------------------------------------------

    [Fact]
    public void A_malformed_message_is_flagged()
    {
        byte[] cut = FenixChannelProtoWriter.GoodPush()[..20];

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(cut);

        Assert.True(frame.Malformed);
        Assert.False(frame.ShapesOk);
        Assert.Equal(FenixFdState.Default, frame.Fd);
        Assert.Empty(frame.Fma);
        Assert.Equal(0.0, frame.Altitude);
        Assert.Equal("", frame.FobText);
    }

    [Fact]
    public void A_push_cut_at_any_byte_is_malformed()
    {
        // Field 1 holds the whole of what the app reads, so a cut anywhere after the first byte leaves its
        // length past the end of the data.
        byte[] push = FenixChannelProtoWriter.GoodPush();

        for (int length = 1; length < push.Length; length++)
        {
            FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(push.AsSpan(0, length));
            Assert.True(frame.Malformed, $"cut at {length} bytes");
        }
    }

    [Fact]
    public void A_good_push_with_damage_after_it_carries_none_of_its_values()
    {
        // A group tag, which protobuf here never uses, behind a whole good push.
        byte[] damaged = [.. FenixChannelProtoWriter.GoodPush(altitude: 1234), 0x0B];

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(damaged);

        Assert.Same(FenixChannelFrame.MalformedFrame, frame);
        Assert.Equal(0.0, frame.Altitude);
        Assert.Empty(frame.Fma);
    }

    [Theory]
    [InlineData(110)]   // FdData
    [InlineData(111)]   // an FMA entry
    [InlineData(173)]   // an element of the left column
    [InlineData(174)]   // an element of the right column
    [InlineData(194)]   // HeadingData
    public void A_nested_part_cut_short_inside_itself_makes_the_whole_message_malformed(int field)
    {
        // The part's own length is right, so the reader of the message around it gets through; only the
        // part's reader finds the cut. The valid field after it must not rescue it.
        FenixChannelFrame frame = Decode(Remote().Message(field, CutShort(field)).Double(197, 1000.0));

        Assert.True(frame.Malformed);
        Assert.False(frame.ShapesOk);
        Assert.Equal(0.0, frame.Altitude);
        Assert.Empty(frame.Fma);
    }

    [Theory]
    [InlineData(110)]
    [InlineData(111)]
    [InlineData(173)]
    [InlineData(174)]
    [InlineData(194)]
    public void A_nested_part_with_a_damaged_tag_makes_the_whole_message_malformed(int field)
    {
        // The part is one byte, a tag cut short: the part's reader never gets as far as a value.
        FenixChannelFrame frame = Decode(Remote()
            .Message(field, new FenixChannelProtoWriter().Raw(0x80))
            .Double(197, 1000.0));

        Assert.True(frame.Malformed);
        Assert.Equal(0.0, frame.Altitude);
    }

    [Fact]
    public void A_remote_data_part_cut_short_inside_itself_makes_the_whole_message_malformed()
    {
        // Field 1's own length is right; the double inside it has two of its eight bytes.
        byte[] push = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Tag(197, 1).Raw(0, 0))
            .Varint(2, 1)
            .ToArray();

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(push);

        Assert.Same(FenixChannelFrame.MalformedFrame, frame);
    }

    // ---- The helper itself ----------------------------------------------------------------------

    [Fact]
    public void GoodPush_decodes_with_every_shape_right()
    {
        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(FenixChannelProtoWriter.GoodPush());

        Assert.True(frame.ShapesOk);
        Assert.True(frame.HasData);
        Assert.Equal(16, frame.Fma.Count);
        Assert.Equal(1000.0, frame.Altitude);
        Assert.Equal(90.0, frame.MagHeading);
        Assert.Equal("wFOB:   g2940 cs KG", frame.FobText);
        Assert.True(frame.Fd.FdOnCapt);
        Assert.True(frame.Fd.FdOnFo);
        Assert.Empty(frame.UpperEcamLeft);
    }

    [Fact]
    public void GoodPush_carries_the_values_it_is_given()
    {
        byte[] push = FenixChannelProtoWriter.GoodPush(altitude: 2500, magHeading: 270.5, fob: "wFOB:   g6480 cs LBS",
            fdOnCapt: false, fdOnFo: true, upperEcamLeft: ["g    SIGNS ON", "gPARK BRK"]);

        FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(push);

        Assert.True(frame.ShapesOk);
        Assert.Equal(2500.0, frame.Altitude);
        Assert.Equal(270.5, frame.MagHeading);
        Assert.Equal("wFOB:   g6480 cs LBS", frame.FobText);
        Assert.False(frame.Fd.FdOnCapt);
        Assert.True(frame.Fd.FdOnFo);
        Assert.Equal(new[] { "g    SIGNS ON", "gPARK BRK" }, frame.UpperEcamLeft);
    }

    // ---- ECAM entries ---------------------------------------------------------------------------

    [Fact]
    public void Decodes_ecam_entries_with_heartbeat()
    {
        var first = new FenixChannelProtoWriter()
            .String(1, "01- 0-180").String(2, "MEMO-T.O MEMO").String(3, "T.O AUTO BRK.....MAX");
        var second = new FenixChannelProtoWriter()
            .String(1, "22- 0-100").String(2, "TEST NAME").String(3, "TEST TITLE").Bool(4, true);
        byte[] message = new FenixChannelProtoWriter().Message(1, first).Message(1, second).Bool(2, true).ToArray();

        FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(message);

        Assert.Equal(
            new[]
            {
                new FenixEcamEntry("01- 0-180", "MEMO-T.O MEMO", "T.O AUTO BRK.....MAX", false),
                new FenixEcamEntry("22- 0-100", "TEST NAME", "TEST TITLE", true)
            },
            entries.Entries);
        Assert.True(entries.Heartbeat);
        Assert.True(entries.ShapesOk);
    }

    [Fact]
    public void An_ecam_message_with_nothing_in_it_is_an_empty_list()
    {
        FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(ReadOnlySpan<byte>.Empty);

        Assert.Empty(entries.Entries);
        Assert.False(entries.Heartbeat);
        Assert.True(entries.ShapesOk);
    }

    [Fact]
    public void A_wrong_shaped_ecam_entry_is_recorded()
    {
        // Title came as a varint.
        byte[] message = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().String(1, "01- 0-180").Varint(3, 7))
            .ToArray();

        FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(message);

        Assert.Equal(new[] { "1.3" }, entries.WrongShapeFields);
        Assert.Equal(new[] { new FenixEcamEntry("01- 0-180", "", "", false) }, entries.Entries);
        Assert.False(entries.ShapesOk);
        Assert.False(entries.Malformed);
    }

    [Fact]
    public void A_wrong_shaped_ecam_message_field_is_recorded_by_its_path()
    {
        // Entries came as a varint and Heartbeat as a string, around one entry that is right.
        byte[] message = new FenixChannelProtoWriter()
            .Varint(1, 5)
            .Message(1, new FenixChannelProtoWriter().String(1, "A"))
            .String(2, "x")
            .ToArray();

        FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(message);

        Assert.Equal(new[] { "1", "2" }, entries.WrongShapeFields);
        Assert.Equal(new[] { new FenixEcamEntry("A", "", "", false) }, entries.Entries);
        Assert.False(entries.Heartbeat);
    }

    [Fact]
    public void An_ecam_entry_cut_short_inside_itself_makes_the_whole_message_malformed()
    {
        // The entry's own length is right; its Ata is a string of five bytes with one. The heartbeat after
        // it must not rescue it.
        byte[] message = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter().Tag(1, 2).Raw(5, 0x41))
            .Bool(2, true)
            .ToArray();

        FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(message);

        Assert.True(entries.Malformed);
        Assert.False(entries.ShapesOk);
        Assert.Empty(entries.Entries);
        Assert.False(entries.Heartbeat);
    }

    [Theory]
    [InlineData(new byte[] { 0x0A })]                          // Entries with no length
    [InlineData(new byte[] { 0x80 })]                          // a tag cut short
    [InlineData(new byte[] { 0x0B })]                          // a group tag
    [InlineData(new byte[] { 0x0A, 0x01, 0x80, 0x10, 0x01 })]  // an entry that is a tag cut short, then a heartbeat
    public void Damaged_ecam_bytes_give_a_malformed_message_with_nothing_in_it(byte[] message)
    {
        FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(message);

        Assert.True(entries.Malformed);
        Assert.Empty(entries.Entries);
        Assert.False(entries.Heartbeat);
    }

    // ---- Never throws ---------------------------------------------------------------------------

    [Fact]
    public void Never_throws_on_any_input()
    {
        Exception? thrown = Record.Exception(() =>
        {
            // Each message goes to both decoders: either one may be handed the other's.
            foreach (byte[] message in RandomInputs())
            {
                FenixChannelDecoder.DecodeDisplayData(message);
                FenixChannelDecoder.DecodeEcamEntries(message);
            }
        });

        Assert.Null(thrown);
    }

    [Fact]
    public void A_frame_without_data_or_a_malformed_one_is_always_the_shared_empty_frame()
    {
        // So no value rides on a frame that says "nothing to read" or "damaged", and every other frame has data.
        var broken = new List<string>();
        foreach (byte[] message in RandomInputs())
        {
            FenixChannelFrame frame = FenixChannelDecoder.DecodeDisplayData(message);
            if (!frame.HasData && !ReferenceEquals(frame, FenixChannelFrame.NoData))
            {
                broken.Add($"no data, but not NoData: {Convert.ToHexString(message)}");
            }
            if (frame.Malformed && !(ReferenceEquals(frame, FenixChannelFrame.MalformedFrame) && frame.HasData))
            {
                broken.Add($"malformed, but not MalformedFrame with data: {Convert.ToHexString(message)}");
            }

            FenixEcamEntries entries = FenixChannelDecoder.DecodeEcamEntries(message);
            bool carriesValues = entries.Entries.Count != 0 || entries.Heartbeat || entries.WrongShapeFields.Count != 0;
            if (entries.Malformed && carriesValues)
            {
                broken.Add($"malformed ECAM entries that carry values: {Convert.ToHexString(message)}");
            }
        }

        Assert.Empty(broken);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    // A RemoteData part holding the 16 empty FMA slots every real push carries, for a test to add the
    // fields it is about.
    private static FenixChannelProtoWriter Remote()
    {
        var remote = new FenixChannelProtoWriter();
        for (int slot = 0; slot < 16; slot++)
        {
            remote.Message(111, new FenixChannelProtoWriter());
        }
        return remote;
    }

    // The RemoteData part as field 1 of an AircraftDataPush, decoded.
    private static FenixChannelFrame Decode(FenixChannelProtoWriter remote)
        => FenixChannelDecoder.DecodeDisplayData(new FenixChannelProtoWriter().Message(1, remote).ToArray());

    // A string field written from the bytes given. The writer's String takes a string, which can only hold
    // text that is valid, so bytes that are not UTF-8 go in through Tag and Raw. Returns the writer it got.
    private static FenixChannelProtoWriter RawString(FenixChannelProtoWriter writer, int field, params byte[] bytes)
        => writer.Tag(field, 2).Raw((byte)bytes.Length).Raw(bytes);

    // The contents of a nested part cut short inside itself, for the part with this field number: the
    // part's own length (written by Message) is right, but its first value is not all there.
    private static FenixChannelProtoWriter CutShort(int field) => field is 110 or 194
        ? new FenixChannelProtoWriter().Tag(1, 1).Raw(0, 0)       // a double with 2 of its 8 bytes
        : new FenixChannelProtoWriter().Tag(1, 2).Raw(5, 0x41);   // a string of 5 with 1 byte

    // 2,000 rounds of messages from a fixed seed: random bytes, a good push with one byte changed, and the
    // real field numbers in random wire shapes (twice).
    private static IEnumerable<byte[]> RandomInputs()
    {
        var random = new Random(1234);
        byte[] good = FenixChannelProtoWriter.GoodPush(upperEcamLeft: ["g    SIGNS ON"]);

        for (int round = 0; round < 2000; round++)
        {
            byte[] noise = new byte[random.Next(0, 129)];
            random.NextBytes(noise);
            yield return noise;

            byte[] changed = (byte[])good.Clone();
            changed[random.Next(changed.Length)] = (byte)random.Next(256);
            yield return changed;

            yield return RandomShapes(random, 0);
            yield return RandomShapes(random, 0);
        }
    }

    // The real field numbers sent in random wire shapes, nested up to three deep: it reaches the
    // wrong-shape and nested-part paths that raw random bytes almost never do.
    private static byte[] RandomShapes(Random random, int depth)
    {
        int[] fields = [1, 2, 3, 4, 5, 10, 11, 110, 111, 153, 173, 174, 194, 197];
        var writer = new FenixChannelProtoWriter();
        for (int count = random.Next(0, 9); count > 0; count--)
        {
            int field = fields[random.Next(fields.Length)];
            switch (random.Next(5))
            {
                case 0:
                    writer.Varint(field, (ulong)random.NextInt64());
                    break;
                case 1:
                    writer.Double(field, random.NextDouble());
                    break;
                case 2:
                    writer.Float(field, 1.5f);
                    break;
                case 3:
                    writer.String(field, "gX");
                    break;
                default:
                    byte[] inner = depth < 3 ? RandomShapes(random, depth + 1) : Array.Empty<byte>();
                    writer.Message(field, new FenixChannelProtoWriter().Raw(inner));
                    break;
            }
        }
        return writer.ToArray();
    }
}

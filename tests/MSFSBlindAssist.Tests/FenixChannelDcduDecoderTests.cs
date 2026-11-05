// Characterization tests for FenixChannelDcdu, the decoder of the captain's DCDU from the RemoteData message of the Fenix
// display channel (the gRPC service on 127.0.0.1:18083). Pure logic: every message is built here with
// FenixChannelProtoWriter and the field numbers written out literally (186 for the DCDU, 2 to 15 for its lines), never read
// from FenixChannelFields and never from a capture, so a wrong number in the app's table fails a test.
//
// The contract: field 186 holds the DCDU; its header (2, 3), five message lines (4 to 8), three info lines (9 to 11) and
// four key labels (12 to 15) are strings, kept as sent with their format letters; a line left out is "" (protobuf-net
// writes nothing for a null string); the print frame (1) is not read. A 186 that is absent is missing content, a 186 that
// is not a message or a line in another shape is a wrong shape named by its path, and a format letter the app does not
// know makes the section an unknown code that names the part ([FNX-24]); "i", which the DCDU draws on a pressed key and on a
// chosen answer's status (measured 2026-11-03), is a letter the parser knows. A shape problem is reported before a letter.
// Plain is a line's text for the window and the announcement: no codes, no color names, the end trimmed (owner's ruling).

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelDcduDecoderTests
{
    // 14 spaces, as the empty DCDU's Info2 and Info3 were measured on 3.0.0.7613 (2026-11-03).
    private const string Spaces14 = "              ";

    // U+2800, the blank braille cell the display uses as a spacer, written as a cast because it cannot be seen in source.
    private static readonly string Spacer = ((char)0x2800).ToString();

    // A DCDU part with the print frame set, as the empty DCDU was measured, and the lines given: field number and text.
    private static FenixChannelProtoWriter Content(params (int Field, string Text)[] lines)
    {
        var content = new FenixChannelProtoWriter().Bool(1, true);
        foreach ((int field, string text) in lines)
        {
            content.String(field, text);
        }

        return content;
    }

    // The RemoteData message of a push whose field 186 is the given part.
    private static FenixChannelPush Remote(FenixChannelProtoWriter dcdu)
        => FenixChannelPush.From(new FenixChannelProtoWriter().Message(186, dcdu).ToArray())!;

    private static FenixChannelSection<FenixDcduScreen> Decode(FenixChannelProtoWriter dcdu)
        => FenixChannelDcdu.Decode(Remote(dcdu));

    private static FenixDcduScreen Decoded(FenixChannelProtoWriter dcdu)
    {
        FenixChannelSection<FenixDcduScreen> section = Decode(dcdu);
        Assert.True(section.IsOk, section.Problem);
        return section.Value!;
    }

    private static FenixChannelSnapshot Snapshot(bool trusted, string reason, FenixChannelPush? push)
        => new(new FenixDisplayVerdict(trusted, reason), null, push, null, 0, 1);

    // ---- Values -----------------------------------------------------------------------------

    [Fact]
    public void Each_line_lands_in_its_place()
    {
        // No two lines are alike, so a number crossed with another shows. Uppercase and digits only: a lowercase letter
        // would be a format code.
        FenixDcduScreen screen = Decoded(Content(Enumerable.Range(2, 14).Select(field => (field, $"T{field}")).ToArray()));

        Assert.Equal("T2", screen.HeaderLeft);
        Assert.Equal("T3", screen.HeaderRight);
        Assert.Equal(new[] { "T4", "T5", "T6", "T7", "T8" }, screen.Message.ToArray());
        Assert.Equal(new[] { "T9", "T10", "T11" }, screen.Info.ToArray());
        Assert.Equal("T12", screen.LeftKey1);
        Assert.Equal("T13", screen.LeftKey2);
        Assert.Equal("T14", screen.RightKey1);
        Assert.Equal("T15", screen.RightKey2);
    }

    [Fact]
    public void The_measured_empty_dcdu_decodes()
    {
        // Field 1 a varint 1, fields 2 to 15 all present as strings: 2 to 9 and 12 to 15 empty, Info2 and Info3 14 spaces.
        var texts = Enumerable.Range(2, 14)
            .Select(field => (field, field is 10 or 11 ? Spaces14 : ""))
            .ToArray();

        FenixDcduScreen screen = Decoded(Content(texts));

        Assert.Equal("", screen.HeaderLeft);
        Assert.Equal("", screen.HeaderRight);
        Assert.Equal(new[] { "", "", "", "", "" }, screen.Message.ToArray());
        Assert.Equal(new[] { "", Spaces14, Spaces14 }, screen.Info.ToArray());
        Assert.Equal("", screen.LeftKey1);
        Assert.Equal("", screen.LeftKey2);
        Assert.Equal("", screen.RightKey1);
        Assert.Equal("", screen.RightKey2);
    }

    [Fact]
    public void A_coded_line_is_kept_as_sent()
    {
        // The format letters stay in the line: the window and the announcement say each color from them (FNX-24).
        FenixDcduScreen screen = Decoded(Content((2, "cCLIMB gTO aFL350"), (4, "wMESSAGE"), (9, "g⠀ACTIVE"), (12, "c>ROGER")));

        Assert.Equal("cCLIMB gTO aFL350", screen.HeaderLeft);
        Assert.Equal("wMESSAGE", screen.Message[0]);
        Assert.Equal("g⠀ACTIVE", screen.Info[0]);
        Assert.Equal("c>ROGER", screen.LeftKey1);
    }

    [Fact]
    public void An_absent_line_is_empty()
    {
        // Only the print frame and one line were sent: every other line reads as protobuf-net leaves it, "".
        FenixDcduScreen screen = Decoded(Content((6, "ONE LINE")));

        Assert.Equal("", screen.HeaderLeft);
        Assert.Equal("", screen.HeaderRight);
        Assert.Equal(new[] { "", "", "ONE LINE", "", "" }, screen.Message.ToArray());
        Assert.Equal(new[] { "", "", "" }, screen.Info.ToArray());
        Assert.Equal("", screen.LeftKey1);
        Assert.Equal("", screen.LeftKey2);
        Assert.Equal("", screen.RightKey1);
        Assert.Equal("", screen.RightKey2);

        // A part with nothing in it at all is a DCDU with every line empty.
        FenixDcduScreen nothing = Decoded(new FenixChannelProtoWriter());
        Assert.Equal(new[] { "", "", "", "", "" }, nothing.Message.ToArray());
        Assert.Equal(new[] { "", "", "" }, nothing.Info.ToArray());
    }

    [Fact]
    public void The_print_frame_and_other_fields_are_ignored()
    {
        // Field 1 in any shape, and a field the app does not read, change nothing.
        FenixDcduScreen screen = Decoded(new FenixChannelProtoWriter()
            .String(1, "NOT A FLAG").Double(16, 1.5).Float(17, 2.5f).Bool(40, true).String(4, "MSG"));

        Assert.Equal("MSG", screen.Message[0]);
        Assert.Equal("", screen.HeaderLeft);
    }

    // ---- Missing content ---------------------------------------------------------------------

    [Fact]
    public void A_missing_dcdu_is_no_content()
    {
        // A RemoteData message with other parts but no 186.
        FenixChannelPush remote = FenixChannelPush.From(new FenixChannelProtoWriter()
            .Message(110, new FenixChannelProtoWriter().Bool(5, true)).Double(197, 1000).ToArray())!;

        FenixChannelSection<FenixDcduScreen> section = FenixChannelDcdu.Decode(remote);

        Assert.False(section.IsOk);
        Assert.Equal("no DCDU content", section.Problem);

        // A message with no field at all is the same.
        FenixChannelPush empty = FenixChannelPush.From(ReadOnlyMemory<byte>.Empty)!;
        Assert.Equal("no DCDU content", FenixChannelDcdu.Decode(empty).Problem);
    }

    // ---- Shapes ------------------------------------------------------------------------------

    [Fact]
    public void A_wrong_shape_names_its_field()
    {
        // 186.4 a varint.
        FenixChannelSection<FenixDcduScreen> line = Decode(new FenixChannelProtoWriter().Varint(4, 1));
        Assert.False(line.IsOk);
        Assert.Equal("field 186.4 has the wrong shape", line.Problem);

        // 186 itself a fixed64, a varint or a fixed32.
        foreach (FenixChannelProtoWriter remote in new[]
                 {
                     new FenixChannelProtoWriter().Double(186, 1.5),
                     new FenixChannelProtoWriter().Varint(186, 1),
                     new FenixChannelProtoWriter().Float(186, 1.5f)
                 })
        {
            FenixChannelSection<FenixDcduScreen> section = FenixChannelDcdu.Decode(FenixChannelPush.From(remote.ToArray())!);
            Assert.False(section.IsOk);
            Assert.Equal("field 186 has the wrong shape", section.Problem);
        }
    }

    [Fact]
    public void A_malformed_dcdu_is_a_wrong_shape()
    {
        // Field 2 of the part claims five bytes and has one: the part is not valid protobuf.
        FenixChannelPush remote = Remote(new FenixChannelProtoWriter().Raw(0x12, 0x05, 0x41));

        Assert.Equal("field 186 has the wrong shape", FenixChannelDcdu.Decode(remote).Problem);
    }

    [Fact]
    public void Every_line_is_checked_for_its_shape()
    {
        for (int field = 2; field <= 15; field++)
        {
            string expected = $"field 186.{field} has the wrong shape";

            Assert.Equal(expected, Decode(new FenixChannelProtoWriter().Varint(field, 1)).Problem);
            Assert.Equal(expected, Decode(new FenixChannelProtoWriter().Double(field, 1.5)).Problem);
            Assert.Equal(expected, Decode(new FenixChannelProtoWriter().Float(field, 1.5f)).Problem);
        }
    }

    [Fact]
    public void A_line_that_is_not_valid_utf8_is_a_wrong_shape()
    {
        // 0xFF is never valid in UTF-8; a string with a replacement character in it would pass for text.
        FenixChannelSection<FenixDcduScreen> section = Decode(new FenixChannelProtoWriter().Tag(7, 2).Raw(0x02, 0xFF, 0xFE));

        Assert.False(section.IsOk);
        Assert.Equal("field 186.7 has the wrong shape", section.Problem);
    }

    [Fact]
    public void The_first_wrong_line_is_named()
    {
        // 186.9 and 186.5 are both wrong; the lower number is read first.
        FenixChannelSection<FenixDcduScreen> section = Decode(new FenixChannelProtoWriter().Varint(9, 1).Varint(5, 1));

        Assert.Equal("field 186.5 has the wrong shape", section.Problem);
    }

    [Fact]
    public void A_wrong_shape_is_reported_before_an_unknown_letter()
    {
        // The header has a letter the app does not know, and a later key label is in another shape.
        FenixChannelSection<FenixDcduScreen> section = Decode(Content((2, "qHEADER")).Varint(14, 1));

        Assert.Equal("field 186.14 has the wrong shape", section.Problem);
    }

    // ---- Format letters ----------------------------------------------------------------------

    [Fact]
    public void An_unknown_letter_makes_the_dcdu_not_available()
    {
        FenixChannelSection<FenixDcduScreen> uplink = Decode(Content((4, "cCLIMB qTO")));
        Assert.False(uplink.IsOk);
        Assert.Equal("unknown code 'q' in the DCDU message", uplink.Problem);

        // Lowercase free text: h is the first letter of "hello" that is no code (e, l and o follow it, and l is a code).
        FenixChannelSection<FenixDcduScreen> freeText = Decode(Content((5, "hello")));
        Assert.False(freeText.IsOk);
        Assert.Equal("unknown code 'h' in the DCDU message", freeText.Problem);
    }

    [Fact]
    public void The_answer_letter_i_is_known_on_the_dcdu()
    {
        // Measured on 3.0.0.7613 (2026-11-03): "i" is drawn on a pressed key ("ciWILCO*") and on a chosen answer's status
        // ("ciWILCO"). It is a letter the DCDU knows, so a line with it decodes, in every part, and keeps it as sent.
        FenixDcduScreen screen = Decoded(Content(
            (2, "g 2021Z FROM EDDF"), (3, "ciWILCO"), (4, "ciMSG"), (9, "gi"), (12, "c*iCANCEL"), (13, "ci"), (14, "cWILCO*"),
            (15, "ciWILCO*")));

        Assert.Equal("ciWILCO", screen.HeaderRight);
        Assert.Equal("ciMSG", screen.Message[0]);
        Assert.Equal("gi", screen.Info[0]);
        Assert.Equal("c*iCANCEL", screen.LeftKey1);
        Assert.Equal("ci", screen.LeftKey2);
        Assert.Equal("ciWILCO*", screen.RightKey2);

        // It does not make any other letter known: the first letter that is still unknown names the problem, whether it
        // comes before the "i" or after it.
        FenixChannelSection<FenixDcduScreen> before = Decode(Content((15, "cqiWILCO*")));
        Assert.Equal("unknown code 'q' in the DCDU key labels", before.Problem);
        FenixChannelSection<FenixDcduScreen> after = Decode(Content((3, "ciqWILCO")));
        Assert.Equal("unknown code 'q' in the DCDU header", after.Problem);
    }

    [Theory]
    [InlineData(2, "DCDU header")]
    [InlineData(3, "DCDU header")]
    [InlineData(4, "DCDU message")]
    [InlineData(5, "DCDU message")]
    [InlineData(6, "DCDU message")]
    [InlineData(7, "DCDU message")]
    [InlineData(8, "DCDU message")]
    [InlineData(9, "DCDU info lines")]
    [InlineData(10, "DCDU info lines")]
    [InlineData(11, "DCDU info lines")]
    [InlineData(12, "DCDU key labels")]
    [InlineData(13, "DCDU key labels")]
    [InlineData(14, "DCDU key labels")]
    [InlineData(15, "DCDU key labels")]
    public void Every_line_is_checked_for_its_letters_and_names_its_part(int field, string part)
    {
        FenixChannelSection<FenixDcduScreen> section = Decode(Content((field, "qX")));

        Assert.False(section.IsOk);
        Assert.Equal($"unknown code 'q' in the {part}", section.Problem);
    }

    [Fact]
    public void The_first_unknown_letter_in_the_order_the_parts_are_read_is_named()
    {
        // A key label, an info line and a header all carry one; the header is read first, then the message, the info
        // lines and the key labels.
        Assert.Equal("unknown code 'q' in the DCDU header",
            Decode(Content((12, "zKEY"), (9, "kINFO"), (3, "qHEAD"))).Problem);
        Assert.Equal("unknown code 'k' in the DCDU info lines",
            Decode(Content((12, "zKEY"), (9, "kINFO"))).Problem);
        Assert.Equal("unknown code 'z' in the DCDU key labels", Decode(Content((12, "zKEY"))).Problem);
    }

    [Fact]
    public void A_known_but_silent_letter_is_not_an_unknown_one()
    {
        // p, d and x1 are known and draw nothing; the lines decode.
        Assert.True(Decode(Content((4, "pgTEXT"), (5, "wA dB"), (6, "ax1ILS"))).IsOk);
    }

    // ---- Read --------------------------------------------------------------------------------

    [Fact]
    public void Read_takes_the_verdict_and_the_message_from_one_snapshot()
    {
        FenixChannelPush push = Remote(Content((4, "wMESSAGE"), (12, "c>ROGER")));

        // Not trusted: the verdict's reason is the problem, whatever the message says.
        FenixChannelSection<FenixDcduScreen> off = FenixChannelDcdu.Read(Snapshot(false, "display off", push));
        Assert.False(off.IsOk);
        Assert.Equal("display off", off.Problem);

        // Not trusted with no message: still the verdict's reason.
        Assert.Equal("display off", FenixChannelDcdu.Read(Snapshot(false, "display off", null)).Problem);

        // Trusted: the message decoded.
        FenixChannelSection<FenixDcduScreen> on = FenixChannelDcdu.Read(Snapshot(true, "trusted", push));
        Assert.True(on.IsOk, on.Problem);
        Assert.Equal("wMESSAGE", on.Value!.Message[0]);
        Assert.Equal("c>ROGER", on.Value.LeftKey1);

        // Trusted with no message at all.
        FenixChannelSection<FenixDcduScreen> none = FenixChannelDcdu.Read(Snapshot(true, "trusted", null));
        Assert.False(none.IsOk);
        Assert.Equal("no DCDU content", none.Problem);

        // Trusted, and the decoder's own problems come through.
        Assert.Equal("field 186.4 has the wrong shape",
            FenixChannelDcdu.Read(Snapshot(true, "trusted", Remote(new FenixChannelProtoWriter().Varint(4, 1)))).Problem);
        Assert.Equal("unknown code 'h' in the DCDU message",
            FenixChannelDcdu.Read(Snapshot(true, "trusted", Remote(Content((5, "hello"))))).Problem);
    }

    // ---- Plain and Spoken ---------------------------------------------------------------------

    [Fact]
    public void Plain_is_the_lines_text_without_codes_and_with_the_end_trimmed()
    {
        // The Fenix's own lines, as measured: the codes are dropped, the spacing inside the line is kept, and the padding at
        // the end goes.
        Assert.Equal("BVI4RK  RCD REQUEST", FenixChannelDcdu.Plain("cBVI4RKw  RCD REQUEST   "));
        Assert.Equal("STANDBY  IF NO", FenixChannelDcdu.Plain("cSTANDBYw  IF cNO       "));
        Assert.Equal("FSM 2010 261103 EDDF", FenixChannelDcdu.Plain("FSM 2010 261103 cEDDFw  "));
        Assert.Equal("MSG        PGE", FenixChannelDcdu.Plain("MSG        PGE"));

        // The start is kept as the display drew it: the header's left half starts with a space after its color.
        Assert.Equal(" 2021Z FROM EDDF", FenixChannelDcdu.Plain("g 2021Z FROM EDDF"));

        // The answer letter is a code and is never part of the text.
        Assert.Equal("WILCO*", FenixChannelDcdu.Plain("ciWILCO*"));
        Assert.Equal("WILCO", FenixChannelDcdu.Plain("ciWILCO"));

        // "!" is a space, as the speech function has it, and the braille blank is a space too.
        Assert.Equal("ALT CRZ", FenixChannelDcdu.Plain("gALT!CRZ"));
        Assert.Equal(" ACTIVE", FenixChannelDcdu.Plain("g" + Spacer + "ACTIVE"));
        Assert.Equal("ALT", FenixChannelDcdu.Plain("gALT!!!"));
    }

    [Fact]
    public void A_line_with_nothing_left_after_the_trim_is_blank()
    {
        // Plain is empty exactly when the line has nothing to read: nothing, spaces, a color letter, a color letter and spaces.
        foreach (string blank in new[] { "", "              ", "c", "c ", "g", "wc", "ci", "c!", "c" + Spacer + Spacer })
        {
            Assert.Equal("", FenixChannelDcdu.Plain(blank));
        }
    }

    [Fact]
    public void Spoken_collapses_runs_of_spaces_and_trims_both_ends()
    {
        Assert.Equal("BVI4RK RCD REQUEST", FenixChannelDcdu.Spoken("cBVI4RKw  RCD REQUEST   "));
        Assert.Equal("2021Z FROM EDDF", FenixChannelDcdu.Spoken("g 2021Z FROM EDDF"));
        Assert.Equal("ACTIVE", FenixChannelDcdu.Spoken("g" + Spacer + "ACTIVE"));
        Assert.Equal("FL350 AT 1426Z", FenixChannelDcdu.Spoken("cFL350   AT  1426Z"));
        Assert.Equal("", FenixChannelDcdu.Spoken("c  "));
    }

    [Fact]
    public void The_display_name_is_dcdu()
    {
        Assert.Equal("DCDU", FenixChannelDcdu.DisplayName);
    }
}

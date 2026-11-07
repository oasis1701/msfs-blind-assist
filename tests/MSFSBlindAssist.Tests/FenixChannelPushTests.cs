// Characterization tests for FenixChannelPush, the message every feature of the Fenix display channel reads its
// fields from (the gRPC service on 127.0.0.1:18083), and for FenixChannelSection and FenixChannelRows, what a
// feature's decoder and its window composer give back. Pure logic: every message is built here with
// FenixChannelProtoWriter, never read from Fenix's own capture.
//
// The contract: a push is one protobuf message whose top level has been walked and found valid, or there is no
// push; its fields are indexed by number, once, the first time one is read; a singular field reads as the last
// occurrence sent, a repeated one as every occurrence in order; a part nested inside is judged only when it is
// read, so a part that is damaged inside costs the feature that reads it and nothing else; nothing throws,
// whatever the bytes are.

using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelPushTests
{
    // ---- Reading fields -------------------------------------------------------------------------

    [Fact]
    public void A_message_indexes_each_field_and_keeps_the_last_singular_one()
    {
        byte[] bytes = new FenixChannelProtoWriter()
            .Varint(1, 5)
            .String(2, "abc")
            .Double(3, 1.5)
            .Float(4, 2.5f)
            .Bool(5, true)
            .Bool(6, false)
            .Varint(7, 2)      // any varint but 0 is true, as protobuf reads a bool
            .Varint(1, 9)      // field 1 again: a singular field reads as the last one sent
            .ToArray();

        FenixChannelPush? push = FenixChannelPush.From(bytes);

        Assert.NotNull(push);
        Assert.True(push.TryGet(1, out FenixChannelField one));
        Assert.Equal(1, one.Number);
        Assert.Equal(FenixWireType.Varint, one.Type);
        Assert.Equal(9UL, one.Varint);

        Assert.True(push.TryGet(2, out FenixChannelField text));
        Assert.Equal(FenixWireType.LengthDelimited, text.Type);
        Assert.Equal("abc", text.AsString());

        Assert.True(push.TryGet(3, out FenixChannelField altitude));
        Assert.Equal(FenixWireType.Fixed64, altitude.Type);
        Assert.Equal(1.5, altitude.AsDouble());

        Assert.True(push.TryGet(4, out FenixChannelField pressure));
        Assert.Equal(FenixWireType.Fixed32, pressure.Type);
        Assert.Equal(2.5f, pressure.Fixed32);

        Assert.True(push.TryGet(5, out FenixChannelField on));
        Assert.True(on.AsBool());
        Assert.True(push.TryGet(6, out FenixChannelField off));
        Assert.False(off.AsBool());
        Assert.True(push.TryGet(7, out FenixChannelField two));
        Assert.True(two.AsBool());

        // A field that was not sent is not there.
        Assert.False(push.TryGet(8, out FenixChannelField absent));
        Assert.Equal(default(FenixChannelField), absent);
    }

    [Fact]
    public void A_repeated_field_comes_back_in_order()
    {
        byte[] bytes = new FenixChannelProtoWriter()
            .Varint(7, 30)
            .String(8, "between")
            .Varint(7, 10)
            .String(8, "after")
            .Varint(7, 20)
            .PackedVarints(9, 1, 2, 3)
            .ToArray();

        FenixChannelPush? push = FenixChannelPush.From(bytes);

        Assert.NotNull(push);
        // Another field in between does not break the order of the first, nor the other's own.
        Assert.Equal(new ulong[] { 30, 10, 20 }, push.All(7).Select(field => field.Varint).ToArray());
        Assert.Equal(new[] { "between", "after" }, push.All(8).Select(field => field.AsString()).ToArray());

        // The last of them is what a singular read of the same number gives.
        Assert.True(push.TryGet(7, out FenixChannelField last));
        Assert.Equal(20UL, last.Varint);

        // A packed list is one field whose bytes are the caller's to unpack.
        FenixChannelField packed = Assert.Single(push.All(9));
        Assert.Equal(FenixWireType.LengthDelimited, packed.Type);
        Assert.Equal(new byte[] { 1, 2, 3 }, packed.Bytes.ToArray());

        // A number that was not sent has no occurrence.
        Assert.Empty(push.All(99));
    }

    [Fact]
    public void A_nested_message_reads_as_its_own_push()
    {
        byte[] bytes = new FenixChannelProtoWriter()
            .Message(1, new FenixChannelProtoWriter()
                .String(2, "inner")
                .Message(3, new FenixChannelProtoWriter().Double(1, 90.5)))
            .Varint(2, 4)
            .Message(5, new FenixChannelProtoWriter())
            .Double(6, 1.0)
            .ToArray();

        FenixChannelPush? push = FenixChannelPush.From(bytes);

        Assert.NotNull(push);
        Assert.True(push.TryGet(1, out FenixChannelField part));
        FenixChannelPush? inner = part.AsMessage();
        Assert.NotNull(inner);

        // Field numbers belong to their message: field 2 inside is the string, field 2 outside the varint.
        Assert.True(inner.TryGet(2, out FenixChannelField innerText));
        Assert.Equal("inner", innerText.AsString());
        Assert.True(push.TryGet(2, out FenixChannelField outerVarint));
        Assert.Equal(4UL, outerVarint.Varint);
        Assert.False(inner.TryGet(1, out _));

        // A part inside a part reads the same way.
        Assert.True(inner.TryGet(3, out FenixChannelField deeper));
        FenixChannelPush? innermost = deeper.AsMessage();
        Assert.NotNull(innermost);
        Assert.True(innermost.TryGet(1, out FenixChannelField heading));
        Assert.Equal(90.5, heading.AsDouble());

        // An empty part is a message with no fields, not a missing one.
        Assert.True(push.TryGet(5, out FenixChannelField empty));
        FenixChannelPush? nothing = empty.AsMessage();
        Assert.NotNull(nothing);
        Assert.False(nothing.TryGet(1, out _));

        // Only a length-delimited field can be a message.
        Assert.Null(outerVarint.AsMessage());
        Assert.True(push.TryGet(6, out FenixChannelField fixed64));
        Assert.Null(fixed64.AsMessage());
    }

    // ---- Damaged bytes --------------------------------------------------------------------------

    [Fact]
    public void A_malformed_message_gives_no_push()
    {
        var cases = new Dictionary<string, byte[]>
        {
            ["a tag cut short"] = [0x80],
            ["a varint cut short"] = new FenixChannelProtoWriter().Tag(1, 0).Raw(0x80).ToArray(),
            ["a fixed64 cut short"] = new FenixChannelProtoWriter().Tag(1, 1).Raw(1, 2, 3).ToArray(),
            ["a fixed32 cut short"] = new FenixChannelProtoWriter().Tag(1, 5).Raw(1, 2).ToArray(),
            ["a length past the end"] = new FenixChannelProtoWriter().Tag(1, 2).Raw(5, 1, 2).ToArray(),
            ["a field number of 0"] = new FenixChannelProtoWriter().Tag(0, 0).Raw(0).ToArray(),
            ["a field number past 29 bits"] = new FenixChannelProtoWriter().Tag(1 << 29, 0).Raw(0).ToArray(),
            ["a start group"] = new FenixChannelProtoWriter().Tag(1, 3).ToArray(),
            ["an end group"] = new FenixChannelProtoWriter().Tag(1, 4).ToArray(),
            ["a reserved wire type 6"] = new FenixChannelProtoWriter().Tag(1, 6).ToArray(),
            ["a reserved wire type 7"] = new FenixChannelProtoWriter().Tag(1, 7).ToArray(),
            ["a varint past 64 bits"] = new FenixChannelProtoWriter().Tag(1, 0)
                .Raw(0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x02).ToArray(),
            ["a good field and then a cut one"] = new FenixChannelProtoWriter().Varint(1, 5).Tag(2, 2).Raw(9, 1).ToArray(),
            ["a real push cut short"] = FenixChannelProtoWriter.GoodPush()[..20],
        };

        foreach ((string what, byte[] bytes) in cases)
        {
            Assert.True(FenixChannelPush.From(bytes) is null, what);
        }
    }

    [Fact]
    public void A_part_that_is_malformed_inside_is_judged_only_when_it_is_read()
    {
        byte[] bytes = new FenixChannelProtoWriter()
            .Varint(1, 5)
            .Message(2, new FenixChannelProtoWriter().Tag(1, 0).Raw(0x80))   // a part with a varint cut short inside it
            .Varint(3, 7)
            .ToArray();

        FenixChannelPush? push = FenixChannelPush.From(bytes);

        // The message around it is whole, and its other fields read as always.
        Assert.NotNull(push);
        Assert.True(push.TryGet(1, out FenixChannelField before));
        Assert.Equal(5UL, before.Varint);
        Assert.True(push.TryGet(3, out FenixChannelField after));
        Assert.Equal(7UL, after.Varint);

        // The damaged part is the one that gives no push.
        Assert.True(push.TryGet(2, out FenixChannelField damaged));
        Assert.Null(damaged.AsMessage());
    }

    [Fact]
    public void A_string_with_invalid_utf8_reads_as_null()
    {
        byte[] bytes = new FenixChannelProtoWriter()
            .Tag(1, 2).Raw(2, 0xFF, 0xFE)             // bytes that are no UTF-8 at all
            .Tag(2, 2).Raw(1, 0xC3)                   // a lead byte with nothing after it
            .String(3, "Température 15°C")  // valid, with two-byte characters
            .String(4, "")                            // an empty string is still a string
            .Varint(5, 65)
            .Double(6, 1.0)
            .ToArray();

        FenixChannelPush? push = FenixChannelPush.From(bytes);

        Assert.NotNull(push);
        Assert.True(push.TryGet(1, out FenixChannelField noise));
        Assert.Null(noise.AsString());
        Assert.True(push.TryGet(2, out FenixChannelField cut));
        Assert.Null(cut.AsString());

        Assert.True(push.TryGet(3, out FenixChannelField valid));
        Assert.Equal("Température 15°C", valid.AsString());
        Assert.True(push.TryGet(4, out FenixChannelField empty));
        Assert.Equal("", empty.AsString());

        // Only a length-delimited field is a string.
        Assert.True(push.TryGet(5, out FenixChannelField varint));
        Assert.Null(varint.AsString());
        Assert.True(push.TryGet(6, out FenixChannelField fixed64));
        Assert.Null(fixed64.AsString());
    }

    // ---- One index, however many readers --------------------------------------------------------

    [Fact]
    public async Task A_push_is_read_by_several_threads_through_one_index()
    {
        // The fields are indexed the first time one is read, once: threads that ask together at the start all get
        // the very list the first of them built, never one of their own, and the push does not change after.
        var message = new FenixChannelProtoWriter();
        for (int slot = 0; slot < 16; slot++)
        {
            message.Message(111, new FenixChannelProtoWriter().String(1, "slot"));
        }

        FenixChannelPush? push = FenixChannelPush.From(message.Varint(197, 5).ToArray());
        Assert.NotNull(push);

        using var start = new ManualResetEventSlim(false);
        Task<IReadOnlyList<FenixChannelField>>[] readers = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() =>
            {
                start.Wait();
                return push.All(111);
            }))
            .ToArray();
        start.Set();
        IReadOnlyList<FenixChannelField>[] lists = await Task.WhenAll(readers).WaitAsync(MonitorTestKit.Generous);

        Assert.All(lists, list => Assert.Same(lists[0], list));
        Assert.Equal(16, lists[0].Count);
        Assert.Same(lists[0], push.All(111));
        Assert.True(push.TryGet(197, out FenixChannelField altitude));
        Assert.Equal(5UL, altitude.Varint);
    }

    // ---- What a decoder and a composer give back ------------------------------------------------

    [Fact]
    public void A_section_says_ok_wrong_shape_or_missing()
    {
        FenixChannelSection<int> ok = FenixChannelSection<int>.Ok(5);
        Assert.True(ok.IsOk);
        Assert.Equal(5, ok.Value);
        Assert.Null(ok.Problem);

        FenixChannelSection<string> wrong = FenixChannelSection<string>.WrongShape("125.2");
        Assert.False(wrong.IsOk);
        Assert.Equal("field 125.2 has the wrong shape", wrong.Problem);
        Assert.Null(wrong.Value);

        FenixChannelSection<string> missing = FenixChannelSection<string>.Missing("speed limits");
        Assert.False(missing.IsOk);
        Assert.Equal("no speed limits", missing.Problem);
        Assert.Null(missing.Value);

        // The gate's wording for a letter it does not know ("unknown code 'k' in the ground speed line"): the
        // caller names where, so it reads after "the".
        FenixChannelSection<int> unknown = FenixChannelSection<int>.UnknownCode('k', "ground speed line");
        Assert.False(unknown.IsOk);
        Assert.Equal("unknown code 'k' in the ground speed line", unknown.Problem);
        Assert.Equal(0, unknown.Value);

        // A decoder that returns default by mistake fails closed: no value is not ok, though it names no problem.
        Assert.False(default(FenixChannelSection<string>).IsOk);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void A_section_reads_the_same_under_a_comma_decimal_culture(string culture)
    {
        // The wording reaches a window's status row and the log, and a field path is written with a decimal
        // point ("125.2"): nothing in it may follow the PC's culture.
        TestCulture.Run(culture, () =>
        {
            Assert.Equal("field 125.2 has the wrong shape", FenixChannelSection<int>.WrongShape("125.2").Problem);
            Assert.Equal("no speed limits", FenixChannelSection<int>.Missing("speed limits").Problem);
            Assert.Equal(
                "unknown code 'k' in the ground speed line",
                FenixChannelSection<int>.UnknownCode('k', "ground speed line").Problem);
            return true;
        });
    }

    [Fact]
    public void A_rows_result_is_the_rows_or_the_reason_there_are_none()
    {
        FenixChannelRows rows = FenixChannelRows.Of(["SPD 250", "ALT 10000"]);
        Assert.Equal(new[] { "SPD 250", "ALT 10000" }, rows.Rows);
        Assert.Null(rows.Problem);

        FenixChannelRows none = FenixChannelRows.NotAvailable("field 125.2 has the wrong shape");
        Assert.Null(none.Rows);
        Assert.Equal("field 125.2 has the wrong shape", none.Problem);
    }
}

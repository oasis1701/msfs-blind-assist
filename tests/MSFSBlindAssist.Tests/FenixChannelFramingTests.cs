// Characterization tests for FenixChannelFraming and FenixChannelMessageReader, the gRPC envelope under
// the Fenix display channel client (the service on 127.0.0.1:18083). Pure logic: the response bytes are
// built with FenixChannelProtoWriter.Frame and the status rides on a hand-made HttpResponseMessage,
// never over the network.
//
// The contract the client builds on: a stream cut at any byte gives the same messages in the same
// order; a header the reader cannot go on with (a compressed flag, an unknown flag, a length over the
// cap) ends the messages for good instead of being guessed at; and the status comes from the trailers,
// or from the headers on a trailers-only answer, and reads as null when the answer sent none.

using System.Net;
using MSFSBlindAssist.Services;

namespace MSFSBlindAssist.Tests;

public class FenixChannelFramingTests
{
    // ---- The request ----------------------------------------------------------------------------

    [Fact]
    public void The_empty_request_is_five_zero_bytes()
    {
        Assert.Equal(new byte[5], FenixChannelFraming.EmptyRequest());
    }

    [Fact]
    public void The_empty_request_is_a_new_array_each_call()
    {
        // A caller that wrote into a shared array would change every request after it.
        byte[] first = FenixChannelFraming.EmptyRequest();
        first[0] = 0xFF;

        Assert.Equal(new byte[5], FenixChannelFraming.EmptyRequest());
    }

    // ---- Splitting the stream into messages -----------------------------------------------------

    [Fact]
    public void Takes_one_message()
    {
        var reader = new FenixChannelMessageReader();
        reader.Append(FenixChannelProtoWriter.Frame([1, 2, 3]));

        Assert.True(reader.TryTake(out byte[] message));
        Assert.Equal(new byte[] { 1, 2, 3 }, message);
        Assert.False(reader.TryTake(out _));
        Assert.False(reader.HasPartialMessage);
        Assert.Null(reader.Error);
    }

    [Fact]
    public void Takes_several_messages_from_one_chunk()
    {
        byte[] a = [1, 2, 3];
        byte[] b = [4, 5];
        byte[] c = [6];
        byte[] chunk =
        [
            .. FenixChannelProtoWriter.Frame(a),
            .. FenixChannelProtoWriter.Frame(b),
            .. FenixChannelProtoWriter.Frame(c)
        ];
        var reader = new FenixChannelMessageReader();

        reader.Append(chunk);

        Assert.True(reader.TryTake(out byte[] first));
        Assert.Equal(a, first);
        Assert.True(reader.TryTake(out byte[] second));
        Assert.Equal(b, second);
        Assert.True(reader.TryTake(out byte[] third));
        Assert.Equal(c, third);
        Assert.False(reader.TryTake(out _));
        Assert.False(reader.HasPartialMessage);
        Assert.Null(reader.Error);
    }

    [Fact]
    public void Takes_a_message_split_byte_by_byte()
    {
        byte[] message = [1, 2, 3, 4, 5];
        byte[] frame = FenixChannelProtoWriter.Frame(message);
        var reader = new FenixChannelMessageReader();

        // Nothing comes out, and nothing is refused, until the last byte is in.
        for (int i = 0; i < frame.Length - 1; i++)
        {
            reader.Append(frame.AsSpan(i, 1));
            Assert.False(reader.TryTake(out _));
            Assert.Null(reader.Error);
        }

        reader.Append(frame.AsSpan(frame.Length - 1, 1));

        Assert.True(reader.TryTake(out byte[] taken));
        Assert.Equal(message, taken);
        Assert.False(reader.TryTake(out _));
        Assert.False(reader.HasPartialMessage);
    }

    [Fact]
    public void Takes_a_zero_length_message()
    {
        var reader = new FenixChannelMessageReader();
        reader.Append(FenixChannelProtoWriter.Frame([]));

        Assert.True(reader.TryTake(out byte[] message));
        Assert.Empty(message);
        Assert.False(reader.TryTake(out _));
        Assert.False(reader.HasPartialMessage);
        Assert.Null(reader.Error);
    }

    [Fact]
    public void Takes_the_same_messages_however_the_stream_is_cut()
    {
        // Sixty messages: one in ten is empty, one in ten is 20000 to 40000 bytes (past the reader's
        // starting buffer) and the rest are small, so the reader has to slide the bytes it holds down and
        // to grow. Each stream is cut at random places, from one byte to the whole thing at once, and
        // every cut must give the same messages in the same order.
        var random = new Random(4321);
        var messages = new List<byte[]>();
        for (int i = 0; i < 60; i++)
        {
            int size = i % 10 == 0 ? 0 : i % 10 == 9 ? random.Next(20_000, 40_001) : random.Next(1, 300);
            byte[] message = new byte[size];
            random.NextBytes(message);
            messages.Add(message);
        }
        byte[] stream = messages.SelectMany(m => FenixChannelProtoWriter.Frame(m)).ToArray();

        foreach (int largestChunk in new[] { 1, 7, 64, 5000, stream.Length })
        {
            var reader = new FenixChannelMessageReader();
            var taken = new List<byte[]>();

            for (int at = 0; at < stream.Length;)
            {
                int size = Math.Min(random.Next(1, largestChunk + 1), stream.Length - at);
                reader.Append(stream.AsSpan(at, size));
                at += size;
                while (reader.TryTake(out byte[] message))
                {
                    taken.Add(message);
                }
            }

            Assert.Null(reader.Error);
            Assert.False(reader.HasPartialMessage);
            Assert.Equal(messages.Count, taken.Count);
            for (int i = 0; i < messages.Count; i++)
            {
                Assert.Equal(messages[i], taken[i]);
            }
        }
    }

    // ---- Headers the reader refuses -------------------------------------------------------------

    [Fact]
    public void Refuses_a_compressed_message()
    {
        var reader = new FenixChannelMessageReader();
        reader.Append(FenixChannelProtoWriter.Frame([1, 2, 3], flag: 1));

        Assert.False(reader.TryTake(out _));
        Assert.NotNull(reader.Error);
        Assert.Contains("compressed", reader.Error);
    }

    [Fact]
    public void Refuses_an_unknown_flag()
    {
        var reader = new FenixChannelMessageReader();
        reader.Append(FenixChannelProtoWriter.Frame([1, 2, 3], flag: 2));

        Assert.False(reader.TryTake(out _));
        Assert.NotNull(reader.Error);
    }

    [Fact]
    public void Refuses_an_oversized_length()
    {
        // The length is 0x00400001, one byte over 4 MiB. The header is all there is: the reader must
        // refuse it now, not wait for 4 MiB of body that would never come.
        var reader = new FenixChannelMessageReader();
        reader.Append(new byte[] { 0, 0x00, 0x40, 0x00, 0x01 });

        Assert.False(reader.TryTake(out _));
        Assert.NotNull(reader.Error);
        Assert.Contains("too large", reader.Error);
    }

    [Fact]
    public void Accepts_a_length_of_exactly_the_limit()
    {
        // Only a length over the cap is refused: at the cap the reader waits for the body.
        Assert.Equal(0x0040_0000, FenixChannelFraming.MaxMessageBytes);
        var reader = new FenixChannelMessageReader();
        reader.Append(new byte[] { 0, 0x00, 0x40, 0x00, 0x00 });

        Assert.False(reader.TryTake(out _));
        Assert.Null(reader.Error);
        Assert.True(reader.HasPartialMessage);
    }

    [Fact]
    public void Stays_failed_after_an_error_but_still_gives_the_messages_before_it()
    {
        byte[] before = [1, 2, 3];
        byte[] chunk =
        [
            .. FenixChannelProtoWriter.Frame(before),
            .. FenixChannelProtoWriter.Frame([4, 5], flag: 1),
            .. FenixChannelProtoWriter.Frame([6, 7])
        ];
        var reader = new FenixChannelMessageReader();
        reader.Append(chunk);

        // The whole message ahead of the refused header is good data and comes out.
        Assert.True(reader.TryTake(out byte[] message));
        Assert.Equal(before, message);
        Assert.Null(reader.Error);

        // The refused header ends it: the message behind it is not read as if the stream were in step,
        // and neither is anything that arrives later.
        Assert.False(reader.TryTake(out _));
        string? error = reader.Error;
        Assert.NotNull(error);

        reader.Append(FenixChannelProtoWriter.Frame([8, 9]));

        Assert.False(reader.TryTake(out _));
        Assert.Equal(error, reader.Error);
        Assert.False(reader.HasPartialMessage);
    }

    // ---- A stream that ends mid-message ---------------------------------------------------------

    [Fact]
    public void Reports_a_cut_off_message_as_partial()
    {
        byte[] frame = FenixChannelProtoWriter.Frame([1, 2, 3, 4, 5]);
        var reader = new FenixChannelMessageReader();
        Assert.False(reader.HasPartialMessage);   // nothing held, nothing cut off

        // Three of the five header bytes.
        reader.Append(frame.AsSpan(0, 3));
        Assert.False(reader.TryTake(out _));
        Assert.True(reader.HasPartialMessage);

        // The whole header and two of the five body bytes.
        reader.Append(frame.AsSpan(3, 4));
        Assert.False(reader.TryTake(out _));
        Assert.True(reader.HasPartialMessage);
        Assert.Null(reader.Error);

        // The rest arrives: the message is whole, and nothing is cut off any more.
        reader.Append(frame.AsSpan(7));
        Assert.True(reader.TryTake(out byte[] message));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, message);
        Assert.False(reader.HasPartialMessage);
    }

    [Fact]
    public void Reports_a_cut_off_message_behind_a_whole_one_as_partial()
    {
        // Asked before the whole message is taken, the answer still covers the bytes behind it.
        byte[] whole = FenixChannelProtoWriter.Frame([1, 2, 3]);
        byte[] cutOff = FenixChannelProtoWriter.Frame([4, 5, 6, 7]);
        var reader = new FenixChannelMessageReader();

        reader.Append(whole);
        Assert.False(reader.HasPartialMessage);

        reader.Append(cutOff.AsSpan(0, 6));
        Assert.True(reader.HasPartialMessage);

        Assert.True(reader.TryTake(out _));
        Assert.True(reader.HasPartialMessage);
    }

    // ---- The status -----------------------------------------------------------------------------

    [Fact]
    public void Reads_the_status_from_the_trailers()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.TrailingHeaders.Add("grpc-status", "0");

        var (status, message) = FenixChannelFraming.ReadStatus(response);

        Assert.Equal(0, status);
        Assert.Null(message);
    }

    [Fact]
    public void Reads_the_status_from_the_headers_on_a_trailers_only_answer()
    {
        // An answer with no message (grpc-status 12 for a call Fenix does not offer) is one block of
        // headers, and the message in it is percent-encoded.
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("grpc-status", "12");
        response.Headers.Add("grpc-message", "Method%20not%20found");

        var (status, message) = FenixChannelFraming.ReadStatus(response);

        Assert.Equal(12, status);
        Assert.Equal("Method not found", message);
    }

    [Fact]
    public void Prefers_the_trailers_over_the_headers()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("grpc-status", "12");
        response.Headers.Add("grpc-message", "from%20the%20headers");
        response.TrailingHeaders.Add("grpc-status", "0");

        var (status, message) = FenixChannelFraming.ReadStatus(response);

        // The message comes from the block the status came from, never the other one's next to it.
        Assert.Equal(0, status);
        Assert.Null(message);
    }

    [Fact]
    public void No_status_reads_as_null()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);

        var (status, message) = FenixChannelFraming.ReadStatus(response);

        Assert.Null(status);
        Assert.Null(message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void A_status_that_is_not_a_whole_number_reads_as_null(string value)
    {
        // The status is digits only: a sign, a decimal point or a value past an int is no status, and
        // the app never takes a guess at one.
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.TrailingHeaders.Add("grpc-status", value);

        Assert.Null(FenixChannelFraming.ReadStatus(response).Status);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Does_not_depend_on_the_pc_culture(string culture)
    {
        // The status is read, and the numbers in an error are written, with the invariant culture.
        var (status, error) = TestCulture.Run(culture, () =>
        {
            using var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.TrailingHeaders.Add("grpc-status", "12");

            var reader = new FenixChannelMessageReader();
            reader.Append(new byte[] { 0, 0x00, 0x40, 0x00, 0x01 });
            reader.TryTake(out _);

            return (FenixChannelFraming.ReadStatus(response).Status, reader.Error);
        });

        Assert.Equal(12, status);
        Assert.NotNull(error);
        Assert.Contains("4194305", error);
        Assert.Contains("4194304", error);
    }
}

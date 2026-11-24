using System.Buffers.Binary;
using System.Globalization;
using System.Net.Http.Headers;

namespace MSFSBlindAssist.Services;

/// <summary>
/// The gRPC envelope under the Fenix display channel (127.0.0.1:18083): the bytes of a request that
/// carries no message, and how a response says its call ended. The messages in a response come out of a
/// <see cref="FenixChannelMessageReader"/>. Nothing here touches the network: the client hands it the
/// bytes it read and the <see cref="HttpResponseMessage"/> it holds.
/// </summary>
internal static class FenixChannelFraming
{
    /// <summary>
    /// The largest message the reader waits for, 4 MiB. A display push from Fenix is 6 to 10 KB, so a
    /// length past this is a stream out of step, not a message.
    /// </summary>
    public const int MaxMessageBytes = 4 * 1024 * 1024;

    private const string StatusHeader = "grpc-status";
    private const string MessageHeader = "grpc-message";

    /// <summary>
    /// The body of a call that takes no request: one frame holding an empty message, a flag byte of 0 and
    /// a length of 0. A new array each call, so no caller can change another's request.
    /// </summary>
    public static byte[] EmptyRequest() => new byte[5];

    /// <summary>
    /// One request message as the one frame of a call's body: a flag byte of 0 (not compressed), the length as four
    /// big-endian bytes, and the message. A new array each call.
    /// </summary>
    public static byte[] Frame(byte[] message)
    {
        ArgumentNullException.ThrowIfNull(message);

        byte[] framed = new byte[5 + message.Length];
        BinaryPrimitives.WriteUInt32BigEndian(framed.AsSpan(1), (uint)message.Length);
        message.CopyTo(framed, 5);
        return framed;
    }

    /// <summary>
    /// Reads how a call ended: its <c>grpc-status</c>, and the <c>grpc-message</c> that goes with it. A
    /// normal answer puts them in the trailers, after its last message. A trailers-only answer, which is
    /// an error that sends no message (status 12 for a call Fenix does not offer), puts them in the
    /// headers. The trailers win when both carry a status, and the message always comes from the block
    /// the status came from.
    /// </summary>
    /// <remarks>
    /// The trailers arrive after the body: ask only once the body has been read to its end. The status is
    /// null when the answer sent none or one that is not a whole number, which the app never guesses at;
    /// the message is null when that block sent none, and is percent-decoded, as gRPC encodes it.
    /// </remarks>
    public static (int? Status, string? Message) ReadStatus(HttpResponseMessage response)
    {
        if (TryReadStatus(response.TrailingHeaders, out var fromTrailers)) return fromTrailers;
        if (TryReadStatus(response.Headers, out var fromHeaders)) return fromHeaders;
        return (null, null);
    }

    // The status and message of one block of headers, if that block carries a grpc-status at all. A
    // status that is there but not a plain number still counts as the block's: it reads as null rather
    // than sending the caller to the other block for a more convenient answer.
    private static bool TryReadStatus(HttpHeaders block, out (int? Status, string? Message) result)
    {
        result = (null, null);
        if (!TryGetFirst(block, StatusHeader, out string rawStatus)) return false;

        // Digits only: no sign, no decimal point, no group separator, so the PC's culture cannot matter.
        int? status = int.TryParse(rawStatus, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;
        string? message = TryGetFirst(block, MessageHeader, out string rawMessage)
            ? Uri.UnescapeDataString(rawMessage)
            : null;

        result = (status, message);
        return true;
    }

    private static bool TryGetFirst(HttpHeaders block, string name, out string value)
    {
        value = "";
        if (!block.TryGetValues(name, out IEnumerable<string>? values)) return false;

        string? first = values.FirstOrDefault();
        if (first is null) return false;

        value = first;
        return true;
    }
}

/// <summary>
/// Splits a gRPC response stream into its messages. The client appends each chunk of bytes it reads,
/// then takes every whole message there is. A gRPC message is a flag byte (0 for a plain message, 1 for
/// a compressed one), a length as four big-endian bytes, and that many bytes of message. A chunk can end
/// anywhere inside a message, and can hold several.
/// </summary>
/// <remarks>
/// <para>The reader refuses a header it cannot go on with, and says why in <see cref="Error"/>: a
/// compressed message (the client asks for identity encoding and reads no other), a flag other than 0
/// and 1, and a length over <see cref="FenixChannelFraming.MaxMessageBytes"/>. The flag and the length
/// are checked as soon as the five header bytes are in, before the body is waited for, so a bad length
/// never makes the reader hold on for a body that will not come. Messages that were whole ahead of the
/// bad header still come out. From then on <see cref="TryTake"/> returns false, and the reader ignores
/// what it is given and holds nothing: the stream is out of step and nothing behind the bad header can be
/// trusted, so the client drops the stream and reconnects.</para>
/// <para>Take every whole message after each <see cref="Append"/>: the reader keeps only the bytes not
/// yet taken, so the most it holds is one message and the chunk behind it. It is not thread safe: one
/// reader per stream, used by the one loop that reads it.</para>
/// </remarks>
internal sealed class FenixChannelMessageReader
{
    // A message's prefix: the flag byte, then the length as four big-endian bytes.
    private const int HeaderBytes = 5;

    // The bytes held are _buffer[_start.._end]: what has arrived and not been taken yet.
    private byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    /// <summary>
    /// Why the reader stopped, or null while it has not (see the remarks). <see cref="TryTake"/> sets it
    /// when it reaches a header it refuses. It is the first reason and it never clears.
    /// </summary>
    public string? Error { get; private set; }

    /// <summary>
    /// True when the bytes held include a message that is not whole: a header cut short, or a body with
    /// bytes still to come. The client asks it when the stream ends, once it has taken every whole
    /// message: a stream that ends mid-message was cut off. False after an error, which leaves nothing held.
    /// </summary>
    public bool HasPartialMessage
    {
        get
        {
            if (Error != null) return false;

            // Step over every whole message held. Whatever is left behind them is cut off.
            ReadOnlySpan<byte> rest = Held;
            while (!rest.IsEmpty)
            {
                if (rest.Length < HeaderBytes) return true;

                uint length = BinaryPrimitives.ReadUInt32BigEndian(rest[1..]);
                if (length > (uint)(rest.Length - HeaderBytes)) return true;

                rest = rest[(HeaderBytes + (int)length)..];
            }

            return false;
        }
    }

    // What has arrived and not been taken.
    private ReadOnlySpan<byte> Held => _buffer.AsSpan(_start, _end - _start);

    /// <summary>
    /// Adds the next bytes of the stream. Nothing is read from them until <see cref="TryTake"/>. Ignored
    /// after an error.
    /// </summary>
    public void Append(ReadOnlySpan<byte> chunk)
    {
        if (Error != null || chunk.IsEmpty) return;

        if (_buffer.Length - _end < chunk.Length)
        {
            MakeRoom(chunk.Length);
        }

        chunk.CopyTo(_buffer.AsSpan(_end));
        _end += chunk.Length;
    }

    /// <summary>
    /// Takes the next whole message, without its five header bytes. False when the bytes held are not a
    /// whole message yet, and false for good after an error (see <see cref="Error"/>).
    /// </summary>
    public bool TryTake(out byte[] message)
    {
        message = [];
        if (Error != null) return false;

        ReadOnlySpan<byte> held = Held;
        if (held.Length < HeaderBytes) return false;

        byte flag = held[0];
        if (flag != 0)
        {
            return Fail(flag == 1
                ? "a compressed message arrived, and the client reads only plain ones"
                : string.Create(CultureInfo.InvariantCulture, $"a message flag of {flag} arrived, and gRPC defines only 0 and 1"));
        }

        uint declared = BinaryPrimitives.ReadUInt32BigEndian(held[1..]);
        if (declared > FenixChannelFraming.MaxMessageBytes)
        {
            return Fail(string.Create(CultureInfo.InvariantCulture,
                $"a message length of {declared} bytes is too large (the limit is {FenixChannelFraming.MaxMessageBytes})"));
        }

        int length = (int)declared;
        if (held.Length - HeaderBytes < length) return false;   // the body is not all here yet

        message = held.Slice(HeaderBytes, length).ToArray();
        _start += HeaderBytes + length;
        if (_start == _end)
        {
            _start = 0;
            _end = 0;
        }

        return true;
    }

    // Makes room for extra more bytes behind the ones held: slides them to the front of the buffer, and
    // grows the buffer only when that is not enough.
    private void MakeRoom(int extra)
    {
        int held = _end - _start;
        if (held + extra > _buffer.Length)
        {
            byte[] bigger = new byte[Math.Max(held + extra, _buffer.Length * 2)];
            Held.CopyTo(bigger);
            _buffer = bigger;
        }
        else
        {
            Held.CopyTo(_buffer);   // the two ranges can overlap: a span copy handles that
        }

        _start = 0;
        _end = held;
    }

    // Records why the stream is refused and lets go of everything held: nothing will be taken again.
    private bool Fail(string error)
    {
        Error = error;
        _buffer = [];
        _start = 0;
        _end = 0;
        return false;
    }
}

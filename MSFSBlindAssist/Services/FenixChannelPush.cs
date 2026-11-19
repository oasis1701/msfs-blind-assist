using System.Collections.ObjectModel;
using System.Text;
using System.Text.Unicode;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One field of a protobuf message as the wire gave it: its number, its wire type, and its value in the member that
/// wire type fills. A varint is in <see cref="Varint"/>, a fixed64 in <see cref="Fixed64"/> (read as a double), a
/// fixed32 in <see cref="Fixed32"/> (read as a single), and a length-delimited value (a string, bytes or a nested
/// message) in <see cref="Bytes"/>, a view of the message the field came from; every other member is its default.
/// </summary>
/// <remarks>
/// The wire type is the shape the field arrived in. A decoder checks <see cref="Type"/> against the shape the
/// contract gives the field (the comment beside its number in <see cref="FenixChannelFields"/>) before it reads the
/// value, and a field in another shape is a wrong shape, not a value. The readers below convert nothing:
/// <see cref="AsString"/> and <see cref="AsMessage"/> answer null for any other wire type, and <see cref="AsBool"/>
/// and <see cref="AsDouble"/> read the member of their own shape, which is false or 0 in a field that arrived in
/// another.
/// </remarks>
internal readonly record struct FenixChannelField(
    int Number, FenixWireType Type, ulong Varint, double Fixed64, float Fixed32, ReadOnlyMemory<byte> Bytes)
{
    /// <summary>A varint read as a bool, by protobuf's rule: any value but 0 is true.</summary>
    public bool AsBool() => Varint != 0;

    /// <summary>A fixed64 read as a double. NaN and the infinities are valid values.</summary>
    public double AsDouble() => Fixed64;

    /// <summary>
    /// A length-delimited value read as a string, or null when this is another wire type or its bytes are not valid
    /// UTF-8. protobuf-net writes only valid UTF-8, so bytes that will not decode can only be a reshaped field (a
    /// string, bytes and a message share wire type 2) or a framing fault, and a string with replacement characters
    /// in it would pass for text.
    /// </summary>
    public string? AsString()
    {
        if (Type != FenixWireType.LengthDelimited) return null;

        ReadOnlySpan<byte> bytes = Bytes.Span;
        return Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }

    /// <summary>
    /// A length-delimited value read as a nested message, or null when this is another wire type or the top level of
    /// its bytes is not valid protobuf. A part is judged here, when it is read, and not before. Every call walks the
    /// part and gives a push of its own, so a reader that takes several fields from one part takes the part once.
    /// </summary>
    public FenixChannelPush? AsMessage()
        => Type == FenixWireType.LengthDelimited ? FenixChannelPush.From(Bytes) : null;
}

/// <summary>
/// One message of the Fenix display channel held for reading by field number: the display push, its RemoteData part,
/// or any part nested in them. A feature's decoder takes a singular field with <see cref="TryGet"/> and a repeated
/// one with <see cref="All"/>, checks the wire type of each against the shape the contract gives it, and reads the
/// value with the <c>As</c> methods of <see cref="FenixChannelField"/>; a nested part is a push of its own, through
/// <see cref="FenixChannelField.AsMessage"/>.
/// </summary>
/// <remarks>
/// <para><see cref="From"/> walks the top level of the message once, with <see cref="FenixChannelWire"/>, and
/// allocates nothing doing it. It gives null when the bytes of that level are not valid protobuf: a value read out of
/// damaged bytes might be wrong, and the app never speaks a guess. A part nested inside is not walked then. It is
/// judged when a feature reads it, so a part that is damaged inside costs the feature that reads it and no other.</para>
/// <para>The fields are indexed by number the first time one is read, once, even when several threads read together,
/// and the instance does not change after that: it may be read from any thread, and a message that nobody reads costs
/// the one walk. A push holds the memory it is made from, and the <see cref="FenixChannelField.Bytes"/> of its fields
/// are views of it, so nothing is copied; the memory must not be written to while the push, or a part of it, is
/// in use. (The channel client hands every message over in an array of its own, which nothing writes to again.)</para>
/// </remarks>
internal sealed class FenixChannelPush
{
    private static readonly ReadOnlyCollection<FenixChannelField> NoFields =
        new(Array.Empty<FenixChannelField>());

    private readonly ReadOnlyMemory<byte> _message;
    private readonly Lazy<Dictionary<int, ReadOnlyCollection<FenixChannelField>>> _index;

    private FenixChannelPush(ReadOnlyMemory<byte> message)
    {
        _message = message;
        _index = new Lazy<Dictionary<int, ReadOnlyCollection<FenixChannelField>>>(
            BuildIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Holds <paramref name="message"/> for reading, or gives null when its top level is not valid protobuf (see the
    /// remarks). A message with no field at all is valid: it is a push with nothing in it to read.
    /// </summary>
    public static FenixChannelPush? From(ReadOnlyMemory<byte> message)
    {
        var wire = new FenixChannelWire(message.Span);
        while (wire.TryReadTag(out _, out FenixWireType type))
        {
            if (!wire.TrySkip(type)) return null;
        }

        // The walk ends at the end of the message, or at bytes that are not a tag.
        return wire.IsMalformed ? null : new FenixChannelPush(message);
    }

    /// <summary>
    /// The field <paramref name="number"/>, as protobuf reads a singular field: the last occurrence, when the message
    /// sent it more than once. False, with a default field, when the message did not send it.
    /// </summary>
    public bool TryGet(int number, out FenixChannelField field)
    {
        if (_index.Value.TryGetValue(number, out ReadOnlyCollection<FenixChannelField>? occurrences))
        {
            field = occurrences[^1];
            return true;
        }

        field = default;
        return false;
    }

    /// <summary>
    /// Every occurrence of the field <paramref name="number"/>, in the order the message sent them: how a repeated
    /// field reads. A packed list comes as one occurrence, whose <see cref="FenixChannelField.Bytes"/> the caller
    /// unpacks. Empty when the message did not send the field. The list is the same object every time, and read-only.
    /// </summary>
    public IReadOnlyList<FenixChannelField> All(int number)
        => _index.Value.TryGetValue(number, out ReadOnlyCollection<FenixChannelField>? occurrences)
            ? occurrences
            : NoFields;

    // Walks the top level once more, which From has found valid, and gathers each field under its number in the order
    // it came. Called once, by the Lazy, on whichever thread reads first.
    private Dictionary<int, ReadOnlyCollection<FenixChannelField>> BuildIndex()
    {
        var gathered = new Dictionary<int, List<FenixChannelField>>();
        var wire = new FenixChannelWire(_message.Span);
        while (wire.TryReadTag(out int number, out FenixWireType type))
        {
            // False only when the bytes run out in a value, which a level that From has walked does not do.
            if (!TryReadValue(ref wire, number, type, out FenixChannelField field)) break;

            if (!gathered.TryGetValue(number, out List<FenixChannelField>? sent))
            {
                sent = [];
                gathered.Add(number, sent);
            }

            sent.Add(field);
        }

        var index = new Dictionary<int, ReadOnlyCollection<FenixChannelField>>(gathered.Count);
        foreach (KeyValuePair<int, List<FenixChannelField>> entry in gathered)
        {
            index.Add(entry.Key, entry.Value.AsReadOnly());
        }

        return index;
    }

    // Reads the value that follows a tag into the member of its wire type.
    private bool TryReadValue(ref FenixChannelWire wire, int number, FenixWireType type, out FenixChannelField field)
    {
        field = default;
        switch (type)
        {
            case FenixWireType.Varint:
                if (!wire.TryReadVarint(out ulong varint)) return false;

                field = new FenixChannelField(number, type, varint, 0, 0, default);
                return true;

            case FenixWireType.Fixed64:
                if (!wire.TryReadFixed64(out double fixed64)) return false;

                field = new FenixChannelField(number, type, 0, fixed64, 0, default);
                return true;

            case FenixWireType.Fixed32:
                if (!wire.TryReadFixed32(out float fixed32)) return false;

                field = new FenixChannelField(number, type, 0, 0, fixed32, default);
                return true;

            case FenixWireType.LengthDelimited:
                if (!wire.TryReadLengthDelimited(out ReadOnlySpan<byte> value)) return false;

                field = new FenixChannelField(number, type, 0, 0, 0, MemoryOf(value));
                return true;

            default:
                return false;
        }
    }

    // The wire reader hands a value out as a span of the message it walks, so where that span sits in the message is
    // found from the span itself. A value of no bytes has no place to find, and needs none. A span from outside the
    // message cannot come out of the reader; a copy keeps this right even so.
    private ReadOnlyMemory<byte> MemoryOf(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) return ReadOnlyMemory<byte>.Empty;

        return _message.Span.Overlaps(value, out int offset) ? _message.Slice(offset, value.Length) : value.ToArray();
    }
}

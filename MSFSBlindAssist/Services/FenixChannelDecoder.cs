using System.Globalization;
using System.Text;
using System.Text.Unicode;
using static MSFSBlindAssist.Services.FenixChannelFields;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Turns one message of the Fenix display channel into what the trust gate reads: a display push into a
/// <see cref="FenixChannelFrame"/>, an ECAM entries message into a <see cref="FenixEcamEntries"/>. It
/// walks the message with <see cref="FenixChannelWire"/> and reads only the fields
/// <see cref="FenixChannelFields"/> names.
/// </summary>
/// <remarks>
/// <para>Nothing throws, whatever the bytes are. A message is malformed when any reader of it hit bytes that
/// are not valid protobuf. A nested part is read with a reader of its own, whose
/// <see cref="FenixChannelWire.IsMalformed"/> never reaches the reader of the message around it, so every
/// reader created here is checked: a part cut short inside itself makes the whole message malformed even
/// though the message around it read cleanly. A malformed message is the frame's <c>MalformedFrame</c>,
/// with no value from it, since a value read out of damaged bytes might be wrong and the app never speaks
/// a guess.</para>
/// <para>A field in another wire shape than the contract gives it is recorded by its path ("110" for
/// field 110, "110.5" for field 5 inside it), skipped by the wire type it really has, and left at its
/// default; every other field still decodes. A string whose bytes are not valid UTF-8 is a wrong shape
/// too, recorded and left at its default the same way: protobuf-net writes only valid UTF-8, so such bytes
/// can only come from a reshaped field (a string, bytes and a message share wire type 2) or a framing
/// fault, and a string with replacement characters in it would pass for text. A field left out reads as
/// its protobuf default, a field of a number the app does not know is skipped, and a repeated field
/// accumulates in the order it came.
/// An element with no field 1 reads as "". An FMA of 1 to 15 or of 17 or more slots is a wrong shape for
/// field 111; one with no slot is not, since protobuf-net leaves out an empty or null array (an unpowered PFD
/// may have no FMA to send), and the gate judges that for the PFD alone. A field that is not repeated takes
/// the last value if it is sent twice, and its nested part is merged, as protobuf reads them.</para>
/// <para>The display units (field 12) say which display is live: the PFD, the ND, the E/WD, the SD or the DCDU while
/// a powered unit shows it (<see cref="FenixChannelFrame.PfdLive"/>, <see cref="FenixChannelFrame.NdLive"/>,
/// <see cref="FenixChannelFrame.EwdLive"/>, <see cref="FenixChannelFrame.SdLive"/>,
/// <see cref="FenixChannelFrame.DcduLive"/>). Their Power and Content lists are repeated varints, read in protobuf's
/// unpacked form (the Fenix's) or its packed form, both the same contract; any other wire type is a wrong shape
/// ("12.1", "12.2"). Unit i is Power[i] with Content[i], up to the shorter list. No field 12, or no unit powered,
/// leaves every one of those displays off, and is never a wrong shape. The ISIS is no display unit: its own
/// message (field 182) carries a Powered flag (182.1), which is <see cref="FenixChannelFrame.IsisPowered"/>. No field
/// 182 leaves the ISIS off, and is never a wrong shape; a 182 that is not a part is one ("182"), and so is a 182.1
/// in another wire type ("182.1").</para>
/// <para>A push with no display data, that is with no RemoteData or an empty one, is
/// <see cref="FenixChannelFrame.NoData"/>: it is neither damaged nor reshaped, so it records no wrong shape
/// (not "111" for an FMA it never had) and the gate skips it. A field 1 in another wire shape than a part
/// is still a wrong shape, "1", and a RemoteData that is not empty is judged as always.</para>
/// </remarks>
internal static class FenixChannelDecoder
{
    private static readonly FenixEcamEntries MalformedEntries =
        new(true, Array.Empty<string>(), Array.Empty<FenixEcamEntry>(), false);

    /// <summary>
    /// Decodes one AircraftDataPush, the message of the display data call. A push with no display data (no
    /// PrimaryData, or an empty one) is <see cref="FenixChannelFrame.NoData"/>.
    /// </summary>
    public static FenixChannelFrame DecodeDisplayData(ReadOnlySpan<byte> message)
    {
        var draft = new Draft();
        if (!ReadPush(message, string.Empty, draft)) return FenixChannelFrame.MalformedFrame;

        // Nothing to read is not a wrong shape. A field 1 in a wrong shape is the one finding that can already
        // be in the list when no RemoteData was read, and it is judged below as always.
        if (!draft.HasRemoteData && draft.Wrong.Count == 0) return FenixChannelFrame.NoData;

        // The FMA is 16 slots; any other count is not the shape the app was checked against, except none at all:
        // protobuf-net leaves out an empty or null array, so a PFD with no FMA to draw (unpowered) can send no
        // slot. Nothing is reshaped then, and the gate judges it for the PFD alone ("no FMA").
        if (draft.Fma.Count != 0 && draft.Fma.Count != FmaEntry.SlotCount) draft.WrongShape(Number(Remote.Fma));

        return new FenixChannelFrame(
            Malformed: false,
            WrongShapeFields: draft.Wrong,
            Fd: new FenixFdState(draft.Pitch, draft.Fpa, draft.Roll, draft.Yawbar, draft.FdOnCapt, draft.FdOnFo,
                draft.TrkFpa, draft.RollGuidance, draft.FdFlash, draft.FdInhibit, draft.InhibitRoll,
                draft.InhibitPitch),
            Fma: draft.Fma,
            UpperEcamLeft: draft.UpperEcamLeft,
            UpperEcamRight: draft.UpperEcamRight,
            FwcPage: draft.FwcPage,
            FwcPhase: draft.FwcPhase,
            MagHeading: draft.MagHeading,
            FobText: draft.FobText)
        {
            PfdLive = draft.Shows(DisplayContent.PfdCapt),
            EwdLive = draft.Shows(DisplayContent.EcamUpper),
            SdLive = draft.Shows(DisplayContent.EcamLower),
            NdLive = draft.Shows(DisplayContent.NdCapt),
            DcduLive = draft.Shows(DisplayContent.DcduCapt),
            IsisPowered = draft.IsisPowered
        };
    }

    /// <summary>Decodes one EcamUpdate, the message of the ECAM entries call.</summary>
    public static FenixEcamEntries DecodeEcamEntries(ReadOnlySpan<byte> message)
    {
        var draft = new Draft();
        if (!ReadEcamUpdate(message, string.Empty, draft)) return MalformedEntries;

        return new FenixEcamEntries(false, draft.Wrong, draft.Entries, draft.Heartbeat);
    }

    // ---- The messages -----------------------------------------------------------------------------
    // Each reader walks one message and returns false when the bytes of it are malformed. Its switch is
    // the field table: a field number, and the read that checks the wire shape the contract gives it (the
    // comment beside the number in FenixChannelFields). A reader gets the path of its message ("" for the
    // whole message, "110" inside field 110) so a wrong shape inside it is recorded by its full path.

    private static bool ReadPush(ReadOnlySpan<byte> message, string path, Draft d)
    {
        var w = new Walker(message, path, d);
        while (w.Next(out int field))
        {
            bool ok = field == Push.PrimaryData ? w.Part(ReadRemote) : w.Skip();
            if (!ok) return false;
        }

        return !w.IsMalformed;
    }

    // RemoteData is where the paths start: its field 110 is "110", never "1.110". So this reader ignores
    // the path of field 1 of the push that it is given.
    private static bool ReadRemote(ReadOnlySpan<byte> part, string pushFieldPath, Draft d)
    {
        // An empty RemoteData holds no display data (see DecodeDisplayData), and has no field to read.
        d.HasRemoteData |= !part.IsEmpty;

        var w = new Walker(part, string.Empty, d);
        while (w.Next(out int field))
        {
            bool ok = field switch
            {
                Remote.FwcPage => w.Int(ref d.FwcPage),
                Remote.FwcPhase => w.Int(ref d.FwcPhase),
                Remote.DuData => w.Part(ReadDisplayUnits),
                Remote.FdData => w.Part(ReadFd),
                Remote.Fma => w.Part(ReadFmaEntry),
                Remote.FobString => w.Text(ref d.FobText),
                Remote.UpperEcamLeft => w.Part(ReadUpperEcamLeft),
                Remote.UpperEcamRight => w.Part(ReadUpperEcamRight),
                Remote.Isis => w.Part(ReadIsis),
                Remote.Heading => w.Part(ReadHeading),
                _ => w.Skip()
            };
            if (!ok) return false;
        }

        return !w.IsMalformed;
    }

    // The display units: entry i of Power and of Content is unit i, each a repeated varint, unpacked or packed.
    // Brightness and fields the app does not know are skipped.
    private static bool ReadDisplayUnits(ReadOnlySpan<byte> part, string path, Draft d)
    {
        var w = new Walker(part, path, d);
        while (w.Next(out int field))
        {
            bool ok = field switch
            {
                DuData.Power => w.Varints(d.UnitPower),
                DuData.Content => w.Varints(d.UnitContent),
                _ => w.Skip()
            };
            if (!ok) return false;
        }

        return !w.IsMalformed;
    }

    // The ISIS's own message: whether it is powered. Its other fields belong to the ISIS feature, which reads them from
    // the push itself, and are skipped here.
    private static bool ReadIsis(ReadOnlySpan<byte> part, string path, Draft d)
    {
        var w = new Walker(part, path, d);
        while (w.Next(out int field))
        {
            bool ok = field == IsisState.Powered ? w.Bool(ref d.IsisPowered) : w.Skip();
            if (!ok) return false;
        }

        return !w.IsMalformed;
    }

    private static bool ReadFd(ReadOnlySpan<byte> part, string path, Draft d)
    {
        var w = new Walker(part, path, d);
        while (w.Next(out int field))
        {
            bool ok = field switch
            {
                FdData.Pitch => w.Double(ref d.Pitch),
                FdData.Fpa => w.Double(ref d.Fpa),
                FdData.Roll => w.Double(ref d.Roll),
                FdData.Yawbar => w.Double(ref d.Yawbar),
                FdData.FdOnCapt => w.Bool(ref d.FdOnCapt),
                FdData.FdOnFo => w.Bool(ref d.FdOnFo),
                FdData.TrkFpa => w.Bool(ref d.TrkFpa),
                FdData.RollGuidance => w.Bool(ref d.RollGuidance),
                FdData.FdFlash => w.Bool(ref d.FdFlash),
                FdData.FdInhibit => w.Bool(ref d.FdInhibit),
                FdData.InhibitRoll => w.Bool(ref d.InhibitRoll),
                FdData.InhibitPitch => w.Bool(ref d.InhibitPitch),
                _ => w.Skip()
            };
            if (!ok) return false;
        }

        return !w.IsMalformed;
    }

    // One slot of the FMA. Each occurrence of field 111 is a slot of its own, added once its part has read
    // cleanly.
    private static bool ReadFmaEntry(ReadOnlySpan<byte> part, string path, Draft d)
    {
        string text = string.Empty;
        bool changeBox = false;

        var w = new Walker(part, path, d);
        while (w.Next(out int field))
        {
            bool ok = field switch
            {
                FmaEntry.Text => w.Text(ref text),
                FmaEntry.ChangeBox => w.Bool(ref changeBox),
                _ => w.Skip()
            };
            if (!ok) return false;
        }
        if (w.IsMalformed) return false;

        d.Fma.Add(new FenixFmaSlot(text, changeBox));
        return true;
    }

    private static bool ReadUpperEcamLeft(ReadOnlySpan<byte> part, string path, Draft d)
        => ReadNullWrapped(part, path, d, d.UpperEcamLeft);

    private static bool ReadUpperEcamRight(ReadOnlySpan<byte> part, string path, Draft d)
        => ReadNullWrapped(part, path, d, d.UpperEcamRight);

    // One element of a NullWrapped string[]: a part whose field 1 is the string. An element without it is a
    // null string, which reads as "" and keeps its place in the column.
    private static bool ReadNullWrapped(ReadOnlySpan<byte> part, string path, Draft d, List<string> column)
    {
        string value = string.Empty;

        var w = new Walker(part, path, d);
        while (w.Next(out int field))
        {
            bool ok = field == NullWrapped.Value ? w.Text(ref value) : w.Skip();
            if (!ok) return false;
        }
        if (w.IsMalformed) return false;

        column.Add(value);
        return true;
    }

    private static bool ReadHeading(ReadOnlySpan<byte> part, string path, Draft d)
    {
        var w = new Walker(part, path, d);
        while (w.Next(out int field))
        {
            bool ok = field == HeadingData.MagHeading ? w.Double(ref d.MagHeading) : w.Skip();
            if (!ok) return false;
        }

        return !w.IsMalformed;
    }

    private static bool ReadEcamUpdate(ReadOnlySpan<byte> message, string path, Draft d)
    {
        var w = new Walker(message, path, d);
        while (w.Next(out int field))
        {
            bool ok = field switch
            {
                EcamUpdate.Entries => w.Part(ReadEcamEntry),
                EcamUpdate.Heartbeat => w.Bool(ref d.Heartbeat),
                _ => w.Skip()
            };
            if (!ok) return false;
        }

        return !w.IsMalformed;
    }

    // One entry of the ECAM list, added once its part has read cleanly.
    private static bool ReadEcamEntry(ReadOnlySpan<byte> part, string path, Draft d)
    {
        string ata = string.Empty;
        string name = string.Empty;
        string title = string.Empty;
        bool isAlert = false;

        var w = new Walker(part, path, d);
        while (w.Next(out int field))
        {
            bool ok = field switch
            {
                EcamEntryInfo.Ata => w.Text(ref ata),
                EcamEntryInfo.Name => w.Text(ref name),
                EcamEntryInfo.Title => w.Text(ref title),
                EcamEntryInfo.IsAlert => w.Bool(ref isAlert),
                _ => w.Skip()
            };
            if (!ok) return false;
        }
        if (w.IsMalformed) return false;

        d.Entries.Add(new FenixEcamEntry(ata, name, title, isAlert));
        return true;
    }

    private static string Number(int field) => field.ToString(CultureInfo.InvariantCulture);

    // ---- Working state --------------------------------------------------------------------------------

    // Reads the part of a message that one field holds (see Walker.Part).
    private delegate bool PartReader(ReadOnlySpan<byte> part, string path, Draft draft);

    // The working state of one decode: the values read so far and the paths that had the wrong shape. A new
    // one for each message, so a frame never carries anything from the one before. Public fields, so a
    // reader can hand one to Walker as the place a value goes.
    private sealed class Draft
    {
        public readonly List<string> Wrong = [];

        // True once a RemoteData part of at least one byte has been read: without one the push has no display data.
        public bool HasRemoteData;

        // The display units' Power and Content, entry i of each being unit i.
        public readonly List<ulong> UnitPower = [];
        public readonly List<ulong> UnitContent = [];

        // The ISIS's own Powered flag: it is on no display unit.
        public bool IsisPowered;

        public double Pitch, Fpa, Roll, Yawbar;
        public bool FdOnCapt, FdOnFo, TrkFpa, RollGuidance, FdFlash, FdInhibit, InhibitRoll, InhibitPitch;
        public readonly List<FenixFmaSlot> Fma = new(FmaEntry.SlotCount);
        public readonly List<string> UpperEcamLeft = [];
        public readonly List<string> UpperEcamRight = [];
        public int FwcPage, FwcPhase;
        public double MagHeading;
        public string FobText = string.Empty;

        public readonly List<FenixEcamEntry> Entries = [];
        public bool Heartbeat;

        // Each path once, in the order found: sixteen FMA slots with the same wrong field are one finding.
        public void WrongShape(string path)
        {
            if (!Wrong.Contains(path)) Wrong.Add(path);
        }

        // Whether a powered unit shows this DisplayTypeEnum value. Unit i is Power[i] with Content[i], up to the
        // shorter list; any Power but 0 is on, as protobuf reads a bool, and Content is an enum, its low 32 bits.
        public bool Shows(int content)
        {
            int units = Math.Min(UnitPower.Count, UnitContent.Count);
            for (int i = 0; i < units; i++)
            {
                if (UnitPower[i] != 0 && unchecked((int)UnitContent[i]) == content) return true;
            }

            return false;
        }
    }

    // One message being walked: the wire reader over it, where it sits (see the readers above) and the
    // draft that the values and the wrong shapes go to. The typed reads take the wire type of the field
    // just read from Next and check it against the shape the caller asks for: a read of that shape stores
    // the value and returns true; a field of another shape is recorded, skipped by its real wire type and
    // leaves the target as it was, also true (a string that is not valid UTF-8 is recorded the same way, its
    // bytes already read); false only when the bytes are malformed.
    private ref struct Walker
    {
        private FenixChannelWire _wire;
        private readonly string _path;
        private readonly Draft _draft;
        private int _field;
        private FenixWireType _type;

        public Walker(ReadOnlySpan<byte> message, string path, Draft draft)
        {
            _wire = new FenixChannelWire(message);
            _path = path;
            _draft = draft;
            _field = 0;
            _type = FenixWireType.Varint;
        }

        /// <summary>True once the wire reader hit bytes that are not valid protobuf.</summary>
        public bool IsMalformed => _wire.IsMalformed;

        /// <summary>The next field's number; false at the end of the message or when its bytes are malformed.</summary>
        public bool Next(out int field)
        {
            bool found = _wire.TryReadTag(out _field, out _type);
            field = _field;
            return found;
        }

        public bool Double(ref double target)
        {
            if (_type != FenixWireType.Fixed64) return WrongShape();
            if (!_wire.TryReadFixed64(out double value)) return false;

            target = value;
            return true;
        }

        // Any value but 0 is true, as protobuf reads a bool.
        public bool Bool(ref bool target)
        {
            if (_type != FenixWireType.Varint) return WrongShape();
            if (!_wire.TryReadVarint(out ulong value)) return false;

            target = value != 0;
            return true;
        }

        // An int32 or an enum: a negative one comes sign-extended to 64 bits, and the low 32 bits are the value.
        public bool Int(ref int target)
        {
            if (_type != FenixWireType.Varint) return WrongShape();
            if (!_wire.TryReadVarint(out ulong value)) return false;

            target = unchecked((int)value);
            return true;
        }

        // A repeated varint (a bool or an enum list), in either form protobuf gives it: unpacked, one varint field per
        // entry, as the Fenix sends it, or packed, every entry back to back in one length-delimited value. Both encode
        // the same contract, so both are its shape; a fixed64 or fixed32 is not, and only such a reshape is caught
        // here: a string, bytes or message shares wire type 2 with the packed form, so a field reshaped into one of
        // them can read as packed varints. The build check (FNX-23) covers that, and such a misread can only make a
        // display read live, never weaken another check. Each entry is added in the order it came. A packed list is
        // walked with a reader of its own to its end, and bytes in it that are not whole varints make the message
        // malformed.
        public bool Varints(List<ulong> target)
        {
            if (_type == FenixWireType.Varint)
            {
                if (!_wire.TryReadVarint(out ulong value)) return false;

                target.Add(value);
                return true;
            }

            if (_type != FenixWireType.LengthDelimited) return WrongShape();
            if (!_wire.TryReadLengthDelimited(out ReadOnlySpan<byte> packed)) return false;

            var list = new FenixChannelWire(packed);
            while (!list.IsAtEnd)
            {
                if (!list.TryReadVarint(out ulong value)) return false;

                target.Add(value);
            }

            return true;
        }

        // A string is valid UTF-8 or a wrong shape. protobuf-net writes only valid UTF-8, so bytes that will
        // not decode can only be a reshaped field (a string, bytes and a message share wire type 2) or a
        // framing fault, and a string with replacement characters in it would pass for text. The bytes are
        // already consumed, so this records the path without a skip and is true, like any other wrong shape.
        public bool Text(ref string target)
        {
            if (_type != FenixWireType.LengthDelimited) return WrongShape();
            if (!_wire.TryReadLengthDelimited(out ReadOnlySpan<byte> bytes)) return false;

            if (!Utf8.IsValid(bytes))
            {
                _draft.WrongShape(FieldPath());
                return true;
            }

            target = Encoding.UTF8.GetString(bytes);
            return true;
        }

        /// <summary>
        /// A nested part: reads its bytes and gives them to <paramref name="read"/> with the path of the
        /// field, so its reader's own Walker is a new one (its malformed state never reaches this one), and
        /// returns what <paramref name="read"/> returns.
        /// </summary>
        public bool Part(PartReader read)
        {
            if (_type != FenixWireType.LengthDelimited) return WrongShape();
            if (!_wire.TryReadLengthDelimited(out ReadOnlySpan<byte> part)) return false;

            return read(part, FieldPath(), _draft);
        }

        /// <summary>Skips the field just read, whatever its shape, for a field the app does not know.</summary>
        public bool Skip() => _wire.TrySkip(_type);

        private bool WrongShape()
        {
            _draft.WrongShape(FieldPath());
            return _wire.TrySkip(_type);
        }

        private readonly string FieldPath() => _path.Length == 0 ? Number(_field) : $"{_path}.{Number(_field)}";
    }
}

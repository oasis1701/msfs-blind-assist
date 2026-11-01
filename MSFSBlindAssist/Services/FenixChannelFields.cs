namespace MSFSBlindAssist.Services;

/// <summary>
/// The calls the Fenix display channel client may open. Both only read: the display data stream and the
/// ECAM entries stream. The service has calls that write values, run actions and hand out credentials
/// too; none has a member here, and <see cref="FenixChannelFields.PathOf"/> gives a path for no other
/// value.
/// </summary>
internal enum FenixChannelCall
{
    /// <summary>The display data: a stream of AircraftDataPush messages, about 17 a second.</summary>
    DisplayData,

    /// <summary>The ECAM entries: a stream of EcamUpdate messages, sent when the list changes.</summary>
    EcamEntries
}

/// <summary>
/// The app's own copy of what it uses of the Fenix display channel (gRPC on 127.0.0.1:18083): where the
/// channel is, the two calls that read it, the field numbers the decoder reads and the wire shape of each.
/// The service is protobuf-net "code first", so there is no .proto file to take them from: they were read
/// off the live channel, and after a Fenix update this one file is what to compare with it. No other file
/// in the app names a call path or a field number.
/// </summary>
/// <remarks>
/// A field's contract name and wire shape sit in the comment beside its number. The shape is not looked
/// up at run time: <see cref="FenixChannelDecoder"/> enforces it, and a field that arrives in another shape
/// is recorded by its path and never read as a value.
/// </remarks>
internal static class FenixChannelFields
{
    /// <summary>
    /// The numeric loopback address, never the name "localhost" (as [A320-40] has it for the data service).
    /// </summary>
    public const string Host = "127.0.0.1";

    public const int Port = 18083;

    /// <summary>
    /// The gRPC path of a call: <c>/service/method</c>, the service named without the leading I of its
    /// interface. An enum member that is not one of the two throws, so no other path can come out of here.
    /// </summary>
    public static string PathOf(FenixChannelCall call) => call switch
    {
        FenixChannelCall.DisplayData => "/ProsimRemoting.GrpcAircraftDataSource/Subscribe",
        FenixChannelCall.EcamEntries => "/ProsimRemoting.GrpcSdk/SubscribeEcamEntries",
        _ => throw new ArgumentOutOfRangeException(nameof(call), call, "Not a Fenix channel call.")
    };

    /// <summary>
    /// The Fenix builds the channel has been checked against in the sim, spelled as
    /// <c>FenixDataService.GetSoftwareVersionAsync</c> gives them (for example "3.0.0.7613"). The trust gate
    /// trusts no display on a build outside this set. Empty until a build is confirmed in the sim
    /// ([FNX-23]), so until then nothing is trusted.
    /// </summary>
    public static readonly IReadOnlySet<string> VerifiedBuilds = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>AircraftDataPush, the message of the display data call.</summary>
    public static class Push
    {
        public const int PrimaryData = 1;           // PrimaryData: nested RemoteData
    }

    /// <summary>RemoteData, the part of a push the app reads.</summary>
    public static class Remote
    {
        public const int FwcPage = 10;              // fwcPage: varint (enum), the lower ECAM page shown
        public const int FwcPhase = 11;             // fwcPhase: varint (int32), the flight warning phase
        public const int FdData = 110;              // fdData: nested FdData, the flight director
        public const int Fma = 111;                 // fma: repeated nested FmaEntry, one per slot
        public const int FobString = 153;           // fobString: string, the E/WD's FOB line
        public const int UpperEcamLeft = 173;       // upperEcamLeft: repeated nested NullWrapped string, E/WD left
        public const int UpperEcamRight = 174;      // upperEcamRight: repeated nested NullWrapped string, E/WD right
        public const int Heading = 194;             // heading: nested HeadingData
        public const int Altitude = 197;            // altitude: fixed64 (double)
    }

    /// <summary>FdData, the flight director.</summary>
    public static class FdData
    {
        public const int Pitch = 1;                 // Pitch: fixed64 (double), NaN when the bar is not drawn
        public const int Fpa = 2;                   // Fpa: fixed64 (double)
        public const int Roll = 3;                  // Roll: fixed64 (double)
        public const int Yawbar = 4;                // Yawbar: fixed64 (double)
        public const int FdOnCapt = 5;              // FdOnCapt: varint (bool)
        public const int FdOnFo = 6;                // FdOnFo: varint (bool)
        public const int TrkFpa = 7;                // TrkFpa: varint (bool)
        public const int RollGuidance = 8;          // RollGuidance: varint (bool)
        public const int FdFlash = 9;               // FdFlash: varint (bool)
        public const int FdInhibit = 10;            // FdInhibit: varint (bool)
        public const int InhibitRoll = 11;          // InhibitRoll: varint (bool)
        public const int InhibitPitch = 12;         // InhibitPitch: varint (bool)
    }

    /// <summary>FmaEntry, one slot of the FMA.</summary>
    public static class FmaEntry
    {
        public const int Text = 1;                  // Text: string, the coded line
        public const int ChangeBox = 2;             // ChangeBox: varint (bool)

        /// <summary>
        /// The FMA is 16 slots. A push with another count is not the shape the app was checked against.
        /// </summary>
        public const int SlotCount = 16;
    }

    /// <summary>HeadingData, the heading the push carries.</summary>
    public static class HeadingData
    {
        public const int MagHeading = 1;            // MagHeading: fixed64 (double)
    }

    /// <summary>NullWrapped, the element of a string[] the contract marks NullWrapped.</summary>
    public static class NullWrapped
    {
        public const int Value = 1;                 // Value: string; an element without it is a null string
    }

    /// <summary>EcamUpdate, the message of the ECAM entries call.</summary>
    public static class EcamUpdate
    {
        public const int Entries = 1;               // Entries: repeated nested EcamEntryInfo
        public const int Heartbeat = 2;             // Heartbeat: varint (bool)
    }

    /// <summary>EcamEntryInfo (the contract's GrpcEcamEntryInfo), one entry of the ECAM list.</summary>
    public static class EcamEntryInfo
    {
        public const int Ata = 1;                   // Ata: string
        public const int Name = 2;                  // Name: string
        public const int Title = 3;                 // Title: string
        public const int IsAlert = 4;               // IsAlert: varint (bool)
    }
}
